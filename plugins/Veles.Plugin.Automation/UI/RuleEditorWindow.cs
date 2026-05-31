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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Veles.Plugin.Automation.UI;

/// <summary>
/// Simple, code-only dialog for editing a single <see cref="AutomationRule"/>.
/// Fields are written directly back to the rule on OK.
/// </summary>
internal sealed class RuleEditorWindow : Window
{
    /// <summary>True when the user clicked OK and the rule was updated.</summary>
    public bool Committed { get; private set; }

    private readonly AutomationRule _rule;

    // ── Controls ───────────────────────────────────────────────────
    private readonly TextBox _nameBox;
    private readonly CheckBox _enabledBox;
    private readonly ComboBox _triggerTypeBox;
    private readonly NumericUpDown _radiusBox;
    private readonly NumericUpDown _minPaymentBox;
    private readonly TextBox _keywordBox;
    private readonly NumericUpDown _chatChannelBox;
    private readonly NumericUpDown _cooldownBox;

    // Action list (each entry is a stack panel of controls)
    private readonly StackPanel _actionsPanel;
    private readonly List<ActionEditorRow> _actionRows = [];

    public RuleEditorWindow(AutomationRule rule)
    {
        _rule = rule;
        Title = string.IsNullOrEmpty(rule.Name) ? "New Automation Rule" : $"Edit: {rule.Name}";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ── Build controls ────────────────────────────────────────
        _nameBox    = new TextBox { Text = rule.Name, PlaceholderText = "Rule name" };
        _enabledBox = new CheckBox { Content = "Enabled", IsChecked = rule.Enabled };

        _triggerTypeBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<TriggerType>(),
            SelectedItem = rule.Trigger.Type,
        };
        _triggerTypeBox.SelectionChanged += (_, _) => UpdateTriggerVisibility();

        _radiusBox = new NumericUpDown
        {
            Minimum = 1, Maximum = 96, Increment = 1,
            Value = (decimal)rule.Trigger.ProximityRadius,
            FormatString = "0.#",
        };
        _minPaymentBox = new NumericUpDown
        {
            Minimum = 0, Maximum = 100000, Increment = 1,
            Value = rule.Trigger.MinPaymentAmount,
        };
        _keywordBox    = new TextBox { Text = rule.Trigger.Keyword, PlaceholderText = "Keyword (optional)" };
        _chatChannelBox = new NumericUpDown
        {
            Minimum = -1, Maximum = 2147483647, Increment = 1,
            Value = rule.Trigger.ChatChannel,
        };
        _cooldownBox = new NumericUpDown
        {
            Minimum = 0, Maximum = 86400, Increment = 1,
            Value = (decimal)rule.CooldownSeconds,
            FormatString = "0",
        };

        // ── Actions area ──────────────────────────────────────────
        _actionsPanel = new StackPanel { Spacing = 4 };
        foreach (var action in rule.Actions)
            AddActionRow(action);

        var addActionBtn = new Button { Content = "Add Action", Margin = new Thickness(0, 4, 0, 0) };
        addActionBtn.Click += (_, _) => AddActionRow(new ActionConfig());

        // ── OK / Cancel ────────────────────────────────────────────
        var okBtn     = new Button { Content = "OK",     IsDefault = true, MinWidth = 80 };
        var cancelBtn = new Button { Content = "Cancel", IsCancel  = true, MinWidth = 80 };
        okBtn.Click     += OnOk;
        cancelBtn.Click += (_, _) => Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin  = new Thickness(0, 8, 0, 0),
        };
        buttonRow.Children.Add(okBtn);
        buttonRow.Children.Add(cancelBtn);

        // ── Trigger section ────────────────────────────────────────
        var triggerSection = new StackPanel { Spacing = 4 };
        triggerSection.Children.Add(Label("Trigger type"));
        triggerSection.Children.Add(_triggerTypeBox);

        _radiusBox.Tag = "proximity";
        _minPaymentBox.Tag = "payment";
        _keywordBox.Tag = "keyword";
        _chatChannelBox.Tag = "chatchannel";

        triggerSection.Children.Add(LabeledRow("Radius (m)", _radiusBox));
        triggerSection.Children.Add(LabeledRow("Min L$ amount (0=any)", _minPaymentBox));
        triggerSection.Children.Add(LabeledRow("Keyword filter", _keywordBox));
        triggerSection.Children.Add(LabeledRow("Chat channel (-1=any)", _chatChannelBox));

        // ── Root layout ────────────────────────────────────────────
        var root = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        root.Children.Add(Label("Name"));
        root.Children.Add(_nameBox);
        root.Children.Add(_enabledBox);
        root.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        root.Children.Add(Label("Trigger"));
        root.Children.Add(triggerSection);
        root.Children.Add(LabeledRow("Cooldown (seconds)", _cooldownBox));
        root.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        root.Children.Add(Label("Actions"));
        root.Children.Add(_actionsPanel);
        root.Children.Add(addActionBtn);
        root.Children.Add(buttonRow);

        Content = new ScrollViewer { Content = root };
        UpdateTriggerVisibility();
    }

    // ── Action rows ────────────────────────────────────────────────

    private void AddActionRow(ActionConfig action)
    {
        var row = new ActionEditorRow(action, () =>
        {
            var r = _actionRows.Find(x => x.Action == action);
            if (r == null) return;
            _actionsPanel.Children.Remove(r.Panel);
            _actionRows.Remove(r);
        });
        _actionRows.Add(row);
        _actionsPanel.Children.Add(row.Panel);
    }

    // ── Trigger field visibility ───────────────────────────────────

    private void UpdateTriggerVisibility()
    {
        var type = (TriggerType?)_triggerTypeBox.SelectedItem ?? TriggerType.ProximityEnter;
        bool proximity = type is TriggerType.ProximityEnter or TriggerType.ProximityLeave;
        bool payment   = type == TriggerType.PaymentReceived;
        bool keyword   = type is TriggerType.IMReceived or TriggerType.ChatReceived;
        bool chatCh    = type == TriggerType.ChatReceived;

        SetVisible(_radiusBox,      proximity);
        SetVisible(_minPaymentBox,  payment);
        SetVisible(_keywordBox,     keyword);
        SetVisible(_chatChannelBox, chatCh);
    }

    private static void SetVisible(Control c, bool visible)
    {
        // Walk up to find the labeled row Grid and toggle it
        if (c.Parent is Grid g)
            g.IsVisible = visible;
        else
            c.IsVisible = visible;
    }

    // ── OK handler ─────────────────────────────────────────────────

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _rule.Name    = _nameBox.Text?.Trim() ?? string.Empty;
        _rule.Enabled = _enabledBox.IsChecked == true;
        _rule.CooldownSeconds = (float)(_cooldownBox.Value ?? 30m);

        _rule.Trigger.Type               = (TriggerType?)_triggerTypeBox.SelectedItem ?? TriggerType.ProximityEnter;
        _rule.Trigger.ProximityRadius    = (float)(_radiusBox.Value ?? 10m);
        _rule.Trigger.MinPaymentAmount   = (int)(_minPaymentBox.Value ?? 0m);
        _rule.Trigger.Keyword            = _keywordBox.Text?.Trim() ?? string.Empty;
        _rule.Trigger.ChatChannel        = (int)(_chatChannelBox.Value ?? -1m);

        _rule.Actions.Clear();
        foreach (var row in _actionRows)
        {
            row.Commit();
            _rule.Actions.Add(row.Action);
        }

        Committed = true;
        Close();
    }

    // ── Static helpers ─────────────────────────────────────────────

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 12,
    };

    private static Grid LabeledRow(string label, Control control)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), Margin = new Thickness(0, 1) };
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }
}

/// <summary>
/// Represents one editable action row inside <see cref="RuleEditorWindow"/>.
/// </summary>
internal sealed class ActionEditorRow
{
    public ActionConfig Action { get; }
    public Control Panel { get; }

    private readonly ComboBox _typeBox;
    private readonly TextBox  _groupIdBox;
    private readonly TextBox  _roleIdBox;
    private readonly TextBox  _messageBox;
    private readonly NumericUpDown _chatChannelBox;
    private readonly TextBox  _inventoryIdBox;

    public ActionEditorRow(ActionConfig action, Action onRemove)
    {
        Action = action;

        _typeBox = new ComboBox
        {
            ItemsSource  = Enum.GetValues<ActionType>(),
            SelectedItem = action.Type,
            MinWidth     = 140,
        };
        _typeBox.SelectionChanged += (_, _) => UpdateVisibility();

        _groupIdBox     = new TextBox { Text = action.GroupId, PlaceholderText = "Group UUID" };
        _roleIdBox      = new TextBox { Text = action.RoleId, PlaceholderText = "Role UUID (blank=default)" };
        _messageBox     = new TextBox { Text = action.Message, PlaceholderText = "Message ({agent_id}, {agent_name})" };
        _chatChannelBox = new NumericUpDown { Minimum = 0, Maximum = 2147483647, Value = action.ChatChannel, MinWidth = 80 };
        _inventoryIdBox = new TextBox { Text = action.InventoryItemId, PlaceholderText = "Inventory item UUID" };

        var removeBtn = new Button { Content = "✕", Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Top };
        removeBtn.Click += (_, _) => onRemove();

        var fields = new StackPanel { Spacing = 3, Margin = new Thickness(0, 2) };
        fields.Children.Add(_typeBox);
        fields.Children.Add(FieldRow("Group UUID",      _groupIdBox));
        fields.Children.Add(FieldRow("Role UUID",       _roleIdBox));
        fields.Children.Add(FieldRow("Message",         _messageBox));
        fields.Children.Add(FieldRow("Chat channel",    _chatChannelBox));
        fields.Children.Add(FieldRow("Inventory UUID",  _inventoryIdBox));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,28") };
        Grid.SetColumn(fields, 0);
        Grid.SetColumn(removeBtn, 1);
        row.Children.Add(fields);
        row.Children.Add(removeBtn);

        Panel = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(0, 4),
            Child           = row,
        };

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var type = (ActionType?)_typeBox.SelectedItem ?? ActionType.SendIM;
        bool isGroup  = type == ActionType.InviteToGroup;
        bool isIM     = type == ActionType.SendIM;
        bool isChat   = type == ActionType.SendChat;
        bool isInv    = type == ActionType.GiveInventory;
        bool isSpeak  = type == ActionType.SpeakToVoice;

        SetRowVisible(_groupIdBox,     isGroup);
        SetRowVisible(_roleIdBox,      isGroup);
        SetRowVisible(_messageBox,     isIM || isChat || isSpeak);
        SetRowVisible(_chatChannelBox, isChat);
        SetRowVisible(_inventoryIdBox, isInv);
    }

    private static void SetRowVisible(Control c, bool visible)
    {
        if (c.Parent is Grid g)
            g.IsVisible = visible;
        else
            c.IsVisible = visible;
    }

    public void Commit()
    {
        Action.Type            = (ActionType?)_typeBox.SelectedItem ?? ActionType.SendIM;
        Action.GroupId         = _groupIdBox.Text?.Trim() ?? string.Empty;
        Action.RoleId          = _roleIdBox.Text?.Trim() ?? string.Empty;
        Action.Message         = _messageBox.Text?.Trim() ?? string.Empty;
        Action.ChatChannel     = (int)(_chatChannelBox.Value ?? 0m);
        Action.InventoryItemId = _inventoryIdBox.Text?.Trim() ?? string.Empty;
    }

    private static Grid FieldRow(string label, Control control)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), Margin = new Thickness(0, 1) };
        var lbl = new TextBlock
        {
            Text = label, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
            Margin  = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }
}
