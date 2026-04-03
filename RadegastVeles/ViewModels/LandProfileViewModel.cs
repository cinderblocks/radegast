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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class LandProfileViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    // Cached parcel and simulator for edits/actions
    private Parcel? _parcel;
    private Simulator? _parcelSim;

    // --- Display properties ---
    [ObservableProperty] private string _parcelName = string.Empty;
    [ObservableProperty] private string _parcelDescription = string.Empty;
    [ObservableProperty] private string _ownerName = string.Empty;
    [ObservableProperty] private UUID _ownerID = UUID.Zero;
    [ObservableProperty] private string _groupName = string.Empty;
    [ObservableProperty] private string _regionName = string.Empty;
    [ObservableProperty] private string _regionType = string.Empty;
    [ObservableProperty] private string _traffic = "0";
    [ObservableProperty] private string _area = "0";
    [ObservableProperty] private string _simPrims = string.Empty;
    [ObservableProperty] private string _parcelPrims = string.Empty;
    [ObservableProperty] private string _autoReturn = string.Empty;
    [ObservableProperty] private string _maturityRating = string.Empty;
    [ObservableProperty] private bool _isGroupOwned;
    [ObservableProperty] private Bitmap? _snapshotImage;
    [ObservableProperty] private bool _isLoading = true;

    // Display flags (read-only view)
    [ObservableProperty] private bool _allowFly;
    [ObservableProperty] private bool _allowScripts;
    [ObservableProperty] private bool _allowBuild;
    [ObservableProperty] private bool _allowLandmark;
    [ObservableProperty] private bool _allowVoice;

    // For-sale display
    [ObservableProperty] private bool _isForSale;
    [ObservableProperty] private string _salePriceDisplay = string.Empty;

    // --- Ownership / edit permissions ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    [NotifyPropertyChangedFor(nameof(IsEditingOwnParcel))]
    [NotifyPropertyChangedFor(nameof(ShowSalePriceEdit))]
    [NotifyPropertyChangedFor(nameof(ShowAuthBuyerEdit))]
    [NotifyPropertyChangedFor(nameof(ShowBuyButton))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingOwnParcel))]
    [NotifyPropertyChangedFor(nameof(ShowBuyButton))]
    private bool _isOwnParcel;

    public bool IsNotEditing => !IsEditing;
    public bool IsEditingOwnParcel => IsEditing && IsOwnParcel;

    // --- Edit-mode fields ---
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private bool _editAllowFly;
    [ObservableProperty] private bool _editAllowScripts;
    [ObservableProperty] private bool _editAllowBuild;
    [ObservableProperty] private bool _editAllowLandmark;
    [ObservableProperty] private bool _editAllowVoice;
    [ObservableProperty] private bool _editAllowDamage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSalePriceEdit))]
    [NotifyPropertyChangedFor(nameof(ShowAuthBuyerEdit))]
    private bool _editForSale;

    [ObservableProperty] private decimal _editSalePrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAuthBuyerEdit))]
    private bool _editSellToAnyone = true;

    [ObservableProperty] private string _editAuthBuyerIdText = string.Empty;

    public bool ShowSalePriceEdit => IsEditing && IsOwnParcel && EditForSale;
    public bool ShowAuthBuyerEdit => ShowSalePriceEdit && !EditSellToAnyone;
    public bool ShowBuyButton => IsForSale && !IsOwnParcel && !IsEditing;

    // --- Groups for deed-to-group ---
    public ObservableCollection<GroupEntry> AgentGroups { get; } = [];
    [ObservableProperty] private GroupEntry? _selectedGroupForDeed;
    [ObservableProperty] private bool _hasAgentGroups;

    private UUID _parcelGroupId = UUID.Zero;

    public RadegastInstanceAvalonia Instance => _instance;

    public LandProfileViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        Client.Parcels.ParcelProperties += Parcels_ParcelProperties;
        Client.Parcels.ParcelDwellReply += Parcels_ParcelDwellReply;
        Client.Groups.GroupNamesReply += Groups_GroupNamesReply;
        Client.Groups.CurrentGroups += Groups_CurrentGroups;

        LoadCurrentParcel();
    }

    public void Dispose()
    {
        Client.Parcels.ParcelProperties -= Parcels_ParcelProperties;
        Client.Parcels.ParcelDwellReply -= Parcels_ParcelDwellReply;
        Client.Groups.GroupNamesReply -= Groups_GroupNamesReply;
        Client.Groups.CurrentGroups -= Groups_CurrentGroups;
    }

    private void LoadCurrentParcel()
    {
        var parcel = _instance.State.Parcel;
        if (parcel != null)
        {
            PopulateFromParcel(parcel);
            if (Client.Network.CurrentSim != null)
                Client.Parcels.RequestDwell(Client.Network.CurrentSim, parcel.LocalID);
        }

        if (Client.Network.CurrentSim != null)
        {
            var pos = Client.Self.SimPosition;
            Client.Parcels.RequestParcelProperties(Client.Network.CurrentSim,
                pos.Y + 0.5f, pos.X + 0.5f,
                pos.Y - 0.5f, pos.X - 0.5f,
                0, false);
        }
        else if (parcel == null)
        {
            ParcelName = "Unknown";
            IsLoading = false;
        }
    }

    private void PopulateFromParcel(Parcel p)
    {
        _parcel = p;
        _parcelSim = Client.Network.CurrentSim;

        ParcelName = p.Name ?? "Unnamed Parcel";
        ParcelDescription = p.Desc ?? string.Empty;
        Area = p.Area.ToString();
        Traffic = $"{p.Dwell:0}";
        SimPrims = $"{p.SimWideTotalPrims} / {p.SimWideMaxPrims}";
        ParcelPrims = $"{p.TotalPrims} / {p.MaxPrims}";
        AutoReturn = p.OtherCleanTime > 0 ? $"{p.OtherCleanTime} min" : "Off";
        IsGroupOwned = p.IsGroupOwned;

        MaturityRating = p.Category switch
        {
            ParcelCategory.Adult => "Adult",
            _ => Client.Network.CurrentSim?.Access switch
            {
                SimAccess.Mature => "Moderate",
                SimAccess.Adult => "Adult",
                SimAccess.PG => "General",
                _ => "Unknown"
            }
        };

        AllowFly = (p.Flags & ParcelFlags.AllowFly) != 0;
        AllowScripts = (p.Flags & ParcelFlags.AllowOtherScripts) != 0;
        AllowBuild = (p.Flags & ParcelFlags.CreateObjects) != 0;
        AllowLandmark = (p.Flags & ParcelFlags.AllowLandmark) != 0;
        AllowVoice = (p.Flags & ParcelFlags.AllowVoiceChat) != 0;

        IsForSale = (p.Flags & ParcelFlags.ForSale) != 0;
        SalePriceDisplay = IsForSale ? $"L${p.SalePrice:N0}" : string.Empty;
        OnPropertyChanged(nameof(ShowBuyButton));

        if (p.IsGroupOwned)
        {
            OwnerName = "(Group owned)";
            OwnerID = UUID.Zero;
        }
        else if (p.OwnerID != UUID.Zero)
        {
            OwnerID = p.OwnerID;
            OwnerName = _instance.Names.Get(p.OwnerID);
        }

        IsOwnParcel = !p.IsGroupOwned && p.OwnerID == Client.Self.AgentID;

        if (p.GroupID != UUID.Zero)
        {
            _parcelGroupId = p.GroupID;
            Client.Groups.RequestGroupName(p.GroupID);
        }

        if (Client.Network.CurrentSim != null)
        {
            RegionName = Client.Network.CurrentSim.Name;
            RegionType = Client.Network.CurrentSim.ProductName ?? string.Empty;
        }

        if (p.SnapshotID != UUID.Zero)
            GridTextureHelper.Download(Client, p.SnapshotID, img => SnapshotImage = img);

        IsLoading = false;
        IsEditing = false;
    }

    // --- Edit commands ---

    [RelayCommand]
    private void BeginEdit()
    {
        if (_parcel == null || !IsOwnParcel) return;

        EditName = _parcel.Name ?? string.Empty;
        EditDescription = _parcel.Desc ?? string.Empty;
        EditAllowFly = (_parcel.Flags & ParcelFlags.AllowFly) != 0;
        EditAllowScripts = (_parcel.Flags & ParcelFlags.AllowOtherScripts) != 0;
        EditAllowBuild = (_parcel.Flags & ParcelFlags.CreateObjects) != 0;
        EditAllowLandmark = (_parcel.Flags & ParcelFlags.AllowLandmark) != 0;
        EditAllowVoice = (_parcel.Flags & ParcelFlags.AllowVoiceChat) != 0;
        EditAllowDamage = (_parcel.Flags & ParcelFlags.AllowDamage) != 0;
        EditForSale = (_parcel.Flags & ParcelFlags.ForSale) != 0;
        EditSalePrice = _parcel.SalePrice;
        EditSellToAnyone = _parcel.AuthBuyerID == UUID.Zero;
        EditAuthBuyerIdText = _parcel.AuthBuyerID != UUID.Zero ? _parcel.AuthBuyerID.ToString() : string.Empty;

        // Populate groups for deed
        Client.Groups.RequestCurrentGroups();

        IsEditing = true;
    }

    [RelayCommand]
    private void DiscardEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void SaveParcel()
    {
        if (_parcel == null || _parcelSim == null || !IsOwnParcel) return;

        _parcel.Name = EditName;
        _parcel.Desc = EditDescription;

        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowFly, EditAllowFly);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowOtherScripts, EditAllowScripts);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.CreateObjects, EditAllowBuild);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowLandmark, EditAllowLandmark);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowVoiceChat, EditAllowVoice);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowDamage, EditAllowDamage);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.ForSale, EditForSale);

        if (EditForSale)
        {
            _parcel.SalePrice = (int)EditSalePrice;
            if (!EditSellToAnyone && UUID.TryParse(EditAuthBuyerIdText, out var authBuyer))
                _parcel.AuthBuyerID = authBuyer;
            else
                _parcel.AuthBuyerID = UUID.Zero;
        }
        else
        {
            _parcel.SalePrice = 0;
            _parcel.AuthBuyerID = UUID.Zero;
        }

        _parcel.Update(Client, _parcelSim, true);

        // Refresh display from updated parcel
        PopulateFromParcel(_parcel);
    }

    [RelayCommand]
    private void BuyLand()
    {
        if (_parcel == null || _parcelSim == null || !IsForSale || IsOwnParcel) return;
        Client.Parcels.Buy(_parcelSim, _parcel.LocalID,
            forGroup: false, groupID: UUID.Zero,
            removeContribution: false,
            parcelArea: _parcel.Area,
            parcelPrice: _parcel.SalePrice);
    }

    [RelayCommand]
    private void DeedToGroup()
    {
        if (_parcel == null || _parcelSim == null || !IsOwnParcel) return;
        if (SelectedGroupForDeed == null) return;
        Client.Parcels.DeedToGroup(_parcelSim, _parcel.LocalID, SelectedGroupForDeed.Id);
    }

    [RelayCommand]
    private void AbandonParcel()
    {
        if (_parcel == null || _parcelSim == null || !IsOwnParcel) return;
        Client.Parcels.ReleaseParcel(_parcelSim, _parcel.LocalID);
    }

    // --- Event handlers ---

    private void Parcels_ParcelProperties(object? sender, ParcelPropertiesEventArgs e)
    {
        if (e.Result != ParcelResult.Single) return;
        Dispatcher.UIThread.Post(() => PopulateFromParcel(e.Parcel));
    }

    private void Parcels_ParcelDwellReply(object? sender, ParcelDwellReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() => Traffic = $"{e.Dwell:0}");
    }

    private void Groups_GroupNamesReply(object? sender, GroupNamesEventArgs e)
    {
        if (!e.GroupNames.TryGetValue(_parcelGroupId, out var name)) return;
        Dispatcher.UIThread.Post(() => GroupName = name);
    }

    private void Groups_CurrentGroups(object? sender, CurrentGroupsEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AgentGroups.Clear();
            foreach (var kvp in e.Groups.OrderBy(g => g.Value.Name))
                AgentGroups.Add(new GroupEntry(kvp.Key, kvp.Value.Name, kvp.Key == Client.Self.ActiveGroup));
            HasAgentGroups = AgentGroups.Count > 0;
            if (SelectedGroupForDeed == null && AgentGroups.Count > 0)
                SelectedGroupForDeed = AgentGroups[0];
        });
    }

    // --- Profile navigation commands ---

    [RelayCommand]
    private void ShowOwnerProfile()
    {
        if (_parcel == null || _parcel.OwnerID == UUID.Zero || _parcel.IsGroupOwned) return;
        _instance.ShowAgentProfile(OwnerName, _parcel.OwnerID);
    }

    [RelayCommand]
    private void ShowGroupProfile()
    {
        if (_parcelGroupId == UUID.Zero) return;
        _instance.ShowGroupProfile(_parcelGroupId);
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadCurrentParcel();
    }

    private static ParcelFlags SetFlag(ParcelFlags flags, ParcelFlags flag, bool value)
        => value ? flags | flag : flags & ~flag;
}
