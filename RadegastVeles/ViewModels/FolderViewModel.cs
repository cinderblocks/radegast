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

using CommunityToolkit.Mvvm.ComponentModel;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public class FolderViewModel : ObservableObject
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private Inventory Inventory => Client.Inventory.Store!;

    public string FolderName { get; }
    public string FolderPath { get; }
    public int DirectItemCount { get; }
    public int DirectFolderCount { get; }
    public int TotalDescendantCount { get; }

    public FolderViewModel(RadegastInstanceAvalonia instance, InventoryFolder folder, InvTreeNode node)
    {
        _instance = instance;
        FolderName = folder.Name ?? "(unnamed)";
        FolderPath = BuildPath(node);

        var contents = Inventory.GetContents(folder.UUID);
        DirectFolderCount = 0;
        DirectItemCount = 0;
        foreach (var c in contents)
        {
            if (c is InventoryFolder) DirectFolderCount++;
            else DirectItemCount++;
        }

        TotalDescendantCount = CountDescendants(folder.UUID);
    }

    private int CountDescendants(UUID folderUuid)
    {
        var contents = Inventory.GetContents(folderUuid);
        int count = contents.Count;
        foreach (var c in contents)
        {
            if (c is InventoryFolder sub)
                count += CountDescendants(sub.UUID);
        }
        return count;
    }

    private static string BuildPath(InvTreeNode node)
    {
        var parts = new System.Collections.Generic.List<string>();
        var current = node.Parent;
        while (current != null)
        {
            parts.Insert(0, current.Name);
            current = current.Parent;
        }
        return parts.Count > 0 ? string.Join(" › ", parts) : "Root";
    }
}
