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

using LibreMetaverse;

namespace Radegast.Veles.Core;

public enum InventoryClipboardMode { None, Cut, Copy }

/// <summary>
/// Inventory clipboard for cut/copy/paste operations. Each logged-in account has its own,
/// since an inventory id means nothing in another account's inventory.
/// </summary>
public sealed class InventoryClipboard
{
    public InventoryClipboardMode Mode { get; private set; } = InventoryClipboardMode.None;
    public UUID ItemId  { get; private set; } = UUID.Zero;
    public string ItemName { get; private set; } = string.Empty;
    public bool IsFolder { get; private set; }

    public bool HasContent => Mode != InventoryClipboardMode.None && ItemId != UUID.Zero;

    public void Cut(UUID id, string name, bool isFolder)
    {
        Mode = InventoryClipboardMode.Cut;
        ItemId = id;
        ItemName = name;
        IsFolder = isFolder;
    }

    public void Copy(UUID id, string name, bool isFolder)
    {
        Mode = InventoryClipboardMode.Copy;
        ItemId = id;
        ItemName = name;
        IsFolder = isFolder;
    }

    public void Clear()
    {
        Mode = InventoryClipboardMode.None;
        ItemId = UUID.Zero;
        ItemName = string.Empty;
        IsFolder = false;
    }
}
