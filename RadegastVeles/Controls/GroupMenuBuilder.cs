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

using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.Controls;

public static class GroupMenuBuilder
{
    public static ContextMenu Build(RadegastInstanceAvalonia instance, UUID groupId, string groupName)
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem
        {
            Header = groupName,
            Command = new RelayCommand(() => instance.ShowGroupProfile(groupId))
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = "Activate",
            Command = new RelayCommand(() => instance.Client.Groups.ActivateGroup(groupId))
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Group IM",
            Command = new RelayCommand(() => instance.RequestGroupIM(groupId, groupName))
        });

        bool isMuted = instance.Client.Self.MuteList.Values
            .Any(m => m.Type == MuteType.Group && m.ID == groupId);
        if (isMuted)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Unmute Group",
                Command = new RelayCommand(() =>
                    instance.Client.Self.RemoveMuteListEntry(groupId, groupName))
            });
        }
        else
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Mute Group",
                Command = new RelayCommand(() =>
                    instance.Client.Self.UpdateMuteListEntry(MuteType.Group, groupId, groupName))
            });
        }

        menu.Items.Add(new MenuItem
        {
            Header = "Leave Group",
            Command = new RelayCommand(() => instance.Client.Groups.LeaveGroup(groupId))
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Invite Member\u2026",
            Command = new RelayCommand(() =>
                instance.ShowAvatarPicker($"Invite to {groupName}", entry =>
                {
                    instance.Client.Groups.Invite(groupId,
                        new System.Collections.Generic.List<OpenMetaverse.UUID> { OpenMetaverse.UUID.Zero },
                        entry.Id);
                    instance.ShowNotificationInChat($"Invited {entry.Name} to {groupName}.");
                }))
        });

        menu.Items.Add(new Separator());

        var slapp = SlurlParser.GetGroupUrl(groupId);
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
