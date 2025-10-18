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

using LibreMetaverse.RLV;
using LibreMetaverse.RLV.EventArguments;
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

        private GridClient client;
        private readonly RadegastInstance instance;
        private readonly CompositeCOFPolicy policy = new CompositeCOFPolicy();
        private bool initializedCOF = false;

        public InventoryFolder COF { get; private set; }

        public int MaxClothingLayers => 60;

        #endregion Fields

        #region Construction and disposal
        public CurrentOutfitFolder(RadegastInstance instance)
        {
            this.instance = instance;
            client = instance.Client;
            this.instance.ClientChanged += instance_ClientChanged;
            RegisterClientEvents(client);
        }

        public void Dispose()
        {
            UnregisterClientEvents(client);
            instance.ClientChanged -= instance_ClientChanged;
        }
        #endregion Construction and disposal

        #region Policies

        public ICOFPolicy AddPolicy(ICOFPolicy policy) => this.policy.AddPolicy(policy);
        public void RemovePolicy(ICOFPolicy policy) => this.policy.RemovePolicy(policy);

        #endregion

        #region Event handling

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(client);
            client = e.Client;
            RegisterClientEvents(client);
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

            initializedCOF = false;
        }

        private void Inventory_FolderUpdated(object sender, FolderUpdatedEventArgs e)
        {
            if (COF == null)
            {
                return;
            }

            if (e.FolderID == COF.UUID && e.Success)
            {
                if (client.Inventory.Store.TryGetValue<InventoryFolder>(COF.UUID, out var newCOF))
                {
                    // Sometimes we will need to update our COF reference, such as when we clear
                    //   and re-fetch our Inventory.Store
                    COF = newCOF;
                }

                var cofLinks = GetCurrentOutfitLinks().Result;

                var items = new Dictionary<UUID, UUID>();
                foreach (var link in cofLinks)
                {
                    items[link.AssetUUID] = client.Self.AgentID;
                }

                if (items.Count > 0)
                {
                    client.Inventory.RequestFetchInventory(items);
                }
            }
        }

        private void Objects_KillObject(object sender, KillObjectEventArgs e)
        {
            if (client.Network.CurrentSim != e.Simulator)
            {
                return;
            }

            if (client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(e.ObjectLocalID, out var prim))
            {
                var invItemId = CurrentOutfitFolder.GetAttachmentItemID(prim);
                if (invItemId != UUID.Zero)
                {
                    RemoveLinksToByActualId(new List<UUID>() { invItemId }).Wait();
                }
            }
        }

        private void Network_OnSimChanged(object sender, SimChangedEventArgs e)
        {
            client.Network.CurrentSim.Caps.CapabilitiesReceived += Simulator_OnCapabilitiesReceived;
        }

        private void Simulator_OnCapabilitiesReceived(object sender, CapabilitiesReceivedEventArgs e)
        {
            e.Simulator.Caps.CapabilitiesReceived -= Simulator_OnCapabilitiesReceived;

            if (e.Simulator == client.Network.CurrentSim && !initializedCOF)
            {
                InitializeCurrentOutfitFolder().Wait();
            }
        }

        #endregion Event handling

        #region Private methods

        private async Task<bool> InitializeCurrentOutfitFolder(CancellationToken cancellationToken = default)
        {
            COF = await client.Appearance.GetCurrentOutfitFolder(cancellationToken);

            if (COF == null)
            {
                //CreateCurrentOutfitFolder();
            }
            else
            {
                await client.Inventory.RequestFolderContents(COF.UUID, client.Self.AgentID,
                    true, true, InventorySortOrder.ByDate, cancellationToken);
            }

            Logger.Log($"Initialized Current Outfit Folder with UUID {COF.UUID} v.{COF.Version}", Helpers.LogLevel.Info, client);

            initializedCOF = COF != null;
            return initializedCOF;
        }

        private void CreateCurrentOutfitFolder()
        {
            UUID cofId = client.Inventory.CreateFolder(client.Inventory.Store.RootFolder.UUID,
                "Current Outfit", FolderType.CurrentOutfit);
            if (client.Inventory.Store.Contains(cofId) && client.Inventory.Store[cofId] is InventoryFolder folder)
            {
                COF = folder;
            }
        }

        private bool IsBodyPart(InventoryItem item)
        {
            var realItem = instance.COF.ResolveInventoryLink(item);
            if (realItem == null)
            {
                return false;
            }

            if (!(realItem is InventoryWearable wearable))
            {
                return false;
            }

            return wearable.WearableType == WearableType.Shape ||
                   wearable.WearableType == WearableType.Skin ||
                   wearable.WearableType == WearableType.Eyes ||
                   wearable.WearableType == WearableType.Hair;
        }

        /// <summary>
        /// Return links found in Current Outfit Folder
        /// </summary>
        /// <returns>List of <see cref="InventoryItem"/> that can be part of appearance (attachments, wearables)</returns>
        /// <param name="cancellationToken"></param>
        public async Task<List<InventoryItem>> GetCurrentOutfitLinks(CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                await InitializeCurrentOutfitFolder(cancellationToken);
            }

            if (COF == null)
            {
                Logger.Log($"COF is null", Helpers.LogLevel.Warning, client);
                return new List<InventoryItem>();
            }

            if (!client.Inventory.Store.TryGetNodeFor(COF.UUID, out var cofNode))
            {
                Logger.Log($"Failed to find COF node in inventory store", Helpers.LogLevel.Warning, client);
                return new List<InventoryItem>();
            }

            List<InventoryBase> cofContents;
            if (cofNode.NeedsUpdate)
            {
                cofContents = await client.Inventory.RequestFolderContents(
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
                cofContents = client.Inventory.Store.GetContents(COF);
            }

            var cofLinks = cofContents.OfType<InventoryItem>()
                .Where(n => n.IsLink())
                .ToList();

            return cofLinks;
        }

        /// <summary>
        /// Creates a new COF link
        /// </summary>
        /// <param name="item">Original item to be linked from COF</param>
        /// <param name="cancellationToken"></param>
        private async Task AddLink(InventoryItem item, CancellationToken cancellationToken = default)
        {
            if (item is InventoryWearable wearableItem && !IsBodyPart(item))
            {
                var layer = 0;
                var description = $"{(int)wearableItem.WearableType}{layer:00}";
                await AddLink(item, description, cancellationToken);
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
        /// <param name="cancellationToken"></param>
        private async Task AddLink(InventoryItem item, string newDescription, CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                Logger.Log("Can't add link; COF hasn't been initialized.", Helpers.LogLevel.Warning, client);
                return;
            }

            var cofLinks = await GetCurrentOutfitLinks(cancellationToken);
            if (cofLinks.Find(itemLink => itemLink.AssetUUID == item.UUID) == null)
            {
                client.Inventory.CreateLink(
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
                            client.Inventory.RequestFetchInventory(newItem.UUID, newItem.OwnerID);
                        }
                    },
                    cancellationToken
                );
            }
        }

        /// <summary>
        /// Removes all COF links to the specified actual item IDs
        /// </summary>
        /// <param name="actualItemIdsToRemoveLinksTo">List of item IDs to remove all COF links to</param>
        /// <param name="cancellationToken"></param>
        private async Task RemoveLinksToByActualId(IEnumerable<UUID> actualItemIdsToRemoveLinksTo, CancellationToken cancellationToken = default)
        {
            var actualItemIdsSet = actualItemIdsToRemoveLinksTo.ToHashSet();

            var cofLinks = await GetCurrentOutfitLinks(cancellationToken);
            var linkIdsToRemove = cofLinks
                .Where(n => n.IsLink() && actualItemIdsSet.Contains(n.ActualUUID))
                .Select(n => n.UUID)
                .Distinct()
                .ToList();

            if (linkIdsToRemove.Count > 0)
            {
                await client.Inventory.RemoveItemsAsync(linkIdsToRemove, cancellationToken);
            }
        }

        /// <summary>
        /// Removes all COF links to the specified items
        /// </summary>
        /// <param name="actualItemsToRemoveLinksTo">List of actual item we want to remove COF links to</param>
        /// <param name="cancellationToken"></param>
        private async Task RemoveLinksTo(List<InventoryItem> actualItemsToRemoveLinksTo, CancellationToken cancellationToken = default)
        {
            if (COF == null)
            {
                Logger.Log("Can't remove link; COF hasn't been initialized.", Helpers.LogLevel.Warning, client);
                return;
            }

            var cofLinks = await GetCurrentOutfitLinks(cancellationToken);

            var actualItemIDsToRemoveLinksTo = actualItemsToRemoveLinksTo
                .Select(n => n.ActualUUID)
                .ToHashSet();

            var linkIdsToRemove = cofLinks
                .Where(n => n.IsLink() && actualItemIDsToRemoveLinksTo.Contains(n.ActualUUID))
                .Select(n => n.UUID)
                .Distinct()
                .ToList();

            if (linkIdsToRemove.Count > 0)
            {
                await client.Inventory.RemoveItemsAsync(linkIdsToRemove, cancellationToken);
            }
        }

        #endregion Private methods

        #region Public methods

        /// <summary>
        /// Determines if we can attach the specified object
        /// </summary>
        /// <param name="item">Object to check</param>
        /// <param name="cancellationToken"></param>
        /// <returns>True if we are able to attach this object</returns>
        public async Task<bool> CanAttachItem(InventoryItem item, CancellationToken cancellationToken = default)
        {
            var trashFolderId = client.Inventory.FindFolderForType(FolderType.Trash);
            var rootFolderId = client.Inventory.FindFolderForType(FolderType.Root);

            var realItem = instance.COF.ResolveInventoryLink(item);
            if (realItem == null)
            {
                Logger.Log($"Cannot attach an item because the link could not be resolved.", Helpers.LogLevel.Warning, client);
                return false;
            }

            if (!policy.CanAttach(realItem))
            {
                return false;
            }

            var isInTrash = await instance.COF.IsObjectDescendentOf(realItem, trashFolderId, cancellationToken);
            if (isInTrash)
            {
                Logger.Log($"Cannot attach an item that is currently in the trash.", Helpers.LogLevel.Warning, client);
                return false;
            }

            var isInPlayerInventory = await instance.COF.IsObjectDescendentOf(realItem, rootFolderId, cancellationToken);
            if (!isInPlayerInventory)
            {
                Logger.Log($"Cannot attach an item that is not in your inventory.", Helpers.LogLevel.Warning, client);
                return false;
            }

            var cofLinks = await GetCurrentOutfitLinks(cancellationToken);

            if (cofLinks.FirstOrDefault(n => n.ActualUUID == item.ActualUUID) != null)
            {
                return false;
            }

            if (item is InventoryObject obj)
            {
                var numAttachedObjects = cofLinks
                    .Count(n => n is InventoryObject);

                if (numAttachedObjects + 1 >= client.Self.Benefits.AttachmentLimit)
                {
                    return false;
                }
            }
            else if (item is InventoryWearable wearable)
            {
                var numClothingLayers = cofLinks
                    .Count(n => n is InventoryWearable);

                numClothingLayers++;

                if (numClothingLayers + 1 >= MaxClothingLayers)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines if we can detach the specified object
        /// </summary>
        /// <param name="item">Object to check</param>
        /// <param name="cancellationToken"></param>
        /// <returns>True if we are able to detach this object</returns>
        public async Task<bool> CanDetachItem(InventoryItem item, CancellationToken cancellationToken = default)
        {
            if (!policy.CanDetach(item))
            {
                return false;
            }

            var realItem = instance.COF.ResolveInventoryLink(item);

            if (realItem == null)
            {
                return false;
            }

            if (IsBodyPart(realItem))
            {
                return false;
            }

            var cofLinks = await GetCurrentOutfitLinks(cancellationToken);
            if (cofLinks.FirstOrDefault(n => n.ActualUUID == realItem.UUID) == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempt to attach an object to a specific attachment point
        /// </summary>
        /// <param name="item">Item to be attached</param>
        /// <param name="point">Attachment point</param>
        /// <param name="replace">Replace existing attachment at that point first?</param>
        /// <param name="cancellationToken"></param>
        public async Task Attach(InventoryItem item, AttachmentPoint point, bool replace, CancellationToken cancellationToken = default)
        {
            if (!await CanAttachItem(item, cancellationToken))
            {
                return;
            }

            client.Appearance.Attach(item, point, replace);

            await policy.ReportItemChange(new List<InventoryItem>() { item }, new List<InventoryItem>(), cancellationToken);
            await AddLink(item, cancellationToken);
        }

        /// <summary>
        /// Remove attachment
        /// </summary>
        /// <param name="item">Inventory item to be detached</param>
        /// <param name="cancellationToken"></param>
        public async Task Detach(InventoryItem item, CancellationToken cancellationToken = default)
        {
            if (!await CanDetachItem(item, cancellationToken))
            {
                return;
            }

            var attachmentPoint = AttachmentPoint.Default;
            if (item is InventoryAttachment attachedItem)
            {
                attachmentPoint = attachedItem.AttachmentPoint;
            }
            else if(item is InventoryObject attachedObject)
            {
                attachmentPoint = attachedObject.AttachPoint;
            }

            client.Appearance.Detach(item);

            await policy.ReportItemChange(new List<InventoryItem>(), new List<InventoryItem>() { item }, cancellationToken);
            await RemoveLinksTo(new List<InventoryItem>() { item }, cancellationToken);
        }

        /// <summary>
        /// Gets a list of worn items of a specific wearable type
        /// </summary>
        /// <param name="type">Specific wearable type to find</param>
        /// <param name="cancellationToken"></param>
        /// <returns>List of all worn items of the specified wearable type</returns>
        public async Task<List<InventoryItem>> GetWornAt(WearableType type, CancellationToken cancellationToken = default)
        {
            var wornItemsByAssetId = new Dictionary<UUID, InventoryItem>();

            var cofLinks = await GetCurrentOutfitLinks(cancellationToken);
            foreach (var link in cofLinks)
            {
                var realItem = instance.COF.ResolveInventoryLink(link);
                if (realItem == null)
                {
                    continue;
                }

                if (!(realItem is InventoryWearable wearable))
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
        /// Replaces the current outfit and updates COF links accordingly
        /// </summary>
        /// <param name="newOutfitFolderId">List of new wearables and attachments that comprise the new outfit</param>
        /// <param name="cancellationToken"></param>
        /// <returns>True on success</returns>
        public async Task<bool> ReplaceOutfit(UUID newOutfitFolderId, CancellationToken cancellationToken = default)
        {
            // TODO: Copy from library if necessary

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
                Logger.Log($"Failed to request contents of replacement outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, client);
                return false;
            }

            if (!client.Inventory.Store.TryGetNodeFor(newOutfitFolderId, out var newOutfitFolderNode))
            {
                Logger.Log($"Failed to get node for replacement outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, client);
                return false;
            }

            var isOutfitInTrash = await instance.COF.IsObjectDescendentOf(newOutfitFolderNode.Data, trashFolderId, cancellationToken);
            if (isOutfitInTrash)
            {
                Logger.Log($"Cannot wear an outfit that is currently in the trash.", Helpers.LogLevel.Warning, client);
                return false;
            }

            var isOutfitInInventory = await instance.COF.IsObjectDescendentOf(newOutfitFolderNode.Data, rootFolderId, cancellationToken);
            if (!isOutfitInInventory)
            {
                Logger.Log($"Cannot wear an outfit that is not currently in your inventory.", Helpers.LogLevel.Warning, client);
                return false;
            }

            var currentOutfitFolder = await client.Appearance.GetCurrentOutfitFolder(cancellationToken);
            if (currentOutfitFolder == null)
            {
                Logger.Log($"Failed to find current outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, client);
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
                Logger.Log($"Failed to request contents of current outfit folder. {generalErrorMessage}", Helpers.LogLevel.Warning, client);
                return false;
            }

            var itemsToRemove = new List<InventoryItem>();

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

                if (!policy.CanAttach(inventoryItem))
                {
                    continue;
                }

                var isInTrash = await instance.COF.IsObjectDescendentOf(inventoryItem, trashFolderId, cancellationToken);
                if (isInTrash)
                {
                    continue;
                }

                var isInInventory = await instance.COF.IsObjectDescendentOf(inventoryItem, rootFolderId, cancellationToken);
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
                    // We're already wearing the item that exists in the new outfit, don't re-add links to it
                    itemsBeingAdded.Remove(realItem.UUID);
                    continue;
                }

                if (!policy.CanDetach(realItem))
                {
                    if (newOutfitItemMap.ContainsKey(realItem.UUID))
                    {
                        newOutfitItemMap[realItem.UUID] = realItem;
                    }
                    else
                    {
                        newOutfitItemMap[realItem.UUID] = realItem;
                    }

                    if (realItem is InventoryWearable bodypartItem)
                    {
                        // We cannot detach this bodypart, we need to ignore the replacement body part
                        bodypartsToWear[bodypartItem.WearableType] = bodypartItem;
                    }

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

            // Deactivate old gestures, activate new gestures
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
                Logger.Log("New outfit must contain a Shape, Skin, Eyes, and Hair", Helpers.LogLevel.Error, client);
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

            // Wear new outfit
            var tcs = new TaskCompletionSource<bool>();
            void handleAppearanceSet(object sender, AppearanceSetEventArgs e)
            {
                tcs.TrySetResult(true);
            }

            await policy.ReportItemChange(new List<InventoryItem>(), itemsBeingRemoved.Values.ToList(), cancellationToken);

            try
            {
                client.Appearance.AppearanceSet += handleAppearanceSet;
                client.Appearance.ReplaceOutfit(newOutfitItemMap.Values.ToList(), false);

                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000, cancellationToken));
                if (completedTask != tcs.Task)
                {
                    Logger.Log("Timed out while waiting for AppearanceSet confirmation. Are you changing outfits too quickly?", Helpers.LogLevel.Error, client);
                    return false;
                }
            }
            finally
            {
                client.Appearance.AppearanceSet -= handleAppearanceSet;
            }

            await policy.ReportItemChange(itemsBeingAdded.Values.ToList(), new List<InventoryItem>(), cancellationToken);
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
            // TODO: Copy from library if necessary

            if (COF == null)
            {
                Logger.Log("Can't add to outfit link; COF hasn't been initialized.", Helpers.LogLevel.Warning, client);
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
                var realItem = instance.COF.ResolveInventoryLink(item) ?? item;
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
                var realItem = instance.COF.ResolveInventoryLink(item);
                if (realItem == null)
                {
                    continue;
                }

                if (!policy.CanAttach(realItem))
                {
                    continue;
                }

                var isItemInTrash = await instance.COF.IsObjectDescendentOf(realItem, trashFolderId, cancellationToken);
                if (isItemInTrash)
                {
                    continue;
                }

                var isItemInInventory = await instance.COF.IsObjectDescendentOf(realItem, rootFolderId, cancellationToken);
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
                                // Remove all existing clothing links for this wearable type
                                foreach (var clothingToRemove in currentClothingOfType)
                                {
                                    if (!policy.CanDetach(clothingToRemove))
                                    {
                                        continue;
                                    }

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
                            if (!policy.CanDetach(existingBodyPart))
                            {
                                continue;
                            }

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
                        // TODO: It's really confusing what should be done with AddToOutfit(replace=true) with objects
                        if (currentAttachmentPoints.TryGetValue(objectToAdd.AttachPoint, out var attachedObjectsToRemove))
                        {
                            foreach (var attachedObject in attachedObjectsToRemove)
                            {
                                if (!policy.CanDetach(attachedObject))
                                {
                                    continue;
                                }

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

            // Add links to new items
            foreach (var item in itemsToAdd)
            {
                await AddLink(item, cancellationToken);
            }

            client.Appearance.AddToOutfit(itemsToAdd, replace);
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Thread.Sleep(2000);
                client.Appearance.RequestSetAppearance(true);

                policy.ReportItemChange(itemsToAdd, itemsToRemove, cancellationToken).Wait();
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
                Logger.Log("Can't remove from outfit; COF hasn't been initialized.", Helpers.LogLevel.Warning, client);
                return;
            }

            var itemsToRemove = requestedItemsToRemove
                .Select(n => instance.COF.ResolveInventoryLink(n))
                .Where(n => n != null && !IsBodyPart(n) && policy.CanDetach(n))
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
            await policy.ReportItemChange(new List<InventoryItem>(), itemsToRemove, cancellationToken);

            client.Appearance.RemoveFromOutfit(itemsToRemove);
        }

        #endregion Public methods

        #region UnrelatedToCOF

        /// <summary>
        /// Get the inventory ID of an attached prim
        /// </summary>
        /// <param name="prim">Prim to check</param>
        /// <returns>Inventory ID of the object. UUID.Zero if not found</returns>
        public static UUID GetAttachmentItemID(Primitive prim)
        {
            if (prim.NameValues == null)
            {
                return UUID.Zero;
            }

            var attachmentId = prim.NameValues
                .Where(n => n.Name == "AttachItemID")
                .Select(n => new UUID(n.Value.ToString()))
                .FirstOrDefault();

            return attachmentId;
        }

        /// <summary>
        /// Retrieves the linked item from <paramref name="itemLink"/> if it is a link.
        /// </summary>
        /// <param name="itemLink">The link to an inventory item</param>
        /// <returns>The original inventory item, or null if the link could not be resolved</returns>
        public InventoryItem ResolveInventoryLink(InventoryItem itemLink)
        {
            if (itemLink.AssetType != AssetType.Link)
            {
                return itemLink;
            }

            if (!client.Inventory.Store.TryGetValue<InventoryItem>(itemLink.AssetUUID, out var inventoryItem))
            {
                client.Inventory.RequestFetchInventory(itemLink.AssetUUID, itemLink.OwnerID);

                if (!client.Inventory.Store.TryGetValue<InventoryItem>(itemLink.AssetUUID, out inventoryItem))
                {
                    return null;
                }
            }

            return inventoryItem;
        }

        /// <summary>
        /// Retrieves the parent of <paramref name="item"/>
        /// </summary>
        /// <param name="item">Item to retrieve the parent of</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The parent of <paramref name="item"/>, or null if item has no parent or parent does not exist</returns>
        public async Task<InventoryBase> FetchParent(InventoryBase item, CancellationToken cancellationToken = default)
        {
            if (item.ParentUUID == UUID.Zero)
            {
                return null;
            }

            if (!client.Inventory.Store.TryGetNodeFor(item.ParentUUID, out var parent))
            {
                var fetchedParent = await client.Inventory.FetchItemHttpAsync(item.ParentUUID, item.OwnerID, cancellationToken);
                return fetchedParent;
            }

            return parent.Data;
        }

        /// <summary>
        /// Determines if inventory item <paramref name="item"/> is a descendant of inventory folder <paramref name="parentId"/>
        /// </summary>
        /// <param name="item">Item to check</param>
        /// <param name="parentId">ID of the folder to check</param>
        /// <param name="cancellationToken"></param>
        /// <returns>True if <paramref name="item"/> exists as a child, or sub-child of folder <paramref name="parentId"/></returns>
        public async Task<bool> IsObjectDescendentOf(InventoryBase item, UUID parentId, CancellationToken cancellationToken = default)
        {
            const int kArbitraryDepthLimit = 255;

            if (parentId == UUID.Zero)
            {
                return false;
            }

            var parentIter = item;
            for (var i = 0; i < kArbitraryDepthLimit; ++i)
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
        #endregion
    }
}
