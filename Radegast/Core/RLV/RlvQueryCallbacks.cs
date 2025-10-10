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
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast.Core.RLV
{
    internal class RlvQueryCallbacks : IRlvQueryCallbacks
    {
        private readonly RadegastInstance instance;

        public RlvQueryCallbacks(RadegastInstance instance)
        {
            this.instance = instance;
        }

        public Task<bool> IsSittingAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(instance.State.IsSitting);
        }

        public Task<bool> ObjectExistsAsync(Guid objectId, CancellationToken cancellationToken)
        {
            var objectExistsInWorld = instance.Client.Network.CurrentSim.ObjectsPrimitives
                .Where(n => n.Value.ID.Guid == objectId)
                .Any();

            return Task.FromResult(objectExistsInWorld);
        }

        public async Task<(bool Success, string ActiveGroupName)> TryGetActiveGroupNameAsync(CancellationToken cancellationToken)
        {
            var activeGroupId = instance.Client.Self.ActiveGroup;

            string groupName = null;

            var tcs = new TaskCompletionSource<bool>();
            void groupNameReply(object sender, GroupNamesEventArgs e)
            {
                if (e.GroupNames.TryGetValue(activeGroupId, out var groupNameTemp))
                {
                    groupName = groupNameTemp;
                }
                var unused = tcs.TrySetResult(true);
            }

            try
            {
                instance.Client.Groups.GroupNamesReply += groupNameReply;
                instance.Client.Groups.RequestGroupName(activeGroupId);

                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(1000, cancellationToken));
                if (completedTask != tcs.Task)
                {
                    Logger.Log("Timed out while waiting for Group Name Reply", Helpers.LogLevel.Error, instance.Client);
                    return (false, string.Empty);
                }

                return (true, groupName);
            }
            finally
            {
                instance.Client.Groups.GroupNamesReply -= groupNameReply;
            }
        }

        public Task<(bool Success, CameraSettings CameraSettings)> TryGetCameraSettingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult((false, (CameraSettings)null));
        }

        private Dictionary<UUID, UUID> GetAttachedItemIdToPrimitiveIdMap()
        {
            var attachedItemMap = new Dictionary<UUID, UUID>();
            var objectPrimitivesSnapshot = instance.Client.Network.CurrentSim.ObjectsPrimitives.Values.ToList();

            foreach (var item in objectPrimitivesSnapshot)
            {
                if (!item.IsAttachment)
                {
                    continue;
                }

                var attachItemID = item.NameValues
                    .Where(n => n.Name == "AttachItemID")
                    .Select(n => n.Value)
                    .FirstOrDefault();

                if (attachItemID == null)
                {
                    continue;
                }

                if (!UUID.TryParse(attachItemID.ToString(), out var attachmentId))
                {
                    continue;
                }

                attachedItemMap[attachmentId] = item.ID;
            }

            return attachedItemMap;
        }

        public Task<(bool Success, string DebugSettingValue)> TryGetDebugSettingValueAsync(string settingName, CancellationToken cancellationToken)
        {
            return Task.FromResult((false, string.Empty));
        }

        public Task<(bool Success, string EnvironmentSettingValue)> TryGetEnvironmentSettingValueAsync(string settingName, CancellationToken cancellationToken)
        {
            return Task.FromResult((false, string.Empty));
        }

        public Task<(bool Success, Guid SitId)> TryGetSitIdAsync(CancellationToken cancellationToken)
        {
            if (instance.Client.Self.SittingOn != 0)
            {
                if (instance.Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(instance.Client.Self.SittingOn, out var objectWeAreSittingOn))
                {
                    return Task.FromResult((true, objectWeAreSittingOn.ID.Guid));
                }
            }

            return Task.FromResult((false, Guid.Empty));
        }

        private void GetItemAttachmentInfo(
            InventoryItem item,
            Dictionary<UUID, UUID> attachmentIdToInventoryIdMap,
            out RlvWearableType? wornOn,
            out RlvAttachmentPoint? attachedTo,
            out Guid? attachedPrimId,
            out RlvGestureState? gestureState)
        {
            wornOn = null;
            attachedTo = null;
            attachedPrimId = null;
            gestureState = null;
            if (item is InventoryWearable wearable)
            {
                wornOn = (RlvWearableType)wearable.WearableType;
            }
            else if (item is InventoryAttachment attachment)
            {
                if (attachmentIdToInventoryIdMap.TryGetValue(item.ActualUUID, out var primIdTemp))
                {
                    attachedPrimId = primIdTemp.Guid;
                }

                attachedTo = (RlvAttachmentPoint)attachment.AttachmentPoint;
            }
            else if (item is InventoryObject obj)
            {
                if (attachmentIdToInventoryIdMap.TryGetValue(item.ActualUUID, out var primIdTemp))
                {
                    attachedPrimId = primIdTemp.Guid;
                }

                attachedTo = (RlvAttachmentPoint)obj.AttachPoint;
            }
            else if (item is InventoryGesture)
            {
                gestureState = instance.Client.Self.ActiveGestures.ContainsKey(item.UUID) ? RlvGestureState.Active : RlvGestureState.Inactive;
            }
        }

        private void BuildSharedFolder(
            Dictionary<UUID, InventoryItem> currentOutfitMap,
            Dictionary<UUID, UUID> attachmentIdToInventoryIdMap,
            InventoryNode root,
            RlvSharedFolder rootConverted,
            Dictionary<Guid, RlvSharedFolder> folderMap,
            Dictionary<Guid, RlvInventoryItem> itemMap
        )
        {
            folderMap[root.Data.UUID.Guid] = rootConverted;

            foreach (var node in root.Nodes.Values)
            {
                if (node.Data is InventoryFolder)
                {
                    var newChild = rootConverted.AddChild(node.Data.UUID.Guid, node.Data.Name);
                    BuildSharedFolder(currentOutfitMap, attachmentIdToInventoryIdMap, node, newChild, folderMap, itemMap);
                    continue;
                }

                if (!(node.Data is InventoryItem item))
                {
                    continue;
                }

                var realItem = instance.COF.ResolveInventoryLink(item);
                if (realItem == null)
                {
                    continue;
                }

                if (realItem.AssetType != AssetType.Bodypart &&
                    realItem.AssetType != AssetType.Clothing &&
                    realItem.AssetType != AssetType.Gesture &&
                    realItem.AssetType != AssetType.Object)
                {
                    continue;
                }

                if (currentOutfitMap.ContainsKey(item.ActualUUID))
                {
                    // Note: Inventory item link and the real item will report different wearable type. Only use RealItem for this
                    GetItemAttachmentInfo(realItem, attachmentIdToInventoryIdMap, out var wornOn, out var attachedTo, out var attachedPrimId, out var isActiveGesture);

                    var newItem = rootConverted.AddItem(
                        item.ActualUUID.Guid,
                        item.Name,
                        item.IsLink(),
                        attachedTo,
                        attachedPrimId,
                        wornOn,
                        isActiveGesture
                     );
                    itemMap[newItem.Id] = newItem;
                }
                else
                {
                    var newItem = rootConverted.AddItem(
                        item.ActualUUID.Guid,
                        item.Name,
                        item.IsLink(),
                        null,
                        null,
                        null,
                        realItem.AssetType == AssetType.Gesture ? RlvGestureState.Inactive : (RlvGestureState?)null
                    );
                    itemMap[newItem.Id] = newItem;
                }
            }
        }

        public async Task<(bool Success, InventoryMap InventoryMap)> TryGetInventoryMapAsync(CancellationToken cancellationToken)
        {
            // Get current attached items <InventoryItem>
            var currentOutfitLinks = await instance.COF.GetCurrentOutfitLinks(cancellationToken);
            var attachmentIdToInventoryIdMap = GetAttachedItemIdToPrimitiveIdMap();

            // Build shared folder
            var sharedFolder = instance.Client.Inventory.Store.RootNode.Nodes.Values
                .FirstOrDefault(n => n.Data.Name == "#RLV" && n.Data is InventoryFolder);

            var currentOutfitMap = currentOutfitLinks.ToDictionary(k => k.ActualUUID, v => v);

            var sharedFolderConverted = new RlvSharedFolder(sharedFolder.Data.UUID.Guid, "");

            var itemMap = new Dictionary<Guid, RlvInventoryItem>();
            var folderMap = new Dictionary<Guid, RlvSharedFolder>();

            BuildSharedFolder(currentOutfitMap, attachmentIdToInventoryIdMap, sharedFolder, sharedFolderConverted, folderMap, itemMap);

            // Gather external attached items
            var externalItems = new List<RlvInventoryItem>();

            foreach (var item in currentOutfitLinks)
            {
                if (itemMap.ContainsKey(item.ActualUUID.Guid))
                {
                    continue;
                }

                var realItem = instance.COF.ResolveInventoryLink(item);
                if (realItem == null)
                {
                    continue;
                }

                // Note: Inventory item link and the real item will report different wearable type. Only use RealItem for this
                GetItemAttachmentInfo(realItem, attachmentIdToInventoryIdMap, out var wornOn, out var attachedTo, out var attachedPrimId, out var gestureState);
                var newItem = new RlvInventoryItem(
                    item.ActualUUID.Guid,
                    item.Name,
                    item.IsLink(),
                    item.ParentUUID.Guid,
                    attachedTo,
                    attachedPrimId,
                    wornOn,
                    gestureState
                 );

                itemMap.Add(item.ActualUUID.Guid, newItem);
                externalItems.Add(newItem);
            }

            var inventoryContext = new InventoryMap(sharedFolderConverted, externalItems);
            return (true, inventoryContext);
        }
    }
}
