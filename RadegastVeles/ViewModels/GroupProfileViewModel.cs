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
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class GroupProfileViewModel : ObservableObject, IDisposable, IChatContext
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    public RadegastInstanceAvalonia Instance => _instance;
    public UUID GroupID { get; }

    // ── General ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _groupName = string.Empty;
    [ObservableProperty] private string _charter = string.Empty;
    [ObservableProperty] private string _founderName = string.Empty;
    [ObservableProperty] private string _memberCount = string.Empty;
    [ObservableProperty] private string _enrollmentInfo = string.Empty;
    [ObservableProperty] private string _groupIdText = string.Empty;
    [ObservableProperty] private Bitmap? _insigniaImage;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isMember;
    [ObservableProperty] private bool _canJoin;
    [ObservableProperty] private bool _showInSearch;
    [ObservableProperty] private bool _openEnrollment;
    [ObservableProperty] private bool _maturePublish;
    [ObservableProperty] private string _statusText = string.Empty;

    // Active title selector (member only)
    public ObservableCollection<GroupTitleEntry> Titles { get; } = [];
    [ObservableProperty] private GroupTitleEntry? _selectedTitle;
    [ObservableProperty] private bool _hasTitles;
    private bool _suppressTitleChange;

    // Editable settings (permission-gated)
    [ObservableProperty] private string _editCharter = string.Empty;
    [ObservableProperty] private bool _editShowInSearch;
    [ObservableProperty] private bool _editOpenEnrollment;
    [ObservableProperty] private int _editEnrollmentFee;
    [ObservableProperty] private bool _editMature;
    [ObservableProperty] private bool _acceptNotices;
    [ObservableProperty] private bool _listInProfile;
    [ObservableProperty] private bool _canEditOptions;
    [ObservableProperty] private bool _canEditIdentity;
    [ObservableProperty] private bool _settingsChanged;
    private bool _suppressSettingsChange;

    // ── Members tab ───────────────────────────────────────────────────────────
    public ObservableCollection<GroupMemberEntry> Members { get; } = [];
    [ObservableProperty] private GroupMemberEntry? _selectedMember;
    [ObservableProperty] private bool _canEject;
    [ObservableProperty] private bool _canInvite;
    [ObservableProperty] private bool _canAssignMember;
    [ObservableProperty] private bool _canRemoveMember;
    [ObservableProperty] private AvatarPickerEntry? _pickedInviteAvatar;
    [ObservableProperty] private string _pickedInviteName = string.Empty;
    [ObservableProperty] private GroupRoleEntry? _selectedInviteRole;
    [ObservableProperty] private string _inviteStatusText = string.Empty;

    // ── Roles tab ─────────────────────────────────────────────────────────────
    public ObservableCollection<GroupRoleEntry> Roles { get; } = [];
    [ObservableProperty] private GroupRoleEntry? _selectedRole;
    [ObservableProperty] private bool _canCreateRole;
    [ObservableProperty] private bool _canDeleteRole;
    [ObservableProperty] private bool _canEditRoleProps;
    [ObservableProperty] private bool _canEditRolePowers;
    [ObservableProperty] private bool _isNewRole;
    [ObservableProperty] private string _editRoleName = string.Empty;
    [ObservableProperty] private string _editRoleTitle = string.Empty;
    [ObservableProperty] private string _editRoleDescription = string.Empty;
    public ObservableCollection<GroupPowerEntry> EditRolePowers { get; } = [];
    public ObservableCollection<GroupRoleMemberEntry> SelectedRoleMembers { get; } = [];
    [ObservableProperty] private bool _isNotEveryoneRole;
    private List<KeyValuePair<UUID, UUID>>? _roleMembers;
    private UUID _groupRolesRequestId;
    private UUID _groupRolesMembersRequestId;
    private UUID _groupTitlesRequestId;

    // ── Notices tab ───────────────────────────────────────────────────────────
    public ObservableCollection<GroupNoticeEntry> Notices { get; } = [];
    [ObservableProperty] private GroupNoticeEntry? _selectedNotice;
    [ObservableProperty] private string _selectedNoticeBody = string.Empty;
    [ObservableProperty] private bool _isNoticeBodyLoading;
    [ObservableProperty] private bool _hasNoticeAttachment;
    [ObservableProperty] private string _noticeAttachmentName = string.Empty;
    private InstantMessage? _pendingNoticeIM;
    [ObservableProperty] private bool _canSendNotices;
    [ObservableProperty] private bool _showNewNoticeForm;
    [ObservableProperty] private string _newNoticeSubject = string.Empty;
    [ObservableProperty] private string _newNoticeBody = string.Empty;

    private InventoryPickerEntry? _newNoticeAttachment;
    public InventoryPickerEntry? NewNoticeAttachment
    {
        get => _newNoticeAttachment;
        set => SetProperty(ref _newNoticeAttachment, value);
    }

    // ── Banned tab ────────────────────────────────────────────────────────────
    public ObservableCollection<GroupBanEntry> BannedMembers { get; } = [];
    [ObservableProperty] private GroupBanEntry? _selectedBanEntry;
    [ObservableProperty] private bool _isBanListLoaded;

    private Group? _group;

    public GroupProfileViewModel(RadegastInstanceAvalonia instance, UUID groupId)
    {
        _instance = instance;
        GroupID = groupId;
        GroupIdText = groupId.ToString();
        IsMember = instance.Groups.ContainsKey(groupId);

        Client.Groups.GroupProfile += Groups_GroupProfile;
        Client.Groups.GroupMembersReply += Groups_GroupMembersReply;
        Client.Groups.GroupNoticesListReply += Groups_GroupNoticesListReply;
        Client.Groups.GroupJoinedReply += Groups_GroupJoinedReply;
        Client.Groups.GroupLeaveReply += Groups_GroupLeaveReply;
        Client.Groups.GroupTitlesReply += Groups_GroupTitlesReply;
        Client.Groups.GroupRoleDataReply += Groups_GroupRoleDataReply;
        Client.Groups.GroupRoleMembersReply += Groups_GroupRoleMembersReply;
        Client.Groups.GroupMemberEjected += Groups_GroupMemberEjected;
        Client.Self.IM += Self_IM;
        _instance.Names.NameUpdated += Names_NameUpdated;

        Client.Groups.RequestGroupProfile(groupId);

        if (IsMember)
        {
            Client.Groups.RequestGroupMembers(groupId);
            Client.Groups.RequestGroupNoticesList(groupId);
            LoadBannedMembers(groupId);
            _groupTitlesRequestId = Client.Groups.RequestGroupTitles(groupId);
            _groupRolesRequestId = Client.Groups.RequestGroupRoles(groupId);
        }
    }

    public void Dispose()
    {
        Client.Groups.GroupProfile -= Groups_GroupProfile;
        Client.Groups.GroupMembersReply -= Groups_GroupMembersReply;
        Client.Groups.GroupNoticesListReply -= Groups_GroupNoticesListReply;
        Client.Groups.GroupJoinedReply -= Groups_GroupJoinedReply;
        Client.Groups.GroupLeaveReply -= Groups_GroupLeaveReply;
        Client.Groups.GroupTitlesReply -= Groups_GroupTitlesReply;
        Client.Groups.GroupRoleDataReply -= Groups_GroupRoleDataReply;
        Client.Groups.GroupRoleMembersReply -= Groups_GroupRoleMembersReply;
        Client.Groups.GroupMemberEjected -= Groups_GroupMemberEjected;
        Client.Self.IM -= Self_IM;
        _instance.Names.NameUpdated -= Names_NameUpdated;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasPower(GroupPowers power)
    {
        if (!_instance.Groups.TryGetValue(GroupID, out var g)) return false;
        return (g.Powers & power) != 0;
    }

    private void UpdatePermissions()
    {
        CanEditOptions   = HasPower(GroupPowers.ChangeOptions);
        CanEditIdentity  = HasPower(GroupPowers.ChangeIdentity);
        CanEject         = HasPower(GroupPowers.Eject);
        CanInvite        = HasPower(GroupPowers.Invite);
        CanCreateRole    = HasPower(GroupPowers.CreateRole);
        CanDeleteRole    = HasPower(GroupPowers.DeleteRole);
        CanEditRoleProps = HasPower(GroupPowers.RoleProperties);
        CanEditRolePowers = HasPower(GroupPowers.ChangeActions);
        CanAssignMember  = HasPower(GroupPowers.AssignMember) || HasPower(GroupPowers.AssignMemberLimited);
        CanRemoveMember  = HasPower(GroupPowers.RemoveMember);
        CanSendNotices   = HasPower(GroupPowers.SendNotices);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void Groups_GroupProfile(object? sender, GroupProfileEventArgs e)
    {
        if (e.Group.ID != GroupID) return;
        Dispatcher.UIThread.Post(() =>
        {
            _group = e.Group;
            GroupName    = e.Group.Name;
            Charter      = (e.Group.Charter ?? string.Empty).Replace("\n", Environment.NewLine);
            FounderName  = "Founded by: " + _instance.Names.Get(e.Group.FounderID);
            MemberCount  = $"{e.Group.GroupMembershipCount} members";
            ShowInSearch = e.Group.ShowInList;
            OpenEnrollment = e.Group.OpenEnrollment;
            MaturePublish  = e.Group.MaturePublish;

            EnrollmentInfo = e.Group.MembershipFee > 0
                ? $"Enrollment fee: L${e.Group.MembershipFee}"
                : "Free to join";

            IsMember = _instance.Groups.ContainsKey(GroupID);
            CanJoin  = !IsMember && e.Group.OpenEnrollment;

            // Populate editable settings without triggering the change flag
            _suppressSettingsChange = true;
            EditCharter        = e.Group.Charter ?? string.Empty;
            EditShowInSearch   = e.Group.ShowInList;
            EditOpenEnrollment = e.Group.OpenEnrollment;
            EditEnrollmentFee  = e.Group.MembershipFee;
            EditMature         = e.Group.MaturePublish;
            _suppressSettingsChange = false;
            SettingsChanged = false;

            if (_instance.Groups.TryGetValue(GroupID, out var myGroup))
            {
                _suppressSettingsChange = true;
                AcceptNotices  = myGroup.AcceptNotices;
                ListInProfile  = myGroup.ListInProfile;
                _suppressSettingsChange = false;
            }

            UpdatePermissions();
            IsLoading = false;

            if (e.Group.InsigniaID != UUID.Zero)
                GridTextureHelper.Download(Client, e.Group.InsigniaID, img => InsigniaImage = img);
        });
    }

    private void Groups_GroupTitlesReply(object? sender, GroupTitlesReplyEventArgs e)
    {
        if (e.RequestID != _groupTitlesRequestId) return;
        Dispatcher.UIThread.Post(() =>
        {
            _suppressTitleChange = true;
            Titles.Clear();
            GroupTitleEntry? selected = null;
            foreach (var title in e.Titles.Values)
            {
                var entry = new GroupTitleEntry(title.RoleID, title.Title, title.Selected);
                Titles.Add(entry);
                if (title.Selected) selected = entry;
            }
            HasTitles     = Titles.Count > 0;
            SelectedTitle = selected ?? Titles.FirstOrDefault();
            _suppressTitleChange = false;
        });
    }

    partial void OnSelectedTitleChanged(GroupTitleEntry? value)
    {
        if (_suppressTitleChange || value == null) return;
        Client.Groups.ActivateTitle(GroupID, value.RoleID);
    }

    private void Groups_GroupMembersReply(object? sender, GroupMembersReplyEventArgs e)
    {
        if (e.GroupID != GroupID) return;
        Dispatcher.UIThread.Post(() =>
        {
            Members.Clear();
            foreach (var kvp in e.Members.OrderBy(m => _instance.Names.Get(m.Key), StringComparer.OrdinalIgnoreCase))
                Members.Add(new GroupMemberEntry(kvp.Key, _instance.Names.Get(kvp.Key), kvp.Value.Title));
        });
    }

    private void Names_NameUpdated(object? sender, UUIDNameReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var kvp in e.Names)
            {
                var idx = Members.IndexOf(Members.FirstOrDefault(m => m.AgentID == kvp.Key)!);
                if (idx >= 0)
                    Members[idx] = new GroupMemberEntry(Members[idx].AgentID, kvp.Value, Members[idx].Title);
            }
            if (_roleMembers != null && SelectedRole != null)
                RefreshSelectedRoleMembers();
        });
    }

    private void Groups_GroupMemberEjected(object? sender, GroupOperationEventArgs e)
    {
        if (e.GroupID != GroupID) return;
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = e.Success ? "Member ejected." : "Failed to eject member.";
            if (e.Success) Client.Groups.RequestGroupMembers(GroupID);
        });
    }

    private void Groups_GroupRoleDataReply(object? sender, GroupRolesDataReplyEventArgs e)
    {
        if (e.GroupID != GroupID || e.RequestID != _groupRolesRequestId) return;
        _groupRolesMembersRequestId = Client.Groups.RequestGroupRolesMembers(GroupID);
        Dispatcher.UIThread.Post(() =>
        {
            var prevId = SelectedRole?.RoleID;
            Roles.Clear();
            foreach (var role in e.Roles.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                Roles.Add(new GroupRoleEntry(role.ID, role.Name, role.Title, role.Description, role.Powers, 0));

            SelectedInviteRole = Roles.FirstOrDefault(r => r.RoleID == UUID.Zero) ?? Roles.FirstOrDefault();
            if (prevId.HasValue)
                SelectedRole = Roles.FirstOrDefault(r => r.RoleID == prevId.Value);
        });
    }

    private void Groups_GroupRoleMembersReply(object? sender, GroupRolesMembersReplyEventArgs e)
    {
        if (e.GroupID != GroupID || e.RequestID != _groupRolesMembersRequestId) return;
        _roleMembers = e.RolesMembers;
        Dispatcher.UIThread.Post(() =>
        {
            // Update member counts on roles now that we have the membership data
            foreach (var r in Roles)
                r.MemberCount = _roleMembers.Count(kv => kv.Key == r.RoleID);
            RefreshSelectedRoleMembers();
        });
    }

    partial void OnSelectedRoleChanged(GroupRoleEntry? value)
    {
        EditRoleName        = value?.Name ?? string.Empty;
        EditRoleTitle       = value?.Title ?? string.Empty;
        EditRoleDescription = value?.Description ?? string.Empty;
        IsNewRole           = false;
        IsNotEveryoneRole   = value?.RoleID != UUID.Zero;
        PopulateEditRolePowers(value?.Powers ?? GroupPowers.None);
        RefreshSelectedRoleMembers();
    }

    private void PopulateEditRolePowers(GroupPowers currentPowers)
    {
        EditRolePowers.Clear();
        foreach (GroupPowers p in Enum.GetValues(typeof(GroupPowers)))
        {
            if (p == GroupPowers.None) continue;
            ulong v = (ulong)p;
            if ((v & (v - 1)) != 0) continue; // skip composite flags
            EditRolePowers.Add(new GroupPowerEntry(p, (currentPowers & p) != 0));
        }
    }

    private void RefreshSelectedRoleMembers()
    {
        SelectedRoleMembers.Clear();
        if (SelectedRole == null || _roleMembers == null) return;
        var roleId = SelectedRole.RoleID;
        if (roleId == UUID.Zero)
        {
            foreach (var m in Members)
                SelectedRoleMembers.Add(new GroupRoleMemberEntry(m.AgentID, m.Name));
        }
        else
        {
            foreach (var kvp in _roleMembers.Where(kv => kv.Key == roleId))
                SelectedRoleMembers.Add(new GroupRoleMemberEntry(kvp.Value, _instance.Names.Get(kvp.Value)));
        }
    }

    private void Groups_GroupNoticesListReply(object? sender, GroupNoticesListReplyEventArgs e)
    {
        if (e.GroupID != GroupID) return;
        Dispatcher.UIThread.Post(() =>
        {
            Notices.Clear();
            foreach (var notice in e.Notices.OrderByDescending(n => n.Timestamp))
            {
                var date = notice.Timestamp != 0
                    ? Utils.UnixTimeToDateTime(notice.Timestamp).ToShortDateString()
                    : string.Empty;
                Notices.Add(new GroupNoticeEntry(notice.NoticeID, notice.Subject, notice.FromName, date));
            }
        });
    }

    private void Self_IM(object? sender, InstantMessageEventArgs e)
    {
        if (e.IM.Dialog != InstantMessageDialog.GroupNoticeRequested) return;
        UUID groupId = e.IM.BinaryBucket.Length >= 18
            ? new UUID(e.IM.BinaryBucket, 2)
            : e.IM.FromAgentID;
        if (groupId != GroupID) return;

        Dispatcher.UIThread.Post(() =>
        {
            IsNoticeBodyLoading  = false;
            HasNoticeAttachment  = false;
            NoticeAttachmentName = string.Empty;
            _pendingNoticeIM     = null;

            if (e.IM.BinaryBucket.Length > 18 && e.IM.BinaryBucket[0] != 0)
            {
                HasNoticeAttachment  = true;
                NoticeAttachmentName = Utils.BytesToString(e.IM.BinaryBucket, 18, e.IM.BinaryBucket.Length - 19);
                _pendingNoticeIM     = e.IM;
            }

            var msg = e.IM.Message ?? string.Empty;
            var sep = msg.IndexOf('|');
            SelectedNoticeBody = (sep >= 0 ? msg.Substring(sep + 1) : msg)
                .Replace("\n", Environment.NewLine);
        });
    }

    partial void OnSelectedNoticeChanged(GroupNoticeEntry? value)
    {
        SelectedNoticeBody   = string.Empty;
        HasNoticeAttachment  = false;
        NoticeAttachmentName = string.Empty;
        _pendingNoticeIM     = null;
        ShowNewNoticeForm    = false;
        if (value == null) return;
        IsNoticeBodyLoading = true;
        Client.Groups.RequestGroupNotice(value.NoticeID);
    }

    private void Groups_GroupJoinedReply(object? sender, GroupOperationEventArgs e)
    {
        if (e.GroupID != GroupID) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Success)
            {
                IsMember = true;
                CanJoin  = false;
                _instance.ShowNotificationInChat($"Joined group {GroupName}");
                Client.Groups.RequestGroupMembers(GroupID);
                Client.Groups.RequestGroupNoticesList(GroupID);
                LoadBannedMembers(GroupID);
                _groupTitlesRequestId = Client.Groups.RequestGroupTitles(GroupID);
                _groupRolesRequestId  = Client.Groups.RequestGroupRoles(GroupID);
            }
            else
            {
                _instance.ShowNotificationInChat($"Failed to join group {GroupName}");
            }
        });
    }

    private void Groups_GroupLeaveReply(object? sender, GroupOperationEventArgs e)
    {
        if (e.GroupID != GroupID) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Success)
            {
                IsMember = false;
                CanJoin  = _group?.OpenEnrollment == true;
                _instance.ShowNotificationInChat($"Left group {GroupName}");
                Members.Clear(); Notices.Clear(); Roles.Clear();
                Titles.Clear();  BannedMembers.Clear();
                IsBanListLoaded = false;
                HasTitles       = false;
                UpdatePermissions();
            }
        });
    }

    // ── Settings change tracking ──────────────────────────────────────────────

    partial void OnEditCharterChanged(string value)        { if (!_suppressSettingsChange) SettingsChanged = true; }
    partial void OnEditShowInSearchChanged(bool value)     { if (!_suppressSettingsChange) SettingsChanged = true; }
    partial void OnEditOpenEnrollmentChanged(bool value)   { if (!_suppressSettingsChange) SettingsChanged = true; }
    partial void OnEditEnrollmentFeeChanged(int value)     { if (!_suppressSettingsChange) SettingsChanged = true; }
    partial void OnEditMatureChanged(bool value)           { if (!_suppressSettingsChange) SettingsChanged = true; }

    partial void OnAcceptNoticesChanged(bool value)
    {
        if (_suppressSettingsChange || !IsMember) return;
        Client.Groups.SetGroupAcceptNotices(GroupID, value, ListInProfile);
    }

    partial void OnListInProfileChanged(bool value)
    {
        if (_suppressSettingsChange || !IsMember) return;
        Client.Groups.SetGroupAcceptNotices(GroupID, AcceptNotices, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void JoinGroup()
    {
        if (IsMember) return;
        Client.Groups.RequestJoinGroup(GroupID);
    }

    [RelayCommand]
    private void LeaveGroup()
    {
        if (!IsMember) return;
        Client.Groups.LeaveGroup(GroupID);
    }

    [RelayCommand]
    private void OpenGroupChat()
    {
        if (!IsMember) return;
        _instance.RequestGroupIM(GroupID, GroupName);
    }

    [RelayCommand]
    private async Task JoinGroupVoice()
    {
        if (!IsMember || _instance.Voice == null) return;
        await _instance.Voice.JoinGroupVoiceAsync(GroupID);
    }

    [RelayCommand]
    private void ActivateGroup()
    {
        if (!IsMember) return;
        Client.Groups.ActivateGroup(GroupID);
        StatusText = $"Activated {GroupName}";
    }

    [RelayCommand]
    private void ShowFounderProfile()
    {
        if (_group is not { } g || g.FounderID == UUID.Zero) return;
        _instance.ShowAgentProfile(_instance.Names.Get(g.FounderID), g.FounderID);
    }

    [RelayCommand]
    private void ShowMemberProfile(GroupMemberEntry? member)
    {
        if (member == null) return;
        _instance.ShowAgentProfile(member.Name, member.AgentID);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (_group == null) return;
        var updated = _group.Value;
        updated.Charter       = EditCharter;
        updated.ShowInList    = EditShowInSearch;
        updated.OpenEnrollment = EditOpenEnrollment;
        updated.MembershipFee = EditEnrollmentFee;
        updated.MaturePublish = EditMature;
        Client.Groups.UpdateGroup(GroupID, updated);
        SettingsChanged = false;
        StatusText = "Settings saved.";
    }

    [RelayCommand]
    private void EjectMember()
    {
        if (SelectedMember == null || !CanEject) return;
        Client.Groups.EjectUser(GroupID, SelectedMember.AgentID);
    }

    [RelayCommand]
    private void PickInviteAvatar()
    {
        _instance.ShowAvatarPicker("Invite to Group", entry =>
        {
            PickedInviteAvatar = entry;
            PickedInviteName   = entry.Name;
        });
    }

    [RelayCommand]
    private void ClearInvitePick()
    {
        PickedInviteAvatar = null;
        PickedInviteName   = string.Empty;
        InviteStatusText   = string.Empty;
    }

    [RelayCommand]
    private void InviteMember()
    {
        if (!CanInvite || PickedInviteAvatar == null) return;
        var roleIds = new List<UUID> { SelectedInviteRole?.RoleID ?? UUID.Zero };
        Client.Groups.Invite(GroupID, roleIds, PickedInviteAvatar.Id);
        PickedInviteAvatar = null;
        PickedInviteName   = string.Empty;
        InviteStatusText   = "Invitation sent.";
    }

    [RelayCommand]
    private void AddMemberToRole()
    {
        if (SelectedRole == null || SelectedRole.RoleID == UUID.Zero || !CanAssignMember) return;
        var role = SelectedRole;
        _instance.ShowAvatarPicker($"Add to role: {role.Name}", entry =>
        {
            Client.Groups.AddToRole(GroupID, role.RoleID, entry.Id);
            _ = Task.Delay(1000).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() =>
                    _groupRolesMembersRequestId = Client.Groups.RequestGroupRolesMembers(GroupID)));
        });
    }

    [RelayCommand]
    private void RemoveMemberFromRole(GroupRoleMemberEntry? entry)
    {
        if (entry == null || SelectedRole == null || SelectedRole.RoleID == UUID.Zero || !CanRemoveMember) return;
        Client.Groups.RemoveFromRole(GroupID, SelectedRole.RoleID, entry.AgentID);
        SelectedRoleMembers.Remove(entry);
    }

    [RelayCommand]
    private void NewRole()
    {
        SelectedRole        = null;
        IsNewRole           = true;
        EditRoleName        = string.Empty;
        EditRoleTitle       = string.Empty;
        EditRoleDescription = string.Empty;
        PopulateEditRolePowers(GroupPowers.None);
        SelectedRoleMembers.Clear();
    }

    [RelayCommand]
    private void SaveRole()
    {
        if (IsNewRole)
        {
            Client.Groups.CreateRole(GroupID, new GroupRole
            {
                Name        = EditRoleName,
                Title       = EditRoleTitle,
                Description = EditRoleDescription,
                Powers      = GetCurrentEditPowers()
            });
        }
        else if (SelectedRole != null)
        {
            Client.Groups.UpdateRole(new GroupRole
            {
                GroupID     = GroupID,
                ID          = SelectedRole.RoleID,
                Name        = CanEditRoleProps  ? EditRoleName        : SelectedRole.Name,
                Title       = CanEditRoleProps  ? EditRoleTitle       : SelectedRole.Title,
                Description = CanEditRoleProps  ? EditRoleDescription : SelectedRole.Description,
                Powers      = CanEditRolePowers ? GetCurrentEditPowers() : SelectedRole.Powers
            });
        }
        IsNewRole  = false;
        StatusText = "Role saved.";
        _groupRolesRequestId = Client.Groups.RequestGroupRoles(GroupID);
    }

    [RelayCommand]
    private void DeleteRole()
    {
        if (SelectedRole == null || SelectedRole.RoleID == UUID.Zero) return;
        Client.Groups.DeleteRole(GroupID, SelectedRole.RoleID);
        SelectedRole = null;
        StatusText   = "Role deleted.";
        _groupRolesRequestId = Client.Groups.RequestGroupRoles(GroupID);
    }

    private GroupPowers GetCurrentEditPowers()
    {
        var powers = GroupPowers.None;
        foreach (var p in EditRolePowers)
            if (p.IsEnabled) powers |= p.Power;
        return powers;
    }

    [RelayCommand]
    private void BeginNewNotice()
    {
        ShowNewNoticeForm = true;
        SelectedNotice    = null;
        NewNoticeSubject  = string.Empty;
        NewNoticeBody     = string.Empty;
        NewNoticeAttachment = null;
    }

    [RelayCommand]
    private void CancelNewNotice()
    {
        ShowNewNoticeForm = false;
        NewNoticeSubject  = string.Empty;
        NewNoticeBody     = string.Empty;
        NewNoticeAttachment = null;
    }

    [RelayCommand]
    private void SendNotice()
    {
        if (string.IsNullOrWhiteSpace(NewNoticeSubject)) return;
        var notice = new GroupNotice
        {
            Subject = NewNoticeSubject,
            Message = NewNoticeBody
        };
        if (_newNoticeAttachment != null)
        {
            notice.OwnerID     = Client.Self.AgentID;
            notice.AttachmentID = _newNoticeAttachment.ItemId;
        }
        Client.Groups.SendGroupNotice(GroupID, notice);
        ShowNewNoticeForm = false;
        NewNoticeSubject  = string.Empty;
        NewNoticeBody     = string.Empty;
        NewNoticeAttachment = null;
        StatusText = "Notice sent.";
        Client.Groups.RequestGroupNoticesList(GroupID);
    }

    [RelayCommand]
    private void PickNoticeAttachment()
    {
        _instance.ShowInventoryPicker(
            "Select notice attachment",
            null,
            entry => { NewNoticeAttachment = entry; },
            item => (item.Permissions.OwnerMask & PermissionMask.Transfer) != 0);
    }

    [RelayCommand]
    private void ClearNoticeAttachment()
    {
        NewNoticeAttachment = null;
    }

    [RelayCommand]
    private void AcceptNoticeAttachment()
    {
        if (_pendingNoticeIM is not { } msg) return;
        var assetType  = msg.BinaryBucket.Length > 1 ? (AssetType)msg.BinaryBucket[1] : AssetType.Unknown;
        var destFolder = Client.Inventory.FindFolderForType(assetType);
        Client.Self.InstantMessage(
            Client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID,
            InstantMessageDialog.GroupNoticeInventoryAccepted, InstantMessageOnline.Offline,
            Client.Self.SimPosition, Client.Network.CurrentSim?.RegionID ?? UUID.Zero, destFolder.GetBytes());
        HasNoticeAttachment = false;
        _pendingNoticeIM    = null;
        StatusText = "Attachment saved to inventory.";
    }

    [RelayCommand]
    private void RefreshNotices()
    {
        Notices.Clear();
        Client.Groups.RequestGroupNoticesList(GroupID);
    }

    private void LoadBannedMembers(UUID groupId)
    {
        _ = Client.Groups.RequestBannedAgents(groupId, OnBannedAgentsReceived);
    }

    private void OnBannedAgentsReceived(object? sender, BannedAgentsEventArgs e)
    {
        if (e.GroupID != GroupID) return;
        Dispatcher.UIThread.Post(() =>
        {
            BannedMembers.Clear();
            if (e.BannedAgents == null) return;
            foreach (var kvp in e.BannedAgents.OrderBy(k => k.Key.ToString()))
            {
                var name = _instance.Names.Get(kvp.Key);
                var date = kvp.Value.ToString("yyyy-MM-dd");
                BannedMembers.Add(new GroupBanEntry(kvp.Key, name, date));
            }
            IsBanListLoaded = true;
        });
    }

    [RelayCommand]
    private void UnbanMember(GroupBanEntry entry)
    {
        _ = Client.Groups.RequestBanAction(GroupID, GroupBanAction.Unban, [entry.Id],
            (_, _) => LoadBannedMembers(GroupID));
    }

    [RelayCommand]
    private void BanGroupMember(GroupMemberEntry? member)
    {
        if (member == null) return;
        _ = Client.Groups.RequestBanAction(GroupID, GroupBanAction.Ban, [member.AgentID],
            (_, _) => LoadBannedMembers(GroupID));
    }

    [RelayCommand]
    private void RefreshBanList()
    {
        LoadBannedMembers(GroupID);
    }
}

// ── Model types ───────────────────────────────────────────────────────────────

public partial class GroupRoleEntry : ObservableObject
{
    public UUID RoleID { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    public GroupPowers Powers { get; set; }
    [ObservableProperty] private int _memberCount;

    public GroupRoleEntry(UUID roleId, string name, string title, string description, GroupPowers powers, int memberCount)
    {
        RoleID        = roleId;
        _name         = name;
        _title        = title;
        _description  = description;
        Powers        = powers;
        _memberCount  = memberCount;
    }
}

public partial class GroupPowerEntry : ObservableObject
{
    public GroupPowers Power { get; }
    public string Name => Power.ToString();
    [ObservableProperty] private bool _isEnabled;

    public GroupPowerEntry(GroupPowers power, bool enabled)
    {
        Power     = power;
        IsEnabled = enabled;
    }
}

public record GroupTitleEntry(UUID RoleID, string Title, bool IsSelected);
public record GroupMemberEntry(UUID AgentID, string Name, string Title);
public record GroupNoticeEntry(UUID NoticeID, string Subject, string FromName, string Date);
public record GroupBanEntry(UUID Id, string Name, string BanDate);
public record GroupRoleMemberEntry(UUID AgentID, string Name);
