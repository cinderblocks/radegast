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
using System.Threading;

namespace Radegast.Core.RLV
{
    internal class RLVCOFPolicy : ICOFPolicy
    {
        private readonly RlvService _rlvService;
        private readonly RadegastInstance _instance;
        private readonly RlvQueryCallbacks _queryCallbacks;

        public RLVCOFPolicy(LibreMetaverse.RLV.RlvService rlvService, RadegastInstance instance, RlvQueryCallbacks queryCallbacks)
        {
            _rlvService = rlvService;
            _instance = instance;
            _queryCallbacks = queryCallbacks;
        }

        public bool CanAttach(InventoryItem item)
        {
            if (!_instance.RLV.Enabled)
            {
                return true;
            }

            var (hasInventoryMap, inventoryMap) = _queryCallbacks.TryGetInventoryMapAsync(CancellationToken.None).Result;
            if (hasInventoryMap && inventoryMap != null)
            {
                var rlvItems = inventoryMap.GetItemsById(item.UUID.Guid);
                foreach (var rlvItem in rlvItems)
                {
                    if (rlvItem.AttachedTo != null || rlvItem.WornOn != null || rlvItem.GestureState == RlvGestureState.Active)
                    {
                        return false;
                    }

                    if (!_rlvService.Permissions.CanAttach(rlvItem.FolderId, rlvItem.Folder != null, rlvItem.AttachedTo, rlvItem.WornOn))
                    {
                        return false;
                    }
                }

                return true;
            }

            return item is InventoryWearable wearable
                ? _rlvService.Permissions.CanAttach(item.ParentUUID.Guid, false, null, (LibreMetaverse.RLV.RlvWearableType)wearable.WearableType)
                : item is InventoryObject obj && _rlvService.Permissions.CanAttach(item.ParentUUID.Guid, false, (LibreMetaverse.RLV.RlvAttachmentPoint)obj.AttachPoint, null);
        }

        public bool CanDetach(InventoryItem item)
        {
            if (!_instance.RLV.Enabled)
            {
                return true;
            }

            var (hasInventoryMap, inventoryMap) = _queryCallbacks.TryGetInventoryMapAsync(CancellationToken.None).Result;
            if (!hasInventoryMap || inventoryMap == null)
            {
                return false;
            }

            var foundItems = inventoryMap.GetItemsById(item.UUID.Guid);
            foreach (var foundItem in foundItems)
            {
                if (foundItem.WornOn == null && foundItem.AttachedTo == null && foundItem.GestureState != RlvGestureState.Active)
                {
                    return false;
                }

                if (!_rlvService.Permissions.CanDetach(foundItem))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
