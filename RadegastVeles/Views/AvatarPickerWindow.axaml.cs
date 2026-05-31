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

public partial class AvatarPickerWindow : Window
{
    public AvatarPickerWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var friendsList = this.FindControl<ListBox>("FriendsList");
        var nearbyList = this.FindControl<ListBox>("NearbyList");
        var searchList = this.FindControl<ListBox>("SearchList");

        if (friendsList != null)
            friendsList.DoubleTapped += OnListDoubleTapped;
        if (nearbyList != null)
            nearbyList.DoubleTapped += OnListDoubleTapped;
        if (searchList != null)
            searchList.DoubleTapped += OnListDoubleTapped;

        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox != null)
            searchBox.KeyDown += OnSearchBoxKeyDown;
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AvatarPickerViewModel vm && vm.SelectCommand.CanExecute(null))
        {
            vm.SelectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && DataContext is AvatarPickerViewModel vm && vm.SearchCommand.CanExecute(null))
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) { return; }

        if (e.Key == Key.Return && DataContext is AvatarPickerViewModel vm && vm.SelectCommand.CanExecute(null))
        {
            vm.SelectCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            (DataContext as AvatarPickerViewModel)?.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        (DataContext as AvatarPickerViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
