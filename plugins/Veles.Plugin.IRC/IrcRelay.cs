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
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.IRC;

/// <summary>
/// Bridges a single IRC channel ↔ one SL relay target (nearby chat, group/conference,
/// or direct IM).
/// </summary>
internal sealed class IrcRelay : IAsyncDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────

    public string Server { get; set; } = "irc.libera.chat";
    public int Port { get; set; } = 6697;
    public bool UseTls { get; set; } = true;
    public bool AcceptInvalidCert { get; set; } = false;
    public string Nick { get; set; } = "VelesRelay";
    public string Username { get; set; } = "veles";
    public string Realname { get; set; } = "Veles SL Relay";
    public string Channel { get; set; } = "#veles";
    public string? SaslLogin { get; set; }
    public string? SaslPassword { get; set; }

    /// <summary>
    /// Where SL messages come from / are sent to.
    /// </summary>
    public RelayTarget Target { get; set; } = new RelayTarget(RelayTargetType.NearbyChat, UUID.Zero, "Nearby");

    // ── State ─────────────────────────────────────────────────────────────

    public bool IsConnected => _irc?.IsConnected == true;

    private IrcClient? _irc;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private readonly IPluginContext _ctx;
    private readonly Action<string> _log;

    public IrcRelay(IPluginContext ctx, Action<string> log)
    {
        _ctx = ctx;
        _log = log;
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────

    public void Start()
    {
        _reconnectCts = new CancellationTokenSource();
        _reconnectTask = Task.Run(() => ConnectLoopAsync(_reconnectCts.Token));
    }

    public async Task StopAsync()
    {
        _reconnectCts?.Cancel();
        if (_reconnectTask != null)
            try { await _reconnectTask.ConfigureAwait(false); } catch { }

        if (_irc != null)
        {
            _irc.ChannelMessage -= OnIrcChannelMessage;
            _irc.Connected -= OnIrcConnected;
            _irc.Disconnected -= OnIrcDisconnected;
            await _irc.DisposeAsync().ConfigureAwait(false);
            _irc = null;
        }
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        int delay = 5;
        while (!ct.IsCancellationRequested)
        {
            if (_irc != null)
            {
                _irc.ChannelMessage -= OnIrcChannelMessage;
                _irc.Connected -= OnIrcConnected;
                _irc.Disconnected -= OnIrcDisconnected;
                await _irc.DisposeAsync().ConfigureAwait(false);
            }

            _irc = new IrcClient();
            _irc.ChannelMessage += OnIrcChannelMessage;
            _irc.Connected += OnIrcConnected;
            _irc.Disconnected += OnIrcDisconnected;

            try
            {
                _log($"[IRC] Connecting to {Server}:{Port} as {Nick} …");
                await _irc.ConnectAsync(Server, Port, Nick, Username, Realname,
                    UseTls, AcceptInvalidCert, SaslLogin, SaslPassword, ct).ConfigureAwait(false);

                await _irc.JoinAsync(Channel, ct).ConfigureAwait(false);
                _log($"[IRC] Joined {Channel}");
                delay = 5;

                // Stay connected until cancelled or disconnected
                while (!ct.IsCancellationRequested && _irc.IsConnected)
                    await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log($"[IRC] Connection error: {ex.Message}");
            }

            if (ct.IsCancellationRequested) break;
            _log($"[IRC] Reconnecting in {delay}s …");
            await Task.Delay(TimeSpan.FromSeconds(delay), ct).ConfigureAwait(false);
            delay = Math.Min(delay * 2, 120);
        }
    }

    // ── IRC → SL ──────────────────────────────────────────────────────────

    private void OnIrcConnected()
    {
        _log($"[IRC] Connected as {_irc?.Nickname}");
        _ctx.ShowNotification("IRC Relay", $"Connected to {Server} {Channel}");
    }

    private void OnIrcDisconnected(string reason)
    {
        _log($"[IRC] Disconnected: {reason}");
    }

    private void OnIrcChannelMessage(string nick, string channel, string text)
    {
        // Ignore echoes of messages we forwarded (they start with the relay prefix pattern)
        if (text.StartsWith($"({_ctx.Client.Self.Name})", StringComparison.OrdinalIgnoreCase))
            return;

        string relayMsg = $"(irc:{channel}) {nick}: {text}";

        switch (Target.Type)
        {
            case RelayTargetType.NearbyChat:
                _ctx.Client.Self.Chat(relayMsg, 0, ChatType.Normal);
                break;
            case RelayTargetType.GroupOrConference:
                _ctx.Client.Self.InstantMessageGroup(Target.SessionId, relayMsg);
                break;
            case RelayTargetType.DirectIM:
                _ctx.Client.Self.InstantMessage(Target.SessionId, relayMsg);
                break;
        }

        _log($"[IRC→SL] <{nick}> {text}");
    }

    // ── SL → IRC ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a nearby-chat message should be forwarded to IRC.
    /// </summary>
    public void ForwardChatMessage(string fromName, string message)
    {
        if (!IsConnected || Target.Type != RelayTargetType.NearbyChat) return;
        if (message.StartsWith("(irc:", StringComparison.OrdinalIgnoreCase)) return;

        _ = _irc!.SendChunkedAsync(Channel, fromName, message);
    }

    /// <summary>
    /// Called when an IM or group message should be forwarded to IRC.
    /// </summary>
    public void ForwardInstantMessage(UUID sessionId, string fromName, string message,
        InstantMessageDialog dialog)
    {
        if (!IsConnected) return;
        if (message.StartsWith("(irc:", StringComparison.OrdinalIgnoreCase)) return;

        bool isGroupOrConf = dialog == InstantMessageDialog.SessionSend;
        bool isDirect = dialog is InstantMessageDialog.MessageFromAgent
            or InstantMessageDialog.MessageFromObject;

        if (Target.Type == RelayTargetType.GroupOrConference && isGroupOrConf
            && sessionId == Target.SessionId)
        {
            _ = _irc!.SendChunkedAsync(Channel, fromName, message);
        }
        else if (Target.Type == RelayTargetType.DirectIM && isDirect
            && sessionId == Target.SessionId)
        {
            _ = _irc!.SendChunkedAsync(Channel, fromName, message);
        }
    }

    /// <summary>Send a message directly to the IRC channel from the plugin operator.</summary>
    public Task SayAsync(string text)
        => _irc?.PrivMsgAsync(Channel, text) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _reconnectCts?.Dispose();
    }
}

// ── Relay target ─────────────────────────────────────────────────────────

internal enum RelayTargetType { NearbyChat, GroupOrConference, DirectIM }

internal sealed record RelayTarget(RelayTargetType Type, UUID SessionId, string Label)
{
    public static readonly RelayTarget NearbyChat =
        new(RelayTargetType.NearbyChat, UUID.Zero, "Nearby Chat");
}
