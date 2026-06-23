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
using System.Linq;
using System.Threading;
using LibreMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.Automation;

/// <summary>
/// Evaluates <see cref="AutomationRule"/> instances and executes their
/// <see cref="ActionConfig"/>s when the corresponding trigger fires.
///
/// Trigger sources wired here:
/// <list type="bullet">
///   <item><see cref="TriggerType.ProximityEnter"/> / <see cref="TriggerType.ProximityLeave"/>
///         — polled via a <see cref="Timer"/> at ~2 Hz using <c>ObjectsAvatars</c>.</item>
///   <item><see cref="TriggerType.PaymentReceived"/> — <c>Client.Self.MoneyBalanceReply</c>.</item>
///   <item><see cref="TriggerType.IMReceived"/> — <c>IPluginContext.IMReceived</c>.</item>
///   <item><see cref="TriggerType.ChatReceived"/> — <c>IPluginContext.ChatReceived</c>.</item>
///   <item><see cref="TriggerType.FriendOnline"/> / <see cref="TriggerType.FriendOffline"/>
///         — <c>IPluginContext.FriendOnline</c> / <c>IPluginContext.FriendOffline</c>.</item>
/// </list>
/// </summary>
internal sealed class RuleEngine : IDisposable
{
    private readonly IPluginContext _ctx;
    private readonly List<AutomationRule> _rules;
    private readonly object _rulesLock = new();

    // Cooldown tracking: (ruleId, agentId) → time the rule last fired for that agent.
    private readonly Dictionary<(string RuleId, UUID AgentId), DateTime> _lastFired = new();

    // Proximity state: agents currently within range of *any* proximity rule.
    // Key = agentId, Value = set of ruleIds that currently have that agent "in range".
    private readonly Dictionary<UUID, HashSet<string>> _inRange = new();

    private Timer? _proximityTimer;
    private bool _disposed;

    public RuleEngine(IPluginContext ctx, List<AutomationRule> rules)
    {
        _ctx   = ctx;
        _rules = rules;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public void Start()
    {
        _ctx.IMReceived      += OnIMReceived;
        _ctx.ChatReceived    += OnChatReceived;
        _ctx.FriendOnline    += OnFriendOnline;
        _ctx.FriendOffline   += OnFriendOffline;
        _ctx.Client.Self.MoneyBalanceReply += OnMoneyBalanceReply;

        _proximityTimer = new Timer(_ => PollProximity(), null,
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public void Stop()
    {
        _ctx.IMReceived      -= OnIMReceived;
        _ctx.ChatReceived    -= OnChatReceived;
        _ctx.FriendOnline    -= OnFriendOnline;
        _ctx.FriendOffline   -= OnFriendOffline;
        _ctx.Client.Self.MoneyBalanceReply -= OnMoneyBalanceReply;

        _proximityTimer?.Dispose();
        _proximityTimer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Rule list management ───────────────────────────────────────────────────

    public List<AutomationRule> GetRules()
    {
        lock (_rulesLock) return [.._rules];
    }

    public void AddRule(AutomationRule rule)
    {
        lock (_rulesLock) _rules.Add(rule);
    }

    public bool RemoveRule(string id)
    {
        lock (_rulesLock) return _rules.RemoveAll(r => r.Id == id) > 0;
    }

    public AutomationRule? FindRule(string id)
    {
        lock (_rulesLock) return _rules.Find(r => r.Id == id);
    }

    public bool SetEnabled(string id, bool enabled)
    {
        lock (_rulesLock)
        {
            var rule = _rules.Find(r => r.Id == id);
            if (rule == null) return false;
            rule.Enabled = enabled;
            return true;
        }
    }

    /// <summary>
    /// Replace the entire rule set (used after import or UI save).
    /// </summary>
    public void ReplaceRules(IEnumerable<AutomationRule> newRules)
    {
        lock (_rulesLock)
        {
            _rules.Clear();
            _rules.AddRange(newRules);
        }
    }

    // ── Proximity polling ──────────────────────────────────────────────────────

    private void PollProximity()
    {
        if (!_ctx.Client.Network.Connected) return;

        List<AutomationRule> snapshot;
        lock (_rulesLock)
        {
            snapshot = _rules.FindAll(r => r.Enabled &&
                (r.Trigger.Type == TriggerType.ProximityEnter ||
                 r.Trigger.Type == TriggerType.ProximityLeave));
        }

        if (snapshot.Count == 0) return;

        var selfPos = _ctx.Client.Self.SimPosition;
        var avatarsNow = new HashSet<UUID>();

        var sim = _ctx.Client.Network.CurrentSim;
        if (sim == null) return;

        foreach (var kv in sim.ObjectsAvatars)
        {
            if (kv.Value?.ID == null || kv.Value.ID == _ctx.Client.Self.AgentID) continue;
            avatarsNow.Add(kv.Value.ID);
        }

        foreach (var rule in snapshot)
        {
            float r2 = rule.Trigger.ProximityRadius * rule.Trigger.ProximityRadius;

            foreach (var agentId in avatarsNow)
            {
                // Look up the avatar object to get its position.
                Avatar? av = sim.ObjectsAvatars.Values.FirstOrDefault(a => a?.ID == agentId);
                if (av == null) continue;

                float dist2 = Vector3.DistanceSquared(selfPos, av.Position);
                bool wasIn;
                lock (_inRange) { wasIn = _inRange.TryGetValue(agentId, out var ruleSet) && ruleSet.Contains(rule.Id); }
                bool nowIn = dist2 <= r2;

                if (rule.Trigger.Type == TriggerType.ProximityEnter && !wasIn && nowIn)
                {
                    MarkInRange(agentId, rule.Id, true);
                    TryFire(rule, agentId, av.Name);
                }
                else if (rule.Trigger.Type == TriggerType.ProximityLeave && wasIn && !nowIn)
                {
                    MarkInRange(agentId, rule.Id, false);
                    TryFire(rule, agentId, av.Name);
                }
                else if (nowIn)
                {
                    MarkInRange(agentId, rule.Id, true);
                }
            }

            // Avatars that left the sim entirely count as leaving range too.
            lock (_inRange)
            {
                foreach (var agentId in new List<UUID>(_inRange.Keys))
                {
                    if (avatarsNow.Contains(agentId)) continue;
                    if (!_inRange.TryGetValue(agentId, out var rs) || !rs.Contains(rule.Id)) continue;

                    MarkInRange(agentId, rule.Id, false);
                    if (rule.Trigger.Type == TriggerType.ProximityLeave && rule.Enabled)
                        TryFire(rule, agentId, agentId.ToString());
                }
            }
        }
    }

    private void MarkInRange(UUID agentId, string ruleId, bool inside)
    {
        lock (_inRange)
        {
            if (!_inRange.TryGetValue(agentId, out var set))
            {
                set = [];
                _inRange[agentId] = set;
            }
            if (inside) set.Add(ruleId);
            else        set.Remove(ruleId);
        }
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnMoneyBalanceReply(object? sender, MoneyBalanceReplyEventArgs e)
    {
        if (!e.Success) return;
        var info = e.TransactionInfo;
        if (info == null || info.Amount <= 0) return;
        // TransactionType 5000 = ObjectPays / AvatarPays (payment *to* us).
        // SourceID is the payer; DestID should be our agent.
        if (info.DestID != _ctx.Client.Self.AgentID) return;

        UUID payer = info.SourceID;
        if (payer == UUID.Zero) return;

        List<AutomationRule> snapshot;
        lock (_rulesLock) snapshot = _rules.FindAll(r => r.Enabled && r.Trigger.Type == TriggerType.PaymentReceived);

        foreach (var rule in snapshot)
        {
            if (rule.Trigger.MinPaymentAmount > 0 && info.Amount < rule.Trigger.MinPaymentAmount) continue;
            TryFire(rule, payer, payer.ToString());
        }
    }

    private void OnIMReceived(object? sender, InstantMessageEventArgs e)
    {
        // Only handle IMs from avatars (not objects).
        if (e.IM.Dialog != InstantMessageDialog.MessageFromAgent) return;
        UUID from = e.IM.FromAgentID;
        if (from == UUID.Zero || from == _ctx.Client.Self.AgentID) return;

        List<AutomationRule> snapshot;
        lock (_rulesLock) snapshot = _rules.FindAll(r => r.Enabled && r.Trigger.Type == TriggerType.IMReceived);

        foreach (var rule in snapshot)
        {
            if (!string.IsNullOrEmpty(rule.Trigger.Keyword) &&
                !e.IM.Message.Contains(rule.Trigger.Keyword, StringComparison.OrdinalIgnoreCase))
                continue;
            TryFire(rule, from, e.IM.FromAgentName);
        }
    }

    private void OnChatReceived(object? sender, ChatEventArgs e)
    {
        if (e.SourceType != ChatSourceType.Agent) return;
        UUID from = e.SourceID;
        if (from == UUID.Zero || from == _ctx.Client.Self.AgentID) return;

        List<AutomationRule> snapshot;
        lock (_rulesLock) snapshot = _rules.FindAll(r => r.Enabled && r.Trigger.Type == TriggerType.ChatReceived);

        foreach (var rule in snapshot)
        {
            // ChatEventArgs has no channel number; ChatChannel == 0 means open chat only
            // (Type == Normal/Whisper/Shout); any other value disables the channel filter.
            if (rule.Trigger.ChatChannel == 0 &&
                e.Type != ChatType.Normal && e.Type != ChatType.Whisper && e.Type != ChatType.Shout)
                continue;
            if (!string.IsNullOrEmpty(rule.Trigger.Keyword) &&
                !e.Message.Contains(rule.Trigger.Keyword, StringComparison.OrdinalIgnoreCase))
                continue;
            TryFire(rule, from, e.FromName);
        }
    }

    private void OnFriendOnline(object? sender, FriendInfoEventArgs e)
    {
        List<AutomationRule> snapshot;
        lock (_rulesLock) snapshot = _rules.FindAll(r => r.Enabled && r.Trigger.Type == TriggerType.FriendOnline);
        foreach (var rule in snapshot)
            TryFire(rule, e.Friend.UUID, e.Friend.Name);
    }

    private void OnFriendOffline(object? sender, FriendInfoEventArgs e)
    {
        List<AutomationRule> snapshot;
        lock (_rulesLock) snapshot = _rules.FindAll(r => r.Enabled && r.Trigger.Type == TriggerType.FriendOffline);
        foreach (var rule in snapshot)
            TryFire(rule, e.Friend.UUID, e.Friend.Name);
    }

    // ── Core fire logic ────────────────────────────────────────────────────────

    private void TryFire(AutomationRule rule, UUID agentId, string agentName)
    {
        if (rule.CooldownSeconds > 0f)
        {
            var key = (rule.Id, agentId);
            lock (_lastFired)
            {
                if (_lastFired.TryGetValue(key, out var last) &&
                    (DateTime.UtcNow - last).TotalSeconds < rule.CooldownSeconds)
                    return;
                _lastFired[key] = DateTime.UtcNow;
            }
        }

        foreach (var action in rule.Actions)
        {
            try { ExecuteAction(action, agentId, agentName); }
            catch (Exception ex)
            {
                _ctx.LogToChat($"[Automation] Rule '{rule.Name}' action {action.Type} error: {ex.Message}");
            }
        }
    }

    private void ExecuteAction(ActionConfig action, UUID agentId, string agentName)
    {
        switch (action.Type)
        {
            case ActionType.InviteToGroup:
            {
                if (!UUID.TryParse(action.GroupId, out var groupId) || groupId == UUID.Zero) return;
                UUID.TryParse(action.RoleId, out var roleId);
                _ctx.Client.Groups.Invite(groupId, [roleId], agentId);
                _ctx.LogToChat($"[Automation] Invited {agentName} to group {groupId}.");
                break;
            }

            case ActionType.SendIM:
            {
                var msg = Substitute(action.Message, agentId, agentName);
                _ctx.Client.Self.InstantMessage(agentId, msg);
                break;
            }

            case ActionType.SendChat:
            {
                var msg = Substitute(action.Message, agentId, agentName);
                _ctx.Client.Self.Chat(msg, action.ChatChannel, ChatType.Normal);
                break;
            }

            case ActionType.GiveInventory:
            {
                if (!UUID.TryParse(action.InventoryItemId, out var itemId) || itemId == UUID.Zero) return;
                if (_ctx.Client.Inventory.Store?.Contains(itemId) != true)
                {
                    _ctx.LogToChat($"[Automation] GiveInventory: item {itemId} not in inventory.");
                    return;
                }
                if (_ctx.Client.Inventory.Store![itemId] is InventoryItem item)
                    _ctx.Client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, agentId, true);
                break;
            }

            case ActionType.SpeakToVoice:
            {
                var msg = Substitute(action.Message, agentId, agentName);
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    if (_ctx.VoiceSynth?.IsReady == true)
                        _ctx.VoiceSynth.Speak(msg);
                    else
                        _ctx.LogToChat("[Automation] SpeakToVoice: voice synth is not ready (no model loaded or voice unavailable).");
                }
                break;
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string Substitute(string template, UUID agentId, string agentName)
        => template
            .Replace("{agent_id}",   agentId.ToString(),  StringComparison.OrdinalIgnoreCase)
            .Replace("{agent_name}", agentName,            StringComparison.OrdinalIgnoreCase);
}
