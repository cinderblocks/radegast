/**
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.Messages.Linden;


namespace Radegast
{
    public partial class frmProfile : RadegastForm
    {
        private readonly string fullName;

        public UUID AgentID { get; }
        private Avatar.AvatarProperties AvatarProperties;
        private Avatar.Interests Interests;
        private readonly bool myProfile = false;
        private UUID newPickID = UUID.Zero;

        private UUID FLImageID;
        private UUID SLImageID;

        private bool gotPicks = false;
        private UUID requestedPick;
        private ProfilePick currentPick;
        private readonly Dictionary<UUID, ProfilePick> pickCache = new Dictionary<UUID, ProfilePick>();
        private readonly Dictionary<UUID, ParcelInfo> parcelCache = new Dictionary<UUID, ParcelInfo>();

        public frmProfile(RadegastInstanceForms instance, string fullName, UUID agentID)
            : base(instance)
        {
            InitializeComponent();
            Disposed += frmProfile_Disposed;

            AutoSavePosition = true;

            this.fullName = fullName;
            AgentID = agentID;

            Text = $"{fullName} (profile) - {Properties.Resources.ProgramName}";
            txtUUID.Text = agentID.ToString();

            if (Client.Friends.FriendList.ContainsKey(agentID))
            {
                btnFriend.Enabled = false;
            }

            if (Instance.InventoryClipboard != null)
            {
                btnGive.Enabled = true;
            }

            if (agentID == Client.Self.AgentID)
            {
                myProfile = true;
                rtbAbout.ReadOnly = false;
                rtbAboutFL.ReadOnly = false;
                txtWebURL.ReadOnly = false;
                pickTitle.ReadOnly = false;
                pickDetail.ReadOnly = false;
                btnRequestTeleport.Visible = false;
                btnDeletePick.Visible = true;
                btnNewPick.Visible = true;

                txtWantTo.ReadOnly = false;
                txtSkills.ReadOnly = false;
                txtLanguages.ReadOnly = false;

                checkBoxBuild.Enabled = true;
                checkBoxExplore.Enabled = true;
                checkBoxMeet.Enabled = true;
                checkBoxGroup.Enabled = true;
                checkBoxBuy.Enabled = true;
                checkBoxSell.Enabled = true;
                checkBoxBeHired.Enabled = true;
                checkBoxHire.Enabled = true;
                checkBoxTextures.Enabled = true;
                checkBoxArchitecture.Enabled = true;
                checkBoxEventPlanning.Enabled = true;
                checkBoxModeling.Enabled = true;
                checkBoxScripting.Enabled = true;
                checkBoxCustomCharacters.Enabled = true;
            }

            // Callbacks
            Client.Avatars.AvatarPropertiesReply += Avatars_AvatarPropertiesReply;
            Client.Avatars.AvatarPicksReply += Avatars_AvatarPicksReply;
            //Client.Avatars.AvatarClassifiedReply += new EventHandler<AvatarClassifiedReplyEventArgs>(Avatars_AvatarClassifiedsReply);
            Client.Avatars.PickInfoReply += Avatars_PickInfoReply;
            Client.Parcels.ParcelInfoReply += Parcels_ParcelInfoReply;
            Client.Avatars.AvatarGroupsReply += Avatars_AvatarGroupsReply;
            Client.Avatars.AvatarInterestsReply += Avatars_AvatarInterestsReply;
            Client.Avatars.AvatarNotesReply += Avatars_AvatarNotesReply;
            Client.Self.MuteListUpdated += Self_MuteListUpdated;
            NetCom.ClientDisconnected += NetComClientDisconnected;
            Instance.InventoryClipboardUpdated += instance_InventoryClipboardUpdated;
            InitializeProfile();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void frmProfile_Disposed(object sender, EventArgs e)
        {
            Client.Avatars.AvatarPropertiesReply -= Avatars_AvatarPropertiesReply;
            Client.Avatars.AvatarPicksReply -= Avatars_AvatarPicksReply;
            //Client.Avatars.AvatarClassifiedReply -= new EventHandler<AvatarClassifiedReplyEventArgs>(Avatars_AvatarClassifiedsReply);
            Client.Avatars.PickInfoReply -= Avatars_PickInfoReply;
            Client.Parcels.ParcelInfoReply -= Parcels_ParcelInfoReply;
            Client.Avatars.AvatarGroupsReply -= Avatars_AvatarGroupsReply;
            Client.Avatars.AvatarInterestsReply -= Avatars_AvatarInterestsReply;
            Client.Avatars.AvatarNotesReply -= Avatars_AvatarNotesReply;
            Client.Self.MuteListUpdated -= Self_MuteListUpdated;
            NetCom.ClientDisconnected -= NetComClientDisconnected;
            Instance.InventoryClipboardUpdated -= instance_InventoryClipboardUpdated;
        }

        private void Self_MuteListUpdated(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                if (!Instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => Self_MuteListUpdated(sender, e)));
                }
                return;
            }

            UpdateMuteButton();
        }

        private void UpdateMuteButton()
        {
            bool isMuted = Client.Self.MuteList.Find(me => me.Type == MuteType.Resident && me.ID == AgentID) != null;

            if (isMuted)
            {
                btnMute.Enabled = false;
                btnUnmute.Enabled = true;
            }
            else
            {
                btnMute.Enabled = true;
                btnUnmute.Enabled = false;
            }
        }

        private void instance_InventoryClipboardUpdated(object sender, EventArgs e)
        {
            btnGive.Enabled = Instance.InventoryClipboard != null;
        }

        private void Avatars_AvatarGroupsReply(object sender, AvatarGroupsReplyEventArgs e)
        {
            if (e.AvatarID != AgentID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Avatars_AvatarGroupsReply(sender, e)));
                return;
            }

            lvwGroups.BeginUpdate();

            foreach (AvatarGroup g in e.Groups)
            {
                if (!lvwGroups.Items.ContainsKey(g.GroupID.ToString()))
                {
                    ListViewItem item = new ListViewItem {Name = g.GroupID.ToString(), Text = g.GroupName, Tag = g};
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item, g.GroupTitle));

                    lvwGroups.Items.Add(item);
                }
            }

            lvwGroups.EndUpdate();

        }

        private void Avatars_AvatarInterestsReply(object sender, AvatarInterestsReplyEventArgs e)
        {
            if (e.AvatarID != AgentID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Avatars_AvatarInterestsReply(sender, e)));
                return;
            }

            Interests = e.Interests;

            // want to's
            checkBoxBuild.Checked = (Interests.WantToMask & (1 << 0)) != 0;
            checkBoxExplore.Checked = (Interests.WantToMask & (1 << 1)) != 0;
            checkBoxMeet.Checked = (Interests.WantToMask & (1 << 2)) != 0;
            checkBoxGroup.Checked = (Interests.WantToMask & (1 << 3)) != 0;
            checkBoxBuy.Checked = (Interests.WantToMask & (1 << 4)) != 0;
            checkBoxSell.Checked = (Interests.WantToMask & (1 << 5)) != 0;
            checkBoxBeHired.Checked = (Interests.WantToMask & (1 << 6)) != 0;
            checkBoxHire.Checked = (Interests.WantToMask & (1 << 7)) != 0;
            txtWantTo.Text = Interests.WantToText;

            // skills
            checkBoxTextures.Checked = (Interests.WantToMask & (1 << 0)) != 0;
            checkBoxArchitecture.Checked = (Interests.WantToMask & (1 << 1)) != 0;
            checkBoxEventPlanning.Checked = (Interests.WantToMask & (1 << 2)) != 0;
            checkBoxModeling.Checked = (Interests.WantToMask & (1 << 3)) != 0;
            checkBoxScripting.Checked = (Interests.WantToMask & (1 << 4)) != 0;
            checkBoxCustomCharacters.Checked = (Interests.WantToMask & (1 << 5)) != 0;
            txtSkills.Text = Interests.SkillsText;

            txtLanguages.Text = Interests.LanguagesText;
        }

        private void Avatars_AvatarNotesReply(object sender, AvatarNotesReplyEventArgs e)
        {
            if (e.AvatarID != AgentID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Avatars_AvatarNotesReply(sender, e)));
                return;
            }
            rtbNotes.Text = e.Notes;
        }

        private void Avatars_AvatarPicksReply(object sender, AvatarPicksReplyEventArgs e)
        {
            if (e.AvatarID != AgentID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Avatars_AvatarPicksReply(sender, e)));
                return;
            }
            gotPicks = true;
            DisplayListOfPicks(e.Picks);
        }

        private void ClearPicks()
        {
            List<Control> controls = pickListPanel.Controls.Cast<Control>().Where(c => c != btnNewPick).ToList();
            foreach (Control c in controls)
                c.Dispose();
            pickDetailPanel.Visible = false;
        }

        private void DisplayListOfPicks(Dictionary<UUID, string> picks)
        {
            ClearPicks();

            int i = 0;
            Button firstButton = null;

            foreach (KeyValuePair<UUID, string> PickInfo in picks)
            {
                Button b = new Button
                {
                    AutoSize = false,
                    Tag = PickInfo.Key,
                    Name = PickInfo.Key.ToString(),
                    Text = PickInfo.Value,
                    Width = 135,
                    Height = 25,
                    Left = 2
                };
                b.Top = i++ * b.Height + 5;
                b.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                b.Click += PickButtonClick;
                pickListPanel.Controls.Add(b);

                if (firstButton == null)
                    firstButton = b;

                if (newPickID == PickInfo.Key)
                    firstButton = b;
            }

            newPickID = UUID.Zero;

            firstButton?.PerformClick();
        }

        private void PickButtonClick(object sender, EventArgs e)
        {
            pickDetailPanel.Visible = true;
            Button b = (Button)sender;
            requestedPick = (UUID)b.Tag;

            if (pickCache.ContainsKey(requestedPick))
            {
                Avatars_PickInfoReply(this, new PickInfoReplyEventArgs(requestedPick, pickCache[requestedPick]));
            }
            else
            {
                Client.Avatars.RequestPickInfo(AgentID, requestedPick);
            }
        }

        private void Avatars_PickInfoReply(object sender, PickInfoReplyEventArgs e)
        {
            if (e.PickID != requestedPick) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Avatars_PickInfoReply(sender, e)));
                return;
            }

            lock (pickCache)
            {
                if (!pickCache.ContainsKey(e.PickID))
                    pickCache.Add(e.PickID, e.Pick);
            }

            currentPick = e.Pick;

            if (pickPicturePanel.Controls.Count > 0)
                pickPicturePanel.Controls[0].Dispose();
            pickPicturePanel.Controls.Clear();

            if (AgentID == Client.Self.AgentID || e.Pick.SnapshotID != UUID.Zero)
            {
                SLImageHandler img = new SLImageHandler(Instance, e.Pick.SnapshotID, string.Empty)
                {
                    Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage
                };
                pickPicturePanel.Controls.Add(img);

                if (AgentID == Client.Self.AgentID)
                {
                    img.AllowUpdateImage = true;
                    ProfilePick p = e.Pick;
                    img.ImageUpdated += (psender, pe) =>
                    {
                        img.UpdateImage(pe.NewImageID);
                        p.SnapshotID = pe.NewImageID;
                        Client.Self.PickInfoUpdate(p.PickID, p.TopPick, p.ParcelID, p.Name, p.PosGlobal, p.SnapshotID, p.Desc);
                    };
                }
            }

            pickTitle.Text = e.Pick.Name;

            pickDetail.Text = e.Pick.Desc;

            if (!parcelCache.TryGetValue(e.Pick.ParcelID, out var parcel))
            {
                pickLocation.Text =
                    $"Unkown parcel, {e.Pick.SimName} ({(int)e.Pick.PosGlobal.X % 256}, {(int)e.Pick.PosGlobal.Y % 256}, {(int)e.Pick.PosGlobal.Z % 256})";
                Client.Parcels.RequestParcelInfo(e.Pick.ParcelID);
            }
            else
            {
                Parcels_ParcelInfoReply(this, new ParcelInfoReplyEventArgs(parcel));
            }
        }

        private void Parcels_ParcelInfoReply(object sender, ParcelInfoReplyEventArgs e)
        {
            if (currentPick.ParcelID != e.Parcel.ID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Parcels_ParcelInfoReply(sender, e)));
                return;
            }

            try
            {
                if (!parcelCache.ContainsKey(e.Parcel.ID))
                    parcelCache.Add(e.Parcel.ID, e.Parcel);
            }
            catch (ArgumentException) { /* ignore. faster than lock and check for duplicates. */ }

            // PickInfoReply packet always sends empty SimName. Why and when did that start? Dumb. Update it from parcel info.
            if (string.IsNullOrWhiteSpace(currentPick.SimName))
            {
                currentPick.SimName = e.Parcel.SimName;
            }

            pickLocation.Text =
                $"{e.Parcel.Name}, {currentPick.SimName} ({(int)currentPick.PosGlobal.X % 256}, {(int)currentPick.PosGlobal.Y % 256}, {(int)currentPick.PosGlobal.Z % 256})";
        }

        private void NetComClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(Close));
            }
            else
            {
                Close();
            }
        }

        private void Avatars_AvatarProfileReply(bool success, AgentProfileMessage profile)
        {
            if (!success)
            {
                return;
            }

            AvatarProperties = new Avatar.AvatarProperties
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
            populateFields();

            // populate notes
            rtbNotes.Text = profile.Notes;
        }

        private void Avatars_AvatarPropertiesReply(object sender, AvatarPropertiesReplyEventArgs e)
        {
            if (e.AvatarID != AgentID) return;

            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => Avatars_AvatarPropertiesReply(sender, e)));
                return;
            }
            AvatarProperties = e.Properties;

            populateFields();
        }

        private void populateFields()
        {
            FLImageID = AvatarProperties.FirstLifeImage;
            SLImageID = AvatarProperties.ProfileImage;

            if (AgentID == Client.Self.AgentID || SLImageID != UUID.Zero)
            {
                SLImageHandler pic = new SLImageHandler(Instance, SLImageID, "");

                if (AgentID == Client.Self.AgentID)
                {
                    pic.AllowUpdateImage = true;
                    pic.ImageUpdated += (usender, ue) =>
                    {
                        AvatarProperties.ProfileImage = ue.NewImageID;
                        pic.UpdateImage(ue.NewImageID);
                        Client.Self.UpdateProfile(AvatarProperties);
                    };
                }

                pic.Dock = DockStyle.Fill;
                pic.SizeMode = PictureBoxSizeMode.StretchImage;
                slPicPanel.Controls.Add(pic);
                slPicPanel.Show();
            }
            else
            {
                slPicPanel.Hide();
            }

            if (AgentID == Client.Self.AgentID || FLImageID != UUID.Zero)
            {
                SLImageHandler pic = new SLImageHandler(Instance, FLImageID, string.Empty) { Dock = DockStyle.Fill };

                if (AgentID == Client.Self.AgentID)
                {
                    pic.AllowUpdateImage = true;
                    pic.ImageUpdated += (usender, ue) =>
                    {
                        AvatarProperties.FirstLifeImage = ue.NewImageID;
                        pic.UpdateImage(ue.NewImageID);
                        Client.Self.UpdateProfile(AvatarProperties);
                    };
                }

                rlPicPanel.Controls.Add(pic);
                rlPicPanel.Show();
            }
            else
            {
                rlPicPanel.Hide();
            }

            BeginInvoke(
                new OnSetProfileProperties(SetProfileProperties),
                new object[] { AvatarProperties });
        }

        //called on GUI thread
        private delegate void OnSetProfileProperties(Avatar.AvatarProperties properties);
        private void SetProfileProperties(Avatar.AvatarProperties properties)
        {
            txtBornOn.Text = properties.BornOn;
            anPartner.AgentID = properties.Partner;

            if (fullName.EndsWith("Linden"))
            {
                rtbAccountInfo.AppendText("Linden Lab Employee\n");
            }
            else if (!string.IsNullOrEmpty(properties.CharterMember))
            {
                rtbAccountInfo.AppendText($"{properties.CharterMember}\n");
            }
            if (properties.Identified) { rtbAccountInfo.AppendText("Identified\n"); }
            if (properties.Transacted) { rtbAccountInfo.AppendText("Transacted\n"); }

            rtbAbout.AppendText(properties.AboutText);

            txtWebURL.Text = properties.ProfileURL;
            btnWebView.Enabled = btnWebOpen.Enabled = txtWebURL.TextLength > 0;

            rtbAboutFL.AppendText(properties.FirstLifeText);
        }

        private void InitializeProfile()
        {
            txtFullName.Text = fullName;
            txtFullName.AgentID = AgentID;
            btnOfferTeleport.Enabled = btnPay.Enabled = AgentID != Client.Self.AgentID;

            if (Client.Avatars.AgentProfileAvailable())
            {
                Task profileReq = Client.Avatars.RequestAgentProfile(AgentID, Avatars_AvatarProfileReply);
            }
            else
            {
                Client.Avatars.RequestAvatarProperties(AgentID);
                Client.Avatars.RequestAvatarNotes(AgentID);
            }

            UpdateMuteButton();

            if (AgentID == Client.Self.AgentID)
            {
                btnGive.Visible =
                    btnIM.Visible =
                    btnMute.Visible =
                    btnUnmute.Visible =
                    btnOfferTeleport.Visible =
                    btnPay.Visible =
                    btnFriend.Visible =
                    false;
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnWebView_Click(object sender, EventArgs e)
        {
            WebBrowser web = new WebBrowser {Dock = DockStyle.Fill, Url = new Uri(txtWebURL.Text)};

            pnlWeb.Controls.Add(web);
        }

        private void btnWebOpen_Click(object sender, EventArgs e)
        {
            Instance.MainForm.ProcessLink(txtWebURL.Text);
        }

        private void rtbAbout_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Instance.MainForm.ProcessLink(e.LinkText);
        }

        private void rtbAboutFL_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Instance.MainForm.ProcessLink(e.LinkText);
        }

        private void btnOfferTeleport_Click(object sender, EventArgs e)
        {
            Instance.MainForm.AddNotification(new ntfSendLureOffer(Instance, AgentID));
        }

        private void btnPay_Click(object sender, EventArgs e)
        {
            new frmPay(Instance, AgentID, fullName, false).ShowDialog();
        }

        private void textBox1_DragEnter(object sender, DragEventArgs e)
        {
            TreeNode node = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if (node == null)
            {
                e.Effect = DragDropEffects.None;
            }
            else
            {
                e.Effect = DragDropEffects.Copy | DragDropEffects.Move;
            }
        }

        private void textBox1_DragDrop(object sender, DragEventArgs e)
        {
            TreeNode node = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if (node == null) return;

            if (node.Tag is InventoryItem item)
            {
                Client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, AgentID, true);
                Instance.TabConsole.DisplayNotificationInChat($"Offered item {item.Name} to {fullName}.");
            }
            else if (node.Tag is InventoryFolder folder)
            {
                Client.Inventory.GiveFolder(folder.UUID, folder.Name, AgentID, true);
                Instance.TabConsole.DisplayNotificationInChat($"Offered folder {folder.Name} to {fullName}.");
            }
        }

        private void btnFriend_Click(object sender, EventArgs e)
        {
            Client.Friends.OfferFriendship(AgentID);
            this.btnFriend.Enabled = false;
            this.btnFriend.Text = "Sent Request";

            var message = $"You have offered friendship to {fullName}.";

            if (FriendsConsole.TryFindIMTab(Instance, AgentID, out var console))
            {
                console.TextManager.DisplayNotification(message);
            }

            Instance.TabConsole.DisplayNotificationInChat(message);
        }

        private void btnIM_Click(object sender, EventArgs e)
        {
            Instance.TabConsole.ShowIMTab(AgentID, fullName, true);
        }

        private void tabProfile_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabProfile.SelectedTab.Name == "tbpPicks" && !gotPicks)
            {
                Client.Avatars.RequestAvatarPicks(AgentID);
            }
        }

        private void btnTeleport_Click(object sender, EventArgs e)
        {
            if (currentPick.PickID == UUID.Zero) return;
            btnShowOnMap_Click(this, EventArgs.Empty);
            Instance.MainForm.WorldMap.DoTeleport();
        }

        private void btnShowOnMap_Click(object sender, EventArgs e)
        {
            if (currentPick.PickID == UUID.Zero) return;
            Instance.MainForm.MapTab.Select();
            Instance.MainForm.WorldMap.DisplayLocation(
                currentPick.SimName,
                (int)currentPick.PosGlobal.X % 256,
                (int)currentPick.PosGlobal.Y % 256,
                (int)currentPick.PosGlobal.Z % 256
            );
        }

        private void lvwGroups_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewItem item = lvwGroups.GetItemAt(e.X, e.Y);

            if (item != null)
            {
                try
                {
                    Instance.MainForm.ShowGroupProfile((AvatarGroup)item.Tag);
                }
                catch (Exception) { }
            }
        }

        private void btnGive_Click(object sender, EventArgs e)
        {
            if (Instance.InventoryClipboard == null) return;

            InventoryBase inv = Instance.InventoryClipboard.Item;

            if (inv is InventoryItem)
            {
                InventoryItem item = inv as InventoryItem;
                Client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, AgentID, true);
                Instance.TabConsole.DisplayNotificationInChat($"Offered item {item.Name} to {fullName}.");
            }
            else if (inv is InventoryFolder)
            {
                InventoryFolder folder = inv as InventoryFolder;
                Client.Inventory.GiveFolder(folder.UUID, folder.Name, AgentID, true);
                Instance.TabConsole.DisplayNotificationInChat($"Offered folder {folder.Name} to {fullName}.");
            }

        }

        private void rtbAbout_Leave(object sender, EventArgs e)
        {
            if (!myProfile) return;
            AvatarProperties.AboutText = rtbAbout.Text;
            Client.Self.UpdateProfile(AvatarProperties);
        }

        private void rtbAboutFL_Leave(object sender, EventArgs e)
        {
            if (!myProfile) return;
            AvatarProperties.FirstLifeText = rtbAboutFL.Text;
            Client.Self.UpdateProfile(AvatarProperties);
        }

        private void txtWebURL_Leave(object sender, EventArgs e)
        {
            if (!myProfile) return;
            btnWebView.Enabled = btnWebOpen.Enabled = txtWebURL.TextLength > 0;
            AvatarProperties.ProfileURL = txtWebURL.Text;
            Client.Self.UpdateProfile(AvatarProperties);
        }

        private void pickTitle_Leave(object sender, EventArgs e)
        {
            if (!myProfile) return;
            currentPick.Name = pickTitle.Text;
            currentPick.Desc = pickDetail.Text;

            Client.Self.PickInfoUpdate(currentPick.PickID,
                currentPick.TopPick,
                currentPick.ParcelID,
                currentPick.Name,
                currentPick.PosGlobal,
                currentPick.SnapshotID,
                currentPick.Desc);

            pickCache[currentPick.PickID] = currentPick;
        }

        private void btnDeletePick_Click(object sender, EventArgs e)
        {
            if (!myProfile) return;
            Client.Self.PickDelete(currentPick.PickID);
            ClearPicks();
            Client.Avatars.RequestAvatarPicks(AgentID);
        }

        private void btnNewPick_Click(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
                {
                    UUID parcelID = Client.Parcels.RequestRemoteParcelID(
                        Client.Self.SimPosition, Client.Network.CurrentSim.Handle, Client.Network.CurrentSim.ID);
                    newPickID = UUID.Random();

                    Client.Self.PickInfoUpdate(
                        newPickID,
                        false,
                        parcelID,
                        Instance.State.Parcel.Name,
                        Client.Self.GlobalPosition,
                        Instance.State.Parcel.SnapshotID,
                        Instance.State.Parcel.Desc
                        );

                    Invoke(new MethodInvoker(ClearPicks));
                    Client.Avatars.RequestAvatarPicks(AgentID);
                });
        }

        private void interestsUpdated(object sender, EventArgs e)
        {
            if (AgentID != Client.Self.AgentID)
            {
                return;
            }

            uint wantto = (checkBoxBuild.Checked ? 1u << 0 : 0u)
                          | (checkBoxExplore.Checked ? 1u << 1 : 0u)
                          | (checkBoxMeet.Checked ? 1u << 2 : 0u)
                          | (checkBoxGroup.Checked ? 1u << 3 : 0u)
                          | (checkBoxBuy.Checked ? 1u << 4 : 0u)
                          | (checkBoxSell.Checked ? 1u << 5 : 0u)
                          | (checkBoxBeHired.Checked ? 1u << 6 : 0u)
                          | (checkBoxHire.Checked ? 1u << 7 : 0u);

            uint skills = (checkBoxTextures.Checked ? 1u << 0 : 0u)
                          | (checkBoxArchitecture.Checked ? 1u << 1 : 0u)
                          | (checkBoxEventPlanning.Checked ? 1u << 2 : 0u)
                          | (checkBoxModeling.Checked ? 1u << 3 : 0u)
                          | (checkBoxScripting.Checked ? 1u << 4 : 0u)
                          | (checkBoxCustomCharacters.Checked ? 1u << 5 : 0u);

            var interests = new Avatar.Interests
            {
                SkillsMask = skills,
                WantToMask = wantto,
                LanguagesText = txtLanguages.Text,
                SkillsText = txtSkills.Text,
                WantToText = txtWantTo.Text
            };
            Client.Self.UpdateInterests(interests);
        }

        private void rtbNotes_Leave(object sender, EventArgs e)
        {
            Client.Self.UpdateProfileNotes(AgentID, rtbNotes.Text);
        }

        private void btnMute_Click(object sender, EventArgs e)
        {
            Client.Self.UpdateMuteListEntry(MuteType.Resident, AgentID, Instance.Names.GetLegacyName(AgentID));
        }

        private void btnUnmute_Click(object sender, EventArgs e)
        {
            MuteEntry me = Client.Self.MuteList.Find(mle => mle.Type == MuteType.Resident && mle.ID == AgentID);

            if (me != null)
            {
                Client.Self.RemoveMuteListEntry(me.ID, me.Name);
            }
            else
            {
                Client.Self.RemoveMuteListEntry(AgentID, Instance.Names.GetLegacyName(AgentID));
            }
        }

        private void btnRequestTeleport_Click(object sender, EventArgs e)
        {
            Instance.MainForm.AddNotification(new ntfSendLureRequest(Instance, AgentID));
        }
    }
}
