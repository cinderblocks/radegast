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
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Manages the ordered queue of in-world notification overlays. Exposes the
/// currently visible notification as <see cref="Current"/> and provides
/// navigation commands to cycle through pending items without dismissing them.
/// </summary>
public partial class NotificationQueueViewModel : ObservableObject, IChatContext
{
    /// <inheritdoc />
    public RadegastInstanceAvalonia Instance { get; }

    public NotificationQueueViewModel(RadegastInstanceAvalonia instance)
    {
        Instance = instance;
    }

    private readonly ObservableCollection<NotificationViewModel> _items = new();

    [ObservableProperty]
    private NotificationViewModel? _current;

    /// <summary>True when at least one notification is pending — drives the overlay's IsVisible.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>True when more than one notification is queued, showing the navigation strip.</summary>
    [ObservableProperty]
    private bool _hasMultiple;

    /// <summary>Human-readable "N of M" counter shown in the navigation strip.</summary>
    [ObservableProperty]
    private string _positionText = string.Empty;

    /// <summary>Enqueue a new notification. If the queue was empty the new item becomes current.</summary>
    public void Add(NotificationViewModel vm)
    {
        vm.Dismissed += OnItemDismissed;
        _items.Add(vm);
        if (Current == null)
            Current = vm;
        Refresh();
    }

    private void OnItemDismissed(object? sender, EventArgs e)
    {
        if (sender is NotificationViewModel vm)
            Remove(vm);
    }

    private void Remove(NotificationViewModel vm)
    {
        vm.Dismissed -= OnItemDismissed;
        int idx = _items.IndexOf(vm);
        _items.Remove(vm);
        if (Current == vm)
            Current = _items.Count > 0 ? _items[Math.Min(idx, _items.Count - 1)] : null;
        Refresh();
    }

    [RelayCommand]
    private void Previous()
    {
        if (Current == null || _items.Count < 2) return;
        int idx = _items.IndexOf(Current);
        Current = _items[(idx - 1 + _items.Count) % _items.Count];
        Refresh();
    }

    [RelayCommand]
    private void Next()
    {
        if (Current == null || _items.Count < 2) return;
        int idx = _items.IndexOf(Current);
        Current = _items[(idx + 1) % _items.Count];
        Refresh();
    }

    private void Refresh()
    {
        IsVisible = _items.Count > 0;
        HasMultiple = _items.Count > 1;
        PositionText = HasMultiple && Current != null
            ? $"{_items.IndexOf(Current) + 1} of {_items.Count}"
            : string.Empty;
    }
}
