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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Radegast.Veles.Controls;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class GroupsPanel : UserControl
{
    public GroupsPanel()
    {
        InitializeComponent();
    }

    private void GroupListBox_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not GroupsViewModel vm) return;
        if (sender is not ListBox lb) return;

        // Try to find the hovered ListBoxItem to get its group entry
        GroupEntry? entry = null;
        if (e.Source is Visual v)
        {
            var item = (v as ListBoxItem) ?? v.FindAncestorOfType<ListBoxItem>();
            entry = item?.DataContext as GroupEntry;
        }
        entry ??= vm.SelectedGroup;

        if (entry == null || entry.Id == LibreMetaverse.UUID.Zero) return;

        var menu = GroupMenuBuilder.Build(vm.Instance, entry.Id, entry.Name);
        menu.Open(lb);
        e.Handled = true;
    }
}
