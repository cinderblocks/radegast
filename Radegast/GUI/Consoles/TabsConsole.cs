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
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class TabsConsole : UserControl
    {

        /// <summary>
        /// List of nearby avatars (radar data)
        /// </summary>
        public List<NearbyAvatar> NearbyAvatars
        {
            get
            {
                List<NearbyAvatar> res = new List<NearbyAvatar>();

                if (TabExists("chat"))
                {
                    ChatConsole c = (ChatConsole)Tabs["chat"].Control;
                    lock (c.agentSimHandle)
                    {
                        foreach (ListViewItem item in c.lvwObjects.Items)
                        {
                            if (item.Name != client.Self.AgentID.ToString())
                            {
                                res.Add(new NearbyAvatar() { ID = new UUID(item.Name), Name = item.Text });
                            }
                        }
                    }
                }

                return res;
            }
        }

        /// <summary>
        /// Delegate invoked on tab operations
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        public delegate void TabCallback(object sender, TabEventArgs e);

        /// <summary>
        /// Fired when a tab is selected
        /// </summary>
        public event TabCallback OnTabSelected;


        /// <summary>
        /// Delegate invoked when chat notification is printed
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        public delegate void ChatNotificationCallback(object sender, ChatNotificationEventArgs e);

        /// <summary>
        /// Fired when a tab is selected
        /// </summary>
        public event ChatNotificationCallback OnChatNotification;

        /// <summary>
        /// Fired when a new tab is added
        /// </summary>
        public event TabCallback OnTabAdded;

        /// <summary>
        /// Fired when a tab is removed
        /// </summary>
        public event TabCallback OnTabRemoved;

        private RadegastInstance instance;
        private GridClient client => instance.Client;
        private Radegast.Netcom netcom => instance.Netcom;
        public ChatTextManager MainChatManger { get; private set; }

        public Dictionary<string, RadegastTab> Tabs { get; } = new Dictionary<string, RadegastTab>();

        private ChatConsole chatConsole;

        /// <summary>
        /// Currently selected tab
        /// </summary>
        public RadegastTab SelectedTab { get; private set; }

        private Form owner;

        public TabsConsole(RadegastInstance instance)
        {
            InitializeComponent();
            Disposed += TabsConsole_Disposed;

            this.instance = instance;
            this.instance.ClientChanged += instance_ClientChanged;

            AddNetcomEvents();

            InitializeMainTab();
            InitializeChatTab();

            // Callbacks
            RegisterClientEvents(client);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void RegisterClientEvents(GridClient client)
        {
            client.Self.ScriptQuestion += Self_ScriptQuestion;
            client.Self.ScriptDialog += Self_ScriptDialog;
            client.Self.LoadURL += Self_LoadURL;
            client.Self.SetDisplayNameReply += Self_SetDisplayNameReply;
            client.Avatars.DisplayNameUpdate += Avatars_DisplayNameUpdate;
            client.Network.EventQueueRunning += Network_EventQueueRunning;
            client.Network.RegisterCallback(OpenMetaverse.Packets.PacketType.ScriptTeleportRequest, ScriptTeleportRequestHandler);
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Self.ScriptQuestion -= Self_ScriptQuestion;
            client.Self.ScriptDialog -= Self_ScriptDialog;
            client.Self.LoadURL -= Self_LoadURL;
            client.Self.SetDisplayNameReply -= Self_SetDisplayNameReply;
            client.Avatars.DisplayNameUpdate -= Avatars_DisplayNameUpdate;
            client.Network.EventQueueRunning -= Network_EventQueueRunning;
            client.Network.UnregisterCallback(OpenMetaverse.Packets.PacketType.ScriptTeleportRequest, ScriptTeleportRequestHandler);
        }

        void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(client);
        }

        void TabsConsole_Disposed(object sender, EventArgs e)
        {
            RemoveNetcomEvents();
            UnregisterClientEvents(client);
        }

        private void AddNetcomEvents()
        {
            netcom.ClientLoginStatus += netcom_ClientLoginStatus;
            netcom.ClientLoggedOut += netcom_ClientLoggedOut;
            netcom.ClientDisconnected += netcom_ClientDisconnected;
            netcom.ChatSent += netcom_ChatSent;
            netcom.AlertMessageReceived += netcom_AlertMessageReceived;
            netcom.InstantMessageReceived += netcom_InstantMessageReceived;
        }

        private void RemoveNetcomEvents()
        {
            netcom.ClientLoginStatus -= netcom_ClientLoginStatus;
            netcom.ClientLoggedOut -= netcom_ClientLoggedOut;
            netcom.ClientDisconnected -= netcom_ClientDisconnected;
            netcom.ChatSent -= netcom_ChatSent;
            netcom.AlertMessageReceived -= netcom_AlertMessageReceived;
            netcom.InstantMessageReceived -= netcom_InstantMessageReceived;
        }

        void ScriptTeleportRequestHandler(object sender, PacketReceivedEventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => ScriptTeleportRequestHandler(sender, e)));
                return;
            }

            var msg = (OpenMetaverse.Packets.ScriptTeleportRequestPacket)e.Packet;

            if (TabExists("map"))
            {
                Tabs["map"].Select();
                ((MapConsole)Tabs["map"].Control).CenterOnGlobalPos(
                    (float)(client.Self.GlobalPosition.X - client.Self.SimPosition.X) + msg.Data.SimPosition.X,
                    (float)(client.Self.GlobalPosition.Y - client.Self.SimPosition.Y) + msg.Data.SimPosition.Y,
                    msg.Data.SimPosition.Z);
            }
        }

        void Network_EventQueueRunning(object sender, EventQueueRunningEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Network_EventQueueRunning(sender, e)));
                return;
            }

            if (TabExists("friends")) return;
            if (e.Simulator == client.Network.CurrentSim)
            {
                client.Self.UpdateAgentLanguage("en", true); // FIXME: This should user set.
                InitializeOnlineTabs();
            }
        }

        void Self_ScriptDialog(object sender, ScriptDialogEventArgs e)
        {
            if (instance.MainForm.InvokeRequired)
            {
                instance.MainForm.BeginInvoke(new MethodInvoker(() => Self_ScriptDialog(sender, e)));
                return;
            }

            // Is this object muted
            if (null != client.Self.MuteList.Find(m => (m.Type == MuteType.Object && m.ID == e.ObjectID) // muted object by id
                || (m.Type == MuteType.ByName && m.Name == e.ObjectName) // object muted by name
                )) return;

            instance.MainForm.AddNotification(new ntfScriptDialog(instance, e.Message, e.ObjectName, e.ImageID, e.ObjectID, e.FirstName, e.LastName, e.Channel, e.ButtonLabels));
        }

        void Self_ScriptQuestion(object sender, ScriptQuestionEventArgs e)
        {
            // Is this object muted
            if (null != client.Self.MuteList.Find(m => (m.Type == MuteType.Object && m.ID == e.TaskID) // muted object by id
                || (m.Type == MuteType.ByName && m.Name == e.ObjectName) // object muted by name
                )) return;

            if (instance.GlobalSettings["on_script_question"] == "Auto Decline"
                || (instance.RLV.Enabled && instance.RLV.Permissions.IsAutoDenyPermissions()))
            {
                instance.Client.Self.ScriptQuestionReply(e.Simulator, e.ItemID, e.TaskID, 0);
            }
            else if (instance.GlobalSettings["on_script_question"] == "Auto Accept"
                || (instance.RLV.Enabled && instance.RLV.Permissions.IsAutoAcceptPermissions()))
            {
                instance.Client.Self.ScriptQuestionReply(e.Simulator, e.ItemID, e.TaskID, e.Questions);
            }
            else
            {
                instance.MainForm.AddNotification(new ntfPermissions(instance, e.Simulator, e.TaskID, e.ItemID, e.ObjectName, e.ObjectOwnerName, e.Questions));
            }
        }

        private void netcom_ClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Failed)
            {
                DisplayNotificationInChat("Login error: " + e.Message, ChatBufferTextStyle.Error);
            }
            else if (e.Status == LoginStatus.Success)
            {
                DisplayNotificationInChat($"Logged in as {netcom.LoginOptions.FullName}.", ChatBufferTextStyle.StatusDarkBlue);
                DisplayNotificationInChat("Login reply: " + e.Message, ChatBufferTextStyle.StatusDarkBlue);

                if (Tabs.ContainsKey("login"))
                {
                    if (SelectedTab.Name == "login")
                        SelectDefaultTab();
                    ForceCloseTab("login");
                }

                _ = client.Self.RetrieveInstantMessages();
            }
        }

        private void netcom_ClientLoggedOut(object sender, EventArgs e)
        {
            DisposeOnlineTabs();

            SelectDefaultTab();
            DisplayNotificationInChat("Logged out.");

        }

        private void netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            if (e.Reason == NetworkManager.DisconnectType.ClientInitiated) return;

            DisposeOnlineTabs();
            SelectDefaultTab();
            DisplayNotificationInChat($"Disconnected: {e.Message}", ChatBufferTextStyle.Error);
        }

        void Avatars_DisplayNameUpdate(object sender, DisplayNameUpdateEventArgs e)
        {
            DisplayNotificationInChat($"({e.DisplayName.UserName}) is now known as {e.DisplayName.DisplayName}");
        }

        void Self_SetDisplayNameReply(object sender, SetDisplayNameReplyEventArgs e)
        {
            if (e.Status == 200)
            {
                DisplayNotificationInChat($"You are now knows as {e.DisplayName.DisplayName}");
            }
            else
            {
                DisplayNotificationInChat($"Failed to set a new display name: {e.Reason}", ChatBufferTextStyle.Error);
            }
        }

        private void netcom_AlertMessageReceived(object sender, AlertMessageEventArgs e)
        {
            if (e.NotificationId == "AutopilotCanceled") { return; }

            // Pop up a notification for region restarts
            if (e.NotificationId == "RegionRestartMinutes") {
                var minutes = e.ExtraParams["MINUTES"].AsInteger();
                var regionName = e.ExtraParams["NAME"].AsString();
                instance.MainForm.AddNotification(new ntfRegionRestart(instance, regionName, minutes * 60));
            } else if (e.NotificationId == "RegionRestartSeconds") {
                var seconds = e.ExtraParams["SECONDS"].AsInteger();
                var regionName = e.ExtraParams["NAME"].AsString();
                instance.MainForm.AddNotification(new ntfRegionRestart(instance, regionName, seconds));
            }

            Tabs["chat"].Highlight();
        }

        private void netcom_ChatSent(object sender, ChatSentEventArgs e)
        {
            Tabs["chat"].Highlight();
        }

        void Self_LoadURL(object sender, LoadUrlEventArgs e)
        {
            // Is the object or the owner muted?
            if (null != client.Self.MuteList.Find(m => (m.Type == MuteType.Object && m.ID == e.ObjectID) // muted object by id 
                || (m.Type == MuteType.ByName && m.Name == e.ObjectName) // object muted by name
                || (m.Type == MuteType.Resident && m.ID == e.OwnerID) // object's owner muted
                )) return;

            instance.MainForm.AddNotification(new ntfLoadURL(instance, e));
        }

        private void netcom_InstantMessageReceived(object sender, InstantMessageEventArgs e)
        {
            // Messaage from someone we muted?
            if (null != client.Self.MuteList.Find(me => me.Type == MuteType.Resident && me.ID == e.IM.FromAgentID)) return;

            try
            {
                if (instance.State.LSLHelper.ProcessIM(e))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed executing automation action: " + ex, Helpers.LogLevel.Warning);
            }

            switch (e.IM.Dialog)
            {
                case InstantMessageDialog.SessionSend:
                    if (instance.Groups.ContainsKey(e.IM.IMSessionID))
                    {
                        HandleGroupIM(e);
                    }
                    else
                    {
                        HandleConferenceIM(e);
                    }
                    break;

                case InstantMessageDialog.MessageFromAgent:
                    if (e.IM.FromAgentName == "Second Life")
                    {
                        HandleIMFromObject(e);
                    }
                    else if (e.IM.FromAgentID == UUID.Zero)
                    {
                        instance.MainForm.AddNotification(new ntfGeneric(instance, e.IM.Message));
                    }
                    else if (e.IM.GroupIM || instance.Groups.ContainsKey(e.IM.IMSessionID))
                    {
                        HandleGroupIM(e);
                    }
                    else if (e.IM.BinaryBucket.Length > 1)
                    { // conference
                        HandleConferenceIM(e);
                    }
                    else if (e.IM.IMSessionID == UUID.Zero)
                    {
                        String msg = string.Format("Message from {0}: {1}", instance.Names.Get(e.IM.FromAgentID, e.IM.FromAgentName), e.IM.Message);
                        instance.MainForm.AddNotification(new ntfGeneric(instance, msg));
                        DisplayNotificationInChat(msg);
                    }
                    else
                    {
                        HandleIM(e);
                    }
                    break;

                case InstantMessageDialog.MessageFromObject:
                    HandleIMFromObject(e);
                    break;

                case InstantMessageDialog.StartTyping:
                    if (TabExists(e.IM.FromAgentName))
                    {
                        RadegastTab tab = Tabs[e.IM.FromAgentName.ToLower()];
                        if (!tab.Highlighted) tab.PartialHighlight();
                    }

                    break;

                case InstantMessageDialog.StopTyping:
                    if (TabExists(e.IM.FromAgentName))
                    {
                        RadegastTab tab = Tabs[e.IM.FromAgentName.ToLower()];
                        if (!tab.Highlighted) tab.Unhighlight();
                    }

                    break;

                case InstantMessageDialog.MessageBox:
                    instance.MainForm.AddNotification(new ntfGeneric(instance, e.IM.Message));
                    break;

                case InstantMessageDialog.RequestTeleport:
                    if (instance.RLV.Enabled && instance.RLV.Permissions.IsAutoAcceptTp(e.IM.FromAgentID.Guid))
                    {
                        DisplayNotificationInChat($"Automatically accepted a teleport request from {e.IM.FromAgentName}");
                        instance.Client.Self.TeleportLureRespond(e.IM.FromAgentID, e.IM.IMSessionID, true);
                    }
                    else
                    {
                        instance.MainForm.AddNotification(new ntfTeleport(instance, e.IM));
                    }
                    break;

                case InstantMessageDialog.RequestLure:
                    instance.MainForm.AddNotification(new ntfRequestLure(instance, e.IM));
                    break;

                case InstantMessageDialog.GroupInvitation:
                    instance.MainForm.AddNotification(new ntfGroupInvitation(instance, e.IM));
                    break;

                case InstantMessageDialog.FriendshipOffered:
                    if (e.IM.FromAgentName == "Second Life")
                    {
                        HandleIMFromObject(e);
                    }
                    else
                    {
                        instance.MainForm.AddNotification(new ntfFriendshipOffer(instance, e.IM));
                    }
                    break;

                case InstantMessageDialog.InventoryAccepted:
                    DisplayNotificationInChat($"{e.IM.FromAgentName} accepted your inventory offer.");
                    break;

                case InstantMessageDialog.InventoryDeclined:
                    DisplayNotificationInChat($"{e.IM.FromAgentName} declined your inventory offer.");
                    break;

                case InstantMessageDialog.GroupNotice:
                    // Is this group muted?
                    if (null != client.Self.MuteList.Find(me => me.Type == MuteType.Group && me.ID == e.IM.FromAgentID)) break;

                    instance.MainForm.AddNotification(new ntfGroupNotice(instance, e.IM));
                    break;

                case InstantMessageDialog.InventoryOffered:
                    var ion = new ntfInventoryOffer(instance, e.IM);
                    instance.MainForm.AddNotification(ion);
                    if (instance.GlobalSettings["inv_auto_accept_mode"].AsInteger() == 1)
                    {
                        ion.btnAccept.PerformClick();
                    }
                    else if (instance.GlobalSettings["inv_auto_accept_mode"].AsInteger() == 2)
                    {
                        ion.btnDiscard.PerformClick();
                    }
                    break;

                case InstantMessageDialog.TaskInventoryOffered:
                    // Is the object muted by name?
                    if (null != client.Self.MuteList.Find(me => me.Type == MuteType.ByName && me.Name == e.IM.FromAgentName)) break;

                    var iont = new ntfInventoryOffer(instance, e.IM);
                    instance.MainForm.AddNotification(iont);
                    if (instance.GlobalSettings["inv_auto_accept_mode"].AsInteger() == 1)
                    {
                        iont.btnAccept.PerformClick();
                    }
                    else if (instance.GlobalSettings["inv_auto_accept_mode"].AsInteger() == 2)
                    {
                        iont.btnDiscard.PerformClick();
                    }
                    break;
            }
        }

        /// <summary>
        /// Make default tab (chat) active
        /// </summary>
        public void SelectDefaultTab()
        {
            if (IsHandleCreated && TabExists("chat"))
                Tabs["chat"].Select();
        }

        /// <summary>
        /// Displays notification in the main chat tab
        /// </summary>
        /// <param name="msg">Message to be printed in the chat tab</param>
        public void DisplayNotificationInChat(string msg)
        {
            DisplayNotificationInChat(msg, ChatBufferTextStyle.ObjectChat);
        }

        /// <summary>
        /// Displays notification in the main chat tab
        /// </summary>
        /// <param name="msg">Message to be printed in the chat tab</param>
        /// <param name="style">Style of the message to be printed, normal, object, etc.</param>
        public void DisplayNotificationInChat(string msg, ChatBufferTextStyle style)
        {
            DisplayNotificationInChat(msg, style, true);
        }

        /// <summary>
        /// Displays notification in the main chat tab
        /// </summary>
        /// <param name="msg">Message to be printed in the chat tab</param>
        /// <param name="style">Style of the message to be printed, normal, object, etc.</param>
        /// <param name="highlightChatTab">Highligt (and flash in taskbar) chat tab if not selected</param>
        public void DisplayNotificationInChat(string msg, ChatBufferTextStyle style, bool highlightChatTab)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => DisplayNotificationInChat(msg, style, highlightChatTab)));
                return;
            }

            if (style != ChatBufferTextStyle.Invisible)
            {
                ChatBufferItem line = new ChatBufferItem(
                    DateTime.Now,
                    string.Empty,
                    UUID.Zero,
                    msg,
                    style
                );

                try
                {
                    MainChatManger.ProcessBufferItem(line, true);
                    if (highlightChatTab)
                    {
                        Tabs["chat"].Highlight();
                    }
                }
                catch (Exception) { }
            }

            if (OnChatNotification != null)
            {
                try { OnChatNotification(this, new ChatNotificationEventArgs(msg, style)); }
                catch { }
            }
        }

        private void HandleIMFromObject(InstantMessageEventArgs e)
        {
            // Is the object or the owner muted?
            if (null != client.Self.MuteList.Find(m => (m.Type == MuteType.Object && m.ID == e.IM.IMSessionID) // muted object by id 
                || (m.Type == MuteType.ByName && m.Name == e.IM.FromAgentName) // object muted by name
                || (m.Type == MuteType.Resident && m.ID == e.IM.FromAgentID) // object's owner muted
                )) return;

            DisplayNotificationInChat(e.IM.FromAgentName + ": " + e.IM.Message);
        }

        public static Control FindFocusedControl(Control control)
        {
            var container = control as ContainerControl;
            while (container != null)
            {
                control = container.ActiveControl;
                container = control as ContainerControl;
            }
            return control;
        }

        /// <summary>
        /// Creates new IM tab if needed
        /// </summary>
        /// <param name="agentID">IM session with agentID</param>
        /// <param name="label">Tab label</param>
        /// <param name="makeActive">Should tab be selected and focused</param>
        /// <returns>True if there was an existing IM tab, false if it was created</returns>
        public bool ShowIMTab(UUID agentID, string label, bool makeActive)
        {
            if (instance.TabConsole.TabExists((client.Self.AgentID ^ agentID).ToString()))
            {
                if (makeActive)
                {
                    instance.TabConsole.SelectTab((client.Self.AgentID ^ agentID).ToString());
                }
                return false;
            }

            instance.MediaManager.PlayUISound(makeActive ? UISounds.IMWindow : UISounds.IM);

            Control active = FindFocusedControl(instance.MainForm);

            instance.TabConsole.AddIMTab(agentID, client.Self.AgentID ^ agentID, label);

            if (makeActive)
            {
                instance.TabConsole.SelectTab((client.Self.AgentID ^ agentID).ToString());
            }
            else
            {
                active?.Focus();
            }

            return true;
        }

        private void HandleIM(InstantMessageEventArgs e)
        {
            bool isNew = ShowIMTab(e.IM.FromAgentID, e.IM.FromAgentName, false);
            if (!TabExists(e.IM.IMSessionID.ToString())) return; // this should now exist. sanity check anyway
            RadegastTab tab = Tabs[e.IM.IMSessionID.ToString()];
            tab.Highlight();

            if (isNew)
            {
                ((IMTabWindow)tab.Control).TextManager.ProcessIM(e, true);
            }
        }

        private bool ShouldIgnoreConference(UUID fromAgentId)
        {
            var ignoreConferenceChats = instance.GlobalSettings["ignore_conference_chats"].AsBoolean();
            var allowConferenceChatsFromFriends = instance.GlobalSettings["allow_conference_chats_from_friends"].AsBoolean();

            if (ignoreConferenceChats)
            {
                if (allowConferenceChatsFromFriends)
                {
                    var isConferenceFromFriend = instance.Client.Friends.FriendList.ContainsKey(fromAgentId);
                    if (!isConferenceFromFriend)
                    {
                        return true;
                    }
                }
                else
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleConferenceIM(InstantMessageEventArgs e)
        {
            if (TabExists(e.IM.IMSessionID.ToString()))
            {
                RadegastTab tab = Tabs[e.IM.IMSessionID.ToString()];
                tab.Highlight();
                return;
            }

            if (ShouldIgnoreConference(e.IM.FromAgentID))
            {
                var logIgnoredConferencesToChat = instance.GlobalSettings["log_ignored_conferences_to_chat"].AsBoolean();
                if (logIgnoredConferencesToChat)
                {
                    instance.TabConsole.DisplayNotificationInChat($"Your settings have prevented an unsolicited conference request from {e.IM.FromAgentName}.", ChatBufferTextStyle.Alert);
                }

                instance.Client.Self.RequestLeaveGroupChat(e.IM.IMSessionID);
                return;
            }

            instance.MediaManager.PlayUISound(UISounds.IM);

            Control active = FindFocusedControl(instance.MainForm);

            ConferenceIMTabWindow imTab = AddConferenceIMTab(e.IM.IMSessionID, Utils.BytesToString(e.IM.BinaryBucket));
            Tabs[e.IM.IMSessionID.ToString()].Highlight();
            imTab.TextManager.ProcessIM(e, true);

            active?.Focus();
        }

        private void HandleGroupIM(InstantMessageEventArgs e)
        {
            // Ignore group IM from a muted group
            if (null != client.Self.MuteList.Find(me => me.Type == MuteType.Group && (me.ID == e.IM.IMSessionID || me.ID == e.IM.FromAgentID))) return;

            if (TabExists(e.IM.IMSessionID.ToString()))
            {
                RadegastTab tab = Tabs[e.IM.IMSessionID.ToString()];
                tab.Highlight();
                return;
            }

            if (instance.GlobalSettings["group_im_sound"].AsBoolean())
            {
                instance.MediaManager.PlayUISound(UISounds.IM);
            }

            Control active = FindFocusedControl(instance.MainForm);

            GroupIMTabWindow imTab = AddGroupIMTab(e.IM.IMSessionID, Utils.BytesToString(e.IM.BinaryBucket));
            imTab.TextManager.ProcessIM(e, true);
            Tabs[e.IM.IMSessionID.ToString()].Highlight();

            active?.Focus();
        }

        public void InitializeMainTab()
        {
            if (TabExists("login"))
            {
                ForceCloseTab("login");
            }

            LoginConsole loginConsole = new LoginConsole(instance);

            RadegastTab tab = AddTab("login", "Login", loginConsole);
            tab.AllowClose = false;
            tab.AllowDetach = false;
            tab.AllowMerge = false;
            tab.AllowHide = false;

            loginConsole.RegisterTab(tab);
        }

        private void InitializeChatTab()
        {
            chatConsole = new ChatConsole(instance);
            MainChatManger = chatConsole.ChatManager;

            RadegastTab tab = AddTab("chat", "Chat", chatConsole);
            tab.AllowClose = false;
            tab.AllowDetach = false;
            tab.AllowHide = false;
        }


        /// <summary>
        /// Create Tabs that only make sense when connected
        /// </summary>
        private void InitializeOnlineTabs()
        {
            RadegastTab tab = AddTab("friends", "Friends", new FriendsConsole(instance));
            tab.AllowClose = false;
            tab.AllowDetach = true;
            tab.Visible = false;

            tab = AddTab("groups", "Groups", new GroupsConsole(instance));
            tab.AllowClose = false;
            tab.AllowDetach = true;
            tab.Visible = false;

            // Ugly workaround for a mono bug
            InventoryConsole inv = new InventoryConsole(instance);
            if (instance.MonoRuntime) inv.invTree.Scrollable = false;
            tab = AddTab("inventory", "Inventory", inv);
            if (instance.MonoRuntime) inv.invTree.Scrollable = true;
            tab.AllowClose = false;
            tab.AllowDetach = true;
            tab.Visible = false;

            tab = AddTab("search", "Search", new SearchConsole(instance));
            tab.AllowClose = false;
            tab.AllowDetach = true;
            tab.Visible = false;

            tab = AddTab("map", "Map", new MapConsole(instance));
            tab.AllowClose = false;
            tab.AllowDetach = true;
            tab.Visible = false;

            tab = AddTab("voice", "Voice", new VoiceConsole(instance));
            tab.AllowClose = false;
            tab.AllowDetach = true;
            tab.Visible = false;
        }

        /// <summary>
        /// Close tabs that make no sense when not connected
        /// </summary>
        private void DisposeOnlineTabs()
        {
            lock (Tabs)
            {
                ForceCloseTab("voice");
                ForceCloseTab("map");
                ForceCloseTab("search");
                ForceCloseTab("inventory");
                ForceCloseTab("groups");
                ForceCloseTab("friends");
            }
        }

        private void ForceCloseTab(string name)
        {
            if (!TabExists(name)) return;

            RadegastTab tab = Tabs[name];
            if (tab.Merged) SplitTab(tab);

            tab.AllowClose = true;
            tab.Close();
            tab = null;
        }


        public void RegisterContextAction(Type libomvType, String label, EventHandler handler)
        {
            instance.ContextActionManager.RegisterContextAction(libomvType, label, handler);
        }

        public void RegisterContextAction(ContextAction handler)
        {
            instance.ContextActionManager.RegisterContextAction(handler);
        }

        public void AddTab(RadegastTab tab)
        {
            ToolStripButton button = (ToolStripButton)tstTabs.Items.Add(tab.Label);
            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            button.Image = null;
            button.AutoToolTip = false;
            button.Tag = tab.Name;
            button.Click += TabButtonClick;
            tab.Button = button;
            Tabs.Add(tab.Name, tab);

            if (OnTabAdded != null)
            {
                try { OnTabAdded(this, new TabEventArgs(tab)); }
                catch (Exception) { }
            }
        }

        public RadegastTab AddTab(string name, string label, Control control)
        {
            // WORKAROUND: one should never add tab that alrady exists
            // but under some weird conditions disconnect/connect
            // fire in the wrong order
            if (TabExists(name))
            {
                Logger.Log("Force closing tab '" + name + "'", Helpers.LogLevel.Warning, client);
                ForceCloseTab(name);
            }

            control.Visible = false;
            control.Dock = DockStyle.Fill;
            toolStripContainer1.ContentPanel.Controls.Add(control);

            ToolStripButton button = (ToolStripButton)tstTabs.Items.Add(label);
            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            button.Image = null;
            button.AutoToolTip = false;
            button.Tag = name.ToLower();
            button.AllowDrop = true;
            button.Click += TabButtonClick;

            RadegastTab tab = new RadegastTab(instance, button, control, name.ToLower(), label);
            if (control is RadegastTabControl)
                ((RadegastTabControl)control).RadegastTab = tab;
            tab.TabAttached += tab_TabAttached;
            tab.TabDetached += tab_TabDetached;
            tab.TabSelected += tab_TabSelected;
            tab.TabClosed += tab_TabClosed;
            tab.TabHidden += tab_TabHidden;
            Tabs.Add(name.ToLower(), tab);

            if (OnTabAdded != null)
            {
                try { OnTabAdded(this, new TabEventArgs(tab)); }
                catch (Exception) { }
            }

            button.MouseDown += (msender, margs) =>
            {
                if (margs.Button == MouseButtons.Middle)
                {
                    if (tab.AllowClose)
                    {
                        tab.Close();
                    }
                    else if (tab.AllowHide)
                    {
                        tab.Hide();
                    }
                }
            };

            return tab;
        }

        private void tab_TabAttached(object sender, EventArgs e)
        {
            RadegastTab tab = (RadegastTab)sender;
            tab.Select();
        }

        private void tab_TabDetached(object sender, EventArgs e)
        {
            RadegastTab tab = (RadegastTab)sender;
            frmDetachedTab form = (frmDetachedTab)tab.Owner;

            form.ReattachStrip = tstTabs;
            form.ReattachContainer = toolStripContainer1.ContentPanel;
        }

        private void tab_TabSelected(object sender, EventArgs e)
        {
            RadegastTab tab = (RadegastTab)sender;

            if (SelectedTab != null &&
                SelectedTab != tab)
            { SelectedTab.Deselect(); }

            SelectedTab = tab;

            tbtnCloseTab.Enabled = !tab.Merged && (tab.AllowClose || tab.AllowHide);

            if (owner != null)
            {
                owner.AcceptButton = tab.DefaultControlButton;
            }

            if (OnTabSelected != null)
            {
                try { OnTabSelected(this, new TabEventArgs(SelectedTab)); }
                catch (Exception) { }
            }
        }

        void tab_TabHidden(object sender, EventArgs e)
        {
            RadegastTab tab = (RadegastTab)sender;

            if (SelectedTab != null && SelectedTab == tab)
            {
                tab.Deselect();
                SelectDefaultTab();
            }
        }

        private void tab_TabClosed(object sender, EventArgs e)
        {
            RadegastTab tab = (RadegastTab)sender;

            if (SelectedTab != null && SelectedTab == tab && tab.Name != "chat")
            {
                tab.Deselect();
                SelectDefaultTab();
            }

            Tabs.Remove(tab.Name);

            if (OnTabRemoved != null)
            {
                try { OnTabRemoved(this, new TabEventArgs(tab)); }
                catch (Exception) { }
            }

            tab = null;
        }

        private void TabButtonClick(object sender, EventArgs e)
        {
            ToolStripButton button = (ToolStripButton)sender;

            RadegastTab tab = Tabs[button.Tag.ToString()];
            tab.Select();
        }

        public void RemoveTabEntry(RadegastTab tab)
        {
            if (tstTabs.Items.Contains(tab.Button))
            {
                tstTabs.Items.Remove(tab.Button);
            }

            tab.Button.Dispose();
            Tabs.Remove(tab.Name);
        }

        public void RemoveTab(string name)
        {
            if (Tabs.ContainsKey(name))
            {
                Tabs.Remove(name);
            }
        }

        //Used for outside classes that have a reference to TabsConsole
        public void SelectTab(string name)
        {
            if (TabExists(name.ToLower()))
                Tabs[name.ToLower()].Select();
        }

        public bool TabExists(string name)
        {
            return Tabs.ContainsKey(name.ToLower());
        }

        public RadegastTab GetTab(string name)
        {
            if (TabExists(name.ToLower()))
                return Tabs[name.ToLower()];
            else
                return null;
        }

        public List<RadegastTab> GetOtherTabs()
        {
            List<RadegastTab> otherTabs = new List<RadegastTab>();

            foreach (ToolStripItem item in tstTabs.Items)
            {
                if (item.Tag == null) continue;
                if ((ToolStripItem)item == SelectedTab.Button) continue;

                RadegastTab tab = Tabs[item.Tag.ToString()];
                if (!tab.AllowMerge) continue;
                if (tab.Merged) continue;

                otherTabs.Add(tab);
            }

            return otherTabs;
        }

        /// <summary>
        /// Activates the next tab
        /// </summary>
        public void SelectNextTab()
        {
            List<ToolStripItem> buttons = new List<ToolStripItem>();

            foreach (ToolStripItem item in tstTabs.Items)
            {
                if (item.Tag == null || !item.Visible) continue;

                buttons.Add(item);
            }

            int current = 0;

            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] == SelectedTab.Button)
                {
                    current = i;
                    break;
                }
            }

            current++;

            if (current == buttons.Count)
                current = 0;

            SelectTab(Tabs[buttons[current].Tag.ToString()].Name);
        }

        /// <summary>
        /// Activates the previous tab
        /// </summary>
        public void SelectPreviousTab()
        {
            List<ToolStripItem> buttons = new List<ToolStripItem>();

            foreach (ToolStripItem item in tstTabs.Items)
            {
                if (item.Tag == null || !item.Visible) continue;

                buttons.Add(item);
            }

            int current = 0;

            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] == SelectedTab.Button)
                {
                    current = i;
                    break;
                }
            }

            current--;

            if (current == -1)
                current = buttons.Count - 1;

            SelectTab(Tabs[buttons[current].Tag.ToString()].Name);
        }


        public IMTabWindow AddIMTab(UUID target, UUID session, string targetName)
        {
            IMTabWindow imTab = new IMTabWindow(instance, target, session, targetName);

            RadegastTab tab = AddTab(session.ToString(), "IM: " + targetName, imTab);
            imTab.SelectIMInput();

            return imTab;
        }

        public ConferenceIMTabWindow AddConferenceIMTab(UUID session, string name)
        {
            ConferenceIMTabWindow imTab = new ConferenceIMTabWindow(instance, session, name);

            RadegastTab tab = AddTab(session.ToString(), name, imTab);
            imTab.SelectIMInput();

            return imTab;
        }

        public GroupIMTabWindow AddGroupIMTab(UUID session, string name)
        {
            GroupIMTabWindow imTab = new GroupIMTabWindow(instance, session, name);

            RadegastTab tab = AddTab(session.ToString(), name, imTab);
            imTab.SelectIMInput();

            return imTab;
        }

        public OutfitTextures AddOTTab(Avatar avatar)
        {
            OutfitTextures otTab = new OutfitTextures(instance, avatar);

            RadegastTab tab = AddTab("OT: " + avatar.Name, "OT: " + avatar.Name, otTab);
            otTab.GetTextures();
            return otTab;
        }

        public MasterTab AddMSTab(Avatar avatar)
        {
            MasterTab msTab = new MasterTab(instance, avatar);

            RadegastTab tab = AddTab("MS: " + avatar.Name, "MS: " + avatar.Name, msTab);
            return msTab;
        }

        public AnimTab AddAnimTab(Avatar avatar)
        {
            AnimTab animTab = new AnimTab(instance, avatar);

            RadegastTab tab = AddTab("Anim: " + avatar.Name, "Anim: " + avatar.Name, animTab);
            return animTab;
        }

        private void tbtnTabOptions_Click(object sender, EventArgs e)
        {
            tmnuMergeWith.Enabled = SelectedTab.AllowMerge;
            tmnuDetachTab.Enabled = SelectedTab.AllowDetach;

            tmnuMergeWith.DropDown.Items.Clear();

            if (!SelectedTab.AllowMerge) return;
            if (!SelectedTab.Merged)
            {
                tmnuMergeWith.Text = "Merge With";

                List<RadegastTab> otherTabs = GetOtherTabs();

                tmnuMergeWith.Enabled = (otherTabs.Count > 0);
                if (!tmnuMergeWith.Enabled) return;

                foreach (RadegastTab tab in otherTabs)
                {
                    ToolStripItem item = tmnuMergeWith.DropDown.Items.Add(tab.Label);
                    item.Tag = tab.Name;
                    item.Click += MergeItemClick;
                }
            }
            else
            {
                tmnuMergeWith.Text = "Split";
                tmnuMergeWith.Click += SplitClick;
            }
        }

        private void MergeItemClick(object sender, EventArgs e)
        {
            ToolStripItem item = (ToolStripItem)sender;
            RadegastTab tab = Tabs[item.Tag.ToString()];

            SelectedTab.MergeWith(tab);

            SplitContainer container = (SplitContainer)SelectedTab.Control;
            toolStripContainer1.ContentPanel.Controls.Add(container);

            SelectedTab.Select();
            RemoveTabEntry(tab);

            Tabs.Add(tab.Name, SelectedTab);
        }

        private void SplitClick(object sender, EventArgs e)
        {
            SplitTab(SelectedTab);
            SelectedTab.Select();
        }

        public void SplitTab(RadegastTab tab)
        {
            RadegastTab otherTab = tab.Split();
            if (otherTab == null) return;

            toolStripContainer1.ContentPanel.Controls.Add(tab.Control);
            toolStripContainer1.ContentPanel.Controls.Add(otherTab.Control);

            Tabs.Remove(otherTab.Name);
            AddTab(otherTab);
        }

        private void tmnuDetachTab_Click(object sender, EventArgs e)
        {
            if (!SelectedTab.AllowDetach) return;
            RadegastTab tab = SelectedTab;
            SelectDefaultTab();
            tab.Detach(instance);
        }

        private void tbtnCloseTab_Click(object sender, EventArgs e)
        {
            RadegastTab tab = SelectedTab;
            if (tab.Merged)
                return;
            else if (tab.AllowClose)
                tab.Close();
            else if (tab.AllowHide)
                tab.Hide();
        }

        private void TabsConsole_Load(object sender, EventArgs e)
        {
            owner = FindForm();
        }

        private void ctxTabs_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = false;

            Point pt = PointToClient(Cursor.Position);
            ToolStripItem stripItem = tstTabs.GetItemAt(pt);

            if (stripItem == null)
            {
                e.Cancel = true;
            }
            else
            {
                Tabs[stripItem.Tag.ToString()].Select();

                ctxBtnClose.Enabled = !SelectedTab.Merged && (SelectedTab.AllowClose || SelectedTab.AllowHide);
                ctxBtnDetach.Enabled = SelectedTab.AllowDetach;
                ctxBtnMerge.Enabled = SelectedTab.AllowMerge;
                ctxBtnMerge.DropDown.Items.Clear();

                if (!ctxBtnClose.Enabled && !ctxBtnDetach.Enabled && !ctxBtnMerge.Enabled)
                {
                    e.Cancel = true;
                    return;
                }

                if (!SelectedTab.AllowMerge) return;
                if (!SelectedTab.Merged)
                {
                    ctxBtnMerge.Text = "Merge With";

                    List<RadegastTab> otherTabs = GetOtherTabs();

                    ctxBtnMerge.Enabled = (otherTabs.Count > 0);
                    if (!ctxBtnMerge.Enabled) return;

                    foreach (RadegastTab tab in otherTabs)
                    {
                        ToolStripItem item = ctxBtnMerge.DropDown.Items.Add(tab.Label);
                        item.Tag = tab.Name;
                        item.Click += MergeItemClick;
                    }
                }
                else
                {
                    ctxBtnMerge.Text = "Split";
                    ctxBtnMerge.Click += SplitClick;
                }

            }
        }

    }

    /// <summary>
    /// Arguments for tab events
    /// </summary>
    public class TabEventArgs : EventArgs
    {
        /// <summary>
        /// Tab that was manipulated in the event
        /// </summary>
        public RadegastTab Tab;

        public TabEventArgs()
        {
        }

        public TabEventArgs(RadegastTab tab)
        {
            Tab = tab;
        }
    }

    /// <summary>
    /// Argument for chat notification events
    /// </summary>
    public class ChatNotificationEventArgs : EventArgs
    {
        public string Message;
        public ChatBufferTextStyle Style;

        public ChatNotificationEventArgs(string message, ChatBufferTextStyle style)
        {
            Message = message;
            Style = style;
        }
    }

    /// <summary>
    /// Element of list of nearby avatars
    /// </summary>
    public class NearbyAvatar
    {
        public UUID ID { get; set; }
        public string Name { get; set; }
    }

}
