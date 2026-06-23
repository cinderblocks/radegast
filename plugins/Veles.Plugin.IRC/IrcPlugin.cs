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
using LibreMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.IRC;

[VelesPlugin("IRC Relay",
    Description = "Bridges SL nearby chat / group chat / IMs to an IRC channel (TLS + SASL supported).",
    Author = "Sjofn LLC",
    Version = "1.0.0",
    Url = "https://radegast.life/")]
public sealed class IrcPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;
    private IrcRelay? _relay;
    private IrcSettingsControl? _settingsControl;

    // ── IVelesPlugin ──────────────────────────────────────────────────────

    public void Attach(IPluginContext context)
    {
        _ctx = context;

        _ctx.RegisterCommand("irc",
            "Manage the IRC relay",
            "irc connect|disconnect|status",
            OnIrcCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("irc.connect",    "IRC: Connect",    OnMenuConnect));
        _ctx.AddMenuItem(new PluginMenuItemInfo("irc.disconnect", "IRC: Disconnect", OnMenuDisconnect));
        _ctx.AddMenuItem(new PluginMenuItemInfo("irc.status",     "IRC: Status",     OnMenuStatus));

        _ctx.AddPreferenceTab(new PluginPreferenceTab(
            "irc.settings",
            "IRC Relay",
            () => _settingsControl = new IrcSettingsControl(_ctx))
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

        _ctx.UnregisterCommand("irc");
        _ctx.RemoveMenuItem("irc.connect");
        _ctx.RemoveMenuItem("irc.disconnect");
        _ctx.RemoveMenuItem("irc.status");
        _ctx.RemovePreferenceTab("irc.settings");

        if (_relay != null)
        {
            _ = _relay.StopAsync().ContinueWith(_ => _relay.DisposeAsync().AsTask());
            _relay = null;
        }
    }

    public void Dispose() { }

    // ── Settings helpers ──────────────────────────────────────────────────

    private string GetSetting(string key, string def) => _ctx.GetSetting($"irc_{key}") ?? def;

    private void SaveSetting(string key, string value) => _ctx.SetSetting($"irc_{key}", value);

    private IrcRelay BuildRelay()
    {
        var relay = new IrcRelay(_ctx, s => _ctx.LogToChat(s))
        {
            Server = GetSetting("server", "irc.libera.chat"),
            Port = int.TryParse(GetSetting("port", "6697"), out int p) ? p : 6697,
            UseTls = GetSetting("tls", "on") != "off",
            Nick = GetSetting("nick", _ctx.Client.Self.Name.Replace(' ', '_')),
            Username = "veles",
            Realname = $"Veles SL Relay ({_ctx.Client.Self.Name})",
            Channel = GetSetting("channel", "#veles"),
            SaslLogin = _ctx.GetSetting("irc_sasl_login"),
            SaslPassword = _ctx.GetSetting("irc_sasl_password"),
        };

        // Restore relay target
        string targetType = GetSetting("relay_type", "nearby");
        string targetUuid = GetSetting("relay_uuid", UUID.Zero.ToString());
        string targetLabel = GetSetting("relay_label", "Nearby Chat");
        UUID.TryParse(targetUuid, out UUID uuid);

        relay.Target = targetType switch
        {
            "group" => new RelayTarget(RelayTargetType.GroupOrConference, uuid, targetLabel),
            "im" => new RelayTarget(RelayTargetType.DirectIM, uuid, targetLabel),
            _ => RelayTarget.NearbyChat,
        };

        return relay;
    }

    // ── Command handler ───────────────────────────────────────────────────

    private void OnIrcCommand(string[] args, Action<string> write)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "connect":    OnMenuConnect();    break;
            case "disconnect": OnMenuDisconnect(); break;
            case "status":     OnMenuStatus();     break;
            default:
                write("Usage: irc connect|disconnect|status");
                break;
        }
    }

    // ── Menu handlers ─────────────────────────────────────────────────────

    private void OnMenuConnect()
    {
        if (_relay?.IsConnected == true) { _ctx.LogToChat("[IRC] Already connected."); return; }

        if (_relay != null)
            _ = _relay.StopAsync().ContinueWith(_ => _relay.DisposeAsync().AsTask());

        _relay = BuildRelay();
        _relay.Start();
        _ctx.LogToChat("[IRC] Connecting …");
    }

    private void OnMenuDisconnect()
    {
        if (_relay == null) { _ctx.LogToChat("[IRC] Not connected."); return; }
        _ = _relay.StopAsync().ContinueWith(t =>
        {
            _relay.DisposeAsync().AsTask().Wait();
            _relay = null;
        });
        _ctx.LogToChat("[IRC] Disconnecting …");
    }

    private void OnMenuStatus()
    {
        _ctx.LogToChat($"[IRC] Status : {(_relay?.IsConnected == true ? $"CONNECTED as {_relay.Nick}" : "disconnected")}");
        _ctx.LogToChat($"[IRC] Server : {GetSetting("server", "irc.libera.chat")}:{GetSetting("port", "6697")} TLS={GetSetting("tls", "on")}");
        _ctx.LogToChat($"[IRC] Nick   : {GetSetting("nick", "(not set)")}");
        _ctx.LogToChat($"[IRC] Channel: {GetSetting("channel", "#veles")}");
        _ctx.LogToChat($"[IRC] SASL   : {(_ctx.GetSetting("irc_sasl_login") != null ? "configured" : "none")}");
        string relayType  = GetSetting("relay_type",  "nearby");
        string relayLabel = GetSetting("relay_label", "Nearby Chat");
        _ctx.LogToChat($"[IRC] Relay  : {relayType} — {relayLabel}");
    }

    private void OnPreferencesApply()
    {
        _settingsControl?.Apply(_ctx);
        // If relay is running, rebuild it with the new settings on the next connect.
        if (_relay?.IsConnected == true)
            _ctx.LogToChat("[IRC] Settings saved. Reconnect for changes to take effect.");
        else
            _ctx.LogToChat("[IRC] Settings saved.");
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
