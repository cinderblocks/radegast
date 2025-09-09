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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OpenMetaverse;
using Radegast.Core;

namespace Radegast
{
    public partial class ChatConsole : UserControl
    {
        private readonly RadegastInstance instance;
        private Radegast.Netcom netcom => instance.Netcom;
        private GridClient client => instance.Client;
        private TabsConsole tabConsole;
        private Avatar currentAvatar;
        private RadegastMovement movement => instance.Movement;
        private readonly Regex chatRegex = new Regex(@"^/(\d+)\s*(.*)", RegexOptions.Compiled);
        private readonly List<string> chatHistory = new List<string>();
        private int chatPointer;

        public readonly Dictionary<UUID, ulong> agentSimHandle = new Dictionary<UUID, ulong>();
        public ChatInputBox ChatInputText => cbxInput;

        public ChatConsole(RadegastInstance instance)
        {
            InitializeComponent();
            Disposed += ChatConsole_Disposed;

            if (!instance.advancedDebugging)
            {
                ctxAnim.Visible = false;
                ctxTextures.Visible = false;
            }

            this.instance = instance;
            this.instance.ClientChanged += instance_ClientChanged;

            instance.GlobalSettings.OnSettingChanged += GlobalSettings_OnSettingChanged;

            // Callbacks
            netcom.ClientLoginStatus += netcom_ClientLoginStatus;
            netcom.ClientLoggedOut += netcom_ClientLoggedOut;
            RegisterClientEvents(client);

            ChatManager = new ChatTextManager(instance, new RichTextBoxPrinter(rtbChat));
            ChatManager.PrintStartupMessage();

            this.instance.MainForm.Load += MainForm_Load;

            lvwObjects.ListViewItemSorter = new SorterClass(instance);
            cbChatType.SelectedIndex = 1;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void RegisterClientEvents(GridClient client)
        {
            client.Grid.CoarseLocationUpdate += Grid_CoarseLocationUpdate;
            client.Self.TeleportProgress += Self_TeleportProgress;
            client.Network.SimDisconnected += Network_SimDisconnected;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Grid.CoarseLocationUpdate -= Grid_CoarseLocationUpdate;
            client.Self.TeleportProgress -= Self_TeleportProgress;
            client.Network.SimDisconnected -= Network_SimDisconnected;
        }

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(client);
        }

        private void ChatConsole_Disposed(object sender, EventArgs e)
        {
            instance.ClientChanged -= instance_ClientChanged;
            netcom.ClientLoginStatus -= netcom_ClientLoginStatus;
            netcom.ClientLoggedOut -= netcom_ClientLoggedOut;
            UnregisterClientEvents(client);
            ChatManager.Dispose();
            ChatManager = null;
        }

        public static Font ChangeFontSize(Font font, float fontSize)
        {
            if (font != null)
            {
                float currentSize = font.Size;
                if (Math.Abs(currentSize - fontSize) > 0.01)
                {
                    font = new Font(font.Name, fontSize,
                        font.Style, font.Unit,
                        font.GdiCharSet, font.GdiVerticalFont);
                }
            }
            return font;
        }

        private void GlobalSettings_OnSettingChanged(object sender, SettingsEventArgs e)
        {
        }

        public List<UUID> GetAvatarList()
        {
            lock (agentSimHandle)
            {
                List<UUID> ret = new List<UUID>();
                foreach (ListViewItem item in lvwObjects.Items)
                {
                    if (item.Tag is UUID tag)
                        ret.Add(tag);
                }
                return ret;
            }
        }

        private void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            if (e.Status == TeleportStatus.Progress || e.Status == TeleportStatus.Finished)
            {
                ResetAvatarList();
            }
        }
        private void Network_SimDisconnected(object sender, SimDisconnectedEventArgs e)
        {
            try
            {
                if (InvokeRequired)
                {
                    if (!instance.MonoRuntime || IsHandleCreated)
                        BeginInvoke(new MethodInvoker(() => Network_SimDisconnected(sender, e)));
                    return;
                }
                lock (agentSimHandle)
                {
                    var h = e.Simulator.Handle;
                    List<UUID> remove = new List<UUID>();
                    foreach (var uh in agentSimHandle)
                    {
                        if (uh.Value == h)
                        {
                            remove.Add(uh.Key);
                        }
                    }
                    if (remove.Count == 0) return;
                    lvwObjects.BeginUpdate();
                    try
                    {
                        foreach (UUID key in remove)
                        {
                            agentSimHandle.Remove(key);
                            try
                            {
                                lvwObjects.Items.RemoveByKey(key.ToString());
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                    finally
                    {
                        lvwObjects.EndUpdate();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.DebugLog("Failed to update radar: " + ex);
            }
        }

        private void ResetAvatarList()
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(ResetAvatarList));
                return;
            }
            lock (agentSimHandle)
            {
                try
                {

                    lvwObjects.BeginUpdate();
                    agentSimHandle.Clear();
                    lvwObjects.Clear();
                }
                finally
                {
                    lvwObjects.EndUpdate();
                }
            }
        }

        private void Grid_CoarseLocationUpdate(object sender, CoarseLocationUpdateEventArgs e)
        {
            try
            {
                UpdateRadar(e);
            }
            catch { }
        }

        private void UpdateRadar(CoarseLocationUpdateEventArgs e)
        {
            if (client.Network.CurrentSim == null /*|| client.Network.CurrentSim.Handle != sim.Handle*/)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => UpdateRadar(e)));
                return;
            }

            // *TODO: later on we can set this with something from the GUI
            const double MAX_DISTANCE = 362.0; // one sim a corner to corner distance
            lock (agentSimHandle)
                try
                {
                    lvwObjects.BeginUpdate();
                    var agentPosition = e.Simulator.AvatarPositions.TryGetValue(client.Self.AgentID, out var position)
                        ? StateManager.ToVector3D(e.Simulator.Handle, position) 
                        : client.Self.GlobalPosition;

                    // CoarseLocationUpdate gives us height of 0 when actual height is
                    // between 1024-4096m.
                    if (agentPosition.Z < 0.1)
                    {
                        agentPosition.Z = client.Self.GlobalPosition.Z;
                    }

                    var existing = new List<UUID>();
                    var removed = new List<UUID>(e.RemovedEntries);

                    foreach (var avatarPos in e.Simulator.AvatarPositions)
                    {
                        existing.Add(avatarPos.Key);
                        if (lvwObjects.Items.ContainsKey(avatarPos.Key.ToString()))
                        {
                            continue;
                        }
                        var name = instance.Names.Get(avatarPos.Key);
                        var item = lvwObjects.Items.Add(avatarPos.Key.ToString(), name, string.Empty);
                        if (avatarPos.Key == client.Self.AgentID)
                        {
                            // Stops our name saying "Loading..."
                            item.Text = instance.Names.Get(avatarPos.Key, client.Self.Name);
                            item.Font = new Font(item.Font, FontStyle.Bold);
                        }
                        item.Tag = avatarPos.Key;
                        agentSimHandle[avatarPos.Key] = e.Simulator.Handle;
                    }

                    foreach (ListViewItem item in lvwObjects.Items)
                    {
                        if (item == null) continue;
                        var key = (UUID)item.Tag;

                        if (agentSimHandle[key] != e.Simulator.Handle)
                        {
                            // not for this sim
                            continue;
                        }

                        if (key == client.Self.AgentID)
                        {
                            if (instance.Names.Mode != NameMode.Standard)
                                item.Text = instance.Names.Get(key);
                            continue;
                        }

                        //the AvatarPositions is checked once more because it changes wildly on its own
                        //even though the !existing should have been adequate
                        if (!existing.Contains(key) || !e.Simulator.AvatarPositions.TryGetValue(key, out var pos))
                        {
                            // not here anymore
                            removed.Add(key);
                            continue;
                        }

                        var kvp = e.Simulator.ObjectsAvatars.FirstOrDefault(
                            av => av.Value.ID == key);
                        var foundAvi = kvp.Value;

                        // CoarseLocationUpdate gives us height of 0 when actual height is
                        // between 1024-4096m on OpenSim grids. 1020 on SL
                        var unknownAltitude = instance.Netcom.LoginOptions.Grid.Platform == "SecondLife" ? pos.Z == 1020f : pos.Z == 0f;
                        if (unknownAltitude) 
                        {
                            if (foundAvi != null)
                            {
                                if (foundAvi.ParentID == 0)
                                {
                                    pos.Z = foundAvi.Position.Z;
                                }
                                else
                                {
                                    if (e.Simulator.ObjectsPrimitives.TryGetValue(foundAvi.ParentID, out var primitive))
                                    {
                                        pos.Z = primitive.Position.Z;
                                    }
                                }
                            }
                        }

                        var d = (int)Vector3d.Distance(StateManager.ToVector3D(e.Simulator.Handle, pos), agentPosition);

                        if (e.Simulator != client.Network.CurrentSim && d > MAX_DISTANCE)
                        {
                            removed.Add(key);
                            continue;
                        }

                        if (unknownAltitude)
                        {
                            item.Text = instance.Names.Get(key) + " (?m)";
                        }
                        else
                        {
                            item.Text = instance.Names.Get(key) + $" ({d}m)";
                        }

                        if (foundAvi != null)
                        {
                            item.Text += "*";
                        }
                    }

                    foreach (var key in removed)
                    {
                        lvwObjects.Items.RemoveByKey(key.ToString());
                        agentSimHandle.Remove(key);
                    }

                    lvwObjects.Sort();
                }
                catch (Exception ex)
                {
                    Logger.Log("Grid_OnCoarseLocationUpdate: " + ex, Helpers.LogLevel.Error, client);
                }
                finally
                {
                    lvwObjects.EndUpdate();
                }
        }


        private void MainForm_Load(object sender, EventArgs e)
        {
            tabConsole = instance.TabConsole;
        }

        private void netcom_ClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status != LoginStatus.Success) return;

            cbxInput.Enabled = true;
            client.Avatars.RequestAvatarProperties(client.Self.AgentID);
            cbxInput.Focus();
        }

        private void netcom_ClientLoggedOut(object sender, EventArgs e)
        {
            cbxInput.Enabled = false;
            btnSay.Enabled = false;
            cbChatType.Enabled = false;

            lvwObjects.Items.Clear();
        }

        private void ChatHistoryPrev()
        {
            if (chatPointer == 0) return;
            chatPointer--;
            if (chatHistory.Count > chatPointer)
            {
                cbxInput.Text = chatHistory[chatPointer];
                cbxInput.SelectionStart = cbxInput.Text.Length;
                cbxInput.SelectionLength = 0;
            }
        }

        private void ChatHistoryNext()
        {
            if (chatPointer == chatHistory.Count) return;
            chatPointer++;
            if (chatPointer == chatHistory.Count)
            {
                cbxInput.Text = string.Empty;
                return;
            }
            cbxInput.Text = chatHistory[chatPointer];
            cbxInput.SelectionStart = cbxInput.Text.Length;
            cbxInput.SelectionLength = 0;
        }

        private void cbxInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                ChatHistoryPrev();
                return;
            }

            if (e.KeyCode == Keys.Down && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                ChatHistoryNext();
                return;
            }

            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;

            if (e.Shift)
                ProcessChatInput(cbxInput.Text, ChatType.Whisper);
            else if (e.Control)
                ProcessChatInput(cbxInput.Text, ChatType.Shout);
            else
                ProcessChatInput(cbxInput.Text, ChatType.Normal);
        }

        private static readonly char[] InvalidRlvEmoteCharacters = { '(', ')', '"', '-', '*', '=', '_', '^' };

        public void ProcessChatInput(string input, ChatType type)
        {
            if (string.IsNullOrEmpty(input)) return;
            chatHistory.Add(input);
            chatPointer = chatHistory.Count;
            ClearChatInput();

            var msg = input.Length >= 1000 ? input.Substring(0, 1000) : input;
            msg = msg.Replace(ChatInputBox.NewlineMarker, Environment.NewLine);

            if (instance.GlobalSettings["mu_emotes"].AsBoolean() && msg.StartsWith(":"))
            {
                msg = "/me " + msg.Substring(1);
            }

            int ch = 0;
            Match m = chatRegex.Match(msg);

            if (m.Groups.Count > 2)
            {
                ch = int.Parse(m.Groups[1].Value);
                msg = m.Groups[2].Value;
            }

            if (instance.CommandsManager.IsValidCommand(msg))
            {
                instance.CommandsManager.ExecuteCommand(msg);
            }
            else
            {
                #region RLV
                if (instance.RLV.Enabled)
                {
                    if ((type == ChatType.Normal || type == ChatType.Shout) && !instance.RLV.Permissions.CanChatNormal())
                    {
                        type = ChatType.Whisper;
                    }
                    else if (type == ChatType.Shout && !instance.RLV.Permissions.CanChatShout())
                    {
                        type = ChatType.Normal;
                    }
                    else if (type == ChatType.Whisper && !instance.RLV.Permissions.CanChatWhisper())
                    {
                        type = ChatType.Normal;
                    }

                    if (ch == 0 && !instance.RLV.Permissions.CanSendChat())
                    {
                        var hasValidCharacters = input.IndexOfAny(InvalidRlvEmoteCharacters) == -1;

                        // TODO: Make 'SplitMultibyteString' publicly accessible and use it here to truncate slash emotes and messages that start with '/'
                        if (!hasValidCharacters || !input.StartsWith("/", StringComparison.OrdinalIgnoreCase))
                        {
                            msg = "...";
                        }
                    }
                }
                #endregion

                var processedMessage = GestureManager.Instance.PreProcessChatMessage(msg).Trim();
                if (!string.IsNullOrEmpty(processedMessage))
                {
                    netcom.ChatOut(processedMessage, type, ch);
                }
            }
        }

        private void ClearChatInput()
        {
            cbxInput.Text = string.Empty;
        }

        private void btnSay_Click(object sender, EventArgs e)
        {
            ProcessChatInput(cbxInput.Text, (ChatType)cbChatType.SelectedIndex);
            cbxInput.Focus();
        }

        private void cbxInput_TextChanged(object sender, EventArgs e)
        {
            if (cbxInput.Text.Length > 0)
            {
                btnSay.Enabled = cbChatType.Enabled = true;

                if (!cbxInput.Text.StartsWith("/"))
                {
                    if (!instance.State.IsTyping && !instance.GlobalSettings["no_typing_anim"].AsBoolean())
                        instance.State.SetTyping(true);
                }
            }
            else
            {
                btnSay.Enabled = cbChatType.Enabled = false;
                if (!instance.GlobalSettings["no_typing_anim"].AsBoolean())
                    instance.State.SetTyping(false);
            }
        }

        public ChatTextManager ChatManager { get; private set; }

        private void tbtnStartIM_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count == 0) return;
            UUID av = (UUID)lvwObjects.SelectedItems[0].Tag;
            string name = instance.Names.Get(av);
            instance.TabConsole.ShowIMTab(av, name, true);
        }

        private void tbtnFollow_Click(object sender, EventArgs e)
        {
            Avatar av = currentAvatar;
            if (av == null) return;

            if (instance.State.FollowName == string.Empty)
            {
                instance.State.Follow(av.Name, av.ID);
                ctxFollow.Text = $"Unfollow {av.Name}";
            }
            else
            {
                instance.State.Follow(string.Empty, UUID.Zero);
                ctxFollow.Text = "Follow";
            }
        }

        private void ctxPoint_Click(object sender, EventArgs e)
        {
            if (!instance.State.IsPointing)
            {
                Avatar av = currentAvatar;
                if (av == null) return;
                instance.State.SetPointing(av, 5);
                ctxPoint.Text = "Unpoint";
            }
            else
            {
                ctxPoint.Text = "Point at";
                instance.State.UnSetPointing();
            }
        }


        private void lvwObjects_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count == 0)
            {
                currentAvatar = null;
                ctxPay.Enabled = ctxPoint.Enabled = ctxStartIM.Enabled = ctxFollow.Enabled = ctxProfile.Enabled = ctxTextures.Enabled = ctxMaster.Enabled = ctxAttach.Enabled = ctxAnim.Enabled = false;
            }
            else
            {

                var kvp = client.Network.CurrentSim.ObjectsAvatars.FirstOrDefault(
                    a => a.Value.ID == (UUID) lvwObjects.SelectedItems[0].Tag);
                if (kvp.Value != null)
                {
                    currentAvatar = kvp.Value;
                }

                ctxPay.Enabled = ctxStartIM.Enabled = ctxProfile.Enabled = true;
                ctxPoint.Enabled = ctxFollow.Enabled = ctxTextures.Enabled = ctxMaster.Enabled = ctxAttach.Enabled = ctxAnim.Enabled = currentAvatar != null;

                if ((UUID)lvwObjects.SelectedItems[0].Tag == client.Self.AgentID)
                {
                    ctxPay.Enabled = ctxFollow.Enabled = ctxStartIM.Enabled = false;
                }
            }
            if (instance.State.IsPointing)
            {
                ctxPoint.Enabled = true;
            }
        }

        private void rtbChat_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            instance.MainForm.ProcessLink(e.LinkText);
        }

        private void tbtnProfile_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count == 0) return;
            UUID av = (UUID)lvwObjects.SelectedItems[0].Tag;
            string name = instance.Names.Get(av);

            instance.MainForm.ShowAgentProfile(name, av);
        }

        private void dumpOutfitBtn_Click(object sender, EventArgs e)
        {
            Avatar av = currentAvatar;
            if (av == null) return;

            if (!instance.TabConsole.TabExists($"OT: {av.Name}"))
            {
                instance.TabConsole.AddOTTab(av);
            }
            instance.TabConsole.SelectTab($"OT: {av.Name}");
        }

        private void tbtnMaster_Click(object sender, EventArgs e)
        {
            Avatar av = currentAvatar;
            if (av == null) return;

            if (!instance.TabConsole.TabExists($"MS: {av.Name}"))
            {
                instance.TabConsole.AddMSTab(av);
            }
            instance.TabConsole.SelectTab($"MS: {av.Name}");
        }

        private void tbtnAttach_Click(object sender, EventArgs e)
        {
            Avatar av = currentAvatar;
            if (av == null) return;

            if (!instance.TabConsole.TabExists($"AT: {av.ID}"))
            {
                instance.TabConsole.AddTab($"AT: {av.ID}", $"AT: {av.Name}", new AttachmentTab(instance, av));

            }
            instance.TabConsole.SelectTab($"AT: {av.ID}");
        }

        private void tbtnAnim_Click(object sender, EventArgs e)
        {
            Avatar av = currentAvatar;
            if (av == null) return;

            if (!instance.TabConsole.TabExists($"Anim: {av.Name}"))
            {
                instance.TabConsole.AddAnimTab(av);
            }
            instance.TabConsole.SelectTab($"Anim: {av.Name}");


        }

        private void btnTurnLeft_MouseDown(object sender, MouseEventArgs e)
        {
            movement.TurningLeft = true;
        }

        private void btnTurnLeft_MouseUp(object sender, MouseEventArgs e)
        {
            movement.TurningLeft = false;
        }

        private void btnTurnRight_MouseDown(object sender, MouseEventArgs e)
        {
            movement.TurningRight = true;
        }

        private void btnTurnRight_MouseUp(object sender, MouseEventArgs e)
        {
            movement.TurningRight = false;
        }

        private void btnFwd_MouseDown(object sender, MouseEventArgs e)
        {
            movement.MovingForward = true;
        }

        private void btnFwd_MouseUp(object sender, MouseEventArgs e)
        {
            movement.MovingForward = false;
        }

        private void btnMoveBack_MouseDown(object sender, MouseEventArgs e)
        {
            movement.MovingBackward = true;
        }

        private void btnMoveBack_MouseUp(object sender, MouseEventArgs e)
        {
            movement.MovingBackward = false;
        }

        private void btnMoveUp_MouseDown(object Sender, MouseEventArgs e)
        {
            movement.Jump = true;
        }

        private void btnMoveUp_MouseUp(object Sender, MouseEventArgs e)
        {
            movement.Jump = false;
        }

        private void btnMoveDown_MouseDown(object Sender, MouseEventArgs e)
        {
            movement.Crouch = true;
        }

        private void btnMoveDown_MouseUp(object Sender, MouseEventArgs e)
        {
            movement.Crouch = false;
        }

        private void btnFly_Click(object sender, EventArgs e)
        {
            movement.ToggleFlight();
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            movement.ToggleAlwaysRun();
        }

        private void lvwObjects_DragDrop(object sender, DragEventArgs e)
        {
            Point local = lvwObjects.PointToClient(new Point(e.X, e.Y));
            ListViewItem litem = lvwObjects.GetItemAt(local.X, local.Y);
            if (litem == null) { return; }
            if (!(e.Data.GetData(typeof(TreeNode)) is TreeNode node)) { return; }

            if (node.Tag is InventoryItem item)
            {
                client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, (UUID)litem.Tag, true);
                instance.TabConsole.DisplayNotificationInChat($"Offered item {item.Name} to {instance.Names.Get((UUID)litem.Tag)}.");
            }
            else if (node.Tag is InventoryFolder folder)
            {
                client.Inventory.GiveFolder(folder.UUID, folder.Name, (UUID)litem.Tag, true);
                instance.TabConsole.DisplayNotificationInChat($"Offered folder {folder.Name} to {instance.Names.Get((UUID)litem.Tag)}.");
            }
        }

        private void lvwObjects_DragOver(object sender, DragEventArgs e)
        {
            Point local = lvwObjects.PointToClient(new Point(e.X, e.Y));
            ListViewItem litem = lvwObjects.GetItemAt(local.X, local.Y);
            if (litem == null) { return; }

            if (!e.Data.GetDataPresent(typeof(TreeNode))) { return; }

            e.Effect = DragDropEffects.Copy;
        }

        private void avatarContext_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = false;
            if (lvwObjects.SelectedItems.Count == 0 && !instance.State.IsPointing)
            {
                e.Cancel = true;
                return;
            }
            else if (instance.State.IsPointing)
            {
                ctxPoint.Enabled = true;
                ctxPoint.Text = "Unpoint";
            }

            bool isMuted = null != client.Self.MuteList.Find(me => me.Type == MuteType.Resident && me.ID == (UUID)lvwObjects.SelectedItems[0].Tag);
            muteToolStripMenuItem.Text = isMuted ? "Unmute" : "Mute";

            instance.ContextActionManager.AddContributions(
                avatarContext, typeof(Avatar), lvwObjects.SelectedItems[0]);
        }

        private void ctxPay_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            (new frmPay(instance, (UUID)lvwObjects.SelectedItems[0].Tag, instance.Names.Get((UUID)lvwObjects.SelectedItems[0].Tag), false)).ShowDialog();
        }

        private void ChatConsole_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
                cbxInput.Focus();
        }

        private void rtbChat_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            RadegastContextMenuStrip cms = new RadegastContextMenuStrip();
            instance.ContextActionManager.AddContributions(cms, instance.Client);
            cms.Show((Control)sender, new Point(e.X, e.Y));
        }

        private void lvwObjects_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewItem item = lvwObjects.GetItemAt(e.X, e.Y);
            if (item != null)
            {
                try
                {
                    UUID agentID = new UUID(item.Tag.ToString());
                    instance.MainForm.ShowAgentProfile(instance.Names.Get(agentID), agentID);
                }
                catch (Exception) { }
            }
        }

        private void cbxInput_SizeChanged(object sender, EventArgs e)
        {
            pnlChatInput.Height = cbxInput.Height + 3;
        }

        private void splitContainer1_Panel1_SizeChanged(object sender, EventArgs e)
        {
            rtbChat.Size = splitContainer1.Panel1.ClientSize;
        }

        private void ctxOfferTP_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID av = (UUID)lvwObjects.SelectedItems[0].Tag;
            instance.MainForm.AddNotification(new ntfSendLureOffer(instance, av));
        }

        private void ctxTeleportTo_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID person = (UUID)lvwObjects.SelectedItems[0].Tag;
            string pname = instance.Names.Get(person);

            if (instance.State.TryFindAvatar(person, out var sim, out var pos))
            {
                tabConsole.DisplayNotificationInChat($"Teleporting to {pname}");
                instance.State.MoveTo(sim, pos, true);
            }
            else
            {
                tabConsole.DisplayNotificationInChat($"Could not locate {pname}");
            }
        }

        private void ctxEject_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID av = (UUID)lvwObjects.SelectedItems[0].Tag;
            client.Parcels.EjectUser(av, false);
        }

        private void ctxBan_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID av = (UUID)lvwObjects.SelectedItems[0].Tag;
            client.Parcels.EjectUser(av, true);
        }

        private void ctxEstateEject_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID av = (UUID)lvwObjects.SelectedItems[0].Tag;
            client.Estate.KickUser(av);
        }

        private void muteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;

            var agentID = (UUID)lvwObjects.SelectedItems[0].Tag;
            if (agentID == client.Self.AgentID) return;

            if (muteToolStripMenuItem.Text == "Mute")
            {
                client.Self.UpdateMuteListEntry(MuteType.Resident, agentID, instance.Names.GetLegacyName(agentID));
            }
            else
            {
                client.Self.RemoveMuteListEntry(agentID, instance.Names.GetLegacyName(agentID));
            }
        }

        private void faceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID person = (UUID)lvwObjects.SelectedItems[0].Tag;
            string pname = instance.Names.Get(person);

            if (instance.State.TryFindAvatar(person, out var targetPos))
            {
                client.Self.Movement.TurnToward(targetPos);
                instance.TabConsole.DisplayNotificationInChat("Facing " + pname);
            }
        }

        private void goToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID person = (UUID)lvwObjects.SelectedItems[0].Tag;
            string pname = instance.Names.Get(person);

            if (instance.State.TryFindAvatar(person, out var sim, out var targetPos))
            {
                instance.State.MoveTo(sim, targetPos, false);
            }
        }

        private void ctxReqestLure_Click(object sender, EventArgs e)
        {
            if (lvwObjects.SelectedItems.Count != 1) return;
            UUID av = (UUID)lvwObjects.SelectedItems[0].Tag;
            instance.MainForm.AddNotification(new ntfSendLureRequest(instance, av));
        }
    }

    public class SorterClass : System.Collections.IComparer
    {
        private static readonly Regex distanceRegex = new Regex(@"\((?<dist>\d+)\s*m\)", RegexOptions.Compiled);
        private Match match;
        private readonly RadegastInstance instance;

        public SorterClass(RadegastInstance instance)
        {
            this.instance = instance;
        }

        //this routine should return -1 if xy and 0 if x==y.
        // for our sample we'll just use string comparison
        public int Compare(object x, object y)
        {

            ListViewItem item1 = (ListViewItem)x;
            ListViewItem item2 = (ListViewItem)y;

            if ((item1.Tag is UUID tag) && (tag == instance.Client.Self.AgentID))
                return -1;

            if ((item2.Tag is UUID uuid) && (uuid == instance.Client.Self.AgentID))
                return 1;

            int distance1 = int.MaxValue, distance2 = int.MaxValue;

            if ((match = distanceRegex.Match(item1.Text)).Success)
                distance1 = int.Parse(match.Groups["dist"].Value);

            if ((match = distanceRegex.Match(item2.Text)).Success)
                distance2 = int.Parse(match.Groups["dist"].Value);

            if (distance1 < distance2)
                return -1;
            else if (distance1 > distance2)
                return 1;
            else
                return string.CompareOrdinal(item1.Text, item2.Text);

        }
    }

}
