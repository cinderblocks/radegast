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
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace Veles.Plugin.Macros.UI;

/// <summary>
/// Code-only dialog for editing a single <see cref="Macro"/> and its steps.
/// Writes directly to the macro object on OK.
/// </summary>
internal sealed class MacroEditorWindow : Window
{
    /// <summary>True when the user confirmed the edit.</summary>
    public bool Committed { get; private set; }

    private readonly Macro _macro;

    // ── Header controls ────────────────────────────────────────────
    private readonly TextBox   _nameBox;
    private readonly CheckBox  _enabledBox;

    // ── Step list ──────────────────────────────────────────────────
    private readonly ObservableCollection<MacroStep> _steps;
    private readonly ListBox    _stepList;
    private MacroStep? SelectedStep => _stepList.SelectedItem as MacroStep;

    // ── Step parameter area ────────────────────────────────────────
    // Shared
    private readonly ComboBox       _typeBox;
    // Say
    private readonly TextBox        _textBox;
    private readonly NumericUpDown  _channelBox;
    private readonly ComboBox       _chatVolumeBox;
    // Wait
    private readonly NumericUpDown  _delayBox;
    // IM / Sit
    private readonly TextBox        _targetBox;
    // PlayGesture
    private readonly TextBox        _assetBox;

    // Panels that show/hide based on step type
    private readonly StackPanel     _sayPanel;
    private readonly StackPanel     _waitPanel;
    private readonly StackPanel     _imPanel;
    private readonly StackPanel     _sitPanel;
    private readonly StackPanel     _gesturePanel;
    private readonly StackPanel     _commandPanel;
    private readonly StackPanel     _emotePanel;

    private bool _suppressSync;

    public MacroEditorWindow(Macro macro)
    {
        _macro = macro;
        _steps = new ObservableCollection<MacroStep>(macro.Steps);

        Title = string.IsNullOrEmpty(macro.Name) ? "New Macro" : $"Edit: {macro.Name}";
        Width  = 560;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ── Header ────────────────────────────────────────────────
        _nameBox    = new TextBox { Text = macro.Name, PlaceholderText = "Macro name" };
        _enabledBox = new CheckBox { Content = "Enabled", IsChecked = macro.Enabled };

        // ── Step list + toolbar ───────────────────────────────────
        _stepList = new ListBox
        {
            ItemsSource  = _steps,
            MinHeight    = 120,
            MaxHeight    = 200,
            ItemTemplate = BuildStepTemplate(),
        };
        _stepList.SelectionChanged += (_, _) => OnStepSelected();

        var addTypeBox = new ComboBox
        {
            ItemsSource  = Enum.GetValues<StepType>(),
            SelectedItem = StepType.Say,
            MinWidth     = 120,
        };

        Button StepBtn(string label, Action act)
        { var b = new Button { Content = label }; b.Click += (_, _) => act(); return b; }

        var stepToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 4,
            Margin      = new Thickness(0, 4, 0, 4),
        };
        stepToolbar.Children.Add(new TextBlock { Text = "Add:", VerticalAlignment = VerticalAlignment.Center });
        stepToolbar.Children.Add(addTypeBox);
        stepToolbar.Children.Add(StepBtn("Add",    () => AddStep((StepType)addTypeBox.SelectedItem!)));
        stepToolbar.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 0) });
        stepToolbar.Children.Add(StepBtn("Remove", RemoveStep));
        stepToolbar.Children.Add(StepBtn("↑",      MoveStepUp));
        stepToolbar.Children.Add(StepBtn("↓",      MoveStepDown));

        // ── Step parameters ────────────────────────────────────────
        _typeBox = new ComboBox
        {
            ItemsSource  = Enum.GetValues<StepType>(),
            SelectedItem = StepType.Say,
            IsEnabled    = false, // shown for context; change via Remove + Add
        };

        _textBox = new TextBox { PlaceholderText = "Text…", AcceptsReturn = false };
        _textBox.TextChanged += (_, _) => SyncStepField(s => s.Text = _textBox.Text ?? string.Empty);

        _channelBox = new NumericUpDown { Minimum = 0, Maximum = 2_147_483_647, Increment = 1, Value = 0 };
        _channelBox.ValueChanged += (_, _) => SyncStepField(s => s.Channel = (int)(_channelBox.Value ?? 0));

        _chatVolumeBox = new ComboBox { ItemsSource = new[] { "Normal", "Whisper", "Shout" }, SelectedIndex = 0 };
        _chatVolumeBox.SelectionChanged += (_, _) =>
            SyncStepField(s => s.ChatVolume = _chatVolumeBox.SelectedItem as string ?? "Normal");

        _delayBox = new NumericUpDown { Minimum = 0, Maximum = 60_000, Increment = 100, Value = 1000 };
        _delayBox.ValueChanged += (_, _) => SyncStepField(s => s.DelayMs = (int)(_delayBox.Value ?? 0));

        _targetBox = new TextBox { PlaceholderText = "Avatar/Object UUID…" };
        _targetBox.TextChanged += (_, _) => SyncStepField(s => s.TargetId = _targetBox.Text ?? string.Empty);

        _assetBox = new TextBox { PlaceholderText = "Gesture asset UUID…" };
        _assetBox.TextChanged += (_, _) => SyncStepField(s => s.AssetId = _assetBox.Text ?? string.Empty);

        // Groups of controls per step type
        _sayPanel = LabeledRow("Text", _textBox,
            LabeledRow("Channel", _channelBox, LabeledRow("Volume", _chatVolumeBox)));
        _emotePanel   = LabeledRow("Text", _textBox);
        _waitPanel    = LabeledRow("Delay (ms)", _delayBox);
        _imPanel      = LabeledRow("Target UUID", _targetBox, LabeledRow("Text", _textBox));
        _sitPanel     = LabeledRow("Object UUID", _targetBox);
        _gesturePanel = LabeledRow("Asset UUID", _assetBox);
        _commandPanel = LabeledRow("Command", _textBox);

        var paramsHeader = new TextBlock
        {
            Text   = "Step parameters",
            Margin = new Thickness(0, 8, 0, 4),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        var paramBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(8),
            CornerRadius    = new CornerRadius(4),
        };
        var paramStack = new StackPanel { Spacing = 4 };
        paramStack.Children.Add(LabeledRow("Type", _typeBox));
        paramStack.Children.Add(_sayPanel);
        paramStack.Children.Add(_emotePanel);
        paramStack.Children.Add(_waitPanel);
        paramStack.Children.Add(_imPanel);
        paramStack.Children.Add(_sitPanel);
        paramStack.Children.Add(_gesturePanel);
        paramStack.Children.Add(_commandPanel);
        paramBorder.Child = paramStack;

        HideAllParamPanels();

        // ── OK / Cancel ────────────────────────────────────────────
        var okBtn     = new Button { Content = "OK",     IsDefault = true, MinWidth = 80 };
        var cancelBtn = new Button { Content = "Cancel", IsCancel  = true, MinWidth = 80 };
        okBtn.Click     += OnOk;
        cancelBtn.Click += (_, _) => Close();

        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing             = 6,
            Margin              = new Thickness(0, 8, 0, 0),
        };
        buttonRow.Children.Add(okBtn);
        buttonRow.Children.Add(cancelBtn);

        // ── Root layout ────────────────────────────────────────────
        var root = new StackPanel
        {
            Spacing = 4,
            Margin  = new Thickness(12),
        };
        root.Children.Add(LabeledRow("Name",    _nameBox, _enabledBox));
        root.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        root.Children.Add(new TextBlock { Text = "Steps", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        root.Children.Add(_stepList);
        root.Children.Add(stepToolbar);
        root.Children.Add(paramsHeader);
        root.Children.Add(paramBorder);
        root.Children.Add(buttonRow);

        var scroll = new ScrollViewer { Content = root };
        Content = scroll;
    }

    // ── Step management ────────────────────────────────────────────

    private void AddStep(StepType type)
    {
        var step = new MacroStep { Type = type };
        _steps.Add(step);
        _stepList.SelectedIndex = _steps.Count - 1;
    }

    private void RemoveStep()
    {
        if (SelectedStep is not { } s) return;
        var idx = _steps.IndexOf(s);
        _steps.Remove(s);
        if (idx < _steps.Count) _stepList.SelectedIndex = idx;
        else if (_steps.Count > 0) _stepList.SelectedIndex = _steps.Count - 1;
    }

    private void MoveStepUp()
    {
        if (SelectedStep is not { } s) return;
        var i = _steps.IndexOf(s);
        if (i <= 0) return;
        _steps.RemoveAt(i); _steps.Insert(i - 1, s);
        _stepList.SelectedIndex = i - 1;
    }

    private void MoveStepDown()
    {
        if (SelectedStep is not { } s) return;
        var i = _steps.IndexOf(s);
        if (i < 0 || i >= _steps.Count - 1) return;
        _steps.RemoveAt(i); _steps.Insert(i + 1, s);
        _stepList.SelectedIndex = i + 1;
    }

    // ── Parameter sync ─────────────────────────────────────────────

    private void OnStepSelected()
    {
        _suppressSync = true;
        try
        {
            HideAllParamPanels();
            if (SelectedStep is not { } s) return;

            _typeBox.SelectedItem = s.Type;
            _textBox.Text         = s.Text;
            _channelBox.Value     = s.Channel;
            _chatVolumeBox.SelectedItem = s.ChatVolume;
            _delayBox.Value       = s.DelayMs;
            _targetBox.Text       = s.TargetId;
            _assetBox.Text        = s.AssetId;

            ShowParamPanelFor(s.Type);
        }
        finally { _suppressSync = false; }
    }

    private void SyncStepField(Action<MacroStep> update)
    {
        if (_suppressSync || SelectedStep is not { } s) return;
        update(s);
        RefreshStepLabel(s);
    }

    private void RefreshStepLabel(MacroStep s)
    {
        var idx = _steps.IndexOf(s);
        if (idx < 0) return;
        _steps.RemoveAt(idx);
        _steps.Insert(idx, s);
        _stepList.SelectedIndex = idx;
    }

    // ── Param panel visibility ─────────────────────────────────────

    private void HideAllParamPanels()
    {
        _sayPanel.IsVisible     = false;
        _emotePanel.IsVisible   = false;
        _waitPanel.IsVisible    = false;
        _imPanel.IsVisible      = false;
        _sitPanel.IsVisible     = false;
        _gesturePanel.IsVisible = false;
        _commandPanel.IsVisible = false;
    }

    private void ShowParamPanelFor(StepType type)
    {
        switch (type)
        {
            case StepType.Say:         _sayPanel.IsVisible     = true; break;
            case StepType.Emote:       _emotePanel.IsVisible   = true; break;
            case StepType.Wait:        _waitPanel.IsVisible    = true; break;
            case StepType.IM:          _imPanel.IsVisible      = true; break;
            case StepType.Sit:         _sitPanel.IsVisible     = true; break;
            case StepType.PlayGesture: _gesturePanel.IsVisible = true; break;
            case StepType.Command:     _commandPanel.IsVisible = true; break;
            // Stand: no params — nothing to show
        }
    }

    // ── OK handler ─────────────────────────────────────────────────

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _macro.Name    = _nameBox.Text?.Trim() ?? string.Empty;
        _macro.Enabled = _enabledBox.IsChecked == true;
        _macro.Steps.Clear();
        _macro.Steps.AddRange(_steps);
        if (string.IsNullOrWhiteSpace(_macro.Name))
            _macro.Name = "Unnamed Macro";
        Committed = true;
        Close();
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static StackPanel LabeledRow(string label, params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2) };
        row.Children.Add(new TextBlock
        {
            Text = label + ":",
            VerticalAlignment = VerticalAlignment.Center,
            Width = 90,
            Opacity = 0.8,
        });
        foreach (var c in controls) row.Children.Add(c);
        return row;
    }

    private static FuncDataTemplate BuildStepTemplate() =>
        new(typeof(MacroStep), (obj, _) =>
        {
            if (obj is not MacroStep s) return new TextBlock { Text = "?" };
            return new TextBlock
            {
                Text     = s.Summary,
                Padding  = new Thickness(2),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
        });
}
