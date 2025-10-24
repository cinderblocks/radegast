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
using System.IO;
using System.Threading;
using OpenMetaverse;

namespace Radegast
{
	/// <summary>
	/// Description of ImageUploader.
	/// </summary>
	public class ImageUploader
	{
        private readonly GridClient Client;
        private DateTime start;
        private readonly AutoResetEvent UploadCompleteEvent = new AutoResetEvent(false);
		public UUID TextureID = UUID.Zero;
		public TimeSpan Duration;
		public string Status;
		public bool Success;
		
		public ImageUploader(GridClient client)
		{
			Client = client;
		}
		
		public bool UploadImage(string filename, string desc, UUID folder)
		{
			TextureID = UUID.Zero;
			string inventoryName = Path.GetFileNameWithoutExtension(filename);
			uint timeout = 180 * 1000;
			byte[] data = File.ReadAllBytes(filename);
			start = DateTime.Now;
			
			Client.Inventory.RequestCreateItemFromAsset(data,inventoryName,desc,AssetType.Texture,InventoryType.Texture,folder,
			                                            delegate(bool success, string status, UUID itemID, UUID assetID)
			                                            {
			                                            	TextureID = assetID;
			                                            	Success = success;
			                                            	Status = status;
			                                            	UploadCompleteEvent.Set();
			                                            });
			if (UploadCompleteEvent.WaitOne((int)timeout, false))
            {
                return TextureID != UUID.Zero;
            }

            Status = "Texture upload timed out";
            return false;
        }
	}
}
