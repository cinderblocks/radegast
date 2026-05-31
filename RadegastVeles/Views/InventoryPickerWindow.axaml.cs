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

public partial class InventoryPickerWindow : Window
{
    public InventoryPickerWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("ItemList");
        if (list != null)
            list.DoubleTapped += OnListDoubleTapped;

        this.FindControl<TextBox>("FilterBox")?.Focus();
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is InventoryPickerViewModel vm && vm.SelectCommand.CanExecute(null))
        {
            vm.SelectCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        if (e.Key == Key.Return && DataContext is InventoryPickerViewModel vm && vm.SelectCommand.CanExecute(null))
        {
            vm.SelectCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DataContext is InventoryPickerViewModel vm2)
        {
            vm2.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
