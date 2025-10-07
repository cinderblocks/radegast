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
    internal class RlvCOFPolicy : ICOFPolicy
    {
        private readonly RlvService rlvService;
        private readonly RadegastInstance instance;
        private readonly RlvQueryCallbacks queryCallbacks;

        public RlvCOFPolicy(LibreMetaverse.RLV.RlvService rlvService, RadegastInstance instance, RlvQueryCallbacks queryCallbacks)
        {
            this.rlvService = rlvService;
            this.instance = instance;
            this.queryCallbacks = queryCallbacks;
        }

        public bool CanAttach(InventoryItem item)
        {
            if (!instance.RLV.Enabled)
            {
                return true;
            }

            var (hasInventoryMap, inventoryMap) = queryCallbacks.TryGetInventoryMapAsync(CancellationToken.None).Result;
            if (hasInventoryMap && inventoryMap != null)
            {
                var rlvItems = inventoryMap.GetItemsById(item.UUID.Guid);
                foreach (var rlvItem in rlvItems)
                {
                    if (rlvItem.AttachedTo != null || rlvItem.WornOn != null || rlvItem.GestureState == RlvGestureState.Active)
                    {
                        return false;
                    }

                    if (!rlvService.Permissions.CanAttach(rlvItem.FolderId, rlvItem.Folder != null, rlvItem.AttachedTo, rlvItem.WornOn))
                    {
                        return false;
                    }
                }

                return true;
            }

            return item is InventoryWearable wearable
                ? rlvService.Permissions.CanAttach(item.ParentUUID.Guid, false, null, (LibreMetaverse.RLV.RlvWearableType)wearable.WearableType)
                : item is InventoryObject obj && rlvService.Permissions.CanAttach(item.ParentUUID.Guid, false, (LibreMetaverse.RLV.RlvAttachmentPoint)obj.AttachPoint, null);
        }

        public bool CanDetach(InventoryItem item)
        {
            if (!instance.RLV.Enabled)
            {
                return true;
            }

            var (hasInventoryMap, inventoryMap) = queryCallbacks.TryGetInventoryMapAsync(CancellationToken.None).Result;
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

                if (!rlvService.Permissions.CanDetach(foundItem))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
