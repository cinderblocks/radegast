/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.Packets;

namespace Radegast
{
    public partial class GroupDetails : UserControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private Group group;
        private Dictionary<UUID, GroupTitle> titles;
        private Dictionary<UUID, Group> myGroups => instance.Groups;
        private List<KeyValuePair<UUID, UUID>> roleMembers;
        private Dictionary<UUID, GroupRole> roles;
        private readonly bool isMember;
        private readonly GroupMemberSorter memberSorter = new GroupMemberSorter();
        private System.Threading.Timer nameUpdateTimer;

        private UUID groupTitlesRequest, groupMembersRequest, groupRolesRequest, groupRolesMembersRequest;

        public GroupDetails(RadegastInstanceForms instance, Group group)
        {
            InitializeComponent();
            Disposed += GroupDetails_Disposed;

            this.instance = instance;
            this.group = group;

            if (group.InsigniaID != UUID.Zero)
            {
                SLImageHandler insignia = new SLImageHandler(instance, group.InsigniaID, string.Empty)
                {
                    Dock = DockStyle.Fill
                };
                pnlInsignia.Controls.Add(insignia);
            }

            nameUpdateTimer = new System.Threading.Timer(nameUpdateTimer_Elapsed, this,
                System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            txtGroupID.Text = group.ID.ToString();

            lblGroupName.Text = group.Name;

            if (client.Network.CurrentSim.Caps.CapabilityURI("GroupAPIv1") == null)
            {
                lblGroupBansTitle.Text = "Region does not support group bans";
                pnlBannedBottom.Enabled = pnlBannedTop.Enabled = lwBannedMembers.Enabled = false;
            }

            isMember = instance.Groups.ContainsKey(group.ID);

            if (!isMember)
            {
                tcGroupDetails.TabPages.Remove(tpMembersRoles);
                tcGroupDetails.TabPages.Remove(tpNotices);
                tcGroupDetails.TabPages.Remove(tpBanned);
            }
            else
            {
                RefreshBans();
            }

            lvwNoticeArchive.SmallImageList = frmMain.ResourceImages;
            lvwNoticeArchive.ListViewItemSorter = new GroupNoticeSorter();

            // Callbacks
            client.Groups.GroupTitlesReply += Groups_GroupTitlesReply;
            client.Groups.GroupMembersReply += Groups_GroupMembersReply;
            client.Groups.GroupProfile += Groups_GroupProfile;
            client.Groups.CurrentGroups += Groups_CurrentGroups;
            client.Groups.GroupNoticesListReply += Groups_GroupNoticesListReply;
            client.Groups.GroupJoinedReply += Groups_GroupJoinedReply;
            client.Groups.GroupLeaveReply += Groups_GroupLeaveReply;
            client.Groups.GroupRoleDataReply += Groups_GroupRoleDataReply;
            client.Groups.GroupMemberEjected += Groups_GroupMemberEjected;
            client.Groups.GroupRoleMembersReply += Groups_GroupRoleMembersReply;
            client.Self.IM += Self_IM;
            instance.Names.NameUpdated += Names_NameUpdated;
            RefreshControlsAvailability();
            RefreshGroupInfo();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void GroupDetails_Disposed(object sender, EventArgs e)
        {
            client.Groups.GroupTitlesReply -= Groups_GroupTitlesReply;
            client.Groups.GroupMembersReply -= Groups_GroupMembersReply;
            client.Groups.GroupProfile -= Groups_GroupProfile;
            client.Groups.CurrentGroups -= Groups_CurrentGroups;
            client.Groups.GroupNoticesListReply -= Groups_GroupNoticesListReply;
            client.Groups.GroupJoinedReply -= Groups_GroupJoinedReply;
            client.Groups.GroupLeaveReply -= Groups_GroupLeaveReply;
            client.Groups.GroupRoleDataReply -= Groups_GroupRoleDataReply;
            client.Groups.GroupRoleMembersReply -= Groups_GroupRoleMembersReply;
            client.Groups.GroupMemberEjected -= Groups_GroupMemberEjected;
            client.Self.IM -= Self_IM;
            if (instance?.Names != null)
            {
                instance.Names.NameUpdated -= Names_NameUpdated;
            }
            if (nameUpdateTimer != null)
            {
                nameUpdateTimer.Dispose();
                nameUpdateTimer = null;
            }
        }

        #region Network callbacks

        private void Groups_GroupMemberEjected(object sender, GroupOperationEventArgs e)
        {
            if (e.GroupID != group.ID) return;

            if (e.Success)
            {
                BeginInvoke(new MethodInvoker(RefreshGroupInfo));
                instance.ShowNotificationInChat("Group member ejected.");
            }
            else
            {
                instance.ShowNotificationInChat("Failed to eject group member.");
            }
        }

        private void Groups_GroupRoleMembersReply(object sender, GroupRolesMembersReplyEventArgs e)
        {
            if (e.GroupID == group.ID && e.RequestID == groupRolesMembersRequest)
            {
                roleMembers = e.RolesMembers;
                BeginInvoke(new MethodInvoker(() =>
                    {
                        btnInviteNewMember.Enabled = HasPower(GroupPowers.Invite);
                        btnEjectMember.Enabled = HasPower(GroupPowers.Eject);
                        lvwMemberDetails_SelectedIndexChanged(null, null);
                    }
                ));
            }
        }

        private void Groups_GroupRoleDataReply(object sender, GroupRolesDataReplyEventArgs e)
        {
            if (e.GroupID == group.ID && e.RequestID == groupRolesRequest)
            {
                groupRolesMembersRequest = client.Groups.RequestGroupRolesMembers(group.ID);
                if (roles == null) roles = e.Roles;
                else lock (roles) roles = e.Roles;
                BeginInvoke(new MethodInvoker(DisplayGroupRoles));
            }
        }

        private void Groups_GroupLeaveReply(object sender, GroupOperationEventArgs e)
        {
            if (e.GroupID == group.ID && e.Success)
            {
                BeginInvoke(new MethodInvoker(RefreshGroupInfo));
            }
        }

        private void Groups_GroupJoinedReply(object sender, GroupOperationEventArgs e)
        {
            if (e.GroupID == group.ID && e.Success)
            {
                BeginInvoke(new MethodInvoker(RefreshGroupInfo));
            }
        }

        private UUID destinationFolderID;

        private void Self_IM(object sender, InstantMessageEventArgs e)
        {
            if (e.IM.Dialog != InstantMessageDialog.GroupNoticeRequested) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Self_IM(sender, e)));
                return;
            }

            InstantMessage msg = e.IM;
            UUID groupID;

            groupID = msg.BinaryBucket.Length >= 18 ? new UUID(msg.BinaryBucket, 2) : msg.FromAgentID;

            if (groupID != group.ID) return;

            if (msg.BinaryBucket.Length > 18 && msg.BinaryBucket[0] != 0)
            {
                var type = (AssetType)msg.BinaryBucket[1];
                destinationFolderID = client.Inventory.FindFolderForType(type);
                int icoIndx = InventoryConsole.GetItemImageIndex(type.ToString().ToLower());
                if (icoIndx >= 0)
                {
                    icnItem.Image = frmMain.ResourceImages.Images[icoIndx];
                    icnItem.Visible = true;
                }
                txtItemName.Text = Utils.BytesToString(msg.BinaryBucket, 18, msg.BinaryBucket.Length - 19);
                btnSave.Enabled = true;
                btnSave.Visible = icnItem.Visible = txtItemName.Visible = true;
                btnSave.Tag = msg;
            }

            string text = msg.Message.Replace("\n", Environment.NewLine);
            int pos = msg.Message.IndexOf('|');
            string title = msg.Message.Substring(0, pos);
            text = text.Remove(0, pos + 1);
            txtNotice.Text = text;
        }

        private void Groups_GroupNoticesListReply(object sender, GroupNoticesListReplyEventArgs e)
        {
            if (e.GroupID != group.ID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Groups_GroupNoticesListReply(sender, e)));
                return;
            }

            lvwNoticeArchive.BeginUpdate();

            foreach (GroupNoticesListEntry notice in e.Notices)
            {
                ListViewItem item = new ListViewItem();
                item.SubItems.Add(notice.Subject);
                item.SubItems.Add(notice.FromName);
                string noticeDate = string.Empty;
                if (notice.Timestamp != 0)
                {
                    noticeDate = Utils.UnixTimeToDateTime(notice.Timestamp).ToShortDateString();
                }
                item.SubItems.Add(noticeDate);

                if (notice.HasAttachment)
                {
                    item.ImageIndex = InventoryConsole.GetItemImageIndex(notice.AssetType.ToString().ToLower());
                }

                item.Tag = notice;

                lvwNoticeArchive.Items.Add(item);
            }
            lvwNoticeArchive.EndUpdate();
        }

        private void Groups_CurrentGroups(object sender, CurrentGroupsEventArgs e)
        {
            BeginInvoke(new MethodInvoker(RefreshControlsAvailability));
        }

        private void Groups_GroupProfile(object sender, GroupProfileEventArgs e)
        {
            if (group.ID != e.Group.ID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Groups_GroupProfile(sender, e)));
                return;
            }

            group = e.Group;
            if (group.InsigniaID != UUID.Zero && pnlInsignia.Controls.Count == 0)
            {
                SLImageHandler insignia = new SLImageHandler(instance, group.InsigniaID, string.Empty)
                {
                    Dock = DockStyle.Fill
                };
                pnlInsignia.Controls.Add(insignia);
            }

            lblGroupName.Text = e.Group.Name;
            tbxCharter.Text = group.Charter.Replace("\n", Environment.NewLine);
            lblFounded.Text = "Founded by: " + instance.Names.Get(group.FounderID);
            cbxShowInSearch.Checked = group.ShowInList;
            cbxOpenEnrollment.Checked = group.OpenEnrollment;

            if (group.MembershipFee > 0)
            {
                cbxEnrollmentFee.Checked = true;
                nudEnrollmentFee.Value = group.MembershipFee;
            }
            else
            {
                cbxEnrollmentFee.Checked = false;
                nudEnrollmentFee.Value = 0;
            }

            cbxMaturity.SelectedIndex = group.MaturePublish ? 1 : 0;

            btnJoin.Enabled = btnJoin.Visible = false;

            if (myGroups.ContainsKey(group.ID)) // I am in this group
            {
                cbxReceiveNotices.Checked = myGroups[group.ID].AcceptNotices;
                cbxListInProfile.Checked = myGroups[group.ID].ListInProfile;
                cbxReceiveNotices.CheckedChanged += cbxListInProfile_CheckedChanged;
                cbxListInProfile.CheckedChanged += cbxListInProfile_CheckedChanged;
            }
            else if (group.OpenEnrollment) // I am not in this group, but I could join it
            {
                btnJoin.Text = "Join $L" + group.MembershipFee;
                btnJoin.Enabled = btnJoin.Visible = true;
            }

            RefreshControlsAvailability();
        }

        private void Names_NameUpdated(object sender, UUIDNameReplyEventArgs e)
        {
            ProcessNameUpdate(e.Names);
        }

        private int lastTick = 0;

        private void ProcessNameUpdate(Dictionary<UUID, string> Names)
        {
            if (Names.ContainsKey(group.FounderID))
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new MethodInvoker(() => { lblFounded.Text = "Founded by: " + Names[group.FounderID]; }));
                }
                else
                {
                    lblFounded.Text = "Founded by: " + Names[group.FounderID];
                }
            }

            ThreadPool.QueueUserWorkItem(sync =>
            {
                try
                {
                    bool hasUpdates = false;

                    foreach (var name in Names)
                    {
                        var member = GroupMembers.Find((m) => m.Base.ID == name.Key);
                        if (member == null) continue;

                        hasUpdates = true;
                        member.Name = name.Value;
                    }

                    if (hasUpdates)
                    {
                        int tick = Environment.TickCount;
                        int elapsed = tick - lastTick;
                        if (elapsed > 500)
                        {
                            lastTick = tick;
                            nameUpdateTimer_Elapsed(this);
                        }
                        nameUpdateTimer.Change(500, System.Threading.Timeout.Infinite);
                    }
                }
                catch (Exception ex)
                {
                    Logger.DebugLog("Failed updating group member names: " + ex.ToString());
                }
            });
        }

        private void Groups_GroupTitlesReply(object sender, GroupTitlesReplyEventArgs e)
        {
            if (groupTitlesRequest != e.RequestID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Groups_GroupTitlesReply(sender, e)));
                return;
            }

            titles = e.Titles;

            foreach (GroupTitle title in titles.Values)
            {
                cbxActiveTitle.Items.Add(title);
                if (title.Selected)
                {
                    cbxActiveTitle.SelectedItem = title;
                }
            }

            cbxActiveTitle.SelectedIndexChanged += cbxActiveTitle_SelectedIndexChanged;
        }

        private List<EnhancedGroupMember> GroupMembers = new List<EnhancedGroupMember>();

        private void Groups_GroupMembersReply(object sender, GroupMembersReplyEventArgs e)
        {
            if (group.ID != e.GroupID) return;
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Groups_GroupMembersReply(sender, e)));
                return;
            }

            lvwGeneralMembers.VirtualListSize = 0;
            lvwMemberDetails.VirtualListSize = 0;

            var members = new List<EnhancedGroupMember>(e.Members.Count);
            foreach (var member in e.Members)
            {
                members.Add(new EnhancedGroupMember(instance.Names.Get(member.Key), member.Value));
            }

            GroupMembers = members;
            GroupMembers.Sort(memberSorter);
            lvwGeneralMembers.VirtualListSize = GroupMembers.Count;
            lvwMemberDetails.VirtualListSize = GroupMembers.Count;
        }

        private void lvwMemberDetails_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            EnhancedGroupMember member = null;
            try
            {
                member = GroupMembers[e.ItemIndex];
            }
            catch
            {
                e.Item = new ListViewItem();
                return;
            }

            ListViewItem item = new ListViewItem(member.Name) {Tag = member, Name = member.Base.ID.ToString()};
            item.SubItems.Add(new ListViewItem.ListViewSubItem(item, member.Base.Contribution.ToString()));
            if (member.LastOnline != DateTime.MinValue)
            {
                item.SubItems.Add(new ListViewItem.ListViewSubItem(item, member.Base.OnlineStatus));
            }

            e.Item = item;
        }

        private void lvwGeneralMembers_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            EnhancedGroupMember member = null;
            try
            {
                member = GroupMembers[e.ItemIndex];
            }
            catch
            {
                e.Item = new ListViewItem();
                return;
            }

            ListViewItem item = new ListViewItem(member.Name) {Tag = member, Name = member.Base.ID.ToString()};

            item.SubItems.Add(new ListViewItem.ListViewSubItem(item, member.Base.Title));
            item.SubItems.Add(member.LastOnline != DateTime.MinValue
                ? new ListViewItem.ListViewSubItem(item, member.Base.OnlineStatus)
                : new ListViewItem.ListViewSubItem(item, "N/A"));

            e.Item = item;
        }
        #endregion

        #region Privatate methods

        private void nameUpdateTimer_Elapsed(object sync)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => nameUpdateTimer_Elapsed(sync)));
                return;
            }

            GroupMembers.Sort(memberSorter);
            lvwGeneralMembers.Invalidate();
            lvwMemberDetails.Invalidate();
        }

        private void DisplayGroupRoles()
        {
            lvwRoles.Items.Clear();

            lock (roles)
            {
                foreach (GroupRole role in roles.Values)
                {
                    ListViewItem item = new ListViewItem {Name = role.ID.ToString(), Text = role.Name};
                    item.SubItems.Add(role.Title);
                    item.SubItems.Add(role.ID.ToString());
                    item.Tag = role;
                    lvwRoles.Items.Add(item);
                }
            }

        }


        private bool HasPower(GroupPowers power)
        {
            if (!instance.Groups.ContainsKey(group.ID))
                return false;

            return (instance.Groups[group.ID].Powers & power) != 0;
        }

        private void RefreshControlsAvailability()
        {
            if (!HasPower(GroupPowers.ChangeOptions))
            {
                nudEnrollmentFee.ReadOnly = true;
                cbxEnrollmentFee.Enabled = false;
                cbxOpenEnrollment.Enabled = false;
            }

            if (!HasPower(GroupPowers.ChangeIdentity))
            {
                tbxCharter.ReadOnly = true;
                cbxShowInSearch.Enabled = false;
                cbxMaturity.Enabled = false;
            }

            if (!myGroups.ContainsKey(group.ID))
            {
                cbxReceiveNotices.Enabled = false;
                cbxListInProfile.Enabled = false;
            }
        }

        private void RefreshGroupNotices()
        {
            lvwNoticeArchive.Items.Clear();
            client.Groups.RequestGroupNoticesList(group.ID);
            btnNewNotice.Enabled = HasPower(GroupPowers.SendNotices);
        }

        private void RefreshGroupInfo()
        {
            lvwGeneralMembers.VirtualListSize = 0;
            if (isMember) lvwMemberDetails.VirtualListSize = 0;

            cbxActiveTitle.SelectedIndexChanged -= cbxActiveTitle_SelectedIndexChanged;
            cbxReceiveNotices.CheckedChanged -= cbxListInProfile_CheckedChanged;
            cbxListInProfile.CheckedChanged -= cbxListInProfile_CheckedChanged;

            cbxActiveTitle.Items.Clear();

            // Request group info
            client.Groups.RequestGroupProfile(group.ID);
            groupTitlesRequest = client.Groups.RequestGroupTitles(group.ID);
            groupMembersRequest = client.Groups.RequestGroupMembers(group.ID);
        }

        private void RefreshRoles()
        {
            if (!isMember) return;

            lvwRoles.SelectedItems.Clear();
            lvwRoles.Items.Clear();
            btnApply.Enabled = false;
            btnCreateNewRole.Enabled = HasPower(GroupPowers.CreateRole);
            btnDeleteRole.Enabled = HasPower(GroupPowers.DeleteRole);
            txtRoleDescription.Enabled = txtRoleName.Enabled = txtRoleTitle.Enabled = lvwRoleAbilitis.Enabled = btnSaveRole.Enabled = false;
            groupRolesRequest = client.Groups.RequestGroupRoles(group.ID);
        }

        private void RefreshMembersRoles()
        {
            if (!isMember) return;

            btnApply.Enabled = false;
            lvwAssignedRoles.Items.Clear();
            groupRolesRequest = client.Groups.RequestGroupRoles(group.ID);
        }
        #endregion

        #region Controls change handlers

        private void cbxListInProfile_CheckedChanged(object sender, EventArgs e)
        {
            if (myGroups.ContainsKey(group.ID))
            {
                Group g = myGroups[group.ID];
                // g.AcceptNotices = cbxReceiveNotices.Checked;
                // g.ListInProfile = cbxListInProfile.Checked;
                client.Groups.SetGroupAcceptNotices(g.ID, cbxReceiveNotices.Checked, cbxListInProfile.Checked);
                client.Groups.RequestCurrentGroups();
            }
        }

        private void cbxActiveTitle_SelectedIndexChanged(object sender, EventArgs e)
        {
            GroupTitle title = (GroupTitle)cbxActiveTitle.SelectedItem;
            client.Groups.ActivateTitle(title.GroupID, title.RoleID);
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            switch (tcGroupDetails.SelectedTab.Name)
            {
                case "tpGeneral":
                    RefreshGroupInfo();
                    break;

                case "tpNotices":
                    RefreshGroupNotices();
                    break;

                case "tpMembersRoles":
                    RefreshMembersRoles();
                    break;

                case "tpBanned":
                    RefreshBans();
                    break;
            }
        }
        #endregion

        private void lvwGeneralMembers_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            ListView lb = (ListView)sender;
            switch (e.Column)
            {
                case 0:
                    memberSorter.SortBy = GroupMemberSorter.SortByColumn.Name;
                    break;

                case 1:
                    memberSorter.SortBy = lb.Name == "lvwMemberDetails"
                        ? GroupMemberSorter.SortByColumn.Contribution 
                        : GroupMemberSorter.SortByColumn.Title;
                    break;

                case 2:
                    memberSorter.SortBy = GroupMemberSorter.SortByColumn.LastOnline;
                    break;
            }

            memberSorter.CurrentOrder = memberSorter.CurrentOrder == GroupMemberSorter.SortOrder.Ascending 
                ? GroupMemberSorter.SortOrder.Descending 
                : GroupMemberSorter.SortOrder.Ascending;

            GroupMembers.Sort(memberSorter);
            lb.Invalidate();
        }

        private void lvwNoticeArchive_ColumnClick(object sender, ColumnClickEventArgs e)
        {

            GroupNoticeSorter sorter = (GroupNoticeSorter)lvwNoticeArchive.ListViewItemSorter;

            switch (e.Column)
            {
                case 1:
                    sorter.SortBy = GroupNoticeSorter.SortByColumn.Subject;
                    break;

                case 2:
                    sorter.SortBy = GroupNoticeSorter.SortByColumn.Sender;
                    break;

                case 3:
                    sorter.SortBy = GroupNoticeSorter.SortByColumn.Date;
                    break;
            }

            sorter.CurrentOrder = sorter.CurrentOrder == GroupNoticeSorter.SortOrder.Ascending 
                ? GroupNoticeSorter.SortOrder.Descending
                : GroupNoticeSorter.SortOrder.Ascending;

            lvwNoticeArchive.Sort();
        }


        private void btnClose_Click(object sender, EventArgs e)
        {
            FindForm()?.Close();
        }

        private void tcGroupDetails_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (tcGroupDetails.SelectedTab.Name)
            {
                case "tpNotices":
                    RefreshGroupNotices();
                    break;

                case "tpMembersRoles":
                    RefreshMembersRoles();
                    break;

                case "tpBanned":
                    RefreshBans();
                    break;
            }
        }

        private void lvwNoticeArchive_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvwNoticeArchive.SelectedItems.Count == 1)
            {
                if (lvwNoticeArchive.SelectedItems[0].Tag is GroupNoticesListEntry)
                {
                    GroupNoticesListEntry notice = (GroupNoticesListEntry)lvwNoticeArchive.SelectedItems[0].Tag;
                    lblSentBy.Text = "Sent by " + notice.FromName;
                    lblTitle.Text = notice.Subject;
                    txtNotice.Text = string.Empty;
                    btnSave.Enabled = btnSave.Visible = icnItem.Visible = txtItemName.Visible = false;
                    client.Groups.RequestGroupNotice(notice.NoticeID);
                    pnlNewNotice.Visible = false;
                    pnlArchivedNotice.Visible = true;
                    return;
                }
            }
            pnlArchivedNotice.Visible = false;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (btnSave.Tag is InstantMessage msg)
            {
                client.Self.InstantMessage(client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID,
                    InstantMessageDialog.GroupNoticeInventoryAccepted, InstantMessageOnline.Offline, client.Self.SimPosition,
                    client.Network.CurrentSim.RegionID, destinationFolderID.GetBytes());
                btnSave.Enabled = false;
                btnClose.Focus();
            }
        }

        private void btnJoin_Click(object sender, EventArgs e)
        {
            client.Groups.RequestJoinGroup(group.ID);
        }

        private void lvwGeneralMembers_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewItem item = lvwGeneralMembers.GetItemAt(e.X, e.Y);
            if (item != null)
            {
                try
                {
                    UUID agentID = new UUID(item.Name);
                    instance.ShowAgentProfile(item.Text, agentID);
                }
                catch (Exception) { }
            }
        }

        private void lvwMemberDetails_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewItem item = lvwMemberDetails.GetItemAt(e.X, e.Y);
            if (item != null)
            {
                try
                {
                    UUID agentID = new UUID(item.Name);
                    instance.ShowAgentProfile(item.Text, agentID);
                }
                catch (Exception) { }
            }
        }

        private void btnEjectMember_Click(object sender, EventArgs e)
        {
            if (lvwMemberDetails.SelectedIndices.Count != 1 || roles == null || roleMembers == null) return;
            EnhancedGroupMember m = GroupMembers[lvwMemberDetails.SelectedIndices[0]];
            client.Groups.EjectUser(group.ID, m.Base.ID);
        }

        private void lvwMemberDetails_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnBanMember.Enabled = lvwMemberDetails.SelectedIndices.Count > 0;

            if (lvwMemberDetails.SelectedIndices.Count != 1 || roles == null || roleMembers == null) return;
            EnhancedGroupMember m = GroupMembers[lvwMemberDetails.SelectedIndices[0]];

            btnApply.Enabled = false;

            lvwAssignedRoles.BeginUpdate();
            lvwAssignedRoles.ItemChecked -= lvwAssignedRoles_ItemChecked;
            lvwAssignedRoles.Items.Clear();
            lvwAssignedRoles.Tag = m;

            ListViewItem defaultItem = new ListViewItem {Name = "Everyone"};
            defaultItem.SubItems.Add(defaultItem.Name);
            defaultItem.Checked = true;
            lvwAssignedRoles.Items.Add(defaultItem);

            GroupPowers abilities = GroupPowers.None;

            lock (roles)
            {
                foreach (var r in roles)
                {
                    GroupRole role = r.Value;

                    if (role.ID == UUID.Zero)
                    {
                        abilities |= role.Powers;
                        continue;
                    }

                    ListViewItem item = new ListViewItem {Name = role.Name};
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item, role.Name));
                    item.Tag = role;
                    var foundRole = roleMembers.Find(kvp => kvp.Value == m.Base.ID && kvp.Key == role.ID);
                    bool hasRole = foundRole.Value == m.Base.ID;
                    item.Checked = hasRole;
                    lvwAssignedRoles.Items.Add(item);

                    if (hasRole)
                        abilities |= role.Powers;
                }
            }

            lvwAssignedRoles.ItemChecked += lvwAssignedRoles_ItemChecked;
            lvwAssignedRoles.EndUpdate();

            lvwAllowedAbilities.BeginUpdate();
            lvwAllowedAbilities.Items.Clear();

            foreach (GroupPowers p in Enum.GetValues(typeof(GroupPowers)))
            {
                if (p != GroupPowers.None && (abilities & p) == p)
                {
                    lvwAllowedAbilities.Items.Add(p.ToString());
                }
            }


            lvwAllowedAbilities.EndUpdate();

        }

        private void UpdateMemberRoles()
        {
            EnhancedGroupMember m = (EnhancedGroupMember)lvwAssignedRoles.Tag;
            GroupRoleChangesPacket p = new GroupRoleChangesPacket
            {
                AgentData = {AgentID = client.Self.AgentID, SessionID = client.Self.SessionID, GroupID = @group.ID}
            };
            List<GroupRoleChangesPacket.RoleChangeBlock> changes = new List<GroupRoleChangesPacket.RoleChangeBlock>();

            foreach (ListViewItem item in lvwAssignedRoles.Items)
            {
                if (!(item.Tag is GroupRole))
                    continue;

                GroupRole role = (GroupRole)item.Tag;
                var foundRole = roleMembers.Find(kvp => kvp.Value == m.Base.ID && kvp.Key == role.ID);
                bool hasRole = foundRole.Value == m.Base.ID;

                if (item.Checked != hasRole)
                {
                    if (item.Checked)
                        roleMembers.Add(new KeyValuePair<UUID, UUID>(role.ID, m.Base.ID));
                    else
                        roleMembers.Remove(foundRole);

                    var rc = new GroupRoleChangesPacket.RoleChangeBlock
                    {
                        MemberID = m.Base.ID, RoleID = role.ID, Change = item.Checked ? 0u : 1u
                    };
                    changes.Add(rc);
                }
            }

            if (changes.Count > 0)
            {
                p.RoleChange = changes.ToArray();
                client.Network.CurrentSim.SendPacket(p);
            }

            btnApply.Enabled = false;
            lvwMemberDetails_SelectedIndexChanged(lvwMemberDetails, EventArgs.Empty);
        }


        private void lvwAssignedRoles_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e.Item.Tag == null) // click on the default role
            {
                if (!e.Item.Checked)
                    e.Item.Checked = true;

                return;
            }
            if (e.Item.Tag is GroupRole)
            {
                EnhancedGroupMember m = (EnhancedGroupMember)lvwAssignedRoles.Tag;
                bool modified = false;

                foreach (ListViewItem item in lvwAssignedRoles.Items)
                {
                    if (!(item.Tag is GroupRole))
                        continue;

                    GroupRole role = (GroupRole)item.Tag;
                    var foundRole = roleMembers.Find(kvp => kvp.Value == m.Base.ID && kvp.Key == role.ID);
                    bool hasRole = foundRole.Value == m.Base.ID;

                    if (item.Checked != hasRole)
                    {
                        modified = true;
                    }
                }

                btnApply.Enabled = modified;
            }
        }

        private void tbxCharter_TextChanged(object sender, EventArgs e)
        {
            btnApply.Enabled = true;
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            switch (tcGroupDetails.SelectedTab.Name)
            {
                case "tpMembersRoles":
                    UpdateMemberRoles();
                    break;
            }
        }

        private void btnInviteNewMember_Click(object sender, EventArgs e)
        {
            (new GroupInvite(instance, group, roles)).Show();
        }

        private void lvwAllowedAbilities_SizeChanged(object sender, EventArgs e)
        {
            lvwAllowedAbilities.Columns[0].Width = lvwAllowedAbilities.Width - 30;
        }

        private void tcMembersRoles_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (tcMembersRoles.SelectedTab.Name)
            {
                case "tpMembers":
                    RefreshMembersRoles();
                    break;

                case "tpRoles":
                    RefreshRoles();
                    break;

            }

        }

        private void lvwRoles_SelectedIndexChanged(object sender, EventArgs e)
        {
            txtRoleDescription.Text = txtRoleName.Text = txtRoleTitle.Text = string.Empty;
            txtRoleDescription.Enabled = txtRoleName.Enabled = txtRoleTitle.Enabled = lvwRoleAbilitis.Enabled = btnSaveRole.Enabled = false;
            lvwAssignedMembers.Items.Clear();
            lvwRoleAbilitis.Items.Clear();

            if (lvwRoles.SelectedItems.Count != 1) return;

            GroupRole role = (GroupRole)lvwRoles.SelectedItems[0].Tag;
            txtRoleName.Text = role.Name;
            txtRoleTitle.Text = role.Title;
            txtRoleDescription.Text = role.Description;

            if (HasPower(GroupPowers.RoleProperties))
            {
                txtRoleDescription.Enabled = txtRoleName.Enabled = txtRoleTitle.Enabled = btnSaveRole.Enabled = true;
            }

            if (HasPower(GroupPowers.ChangeActions))
            {
                lvwRoleAbilitis.Enabled = btnSaveRole.Enabled = true;
            }

            btnSaveRole.Tag = role;

            lvwAssignedMembers.BeginUpdate();
            if (role.ID == UUID.Zero)
            {
                foreach (var member in GroupMembers)
                    lvwAssignedMembers.Items.Add(member.Name);
            }
            else if (roleMembers != null)
            {
                var mmb = roleMembers.FindAll(kvp => kvp.Key == role.ID);
                foreach (var m in mmb)
                {
                    lvwAssignedMembers.Items.Add(instance.Names.Get(m.Value));
                }
            }
            lvwAssignedMembers.EndUpdate();

            lvwRoleAbilitis.Tag = role;

            foreach (GroupPowers p in Enum.GetValues(typeof(GroupPowers)))
            {
                if (p != GroupPowers.None)
                {
                    ListViewItem item = new ListViewItem {Tag = p};
                    item.SubItems.Add(p.ToString());
                    item.Checked = (p & role.Powers) != 0;
                    lvwRoleAbilitis.Items.Add(item);
                }
            }
        }

        private void btnCreateNewRole_Click(object sender, EventArgs e)
        {
            lvwRoles.SelectedItems.Clear();
            txtRoleDescription.Enabled = txtRoleName.Enabled = txtRoleTitle.Enabled = btnSaveRole.Enabled = true;
            btnSaveRole.Tag = null;
            txtRoleName.Focus();
        }

        private void btnSaveRole_Click(object sender, EventArgs e)
        {
            if (btnSaveRole.Tag == null) // new role
            {
                GroupRole role = new GroupRole
                {
                    Name = txtRoleName.Text, Title = txtRoleTitle.Text, Description = txtRoleDescription.Text
                };
                client.Groups.CreateRole(group.ID, role);
                System.Threading.Thread.Sleep(100);
                RefreshRoles();
            }
            else if (btnSaveRole.Tag is GroupRole role) // update role
            {
                if (HasPower(GroupPowers.ChangeActions))
                {
                    role.Powers = GroupPowers.None;

                    foreach (ListViewItem item in lvwRoleAbilitis.Items)
                    {
                        if (item.Checked)
                            role.Powers |= (GroupPowers)item.Tag;
                    }
                }

                if (HasPower(GroupPowers.RoleProperties))
                {
                    role.Name = txtRoleName.Text;
                    role.Title = txtRoleTitle.Text;
                    role.Description = txtRoleDescription.Text;
                }

                client.Groups.UpdateRole(role);
                System.Threading.Thread.Sleep(100);
                RefreshRoles();
            }
        }

        private void btnDeleteRole_Click(object sender, EventArgs e)
        {
            if (lvwRoles.SelectedItems.Count == 1)
            {
                client.Groups.DeleteRole(group.ID, ((GroupRole)lvwRoles.SelectedItems[0].Tag).ID);
                System.Threading.Thread.Sleep(100);
                RefreshRoles();
            }
        }

        #region New notice
        private void btnNewNotice_Click(object sender, EventArgs e)
        {
            if (HasPower(GroupPowers.SendNotices))
            {
                pnlArchivedNotice.Visible = false;
                pnlNewNotice.Visible = true;
                txtNewNoticeTitle.Focus();
            }
            else
            {
                instance.ShowNotificationInChat("Don't have permission to send notices in this group", ChatBufferTextStyle.Error);
            }
        }

        private void btnPasteInv_Click(object sender, EventArgs e)
        {
            if (instance.InventoryClipboard?.Item is InventoryItem inv)
            {
                txtNewNoteAtt.Text = inv.Name;
                int icoIndx = InventoryConsole.GetItemImageIndex(inv.AssetType.ToString().ToLower());
                if (icoIndx >= 0)
                {
                    icnNewNoticeAtt.Image = frmMain.ResourceImages.Images[icoIndx];
                    icnNewNoticeAtt.Visible = true;
                }
                txtNewNoteAtt.Tag = inv;
                btnRemoveAttachment.Enabled = true;
            }
        }

        private void btnRemoveAttachment_Click(object sender, EventArgs e)
        {
            txtNewNoteAtt.Tag = null;
            txtNewNoteAtt.Text = string.Empty;
            btnRemoveAttachment.Enabled = false;
            icnNewNoticeAtt.Visible = false;
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            GroupNotice ntc = new GroupNotice
            {
                Subject = txtNewNoticeTitle.Text, 
                Message = txtNewNoticeBody.Text
            };

            if (txtNewNoteAtt.Tag is InventoryItem inv)
            {
                ntc.OwnerID = inv.OwnerID;
                ntc.AttachmentID = inv.UUID;
            }
            client.Groups.SendGroupNotice(group.ID, ntc);
            btnRemoveAttachment.PerformClick();
            txtNewNoticeTitle.Text = txtNewNoticeBody.Text = string.Empty;
            pnlNewNotice.Visible = false;
            btnRefresh.PerformClick();
            instance.ShowNotificationInChat("Notice sent", ChatBufferTextStyle.Invisible);
        }
        #endregion

        private void memberListContextMenuSave_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveMembers = new SaveFileDialog
            {
                Filter = "CSV|.csv|JSON|.json", Title = "Save visible group members"
            };
            saveMembers.ShowDialog();
            if (saveMembers.FileName != string.Empty)
            {
                try
                {
                    switch (saveMembers.FilterIndex)
                    {
                        case 1:
                            System.IO.FileStream fs = (System.IO.FileStream)saveMembers.OpenFile();
                            System.IO.StreamWriter sw = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8);
                            sw.WriteLine("UUID,Name");
                            foreach (var item in GroupMembers)
                            {
                                sw.WriteLine("{0},{1}", item.Base.ID, item.Name);
                            }
                            sw.Close();
                            break;
                        case 2:
                            OpenMetaverse.StructuredData.OSDArray members = new OpenMetaverse.StructuredData.OSDArray(GroupMembers.Count);
                            foreach (var item in GroupMembers)
                            {
                                OpenMetaverse.StructuredData.OSDMap member = new OpenMetaverse.StructuredData.OSDMap(2)
                                {
                                    ["UUID"] = item.Base.ID, ["Name"] = item.Name
                                };
                                members.Add(member);
                            }
                            System.IO.File.WriteAllText(saveMembers.FileName, OpenMetaverse.StructuredData.OSDParser.SerializeJsonString(members));
                            break;
                    }

                    instance.ShowNotificationInChat(
                        $"Saved {GroupMembers.Count} members to {saveMembers.FileName}");
                }
                catch (Exception ex)
                {
                    instance.ShowNotificationInChat("Failed to save member list: " + ex.Message, ChatBufferTextStyle.Error);
                }
            }
        }

        private void copyRoleIDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lvwRoles.SelectedItems.Count != 1) return;
            if (lvwRoles.SelectedItems[0].Tag is GroupRole)
            {
                Clipboard.SetText(((GroupRole)lvwRoles.SelectedItems[0].Tag).ID.ToString());
            }
        }

        private void lvwMemberDetails_SearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
        {
            if (e.IsTextSearch)
            {
                for (int i = 0; i < GroupMembers.Count; i++)
                {
                    if (GroupMembers[i].Name.StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase))
                    {
                        e.Index = i;
                        break;
                    }
                }
            }
        }

        #region Group Bans
        public void RefreshBans()
        {
            _ = client.Groups.RequestBannedAgents(group.ID, (xs, xe) =>
            {
                UpdateBannedAgents(xe);
            });
        }

        private void UpdateBannedAgents(BannedAgentsEventArgs e)
        {
            if (!e.Success || e.GroupID != group.ID) return;
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => UpdateBannedAgents(e)));
                return;
            }

            lwBannedMembers.BeginUpdate();
            lwBannedMembers.Items.Clear();

            foreach (var member in e.BannedAgents)
            {
                var item = new ListViewItem(instance.Names.Get(member.Key)) {Name = member.Key.ToString()};
                item.SubItems.Add(member.Value.ToShortDateString());
                lwBannedMembers.Items.Add(item);
            }
            lwBannedMembers.EndUpdate();
        }

        private void btnBan_Click(object sender, EventArgs e)
        {
            (new BanGroupMember(instance, group, this)).Show();
        }

        private void btnUnban_Click(object sender, EventArgs e)
        {
            List<UUID> toUnban = new List<UUID>();
            for (int i=0; i<lwBannedMembers.SelectedItems.Count; i++)
            {
                UUID id;
                if (UUID.TryParse(lwBannedMembers.SelectedItems[i].Name, out id))
                {
                    toUnban.Add(id);
                }
            }

            if (toUnban.Count > 0)
            {
                _ = client.Groups.RequestBanAction(group.ID, GroupBanAction.Unban, toUnban.ToArray(), (xs, se) =>
                {
                    RefreshBans();
                });
            }
        }

        private void lwBannedMembers_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnUnban.Enabled = lwBannedMembers.SelectedItems.Count > 0;
        }

        private void btnBanMember_Click(object sender, EventArgs e)
        {
            try
            {
                List<UUID> toBan = new List<UUID>();
                for (int i = 0; i < lvwMemberDetails.SelectedIndices.Count; i++)
                {
                    EnhancedGroupMember m = GroupMembers[lvwMemberDetails.SelectedIndices[i]];
                    toBan.Add(m.Base.ID);
                    client.Groups.EjectUser(group.ID, m.Base.ID);
                }

                if (toBan.Count > 0)
                {
                    _ = client.Groups.RequestBanAction(group.ID, GroupBanAction.Ban, toBan.ToArray(), (xs, xe) =>
                    {
                        RefreshBans();
                    });
                }
            }
            catch { }

        }
        #endregion Group Bans
    }

    public class EnhancedGroupMember
    {
        public GroupMember Base;
        public DateTime LastOnline;
        public string Name;

        public EnhancedGroupMember(string name, GroupMember baseMember)
        {
            Base = baseMember;
            Name = name;

            if (baseMember.OnlineStatus == "Online")
            {
                LastOnline = DateTime.Now;
            }
            else if (string.IsNullOrEmpty(baseMember.OnlineStatus) || baseMember.OnlineStatus == "unknown")
            {
                LastOnline = DateTime.MinValue;
            }
            else
            {
                try
                {
                    LastOnline = Convert.ToDateTime(baseMember.OnlineStatus, Utils.EnUsCulture);
                }
                catch (FormatException)
                {
                    LastOnline = DateTime.MaxValue;
                }
            }
        }
    }

    #region Sorter classes
    public class GroupMemberSorter : IComparer<EnhancedGroupMember>
    {
        public enum SortByColumn
        {
            Name,
            Title,
            LastOnline,
            Contribution
        }

        public enum SortOrder
        {
            Ascending,
            Descending
        }

        public SortOrder CurrentOrder = SortOrder.Ascending;
        public SortByColumn SortBy = SortByColumn.Name;

        public int Compare(EnhancedGroupMember member1, EnhancedGroupMember member2)
        {
            switch (SortBy)
            {
                case SortByColumn.Name:
                    return CurrentOrder == SortOrder.Ascending ? string.CompareOrdinal(member1.Name, member2.Name) : string.CompareOrdinal(member2.Name, member1.Name);
                case SortByColumn.Title:
                    return CurrentOrder == SortOrder.Ascending ? string.CompareOrdinal(member1.Base.Title, member2.Base.Title) : string.CompareOrdinal(member2.Base.Title, member1.Base.Title);
                case SortByColumn.LastOnline:
                    return CurrentOrder == SortOrder.Ascending ? DateTime.Compare(member1.LastOnline, member2.LastOnline) : DateTime.Compare(member2.LastOnline, member1.LastOnline);
                case SortByColumn.Contribution:
                    if (member1.Base.Contribution < member2.Base.Contribution)
                        return CurrentOrder == SortOrder.Ascending ? -1 : 1;
                    else if (member1.Base.Contribution > member2.Base.Contribution)
                        return CurrentOrder == SortOrder.Ascending ? 1 : -1;
                    else
                        return 0;
            }

            return 0;
        }
    }


    public class GroupNoticeSorter : IComparer
    {
        public enum SortByColumn
        {
            Subject,
            Sender,
            Date
        }

        public enum SortOrder
        {
            Ascending,
            Descending
        }

        public SortOrder CurrentOrder = SortOrder.Descending;
        public SortByColumn SortBy = SortByColumn.Date;

        private int IntCompare(uint x, uint y)
        {
            if (x < y)
            {
                return -1;
            }
            else if (x > y)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public int Compare(object x, object y)
        {
            ListViewItem item1 = (ListViewItem)x;
            ListViewItem item2 = (ListViewItem)y;
            GroupNoticesListEntry member1 = (GroupNoticesListEntry)item1.Tag;
            GroupNoticesListEntry member2 = (GroupNoticesListEntry)item2.Tag;

            switch (SortBy)
            {
                case SortByColumn.Subject:
                    return CurrentOrder == SortOrder.Ascending
                        ? string.CompareOrdinal(member1.Subject, member2.Subject) 
                        : string.CompareOrdinal(member2.Subject, member1.Subject);
                case SortByColumn.Sender:
                    return CurrentOrder == SortOrder.Ascending 
                        ? string.CompareOrdinal(member1.FromName, member2.FromName) 
                        : string.CompareOrdinal(member2.FromName, member1.FromName);
                case SortByColumn.Date:
                    return CurrentOrder == SortOrder.Ascending 
                        ? IntCompare(member1.Timestamp, member2.Timestamp) 
                        : IntCompare(member2.Timestamp, member1.Timestamp);
            }

            return 0;
        }
    }
    #endregion
}
