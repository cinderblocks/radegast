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
using OpenMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.Discord;

[VelesPlugin("Discord Relay",
    Description = "Bridges SL nearby chat / group chat / IMs to a Discord channel via a bot token.",
    Author = "Sjofn LLC",
    Version = "1.0.0",
    Url = "https://radegast.life/")]
public sealed class DiscordPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;
    private DiscordRelay? _relay;
    private DiscordSettingsControl? _settingsControl;

    // ── IVelesPlugin ──────────────────────────────────────────────────────

    public void Attach(IPluginContext context)
    {
        _ctx = context;

        _ctx.RegisterCommand("discord",
            "Manage the Discord relay",
            "discord connect|disconnect|status",
            OnDiscordCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("discord.connect",    "Discord: Connect",    OnMenuConnect));
        _ctx.AddMenuItem(new PluginMenuItemInfo("discord.disconnect", "Discord: Disconnect", OnMenuDisconnect));
        _ctx.AddMenuItem(new PluginMenuItemInfo("discord.status",     "Discord: Status",     OnMenuStatus));

        _ctx.AddPreferenceTab(new PluginPreferenceTab(
            "discord.settings",
            "Discord Relay",
            () => _settingsControl = new DiscordSettingsControl(_ctx))
        {
            OnApply = OnPreferencesApply,
        });

        _ctx.ChatReceived += OnChatReceived;
        _ctx.IMReceived   += OnIMReceived;
    }

    public void Detach()
    {
        _ctx.ChatReceived -= OnChatReceived;
        _ctx.IMReceived   -= OnIMReceived;

        _ctx.UnregisterCommand("discord");
        _ctx.RemoveMenuItem("discord.connect");
        _ctx.RemoveMenuItem("discord.disconnect");
        _ctx.RemoveMenuItem("discord.status");
        _ctx.RemovePreferenceTab("discord.settings");

        if (_relay != null)
        {
            _ = _relay.StopAsync().ContinueWith(_ => _relay.DisposeAsync().AsTask());
            _relay = null;
        }
    }

    public void Dispose() { }

    // ── Settings helpers ──────────────────────────────────────────────────

    private string GetSetting(string key, string def) => _ctx.GetSetting($"discord_{key}") ?? def;

    private DiscordRelay BuildRelay()
    {
        var relay = new DiscordRelay(_ctx, s => _ctx.LogToChat(s))
        {
            BotToken  = GetSetting("bot_token", string.Empty),
            ChannelId = ulong.TryParse(GetSetting("channel_id", "0"), out ulong cid) ? cid : 0UL,
            WebhookUrl = GetSetting("webhook_url", string.Empty),
        };

        string targetType  = GetSetting("relay_type",  "nearby");
        string targetUuid  = GetSetting("relay_uuid",  UUID.Zero.ToString());
        string targetLabel = GetSetting("relay_label", "Nearby Chat");
        UUID.TryParse(targetUuid, out UUID uuid);

        relay.Target = targetType switch
        {
            "group" => new RelayTarget(RelayTargetType.GroupOrConference, uuid, targetLabel),
            "im"    => new RelayTarget(RelayTargetType.DirectIM,          uuid, targetLabel),
            _       => RelayTarget.NearbyChat,
        };

        return relay;
    }

    // ── Command handler ───────────────────────────────────────────────────

    private void OnDiscordCommand(string[] args, Action<string> write)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "connect":    OnMenuConnect();    break;
            case "disconnect": OnMenuDisconnect(); break;
            case "status":     OnMenuStatus();     break;
            default:
                write("Usage: discord connect|disconnect|status");
                break;
        }
    }

    // ── Menu handlers ─────────────────────────────────────────────────────

    private void OnMenuConnect()
    {
        if (_relay?.IsConnected == true) { _ctx.LogToChat("[Discord] Already connected."); return; }

        if (_relay != null)
            _ = _relay.StopAsync().ContinueWith(_ => _relay.DisposeAsync().AsTask());

        if (string.IsNullOrWhiteSpace(GetSetting("bot_token", string.Empty)))
        {
            _ctx.LogToChat("[Discord] No bot token configured. Open Preferences → Discord Relay.");
            return;
        }

        _relay = BuildRelay();
        _relay.Start();
        _ctx.LogToChat("[Discord] Connecting …");
    }

    private void OnMenuDisconnect()
    {
        if (_relay == null) { _ctx.LogToChat("[Discord] Not connected."); return; }
        _ = _relay.StopAsync().ContinueWith(t =>
        {
            _relay.DisposeAsync().AsTask().Wait();
            _relay = null;
        });
        _ctx.LogToChat("[Discord] Disconnecting …");
    }

    private void OnMenuStatus()
    {
        _ctx.LogToChat($"[Discord] Status   : {(_relay?.IsConnected == true ? "CONNECTED" : "disconnected")}");
        _ctx.LogToChat($"[Discord] Token    : {(string.IsNullOrEmpty(GetSetting("bot_token", string.Empty)) ? "(not set)" : "configured")}");
        _ctx.LogToChat($"[Discord] Channel  : {GetSetting("channel_id", "(not set)")}");
        string webhook = GetSetting("webhook_url", string.Empty);
        _ctx.LogToChat($"[Discord] Webhook  : {(string.IsNullOrEmpty(webhook) ? "none (bot posts as itself)" : "configured")}");
        string relayType  = GetSetting("relay_type",  "nearby");
        string relayLabel = GetSetting("relay_label", "Nearby Chat");
        _ctx.LogToChat($"[Discord] Relay    : {relayType} — {relayLabel}");
    }

    private void OnPreferencesApply()
    {
        _settingsControl?.Apply(_ctx);
        if (_relay?.IsConnected == true)
            _ctx.LogToChat("[Discord] Settings saved. Reconnect for changes to take effect.");
        else
            _ctx.LogToChat("[Discord] Settings saved.");
    }

    // ── SL event handlers ─────────────────────────────────────────────────

    private void OnChatReceived(object? sender, ChatEventArgs e)
    {
        if (_relay == null) return;
        if (e.SourceID == _ctx.Client.Self.AgentID) return;
        if (e.Type is not (ChatType.Normal or ChatType.Shout or ChatType.Whisper)) return;

        _relay.ForwardChatMessage(e.FromName, e.Message);
    }

    private void OnIMReceived(object? sender, InstantMessageEventArgs e)
    {
        if (_relay == null) return;
        if (e.IM.FromAgentID == _ctx.Client.Self.AgentID) return;

        _relay.ForwardInstantMessage(
            e.IM.Dialog == InstantMessageDialog.SessionSend ? e.IM.IMSessionID : e.IM.FromAgentID,
            e.IM.FromAgentName,
            e.IM.Message,
            e.IM.Dialog);
    }
}
