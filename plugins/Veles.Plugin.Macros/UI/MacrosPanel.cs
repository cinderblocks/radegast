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
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Veles.Plugin.Macros.UI;

/// <summary>
/// Preference-tab panel listing all macros with toolbar actions.
/// Built entirely in code to avoid AXAML compilation dependencies on the host application.
/// </summary>
internal sealed class MacrosPanel : UserControl
{
    private readonly List<Macro> _macros;
    private readonly ObservableCollection<Macro> _display;
    private readonly MacroRunner _runner;
    private readonly Action _save;
    private readonly Action _import;
    private readonly Action _export;
    private readonly Func<Macro, MacroEditorWindow> _editorFactory;

    private readonly ListBox _listBox;
    private Macro? Selected => _listBox.SelectedItem as Macro;

    public MacrosPanel(
        List<Macro> macros,
        MacroRunner runner,
        Action save,
        Action import,
        Action export,
        Func<Macro, MacroEditorWindow> editorFactory)
    {
        _macros        = macros;
        _display       = new ObservableCollection<Macro>(macros);
        _runner        = runner;
        _save          = save;
        _import        = import;
        _export        = export;
        _editorFactory = editorFactory;

        // ── Toolbar ────────────────────────────────────────────────
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 4,
            Margin      = new Thickness(0, 0, 0, 4),
        };

        Button Btn(string label, Action onClick)
        {
            var b = new Button { Content = label };
            b.Click += (_, _) => onClick();
            return b;
        }

        toolbar.Children.Add(Btn("Add",            AddMacro));
        toolbar.Children.Add(Btn("Edit",           EditSelected));
        toolbar.Children.Add(Btn("Remove",         RemoveSelected));
        toolbar.Children.Add(Btn("Enable/Disable", ToggleSelected));
        toolbar.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 0) });
        toolbar.Children.Add(Btn("↑",              MoveUp));
        toolbar.Children.Add(Btn("↓",              MoveDown));
        toolbar.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 0) });
        toolbar.Children.Add(Btn("▶ Run",          RunSelected));
        toolbar.Children.Add(Btn("■ Stop",         _runner.Stop));
        toolbar.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 0) });
        toolbar.Children.Add(Btn("Import…",        _import));
        toolbar.Children.Add(Btn("Export…",        _export));

        // ── List ──────────────────────────────────────────────────
        _listBox = new ListBox
        {
            ItemsSource  = _display,
            ItemTemplate = BuildTemplate(),
        };

        // ── Layout ────────────────────────────────────────────────
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_listBox);

        Content = root;
        Margin  = new Thickness(8);
    }

    // ── Actions ───────────────────────────────────────────────────

    private void AddMacro()
    {
        var macro = new Macro();
        OpenEditor(macro, isNew: true);
    }

    private void EditSelected()
    {
        if (Selected == null) return;
        OpenEditor(Selected, isNew: false);
    }

    private void OpenEditor(Macro macro, bool isNew)
    {
        var win = _editorFactory(macro);
        win.Closed += (_, _) =>
        {
            if (!win.Committed) return;
            if (isNew)
            {
                _macros.Add(macro);
                _display.Add(macro);
            }
            else
            {
                // Force list refresh for the edited item
                var idx = _display.IndexOf(macro);
                if (idx >= 0)
                {
                    _display.RemoveAt(idx);
                    _display.Insert(idx, macro);
                    _listBox.SelectedIndex = idx;
                }
            }
            _save();
        };
        win.Show();
    }

    private void RemoveSelected()
    {
        if (Selected is not { } m) return;
        _macros.Remove(m);
        _display.Remove(m);
        _save();
    }

    private void ToggleSelected()
    {
        if (Selected is not { } m) return;
        m.Enabled = !m.Enabled;
        // Refresh display item
        var idx = _display.IndexOf(m);
        if (idx >= 0) { _display.RemoveAt(idx); _display.Insert(idx, m); _listBox.SelectedIndex = idx; }
        _save();
    }

    private void MoveUp()
    {
        if (Selected is not { } m) return;
        var i = _macros.IndexOf(m);
        if (i <= 0) return;
        _macros.RemoveAt(i); _macros.Insert(i - 1, m);
        _display.RemoveAt(i); _display.Insert(i - 1, m);
        _listBox.SelectedIndex = i - 1;
        _save();
    }

    private void MoveDown()
    {
        if (Selected is not { } m) return;
        var i = _macros.IndexOf(m);
        if (i < 0 || i >= _macros.Count - 1) return;
        _macros.RemoveAt(i); _macros.Insert(i + 1, m);
        _display.RemoveAt(i); _display.Insert(i + 1, m);
        _listBox.SelectedIndex = i + 1;
        _save();
    }

    private void RunSelected()
    {
        if (Selected is not { } m || !m.Enabled || m.Steps.Count == 0) return;
        // MacroPlugin holds the context; we fire through the runner directly.
        MacroRunRequested?.Invoke(m);
    }

    /// <summary>Raised when the user clicks Run for a macro. MacroPlugin subscribes to this.</summary>
    public event Action<Macro>? MacroRunRequested;

    // ── Item template ─────────────────────────────────────────────

    private static FuncDataTemplate BuildTemplate() =>
        new(typeof(Macro), (obj, _) =>
        {
            if (obj is not Macro m) return new TextBlock { Text = "?" };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("16,*,80"),
                Margin = new Thickness(2, 1),
            };

            var dot = new TextBlock
            {
                Text = m.Enabled ? "●" : "○",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            var name = new TextBlock
            {
                Text = m.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0),
            };
            var steps = new TextBlock
            {
                Text = $"{m.Steps.Count} step{(m.Steps.Count != 1 ? "s" : "")}",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Opacity = 0.65,
            };

            Grid.SetColumn(dot,   0);
            Grid.SetColumn(name,  1);
            Grid.SetColumn(steps, 2);
            grid.Children.Add(dot);
            grid.Children.Add(name);
            grid.Children.Add(steps);
            return grid;
        });
}
