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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class LandHoldingsViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private UUID _agentQueryId;
    private readonly Dictionary<UUID, UUID> _groupQueryIds = new();
    private int _pendingCount;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _totalAreaDisplay = string.Empty;
    [ObservableProperty] private bool _hasPersonalParcels;
    [ObservableProperty] private bool _hasGroupParcels;
    [ObservableProperty] private bool _hasGroupContributions;

    public ObservableCollection<LandHoldingEntry> PersonalParcels { get; } = [];
    public ObservableCollection<LandHoldingEntry> GroupParcels { get; } = [];
    public ObservableCollection<GroupContributionEntry> GroupContributions { get; } = [];

    public LandHoldingsViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        Client.Directory.PlacesReply += Directory_PlacesReply;
        Refresh();
    }

    public void Dispose()
    {
        Client.Directory.PlacesReply -= Directory_PlacesReply;
    }

    [RelayCommand]
    private void Refresh()
    {
        IsLoading = true;
        StatusMessage = "Searching land holdings…";
        PersonalParcels.Clear();
        GroupParcels.Clear();
        GroupContributions.Clear();
        _groupQueryIds.Clear();

        var groups = _instance.Groups;

        if (groups.Count == 0)
            Client.Groups.RequestCurrentGroups();

        _pendingCount = 1 + groups.Count;
        _agentQueryId = Client.Directory.StartPlacesSearch();

        foreach (var kvp in groups)
        {
            var qid = Client.Directory.StartPlacesSearch(kvp.Key);
            _groupQueryIds[qid] = kvp.Key;
        }
    }

    private void Directory_PlacesReply(object? sender, PlacesReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            bool isPersonal = e.QueryID == _agentQueryId;
            bool isGroup = _groupQueryIds.ContainsKey(e.QueryID);

            if (!isPersonal && !isGroup) return;

            if (isPersonal)
            {
                _agentQueryId = UUID.Zero;
                foreach (var place in e.MatchedPlaces)
                {
                    PersonalParcels.Add(new LandHoldingEntry(
                        place.Name,
                        place.SimName,
                        (int)place.GlobalX % 256,
                        (int)place.GlobalY % 256,
                        place.ActualArea,
                        place.BillableArea,
                        place.SKU));
                }
            }
            else
            {
                var groupId = _groupQueryIds[e.QueryID];
                _groupQueryIds.Remove(e.QueryID);
                _instance.Groups.TryGetValue(groupId, out var group);
                var groupName = group.Name ?? string.Empty;

                foreach (var place in e.MatchedPlaces)
                {
                    GroupParcels.Add(new LandHoldingEntry(
                        place.Name,
                        place.SimName,
                        (int)place.GlobalX % 256,
                        (int)place.GlobalY % 256,
                        place.ActualArea,
                        place.BillableArea,
                        place.SKU,
                        groupName));
                }
            }

            _pendingCount--;
            if (_pendingCount <= 0)
            {
                IsLoading = false;
                UpdateTotals();
            }
        });
    }

    private void UpdateTotals()
    {
        HasPersonalParcels = PersonalParcels.Count > 0;
        HasGroupParcels = GroupParcels.Count > 0;

        GroupContributions.Clear();
        foreach (var group in _instance.Groups.Values)
        {
            if (group.Contribution > 0)
                GroupContributions.Add(new GroupContributionEntry(group.ID, group.Name, group.Contribution));
        }
        HasGroupContributions = GroupContributions.Count > 0;

        int totalArea = 0;
        foreach (var p in PersonalParcels) totalArea += p.BillableArea;
        foreach (var p in GroupParcels) totalArea += p.BillableArea;
        TotalAreaDisplay = $"{totalArea:N0} sqm total";

        int totalParcels = PersonalParcels.Count + GroupParcels.Count;
        StatusMessage = totalParcels == 0
            ? "No land found in your holdings."
            : $"{totalParcels} parcel(s) found.";
    }
}

public record LandHoldingEntry(
    string Name,
    string SimName,
    int LocalX,
    int LocalY,
    int ActualArea,
    int BillableArea,
    string Sku,
    string GroupName = "")
{
    public string LocationDisplay => $"{SimName} ({LocalX}, {LocalY})";
    public string AreaDisplay => BillableArea == ActualArea
        ? $"{BillableArea:N0} sqm"
        : $"{BillableArea:N0} / {ActualArea:N0} sqm (billable/actual)";
    public string LandTypeDisplay => Sku;
    public bool IsGroupLand => !string.IsNullOrEmpty(GroupName);
}

public record GroupContributionEntry(UUID GroupId, string GroupName, int Contribution)
{
    public string ContributionDisplay => $"{Contribution:N0} sqm";
}
