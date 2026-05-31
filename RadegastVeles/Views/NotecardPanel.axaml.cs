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
using Avalonia.Input;
using Avalonia.Interactivity;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class NotecardPanel : UserControl
{
    private NotecardViewModel? _vm;

    public event EventHandler? DetachRequested;
    public event EventHandler? CloseRequested;

    public NotecardPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as NotecardViewModel;
        if (_vm == null) return;

        var standalone = VisualRoot is ProfileWindow;

        var btnDetach = this.FindControl<Button>("BtnDetach");
        if (btnDetach != null)
        {
            if (standalone)
                btnDetach.IsVisible = false;
            else
                btnDetach.Click += (_, _) => DetachRequested?.Invoke(this, EventArgs.Empty);
        }

        var btnClose = this.FindControl<Button>("BtnClose");
        if (btnClose != null)
        {
            if (standalone)
                btnClose.IsVisible = false;
            else
                btnClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        AddHandler(DragDrop.DropEvent,     OnDrop,     RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        RemoveHandler(DragDrop.DropEvent,     OnDrop);
        RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        (_vm as IDisposable)?.Dispose();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(InventoryDragData.Format))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var token = e.DataTransfer.TryGetValue(InventoryDragData.Format);
        if (token == null) return;
        var node = InventoryDragData.GetNode(token);
        if (node == null) return;

        _vm?.HandleDroppedNode(node);
        e.Handled = true;
    }
}
