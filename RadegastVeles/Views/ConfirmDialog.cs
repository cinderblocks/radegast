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
using Avalonia.Layout;
using Avalonia.Media;

namespace Radegast.Veles.Views;

/// <summary>
/// Asks the user to confirm an action that cannot be undone. Cancel is the default button,
/// so Enter and Escape both leave things as they are.
/// </summary>
public sealed class ConfirmDialog : Window
{
    /// <summary>True only if the user pressed the confirm button.</summary>
    public bool Confirmed { get; private set; }

    public ConfirmDialog(string title, string message, string confirmText)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var confirm = new Button
        {
            Content = confirmText,
            MinWidth = 80
        };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            IsDefault = true,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0)
        };

        confirm.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 16, 0, 0),
                    Children = { confirm, cancel }
                }
            }
        };

        Opened += (_, _) => cancel.Focus();
    }
}
