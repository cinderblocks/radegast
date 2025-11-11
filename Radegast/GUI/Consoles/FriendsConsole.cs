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
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenMetaverse.StructuredData;
using System.Threading;
using OpenMetaverse;

namespace Radegast
{
    public partial class FriendsConsole : UserControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private FriendInfo selectedFriend;
        private bool settingFriend = false;
        private readonly object lockOneAtaTime = new object();

        public FriendsConsole(RadegastInstanceForms instance)
        {
            InitializeComponent();
            Disposed += FriendsConsole_Disposed;

            this.instance = instance;

            if (instance.GlobalSettings["show_friends_online_notifications"].Type == OSDType.Unknown)
            {
                instance.GlobalSettings["show_friends_online_notifications"] = OSD.FromBoolean(true);
            }

            if (instance.GlobalSettings["friends_notification_highlight"].Type == OSDType.Unknown)
            {
                instance.GlobalSettings["friends_notification_highlight"] = new OSDBoolean(true);
            }

            // Callbacks
            client.Friends.FriendOffline += Friends_FriendOffline;
            client.Friends.FriendOnline += Friends_FriendOnline;
            client.Friends.FriendshipTerminated += Friends_FriendshipTerminated;
            client.Friends.FriendshipResponse += Friends_FriendshipResponse;
            client.Friends.FriendNames += Friends_FriendNames;
            Load += FriendsConsole_Load;
            instance.Names.NameUpdated += Names_NameUpdated;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void Names_NameUpdated(object sender, UUIDNameReplyEventArgs e)
        {
            bool moded = e.Names.Keys.Any(id => client.Friends.FriendList.ContainsKey(id));

            if (moded)
            {
                if (InvokeRequired)
                    BeginInvoke(new MethodInvoker(() => listFriends.Invalidate()));
                else
                    listFriends.Invalidate();
            }
        }

        private void FriendsConsole_Disposed(object sender, EventArgs e)
        {
            client.Friends.FriendOffline -= Friends_FriendOffline;
            client.Friends.FriendOnline -= Friends_FriendOnline;
            client.Friends.FriendshipTerminated -= Friends_FriendshipTerminated;
            client.Friends.FriendshipResponse -= Friends_FriendshipResponse;
            client.Friends.FriendNames -= Friends_FriendNames;
        }

        private void FriendsConsole_Load(object sender, EventArgs e)
        {
            InitializeFriendsList();
            listFriends.Select();
        }

        private void InitializeFriendsList()
        {
            if (!Monitor.TryEnter(lockOneAtaTime)) return;
            var friends = client.Friends.FriendList.Values.ToList();
            
            friends.Sort((fi1, fi2) =>
                {
                    switch (fi1.IsOnline)
                    {
                        case true when !fi2.IsOnline:
                            return -1;
                        case false when fi2.IsOnline:
                            return 1;
                        default:
                            return string.CompareOrdinal(fi1.Name, fi2.Name);
                    }
                }
            );

            listFriends.BeginUpdate();
            
            listFriends.Items.Clear();
            foreach (FriendInfo friend in friends)
            {
                listFriends.Items.Add(friend);
            }
            
            listFriends.EndUpdate();
            Monitor.Exit(lockOneAtaTime);
        }

        private void RefreshFriendsList()
        {
            InitializeFriendsList();
            SetControls();
        }

        private void Friends_FriendNames(object sender, FriendNamesEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Friends_FriendNames(sender, e)));
                return;
            }

            RefreshFriendsList();
        }

        private void Friends_FriendshipResponse(object sender, FriendshipResponseEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Friends_FriendshipResponse(sender, e)));
                return;
            }

            if (e.Accepted)
            {
                ThreadPool.QueueUserWorkItem(sync =>
                {
                    string name = instance.Names.GetAsync(e.AgentID).GetAwaiter().GetResult();
                    MethodInvoker display = () =>
                    {
                        DisplayNotification(e.AgentID, e.AgentName + " accepted your friendship offer");
                    };

                    if (InvokeRequired)
                    {
                        BeginInvoke(display);
                    }
                    else
                    {
                        display();
                    }
                });
            }

            RefreshFriendsList();
        }

        public static bool TryFindIMTab(RadegastInstanceForms instance, UUID friendID, out IMTabWindow console)
        {
            console = null;

            string tabID = (instance.Client.Self.AgentID ^ friendID).ToString();
            if (instance.TabConsole.TabExists(tabID))
            {
                console = (IMTabWindow)instance.TabConsole.Tabs[tabID].Control;
                return true;
            }
            return false;
        }

        private void DisplayNotification(UUID friendID, string msg)
        {
            IMTabWindow console;
            if (TryFindIMTab(instance, friendID, out console))
            {
                console.TextManager.DisplayNotification(msg);
            }
            instance.ShowNotificationInChat(msg, ChatBufferTextStyle.ObjectChat, instance.GlobalSettings["friends_notification_highlight"]);
        }

        private void Friends_FriendOffline(object sender, FriendInfoEventArgs e)
        {
            if (!instance.GlobalSettings["show_friends_online_notifications"]) return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                string name = instance.Names.GetAsync(e.Friend.UUID).GetAwaiter().GetResult();
                MethodInvoker display = () =>
                {
                    DisplayNotification(e.Friend.UUID, name + " is offline");
                    RefreshFriendsList();
                };

                if (InvokeRequired)
                {
                    BeginInvoke(display);
                }
                else
                {
                    display();
                }
            });
        }

        private void Friends_FriendOnline(object sender, FriendInfoEventArgs e)
        {
            if (!instance.GlobalSettings["show_friends_online_notifications"]) return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                string name = instance.Names.GetAsync(e.Friend.UUID).GetAwaiter().GetResult();
                MethodInvoker display = () =>
                {
                    DisplayNotification(e.Friend.UUID, name + " is online");
                    RefreshFriendsList();
                };

                if (InvokeRequired)
                {
                    BeginInvoke(display);
                }
                else
                {
                    display();
                }
            });
        }

        private void Friends_FriendshipTerminated(object sender, FriendshipTerminatedEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
            {
                string name = instance.Names.GetAsync(e.AgentID).GetAwaiter().GetResult();
                MethodInvoker display = () =>
                {
                    DisplayNotification(e.AgentID, name + " is no longer on your friend list");
                    RefreshFriendsList();
                };

                if (InvokeRequired)
                {
                    BeginInvoke(display);
                }
                else
                {
                    display();
                }
            });
        }

        private void SetControls()
        {
            if (listFriends.SelectedItems.Count == 0)
            {
                pnlActions.Enabled = pnlFriendsRights.Enabled = false;
            }
            else if (listFriends.SelectedItems.Count == 1)
            {
                pnlActions.Enabled = pnlFriendsRights.Enabled = true;
                btnProfile.Enabled = btnIM.Enabled = btnPay.Enabled = btnRemove.Enabled = true;

                FriendInfo friend = (FriendInfo)listFriends.SelectedItems[0];
                lblFriendName.Text = friend.Name + (friend.IsOnline ? " (online)" : " (offline)");

                settingFriend = true;
                chkSeeMeOnline.Checked = friend.CanSeeMeOnline;
                chkSeeMeOnMap.Checked = friend.CanSeeMeOnMap;
                chkModifyMyObjects.Checked = friend.CanModifyMyObjects;
                settingFriend = false;
            }
            else
            {
                btnIM.Enabled = pnlActions.Enabled = true;
                pnlFriendsRights.Enabled = false;
                btnProfile.Enabled = btnPay.Enabled = btnRemove.Enabled = false;
                lblFriendName.Text = "Multiple friends selected";
            }
        }

        private void btnIM_Click(object sender, EventArgs e)
        {
            if (listFriends.SelectedItems.Count == 1)
            {
                selectedFriend = (FriendInfo)listFriends.SelectedItems[0];
                instance.TabConsole.ShowIMTab(selectedFriend.UUID, selectedFriend.Name, true);
            }
            else if (listFriends.SelectedItems.Count > 1)
            {
                List<UUID> participants = new List<UUID>();
                foreach (var item in listFriends.SelectedItems)
                    participants.Add(((FriendInfo)item).UUID);
                UUID tmpID = UUID.Random();
                lblFriendName.Text = "Startings friends conference...";
                instance.ShowNotificationInChat(lblFriendName.Text, ChatBufferTextStyle.Invisible);
                btnIM.Enabled = false;

                ThreadPool.QueueUserWorkItem(sync =>
                    {
                        using (ManualResetEvent started = new ManualResetEvent(false))
                        {
                            UUID sessionID = UUID.Zero;
                            string sessionName = string.Empty;

                            EventHandler<GroupChatJoinedEventArgs> handler = (isender, ie) =>
                                {
                                    if (ie.TmpSessionID == tmpID)
                                    {
                                        sessionID = ie.SessionID;
                                        sessionName = ie.SessionName;
                                        started.Set();
                                    }
                                };

                            client.Self.GroupChatJoined += handler;
                            client.Self.StartIMConference(participants, tmpID);
                            if (started.WaitOne(30 * 1000, false))
                            {
                                instance.TabConsole.BeginInvoke(new MethodInvoker(() =>
                                    {
                                        instance.TabConsole.AddConferenceIMTab(sessionID, sessionName);
                                        instance.TabConsole.SelectTab(sessionID.ToString());
                                    }
                                ));
                            }
                            client.Self.GroupChatJoined -= handler;
                            BeginInvoke(new MethodInvoker(RefreshFriendsList));
                        }
                    }
                );
            }
        }

        private void btnProfile_Click(object sender, EventArgs e)
        {
            if (selectedFriend == null) return;

            instance.MainForm.ShowAgentProfile(selectedFriend.Name, selectedFriend.UUID);
        }

        private void chkSeeMeOnline_CheckedChanged(object sender, EventArgs e)
        {
            if (settingFriend) return;

            selectedFriend.CanSeeMeOnline = chkSeeMeOnline.Checked;
            client.Friends.GrantRights(selectedFriend.UUID, selectedFriend.TheirFriendRights);
        }

        private void chkSeeMeOnMap_CheckedChanged(object sender, EventArgs e)
        {
            if (settingFriend) return;

            selectedFriend.CanSeeMeOnMap = chkSeeMeOnMap.Checked;
            client.Friends.GrantRights(selectedFriend.UUID, selectedFriend.TheirFriendRights);
        }

        private void chkModifyMyObjects_CheckedChanged(object sender, EventArgs e)
        {
            if (settingFriend) return;

            selectedFriend.CanModifyMyObjects = chkModifyMyObjects.Checked;
            client.Friends.GrantRights(selectedFriend.UUID, selectedFriend.TheirFriendRights);
        }

        private void btnOfferTeleport_Click(object sender, EventArgs e)
        {
            foreach (var item in listFriends.SelectedItems)
            {
                FriendInfo friend = (FriendInfo)item;
                instance.MainForm.AddNotification(new ntfSendLureOffer(instance, friend.UUID));
            }
        }

        private void btnPay_Click(object sender, EventArgs e)
        {
            if (selectedFriend == null) return;

            (new frmPay(instance, selectedFriend.UUID, selectedFriend.Name, false)).ShowDialog();
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (selectedFriend == null) return;

            client.Friends.TerminateFriendship(selectedFriend.UUID);
            RefreshFriendsList();
        }

        public void ShowContextMenu()
        {
            RadegastContextMenuStrip menu = GetContextMenu();
            if (menu.HasSelection) menu.Show(listFriends, listFriends.PointToClient(MousePosition));
        }

        public RadegastContextMenuStrip GetContextMenu()
        {
            RadegastContextMenuStrip friendsContextMenuStrip = new RadegastContextMenuStrip();
            if (listFriends.SelectedItems.Count == 1)
            {
                FriendInfo item = (FriendInfo)listFriends.SelectedItems[0];
                instance.ContextActionManager.AddContributions(friendsContextMenuStrip, typeof(Avatar), item, btnPay.Parent);
                friendsContextMenuStrip.Selection = item.Name;
                friendsContextMenuStrip.HasSelection = true;
            }
            else if (listFriends.SelectedItems.Count > 1)
            {
                instance.ContextActionManager.AddContributions(friendsContextMenuStrip, typeof(ListView), listFriends, btnPay.Parent);
                friendsContextMenuStrip.Selection = "Multiple friends";
                friendsContextMenuStrip.HasSelection = true;
            }
            else
            {
                friendsContextMenuStrip.Selection = null;
                friendsContextMenuStrip.HasSelection = false;
            }
            return friendsContextMenuStrip;
        }

        private void listFriends_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            try
            {
                if (e.Index >= 0)
                {
                    var item = ((ListBox)sender).Items[e.Index];
                    if (item is FriendInfo)
                    {
                        var friend = (FriendInfo)((ListBox)sender).Items[e.Index];
                        string title = instance.Names.Get(friend.UUID);

                        using (var brush = new SolidBrush(e.ForeColor))
                        {
                            e.Graphics.DrawImageUnscaled(imageList1.Images[friend.IsOnline ? 1 : 0], e.Bounds.X, e.Bounds.Y);
                            e.Graphics.DrawString(title, e.Font, brush, e.Bounds.X + 20, e.Bounds.Y + 2);
                        }
                    }
                }
            }
            catch { }

            e.DrawFocusRectangle();
        }

        private void listFriends_SelectedIndexChanged(object sender, EventArgs e)
        {
            selectedFriend = (FriendInfo)listFriends.SelectedItem;
            SetControls();
        }

        private void listFriends_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Apps || (e.Control && e.KeyCode == RadegastContextMenuStrip.ContexMenuKeyCode))
            {
                ShowContextMenu();
            }
        }

        private void listFriends_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu();
            }
        }

        private void btnRequestTeleport_Click(object sender, EventArgs e)
        {
            if (selectedFriend == null) return;

            instance.MainForm.AddNotification(new ntfSendLureRequest(instance, selectedFriend.UUID));
        }

    }
}
