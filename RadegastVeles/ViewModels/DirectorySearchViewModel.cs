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
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.Messages.Linden;

using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class DirectorySearchViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private UUID _peopleQueryId;
    private UUID _placesQueryId;
    private UUID _groupsQueryId;
    private UUID _eventsQueryId;

    private int _peopleStart;
    private int _placesStart;
    private int _groupsStart;
    private uint _eventsStart;

    // Event detail fetch / teleport tracking
    private uint _pendingEventInfoId;
    private DirectoryManager.EventInfo _currentEventInfo;

    // People detail
    private UUID _pendingPersonDetailId;

    // Places detail + teleport
    private UUID _pendingPlaceDetailId;
    private ParcelInfo _cachedPlaceParcel;
    private bool _hasCachedPlaceParcel;

    // Groups detail
    private UUID _pendingGroupDetailId;

    // (No per-classified tracking needed — details shown from search result data)

    // LandSales detail + teleport (auto-fetched on selection)
    private UUID _pendingLandSaleDetailId;
    private ParcelInfo _cachedLandSaleParcel;
    private bool _hasCachedLandSaleParcel;

    [ObservableProperty] private int _selectedTabIndex;

    // Maturity filter — 0=General, 1=General+Moderate, 2=All (includes Adult)
    [ObservableProperty] private int _maturityIndex = 1;

    // People
    [ObservableProperty] private string _peopleQuery = string.Empty;
    [ObservableProperty] private bool _isSearchingPeople;
    [ObservableProperty] private bool _peopleHasMore;
    [ObservableProperty] private bool _peopleCanGoPrev;
    [ObservableProperty] private string _peopleStatus = string.Empty;
    [ObservableProperty] private PersonResult? _selectedPerson;

    public ObservableCollection<PersonResult> PeopleResults { get; } = [];

    // Places
    [ObservableProperty] private string _placesQuery = string.Empty;
    [ObservableProperty] private bool _isSearchingPlaces;
    [ObservableProperty] private bool _placesHasMore;
    [ObservableProperty] private bool _placesCanGoPrev;
    [ObservableProperty] private string _placesStatus = string.Empty;
    [ObservableProperty] private PlaceResult? _selectedPlace;

    public ObservableCollection<PlaceResult> PlaceResults { get; } = [];

    // Groups
    [ObservableProperty] private string _groupsQuery = string.Empty;
    [ObservableProperty] private bool _isSearchingGroups;
    [ObservableProperty] private bool _groupsHasMore;
    [ObservableProperty] private bool _groupsCanGoPrev;
    [ObservableProperty] private string _groupsStatus = string.Empty;
    [ObservableProperty] private GroupResult? _selectedGroup;

    public ObservableCollection<GroupResult> GroupResults { get; } = [];

    // Group detail (populated via RequestGroupProfile on selection)
    [ObservableProperty] private bool _hasGroupDetail;
    [ObservableProperty] private string _groupDetailCharter = string.Empty;
    [ObservableProperty] private string _groupDetailMembers = string.Empty;

    // Events
    [ObservableProperty] private string _eventsQuery = string.Empty;
    [ObservableProperty] private bool _isSearchingEvents;
    [ObservableProperty] private bool _eventsHasMore;
    [ObservableProperty] private bool _eventsCanGoPrev;
    [ObservableProperty] private string _eventsStatus = string.Empty;
    [ObservableProperty] private EventResult? _selectedEvent;

    public ObservableCollection<EventResult> EventResults { get; } = [];

    // Event detail (populated via EventInfoRequest on selection)
    [ObservableProperty] private bool _hasEventDetail;
    [ObservableProperty] private string _eventDetailName = string.Empty;
    [ObservableProperty] private string _eventDetailLocation = string.Empty;
    [ObservableProperty] private string _eventDetailDescription = string.Empty;
    [ObservableProperty] private string _eventDetailCategory = string.Empty;
    [ObservableProperty] private string _eventDetailDate = string.Empty;
    [ObservableProperty] private string _eventDetailDuration = string.Empty;

    // Classifieds
    private int _classifiedsGen;
    [ObservableProperty] private string _classifiedsQuery = string.Empty;
    [ObservableProperty] private bool _isSearchingClassifieds;
    [ObservableProperty] private string _classifiedsStatus = string.Empty;
    [ObservableProperty] private ClassifiedResult? _selectedClassified;

    public ObservableCollection<ClassifiedResult> ClassifiedResults { get; } = [];

    // Classified detail (populated via ClassifiedInfoReply event on selection)
    private UUID _pendingClassifiedDetailId;
    private Vector3d _cachedClassifiedPos;
    [ObservableProperty] private bool _hasClassifiedDetail;
    [ObservableProperty] private string _classifiedDetailDescription = string.Empty;
    [ObservableProperty] private string _classifiedDetailLocation = string.Empty;
    [ObservableProperty] private string _classifiedDetailPoster = string.Empty;

    // Land Sales
    private int _landGen;
    private int _landStart;
    [ObservableProperty] private decimal _landMaxPrice;
    [ObservableProperty] private decimal _landMinArea;
    [ObservableProperty] private int _landTypeIndex;
    [ObservableProperty] private bool _isSearchingLandSales;
    [ObservableProperty] private bool _landSalesHasMore;
    [ObservableProperty] private bool _landSalesCanGoPrev;
    [ObservableProperty] private string _landSalesStatus = string.Empty;
    [ObservableProperty] private LandSaleResult? _selectedLandSale;

    public ObservableCollection<LandSaleResult> LandSaleResults { get; } = [];

    // Land sale detail (populated via ParcelInfoRequest on selection)
    [ObservableProperty] private bool _hasLandSaleDetail;
    [ObservableProperty] private string _landSaleDetailDescription = string.Empty;
    [ObservableProperty] private string _landSaleDetailLocation = string.Empty;

    // People detail (populated via AvatarPropertiesRequest on selection)
    [ObservableProperty] private bool _hasPersonDetail;
    [ObservableProperty] private string _personDetailBio = string.Empty;
    [ObservableProperty] private string _personDetailBorn = string.Empty;

    // Places detail (populated via ParcelInfoRequest on selection)
    [ObservableProperty] private bool _hasPlaceDetail;
    [ObservableProperty] private string _placeDetailDescription = string.Empty;
    [ObservableProperty] private string _placeDetailLocation = string.Empty;

    public DirectorySearchViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        Client.Directory.DirPeopleReply += Directory_DirPeopleReply;
        Client.Directory.DirPlacesReply += Directory_DirPlacesReply;
        Client.Directory.DirGroupsReply += Directory_DirGroupsReply;
        Client.Directory.DirEventsReply += Directory_DirEventsReply;
        Client.Directory.DirClassifiedsReply += Directory_DirClassifiedsReply;
        Client.Directory.DirLandReply += Directory_DirLandReply;
        Client.Directory.EventInfoReply += Directory_EventInfoReply;
        Client.Avatars.AvatarPropertiesReply += Avatars_AvatarPropertiesReply;
        Client.Groups.GroupProfile += Groups_GroupProfile;
        Client.Parcels.ParcelInfoReply += Parcels_ParcelInfoReply;
        Client.Avatars.ClassifiedInfoReply += Avatars_ClassifiedInfoReply;
    }

    public void Dispose()
    {
        Client.Directory.DirPeopleReply -= Directory_DirPeopleReply;
        Client.Directory.DirPlacesReply -= Directory_DirPlacesReply;
        Client.Directory.DirGroupsReply -= Directory_DirGroupsReply;
        Client.Directory.DirEventsReply -= Directory_DirEventsReply;
        Client.Directory.DirClassifiedsReply -= Directory_DirClassifiedsReply;
        Client.Directory.DirLandReply -= Directory_DirLandReply;
        Client.Directory.EventInfoReply -= Directory_EventInfoReply;
        Client.Avatars.AvatarPropertiesReply -= Avatars_AvatarPropertiesReply;
        Client.Groups.GroupProfile -= Groups_GroupProfile;
        Client.Parcels.ParcelInfoReply -= Parcels_ParcelInfoReply;
        Client.Avatars.ClassifiedInfoReply -= Avatars_ClassifiedInfoReply;
    }

    // --- Maturity flags helper ---

    private DirectoryManager.DirFindFlags GetMaturityFlags() => MaturityIndex switch
    {
        2 => DirectoryManager.DirFindFlags.IncludePG
           | DirectoryManager.DirFindFlags.IncludeMature
           | DirectoryManager.DirFindFlags.IncludeAdult,
        1 => DirectoryManager.DirFindFlags.IncludePG
           | DirectoryManager.DirFindFlags.IncludeMature,
        _ => DirectoryManager.DirFindFlags.IncludePG,
    };

    // --- People ---

    [RelayCommand(CanExecute = nameof(IsPeopleQueryValid))]
    private void SearchPeople()
    {
        if (string.IsNullOrWhiteSpace(PeopleQuery)) return;
        _peopleStart = 0;
        PeopleResults.Clear();
        PeopleHasMore = false;
        PeopleCanGoPrev = false;
        IsSearchingPeople = true;
        PeopleStatus = "Searching…";
        _peopleQueryId = Client.Directory.StartPeopleSearch(PeopleQuery, _peopleStart);
    }

    private bool IsPeopleQueryValid() => PeopleQuery.Length >= 3;

    partial void OnPeopleQueryChanged(string value)
    {
        SearchPeopleCommand.NotifyCanExecuteChanged();
        if (value.Length > 0 && value.Length < 3)
            PeopleStatus = "Enter at least 3 characters to search.";
        else if (string.IsNullOrEmpty(value))
            PeopleStatus = string.Empty;
    }

    [RelayCommand]
    private void PeopleNext()
    {
        if (!PeopleHasMore) return;
        _peopleStart += 100;
        PeopleResults.Clear();
        IsSearchingPeople = true;
        PeopleStatus = "Loading…";
        _peopleQueryId = Client.Directory.StartPeopleSearch(PeopleQuery, _peopleStart);
    }

    [RelayCommand]
    private void PeoplePrev()
    {
        if (!PeopleCanGoPrev) return;
        _peopleStart = Math.Max(0, _peopleStart - 100);
        PeopleResults.Clear();
        IsSearchingPeople = true;
        PeopleStatus = "Loading…";
        _peopleQueryId = Client.Directory.StartPeopleSearch(PeopleQuery, _peopleStart);
    }

    [RelayCommand]
    private void OpenPersonProfile()
    {
        if (SelectedPerson == null) return;
        _instance.ShowAgentProfile(SelectedPerson.FullName, SelectedPerson.AgentID);
    }

    partial void OnSelectedPersonChanged(PersonResult? value)
    {
        HasPersonDetail = false;
        PersonDetailBio = PersonDetailBorn = string.Empty;
        if (value == null) return;
        _pendingPersonDetailId = value.AgentID;
        // UDP request (always send — also triggers groups/interests/picks on full profile)
        Client.Avatars.RequestAvatarProperties(value.AgentID);
        // Caps-based request for modern SL where UDP may not return bio text
        if (Client.Avatars.AgentProfileAvailable())
            _ = Task.Run(async () =>
            {
                var (success, profile) = await Client.Avatars.RequestAgentProfileAsync(value.AgentID);
                OnPersonProfileCapReply(success, profile);
            });
    }

    private void OnPersonProfileCapReply(bool success, AgentProfileMessage? profile)
    {
        if (!success || profile == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (HasPersonDetail) return; // UDP already populated the panel
            PersonDetailBio = string.IsNullOrWhiteSpace(profile.SecondLifeAboutText)
                ? "(No bio)"
                : profile.SecondLifeAboutText.Length > 250
                    ? profile.SecondLifeAboutText[..250] + "…"
                    : profile.SecondLifeAboutText;
            PersonDetailBorn = profile.MemberSince == default
                ? string.Empty
                : $"Resident since {profile.MemberSince:yyyy-MM-dd}";
            HasPersonDetail = true;
        });
    }

    // --- Places ---

    [RelayCommand]
    private void SearchPlaces()
    {
        if (string.IsNullOrWhiteSpace(PlacesQuery)) return;
        _placesStart = 0;
        PlaceResults.Clear();
        PlacesHasMore = false;
        PlacesCanGoPrev = false;
        IsSearchingPlaces = true;
        PlacesStatus = "Searching…";
        _placesQueryId = Client.Directory.StartDirPlacesSearch(
            PlacesQuery, GetMaturityFlags(), ParcelCategory.Any, _placesStart);
    }

    [RelayCommand]
    private void PlacesNext()
    {
        if (!PlacesHasMore) return;
        _placesStart += 100;
        PlaceResults.Clear();
        IsSearchingPlaces = true;
        PlacesStatus = "Loading…";
        _placesQueryId = Client.Directory.StartDirPlacesSearch(
            PlacesQuery, GetMaturityFlags(), ParcelCategory.Any, _placesStart);
    }

    [RelayCommand]
    private void PlacesPrev()
    {
        if (!PlacesCanGoPrev) return;
        _placesStart = Math.Max(0, _placesStart - 100);
        PlaceResults.Clear();
        IsSearchingPlaces = true;
        PlacesStatus = "Loading…";
        _placesQueryId = Client.Directory.StartDirPlacesSearch(
            PlacesQuery, GetMaturityFlags(), ParcelCategory.Any, _placesStart);
    }

    partial void OnSelectedPlaceChanged(PlaceResult? value)
    {
        HasPlaceDetail = false;
        PlaceDetailDescription = PlaceDetailLocation = string.Empty;
        _hasCachedPlaceParcel = false;
        if (value == null || value.ParcelID == UUID.Zero) return;
        _pendingPlaceDetailId = value.ParcelID;
        Client.Parcels.RequestParcelInfo(value.ParcelID);
    }

    [RelayCommand]
    private void TeleportToPlace()
    {
        if (!_hasCachedPlaceParcel) return;
        var parcel = _cachedPlaceParcel;
        var localPos = new Vector3(parcel.GlobalX % 256, parcel.GlobalY % 256, parcel.GlobalZ);
        _ = Client.Self.TeleportAsync(parcel.SimName, localPos);
    }

    // --- Groups ---

    [RelayCommand(CanExecute = nameof(IsGroupsQueryValid))]
    private void SearchGroups()
    {
        if (string.IsNullOrWhiteSpace(GroupsQuery)) return;
        _groupsStart = 0;
        GroupResults.Clear();
        GroupsHasMore = false;
        GroupsCanGoPrev = false;
        IsSearchingGroups = true;
        GroupsStatus = "Searching…";
        _groupsQueryId = Client.Directory.StartGroupSearch(GroupsQuery, _groupsStart,
            GetMaturityFlags() | DirectoryManager.DirFindFlags.Groups);
    }

    private bool IsGroupsQueryValid() => GroupsQuery.Length >= 3;

    partial void OnGroupsQueryChanged(string value)
    {
        SearchGroupsCommand.NotifyCanExecuteChanged();
        if (value.Length > 0 && value.Length < 3)
            GroupsStatus = "Enter at least 3 characters to search.";
        else if (string.IsNullOrEmpty(value))
            GroupsStatus = string.Empty;
    }

    [RelayCommand]
    private void GroupsNext()
    {
        if (!GroupsHasMore) return;
        _groupsStart += 100;
        GroupResults.Clear();
        IsSearchingGroups = true;
        GroupsStatus = "Loading…";
        _groupsQueryId = Client.Directory.StartGroupSearch(GroupsQuery, _groupsStart,
            GetMaturityFlags() | DirectoryManager.DirFindFlags.Groups);
    }

    [RelayCommand]
    private void GroupsPrev()
    {
        if (!GroupsCanGoPrev) return;
        _groupsStart = Math.Max(0, _groupsStart - 100);
        GroupResults.Clear();
        IsSearchingGroups = true;
        GroupsStatus = "Loading…";
        _groupsQueryId = Client.Directory.StartGroupSearch(GroupsQuery, _groupsStart,
            GetMaturityFlags() | DirectoryManager.DirFindFlags.Groups);
    }

    [RelayCommand]
    private void OpenGroupProfile()
    {
        if (SelectedGroup == null) return;
        _instance.ShowGroupProfile(SelectedGroup.GroupID);
    }

    partial void OnSelectedGroupChanged(GroupResult? value)
    {
        HasGroupDetail = false;
        GroupDetailCharter = GroupDetailMembers = string.Empty;
        if (value == null || value.GroupID == UUID.Zero) return;
        _pendingGroupDetailId = value.GroupID;
        Client.Groups.RequestGroupProfile(value.GroupID);
    }

    // --- Events ---

    [RelayCommand]
    private void SearchEvents()
    {
        if (string.IsNullOrWhiteSpace(EventsQuery)) return;
        _eventsStart = 0;
        EventResults.Clear();
        EventsHasMore = false;
        EventsCanGoPrev = false;
        IsSearchingEvents = true;
        EventsStatus = "Searching…";
        _eventsQueryId = Client.Directory.StartEventsSearch(
            EventsQuery,
            GetMaturityFlags() | DirectoryManager.DirFindFlags.DateEvents,
            "u", _eventsStart,
            DirectoryManager.EventCategories.All);
    }

    [RelayCommand]
    private void EventsNext()
    {
        if (!EventsHasMore) return;
        _eventsStart += 100;
        EventResults.Clear();
        IsSearchingEvents = true;
        EventsStatus = "Loading…";
        _eventsQueryId = Client.Directory.StartEventsSearch(
            EventsQuery,
            GetMaturityFlags() | DirectoryManager.DirFindFlags.DateEvents,
            "u", _eventsStart,
            DirectoryManager.EventCategories.All);
    }

    [RelayCommand]
    private void EventsPrev()
    {
        if (!EventsCanGoPrev) return;
        _eventsStart = (uint)Math.Max(0, (int)_eventsStart - 100);
        EventResults.Clear();
        IsSearchingEvents = true;
        EventsStatus = "Loading…";
        _eventsQueryId = Client.Directory.StartEventsSearch(
            EventsQuery,
            GetMaturityFlags() | DirectoryManager.DirFindFlags.DateEvents,
            "u", _eventsStart,
            DirectoryManager.EventCategories.All);
    }

    partial void OnSelectedEventChanged(EventResult? value)
    {
        HasEventDetail = false;
        EventDetailName = EventDetailLocation = EventDetailDescription =
            EventDetailCategory = EventDetailDate = EventDetailDuration = string.Empty;
        if (value == null) return;
        _pendingEventInfoId = value.EventID;
        Client.Directory.EventInfoRequest(value.EventID);
    }

    [RelayCommand]
    private void TeleportToEvent()
    {
        if (!HasEventDetail) return;
        var evt = _currentEventInfo;
        ulong handle = Helpers.GlobalPosToRegionHandle(
            (float)evt.GlobalPos.X, (float)evt.GlobalPos.Y, out var localX, out var localY);
        var pos = new Vector3(localX, localY, (float)evt.GlobalPos.Z);
        _ = Client.Self.TeleportAsync(handle, pos);
    }

    partial void OnSelectedClassifiedChanged(ClassifiedResult? value)
    {
        HasClassifiedDetail = false;
        ClassifiedDetailDescription = ClassifiedDetailLocation = ClassifiedDetailPoster = string.Empty;
        _pendingClassifiedDetailId = UUID.Zero;
        _cachedClassifiedPos = Vector3d.Zero;
        if (value == null) return;
        _pendingClassifiedDetailId = value.ClassifiedID;
        // Pass our own agent ID as the first param — the server looks up classified by ID only.
        Client.Avatars.RequestClassifiedInfo(value.ClassifiedID);
    }

    private void Avatars_ClassifiedInfoReply(object? sender, ClassifiedInfoReplyEventArgs e)
    {
        if (e.ClassifiedID != _pendingClassifiedDetailId) return;

        var classified = e.Classified;
        var desc = classified.Desc ?? string.Empty;
        var simName = classified.SimName ?? string.Empty;
        var pos = classified.Position;
        var creatorId = classified.CreatorID;

        _cachedClassifiedPos = pos;

        Helpers.GlobalPosToRegionHandle((float)pos.X, (float)pos.Y, out float localX, out float localY);
        var locationText = pos == Vector3d.Zero
            ? "Location unavailable"
            : !string.IsNullOrEmpty(simName)
                ? $"{simName} ({(int)localX}, {(int)localY}, {(int)pos.Z})"
                : $"({(int)localX}, {(int)localY}, {(int)pos.Z})";

        var posterName = creatorId != UUID.Zero ? _instance.Names.Get(creatorId) : string.Empty;

        var classifiedId = e.ClassifiedID;
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedClassified?.ClassifiedID != classifiedId) return;
            ClassifiedDetailDescription = desc;
            ClassifiedDetailLocation = locationText;
            ClassifiedDetailPoster = posterName;
            HasClassifiedDetail = true;
        });
    }

    [RelayCommand]
    private void TeleportToClassified()
    {
        if (_cachedClassifiedPos == Vector3d.Zero) return;
        ulong handle = Helpers.GlobalPosToRegionHandle(
            (float)_cachedClassifiedPos.X, (float)_cachedClassifiedPos.Y,
            out float localX, out float localY);
        var pos = new Vector3(localX, localY, (float)_cachedClassifiedPos.Z);
        _ = Client.Self.TeleportAsync(handle, pos);
    }

    [RelayCommand]
    private void TeleportToLandSale()
    {
        if (!_hasCachedLandSaleParcel) return;
        var parcel = _cachedLandSaleParcel;
        var localPos = new Vector3(parcel.GlobalX % 256, parcel.GlobalY % 256, parcel.GlobalZ);
        _ = Client.Self.TeleportAsync(parcel.SimName, localPos);
    }

    partial void OnSelectedLandSaleChanged(LandSaleResult? value)
    {
        HasLandSaleDetail = false;
        LandSaleDetailDescription = LandSaleDetailLocation = string.Empty;
        _hasCachedLandSaleParcel = false;
        if (value == null || value.ParcelID == UUID.Zero) return;
        _pendingLandSaleDetailId = value.ParcelID;
        Client.Parcels.RequestParcelInfo(value.ParcelID);
    }

    private void Directory_DirPeopleReply(object? sender, DirPeopleReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.QueryID != _peopleQueryId) return;
            IsSearchingPeople = false;

            foreach (var person in e.MatchedPeople)
                PeopleResults.Add(new PersonResult(person.AgentID, person.FirstName, person.LastName));

            PeopleHasMore = e.MatchedPeople.Count >= 100;
            PeopleCanGoPrev = _peopleStart > 0;
            PeopleStatus = $"{PeopleResults.Count} result(s)";
        });
    }

    private void Directory_DirPlacesReply(object? sender, DirPlacesReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.QueryID != _placesQueryId) return;
            IsSearchingPlaces = false;

            bool foundSentinel = false;
            foreach (var parcel in e.MatchedParcels)
            {
                if (parcel.ID == UUID.Zero) { foundSentinel = true; continue; }
                PlaceResults.Add(new PlaceResult(parcel.ID, parcel.Name, parcel.Dwell, parcel.ForSale));
            }

            PlacesHasMore = foundSentinel || e.MatchedParcels.Count >= 100;
            PlacesCanGoPrev = _placesStart > 0;
            PlacesStatus = $"{PlaceResults.Count} result(s)";
        });
    }

    private void Directory_DirGroupsReply(object? sender, DirGroupsReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.QueryID != _groupsQueryId) return;
            IsSearchingGroups = false;

            bool foundSentinel = false;
            foreach (var group in e.MatchedGroups)
            {
                if (group.GroupID == UUID.Zero) { foundSentinel = true; continue; }
                GroupResults.Add(new GroupResult(group.GroupID, group.GroupName, group.Members));
            }

            GroupsHasMore = foundSentinel || e.MatchedGroups.Count >= 100;
            GroupsCanGoPrev = _groupsStart > 0;
            GroupsStatus = $"{GroupResults.Count} result(s)";
        });
    }

    private void Directory_DirEventsReply(object? sender, DirEventsReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.QueryID != _eventsQueryId) return;
            IsSearchingEvents = false;

            foreach (var ev in e.MatchedEvents)
                EventResults.Add(new EventResult(ev.ID, ev.Name, ev.Date, ev.Time));

            EventsHasMore = e.MatchedEvents.Count >= 100;
            EventsCanGoPrev = _eventsStart > 0;
            EventsStatus = $"{EventResults.Count} result(s)";
        });
    }

    private void Directory_EventInfoReply(object? sender, EventInfoReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var evt = e.MatchedEvent;
            if (evt.ID != _pendingEventInfoId) return;

            _currentEventInfo = evt;
            HasEventDetail = true;
            EventDetailName = evt.Name ?? string.Empty;

            float localX = (float)(evt.GlobalPos.X % 256);
            float localY = (float)(evt.GlobalPos.Y % 256);
            float localZ = (float)evt.GlobalPos.Z;
            EventDetailLocation = string.IsNullOrEmpty(evt.SimName)
                ? $"({localX:F0}, {localY:F0}, {localZ:F0})"
                : $"{evt.SimName} ({localX:F0}, {localY:F0}, {localZ:F0})";

            EventDetailCategory = evt.Category.ToString();

            var dateTime = Utils.UnixTimeToDateTime(evt.DateUTC);
            EventDetailDate = dateTime.ToString("ddd, MMM d 'at' h:mm tt") + " SLT";

            uint hours = evt.Duration / 60u;
            uint mins = evt.Duration % 60u;
            EventDetailDuration = hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";

            EventDetailDescription = evt.Desc ?? string.Empty;
        });
    }

    private void Avatars_AvatarPropertiesReply(object? sender, AvatarPropertiesReplyEventArgs e)
    {
        if (e.AvatarID != _pendingPersonDetailId) return;
        var props = e.Properties;
        Dispatcher.UIThread.Post(() =>
        {
            if (e.AvatarID != _pendingPersonDetailId) return;
            PersonDetailBio = string.IsNullOrWhiteSpace(props.AboutText)
                ? "(No bio)" : props.AboutText.Length > 250
                    ? props.AboutText[..250] + "…"
                    : props.AboutText;
            PersonDetailBorn = string.IsNullOrEmpty(props.BornOn)
                ? string.Empty : $"Resident since {props.BornOn}";
            HasPersonDetail = true;
        });
    }

    private void Groups_GroupProfile(object? sender, GroupProfileEventArgs e)
    {
        if (e.Group.ID != _pendingGroupDetailId) return;
        var group = e.Group;
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Group.ID != _pendingGroupDetailId) return;
            GroupDetailCharter = string.IsNullOrWhiteSpace(group.Charter)
                ? "(No description)" : group.Charter.Length > 300
                    ? group.Charter[..300] + "…"
                    : group.Charter;
            GroupDetailMembers = $"{group.GroupMembershipCount:N0} members";
            HasGroupDetail = true;
        });
    }

    private void Parcels_ParcelInfoReply(object? sender, ParcelInfoReplyEventArgs e)
    {
        var parcel = e.Parcel;
        Dispatcher.UIThread.Post(() =>
        {
            // Places detail
            if (parcel.ID == _pendingPlaceDetailId)
            {
                _pendingPlaceDetailId = UUID.Zero;
                _cachedPlaceParcel = parcel;
                _hasCachedPlaceParcel = true;
                PlaceDetailDescription = string.IsNullOrWhiteSpace(parcel.Description)
                    ? "(No description)"
                    : parcel.Description.Length > 300 ? parcel.Description[..300] + "…" : parcel.Description;
                PlaceDetailLocation = string.IsNullOrEmpty(parcel.SimName)
                    ? string.Empty
                    : $"{parcel.SimName} ({(int)(parcel.GlobalX % 256)}, {(int)(parcel.GlobalY % 256)}, {(int)parcel.GlobalZ})";
                HasPlaceDetail = true;
            }

            // Land sale detail
            if (parcel.ID == _pendingLandSaleDetailId)
            {
                _pendingLandSaleDetailId = UUID.Zero;
                _cachedLandSaleParcel = parcel;
                _hasCachedLandSaleParcel = true;
                LandSaleDetailDescription = string.IsNullOrWhiteSpace(parcel.Description)
                    ? "(No description)"
                    : parcel.Description.Length > 300 ? parcel.Description[..300] + "…" : parcel.Description;
                LandSaleDetailLocation = string.IsNullOrEmpty(parcel.SimName)
                    ? string.Empty
                    : $"{parcel.SimName} ({(int)(parcel.GlobalX % 256)}, {(int)(parcel.GlobalY % 256)}, {(int)parcel.GlobalZ})";
                HasLandSaleDetail = true;
            }
        });
    }

    // --- Classifieds ---

    [RelayCommand]
    private void SearchClassifieds()
    {
        if (string.IsNullOrWhiteSpace(ClassifiedsQuery)) return;
        ClassifiedResults.Clear();
        IsSearchingClassifieds = true;
        ClassifiedsStatus = "Searching…";
        _classifiedsGen++;
        Client.Directory.StartClassifiedSearch(ClassifiedsQuery);
    }

    private void Directory_DirClassifiedsReply(object? sender, DirClassifiedsReplyEventArgs e)
    {
        int gen = _classifiedsGen;
        Dispatcher.UIThread.Post(() =>
        {
            if (gen != _classifiedsGen) return;

            foreach (var c in e.Classifieds)
                ClassifiedResults.Add(new ClassifiedResult(c.ID, c.Name, c.Price, c.CreationDate));

            // Server sends batches of 16; a partial batch signals end of results
            if (e.Classifieds.Count < 16)
            {
                IsSearchingClassifieds = false;
                ClassifiedsStatus = ClassifiedResults.Count == 0
                    ? "No results."
                    : $"{ClassifiedResults.Count} result(s)";
            }
        });
    }

    // --- Land Sales ---

    [RelayCommand]
    private void SearchLandSales()
    {
        _landStart = 0;
        LandSaleResults.Clear();
        LandSalesHasMore = false;
        LandSalesCanGoPrev = false;
        IsSearchingLandSales = true;
        LandSalesStatus = "Searching…";
        _landGen++;
        IssueCurrentLandSearch();
    }

    [RelayCommand]
    private void LandSalesNext()
    {
        if (!LandSalesHasMore) return;
        _landStart += 100;
        LandSaleResults.Clear();
        IsSearchingLandSales = true;
        LandSalesStatus = "Loading…";
        _landGen++;
        IssueCurrentLandSearch();
    }

    [RelayCommand]
    private void LandSalesPrev()
    {
        if (!LandSalesCanGoPrev) return;
        _landStart = Math.Max(0, _landStart - 100);
        LandSaleResults.Clear();
        IsSearchingLandSales = true;
        LandSalesStatus = "Loading…";
        _landGen++;
        IssueCurrentLandSearch();
    }

    private void IssueCurrentLandSearch()
    {
        var flags = DirectoryManager.DirFindFlags.SortAsc
            | DirectoryManager.DirFindFlags.PerMeterSort
            | DirectoryManager.DirFindFlags.IncludePG
            | DirectoryManager.DirFindFlags.IncludeMature
            | DirectoryManager.DirFindFlags.IncludeAdult;

        if (LandMaxPrice > 0) flags |= DirectoryManager.DirFindFlags.LimitByPrice;
        if (LandMinArea > 0) flags |= DirectoryManager.DirFindFlags.LimitByArea;

        var searchType = LandTypeIndex switch
        {
            1 => DirectoryManager.SearchTypeFlags.Mainland,
            2 => DirectoryManager.SearchTypeFlags.Estate,
            3 => DirectoryManager.SearchTypeFlags.Auction,
            _ => DirectoryManager.SearchTypeFlags.Any,
        };

        Client.Directory.StartLandSearch(flags, searchType, (int)LandMaxPrice, (int)LandMinArea, _landStart);
    }

    private void Directory_DirLandReply(object? sender, DirLandReplyEventArgs e)
    {
        int gen = _landGen;
        Dispatcher.UIThread.Post(() =>
        {
            if (gen != _landGen) return;
            IsSearchingLandSales = false;

            bool hasSentinel = false;
            foreach (var p in e.DirParcels)
            {
                if (p.ID == UUID.Zero) { hasSentinel = true; continue; }
                LandSaleResults.Add(new LandSaleResult(p.ID, p.Name, p.ActualArea, p.SalePrice, p.Auction));
            }

            LandSalesHasMore = hasSentinel || e.DirParcels.Count >= 100;
            LandSalesCanGoPrev = _landStart > 0;
            LandSalesStatus = LandSaleResults.Count == 0
                ? "No results."
                : $"{LandSaleResults.Count} result(s)";
        });
    }
}

public record PersonResult(UUID AgentID, string FirstName, string LastName)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record PlaceResult(UUID ParcelID, string Name, float Dwell, bool ForSale)
{
    public string DwellDisplay => $"Traffic: {(int)Dwell:N0}";
    public string ForSaleDisplay => ForSale ? "For Sale" : string.Empty;
}

public record GroupResult(UUID GroupID, string Name, int Members)
{
    public string MembersDisplay => $"{Members:N0} members";
}

public record EventResult(uint EventID, string Name, string Date, uint UnixTime)
{
    public string DisplayDate => string.IsNullOrEmpty(Date) ? string.Empty : Date;
}

public record ClassifiedResult(UUID ClassifiedID, string Name, int Price, DateTime CreationDate)
{
    public string PriceDisplay => $"L${Price:N0}";
    public string CreatedDisplay => CreationDate == default
        ? string.Empty
        : $"Posted {CreationDate:MMM d, yyyy}";
}

public record LandSaleResult(UUID ParcelID, string Name, int ActualArea, int SalePrice, bool Auction)
{
    public string PriceDisplay => Auction ? "Auction" : $"L${SalePrice:N0}";
    public string AreaDisplay => $"{ActualArea:N0} sqm";
    public string PricePerMeterDisplay => ActualArea > 0 && !Auction && SalePrice > 0
        ? $"L${(double)SalePrice / ActualArea:F1}/sqm"
        : string.Empty;
}
