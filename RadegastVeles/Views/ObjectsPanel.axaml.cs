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
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class ObjectsPanel : UserControl
{
    private ObjectsViewModel? _vm;

    public ObjectsPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as ObjectsViewModel;
        if (_vm == null) return;

        var searchBox = this.FindControl<TextBox>("TxtObjectSearch");
        if (searchBox != null)
        {
            searchBox.KeyDown += SearchBox_KeyDown;
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm != null)
        {
            _vm.RefreshCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ObjectsList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        if (sender is not ListBox lb) return;

        // Ensure the item under the pointer is selected before the context menu fires.
        var source = e.Source as Control;
        while (source is not null)
        {
            if (source is ListBoxItem item)
            {
                lb.SelectedItem = item.DataContext;
                return;
            }
            source = source.Parent as Control;
        }
    }
}
