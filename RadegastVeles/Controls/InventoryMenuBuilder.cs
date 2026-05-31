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
using System.Threading.Tasks;
using Avalonia.Controls;
using OpenMetaverse;
using OpenMetaverse.Marketplace;
using Radegast.Veles.ViewModels;
using InvClipboard = Radegast.Veles.Core.InventoryClipboard;

namespace Radegast.Veles.Controls;

/// <summary>
/// Builds a dynamic context menu for inventory tree nodes.
/// </summary>
public static class InventoryMenuBuilder
{
    /// <param name="vm">The inventory view model.</param>
    /// <param name="node">The node that was right-clicked.</param>
    /// <param name="renameAction">Async delegate that shows the rename dialog.</param>
    /// <param name="marketplace">Optional marketplace view model for marketplace-aware actions.</param>
    public static ContextMenu Build(InventoryViewModel vm, InvTreeNode node, Func<Task> renameAction,
        MarketplaceViewModel? marketplace = null)
    {
        var menu = new ContextMenu();
        if (node.IsLibrary)
        {
            BuildLibraryMenu(menu, vm, node);
            TrimSep(menu);
            return menu;
        }
        if (node.IsFolder)
            BuildFolderMenu(menu, vm, node, renameAction, marketplace);
        else
            BuildItemMenu(menu, vm, node, renameAction);
        TrimSep(menu);
        return menu;
    }

    // ── Library menu (truly read-only) ─────────────────────────────────────────

    private static void BuildLibraryMenu(ContextMenu menu, InventoryViewModel vm, InvTreeNode node)
    {
        if (node.IsFolder) return; // No actions for library folders

        // Open for viewable types
        switch (node.TypeName)
        {
            case "Notecard":
            case "Script":
            case "Texture":
            case "Snapshot":
            case "Sound":
            case "Calling Card":
            case "Animation":
            case "Gesture":
            case "Landmark":
                Add(menu, "Open", () => vm.OpenItemCommand.Execute(null));
                break;
        }

        // Rez for objects — copy to world, no wear
        if (node.TypeName == "Object")
        {
            Add(menu, "Rez Inworld", () => vm.RezObjectCommand.Execute(null));
        }

        Sep(menu);
        Add(menu, "Copy to My Inventory", () => vm.CopyToInventoryCommand.Execute(null));
        Sep(menu);
        Add(menu, "Copy Asset UUID", () => vm.CopyAssetUUIDCommand.Execute(null));
    }

    // ── Folder menu ─────────────────────────────────────────────────────────

    private static void BuildFolderMenu(ContextMenu menu, InventoryViewModel vm, InvTreeNode node,
        Func<Task> renameAction, MarketplaceViewModel? marketplace = null)
    {
        var role = node.MarketplaceRole;

        // ── Marketplace listings root ─────────────────────────────────────────
        if (role == MarketplaceFolderRole.ListingsRoot)
        {
            if (marketplace != null)
                Add(menu, "Sync Listings", () => marketplace.SyncListingsCommand.Execute(null));
            TrimSep(menu);
            return;
        }

        // ── Marketplace listing folder ────────────────────────────────────────
        if (role == MarketplaceFolderRole.Listing)
        {
            Add(menu, "New Folder",   () => vm.CreateFolderCommand.Execute(null));
            Sep(menu);
            if (marketplace != null)
            {
                var record = marketplace.AllListings.FirstOrDefault(
                    r => r.ListingFolderUUID == node.ItemId);
                Add(menu, "Activate",   () => marketplace.ActivateListingCommand.Execute(record));
                Add(menu, "Deactivate", () => marketplace.DeactivateListingCommand.Execute(record));
                Sep(menu);
                Add(menu, "Register with SLM",  () => marketplace.CreateListingCommand.Execute(record));
                Add(menu, "Delete from SLM",    () => marketplace.DeleteListingCommand.Execute(record));
                Sep(menu);
                Add(menu, "Open on Marketplace", () => marketplace.OpenListingOnWebCommand.Execute(record));
            }
            TrimSep(menu);
            return;
        }

        // ── Marketplace version / stock / content — read-only structure ───────
        if (role == MarketplaceFolderRole.Version
            || role == MarketplaceFolderRole.Stock
            || role == MarketplaceFolderRole.Content)
        {
            Add(menu, "New Folder",   () => vm.CreateFolderCommand.Execute(null));
            Add(menu, "New Notecard", () => vm.CreateNotecardCommand.Execute(null));
            Add(menu, "New Script",   () => vm.CreateScriptCommand.Execute(null));
            Sep(menu);
            if (InvClipboard.HasContent)
                Add(menu, "Paste\tCtrl+V", () => vm.PasteItemCommand.Execute(null));
            TrimSep(menu);
            return;
        }

        // ── Standard (non-marketplace) folder ────────────────────────────────

        // My Outfits: offer to snapshot the current outfit as a new saved outfit
        if (node.FolderKind == FolderType.MyOutfits)
        {
            Add(menu, "Save Current Outfit...", () => vm.RequestSaveCurrentOutfitCommand.Execute(null));
            Sep(menu);
        }

        bool isProtected = vm.IsProtectedFolder(node);
        bool isLocked    = isProtected || vm.IsRootSystemFolder(node);
        bool noCreate    = node.FolderKind is FolderType.Trash
                                           or FolderType.CurrentOutfit
                                           or FolderType.Inbox;

        if (!noCreate)
        {
            Add(menu, "New Folder", () => vm.CreateFolderCommand.Execute(null));
            // Favorites is landmark-only; Notecard/Script creation is not appropriate there
            if (node.FolderKind != FolderType.Favorites)
            {
                Add(menu, "New Notecard", () => vm.CreateNotecardCommand.Execute(null));
                Add(menu, "New Script",   () => vm.CreateScriptCommand.Execute(null));
            }
            Sep(menu);
        }

        if (vm.IsTrashFolder(node))
        {
            Add(menu, "Empty Trash", () => vm.EmptyTrashCommand.Execute(null));
            Sep(menu);
        }
        else if (vm.IsInsideTrash(node))
        {
            Add(menu, "Restore from Trash", () => vm.RestoreFromTrashCommand.Execute(null));
            Sep(menu);
        }
        else if (!isProtected)
        {
            Add(menu, "Empty Folder", () => vm.EmptyFolderCommand.Execute(null));
            Sep(menu);
        }

        // Clipboard — protected and root system folders cannot be cut; folder Copy is not server-supported
        if (!isLocked)
            Add(menu, "Cut\tCtrl+X", () => vm.CutItemCommand.Execute(null));
        if (InvClipboard.HasContent)
            Add(menu, "Paste\tCtrl+V", () => vm.PasteItemCommand.Execute(null));
        if (InvClipboard.HasContent && !InvClipboard.IsFolder)
            Add(menu, "Paste as Link", () => vm.PasteLinkItemCommand.Execute(null));
        Sep(menu);

        if (!isProtected)
        {
            bool canWear    = vm.FolderCanBeWorn(node);
            bool canTakeOff = vm.FolderCanBeTakenOff(node);
            if (canWear)
            {
                Add(menu, "Add to Outfit",  () => vm.WearFolderCommand.Execute(null));
                Add(menu, "Replace Outfit", () => vm.ReplaceOutfitCommand.Execute(null));
            }
            if (canTakeOff)
                Add(menu, "Take Off All",   () => vm.RemoveFolderFromOutfitCommand.Execute(null));
            if (canWear || canTakeOff)
                Sep(menu);
        }

        if (!vm.IsSystemFolder(node))
            Add(menu, "Rename\tF2", () => _ = renameAction());

        if (!isLocked)
            Add(menu, "Delete", () => vm.DeleteItemCommand.Execute(null));
        Sep(menu);

        Add(menu, "Properties...\tCtrl+P", () => vm.ShowPropertiesCommand.Execute(null));
    }

    // ── Item menu

    private static void BuildItemMenu(ContextMenu menu, InventoryViewModel vm, InvTreeNode node, Func<Task> renameAction)
    {
        bool addedPrimary = false;

        switch (node.TypeName)
        {
            case "Notecard":
            case "Script":
            case "Texture":
            case "Snapshot":
            case "Sound":
            case "Calling Card":
                Add(menu, "Open", () => vm.OpenItemCommand.Execute(null));
                addedPrimary = true;
                break;

            case "Landmark":
                Add(menu, "Open",           () => vm.OpenItemCommand.Execute(null));
                Add(menu, "Teleport Here",  () => vm.TeleportToLandmarkCommand.Execute(null));
                addedPrimary = true;
                break;

            case "Gesture":
                Add(menu, "Open",           () => vm.OpenItemCommand.Execute(null));
                Add(menu, "Play",           () => vm.PlayGestureCommand.Execute(null));
                Add(menu, node.IsWorn ? "Deactivate" : "Activate",
                                            () => vm.ToggleGestureCommand.Execute(null));
                addedPrimary = true;
                break;

            case "Animation":
                Add(menu, "Open",           () => vm.OpenItemCommand.Execute(null));
                if (node.IsWorn)
                    Add(menu, "Stop",       () => vm.RemoveFromOutfitCommand.Execute(null));
                else
                    Add(menu, "Play",       () => vm.WearItemCommand.Execute(null));
                addedPrimary = true;
                break;
        }

        // Wear / Take Off for wearables, objects, animations
        if (IsWearable(node.TypeName))
        {
            if (addedPrimary) Sep(menu);
            // "Open" viewer for clothing/body parts (not Object/Animation which have their own handling above)
            if (node.TypeName != "Object" && node.TypeName != "Animation")
                Add(menu, "Open", () => vm.OpenItemCommand.Execute(null));
            if (node.IsWorn)
                Add(menu, "Take Off", () => vm.RemoveFromOutfitCommand.Execute(null));
            else
            {
                Add(menu, "Wear",         () => vm.WearItemCommand.Execute(null));
                Add(menu, "Add to Outfit",() => vm.WearItemCommand.Execute(null));
            }
            addedPrimary = true;
        }

        if (addedPrimary) Sep(menu);

        // Clipboard
        Add(menu, "Cut\tCtrl+X",   () => vm.CutItemCommand.Execute(null));
        if (vm.CanCopyItem(node))
            Add(menu, "Copy\tCtrl+C",  () => vm.CopyItemCommand.Execute(null));
        if (InvClipboard.HasContent)
            Add(menu, "Paste\tCtrl+V", () => vm.PasteItemCommand.Execute(null));
        if (InvClipboard.HasContent && !InvClipboard.IsFolder)
            Add(menu, "Paste as Link", () => vm.PasteLinkItemCommand.Execute(null));
        Sep(menu);

        Add(menu, "Copy Asset UUID", () => vm.CopyAssetUUIDCommand.Execute(null));
        Sep(menu);

        Add(menu, "Rename\tF2", () => _ = renameAction());
        Add(menu, "Delete",     () => vm.DeleteItemCommand.Execute(null));

        if (vm.IsInsideTrash(node))
        {
            Sep(menu);
            Add(menu, "Restore from Trash", () => vm.RestoreFromTrashCommand.Execute(null));
        }

        // Object-specific
        if (node.TypeName == "Object")
        {
            Sep(menu);
            Add(menu, "Rez Inworld", () => vm.RezObjectCommand.Execute(null));
            Sep(menu);
            var wearOnMenu = new MenuItem { Header = "Wear On..." };
            BuildWearOnSubmenu(wearOnMenu, vm);
            menu.Items.Add(wearOnMenu);
        }

        // Create Link (only for non-links)
        if (!node.IsLink)
        {
            Sep(menu);
            Add(menu, "Create Link\tCtrl+L", () => vm.CreateLinkCommand.Execute(null));
        }

        Sep(menu);
        Add(menu, "Properties...\tCtrl+P", () => vm.ShowPropertiesCommand.Execute(null));
    }

    // ── Helpers

    private static void BuildWearOnSubmenu(MenuItem parent, InventoryViewModel vm)
    {
        var bodyMenu = new MenuItem { Header = "Body" };
        var hudMenu  = new MenuItem { Header = "HUD" };

        foreach (AttachmentPoint pt in Enum.GetValues(typeof(AttachmentPoint)))
        {
            if (pt == AttachmentPoint.Default) continue;
            var label = pt.ToString();
            var captured = pt;
            if (label.StartsWith("HUD", StringComparison.OrdinalIgnoreCase))
            {
                var item = new MenuItem { Header = label[3..] }; // strip "HUD" prefix
                item.Click += (_, _) => _ = vm.WearAtPointCommand.ExecuteAsync(captured);
                hudMenu.Items.Add(item);
            }
            else
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => _ = vm.WearAtPointCommand.ExecuteAsync(captured);
                bodyMenu.Items.Add(item);
            }
        }

        parent.Items.Add(bodyMenu);
        parent.Items.Add(hudMenu);
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static void Sep(ContextMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
            menu.Items.Add(new Separator());
    }

    private static void TrimSep(ContextMenu menu)
    {
        while (menu.Items.Count > 0 && menu.Items[^1] is Separator)
            menu.Items.RemoveAt(menu.Items.Count - 1);
    }

    private static bool IsWearable(string typeName) => typeName is
        "Shape" or "Skin" or "Hair" or "Eyes" or "Shirt" or "Pants" or
        "Shoes" or "Socks" or "Jacket" or "Gloves" or "Undershirt" or
        "Underpants" or "Skirt" or "Alpha" or "Tattoo" or "Physics" or
        "Universal" or "Object" or "Animation";
}
