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
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Radegast.Veles.Views;

public partial class PanelHostWindow : Window
{
    private bool _isDocking;

    public event EventHandler? DockRequested;

    public PanelHostWindow()
    {
        InitializeComponent();
    }

    public void SetPanel(Control panel)
    {
        PanelContainer.Content = panel;
    }

    public Control? RemovePanel()
    {
        var panel = PanelContainer.Content as Control;
        PanelContainer.Content = null;
        return panel;
    }

    private void OnDockClick(object? sender, RoutedEventArgs e)
    {
        _isDocking = true;
        DockRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isDocking)
        {
            DockRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnClosing(e);
    }
}
