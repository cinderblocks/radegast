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
using Avalonia.Media;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class AvatarProfilePanel : UserControl
{
    public AvatarProfilePanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AddHandler(DragDrop.DropEvent,     OnDrop,     RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        RemoveHandler(DragDrop.DropEvent,     OnDrop);
        RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(InventoryDragData.Format))
        {
            e.DragEffects = DragDropEffects.Copy;
            SetDropZoneHighlight(true);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        SetDropZoneHighlight(false);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropZoneHighlight(false);
        var token = e.DataTransfer.TryGetValue(InventoryDragData.Format);
        if (token == null) return;
        var node = InventoryDragData.GetNode(token);
        if (node == null) return;

        var vm = DataContext as AvatarProfileViewModel;
        vm?.GiveInventoryNode(node);
        e.Handled = true;
    }

    private void SetDropZoneHighlight(bool active)
    {
        var border = this.FindControl<Border>("DropZoneBorder");
        var label  = this.FindControl<TextBlock>("DropZoneLabel");
        if (border == null) return;

        if (active)
        {
            border.BorderBrush = Brushes.DodgerBlue;
            border.Background  = new SolidColorBrush(Color.FromArgb(30, 30, 144, 255));
            if (label != null) { label.Opacity = 1.0; label.Text = "📦 Release to give item"; }
        }
        else
        {
            border.BorderBrush = Brushes.Transparent;
            border.Background  = Brushes.Transparent;
            if (label != null) { label.Opacity = 0.5; label.Text = "📦 Drop inventory item here to give"; }
        }
    }
}
