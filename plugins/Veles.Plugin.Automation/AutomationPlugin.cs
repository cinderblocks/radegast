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
using System.Text;
using System.Threading;
using Avalonia.Platform.Storage;
using OpenMetaverse;
using Radegast.Veles.PluginApi;
using Veles.Plugin.Automation.UI;

namespace Veles.Plugin.Automation;

[VelesPlugin("Automation",
    Description = "Client-behaviour automation: AutoSit, PseudoHome, and LSL Helper.",
    Author = "Sjofn LLC",
    Version = "1.0.0",
    Url = "https://radegast.life/")]
public sealed class AutomationPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;
    private Timer? _autoSitTimer;
    private Timer? _pseudoHomeTimer;
    private bool _disposed;

    // ── Rule engine ────────────────────────────────────────────────
    private RuleEngine? _ruleEngine;
    private List<AutomationRule> _rules = [];
    private string _rulesFilePath = string.Empty;

    // ── AutoSit state ──────────────────────────────────────────────
    private bool _autoSitEnabled;
    private UUID _autoSitTarget = UUID.Zero;
    private string _autoSitTargetName = string.Empty;

    // ── PseudoHome state ───────────────────────────────────────────
    private bool _pseudoHomeEnabled;
    private string _pseudoHomeRegion = string.Empty;
    private Vector3 _pseudoHomePosition;
    private uint _pseudoHomeTolerance = 256;

    // ── LSLHelper state ────────────────────────────────────────────
    private bool _lslHelperEnabled;
    private readonly HashSet<string> _lslAllowedOwners = new(StringComparer.OrdinalIgnoreCase);

    public void Attach(IPluginContext context)
    {
        _ctx = context;

        // ── Rule engine ────────────────────────────────────────────
        // Start with a temporary path; will be re-keyed to the avatar once connected.
        _rulesFilePath = RulePersistence.DefaultPath("automation");
        if (_ctx.Client.Network.Connected && _ctx.Client.Self.AgentID != UUID.Zero)
            _rulesFilePath = RulePersistence.AvatarPath(_ctx.Client.Self.AgentID.ToString());
        _rules = RulePersistence.Load(_rulesFilePath);
        _ruleEngine = new RuleEngine(_ctx, _rules);
        _ruleEngine.Start();

        LoadSettings();

        // AutoSit timer (10s interval)
        _autoSitTimer = new Timer(_ => TryAutoSit(), null,
            _autoSitEnabled ? TimeSpan.Zero : Timeout.InfiniteTimeSpan,
            TimeSpan.FromSeconds(10));

        // PseudoHome timer (5s interval)
        _pseudoHomeTimer = new Timer(_ => TryPseudoHome(), null,
            _pseudoHomeEnabled ? TimeSpan.Zero : Timeout.InfiniteTimeSpan,
            TimeSpan.FromSeconds(5));

        // Commands
        _ctx.RegisterCommand("autosit", "Manage AutoSit",
            "autosit on|off|status|set <uuid> [name]",
            OnAutoSitCommand);

        _ctx.RegisterCommand("pseudohome", "Manage PseudoHome",
            "pseudohome on|off|status|set [tolerance]",
            OnPseudoHomeCommand);

        _ctx.RegisterCommand("lslhelper", "Manage LSL Helper",
            "lslhelper on|off|status|allow <owner-uuid>|deny <owner-uuid>",
            OnLslHelperCommand);

        // Rule engine commands
        _ctx.RegisterCommand("rule", "Manage automation rules",
            "rule list|add|remove|enable|disable|show <id>",
            OnRuleCommand);

        _ctx.RegisterCommand("groupinviter", "Quick-add a group-inviter rule",
            "groupinviter add <group-uuid> [role-uuid] [trigger: proximity|payment|im] [radius|minamount]",
            OnGroupInviterCommand);

        // Menu items
        _ctx.AddMenuItem(new PluginMenuItemInfo("autosit_toggle",
            "AutoSit: Toggle", () => ToggleAutoSit()));
        _ctx.AddMenuItem(new PluginMenuItemInfo("pseudohome_toggle",
            "PseudoHome: Toggle", () => TogglePseudoHome()));
        _ctx.AddMenuItem(new PluginMenuItemInfo("automation_import",
            "Automation: Import Rules…", ImportRulesAsync));
        _ctx.AddMenuItem(new PluginMenuItemInfo("automation_export",
            "Automation: Export Rules…", ExportRulesAsync));

        // Preference tab — rule editor UI
        _ctx.AddPreferenceTab(new PluginPreferenceTab(
            "automation_rules", "Automation Rules",
            () => new UI.AutomationRulesPanel(_ruleEngine!, _rules,
                () =>
                {
                    RulePersistence.Save(_rulesFilePath, _rules);
                    _ctx.LogToChat("[Automation] Rules saved.");
                },
                ImportRulesAsync,
                ExportRulesAsync)));

        // LSLHelper listens to IMs for object-originated commands
        _ctx.IMReceived += OnIMReceived;
        _ctx.Connected += OnConnected;

        _ctx.LogToChat("[Automation] Plugin attached.");
    }

    public void Detach()
    {
        _ctx.IMReceived -= OnIMReceived;
        _ctx.Connected  -= OnConnected;
        _ruleEngine?.Stop();
        RulePersistence.Save(_rulesFilePath, _rules);
        SaveSettings();
        _ctx.LogToChat("[Automation] Plugin detached.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ruleEngine?.Dispose();
        _autoSitTimer?.Dispose();
        _pseudoHomeTimer?.Dispose();
    }

    // ── Settings ───────────────────────────────────────────────────

    private void LoadSettings()
    {
        _autoSitEnabled = _ctx.GetSetting("autosit_enabled") == "true";
        var sitTarget = _ctx.GetSetting("autosit_target");
        _autoSitTarget = sitTarget != null && UUID.TryParse(sitTarget, out var id) ? id : UUID.Zero;
        _autoSitTargetName = _ctx.GetSetting("autosit_target_name") ?? string.Empty;

        _pseudoHomeEnabled = _ctx.GetSetting("pseudohome_enabled") == "true";
        _pseudoHomeRegion = _ctx.GetSetting("pseudohome_region") ?? string.Empty;
        var posStr = _ctx.GetSetting("pseudohome_position");
        if (posStr != null && Vector3.TryParse(posStr, out var pos))
            _pseudoHomePosition = pos;
        var tolStr = _ctx.GetSetting("pseudohome_tolerance");
        if (tolStr != null && uint.TryParse(tolStr, out var tol))
            _pseudoHomeTolerance = Math.Clamp(tol, 1, 256);

        _lslHelperEnabled = _ctx.GetSetting("lslhelper_enabled") == "true";
        var owners = _ctx.GetSetting("lslhelper_allowed_owners");
        _lslAllowedOwners.Clear();
        if (!string.IsNullOrWhiteSpace(owners))
            _lslAllowedOwners.UnionWith(owners.Split(';', StringSplitOptions.RemoveEmptyEntries));
    }

    private void SaveSettings()
    {
        _ctx.SetSetting("autosit_enabled", _autoSitEnabled ? "true" : "false");
        _ctx.SetSetting("autosit_target", _autoSitTarget.ToString());
        _ctx.SetSetting("autosit_target_name", _autoSitTargetName);

        _ctx.SetSetting("pseudohome_enabled", _pseudoHomeEnabled ? "true" : "false");
        _ctx.SetSetting("pseudohome_region", _pseudoHomeRegion);
        _ctx.SetSetting("pseudohome_position", _pseudoHomePosition.ToString());
        _ctx.SetSetting("pseudohome_tolerance", _pseudoHomeTolerance.ToString());

        _ctx.SetSetting("lslhelper_enabled", _lslHelperEnabled ? "true" : "false");
        _ctx.SetSetting("lslhelper_allowed_owners", string.Join(";", _lslAllowedOwners));
    }

    // ── AutoSit ────────────────────────────────────────────────────

    private void TryAutoSit()
    {
        if (!_autoSitEnabled || _autoSitTarget == UUID.Zero) return;
        if (!_ctx.Client.Network.Connected) return;

        var state = _ctx.Instance.State;
        if (!state.IsSitting)
        {
            state.SetSitting(true, _autoSitTarget);
        }
    }

    private void ToggleAutoSit()
    {
        _autoSitEnabled = !_autoSitEnabled;
        UpdateAutoSitTimer();
        SaveSettings();
        _ctx.LogToChat($"[AutoSit] {(_autoSitEnabled ? "Enabled" : "Disabled")}");
    }

    private void UpdateAutoSitTimer()
    {
        _autoSitTimer?.Change(
            _autoSitEnabled ? TimeSpan.Zero : Timeout.InfiniteTimeSpan,
            TimeSpan.FromSeconds(10));
    }

    private void OnAutoSitCommand(string[] args, Action<string> writeLine)
    {
        if (args.Length == 0) { writeLine("Usage: autosit on|off|status|set <uuid> [name]"); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                _autoSitEnabled = true;
                UpdateAutoSitTimer();
                SaveSettings();
                writeLine("[AutoSit] Enabled.");
                break;
            case "off":
                _autoSitEnabled = false;
                UpdateAutoSitTimer();
                SaveSettings();
                writeLine("[AutoSit] Disabled.");
                break;
            case "status":
                writeLine($"[AutoSit] Enabled={_autoSitEnabled}, Target={_autoSitTarget} ({_autoSitTargetName})");
                break;
            case "set":
                if (args.Length < 2 || !UUID.TryParse(args[1], out var uuid))
                {
                    writeLine("Usage: autosit set <uuid> [name]");
                    return;
                }
                _autoSitTarget = uuid;
                _autoSitTargetName = args.Length > 2 ? string.Join(" ", args[2..]) : string.Empty;
                SaveSettings();
                writeLine($"[AutoSit] Target set to {_autoSitTarget} ({_autoSitTargetName})");
                break;
            default:
                writeLine("Usage: autosit on|off|status|set <uuid> [name]");
                break;
        }
    }

    // ── PseudoHome ─────────────────────────────────────────────────

    private void TryPseudoHome()
    {
        if (!_pseudoHomeEnabled || string.IsNullOrWhiteSpace(_pseudoHomeRegion)) return;
        if (!_ctx.Client.Network.Connected) return;

        var currentSim = _ctx.Client.Network.CurrentSim;
        if (currentSim == null) return;

        var needsTeleport = currentSim.Name != _pseudoHomeRegion
            || Vector3.Distance(_ctx.Client.Self.SimPosition, _pseudoHomePosition) > _pseudoHomeTolerance;

        if (needsTeleport)
        {
            _ctx.Client.Self.Teleport(_pseudoHomeRegion, _pseudoHomePosition);
        }
        else
        {
            // Already home, disable timer until next check cycle
            _pseudoHomeTimer?.Change(Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(5));
        }
    }

    private void TogglePseudoHome()
    {
        _pseudoHomeEnabled = !_pseudoHomeEnabled;
        UpdatePseudoHomeTimer();
        SaveSettings();
        _ctx.LogToChat($"[PseudoHome] {(_pseudoHomeEnabled ? "Enabled" : "Disabled")}");
    }

    private void UpdatePseudoHomeTimer()
    {
        _pseudoHomeTimer?.Change(
            _pseudoHomeEnabled ? TimeSpan.Zero : Timeout.InfiniteTimeSpan,
            TimeSpan.FromSeconds(5));
    }

    private void OnPseudoHomeCommand(string[] args, Action<string> writeLine)
    {
        if (args.Length == 0) { writeLine("Usage: pseudohome on|off|status|set [tolerance]"); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                _pseudoHomeEnabled = true;
                UpdatePseudoHomeTimer();
                SaveSettings();
                writeLine("[PseudoHome] Enabled.");
                break;
            case "off":
                _pseudoHomeEnabled = false;
                UpdatePseudoHomeTimer();
                SaveSettings();
                writeLine("[PseudoHome] Disabled.");
                break;
            case "status":
                writeLine($"[PseudoHome] Enabled={_pseudoHomeEnabled}, Region={_pseudoHomeRegion}, " +
                    $"Pos={_pseudoHomePosition}, Tolerance={_pseudoHomeTolerance}");
                break;
            case "set":
                if (!_ctx.Client.Network.Connected)
                {
                    writeLine("[PseudoHome] Must be connected to set home position.");
                    return;
                }
                _pseudoHomeRegion = _ctx.Client.Network.CurrentSim?.Name ?? string.Empty;
                _pseudoHomePosition = _ctx.Client.Self.SimPosition;
                if (args.Length > 1 && uint.TryParse(args[1], out var tol))
                    _pseudoHomeTolerance = Math.Clamp(tol, 1, 256);
                SaveSettings();
                writeLine($"[PseudoHome] Home set to {_pseudoHomeRegion} {_pseudoHomePosition} (tol={_pseudoHomeTolerance})");
                break;
            default:
                writeLine("Usage: pseudohome on|off|status|set [tolerance]");
                break;
        }
    }

    // ── LSL Helper ─────────────────────────────────────────────────

    private void OnIMReceived(object? sender, InstantMessageEventArgs e)
    {
        if (!_lslHelperEnabled) return;
        if (e.IM.Dialog != InstantMessageDialog.MessageFromObject) return;
        if (!_lslAllowedOwners.Contains(e.IM.FromAgentID.ToString())) return;

        var parts = e.IM.Message.Trim().Split('^');
        if (parts.Length < 1) return;

        switch (parts[0].Trim().ToLowerInvariant())
        {
            case "send_im":
                if (parts.Length < 3) return;
                if (!UUID.TryParse(parts[1].Trim(), out var imTarget)) return;
                _ctx.Client.Self.InstantMessage(imTarget, parts[2].Trim());
                break;

            case "say":
                if (parts.Length < 2) return;
                var chatType = ChatType.Normal;
                var channel = 0;
                if (parts.Length > 2 && int.TryParse(parts[2].Trim(), out var ch) && ch >= 0)
                    channel = ch;
                if (parts.Length > 3)
                {
                    chatType = parts[3].Trim().ToLowerInvariant() switch
                    {
                        "whisper" => ChatType.Whisper,
                        "shout" => ChatType.Shout,
                        _ => ChatType.Normal
                    };
                }
                _ctx.Client.Self.Chat(parts[1].Trim(), channel, chatType);
                break;

            case "give_inventory":
                if (parts.Length < 3) return;
                if (!UUID.TryParse(parts[1].Trim(), out var giveTarget)) return;
                if (!UUID.TryParse(parts[2].Trim(), out var invItemId)) return;
                if (_ctx.Client.Inventory.Store?.Contains(invItemId) != true)
                {
                    _ctx.LogToChat($"[LSLHelper] Item {invItemId} not found in inventory.");
                    return;
                }
                if (_ctx.Client.Inventory.Store![invItemId] is InventoryItem item)
                {
                    _ctx.Client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, giveTarget, true);
                    _ctx.LogToChat($"[LSLHelper] Gave {item.Name} to {giveTarget}");
                }
                break;
        }
    }

    private void OnLslHelperCommand(string[] args, Action<string> writeLine)
    {
        if (args.Length == 0) { writeLine("Usage: lslhelper on|off|status|allow <uuid>|deny <uuid>"); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                _lslHelperEnabled = true;
                SaveSettings();
                writeLine("[LSLHelper] Enabled.");
                break;
            case "off":
                _lslHelperEnabled = false;
                SaveSettings();
                writeLine("[LSLHelper] Disabled.");
                break;
            case "status":
                writeLine($"[LSLHelper] Enabled={_lslHelperEnabled}, AllowedOwners=[{string.Join(", ", _lslAllowedOwners)}]");
                break;
            case "allow":
                if (args.Length < 2) { writeLine("Usage: lslhelper allow <owner-uuid>"); return; }
                _lslAllowedOwners.Add(args[1].Trim());
                SaveSettings();
                writeLine($"[LSLHelper] Added {args[1].Trim()} to allowed owners.");
                break;
            case "deny":
                if (args.Length < 2) { writeLine("Usage: lslhelper deny <owner-uuid>"); return; }
                _lslAllowedOwners.Remove(args[1].Trim());
                SaveSettings();
                writeLine($"[LSLHelper] Removed {args[1].Trim()} from allowed owners.");
                break;
            default:
                writeLine("Usage: lslhelper on|off|status|allow <uuid>|deny <uuid>");
                break;
        }
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        // Switch to the per-avatar rules file now that we know the agent ID.
        var avatarPath = RulePersistence.AvatarPath(_ctx.Client.Self.AgentID.ToString());
        if (avatarPath != _rulesFilePath)
        {
            _rulesFilePath = avatarPath;
            var loaded = RulePersistence.Load(_rulesFilePath);
            _ruleEngine!.ReplaceRules(loaded);
            lock (_rules)
            {
                _rules.Clear();
                _rules.AddRange(loaded);
            }
        }
        LoadSettings();
        UpdateAutoSitTimer();
        UpdatePseudoHomeTimer();
        // Re-start the rule engine after reconnect so its event handlers are live.
        _ruleEngine?.Stop();
        _ruleEngine?.Start();
    }

    // ── Import / Export ────────────────────────────────────────────

    private async void ImportRulesAsync()
    {
        var files = await _ctx.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Automation Rules",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON Rules") { Patterns = ["*.json"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] },
            ]
        });

        if (files is not [var file]) return;
        var imported = RulePersistence.Import(file.Path.LocalPath);
        if (imported.Count == 0)
        {
            _ctx.LogToChat("[Automation] Import: no rules found or file could not be parsed.");
            return;
        }
        _ruleEngine!.ReplaceRules(imported);
        lock (_rules)
        {
            _rules.Clear();
            _rules.AddRange(imported);
        }
        RulePersistence.Save(_rulesFilePath, _rules);
        _ctx.LogToChat($"[Automation] Imported {imported.Count} rule(s) from {file.Name}.");
    }

    private async void ExportRulesAsync()
    {
        var file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Automation Rules",
            SuggestedFileName = "automation_rules.json",
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("JSON Rules") { Patterns = ["*.json"] },
            ]
        });

        if (file == null) return;
        List<AutomationRule> snapshot;
        lock (_rules) { snapshot = [.._rules]; }
        RulePersistence.Export(file.Path.LocalPath, snapshot);
        _ctx.LogToChat($"[Automation] Exported {snapshot.Count} rule(s) to {file.Name}.");
    }

    // ── Rule command handler ───────────────────────────────────────

    private void OnRuleCommand(string[] args, Action<string> w)
    {
        if (args.Length == 0)
        {
            w("Usage: rule list|show <id>|remove <id>|enable <id>|disable <id>");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "list":
            {
                var rules = _ruleEngine!.GetRules();
                if (rules.Count == 0) { w("No automation rules defined."); return; }
                foreach (var rule in rules)
                    w($"  [{rule.Id}] {(rule.Enabled ? "ON " : "off")} {rule.Trigger.Type,-20}  → {rule.Actions.Count} action(s)  \"{rule.Name}\"");
                break;
            }
            case "show":
            {
                if (args.Length < 2) { w("Usage: rule show <id>"); return; }
                var rule = _ruleEngine!.FindRule(args[1]);
                if (rule == null) { w($"Rule '{args[1]}' not found."); return; }
                w($"Id:       {rule.Id}");
                w($"Name:     {rule.Name}");
                w($"Enabled:  {rule.Enabled}");
                w($"Cooldown: {rule.CooldownSeconds}s");
                w($"Trigger:  {rule.Trigger.Type}");
                if (rule.Trigger.Type is TriggerType.ProximityEnter or TriggerType.ProximityLeave)
                    w($"  Radius: {rule.Trigger.ProximityRadius}m");
                if (rule.Trigger.Type == TriggerType.PaymentReceived)
                    w($"  MinAmount: L${rule.Trigger.MinPaymentAmount}");
                if (!string.IsNullOrEmpty(rule.Trigger.Keyword))
                    w($"  Keyword: \"{rule.Trigger.Keyword}\"");
                for (int i = 0; i < rule.Actions.Count; i++)
                {
                    var a = rule.Actions[i];
                    w($"Action[{i}]: {a.Type}");
                    if (a.Type == ActionType.InviteToGroup)  w($"  Group={a.GroupId} Role={a.RoleId}");
                    if (a.Type == ActionType.SendIM)          w($"  Message=\"{a.Message}\"");
                    if (a.Type == ActionType.SendChat)        w($"  Ch={a.ChatChannel} Message=\"{a.Message}\"");
                    if (a.Type == ActionType.GiveInventory)   w($"  Item={a.InventoryItemId}");
                }
                break;
            }
            case "remove":
            {
                if (args.Length < 2) { w("Usage: rule remove <id>"); return; }
                if (_ruleEngine!.RemoveRule(args[1]))
                {
                    RulePersistence.Save(_rulesFilePath, _rules);
                    w($"Rule '{args[1]}' removed.");
                }
                else w($"Rule '{args[1]}' not found.");
                break;
            }
            case "enable":
            {
                if (args.Length < 2) { w("Usage: rule enable <id>"); return; }
                if (_ruleEngine!.SetEnabled(args[1], true))
                {
                    RulePersistence.Save(_rulesFilePath, _rules);
                    w($"Rule '{args[1]}' enabled.");
                }
                else w($"Rule '{args[1]}' not found.");
                break;
            }
            case "disable":
            {
                if (args.Length < 2) { w("Usage: rule disable <id>"); return; }
                if (_ruleEngine!.SetEnabled(args[1], false))
                {
                    RulePersistence.Save(_rulesFilePath, _rules);
                    w($"Rule '{args[1]}' disabled.");
                }
                else w($"Rule '{args[1]}' not found.");
                break;
            }
            default:
                w("Usage: rule list|show <id>|remove <id>|enable <id>|disable <id>");
                break;
        }
    }

    // ── Group Inviter quick-add command ────────────────────────────

    private void OnGroupInviterCommand(string[] args, Action<string> w)
    {
        // groupinviter add <group-uuid> [role-uuid] [proximity|payment|im] [radius|minamount|keyword]
        if (args.Length < 2 || args[0].ToLowerInvariant() != "add")
        {
            w("Usage: groupinviter add <group-uuid> [role-uuid] [proximity|payment|im] [radius|minamount]");
            w("  proximity <radius>  — invite when avatar enters within <radius> metres (default 10)");
            w("  payment <amount>    — invite when avatar pays at least L$<amount> (default any)");
            w("  im [keyword]        — invite when avatar sends an IM (optional keyword filter)");
            return;
        }

        if (!UUID.TryParse(args[1], out var groupId) || groupId == UUID.Zero)
        {
            w("Invalid group UUID.");
            return;
        }

        var roleId  = UUID.Zero;
        int nextArg = 2;
        if (args.Length > nextArg && UUID.TryParse(args[nextArg], out var parsedRole))
        {
            roleId = parsedRole;
            nextArg++;
        }

        var triggerStr = args.Length > nextArg ? args[nextArg].ToLowerInvariant() : "proximity";
        nextArg++;

        TriggerConfig trigger;
        string triggerDesc;

        switch (triggerStr)
        {
            case "payment":
            {
                int min = 0;
                if (args.Length > nextArg && int.TryParse(args[nextArg], out var amt)) min = amt;
                trigger = new TriggerConfig { Type = TriggerType.PaymentReceived, MinPaymentAmount = min };
                triggerDesc = min > 0 ? $"payment ≥ L${min}" : "any payment";
                break;
            }
            case "im":
            {
                var kw = args.Length > nextArg ? args[nextArg] : string.Empty;
                trigger = new TriggerConfig { Type = TriggerType.IMReceived, Keyword = kw };
                triggerDesc = string.IsNullOrEmpty(kw) ? "any IM" : $"IM containing \"{kw}\"";
                break;
            }
            default: // proximity
            {
                float radius = 10f;
                if (args.Length > nextArg && float.TryParse(args[nextArg], out var r)) radius = r;
                trigger = new TriggerConfig { Type = TriggerType.ProximityEnter, ProximityRadius = radius };
                triggerDesc = $"proximity within {radius}m";
                break;
            }
        }

        var rule = new AutomationRule
        {
            Name    = $"GroupInviter {groupId.ToString()[..8]}",
            Enabled = true,
            Trigger = trigger,
            Actions =
            [
                new ActionConfig
                {
                    Type    = ActionType.InviteToGroup,
                    GroupId = groupId.ToString(),
                    RoleId  = roleId != UUID.Zero ? roleId.ToString() : string.Empty,
                }
            ],
            // 5-minute per-agent cooldown prevents re-inviting on every tick/payment.
            CooldownSeconds = 300f,
        };

        _ruleEngine!.AddRule(rule);
        RulePersistence.Save(_rulesFilePath, _rules);
        w($"[GroupInviter] Rule '{rule.Id}' created: invite to {groupId} on {triggerDesc}.");
        w($"  Use 'rule disable {rule.Id}' to pause or 'rule remove {rule.Id}' to delete.");
    }
}
