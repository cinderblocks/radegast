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

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Radegast.WinForms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Radegast
{
    public partial class InventoryConsole : UserControl
    {
        private const int UPDATE_INTERVAL = 1000;

        private readonly RadegastInstanceForms instance;
        private GridClient Client => instance.Client;

        private readonly Inventory Inventory;
        private TreeNode invRootNode;
        private string newItemName = string.Empty;
        private readonly ConcurrentDictionary<UUID, byte> fetchedFolders = new ConcurrentDictionary<UUID, byte>();
        private System.Threading.Timer _EditTimer;
        private TreeNode _EditNode;
        private System.Timers.Timer TreeUpdateTimer;
        private BlockingCollection<InventoryBase> ItemsToAdd;
        private BlockingCollection<InventoryBase> ItemsToUpdate;
        private Task queueProcessorTask;
        private CancellationTokenSource queueProcessorCts;
        private readonly ConcurrentDictionary<UUID, byte> ItemsToUpdateSet = new ConcurrentDictionary<UUID, byte>();
        // Queue bounds to prevent runaway memory growth
        private const int MAX_QUEUE_SIZE = 20000; // maximum items allowed in each queue
        private const int MAX_BATCH_PROCESS = 400; // items processed per timer tick
        private bool TreeUpdateInProgress = false;
        private readonly ConcurrentDictionary<UUID, TreeNode> UUID2NodeCache = new ConcurrentDictionary<UUID, TreeNode>();
        private bool appearanceWasBusy;
        private InvNodeSorter sorter;
        private readonly ConcurrentDictionary<UUID, int> QueuedFoldersNeedingUpdate = new ConcurrentDictionary<UUID, int>();
        private readonly AutoResetEvent trashCreated = new AutoResetEvent(false);
        private Task inventoryUpdateTask;
        private CancellationTokenSource inventoryUpdateCancelToken;
        private readonly SynchronizationContext uiContext;

        #region Construction and disposal
        public InventoryConsole(RadegastInstanceForms instance)
        {
            InitializeComponent();
            // capture UI synchronization context for centralized marshaling
            uiContext = SynchronizationContext.Current;
            Disposed += InventoryConsole_Disposed;

            TreeUpdateTimer = new System.Timers.Timer()
            {
                Interval = UPDATE_INTERVAL,
                Enabled = false,
                SynchronizingObject = invTree
            };
            TreeUpdateTimer.Elapsed += TreeUpdateTimerTick;

            // Initialize bounded queues and start background consumer
            ItemsToAdd = new BlockingCollection<InventoryBase>(MAX_QUEUE_SIZE);
            ItemsToUpdate = new BlockingCollection<InventoryBase>(MAX_QUEUE_SIZE);
            queueProcessorCts = new CancellationTokenSource();
            queueProcessorTask = Task.Run(() => QueueProcessor(queueProcessorCts.Token), queueProcessorCts.Token);

            this.instance = instance;
            Inventory = Client.Inventory.Store;
            Inventory.RootFolder.OwnerID = Client.Self.AgentID;
            invTree.ImageList = frmMain.ResourceImages;
            invRootNode = AddDir(null, Inventory.RootFolder);
            UpdateStatus("Reading cache");
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Logger.Debug($"Reading inventory cache from {instance.InventoryCacheFileName}", Client);
                Inventory.RestoreFromDisk(instance.InventoryCacheFileName);
                Init();
            });

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        public void Init()
        {
            if (instance.MainForm.InvokeRequired)
            {
                instance.MainForm.BeginInvoke(new MethodInvoker(Init));
                return;
            }

            AddFolderFromStore(invRootNode, Inventory.RootFolder);

            sorter = new InvNodeSorter();

            if (!instance.GlobalSettings.ContainsKey("inv_sort_bydate"))
                instance.GlobalSettings["inv_sort_bydate"] = OSD.FromBoolean(true);
            if (!instance.GlobalSettings.ContainsKey("inv_sort_sysfirst"))
                instance.GlobalSettings["inv_sort_sysfirst"] = OSD.FromBoolean(true);

            sorter.ByDate = instance.GlobalSettings["inv_sort_bydate"].AsBoolean();
            sorter.SystemFoldersFirst = instance.GlobalSettings["inv_sort_sysfirst"].AsBoolean();

            tbtnSortByDate.Checked = sorter.ByDate;
            tbtbSortByName.Checked = !sorter.ByDate;
            tbtnSystemFoldersFirst.Checked = sorter.SystemFoldersFirst;

            invTree.TreeViewNodeSorter = sorter;

            if (instance.MonoRuntime)
            {
                invTree.BackColor = Color.FromKnownColor(KnownColor.Window);
                invTree.ForeColor = invTree.LineColor = Color.FromKnownColor(KnownColor.WindowText);
                InventoryFolder f = new InventoryFolder(UUID.Random())
                {
                    Name = "",
                    ParentUUID = UUID.Zero,
                    PreferredType = FolderType.None
                };
                TreeNode dirNode = new TreeNode
                {
                    Name = f.UUID.ToString(),
                    Text = f.Name,
                    Tag = f,
                    ImageIndex = GetDirImageIndex(f.PreferredType.ToString().ToLower())
                };
                dirNode.SelectedImageIndex = dirNode.ImageIndex;
                invTree.Nodes.Add(dirNode);
                invTree.Sort();
            }

            saveAllTToolStripMenuItem.Enabled = false;

            inventoryUpdateCancelToken = new CancellationTokenSource();

            // Start traversal asynchronously on a thread-pool thread and pass the cancellation token
            inventoryUpdateTask = Task.Run(() => StartTraverseNodes(inventoryUpdateCancelToken.Token), inventoryUpdateCancelToken.Token);

            if (IsHandleCreated)
            {
                try
                {
                    // Marshal Expand to UI thread to avoid cross-thread control access
                    RunOnUi(() =>
                    {
                        try { invRootNode.Expand(); } catch { }
                    });
                }
                catch { }
            }

            invTree.AfterExpand += TreeView_AfterExpand;
            invTree.NodeMouseClick += invTree_MouseClick;
            invTree.NodeMouseDoubleClick += invTree_NodeMouseDoubleClick;

            _EditTimer = new System.Threading.Timer(OnLabelEditTimer, null, Timeout.Infinite, Timeout.Infinite);

            // Callbacks
            Inventory.InventoryObjectAdded += Inventory_InventoryObjectAdded;
            Inventory.InventoryObjectUpdated += Inventory_InventoryObjectUpdated;
            Inventory.InventoryObjectRemoved += Inventory_InventoryObjectRemoved;

            Client.Appearance.AppearanceSet += Appearance_AppearanceSet;
            Client.Objects.ObjectUpdate += Objects_AttachmentUpdate;
        }

        /// <summary>
        /// Helper to run an action on the UI thread (begin-invoke when required).
        /// Centralizes InvokeRequired/BeginInvoke pattern to reduce repeated code.
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (action == null) return;

            try
            {
                // If we captured a SynchronizationContext (usually the UI thread), post to it.
                if (uiContext != null && SynchronizationContext.Current != uiContext)
                {
                    try
                    {
                        uiContext.Post(_ =>
                        {
                            try { action(); } catch { }
                        }, null);
                        return;
                    }
                    catch { /* fallback to BeginInvoke below */ }
                }

                if (this.IsHandleCreated && this.InvokeRequired)
                {
                    this.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
                // Control may be disposing or not created; fallback to main form if available
                try
                {
                    if (instance?.MainForm != null && instance.MainForm.IsHandleCreated && instance.MainForm.InvokeRequired)
                    {
                        instance.MainForm.BeginInvoke(action);
                    }
                    else
                    {
                        action();
                    }
                }
                catch { }
            }
            catch { }
        }

        private async void InventoryConsole_Disposed(object sender, EventArgs e)
        {
            // Signal stop of background traversal and wait for it to finish (best-effort)
            try
            {
                instance.GestureManager.StopMonitoring();

                if (inventoryUpdateCancelToken != null)
                {
                    try { inventoryUpdateCancelToken.Cancel(); } catch { }
                }

                if (inventoryUpdateTask != null)
                {
                    try
                    {
                        // Wait for the task to complete, but don't block shutdown indefinitely
                        var finished = await Task.WhenAny(inventoryUpdateTask, Task.Delay(5000)).ConfigureAwait(false);
                        if (finished != inventoryUpdateTask)
                        {
                            Logger.Warn("inventoryUpdateTask did not terminate within timeout after disposal.", Client);
                        }
                        else
                        {
                            // Observe exceptions
                            await inventoryUpdateTask.ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Logger.Error("Error while waiting for inventory update task: " + ex.Message, ex);
                    }
                }
            }
            catch (Exception) { }

            // Stop timers and unsubscribe events that touch controls on the UI thread
            try
            {
                RunOnUi(() =>
                {
                    try
                    {
                        if (TreeUpdateTimer != null)
                        {
                            try { TreeUpdateTimer.Stop(); TreeUpdateTimer.Dispose(); } catch { }
                            TreeUpdateTimer = null;
                        }
                    }
                    catch { }

                    try
                    {
                        Inventory.InventoryObjectAdded -= Inventory_InventoryObjectAdded;
                        Inventory.InventoryObjectUpdated -= Inventory_InventoryObjectUpdated;
                        Inventory.InventoryObjectRemoved -= Inventory_InventoryObjectRemoved;

                        Client.Appearance.AppearanceSet -= Appearance_AppearanceSet;
                        Client.Objects.ObjectUpdate -= Objects_AttachmentUpdate;
                    }
                    catch (Exception) { }
                });
            }
            catch { }

            // Shutdown and dispose bounded queues and background processor
            try { queueProcessorCts?.Cancel(); } catch { }
            if (queueProcessorTask != null)
            {
                try
                {
                    var finished = await Task.WhenAny(queueProcessorTask, Task.Delay(2000)).ConfigureAwait(false);
                    if (finished != queueProcessorTask)
                    {
                        Logger.Warn("queueProcessorTask did not terminate within timeout after disposal.", Client);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Error while waiting for queue processor task: " + ex.Message, ex);
                }
            }
            try { ItemsToAdd?.CompleteAdding(); } catch { }
            try { ItemsToUpdate?.CompleteAdding(); } catch { }
            try { ItemsToAdd?.Dispose(); } catch { }
            try { ItemsToUpdate?.Dispose(); } catch { }
            try { queueProcessorCts?.Dispose(); } catch { }
        }
        #endregion

        #region Network callbacks

        private void Appearance_AppearanceSet(object sender, AppearanceSetEventArgs e)
        {
            UpdateWornLabels();
            if (!appearanceWasBusy) { return; }

            appearanceWasBusy = false;
            Client.Appearance.RequestSetAppearance(true);
        }

        private void Objects_AttachmentUpdate(object sender, PrimEventArgs e)
        {
            Primitive prim = e.Prim;

            if (Client.Self.LocalID == 0
                || prim.ParentID != Client.Self.LocalID
                || prim.NameValues == null)
            {
                return;
            }

            for (int i = 0; i < prim.NameValues.Length; ++i)
            {
                if (prim.NameValues[i].Name != "AttachItemID") { continue; }

                var inventoryID = new UUID(prim.NameValues[i].Value.ToString());

                // Don't update the tree yet if we're still updating inventory tree from server
                if (!TreeUpdateInProgress)
                {
                    if (Inventory.Contains(inventoryID))
                    {
                        UpdateNodeLabel(inventoryID);
                    }
                    else
                    {
                        Client.Inventory.RequestFetchInventory(inventoryID, Client.Self.AgentID);
                    }
                }
                break;
            }
        }

        private void Inventory_InventoryObjectAdded(object sender, InventoryObjectAddedEventArgs e)
        {
            if (e.Obj is InventoryFolder folder
                && folder.PreferredType == FolderType.Trash)
            {
                trashCreated.Set();
            }

            if (TreeUpdateInProgress)
            {
                // Try to add to bounded queue; wait briefly before giving up
                if (!ItemsToAdd.TryAdd(e.Obj, 100)) // wait up to 100ms
                {
                    Logger.Warn("ItemsToAdd queue full, dropping inventory add event.", Client);
                }
            }
            else
            {
                Exec_OnInventoryObjectAdded(e.Obj);
            }
        }

        private void Exec_OnInventoryObjectAdded(InventoryBase obj)
        {
            if (this.IsHandleCreated && this.InvokeRequired)
            {
                RunOnUi(() => Exec_OnInventoryObjectAdded(obj));
                return;
            }

            TreeNode parent = FindNodeForItem(obj.ParentUUID);

            if (parent != null)
            {
                TreeNode newNode = AddBase(parent, obj);
                if (obj.Name == newItemName)
                {
                    if (newNode.Parent.IsExpanded)
                    {
                        newNode.BeginEdit();
                    }
                    else
                    {
                        newNode.Parent.Expand();
                    }
                }
            }
            newItemName = string.Empty;
        }

        private void Inventory_InventoryObjectRemoved(object sender, InventoryObjectRemovedEventArgs e)
        {
            if (this.IsHandleCreated && this.InvokeRequired)
            {
                RunOnUi(() => Inventory_InventoryObjectRemoved(sender, e));
                return;
            }

            TreeNode currentNode = FindNodeForItem(e.Obj.UUID);
            if (currentNode != null)
            {
                RemoveNode(currentNode);
            }
        }

        private void Inventory_InventoryObjectUpdated(object sender, InventoryObjectUpdatedEventArgs e)
        {
            if (TreeUpdateInProgress)
            {
                if (e.NewObject is InventoryFolder)
                {
                    TreeNode currentNode = FindNodeForItem(e.NewObject.UUID);
                    if (currentNode != null && currentNode.Text == e.NewObject.Name) return;
                }

                // Deduplicate updates by UUID and enforce bounds
                if (ItemsToUpdateSet.TryAdd(e.NewObject.UUID, 0))
                {
                    // wait briefly for space before dropping the event
                    if (!ItemsToUpdate.TryAdd(e.NewObject, 100)) // wait up to 100ms
                    {
                        // Queue full, remove dedupe entry and drop
                        ItemsToUpdateSet.TryRemove(e.NewObject.UUID, out _);
                        Logger.Warn("ItemsToUpdate queue full, dropping inventory update event.", Client);
                    }
                }
            }
            else
            {
                Exec_OnInventoryObjectUpdated(e.OldObject, e.NewObject);
            }
        }

        private void Exec_OnInventoryObjectUpdated(InventoryBase oldObject, InventoryBase newObject)
        {
            if (newObject == null) { return; }

            if (this.IsHandleCreated && this.InvokeRequired)
            {
                RunOnUi(() => Exec_OnInventoryObjectUpdated(oldObject, newObject));
                return;
            }

            // Find our current node in the tree
            TreeNode currentNode = FindNodeForItem(newObject.UUID);

            // Find which node should be our parent
            TreeNode parent = FindNodeForItem(newObject.ParentUUID);

            if (parent == null) { return; }

            if (currentNode != null)
            {
                // Did we move to a different folder
                if (currentNode.Parent != parent)
                {
                    TreeNode movedNode = (TreeNode)currentNode.Clone();
                    movedNode.Tag = newObject;
                    parent.Nodes.Add(movedNode);
                    RemoveNode(currentNode);
                    CacheNode(movedNode);
                }
                else // Update
                {
                    currentNode.Tag = newObject;
                    currentNode.Text = ItemLabel(newObject, false);
                    currentNode.Name = newObject.Name;
                }
            }
            else // We are not in the tree already, add
            {
                AddBase(parent, newObject);
            }
        }
        #endregion

        #region Node manipulation
        public static int GetDirImageIndex(string t)
        {
            t = System.Text.RegularExpressions.Regex.Replace(t, @"folder$", "");
            int res = frmMain.ImageNames.IndexOf("inv_folder_" + t);
            if (res != -1) return res;

            switch (t)
            {
                case "currentoutfit":
                case "myoutfits":
                    return frmMain.ImageNames.IndexOf("inv_folder_outfit");
                case "lsltext":
                    return frmMain.ImageNames.IndexOf("inv_folder_script");
            }
            return frmMain.ImageNames.IndexOf("inv_folder_plain_closed");
        }

        public static int GetItemImageIndex(string t)
        {
            int res = frmMain.ImageNames.IndexOf("inv_item_" + t);
            if (res != -1) return res;

            switch (t)
            {
                case "lsltext":
                    return frmMain.ImageNames.IndexOf("inv_item_script");
                case "callingcard":
                    return frmMain.ImageNames.IndexOf("inv_item_callingcard_offline");
                default:
                    return res;
            }
        }

        private TreeNode AddBase(TreeNode parent, InventoryBase obj)
        {
            if (obj is InventoryItem item)
            {
                return AddItem(parent, item);
            }

            return AddDir(parent, (InventoryFolder)obj);
        }

        private TreeNode AddDir(TreeNode parentNode, InventoryFolder f)
        {
            TreeNode dirNode = new TreeNode
            {
                Name = f.UUID.ToString(),
                Text = f.Name,
                Tag = f,
                ImageIndex = GetDirImageIndex(f.PreferredType.ToString().ToLower())
            };
            dirNode.SelectedImageIndex = dirNode.ImageIndex;
            if (parentNode == null)
            {
                if (invTree.Nodes.ContainsKey(f.UUID.ToString()))
                {
                    invTree.Nodes.RemoveByKey(f.UUID.ToString());
                }
                invTree.Nodes.Add(dirNode);
            }
            else
            {
                if (parentNode.Nodes.ContainsKey(f.UUID.ToString()))
                {
                    parentNode.Nodes.RemoveByKey(f.UUID.ToString());
                }
                parentNode.Nodes.Add(dirNode);
            }
            lock (UUID2NodeCache)
            {
                UUID2NodeCache[f.UUID] = dirNode;
            }
            return dirNode;
        }


        private TreeNode AddItem(TreeNode parent, InventoryItem item)
        {
            TreeNode itemNode = new TreeNode
            {
                Name = item.UUID.ToString(),
                Text = ItemLabel(item, false),
                Tag = item
            };

            InventoryItem linkedItem = null;

            if (item.IsLink() && Inventory.Contains(item.AssetUUID) && Inventory[item.AssetUUID] is InventoryItem)
            {
                linkedItem = (InventoryItem)Inventory[item.AssetUUID];
            }
            else
            {
                linkedItem = item;
            }

            int img;
            if (linkedItem is InventoryWearable wearable)
            {
                img = GetItemImageIndex(wearable.WearableType.ToString().ToLower());
            }
            else
            {
                img = GetItemImageIndex(linkedItem.AssetType.ToString().ToLower());
            }

            itemNode.ImageIndex = img;
            itemNode.SelectedImageIndex = img;
            parent.Nodes.Add(itemNode);
            lock (UUID2NodeCache)
            {
                UUID2NodeCache[item.UUID] = itemNode;
            }
            return itemNode;
        }

        private TreeNode FindNodeForItem(UUID itemID)
        {
            if (UUID2NodeCache.TryGetValue(itemID, out var item))
            {
                return item;
            }
            return null;
        }

        private void CacheNode(TreeNode node)
        {
            InventoryBase item = (InventoryBase)node.Tag;
            if (item == null) return;

            foreach (TreeNode child in node.Nodes)
            {
                CacheNode(child);
            }
            UUID2NodeCache[item.UUID] = node;
        }

        private void RemoveNode(TreeNode node)
        {
            InventoryBase item = (InventoryBase)node.Tag;
            if (item != null)
            {
                foreach (TreeNode child in node.Nodes)
                {
                    if (child != null)
                        RemoveNode(child);
                }

                UUID2NodeCache.TryRemove(item.UUID, out _);
            }
            node.Remove();
        }

        #endregion

        #region Private methods
        private void UpdateStatus(string text)
        {
            RunOnUi(() =>
            {
                if (text == "OK")
                {
                    saveAllTToolStripMenuItem.Enabled = true;
                }

                tlabelStatus.Text = text;
            });
        }

        private void UpdateNodeLabel(UUID itemID)
        {
            RunOnUi(() =>
            {
                TreeNode node = FindNodeForItem(itemID);
                if (node != null)
                {
                    node.Text = ItemLabel((InventoryBase)node.Tag, false);
                }
            });
        }

        private void AddFolderFromStore(TreeNode parent, InventoryFolder f)
        {
            List<InventoryBase> contents = Inventory.GetContents(f);
            foreach (InventoryBase item in contents)
            {
                TreeNode node = AddBase(parent, item);
                if (item is InventoryFolder folder)
                {
                    AddFolderFromStore(node, folder);
                }
            }
        }

        private void TraverseAndQueueNodes(InventoryNode start)
        {
            if (start.NeedsUpdate)
            {
                QueuedFoldersNeedingUpdate.TryAdd(start.Data.UUID, 0);
            }

            foreach (var item in Client.Inventory.Store.GetContents((InventoryFolder)start.Data).OfType<InventoryFolder>())
            {
                TraverseAndQueueNodes(Client.Inventory.Store.GetNodeFor(item.UUID));
            }
        }

        private void Inventory_FolderUpdated(object sender, FolderUpdatedEventArgs e)
        {
            if (e.Success)
            {
                QueuedFoldersNeedingUpdate.TryRemove(e.FolderID, out var _);
                return;
            }

            if (!QueuedFoldersNeedingUpdate.TryGetValue(e.FolderID, out var retries))
            {
                return;
            }

            if (retries > 3)
            {
                QueuedFoldersNeedingUpdate.TryRemove(e.FolderID, out var _);
            }
            else
            {
                QueuedFoldersNeedingUpdate.TryUpdate(e.FolderID, retries + 1, retries);
            }
        }

        private async Task FetchQueuedFolders(CancellationToken token)
        {
            Client.Inventory.FolderUpdated += Inventory_FolderUpdated;

            try
            {
                QueuedFoldersNeedingUpdate.Clear();
                TraverseAndQueueNodes(Client.Inventory.Store.RootNode);

                while (!QueuedFoldersNeedingUpdate.IsEmpty)
                {
                    token.ThrowIfCancellationRequested();
                    var folderKeys = QueuedFoldersNeedingUpdate.Keys.ToList();
                    // Update UI with current progress: number of items cached and folders remaining
                    try
                    {
                        UpdateStatus($"Loading... {UUID2NodeCache.Count} items, {folderKeys.Count} folders remaining");
                    }
                    catch { }
                    var tasks = folderKeys
                        .Select(folderKey => Client.Inventory.RequestFolderContents(
                            folderKey,
                            Client.Self.AgentID,
                            true,
                            true,
                            InventorySortOrder.ByDate,
                            token
                        ));

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    TraverseAndQueueNodes(Client.Inventory.Store.RootNode);
                }
            }
            finally
            {
                Client.Inventory.FolderUpdated -= Inventory_FolderUpdated;
            }
        }

        /// <summary>
        /// Inventory traversal that accepts a cancellation token for explicit control.
        /// </summary>
        private async Task StartTraverseNodes(CancellationToken token)
        {
            if (!Client.Network.CurrentSim.IsEventQueueRunning(true))
            {
                Logger.Warn("Could not traverse inventory. Event Queue is not running.", Client);
                return;
            }

            UpdateStatus("Loading...");
            TreeUpdateInProgress = true;
            TreeUpdateTimer.Start();

            instance.GestureManager.BeginMonitoring();

            try
            {
                await FetchQueuedFolders(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Inventory traversal cancelled.", Client);
                TreeUpdateTimer.Stop();
                TreeUpdateInProgress = false;
                UpdateStatus("Reading cache");
                return;
            }
            catch (Exception ex)
            {
                Logger.Error("Error during inventory traversal: " + ex.Message, ex);
            }

            TreeUpdateTimer.Stop();
            RunOnUi(() => TreeUpdateTimerTick(null, null));
            TreeUpdateInProgress = false;
            UpdateStatus("OK");
            instance.ShowNotificationInChat("Inventory update completed.");

            // Initial outfit handling moved to login response handler to avoid race with inventory traversal

            // Updated labels on clothes that we are wearing
            UpdateWornLabels();

            // Update attachments now that we are done
            foreach (var attachment in Client.Appearance.GetAttachments())
            {
                if (Inventory.Contains(attachment))
                {
                    UpdateNodeLabel(attachment.UUID);
                }
                else
                {
                    Client.Inventory.RequestFetchInventory(attachment.UUID, Client.Self.AgentID, token);
                    return;
                }
            }

            Logger.Debug("Finished updating inventory folders, saving cache...", Client);

            ThreadPool.QueueUserWorkItem(state => Inventory.SaveToDisk(instance.InventoryCacheFileName));

            RunOnUi(() =>
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    invTree.Sort();
                }
            });
        }

        public void ReloadInventory()
        {
            // Fire-and-forget async reload to avoid blocking the UI thread
            _ = ReloadInventoryAsync();
        }

        private async Task ReloadInventoryAsync()
        {
            if (TreeUpdateInProgress)
            {
                TreeUpdateTimer.Stop();
                // Cancel the running traversal and replace the token for the next run
                try { inventoryUpdateCancelToken.Cancel(); } catch { }
                try { inventoryUpdateCancelToken.Dispose(); } catch { }
                inventoryUpdateCancelToken = new CancellationTokenSource();
            }

            saveAllTToolStripMenuItem.Enabled = false;

            Inventory.Clear();
            Inventory.RootFolder = Inventory.RootFolder;

            invTree.Nodes.Clear();
            UUID2NodeCache.Clear();
            invRootNode = AddDir(null, Inventory.RootFolder);
            Inventory.RootNode.NeedsUpdate = true;

            // Reset queues to clear pending items and stop old processor
            try { queueProcessorCts?.Cancel(); } catch { }
            try
            {
                if (queueProcessorTask != null)
                {
                    await Task.WhenAny(queueProcessorTask, Task.Delay(2000)).ConfigureAwait(false);
                }
            }
            catch { }

            try { ItemsToAdd?.CompleteAdding(); } catch { }
            try { ItemsToUpdate?.CompleteAdding(); } catch { }
            try { ItemsToAdd?.Dispose(); } catch { }
            try { ItemsToUpdate?.Dispose(); } catch { }
            ItemsToAdd = new BlockingCollection<InventoryBase>(MAX_QUEUE_SIZE);
            ItemsToUpdate = new BlockingCollection<InventoryBase>(MAX_QUEUE_SIZE);
            ItemsToUpdateSet.Clear();

            // restart queue processor
            queueProcessorCts = new CancellationTokenSource();
            queueProcessorTask = Task.Run(() => QueueProcessor(queueProcessorCts.Token), queueProcessorCts.Token);

            // Start traversal on thread-pool and keep reference so we can cancel later
            inventoryUpdateTask = Task.Run(() => StartTraverseNodes(inventoryUpdateCancelToken.Token), inventoryUpdateCancelToken.Token);

            if (IsHandleCreated)
            {
                try
                {
                    // Marshal Expand to UI thread to avoid cross-thread control access
                    RunOnUi(() =>
                    {
                        try { invRootNode.Expand(); } catch { }
                    });
                }
                catch { }
            }
        }

        private void reloadInventoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ReloadInventory();
        }

        private void TreeUpdateTimerTick(object sender, EventArgs e)
        {
            // Status update only; actual queue processing occurs on background consumer
            // Keep UI alive and show progress
            UpdateStatus($"Loading... {UUID2NodeCache.Count} items");
        }

        private async Task QueueProcessor(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var adds = new List<InventoryBase>();
                    var updates = new List<InventoryBase>();

                    // Drain up to MAX_BATCH_PROCESS items with a short wait
                    InventoryBase item;
                    while (adds.Count < MAX_BATCH_PROCESS && ItemsToAdd.TryTake(out item, 200, token))
                    {
                        adds.Add(item);
                    }

                    while (updates.Count < MAX_BATCH_PROCESS && ItemsToUpdate.TryTake(out item, 200, token))
                    {
                        updates.Add(item);
                    }

                    if (adds.Count > 0)
                    {
                        // Batch application on UI thread
                        try
                        {
                            RunOnUi(() =>
                            {
                                try
                                {
                                    invTree.BeginUpdate();
                                    foreach (var a in adds)
                                    {
                                        TreeNode node = FindNodeForItem(a.ParentUUID);
                                        if (node != null)
                                        {
                                            AddBase(node, a);
                                        }
                                    }
                                }
                                finally
                                {
                                    invTree.EndUpdate();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Error applying inventory adds: " + ex.Message, ex);
                        }
                    }

                    if (updates.Count > 0)
                    {
                        try
                        {
                            RunOnUi(() =>
                            {
                                try
                                {
                                    invTree.BeginUpdate();
                                    foreach (var u in updates)
                                    {
                                        try { Exec_OnInventoryObjectUpdated(u, u); } catch { }
                                        try { ItemsToUpdateSet.TryRemove(u.UUID, out _); } catch { }
                                    }
                                }
                                finally
                                {
                                    invTree.EndUpdate();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Error applying inventory updates: " + ex.Message, ex);
                        }
                    }

                    // If nothing processed, delay a bit to avoid busy loop
                    if (adds.Count == 0 && updates.Count == 0)
                    {
                        await Task.Delay(200, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Error("Queue processor exception: " + ex.Message, ex);
            }
        }

        #endregion

        private void btnProfile_Click(object sender, EventArgs e)
        {
            instance.ShowAgentProfile(txtCreator.Text, txtCreator.AgentID);
        }

        private void UpdateItemInfo(InventoryItem item)
        {
            foreach (Control c in pnlDetail.Controls)
            {
                c.Dispose();
            }
            pnlDetail.Controls.Clear();
            pnlItemProperties.Tag = item;

            if (item == null)
            {
                pnlItemProperties.Visible = false;
                return;
            }

            pnlItemProperties.Visible = true;
            btnProfile.Enabled = true;
            txtItemName.Text = item.Name;
            txtItemDescription.Text = item.Description;
            txtCreator.AgentID = item.CreatorID;
            txtCreator.Tag = item.CreatorID;
            txtCreated.Text = item.CreationDate.ToString(CultureInfo.InvariantCulture);

            txtAssetID.Text = item.AssetUUID != UUID.Zero ? item.AssetUUID.ToString() : string.Empty;

            txtInvID.Text = item.UUID.ToString();

            Permissions p = item.Permissions;
            cbOwnerModify.Checked = (p.OwnerMask & PermissionMask.Modify) != 0;
            cbOwnerCopy.Checked = (p.OwnerMask & PermissionMask.Copy) != 0;
            cbOwnerTransfer.Checked = (p.OwnerMask & PermissionMask.Transfer) != 0;

            cbNextOwnModify.CheckedChanged -= cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnCopy.CheckedChanged -= cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnTransfer.CheckedChanged -= cbNextOwnerUpdate_CheckedChanged;

            cbNextOwnModify.Checked = (p.NextOwnerMask & PermissionMask.Modify) != 0;
            cbNextOwnCopy.Checked = (p.NextOwnerMask & PermissionMask.Copy) != 0;
            cbNextOwnTransfer.Checked = (p.NextOwnerMask & PermissionMask.Transfer) != 0;

            cbNextOwnModify.CheckedChanged += cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnCopy.CheckedChanged += cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnTransfer.CheckedChanged += cbNextOwnerUpdate_CheckedChanged;


            switch (item.AssetType)
            {
                case AssetType.Texture:
                    SLImageHandler image = new SLImageHandler(instance, item.AssetUUID, item.Name, IsFullPerm(item));
                    image.Dock = DockStyle.Fill;
                    pnlDetail.Controls.Add(image);
                    break;

                case AssetType.Notecard:
                    Notecard note = new Notecard(instance, (InventoryNotecard)item);
                    note.Dock = DockStyle.Fill;
                    note.TabIndex = 3;
                    note.TabStop = true;
                    pnlDetail.Controls.Add(note);
                    note.rtbContent.Focus();
                    break;

                case AssetType.Landmark:
                    Landmark landmark = new Landmark(instance, (InventoryLandmark)item);
                    landmark.Dock = DockStyle.Fill;
                    pnlDetail.Controls.Add(landmark);
                    break;

                case AssetType.LSLText:
                    ScriptEditor script = new ScriptEditor(instance, (InventoryLSL)item);
                    script.Dock = DockStyle.Fill;
                    script.TabIndex = 3;
                    script.TabStop = true;
                    pnlDetail.Controls.Add(script);
                    break;

                case AssetType.Gesture:
                    Gesture gesture = new Gesture(instance, (InventoryGesture)item);
                    gesture.Dock = DockStyle.Fill;
                    pnlDetail.Controls.Add(gesture);
                    break;

                case AssetType.Bodypart:
                case AssetType.Clothing:
                    WearableTextures wearable = new WearableTextures(instance, (InventoryWearable)item);
                    wearable.Dock = DockStyle.Fill;
                    pnlDetail.Controls.Add(wearable);
                    break;

            }

            tabsInventory.SelectedTab = tabDetail;
        }

        private void cbNextOwnerUpdate_CheckedChanged(object sender, EventArgs e)
        {
            InventoryItem item = null;
            if (pnlItemProperties.Tag is InventoryItem tag)
            {
                item = tag;
            }
            if (item == null) return;

            PermissionMask pm = PermissionMask.Move;
            if (cbNextOwnCopy.Checked) pm |= PermissionMask.Copy;
            if (cbNextOwnModify.Checked) pm |= PermissionMask.Modify;
            if (cbNextOwnTransfer.Checked) pm |= PermissionMask.Transfer;
            item.Permissions.NextOwnerMask = pm;

            Client.Inventory.RequestUpdateItem(item);
            Client.Inventory.RequestFetchInventory(item.UUID, item.OwnerID);
        }

        private void txtItemName_Leave(object sender, EventArgs e)
        {
            InventoryItem item = null;
            if (pnlItemProperties.Tag is InventoryItem tag)
            {
                item = tag;
            }
            if (item == null) return;

            item.Name = txtItemName.Text;

            Client.Inventory.RequestUpdateItem(item);
            Client.Inventory.RequestFetchInventory(item.UUID, item.OwnerID);
        }

        private void txtItemDescription_Leave(object sender, EventArgs e)
        {
            InventoryItem item = null;
            if (pnlItemProperties.Tag is InventoryItem tag)
            {
                item = tag;
            }
            if (item == null) return;

            item.Description = txtItemDescription.Text;

            Client.Inventory.RequestUpdateItem(item);
            Client.Inventory.RequestFetchInventory(item.UUID, item.OwnerID);
        }

        private void invTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (!(invTree.SelectedNode.Tag is InventoryItem item)) { return; }
            item = instance.COF.ResolveInventoryLink(item) ?? item;

            switch (item.AssetType)
            {
                case AssetType.Landmark:
                    instance.ShowNotificationInChat($"Teleporting to {item.Name}");
                    Client.Self.RequestTeleport(item.AssetUUID);
                    break;

                case AssetType.Gesture:
                    Client.Self.PlayGesture(item.AssetUUID);
                    break;

                case AssetType.Notecard:
                    Notecard note = new Notecard(instance, (InventoryNotecard)item);
                    note.Dock = DockStyle.Fill;
                    note.ShowDetached();
                    break;

                case AssetType.LSLText:
                    ScriptEditor script = new ScriptEditor(instance, (InventoryLSL)item);
                    script.Dock = DockStyle.Fill;
                    script.ShowDetached();
                    break;

                case AssetType.Object:
                    RunBackgroundTask(async (cancellationtoken) =>
                    {
                        if (IsAttached(item))
                        {
                            await instance.COF.Detach(item, cancellationtoken);
                        }
                        else
                        {
                            await instance.COF.Attach(item, AttachmentPoint.Default, true, cancellationtoken);
                        }
                        UpdateLabelsFor(item);
                    }, UpdateWornLabels);
                    break;

                case AssetType.Bodypart:
                case AssetType.Clothing:
                    RunBackgroundTask(async (cancellationtoken) =>
                    {
                        if (IsWorn(item))
                        {
                            if (item.AssetType == AssetType.Clothing)
                            {
                                await instance.COF.RemoveFromOutfit(item, cancellationtoken);
                            }
                        }
                        else
                        {
                            await instance.COF.AddToOutfit(item, true, cancellationtoken);
                        }
                    }, UpdateWornLabels);
                    break;
            }
        }

        private async Task FetchFolder(UUID folderID, UUID ownerID, bool force, CancellationToken token = default)
        {
            if (force || !fetchedFolders.ContainsKey(folderID))
            {
                if (fetchedFolders.TryAdd(folderID, 0))
                {
                    // recorded as fetched
                }

                await Client.Inventory.RequestFolderContents(folderID, ownerID,
                    true, true, InventorySortOrder.ByDate, token).ConfigureAwait(false);
            }
        }

        public bool IsWorn(InventoryItem item)
        {
            return Client.Appearance.IsItemWorn(item) != WearableType.Invalid;
        }

        public AttachmentPoint AttachedTo(InventoryItem item)
        {
            return Client.Appearance.GetAttachmentsByInventoryItem()
                .TryGetValue(item, out var attachmentPoint) ? attachmentPoint : AttachmentPoint.Default;
        }

        public bool IsAttached(InventoryItem item)
        {
            return Client.Appearance.isItemAttached(item);
        }

        public InventoryItem AttachmentAt(AttachmentPoint point)
        {
            return (from attachment in Client.Appearance.GetAttachmentsByAttachmentPoint()
                    where attachment.Key == point
                    select attachment.Value.First()).FirstOrDefault();
        }

        /// <summary>
        /// Returns text of the label
        /// </summary>
        /// <param name="invBase">Inventory item</param>
        /// <param name="returnRaw">Should we return raw text, or if false decorated text with (worn) info, and (no copy) etc. permission info</param>
        /// <returns></returns>
        public string ItemLabel(InventoryBase invBase, bool returnRaw)
        {
            if (returnRaw || (invBase is InventoryFolder))
                return invBase.Name;

            InventoryItem item = (InventoryItem)invBase;

            string raw = item.Name;

            if (item.IsLink())
            {
                raw += " (link)";
                item = instance.COF.ResolveInventoryLink(item) ?? item;
                if (Inventory.Contains(item.AssetUUID) && Inventory[item.AssetUUID] is InventoryItem)
                {
                    item = (InventoryItem)Inventory[item.AssetUUID];
                }
            }

            if (item is InventoryGesture)
            {
                if (instance.Client.Self.ActiveGestures.ContainsKey(item.UUID))
                {
                    raw += " (active)";
                }
            }
            if ((item.Permissions.OwnerMask & PermissionMask.Modify) == 0)
            {
                raw += " (no modify)";
            }
            if ((item.Permissions.OwnerMask & PermissionMask.Copy) == 0)
            {
                raw += " (no copy)";
            }
            if ((item.Permissions.OwnerMask & PermissionMask.Transfer) == 0)
            {
                raw += " (no transfer)";
            }
            if (IsWorn(item))
            {
                raw += " (worn)";
            }
            if (IsAttached(item))
            {
                raw += $" (worn on {AttachedTo(item)})";
            }

            return raw;
        }

        public static bool IsFullPerm(InventoryItem item)
        {
            return (item.Permissions.OwnerMask & PermissionMask.Modify) != 0 &&
                   (item.Permissions.OwnerMask & PermissionMask.Copy) != 0 &&
                   (item.Permissions.OwnerMask & PermissionMask.Transfer) != 0;
        }

        private void invTree_MouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            TreeNode node = e.Node;

            if (e.Button == MouseButtons.Left)
            {
                invTree.SelectedNode = node;
                if (node.Tag is InventoryItem tag)
                {
                    UpdateItemInfo(instance.COF.ResolveInventoryLink(tag) ?? tag);
                }
                else
                {
                    UpdateItemInfo(null);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                invTree.SelectedNode = node;
                ctxInv.Show(invTree, e.X, e.Y);
            }
        }

        private void ctxInv_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = false;
            TreeNode node = invTree.SelectedNode;
            if (node == null)
            {
                e.Cancel = true;
            }
            else
            {
                #region Folder context menu
                if (node.Tag is InventoryFolder)
                {
                    InventoryFolder folder = (InventoryFolder)node.Tag;
                    ctxInv.Items.Clear();

                    ToolStripMenuItem ctxItem;

                    if (folder.PreferredType >= FolderType.EnsembleStart &&
                        folder.PreferredType <= FolderType.EnsembleEnd)
                    {
                        ctxItem = new ToolStripMenuItem("Fix type", null, OnInvContextClick) { Name = "fix_type" };
                        ctxInv.Items.Add(ctxItem);
                        ctxInv.Items.Add(new ToolStripSeparator());
                    }

                    ctxItem = new ToolStripMenuItem("New Folder", null, OnInvContextClick) { Name = "new_folder" };
                    ctxInv.Items.Add(ctxItem);

                    ctxItem = new ToolStripMenuItem("New Note", null, OnInvContextClick) { Name = "new_notecard" };
                    ctxInv.Items.Add(ctxItem);

                    ctxItem = new ToolStripMenuItem("New Script", null, OnInvContextClick) { Name = "new_script" };
                    ctxInv.Items.Add(ctxItem);

                    ctxItem = new ToolStripMenuItem("Refresh", null, OnInvContextClick) { Name = "refresh" };
                    ctxInv.Items.Add(ctxItem);

                    ctxItem = new ToolStripMenuItem("Backup...", null, OnInvContextClick) { Name = "backup" };
                    ctxInv.Items.Add(ctxItem);

                    ctxInv.Items.Add(new ToolStripSeparator());

                    ctxItem = new ToolStripMenuItem("Expand", null, OnInvContextClick) { Name = "expand" };
                    ctxInv.Items.Add(ctxItem);

                    ctxItem = new ToolStripMenuItem("Expand All", null, OnInvContextClick) { Name = "expand_all" };
                    ctxInv.Items.Add(ctxItem);

                    ctxItem = new ToolStripMenuItem("Collapse", null, OnInvContextClick) { Name = "collapse" };
                    ctxInv.Items.Add(ctxItem);

                    if (folder.PreferredType == FolderType.Trash)
                    {
                        ctxItem = new ToolStripMenuItem("Empty Trash", null, OnInvContextClick) { Name = "empty_trash" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (folder.PreferredType == FolderType.LostAndFound)
                    {
                        ctxItem = new ToolStripMenuItem("Empty Lost and Found", null, OnInvContextClick)
                        {
                            Name = "empty_lost_found"
                        };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (folder.PreferredType == FolderType.None ||
                        folder.PreferredType == FolderType.Outfit)
                    {
                        ctxItem = new ToolStripMenuItem("Rename", null, OnInvContextClick) { Name = "rename_folder" };
                        ctxInv.Items.Add(ctxItem);

                        ctxInv.Items.Add(new ToolStripSeparator());

                        ctxItem = new ToolStripMenuItem("Cut", null, OnInvContextClick) { Name = "cut_folder" };
                        ctxInv.Items.Add(ctxItem);

                        ctxItem = new ToolStripMenuItem("Copy", null, OnInvContextClick) { Name = "copy_folder" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (instance.InventoryClipboard != null)
                    {
                        ctxItem = new ToolStripMenuItem("Paste", null, OnInvContextClick) { Name = "paste_folder" };
                        ctxInv.Items.Add(ctxItem);

                        if (instance.InventoryClipboard.Item is InventoryItem)
                        {
                            ctxItem = new ToolStripMenuItem("Paste as Link", null, OnInvContextClick)
                            {
                                Name = "paste_folder_link"
                            };
                            ctxInv.Items.Add(ctxItem);
                        }
                    }

                    if (folder.PreferredType == FolderType.None ||
                        folder.PreferredType == FolderType.Outfit)
                    {
                        ctxItem = new ToolStripMenuItem("Delete", null, OnInvContextClick) { Name = "delete_folder" };
                        ctxInv.Items.Add(ctxItem);

                        ctxInv.Items.Add(new ToolStripSeparator());
                    }

                    if (folder.PreferredType == FolderType.None || folder.PreferredType == FolderType.Outfit)
                    {
                        var isAppearanceManagerBusy = Client.Appearance.ManagerBusy;

                        ctxItem = new ToolStripMenuItem("Take off Items", null, OnInvContextClick)
                        {
                            Name = "outfit_take_off",
                            Enabled = !isAppearanceManagerBusy
                        };
                        ctxInv.Items.Add(ctxItem);

                        ctxItem = new ToolStripMenuItem("Add to Outfit", null, OnInvContextClick)
                        {
                            Name = "outfit_add",
                            Enabled = !isAppearanceManagerBusy
                        };
                        ctxInv.Items.Add(ctxItem);

                        ctxItem = new ToolStripMenuItem("Replace Outfit", null, OnInvContextClick)
                        {
                            Name = "outfit_replace",
                            Enabled = !isAppearanceManagerBusy
                        };
                        ctxInv.Items.Add(ctxItem);
                    }

                    instance.ContextActionManager.AddContributions(ctxInv, folder);
                    #endregion Folder context menu
                }
                else if (node.Tag is InventoryItem tag)
                {
                    #region Item context menu
                    var item = instance.COF.ResolveInventoryLink(tag) ?? tag;
                    ctxInv.Items.Clear();

                    ToolStripMenuItem ctxItem;


                    if (item.InventoryType == InventoryType.LSL)
                    {
                        ctxItem = new ToolStripMenuItem("Edit script", null, OnInvContextClick) { Name = "edit_script" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (item.AssetType == AssetType.Texture)
                    {
                        ctxItem = new ToolStripMenuItem("View", null, OnInvContextClick) { Name = "view_image" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (item.InventoryType == InventoryType.Landmark)
                    {
                        ctxItem = new ToolStripMenuItem("Teleport", null, OnInvContextClick) { Name = "lm_teleport" };
                        ctxInv.Items.Add(ctxItem);

                        ctxItem = new ToolStripMenuItem("Info", null, OnInvContextClick) { Name = "lm_info" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (item.InventoryType == InventoryType.Notecard)
                    {
                        ctxItem = new ToolStripMenuItem("Open", null, OnInvContextClick) { Name = "notecard_open" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (item.InventoryType == InventoryType.Gesture)
                    {
                        ctxItem = new ToolStripMenuItem("Play", null, OnInvContextClick) { Name = "gesture_play" };
                        ctxInv.Items.Add(ctxItem);

                        ctxItem = new ToolStripMenuItem("Info", null, OnInvContextClick) { Name = "gesture_info" };
                        ctxInv.Items.Add(ctxItem);

                        if (instance.Client.Self.ActiveGestures.ContainsKey(item.UUID))
                        {
                            ctxItem = new ToolStripMenuItem("Deactivate", null, OnInvContextClick)
                            {
                                Name = "gesture_deactivate"
                            };
                            ctxInv.Items.Add(ctxItem);
                        }
                        else
                        {
                            ctxItem = new ToolStripMenuItem("Activate", null, OnInvContextClick)
                            {
                                Name = "gesture_activate"
                            };
                            ctxInv.Items.Add(ctxItem);
                        }
                    }

                    if (item.InventoryType == InventoryType.Animation)
                    {
                        if (!Client.Self.SignaledAnimations.ContainsKey(item.AssetUUID))
                        {
                            ctxItem = new ToolStripMenuItem("Play", null, OnInvContextClick) { Name = "animation_play" };
                            ctxInv.Items.Add(ctxItem);
                        }
                        else
                        {
                            ctxItem = new ToolStripMenuItem("Stop", null, OnInvContextClick) { Name = "animation_stop" };
                            ctxInv.Items.Add(ctxItem);
                        }
                    }

                    if (item.InventoryType == InventoryType.Object)
                    {
                        ctxItem = new ToolStripMenuItem("Rez inworld", null, OnInvContextClick) { Name = "rez_inworld" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    ctxItem = new ToolStripMenuItem("Rename", null, OnInvContextClick) { Name = "rename_item" };
                    ctxInv.Items.Add(ctxItem);

                    ctxInv.Items.Add(new ToolStripSeparator());

                    ctxItem = new ToolStripMenuItem("Cut", null, OnInvContextClick) { Name = "cut_item" };
                    ctxInv.Items.Add(ctxItem);

                    ctxItem = new ToolStripMenuItem("Copy", null, OnInvContextClick) { Name = "copy_item" };
                    ctxInv.Items.Add(ctxItem);

                    if (instance.InventoryClipboard != null)
                    {
                        ctxItem = new ToolStripMenuItem("Paste", null, OnInvContextClick) { Name = "paste_item" };
                        ctxInv.Items.Add(ctxItem);

                        if (instance.InventoryClipboard.Item is InventoryItem)
                        {
                            ctxItem = new ToolStripMenuItem("Paste as Link", null, OnInvContextClick)
                            {
                                Name = "paste_item_link"
                            };
                            ctxInv.Items.Add(ctxItem);
                        }
                    }

                    ctxItem = new ToolStripMenuItem("Delete", null, OnInvContextClick) { Name = "delete_item" };

                    if (IsAttached(item) || IsWorn(item))
                    {
                        ctxItem.Enabled = false;
                    }
                    ctxInv.Items.Add(ctxItem);

                    if (item.InventoryType == InventoryType.Object && IsAttached(item))
                    {
                        ctxItem = new ToolStripMenuItem("Touch", null, OnInvContextClick) { Name = "touch" };
                        //TODO: add RLV support
                        var kvp = Client.Network.CurrentSim.ObjectsPrimitives.FirstOrDefault(
                            p => p.Value.ParentID == Client.Self.LocalID
                                 && OutfitManager.GetAttachmentItemID(p.Value) == item.UUID);
                        if (kvp.Value != null)
                        {
                            var attached = kvp.Value;
                            ctxItem.Enabled = (attached.Flags & PrimFlags.Touch) != 0;
                        }
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (IsAttached(item) && instance.COF.CanDetachItem(item).Result)
                    {
                        ctxItem =
                            new ToolStripMenuItem("Detach from yourself", null, OnInvContextClick) { Name = "detach" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (!IsAttached(item) &&
                        (item.InventoryType == InventoryType.Object || item.InventoryType == InventoryType.Attachment) &&
                        instance.COF.CanAttachItem(item).Result)
                    {
                        ToolStripMenuItem ctxItemAttach = new ToolStripMenuItem("Attach to");
                        ctxInv.Items.Add(ctxItemAttach);

                        ToolStripMenuItem ctxItemAttachHUD = new ToolStripMenuItem("Attach to HUD");
                        ctxInv.Items.Add(ctxItemAttachHUD);

                        foreach (AttachmentPoint pt in Enum.GetValues(typeof(AttachmentPoint)))
                        {
                            if (!pt.ToString().StartsWith("HUD"))
                            {
                                string name = Utils.EnumToText(pt);

                                InventoryItem alreadyAttached = null;
                                if ((alreadyAttached = AttachmentAt(pt)) != null)
                                {
                                    name += $" ({alreadyAttached.Name})";
                                }

                                ToolStripMenuItem ptItem =
                                    new ToolStripMenuItem(name, null, OnInvContextClick)
                                    {
                                        Name = "attach_to",
                                        Tag = pt
                                    };
                                ptItem.Name = "attach_to";
                                ctxItemAttach.DropDownItems.Add(ptItem);
                            }
                            else
                            {
                                string name = Utils.EnumToText(pt).Substring(3);

                                InventoryItem alreadyAttached = null;
                                if ((alreadyAttached = AttachmentAt(pt)) != null)
                                {
                                    name += $" ({alreadyAttached.Name})";
                                }

                                ToolStripMenuItem ptItem =
                                    new ToolStripMenuItem(name, null, OnInvContextClick)
                                    {
                                        Name = "attach_to",
                                        Tag = pt
                                    };
                                ptItem.Name = "attach_to";
                                ctxItemAttachHUD.DropDownItems.Add(ptItem);
                            }
                        }

                        ctxItem = new ToolStripMenuItem("Add to Worn", null, OnInvContextClick)
                        {
                            Name = "wear_attachment_add"
                        };
                        ctxInv.Items.Add(ctxItem);

                        ctxItem = new ToolStripMenuItem("Wear", null, OnInvContextClick) { Name = "wear_attachment" };
                        ctxInv.Items.Add(ctxItem);
                    }

                    if (item is InventoryWearable wearable)
                    {
                        ctxInv.Items.Add(new ToolStripSeparator());

                        if (IsWorn(wearable))
                        {
                            ctxItem = new ToolStripMenuItem("Take off", null, OnInvContextClick) { Name = "wearable_take_off" };
                            ctxInv.Items.Add(ctxItem);
                        }
                        else
                        {
                            switch (wearable.WearableType)
                            {
                                case WearableType.Invalid:
                                    break;
                                case WearableType.Alpha:
                                case WearableType.Gloves:
                                case WearableType.Jacket:
                                case WearableType.Pants:
                                case WearableType.Physics:
                                case WearableType.Shirt:
                                case WearableType.Shoes:
                                case WearableType.Socks:
                                case WearableType.Skirt:
                                case WearableType.Tattoo:
                                case WearableType.Underpants:
                                case WearableType.Undershirt:
                                case WearableType.Universal:
                                    ctxItem = new ToolStripMenuItem("Add to Worn", null, OnInvContextClick) { Name = "wearable_add" };
                                    ctxInv.Items.Add(ctxItem);
                                    goto default;
                                default:
                                    ctxItem = new ToolStripMenuItem("Wear", null, OnInvContextClick) { Name = "wearable_wear" };
                                    ctxInv.Items.Add(ctxItem);
                                    break;
                            }
                        }
                    }

                    instance.ContextActionManager.AddContributions(ctxInv, item);
                    #endregion Item context menu
                }
            }
        }

        private void RunBackgroundTask(Func<CancellationToken, Task> work, Action uiCallback = null, int timeoutSeconds = 120)
        {
            Task.Run(async () =>
            {
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                    {
                        await work(cts.Token);
                    }
                }
                catch (TaskCanceledException ex)
                {
                    Logger.Error("Timed out running inventory console background task", ex);
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception while running inventory console background task", ex);
                }

                if (uiCallback != null)
                {
                    RunOnUi(() => uiCallback());
                }
            });
        }

        #region Context menu folder
        private async void OnInvContextClick(object sender, EventArgs e)
        {
            if (!(invTree.SelectedNode?.Tag is InventoryBase))
            {
                return;
            }

            string cmd = ((ToolStripMenuItem)sender).Name;

            if (invTree.SelectedNode.Tag is InventoryFolder folder)
            {
                #region Folder actions

                switch (cmd)
                {
                    case "refresh":
                        foreach (TreeNode old in invTree.SelectedNode.Nodes)
                        {
                            if (old != null && !(old.Tag is InventoryFolder))
                            {
                                RemoveNode(old);
                            }
                        }
                        try
                        {
                            await FetchFolder(folder.UUID, folder.OwnerID, true, inventoryUpdateCancelToken?.Token ?? CancellationToken.None).ConfigureAwait(true);
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error fetching folder: {ex.Message}", Client);
                        }
                        break;

                    case "backup":
                        (new InventoryBackup(instance, folder.UUID)).Show();
                        break;

                    case "expand":
                        invTree.SelectedNode.Expand();
                        break;

                    case "expand_all":
                        invTree.SelectedNode.ExpandAll();
                        break;

                    case "collapse":
                        invTree.SelectedNode.Collapse();
                        break;

                    case "new_folder":
                        newItemName = "New folder";
                        Client.Inventory.CreateFolder(folder.UUID, "New folder");
                        break;

                    case "fix_type":
                        Client.Inventory.UpdateFolderProperties(folder.UUID, folder.ParentUUID, folder.Name, FolderType.None);
                        invTree.Sort();
                        break;

                    case "new_notecard":
                        Client.Inventory.RequestCreateItem(folder.UUID, "New Note", "Radegast note: " + DateTime.Now.ToString(CultureInfo.InvariantCulture),
                            AssetType.Notecard, UUID.Zero, InventoryType.Notecard, PermissionMask.All, NotecardCreated);
                        break;

                    case "new_script":
                        Client.Inventory.RequestCreateItem(folder.UUID, "New script", "Radegast script: " + DateTime.Now.ToString(CultureInfo.InvariantCulture),
                            AssetType.LSLText, UUID.Zero, InventoryType.LSL, PermissionMask.All, ScriptCreated);
                        break;

                    case "cut_folder":
                        instance.InventoryClipboard = new InventoryClipboard(ClipboardOperation.Cut, folder);
                        break;

                    case "copy_folder":
                        instance.InventoryClipboard = new InventoryClipboard(ClipboardOperation.Copy, folder);
                        break;

                    case "paste_folder":
                        PerformClipboardOperation(invTree.SelectedNode.Tag as InventoryFolder);
                        break;

                    case "paste_folder_link":
                        PerformLinkOperation(invTree.SelectedNode.Tag as InventoryFolder);
                        break;


                    case "delete_folder":
                        var trash = Client.Inventory.FindFolderForType(FolderType.Trash);
                        if (trash == Inventory.RootFolder.UUID)
                        {
                            ThreadPool.QueueUserWorkItem(sync =>
                            {
                                trashCreated.Reset();
                                trash = Client.Inventory.CreateFolder(Inventory.RootFolder.UUID, "Trash", FolderType.Trash);
                                trashCreated.WaitOne(20 * 1000, false);
                                Thread.Sleep(200);
                                Client.Inventory.MoveFolder(folder.UUID, trash);
                            });
                            return;
                        }

                        await Client.Inventory.MoveFolderAsync(folder.UUID, trash);
                        break;

                    case "empty_trash":
                        {
                            DialogResult res = MessageBox.Show("Are you sure you want to empty your trash?", "Confirmation", MessageBoxButtons.OKCancel);
                            if (res == DialogResult.OK)
                            {
                                await Client.Inventory.EmptyTrashAsync();
                            }
                        }
                        break;

                    case "empty_lost_found":
                        {
                            DialogResult res = MessageBox.Show("Are you sure you want to empty your lost and found folder?", "Confirmation", MessageBoxButtons.OKCancel);
                            if (res == DialogResult.OK)
                            {
                                await Client.Inventory.EmptyLostAndFoundAsync();
                            }
                        }
                        break;

                    case "rename_folder":
                        invTree.SelectedNode.BeginEdit();
                        break;

                    case "outfit_replace":
                        appearanceWasBusy = Client.Appearance.ManagerBusy;
                        RunBackgroundTask(async (cancellationToken) =>
                        {
                            await instance.COF.ReplaceOutfit(folder.UUID, cancellationToken);
                        }, UpdateWornLabels);
                        break;
                    case "outfit_add":
                        var addToOutfit = GetInventoryItemsForOutFit(folder);
                        appearanceWasBusy = Client.Appearance.ManagerBusy;
                        RunBackgroundTask(async (cancellationToken) =>
                        {
                            await instance.COF.AddToOutfit(addToOutfit, false, cancellationToken);
                        }, UpdateWornLabels);
                        break;
                    case "outfit_take_off":
                        var removeFromOutfit = GetInventoryItemsForOutFit(folder);
                        appearanceWasBusy = Client.Appearance.ManagerBusy;
                        RunBackgroundTask(async (cancellationToken) =>
                        {
                            await instance.COF.RemoveFromOutfit(removeFromOutfit, cancellationToken);
                        }, UpdateWornLabels);
                        break;
                }
                #endregion
            }
            else if (invTree.SelectedNode.Tag is InventoryItem item)
            {
                #region Item actions

                // Copy, cut, and delete works on links directly
                // The rest operate on the item that is pointed by the link
                if (cmd != "copy_item" && cmd != "cut_item" && cmd != "delete_item")
                {
                    item = instance.COF.ResolveInventoryLink(item);
                    if (item == null)
                    {
                        return;
                    }
                }

                switch (cmd)
                {
                    case "copy_item":
                        instance.InventoryClipboard = new InventoryClipboard(ClipboardOperation.Copy, item);
                        break;

                    case "cut_item":
                        instance.InventoryClipboard = new InventoryClipboard(ClipboardOperation.Cut, item);
                        break;

                    case "paste_item":
                        PerformClipboardOperation(invTree.SelectedNode.Parent.Tag as InventoryFolder);
                        break;

                    case "paste_item_link":
                        PerformLinkOperation(invTree.SelectedNode.Parent.Tag as InventoryFolder);
                        break;

                    case "delete_item":
                        var trash = Client.Inventory.FindFolderForType(FolderType.Trash);
                        if (trash == Inventory.RootFolder.UUID)
                        {
                            ThreadPool.QueueUserWorkItem(sync =>
                            {
                                trashCreated.Reset();
                                trash = Client.Inventory.CreateFolder(Inventory.RootFolder.UUID, "Trash", FolderType.Trash);
                                trashCreated.WaitOne(20 * 1000, false);
                                Thread.Sleep(200);
                                Client.Inventory.MoveItem(item.UUID, trash, item.Name);
                            });
                            return;
                        }

                        Client.Inventory.MoveItem(item.UUID, Client.Inventory.FindFolderForType(FolderType.Trash), item.Name);
                        break;

                    case "rename_item":
                        invTree.SelectedNode.BeginEdit();
                        break;

                    case "touch":
                        var kvp = Client.Network.CurrentSim.ObjectsPrimitives.FirstOrDefault(
                            p => p.Value.ParentID == Client.Self.LocalID
                                 && OutfitManager.GetAttachmentItemID(p.Value) == item.UUID);
                        if (kvp.Value != null)
                        {
                            var attached = kvp.Value;
                            Client.Self.Grab(attached.LocalID,
                                Vector3.Zero, Vector3.Zero, Vector3.Zero, 0,
                                Vector3.Zero, Vector3.Zero, Vector3.Zero);
                            Thread.Sleep(100);
                            Client.Self.DeGrab(attached.LocalID, Vector3.Zero, Vector3.Zero, 0,
                                Vector3.Zero, Vector3.Zero, Vector3.Zero);
                        }
                        break;

                    case "detach":
                        RunBackgroundTask(async (cancellationtoken) =>
                        {
                            await instance.COF.Detach(item, cancellationtoken);

                            UpdateLabelsFor(item);
                        }, UpdateWornLabels);
                        break;

                    case "wear_attachment":
                        RunBackgroundTask(async (cancellationtoken) =>
                        {
                            await instance.COF.Attach(item, AttachmentPoint.Default, true, cancellationtoken);
                        }, UpdateWornLabels);
                        break;

                    case "wear_attachment_add":
                        RunBackgroundTask(async (cancellationtoken) =>
                        {
                            await instance.COF.Attach(item, AttachmentPoint.Default, false, cancellationtoken);
                        }, UpdateWornLabels);
                        break;

                    case "attach_to":
                        AttachmentPoint pt = (AttachmentPoint)((ToolStripMenuItem)sender).Tag;

                        RunBackgroundTask(async (cancellationtoken) =>
                        {
                            await instance.COF.Attach(item, pt, true, cancellationtoken);
                        }, UpdateWornLabels);
                        break;

                    case "edit_script":
                        ScriptEditor se = new ScriptEditor(instance, (InventoryLSL)item);
                        se.Detached = true;
                        return;

                    case "view_image":
                        UpdateItemInfo(item);
                        break;

                    case "wearable_take_off":
                        appearanceWasBusy = Client.Appearance.ManagerBusy;
                        RunBackgroundTask(async (cancellationtoken) =>
                        {
                            await instance.COF.RemoveFromOutfit(item, cancellationtoken);

                            UpdateLabelsFor(item);
                        }, UpdateWornLabels);
                        break;

                    case "wearable_wear":
                        appearanceWasBusy = Client.Appearance.ManagerBusy;
                        RunBackgroundTask(async (cancellationtoken) =>
                        {
                            await instance.COF.AddToOutfit(item, true, cancellationtoken);
                        }, UpdateWornLabels);
                        break;

                    case "wearable_add":
                        appearanceWasBusy = Client.Appearance.ManagerBusy;
                        RunBackgroundTask(async (cancellationtoken) =>
                        {
                            await instance.COF.AddToOutfit(item, false, cancellationtoken);
                        }, UpdateWornLabels);
                        break;

                    case "lm_teleport":
                        instance.ShowNotificationInChat($"Teleporting to {item.Name}");
                        Client.Self.RequestTeleport(item.AssetUUID);
                        break;

                    case "lm_info":
                        UpdateItemInfo(item);
                        break;

                    case "notecard_open":
                        UpdateItemInfo(item);
                        break;

                    case "gesture_info":
                        UpdateItemInfo(item);
                        break;

                    case "gesture_play":
                        Client.Self.PlayGesture(item.AssetUUID);
                        break;

                    case "gesture_activate":
                        instance.Client.Self.ActivateGesture(item.UUID, item.AssetUUID);
                        invTree.SelectedNode.Text = ItemLabel(item, false);
                        break;

                    case "gesture_deactivate":
                        instance.Client.Self.DeactivateGesture(item.UUID);
                        invTree.SelectedNode.Text = ItemLabel(item, false);
                        break;

                    case "animation_play":
                        Dictionary<UUID, bool> anim = new Dictionary<UUID, bool> { { item.AssetUUID, true } };
                        Client.Self.Animate(anim, true);
                        break;

                    case "animation_stop":
                        Dictionary<UUID, bool> animStop = new Dictionary<UUID, bool> { { item.AssetUUID, false } };
                        Client.Self.Animate(animStop, true);
                        break;

                    case "rez_inworld":
                        instance.MediaManager.PlayUISound(UISounds.ObjectRez);
                        Vector3 rezpos = new Vector3(2, 0, 0);
                        rezpos = Client.Self.SimPosition + rezpos * Client.Self.Movement.BodyRotation;
                        Client.Inventory.RequestRezFromInventory(Client.Network.CurrentSim, Quaternion.Identity, rezpos, item);
                        break;
                }
                #endregion
            }
        }

        private List<InventoryItem> GetInventoryItemsForOutFit(InventoryFolder folder)
        {
            List<InventoryItem> outfitItems = new List<InventoryItem>();
            foreach (InventoryBase item in Inventory.GetContents(folder))
            {
                if (item is InventoryItem inventoryItem)
                {
                    outfitItems.Add(inventoryItem);
                }
                if (item is InventoryFolder inventoryFolder)
                {
                    outfitItems.AddRange(GetInventoryItemsForOutFit(inventoryFolder));
                }
            }
            return outfitItems;
        }

        private void NotecardCreated(bool success, InventoryItem item)
        {
            RunOnUi(() =>
            {
                if (!success)
                {
                    instance.ShowNotificationInChat("Creation of notecard failed");
                    return;
                }

                instance.ShowNotificationInChat("New notecard created, enter notecard name and press enter", ChatBufferTextStyle.Invisible);
                var node = FindNodeForItem(item.ParentUUID);
                node?.Expand();
                node = FindNodeForItem(item.UUID);
                if (node != null)
                {
                    invTree.SelectedNode = node;
                    node.BeginEdit();
                }
            });
        }

        private void ScriptCreated(bool success, InventoryItem item)
        {
            RunOnUi(() =>
            {
                if (!success)
                {
                    instance.ShowNotificationInChat("Creation of script failed");
                    return;
                }

                instance.ShowNotificationInChat("New script created, enter script name and press enter", ChatBufferTextStyle.Invisible);
                var node = FindNodeForItem(item.ParentUUID);
                node?.Expand();
                node = FindNodeForItem(item.UUID);
                if (node != null)
                {
                    invTree.SelectedNode = node;
                    node.BeginEdit();
                }
            });
        }

        private void PerformClipboardOperation(InventoryFolder dest)
        {
            if (instance.InventoryClipboard == null) return;

            if (dest == null) return;

            switch (instance.InventoryClipboard.Operation)
            {
                case ClipboardOperation.Cut:
                    {
                        switch (instance.InventoryClipboard.Item)
                        {
                            case InventoryItem _:
                                Client.Inventory.MoveItem(instance.InventoryClipboard.Item.UUID, dest.UUID, instance.InventoryClipboard.Item.Name);
                                break;
                            case InventoryFolder _:
                                {
                                    if (instance.InventoryClipboard.Item.UUID != dest.UUID)
                                    {
                                        Client.Inventory.MoveFolder(instance.InventoryClipboard.Item.UUID, dest.UUID);
                                    }

                                    break;
                                }
                        }

                        instance.InventoryClipboard = null;
                        break;
                    }

                case ClipboardOperation.Copy when instance.InventoryClipboard.Item is InventoryItem:
                    Client.Inventory.RequestCopyItem(instance.InventoryClipboard.Item.UUID, dest.UUID, instance.InventoryClipboard.Item.Name, instance.InventoryClipboard.Item.OwnerID, target =>
                    {
                    }
                    );
                    break;
                case ClipboardOperation.Copy:
                    {
                        if (instance.InventoryClipboard.Item is InventoryFolder)
                        {
                            ThreadPool.QueueUserWorkItem(state =>
                            {
                                UUID newFolderID = Client.Inventory.CreateFolder(dest.UUID, instance.InventoryClipboard.Item.Name, FolderType.None);
                                Thread.Sleep(500);

                                // FIXME: for some reason copying a bunch of items in one operation does not work

                                //List<UUID> items = new List<UUID>();
                                //List<UUID> folders = new List<UUID>();
                                //List<string> names = new List<string>();
                                //UUID oldOwner = UUID.Zero;

                                foreach (InventoryBase oldItem in Inventory.GetContents((InventoryFolder)instance.InventoryClipboard.Item))
                                {
                                    //folders.Add(newFolderID);
                                    //names.Add(oldItem.Name);
                                    //items.Add(oldItem.UUID);
                                    //oldOwner = oldItem.OwnerID;
                                    Client.Inventory.RequestCopyItem(oldItem.UUID, newFolderID, oldItem.Name, oldItem.OwnerID, target => { });
                                }

                                //if (folders.Count > 0)
                                //{
                                //    Client.Inventory.RequestCopyItems(items, folders, names, oldOwner, (InventoryBase target) => { });
                                //}
                            }
                            );
                        }

                        break;
                    }
            }
        }

        private void PerformLinkOperation(InventoryFolder dest)
        {
            if (instance.InventoryClipboard == null || dest == null) return;

            Client.Inventory.CreateLink(dest.UUID, instance.InventoryClipboard.Item,
                (success, item) =>
                {
                    if (success)
                    {
                        Client.Inventory.RequestFetchInventory(item.UUID, item.OwnerID);
                    }
                });
        }

        #endregion

        private void UpdateWornLabels()
        {
            if (this.IsHandleCreated && this.InvokeRequired)
            {
                RunOnUi(() => UpdateWornLabels());
                return;
            }

            invTree.BeginUpdate();
            foreach (var wearable in Client.Appearance.GetWearables())
            {
                UpdateLabelsFor(wearable, suspendLayout: false);
            }
            invTree.EndUpdate();
        }

        private void UpdateLabelsFor(OpenMetaverse.AppearanceManager.WearableData wearable, bool suspendLayout = true)
        {
            UUID itemUUID = wearable.AssetType == AssetType.Link ? wearable.AssetID : wearable.ItemID;
            UpdateLabelsFor(itemUUID, suspendLayout);
        }

        private void UpdateLabelsFor(InventoryBase item, bool suspendLayout = true)
        {
            UUID itemUUID = item.UUID;
            if (item is InventoryItem inventoryItem)
            {
                itemUUID = inventoryItem.ActualUUID;
            }

            UpdateLabelsFor(itemUUID, suspendLayout);
        }

        private void UpdateLabelsFor(UUID assertId, bool suspendLayout = true)
        {
            if (this.IsHandleCreated && this.InvokeRequired)
            {
                RunOnUi(() => UpdateLabelsFor(assertId));
                return;
            }

            if (suspendLayout)
            {
                invTree.SuspendLayout();
            }

            TreeNode itemNode = FindNodeForItem(assertId);
            if (itemNode != null)
            {
                itemNode.Text = ItemLabel((InventoryBase)itemNode.Tag, false);
            }

            List<InventoryNode> links = Client.Inventory.Store.FindAllLinks(assertId);
            foreach (var link in links)
            {
                if (link.Data is InventoryItem item)
                {
                    TreeNode linkNode = FindNodeForItem(item.UUID);
                    if (linkNode != null)
                    {
                        linkNode.Text = ItemLabel((InventoryBase)linkNode.Tag, false);
                    }
                }
            }
            if (suspendLayout)
            {
                invTree.EndUpdate();
            }
        }

        private void TreeView_AfterExpand(object sender, TreeViewEventArgs e)
        {
            // Check if we need to go into edit mode for new items
            if (newItemName == string.Empty) return;
            foreach (TreeNode n in e.Node.Nodes)
            {
                if (n.Name != newItemName) continue;

                n.BeginEdit();
                break;
            }
            newItemName = string.Empty;
        }

        private bool _EditingNode = false;

        private void OnLabelEditTimer(object sender)
        {
            if (!(_EditNode?.Tag is InventoryBase))
                return;

            if (this.IsHandleCreated && this.InvokeRequired)
            {
                RunOnUi(() => OnLabelEditTimer(sender));
                return;
            }

            _EditingNode = true;
            _EditNode.Text = ItemLabel((InventoryBase)_EditNode.Tag, true);
            _EditNode.BeginEdit();
        }

        private void invTree_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (!(e.Node?.Tag is InventoryBase) || (e.Node.Tag is InventoryFolder folder
                 && folder.PreferredType != FolderType.None
                 && folder.PreferredType != FolderType.Outfit))
            {
                e.CancelEdit = true;
                return;
            }

            if (_EditingNode)
            {
                _EditingNode = false;
            }
            else
            {
                e.CancelEdit = true;
                _EditNode = e.Node;
                _EditTimer.Change(20, Timeout.Infinite);
            }
        }

        private void invTree_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            // Sometimes its null on inventory refresh.
            if (e.Node is null)
            {
                return;
            }

            if (string.IsNullOrEmpty(e.Label))
            {
                if (e.Node.Tag is InventoryBase tag)
                {
                    e.Node.Text = ItemLabel(tag, false);
                }
                e.CancelEdit = true;
                return;
            }

            switch (e.Node.Tag)
            {
                case InventoryFolder folder:
                    folder.Name = e.Label;
                    Client.Inventory.UpdateFolderProperties(folder.UUID, folder.ParentUUID, folder.Name, folder.PreferredType);
                    break;
                case InventoryItem item:
                    item.Name = e.Label;
                    e.Node.Text = ItemLabel((InventoryBase)item, false);
                    Client.Inventory.MoveItem(item.UUID, item.ParentUUID, item.Name);
                    UpdateItemInfo(item);
                    break;
            }
        }

        private void invTree_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.F2 when invTree.SelectedNode != null:
                    invTree.SelectedNode.BeginEdit();
                    break;
                case Keys.F5 when invTree.SelectedNode != null:
                    {
                        if (invTree.SelectedNode.Tag is InventoryFolder folder)
                        {
                            // Start async fetch but don't block UI thread
                            _ = FetchFolder(folder.UUID, folder.OwnerID, true, inventoryUpdateCancelToken?.Token ?? CancellationToken.None);
                        }

                        break;
                    }
                case Keys.Delete when invTree.SelectedNode != null:
                    {
                        var trash = Client.Inventory.FindFolderForType(FolderType.Trash);
                        if (trash == Inventory.RootFolder.UUID)
                        {
                            trash = Client.Inventory.CreateFolder(Inventory.RootFolder.UUID, "Trash", FolderType.Trash);
                            Thread.Sleep(2000);
                        }

                        switch (invTree.SelectedNode.Tag)
                        {
                            case InventoryItem item:
                                Client.Inventory.MoveItem(item.UUID, trash);
                                break;
                            case InventoryFolder folder:
                                Client.Inventory.MoveFolder(folder.UUID, trash);
                                break;
                        }

                        break;
                    }
                case Keys.Apps when invTree.SelectedNode != null:
                    ctxInv.Show();
                    break;
            }
        }

        #region Drag and Drop
        private void invTree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            invTree.SelectedNode = e.Item as TreeNode;
            if (invTree.SelectedNode?.Tag is InventoryFolder folder && folder.PreferredType != FolderType.None)
            {
                return;
            }
            invTree.DoDragDrop(e.Item, DragDropEffects.Move);
        }

        private void invTree_DragDrop(object sender, DragEventArgs e)
        {
            if (highlightedNode != null)
            {
                highlightedNode.BackColor = invTree.BackColor;
                highlightedNode = null;
            }

            TreeNode sourceNode = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if (sourceNode == null) return;

            Point pt = ((BufferedTreeView)sender).PointToClient(new Point(e.X, e.Y));
            TreeNode destinationNode = ((BufferedTreeView)sender).GetNodeAt(pt);

            if (destinationNode == null) return;

            if (sourceNode == destinationNode) return;

            // If dropping to item within folder drop to its folder
            if (destinationNode.Tag is InventoryItem)
            {
                destinationNode = destinationNode.Parent;
            }

            if (!(destinationNode.Tag is InventoryFolder dest)) return;

            switch (sourceNode.Tag)
            {
                case InventoryItem item:
                    Client.Inventory.MoveItem(item.UUID, dest.UUID, item.Name);
                    break;
                case InventoryFolder folder:
                    Client.Inventory.MoveFolder(folder.UUID, dest.UUID);
                    break;
            }
        }

        private void invTree_DragEnter(object sender, DragEventArgs e)
        {
            TreeNode node = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            e.Effect = node == null ? DragDropEffects.None : DragDropEffects.Move;
        }

        private TreeNode highlightedNode = null;

        private void invTree_DragOver(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(typeof(TreeNode)) is TreeNode node))
            {
                e.Effect = DragDropEffects.None;
            }

            Point pt = ((BufferedTreeView)sender).PointToClient(new Point(e.X, e.Y));
            TreeNode destinationNode = ((BufferedTreeView)sender).GetNodeAt(pt);

            if (highlightedNode != destinationNode)
            {
                if (highlightedNode != null)
                {
                    highlightedNode.BackColor = invTree.BackColor;
                    highlightedNode = null;
                }

                if (destinationNode != null)
                {
                    highlightedNode = destinationNode;
                    highlightedNode.BackColor = Color.LightSlateGray;
                }
            }

            if (destinationNode == null)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;


        }
        #endregion

        private void saveAllTToolStripMenuItem_Click(object sender, EventArgs e)
        {
            (new InventoryBackup(instance, Inventory.RootFolder.UUID)).Show();
        }

        private void tbtnSystemFoldersFirst_Click(object sender, EventArgs e)
        {
            sorter.SystemFoldersFirst = tbtnSystemFoldersFirst.Checked = !sorter.SystemFoldersFirst;
            instance.GlobalSettings["inv_sort_sysfirst"] = OSD.FromBoolean(sorter.SystemFoldersFirst);
            invTree.Sort();
        }

        private void tbtbSortByName_Click(object sender, EventArgs e)
        {
            if (tbtbSortByName.Checked) return;

            tbtbSortByName.Checked = true;
            tbtnSortByDate.Checked = sorter.ByDate = false;
            instance.GlobalSettings["inv_sort_bydate"] = OSD.FromBoolean(sorter.ByDate);

            invTree.Sort();
        }

        private void tbtnSortByDate_Click(object sender, EventArgs e)
        {
            if (tbtnSortByDate.Checked) return;

            tbtbSortByName.Checked = false;
            tbtnSortByDate.Checked = sorter.ByDate = true;
            instance.GlobalSettings["inv_sort_bydate"] = OSD.FromBoolean(sorter.ByDate);

            invTree.Sort();
        }

        #region Search

        public class SearchResult
        {
            public InventoryBase Inv;
            public int Level;

            public SearchResult(InventoryBase inv, int level)
            {
                Inv = inv;
                Level = level;
            }
        }

        private List<SearchResult> searchRes;
        private string searchString;
        private readonly Dictionary<int, ListViewItem> searchItemCache = new Dictionary<int, ListViewItem>();
        private ListViewItem emptyItem = null;
        private int found;

        private void PerformRecursiveSearch(int level, UUID folderID)
        {
            var folder = Inventory[folderID];
            searchRes.Add(new SearchResult(folder, level));
            var sorted = Inventory.GetContents(folderID);

            sorted.Sort((b1, b2) =>
            {
                if (b1 is InventoryFolder && !(b2 is InventoryFolder))
                {
                    return -1;
                }
                else if (!(b1 is InventoryFolder) && b2 is InventoryFolder)
                {
                    return 1;
                }
                else
                {
                    return string.CompareOrdinal(b1.Name, b2.Name);
                }
            });

            foreach (var item in sorted)
            {
                if (item is InventoryFolder)
                {
                    PerformRecursiveSearch(level + 1, item.UUID);
                }
                else
                {
                    var it = item as InventoryItem;
                    bool add = false;

                    if (cbSrchName.Checked && it.Name.ToLower().Contains(searchString))
                    {
                        add = true;
                    }
                    else if (cbSrchDesc.Checked && it.Description.ToLower().Contains(searchString))
                    {
                        add = true;
                    }

                    if (cbSrchWorn.Checked && add &&
                        !(it.InventoryType == InventoryType.Wearable && Client.Appearance.IsItemWorn(it.ActualUUID)
                          || (it.InventoryType == InventoryType.Attachment
                              || it.InventoryType == InventoryType.Object) && IsAttached(it)))
                    {
                        add = false;
                    }

                    if (cbSrchGestures.Checked && add &&
                        it.InventoryType != InventoryType.Gesture)
                    {
                        add = false;
                    }

                    if (cbSrchRecent.Checked && add && it.CreationDate < instance.StartupTimeUTC)
                    {
                        add = false;
                    }

                    if (add)
                    {
                        found++;
                        searchRes.Add(new SearchResult(it, level + 1));
                    }
                }
            }

            if (searchRes[searchRes.Count - 1].Inv.Equals(folder))
            {
                searchRes.RemoveAt(searchRes.Count - 1);
            }
        }

        public void UpdateSearch()
        {
            found = 0;

            if (instance.MonoRuntime)
            {
                lstInventorySearch.VirtualMode = false;
                lstInventorySearch.Items.Clear();
                lstInventorySearch.VirtualMode = true;
            }

            lstInventorySearch.VirtualListSize = 0;
            searchString = txtSearch.Text.Trim().ToLower();

            //if (searchString == string.Empty && rbSrchAll.Checked)
            //{
            //    lblSearchStatus.Text = "0 results";
            //    return;
            //}

            if (emptyItem == null)
            {
                emptyItem = new ListViewItem(string.Empty);
            }

            searchRes = new List<SearchResult>(Inventory.Count);
            searchItemCache.Clear();
            PerformRecursiveSearch(0, Inventory.RootFolder.UUID);
            lstInventorySearch.VirtualListSize = searchRes.Count;
            lblSearchStatus.Text = $"{found} results";
        }

        private void lstInventorySearch_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (searchItemCache.TryGetValue(e.ItemIndex, out var value))
            {
                e.Item = value;
            }
            else if (e.ItemIndex < searchRes.Count)
            {
                InventoryBase inv = searchRes[e.ItemIndex].Inv;
                string desc = inv.Name;
                if (inv is InventoryItem inventoryItem)
                {
                    desc += $" - {inventoryItem.Description}";
                }

                ListViewItem item = new ListViewItem(desc) { Tag = searchRes[e.ItemIndex] };
                e.Item = item;
                searchItemCache[e.ItemIndex] = item;
            }
            else
            {
                e.Item = emptyItem;
            }
        }

        private void btnInvSearch_Click(object sender, EventArgs e)
        {
            UpdateSearch();
        }

        private void cbSrchName_CheckedChanged(object sender, EventArgs e)
        {
            if (!cbSrchName.Checked && !cbSrchDesc.Checked && !cbSrchCreator.Checked)
            {
                cbSrchName.Checked = true;
            }
            UpdateSearch();
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (txtSearch.Text.Trim().Length > 0)
                {
                    UpdateSearch();
                }
            }
        }

        private void lstInventorySearch_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            Graphics g = e.Graphics;
            e.DrawBackground();

            if (!(e.Item.Tag is SearchResult result))
                return;

            if (e.Item.Selected)
            {
                g.FillRectangle(SystemBrushes.Highlight, e.Bounds);
            }

            int offset = 20 * (result.Level + 1);
            Rectangle rec = new Rectangle(e.Bounds.X + offset, e.Bounds.Y, e.Bounds.Width - offset, e.Bounds.Height);

            int iconIx = 0;

            switch (result.Inv)
            {
                case InventoryFolder folder:
                    iconIx = GetDirImageIndex(folder.PreferredType.ToString().ToLower());
                    break;
                case InventoryWearable wearable:
                    iconIx = GetItemImageIndex(wearable.WearableType.ToString().ToLower());
                    break;
                case InventoryItem item:
                    iconIx = GetItemImageIndex(item.AssetType.ToString().ToLower());
                    break;
            }

            if (iconIx < 0)
            {
                iconIx = 0;
            }

            try
            {
                var icon = frmMain.ResourceImages.Images[iconIx];
                g.DrawImageUnscaled(icon, e.Bounds.X + offset - 18, e.Bounds.Y);
            }
            catch { }

            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.LineLimit))
            {
                string label = ItemLabel(result.Inv, false);
                SizeF len = e.Graphics.MeasureString(label, lstInventorySearch.Font, rec.Width, sf);

                e.Graphics.DrawString(
                    ItemLabel(result.Inv, false),
                    lstInventorySearch.Font,
                    e.Item.Selected ? SystemBrushes.HighlightText : SystemBrushes.WindowText,
                    rec,
                    sf);

                if (result.Inv is InventoryItem inv)
                {
                    string desc = inv.Description.Trim();
                    if (desc == string.Empty) return;

                    using (Font descFont = new Font(lstInventorySearch.Font, FontStyle.Italic))
                    {
                        e.Graphics.DrawString(desc,
                            descFont,
                            e.Item.Selected ? SystemBrushes.HighlightText : SystemBrushes.GrayText,
                            rec.X + len.Width + 5,
                            rec.Y,
                            sf);
                    }
                }
            }

        }

        private void lstInventorySearch_SizeChanged(object sender, EventArgs e)
        {
            chResItemName.Width = lstInventorySearch.Width - 30;
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            UpdateSearch();
        }

        private void rbSrchAll_CheckedChanged(object sender, EventArgs e)
        {
            UpdateSearch();
        }

        private void lstInventorySearch_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Apps) || (e.Control && e.KeyCode == RadegastContextMenuStrip.ContexMenuKeyCode))
            {
                lstInventorySearch_MouseClick(sender, new MouseEventArgs(MouseButtons.Right, 1, 50, 150, 0));
            }
        }

        /// <summary>
        /// Finds and highlights inventory node
        /// </summary>
        /// <param name="itemID">Inventory of ID of the item to select</param>
        public void SelectInventoryNode(UUID itemID)
        {
            TreeNode node = FindNodeForItem(itemID);
            if (node == null) { return; }
            invTree.SelectedNode = node;
            if (node.Tag is InventoryItem tag)
            {
                UpdateItemInfo(tag);
            }
        }

        private void lstInventorySearch_MouseClick(object sender, MouseEventArgs e)
        {
            if (lstInventorySearch.SelectedIndices.Count != 1)
                return;

            try
            {
                SearchResult res = searchRes[lstInventorySearch.SelectedIndices[0]];
                TreeNode node = FindNodeForItem(res.Inv.UUID);
                if (node == null) { return; }
                invTree.SelectedNode = node;
                if (e.Button == MouseButtons.Right)
                {
                    ctxInv.Show(lstInventorySearch, e.X, e.Y);
                }
            }
            catch { }
        }

        private void lstInventorySearch_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (lstInventorySearch.SelectedIndices.Count != 1)
                return;

            try
            {
                SearchResult res = searchRes[lstInventorySearch.SelectedIndices[0]];
                TreeNode node = FindNodeForItem(res.Inv.UUID);
                if (node == null) { return; }
                invTree.SelectedNode = node;
                invTree_NodeMouseDoubleClick(null, null);
            }
            catch { }

        }
        #endregion Search

        private void txtAssetID_Enter(object sender, EventArgs e)
        {
            txtAssetID.SelectAll();
        }

        private void txtInvID_Enter(object sender, EventArgs e)
        {
            txtInvID.SelectAll();
        }

        private async void copyInitialOutfitsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var c = new FolderCopy(instance);
            await c.GetFoldersAsync("Initial Outfits").ConfigureAwait(true);
        }
    }

    #region Sorter class
    // Create a node sorter that implements the IComparer interface.
    public class InvNodeSorter : System.Collections.IComparer
    {
        private int CompareFolders(InventoryFolder x, InventoryFolder y)
        {
            if (!SystemFoldersFirst) return string.CompareOrdinal(x.Name, y.Name);

            if (x.PreferredType != FolderType.None && y.PreferredType == FolderType.None)
            {
                return -1;
            }
            else if (x.PreferredType == FolderType.None && y.PreferredType != FolderType.None)
            {
                return 1;
            }
            return string.CompareOrdinal(x.Name, y.Name);
        }

        public bool SystemFoldersFirst { set; get; } = true;
        public bool ByDate { set; get; } = true;

        public int Compare(object x, object y)
        {
            TreeNode tx = x as TreeNode;
            TreeNode ty = y as TreeNode;

            switch (tx.Tag)
            {
                case InventoryFolder x_folder when ty.Tag is InventoryFolder y_folder:
                    return CompareFolders(x_folder, y_folder);
                case InventoryFolder _ when ty.Tag is InventoryItem:
                    return -1;
                case InventoryItem _ when ty.Tag is InventoryFolder:
                    return 1;
            }

            // Two items
            if (!(tx.Tag is InventoryItem item1) || !(ty.Tag is InventoryItem item2))
            {
                return 0;
            }

            if (!ByDate) return string.CompareOrdinal(item1.Name, item2.Name);

            if (item1.CreationDate < item2.CreationDate)
            {
                return 1;
            }

            if (item1.CreationDate > item2.CreationDate)
            {
                return -1;
            }
            return string.CompareOrdinal(item1.Name, item2.Name);
        }
    }
    #endregion
}
