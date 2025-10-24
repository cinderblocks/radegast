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
using OpenMetaverse;
using Radegast;
using System.Windows.Forms;
using System.Threading;

namespace RadegastSpeech.Conversation
{
    /// <summary>
    /// Manages all conversations
    /// </summary>
    internal class Control : AreaControl
    {
        /// <summary>
        /// Conversations correspond to tabbed panels on the main window.
        /// </summary>
        private readonly Dictionary<string, Mode> conversations;
        /// <summary>
        /// Interruptions are short-lived conversations about dialog boxes, etc
        /// </summary>
        private readonly LinkedList<Mode> interruptions;

        // The permanent conversations.
        private Chat chat;
        private Closet inventory;
        private Friends friends;
        private Voice voice;
        private Surroundings surroundings;
        private Mode currentMode;
        private Mode interrupted;
        internal string LoginName;
        private bool firstTime = true;
        private const string CONVGRAMMAR = "conv";

        internal Control(PluginControl pc)
            : base(pc)
        {
            // Initialize the index to conversations and the list of pending interruptions.
            interruptions = new LinkedList<Mode>();
            conversations = new Dictionary<string, Mode>();
        }

        internal override void Start()
        {
            if (firstTime)
            {
                firstTime = false;

                control.listener.CreateGrammar(
                    CONVGRAMMAR,
                    new string[] { "talk to Max",
                    "skip",
                    "who is online",
                    "open the closet",
                    "friends",
                    "talk" });
            }

            // Automatically handle notifications (blue dialogs)
            Notification.OnNotificationDisplayed +=
                OnNotificationDisplayed;
            //            Notification.OnNotificationClosed +=
            //                new Notification.NotificationCallback(OnNotificationClosed);

            // Announce connect and disconnect.
            control.instance.NetCom.ClientConnected +=
                Network_ClientConnected;
            control.instance.NetCom.ClientDisconnected +=
                Network_Disconnected;

            control.instance.NetCom.ClientLoginStatus +=
                netcom_ClientLoginStatus;

            // Notice arrival in a new sim
            control.instance.Client.Network.SimChanged +=
                Network_SimChanged;

            control.instance.NetCom.ClientLoggingIn +=
                Netcom_ClientLoggingIn;
            // Watch the coming and going of main window tabs.
            control.instance.TabConsole.OnTabAdded +=
                TabConsole_OnTabAdded;
            control.instance.TabConsole.OnTabRemoved +=
                TabConsole_OnTabRemoved;

            // Notice when the active tab changes on the graphics user interface.
            control.instance.TabConsole.OnTabSelected +=
                OnTabChange;

            // Handle Instant Messages too
            control.instance.Client.Self.IM +=
                OnInstantMessage;

            // Outgoing IMs
            control.instance.NetCom.InstantMessageSent += Netcom_InstantMessageSent;

            // Watch for global keys
            control.instance.MainForm.KeyUp += MainForm_KeyUp;

            // System messages in chat window
            control.instance.TabConsole.OnChatNotification += TabConsole_OnChatNotification;

            control.listener.ActivateGrammar(CONVGRAMMAR);

        }

        /// <summary>
        /// Say various notifications that come in the chat
        /// </summary>
        /// <param name="sender">Message sender</param>
        /// <param name="e">Event args</param>
        private void TabConsole_OnChatNotification(object sender, ChatNotificationEventArgs e)
        {
            Talker.Say(e.Message);
        }

        /// <summary>
        /// Watch for global function keys.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainForm_KeyUp(object sender, KeyEventArgs e)
        {
            // Escape clears the speak-ahead queue.
            if (e.KeyCode == Keys.Escape)
            {
                Talker.Flush();
                Talker.SayMore("Flushed.");
                e.Handled = true;
            }
        }


        private void Netcom_ClientLoggingIn(object sender, Radegast.OverrideEventArgs e)
        {
            Talker.SayMore("Logging in.  Please wait.");
        }

        private void netcom_ClientLoginStatus(
            object sender,
            LoginProgressEventArgs e)
        {
            switch (e.Status)
            {
                case LoginStatus.ConnectingToLogin:
                    // Never seems to happen.  See Netcom_ClientLoggingIn
                    Talker.SayMore("Connecting to login server");
                    return;

                case LoginStatus.ConnectingToSim:
                    Talker.SayMore("Connecting to region");
                    return;

                case LoginStatus.Success:
                    LoginName = control.instance.NetCom.LoginOptions.FullName;
                    //Talker.SayMore("Logged in as " + LoginName);
                    //if (friends != null)
                    //    friends.Announce = true;
                    return;

                case LoginStatus.Failed:
                    Talker.Say(e.Message +
                        ". Press Enter twice to retry", Talk.BeepType.Bad);
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// Switch active conversation as tab focus moves.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnTabChange(object sender, TabEventArgs e)
        {
            ActivateConversationFromTab(e.Tab);
        }

        public void ActivateConversationFromTab(RadegastTab Tab)
        {
            System.Windows.Forms.Control sTabControl = Tab.Control;

            if (sTabControl is InventoryConsole && control.config["enabled_for_inventory"])
            {
                SelectConversation(inventory);
            }
            else if (sTabControl is ChatConsole)
            {
                if (chat == null)
                {
                    chat = new Chat(control);
                    chat.Console = sTabControl;
                    AddConversation(chat);
                }
                SelectConversation(chat);
            }
            else if (sTabControl is FriendsConsole && control.config["enabled_for_friends"])
            {
                SelectConversation(friends);
            }
            else if (sTabControl is VoiceConsole)
            {
                SelectConversation(voice);
            }
            else if (sTabControl is GroupIMTabWindow)
            {
                GroupIMTabWindow tab = (GroupIMTabWindow)sTabControl;
                SelectConversation(
                    control.instance.Groups[tab.SessionId].Name);
            }
            else if (sTabControl is ConferenceIMTabWindow)
            {
                ConferenceIMTabWindow tab = (ConferenceIMTabWindow)sTabControl;
                SelectConversation(tab.SessionName);
            }
            else if (sTabControl is IMTabWindow)
            {
                IMTabWindow tab = (IMTabWindow)sTabControl;
                SelectConversation(tab.TargetName);
            }
            else if (sTabControl is ObjectsConsole && control.config["enabled_for_objects"])
            {
                SelectConversation(surroundings);
            }

        }

        /// <summary>
        /// Create conversations as tabs are created.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TabConsole_OnTabAdded(object sender, TabEventArgs e)
        {
            CreateConversationFromTab(e.Tab, true);
        }

        public void CreateConversationFromTab(RadegastTab Tab, bool selectConversation)
        {
            System.Windows.Forms.Control sTabControl = Tab.Control;

            Mode newConv = null;

            // Create a conversation on first appearance of its tab.
            if (sTabControl is InventoryConsole && control.config["enabled_for_inventory"])
            {
                newConv = inventory = new Closet(control);
            }
            else if (sTabControl is ChatConsole)
            {
                if (chat != null) return;
                newConv = chat = new Chat(control);
            }
            else if (sTabControl is FriendsConsole && control.config["enabled_for_friends"])
            {
                newConv = friends = new Friends(control);
            }
            else if (sTabControl is VoiceConsole)
            {
                newConv = voice = new Voice(control);
            }
            else if (sTabControl is GroupIMTabWindow)
            {
                GroupIMTabWindow tab = (GroupIMTabWindow)sTabControl;
                AddConversation(new GroupIMSession(control, tab.SessionId));
                return;
            }
            else if (sTabControl is ConferenceIMTabWindow)
            {
                ConferenceIMTabWindow tab = (ConferenceIMTabWindow)sTabControl;
                AddConversation(new ConferenceIMSession(control, tab.SessionId, tab.SessionName));
                return;
            }
            else if (sTabControl is IMTabWindow)
            {
                IMTabWindow tab = (IMTabWindow)sTabControl;
                AddConversation(new SingleIMSession(control, tab.TargetName, tab.TargetId, tab.SessionId));
                return;
            }
            else if (sTabControl is ObjectsConsole && control.config["enabled_for_objects"])
            {
                surroundings = new Surroundings(control);
                AddConversation(surroundings);
            }

            // If a conversation was created, switch to it.
            if (newConv != null)
            {
                AddConversation(newConv);
                // Select CHAT as soon as it is created.
                if (selectConversation && sTabControl is ChatConsole)
                    SelectConversation(newConv);
            }
        }

        /// <summary>
        /// Quietly close conversations.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TabConsole_OnTabRemoved(object sender, TabEventArgs e)
        {
            System.Windows.Forms.Control sTabControl = e.Tab.Control;
            if (sTabControl is InventoryConsole)
                RemoveConversation(inventory.Title);
            else if (sTabControl is ChatConsole)
                RemoveConversation(chat.Title);
            else if (sTabControl is FriendsConsole)
                RemoveConversation(friends.Title);
            else if (sTabControl is VoiceConsole)
                RemoveConversation(voice.Title);
            else if (sTabControl is ConferenceIMTabWindow)
                RemoveConversation(((ConferenceIMTabWindow)e.Tab.Control).SessionName);
            else if (sTabControl is GroupIMTabWindow ||
                     sTabControl is IMTabWindow)
                RemoveConversation(sTabControl.Name);  // TODO wrong name
        }


        internal override void Shutdown()
        {
            if (chat != null)
            {
                chat.Dispose();
                chat = null;
            }

            // Automatically handle notifications (blue dialogs)
            Notification.OnNotificationDisplayed -=
                OnNotificationDisplayed;

            // Announce connect and disconnect.
            control.instance.NetCom.ClientConnected -=
                Network_ClientConnected;
            control.instance.NetCom.ClientDisconnected -=
                Network_Disconnected;

            control.instance.NetCom.ClientLoginStatus -=
                netcom_ClientLoginStatus;

            // Notice arrival in a new sim
            control.instance.Client.Network.SimChanged -=
                Network_SimChanged;

            control.instance.NetCom.ClientLoggingIn -=
                Netcom_ClientLoggingIn;
            // Watch the coming and going of main window tabs.
            control.instance.TabConsole.OnTabAdded -=
                TabConsole_OnTabAdded;
            control.instance.TabConsole.OnTabRemoved -=
                TabConsole_OnTabRemoved;

            // Notice when the active tab changes on the graphics user interface.
            control.instance.TabConsole.OnTabSelected -=
                OnTabChange;

            // Handle Instant Messages too
            control.instance.Client.Self.IM -=
                OnInstantMessage;

            // Outgoing IMs
            control.instance.NetCom.InstantMessageSent -= Netcom_InstantMessageSent;

            // System notifications in chat
            control.instance.TabConsole.OnChatNotification -= TabConsole_OnChatNotification;

            control.listener.DeactivateGrammar(CONVGRAMMAR);

            foreach (Mode m in conversations.Values)
            {
                m.Stop();
            }
            foreach (Mode m in interruptions)
            {
                m.Stop();
            }
            conversations.Clear();
            interruptions.Clear();
        }

        private void WatchKeys()
        {
        }

        internal bool amCurrent(Mode m)
        {
            return (currentMode == m);
        }

        /// <summary>
        /// Start an interrupting conversation.
        /// </summary>
        private void StartInterruption()
        {
            // Remember what we were talking about.
            if (interrupted == null)
                interrupted = currentMode;

            // Visually they stack up, so we take the last one first.
            currentMode = interruptions.Last();
            currentMode.Start();
        }

        /// <summary>
        /// Finish an interruption and resume normal conversation
        /// </summary>
        internal void FinishInterruption(Mode m)
        {
            lock (interruptions)
            {
                // Remove the terminating interruption from the list.
                interruptions.Remove(m);

                // Let it remove any event hooks, etc
                m.Stop();

                // If there are any other interruptions pending, start one.
                // Otherwise resume the interrupted conversation.
                if (interruptions.Count > 0)
                    StartInterruption();
                else
                {
                    currentMode = interrupted;
                    interrupted = null;
                    currentMode.Start();
                }
            }
        }

        private void Network_ClientConnected(object sender, EventArgs e)
        {
            Talker.Say("You are connected.", Talk.BeepType.Good);
            if (chat == null)
            {
                chat = new Chat(control);

                AddConversation(chat);
                SelectConversation(chat);
            }
        }

        private void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            Talker.Say("You are now in " +
                control.instance.Client.Network.CurrentSim.Name,
                Talk.BeepType.Good);
        }


        /// <summary>
        /// Announce reason for disconnect.
        /// </summary>
        /// <param name="reason"></param>
        /// <param name="message"></param>
        private void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            switch (e.Reason)
            {
                case NetworkManager.DisconnectType.ClientInitiated:
                    Talker.Say("You have been disconnected.");
                    break;
                case NetworkManager.DisconnectType.SimShutdown:
                    Talker.Say("You have been disconnected from the current region.",
                        Talk.BeepType.Bad);
                    break;
                case NetworkManager.DisconnectType.NetworkTimeout:
                    Talker.Say("You have been disconnected by a network timeout.",
                        Talk.BeepType.Bad);
                    break;
                case NetworkManager.DisconnectType.ServerInitiated:
                    Talker.Say("You have been disconnected by the server with the message " + e.Message,
                        Talk.BeepType.Bad);
                    break;
            }
        }
        private void ListFriends()
        {
            List<FriendInfo> onlineFriends = (from friend in control.instance.Client.Friends.FriendList 
                where friend.Value != null && friend.Value.IsOnline select friend.Value).ToList();

            string list = onlineFriends.Aggregate("", (current, f) => current + (f.Name + ", "));
            list += "are online.";
            Talker.Say(list);
        }


        /// <summary>
        /// Check for general commands
        /// </summary>
        /// <param name="message"></param>
        /// <returns>true if command recognized</returns>
        private bool Command(string message)
        {
            switch (message.ToLower())
            {
                case "talk to max":
                    AddInterruption(new Max(control));
                    break;
                case "who is online":
                    ListFriends();
                    break;
                case "open the closet":
                    control.instance.TabConsole.SelectTab("inventory");
                    SelectConversation(inventory);
                    break;
                case "friends":
                    control.instance.TabConsole.SelectTab("friends");
                    SelectConversation(friends);
                    break;
                case "skip":
                    Talker.Flush();
                    Talker.SayMore("Flushed.");
                    break;
                case "talk":
                    control.instance.TabConsole.SelectTab("chat");
                    SelectConversation(chat);
                    break;
                case "voice":
                    control.instance.TabConsole.SelectTab("voice");
                    SelectConversation(voice);
                    break;
                default:
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Dispatch recognized text to appropriate conversation.
        /// </summary>
        /// <param name="message"></param>
        internal void Hear(string message)
        {
            // General commands.
            if (Command(message)) return;

            // Let the current conversation handle it.
            currentMode?.Hear(message);
        }

        internal void SelectConversation(Mode c)
        {
            if (c == null)
            {
                Logger.Log("Trying to start non-existant conversation", Helpers.LogLevel.Warning);
                return;
            }
            // Avoid multiple starts.
            if (currentMode == c) return;

            // Let the old conversation deactivate any event hooks, grammars, etc.
            currentMode?.Stop();

            currentMode = c;
            currentMode.Start();
        }

        internal void SelectConversation(string name)
        {
            if (conversations.ContainsKey(name))
            {
                SelectConversation(conversations[name]);
            }
            else
            {
                Talker.Say("Can not find conversation " + name, Talk.BeepType.Bad);
            }
        }

        /// <summary>
        /// Find an existing conversation by name.
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        /// <remarks>Used for IM sessions.</remarks>
        internal Mode GetConversation(string title)
        {
            if (conversations.ContainsKey(title))
                return conversations[title];

            return null;
        }

        /// <summary>
        /// Add a conversation context to those we are tracking.
        /// </summary>
        /// <param name="m"></param>
        internal void AddConversation(Mode m)
        {
            if (!conversations.ContainsKey(m.Title))
                conversations[m.Title] = m;
        }

        /// <summary>
        /// Remove the context for a conversation that is no longer visible.
        /// </summary>
        /// <param name="name"></param>
        internal void RemoveConversation(string name)
        {
            bool change = false;

            lock (conversations)
            {
                if (conversations.ContainsKey(name))
                {
                    Mode doomed = conversations[name];
                    if (currentMode == doomed)
                    {
                        change = true;
                        currentMode = chat;
                    }
                    if (interrupted == doomed)
                        interrupted = chat;

                    conversations.Remove(name);
                    if (change)
                        SelectConversation(currentMode);
                }
            }
        }

        /// <summary>
        /// Take note of a new interruption.
        /// </summary>
        /// <param name="m"></param>
        internal void AddInterruption(Mode m)
        {
            lock (interruptions)
            {
                // Add to the end of the list.
                interruptions.AddLast(m);

                // If the list WAS empty, start this.
                if (interruptions.Count == 1)
                    StartInterruption();
            }
        }

        internal void ChangeFocus(Mode toThis)
        {
            currentMode = toThis;
            currentMode?.Start();
        }


        /// <summary>
        /// Event handler for new blue dialog boxes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnNotificationDisplayed(object sender, NotificationEventArgs e)
        {
            AddInterruption(new BlueMenu(control, e));
        }

        /// <summary>
        /// Event handler for outgoing IMs
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Netcom_InstantMessageSent(object sender, Radegast.InstantMessageSentEventArgs e)
        {
            // Message to an individual
            IMSession sess = (IMSession)control.converse.GetConversation(control.instance.Names.GetAsync(e.TargetID).GetAwaiter().GetResult());
            sess?.OnMessage(Client.Self.AgentID, Client.Self.Name, e.Message);
        }


        /// <summary>
        /// Handle Instant Messages
        /// </summary>
        /// <param name="im"></param>
        /// <param name="simulator"></param>
        private void OnInstantMessage(object sender, InstantMessageEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
                {
                    Thread.Sleep(100); // Give tab a chance to show up
                    IMSession sess = null;
                    string groupName;

                    // All sorts of things come in as a instant messages. For actual messages
                    // we need to match them up with an existing Conversation.  IM Conversations
                    // are keyed by the name of the group or individual involved.
                    switch (e.IM.Dialog)
                    {
                        case InstantMessageDialog.MessageFromAgent:
                            if (control.instance.Groups.ContainsKey(e.IM.IMSessionID))
                            {
                                // Message from a group member
                                groupName = control.instance.Groups[e.IM.IMSessionID].Name;
                                sess = (IMSession)control.converse.GetConversation(groupName);
                                if (sess != null)
                                    sess.OnMessage(e.IM.FromAgentID, e.IM.FromAgentName, e.IM.Message);
                                else
                                    Talker.Say(e.IM.FromAgentName + ", " + e.IM.Message);
                            }
                            else if (e.IM.BinaryBucket.Length >= 2)
                            {
                                // Ad-hoc friend conference
                                sess = (IMSession)control.converse.GetConversation(Utils.BytesToString(e.IM.BinaryBucket));
                                if (sess != null)
                                    sess.OnMessage(e.IM.FromAgentID, e.IM.FromAgentName, e.IM.Message);
                                else
                                    Talker.Say(e.IM.FromAgentName + ", " + e.IM.Message);
                            }
                            else if (e.IM.FromAgentName == "Second Life")
                            {
                                Talker.Say("Second Life says " + e.IM.Message);
                            }
                            else
                            {
                                // Message from an individual
                                sess = (IMSession)control.converse.GetConversation(e.IM.FromAgentName);
                                if (sess != null)
                                    sess.OnMessage(e.IM.FromAgentID, e.IM.FromAgentName, e.IM.Message);
                                else
                                    Talker.Say(e.IM.FromAgentName + ", " + e.IM.Message);
                            }
                            break;

                        case InstantMessageDialog.SessionSend:
                            if (control.instance.Groups.ContainsKey(e.IM.IMSessionID))
                            {
                                // Message from a group member
                                groupName = control.instance.Groups[e.IM.IMSessionID].Name;
                                sess = (IMSession)control.converse.GetConversation(groupName);
                            }
                            else if (e.IM.BinaryBucket.Length >= 2) // ad hoc friends conference
                            {
                                sess = (IMSession)control.converse.GetConversation(Utils.BytesToString(e.IM.BinaryBucket));
                            }

                            sess?.OnMessage(e.IM.FromAgentID, e.IM.FromAgentName, e.IM.Message);
                            break;

                        case InstantMessageDialog.FriendshipOffered:
                            Talker.Say(e.IM.FromAgentName + " is offering friendship.");
                            break;

                        default:
                            break;
                    }
                }
            );

        }
    }
}
