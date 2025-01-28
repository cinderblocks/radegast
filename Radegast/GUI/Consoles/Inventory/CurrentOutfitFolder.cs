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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;

namespace Radegast
{
    public class CurrentOutfitFolder : IDisposable
    {
        #region Fields

        private GridClient Client;
        private readonly RadegastInstance Instance;
        private bool InitializedCOF = false;
        public InventoryFolder COF;

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
            client.Inventory.ItemReceived += Inventory_ItemReceived;
            client.Appearance.AppearanceSet += Appearance_AppearanceSet;
            client.Objects.KillObject += Objects_KillObject;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Network.SimChanged -= Network_OnSimChanged;
            client.Inventory.FolderUpdated -= Inventory_FolderUpdated;
            client.Inventory.ItemReceived -= Inventory_ItemReceived;
            client.Appearance.AppearanceSet -= Appearance_AppearanceSet;
            client.Objects.KillObject -= Objects_KillObject;
            InitializedCOF = false;
        }

        private void Appearance_AppearanceSet(object sender, AppearanceSetEventArgs e)
        {

        }

        private void Inventory_ItemReceived(object sender, ItemReceivedEventArgs e)
        {

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
                    foreach (var link in ContentLinks().Where(link => !items.ContainsKey(link.AssetUUID)))
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
                    RemoveLink(invItem);
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
                InitializeCurrentOutfitFolder();
                InitializedCOF = true;
            }
        }

        #endregion Event handling

        #region Private methods

        private void InitializeCurrentOutfitFolder()
        {
            COF = Client.Appearance.GetCurrentOutfitFolder(CancellationToken.None).Result;

            if (COF == null)
            {
                CreateCurrentOutfitFolder();
            }
            else
            {
                Task task = Client.Inventory.RequestFolderContents(COF.UUID, Client.Self.AgentID,
                    true, true, InventorySortOrder.ByDate);
            }
            Logger.Log($"Initialized Current Outfit Folder with UUID {COF.UUID}", Helpers.LogLevel.Info, Client);
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
        /// Return contents of COF
        /// </summary>
        /// <returns>List if InventoryItems that can be part of appearance (attachments, wearables)</returns>
        private List<InventoryItem> ContentLinks()
        {
            var ret = new List<InventoryItem>();
            if (COF != null)
            {
                Client.Inventory.Store.GetContents(COF)
                    .FindAll(b => CanBeWorn(b) && ((InventoryItem)b).AssetType == AssetType.Link)
                    .ForEach(item => ret.Add((InventoryItem)item));
            }
            return ret;
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
        public static bool IsAttached(List<Primitive> attachments, InventoryItem item)
        {
            return attachments.Any(prim => GetAttachmentItem(prim) == item.UUID);
        }

        /// <summary>
        /// Checks if inventory item of Wearable type is worn
        /// </summary>
        /// <param name="currentlyWorn">Current outfit</param>
        /// <param name="item">Item to check</param>
        /// <returns>True if the item is worn</returns>
        public static bool IsWorn(List<AppearanceManager.WearableData> currentlyWorn, InventoryItem item)
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
        public void Attach(InventoryItem item, AttachmentPoint point, bool replace)
        {
            Client.Appearance.Attach(item, point, replace);
            AddLink(item);
        }

        /// <summary>
        /// Creates a new COF link
        /// </summary>
        /// <param name="item">Original item to be linked from COF</param>
        public void AddLink(InventoryItem item)
        {
            if (item.InventoryType == InventoryType.Wearable && !IsBodyPart(item))
            {
                var w = (InventoryWearable)item;
                int layer = 0;
                string desc = $"@{(int) w.WearableType}{layer:00}";
                AddLink(item, desc);
            }
            else
            {
                AddLink(item, string.Empty);
            }
        }

        /// <summary>
        /// Creates a new COF link
        /// </summary>
        /// <param name="item">Original item to be linked from COF</param>
        /// <param name="newDescription">Description for the link</param>
        public void AddLink(InventoryItem item, string newDescription)
        {
            if (COF == null)
            {
                Logger.Log("Can't add link; COF hasn't been initialized.", Helpers.LogLevel.Warning, Client);
                return;
            }

            if (ContentLinks().Find(itemLink => itemLink.AssetUUID == item.UUID) == null)
            {
                Client.Inventory.CreateLink(COF.UUID, item.UUID, item.Name, newDescription, 
                    item.InventoryType, UUID.Random(), 
                    (success, newItem) =>
                {
                    if (success)
                    {
                        Client.Inventory.RequestFetchInventory(newItem.UUID, newItem.OwnerID);
                    }
                });
            }
        }

        /// <summary>
        /// Remove a link to specified inventory item
        /// </summary>
        /// <param name="itemID">ID of the target inventory item for which we want link to be removed</param>
        public void RemoveLink(UUID itemID)
        {
            RemoveLink(new List<UUID>(1) { itemID });
        }

        /// <summary>
        /// Remove a link to specified inventory item
        /// </summary>
        /// <param name="itemIDs">List of IDs of the target inventory item for which we want link to be removed</param>
        public void RemoveLink(List<UUID> itemIDs)
        {
            if (COF == null)
            {
                Logger.Log("Can't remove link; COF hasn't been initialized.", Helpers.LogLevel.Warning, Client);
                return;
            }

            foreach (var links in itemIDs.Select(itemID => ContentLinks()
                         .FindAll(itemLink => itemLink.AssetUUID == itemID)))
            {
                links.ForEach(item => Client.Inventory.RemoveItem(item.UUID));
            }
        }

        /// <summary>
        /// Remove attachment
        /// </summary>
        /// <param name="item">>Inventory item to be detached</param>
        public void Detach(InventoryItem item)
        {
            var realItem = OriginalInventoryItem(item);
            if (!Instance.RLV.AllowDetach(realItem)) { return; }

            Client.Appearance.Detach(item);
            RemoveLink(item.UUID);
        }

        public List<InventoryItem> GetWornAt(WearableType type)
        {
            var ret = new List<InventoryItem>();
            ContentLinks().ForEach(link =>
            {
                var item = OriginalInventoryItem(link);
                if (!(item is InventoryWearable wearable)) { return; }

                if (wearable.WearableType == type)
                {
                    ret.Add(wearable);
                }
            });

            return ret;
        }

        /// <summary>
        /// Resolves inventory links and returns a real inventory item that
        /// the link is pointing to
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public InventoryItem OriginalInventoryItem(InventoryItem item)
        {
            if (item.IsLink() && Client.Inventory.Store.Contains(item.AssetUUID)
                              && Client.Inventory.Store[item.AssetUUID] is InventoryItem invItem)
            {
                return invItem;
            }

            return item;
        }

        /// <summary>
        /// Replaces the current outfit and updates COF links accordingly
        /// </summary>
        /// <param name="newOutfit">List of new wearables and attachments that comprise the new outfit</param>
        public void ReplaceOutfit(List<InventoryItem> newOutfit)
        {
            // Resolve inventory links
            var outfit = newOutfit.Select(OriginalInventoryItem).ToList();

            // Remove links to all exiting items
            var toRemove = new List<UUID>();
            ContentLinks().ForEach(item =>
            {
                if (IsBodyPart(item))
                {
                    WearableType linkType = ((InventoryWearable)OriginalInventoryItem(item)).WearableType;
                    bool hasBodyPart = newOutfit.Select(OriginalInventoryItem).Where(IsBodyPart).Any(newItem =>
                        ((InventoryWearable) newItem).WearableType == linkType);

                    if (hasBodyPart)
                    {
                        toRemove.Add(item.UUID);
                    }
                }
                else
                {
                    toRemove.Add(item.UUID);
                }
            });

            foreach (var item in toRemove)
            {
                Client.Inventory.RemoveItem(item);
            }

            // Add links to new items
            var newItems = outfit.FindAll(CanBeWorn);
            foreach (var item in newItems)
            {
                AddLink(item);
            }

            Client.Appearance.ReplaceOutfit(outfit, false);
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Thread.Sleep(2000);
                Client.Appearance.RequestSetAppearance(true);
            });
        }

        /// <summary>
        /// Add items to current outfit
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="replace">Should existing wearable of the same type be removed</param>
        public void AddToOutfit(InventoryItem item, bool replace)
        {
            AddToOutfit(new List<InventoryItem>(1) { item }, replace);
        }

        /// <summary>
        /// Add items to current outfit
        /// </summary>
        /// <param name="items">List of items to add</param>
        /// <param name="replace">Should existing wearable of the same type be removed</param>
        public void AddToOutfit(List<InventoryItem> items, bool replace)
        {
            var current = ContentLinks();
            var toRemove = new List<UUID>();

            // Resolve inventory links and remove wearables of the same type from COF
            var outfit = new List<InventoryItem>();

            foreach (var item in items)
            {
                var realItem = OriginalInventoryItem(item);
                if (replace && realItem is InventoryWearable wearable)
                {
                    foreach (var link in current)
                    {
                        var currentItem = OriginalInventoryItem(link);
                        if (link.AssetUUID == item.UUID)
                        {
                            toRemove.Add(link.UUID);
                        }
                        else
                        {
                            var w = currentItem as InventoryWearable;
                            if (w?.WearableType == wearable.WearableType)
                            {
                                toRemove.Add(link.UUID);
                            }
                        }
                    }
                }

                outfit.Add(realItem);
            }

            foreach (var item in toRemove)
            {
                Client.Inventory.RemoveItem(item);
            }

            // Add links to new items
            var newItems = outfit.FindAll(CanBeWorn);
            foreach (var item in newItems)
            {
                AddLink(item);
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
        public void RemoveFromOutfit(InventoryItem item)
        {
            RemoveFromOutfit(new List<InventoryItem>(1) { item });
        }

        /// <summary>
        /// Remove specified items from the current outfit
        /// </summary>
        /// <param name="items">List of items to remove</param>
        public void RemoveFromOutfit(List<InventoryItem> items)
        {
            // Resolve inventory links
            var outfit = items.Select(OriginalInventoryItem).Where(realItem => Instance.RLV.AllowDetach(realItem)).ToList();

            // Remove links to all items that were removed
            var toRemove = outfit.FindAll(item => CanBeWorn(item) && !IsBodyPart(item)).Select(item => item.UUID).ToList();
            RemoveLink(toRemove);

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
