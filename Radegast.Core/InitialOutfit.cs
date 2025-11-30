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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using OpenMetaverse;
using Radegast.Core;

namespace Radegast
{
    public class InitialOutfit
    {
        private readonly IRadegastInstance Instance;
        private readonly Inventory Store;
        private GridClient Client => Instance.Client;

        public InitialOutfit(IRadegastInstance instance)
        {
            Instance = instance;
            Store = Client.Inventory.Store;
        }

        public static InventoryNode FindNodeByName(InventoryNode root, string name)
        {
            if (root.Data.Name == name)
            {
                return root;
            }

            return root.Nodes.Values.Select(node => FindNodeByName(node, name))
                .FirstOrDefault(ret => ret != null);
        }

        public UUID CreateFolder(UUID parent, string name, FolderType type)
        {
            UUID ret = EventSubscriptionHelper.WaitForEvent<InventoryObjectAddedEventArgs, UUID>(
                h => Client.Inventory.Store.InventoryObjectAdded += h,
                h => Client.Inventory.Store.InventoryObjectAdded -= h,
                e => e.Obj.Name == name && e.Obj is InventoryFolder folder && folder.PreferredType == type,
                e =>
                {
                    var folder = (InventoryFolder)e.Obj;
                    Logger.Info($"Created folder {folder.Name}");
                    return folder.UUID;
                },
                20000,
                UUID.Zero);

            if (ret == UUID.Zero)
            {
                ret = Client.Inventory.CreateFolder(parent, name, type);
            }

            return ret;
        }

        private List<InventoryBase> FetchFolder(InventoryFolder folder)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var contents = Client.Inventory.RequestFolderContents(folder.UUID, folder.OwnerID,
                true, true, InventorySortOrder.ByName, cts.Token).GetAwaiter().GetResult();
            return contents;
        }

        public void CheckFolders()
        {
            // Check if we have clothing folder
            var clothingID = Client.Inventory.FindFolderForType(FolderType.Clothing);
            if (clothingID == Store.RootFolder.UUID)
            {
                clothingID = CreateFolder(Store.RootFolder.UUID, "Clothing", FolderType.Clothing);
            }

            // Check if we have trash folder
            var trashID = Client.Inventory.FindFolderForType(FolderType.Trash);
            if (trashID == Store.RootFolder.UUID)
            {
                trashID = CreateFolder(Store.RootFolder.UUID, "Trash", FolderType.Trash);
            }
        }

        public UUID CopyFolder(InventoryFolder folder, UUID destination)
        {
            UUID newFolderID = CreateFolder(destination, folder.Name, folder.PreferredType);

            var items = FetchFolder(folder);
            foreach (var item in items)
            {
                if (item is InventoryItem inventoryItem)
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Client.Inventory.RequestCopyItem(item.UUID, newFolderID, item.Name, item.OwnerID, (newItem) =>
                    {
                        try { tcs.TrySetResult(true); } catch { }
                    });

                    var completedTask = Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(20))).GetAwaiter().GetResult();
                    if (completedTask == tcs.Task && tcs.Task.GetAwaiter().GetResult())
                    {
                        Logger.Info($"Copied item {item.Name}");
                    }
                    else
                    {
                        Logger.Warn($"Failed to copy item {item.Name}");
                    }
                }
                else if (item is InventoryFolder inventoryFolder)
                {
                    CopyFolder(inventoryFolder, newFolderID);
                }
            }

            return newFolderID;
        }

        public void SetInitialOutfit(string outfit)
        {
            Thread t = new Thread(() => PerformInit(outfit)) {IsBackground = true, Name = "Initial outfit thread"};

            t.Start();
        }

        private void PerformInit(string initialOutfitName)
        {
            Logger.Debug("Starting initial outfit thread (first login)");
            var outfitFolder = FindNodeByName(Store.LibraryRootNode, initialOutfitName);

            if (outfitFolder == null)
            {
                return;
            }

            CheckFolders();

            UUID newClothingFolder = CopyFolder((InventoryFolder)outfitFolder.Data,
                Client.Inventory.FindFolderForType(AssetType.Clothing));

            List<InventoryItem> newOutfit = Store.GetContents(newClothingFolder)
                .Where(item => item is InventoryWearable || item is InventoryAttachment || item is InventoryObject)
                .Cast<InventoryItem>().ToList();

            Instance.COF.ReplaceOutfit(newClothingFolder).Wait();
            Logger.Debug("Initial outfit thread (first login) exiting");
        }
    }
}
