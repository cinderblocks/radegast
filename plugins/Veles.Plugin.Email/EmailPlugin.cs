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

namespace Veles.Plugin.Email;

[VelesPlugin("Email Digest",
    Description = "Batches nearby chat, group chat, and IMs and delivers them to an e-mail address on a configurable schedule.",
    Author = "Sjofn LLC",
    Version = "1.0.0",
    Url = "https://radegast.life/")]
public sealed class EmailPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;
    private EmailDigest? _digest;
    private EmailSettingsControl? _settingsControl;

    // ── IVelesPlugin ──────────────────────────────────────────────────────

    public void Attach(IPluginContext context)
    {
        _ctx = context;

        _ctx.RegisterCommand("email",
            "Manage the Email Digest",
            "email start|stop|sendnow|status",
            OnEmailCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("email.start",     "Email Digest: Start",        OnMenuStart));
        _ctx.AddMenuItem(new PluginMenuItemInfo("email.stop",      "Email Digest: Stop",         OnMenuStop));
        _ctx.AddMenuItem(new PluginMenuItemInfo("email.send_now",  "Email Digest: Send Now",     OnMenuSendNow));
        _ctx.AddMenuItem(new PluginMenuItemInfo("email.status",    "Email Digest: Status",       OnMenuStatus));

        _ctx.AddPreferenceTab(new PluginPreferenceTab(
            "email.settings",
            "Email Digest",
            () => _settingsControl = new EmailSettingsControl(_ctx))
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

        _ctx.UnregisterCommand("email");
        _ctx.RemoveMenuItem("email.start");
        _ctx.RemoveMenuItem("email.stop");
        _ctx.RemoveMenuItem("email.send_now");
        _ctx.RemoveMenuItem("email.status");
        _ctx.RemovePreferenceTab("email.settings");

        _digest?.Dispose();
        _digest = null;
    }

    public void Dispose() { }

    // ── Settings helpers ──────────────────────────────────────────────────

    private string Get(string key, string def) => _ctx.GetSetting($"email_{key}") ?? def;

    private EmailConfig BuildConfig() => new()
    {
        SmtpHost     = Get("smtp_host",     string.Empty),
        SmtpPort     = int.TryParse(Get("smtp_port", "587"), out int p) ? p : 587,
        UseSsl       = Get("smtp_ssl",      "true") == "true",
        Username     = Get("smtp_user",     string.Empty),
        Password     = Get("smtp_pass",     string.Empty),
        FromAddress  = Get("from_address",  string.Empty),
        ToAddress    = Get("to_address",    string.Empty),
        Subject      = Get("subject",       "SL Chat Digest — {date}"),
        IntervalMins = int.TryParse(Get("interval_mins", "60"), out int i) ? i : 60,
        MaxMessages  = int.TryParse(Get("max_messages",  "200"), out int m) ? m : 200,
        IncludeNearby = Get("include_nearby", "true") == "true",
        IncludeIm     = Get("include_im",     "true") == "true",
        IncludeGroup  = Get("include_group",  "true") == "true",
    };

    // ── Command handler ───────────────────────────────────────────────────

    private void OnEmailCommand(string[] args, Action<string> write)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "start":   OnMenuStart();   break;
            case "stop":    OnMenuStop();    break;
            case "sendnow": OnMenuSendNow(); break;
            case "status":  OnMenuStatus();  break;
            default:
                write("Usage: email start|stop|sendnow|status");
                break;
        }
    }

    // ── Menu handlers ─────────────────────────────────────────────────────

    private void OnMenuStart()
    {
        if (_digest?.IsRunning == true) { _ctx.LogToChat("[Email] Digest already running."); return; }

        var cfg = BuildConfig();
        if (!cfg.IsValid(out string reason))
        {
            _ctx.LogToChat($"[Email] Cannot start: {reason}. Open Preferences → Email Digest.");
            return;
        }

        _digest?.Dispose();
        _digest = new EmailDigest(cfg, _ctx.LogToChat);
        _digest.Start();
        _ctx.LogToChat($"[Email] Digest started — sending every {cfg.IntervalMins} minute(s) to {cfg.ToAddress}.");
    }

    private void OnMenuStop()
    {
        if (_digest == null || !_digest.IsRunning) { _ctx.LogToChat("[Email] Digest is not running."); return; }
        _digest.Dispose();
        _digest = null;
        _ctx.LogToChat("[Email] Digest stopped.");
    }

    private void OnMenuSendNow()
    {
        if (_digest == null)
        {
            _ctx.LogToChat("[Email] Digest is not running. Use 'Email Digest: Start' first.");
            return;
        }
        _digest.SendNow();
        _ctx.LogToChat("[Email] Digest send queued.");
    }

    private void OnMenuStatus()
    {
        bool running = _digest?.IsRunning == true;
        _ctx.LogToChat($"[Email] Status   : {(running ? "RUNNING" : "stopped")}");
        _ctx.LogToChat($"[Email] To       : {Get("to_address", "(not set)")}");
        _ctx.LogToChat($"[Email] SMTP     : {Get("smtp_host", "(not set)")}:{Get("smtp_port", "587")}");
        _ctx.LogToChat($"[Email] Interval : {Get("interval_mins", "60")} min(s)");
        if (running) _ctx.LogToChat($"[Email] Buffered : {_digest!.BufferedCount} message(s)");
    }

    private void OnPreferencesApply()
    {
        _settingsControl?.Apply(_ctx);

        bool wasRunning = _digest?.IsRunning == true;
        if (wasRunning)
        {
            _digest!.Dispose();
            _digest = null;
            var cfg = BuildConfig();
            if (cfg.IsValid(out _))
            {
                _digest = new EmailDigest(cfg, _ctx.LogToChat);
                _digest.Start();
                _ctx.LogToChat("[Email] Settings applied — digest restarted with new configuration.");
            }
            else
            {
                _ctx.LogToChat("[Email] Settings saved but configuration is incomplete — digest stopped.");
            }
        }
        else
        {
            _ctx.LogToChat("[Email] Settings saved.");
        }
    }

    // ── SL event handlers ─────────────────────────────────────────────────

    private void OnChatReceived(object? sender, ChatEventArgs e)
    {
        if (_digest == null) return;
        if (e.SourceID == _ctx.Client.Self.AgentID) return;
        if (e.Type is not (ChatType.Normal or ChatType.Shout or ChatType.Whisper or ChatType.OwnerSay)) return;

        _digest.Buffer(new DigestEntry(DigestCategory.Nearby, e.FromName, e.Message, e.Type.ToString()));
    }

    private void OnIMReceived(object? sender, InstantMessageEventArgs e)
    {
        if (_digest == null) return;
        if (e.IM.FromAgentID == _ctx.Client.Self.AgentID) return;

        bool isGroup = e.IM.Dialog is InstantMessageDialog.SessionSend
                                   or InstantMessageDialog.SessionAdd;
        var category = isGroup ? DigestCategory.Group : DigestCategory.Im;
        _digest.Buffer(new DigestEntry(category, e.IM.FromAgentName, e.IM.Message, e.IM.Dialog.ToString()));
    }
}
