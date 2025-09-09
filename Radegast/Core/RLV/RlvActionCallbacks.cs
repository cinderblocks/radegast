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
    internal class RlvActionCallbacks : IRlvActionCallbacks
    {
        private readonly RadegastInstance _instance;

        public RlvActionCallbacks(RadegastInstance instance)
        {
            _instance = instance;
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
                if (!_instance.Client.Inventory.Store.TryGetValue(new UUID(item.ItemId), out var foundItem))
                {
                    continue;
                }

                if (!(foundItem is InventoryItem inventoryItem))
                {
                    continue;
                }

                if (inventoryItem.InventoryType == InventoryType.Wearable)
                {
                    await _instance.COF.AddToOutfit(inventoryItem, item.ReplaceExistingAttachments, cancellationToken);
                }
                else
                {
                    await _instance.COF.Attach(inventoryItem, (AttachmentPoint)item.AttachmentPoint, item.ReplaceExistingAttachments, cancellationToken);
                }
            }
        }

        public async Task DetachAsync(IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            var items = GetInventoryItemsById(itemIds);
            await _instance.COF.RemoveFromOutfit(items, cancellationToken);
        }

        public async Task RemOutfitAsync(IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            var items = GetInventoryItemsById(itemIds);
            await _instance.COF.RemoveFromOutfit(items);
        }

        public Task SendInstantMessageAsync(Guid targetUser, string message, CancellationToken cancellationToken)
        {
            _instance.Client.Self.InstantMessage(new UUID(targetUser), message);

            return Task.CompletedTask;
        }

        public Task SendReplyAsync(int channel, string message, CancellationToken cancellationToken)
        {
            if (_instance.RLV.EnabledDebugCommands)
            {
                _instance.TabConsole.DisplayNotificationInChat($"[RLV] Send channel {channel}: {message}");
            }

            _instance.Client.Self.Chat(message, channel, ChatType.Normal);
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
            if (groupId == null || groupId == Guid.Empty)
            {
                return;
            }

            if (!_instance.Groups.TryGetValue(new UUID(groupId), out var group))
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

            _instance.Client.Groups.ActivateGroup(new UUID(groupId));
            if (roleId.HasValue && roleId.Value != UUID.Zero)
            {
                _instance.Client.Groups.ActivateTitle(group.ID, roleId.Value);
            }
        }

        public async Task SetGroupAsync(string groupName, string roleName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                return;
            }

            var group = _instance.Groups.Values
                .Where(n => string.Equals(n.Name, groupName, StringComparison.InvariantCultureIgnoreCase))
                .FirstOrDefault();

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

            _instance.Client.Groups.ActivateGroup(new UUID(group.ID));
            if (roleId.HasValue && roleId.Value != UUID.Zero)
            {
                _instance.Client.Groups.ActivateTitle(group.ID, roleId.Value);
            }
        }

        public Task SetRotAsync(float angleInRadians, CancellationToken cancellationToken)
        {
            // TODO: Why did the original code have pi/2 - angle?
            _instance.Client.Self.Movement.UpdateFromHeading((Math.PI / 2d) - angleInRadians, true);
            _instance.State.LookInFront();
            return Task.CompletedTask;
        }

        public Task SitAsync(Guid target, CancellationToken cancellationToken)
        {
            _instance.State.SetSitting(true, new UUID(target));
            return Task.CompletedTask;
        }

        public Task SitGroundAsync(CancellationToken cancellationToken)
        {
            _instance.State.SetSitting(true, UUID.Zero);
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
                _ = _instance.Client.Self.Teleport(regionHandle, new Vector3(localX, localY, z), vecLookAt);
            }
            else
            {
                _ = _instance.Client.Self.Teleport(regionName, new Vector3(x, y, z), vecLookAt);
            }

            return Task.CompletedTask;
        }

        public Task UnsitAsync(CancellationToken cancellationToken)
        {
            _instance.State.SetSitting(false, UUID.Zero);
            return Task.CompletedTask;
        }

        private List<InventoryItem> GetInventoryItemsById(IReadOnlyList<Guid> itemIds)
        {
            var items = new List<InventoryItem>();

            foreach (var itemId in itemIds)
            {
                if (!_instance.Client.Inventory.Store.TryGetValue(new UUID(itemId), out var foundItem))
                {
                    foundItem = _instance.Client.Inventory.FetchItem(new UUID(itemId), _instance.Client.Self.AgentID, TimeSpan.FromSeconds(5));
                    if (foundItem == null)
                    {
                        continue;
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
            var tcs = new TaskCompletionSource<bool>();

            Dictionary<UUID, GroupRole> groupRoles = null;
            void OnGroupRoleDataReply(object _, GroupRolesDataReplyEventArgs roleArgs)
            {
                if (roleArgs.GroupID != groupId)
                {
                    return;
                }

                groupRoles = roleArgs.Roles;
                tcs.SetResult(true);
            }

            try
            {
                _instance.Client.Groups.GroupRoleDataReply += OnGroupRoleDataReply;
                var unused = _instance.Client.Groups.RequestGroupRoles(groupId);

                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));
                if (completedTask != tcs.Task)
                {
                    Logger.Log("RLV Commands_SetGroup: Timed out while waiting for GroupRoleDataReply", Helpers.LogLevel.Error);
                    return (false, UUID.Zero);
                }
            }
            finally
            {
                _instance.Client.Groups.GroupRoleDataReply -= OnGroupRoleDataReply;
            }

            if (groupRoles == null)
            {
                return (false, UUID.Zero);
            }

            var roleId = groupRoles.Values
                .Where(n => n.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase))
                .Select(n => n.ID)
                .FirstOrDefault();

            return (roleId != default, roleId);
        }
    }
}
