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
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class MapViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private bool Active => Client.Network.Connected;

    private readonly Dictionary<string, ulong> _regionHandles = new();
    private readonly HashSet<string> _requestedBlocks = new();
    private ulong _targetRegionHandle;
#pragma warning disable CS0414
    private bool _inTeleport;
#pragma warning restore CS0414
    private string? _pendingGoToRegion;

    [ObservableProperty]
    private string _regionSearch = string.Empty;

    [ObservableProperty]
    private string? _selectedRegion;

    [ObservableProperty]
    private int _coordX = 128;

    [ObservableProperty]
    private int _coordY = 128;

    [ObservableProperty]
    private int _coordZ;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isTeleporting;

    [ObservableProperty]
    private bool _canTeleport;

    [ObservableProperty]
    private string? _selectedFriend;

    [ObservableProperty]
    private double _zoomLevel = 100;

    public ObservableCollection<string> RegionResults { get; } = [];
    public ObservableCollection<string> OnlineFriends { get; } = [];

    // Map data exposed for the grid map control
    public ObservableCollection<MapRegionEntry> MapRegions { get; } = [];
    public ObservableCollection<MapAvatarEntry> MapAvatars { get; } = [];

    /// <summary>
    /// Raised when the map center should change (region grid X, region grid Y, local X, local Y).
    /// </summary>
    public event EventHandler<MapCenterChangedEventArgs>? MapCenterChanged;

    public MapViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        RegisterClientEvents(Client);
        _instance.ClientChanged += Instance_ClientChanged;

        // Initial location
        if (Client.Network.CurrentSim != null)
        {
            GotoMyPosition();
        }
    }

    public void Dispose()
    {
        UnregisterClientEvents(Client);
        _instance.ClientChanged -= Instance_ClientChanged;
    }

    private void RegisterClientEvents(GridClient client)
    {
        client.Grid.GridRegion += Grid_GridRegion;
        client.Grid.GridItems += Grid_GridItems;
        client.Self.TeleportProgress += Self_TeleportProgress;
        client.Network.SimChanged += Network_SimChanged;
        client.Friends.FriendFoundReply += Friends_FriendFoundReply;
        client.Friends.FriendOnline += Friends_FriendStatusChanged;
        client.Friends.FriendOffline += Friends_FriendStatusChanged;
    }

    private void UnregisterClientEvents(GridClient client)
    {
        client.Grid.GridRegion -= Grid_GridRegion;
        client.Grid.GridItems -= Grid_GridItems;
        client.Self.TeleportProgress -= Self_TeleportProgress;
        client.Network.SimChanged -= Network_SimChanged;
        client.Friends.FriendFoundReply -= Friends_FriendFoundReply;
        client.Friends.FriendOnline -= Friends_FriendStatusChanged;
        client.Friends.FriendOffline -= Friends_FriendStatusChanged;
    }

    private void Instance_ClientChanged(object? sender, ClientChangedEventArgs e)
    {
        UnregisterClientEvents(e.OldClient);
        RegisterClientEvents(e.Client);
    }

    #region Search & Navigation

    [RelayCommand]
    private void Search()
    {
        if (!Active || string.IsNullOrWhiteSpace(RegionSearch) || RegionSearch.Length < 2) return;
        RegionResults.Clear();
        Client.Grid.RequestMapRegion(RegionSearch.Trim(), GridLayerType.Objects);
    }

    partial void OnSelectedRegionChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        CanTeleport = true;
        RegionSearch = value;
        CoordX = 128;
        CoordY = 128;
        StatusText = $"Ready for {value}";
        GotoRegion(value, CoordX, CoordY);
    }

    [RelayCommand]
    private void GoToMyPosition()
    {
        GotoMyPosition();
    }

    private void GotoMyPosition()
    {
        if (Client.Network.CurrentSim == null) return;
        var simName = Client.Network.CurrentSim.Name;
        var pos = Client.Self.SimPosition;
        RegionSearch = simName;
        CoordX = (int)pos.X;
        CoordY = (int)pos.Y;
        CoordZ = (int)pos.Z;
        GotoRegion(simName, CoordX, CoordY);
    }

    [RelayCommand]
    private void GoToDestination()
    {
        if (!string.IsNullOrWhiteSpace(RegionSearch))
        {
            GotoRegion(RegionSearch.Trim(), CoordX, CoordY);
        }
    }

    [RelayCommand]
    private void GoHome()
    {
        if (!Active) return;
        _inTeleport = true;
        IsTeleporting = true;
        StatusText = "Teleporting home...";
        Client.Self.RequestTeleport(UUID.Zero);
    }

    [RelayCommand]
    private void Teleport()
    {
        if (!Active || string.IsNullOrWhiteSpace(RegionSearch)) return;
        DoTeleport();
    }

    private void DoTeleport()
    {
        IsTeleporting = true;
        StatusText = $"Teleporting to {RegionSearch}...";

        var region = RegionSearch.Trim();
        var destination = new Vector3(CoordX, CoordY, CoordZ);

        Task.Run(() =>
        {
            if (!Client.Self.Teleport(region, destination))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsTeleporting = false;
                    StatusText = "Teleport failed.";
                });
            }
            _inTeleport = false;
        });
    }

    [RelayCommand]
    private void Refresh()
    {
        if (!Active || Client.Network.CurrentSim == null) return;
        // Request map items (avatar locations) around current position
        Client.Grid.RequestMapItems(
            Client.Network.CurrentSim.Handle,
            GridItemType.AgentLocations,
            GridLayerType.Objects);
    }

    /// <summary>
    /// Request region metadata (names, access levels) for the visible grid range.
    /// Matches the legacy MapControl pattern of calling RequestMapBlocks during paint.
    /// </summary>
    public void RequestMapBlocksForRange(ushort minX, ushort minY, ushort maxX, ushort maxY)
    {
        if (!Active) return;
        string block = $"{minX},{minY},{maxX},{maxY}";
        lock (_requestedBlocks)
        {
            if (!_requestedBlocks.Add(block)) return;
        }
        Client.Grid.RequestMapBlocks(GridLayerType.Objects, minX, minY, maxX, maxY, true);
    }

    private void GotoRegion(string regionName, int simX, int simY)
    {
        // Clear previously-requested block ranges so the new viewport
        // will fetch fresh region metadata from the server.
        lock (_requestedBlocks)
        {
            _requestedBlocks.Clear();
        }

        bool hasHandle;
        ulong handleValue;
        lock (_regionHandles)
        {
            hasHandle = _regionHandles.TryGetValue(regionName, out handleValue);
        }

        if (hasHandle)
        {
            uint rx, ry;
            Utils.LongToUInts(handleValue, out rx, out ry);
            rx /= 256;
            ry /= 256;
            MapCenterChanged?.Invoke(this, new MapCenterChangedEventArgs(rx, ry, (uint)simX, (uint)simY));
        }
        else
        {
            // Request the region info, and when it comes back we'll center the map
            _pendingGoToRegion = regionName;
            Client.Grid.RequestMapRegion(regionName, GridLayerType.Objects);
        }
    }

    #endregion

    #region Online Friends

    public void RefreshOnlineFriends()
    {
        OnlineFriends.Clear();

        var friends = Client.Friends.FriendList
            .Where(f => f.Value.IsOnline)
            .Select(f => f.Value.Name)
            .OrderBy(n => n)
            .ToList();

        foreach (var name in friends)
        {
            OnlineFriends.Add(name);
        }
    }

    partial void OnSelectedFriendChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        FriendInfo? friend = null;
        foreach (var kvp in Client.Friends.FriendList)
        {
            if (string.Equals(kvp.Value.Name, value, StringComparison.InvariantCulture))
            {
                friend = kvp.Value;
                break;
            }
        }

        if (friend != null)
        {
            _targetRegionHandle = 0;
            Client.Friends.MapFriend(friend.UUID);
            StatusText = $"Locating {friend.Name}...";
        }
    }

    private void Friends_FriendStatusChanged(object? sender, FriendInfoEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshOnlineFriends);
    }

    /// <summary>
    /// Teleport to a position identified by region grid coords + local offset.
    /// Called from the map control's double-click event.
    /// </summary>
    /// <summary>
    /// Updates the current map target (region + local coords) from a map click,
    /// without triggering a teleport.
    /// </summary>
    public void SetMapTarget(uint regionGridX, uint regionGridY, uint localX, uint localY)
    {
        if (!Active) return;
        var region = MapRegions.FirstOrDefault(r => r.GridX == regionGridX && r.GridY == regionGridY);
        if (region != null)
        {
            RegionSearch = region.Name;
        }
        CoordX = (int)localX;
        CoordY = (int)localY;
        CanTeleport = true;
    }

    public void TeleportToPosition(uint regionGridX, uint regionGridY, uint localX, uint localY)
    {
        if (!Active) return;
        var region = MapRegions.FirstOrDefault(r => r.GridX == regionGridX && r.GridY == regionGridY);
        if (region == null)
        {
            StatusText = "Unknown region — click a known region to teleport.";
            return;
        }
        RegionSearch = region.Name;
        CoordX = (int)localX;
        CoordY = (int)localY;
        CanTeleport = true;
        DoTeleport();
    }

    #endregion

    #region Network Events

    private void Grid_GridRegion(object? sender, GridRegionEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (_regionHandles)
            {
                _regionHandles[e.Region.Name] = e.Region.RegionHandle;
            }

            // If we were waiting for a region handle for the target
            if (e.Region.RegionHandle == _targetRegionHandle)
            {
                RegionSearch = e.Region.Name;
                CanTeleport = true;
                _targetRegionHandle = 0;
            }

            // Add to search results if matching
            if (!string.IsNullOrEmpty(RegionSearch)
                && e.Region.Name.Contains(RegionSearch, StringComparison.OrdinalIgnoreCase)
                && !RegionResults.Contains(e.Region.Name))
            {
                RegionResults.Add(e.Region.Name);
            }

            // Update map regions (skip non-existent placeholder regions)
            // GridRegion.X/Y are grid coordinates (not global), use directly
            uint rx = (uint)e.Region.X;
            uint ry = (uint)e.Region.Y;
            if (e.Region.Access != SimAccess.NonExistent)
            {
                var existing = MapRegions.FirstOrDefault(r => r.Handle == e.Region.RegionHandle);
                if (existing == null)
                {
                    MapRegions.Add(new MapRegionEntry(
                        e.Region.RegionHandle,
                        e.Region.Name,
                        rx, ry,
                        e.Region.Access));
                }
            }

            // If we were waiting for a search/goto result, center map once then clear
            if (_pendingGoToRegion != null
                && string.Equals(e.Region.Name, _pendingGoToRegion, StringComparison.OrdinalIgnoreCase))
            {
                _pendingGoToRegion = null;
                MapCenterChanged?.Invoke(this, new MapCenterChangedEventArgs(rx, ry, (uint)CoordX, (uint)CoordY));
            }
        });
    }

    private void Grid_GridItems(object? sender, GridItemsEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var item in e.Items)
            {
                if (item is MapAgentLocation loc && loc.AvatarCount > 0)
                {
                    uint rx, ry;
                    Utils.LongToUInts(loc.RegionHandle, out rx, out ry);
                    rx /= 256;
                    ry /= 256;

                    MapAvatars.Add(new MapAvatarEntry(
                        rx, ry,
                        loc.LocalX, loc.LocalY,
                        loc.AvatarCount));
                }
            }
        });
    }

    private void Network_SimChanged(object? sender, SimChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Client.Network.CurrentSim == null) return;
            var simName = Client.Network.CurrentSim.Name;
            var pos = Client.Self.SimPosition;
            StatusText = $"Now in {simName}";
            GotoRegion(simName, (int)pos.X, (int)pos.Y);
        });
    }

    private int _lastTeleportTick;

    private void Self_TeleportProgress(object? sender, TeleportEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.Status)
            {
                case TeleportStatus.Start:
                    StatusText = $"Teleporting to {RegionSearch}...";
                    _inTeleport = true;
                    IsTeleporting = true;
                    break;

                case TeleportStatus.Progress:
                    StatusText = $"Progress: {e.Message}";
                    _inTeleport = true;
                    IsTeleporting = true;
                    break;

                case TeleportStatus.Failed:
                case TeleportStatus.Cancelled:
                    _inTeleport = false;
                    IsTeleporting = false;
                    StatusText = $"Failed: {e.Message}";
                    if (Environment.TickCount - _lastTeleportTick > 500)
                        _instance.ShowNotificationInChat("Teleport failed");
                    _lastTeleportTick = Environment.TickCount;
                    break;

                case TeleportStatus.Finished:
                    _inTeleport = false;
                    IsTeleporting = false;
                    StatusText = "Teleport complete";
                    if (Environment.TickCount - _lastTeleportTick > 500)
                        _instance.ShowNotificationInChat("Teleport complete");
                    _lastTeleportTick = Environment.TickCount;
                    break;

                default:
                    _inTeleport = false;
                    IsTeleporting = false;
                    break;
            }
        });
    }

    private void Friends_FriendFoundReply(object? sender, FriendFoundReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CoordX = (int)e.Location.X;
            CoordY = (int)e.Location.Y;
            CoordZ = (int)e.Location.Z;
            _targetRegionHandle = e.RegionHandle;

            uint rx, ry;
            Utils.LongToUInts(e.RegionHandle, out rx, out ry);
            rx /= 256;
            ry /= 256;

            // Try to find region name from known handles
            lock (_regionHandles)
            {
                foreach (var kvp in _regionHandles)
                {
                    if (kvp.Value == e.RegionHandle)
                    {
                        RegionSearch = kvp.Key;
                        CanTeleport = true;
                        break;
                    }
                }
            }

            MapCenterChanged?.Invoke(this, new MapCenterChangedEventArgs(rx, ry, (uint)CoordX, (uint)CoordY));
            StatusText = $"Friend found at ({CoordX}, {CoordY}, {CoordZ})";
        });
    }

    #endregion
}

#region Data Models

public record MapRegionEntry(
    ulong Handle,
    string Name,
    uint GridX,
    uint GridY,
    SimAccess Access);

public record MapAvatarEntry(
    uint GridX,
    uint GridY,
    float LocalX,
    float LocalY,
    int Count);

public class MapCenterChangedEventArgs : EventArgs
{
    public uint RegionGridX { get; }
    public uint RegionGridY { get; }
    public uint LocalX { get; }
    public uint LocalY { get; }

    public MapCenterChangedEventArgs(uint regionGridX, uint regionGridY, uint localX, uint localY)
    {
        RegionGridX = regionGridX;
        RegionGridY = regionGridY;
        LocalX = localX;
        LocalY = localY;
    }
}

#endregion
