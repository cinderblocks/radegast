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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class MapViewModel : ClientAwareViewModelBase
{
    private bool Active => Client.Network.Connected;

    private readonly Dictionary<string, ulong> _regionHandles = new();
    private readonly HashSet<string> _requestedBlocks = new();
    private ulong _targetRegionHandle;
#pragma warning disable CS0414
    private bool _inTeleport;
#pragma warning restore CS0414
    private string? _pendingGoToRegion;

    // Favorites resolution state (for saving to credentials)
    private readonly Dictionary<UUID, List<(string Name, UUID AssetUUID, Vector3 Position)>> _pendingFavByRegionId = new();
    private readonly Dictionary<ulong, List<(string Name, UUID AssetUUID, Vector3 Position)>> _pendingFavByHandle = new();
    private readonly List<(string Name, string Location)> _resolvedFavorites = new();
    private int _pendingFavoriteCount;
    private volatile bool _loadingFavorites;
    private volatile bool _favoritesPopulated;
    private Timer? _favoritesTimeoutTimer;
    // Resolved positions cached for instant map navigation (UUID = landmark asset UUID)
    private readonly Dictionary<UUID, (string RegionName, int X, int Y, int Z)> _resolvedFavoritePositions = new();
    // Navigation-only pipeline (from OnSelectedFavoriteChanged when not yet resolved)
    private readonly Dictionary<UUID, (int X, int Y, int Z)> _navPendingByRegionId = new();
    private readonly Dictionary<ulong, (int X, int Y, int Z)> _navPendingByHandle = new();

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
    public ObservableCollection<FavoriteLandmarkEntry> FavoriteLandmarks { get; } = [];

    [ObservableProperty]
    private FavoriteLandmarkEntry? _selectedFavorite;

    [ObservableProperty]
    private MapSelfEntry? _selfEntry;

    // Map data exposed for the grid map control
    public ObservableCollection<MapRegionEntry> MapRegions { get; } = [];
    public ObservableCollection<MapAvatarEntry> MapAvatars { get; } = [];

    /// <summary>
    /// Raised when the map center should change (region grid X, region grid Y, local X, local Y).
    /// </summary>
    public event EventHandler<MapCenterChangedEventArgs>? MapCenterChanged;

    public MapViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        RegisterClientEvents(Client);

        // Initial location and favorites
        if (Client.Network.CurrentSim != null)
        {
            GotoMyPosition();
            LoadFavorites();
        }
    }

    public override void Dispose()
    {
        base.Dispose();
    }

    protected override void RegisterClientEvents(GridClient client)
    {
        client.Grid.GridRegion += Grid_GridRegion;
        client.Grid.GridItems += Grid_GridItems;
        client.Self.TeleportProgress += Self_TeleportProgress;
        client.Network.SimChanged += Network_SimChanged;
        client.Friends.FriendFoundReply += Friends_FriendFoundReply;
        client.Friends.FriendOnline += Friends_FriendStatusChanged;
        client.Friends.FriendOffline += Friends_FriendStatusChanged;
        client.Grid.RegionHandleReply += Grid_RegionHandleReply;
        client.Inventory.FolderUpdated += Inventory_FolderUpdated;
    }

    protected override void UnregisterClientEvents(GridClient client)
    {
        client.Grid.GridRegion -= Grid_GridRegion;
        client.Grid.GridItems -= Grid_GridItems;
        client.Self.TeleportProgress -= Self_TeleportProgress;
        client.Network.SimChanged -= Network_SimChanged;
        client.Friends.FriendFoundReply -= Friends_FriendFoundReply;
        client.Friends.FriendOnline -= Friends_FriendStatusChanged;
        client.Friends.FriendOffline -= Friends_FriendStatusChanged;
        client.Grid.RegionHandleReply -= Grid_RegionHandleReply;
        client.Inventory.FolderUpdated -= Inventory_FolderUpdated;
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
        Utils.LongToUInts(Client.Network.CurrentSim.Handle, out var rx, out var ry);
        SelfEntry = new MapSelfEntry(rx / 256, ry / 256, pos.X, pos.Y);
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

        _ = Task.Run(async () =>
        {
            if (!await Client.Self.TeleportAsync(region, destination))
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
            .Where(f => f.Value.IsOnline && f.Value.CanSeeThemOnMap)
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
    /// Locate a friend on the map. Call after switching to the map tab so the result is visible.
    /// </summary>
    public void ShowFriendOnMap(UUID agentId)
    {
        if (!Client.Friends.FriendList.TryGetValue(agentId, out var info)) return;
        if (!info.CanSeeThemOnMap) return;
        _targetRegionHandle = 0;
        Client.Friends.MapFriend(agentId);
        StatusText = $"Locating {info.Name}\u2026";
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

    #region Favorites

    partial void OnSelectedFavoriteChanged(FavoriteLandmarkEntry? value)
    {
        if (value == null || !Active) return;

        // If already resolved, navigate immediately
        lock (_resolvedFavoritePositions)
        {
            if (_resolvedFavoritePositions.TryGetValue(value.AssetUUID, out var pos))
            {
                RegionSearch = pos.RegionName;
                CoordX = pos.X;
                CoordY = pos.Y;
                CoordZ = pos.Z;
                CanTeleport = true;
                StatusText = $"Ready for {value.Name}";
                GotoRegion(pos.RegionName, pos.X, pos.Y);
                return;
            }
        }

        // Decode the landmark asset to find the region
        StatusText = $"Locating {value.Name}...";
        var capturedEntry = value;
        _ = Task.Run(async () =>
        {
            var asset = await Client.Assets.RequestAssetAsync(value.AssetUUID, AssetType.Landmark, true);
            if (asset is not AssetLandmark la || !la.Decode()) return;
            var localPos = la.Position;
            lock (_navPendingByRegionId)
                _navPendingByRegionId[la.RegionID] = ((int)localPos.X, (int)localPos.Y, (int)localPos.Z);
            Client.Grid.RequestRegionHandle(la.RegionID);
        });
    }

    private async void LoadFavorites()
    {
        if (!Active) return;
        if (_loadingFavorites) return;
        _loadingFavorites = true;
        try
        {
            var favFolderId = Client.Inventory.FindFolderForType(FolderType.Favorites);
            if (favFolderId == UUID.Zero) return;

            List<InventoryBase> contents;
            try
            {
                contents = await Client.Inventory.RequestFolderContentsAsync(
                    favFolderId, Client.Self.AgentID, false, true, InventorySortOrder.ByName);
            }
            catch { return; }

            // CAPS returned nothing: fall back to the local inventory store.
            // This handles two cases: (a) CAPS not yet available at startup,
            // (b) inventory restored from cache with NeedsUpdate=false so FetchAllFolders
            //     never re-fetches the favorites folder and FolderUpdated never fires for it.
            if (contents.Count == 0)
            {
                try { contents = Client.Inventory.Store?.GetContents(favFolderId) ?? []; }
                catch { }
            }

            var landmarks = contents.OfType<InventoryLandmark>().ToList();
            if (landmarks.Count > 0)
                _favoritesPopulated = true;

            Dispatcher.UIThread.Post(() =>
            {
                FavoriteLandmarks.Clear();
                foreach (var lm in landmarks)
                    FavoriteLandmarks.Add(new FavoriteLandmarkEntry(lm.Name, lm.AssetUUID));
            });

            // Resolve region names so we can persist login-time start locations
            lock (_resolvedFavorites) { _resolvedFavorites.Clear(); }
            lock (_pendingFavByRegionId) { _pendingFavByRegionId.Clear(); }
            lock (_pendingFavByHandle) { _pendingFavByHandle.Clear(); }

            _pendingFavoriteCount = landmarks.Count;
            if (landmarks.Count == 0) { SaveFavoritesToCredentials(); return; }

            foreach (var lm in landmarks)
            {
                var capturedName = lm.Name;
                var capturedUUID = lm.AssetUUID;
                _ = Task.Run(async () =>
                {
                    var asset = await Client.Assets.RequestAssetAsync(capturedUUID, AssetType.Landmark, true);
                    if (asset is AssetLandmark la && la.Decode())
                    {
                        lock (_pendingFavByRegionId)
                        {
                            if (!_pendingFavByRegionId.TryGetValue(la.RegionID, out var list))
                                _pendingFavByRegionId[la.RegionID] = list = [];
                            list.Add((capturedName, capturedUUID, la.Position));
                        }
                        Client.Grid.RequestRegionHandle(la.RegionID);
                    }
                    else if (Interlocked.Decrement(ref _pendingFavoriteCount) == 0)
                    {
                        SaveFavoritesToCredentials();
                    }
                });
            }

            // Safety-net: if any landmark regions never respond after 15 s, save whatever resolved
            _favoritesTimeoutTimer?.Dispose();
            _favoritesTimeoutTimer = new Timer(_ =>
            {
                if (_pendingFavoriteCount > 0)
                {
                    Interlocked.Exchange(ref _pendingFavoriteCount, 0);
                    Dispatcher.UIThread.Post(SaveFavoritesToCredentials);
                }
            }, null, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
        }
        finally { _loadingFavorites = false; }
    }

    private void Grid_RegionHandleReply(object? sender, RegionHandleReplyEventArgs e)
    {
        // Resolution pipeline (save favorites to credentials)
        List<(string Name, UUID AssetUUID, Vector3 Position)>? resPendingList;
        bool resFound;
        lock (_pendingFavByRegionId)
        {
            resFound = _pendingFavByRegionId.TryGetValue(e.RegionID, out resPendingList);
            if (resFound) _pendingFavByRegionId.Remove(e.RegionID);
        }
        if (resFound && resPendingList != null)
        {
            lock (_pendingFavByHandle)
            {
                if (!_pendingFavByHandle.TryGetValue(e.RegionHandle, out var existing))
                    _pendingFavByHandle[e.RegionHandle] = existing = [];
                existing.AddRange(resPendingList);
            }
        }

        // Navigation pipeline (from OnSelectedFavoriteChanged)
        (int X, int Y, int Z) navPos;
        bool navFound;
        lock (_navPendingByRegionId)
        {
            navFound = _navPendingByRegionId.TryGetValue(e.RegionID, out navPos);
            if (navFound) _navPendingByRegionId.Remove(e.RegionID);
        }
        if (navFound)
        {
            lock (_navPendingByHandle)
                _navPendingByHandle[e.RegionHandle] = navPos;
        }

        if (resFound || navFound)
        {
            // Shortcut: if region name is already known, resolve immediately without another round-trip
            string? knownName = null;
            lock (_regionHandles)
            {
                foreach (var kvp in _regionHandles)
                {
                    if (kvp.Value == e.RegionHandle) { knownName = kvp.Key; break; }
                }
            }

            if (knownName != null)
            {
                // Remove from by-handle dicts now so Grid_GridRegion won't double-process
                lock (_pendingFavByHandle) _pendingFavByHandle.Remove(e.RegionHandle);
                lock (_navPendingByHandle) _navPendingByHandle.Remove(e.RegionHandle);

                var capturedName = knownName;
                var capturedResPendingList = resPendingList;
                var capturedResFound = resFound;
                var capturedNavPos = navPos;
                var capturedNavFound = navFound;
                Dispatcher.UIThread.Post(() =>
                {
                    if (capturedResFound && capturedResPendingList != null)
                    {
                        foreach (var fav in capturedResPendingList)
                        {
                            var location = $"uri:{capturedName}&{(int)fav.Position.X}&{(int)fav.Position.Y}&{(int)fav.Position.Z}";
                            lock (_resolvedFavorites)
                                _resolvedFavorites.Add((fav.Name, location));
                            lock (_resolvedFavoritePositions)
                                _resolvedFavoritePositions[fav.AssetUUID] = (capturedName, (int)fav.Position.X, (int)fav.Position.Y, (int)fav.Position.Z);
                            if (Interlocked.Decrement(ref _pendingFavoriteCount) == 0)
                                SaveFavoritesToCredentials();
                        }
                    }
                    if (capturedNavFound)
                    {
                        RegionSearch = capturedName;
                        CoordX = capturedNavPos.X;
                        CoordY = capturedNavPos.Y;
                        CoordZ = capturedNavPos.Z;
                        CanTeleport = true;
                        StatusText = $"Ready for {capturedName}";
                        GotoRegion(capturedName, capturedNavPos.X, capturedNavPos.Y);
                    }
                });
            }
            else
            {
                // Region not yet known — request map block; returnNonExistent=true so deleted
                // regions still fire Grid_GridRegion and decrement _pendingFavoriteCount
                Utils.LongToUInts(e.RegionHandle, out uint gx, out uint gy);
                Client.Grid.RequestMapBlocks(GridLayerType.Objects,
                    (ushort)(gx / 256), (ushort)(gy / 256),
                    (ushort)(gx / 256), (ushort)(gy / 256), true);
            }
        }
    }

    private void Inventory_FolderUpdated(object? sender, FolderUpdatedEventArgs e)
    {
        if (!e.Success || _loadingFavorites) return;

        var favFolderId = Client.Inventory.FindFolderForType(FolderType.Favorites);
        if (e.FolderID == favFolderId)
        {
            // The favorites folder itself was updated — always reload
            // (handles the case where the user adds/removes favorites).
            _favoritesPopulated = false;
            Dispatcher.UIThread.Post(LoadFavorites);
        }
        else if (!_favoritesPopulated)
        {
            // Any other folder update during initial inventory loading is used as a
            // signal to retry, because the favorites folder may now be in the store
            // even if its FolderUpdated event never fires (NeedsUpdate=false from cache).
            Dispatcher.UIThread.Post(LoadFavorites);
        }
    }

    private void SaveFavoritesToCredentials()
    {
        _favoritesTimeoutTimer?.Dispose();
        var key = Instance.AccountKey;
        var cm = Instance.CredentialManager;
        if (string.IsNullOrEmpty(key) || cm == null) return;
        List<(string Name, string Location)> snapshot;
        lock (_resolvedFavorites)
            snapshot = [.. _resolvedFavorites];
        cm.SaveFavoriteLocations(key, snapshot);
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

            // Resolve pending favorite if this region was requested for location persistence
            List<(string Name, UUID AssetUUID, Vector3 Position)>? favPendingList;
            bool hasFav;
            lock (_pendingFavByHandle)
            {
                hasFav = _pendingFavByHandle.TryGetValue(e.Region.RegionHandle, out favPendingList);
                if (hasFav) _pendingFavByHandle.Remove(e.Region.RegionHandle);
            }
            if (hasFav && favPendingList != null)
            {
                foreach (var favPending in favPendingList)
                {
                    if (e.Region.Access != SimAccess.NonExistent)
                    {
                        var location = $"uri:{e.Region.Name}&{(int)favPending.Position.X}&{(int)favPending.Position.Y}&{(int)favPending.Position.Z}";
                        lock (_resolvedFavorites)
                            _resolvedFavorites.Add((favPending.Name, location));
                        lock (_resolvedFavoritePositions)
                            _resolvedFavoritePositions[favPending.AssetUUID] = (e.Region.Name, (int)favPending.Position.X, (int)favPending.Position.Y, (int)favPending.Position.Z);
                    }
                    if (Interlocked.Decrement(ref _pendingFavoriteCount) == 0)
                        SaveFavoritesToCredentials();
                }
            }

            // Navigation pipeline: center map on a selected favorite
            (int X, int Y, int Z) navPos;
            bool hasNav;
            lock (_navPendingByHandle)
            {
                hasNav = _navPendingByHandle.TryGetValue(e.Region.RegionHandle, out navPos);
                if (hasNav) _navPendingByHandle.Remove(e.Region.RegionHandle);
            }
            if (hasNav)
            {
                RegionSearch = e.Region.Name;
                CoordX = navPos.X;
                CoordY = navPos.Y;
                CoordZ = navPos.Z;
                CanTeleport = true;
                StatusText = $"Ready for {e.Region.Name}";
                GotoRegion(e.Region.Name, navPos.X, navPos.Y);
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
            Utils.LongToUInts(Client.Network.CurrentSim.Handle, out var rx, out var ry);
            SelfEntry = new MapSelfEntry(rx / 256, ry / 256, pos.X, pos.Y);
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
            bool nameResolved = false;
            lock (_regionHandles)
            {
                foreach (var kvp in _regionHandles)
                {
                    if (kvp.Value == e.RegionHandle)
                    {
                        RegionSearch = kvp.Key;
                        CanTeleport = true;
                        nameResolved = true;
                        break;
                    }
                }
            }
            if (!nameResolved)
            {
                // Region name not yet known; request map block so Grid_GridRegion fires
                // and sets RegionSearch + CanTeleport via _targetRegionHandle
                Client.Grid.RequestMapBlocks(GridLayerType.Objects,
                    (ushort)rx, (ushort)ry, (ushort)rx, (ushort)ry, false);
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

public record FavoriteLandmarkEntry(string Name, UUID AssetUUID)
{
    public override string ToString() => Name;
}

/// <summary>Current agent position on the world map.</summary>
public record MapSelfEntry(uint GridX, uint GridY, float LocalX, float LocalY);

#endregion
