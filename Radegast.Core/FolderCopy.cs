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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using OpenMetaverse;

namespace Radegast
{

    public class FolderCopy
    {
        private readonly IRadegastInstance Instance;
        private readonly GridClient Client;

        // Batch size for copy operations
        private const int COPY_BATCH_SIZE = 10;
        private const int COPY_BATCH_RETRIES = 2;

        public FolderCopy(IRadegastInstance instance)
        {
            Instance = instance;
            Client = Instance.Client;
        }

        public async Task GetFoldersAsync(string folder, CancellationToken cancellationToken = default)
        {
            var f = FindFolder(folder, Client.Inventory.Store.LibraryRootNode);
            if (f == null) return;

            UUID dest = Client.Inventory.FindFolderForType(AssetType.Clothing);
            if (dest == UUID.Zero) return;

            var destFolder = (InventoryFolder)Client.Inventory.Store[dest];

            Instance.ShowNotificationInChat("Starting copy operation...");
            foreach (var node in f.Nodes.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (node.Data is InventoryFolder sourceFolder)
                {
                    Instance.ShowNotificationInChat($"  Copying {sourceFolder.Name} to {destFolder.Name}");
                    try
                    {
                        await CopyFolderAsync(destFolder, sourceFolder, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Instance.ShowNotificationInChat($"  Copy of {sourceFolder.Name} cancelled or timed out.");
                    }
                    catch (Exception ex)
                    {
                        Instance.ShowNotificationInChat($"  Failed copying {sourceFolder.Name}: {ex.Message}");
                    }
                }
            }
            Instance.ShowNotificationInChat("Done.");
        }

        public async Task CopyFolderAsync(InventoryFolder dest, InventoryFolder folder, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Create destination folder locally and on server
            UUID newFolderID = Client.Inventory.CreateFolder(dest.UUID, folder.Name, FolderType.None);

            // small delay to give the server time to create the folder record if needed
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            // Use the async FolderContents API with a timeout
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(45));
                var items = await Client.Inventory.FolderContentsAsync(folder.UUID, folder.OwnerID, true, true, InventorySortOrder.ByDate, cts.Token).ConfigureAwait(false);
                if (items == null) return;

                // First, handle subfolders to preserve hierarchy
                var subfolders = items.OfType<InventoryFolder>().ToList();
                foreach (var sub in subfolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Create corresponding subfolder inside newFolderID
                    UUID newChildFolderID = Client.Inventory.CreateFolder(newFolderID, sub.Name, sub.PreferredType);

                    // Small delay to allow server/store to register the new folder
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);

                    // Construct a lightweight InventoryFolder instance to represent the destination
                    var newChildFolder = new InventoryFolder(newChildFolderID)
                    {
                        ParentUUID = newFolderID,
                        Name = sub.Name,
                        PreferredType = sub.PreferredType,
                        OwnerID = Client.Self.AgentID
                    };

                    // Recurse into the subfolder
                    await CopyFolderAsync(newChildFolder, sub, cancellationToken).ConfigureAwait(false);
                }

                // Then process items in this folder (exclude subfolders)
                var invItems = items.OfType<InventoryItem>().ToList();
                var total = invItems.Count;
                if (total == 0) return;

                int copiedCount = 0;

                // Process in batches
                for (int i = 0; i < total; i += COPY_BATCH_SIZE)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = invItems.Skip(i).Take(COPY_BATCH_SIZE).ToList();
                    var itemIds = batch.Select(it => it.UUID).ToList();
                    var targetFolders = Enumerable.Repeat(newFolderID, batch.Count).ToList();
                    var names = batch.Select(it => it.Name).ToList();

                    bool batchSuccess = false;
                    Exception lastEx = null;

                    for (int attempt = 0; attempt <= COPY_BATCH_RETRIES; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var copyResult = await Client.Inventory.RequestCopyItemsWithResultAsync(itemIds, targetFolders, names, folder.OwnerID, cancellationToken).ConfigureAwait(false);

                            if (copyResult != null && copyResult.Success)
                            {
                                batchSuccess = true;
                                copiedCount += batch.Count;
                                Instance.ShowNotificationInChat($"    * Copied {copiedCount}/{total} items to {dest.Name}");
                                break;
                            }

                            // If not success, record exception and retry
                            lastEx = copyResult?.Error ?? new Exception("CopyItemsWithResult reported failure");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                        }

                        // small backoff before retry
                        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    }

                    if (!batchSuccess)
                    {
                        // Try per-item fallback for the batch to maximize partial success
                        foreach (var it in batch)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                var singleResult = await Client.Inventory.RequestCopyItemsWithResultAsync(
                                    new List<UUID> { it.UUID }, new List<UUID> { newFolderID }, new List<string> { it.Name }, folder.OwnerID, cancellationToken).ConfigureAwait(false);

                                if (singleResult != null && singleResult.Success)
                                {
                                    copiedCount++;
                                    Instance.ShowNotificationInChat($"    * Copied {copiedCount}/{total} items to {dest.Name}");
                                }
                                else
                                {
                                    Instance.ShowNotificationInChat($"    ! Failed copying {it.Name} to {dest.Name}");
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                Instance.ShowNotificationInChat($"    ! Exception copying {it.Name}: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }

        public InventoryNode FindFolder(string folder, InventoryNode start)
        {
            if (start.Data.Name == folder)
            {
                return start;
            }

            foreach (var node in start.Nodes.Values)
            {
                if (node.Data is InventoryFolder)
                {
                    var n = FindFolder(folder, node);
                    if (n != null)
                    {
                        return n;
                    }
                }
            }

            return null;
        }
    }
}
