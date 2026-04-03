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
using Avalonia.Threading;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class GroupsViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    public RadegastInstanceAvalonia Instance => _instance;

    public ObservableCollection<GroupEntry> Groups { get; } = [];

    [ObservableProperty]
    private GroupEntry? _selectedGroup;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _groupCountText = string.Empty;

    public GroupsViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        Client.Groups.CurrentGroups += Groups_CurrentGroups;
        Client.Groups.RequestCurrentGroups();

        UpdateDisplay();
    }

    public void Dispose()
    {
        Client.Groups.CurrentGroups -= Groups_CurrentGroups;
    }

    private void Groups_CurrentGroups(object? sender, CurrentGroupsEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateDisplay);
    }

    [RelayCommand]
    private void Refresh()
    {
        Client.Groups.RequestCurrentGroups();
    }

    private void UpdateDisplay()
    {
        UUID selectedId = SelectedGroup?.Id ?? UUID.Zero;

        Groups.Clear();

        // Add "(none)" entry for deactivating active group
        Groups.Add(new GroupEntry(UUID.Zero, "(none)", false));

        foreach (var g in _instance.Groups.Values.OrderBy(g => g.Name, StringComparer.Ordinal))
        {
            bool isActive = g.ID == Client.Self.ActiveGroup;
            Groups.Add(new GroupEntry(g.ID, g.Name, isActive));
        }

        // Reselect
        GroupEntry? reselect = null;
        foreach (var g in Groups)
        {
            if (g.Id == selectedId)
            {
                reselect = g;
                break;
            }
            if (selectedId == UUID.Zero && g.Id == Client.Self.ActiveGroup)
            {
                reselect = g;
            }
        }
        if (reselect != null)
            SelectedGroup = reselect;

        GroupCountText = $"{_instance.Groups.Count} groups";
        var max = Client.Network.MaxAgentGroups;
        if (max > 0)
            GroupCountText += $" (max {max})";
    }

    [RelayCommand]
    private void ActivateGroup()
    {
        if (SelectedGroup == null) return;
        Client.Groups.ActivateGroup(SelectedGroup.Id);
        StatusText = SelectedGroup.Id == UUID.Zero
            ? "Deactivated group"
            : $"Activated {SelectedGroup.Name}";
    }

    [RelayCommand]
    private void LeaveGroup()
    {
        if (SelectedGroup == null || SelectedGroup.Id == UUID.Zero) return;
        Client.Groups.LeaveGroup(SelectedGroup.Id);
        StatusText = $"Left {SelectedGroup.Name}";

        // Remove from local list immediately
        Groups.Remove(SelectedGroup);
        SelectedGroup = null;
    }

    [RelayCommand]
    private void GroupIM()
    {
        if (SelectedGroup == null || SelectedGroup.Id == UUID.Zero) return;
        var name = SelectedGroup.Name;
        _instance.RequestGroupIM(SelectedGroup.Id, name);
    }

    [RelayCommand]
    private void GroupInfo()
    {
        if (SelectedGroup == null || SelectedGroup.Id == UUID.Zero) return;
        _instance.ShowGroupProfile(SelectedGroup.Id);
    }
}

public record GroupEntry(UUID Id, string Name, bool IsActive)
{
    public string DisplayText => IsActive ? $"★ {Name}" : Name;
}
