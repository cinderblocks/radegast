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
using OpenMetaverse.StructuredData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast.Core.RLV
{
    public class RLVManager : IDisposable
    {
        public bool Enabled
        {
            get
            {
                if (_instance.GlobalSettings["rlv_enabled"].Type == OSDType.Unknown)
                {
                    _instance.GlobalSettings["rlv_enabled"] = new OSDBoolean(false);
                }

                return _instance.GlobalSettings["rlv_enabled"].AsBoolean();
            }

            set
            {
                if (Enabled != _instance.GlobalSettings["rlv_enabled"].AsBoolean())
                {
                    _instance.GlobalSettings["rlv_enabled"] = new OSDBoolean(value);
                }

                if (value)
                {
                    StartTimer();
                }
                else
                {
                    StopTimer();
                }
            }
        }

        public bool EnabledDebugCommands
        {
            get
            {
                if (_instance.GlobalSettings["rlv_debugcommands"].Type == OSDType.Unknown)
                {
                    _instance.GlobalSettings["rlv_debugcommands"] = new OSDBoolean(false);
                }

                return _instance.GlobalSettings["rlv_debugcommands"].AsBoolean();
            }

            set
            {
                if (EnabledDebugCommands != _instance.GlobalSettings["rlv_debugcommands"].AsBoolean())
                {
                    _instance.GlobalSettings["rlv_debugcommands"] = new OSDBoolean(value);
                }
            }
        }

        private readonly RadegastInstance _instance;
        private System.Timers.Timer CleanupTimer;

        private readonly RlvQueryCallbacks _queryCallbacks;
        private readonly RlvActionCallbacks _actionCallbacks;

        public RlvService RlvService { get; }
        public LibreMetaverse.RLV.RlvPermissionsService Permissions => RlvService.Permissions;

        public RLVManager(RadegastInstance instance)
        {
            _instance = instance;

            _queryCallbacks = new RlvQueryCallbacks(_instance);
            _actionCallbacks = new RlvActionCallbacks(_instance);

            RlvService = new RlvService(_queryCallbacks, _actionCallbacks, Enabled);
            RlvService.Restrictions.RestrictionUpdated += Restrictions_RestrictionUpdated;

            instance.COF.AddPolicy(new RLVCOFPolicy(RlvService, _instance, _queryCallbacks));
            instance.Client.Objects.ObjectUpdate += Objects_AttachmentUpdate;
            instance.Client.Objects.KillObject += Objects_KillObject;

            if (Enabled)
            {
                StartTimer();
            }
        }

        private void Restrictions_RestrictionUpdated(object sender, LibreMetaverse.RLV.EventArguments.RestrictionUpdatedEventArgs e)
        {
            if (EnabledDebugCommands)
            {
                _instance.TabConsole.DisplayNotificationInChat($"[RLV] Restriction Updated: {e.Restriction}");
            }
        }

        #region Item Reporting
        private void Objects_KillObject(object sender, KillObjectEventArgs e)
        {
            if (!Enabled)
            {
                return;
            }

            if (!e.Simulator.ObjectsPrimitives.TryGetValue(e.ObjectLocalID, out var prim))
            {
                return;
            }

            if (_instance.Client.Self.LocalID == 0
                || prim.ParentID != _instance.Client.Self.LocalID
                || prim.NameValues == null
                || !prim.IsAttachment)
            {
                return;
            }

            var attachItemId = prim
                .NameValues
                .Where(n => n.Name == "AttachItemID" && n.Value != null && n.Value is string)
                .Select(n => new UUID(n.Value as string))
                .FirstOrDefault();
            if(attachItemId == null)
            {
                return;
            }

            if (_instance.Client.Inventory.Store.TryGetValue(attachItemId, out InventoryItem item))
            {
                ReportItemChange(item, false).Wait();
            }
        }

        private void Objects_AttachmentUpdate(object sender, PrimEventArgs e)
        {
            if (!Enabled)
            {
                return;
            }

            Primitive prim = e.Prim;

            if (_instance.Client.Self.LocalID == 0
                || prim.ParentID != _instance.Client.Self.LocalID
                || prim.NameValues == null
                || !prim.IsAttachment
                || !e.IsNew)
            {
                return;
            }

            var attachItemId = prim
                .NameValues
                .Where(n => n.Name == "AttachItemID" && n.Value != null && n.Value is string)
                .Select(n => new UUID(n.Value as string))
                .FirstOrDefault();
            if (attachItemId == null)
            {
                return;
            }

            if (_instance.Client.Inventory.Store.TryGetValue(attachItemId, out InventoryItem item))
            {
                ReportItemChange(item, true).Wait();
            }
        }

        private async Task<bool> IsInSharedFolder(InventoryItem item, CancellationToken cancellationToken = default)
        {
            var sharedFolder = _instance.Client.Inventory.Store.RootNode.Nodes.Values
                .FirstOrDefault(n => n.Data.Name == "#RLV" && n.Data is InventoryFolder);

            if (sharedFolder == null)
            {
                return false;
            }

            var realItem = _instance.COF.ResolveInventoryLink(item);
            if (realItem == null)
            {
                return false;
            }

            var isInTrash = await _instance.COF.IsObjectDescendentOf(realItem, sharedFolder.Data.UUID, cancellationToken);
            if (isInTrash)
            {
                return true;
            }

            return false;
        }

        private async Task ReportItemChange(InventoryItem item, bool isAdded, CancellationToken cancellationToken = default)
        {
            var isShared = await IsInSharedFolder(item, cancellationToken);

            if (isAdded)
            {
                if (item is InventoryWearable wearable)
                {
                    await _instance.RLV.RlvService.ReportItemWorn(
                        wearable.ParentUUID.Guid,
                        isShared,
                        (RlvWearableType)wearable.WearableType,
                        cancellationToken
                    );
                }
                else if (item is InventoryAttachment attachment)
                {
                    var (attachedPrimId, attachmentPoint) = GetAttachedPrimId(item);

                    await _instance.RLV.RlvService.ReportItemAttached(
                        attachment.ParentUUID.Guid,
                        isShared,
                        attachmentPoint,
                        cancellationToken
                    );
                }
                else if (item is InventoryObject obj)
                {
                    var (attachedPrimId, attachmentPoint) = GetAttachedPrimId(item);

                    await _instance.RLV.RlvService.ReportItemAttached(
                        obj.ParentUUID.Guid,
                        isShared,
                        attachmentPoint,
                        cancellationToken
                    );
                }
            }
            else
            {
                if (item is InventoryWearable wearable)
                {
                    await _instance.RLV.RlvService.ReportItemUnworn(
                        wearable.ActualUUID.Guid,
                        wearable.ParentUUID.Guid,
                        isShared,
                        (RlvWearableType)wearable.WearableType,
                        cancellationToken
                    );
                }
                else if (item is InventoryAttachment attachment)
                {
                    var (attachedPrimId, attachmentPoint) = GetAttachedPrimId(item);

                    await _instance.RLV.RlvService.ReportItemDetached(
                        attachment.ActualUUID.Guid,
                        attachedPrimId.Guid,
                        attachment.ParentUUID.Guid,
                        isShared,
                        attachmentPoint,
                        cancellationToken
                    );
                }
                else if (item is InventoryObject attachedObj)
                {
                    var (attachedPrimId, attachmentPoint) = GetAttachedPrimId(item);

                    await _instance.RLV.RlvService.ReportItemDetached(
                        attachedObj.ActualUUID.Guid,
                        attachedPrimId.Guid,
                        attachedObj.ParentUUID.Guid,
                        isShared,
                        attachmentPoint,
                        cancellationToken
                    );
                }
            }
        }

        private (UUID, RlvAttachmentPoint) GetAttachedPrimId(InventoryItem attachedItem)
        {
            var objectPrimitivesSnapshot = _instance.Client.Network.CurrentSim.ObjectsPrimitives.Values.ToList();

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

                if (attachmentId == attachedItem.ActualUUID)
                {
                    return (attachmentId, (RlvAttachmentPoint)item.PrimData.AttachmentPoint);
                }
            }

            return (UUID.Zero, RlvAttachmentPoint.Default);
        }
        #endregion

        public void Dispose()
        {
            StopTimer();
        }

        private void StartTimer()
        {
            StopTimer();
            CleanupTimer = new System.Timers.Timer()
            {
                Enabled = true,
                Interval = 120 * 1000 // two minutes
            };

            CleanupTimer.Elapsed += CleanupTimer_Elapsed;
        }

        private void StopTimer()
        {
            if (CleanupTimer != null)
            {
                CleanupTimer.Elapsed -= CleanupTimer_Elapsed;
                CleanupTimer.Enabled = false;
                CleanupTimer.Dispose();
                CleanupTimer = null;
            }
        }

        private void CleanupTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!Enabled)
            {
                return;
            }

            var objects = new List<UUID>();
            var rlvTrackedPrimIds = RlvService.Restrictions.GetTrackedPrimIds();

            var wornItems = _instance.COF.GetCurrentOutfitLinks().Result
                .ToDictionary(k => k.UUID.Guid, v => v);

            var deadPrimIds = new List<Guid>();
            foreach (var primId in rlvTrackedPrimIds)
            {
                var itemExistsInWorld = _instance.Client.Network.CurrentSim.ObjectsPrimitives
                    .Where(n => n.Value.ID.Guid == primId)
                    .Any();
                if (itemExistsInWorld)
                {
                    continue;
                }

                deadPrimIds.Add(primId);
            }

            if (deadPrimIds.Count > 0)
            {
                RlvService.Restrictions.RemoveRestrictionsForObjects(deadPrimIds).Wait();
            }
        }

        public async Task<bool> ProcessCMD(ChatEventArgs e, CancellationToken cancellationToken = default)
        {
            if (!Enabled || !e.Message.StartsWith("@"))
            {
                return false;
            }

            var result = await RlvService.ProcessMessage(e.Message, e.SourceID.Guid, e.FromName, cancellationToken);
            return result;
        }
    }
}
