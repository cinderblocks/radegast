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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LibreMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.Discord;

/// <summary>
/// Bridges a Discord text channel ↔ one SL relay target (nearby chat,
/// group/conference, or direct IM) using a Discord bot token.
/// Optionally uses a webhook URL to post SL → Discord messages with
/// avatar names as the sender username.
/// </summary>
internal sealed class DiscordRelay : IAsyncDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────

    public string BotToken   { get; set; } = string.Empty;
    public ulong  ChannelId  { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Where SL messages come from / are sent to.</summary>
    public RelayTarget Target { get; set; } = RelayTarget.NearbyChat;

    // ── State ─────────────────────────────────────────────────────────────

    public bool IsConnected => _client?.ConnectionState == ConnectionState.Connected;

    private DiscordSocketClient? _client;
    private HttpClient?          _httpClient;
    private CancellationTokenSource? _cts;
    private Task? _connectTask;
    private readonly IPluginContext _ctx;
    private readonly Action<string> _log;

    public DiscordRelay(IPluginContext ctx, Action<string> log)
    {
        _ctx = ctx;
        _log = log;
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _connectTask = Task.Run(() => ConnectLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_connectTask != null)
            try { await _connectTask.ConfigureAwait(false); } catch { }

        await TearDownClientAsync().ConfigureAwait(false);
        _httpClient?.Dispose();
        _httpClient = null;
    }

    private async Task TearDownClientAsync()
    {
        if (_client == null) return;
        _client.MessageReceived -= OnDiscordMessageReceived;
        _client.Connected       -= OnDiscordConnected;
        _client.Disconnected    -= OnDiscordDisconnected;
        try { await _client.StopAsync().ConfigureAwait(false); } catch { }
        await _client.DisposeAsync().ConfigureAwait(false);
        _client = null;
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        int delay = 5;
        while (!ct.IsCancellationRequested)
        {
            await TearDownClientAsync().ConfigureAwait(false);

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
                LogLevel = LogSeverity.Warning,
            };

            _client = new DiscordSocketClient(config);
            _client.MessageReceived += OnDiscordMessageReceived;
            _client.Connected       += OnDiscordConnected;
            _client.Disconnected    += OnDiscordDisconnected;
            _client.Log             += OnDiscordLog;

            if (!string.IsNullOrWhiteSpace(WebhookUrl))
                _httpClient ??= new HttpClient();

            try
            {
                _log($"[Discord] Logging in …");
                await _client.LoginAsync(TokenType.Bot, BotToken).ConfigureAwait(false);
                await _client.StartAsync().ConfigureAwait(false);
                delay = 5;

                // Stay connected until cancelled
                while (!ct.IsCancellationRequested && _client.ConnectionState != ConnectionState.Disconnected)
                    await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log($"[Discord] Connection error: {ex.Message}");
            }

            if (ct.IsCancellationRequested) break;
            _log($"[Discord] Reconnecting in {delay}s …");
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            delay = Math.Min(delay * 2, 120);
        }
    }

    // ── Discord → SL ──────────────────────────────────────────────────────

    private Task OnDiscordConnected()
    {
        _log("[Discord] Connected.");
        _ctx.ShowNotification("Discord Relay", $"Connected — channel {ChannelId}");
        return Task.CompletedTask;
    }

    private Task OnDiscordDisconnected(Exception ex)
    {
        _log($"[Discord] Disconnected: {ex?.Message ?? "unknown"}");
        return Task.CompletedTask;
    }

    private Task OnDiscordLog(LogMessage msg)
    {
        if (msg.Severity <= LogSeverity.Warning)
            _log($"[Discord] {msg.Severity}: {msg.Message ?? msg.Exception?.Message}");
        return Task.CompletedTask;
    }

    private Task OnDiscordMessageReceived(SocketMessage message)
    {
        // Ignore bots (including ourselves) and messages on other channels
        if (message.Author.IsBot)    return Task.CompletedTask;
        if (message.Channel.Id != ChannelId) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(message.Content)) return Task.CompletedTask;

        string author  = message.Author.GlobalName ?? message.Author.Username;
        string relayMsg = $"(discord) {author}: {message.Content}";

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

        _log($"[Discord→SL] <{author}> {message.Content}");
        return Task.CompletedTask;
    }

    // ── SL → Discord ──────────────────────────────────────────────────────

    public void ForwardChatMessage(string fromName, string message)
    {
        if (!IsConnected || Target.Type != RelayTargetType.NearbyChat) return;
        if (message.StartsWith("(discord)", StringComparison.OrdinalIgnoreCase)) return;

        _ = SendToDiscordAsync(fromName, message);
    }

    public void ForwardInstantMessage(UUID sessionId, string fromName, string message,
        InstantMessageDialog dialog)
    {
        if (!IsConnected) return;
        if (message.StartsWith("(discord)", StringComparison.OrdinalIgnoreCase)) return;

        bool isGroupOrConf = dialog == InstantMessageDialog.SessionSend;
        bool isDirect = dialog is InstantMessageDialog.MessageFromAgent
            or InstantMessageDialog.MessageFromObject;

        if (Target.Type == RelayTargetType.GroupOrConference && isGroupOrConf
            && sessionId == Target.SessionId)
        {
            _ = SendToDiscordAsync(fromName, message);
        }
        else if (Target.Type == RelayTargetType.DirectIM && isDirect
            && sessionId == Target.SessionId)
        {
            _ = SendToDiscordAsync(fromName, message);
        }
    }

    /// <summary>
    /// Post a message to the configured Discord channel.
    /// Uses the webhook (with avatar name as username) when configured,
    /// otherwise falls back to the bot posting as itself.
    /// </summary>
    private async Task SendToDiscordAsync(string fromName, string text)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(WebhookUrl) && _httpClient != null)
            {
                await SendViaWebhookAsync(fromName, text).ConfigureAwait(false);
                return;
            }

            if (_client?.GetChannel(ChannelId) is IMessageChannel ch)
            {
                foreach (string chunk in SplitMessage(text, 2000))
                    await ch.SendMessageAsync($"**{EscapeMarkdown(fromName)}**: {chunk}").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log($"[Discord] Send error: {ex.Message}");
        }
    }

    private async Task SendViaWebhookAsync(string username, string text)
    {
        // Discord webhook JSON: { "username": "...", "content": "..." }
        foreach (string chunk in SplitMessage(text, 2000))
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                username = username.Length > 80 ? username[..80] : username,
                content  = chunk,
            });
            using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient!.PostAsync(WebhookUrl, body).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                _log($"[Discord] Webhook error {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static System.Collections.Generic.IEnumerable<string> SplitMessage(string text, int maxLen)
    {
        if (text.Length <= maxLen) { yield return text; yield break; }
        for (int i = 0; i < text.Length; i += maxLen)
            yield return text.Substring(i, Math.Min(maxLen, text.Length - i));
    }

    private static string EscapeMarkdown(string s)
        => s.Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~");

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}

// ── Relay target ─────────────────────────────────────────────────────────

internal enum RelayTargetType { NearbyChat, GroupOrConference, DirectIM }

internal sealed record RelayTarget(RelayTargetType Type, UUID SessionId, string Label)
{
    public static readonly RelayTarget NearbyChat =
        new(RelayTargetType.NearbyChat, UUID.Zero, "Nearby Chat");
}
