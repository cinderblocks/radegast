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

namespace Radegast.Veles.Views;

/// <summary>
/// A generic host window that displays a profile panel (avatar, group, etc.)
/// in its own standalone window. Disposes the DataContext on close if it
/// implements IDisposable.
/// </summary>
public class ProfileWindow : Window
{
    public ProfileWindow(string title, Control content)
    {
        Title = title;
        Width = 500;
        Height = 600;
        Content = content;

        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (Content is Control control && control.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
