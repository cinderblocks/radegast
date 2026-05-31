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

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Veles.Plugin.Automation.UI;

/// <summary>
/// Observable wrapper around <see cref="AutomationRule"/> for use in the rules list.
/// </summary>
internal sealed class AutomationRuleViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public AutomationRule Rule { get; }

    private string _name;
    private bool _enabled;
    private string _triggerSummary;
    private string _actionSummary;

    public string Name
    {
        get => _name;
        set { _name = value; Notify(); }
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Notify(); }
    }

    public string TriggerSummary
    {
        get => _triggerSummary;
        set { _triggerSummary = value; Notify(); }
    }

    public string ActionSummary
    {
        get => _actionSummary;
        set { _actionSummary = value; Notify(); }
    }

    public AutomationRuleViewModel(AutomationRule rule)
    {
        Rule            = rule;
        _name           = rule.Name;
        _enabled        = rule.Enabled;
        _triggerSummary = BuildTriggerSummary(rule);
        _actionSummary  = BuildActionSummary(rule);
    }

    public void Refresh()
    {
        Name           = Rule.Name;
        Enabled        = Rule.Enabled;
        TriggerSummary = BuildTriggerSummary(Rule);
        ActionSummary  = BuildActionSummary(Rule);
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string BuildTriggerSummary(AutomationRule rule)
    {
        var t = rule.Trigger;
        return t.Type switch
        {
            TriggerType.ProximityEnter  => $"Proximity enter ({t.ProximityRadius}m)",
            TriggerType.ProximityLeave  => $"Proximity leave ({t.ProximityRadius}m)",
            TriggerType.PaymentReceived => t.MinPaymentAmount > 0
                ? $"Payment ≥ L${t.MinPaymentAmount}"
                : "Any payment",
            TriggerType.IMReceived  => string.IsNullOrEmpty(t.Keyword)
                ? "Any IM"
                : $"IM contains \"{t.Keyword}\"",
            TriggerType.ChatReceived => string.IsNullOrEmpty(t.Keyword)
                ? "Any chat"
                : $"Chat contains \"{t.Keyword}\"",
            TriggerType.FriendOnline  => "Friend online",
            TriggerType.FriendOffline => "Friend offline",
            _ => t.Type.ToString()
        };
    }

    private static string BuildActionSummary(AutomationRule rule)
    {
        if (rule.Actions.Count == 0) return "(no actions)";
        if (rule.Actions.Count == 1) return rule.Actions[0].Type.ToString();
        return $"{rule.Actions[0].Type} +{rule.Actions.Count - 1} more";
    }
}
