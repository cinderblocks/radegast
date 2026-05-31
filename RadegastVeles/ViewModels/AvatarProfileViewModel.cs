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
using OpenMetaverse.Messages.Linden;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class AvatarProfileViewModel : InstanceViewModelBase, IDisposable, IChatContext
{
    public UUID AgentID { get; }
    public bool IsOwnProfile => AgentID == Client.Self.AgentID;

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _bornOn = string.Empty;
    [ObservableProperty] private string _accountInfo = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAbout))]
    private string _aboutText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFirstLife))]
    private string _firstLifeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWeb))]
    private string _webUrl = string.Empty;

    [ObservableProperty] private string _partnerName = string.Empty;
    [ObservableProperty] private UUID _partnerID = UUID.Zero;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private Bitmap? _profileImage;
    [ObservableProperty] private Bitmap? _firstLifeImage;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isFriend;
    [ObservableProperty] private string _agentIdText = string.Empty;

    [ObservableProperty] private string _wantToText = string.Empty;
    [ObservableProperty] private string _skillsText = string.Empty;
    [ObservableProperty] private string _languagesText = string.Empty;
    [ObservableProperty] private string _wantToFlags = string.Empty;
    [ObservableProperty] private string _skillsFlags = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInterests))]
    private bool _hasInterests;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteButtonText))]
    private bool _isMuted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FriendButtonText))]
    private bool _isFriendRequestSent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingOwnProfile))]
    [NotifyPropertyChangedFor(nameof(IsNotEditingOwnProfile))]
    [NotifyPropertyChangedFor(nameof(CanBeginEdit))]
    private bool _isEditing;

    // ── Interest flags (WantTo) ───────────────────────────────────────────────
    [ObservableProperty] private bool _wantToBuild;
    [ObservableProperty] private bool _wantToExplore;
    [ObservableProperty] private bool _wantToMeet;
    [ObservableProperty] private bool _wantToGroup;
    [ObservableProperty] private bool _wantToBuy;
    [ObservableProperty] private bool _wantToSell;
    [ObservableProperty] private bool _wantToBeHired;
    [ObservableProperty] private bool _wantToHire;

    // ── Interest flags (Skills) ───────────────────────────────────────────────
    [ObservableProperty] private bool _skillTextures;
    [ObservableProperty] private bool _skillArchitecture;
    [ObservableProperty] private bool _skillEventPlanning;
    [ObservableProperty] private bool _skillModeling;
    [ObservableProperty] private bool _skillScripting;
    [ObservableProperty] private bool _skillCustomCharacters;

    public string MuteButtonText => IsMuted ? "Unmute" : "Mute";
    public string FriendButtonText => IsFriendRequestSent ? "Friend Request Sent" : "Add Friend";

    /// <summary>True when viewing own profile in edit mode.</summary>
    public bool IsEditingOwnProfile => IsOwnProfile && IsEditing;
    /// <summary>True when NOT viewing own profile in edit mode.</summary>
    public bool IsNotEditingOwnProfile => !IsEditingOwnProfile;
    /// <summary>True when own profile is loaded but not yet in edit mode.</summary>
    public bool CanBeginEdit => IsOwnProfile && !IsEditing;

    // Computed section visibility: always show for own profile so user can fill in empty fields.
    public bool ShowAbout    => IsOwnProfile || !string.IsNullOrEmpty(AboutText);
    public bool ShowFirstLife => IsOwnProfile || !string.IsNullOrEmpty(FirstLifeText);
    public bool ShowWeb      => IsOwnProfile || !string.IsNullOrEmpty(WebUrl);
    public bool ShowInterests => IsOwnProfile || HasInterests;

    // Saved values used by Discard.
    private string _savedAboutText = string.Empty;
    private string _savedFirstLifeText = string.Empty;
    private string _savedWebUrl = string.Empty;
    private string _savedWantToText = string.Empty;
    private string _savedSkillsText = string.Empty;
    private string _savedLanguagesText = string.Empty;
    private uint _savedWantToMask;
    private uint _savedSkillsMask;

    public ObservableCollection<AvatarGroupEntry> Groups { get; } = [];
    public ObservableCollection<AvatarPickEntry> Picks { get; } = [];
    public ObservableCollection<AvatarClassifiedEntry> Classifieds { get; } = [];

    public bool ShowPicks => IsOwnProfile || Picks.Count > 0;
    public bool ShowClassifieds => IsOwnProfile || Classifieds.Count > 0;

    private Avatar.AvatarProperties? _properties;
    private bool _gotProperties;

    public AvatarProfileViewModel(RadegastInstanceAvalonia instance, string name, UUID agentId) : base(instance)
    {
        AgentID = agentId;
        DisplayName = name;
        AgentIdText = agentId.ToString();
        IsFriend = Client.Friends.FriendList.ContainsKey(agentId);
        IsMuted = Client.Self.MuteList.Values.Any(m => m.Type == MuteType.Resident && m.ID == agentId);

        Client.Avatars.AvatarPropertiesReply += Avatars_AvatarPropertiesReply;
        Client.Avatars.AvatarGroupsReply += Avatars_AvatarGroupsReply;
        Client.Avatars.AvatarInterestsReply += Avatars_AvatarInterestsReply;
        Client.Avatars.AvatarNotesReply += Avatars_AvatarNotesReply;
        Client.Avatars.AvatarPicksReply += Avatars_AvatarPicksReply;
        Client.Avatars.PickInfoReply += Avatars_PickInfoReply;
        Client.Avatars.AvatarClassifiedReply += Avatars_AvatarClassifiedReply;
        Client.Avatars.ClassifiedInfoReply += Avatars_ClassifiedInfoReply;
        Client.Self.MuteListUpdated += Self_MuteListUpdated;

        Picks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowPicks));
        Classifieds.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowClassifieds));

        RequestProfile();
    }

    public void Dispose()
    {
        Client.Avatars.AvatarPropertiesReply -= Avatars_AvatarPropertiesReply;
        Client.Avatars.AvatarGroupsReply -= Avatars_AvatarGroupsReply;
        Client.Avatars.AvatarInterestsReply -= Avatars_AvatarInterestsReply;
        Client.Avatars.AvatarNotesReply -= Avatars_AvatarNotesReply;
        Client.Avatars.AvatarPicksReply -= Avatars_AvatarPicksReply;
        Client.Avatars.PickInfoReply -= Avatars_PickInfoReply;
        Client.Avatars.AvatarClassifiedReply -= Avatars_AvatarClassifiedReply;
        Client.Avatars.ClassifiedInfoReply -= Avatars_ClassifiedInfoReply;
        Client.Self.MuteListUpdated -= Self_MuteListUpdated;
    }

    private void Self_MuteListUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            IsMuted = Client.Self.MuteList.Values.Any(m => m.Type == MuteType.Resident && m.ID == AgentID));
    }

    private void RequestProfile()
    {
        // Always request via UDP — triggers groups, interests, and picks replies
        Client.Avatars.RequestAvatarProperties(AgentID);
        Client.Avatars.RequestAvatarNotes(AgentID);
        Client.Avatars.RequestAvatarPicks(AgentID);
        Client.Avatars.RequestAvatarClassified(AgentID);

        // Also try cap path for potentially faster property data
        if (Client.Avatars.AgentProfileAvailable())
        {
            _ = Client.Avatars.RequestAgentProfile(AgentID, OnAgentProfileReply);
        }
    }

    private void OnAgentProfileReply(bool success, AgentProfileMessage? profile)
    {
        if (!success || profile == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_gotProperties) return;

            _properties = new Avatar.AvatarProperties
            {
                AboutText = profile.SecondLifeAboutText,
                FirstLifeText = profile.FirstLifeAboutText,
                ProfileImage = profile.SecondLifeImageID,
                FirstLifeImage = profile.FirstLifeImageID,
                CharterMember = profile.CustomerType,
                BornOn = profile.MemberSince.ToShortDateString(),
                Partner = profile.PartnerID,
                Flags = profile.Flags,
            };
            PopulateFromProperties(_properties.Value);
            Notes = profile.Notes ?? string.Empty;
        });
    }

    private void Avatars_AvatarPropertiesReply(object? sender, AvatarPropertiesReplyEventArgs e)
    {
        if (e.AvatarID != AgentID) return;

        Dispatcher.UIThread.Post(() =>
        {
            _properties = e.Properties;
            _gotProperties = true;
            PopulateFromProperties(e.Properties);
        });
    }

    private void PopulateFromProperties(Avatar.AvatarProperties props)
    {
        BornOn = props.BornOn ?? string.Empty;
        AboutText = props.AboutText ?? string.Empty;
        FirstLifeText = props.FirstLifeText ?? string.Empty;
        WebUrl = props.ProfileURL ?? string.Empty;

        var info = string.Empty;
        if (DisplayName.EndsWith("Linden"))
            info = "Linden Lab Employee";
        else if (!string.IsNullOrEmpty(props.CharterMember))
            info = props.CharterMember;
        if (props.Identified) info += (info.Length > 0 ? ", " : "") + "Identified";
        if (props.Transacted) info += (info.Length > 0 ? ", " : "") + "Transacted";
        AccountInfo = info;

        if (props.Partner != UUID.Zero)
        {
            PartnerID = props.Partner;
            PartnerName = _instance.Names.Get(props.Partner);
        }

        IsLoading = false;

        if (props.ProfileImage != UUID.Zero)
            DownloadImage(props.ProfileImage, img => ProfileImage = img);
        if (props.FirstLifeImage != UUID.Zero)
            DownloadImage(props.FirstLifeImage, img => FirstLifeImage = img);
    }

    private void Avatars_AvatarGroupsReply(object? sender, AvatarGroupsReplyEventArgs e)
    {
        if (e.AvatarID != AgentID) return;

        Dispatcher.UIThread.Post(() =>
        {
            Groups.Clear();
            foreach (var g in e.Groups)
            {
                Groups.Add(new AvatarGroupEntry(g.GroupID, g.GroupName, g.GroupTitle));
            }
        });
    }

    private static readonly string[] WantToLabels =
        ["Build", "Explore", "Meet", "Group", "Buy", "Sell", "Be Hired", "Hire"];

    private static readonly string[] SkillsLabels =
        ["Textures", "Architecture", "Event Planning", "Modeling", "Scripting", "Custom Characters"];

    private void Avatars_AvatarInterestsReply(object? sender, AvatarInterestsReplyEventArgs e)
    {
        if (e.AvatarID != AgentID) return;

        Dispatcher.UIThread.Post(() =>
        {
            var interests = e.Interests;

            WantToText = interests.WantToText ?? string.Empty;
            SkillsText = interests.SkillsText ?? string.Empty;
            LanguagesText = interests.LanguagesText ?? string.Empty;

            var wantFlags = new List<string>();
            for (int i = 0; i < WantToLabels.Length; i++)
            {
                if ((interests.WantToMask & (1 << i)) != 0)
                    wantFlags.Add(WantToLabels[i]);
            }
            WantToFlags = string.Join(", ", wantFlags);

            var skillFlags = new List<string>();
            for (int i = 0; i < SkillsLabels.Length; i++)
            {
                if ((interests.SkillsMask & (1 << i)) != 0)
                    skillFlags.Add(SkillsLabels[i]);
            }
            SkillsFlags = string.Join(", ", skillFlags);

            HasInterests = !string.IsNullOrEmpty(WantToFlags) || !string.IsNullOrEmpty(WantToText) ||
                           !string.IsNullOrEmpty(SkillsFlags) || !string.IsNullOrEmpty(SkillsText) ||
                           !string.IsNullOrEmpty(LanguagesText);

            PopulateInterestFlags(interests);
        });
    }

    private void Avatars_AvatarNotesReply(object? sender, AvatarNotesReplyEventArgs e)
    {
        if (e.AvatarID != AgentID) return;
        Dispatcher.UIThread.Post(() => Notes = e.Notes ?? string.Empty);
    }

    private void Avatars_AvatarPicksReply(object? sender, AvatarPicksReplyEventArgs e)
    {
        if (e.AvatarID != AgentID) return;

        Dispatcher.UIThread.Post(() =>
        {
            Picks.Clear();
            foreach (var kvp in e.Picks)
            {
                Picks.Add(new AvatarPickEntry(kvp.Key, kvp.Value));
                Client.Avatars.RequestPickInfo(AgentID, kvp.Key);
            }
        });
    }

    private void Avatars_PickInfoReply(object? sender, PickInfoReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = 0; i < Picks.Count; i++)
            {
                if (Picks[i].PickID != e.PickID) continue;

                var pick = e.Pick;
                var location = !string.IsNullOrEmpty(pick.SimName)
                    ? $"{pick.SimName} ({(int)pick.PosGlobal.X % 256}, {(int)pick.PosGlobal.Y % 256}, {(int)pick.PosGlobal.Z})"
                    : string.Empty;

                Picks[i].Description = pick.Desc ?? string.Empty;
                Picks[i].Location = location;
                Picks[i].SnapshotID = pick.SnapshotID;
                Picks[i].TopPick = pick.TopPick;
                Picks[i].ParcelID = pick.ParcelID;
                Picks[i].PosGlobal = pick.PosGlobal;

                if (pick.SnapshotID != UUID.Zero)
                {
                    var pickEntry = Picks[i];
                    DownloadImage(pick.SnapshotID, img => pickEntry.Snapshot = img);
                }
                break;
            }
        });
    }

    [RelayCommand]
    private void OfferFriendship()
    {
        if (IsOwnProfile || IsFriend) return;
        Client.Friends.OfferFriendship(AgentID);
        IsFriendRequestSent = true;
    }

    [RelayCommand]
    private void RemoveFriend()
    {
        if (IsOwnProfile || !IsFriend) return;
        Client.Friends.TerminateFriendship(AgentID);
        IsFriend = false;
        _instance.ShowNotificationInChat($"Removed {DisplayName} from friends.");
    }

    [RelayCommand]
    private void SendIM()
    {
        if (IsOwnProfile) return;
        _instance.RequestIM(AgentID, DisplayName);
    }

    [RelayCommand]
    private void Pay()
    {
        if (IsOwnProfile) return;
        _instance.OpenPayWindow(AgentID, DisplayName, false);
    }

    [RelayCommand]
    private void OfferTeleport()
    {
        if (IsOwnProfile) return;
        Client.Self.SendTeleportLure(AgentID);
        _instance.ShowNotificationInChat($"Teleport offer sent to {DisplayName}.");
    }

    [RelayCommand]
    private void RequestTeleport()
    {
        if (IsOwnProfile) return;
        Client.Self.SendTeleportLureRequest(AgentID, "Please come!");
        _instance.ShowNotificationInChat($"Teleport request sent to {DisplayName}.");
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsOwnProfile) return;
        var legacyName = _instance.Names.GetLegacyName(AgentID);
        if (IsMuted)
        {
            var entry = Client.Self.MuteList.Find(m => m.Type == MuteType.Resident && m.ID == AgentID);
            if (entry != null)
                Client.Self.RemoveMuteListEntry(entry.ID, entry.Name);
            else
                Client.Self.RemoveMuteListEntry(AgentID, legacyName);
        }
        else
        {
            Client.Self.UpdateMuteListEntry(MuteType.Resident, AgentID, legacyName);
        }
        // IsMuted will be updated via Self_MuteListUpdated
    }

    [RelayCommand]
    private void OpenWebUrl()
    {
        if (string.IsNullOrWhiteSpace(WebUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WebUrl) { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private async Task SaveNotes(CancellationToken ct)
    {
        bool httpOk = false;
        if (Client.Avatars.AgentProfileAvailable())
        {
            try { await Client.Self.UpdateProfileNotesHttp(AgentID, Notes, ct); httpOk = true; }
            catch { }
        }
        if (!httpOk)
            Client.Self.UpdateProfileNotes(AgentID, Notes);
        VelesNotificationService.Show("Profile", $"Notes saved for {DisplayName}.");
    }

    [RelayCommand]
    private void ChangeDisplayName()
    {
        if (!IsOwnProfile) return;
        _instance.ShowChangeDisplayName();
    }

    [RelayCommand]
    private void OpenGroupProfile(AvatarGroupEntry? group)
    {
        if (group == null) return;
        _instance.ShowGroupProfile(group.GroupID);
    }

    // ── Classified event handlers ─────────────────────────────────────────────

    private void Avatars_AvatarClassifiedReply(object? sender, AvatarClassifiedReplyEventArgs e)
    {
        if (e.AvatarID != AgentID) return;

        Dispatcher.UIThread.Post(() =>
        {
            Classifieds.Clear();
            foreach (var kvp in e.Classifieds)
            {
                var entry = new AvatarClassifiedEntry(kvp.Key) { Name = kvp.Value };
                Classifieds.Add(entry);
                Client.Avatars.RequestClassifiedInfo(kvp.Key);
            }
        });
    }

    private void Avatars_ClassifiedInfoReply(object? sender, ClassifiedInfoReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = 0; i < Classifieds.Count; i++)
            {
                if (Classifieds[i].ClassifiedID != e.ClassifiedID) continue;

                var ad = e.Classified;
                Classifieds[i].Name = ad.Name ?? string.Empty;
                Classifieds[i].Description = ad.Desc ?? string.Empty;
                Classifieds[i].Category = (DirectoryManager.ClassifiedCategories)ad.Category;
                Classifieds[i].Price = (decimal)ad.Price;
                Classifieds[i].AutoRenew = (ad.ClassifiedFlags & 32) != 0;
                Classifieds[i].Position = ad.Position;
                Classifieds[i].SnapshotID = ad.SnapShotID;

                if (ad.SnapShotID != UUID.Zero)
                {
                    var entry = Classifieds[i];
                    DownloadImage(ad.SnapShotID, img => entry.Snapshot = img);
                }
                break;
            }
        });
    }

    // ── Pick editing commands ─────────────────────────────────────────────────

    [RelayCommand]
    private void AddPick()
    {
        if (!IsOwnProfile) return;
        var newPickID = UUID.Random();
        var pos = Client.Self.GlobalPosition;
        var entry = new AvatarPickEntry(newPickID, "New Pick") { PosGlobal = pos };
        Picks.Add(entry);
        Client.Self.PickInfoUpdate(newPickID, false, UUID.Zero, entry.Name, pos, UUID.Zero, string.Empty);
    }

    [RelayCommand]
    private void SavePick(AvatarPickEntry? pick)
    {
        if (pick == null || !IsOwnProfile) return;
        Client.Self.PickInfoUpdate(pick.PickID, pick.TopPick, pick.ParcelID,
            pick.Name, pick.PosGlobal, pick.SnapshotID, pick.Description);
        _instance.ShowNotificationInChat($"Pick '{pick.Name}' saved.");
    }

    [RelayCommand]
    private void DeletePick(AvatarPickEntry? pick)
    {
        if (pick == null || !IsOwnProfile) return;
        Client.Self.PickDelete(pick.PickID);
        Picks.Remove(pick);
    }

    // ── Classified editing commands ───────────────────────────────────────────

    [RelayCommand]
    private void AddClassified()
    {
        if (!IsOwnProfile) return;
        var entry = new AvatarClassifiedEntry(UUID.Random(), isNew: true)
        {
            Name = "New Classified",
            Position = Client.Self.GlobalPosition,
        };
        Classifieds.Add(entry);
    }

    [RelayCommand]
    private void SaveClassified(AvatarClassifiedEntry? classified)
    {
        if (classified == null || !IsOwnProfile) return;
        if (string.IsNullOrWhiteSpace(classified.Name))
        {
            _instance.ShowNotificationInChat("Classified must have a name.");
            return;
        }
        Client.Self.UpdateClassifiedInfo(classified.ClassifiedID, classified.Category,
            classified.SnapshotID, (int)classified.Price, classified.Position,
            classified.Name, classified.Description, classified.AutoRenew);
        _instance.ShowNotificationInChat($"Classified '{classified.Name}' saved.");
    }

    [RelayCommand]
    private void DeleteClassified(AvatarClassifiedEntry? classified)
    {
        if (classified == null || !IsOwnProfile) return;
        if (!classified.IsNew)
            Client.Self.DeleteClassified(classified.ClassifiedID);
        Classifieds.Remove(classified);
    }

    // ── Own-profile editing ───────────────────────────────────────────────────

    [RelayCommand]
    private void BeginEdit()
    {
        _savedAboutText = AboutText;
        _savedFirstLifeText = FirstLifeText;
        _savedWebUrl = WebUrl;
        _savedWantToText = WantToText;
        _savedSkillsText = SkillsText;
        _savedLanguagesText = LanguagesText;
        _savedWantToMask = BuildWantToMask();
        _savedSkillsMask = BuildSkillsMask();
        IsEditing = true;
    }

    [RelayCommand]
    private void DiscardEdit()
    {
        AboutText = _savedAboutText;
        FirstLifeText = _savedFirstLifeText;
        WebUrl = _savedWebUrl;
        WantToText = _savedWantToText;
        SkillsText = _savedSkillsText;
        LanguagesText = _savedLanguagesText;
        PopulateInterestFlags(new Avatar.Interests
        {
            WantToMask = _savedWantToMask,
            SkillsMask = _savedSkillsMask,
        });
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveProfile(CancellationToken ct)
    {
        if (_properties == null) return;

        var props = _properties.Value;
        props.AboutText = AboutText;
        props.FirstLifeText = FirstLifeText;
        props.ProfileURL = WebUrl;

        bool httpOk = false;
        if (Client.Avatars.AgentProfileAvailable())
        {
            try { await Client.Self.UpdateProfileHttp(props, ct); httpOk = true; }
            catch { }
        }
        if (!httpOk)
            Client.Self.UpdateProfileUdp(props);

        _properties = props;
        Client.Self.UpdateInterests(BuildInterests());

        IsEditing = false;
        _instance.ShowNotificationInChat("Profile saved.");
    }

    private uint BuildWantToMask()
    {
        uint m = 0;
        if (WantToBuild)    m |= 1u << 0;
        if (WantToExplore)  m |= 1u << 1;
        if (WantToMeet)     m |= 1u << 2;
        if (WantToGroup)    m |= 1u << 3;
        if (WantToBuy)      m |= 1u << 4;
        if (WantToSell)     m |= 1u << 5;
        if (WantToBeHired)  m |= 1u << 6;
        if (WantToHire)     m |= 1u << 7;
        return m;
    }

    private uint BuildSkillsMask()
    {
        uint m = 0;
        if (SkillTextures)          m |= 1u << 0;
        if (SkillArchitecture)      m |= 1u << 1;
        if (SkillEventPlanning)     m |= 1u << 2;
        if (SkillModeling)          m |= 1u << 3;
        if (SkillScripting)         m |= 1u << 4;
        if (SkillCustomCharacters)  m |= 1u << 5;
        return m;
    }

    private Avatar.Interests BuildInterests() => new()
    {
        WantToMask    = BuildWantToMask(),
        SkillsMask    = BuildSkillsMask(),
        WantToText    = WantToText,
        SkillsText    = SkillsText,
        LanguagesText = LanguagesText,
    };

    private void PopulateInterestFlags(Avatar.Interests interests)
    {
        WantToBuild           = (interests.WantToMask & (1u << 0)) != 0;
        WantToExplore         = (interests.WantToMask & (1u << 1)) != 0;
        WantToMeet            = (interests.WantToMask & (1u << 2)) != 0;
        WantToGroup           = (interests.WantToMask & (1u << 3)) != 0;
        WantToBuy             = (interests.WantToMask & (1u << 4)) != 0;
        WantToSell            = (interests.WantToMask & (1u << 5)) != 0;
        WantToBeHired         = (interests.WantToMask & (1u << 6)) != 0;
        WantToHire            = (interests.WantToMask & (1u << 7)) != 0;

        SkillTextures         = (interests.SkillsMask & (1u << 0)) != 0;
        SkillArchitecture     = (interests.SkillsMask & (1u << 1)) != 0;
        SkillEventPlanning    = (interests.SkillsMask & (1u << 2)) != 0;
        SkillModeling         = (interests.SkillsMask & (1u << 3)) != 0;
        SkillScripting        = (interests.SkillsMask & (1u << 4)) != 0;
        SkillCustomCharacters = (interests.SkillsMask & (1u << 5)) != 0;
    }

    private void DownloadImage(UUID textureId, Action<Bitmap?> setter)
    {
        GridTextureHelper.Download(Client, textureId, bitmap =>
        {
            setter(bitmap);
        });
    }

    [RelayCommand]
    private void ShareItem()
    {
        if (IsOwnProfile) return;
        _instance.ShowInventoryPicker(
            $"Share item with {DisplayName}",
            null,
            entry =>
            {
                Client.Inventory.GiveItem(entry.ItemId, entry.Name, entry.AssetType, AgentID, true);
                _instance.ShowNotificationInChat($"Offered '{entry.Name}' to {DisplayName}.");
            },
            item => (item.Permissions.OwnerMask & PermissionMask.Transfer) != 0);
    }

    /// <summary>
    /// Gives an inventory item or folder to this avatar.
    /// Respects the Transfer permission — no-transfer items are blocked with a chat notice.
    /// </summary>
    public void GiveInventoryNode(InvTreeNode node)
    {
        Client.Inventory.Store!.TryGetValue(node.ItemId, out InventoryBase? invBase);
        if (invBase == null)
        {
            _instance.ShowNotificationInChat($"Could not locate item '{node.Name}' in inventory.");
            return;
        }

        switch (invBase)
        {
            case InventoryItem item:
                if ((item.Permissions.OwnerMask & PermissionMask.Transfer) == 0)
                {
                    _instance.ShowNotificationInChat(
                        $"Cannot give '{item.Name}' — item has no-transfer permissions.");
                    return;
                }
                Client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, AgentID, true);
                _instance.ShowNotificationInChat($"Offered '{item.Name}' to {DisplayName}.");
                break;

            case InventoryFolder folder:
                Client.Inventory.GiveFolder(folder.UUID, folder.Name, AgentID, true);
                _instance.ShowNotificationInChat($"Offered folder '{folder.Name}' to {DisplayName}.");
                break;
        }
    }
}

public record AvatarGroupEntry(UUID GroupID, string GroupName, string GroupTitle);

public partial class AvatarPickEntry : ObservableObject
{
    public UUID PickID { get; }
    public bool TopPick { get; set; }
    public UUID ParcelID { get; set; } = UUID.Zero;
    public Vector3d PosGlobal { get; set; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private UUID _snapshotID = UUID.Zero;
    [ObservableProperty] private Bitmap? _snapshot;

    public AvatarPickEntry(UUID pickID, string name)
    {
        PickID = pickID;
        _name = name;
    }
}

public partial class AvatarClassifiedEntry : ObservableObject
{
    public UUID ClassifiedID { get; }
    public bool IsNew { get; }
    public Vector3d Position { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryName))]
    private DirectoryManager.ClassifiedCategories _category = DirectoryManager.ClassifiedCategories.Any;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private decimal _price = 50m;
    [ObservableProperty] private bool _autoRenew = true;
    [ObservableProperty] private UUID _snapshotID = UUID.Zero;
    [ObservableProperty] private Bitmap? _snapshot;

    public string CategoryName => Category.ToString();

    public static IReadOnlyList<DirectoryManager.ClassifiedCategories> AllCategories { get; } =
        Enum.GetValues<DirectoryManager.ClassifiedCategories>().ToList();

    public AvatarClassifiedEntry(UUID classifiedID, bool isNew = false)
    {
        ClassifiedID = classifiedID;
        IsNew = isNew;
    }
}
