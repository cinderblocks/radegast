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
using Avalonia.Interactivity;

namespace Radegast.Veles.Views;

public partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow()
    {
        InitializeComponent();

        // Without an explicit focus target, screen readers (e.g. JAWS) never notice this
        // window opened - keyboard/UIA focus stays on whatever had it in the owner window.
        Opened += (_, _) => CloseButton.Focus();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
