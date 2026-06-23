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
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Veles.Plugin.Automation.UI;

/// <summary>
/// Preference-tab panel for managing automation rules.
/// Built entirely in code to avoid AXAML compilation dependencies on the host application.
/// </summary>
internal sealed class AutomationRulesPanel : UserControl
{
    private readonly AutomationRulesPanelViewModel _vm;

    public AutomationRulesPanel(RuleEngine engine, List<AutomationRule> rules,
        Action onSave, Action onImport, Action onExport)
    {
        _vm = new AutomationRulesPanelViewModel(engine, rules, onSave, onImport, onExport);

        // ── Toolbar ────────────────────────────────────────────────
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin  = new Avalonia.Thickness(0, 0, 0, 4),
        };

        Button Btn(string label, System.Windows.Input.ICommand cmd)
        {
            var b = new Button { Content = label, Command = cmd };
            return b;
        }

        toolbar.Children.Add(Btn("Add",            _vm.AddRuleCommand));
        toolbar.Children.Add(Btn("Edit",           _vm.EditRuleCommand));
        toolbar.Children.Add(Btn("Remove",         _vm.RemoveRuleCommand));
        toolbar.Children.Add(Btn("Enable/Disable", _vm.ToggleRuleCommand));
        toolbar.Children.Add(new Separator { Width = 1, Margin = new Avalonia.Thickness(4, 0) });
        toolbar.Children.Add(Btn("↑",              _vm.MoveUpCommand));
        toolbar.Children.Add(Btn("↓",              _vm.MoveDownCommand));
        toolbar.Children.Add(new Separator { Width = 1, Margin = new Avalonia.Thickness(4, 0) });
        toolbar.Children.Add(Btn("Import…",        _vm.ImportRulesCommand));
        toolbar.Children.Add(Btn("Export…",        _vm.ExportRulesCommand));

        // ── Rules list ─────────────────────────────────────────────
        var listBox = new ListBox
        {
            ItemsSource = _vm.Rules,
            ItemTemplate = BuildItemTemplate(),
        };
        listBox.SelectionChanged += (_, _) =>
            _vm.SelectedRule = listBox.SelectedItem as AutomationRuleViewModel;

        // ── Layout ─────────────────────────────────────────────────
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(listBox);

        Content = root;
        Margin  = new Avalonia.Thickness(8);
    }

    private static FuncDataTemplate BuildItemTemplate()
    {
        return new FuncDataTemplate(typeof(AutomationRuleViewModel), (obj, _) =>
        {
            if (obj is not AutomationRuleViewModel vm) return new TextBlock { Text = "?" };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("16,*,200,140"),
                Margin = new Avalonia.Thickness(2, 1),
            };

            var indicator = new TextBlock
            {
                Text = vm.Enabled ? "●" : "○",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AutomationRuleViewModel.Enabled))
                    indicator.Text = vm.Enabled ? "●" : "○";
            };

            var nameText = new TextBlock
            {
                Text = vm.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Avalonia.Thickness(4, 0),
            };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AutomationRuleViewModel.Name))
                    nameText.Text = vm.Name;
            };

            var triggerText = new TextBlock
            {
                Text = vm.TriggerSummary,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AutomationRuleViewModel.TriggerSummary))
                    triggerText.Text = vm.TriggerSummary;
            };

            var actionText = new TextBlock
            {
                Text = vm.ActionSummary,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AutomationRuleViewModel.ActionSummary))
                    actionText.Text = vm.ActionSummary;
            };

            Grid.SetColumn(indicator,   0);
            Grid.SetColumn(nameText,    1);
            Grid.SetColumn(triggerText, 2);
            Grid.SetColumn(actionText,  3);

            grid.Children.Add(indicator);
            grid.Children.Add(nameText);
            grid.Children.Add(triggerText);
            grid.Children.Add(actionText);

            return grid;
        });
    }
}

