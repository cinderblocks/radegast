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
using LibreMetaverse;

namespace Radegast.Core.RLV
{
    internal class RlvActionCallbacks : IRlvActionCallbacks
    {
        private readonly IRadegastInstance instance;

        public RlvActionCallbacks(IRadegastInstance instance)
        {
            this.instance = instance;
        }

        public Task AdjustHeightAsync(float distance, float factor, float delta, CancellationToken cancellationToken)
        {
            // No-op
            return Task.CompletedTask;
        }

        public async Task AttachAsync(IReadOnlyList<AttachmentRequest> itemsToAttach, CancellationToken cancellationToken)
        {
            foreach (var item in itemsToAttach)
            {
                if (!instance.Client.Inventory.Store.TryGetValue(new UUID(item.ItemId), out var foundItem))
                {
                    continue;
                }

                if (!(foundItem is InventoryItem inventoryItem))
                {
                    continue;
                }

                if (inventoryItem.InventoryType == InventoryType.Wearable)
                {
                    await instance.COF.AddToOutfit(inventoryItem, item.ReplaceExistingAttachments, cancellationToken);
                }
                else
                {
                    await instance.COF.Attach(inventoryItem, (AttachmentPoint)item.AttachmentPoint, item.ReplaceExistingAttachments, cancellationToken);
                }
            }
        }

        public async Task DetachAsync(IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            var items = await GetInventoryItemsByIdAsync(itemIds).ConfigureAwait(false);
            await instance.COF.RemoveFromOutfit(items, cancellationToken);
        }

        public async Task RemOutfitAsync(IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            var items = await GetInventoryItemsByIdAsync(itemIds).ConfigureAwait(false);
            await instance.COF.RemoveFromOutfit(items, cancellationToken);
        }

        public Task SendInstantMessageAsync(Guid targetUser, string message, CancellationToken cancellationToken)
        {
            instance.Client.Self.InstantMessage(new UUID(targetUser), message);

            return Task.CompletedTask;
        }

        public Task SendReplyAsync(int channel, string message, CancellationToken cancellationToken)
        {
            if (instance.RLV.EnabledDebugCommands)
            {
                instance.ShowNotificationInChat($"[RLV] Send channel {channel}: {message}");
            }

            instance.Client.Self.Chat(message, channel, ChatType.Normal);
            return Task.CompletedTask;
        }

        public Task SetCamFOVAsync(float fovInRadians, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SetDebugAsync(string settingName, string settingValue, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SetEnvAsync(string settingName, string settingValue, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task SetGroupAsync(Guid groupId, string roleName, CancellationToken cancellationToken)
        {
            if (groupId == Guid.Empty)
            {
                return;
            }

            if (!instance.Groups.TryGetValue(new UUID(groupId), out var group))
            {
                return;
            }

            UUID? roleId = null;
            if (!string.IsNullOrEmpty(roleName))
            {
                var (roleIdValid, roleIdTemp) = await TryGetGroupRoleId(group.ID, roleName);
                if (roleIdValid)
                {
                    roleId = roleIdTemp;
                }
            }

            instance.Client.Groups.ActivateGroup(new UUID(groupId));
            if (roleId.HasValue && roleId.Value != UUID.Zero)
            {
                instance.Client.Groups.ActivateTitle(group.ID, roleId.Value);
            }
        }

        public async Task SetGroupAsync(string groupName, string roleName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                return;
            }

            var group = instance.Groups.Values
                .FirstOrDefault(n => string.Equals(n.Name, groupName, StringComparison.InvariantCultureIgnoreCase));

            if (group.ID == UUID.Zero)
            {
                return;
            }

            UUID? roleId = null;
            if (!string.IsNullOrEmpty(roleName))
            {
                var (roleIdValid, roleIdTemp) = await TryGetGroupRoleId(group.ID, roleName);
                if (roleIdValid)
                {
                    roleId = roleIdTemp;
                }
            }

            instance.Client.Groups.ActivateGroup(new UUID(group.ID));
            if (roleId.HasValue && roleId.Value != UUID.Zero)
            {
                instance.Client.Groups.ActivateTitle(group.ID, roleId.Value);
            }
        }

        public Task SetRotAsync(float angleInRadians, CancellationToken cancellationToken)
        {
            // TODO: Why did the original code have pi/2 - angle?
            instance.Client.Self.Movement.UpdateFromHeading((Math.PI / 2d) - angleInRadians, true);
            instance.State.LookInFront();
            return Task.CompletedTask;
        }

        public Task SitAsync(Guid target, CancellationToken cancellationToken)
        {
            instance.State.SetSitting(true, new UUID(target));
            return Task.CompletedTask;
        }

        public Task SitGroundAsync(CancellationToken cancellationToken)
        {
            instance.State.SetSitting(true, UUID.Zero);
            return Task.CompletedTask;
        }

        public Task TpToAsync(float x, float y, float z, string regionName, float? lookat, CancellationToken cancellationToken)
        {
            var vecLookAt = Vector3.UnitY;

            if (lookat.HasValue)
            {
                vecLookAt = Vector3.UnitX;
                vecLookAt *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, lookat.Value);
                vecLookAt.Normalize();
            }

            if (string.IsNullOrEmpty(regionName))
            {
                var regionHandle = Helpers.GlobalPosToRegionHandle(x, y, out var localX, out var localY);
                _ = instance.Client.Self.Teleport(regionHandle, new Vector3(localX, localY, z), vecLookAt);
            }
            else
            {
                _ = instance.Client.Self.Teleport(regionName, new Vector3(x, y, z), vecLookAt);
            }

            return Task.CompletedTask;
        }

        public Task UnsitAsync(CancellationToken cancellationToken)
        {
            instance.State.SetSitting(false, UUID.Zero);
            return Task.CompletedTask;
        }

        private async Task<List<InventoryItem>> GetInventoryItemsByIdAsync(IReadOnlyList<Guid> itemIds)
        {
            var items = new List<InventoryItem>();

            foreach (var itemId in itemIds)
            {
                if (!instance.Client.Inventory.Store.TryGetValue(new UUID(itemId), out var foundItem))
                {
                    using (var cts = new CancellationTokenSource())
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(5));
                        foundItem = await instance.Client.Inventory.FetchItemAsync(new UUID(itemId), instance.Client.Self.AgentID, cts.Token).ConfigureAwait(false);
                        if (foundItem == null)
                        {
                            continue;
                        }
                    }
                }

                if (!(foundItem is InventoryItem item))
                {
                    continue;
                }

                items.Add(item);
            }

            return items;
        }

        private async Task<(bool, UUID)> TryGetGroupRoleId(UUID groupId, string roleName)
        {
            var result = await EventSubscriptionHelper.WaitForEventAsync<GroupRolesDataReplyEventArgs, (bool, UUID)>(
                h => instance.Client.Groups.GroupRoleDataReply += h,
                h => instance.Client.Groups.GroupRoleDataReply -= h,
                e => e.GroupID == groupId,
                e =>
                {
                    if (e.Roles == null) return (false, UUID.Zero);
                    
                    var roleId = e.Roles.Values
                        .Where(n => n.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase))
                        .Select(n => n.ID)
                        .FirstOrDefault();
                    
                    return (roleId != default, roleId);
                },
                10000,
                CancellationToken.None,
                (false, UUID.Zero));

            if (!result.Item1)
            {
                Logger.Error("RLV Commands_SetGroup: Timed out while waiting for GroupRoleDataReply");
            }

            _ = instance.Client.Groups.RequestGroupRoles(groupId);
            return result;
        }
    }
}
