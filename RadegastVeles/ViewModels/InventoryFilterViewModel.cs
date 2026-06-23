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

namespace Radegast.Veles.ViewModels;

public partial class InventoryFilterViewModel : ObservableObject
{
    // Type visibility
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showTextures = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showSnapshots = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showScripts = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showNotecards = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showSounds = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showObjects = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showAnimations = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showGestures = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showWearables = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showLandmarks = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _showCallingCards = true;

    // Date range
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _filterByDate;
    [ObservableProperty] private DateTime? _dateFrom;
    [ObservableProperty] private DateTime? _dateTo;

    // Creator
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _filterByCreator;
    [ObservableProperty] private string _creatorFilter = string.Empty;

    // Permissions
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _filterByPermissions;
    [ObservableProperty] private bool _mustHaveModify;
    [ObservableProperty] private bool _mustHaveCopy;
    [ObservableProperty] private bool _mustHaveTransfer;

    // Links — mutually exclusive
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _linksOnly;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsActive))] private bool _hideLinks;

    partial void OnLinksOnlyChanged(bool value)  { if (value) HideLinks  = false; }
    partial void OnHideLinksChanged(bool value)  { if (value) LinksOnly  = false; }

    public bool IsActive =>
        FilterByDate || FilterByCreator || FilterByPermissions || LinksOnly || HideLinks ||
        !ShowTextures || !ShowSnapshots || !ShowScripts || !ShowNotecards || !ShowSounds ||
        !ShowObjects || !ShowAnimations || !ShowGestures || !ShowWearables ||
        !ShowLandmarks || !ShowCallingCards;

    /// <summary>
    /// Returns true if <paramref name="item"/> passes all active filter criteria.
    /// </summary>
    public bool Matches(InventoryBase item, Func<UUID, string>? nameResolver = null)
    {
        if (item is InventoryFolder) return false;

        bool typeOk = item switch
        {
            InventoryTexture     => ShowTextures,
            InventorySnapshot    => ShowSnapshots,
            InventoryLSL         => ShowScripts,
            InventoryNotecard    => ShowNotecards,
            InventorySound       => ShowSounds,
            InventoryObject      => ShowObjects,
            InventoryAnimation   => ShowAnimations,
            InventoryGesture     => ShowGestures,
            InventoryWearable    => ShowWearables,
            InventoryLandmark    => ShowLandmarks,
            InventoryCallingCard => ShowCallingCards,
            _                    => true
        };
        if (!typeOk) return false;

        if (item is not InventoryItem invItem) return true;

        bool isLink = invItem.IsLink();
        if (LinksOnly && !isLink) return false;
        if (HideLinks && isLink) return false;

        if (FilterByDate)
        {
            var created = invItem.CreationDate;
            if (DateFrom.HasValue && created < DateFrom.Value) return false;
            if (DateTo.HasValue   && created > DateTo.Value.AddDays(1)) return false;
        }

        if (FilterByCreator && !string.IsNullOrWhiteSpace(CreatorFilter))
        {
            string idStr   = invItem.CreatorID.ToString();
            string name    = nameResolver?.Invoke(invItem.CreatorID) ?? idStr;
            if (!idStr.Contains(CreatorFilter, StringComparison.OrdinalIgnoreCase) &&
                !name.Contains(CreatorFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (FilterByPermissions)
        {
            if (MustHaveModify   && (invItem.Permissions.OwnerMask & PermissionMask.Modify)   == 0) return false;
            if (MustHaveCopy     && (invItem.Permissions.OwnerMask & PermissionMask.Copy)     == 0) return false;
            if (MustHaveTransfer && (invItem.Permissions.OwnerMask & PermissionMask.Transfer) == 0) return false;
        }

        return true;
    }

    public event EventHandler? FilterApplied;
    public event EventHandler? FilterCleared;

    [RelayCommand]
    private void Apply() => FilterApplied?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Clear()
    {
        ShowTextures = ShowSnapshots = ShowScripts = ShowNotecards = ShowSounds = true;
        ShowObjects = ShowAnimations = ShowGestures = ShowWearables = ShowLandmarks = ShowCallingCards = true;
        FilterByDate = false; DateFrom = null; DateTo = null;
        FilterByCreator = false; CreatorFilter = string.Empty;
        FilterByPermissions = false; MustHaveModify = MustHaveCopy = MustHaveTransfer = false;
        LinksOnly = HideLinks = false;
        FilterCleared?.Invoke(this, EventArgs.Empty);
    }
}
