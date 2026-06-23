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

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Radegast.Veles.PluginApi;

namespace Radegast.Veles.Plugins;

/// <summary>
/// A standalone settings window for a single plugin, populated with
/// all <see cref="PluginPreferenceTab"/>s the plugin registered.
/// Shown via the plugin's sub-menu entry in the Plugins menu.
/// </summary>
public partial class PluginSettingsWindow : Window
{
    private readonly IReadOnlyList<PluginPreferenceTab> _tabs;

    public PluginSettingsWindow()
    {
        InitializeComponent();
        _tabs = [];
    }

    public PluginSettingsWindow(string pluginName, IReadOnlyList<PluginPreferenceTab> tabs) : this()
    {
        Title = $"{pluginName} Settings";
        _tabs = tabs;

        OkButton.Click     += OnOkClick;
        ApplyButton.Click  += OnApplyClick;
        CancelButton.Click += OnCancelClick;

        foreach (var tab in tabs)
        {
            TabControl.Items.Add(new TabItem
            {
                Header  = tab.Header,
                Content = tab.ContentFactory(),
            });
        }

        // Hide tab strip when there is only one tab — cleaner single-page layout
        if (tabs.Count <= 1)
            TabControl.TabStripPlacement = Dock.Top;
    }

    private void Apply()
    {
        foreach (var tab in _tabs)
            tab.OnApply?.Invoke();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)  => Apply();
    private void OnOkClick(object? sender, RoutedEventArgs e)     { Apply(); Close(); }
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
