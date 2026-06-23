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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class ItemPropertiesViewModel : ObservableObject
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly InventoryItem _item;
    private GridClient Client => _instance.Client;

    // ── Display-only fields ───────────────────────────────────────────────────

    public string ItemUUID    => _item.UUID.ToString();
    public string AssetUUID   => _item.AssetUUID.ToString();
    public string Creator     => _instance.Names.Get(_item.CreatorID);
    public string Owner       => _instance.Names.Get(_item.OwnerID);
    public string Acquired    => _item.CreationDate == DateTime.MinValue
                                  ? "Unknown"
                                  : _item.CreationDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string TypeName    => InventoryViewModel.GetTypeName(_item);

    // Permissions the current owner has
    public bool CanModify      => (_item.Permissions.OwnerMask & PermissionMask.Modify)   != 0;
    public bool CanCopy        => (_item.Permissions.OwnerMask & PermissionMask.Copy)     != 0;
    public bool CanTransfer    => (_item.Permissions.OwnerMask & PermissionMask.Transfer) != 0;

    // ── Editable fields ───────────────────────────────────────────────────────

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;

    // Next-owner permissions (editable only if owner has Modify)
    [ObservableProperty] private bool _nextOwnerModify;
    [ObservableProperty] private bool _nextOwnerCopy;
    [ObservableProperty] private bool _nextOwnerTransfer;

    [ObservableProperty] private bool _shareWithGroup;
    [ObservableProperty] private bool _everyoneCopy;

    public bool CanEditPerms => CanModify;

    public ItemPropertiesViewModel(RadegastInstanceAvalonia instance, InventoryItem item)
    {
        _instance = instance;
        _item     = item;

        Name        = item.Name        ?? string.Empty;
        Description = item.Description ?? string.Empty;

        NextOwnerModify   = (item.Permissions.NextOwnerMask & PermissionMask.Modify)   != 0;
        NextOwnerCopy     = (item.Permissions.NextOwnerMask & PermissionMask.Copy)     != 0;
        NextOwnerTransfer = (item.Permissions.NextOwnerMask & PermissionMask.Transfer) != 0;

        ShareWithGroup = (item.Permissions.GroupMask    & PermissionMask.Modify) != 0;
        EveryoneCopy   = (item.Permissions.EveryoneMask & PermissionMask.Copy)   != 0;
    }

    [RelayCommand]
    private void OpenCreatorProfile()
    {
        var name = _instance.Names.Get(_item.CreatorID);
        _instance.ShowAgentProfile(name, _item.CreatorID);
    }

    [RelayCommand]
    private void Save()
    {
        _item.Name        = Name.Trim();
        _item.Description = Description.Trim();

        if (CanModify)
        {
            var next = PermissionMask.Move;
            if (NextOwnerModify)   next |= PermissionMask.Modify;
            if (NextOwnerCopy)     next |= PermissionMask.Copy;
            if (NextOwnerTransfer) next |= PermissionMask.Transfer;
            _item.Permissions = new Permissions(
                (uint)_item.Permissions.BaseMask,
                (uint)_item.Permissions.EveryoneMask,
                (uint)_item.Permissions.GroupMask,
                (uint)next,
                (uint)_item.Permissions.OwnerMask);
        }

        // RequestUpdateItem sends name, description, and permissions in one message
        Client.Inventory.RequestUpdateItem(_item);
    }
}
