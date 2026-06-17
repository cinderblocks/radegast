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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class ObjectsViewModel : ClientAwareViewModelBase
{
    private CancellationTokenSource? _refreshCts;
    private readonly object _refreshLock = new();
    private Timer? _objectUpdateTimer;
    private readonly object _timerLock = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private double _searchRadius = 40.0;

    [ObservableProperty]
    private int _filterIndex; // 0=Rezzed, 1=Attached, 2=Both

    private int _objectCount;

    // StatusText is computed; _statusText is the base (object count) portion.
    public string StatusText => PendingCount > 0
        ? $"Tracking {_objectCount} objects. (Loading {PendingCount} properties...)"
        : $"Tracking {_objectCount} objects.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedObjectMapId))]
    [NotifyPropertyChangedFor(nameof(IsRezzedObject))]
    [NotifyCanExecuteChangedFor(nameof(WalkToCommand))]
    [NotifyCanExecuteChangedFor(nameof(PointAtCommand))]
    private ObjectEntry? _selectedObject;

    [ObservableProperty]
    private string _objectName = string.Empty;

    [ObservableProperty]
    private string _objectDescription = string.Empty;

    [ObservableProperty]
    private string _objectOwner = string.Empty;

    [ObservableProperty]
    private UUID _objectOwnerID;

    [ObservableProperty]
    private string _objectCreator = string.Empty;

    [ObservableProperty]
    private UUID _objectCreatorID;

    [ObservableProperty]
    private int _objectPrimCount;

    [ObservableProperty]
    private string _objectHoverText = string.Empty;

    [ObservableProperty]
    private string _objectAttachmentPoint = string.Empty;

    [ObservableProperty]
    private string _objectGroupName = string.Empty;

    [ObservableProperty]
    private UUID _objectGroupID;

    [ObservableProperty]
    private bool _isGroupOwned;

    // Pending property requests — drives the progress indicator
    private int _pendingPropertiesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsLoadingProperties))]
    private int _pendingCount;

    public bool IsLoadingProperties => PendingCount > 0;

    // Editable fields (only writable when CanEditProperties)
    [ObservableProperty] private string _objectNameInput = string.Empty;
    [ObservableProperty] private string _objectDescriptionInput = string.Empty;

    // Owner permissions — display only
    [ObservableProperty] private bool _ownerCanModify;
    [ObservableProperty] private bool _ownerCanCopy;
    [ObservableProperty] private bool _ownerCanTransfer;

    // Next-owner permissions — editable if CanEditProperties
    [ObservableProperty] private bool _nextOwnerModify;
    [ObservableProperty] private bool _nextOwnerCopy;
    [ObservableProperty] private bool _nextOwnerTransfer;

    [ObservableProperty] private bool _canEditProperties;

    // Transform (position, rotation, scale) of selected prim
    [ObservableProperty] private decimal _posX;
    [ObservableProperty] private decimal _posY;
    [ObservableProperty] private decimal _posZ;
    [ObservableProperty] private decimal _rotX;
    [ObservableProperty] private decimal _rotY;
    [ObservableProperty] private decimal _rotZ;
    [ObservableProperty] private decimal _scaleX;
    [ObservableProperty] private decimal _scaleY;
    [ObservableProperty] private decimal _scaleZ;
    [ObservableProperty] private bool _hasTransform;

    private bool _suppressPermissionSave;

    [ObservableProperty]
    private bool _isSitting;

    [ObservableProperty]
    private bool _isPointing;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isForSale;

    [ObservableProperty]
    private int _salePrice;

    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private bool _canReturn;

    [ObservableProperty]
    private int _sortIndex; // 0=By Distance, 1=By Name

    private SaleType _objectSaleType;

    public string SitButtonText => IsSitting ? "Stand Up" : "Sit On";
    public string PointButtonText => IsPointing ? "Unpoint" : "Point At";
    public string MuteButtonText => IsMuted ? "Unmute" : "Mute";
    public string BuyButtonText => $"Buy L${SalePrice}";

    public ObservableCollection<ObjectEntry> Objects { get; } = [];
    public string[] FilterOptions { get; } = ["Rezzed", "Attached", "Both"];
    public string[] SortOptions { get; } = ["By Distance", "By Name"];

    // ── Object minimap ───────────────────────────────────────────────────────
    [ObservableProperty] private Bitmap? _objectMapTile;
    [ObservableProperty] private float _selfX;
    [ObservableProperty] private float _selfY;
    public ObservableCollection<ObjectMapEntry> MapEntries { get; } = [];
    /// <summary>ID of the currently selected object, forwarded to the object minimap control.</summary>
    public UUID SelectedObjectMapId => SelectedObject?.Id ?? UUID.Zero;

    /// <summary>True when the selected object is rezzed in-world (not an attachment or HUD).</summary>
    public bool IsRezzedObject => SelectedObject is { IsAttachment: false };

    public ObjectsViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        _isSitting = _instance.State.IsSitting;

        RegisterClientEvents(Client);
        _instance.State.SitStateChanged += State_SitStateChanged;

        var selfPos = Client.Self.SimPosition;
        _selfX = selfPos.X;
        _selfY = selfPos.Y;
        FetchMapTile();
        QueueRefresh();
    }

    public override void Dispose()
    {
        base.Dispose();
        _instance.State.SitStateChanged -= State_SitStateChanged;

        lock (_timerLock)
        {
            _objectUpdateTimer?.Dispose();
            _objectUpdateTimer = null;
        }

        lock (_refreshLock)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
        }
    }

    protected override void RegisterClientEvents(GridClient client)
    {
        client.Objects.ObjectUpdate += Objects_ObjectUpdate;
        client.Objects.KillObjects += Objects_KillObjects;
        client.Objects.ObjectProperties += Objects_ObjectProperties;
        client.Network.SimChanged += Network_SimChanged;
        client.Self.MuteListUpdated += Self_MuteListUpdated;
        client.Groups.GroupNamesReply += Groups_GroupNamesReply;
    }

    protected override void UnregisterClientEvents(GridClient client)
    {
        client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
        client.Objects.KillObjects -= Objects_KillObjects;
        client.Objects.ObjectProperties -= Objects_ObjectProperties;
        client.Network.SimChanged -= Network_SimChanged;
        client.Self.MuteListUpdated -= Self_MuteListUpdated;
        client.Groups.GroupNamesReply -= Groups_GroupNamesReply;
    }

    private void State_SitStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => IsSitting = _instance.State.IsSitting);
    }

    partial void OnSortIndexChanged(int value) => QueueRefresh();

    partial void OnIsSittingChanged(bool value)
    {
        OnPropertyChanged(nameof(SitButtonText));
    }

    partial void OnIsPointingChanged(bool value)
    {
        OnPropertyChanged(nameof(PointButtonText));
        PointAtCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MuteButtonText));
    }

    partial void OnIsForSaleChanged(bool value)
    {
        OnPropertyChanged(nameof(BuyButtonText));
        BuyObjectCommand.NotifyCanExecuteChanged();
    }

    partial void OnSalePriceChanged(int value) => OnPropertyChanged(nameof(BuyButtonText));

    partial void OnCanDeleteChanged(bool value) => DeleteObjectCommand.NotifyCanExecuteChanged();

    partial void OnCanReturnChanged(bool value) => ReturnObjectCommand.NotifyCanExecuteChanged();

    partial void OnCanEditPropertiesChanged(bool value) => SavePropertiesCommand.NotifyCanExecuteChanged();

    partial void OnSelectedObjectChanged(ObjectEntry? value)
    {
        if (value == null)
        {
            ObjectName = string.Empty;
            ObjectDescription = string.Empty;
            ObjectOwner = string.Empty;
            ObjectOwnerID = UUID.Zero;
            ObjectCreator = string.Empty;
            ObjectCreatorID = UUID.Zero;
            ObjectPrimCount = 0;
            ObjectHoverText = string.Empty;
            ObjectAttachmentPoint = string.Empty;
            ObjectGroupName = string.Empty;
            ObjectGroupID = UUID.Zero;
            IsGroupOwned = false;
            ObjectNameInput = string.Empty;
            ObjectDescriptionInput = string.Empty;
            IsMuted = false;
            IsForSale = false;
            SalePrice = 0;
            _objectSaleType = SaleType.Not;
            CanDelete = false;
            CanReturn = false;
            CanEditProperties = false;
            _suppressPermissionSave = true;
            OwnerCanModify = OwnerCanCopy = OwnerCanTransfer = false;
            NextOwnerModify = NextOwnerCopy = NextOwnerTransfer = false;
            _suppressPermissionSave = false;
            HasTransform = false;
            PosX = PosY = PosZ = 0;
            RotX = RotY = RotZ = 0;
            ScaleX = ScaleY = ScaleZ = 0;
            return;
        }

        ObjectName = value.Name;
        ObjectDescription = value.Description;
        ObjectOwner = value.OwnerName;
        ObjectOwnerID = value.OwnerID;
        ObjectCreator = value.CreatorName;
        ObjectCreatorID = value.CreatorID;
        ObjectPrimCount = value.PrimCount;
        ObjectHoverText = value.HoverText;
        ObjectNameInput = value.Name;
        ObjectDescriptionInput = value.Description;

        // Attachment point
        ObjectAttachmentPoint = value.IsAttachment
            ? FormatAttachmentPoint(value.AttachmentPoint)
            : string.Empty;

        // Group ownership
        IsGroupOwned = value.IsGroupOwned;
        ObjectGroupID = value.GroupID;
        if (value.IsGroupOwned && value.GroupID != UUID.Zero)
        {
            if (Client.Groups.GroupName2KeyCache.TryGetValue(value.GroupID, out var cachedName))
                ObjectGroupName = cachedName;
            else
            {
                ObjectGroupName = "(loading...)";
                Client.Groups.RequestGroupName(value.GroupID);
            }
        }
        else
        {
            ObjectGroupName = string.Empty;
        }

        IsMuted = Client.Self.MuteList.Values.Any(m => m.Type == MuteType.Object && m.ID == value.Id);

        _objectSaleType = value.SaleType;
        SalePrice = value.SalePrice;
        IsForSale = value.SaleType != SaleType.Not;

        CanDelete = value.OwnerID == Client.Self.AgentID;
        CanReturn = value.Distance > 0;
        CanEditProperties = value.OwnerID == Client.Self.AgentID &&
                            (value.Flags & PrimFlags.ObjectModify) != 0;

        _suppressPermissionSave = true;
        OwnerCanModify   = (value.OwnerMask & PermissionMask.Modify)   != 0;
        OwnerCanCopy     = (value.OwnerMask & PermissionMask.Copy)     != 0;
        OwnerCanTransfer = (value.OwnerMask & PermissionMask.Transfer) != 0;
        NextOwnerModify   = (value.NextOwnerMask & PermissionMask.Modify)   != 0;
        NextOwnerCopy     = (value.NextOwnerMask & PermissionMask.Copy)     != 0;
        NextOwnerTransfer = (value.NextOwnerMask & PermissionMask.Transfer) != 0;
        _suppressPermissionSave = false;

        // Populate transform from live prim data
        if (Client.Network.CurrentSim?.ObjectsPrimitives.TryGetValue(value.LocalId, out var prim) == true)
        {
            PosX = (decimal)prim.Position.X;
            PosY = (decimal)prim.Position.Y;
            PosZ = (decimal)prim.Position.Z;
            prim.Rotation.GetEulerAngles(out float roll, out float pitch, out float yaw);
            RotX = (decimal)(roll  * 180f / MathF.PI);
            RotY = (decimal)(pitch * 180f / MathF.PI);
            RotZ = (decimal)(yaw   * 180f / MathF.PI);
            ScaleX = (decimal)prim.Scale.X;
            ScaleY = (decimal)prim.Scale.Y;
            ScaleZ = (decimal)prim.Scale.Z;
            HasTransform = true;
        }
        else
        {
            HasTransform = false;
            PosX = PosY = PosZ = 0;
            RotX = RotY = RotZ = 0;
            ScaleX = ScaleY = ScaleZ = 0;
        }
    }

    // Insert spaces before capital letters to make enum names human-readable.
    // e.g. "RightHand" → "Right Hand"
    private static string FormatAttachmentPoint(AttachmentPoint ap)
    {
        var raw = ap.ToString();
        var sb = new System.Text.StringBuilder(raw.Length + 8);
        foreach (var c in raw)
        {
            if (sb.Length > 0 && char.IsUpper(c)) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    partial void OnFilterIndexChanged(int value)
    {
        QueueRefresh();
    }

    partial void OnNextOwnerModifyChanged(bool value) => SendPermissionUpdate(PermissionMask.Modify, value);
    partial void OnNextOwnerCopyChanged(bool value) => SendPermissionUpdate(PermissionMask.Copy, value);
    partial void OnNextOwnerTransferChanged(bool value) => SendPermissionUpdate(PermissionMask.Transfer, value);

    private void SendPermissionUpdate(PermissionMask mask, bool grant)
    {
        if (_suppressPermissionSave || SelectedObject == null || Client.Network.CurrentSim == null) return;
        Client.Objects.SetPermissions(Client.Network.CurrentSim,
            [SelectedObject.LocalId], PermissionWho.NextOwner, mask, grant);
    }

    #region Commands

    [RelayCommand]
    private void Refresh()
    {
        QueueRefresh();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        QueueRefresh();
    }

    [RelayCommand(CanExecute = nameof(CanEditProperties))]
    private void SaveProperties()
    {
        if (SelectedObject == null || Client.Network.CurrentSim == null) return;
        var sim = Client.Network.CurrentSim;
        if (ObjectNameInput != SelectedObject.Name)
            Client.Objects.SetName(sim, SelectedObject.LocalId, ObjectNameInput);
        if (ObjectDescriptionInput != SelectedObject.Description)
            Client.Objects.SetDescription(sim, SelectedObject.LocalId, ObjectDescriptionInput);
    }

    [RelayCommand]
    private void ViewContents()
    {
        if (SelectedObject == null) return;
        _instance.ShowObjectContents(SelectedObject.Id, SelectedObject.LocalId, SelectedObject.Name);
    }

    [RelayCommand]
    private void View3D()
    {
        if (SelectedObject == null) return;
        _instance.ShowPrimViewer(SelectedObject.LocalId, SelectedObject.Name);
    }

    [RelayCommand]
    private void TouchObject()
    {
        if (SelectedObject == null) return;
        Client.Self.Touch(SelectedObject.LocalId);
    }

    [RelayCommand]
    private void SitOn()
    {
        if (SelectedObject == null) return;
        if (_instance.State.IsSitting)
        {
            _instance.State.SetSitting(false, UUID.Zero);
        }
        else
        {
            _instance.State.SetSitting(true, SelectedObject.Id);
        }
    }

    [RelayCommand]
    private void TurnTo()
    {
        if (SelectedObject == null || Client.Network.CurrentSim == null) return;
        if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(SelectedObject.LocalId, out var prim))
        {
            Client.Self.Movement.TurnToward(prim.Position);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRezzedObject))]
    private void WalkTo()
    {
        if (SelectedObject == null || Client.Network.CurrentSim == null) return;
        if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(SelectedObject.LocalId, out var prim))
        {
            _instance.State.WalkTo(prim);
        }
    }

    private bool CanPointAt() => IsPointing || IsRezzedObject;

    [RelayCommand(CanExecute = nameof(CanPointAt))]
    private void PointAt()
    {
        if (SelectedObject == null || Client.Network.CurrentSim == null) return;
        if (_instance.State.IsPointing)
        {
            _instance.State.UnSetPointing();
            IsPointing = false;
        }
        else
        {
            if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(SelectedObject.LocalId, out var prim))
            {
                _instance.State.SetPointing(prim, 3);
                IsPointing = true;
            }
        }
    }

    [RelayCommand]
    private void TakeObject()
    {
        if (SelectedObject == null) return;
        Client.Inventory.RequestDeRezToInventory(SelectedObject.LocalId);
        _instance.ShowNotificationInChat($"Taking {SelectedObject.Name}...");
    }

    [RelayCommand]
    private void PayObject()
    {
        if (SelectedObject == null) return;
        _instance.OpenPayWindow(SelectedObject.Id, SelectedObject.Name, true, Client.Network.CurrentSim);
    }

    [RelayCommand]
    private void MuteObject()
    {
        if (SelectedObject == null) return;
        if (IsMuted)
        {
            Client.Self.RemoveMuteListEntry(SelectedObject.Id, SelectedObject.Name);
            IsMuted = false;
            _instance.ShowNotificationInChat($"Unmuted {SelectedObject.Name}");
        }
        else
        {
            Client.Self.UpdateMuteListEntry(MuteType.Object, SelectedObject.Id, SelectedObject.Name);
            IsMuted = true;
            _instance.ShowNotificationInChat($"Muted {SelectedObject.Name}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsForSale))]
    private void BuyObject()
    {
        if (SelectedObject == null || Client.Network.CurrentSim == null) return;
        var folder = Client.Inventory.FindFolderForType(AssetType.Object);
        Client.Objects.BuyObject(Client.Network.CurrentSim, SelectedObject.LocalId,
            _objectSaleType, SalePrice, Client.Self.ActiveGroup, folder);
        _instance.ShowNotificationInChat($"Buying {SelectedObject.Name} for L${SalePrice}...");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteObject()
    {
        if (SelectedObject == null || Client.Network.CurrentSim == null) return;
        Client.Inventory.RequestDeRezToInventory(SelectedObject.LocalId,
            DeRezDestination.AgentInventoryTake, UUID.Zero, UUID.Random());
        _instance.ShowNotificationInChat($"Deleting {SelectedObject.Name}...");
    }

    [RelayCommand(CanExecute = nameof(CanReturn))]
    private void ReturnObject()
    {
        if (SelectedObject == null) return;
        Client.Inventory.RequestDeRezToInventory(SelectedObject.LocalId,
            DeRezDestination.ReturnToOwner, UUID.Zero, UUID.Random());
        _instance.ShowNotificationInChat($"Returning {SelectedObject.Name}...");
    }

    #endregion

    #region Refresh Logic

    private void QueueRefresh()
    {
        _ = RefreshObjectsAsync();
    }

    private async Task RefreshObjectsAsync()
    {
        CancellationTokenSource cts;
        lock (_refreshLock)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = new CancellationTokenSource();
            cts = _refreshCts;
            // Reset the pending counter — property responses from previous refresh contexts
            // (e.g. objects in the old sim after a teleport) will never arrive, so the
            // counter must be zeroed before issuing new requests.
            Interlocked.Exchange(ref _pendingPropertiesCount, 0);
        }

        // Reflect the reset immediately on the UI thread before any new requests are queued.
        Dispatcher.UIThread.Post(() => PendingCount = 0);

        var token = cts.Token;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        var location = Client.Self.SimPosition;
        var selfLocalId = Client.Self.LocalID;
        var currentRadius = SearchRadius;
        var currentFilter = FilterIndex;
        var currentSearch = SearchText.ToLowerInvariant();
        var sortByName = SortIndex == 1;

        try
        {
            var results = await Task.Run(() =>
            {
                var list = new List<ObjectEntry>();

                foreach (var kvp in sim.ObjectsPrimitives)
                {
                    token.ThrowIfCancellationRequested();

                    var prim = kvp.Value;

                    bool include = currentFilter switch
                    {
                        0 => prim.ParentID == 0, // Rezzed
                        1 => prim.ParentID == selfLocalId, // Attached
                        _ => prim.ParentID == 0 || prim.ParentID == selfLocalId // Both
                    };

                    if (!include) continue;
                    if (prim.Position == Vector3.Zero) continue;

                    int distance = prim.ParentID == selfLocalId
                        ? 0
                        : (int)Vector3.Distance(prim.Position, location);
                    if (distance >= currentRadius) continue;

                    var name = prim.Properties?.Name ?? $"Object ({prim.LocalID})";
                    var desc = prim.Properties?.Description ?? string.Empty;

                    if (!string.IsNullOrEmpty(currentSearch))
                    {
                        if (name.IndexOf(currentSearch, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }

                    var ownerName = prim.Properties?.OwnerID != null
                        ? _instance.Names.Get(prim.Properties.OwnerID)
                        : string.Empty;
                    var creatorName = prim.Properties?.CreatorID != null
                        ? _instance.Names.Get(prim.Properties.CreatorID)
                        : string.Empty;

                    // Group-owned detection
                    var isGroupOwned = prim.Properties != null &&
                                       prim.Properties.OwnerID == UUID.Zero &&
                                       prim.Properties.GroupID != UUID.Zero &&
                                       (prim.Flags & PrimFlags.ObjectGroupOwned) != 0;
                    var groupId = isGroupOwned ? prim.Properties!.GroupID : UUID.Zero;
                    if (isGroupOwned) ownerName = "(group)";

                    list.Add(new ObjectEntry(
                        prim.ID,
                        prim.LocalID,
                        name,
                        desc,
                        distance,
                        ownerName,
                        creatorName,
                        prim.Properties?.ObjectID ?? UUID.Zero,
                        1, // Approximate; true prim count requires link set traversal
                        prim.Properties?.OwnerID ?? UUID.Zero,
                        prim.Properties?.CreatorID ?? UUID.Zero,
                        prim.Properties?.SaleType ?? SaleType.Not,
                        prim.Properties?.SalePrice ?? 0,
                        prim.Text ?? string.Empty,
                        prim.Properties?.Permissions.OwnerMask ?? PermissionMask.None,
                        prim.Properties?.Permissions.NextOwnerMask ?? PermissionMask.None,
                        prim.Flags,
                        prim.PrimData.AttachmentPoint,
                        groupId,
                        isGroupOwned
                    ));

                    // Request properties if not yet fetched; track pending count
                    if (prim.Properties == null)
                    {
                        Client.Objects.SelectObject(sim, prim.LocalID);
                        Interlocked.Increment(ref _pendingPropertiesCount);
                    }
                }

                if (sortByName)
                    list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                else
                    list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                return list;
            }, token);

            if (token.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                PendingCount = _pendingPropertiesCount;
                // Diff-based update: never clear the whole collection so SelectedObject
                // is never auto-nulled by the ListBox binding.
                var selectedId = SelectedObject?.Id;

                var newByLocalId = results.ToDictionary(e => e.LocalId);

                // Remove items no longer in results (iterate backwards to keep indices valid)
                for (int i = Objects.Count - 1; i >= 0; i--)
                {
                    if (!newByLocalId.ContainsKey(Objects[i].LocalId))
                        Objects.RemoveAt(i);
                }

                // Build current index lookup
                var existingIndex = new Dictionary<uint, int>(Objects.Count);
                for (int i = 0; i < Objects.Count; i++)
                    existingIndex[Objects[i].LocalId] = i;

                // Update existing items and add new ones
                foreach (var newItem in results)
                {
                    if (existingIndex.TryGetValue(newItem.LocalId, out int idx))
                    {
                        var existing = Objects[idx];
                        if (existing.Distance != newItem.Distance || existing.Name != newItem.Name)
                            Objects[idx] = newItem;
                    }
                    else
                    {
                        Objects.Add(newItem);
                    }
                }

                // Re-sort in-place using Move to avoid clearing selection
                var sorted = sortByName
                    ? Objects.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList()
                    : Objects.OrderBy(o => o.Distance).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    int current = Objects.IndexOf(sorted[i]);
                    if (current != i)
                        Objects.Move(current, i);
                }

                // Restore selection if it still exists and was cleared by a move
                if (selectedId.HasValue && SelectedObject == null)
                {
                    SelectedObject = Objects.FirstOrDefault(o => o.Id == selectedId.Value);
                }

                _objectCount = Objects.Count;
                OnPropertyChanged(nameof(StatusText));

                var selfPos = Client.Self.SimPosition;
                SelfX = selfPos.X;
                SelfY = selfPos.Y;
                RebuildMapEntries();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _objectCount = 0;
                OnPropertyChanged(nameof(StatusText));
                _ = ex; // surface error in debug
            });
        }
    }

    #endregion

    #region Network Events

    private void Objects_ObjectUpdate(object? sender, PrimEventArgs e)
    {
        lock (_timerLock)
        {
            // Leading-edge coalesce: only start a new timer if one isn't already pending.
            // The legacy debounce reset the timer on every update, but ObjectUpdate fires
            // continuously (avatar movements, physics, etc.) so the timer never fired.
            if (_objectUpdateTimer == null)
            {
                _objectUpdateTimer = new Timer(_ =>
                {
                    lock (_timerLock)
                    {
                        _objectUpdateTimer?.Dispose();
                        _objectUpdateTimer = null;
                    }
                    Dispatcher.UIThread.Post(QueueRefresh);
                }, null, 2000, Timeout.Infinite);
            }
        }
    }

    private void Objects_KillObjects(object? sender, KillObjectsEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var localId in e.ObjectLocalIDs)
            {
                for (int i = Objects.Count - 1; i >= 0; i--)
                {
                    if (Objects[i].LocalId == localId)
                    {
                        Objects.RemoveAt(i);
                        break;
                    }
                }
            }
            _objectCount = Objects.Count;
            OnPropertyChanged(nameof(StatusText));
            RebuildMapEntries();
        });
    }

    private void Objects_ObjectProperties(object? sender, ObjectPropertiesEventArgs e)
    {
        // Decrement pending counter (it was previously tracking objects with no properties)
        var wasPending = Interlocked.Decrement(ref _pendingPropertiesCount);
        if (wasPending < 0) Interlocked.Exchange(ref _pendingPropertiesCount, 0);

        Dispatcher.UIThread.Post(() =>
        {
            PendingCount = Math.Max(0, _pendingPropertiesCount);
            for (int i = 0; i < Objects.Count; i++)
            {
                if (Objects[i].Id == e.Properties.ObjectID)
                {
                    var old = Objects[i];
                    var ownerName = _instance.Names.Get(e.Properties.OwnerID);
                    var creatorName = _instance.Names.Get(e.Properties.CreatorID);

                    // Detect group-owned objects (OwnerID is Zero, GroupID is set, ObjectGroupOwned flag set)
                    var isGroupOwned = e.Properties.OwnerID == UUID.Zero &&
                                       e.Properties.GroupID != UUID.Zero &&
                                       (old.Flags & PrimFlags.ObjectGroupOwned) != 0;
                    var groupId = isGroupOwned ? e.Properties.GroupID : UUID.Zero;

                    // Kick off async group name resolution if needed
                    if (isGroupOwned && !Client.Groups.GroupName2KeyCache.TryGetValue(groupId, out _))
                        Client.Groups.RequestGroupName(groupId);

                    Objects[i] = old with
                    {
                        Name = e.Properties.Name,
                        Description = e.Properties.Description ?? string.Empty,
                        OwnerName = isGroupOwned ? "(group)" : ownerName,
                        CreatorName = creatorName,
                        OwnerID = e.Properties.OwnerID,
                        CreatorID = e.Properties.CreatorID,
                        SaleType = e.Properties.SaleType,
                        SalePrice = e.Properties.SalePrice,
                        OwnerMask = e.Properties.Permissions.OwnerMask,
                        NextOwnerMask = e.Properties.Permissions.NextOwnerMask,
                        IsGroupOwned = isGroupOwned,
                        GroupID = groupId,
                    };

                    if (SelectedObject?.Id == e.Properties.ObjectID)
                    {
                        SelectedObject = Objects[i];
                    }
                    break;
                }
            }
        });
    }

    private void Network_SimChanged(object? sender, SimChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MapEntries.Clear();
            ObjectMapTile = null;
            FetchMapTile();
            QueueRefresh();
        });
    }

    private void Self_MuteListUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedObject == null) return;
            IsMuted = Client.Self.MuteList.Values.Any(m =>
                m.Type == MuteType.Object && m.ID == SelectedObject.Id);
        });
    }

    private void Groups_GroupNamesReply(object? sender, GroupNamesEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Update any entries whose GroupID was resolved
            for (int i = 0; i < Objects.Count; i++)
            {
                var entry = Objects[i];
                if (entry.IsGroupOwned && e.GroupNames.TryGetValue(entry.GroupID, out var name))
                {
                    Objects[i] = entry with { OwnerName = name };
                    if (SelectedObject?.Id == entry.Id)
                    {
                        ObjectGroupName = name;
                        SelectedObject = Objects[i];
                    }
                }
            }
        });
    }

    #endregion

    #region Object Map Helpers

    private void FetchMapTile()
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        Utils.LongToUInts(sim.Handle, out var gridX, out var gridY);
        gridX /= 256;
        gridY /= 256;
        var cached = MapTileCache.GetTile(gridX, gridY);
        if (cached != null) { ObjectMapTile = cached; return; }
        MapTileCache.RequestTile(gridX, gridY, () => ObjectMapTile = MapTileCache.GetTile(gridX, gridY));
    }

    private void RebuildMapEntries()
    {
        var sim = Client.Network.CurrentSim;
        MapEntries.Clear();
        if (sim == null) return;
        foreach (var entry in Objects)
        {
            if (sim.ObjectsPrimitives.TryGetValue(entry.LocalId, out var prim))
            {
                MapEntries.Add(new ObjectMapEntry(
                    entry.Id, entry.Name,
                    prim.Position.X, prim.Position.Y, prim.Position.Z,
                    prim.Scale.X * 0.5f, prim.Scale.Y * 0.5f));
            }
        }
    }

    #endregion

    [RelayCommand]
    private void OpenGroupProfile()
    {
        if (ObjectGroupID != UUID.Zero)
            _instance.ShowGroupProfile(ObjectGroupID);
    }

    [RelayCommand]
    private void SaveTransform()
    {
        if (SelectedObject == null) return;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        uint localId = SelectedObject.LocalId;
        Client.Objects.SetPosition(sim, localId, new Vector3((float)PosX, (float)PosY, (float)PosZ));
        Client.Objects.SetRotation(sim, localId,
            Quaternion.CreateFromEulers((float)RotX * MathF.PI / 180f,
                                        (float)RotY * MathF.PI / 180f,
                                        (float)RotZ * MathF.PI / 180f));
        Client.Objects.SetScale(sim, localId, new Vector3((float)ScaleX, (float)ScaleY, (float)ScaleZ), false, false);
    }
}

public record ObjectEntry(
    UUID Id,
    uint LocalId,
    string Name,
    string Description,
    int Distance,
    string OwnerName,
    string CreatorName,
    UUID ObjectId,
    int PrimCount,
    UUID OwnerID = default,
    UUID CreatorID = default,
    SaleType SaleType = default,
    int SalePrice = 0,
    string HoverText = "",
    PermissionMask OwnerMask = PermissionMask.None,
    PermissionMask NextOwnerMask = PermissionMask.None,
    PrimFlags Flags = PrimFlags.None,
    AttachmentPoint AttachmentPoint = AttachmentPoint.Default,
    UUID GroupID = default,
    bool IsGroupOwned = false)
{
    public bool IsAttachment => AttachmentPoint != AttachmentPoint.Default && Distance == 0;

    private static string FormatAttachPoint(AttachmentPoint ap)
    {
        var raw = ap.ToString();
        var sb = new System.Text.StringBuilder(raw.Length + 8);
        foreach (var c in raw)
        {
            if (sb.Length > 0 && char.IsUpper(c)) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public string DisplayText => IsAttachment
        ? $"{Name} ({FormatAttachPoint(AttachmentPoint)})"
        : $"{Name} ({Distance}m)";
}

/// <summary>An object entry for the objects minimap — position and bounding-box half-extents.</summary>
public record ObjectMapEntry(UUID Id, string Name, float X, float Y, float Z, float HalfW, float HalfD)
{
    /// <summary>True when the object position falls within the current sim (0–256 on both axes).</summary>
    public bool IsInSim => X is >= 0 and <= 256 && Y is >= 0 and <= 256;
}
