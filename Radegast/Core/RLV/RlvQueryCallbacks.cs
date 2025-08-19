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
        private readonly RadegastInstance _instance;

        public RlvQueryCallbacks(RadegastInstance instance)
        {
            _instance = instance;
        }

        public Task<bool> IsSittingAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_instance.State.IsSitting);
        }

        public Task<bool> ObjectExistsAsync(Guid objectId, CancellationToken cancellationToken)
        {
            var objectExistsInWorld = _instance.Client.Network.CurrentSim.ObjectsPrimitives
                .Where(n => n.Value.ID.Guid == objectId)
                .Any();

            return Task.FromResult(objectExistsInWorld);
        }

        public async Task<(bool Success, string ActiveGroupName)> TryGetActiveGroupNameAsync(CancellationToken cancellationToken)
        {
            var activeGroupId = _instance.Client.Self.ActiveGroup;

            string groupName = null;

            var tcs = new TaskCompletionSource<bool>();
            void groupNameReply(object sender, GroupNamesEventArgs e)
            {
                if (e.GroupNames.TryGetValue(activeGroupId, out var groupNameTemp))
                {
                    groupName = groupNameTemp;
                }
                tcs.TrySetResult(true);
            }

            try
            {
                _instance.Client.Groups.GroupNamesReply += groupNameReply;
                _instance.Client.Groups.RequestGroupName(activeGroupId);

                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(1000));
                if (completedTask != tcs.Task)
                {
                    Logger.Log("Timed out while waiting for Group Name Reply", Helpers.LogLevel.Error, _instance.Client);
                    return (false, string.Empty);
                }

                return (true, groupName);
            }
            finally
            {
                _instance.Client.Groups.GroupNamesReply -= groupNameReply;
            }
        }

        public Task<(bool Success, CameraSettings CameraSettings)> TryGetCameraSettingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult((false, (CameraSettings)null));
        }

        private Dictionary<UUID, UUID> GetAttachedItemIdToPrimitiveIdMap()
        {
            var attachedItemMap = new Dictionary<UUID, UUID>();
            var objectPrimitivesSnapshot = _instance.Client.Network.CurrentSim.ObjectsPrimitives.Values.ToList();

            foreach (var item in objectPrimitivesSnapshot)
            {
                if(!item.IsAttachment)
                {
                    continue;
                }

                var attachItemID = item.NameValues
                    .Where(n => n.Name == "AttachItemID")
                    .Select(n => n.Value)
                    .FirstOrDefault();

                if(attachItemID == null)
                {
                    continue;
                }

                if(!UUID.TryParse(attachItemID.ToString(), out var attachmentId))
                {
                    continue;
                }

                attachedItemMap[attachmentId] = item.ID;
            }

            return attachedItemMap;
        }

        public async Task<(bool Success, IReadOnlyList<RlvInventoryItem> CurrentOutfit)> TryGetCurrentOutfitAsync(CancellationToken cancellationToken)
        {
            var currentOutfitLinks = await _instance.COF.GetCurrentOutfitLinks(cancellationToken);
            var currentOutfitConverted = new List<RlvInventoryItem>();

            var attachmentIdToInventoryIdMap = GetAttachedItemIdToPrimitiveIdMap();

            foreach (var link in currentOutfitLinks)
            {
                var item = _instance.COF.ResolveInventoryLink(link);
                if (item == null)
                {
                    continue;
                }

                if (item is InventoryWearable wearable)
                {
                    currentOutfitConverted.Add(new RlvInventoryItem(
                        item.UUID.Guid,
                        item.Name,
                        item.ParentUUID.Guid,
                        null,
                        null,
                        (RlvWearableType)wearable.WearableType
                    ));
                }
                else if (item is InventoryAttachment attachment)
                {
                    currentOutfitConverted.Add(new RlvInventoryItem(
                        item.UUID.Guid,
                        item.Name,
                        item.ParentUUID.Guid,
                        (RlvAttachmentPoint)attachment.AttachmentPoint,
                        null,
                        null
                    ));
                }
                else if (item is InventoryObject obj)
                {
                    Guid? primId = null;
                    if(attachmentIdToInventoryIdMap.TryGetValue(item.UUID, out var primIdTemp))
                    {
                        primId = primIdTemp.Guid;
                    }

                    currentOutfitConverted.Add(new RlvInventoryItem(
                        item.UUID.Guid,
                        item.Name,
                        item.ParentUUID.Guid,
                        (RlvAttachmentPoint)obj.AttachPoint,
                        primId,
                        null
                    ));
                }
            }

            return (true, currentOutfitConverted);
        }

        public Task<(bool Success, string DebugSettingValue)> TryGetDebugSettingValueAsync(string settingName, CancellationToken cancellationToken)
        {
            return Task.FromResult((false, string.Empty));
        }

        public Task<(bool Success, string EnvironmentSettingValue)> TryGetEnvironmentSettingValueAsync(string settingName, CancellationToken cancellationToken)
        {
            return Task.FromResult((false, string.Empty));
        }

        private void BuildSharedFolder(Dictionary<Guid, RlvInventoryItem> currentOutfitMap, InventoryNode root, RlvSharedFolder rootConverted)
        {
            foreach (var node in root.Nodes.Values)
            {
                if (node.Data is InventoryFolder)
                {
                    var newChild = rootConverted.AddChild(node.Data.UUID.Guid, node.Data.Name);
                    BuildSharedFolder(currentOutfitMap, node, newChild);
                    continue;
                }

                if (!(node.Data is InventoryItem item))
                {
                    continue;
                }

                if (currentOutfitMap.TryGetValue(item.ActualUUID.Guid, out var currentOutfitItem))
                {
                    rootConverted.AddItem(
                        currentOutfitItem.Id,
                        currentOutfitItem.Name,
                        currentOutfitItem.AttachedTo,
                        currentOutfitItem.AttachedPrimId,
                        currentOutfitItem.WornOn
                    );
                    continue;
                }

                if(item.IsLink())
                {
                    item = _instance.COF.ResolveInventoryLink(item);
                }
                if(item == null)
                {
                    continue;
                }

                if (item.AssetType != AssetType.Bodypart &&
                    item.AssetType != AssetType.Clothing &&
                    item.AssetType != AssetType.Object)
                {
                    continue;
                }

                rootConverted.AddItem(
                    item.UUID.Guid,
                    item.Name,
                    null,
                    null,
                    null
                );
            }
        }
        public async Task<(bool Success, RlvSharedFolder SharedFolder)> TryGetSharedFolderAsync(CancellationToken cancellationToken)
        {
            var (currentOutfitSuccess, currentOutfit) = await TryGetCurrentOutfitAsync(cancellationToken);
            if (!currentOutfitSuccess)
            {
                return (false, null);
            }

            var currentOutfitMap = currentOutfit.ToDictionary(k => k.Id, v => v);

            var sharedFolder = _instance.Client.Inventory.Store.RootNode.Nodes.Values
                .FirstOrDefault(n => n.Data.Name == "#RLV" && n.Data is InventoryFolder);

            var sharedFolderConverted = new RlvSharedFolder(sharedFolder.Data.UUID.Guid, "");

            BuildSharedFolder(currentOutfitMap, sharedFolder, sharedFolderConverted);

            return (true, sharedFolderConverted);
        }

        public Task<(bool Success, Guid SitId)> TryGetSitIdAsync(CancellationToken cancellationToken)
        {
            if (_instance.Client.Self.SittingOn != 0)
            {
                if (_instance.Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(_instance.Client.Self.SittingOn, out var objectWeAreSittingOn))
                {
                    return Task.FromResult((true, objectWeAreSittingOn.ID.Guid));
                }
            }

            return Task.FromResult((false, Guid.Empty));
        }
    }
}
