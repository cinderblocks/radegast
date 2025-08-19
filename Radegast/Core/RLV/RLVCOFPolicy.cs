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
using System.Linq;
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
            if(!_rlvService.Enabled)
            {
                return true;
            }

            var (hasSharedFolder, sharedFolder) = _queryCallbacks.TryGetSharedFolderAsync(CancellationToken.None).Result;
            if (hasSharedFolder && sharedFolder != null)
            {
                var inventoryMap = new InventoryMap(sharedFolder);

                if (inventoryMap.Items.TryGetValue(item.UUID.Guid, out var rlvItem))
                {
                    if (rlvItem.AttachedTo != null || rlvItem.WornOn != null)
                    {
                        return false;
                    }

                    // TODO: Return false if we already have it attached/worn?
                    return _rlvService.Permissions.CanAttach(rlvItem.FolderId, rlvItem.Folder != null, rlvItem.AttachedTo, rlvItem.WornOn);
                }
            }

            return item is InventoryWearable wearable
                ? _rlvService.Permissions.CanAttach(item.ParentUUID.Guid, false, null, (LibreMetaverse.RLV.RlvWearableType)wearable.WearableType)
                : item is InventoryObject obj && _rlvService.Permissions.CanAttach(item.ParentUUID.Guid, false, (LibreMetaverse.RLV.RlvAttachmentPoint)obj.AttachPoint, null);
        }

        public bool CanDetach(InventoryItem item)
        {
            if (!_rlvService.Enabled)
            {
                return true;
            }

            var (hasCurrentOutfit, currentOutfit) = _queryCallbacks.TryGetCurrentOutfitAsync(CancellationToken.None).Result;
            if (hasCurrentOutfit && currentOutfit != null)
            {
                var foundItem = currentOutfit.FirstOrDefault(n => n.Id == item.UUID.Guid);
                if (foundItem != null)
                {
                    return _rlvService.Permissions.CanDetach(foundItem, foundItem.Folder != null);
                }
            }

            return false;
        }
    }
}
