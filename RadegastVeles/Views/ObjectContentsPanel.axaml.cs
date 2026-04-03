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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class ObjectContentsPanel : UserControl
{
    public ObjectContentsPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var list = this.FindControl<ListBox>("ItemList");
        if (list != null)
        {
            list.DoubleTapped += ListBox_DoubleTapped;
            list.AddHandler(DragDrop.DropEvent,     OnDrop,     RoutingStrategies.Bubble);
            list.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        var list = this.FindControl<ListBox>("ItemList");
        if (list != null)
        {
            list.DoubleTapped -= ListBox_DoubleTapped;
            list.RemoveHandler(DragDrop.DropEvent,     OnDrop);
            list.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        }
        (DataContext as ObjectContentsViewModel)?.Dispose();
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        (DataContext as ObjectContentsViewModel)?.OpenItemCommand.Execute(null);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(InventoryDragData.Format)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var token = e.DataTransfer.TryGetValue(InventoryDragData.Format);
        if (token == null) return;
        var node = InventoryDragData.GetNode(token);
        if (node == null) return;
        (DataContext as ObjectContentsViewModel)?.DropInventoryItem(node);
        e.Handled = true;
    }
}
