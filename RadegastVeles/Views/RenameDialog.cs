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
using Avalonia.Layout;

namespace Radegast.Veles.Views;

/// <summary>
/// A minimal dialog for renaming an inventory item or folder.
/// </summary>
public sealed class RenameDialog : Window
{
    /// <summary>The new name entered by the user, or null if cancelled.</summary>
    public string? Result { get; private set; }

    public RenameDialog(string currentName)
    {
        Title = "Rename";
        Width = 360;
        Height = 130;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tb = new TextBox
        {
            Text = currentName,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Avalonia.Automation.AutomationProperties.SetName(tb, "New name");

        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0)
        };

        ok.Click     += (_, _) => Commit(tb.Text);
        cancel.Click += (_, _) => Close();

        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)  Commit(tb.Text);
            else if (e.Key == Key.Escape) Close();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 0,
            Children =
            {
                new TextBlock
                {
                    Text = "Enter new name:",
                    Margin = new Thickness(0, 0, 0, 4)
                },
                tb,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0),
                    Spacing = 0,
                    Children = { ok, cancel }
                }
            }
        };

        Opened += (_, _) =>
        {
            tb.Focus();
            tb.SelectAll();
        };
    }

    private void Commit(string? text)
    {
        var trimmed = text?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            Result = trimmed;
        Close();
    }
}
