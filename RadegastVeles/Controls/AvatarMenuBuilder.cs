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
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.Controls;

/// <summary>
/// Builds a centralized avatar context menu with all common avatar interaction actions.
/// </summary>
public static class AvatarMenuBuilder
{
    /// <summary>
    /// Builds a <see cref="ContextMenu"/> for interacting with an avatar by UUID and name.
    /// Always includes View Profile. When the agent is not the local user, also includes
    /// Send IM, Pay, Offer Teleport, Request Teleport, Add Friend, and Mute.
    /// </summary>
    public static ContextMenu Build(RadegastInstanceAvalonia instance, UUID agentId, string agentName, bool isNearby = true)
    {
        var menu = new ContextMenu();
        bool isSelf = agentId == instance.Client.Self.AgentID;

        menu.Items.Add(new MenuItem
        {
            Header = "View Profile",
            Command = new RelayCommand(() => instance.ShowAgentProfile(agentName, agentId))
        });
        menu.Items.Add(new MenuItem
        {
            Header = "View 3D",
            Command = new RelayCommand(() => instance.ShowAvatarViewer(agentId, agentName))
        });

        if (!isSelf && agentId != UUID.Zero)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Send IM",
                Command = new RelayCommand(() => instance.RequestIM(agentId, agentName))
            });
            menu.Items.Add(new MenuItem
            {
                Header = "Pay",
                Command = new RelayCommand(() => instance.OpenPayWindow(agentId, agentName))
            });
            menu.Items.Add(new MenuItem
            {
                Header = "Share Item\u2026",
                Command = new RelayCommand(() =>
                    instance.ShowInventoryPicker(
                        $"Share item with {agentName}",
                        null,
                        entry =>
                        {
                            instance.Client.Inventory.GiveItem(entry.ItemId, entry.Name, entry.AssetType, agentId, true);
                            instance.ShowNotificationInChat($"Offered '{entry.Name}' to {agentName}.");
                        },
                        item => (item.Permissions.OwnerMask & PermissionMask.Transfer) != 0))
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = "Turn To",
                IsEnabled = isNearby,
                Command = new RelayCommand(() =>
                {
                    var sim = instance.Client.Network.CurrentSim;
                    if (sim != null && instance.State.TryGetCoarsePosition(sim, agentId, out var pos))
                        instance.Client.Self.Movement.TurnToward(pos);
                })
            });
            menu.Items.Add(new MenuItem
            {
                Header = "Walk To",
                IsEnabled = isNearby,
                Command = new RelayCommand(() =>
                {
                    var sim = instance.Client.Network.CurrentSim;
                    if (sim != null && instance.State.TryGetCoarsePosition(sim, agentId, out var pos))
                        instance.State.MoveTo(sim, pos, false);
                })
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = "Offer Teleport",
                Command = new RelayCommand(() => instance.Client.Self.SendTeleportLure(agentId))
            });
            menu.Items.Add(new MenuItem
            {
                Header = "Request Teleport",
                Command = new RelayCommand(() => instance.Client.Self.InstantMessage(
                    agentName, agentId, "Please send me a teleport", agentId,
                    InstantMessageDialog.RequestLure, InstantMessageOnline.Online,
                    instance.Client.Self.SimPosition, UUID.Zero, Array.Empty<byte>()))
            });

            bool canSeeOnMap = instance.Client.Friends.FriendList.TryGetValue(agentId, out var friendInfo)
                               && friendInfo.CanSeeThemOnMap;
            menu.Items.Add(new MenuItem
            {
                Header = "Show on Map",
                IsEnabled = canSeeOnMap,
                Command = new RelayCommand(() => instance.ShowOnMap(agentId))
            });

            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = "Add Friend",
                Command = new RelayCommand(() => instance.Client.Friends.OfferFriendship(agentId))
            });
            menu.Items.Add(new MenuItem
            {
                Header = "Give Calling Card",
                Command = new RelayCommand(() => instance.Client.Friends.GiveCallingCard(agentId))
            });
            menu.Items.Add(new MenuItem
            {
                Header = "Invite to Group\u2026",
                Command = new RelayCommand(() =>
                    instance.ShowGroupPicker($"Invite {agentName} to Group", entry =>
                    {
                        // Invite to the Everyone role (UUID.Zero) by default
                        instance.Client.Groups.Invite(entry.GroupId, new System.Collections.Generic.List<UUID> { UUID.Zero }, agentId);
                        instance.ShowNotificationInChat($"Invited {agentName} to {entry.GroupName}.");
                    }))
            });

            bool isMuted = instance.Client.Self.MuteList.Values
                .Any(m => m.Type == MuteType.Resident && m.ID == agentId);
            if (isMuted)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "Unmute",
                    Command = new RelayCommand(() =>
                        instance.Client.Self.RemoveMuteListEntry(agentId, agentName))
                });
            }
            else
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "Mute",
                    Command = new RelayCommand(() =>
                        instance.Client.Self.UpdateMuteListEntry(MuteType.Resident, agentId, agentName))
                });
            }

            menu.Items.Add(new Separator());

            bool isFriend = instance.Client.Friends.FriendList.ContainsKey(agentId);
            if (isFriend)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "Remove Friend",
                    Command = new RelayCommand(() => instance.Client.Friends.TerminateFriendship(agentId))
                });
            }
        }

        menu.Items.Add(new Separator());
        var copyUuidItem = new MenuItem { Header = "Copy UUID" };
        copyUuidItem.Click += async (s, _) =>
        {
            var clip = TopLevel.GetTopLevel(s as MenuItem)?.Clipboard;
            if (clip != null) await clip.SetTextAsync(agentId.ToString());
        };
        menu.Items.Add(copyUuidItem);

        menu.Items.Add(new Separator());
        var slapp = SlurlParser.GetAgentUrl(agentId);
        var copySlappItem = new MenuItem { Header = "Copy SLAPP" };
        copySlappItem.Click += async (s, _) =>
        {
            var clip = TopLevel.GetTopLevel(s as MenuItem)?.Clipboard;
            if (clip != null) await clip.SetTextAsync(slapp);
        };
        menu.Items.Add(copySlappItem);

        return menu;
    }
}
