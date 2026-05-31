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
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public record GroupPickerEntry(UUID GroupId, string GroupName);

public partial class GroupPickerViewModel : ObservableObject
{
    private readonly RadegastInstanceAvalonia _instance;

    /// <summary>Raised when the user confirms a group selection.</summary>
    public event EventHandler<GroupPickerEntry>? Selected;
    /// <summary>Raised when the user cancels without selecting.</summary>
    public event EventHandler? Cancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDisplay))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private GroupPickerEntry? _selectedGroup;

    public string SelectedDisplay => SelectedGroup != null ? $"Selected: {SelectedGroup.GroupName}" : string.Empty;

    public ObservableCollection<GroupPickerEntry> Groups { get; } = [];

    public GroupPickerViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        Populate();
    }

    private void Populate()
    {
        Groups.Clear();
        foreach (var g in _instance.Groups.Values
                     .Where(g => g.ID != UUID.Zero)
                     .OrderBy(g => g.Name, StringComparer.Ordinal))
        {
            Groups.Add(new GroupPickerEntry(g.ID, g.Name));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select()
    {
        if (SelectedGroup != null)
            Selected?.Invoke(this, SelectedGroup);
    }

    private bool CanSelect() => SelectedGroup != null;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
