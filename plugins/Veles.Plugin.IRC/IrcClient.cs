/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Veles.Plugin.IRC;

/// <summary>
/// Lightweight async IRC client supporting plain TCP and TLS (IRCv3).
/// Raises events for incoming messages on the thread-pool; all sends are
/// serialised through an internal queue so callers never need to lock.
/// </summary>
internal sealed class IrcClient : IAsyncDisposable
{
    // ── Public events ─────────────────────────────────────────────────────

    /// <summary>Fired when the TCP connection to the server is established and registration is complete (RPL_WELCOME received).</summary>
    public event Action? Connected;

    /// <summary>Fired when the connection is closed for any reason.</summary>
    public event Action<string>? Disconnected;

    /// <summary>Fired for every raw line received from the server (after PING/PONG handling).</summary>
    public event Action<IrcMessage>? MessageReceived;

    /// <summary>Fired for PRIVMSG lines directed at a channel.</summary>
    public event Action<string, string, string>? ChannelMessage; // nick, channel, text

    /// <summary>Fired for PRIVMSG lines directed at the bot (private query).</summary>
    public event Action<string, string>? PrivateMessage; // nick, text

    /// <summary>Fired for NOTICE lines.</summary>
    public event Action<string, string, string>? Notice; // nick, target, text

    /// <summary>Fired when a user joins a channel.</summary>
    public event Action<string, string>? UserJoined; // nick, channel

    /// <summary>Fired when a user parts a channel.</summary>
    public event Action<string, string, string>? UserParted; // nick, channel, reason

    /// <summary>Fired when a user quits.</summary>
    public event Action<string, string>? UserQuit; // nick, reason

    // ── State ─────────────────────────────────────────────────────────────

    public bool IsConnected => _connected;
    public string Nickname { get; private set; } = string.Empty;

    private volatile bool _connected;
    private TcpClient? _tcp;
    private Stream? _stream;
    private StreamReader? _reader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    // ── Connect / Disconnect ──────────────────────────────────────────────

    /// <summary>
    /// Connect to an IRC server, perform TLS if <paramref name="useTls"/> is true,
    /// then send NICK/USER registration. Returns when RPL_WELCOME is received or
    /// throws on timeout/error.
    /// </summary>
    public async Task ConnectAsync(
        string host, int port, string nick, string username, string realname,
        bool useTls, bool acceptInvalidCert,
        string? saslLogin, string? saslPassword,
        CancellationToken ct = default)
    {
        Nickname = nick;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linked = _cts.Token;

        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(host, port, linked).ConfigureAwait(false);

        Stream raw = _tcp.GetStream();

        if (useTls)
        {
            var ssl = new SslStream(raw, leaveInnerStreamOpen: false,
                acceptInvalidCert
                    ? (_, _, _, _) => true
                    : (RemoteCertificateValidationCallback?)null);
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = host },
                linked).ConfigureAwait(false);
            _stream = ssl;
        }
        else
        {
            _stream = raw;
        }

        _reader = new StreamReader(_stream, Encoding.UTF8);

        // SASL negotiation (PLAIN only)
        if (!string.IsNullOrEmpty(saslLogin) && !string.IsNullOrEmpty(saslPassword))
        {
            await RawSendAsync("CAP REQ :sasl", linked).ConfigureAwait(false);
        }

        await RawSendAsync($"NICK {nick}", linked).ConfigureAwait(false);
        await RawSendAsync($"USER {username} 0 * :{realname}", linked).ConfigureAwait(false);

        // Wait for RPL_WELCOME (001) or error, reading synchronously until registration done
        using var welcomeCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var welcomeLinked = CancellationTokenSource.CreateLinkedTokenSource(linked, welcomeCts.Token);
        bool saslAcked = false;

        while (!welcomeLinked.Token.IsCancellationRequested)
        {
            string? line = await _reader.ReadLineAsync(welcomeLinked.Token).ConfigureAwait(false);
            if (line == null) throw new IOException("Server closed connection during registration.");

            var msg = IrcMessage.Parse(line);

            // PING during registration
            if (msg.Command == "PING")
            {
                await RawSendAsync($"PONG :{msg.Trailing}", linked).ConfigureAwait(false);
                continue;
            }

            // SASL flow
            if (msg.Command == "CAP" && msg.Trailing?.Contains("sasl") == true && !saslAcked)
            {
                saslAcked = true;
                await RawSendAsync("AUTHENTICATE PLAIN", linked).ConfigureAwait(false);
                continue;
            }

            if (msg.Command == "AUTHENTICATE" && msg.Params.Count > 0 && msg.Params[0] == "+")
            {
                string payload = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"\0{saslLogin}\0{saslPassword}"));
                await RawSendAsync($"AUTHENTICATE {payload}", linked).ConfigureAwait(false);
                continue;
            }

            if (msg.Command is "903") // RPL_SASLSUCCESS
            {
                await RawSendAsync("CAP END", linked).ConfigureAwait(false);
                continue;
            }

            if (msg.Command is "904" or "905") // SASL failure
                throw new Exception($"SASL authentication failed: {msg.Trailing}");

            if (msg.Command == "001") // RPL_WELCOME
            {
                _connected = true;
                // Kick off the read loop
                _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
                Connected?.Invoke();
                return;
            }

            // ERROR during registration
            if (msg.Command == "ERROR")
                throw new Exception($"IRC server error: {msg.Trailing}");

            // Nick already in use
            if (msg.Command == "433")
            {
                nick += "_";
                Nickname = nick;
                await RawSendAsync($"NICK {nick}", linked).ConfigureAwait(false);
            }
        }

        throw new OperationCanceledException("IRC registration timed out.", welcomeLinked.Token);
    }

    public async Task DisconnectAsync(string quitMessage = "Veles IRC Relay disconnecting")
    {
        if (!_connected) return;
        _connected = false;

        try { await RawSendAsync($"QUIT :{quitMessage}").ConfigureAwait(false); } catch { }
        _cts?.Cancel();
        if (_readLoop != null) try { await _readLoop.ConfigureAwait(false); } catch { }
        CleanUp();
    }

    // ── Send helpers ──────────────────────────────────────────────────────

    public Task JoinAsync(string channel, CancellationToken ct = default)
        => RawSendAsync($"JOIN {channel}", ct);

    public Task PartAsync(string channel, string reason = "", CancellationToken ct = default)
        => RawSendAsync(string.IsNullOrEmpty(reason) ? $"PART {channel}" : $"PART {channel} :{reason}", ct);

    public Task PrivMsgAsync(string target, string text, CancellationToken ct = default)
        => RawSendAsync($"PRIVMSG {target} :{text}", ct);

    public Task NoticeAsync(string target, string text, CancellationToken ct = default)
        => RawSendAsync($"NOTICE {target} :{text}", ct);

    public Task NickAsync(string newNick, CancellationToken ct = default)
    {
        Nickname = newNick;
        return RawSendAsync($"NICK {newNick}", ct);
    }

    /// <summary>
    /// Send <paramref name="text"/> to <paramref name="target"/> chunked so that
    /// each individual IRC message stays within 380 characters of payload
    /// (conservative limit below the 512-byte wire limit).
    /// </summary>
    public async Task SendChunkedAsync(string target, string prefix, string text,
        CancellationToken ct = default)
    {
        const int maxChunk = 380;

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string prefixed = string.IsNullOrEmpty(prefix) ? line : $"{prefix}: {line}";

            if (prefixed.Length <= maxChunk)
            {
                await PrivMsgAsync(target, prefixed, ct).ConfigureAwait(false);
                continue;
            }

            // Split at word boundaries
            var words = prefixed.Split(' ');
            var sb = new StringBuilder();
            foreach (string word in words)
            {
                if (sb.Length + word.Length + 1 > maxChunk)
                {
                    await PrivMsgAsync(target, sb.ToString().TrimEnd(), ct).ConfigureAwait(false);
                    sb.Clear();
                }
                sb.Append(word).Append(' ');
            }
            if (sb.Length > 0)
                await PrivMsgAsync(target, sb.ToString().TrimEnd(), ct).ConfigureAwait(false);
        }
    }

    // ── Internal read loop ────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                string? line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break; // server closed connection

                var msg = IrcMessage.Parse(line);

                if (msg.Command == "PING")
                {
                    await RawSendAsync($"PONG :{msg.Trailing}", ct).ConfigureAwait(false);
                    continue;
                }

                MessageReceived?.Invoke(msg);
                DispatchMessage(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            bool wasConnected = _connected;
            _connected = false;
            CleanUp();
            if (wasConnected)
                Disconnected?.Invoke("Connection closed.");
        }
    }

    private void DispatchMessage(IrcMessage msg)
    {
        string nick = msg.Nick ?? string.Empty;

        switch (msg.Command)
        {
            case "PRIVMSG" when msg.Params.Count > 0:
            {
                string target = msg.Params[0];
                string text = msg.Trailing ?? string.Empty;
                if (target.StartsWith('#') || target.StartsWith('&'))
                    ChannelMessage?.Invoke(nick, target, text);
                else
                    PrivateMessage?.Invoke(nick, text);
                break;
            }
            case "NOTICE" when msg.Params.Count > 0:
                Notice?.Invoke(nick, msg.Params[0], msg.Trailing ?? string.Empty);
                break;
            case "JOIN" when msg.Params.Count > 0:
                UserJoined?.Invoke(nick, msg.Params[0]);
                break;
            case "PART" when msg.Params.Count > 0:
                UserParted?.Invoke(nick, msg.Params[0], msg.Trailing ?? string.Empty);
                break;
            case "QUIT":
                UserQuit?.Invoke(nick, msg.Trailing ?? string.Empty);
                break;
            case "NICK" when msg.Params.Count > 0:
                if (string.Equals(nick, Nickname, StringComparison.OrdinalIgnoreCase))
                    Nickname = msg.Params[0];
                break;
            case "ERROR":
                Disconnected?.Invoke(msg.Trailing ?? "Server error.");
                break;
        }
    }

    private async Task RawSendAsync(string line, CancellationToken ct = default)
    {
        if (_stream == null) return;
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void CleanUp()
    {
        try { _reader?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _reader = null;
        _stream = null;
        _tcp = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _cts?.Dispose();
    }
}

// ── IRC message parser ────────────────────────────────────────────────────

/// <summary>
/// Represents a single parsed IRC protocol message.
/// Format: [':' prefix SPACE] command {SPACE param} [SPACE ':' trailing]
/// </summary>
internal sealed class IrcMessage
{
    public string? Prefix { get; private init; }
    public string? Nick { get; private init; }
    public string Command { get; private init; } = string.Empty;
    public List<string> Params { get; private init; } = new();
    public string? Trailing { get; private init; }

    // :nick!user@host PRIVMSG #channel :Hello world
    private static readonly Regex PrefixNick = new(@"^([^!@]+)(?:![^@]+)?(?:@.+)?$", RegexOptions.Compiled);

    public static IrcMessage Parse(string raw)
    {
        string? prefix = null;
        string? nick = null;
        int pos = 0;

        if (raw.StartsWith(':'))
        {
            int space = raw.IndexOf(' ');
            prefix = raw[1..space];
            pos = space + 1;

            var m = PrefixNick.Match(prefix);
            if (m.Success) nick = m.Groups[1].Value;
        }

        // Find trailing
        string? trailing = null;
        int trailIdx = raw.IndexOf(" :", pos);
        string middle;
        if (trailIdx >= 0)
        {
            trailing = raw[(trailIdx + 2)..];
            middle = raw[pos..trailIdx];
        }
        else
        {
            middle = raw[pos..];
        }

        var parts = middle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string command = parts.Length > 0 ? parts[0] : string.Empty;
        var @params = new List<string>();
        for (int i = 1; i < parts.Length; i++)
            @params.Add(parts[i]);

        return new IrcMessage
        {
            Prefix = prefix,
            Nick = nick,
            Command = command.ToUpperInvariant(),
            Params = @params,
            Trailing = trailing
        };
    }

    public override string ToString() =>
        $"{(Prefix != null ? ":" + Prefix + " " : "")}{Command}" +
        $"{(Params.Count > 0 ? " " + string.Join(" ", Params) : "")}" +
        $"{(Trailing != null ? " :" + Trailing : "")}";
}
