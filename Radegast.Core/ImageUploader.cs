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
using System.Threading.Tasks;
using LibreMetaverse;

namespace Radegast
{
	/// <summary>
	/// Description of ImageUploader.
	/// </summary>
	public class ImageUploader
	{
        private readonly GridClient Client;
        private DateTime start;
		public UUID TextureID = UUID.Zero;
		public TimeSpan Duration;
		public string? Status;
		public bool Success;
		
		public ImageUploader(GridClient client)
		{
			Client = client;
		}

        /// <summary>
        /// Asynchronously upload an image. Returns true on success.
        /// </summary>
        public async Task<bool> UploadImageAsync(string filename, string desc, UUID folder, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            TextureID = UUID.Zero;
            string inventoryName = Path.GetFileNameWithoutExtension(filename);
            byte[] data = File.ReadAllBytes(filename);
            start = DateTime.Now;

            try
            {
                using var cts = timeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : null;
                if (cts != null && timeout.HasValue) cts.CancelAfter(timeout.Value);
                var ct = cts?.Token ?? cancellationToken;

                var (success, status, itemID, assetID) = await Client.Inventory.RequestCreateItemFromAssetAsync(
                    data, inventoryName, desc, AssetType.Texture, InventoryType.Texture, folder,
                    Permissions.FullPermissions, ct).ConfigureAwait(false);
                TextureID = assetID;
                Success = success;
                Status = status;
                Duration = DateTime.Now - start;
                return success;
            }
            catch (OperationCanceledException)
            {
                Status = "Texture upload cancelled";
                throw;
            }
            catch (Exception ex)
            {
                Status = "Texture upload failed: " + ex.Message;
                return false;
            }
        }
	}
}
