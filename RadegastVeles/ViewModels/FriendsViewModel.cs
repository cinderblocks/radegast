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
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class FriendsViewModel : InstanceViewModelBase, IDisposable
{
    private DispatcherTimer? _refreshTimer;
    private bool _refreshPending;

    public ObservableCollection<FriendEntry> Friends { get; } = [];

    [ObservableProperty]
    private FriendEntry? _selectedFriend;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canSeeMeOnline;

    [ObservableProperty]
    private bool _canSeeMeOnMap;

    [ObservableProperty]
    private bool _canModifyMyObjects;

    private bool _settingFriend;

    public FriendsViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            if (_refreshPending)
            {
                _refreshPending = false;
                RefreshFriendsList();
            }
        };

        Client.Friends.FriendOnline += Friends_FriendOnline;
        Client.Friends.FriendOffline += Friends_FriendOffline;
        Client.Friends.FriendshipTerminated += Friends_FriendshipTerminated;
        Client.Friends.FriendshipResponse += Friends_FriendshipResponse;
        Client.Friends.FriendNames += Friends_FriendNames;
        Client.Friends.FriendRightsUpdate += Friends_FriendRightsUpdate;
        _instance.Names.NameUpdated += Names_NameUpdated;

        RefreshFriendsList();
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;

        Client.Friends.FriendOnline -= Friends_FriendOnline;
        Client.Friends.FriendOffline -= Friends_FriendOffline;
        Client.Friends.FriendshipTerminated -= Friends_FriendshipTerminated;
        Client.Friends.FriendshipResponse -= Friends_FriendshipResponse;
        Client.Friends.FriendNames -= Friends_FriendNames;
        Client.Friends.FriendRightsUpdate -= Friends_FriendRightsUpdate;
        _instance.Names.NameUpdated -= Names_NameUpdated;
    }

    private void QueueRefresh()
    {
        _refreshPending = true;
        if (_refreshTimer is { IsEnabled: false })
            _refreshTimer.Start();
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshFriendsList();
    }

    private void RefreshFriendsList()
    {
        var friends = Client.Friends.FriendList.Values
            .Select(fi =>
            {
                // Use NameManager to resolve names from cache or queue a request.
                // This handles the name.cache and fires NameUpdated when resolved.
                string name = _instance.Names.Get(fi.UUID, fi.Name);
                if (string.IsNullOrEmpty(name) || name == RadegastInstance.INCOMPLETE_NAME)
                    name = fi.Name ?? RadegastInstance.INCOMPLETE_NAME;
                return new FriendEntry(fi.UUID, name, fi.IsOnline,
                    fi.CanSeeMeOnline, fi.CanSeeMeOnMap, fi.CanModifyMyObjects);
            })
            .OrderByDescending(f => f.IsOnline)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        UUID selectedId = SelectedFriend?.Id ?? UUID.Zero;

        Friends.Clear();
        FriendEntry? reselect = null;
        foreach (var f in friends)
        {
            Friends.Add(f);
            if (f.Id == selectedId)
                reselect = f;
        }

        if (reselect != null)
            SelectedFriend = reselect;

        StatusText = $"{Friends.Count} friends";
    }

    partial void OnSelectedFriendChanged(FriendEntry? value)
    {
        if (value == null) return;

        _settingFriend = true;
        CanSeeMeOnline = value.CanSeeMeOnline;
        CanSeeMeOnMap = value.CanSeeMeOnMap;
        CanModifyMyObjects = value.CanModifyMyObjects;
        _settingFriend = false;
    }

    partial void OnCanSeeMeOnlineChanged(bool value)
    {
        if (_settingFriend || SelectedFriend == null) return;
        UpdateFriendRights();
    }

    partial void OnCanSeeMeOnMapChanged(bool value)
    {
        if (_settingFriend || SelectedFriend == null) return;
        UpdateFriendRights();
    }

    partial void OnCanModifyMyObjectsChanged(bool value)
    {
        if (_settingFriend || SelectedFriend == null) return;
        UpdateFriendRights();
    }

    private void UpdateFriendRights()
    {
        if (SelectedFriend == null) return;

        FriendRights rights = FriendRights.None;
        if (CanSeeMeOnline) rights |= FriendRights.CanSeeOnline;
        if (CanSeeMeOnMap) rights |= FriendRights.CanSeeOnMap;
        if (CanModifyMyObjects) rights |= FriendRights.CanModifyObjects;

        Client.Friends.GrantRights(SelectedFriend.Id, rights);
    }

    [RelayCommand]
    private void ShowProfile()
    {
        if (SelectedFriend == null) return;
        _instance.ShowAgentProfile(SelectedFriend.Name, SelectedFriend.Id);
    }

    [RelayCommand]
    private void IM()
    {
        if (SelectedFriend == null) return;
        _instance.RequestIM(SelectedFriend.Id, SelectedFriend.Name);
    }

    [RelayCommand]
    private void Pay()
    {
        if (SelectedFriend == null) return;
        _instance.OpenPayWindow(SelectedFriend.Id, SelectedFriend.Name);
    }

    [RelayCommand]
    private void RemoveFriend()
    {
        if (SelectedFriend == null) return;
        Client.Friends.TerminateFriendship(SelectedFriend.Id);
        StatusText = $"Removed {SelectedFriend.Name} from friends";
        QueueRefresh();
    }

    [RelayCommand]
    private void OfferTeleport()
    {
        if (SelectedFriend == null) return;
        Client.Self.SendTeleportLure(SelectedFriend.Id);
        StatusText = $"Teleport offer sent to {SelectedFriend.Name}";
    }

    [RelayCommand]
    private void TeleportTo()
    {
        if (SelectedFriend == null) return;
        var friend = Client.Friends.FriendList.GetValueOrDefault(SelectedFriend.Id);
        if (friend == null || !friend.CanSeeThemOnMap)
        {
            StatusText = $"{SelectedFriend.Name} has not granted you map rights";
            return;
        }

        var friendId = SelectedFriend.Id;
        var friendName = SelectedFriend.Name;

        void OnFriendFound(object? sender, FriendFoundReplyEventArgs e)
        {
            if (e.AgentID != friendId) return;
            Client.Friends.FriendFoundReply -= OnFriendFound;

            if (e.RegionHandle == 0)
            {
                Dispatcher.UIThread.Post(() => StatusText = $"Could not locate {friendName}");
                return;
            }

            Client.Self.Teleport(e.RegionHandle, e.Location);
        }

        Client.Friends.FriendFoundReply += OnFriendFound;
        Client.Friends.MapFriend(friendId);
        StatusText = $"Locating {friendName}\u2026";
    }

    [RelayCommand]
    private void InviteToGroup()
    {
        if (SelectedFriend == null) return;
        var friendId = SelectedFriend.Id;
        var friendName = SelectedFriend.Name;
        _instance.ShowGroupPicker($"Invite {friendName} to Group", entry =>
        {
            Client.Groups.Invite(entry.GroupId, new System.Collections.Generic.List<UUID> { UUID.Zero }, friendId);
            _instance.ShowNotificationInChat($"Invited {friendName} to {entry.GroupName}.");
        });
    }

    #region Network Events

    private void Friends_FriendOnline(object? sender, FriendInfoEventArgs e)
    {
        var name = _instance.Names.Get(e.Friend.UUID, e.Friend.Name);
        var msg = $"{name} is online";
        Dispatcher.UIThread.Post(() =>
        {
            UpdateFriendOnlineStatus(e.Friend.UUID, name, true);
            _instance.ShowNotificationInChat(msg);
            VelesNotificationService.Show("Friend Online", msg,
                Avalonia.Controls.Notifications.NotificationType.Information,
                TimeSpan.FromSeconds(6));
        });
    }

    private void Friends_FriendOffline(object? sender, FriendInfoEventArgs e)
    {
        var name = _instance.Names.Get(e.Friend.UUID, e.Friend.Name);
        var msg = $"{name} is offline";
        Dispatcher.UIThread.Post(() =>
        {
            UpdateFriendOnlineStatus(e.Friend.UUID, name, false);
            _instance.ShowNotificationInChat(msg);
            VelesNotificationService.Show("Friend Offline", msg,
                Avalonia.Controls.Notifications.NotificationType.Information,
                TimeSpan.FromSeconds(6));
        });
    }

    /// <summary>
    /// Updates a single friend's online status in-place, maintaining sort order
    /// (online friends first, then alphabetical), without rebuilding the full list.
    /// </summary>
    private void UpdateFriendOnlineStatus(UUID friendId, string name, bool isOnline)
    {
        var info = Client.Friends.FriendList.GetValueOrDefault(friendId);

        var existing = Friends.FirstOrDefault(f => f.Id == friendId);
        if (existing != null)
            Friends.Remove(existing);

        var entry = info != null
            ? new FriendEntry(friendId, name, isOnline,
                info.CanSeeMeOnline, info.CanSeeMeOnMap, info.CanModifyMyObjects)
            : new FriendEntry(friendId, name, isOnline, false, false, false);

        // Find the correct sorted position: online first, then alphabetical
        int insertAt = 0;
        for (int i = 0; i < Friends.Count; i++)
        {
            var f = Friends[i];
            if (isOnline && !f.IsOnline) break;          // insert before first offline
            if (!isOnline && f.IsOnline) { insertAt = i + 1; continue; } // skip online
            if (string.Compare(f.Name, name, StringComparison.Ordinal) > 0) break;
            insertAt = i + 1;
        }
        Friends.Insert(insertAt, entry);

        if (SelectedFriend?.Id == friendId)
            SelectedFriend = entry;
    }

    private void Friends_FriendRightsUpdate(object? sender, FriendInfoEventArgs e)
    {
        var name = _instance.Names.Get(e.Friend.UUID, e.Friend.Name);
        var rights = new List<string>();
        if (e.Friend.CanSeeMeOnline)  rights.Add("see you online");
        if (e.Friend.CanSeeMeOnMap)   rights.Add("see you on map");
        if (e.Friend.CanModifyMyObjects) rights.Add("modify your objects");

        var msg = rights.Count > 0
            ? $"{name} can now: {string.Join(", ", rights)}"
            : $"{name} has removed all special permissions";

        Dispatcher.UIThread.Post(() =>
        {
            _instance.ShowNotificationInChat(msg);
            VelesNotificationService.Show("Friend Permissions Changed", msg,
                Avalonia.Controls.Notifications.NotificationType.Information,
                TimeSpan.FromSeconds(8));
        });
    }

    private void Friends_FriendshipTerminated(object? sender, FriendshipTerminatedEventArgs e)
    {
        Dispatcher.UIThread.Post(QueueRefresh);
    }

    private void Friends_FriendshipResponse(object? sender, FriendshipResponseEventArgs e)
    {
        Dispatcher.UIThread.Post(QueueRefresh);
    }

    private void Friends_FriendNames(object? sender, FriendNamesEventArgs e)
    {
        Dispatcher.UIThread.Post(QueueRefresh);
    }

    private void Names_NameUpdated(object? sender, UUIDNameReplyEventArgs e)
    {
        // Only refresh if any resolved name belongs to a friend
        bool hasFriend = false;
        foreach (var id in e.Names.Keys)
        {
            if (Client.Friends.FriendList.ContainsKey(id))
            {
                hasFriend = true;
                break;
            }
        }
        if (hasFriend)
        {
            Dispatcher.UIThread.Post(QueueRefresh);
        }
    }

    #endregion
}

public record FriendEntry(
    UUID Id,
    string Name,
    bool IsOnline,
    bool CanSeeMeOnline,
    bool CanSeeMeOnMap,
    bool CanModifyMyObjects)
{
    public string DisplayText => IsOnline ? $"● {Name}" : $"○ {Name}";
}
