/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
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
using OpenMetaverse;

namespace Radegast
{
    public sealed class DeRezObjectAction : ContextAction
    {
        public DeRezObjectAction(RadegastInstance inst)
            : base(inst)
        {
            Label = "DeRez..";
            ContextType = typeof(Primitive);
        }

        public override bool TypeContributes(Type type)
        {
            return type == ContextType;
        }

        public override bool IsEnabled(object target)
        {
            Primitive prim = ToPrimitive(target);
            if (prim != null && prim.OwnerID == Client.Self.AgentID) return true;
            return false;
        }

        public override void OnInvoke(object sender, EventArgs e, object target)
        {
            Primitive thePrim = ToPrimitive(target) ?? ToPrimitive(sender);
            if (thePrim == null)
            {
                DebugLog("Not found prim: " + sender + " " +target);
                return;
            }
            DebugLog("Found prim: " + thePrim);
            Client.Inventory.RequestDeRezToInventory(
                thePrim.LocalID, DeRezDestination.AgentInventoryTake,
                Client.Inventory.FindFolderForType(FolderType.Trash),
                UUID.Random());
        }
    }
}