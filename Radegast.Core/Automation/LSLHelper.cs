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
using System.Threading;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Radegast.Automation
{
    public class LSLHelper : IDisposable
    {
        public bool Enabled;
        public HashSet<string> AllowedOwners;

        private readonly RadegastInstance instance;
        private GridClient client => instance.Client;

        public LSLHelper(RadegastInstance instance)
        {
            this.instance = instance;
            AllowedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
        }

        public void LoadSettings()
        {
            if (!client.Network.Connected) return;
            try
            {
                if (!(instance.ClientSettings["LSLHelper"] is OSDMap))
                    return;
                OSDMap map = (OSDMap)instance.ClientSettings["LSLHelper"];
                Enabled = map["enabled"];
                AllowedOwners.Clear();
                var allowedOwnerList = map["allowed_owner"].AsString();
                if (!string.IsNullOrWhiteSpace(allowedOwnerList))
                {
                    AllowedOwners.UnionWith(allowedOwnerList.Split(';'));
                }
            }
            catch { }
        }

        public void SaveSettings()
        {
            if (!client.Network.Connected) return;
            try
            {
                OSDMap map = new OSDMap(2)
                {
                    ["enabled"] = Enabled, 
                    ["allowed_owner"] = string.Join(";", AllowedOwners)
                };
                instance.ClientSettings["LSLHelper"] = map;
            }
            catch { }
        }

        /// <summary>
        /// Dispatcher for incoming IM automation
        /// </summary>
        /// <param name="e">Incoming message</param>
        /// <returns>If message processed correctly, should GUI processing be halted</returns>
        public bool ProcessIM(InstantMessageEventArgs e)
        {
            LoadSettings();

            if (!Enabled)
            {
                return false;
            }

            switch (e.IM.Dialog)
            {
                case InstantMessageDialog.MessageFromObject:
                    {
                        if (!AllowedOwners.Contains(e.IM.FromAgentID.ToString()))
                        {
                            return true;
                        }
                        string[] args = e.IM.Message.Trim().Split('^');
                        if (args.Length < 1) return false;

                        switch (args[0].Trim().ToLower())
                        {
                            case "group_invite":
                                {
                                    if (args.Length < 4) return false;
                                    ProcessInvite(args);
                                    return true;
                                }
                            case "send_im":
                                {
                                    if (args.Length < 3) return false;
                                    UUID sendTo = UUID.Zero;
                                    if (!UUID.TryParse(args[1].Trim(), out sendTo)) return false;
                                    string msg = args[2].Trim();
                                    client.Self.InstantMessage(sendTo, msg);
                                    return true;
                                }
                            case "give_inventory":
                                {
                                    if (args.Length < 3) return false;
                                    UUID sendTo = UUID.Zero;
                                    UUID invItemID = UUID.Zero;
                                    if (!UUID.TryParse(args[1].Trim(), out sendTo)) return false;
                                    if (!UUID.TryParse(args[2].Trim(), out invItemID)) return false;
                                    if (!client.Inventory.Store.Contains(invItemID))
                                    {
                                        instance.ShowNotificationInChat(
                                            $"Tried to offer {invItemID} but could not find it in my inventory",
                                            ChatBufferTextStyle.Error);
                                        return false;
                                    }

                                    if (!(client.Inventory.Store[invItemID] is InventoryItem item))
                                        return false;
                                    client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, sendTo, true);
                                    ThreadPool.QueueUserWorkItem(sync =>
                                        instance.ShowNotificationInChat(
                                            $"Gave {item.Name} to {instance.Names.GetAsync(sendTo).GetAwaiter().GetResult()}",
                                            ChatBufferTextStyle.ObjectChat)
                                    );
                                    return true;
                                }
                            case "say": /* This one doesn't work yet. I don't know why. TODO. - Nico */
                                {
                                    if (args.Length < 2) return true;
                                    ChatType ct = ChatType.Normal;
                                    int chan = 0;
                                    if (args.Length > 2 && int.TryParse(args[2].Trim(), out chan) && chan < 0)
                                    {
                                        chan = 0;
                                    }
                                    if (args.Length > 3)
                                    {
                                        switch (args[3].Trim().ToLower())
                                        {
                                            case "whisper":
                                                {
                                                    ct = ChatType.Whisper;
                                                    break;
                                                }
                                            case "shout":
                                                {
                                                    ct = ChatType.Shout;
                                                }
                                                break;
                                        }
                                    }
                                    client.Self.Chat(args[1].Trim(), chan, ct);
                                    return true;
                                }
                        }
                    }
                    break;
            }

            return false;
        }

        private void ProcessInvite(string[] args)
        {
            if (args == null || args.Length < 4)
                return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                try
                {
                    UUID invitee = UUID.Zero;
                    UUID groupID = UUID.Zero;
                    UUID roleID = UUID.Zero;
                    if (!UUID.TryParse(args[1].Trim(), out invitee)) return;
                    if (!UUID.TryParse(args[2].Trim(), out groupID)) return;
                    if (!UUID.TryParse(args[3].Trim(), out roleID)) return;

                    if (instance.Groups.ContainsKey(groupID))
                    {
                        AutoResetEvent gotMembers = new AutoResetEvent(false);
                        Dictionary<UUID, GroupMember> Members = null;
                        EventHandler<GroupMembersReplyEventArgs> handler = (sender, e) =>
                        {
                            if (e.GroupID != groupID)
                                return;
                            Members = e.Members;
                            gotMembers.Set();
                        };

                        client.Groups.GroupMembersReply += handler;
                        client.Groups.RequestGroupMembers(groupID);
                        gotMembers.WaitOne(30 * 1000, false);
                        client.Groups.GroupMembersReply -= handler;

                        if (Members != null && Members.ContainsKey(invitee))
                        {
                            instance.ShowNotificationInChat(
                                $"Not inviting {instance.Names.GetAsync(invitee).GetAwaiter().GetResult()} ({invitee}) to {instance.Groups[groupID].Name} ({groupID}), already member",
                                ChatBufferTextStyle.ObjectChat);
                        }
                        else
                        {
                            instance.ShowNotificationInChat(
                                $"Inviting {instance.Names.GetAsync(invitee).GetAwaiter().GetResult()} ({invitee}) to {instance.Groups[groupID].Name} ({groupID})",
                                ChatBufferTextStyle.ObjectChat);
                            client.Groups.Invite(groupID, new List<UUID>(1) { roleID }, invitee);
                        }
                    }
                    else
                    {
                        instance.ShowNotificationInChat(
                            $"Cannot invite to group {groupID}, I don't appear to be in it.",
                            ChatBufferTextStyle.Error);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed processing automation IM: " + ex.ToString(), Helpers.LogLevel.Warning);
                }
            });
        }
    }
}
