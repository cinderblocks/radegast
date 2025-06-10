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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    public class CurrentOutfitFolder : IDisposable
    {
        #region Fields

        private GridClient Client;
        private readonly RadegastInstance Instance;
        private bool InitializedCOF = false;
        public InventoryFolder COF;

        public int MaxClothingLayers => 60;

        #endregion Fields

        #region Construction and disposal
        public CurrentOutfitFolder(RadegastInstance instance)
        {
            Instance = instance;
            Client = instance.Client;
            Instance.ClientChanged += instance_ClientChanged;
            RegisterClientEvents(Client);
        }

        public void Dispose()
        {
            UnregisterClientEvents(Client);
            Instance.ClientChanged -= instance_ClientChanged;
        }
        #endregion Construction and disposal

        #region Event handling

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(Client);
            Client = e.Client;
            RegisterClientEvents(Client);
        }

        private void RegisterClientEvents(GridClient client)
        {
            client.Network.SimChanged += Network_OnSimChanged;
            client.Inventory.FolderUpdated += Inventory_FolderUpdated;
            client.Objects.KillObject += Objects_KillObject;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Network.SimChanged -= Network_OnSimChanged;
            client.Inventory.FolderUpdated -= Inventory_FolderUpdated;
            client.Objects.KillObject -= Objects_KillObject;

            InitializedCOF = false;
        }

        private readonly object FolderSync = new object();

        private void Inventory_FolderUpdated(object sender, FolderUpdatedEventArgs e)
        {
            if (COF == null) { return; }

            if (e.FolderID == COF.UUID && e.Success)
            {
                COF = (InventoryFolder)Client.Inventory.Store[COF.UUID];
                lock (FolderSync)
                {
                    var items = new Dictionary<UUID, UUID>();
                    var cofLinks = ContentLinks().Result;

                    foreach (var link in cofLinks.Where(link => !items.ContainsKey(link.AssetUUID)))
                    {
                        items.Add(link.AssetUUID, Client.Self.AgentID);
                    }

                    if (items.Count > 0)
                    {
                        Client.Inventory.RequestFetchInventory(items);
                    }
                }
            }
        }

        private void Objects_KillObject(object sender, KillObjectEventArgs e)
        {
            if (Client.Network.CurrentSim != e.Simulator) { return; }

            Primitive prim = null;
            if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(e.ObjectLocalID, out prim))
            {
                UUID invItem = GetAttachmentItem(prim);
                if (invItem != UUID.Zero)
                {
                    RemoveLink(invItem).Wait();
                }
            }
        }

        private void Network_OnSimChanged(object sender, SimChangedEventArgs e)
        {
            Client.Network.CurrentSim.Caps.CapabilitiesReceived += Simulator_OnCapabilitiesReceived;
        }

        private void Simulator_OnCapabilitiesReceived(object sender, CapabilitiesReceivedEventArgs e)
        {
            e.Simulator.Caps.CapabilitiesReceived -= Simulator_OnCapabilitiesReceived;

            if (e.Simulator == Client.Network.CurrentSim && !InitializedCOF)
            {
                InitializeCurrentOutfitFolder().Wait();
            }
        }

        #endregion Event handling

        #region Private methods

        private async Task<bool> InitializeCurrentOutfitFolder(CancellationToken cancellationToken = default)
        {
            COF = await Client.Appearance.GetCurrentOutfitFolder(cancellationToken);

            if (COF == null)
            {
                //CreateCurrentOutfitFolder();
            }
            else
            {
                await Client.Inventory.RequestFolderContents(COF.UUID, Client.Self.AgentID,
                    true, true, InventorySortOrder.ByDate, cancellationToken);
            }

            Logger.Log($"Initialized Current Outfit Folder with UUID {COF.UUID} v.{COF.Version}", Helpers.LogLevel.Info, Client);

            InitializedCOF = COF != null;
            return InitializedCOF;
        }

        private void CreateCurrentOutfitFolder()
        {
            UUID cofId = Client.Inventory.CreateFolder(Client.Inventory.Store.RootFolder.UUID, 
                "Current Outfit", FolderType.CurrentOutfit);
            if (Client.Inventory.Store.Contains(cofId) && Client.Inventory.Store[cofId] is InventoryFolder folder)
            {
                COF = folder;
            }
        }

        #endregion Private methods

        #region Public methods
        /// <summary>
        /// Return links found in Current Outfit Folder
        /// </summary>
        /// <returns>List of <see cref="InventoryItem"/> that can be part of appearance (attachments, wearables)</returns>
        private async Task<List<InventoryItem>> ContentLinks(CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                await InitializeCurrentOutfitFolder(cancellationToken);
            }

            if(COF == null)
            {
                Logger.Log($"COF is null", Helpers.LogLevel.Warning, Client);
                return new List<InventoryItem>();
            }

            if(!Client.Inventory.Store.TryGetNodeFor(COF.UUID, out var cofNode))
            {
                Logger.Log($"Failed to find COF node in inventory store", Helpers.LogLevel.Warning, Client);
                return new List<InventoryItem>();
            }

            List<InventoryBase> cofContents;
            if (cofNode.NeedsUpdate)
            {
                cofContents = await Client.Inventory.RequestFolderContents(
                    COF.UUID,
                    COF.OwnerID,
                    true,
                    true,
                    InventorySortOrder.ByName,
                    cancellationToken
                );
            }
            else
            {
                cofContents = Client.Inventory.Store.GetContents(COF);
            }

            var cofLinks = cofContents.OfType<InventoryItem>()
                .Where(n => n.IsLink())
                .ToList();

            return cofLinks;
        }

        /// <summary>
        /// Get inventory ID of a prim
        /// </summary>
        /// <param name="prim">Prim to check</param>
        /// <returns>Inventory ID of the object. UUID.Zero if not found</returns>
        public static UUID GetAttachmentItem(Primitive prim)
        {
            if (prim.NameValues == null) return UUID.Zero;

            for (var i = 0; i < prim.NameValues.Length; i++)
            {
                if (prim.NameValues[i].Name == "AttachItemID")
                {
                    return (UUID)prim.NameValues[i].Value.ToString();
                }
            }
            return UUID.Zero;
        }

        /// <summary>
        /// Is an inventory item currently attached
        /// </summary>
        /// <param name="attachments">List of root prims that are attached to our avatar</param>
        /// <param name="item">Inventory item to check</param>
        /// <returns>True if the inventory item is attached to avatar</returns>
        public static bool IsAttached(IEnumerable<Primitive> attachments, InventoryItem item)
        {
            return attachments.Any(prim => GetAttachmentItem(prim) == item.UUID);
        }

        /// <summary>
        /// Checks if inventory item of Wearable type is worn
        /// </summary>
        /// <param name="currentlyWorn">Current outfit</param>
        /// <param name="item">Item to check</param>
        /// <returns>True if the item is worn</returns>
        public static bool IsWorn(IEnumerable<AppearanceManager.WearableData> currentlyWorn, InventoryItem item)
        {
            return currentlyWorn.Any(worn => worn.ItemID == item.UUID);
        }

        /// <summary>
        /// Can this inventory type be worn
        /// </summary>
        /// <param name="item">Item to check</param>
        /// <returns>True if the inventory item can be worn</returns>
        public static bool CanBeWorn(InventoryBase item)
        {
            return item is InventoryWearable || item is InventoryAttachment || item is InventoryObject;
        }

        /// <summary>
        /// Attach an inventory item
        /// </summary>
        /// <param name="item">Item to be attached</param>
        /// <param name="point">Attachment point</param>
        /// <param name="replace">Replace existing attachment at that point first?</param>
        public async Task Attach(InventoryItem item, AttachmentPoint point, bool replace, CancellationToken cancellationToken = default)
        {
            // TODO: Check attachment limits
            // TODO: Check if item is in library and needs to be copied

            var trashFolderId = Client.Inventory.FindFolderForType(FolderType.Trash);
            var isInTrash = await IsObjectDescendentOf(OriginalInventoryItem(item), trashFolderId, cancellationToken);
            if(isInTrash)
            {
                Logger.Log($"Cannot attach an item that is currently in the trash.", Helpers.LogLevel.Warning, Client);
                return;
            }

            Client.Appearance.Attach(item, point, replace);
            await AddLink(item, cancellationToken);
        }

        /// <summary>
        /// Creates a new COF link
        /// </summary>
        /// <param name="item">Original item to be linked from COF</param>
        private async Task AddLink(InventoryItem item, CancellationToken cancellationToken = default)
        {
            if (item is InventoryWearable wearableItem && !IsBodyPart(item))
            {
                var layer = 0;
                var desc = $"{(int)wearableItem.WearableType}{layer:00}";
                await AddLink(item, desc, cancellationToken);
            }
            else
            {
                await AddLink(item, string.Empty, cancellationToken);
            }
        }

        /// <summary>
        /// Creates a new COF link
        /// </summary>
        /// <param name="item">Original item to be linked from COF</param>
        /// <param name="newDescription">Description for the link</param>
        private async Task AddLink(InventoryItem item, string newDescription, CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                Logger.Log("Can't add link; COF hasn't been initialized.", Helpers.LogLevel.Warning, Client);
                return;
            }

            var cofLinks = await ContentLinks(cancellationToken);
            if (cofLinks.Find(itemLink => itemLink.AssetUUID == item.UUID) == null)
            {
                Client.Inventory.CreateLink(
                    COF.UUID,
                    item.UUID,
                    item.Name,
                    newDescription,
                    item.InventoryType,
                    UUID.Random(),
                    (success, newItem) =>
                    {
                        if (success)
                        {
                            Client.Inventory.RequestFetchInventory(newItem.UUID, newItem.OwnerID);
                        }
                    },
                    cancellationToken
                );
            }
        }

        /// <summary>
        /// Remove a link to specified inventory item
        /// </summary>
        /// <param name="itemID">ID of the target inventory item for which we want link to be removed</param>
        private async Task RemoveLink(UUID itemID, CancellationToken cancellationToken = default)
        {
            await RemoveLinks(new List<UUID>(1) { itemID }, cancellationToken);
        }

        /// <summary>
        /// Remove a link to specified inventory item
        /// </summary>
        /// <param name="itemIDsToRemove">List of IDs of the target inventory item for which we want link to be removed</param>
        private async Task RemoveLinks(List<UUID> itemIDsToRemove, CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                Logger.Log("Can't remove link; COF hasn't been initialized.", Helpers.LogLevel.Warning, Client);
                return;
            }

            var cofLinks = await ContentLinks(cancellationToken);
            var cofLinksByAssetId = new Dictionary<UUID, List<UUID>>();
            foreach (var cofLink in cofLinks)
            {
                if(!cofLinksByAssetId.TryGetValue(cofLink.AssetUUID, out var associatedLinks))
                {
                    associatedLinks = new List<UUID>();
                    cofLinksByAssetId[cofLink.AssetUUID] = associatedLinks;
                }

                associatedLinks.Add(cofLink.UUID);
            }

            foreach (var itemIdToRemove in itemIDsToRemove)
            {
                if(!cofLinksByAssetId.TryGetValue(itemIdToRemove, out var linkIdsToRemove))
                {
                    continue;
                }

                await Client.Inventory.RemoveItemsAsync(linkIdsToRemove, cancellationToken);
            }
        }

        /// <summary>
        /// Remove attachment
        /// </summary>
        /// <param name="item">>Inventory item to be detached</param>
        public async Task Detach(InventoryItem item, CancellationToken cancellationToken = default)
        {
            var realItem = OriginalInventoryItem(item);
            if (!Instance.RLV.AllowDetach(realItem))
            {
                return;
            }

            // TODO: Deny removal of body parts?

            Client.Appearance.Detach(item);
            await RemoveLink(item.UUID, cancellationToken);
        }

        public async Task<List<InventoryItem>> GetWornAt(WearableType type, CancellationToken cancellationToken = default)
        {
            var wornItemsByAssetId = new Dictionary<UUID, InventoryItem>();

            var contentLinks = await ContentLinks(cancellationToken);
            foreach (var link in contentLinks)
            {
                var originalItem = OriginalInventoryItem(link);
                if (!(originalItem is InventoryWearable wearable))
                {
                    continue;
                }

                if (wearable.WearableType == type)
                {
                    wornItemsByAssetId[wearable.AssetUUID] = wearable;
                }
            }

            return wornItemsByAssetId.Values.ToList();
        }

        /// <summary>
        /// Resolves inventory links and returns a real inventory item that
        /// the link is pointing to
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public InventoryItem OriginalInventoryItem(InventoryItem item)
        {
            if (!item.IsLink())
            {
                return item;
            }

            if (!Client.Inventory.Store.TryGetValue<InventoryItem>(item.AssetUUID, out var inventoryItem))
            {
                return item;
            }

            return inventoryItem;
        }

        /// <summary>
        /// Replaces the current outfit and updates COF links accordingly
        /// </summary>
        /// <param name="newOutfit">List of new wearables and attachments that comprise the new outfit</param>
        public async Task<bool> ReplaceOutfit(UUID newOutfitFolderId, CancellationToken cancellationToken = default)
        {
            // TODO: Copy from library if necessary

            const string generalErrorMessage = "Try refreshing your inventory or clearing your cache.";

            var trashFolderId = Client.Inventory.FindFolderForType(FolderType.Trash);

            var newOutfit = await Client.Inventory.RequestFolderContents(
                newOutfitFolderId,
                Client.Self.AgentID,
                true,
                true,
                InventorySortOrder.ByName,
                cancellationToken
            );
            if(newOutfit == null)
            {
                Logger.Log($"Failed to request contents of replacement outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, Client);
                return false;
            }

            if(!Client.Inventory.Store.TryGetNodeFor(newOutfitFolderId, out var newOutfitFolderNode))
            {
                Logger.Log($"Failed to get node for replacement outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, Client);
                return false;
            }

            var isOutfitInTrash = await IsObjectDescendentOf(newOutfitFolderNode.Data, newOutfitFolderNode.Data.OwnerID);
            if(isOutfitInTrash)
            {
                Logger.Log($"Cannot wear an outfit that is currently in the trash.", Helpers.LogLevel.Warning, Client);
                return false;
            }

            var currentOutfitFolder = await Client.Appearance.GetCurrentOutfitFolder(cancellationToken);
            if(currentOutfitFolder == null)
            {
                Logger.Log($"Failed to find current outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, Client);
                return false;
            }

            var currentOutfitContents = await Client.Inventory.RequestFolderContents(
                currentOutfitFolder.UUID,
                currentOutfitFolder.OwnerID,
                true,
                true,
                InventorySortOrder.ByName,
                cancellationToken
            );
            if (currentOutfitContents == null)
            {
                Logger.Log($"Failed to request contents of current outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, Client);
                return false;
            }

            var itemsToWear = new Dictionary<UUID, InventoryItem>();
            var existingBodypartLinks = new List<InventoryItem>();
            var bodypartsToWear = new Dictionary<WearableType, InventoryWearable>();
            var gesturesToActivate = new Dictionary<UUID, InventoryItem>();
            var numClothingLayers = 0;
            var numAttachedObjects = 0;

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
                if(isInTrash)
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
                else if(inventoryItem.AssetType == AssetType.Clothing)
                {
                    if(numClothingLayers >= MaxClothingLayers)
                    {
                        continue;
                    }

                    numClothingLayers++;
                }
                else if(inventoryItem.AssetType == AssetType.Object)
                {
                    if(numAttachedObjects >= Client.Self.Benefits.AttachmentLimit)
                    {
                        continue;
                    }

                    ++numAttachedObjects;
                }

                itemsToWear[inventoryItem.UUID] = inventoryItem;
            }

            var existingLinkTargets = currentOutfitContents
                .OfType<InventoryItem>()
                .Where(n => !n.IsLink())
                .ToDictionary(k => k.UUID, v => v);
            var linksToRemove = new List<InventoryBase>();
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

                if (!existingLinkTargets.TryGetValue(itemLink.AssetUUID, out var linkTarget))
                {
                    linksToRemove.Add(itemLink);
                    continue;
                }

                if (linkTarget.AssetType == AssetType.Bodypart)
                {
                    existingBodypartLinks.Add(itemLink);
                    continue;
                }

                if (linkTarget.AssetType == AssetType.Gesture)
                {
                    if (!gesturesToActivate.ContainsKey(linkTarget.UUID))
                    {
                        gesturesToDeactivate.Add(linkTarget.UUID);
                    }
                }

                linksToRemove.Add(itemLink);
            }

            // Deactivate old gestures, activate new gestures
            foreach (var gestureId in gesturesToDeactivate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Client.Self.DeactivateGesture(gestureId);
            }
            foreach (var item in gesturesToActivate.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Client.Self.ActivateGesture(item.UUID, item.AssetUUID);
            }

            // Replace bodyparts, but keep old bodyparts if new outfit lacks them
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
                }

                linksToRemove.Add(existingLink);
            }

            // Bare minimum outfit check
            if (!bodypartsToWear.ContainsKey(WearableType.Shape) ||
                !bodypartsToWear.ContainsKey(WearableType.Skin) ||
                !bodypartsToWear.ContainsKey(WearableType.Eyes) ||
                !bodypartsToWear.ContainsKey(WearableType.Hair))
            {
                Logger.Log("New outfit must contain a Shape, Skin, Eyes, and Hair", Helpers.LogLevel.Error, Client);
                return false;
            }

            // Clear out all existing current outfit links
            var toRemoveIds = linksToRemove
                .Select(n => n.UUID)
                .Distinct();
            await Client.Inventory.RemoveItemsAsync(toRemoveIds, cancellationToken);

            // Add new outfit links
            foreach (var item in bodypartsToWear)
            {
                await AddLink(item.Value, cancellationToken);
            }
            foreach (var item in itemsToWear)
            {
                await AddLink(item.Value, cancellationToken);
            }

            // Add link to outfit folder we're putting on
            if (newOutfitFolderNode != null)
            {
                Client.Inventory.CreateLink(
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
                            Client.Inventory.RequestFetchInventory(newItem.UUID, newItem.OwnerID);
                        }
                    },
                    cancellationToken
                );
            }

            // Wear new outfit
            var tcs = new TaskCompletionSource<bool>();
            void handleAppearanceSet(object sender, AppearanceSetEventArgs e)
            {
                tcs.TrySetResult(true);
            }

            try
            {
                Client.Appearance.AppearanceSet += handleAppearanceSet;
                Client.Appearance.ReplaceOutfit(itemsToWear.Values.ToList(), false);

                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));
                if (completedTask != tcs.Task)
                {
                    Logger.Log("Timed out while waiting for AppearanceSet confirmation. Are you changing outfits too quickly?", Helpers.LogLevel.Error, Client);
                    return false;
                }
            }
            finally
            {
                Client.Appearance.AppearanceSet -= handleAppearanceSet;
            }

            return true;
        }

        /// <summary>
        /// Add items to current outfit
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="replace">Should existing wearable of the same type be removed</param>
        public async Task AddToOutfit(InventoryItem item, bool replace, CancellationToken cancellationToken = default)
        {
            await AddToOutfit(new List<InventoryItem>(1) { item }, replace, cancellationToken);
        }

        private async Task<InventoryBase> FetchParent(InventoryBase item, CancellationToken cancellationToken = default)
        {
            if (!Client.Inventory.Store.TryGetNodeFor(item.ParentUUID, out var parent))
            {
                return await Client.Inventory.FetchItemHttpAsync(item.ParentUUID, item.OwnerID, cancellationToken);
            }

            return parent.Data;
        }

        private async Task<bool> IsObjectDescendentOf(InventoryBase item, UUID parentId, CancellationToken cancellationToken = default)
        {
            const int kArbritrayDepthLimit = 255;

            if (parentId == null)
            {
                return false;
            }

            var parentIter = item;
            for (var i = 0; i < kArbritrayDepthLimit; ++i)
            {
                if (parentIter.ParentUUID == parentId)
                {
                    return true;
                }

                parentIter = await FetchParent(parentIter, cancellationToken);
                if (parentIter == null)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Add items to current outfit
        /// </summary>
        /// <param name="itemsToAdd">List of items to add</param>
        /// <param name="replace">Should existing wearable of the same type be removed</param>
        public async Task AddToOutfit(List<InventoryItem> itemsToAdd, bool replace, CancellationToken cancellationToken = default)
        {
            // TODO: Copy from library if necessary

            if (COF == null)
            {
                Logger.Log("Can't add to outfit link; COF hasn't been initialized.", Helpers.LogLevel.Warning, Client);
                return;
            }

            var trashFolderId= Client.Inventory.FindFolderForType(FolderType.Trash);

            var cofLinks = await ContentLinks(cancellationToken);
            var cofRealItems = new Dictionary<UUID, InventoryBase>();
            var cofLinkAssetIds = new HashSet<UUID>();
            var currentBodyparts = new Dictionary<WearableType, InventoryWearable>();
            var currentClothing = new Dictionary<WearableType, List<InventoryWearable>>();
            var currentAttachmentPoints = new Dictionary<AttachmentPoint, List<InventoryObject>>();
            var numClothingLayers = 0;
            var numAttachedObjects = 0;

            foreach (var item in cofLinks)
            {
                var realItem = OriginalInventoryItem(item);
                cofRealItems[realItem.UUID] = realItem;
                cofLinkAssetIds.Add(item.AssetUUID);

                if (realItem is InventoryWearable wearable)
                {
                    if(realItem.AssetType == AssetType.Bodypart)
                    {
                        currentBodyparts[wearable.WearableType] = wearable;
                    }
                    else if(realItem.AssetType == AssetType.Clothing)
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
                else if(realItem is InventoryObject inventoryObject)
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

            var linksToRemove = new List<UUID>();

            // Resolve inventory links and remove wearables of the same type from COF
            var outfit = new List<InventoryItem>();

            foreach (var item in itemsToAdd)
            {
                var realItem = OriginalInventoryItem(item);
                var isItemInTrash = await IsObjectDescendentOf(realItem, trashFolderId, cancellationToken);

                if(isItemInTrash)
                {
                    continue;
                }
                if (cofLinkAssetIds.Contains(realItem.UUID))
                {
                    continue;
                }
                if(outfit.FirstOrDefault(n => n.UUID == realItem.UUID) != null)
                {
                    continue;
                }

                if (realItem is InventoryWearable wearable)
                {
                    if(wearable.AssetType == AssetType.Clothing)
                    {
                        if(replace)
                        {
                            if(currentClothing.TryGetValue(wearable.WearableType, out var currentClothingOfType))
                            {
                                // Remove all existing clothing links for this wearable type
                                foreach (var clothingToRemove in currentClothingOfType)
                                {
                                    var clothingLinksToRemove = cofLinks
                                        .Where(n => n.IsLink() && n.AssetUUID == clothingToRemove.UUID)
                                        .Select(n => n.UUID);
                                    linksToRemove.AddRange(clothingLinksToRemove);
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
                        if(currentBodyparts.TryGetValue(wearable.WearableType, out var existingBodyPart))
                        {
                            var bodypartLinksToRemove = cofLinks
                                .Where(n => n.IsLink() && n.AssetUUID == existingBodyPart.UUID)
                                .Select(n => n.UUID);
                            linksToRemove.AddRange(bodypartLinksToRemove);
                        }
                    }
                }
                else if (realItem.AssetType == AssetType.Gesture)
                {
                    Client.Self.ActivateGesture(realItem.UUID, realItem.AssetUUID);
                }
                else if(realItem is InventoryObject objectToAdd)
                {
                    if (replace)
                    {
                        // TODO: It's really confusing what should be done with AddToOutfit(replace=true) with objects
                        if (currentAttachmentPoints.TryGetValue(objectToAdd.AttachPoint, out var attachedObjectsToRemove))
                        {
                            foreach (var attachedObject in attachedObjectsToRemove)
                            {
                                var attachedObjectLinksToRemove = cofLinks
                                    .Where(n => n.IsLink() && n.AssetUUID == attachedObject.UUID)
                                    .Select(n => n.UUID);
                                linksToRemove.AddRange(attachedObjectLinksToRemove);
                            }
                        }
                    }
                    else
                    {
                        if (numAttachedObjects >= Client.Self.Benefits.AttachmentLimit)
                        {
                            continue;
                        }

                        ++numAttachedObjects;
                    }
                }
                else
                {
                    continue;
                }

                outfit.Add(realItem);
            }

            await Client.Inventory.RemoveItemsAsync(linksToRemove, cancellationToken);

            // Add links to new items
            foreach (var item in outfit)
            {
                await AddLink(item, cancellationToken);
            }

            Client.Appearance.AddToOutfit(outfit, replace);
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Thread.Sleep(2000);
                Client.Appearance.RequestSetAppearance(true);
            });
        }

        /// <summary>
        /// Remove an item from the current outfit
        /// </summary>
        /// <param name="item">Item to remove</param>
        public async Task RemoveFromOutfit(InventoryItem item, CancellationToken cancellationToken = default)
        {
            await RemoveFromOutfit(new List<InventoryItem>(1) { item }, cancellationToken);
        }

        /// <summary>
        /// Remove specified items from the current outfit
        /// </summary>
        /// <param name="items">List of items to remove</param>
        public async Task RemoveFromOutfit(List<InventoryItem> items, CancellationToken cancellationToken = default)
        {
            // Resolve inventory links
            var outfit = items.Select(OriginalInventoryItem).Where(realItem => Instance.RLV.AllowDetach(realItem)).ToList();

            // Remove links to all items that were removed
            var toRemove = outfit.FindAll(item => CanBeWorn(item) && !IsBodyPart(item)).Select(item => item.UUID).ToList();
            await RemoveLinks(toRemove, cancellationToken);

            Client.Appearance.RemoveFromOutfit(outfit);
        }

        public bool IsBodyPart(InventoryItem item)
        {
            var realItem = OriginalInventoryItem(item);
            if (!(realItem is InventoryWearable wearable)) return false;

            var t = wearable.WearableType;
            return t == WearableType.Shape ||
                   t == WearableType.Skin ||
                   t == WearableType.Eyes ||
                   t == WearableType.Hair;
        }

        /// <summary>
        /// Force rebaking textures
        /// </summary>
        public void RebakeTextures()
        {
            Client.Appearance.RequestSetAppearance(true);
        }

        #endregion Public methods
    }
}
