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
/// Application-wide inventory clipboard for cut/copy/paste operations.
/// </summary>
public static class InventoryClipboard
{
    public static InventoryClipboardMode Mode { get; private set; } = InventoryClipboardMode.None;
    public static UUID ItemId  { get; private set; } = UUID.Zero;
    public static string ItemName { get; private set; } = string.Empty;
    public static bool IsFolder { get; private set; }

    public static bool HasContent => Mode != InventoryClipboardMode.None && ItemId != UUID.Zero;

    public static void Cut(UUID id, string name, bool isFolder)
    {
        Mode = InventoryClipboardMode.Cut;
        ItemId = id;
        ItemName = name;
        IsFolder = isFolder;
    }

    public static void Copy(UUID id, string name, bool isFolder)
    {
        Mode = InventoryClipboardMode.Copy;
        ItemId = id;
        ItemName = name;
        IsFolder = isFolder;
    }

    public static void Clear()
    {
        Mode = InventoryClipboardMode.None;
        ItemId = UUID.Zero;
        ItemName = string.Empty;
        IsFolder = false;
    }
}
