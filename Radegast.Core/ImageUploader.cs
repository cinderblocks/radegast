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
		public UUID TextureID = UUID.Zero;
		public TimeSpan Duration;
		public string Status;
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

            var tcs = new TaskCompletionSource<bool>();

            // Register the callback to complete the task source
            Client.Inventory.RequestCreateItemFromAsset(data, inventoryName, desc, AssetType.Texture, InventoryType.Texture, folder,
                (success, status, itemID, assetID) =>
                {
                    TextureID = assetID;
                    Success = success;
                    Status = status;
                    Duration = DateTime.Now - start;
                    tcs.TrySetResult(success);
                });

            try
            {
                Task delayTask = (timeout.HasValue) ? Task.Delay(timeout.Value, cancellationToken) : Task.Delay(Timeout.Infinite, cancellationToken);

                var completed = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

                if (completed == tcs.Task)
                {
                    return await tcs.Task.ConfigureAwait(false);
                }

                // If we get here, either timeout or cancellation occurred
                if (cancellationToken.IsCancellationRequested)
                {
                    Status = "Texture upload cancelled";
                    throw new OperationCanceledException(cancellationToken);
                }

                Status = "Texture upload timed out";
                return false;
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

		public bool UploadImage(string filename, string desc, UUID folder)
		{
			// Maintain existing synchronous API by calling the async method with a 180s timeout
			try
			{
				return UploadImageAsync(filename, desc, folder, TimeSpan.FromSeconds(180)).GetAwaiter().GetResult();
			}
			catch (AggregateException ae)
			{
				// Unwrap and rethrow inner exception if cancellation occurred
				throw ae.Flatten();
			}
		}
	}
}
