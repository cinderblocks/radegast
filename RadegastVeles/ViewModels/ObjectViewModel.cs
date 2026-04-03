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
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class ObjectViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryItem _item;
    private bool _disposed;

    public ItemMetadataViewModel Metadata { get; }

    // ── Core identity ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _objectName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _hasDescription;
    [ObservableProperty] private string _itemTypeName = string.Empty;

    // ── Attach point ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _attachPointText = string.Empty;
    [ObservableProperty] private string _attachZoneText = string.Empty;   // "HUD" or "Body"
    [ObservableProperty] private bool _isHudObject;

    // ── Attachment state ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _isAttached;
    [ObservableProperty] private string _currentAttachPointText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;

    // ── Permissions — badge booleans (true = restriction / badge shown) ────────
    public bool OwnerNoModify      => (_item.Permissions.OwnerMask     & PermissionMask.Modify)   == 0;
    public bool OwnerNoCopy        => (_item.Permissions.OwnerMask     & PermissionMask.Copy)     == 0;
    public bool OwnerNoTransfer    => (_item.Permissions.OwnerMask     & PermissionMask.Transfer) == 0;
    public bool NextNoModify       => (_item.Permissions.NextOwnerMask & PermissionMask.Modify)   == 0;
    public bool NextNoCopy         => (_item.Permissions.NextOwnerMask & PermissionMask.Copy)     == 0;
    public bool NextNoTransfer     => (_item.Permissions.NextOwnerMask & PermissionMask.Transfer) == 0;
    public bool GroupNoModify      => (_item.Permissions.GroupMask     & PermissionMask.Modify)   == 0;
    public bool GroupNoCopy        => (_item.Permissions.GroupMask     & PermissionMask.Copy)     == 0;
    public bool GroupNoTransfer    => (_item.Permissions.GroupMask     & PermissionMask.Transfer) == 0;
    public bool EveryoneNoModify   => (_item.Permissions.EveryoneMask  & PermissionMask.Modify)   == 0;
    public bool EveryoneNoCopy     => (_item.Permissions.EveryoneMask  & PermissionMask.Copy)     == 0;
    public bool EveryoneNoTransfer => (_item.Permissions.EveryoneMask  & PermissionMask.Transfer) == 0;

    // ── Object flags ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isCoalesced;
    [ObservableProperty] private bool _hasSlamPerm;
    [ObservableProperty] private bool _hasSlamSale;

    // ── Sale info ─────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isForSale;
    [ObservableProperty] private string _saleText = string.Empty;

    // ── Group ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isGroupOwned;
    [ObservableProperty] private string _lastOwnerName = string.Empty;

    public ObjectViewModel(RadegastInstanceAvalonia instance, InventoryItem item)
    {
        _instance = instance;
        _item = item;
        Metadata = new ItemMetadataViewModel(instance, item);

        ObjectName = item.Name;

        Description = item.Description?.Trim() ?? string.Empty;
        HasDescription = !string.IsNullOrEmpty(Description);

        ItemTypeName = item is InventoryAttachment ? "Attachment" : "Object";

        // Attach point
        var rawPoint = item switch
        {
            InventoryObject obj  => obj.AttachPoint,
            InventoryAttachment a => a.AttachmentPoint,
            _                    => AttachmentPoint.Default
        };
        AttachPointText = rawPoint == AttachmentPoint.Default
            ? "Default (Right Hand)"
            : FormatAttachPoint(rawPoint.ToString());
        IsHudObject = IsHudPoint(rawPoint);
        AttachZoneText = IsHudObject ? "HUD" : "Body";

        // Object flags (InventoryObject only)
        if (item is InventoryObject obj2)
        {
            var flags = obj2.ItemFlags;
            IsCoalesced  = flags.HasFlag(InventoryItemFlags.ObjectHasMultipleItems);
            HasSlamPerm  = flags.HasFlag(InventoryItemFlags.ObjectSlamPerm);
            HasSlamSale  = flags.HasFlag(InventoryItemFlags.ObjectSlamSale);
        }

        // Sale info
        IsForSale = item.SaleType != SaleType.Not;
        if (IsForSale)
        {
            var saleTypeText = item.SaleType switch
            {
                SaleType.Original => "Original",
                SaleType.Copy     => "Copy",
                SaleType.Contents => "Contents",
                _                 => item.SaleType.ToString()
            };
            SaleText = $"L${item.SalePrice:N0} ({saleTypeText})";
        }

        IsGroupOwned = item.GroupOwned;

        // Last owner name
        LastOwnerName = _instance.Names.Get(item.LastOwnerID);

        _instance.Names.NameUpdated += Names_NameUpdated;
        Client.Appearance.AppearanceSet += Appearance_AppearanceSet;
        RefreshAttachedState();
    }

    private void Names_NameUpdated(object? sender, UUIDNameReplyEventArgs e)
    {
        if (e.Names.TryGetValue(_item.LastOwnerID, out var name))
            Dispatcher.UIThread.Post(() => LastOwnerName = name);
    }

    private void Appearance_AppearanceSet(object? sender, AppearanceSetEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshAttachedState);
    }

    private void RefreshAttachedState()
    {
        var attachments = Client.Appearance.GetAttachmentsByItemId();
        if (attachments.TryGetValue(_item.UUID, out var point))
        {
            IsAttached = true;
            CurrentAttachPointText = FormatAttachPoint(point.ToString());
            StatusText = $"Attached at {CurrentAttachPointText}.";
        }
        else
        {
            IsAttached = false;
            CurrentAttachPointText = string.Empty;
            StatusText = "Not currently attached.";
        }
    }

    [RelayCommand]
    private void Attach()
    {
        if (_instance.COF != null)
        {
            _ = _instance.COF.AddToOutfit(new List<InventoryItem> { _item }, true, CancellationToken.None);
            StatusText = "Attaching…";
        }
        else
        {
            StatusText = "Outfit manager not available.";
        }
    }

    [RelayCommand]
    private void Detach()
    {
        if (_instance.COF != null)
        {
            _ = _instance.COF.RemoveFromOutfit(_item, CancellationToken.None);
            StatusText = "Detaching…";
        }
        else
        {
            StatusText = "Outfit manager not available.";
        }
    }

    private static bool IsHudPoint(AttachmentPoint p) => p is
        AttachmentPoint.HUDCenter2 or AttachmentPoint.HUDTopRight or
        AttachmentPoint.HUDTop     or AttachmentPoint.HUDTopLeft  or
        AttachmentPoint.HUDCenter  or AttachmentPoint.HUDBottomLeft or
        AttachmentPoint.HUDBottom  or AttachmentPoint.HUDBottomRight;

    private static string FormatAttachPoint(string raw) =>
        Regex.Replace(raw, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _instance.Names.NameUpdated -= Names_NameUpdated;
        Client.Appearance.AppearanceSet -= Appearance_AppearanceSet;
        Metadata.Dispose();
    }
}

