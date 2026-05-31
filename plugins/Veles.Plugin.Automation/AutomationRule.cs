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

namespace Veles.Plugin.Automation;

/// <summary>
/// The event that causes a rule to evaluate.
/// </summary>
public enum TriggerType
{
    /// <summary>An avatar enters within <see cref="TriggerConfig.ProximityRadius"/> metres of the client.</summary>
    ProximityEnter,

    /// <summary>An avatar that was in range leaves <see cref="TriggerConfig.ProximityRadius"/> metres.</summary>
    ProximityLeave,

    /// <summary>An avatar pays the client (optionally with a minimum amount).</summary>
    PaymentReceived,

    /// <summary>The client receives an IM from an avatar (optional keyword match).</summary>
    IMReceived,

    /// <summary>Nearby chat is received (optional keyword and channel filter).</summary>
    ChatReceived,

    /// <summary>A friend comes online.</summary>
    FriendOnline,

    /// <summary>A friend goes offline.</summary>
    FriendOffline,
}

/// <summary>
/// The operation performed when a rule fires.
/// </summary>
public enum ActionType
{
    /// <summary>Invite the triggering agent to a group.</summary>
    InviteToGroup,

    /// <summary>Send an instant message to the triggering agent.</summary>
    SendIM,

    /// <summary>Say something in nearby chat (or on a channel).</summary>
    SendChat,

    /// <summary>Give an inventory item to the triggering agent.</summary>
    GiveInventory,

    /// <summary>Synthesise text and transmit it over the active parcel voice channel via the voice-synth service.</summary>
    SpeakToVoice,
}

/// <summary>
/// Configures what conditions must be met for a rule to fire.
/// Fields not relevant to a given <see cref="TriggerType"/> are ignored.
/// </summary>
public sealed class TriggerConfig
{
    public TriggerType Type { get; set; }

    /// <summary>Radius in metres used by <see cref="TriggerType.ProximityEnter"/> and <see cref="TriggerType.ProximityLeave"/>.</summary>
    public float ProximityRadius { get; set; } = 10f;

    /// <summary>Minimum L$ amount for <see cref="TriggerType.PaymentReceived"/>. 0 = any amount.</summary>
    public int MinPaymentAmount { get; set; } = 0;

    /// <summary>
    /// Optional keyword filter for <see cref="TriggerType.IMReceived"/> and
    /// <see cref="TriggerType.ChatReceived"/>. Empty string = match everything.
    /// Case-insensitive substring match.
    /// </summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Chat channel filter for <see cref="TriggerType.ChatReceived"/>. -1 = any channel.</summary>
    public int ChatChannel { get; set; } = -1;
}

/// <summary>
/// Configures what happens when a rule fires.
/// Fields not relevant to a given <see cref="ActionType"/> are ignored.
/// </summary>
public sealed class ActionConfig
{
    public ActionType Type { get; set; }

    /// <summary>Group UUID string for <see cref="ActionType.InviteToGroup"/>.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Role UUID string for <see cref="ActionType.InviteToGroup"/>.
    /// Empty string uses the group's default enrollment role (UUID.Zero).
    /// </summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>
    /// Message text for <see cref="ActionType.SendIM"/> and <see cref="ActionType.SendChat"/>.
    /// Supports token substitution: <c>{agent_id}</c> and <c>{agent_name}</c>.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Chat channel for <see cref="ActionType.SendChat"/>. Default 0 = open chat.</summary>
    public int ChatChannel { get; set; } = 0;

    /// <summary>Inventory item UUID string for <see cref="ActionType.GiveInventory"/>.</summary>
    public string InventoryItemId { get; set; } = string.Empty;
}

/// <summary>
/// A single automation rule: one trigger condition plus one or more resulting actions.
/// </summary>
public sealed class AutomationRule
{
    /// <summary>Unique identifier. Auto-generated; used in commands to reference the rule.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this rule is currently active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>What must happen for this rule to fire.</summary>
    public TriggerConfig Trigger { get; set; } = new();

    /// <summary>What happens when the rule fires. Executed in order.</summary>
    public List<ActionConfig> Actions { get; set; } = [];

    /// <summary>
    /// Minimum seconds between firings of this rule for the same agent.
    /// Prevents spam when the same trigger keeps repeating.
    /// 0 = no cooldown.
    /// </summary>
    public float CooldownSeconds { get; set; } = 30f;
}
