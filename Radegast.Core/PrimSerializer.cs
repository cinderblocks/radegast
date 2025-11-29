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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using ClientHelpers = OpenMetaverse.Helpers;

namespace Radegast
{
    public class PrimSerializer
    {
        private List<UUID> Textures = new List<UUID>();
        private UUID SelectedObject = UUID.Zero;

        private readonly Dictionary<UUID, Primitive> PrimsWaiting = new Dictionary<UUID, Primitive>();
        private readonly AutoResetEvent AllPropertiesReceived = new AutoResetEvent(false);

        private readonly GridClient Client;

        public PrimSerializer(GridClient c)
        {
            Client = c;
            Client.Objects.ObjectProperties += Objects_ObjectProperties;
        }

        public void CleanUp()
        {
            Client.Objects.ObjectProperties -= Objects_ObjectProperties;
        }

        public string GetSerializedAttachmentPrims(Simulator sim, uint localID)
        {
            List<Primitive> prims = (from p in sim.ObjectsPrimitives
                where p.Value != null
                where p.Value.LocalID == localID || p.Value.ParentID == localID
                select p.Value).ToList();

            RequestObjectProperties(prims, 500);

            int i = prims.FindIndex(
                prim => (prim.LocalID == localID)
            );

            if (i >= 0) {
                prims[i].ParentID = 0;
            }

            return OSDParser.SerializeLLSDXmlString(ClientHelpers.PrimListToOSD(prims));
        }

        public string GetSerializedPrims(Simulator sim, uint localID)
        {
            sim.ObjectsPrimitives.TryGetValue(localID, out var prim);

            if (prim == null) {
                return string.Empty;
            }

            uint rootPrim = prim.ParentID == 0 ? prim.LocalID : prim.ParentID;

            var prims = (from p in sim.ObjectsPrimitives
                where p.Value != null
                where p.Value.LocalID == rootPrim || p.Value.ParentID == rootPrim
                select p.Value).ToList();

            RequestObjectProperties(prims, 500);

            return OSDParser.SerializeLLSDXmlString(ClientHelpers.PrimListToOSD(prims));
        }

        private bool RequestObjectProperties(IReadOnlyList<Primitive> objects, int msPerRequest)
        {
            // Create an array of the local IDs of all the prims we are requesting properties for
            uint[] localids = new uint[objects.Count];

            lock (PrimsWaiting) {
                PrimsWaiting.Clear();

                for (int i = 0; i < objects.Count; ++i)
                {
                    localids[i] = objects[i].LocalID;
                    PrimsWaiting.Add(objects[i].ID, objects[i]);
                }
            }

            if (localids.Length > 0)
            {
                Client.Objects.SelectObjects(Client.Network.CurrentSim, localids, false);
                bool success = AllPropertiesReceived.WaitOne(2000 + msPerRequest * localids.Length, false);
                if (PrimsWaiting.Count > 0)
                {
                    Logger.Warn($"Failed to retrieve object properties for {PrimsWaiting.Count} prims out of {localids.Length}", Client);

                }
                Client.Objects.DeselectObjects(Client.Network.CurrentSim, localids);
                return success;
            }
            return true;
        }

        private void Objects_ObjectProperties(object sender, ObjectPropertiesEventArgs e)
        {
            lock (PrimsWaiting)
            {
                PrimsWaiting.Remove(e.Properties.ObjectID);

                if (PrimsWaiting.Count == 0)
                    AllPropertiesReceived.Set();
            }
        }
    }
}
