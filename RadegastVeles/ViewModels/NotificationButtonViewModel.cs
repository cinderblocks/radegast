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
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// A button displayed inside a <see cref="NotificationViewModel"/> card.
/// Wraps an action closure as an <see cref="ICommand"/> so that Avalonia
/// compiled-binding DataTemplates can bind directly without code-behind handlers.
/// </summary>
public sealed class NotificationButtonViewModel
{
    public string Label { get; }
    public ICommand Command { get; }

    public NotificationButtonViewModel(string label, Action execute)
    {
        Label = label;
        Command = new RelayCommand(execute);
    }
}
