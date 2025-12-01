/*
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

using OpenMetaverse;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    /// <summary>
    /// Radegast-specific extension of LibreMetaverse's CurrentOutfitFolder that adds support for
    /// IRadegastInstance and handles client changes.
    /// </summary>
    public class OutfitManager : OpenMetaverse.Appearance.CurrentOutfitFolder
    {
        private readonly IRadegastInstance instance;

        public OutfitManager(IRadegastInstance instance)
            : base(instance.Client)
        {
            this.instance = instance;
            this.instance.ClientChanged += instance_ClientChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                instance.ClientChanged -= instance_ClientChanged;
            }
            base.Dispose(disposing);
        }

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            client = e.Client;
        }

        /// <summary>
        /// Replaces the current outfit and updates COF links accordingly
        /// </summary>
        /// <param name="newOutfitFolderId">List of new wearables and attachments that comprise the new outfit</param>
        /// <param name="cancellationToken"></param>
        /// <returns>True on success</returns>
        public async Task<bool> ReplaceOutfit(UUID newOutfitFolderId, CancellationToken cancellationToken = default)
        {
            const string generalErrorMessage = "Try refreshing your inventory or clearing your cache.";

            var trashFolderId = client.Inventory.FindFolderForType(FolderType.Trash);
            var rootFolderId = client.Inventory.Store.RootFolder.UUID;

            var newOutfit = await client.Inventory.RequestFolderContents(
                newOutfitFolderId,
                client.Self.AgentID,
                true,
                true,
                InventorySortOrder.ByName,
                cancellationToken
            );
            if (newOutfit == null)
            {
                Logger.Warn($"Failed to request contents of replacement outfit folder. {generalErrorMessage}", client);
                return false;
            }

            if (!client.Inventory.Store.TryGetNodeFor(newOutfitFolderId, out var newOutfitFolderNode))
            {
                Logger.Warn($"Failed to get node for replacement outfit folder. {generalErrorMessage}", client);
                return false;
            }

            var isOutfitInTrash = await IsObjectDescendentOf(newOutfitFolderNode.Data, trashFolderId, cancellationToken);
            if (isOutfitInTrash)
            {
                Logger.Warn($"Cannot wear an outfit that is currently in the trash.", client);
                return false;
            }

            var isOutfitInInventory = await IsObjectDescendentOf(newOutfitFolderNode.Data, rootFolderId, cancellationToken);
            if (!isOutfitInInventory)
            {
                Logger.Warn($"Cannot wear an outfit that is not currently in your inventory.", client);
                return false;
            }

            var currentOutfitFolder = await client.Appearance.GetCurrentOutfitFolder(cancellationToken);
            if (currentOutfitFolder == null)
            {
                Logger.Warn($"Failed to find current outfit folder. {generalErrorMessage}", client);
                return false;
            }

            var currentOutfitContents = await client.Inventory.RequestFolderContents(
                currentOutfitFolder.UUID,
                currentOutfitFolder.OwnerID,
                true,
                true,
                InventorySortOrder.ByName,
                cancellationToken
            );
            if (currentOutfitContents == null)
            {
                Logger.Warn($"Failed to request contents of current outfit folder. {generalErrorMessage}", client);
                return false;
            }

            var newOutfitItemMap = new Dictionary<UUID, InventoryItem>();
            var existingBodypartLinks = new List<InventoryItem>();
            var bodypartsToWear = new Dictionary<WearableType, InventoryWearable>();
            var gesturesToActivate = new Dictionary<UUID, InventoryItem>();
            var numClothingLayers = 0;
            var numAttachedObjects = 0;

            var itemsBeingAdded = new Dictionary<UUID, InventoryItem>();
            var itemsBeingRemoved = new Dictionary<UUID, InventoryItem>();

            foreach (var item in newOutfit)
            {
                if (!(item is InventoryItem inventoryItem))
                {
                    continue;
                }

                if (inventoryItem.IsLink())
                {
                    continue;
                }

                var isInTrash = await IsObjectDescendentOf(inventoryItem, trashFolderId, cancellationToken);
                if (isInTrash)
                {
                    continue;
                }

                var isInInventory = await IsObjectDescendentOf(inventoryItem, rootFolderId, cancellationToken);
                if (!isInInventory)
                {
                    continue;
                }

                if (inventoryItem.AssetType == AssetType.Bodypart)
                {
                    if (!(item is InventoryWearable bodypartItem))
                    {
                        continue;
                    }

                    if (bodypartsToWear.ContainsKey(bodypartItem.WearableType))
                    {
                        continue;
                    }

                    bodypartsToWear[bodypartItem.WearableType] = bodypartItem;
                    continue;
                }
                else if (inventoryItem.AssetType == AssetType.Gesture)
                {
                    gesturesToActivate[inventoryItem.UUID] = inventoryItem;
                }
                else if (inventoryItem.AssetType == AssetType.Clothing)
                {
                    if (numClothingLayers >= MaxClothingLayers)
                    {
                        continue;
                    }

                    numClothingLayers++;
                }
                else if (inventoryItem.AssetType == AssetType.Object)
                {
                    if (numAttachedObjects >= client.Self.Benefits.AttachmentLimit)
                    {
                        continue;
                    }

                    ++numAttachedObjects;
                }

                itemsBeingAdded[inventoryItem.UUID] = inventoryItem;
                newOutfitItemMap[inventoryItem.UUID] = inventoryItem;
            }

            var existingLinkTargets = currentOutfitContents
                .OfType<InventoryItem>()
                .Where(n => !n.IsLink())
                .ToDictionary(k => k.UUID, v => v);
            var linksToRemove = new List<InventoryItem>();
            var gesturesToDeactivate = new HashSet<UUID>();

            foreach (var item in currentOutfitContents)
            {
                if (!(item is InventoryItem itemLink))
                {
                    continue;
                }

                if (!itemLink.IsLink())
                {
                    continue;
                }

                if (!existingLinkTargets.TryGetValue(itemLink.AssetUUID, out var realItem))
                {
                    linksToRemove.Add(itemLink);
                    continue;
                }

                if (newOutfitItemMap.ContainsKey(realItem.UUID))
                {
                    itemsBeingAdded.Remove(realItem.UUID);
                    continue;
                }

                if (realItem.AssetType == AssetType.Bodypart)
                {
                    existingBodypartLinks.Add(itemLink);
                    continue;
                }

                if (realItem.AssetType == AssetType.Gesture)
                {
                    if (!gesturesToActivate.ContainsKey(realItem.UUID))
                    {
                        gesturesToDeactivate.Add(realItem.UUID);
                    }
                }

                itemsBeingRemoved[realItem.UUID] = realItem;
                linksToRemove.Add(itemLink);
            }

            foreach (var gestureId in gesturesToDeactivate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                client.Self.DeactivateGesture(gestureId);
            }
            foreach (var item in gesturesToActivate.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                client.Self.ActivateGesture(item.UUID, item.AssetUUID);
            }

            foreach (var existingLink in existingBodypartLinks)
            {
                if (existingLinkTargets.TryGetValue(existingLink.AssetUUID, out var realItem))
                {
                    if (realItem is InventoryWearable existingBodypart)
                    {
                        if (!bodypartsToWear.ContainsKey(existingBodypart.WearableType))
                        {
                            bodypartsToWear[existingBodypart.WearableType] = existingBodypart;
                            continue;
                        }
                    }

                    itemsBeingRemoved[realItem.UUID] = realItem;
                }

                linksToRemove.Add(existingLink);
            }

            // Bare minimum outfit check
            if (!bodypartsToWear.ContainsKey(WearableType.Shape) ||
                !bodypartsToWear.ContainsKey(WearableType.Skin) ||
                !bodypartsToWear.ContainsKey(WearableType.Eyes) ||
                !bodypartsToWear.ContainsKey(WearableType.Hair))
            {
                Logger.Error("New outfit must contain a Shape, Skin, Eyes, and Hair", client);
                return false;
            }

            // Clear out all existing current outfit links
            var toRemoveIds = linksToRemove
                .Select(n => n.UUID)
                .Distinct();
            await client.Inventory.RemoveItemsAsync(toRemoveIds, cancellationToken);

            // Add body parts from current outfit to new outfit if it's lacking those essential body parts
            foreach (var item in bodypartsToWear)
            {
                itemsBeingAdded.Add(item.Value.UUID, item.Value);
            }
            foreach (var item in itemsBeingAdded)
            {
                await AddLink(item.Value, cancellationToken);
            }

            // Add link to outfit folder we're putting on
            client.Inventory.CreateLink(
                currentOutfitFolder.UUID,
                newOutfitFolderNode.Data.UUID,
                newOutfitFolderNode.Data.Name,
                "",
                InventoryType.Folder,
                UUID.Random(),
                (success, newItem) =>
                {
                    if (success)
                    {
                        client.Inventory.RequestFetchInventory(newItem.UUID, newItem.OwnerID);
                    }
                },
                cancellationToken
            );

            var tcs = new TaskCompletionSource<bool>();
            void handleAppearanceSet(object sender, AppearanceSetEventArgs e)
            {
                tcs.TrySetResult(true);
            }

            try
            {
                client.Appearance.AppearanceSet += handleAppearanceSet;
                client.Appearance.ReplaceOutfit(newOutfitItemMap.Values.ToList(), false);

                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000, cancellationToken));
                if (completedTask != tcs.Task)
                {
                    Logger.Error("Timed out while waiting for AppearanceSet confirmation. Are you changing outfits too quickly?", client);
                    return false;
                }
            }
            finally
            {
                client.Appearance.AppearanceSet -= handleAppearanceSet;
            }

            return true;
        }

        /// <summary>
        /// Add items to current outfit
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="replace">Should existing wearable of the same type be removed</param>
        /// <param name="cancellationToken"></param>
        public async Task AddToOutfit(InventoryItem item, bool replace, CancellationToken cancellationToken = default)
        {
            await AddToOutfit(new List<InventoryItem>(1) { item }, replace, cancellationToken);
        }

        /// <summary>
        /// Add items to current outfit
        /// </summary>
        /// <param name="requestedItemsToAdd">List of items to add</param>
        /// <param name="replace">Should existing wearable of the same type be removed</param>
        /// <param name="cancellationToken"></param>
        public async Task AddToOutfit(List<InventoryItem> requestedItemsToAdd, bool replace, CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                Logger.Warn("Can't add to outfit link; COF hasn't been initialized.", client);
                return;
            }

            var trashFolderId = client.Inventory.FindFolderForType(FolderType.Trash);
            var rootFolderId = client.Inventory.Store.RootFolder.UUID;

            var cofLinks = await GetCurrentOutfitLinks(cancellationToken);
            var cofRealItems = new Dictionary<UUID, InventoryBase>();
            var cofLinkAssetIds = new HashSet<UUID>();
            var currentBodyparts = new Dictionary<WearableType, InventoryWearable>();
            var currentClothing = new Dictionary<WearableType, List<InventoryWearable>>();
            var currentAttachmentPoints = new Dictionary<AttachmentPoint, List<InventoryObject>>();
            var numClothingLayers = 0;
            var numAttachedObjects = 0;

            foreach (var item in cofLinks)
            {
                var realItem = ResolveInventoryLink(item) ?? item;
                if (realItem == null)
                {
                    continue;
                }

                cofRealItems[realItem.UUID] = realItem;
                cofLinkAssetIds.Add(item.AssetUUID);

                if (realItem is InventoryWearable wearable)
                {
                    if (realItem.AssetType == AssetType.Bodypart)
                    {
                        currentBodyparts[wearable.WearableType] = wearable;
                    }
                    else if (realItem.AssetType == AssetType.Clothing)
                    {
                        if (!currentClothing.TryGetValue(wearable.WearableType, out var currentWearablesOfType))
                        {
                            currentWearablesOfType = new List<InventoryWearable>();
                            currentClothing[wearable.WearableType] = currentWearablesOfType;
                            numClothingLayers++;
                        }

                        currentWearablesOfType.Add(wearable);
                    }
                }
                else if (realItem is InventoryObject inventoryObject)
                {
                    if (!currentAttachmentPoints.TryGetValue(inventoryObject.AttachPoint, out var attachedObjects))
                    {
                        attachedObjects = new List<InventoryObject>();
                        currentAttachmentPoints[inventoryObject.AttachPoint] = attachedObjects;
                    }

                    attachedObjects.Add(inventoryObject);
                    numAttachedObjects++;
                }
            }

            var itemsToRemove = new List<InventoryItem>();
            var itemsToAdd = new List<InventoryItem>();

            foreach (var item in requestedItemsToAdd)
            {
                var realItem = ResolveInventoryLink(item);
                if (realItem == null)
                {
                    continue;
                }

                var isItemInTrash = await IsObjectDescendentOf(realItem, trashFolderId, cancellationToken);
                if (isItemInTrash)
                {
                    continue;
                }

                var isItemInInventory = await IsObjectDescendentOf(realItem, rootFolderId, cancellationToken);
                if (!isItemInInventory)
                {
                    continue;
                }

                if (cofLinkAssetIds.Contains(realItem.UUID))
                {
                    continue;
                }
                if (itemsToAdd.FirstOrDefault(n => n.UUID == realItem.UUID) != null)
                {
                    continue;
                }

                if (realItem is InventoryWearable wearable)
                {
                    if (wearable.AssetType == AssetType.Clothing)
                    {
                        if (replace)
                        {
                            if (currentClothing.TryGetValue(wearable.WearableType, out var currentClothingOfType))
                            {
                                foreach (var clothingToRemove in currentClothingOfType)
                                {
                                    itemsToRemove.Add(clothingToRemove);
                                }
                            }
                        }
                        else
                        {
                            if (numClothingLayers >= MaxClothingLayers)
                            {
                                continue;
                            }

                            numClothingLayers++;
                        }
                    }
                    else if (wearable.AssetType == AssetType.Bodypart)
                    {
                        if (currentBodyparts.TryGetValue(wearable.WearableType, out var existingBodyPart))
                        {
                            itemsToRemove.Add(existingBodyPart);
                        }
                    }
                }
                else if (realItem.AssetType == AssetType.Gesture)
                {
                    client.Self.ActivateGesture(realItem.UUID, realItem.AssetUUID);
                }
                else if (realItem is InventoryObject objectToAdd)
                {
                    if (replace)
                    {
                        if (currentAttachmentPoints.TryGetValue(objectToAdd.AttachPoint, out var attachedObjectsToRemove))
                        {
                            foreach (var attachedObject in attachedObjectsToRemove)
                            {
                                itemsToRemove.Add(attachedObject);
                                --numAttachedObjects;
                            }
                        }
                    }

                    if (numAttachedObjects >= client.Self.Benefits.AttachmentLimit)
                    {
                        continue;
                    }

                    ++numAttachedObjects;
                }
                else
                {
                    continue;
                }

                itemsToAdd.Add(realItem);
            }

            if (itemsToRemove.Count > 0)
            {
                await RemoveLinksTo(itemsToRemove, cancellationToken);
            }

            foreach (var item in itemsToAdd)
            {
                await AddLink(item, cancellationToken);
            }

            client.Appearance.AddToOutfit(itemsToAdd, replace);
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Thread.Sleep(2000);
                client.Appearance.RequestSetAppearance(true);
            });
        }

        /// <summary>
        /// Removes specified item from the current outfit. All COF links to this item will be removed from the COF.
        /// The specified item may either be an actual item, or a link to an actual item. Links will be resolved to the
        /// actual item internally.
        /// </summary>
        /// <param name="item">Item (or item link) we want to remove all links to from our COF</param>
        /// <param name="cancellationToken"></param>
        public async Task RemoveFromOutfit(InventoryItem item, CancellationToken cancellationToken = default)
        {
            await RemoveFromOutfit(new List<InventoryItem>(1) { item }, cancellationToken);
        }

        /// <summary>
        /// Removes specified items from the current outfit. All COF links to these items will be removed from the COF.
        /// The specified items may either be actual items, or links to actual items. Links will be resolved to actual
        /// items internally.
        /// </summary>
        /// <param name="requestedItemsToRemove">List of items (or item links) we want to remove all links to from our COF</param>
        /// <param name="cancellationToken"></param>
        public async Task RemoveFromOutfit(List<InventoryItem> requestedItemsToRemove, CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                Logger.Warn("Can't remove from outfit; COF hasn't been initialized.", client);
                return;
            }

            var itemsToRemove = requestedItemsToRemove
                .Select(n => ResolveInventoryLink(n))
                .Where(n => n != null)
                .Distinct()
                .ToList();
            
            foreach (var item in itemsToRemove)
            {
                if (item.AssetType == AssetType.Gesture)
                {
                    client.Self.DeactivateGesture(item.UUID);
                }
            }

            await RemoveLinksTo(itemsToRemove, cancellationToken);

            client.Appearance.RemoveFromOutfit(itemsToRemove);
        }
    }
}
