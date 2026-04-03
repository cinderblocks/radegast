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

public partial class PayWindow : Window
{
    public PayWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("AmountBox");
        box?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Return && DataContext is PayViewModel vm && vm.CanPay)
        {
            vm.PayCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            (DataContext as PayViewModel)?.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        (DataContext as PayViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
