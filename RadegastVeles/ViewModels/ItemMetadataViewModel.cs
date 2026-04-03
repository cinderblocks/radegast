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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class ItemMetadataViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly InventoryItem _item;

    [ObservableProperty] private string _creatorName = "Loading...";
    [ObservableProperty] private bool _isWorn;
    [ObservableProperty] private string _wornSlotText = string.Empty;

    public string TypeIcon => _item switch
    {
        InventoryNotecard    => "\U0001F4DD",
        InventoryLSL         => "\U0001F4DC",
        InventoryTexture     => "\U0001F5BC",
        InventorySnapshot    => "\U0001F4F7",
        InventoryObject      => "\U0001F4E6",
        InventoryAttachment  => "\U0001F4CE",   // 📎 attachment
        InventorySound       => "\U0001F50A",
        InventoryAnimation   => "\U0001F3AC",
        InventoryGesture     => "\U0001F44B",
        InventoryLandmark    => "\U0001F4CD",
        InventoryCallingCard => "\U0001F4C7",
        InventoryWearable    => "\U0001F457",
        InventoryMaterial    => "\U0001F48E",   // 💎 PBR material
        InventorySettings    => "\U0001F324",   // 🌤 environment settings
        _                    => "\U0001F4C4"
    };

    public string CreationDateText => _item.CreationDate > DateTime.MinValue
        ? _item.CreationDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : "Unknown";

    public string ItemIdText  => _item.UUID.ToString();
    public string AssetIdText => _item.AssetUUID.ToString();
    public bool   IsLink      => _item.IsLink();

    public string OwnerPermsText     => FormatPermMask(_item.Permissions.OwnerMask);
    public string NextOwnerPermsText => FormatPermMask(_item.Permissions.NextOwnerMask);
    public string EveryonePermsText  => FormatPermMask(_item.Permissions.EveryoneMask);

    // Individual restriction booleans for badge display
    public bool OwnerNoModify    => (_item.Permissions.OwnerMask     & PermissionMask.Modify)   == 0;
    public bool OwnerNoCopy      => (_item.Permissions.OwnerMask     & PermissionMask.Copy)     == 0;
    public bool OwnerNoTransfer  => (_item.Permissions.OwnerMask     & PermissionMask.Transfer) == 0;
    public bool NextNoModify     => (_item.Permissions.NextOwnerMask & PermissionMask.Modify)   == 0;
    public bool NextNoCopy       => (_item.Permissions.NextOwnerMask & PermissionMask.Copy)     == 0;
    public bool NextNoTransfer   => (_item.Permissions.NextOwnerMask & PermissionMask.Transfer) == 0;
    public bool EveryoneNoModify   => (_item.Permissions.EveryoneMask & PermissionMask.Modify)   == 0;
    public bool EveryoneNoCopy     => (_item.Permissions.EveryoneMask & PermissionMask.Copy)     == 0;
    public bool EveryoneNoTransfer => (_item.Permissions.EveryoneMask & PermissionMask.Transfer) == 0;

    public ItemMetadataViewModel(RadegastInstanceAvalonia instance, InventoryItem item)
    {
        _instance = instance;
        _item = item;
        _instance.Names.NameUpdated += Names_NameUpdated;
        _instance.Client.Appearance.AppearanceSet += Appearance_AppearanceSet;
        CreatorName = _instance.Names.Get(item.CreatorID);
        RefreshWornState();
    }

    private void Names_NameUpdated(object? sender, UUIDNameReplyEventArgs e)
    {
        if (!e.Names.TryGetValue(_item.CreatorID, out var name)) return;
        Dispatcher.UIThread.Post(() => CreatorName = name);
    }

    private void Appearance_AppearanceSet(object? sender, AppearanceSetEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshWornState);
    }

    private void RefreshWornState()
    {
        try
        {
            foreach (var w in _instance.Client.Appearance.GetWearables())
            {
                if (w.ItemID == _item.UUID || (_item.IsLink() && w.ItemID == _item.AssetUUID))
                {
                    IsWorn = true;
                    WornSlotText = w.WearableType.ToString();
                    return;
                }
            }

            foreach (var kvp in _instance.Client.Appearance.GetAttachmentsByItemId())
            {
                if (kvp.Key == _item.UUID || (_item.IsLink() && kvp.Key == _item.AssetUUID))
                {
                    IsWorn = true;
                    WornSlotText = kvp.Value.ToString();
                    return;
                }
            }
        }
        catch { /* appearance not yet available */ }

        IsWorn = false;
        WornSlotText = string.Empty;
    }

    [RelayCommand]
    private void OpenCreatorProfile()
    {
        _instance.ShowAgentProfile(CreatorName, _item.CreatorID);
    }

    private static string FormatPermMask(PermissionMask mask)
    {
        var parts = new List<string>(3);
        if ((mask & PermissionMask.Modify) != 0)   parts.Add("M");
        if ((mask & PermissionMask.Copy) != 0)     parts.Add("C");
        if ((mask & PermissionMask.Transfer) != 0) parts.Add("T");
        return parts.Count > 0 ? string.Join("", parts) : "\u2014";
    }

    public void Dispose()
    {
        _instance.Names.NameUpdated -= Names_NameUpdated;
        _instance.Client.Appearance.AppearanceSet -= Appearance_AppearanceSet;
    }
}
