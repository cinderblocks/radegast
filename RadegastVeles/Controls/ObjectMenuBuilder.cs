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
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.Controls;

/// <summary>
/// Builds a <see cref="ContextMenu"/> for interacting with an in-world object by UUID and name.
/// </summary>
public static class ObjectMenuBuilder
{
    /// <summary>
    /// Builds a context menu for an object that chatted into nearby chat.
    /// Includes Mute Object (by UUID) and Mute by Name.  Shows Unmute when the object
    /// or name is already on the mute list.
    /// </summary>
    public static ContextMenu Build(RadegastInstanceAvalonia instance, UUID objectId, string objectName)
    {
        var menu = new ContextMenu();
        var muteList = instance.Client.Self.MuteList;

        bool mutedById = muteList.Values.Any(m => m.Type == MuteType.Object && m.ID == objectId);
        bool mutedByName = muteList.Values.Any(m => m.Type == MuteType.ByName && m.Name == objectName);

        if (mutedById)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Unmute Object",
                Command = new RelayCommand(() =>
                    instance.Client.Self.RemoveMuteListEntry(objectId, objectName))
            });
        }
        else
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Mute Object",
                Command = new RelayCommand(() =>
                    instance.Client.Self.UpdateMuteListEntry(MuteType.Object, objectId, objectName))
            });
        }

        if (mutedByName)
        {
            menu.Items.Add(new MenuItem
            {
                Header = $"Unmute \"{objectName}\" by Name",
                Command = new RelayCommand(() =>
                    instance.Client.Self.RemoveMuteListEntry(UUID.Zero, objectName))
            });
        }
        else
        {
            menu.Items.Add(new MenuItem
            {
                Header = $"Mute \"{objectName}\" by Name",
                Command = new RelayCommand(() =>
                    instance.Client.Self.UpdateMuteListEntry(MuteType.ByName, UUID.Zero, objectName))
            });
        }

        menu.Items.Add(new Separator());
        var slapp = $"secondlife:///app/objectim/{objectId}?name={Uri.EscapeDataString(objectName)}";
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
