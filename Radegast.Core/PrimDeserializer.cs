/**
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

using System;
using System.Collections.Generic;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using ClientHelpers = OpenMetaverse.Helpers;

namespace Radegast
{
    public class PrimDeserializer
    {
        private enum ImporterState
        {
            RezzingParent,
            RezzingChildren,
            Linking,
            Idle
        }

        private class Linkset
        {
            public Primitive RootPrim;
            public readonly List<Primitive> Children = new List<Primitive>();

            public Linkset()
            {
                RootPrim = new Primitive();
            }

            public Linkset(Primitive rootPrim)
            {
                RootPrim = rootPrim;
            }
        }

        private readonly GridClient Client;
        private Primitive currentPrim;
        private Vector3 currentPosition;
        private readonly AutoResetEvent primDone = new AutoResetEvent(false);
        private List<Primitive> primsCreated;
        private List<uint> linkQueue;
        private uint rootLocalID;
        private ImporterState state = ImporterState.Idle;

        public PrimDeserializer(GridClient c)
        {
            Client = c;
            Client.Objects.ObjectUpdate += Objects_ObjectUpdate;
        }

        public void CleanUp()
        {
            Client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
        }

        public bool CreateObjectFromXml(string xml)
        {
            List<Primitive> prims = ClientHelpers.OSDToPrimList(OSDParser.DeserializeLLSDXml(xml));
            return CreateObject(prims);
        }

        public bool CreateObject(List<Primitive> prims)
        {
            // Build an organized structure from the imported prims
            Dictionary<uint, Linkset> linksets = new Dictionary<uint, Linkset>();
            foreach (var prim in prims)
            {
                if (prim.ParentID == 0)
                {
                    if (linksets.ContainsKey(prim.LocalID))
                        linksets[prim.LocalID].RootPrim = prim;
                    else
                        linksets[prim.LocalID] = new Linkset(prim);
                }
                else
                {
                    if (!linksets.ContainsKey(prim.ParentID))
                        linksets[prim.ParentID] = new Linkset();

                    linksets[prim.ParentID].Children.Add(prim);
                }
            }

            primsCreated = new List<Primitive>();
            Console.WriteLine("Importing " + linksets.Count + " structures.");

            foreach (Linkset linkset in linksets.Values)
            {
                if (linkset.RootPrim.LocalID != 0)
                {
                    state = ImporterState.RezzingParent;
                    currentPrim = linkset.RootPrim;
                    // HACK: Import the structure just above our head
                    // We need a more elaborate solution for importing with relative or absolute offsets
                    linkset.RootPrim.Position = Client.Self.SimPosition;
                    linkset.RootPrim.Position.Z += 3.0f;
                    currentPosition = linkset.RootPrim.Position;

                    // Rez the root prim with no rotation
                    Quaternion rootRotation = linkset.RootPrim.Rotation;
                    linkset.RootPrim.Rotation = Quaternion.Identity;

                    Client.Objects.AddPrim(Client.Network.CurrentSim, linkset.RootPrim.PrimData, Client.Self.ActiveGroup,
                        linkset.RootPrim.Position, linkset.RootPrim.Scale, linkset.RootPrim.Rotation);

                    if (!primDone.WaitOne(25000, false))
                    {
                        throw new Exception("Rez failed, timed out while creating the root prim.");
                    }
                    Client.Objects.SetPosition(Client.Network.CurrentSim, primsCreated[primsCreated.Count - 1].LocalID, currentPosition);

                    state = ImporterState.RezzingChildren;

                    // Rez the child prims
                    foreach (Primitive prim in linkset.Children)
                    {
                        currentPrim = prim;
                        currentPosition = prim.Position + linkset.RootPrim.Position;

                        Client.Objects.AddPrim(Client.Network.CurrentSim, prim.PrimData, UUID.Zero, currentPosition,
                            prim.Scale, prim.Rotation);

                        if (!primDone.WaitOne(25000, false))
                        {
                            throw new Exception("Rez failed, timed out while creating child prim.");
                        }
                        Client.Objects.SetPosition(Client.Network.CurrentSim, primsCreated[primsCreated.Count - 1].LocalID, currentPosition);
                        // Client.Objects.SetRotation(Client.Network.CurrentSim, primsCreated[primsCreated.Count - 1].LocalID, prim.Rotation);
                    }

                    if (linkset.Children.Count != 0)
                    {
                        // Create a list of the local IDs of the newly created prims
                        List<uint> primIDs = new List<uint>(primsCreated.Count) {rootLocalID};
                        // Root prim is first in list.
                        foreach (Primitive prim in primsCreated)
                        {
                            if (prim.LocalID != rootLocalID)
                                primIDs.Add(prim.LocalID);
                        }
                        linkQueue = new List<uint>(primIDs.Count);
                        linkQueue.AddRange(primIDs);

                        // Link and set the permissions + rotation
                        state = ImporterState.Linking;
                        Client.Objects.LinkPrims(Client.Network.CurrentSim, linkQueue);
                        Client.Objects.SetRotation(Client.Network.CurrentSim, rootLocalID, rootRotation);

                        if (!primDone.WaitOne(5000, false))
                        {
                            Logger.Warn($"Warning: Failed to link {linkQueue.Count} prims");
                        }

                        Client.Objects.SetPermissions(Client.Network.CurrentSim, primIDs,
                            PermissionWho.NextOwner,
                            PermissionMask.All, true);
                    }
                    else
                    {
                        List<uint> primsForPerms = new List<uint> {rootLocalID};
                        Client.Objects.SetRotation(Client.Network.CurrentSim, rootLocalID, rootRotation);
                        Client.Objects.SetPermissions(Client.Network.CurrentSim, primsForPerms,
                            PermissionWho.NextOwner,
                            PermissionMask.All, true);
                    }
                    state = ImporterState.Idle;
                }
                else
                {
                    // Skip linksets with a missing root prim
                    Logger.Warn($"Skipping {linkset} linkset with a missing root prim");
                }

                // Reset everything for the next linkset
                primsCreated.Clear();
            }

            return true;
        }

        private void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if ((e.Prim.Flags & PrimFlags.CreateSelected) == 0)
                return; // We received an update for an object we didn't create

            switch (state)
            {
                case ImporterState.RezzingParent:
                    rootLocalID = e.Prim.LocalID;
                    goto case ImporterState.RezzingChildren;
                case ImporterState.RezzingChildren:
                    if (!primsCreated.Contains(e.Prim))
                    {
                        Console.WriteLine("Setting properties for " + e.Prim.LocalID);
                        // TODO: Is there a way to set all of this at once, and update more ObjectProperties stuff?
                        Client.Objects.SetPosition(e.Simulator, e.Prim.LocalID, currentPosition);
                        Client.Objects.SetTextures(e.Simulator, e.Prim.LocalID, currentPrim.Textures);

                        if (currentPrim.Light != null && currentPrim.Light.Intensity > 0)
                        {
                            Client.Objects.SetLight(e.Simulator, e.Prim.LocalID, currentPrim.Light);
                        }

                        if (currentPrim.Flexible != null)
                        {
                            Client.Objects.SetFlexible(e.Simulator, e.Prim.LocalID, currentPrim.Flexible);
                        }

                        if (currentPrim.Sculpt != null && currentPrim.Sculpt.SculptTexture != UUID.Zero)
                        {
                            Client.Objects.SetSculpt(e.Simulator, e.Prim.LocalID, currentPrim.Sculpt);
                        }

                        if (currentPrim.Properties != null && !string.IsNullOrEmpty(currentPrim.Properties.Name))
                        {
                            Client.Objects.SetName(e.Simulator, e.Prim.LocalID, currentPrim.Properties.Name);
                        }

                        if (currentPrim.Properties != null && !string.IsNullOrEmpty(currentPrim.Properties.Description))
                        {
                            Client.Objects.SetDescription(e.Simulator, e.Prim.LocalID, currentPrim.Properties.Description);
                        }

                        primsCreated.Add(e.Prim);
                        primDone.Set();
                    }
                    break;
                case ImporterState.Linking:
                    lock (linkQueue)
                    {
                        int index = linkQueue.IndexOf(e.Prim.LocalID);
                        if (index != -1)
                        {
                            linkQueue.RemoveAt(index);
                            if (linkQueue.Count == 0)
                                primDone.Set();
                        }
                    }
                    break;
            }
        }
    }
}
