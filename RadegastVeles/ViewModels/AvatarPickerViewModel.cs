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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class AvatarPickerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private UUID _searchQueryId = UUID.Zero;

    /// <summary>Raised when the user confirms a selection. The entry holds the chosen avatar's ID and name.</summary>
    public event EventHandler<AvatarPickerEntry>? Selected;
    /// <summary>Raised when the user dismisses the picker without making a selection.</summary>
    public event EventHandler? Cancelled;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntry))]
    [NotifyPropertyChangedFor(nameof(SelectedDisplay))]
    private AvatarPickerEntry? _selectedFriend;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntry))]
    [NotifyPropertyChangedFor(nameof(SelectedDisplay))]
    private AvatarPickerEntry? _selectedNearby;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntry))]
    [NotifyPropertyChangedFor(nameof(SelectedDisplay))]
    private AvatarPickerEntry? _selectedSearch;

    /// <summary>The currently selected entry across all three tabs, or null if nothing is selected.</summary>
    public AvatarPickerEntry? SelectedEntry => SelectedFriend ?? SelectedNearby ?? SelectedSearch;

    /// <summary>Human-readable label shown at the bottom of the picker when a selection exists.</summary>
    public string SelectedDisplay => SelectedEntry != null ? $"Selected: {SelectedEntry.Name}" : string.Empty;

    public ObservableCollection<AvatarPickerEntry> FriendResults { get; } = [];
    public ObservableCollection<AvatarPickerEntry> NearbyResults { get; } = [];
    public ObservableCollection<AvatarPickerEntry> SearchResults { get; } = [];

    public AvatarPickerViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        Client.Avatars.AvatarPickerReply += Avatars_AvatarPickerReply;
        PopulateFriends();
        PopulateNearby();
    }

    public void Dispose()
    {
        Client.Avatars.AvatarPickerReply -= Avatars_AvatarPickerReply;
    }

    private void PopulateFriends()
    {
        FriendResults.Clear();
        foreach (var fi in Client.Friends.FriendList.Values
            .OrderByDescending(f => f.IsOnline)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            string name = _instance.Names.Get(fi.UUID, fi.Name ?? fi.UUID.ToString());
            if (string.IsNullOrEmpty(name) || name == RadegastInstance.INCOMPLETE_NAME)
                name = fi.Name ?? fi.UUID.ToString();
            FriendResults.Add(new AvatarPickerEntry(fi.UUID, name, fi.IsOnline));
        }
    }

    private void PopulateNearby()
    {
        NearbyResults.Clear();
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        foreach (var av in sim.ObjectsAvatars.Values
            .Where(av => av != null && av.ID != UUID.Zero && av.ID != Client.Self.AgentID)
            .OrderBy(av => av.Name, StringComparer.OrdinalIgnoreCase))
        {
            string name = _instance.Names.Get(av.ID, av.Name ?? string.Empty);
            if (string.IsNullOrEmpty(name) || name == RadegastInstance.INCOMPLETE_NAME)
                name = av.Name ?? av.ID.ToString();
            NearbyResults.Add(new AvatarPickerEntry(av.ID, name, false));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
        IsSearching = true;
        SearchStatus = "Searching\u2026";
        SearchResults.Clear();
        _searchQueryId = UUID.Random();
        Client.Avatars.RequestAvatarNameSearch(SearchText.Trim(), _searchQueryId);
    }

    private bool CanSearch() => SearchText.Trim().Length >= 3;

    partial void OnSearchTextChanged(string value) => SearchCommand.NotifyCanExecuteChanged();

    private void Avatars_AvatarPickerReply(object? sender, AvatarPickerReplyEventArgs e)
    {
        if (e.QueryID != _searchQueryId) return;
        Dispatcher.UIThread.Post(() =>
        {
            IsSearching = false;
            SearchResults.Clear();
            foreach (var kvp in e.Avatars)
            {
                string name = _instance.Names.Get(kvp.Key, kvp.Value);
                SearchResults.Add(new AvatarPickerEntry(kvp.Key, name, false));
            }
            SearchStatus = SearchResults.Count == 0 ? "No results found." : string.Empty;
        });
    }

    partial void OnSelectedFriendChanged(AvatarPickerEntry? value)
    {
        if (value != null) { SelectedNearby = null; SelectedSearch = null; }
        SelectCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedNearbyChanged(AvatarPickerEntry? value)
    {
        if (value != null) { SelectedFriend = null; SelectedSearch = null; }
        SelectCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSearchChanged(AvatarPickerEntry? value)
    {
        if (value != null) { SelectedFriend = null; SelectedNearby = null; }
        SelectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Select()
    {
        var entry = SelectedEntry;
        if (entry == null) return;
        Selected?.Invoke(this, entry);
    }

    private bool HasSelection() => SelectedEntry != null;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}

/// <summary>An avatar entry returned by the avatar picker.</summary>
public record AvatarPickerEntry(UUID Id, string Name, bool IsOnline)
{
    /// <summary>Display label shown in lists: includes an online dot for friends who are online.</summary>
    public string DisplayName => IsOnline ? $"\u25cf {Name}" : Name;
}
