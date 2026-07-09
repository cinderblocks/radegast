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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.Packets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class LandProfileViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    // Cached parcel and simulator for edits/actions
    private Parcel? _parcel;
    private Simulator? _parcelSim;
    private Dictionary<UUID, Group> _cachedGroups = [];

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMusicUrl))]
    private string _musicUrl = string.Empty;

    public bool HasMusicUrl => !string.IsNullOrEmpty(MusicUrl);

    // Display flags (read-only view)
    [ObservableProperty] private bool _allowFly;
    [ObservableProperty] private bool _allowScripts;
    [ObservableProperty] private bool _allowBuild;
    [ObservableProperty] private bool _allowLandmark;
    [ObservableProperty] private bool _allowVoice;
    [ObservableProperty] private bool _allowDamage;
    [ObservableProperty] private bool _allowGroupScripts;
    [ObservableProperty] private bool _showInSearch;
    [ObservableProperty] private bool _soundLocal;
    [ObservableProperty] private bool _restrictPushObject;
    [ObservableProperty] private bool _denyAnonymous;
    [ObservableProperty] private bool _denyAgeUnverified;
    [ObservableProperty] private bool _restrictToGroup;
    [ObservableProperty] private bool _restrictToList;
    [ObservableProperty] private bool _banListEnabled;
    [ObservableProperty] private bool _passEnabled;
    [ObservableProperty] private string _passDetails = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(ShowPassSettings))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingOwnParcel))]
    [NotifyPropertyChangedFor(nameof(ShowBuyButton))]
    [NotifyPropertyChangedFor(nameof(CanStartAuction))]
    private bool _isOwnParcel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    [NotifyPropertyChangedFor(nameof(ShowBuyButton))]
    private bool _canEditParcel;

    // --- Auction ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartAuction))]
    private bool _isEstateManager;

    [ObservableProperty] private decimal _auctionStartingBid = 1;
    [ObservableProperty] private string _auctionStatus = string.Empty;

    public bool CanStartAuction =>
        (IsEstateManager || IsOwnParcel)
        && (_instance.Client.Network.AccountLevelBenefits?.LandAuctionsAllowed ?? -1) > 0;

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
    [ObservableProperty] private bool _editAllowGroupScripts;
    [ObservableProperty] private bool _editShowInSearch;
    [ObservableProperty] private bool _editSoundLocal;
    [ObservableProperty] private bool _editRestrictPushObject;
    [ObservableProperty] private bool _editDenyAnonymous;
    [ObservableProperty] private bool _editDenyAgeUnverified;
    [ObservableProperty] private decimal _editAutoReturn;
    [ObservableProperty] private string _editMusicUrl = string.Empty;
    [ObservableProperty] private string _editMediaUrl = string.Empty;
    [ObservableProperty] private ParcelCategory _editCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSalePriceEdit))]
    [NotifyPropertyChangedFor(nameof(ShowAuthBuyerEdit))]
    private bool _editForSale;

    [ObservableProperty] private decimal _editSalePrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAuthBuyerEdit))]
    private bool _editSellToAnyone = true;

    [ObservableProperty] private string _editAuthBuyerIdText = string.Empty;

    // --- Access tab edit fields ---
    [ObservableProperty] private bool _editRestrictToGroup;
    [ObservableProperty] private bool _editRestrictToList;
    [ObservableProperty] private bool _editBanListEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPassSettings))]
    private bool _editPassEnabled;

    [ObservableProperty] private decimal _editPassPrice;
    [ObservableProperty] private decimal _editPassHours;

    public bool ShowSalePriceEdit => IsEditing && IsOwnParcel && EditForSale;
    public bool ShowAuthBuyerEdit => ShowSalePriceEdit && !EditSellToAnyone;
    public bool ShowBuyButton => IsForSale && !IsOwnParcel && !IsEditing;
    public bool ShowPassSettings => IsEditing && EditPassEnabled;

    public static IReadOnlyList<ParcelCategory> CategoryOptions { get; } =
    [
        ParcelCategory.None, ParcelCategory.Linden, ParcelCategory.Adult, ParcelCategory.Arts,
        ParcelCategory.Business, ParcelCategory.Educational, ParcelCategory.Gaming,
        ParcelCategory.Hangout, ParcelCategory.Newcomer, ParcelCategory.Park,
        ParcelCategory.Residential, ParcelCategory.Shopping, ParcelCategory.Stage, ParcelCategory.Other
    ];

    // --- Groups for deed-to-group ---
    public ObservableCollection<GroupEntry> AgentGroups { get; } = [];
    [ObservableProperty] private GroupEntry? _selectedGroupForDeed;
    [ObservableProperty] private bool _hasAgentGroups;

    private UUID _parcelGroupId = UUID.Zero;

    // --- Access lists ---
    public ObservableCollection<AccessEntryViewModel> WhiteList { get; } = [];
    public ObservableCollection<AccessEntryViewModel> BanList { get; } = [];
    [ObservableProperty] private bool _isAccessListLoading = true;
    [ObservableProperty] private AccessEntryViewModel? _selectedWhiteListEntry;
    [ObservableProperty] private AccessEntryViewModel? _selectedBanListEntry;

    // --- Parcel objects (Objects tab) ---
    public ObservableCollection<ParcelPrimOwnerEntry> PrimOwners { get; } = [];
    [ObservableProperty] private bool _isObjectsLoading = true;
    [ObservableProperty] private ParcelPrimOwnerEntry? _selectedPrimOwner;

    // --- Experiences (region-scoped) ---
    public ObservableCollection<ExperienceEntryViewModel> TrustedExperiences { get; } = [];
    public ObservableCollection<ExperienceEntryViewModel> BlockedExperiences { get; } = [];
    public ObservableCollection<ExperienceEntryViewModel> ContribExperiences { get; } = [];
    [ObservableProperty] private bool _isExperienceLoading = true;

    // --- Parcel environment (EEP / legacy WindLight) ---
    [ObservableProperty] private bool _envIsCustom;
    [ObservableProperty] private string _envDayLength = string.Empty;
    [ObservableProperty] private string _envDayOffset = string.Empty;
    [ObservableProperty] private bool _envHasLegacyWindLight;
    [ObservableProperty] private bool _isEnvironmentLoading = true;

    public RadegastInstanceAvalonia Instance => _instance;

    public LandProfileViewModel(RadegastInstanceAvalonia instance, bool loadCurrentParcel = true)
    {
        _instance = instance;

        Client.Parcels.ParcelProperties += Parcels_ParcelProperties;
        Client.Parcels.ParcelDwellReply += Parcels_ParcelDwellReply;
        Client.Parcels.ParcelAccessListReply += Parcels_ParcelAccessListReply;
        Client.Parcels.ParcelObjectOwnersReply += Parcels_ParcelObjectOwnersReply;
        Client.Groups.GroupNamesReply += Groups_GroupNamesReply;
        Client.Groups.CurrentGroups += Groups_CurrentGroups;
        Client.Self.RegionExperiencesUpdated += Self_RegionExperiencesUpdated;

        if (loadCurrentParcel)
            LoadCurrentParcel();
    }

    /// <summary>
    /// Requests parcel info at the given region-local coordinates (0–256) on the current sim.
    /// </summary>
    public void LoadParcelAtPosition(float rx, float ry)
    {
        if (Client.Network.CurrentSim == null) return;
        IsLoading = true;
        Client.Parcels.RequestParcelProperties(Client.Network.CurrentSim,
            ry + 0.5f, rx + 0.5f,
            ry - 0.5f, rx - 0.5f,
            0, false);
    }

    public void Dispose()
    {
        Client.Parcels.ParcelProperties -= Parcels_ParcelProperties;
        Client.Parcels.ParcelDwellReply -= Parcels_ParcelDwellReply;
        Client.Parcels.ParcelAccessListReply -= Parcels_ParcelAccessListReply;
        Client.Parcels.ParcelObjectOwnersReply -= Parcels_ParcelObjectOwnersReply;
        Client.Groups.GroupNamesReply -= Groups_GroupNamesReply;
        Client.Groups.CurrentGroups -= Groups_CurrentGroups;
        Client.Self.RegionExperiencesUpdated -= Self_RegionExperiencesUpdated;
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
        AllowDamage = (p.Flags & ParcelFlags.AllowDamage) != 0;
        AllowGroupScripts = (p.Flags & ParcelFlags.AllowGroupScripts) != 0;
        ShowInSearch = (p.Flags & ParcelFlags.ShowDirectory) != 0;
        SoundLocal = (p.Flags & ParcelFlags.SoundLocal) != 0;
        RestrictPushObject = (p.Flags & ParcelFlags.RestrictPushObject) != 0;
        DenyAnonymous = (p.Flags & ParcelFlags.DenyAnonymous) != 0;
        DenyAgeUnverified = (p.Flags & ParcelFlags.DenyAgeUnverified) != 0;
        RestrictToGroup = (p.Flags & ParcelFlags.UseAccessGroup) != 0;
        RestrictToList = (p.Flags & ParcelFlags.UseAccessList) != 0;
        BanListEnabled = (p.Flags & ParcelFlags.UseBanList) != 0;
        PassEnabled = (p.Flags & ParcelFlags.UsePassList) != 0;
        PassDetails = PassEnabled ? $"L${p.PassPrice:N0} / {p.PassHours:0.#}h" : string.Empty;

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

        var isEstateManager = Client.Network.CurrentSim?.IsEstateManager ?? false;
        IsEstateManager = isEstateManager;
        var isGroupManager = p.IsGroupOwned && p.GroupID != UUID.Zero
            && _cachedGroups.TryGetValue(p.GroupID, out var grpPerms)
            && grpPerms.Powers.HasFlag(GroupPowers.LandOptions);
        CanEditParcel = IsOwnParcel || isEstateManager || isGroupManager;

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

        MusicUrl = p.MusicURL ?? string.Empty;

        // Request access list for the newly loaded parcel
        IsAccessListLoading = true;
        WhiteList.Clear();
        BanList.Clear();
        if (_parcelSim != null)
            Client.Parcels.RequestParcelAccessList(_parcelSim, p.LocalID, AccessList.Both, 0);

        // Request prim object owners for the Objects tab
        IsObjectsLoading = true;
        PrimOwners.Clear();
        if (_parcelSim != null)
            Client.Parcels.RequestObjectOwners(_parcelSim, p.LocalID);

        // Fetch parcel EEP environment and legacy WindLight
        IsEnvironmentLoading = true;
        _ = FetchEnvironmentAsync(p.LocalID);

        // Fetch region experiences (region-scoped)
        IsExperienceLoading = true;
        _ = Client.Self.GetRegionExperiencesAsync();
    }

    // --- Edit commands ---

    [RelayCommand(CanExecute = nameof(CanStartAuction))]
    private void StartAuction()
    {
        if (_parcel == null || _parcelSim == null) return;

        AuctionStatus = "Starting auction\u2026";
        Client.Parcels.StartAuction(_parcelSim, _parcel.LocalID, _parcel.SnapshotID);
        AuctionStatus = "Auction request sent.";
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (_parcel == null || !CanEditParcel) return;

        EditName = _parcel.Name ?? string.Empty;
        EditDescription = _parcel.Desc ?? string.Empty;
        EditAllowFly = (_parcel.Flags & ParcelFlags.AllowFly) != 0;
        EditAllowScripts = (_parcel.Flags & ParcelFlags.AllowOtherScripts) != 0;
        EditAllowBuild = (_parcel.Flags & ParcelFlags.CreateObjects) != 0;
        EditAllowLandmark = (_parcel.Flags & ParcelFlags.AllowLandmark) != 0;
        EditAllowVoice = (_parcel.Flags & ParcelFlags.AllowVoiceChat) != 0;
        EditAllowDamage = (_parcel.Flags & ParcelFlags.AllowDamage) != 0;
        EditAllowGroupScripts = (_parcel.Flags & ParcelFlags.AllowGroupScripts) != 0;
        EditShowInSearch = (_parcel.Flags & ParcelFlags.ShowDirectory) != 0;
        EditSoundLocal = (_parcel.Flags & ParcelFlags.SoundLocal) != 0;
        EditRestrictPushObject = (_parcel.Flags & ParcelFlags.RestrictPushObject) != 0;
        EditDenyAnonymous = (_parcel.Flags & ParcelFlags.DenyAnonymous) != 0;
        EditDenyAgeUnverified = (_parcel.Flags & ParcelFlags.DenyAgeUnverified) != 0;
        EditAutoReturn = _parcel.OtherCleanTime;
        EditMusicUrl = _parcel.MusicURL ?? string.Empty;
        EditMediaUrl = _parcel.Media.MediaURL ?? string.Empty;
        EditCategory = _parcel.Category;
        EditForSale = (_parcel.Flags & ParcelFlags.ForSale) != 0;
        EditSalePrice = _parcel.SalePrice;
        EditSellToAnyone = _parcel.AuthBuyerID == UUID.Zero;
        EditAuthBuyerIdText = _parcel.AuthBuyerID != UUID.Zero ? _parcel.AuthBuyerID.ToString() : string.Empty;
        EditRestrictToGroup = (_parcel.Flags & ParcelFlags.UseAccessGroup) != 0;
        EditRestrictToList = (_parcel.Flags & ParcelFlags.UseAccessList) != 0;
        EditBanListEnabled = (_parcel.Flags & ParcelFlags.UseBanList) != 0;
        EditPassEnabled = (_parcel.Flags & ParcelFlags.UsePassList) != 0;
        EditPassPrice = _parcel.PassPrice;
        EditPassHours = (decimal)_parcel.PassHours;

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
        if (_parcel == null || _parcelSim == null || !CanEditParcel) return;

        _parcel.Name = EditName;
        _parcel.Desc = EditDescription;

        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowFly, EditAllowFly);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowOtherScripts, EditAllowScripts);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.CreateObjects, EditAllowBuild);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowLandmark, EditAllowLandmark);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowVoiceChat, EditAllowVoice);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowDamage, EditAllowDamage);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.AllowGroupScripts, EditAllowGroupScripts);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.ShowDirectory, EditShowInSearch);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.SoundLocal, EditSoundLocal);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.RestrictPushObject, EditRestrictPushObject);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.DenyAnonymous, EditDenyAnonymous);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.DenyAgeUnverified, EditDenyAgeUnverified);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.UseAccessGroup, EditRestrictToGroup);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.UseAccessList, EditRestrictToList);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.UseBanList, EditBanListEnabled);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.UsePassList, EditPassEnabled);
        _parcel.Flags = SetFlag(_parcel.Flags, ParcelFlags.ForSale, EditForSale);
        _parcel.OtherCleanTime = (int)EditAutoReturn;
        _parcel.MusicURL = EditMusicUrl;
        _parcel.Media.MediaURL = EditMediaUrl;
        _parcel.Category = EditCategory;
        _parcel.PassPrice = (int)EditPassPrice;
        _parcel.PassHours = (float)EditPassHours;

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

    [RelayCommand]
    private void OpenBuyerPicker()
    {
        _instance.ShowAvatarPicker("Select Authorized Buyer",
            entry => EditAuthBuyerIdText = entry.Id.ToString());
    }

    // --- Access list commands ---

    [RelayCommand]
    private void AddToWhiteList()
    {
        _instance.ShowAvatarPicker("Add to Allowed Residents", entry =>
        {
            if (WhiteList.Any(e => e.AgentId == entry.Id)) return;
            WhiteList.Add(new AccessEntryViewModel(entry.Id, entry.Name, DateTime.MinValue));
            SendAccessListUpdate(AccessList.Access, [.. WhiteList]);
        });
    }

    [RelayCommand]
    private void RemoveFromWhiteList()
    {
        if (SelectedWhiteListEntry == null || _parcel == null || _parcelSim == null) return;
        WhiteList.Remove(SelectedWhiteListEntry);
        SelectedWhiteListEntry = null;
        SendAccessListUpdate(AccessList.Access, [.. WhiteList]);
    }

    [RelayCommand]
    private void AddToBanList()
    {
        _instance.ShowAvatarPicker("Add to Banned Residents", entry =>
        {
            if (BanList.Any(e => e.AgentId == entry.Id)) return;
            BanList.Add(new AccessEntryViewModel(entry.Id, entry.Name, DateTime.MinValue));
            SendAccessListUpdate(AccessList.Ban, [.. BanList]);
        });
    }

    [RelayCommand]
    private void RemoveFromBanList()
    {
        if (SelectedBanListEntry == null || _parcel == null || _parcelSim == null) return;
        BanList.Remove(SelectedBanListEntry);
        SelectedBanListEntry = null;
        SendAccessListUpdate(AccessList.Ban, [.. BanList]);
    }

    // --- Return objects commands ---

    [RelayCommand]
    private void ReturnAllOwnedObjects()
    {
        if (_parcel == null || _parcelSim == null) return;
        Client.Parcels.ReturnObjects(_parcelSim, _parcel.LocalID, ObjectReturnType.Owner, []);
    }

    [RelayCommand]
    private void ReturnAllGroupObjects()
    {
        if (_parcel == null || _parcelSim == null) return;
        Client.Parcels.ReturnObjects(_parcelSim, _parcel.LocalID, ObjectReturnType.Group, []);
    }

    [RelayCommand]
    private void ReturnAllOtherObjects()
    {
        if (_parcel == null || _parcelSim == null) return;
        Client.Parcels.ReturnObjects(_parcelSim, _parcel.LocalID, ObjectReturnType.Other, []);
    }

    [RelayCommand]
    private void ReturnObjectsByOwner(ParcelPrimOwnerEntry entry)
    {
        if (_parcel == null || _parcelSim == null) return;
        var type = entry.IsGroupOwned ? ObjectReturnType.Group : ObjectReturnType.Owner;
        Client.Parcels.ReturnObjects(_parcelSim, _parcel.LocalID, type, [entry.OwnerId]);
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
        _cachedGroups = e.Groups;
        Dispatcher.UIThread.Post(() =>
        {
            AgentGroups.Clear();
            foreach (var kvp in e.Groups.OrderBy(g => g.Value.Name))
                AgentGroups.Add(new GroupEntry(kvp.Key, kvp.Value.Name, kvp.Key == Client.Self.ActiveGroup));
            HasAgentGroups = AgentGroups.Count > 0;
            if (SelectedGroupForDeed == null && AgentGroups.Count > 0)
                SelectedGroupForDeed = AgentGroups[0];

            if (_parcel is { IsGroupOwned: true, GroupID: var gid } && gid != UUID.Zero)
            {
                var isGroupManager = e.Groups.TryGetValue(gid, out var grp)
                    && grp.Powers.HasFlag(GroupPowers.LandOptions);
                CanEditParcel = IsOwnParcel
                    || (Client.Network.CurrentSim?.IsEstateManager ?? false)
                    || isGroupManager;
            }
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
    private void PlayParcelMusic()
    {
        if (_instance.Media == null || string.IsNullOrEmpty(MusicUrl)) return;
        _instance.Media.PlayUrl(MusicUrl);
    }

    [RelayCommand]
    private void StopParcelMusic()
    {
        _instance.Media?.StopStreamCommand.Execute(null);
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadCurrentParcel();
    }

    private void Parcels_ParcelAccessListReply(object? sender, ParcelAccessListReplyEventArgs e)
    {
        if (_parcel == null || e.LocalID != _parcel.LocalID) return;
        bool isBan = (e.Flags & (uint)AccessList.Ban) != 0;
        var entries = e.AccessList
            .Select(a => new AccessEntryViewModel(a.AgentID, _instance.Names.Get(a.AgentID), a.Time))
            .ToList();
        Dispatcher.UIThread.Post(() =>
        {
            if (isBan)
            {
                BanList.Clear();
                foreach (var entry in entries) BanList.Add(entry);
            }
            else
            {
                WhiteList.Clear();
                foreach (var entry in entries) WhiteList.Add(entry);
            }
            IsAccessListLoading = false;
        });
    }

    private void Parcels_ParcelObjectOwnersReply(object? sender, ParcelObjectOwnersReplyEventArgs e)
    {
        if (_parcel == null) return;
        if (_parcelSim != null && e.Simulator?.Handle != _parcelSim.Handle) return;
        var owners = e.PrimOwners
            .OrderByDescending(o => o.Count)
            .Select(o => new ParcelPrimOwnerEntry(o.OwnerID, _instance.Names.Get(o.OwnerID), o.IsGroupOwned, o.Count, o.NewestPrim))
            .ToList();
        Dispatcher.UIThread.Post(() =>
        {
            PrimOwners.Clear();
            foreach (var entry in owners) PrimOwners.Add(entry);
            IsObjectsLoading = false;
        });
    }

    private void Self_RegionExperiencesUpdated(object? sender, RegionExperiencesEventArgs e)
    {
        var trusted = e.RegionExperiences.Trusted.ToList();
        var blocked = e.RegionExperiences.Blocked.ToList();
        var contrib = e.RegionExperiences.Allowed.ToList();
        Dispatcher.UIThread.Post(() =>
        {
            TrustedExperiences.Clear();
            foreach (var id in trusted) TrustedExperiences.Add(new ExperienceEntryViewModel(id));
            BlockedExperiences.Clear();
            foreach (var id in blocked) BlockedExperiences.Add(new ExperienceEntryViewModel(id));
            ContribExperiences.Clear();
            foreach (var id in contrib) ContribExperiences.Add(new ExperienceEntryViewModel(id));
            IsExperienceLoading = false;
        });
        var allIds = trusted.Concat(blocked).Concat(contrib).Distinct().ToList();
        if (allIds.Count > 0)
            _ = FetchExperienceNamesAsync(allIds);
    }

    private async Task FetchEnvironmentAsync(int parcelId)
    {
        var parcelEnv = await Client.Environment.GetParcelEnvironmentAsync(parcelId);
        var legacyEnv = await Client.Environment.GetLegacyEnvironmentAsync();
        Dispatcher.UIThread.Post(() =>
        {
            var env = parcelEnv?.Environment;
            if (env != null && !env.IsDefault)
            {
                EnvIsCustom = true;
                EnvDayLength = FormatDayTime(env.DayLength);
                EnvDayOffset = FormatDayTime(env.DayOffset);
            }
            else
            {
                EnvIsCustom = false;
                EnvDayLength = string.Empty;
                EnvDayOffset = string.Empty;
            }
            EnvHasLegacyWindLight = legacyEnv != null;
            IsEnvironmentLoading = false;
        });
    }

    private async Task FetchExperienceNamesAsync(List<UUID> ids)
    {
        var info = await Client.Self.GetExperienceInfoAsync(ids);
        if (info == null || info.Experiences.Count == 0) return;
        var nameMap = info.Experiences
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToDictionary(e => e.ExperienceID, e => e.Name);
        Dispatcher.UIThread.Post(() =>
        {
            ApplyExperienceNames(TrustedExperiences, nameMap);
            ApplyExperienceNames(BlockedExperiences, nameMap);
            ApplyExperienceNames(ContribExperiences, nameMap);
        });
    }

    private static void ApplyExperienceNames(ObservableCollection<ExperienceEntryViewModel> coll,
        Dictionary<UUID, string> nameMap)
    {
        foreach (var entry in coll)
            if (nameMap.TryGetValue(entry.ExperienceId, out var name))
                entry.Name = name;
    }

    private static string FormatDayTime(int seconds) =>
        seconds <= 0 ? "Default" :
        seconds >= 3600 ? $"{seconds / 3600.0:0.##}h" :
        $"{seconds / 60.0:0.##}m";

    private static ParcelFlags SetFlag(ParcelFlags flags, ParcelFlags flag, bool value)
        => value ? flags | flag : flags & ~flag;

    private void SendAccessListUpdate(AccessList listType, IList<AccessEntryViewModel> entries)
    {
        if (_parcel == null || _parcelSim == null) return;
        var pkt = new ParcelAccessListUpdatePacket
        {
            AgentData = { AgentID = Client.Self.AgentID, SessionID = Client.Self.SessionID },
            Data =
            {
                Flags = (uint)listType,
                LocalID = _parcel.LocalID,
                TransactionID = UUID.Random(),
                SequenceID = 0,
                Sections = 1
            },
            List = entries.Select(e => new ParcelAccessListUpdatePacket.ListBlock
            {
                ID = e.AgentId, Time = 0, Flags = 0
            }).ToArray()
        };
        Client.Network.SendPacket(pkt, _parcelSim);
    }
}

public class AccessEntryViewModel
{
    public UUID AgentId { get; }
    public string DisplayName { get; }
    public DateTime AccessTime { get; }
    public string DisplayTime => AccessTime != DateTime.MinValue
        ? AccessTime.ToString("yyyy-MM-dd")
        : string.Empty;

    public AccessEntryViewModel(UUID agentId, string displayName, DateTime accessTime)
    {
        AgentId = agentId;
        DisplayName = displayName;
        AccessTime = accessTime;
    }
}

public partial class ExperienceEntryViewModel : ObservableObject
{
    public UUID ExperienceId { get; }
    [ObservableProperty] private string _name;

    public ExperienceEntryViewModel(UUID id)
    {
        ExperienceId = id;
        _name = id.ToString();
    }
}

public partial class ParcelPrimOwnerEntry : ObservableObject
{
    public UUID OwnerId { get; }
    [ObservableProperty] private string _ownerName;
    public bool IsGroupOwned { get; }
    public int Count { get; }
    public string TypeLabel => IsGroupOwned ? "Group" : "Resident";
    public string NewestPrimDate { get; }

    public ParcelPrimOwnerEntry(UUID ownerId, string ownerName, bool isGroupOwned, int count, DateTime newestPrim)
    {
        OwnerId = ownerId;
        _ownerName = ownerName;
        IsGroupOwned = isGroupOwned;
        Count = count;
        NewestPrimDate = newestPrim != DateTime.MinValue ? newestPrim.ToString("yyyy-MM-dd") : string.Empty;
    }
}
