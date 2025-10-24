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
using System.IO;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Radegast
{
	/// <summary>
	/// Description of PrimImporter.
	/// </summary>
	public class PrimImporter
	{
		private enum ImporterState
		{
			RezzingParent,
			RezzingChildren,
			Linking,
			Idle
		}
		
		public enum TextureSet
		{
			OriginalUUID,
			NewUUID,
			SculptUUID,
			WhiteUUID
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

        private Primitive currentPrim;
        private Vector3 currentPosition;
        private readonly AutoResetEvent primDone = new AutoResetEvent(false);
        private List<Primitive> primsCreated;
        private List<uint> linkQueue;
        private uint rootLocalID;
        private ImporterState state = ImporterState.Idle;
        private readonly GridClient Client;

        private string ImportDirectory;
		
		public delegate void LogMessageDelegate(string format, params object[] args);
		public Dictionary<UUID,UUID> Textures;
		public Dictionary<UUID,UUID> SculptTextures;
		public Vector3 RezAt;
		public TextureSet TextureUse = TextureSet.OriginalUUID;
		public LogMessageDelegate LogMessage;
		
		public PrimImporter(GridClient client)
		{
			Client = client;
			Textures = new Dictionary<UUID, UUID>();
			Client.Objects.ObjectUpdate += Objects_OnNewPrim;
		}
		
		public void CleanUp()
		{
			Client.Objects.ObjectUpdate -= Objects_OnNewPrim;
		}
		
		public void ImportFromFile(string filename)
		{
			ImportDirectory = Path.GetDirectoryName(filename);
            var xml = File.ReadAllText(filename);
			var prims = Helpers.OSDToPrimList(OSDParser.DeserializeLLSDXml(xml));
			
			Dictionary<uint, Linkset> linksets = new Dictionary<uint, Linkset>();
			foreach (Primitive prim in prims)
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
			
			foreach (Linkset linkset in linksets.Values)
			{
				if (linkset.RootPrim.LocalID != 0)
				{
					state = ImporterState.RezzingParent;
					currentPrim = linkset.RootPrim;
					linkset.RootPrim.Position = RezAt;
					currentPosition = RezAt;
					
					// Rez the root prim with no rotation
					Quaternion rootRotation = linkset.RootPrim.Rotation;
					linkset.RootPrim.Rotation = Quaternion.Identity;
					
					Client.Objects.AddPrim(Client.Network.CurrentSim, linkset.RootPrim.PrimData, UUID.Zero,
						linkset.RootPrim.Position, linkset.RootPrim.Scale, linkset.RootPrim.Rotation);
					
					if (!primDone.WaitOne(10000, false))
						throw new Exception("Rez failed, timed out while creating the root prim.");
					
					Client.Objects.SetPosition(Client.Network.CurrentSim, primsCreated[primsCreated.Count - 1].LocalID, linkset.RootPrim.Position);
					
					state = ImporterState.RezzingChildren;
					
					// Rez the child prims
					foreach (Primitive prim in linkset.Children)
					{
						currentPrim = prim;
						currentPosition = prim.Position + linkset.RootPrim.Position;
						
						Client.Objects.AddPrim(Client.Network.CurrentSim, prim.PrimData, UUID.Zero, currentPosition,
						                       prim.Scale, prim.Rotation);
						
						if (!primDone.WaitOne(10000, false))
							throw new Exception("Rez failed, timed out while creating child prim.");
						Client.Objects.SetPosition(Client.Network.CurrentSim, primsCreated[primsCreated.Count - 1].LocalID, currentPosition);
					}
					
					// Create a list of the local IDs of the newly created prims
                    List<uint> primIDs = new List<uint>(primsCreated.Count) {rootLocalID};
                    

                    if (linkset.Children.Count != 0)
					{
						// Add the rest of the prims to the list of local IDs)
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
						
						if (primDone.WaitOne(1000 * linkset.Children.Count, false))
							Client.Objects.SetRotation(Client.Network.CurrentSim, rootLocalID, rootRotation);
						else
							LogMessage("Warning: Failed to link {0} prims",linkQueue.Count);
					}
					else
					{
						Client.Objects.SetRotation(Client.Network.CurrentSim, rootLocalID, rootRotation);
					}
					
					// Set permissions on newly created prims
					Client.Objects.SetPermissions(Client.Network.CurrentSim, primIDs,
						PermissionWho.Everyone | PermissionWho.Group | PermissionWho.NextOwner,
						PermissionMask.All, true);
					
					state = ImporterState.Idle;
				}
				else
					LogMessage("WARNING: Skipping a linkset with a missing root prim");
				
				// Reset everything for the next linkset
				primsCreated.Clear();
			}
		}

        private void Objects_OnNewPrim(object sender, PrimEventArgs e)
		{
			Primitive prim = e.Prim;
			
			if ((prim.Flags & PrimFlags.CreateSelected) == 0)
				return; // We received an update for an object we didn't create
			
			switch (state)
			{
				case ImporterState.RezzingParent:
					rootLocalID = prim.LocalID;
					goto case ImporterState.RezzingChildren;
				case ImporterState.RezzingChildren:
					if (!primsCreated.Contains(prim))
					{
						// Need to set the textures first, and we have the old UUID's, which will work fine
						// for anyone who has the cached UUID for the texture in question.
						
						// TODO: Is there a way to set all of this at once, and update more ObjectProperties stuff?
						Client.Objects.SetPosition(e.Simulator, prim.LocalID, currentPosition);
						if (TextureUse == TextureSet.NewUUID)
                        {
                            currentPrim.Textures.DefaultTexture.TextureID = Textures[currentPrim.Textures.DefaultTexture.TextureID] == UUID.Zero
                                ? Primitive.TextureEntry.WHITE_TEXTURE 
                                : Textures[currentPrim.Textures.DefaultTexture.TextureID];

                            for (int j = 0; j < currentPrim.Textures.FaceTextures.Length; j++)
							{
								if (currentPrim.Textures.FaceTextures[j] != null &&
								    currentPrim.Textures.FaceTextures[j].TextureID != Primitive.TextureEntry.WHITE_TEXTURE)
								{
									if (Textures[currentPrim.Textures.FaceTextures[j].TextureID] == UUID.Zero)
										currentPrim.Textures.FaceTextures[j] = null;
									else
										currentPrim.Textures.FaceTextures[j].TextureID = Textures[currentPrim.Textures.FaceTextures[j].TextureID];
								}
							}
                        }
						else if (TextureUse == TextureSet.WhiteUUID || TextureUse == TextureSet.SculptUUID)
						{
							currentPrim.Textures.DefaultTexture.TextureID = Primitive.TextureEntry.WHITE_TEXTURE;
							for (int j = 0; j < currentPrim.Textures.FaceTextures.Length; j++)
							{
								currentPrim.Textures.FaceTextures[j] = null;
							}
						}
						Client.Objects.SetTextures(e.Simulator, prim.LocalID, currentPrim.Textures);
						
						if (currentPrim.Light != null && currentPrim.Light.Intensity > 0)
						{
							Client.Objects.SetLight(e.Simulator, prim.LocalID, currentPrim.Light);
						}
						
						if (currentPrim.Flexible != null)
						{
							Client.Objects.SetFlexible(e.Simulator, prim.LocalID, currentPrim.Flexible);
						}
						
						if (currentPrim.Sculpt != null && currentPrim.Sculpt.SculptTexture != UUID.Zero)
						{
							if (TextureUse == TextureSet.NewUUID || TextureUse == TextureSet.SculptUUID)
                            {
                                currentPrim.Sculpt.SculptTexture = Textures[currentPrim.Sculpt.SculptTexture] == UUID.Zero 
                                    ? Primitive.TextureEntry.WHITE_TEXTURE 
                                    : Textures[currentPrim.Sculpt.SculptTexture];
                            }
							else if (TextureUse == TextureSet.WhiteUUID)
								currentPrim.Sculpt.SculptTexture = Primitive.TextureEntry.WHITE_TEXTURE;
							
							Client.Objects.SetSculpt(e.Simulator, prim.LocalID, currentPrim.Sculpt);
						}
						
						if (currentPrim.Properties != null && !string.IsNullOrEmpty(currentPrim.Properties.Name))
						{
							Client.Objects.SetName(e.Simulator, prim.LocalID, currentPrim.Properties.Name);
						}
						
						if (currentPrim.Properties != null && !string.IsNullOrEmpty(currentPrim.Properties.Description))
						{
							Client.Objects.SetDescription(e.Simulator, prim.LocalID, currentPrim.Properties.Description);
						}
						
						primsCreated.Add(prim);
						primDone.Set();
					}
					break;
				case ImporterState.Linking:
					lock(linkQueue)
					{
						int indx = linkQueue.IndexOf(prim.LocalID);
						if (indx != -1)
						{
							linkQueue.RemoveAt(indx);
							if (linkQueue.Count == 0)
								primDone.Set();
						}
					}
					break;
			}
		}
	}
}
