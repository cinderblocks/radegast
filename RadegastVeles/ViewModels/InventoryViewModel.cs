/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;
using InvClipboard = Radegast.Veles.Core.InventoryClipboard;
using InvClipboardMode = Radegast.Veles.Core.InventoryClipboardMode;

namespace Radegast.Veles.ViewModels;

public partial class InventoryViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    public RadegastInstanceAvalonia Instance => _instance;
    private Inventory Inventory => Client.Inventory.Store!;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _traversalCts;
    private readonly ConcurrentDictionary<UUID, int> _foldersNeedingUpdate = new();

    // Debounce inventory tree refresh: collect dirty folders and flush once per burst.
    private readonly ConcurrentDictionary<UUID, byte> _pendingItemFolders = new();
    private Timer? _itemReceivedBatchTimer;
    private readonly object _itemBatchLock = new();

    // Suppress "Received item" notifications for a short window after teleport.
    // The server always pushes COF/outfit updates post-teleport that look like new items.
    private volatile int _teleportSuppressUntilTicks; // Environment.TickCount value
    private readonly Dictionary<UUID, InvTreeNode> _nodeCache = new();
    private Dictionary<UUID, string> _wornSlots = [];
    private readonly ConcurrentDictionary<uint, UUID> _attachmentObjects = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Inventory";

    [ObservableProperty]
    private InvTreeNode? _selectedNode;

    [ObservableProperty]
    private string _itemName = string.Empty;

    [ObservableProperty]
    private string _itemDescription = string.Empty;

    [ObservableProperty]
    private string _itemType = string.Empty;

    [ObservableProperty]
    private string _itemCreator = string.Empty;

    [ObservableProperty]
    private bool _itemNoModify;

    [ObservableProperty]
    private bool _itemNoCopy;

    [ObservableProperty]
    private bool _itemNoTransfer;

    public ObservableCollection<InvTreeNode> RootNodes { get; } = [];
    public ObservableCollection<InventorySearchResult> SearchResults { get; } = [];

    /// <summary>Looks up the raw <see cref="InventoryBase"/> for a node UUID, or null if not found.</summary>
    public InventoryBase? TryGetInventoryBase(UUID id)
    {
        Inventory.TryGetValue(id, out InventoryBase? result);
        return result;
    }

    /// <summary>Returns the cached <see cref="InvTreeNode"/> for a given UUID, or null.</summary>
    public InvTreeNode? TryGetNode(UUID id)
    {
        _nodeCache.TryGetValue(id, out var node);
        return node;
    }

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasActiveEditor;

    [ObservableProperty]
    private bool _hasActiveFilter;

    [ObservableProperty]
    private bool _hasSearchResults;

    // Suppresses "Received:" notifications while inventory is loading at login
    private bool _inventoryLoaded;

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            HasSearchResults = false;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchResults.Clear();
        HasSearchResults = false;
    }

    [ObservableProperty]
    private InventorySortMode _currentSort = InventorySortMode.ByName;

    partial void OnCurrentSortChanged(InventorySortMode value)
    {
        Dispatcher.UIThread.Post(RebuildTree);
    }

    [RelayCommand]
    private void SetSort(InventorySortMode mode) => CurrentSort = mode;

    public InventoryViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        _wornSlots = BuildWornSlots();
        RegisterClientEvents(Client);
        _instance.ClientChanged += Instance_ClientChanged;

        // Build initial tree from whatever is in the store
        BuildTree();

        // Start background traversal to fetch all folder contents (follows legacy pattern)
        StartInventoryTraversal();
    }

    public void Dispose()
    {
        _traversalCts?.Cancel();
        _traversalCts?.Dispose();
        lock (_itemBatchLock)
        {
            _itemReceivedBatchTimer?.Dispose();
            _itemReceivedBatchTimer = null;
        }
        UnregisterClientEvents(Client);
        _instance.ClientChanged -= Instance_ClientChanged;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }

    private void RegisterClientEvents(GridClient client)
    {
        client.Inventory.FolderUpdated += Inventory_FolderUpdated;
        client.Inventory.ItemReceived += Inventory_ItemReceived;
        client.Self.TeleportProgress += Self_TeleportProgress;
        client.Appearance.AppearanceSet += Appearance_AppearanceSet;
        client.Appearance.AgentWearablesReply += Appearance_AgentWearablesReply;
        client.Objects.ObjectUpdate += Objects_ObjectUpdate;
    }

    private void UnregisterClientEvents(GridClient client)
    {
        client.Inventory.FolderUpdated -= Inventory_FolderUpdated;
        client.Inventory.ItemReceived -= Inventory_ItemReceived;
        client.Self.TeleportProgress -= Self_TeleportProgress;
        client.Appearance.AppearanceSet -= Appearance_AppearanceSet;
        client.Appearance.AgentWearablesReply -= Appearance_AgentWearablesReply;
        client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
    }

    private void Instance_ClientChanged(object? sender, ClientChangedEventArgs e)
    {
        UnregisterClientEvents(e.OldClient);
        RegisterClientEvents(e.Client);
        Dispatcher.UIThread.Post(() =>
        {
            BuildTree();
            StartInventoryTraversal();
        });
    }

    #region Tree Building

    private void BuildTree()
    {
        _nodeCache.Clear();
        RootNodes.Clear();
        var root = Inventory.RootFolder;
        if (root == null)
        {
            StatusText = "Inventory not loaded";
            return;
        }

        var rootNode = CreateNode(root);
        rootNode.IsExpanded = true;
        RootNodes.Add(rootNode);

        // Add Library root immediately below My Inventory
        AddLibraryNode();

        int itemCount = CountItems(Inventory.RootNode);
        StatusText = $"Inventory ({itemCount} items)";
    }

    private void AddLibraryNode()
    {
        var libRootNode = Inventory.LibraryRootNode;
        if (libRootNode?.Data is InventoryFolder libFolder)
        {
            var libNode = CreateNode(libFolder, isLibrary: true);
            libNode.IsExpanded = false;
            RootNodes.Add(libNode);
        }
    }

    private int CountItems(OpenMetaverse.InventoryNode? node)
    {
        if (node == null) return 0;
        int count = node.Data is InventoryItem ? 1 : 0;
        if (node.Nodes == null) return count;

        List<OpenMetaverse.InventoryNode> children;
        try
        {
            children = node.Nodes.Values.ToList();
        }
        catch (Exception)
        {
            // Dictionary was modified concurrently; skip this subtree count
            return count;
        }

        foreach (var child in children)
        {
            count += CountItems(child);
        }
        return count;
    }

    private InvTreeNode CreateNode(InventoryBase item, bool isLibrary = false)
    {
        var asInvItem = item as InventoryItem;
        bool isLink = asInvItem?.IsLink() == true;
        UUID linkedId = isLink ? asInvItem!.AssetUUID : UUID.Zero;
        bool isWorn = _wornSlots.ContainsKey(item.UUID)
                   || (isLink && _wornSlots.ContainsKey(linkedId));
        string wornSlot = _wornSlots.TryGetValue(item.UUID, out var ws) ? ws
                        : (isLink && _wornSlots.TryGetValue(linkedId, out var ls)) ? ls
                        : string.Empty;

        var folderKind = item is InventoryFolder f ? f.PreferredType : FolderType.None;

        var node = new InvTreeNode
        {
            Name = item.Name ?? "(unnamed)",
            ItemId = item.UUID,
            IsFolder = item is InventoryFolder,
            FolderKind = folderKind,
            IsLibrary = isLibrary,
            TypeName = GetInventoryTypeName(item),
            IsWorn = isWorn,
            WornSlot = wornSlot,
            IsLink = isLink
        };

        _nodeCache[item.UUID] = node;

        if (item is InventoryFolder folder)
        {
            var contents = Inventory.GetContents(folder.UUID);
            foreach (var child in SortContents(contents))
            {
                var childNode = CreateNode(child, isLibrary);
                childNode.Parent = node;
                node.Children.Add(childNode);
            }
        }

        return node;
    }

    public static string GetInventoryTypeName(InventoryBase item)
    {
        return item switch
        {
            InventoryFolder    => "Folder",
            InventoryNotecard  => "Notecard",
            InventoryTexture   => "Texture",
            InventoryLSL       => "Script",
            InventoryWearable w => w.WearableType.ToString(),
            InventoryAttachment => "Attachment",
            InventoryObject    => "Object",
            InventorySound     => "Sound",
            InventoryAnimation => "Animation",
            InventoryGesture   => "Gesture",
            InventoryLandmark  => "Landmark",
            InventoryCallingCard => "Calling Card",
            InventorySnapshot  => "Snapshot",
            InventoryMaterial  => "Material",
            InventorySettings  => "Settings",
            _                  => "Item"
        };
    }

    // Convenience alias used by ItemPropertiesViewModel
    public static string GetTypeName(InventoryItem item) => GetInventoryTypeName(item);

    #endregion

    #region Selection

    partial void OnSelectedNodeChanged(InvTreeNode? value)
    {
        if (value == null)
        {
            ItemName = string.Empty;
            ItemDescription = string.Empty;
            ItemType = string.Empty;
            ItemCreator = string.Empty;
            ItemNoModify   = false;
            ItemNoCopy     = false;
            ItemNoTransfer = false;
            return;
        }

        ItemName = value.Name;
        ItemType = value.TypeName;

        // Look up the actual item from the inventory store
        Inventory.TryGetValue<InventoryItem>(value.ItemId, out var invItem);

        if (invItem != null)
        {
            ItemDescription = invItem.Description ?? string.Empty;
            ItemCreator = _instance.Names.Get(invItem.CreatorID);

            ItemNoModify   = (invItem.Permissions.OwnerMask & PermissionMask.Modify)   == 0;
            ItemNoCopy     = (invItem.Permissions.OwnerMask & PermissionMask.Copy)     == 0;
            ItemNoTransfer = (invItem.Permissions.OwnerMask & PermissionMask.Transfer) == 0;
        }
        else
        {
            ItemDescription = string.Empty;
            ItemCreator = string.Empty;
            ItemNoModify   = false;
            ItemNoCopy     = false;
            ItemNoTransfer = false;
        }

        OpenItemCommand.NotifyCanExecuteChanged();

        if (CanOpenItem())
            OpenItem();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void RefreshInventory()
    {
        // Mark all folders as needing update so traversal re-fetches
        if (Inventory.RootNode != null)
        {
            MarkAllNeedsUpdate(Inventory.RootNode);
        }
        BuildTree();
        StartInventoryTraversal();
        _instance.ShowNotificationInChat("Refreshing inventory...");
    }

    [RelayCommand]
    private async Task SearchInventory()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        HasActiveFilter = false;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        SearchResults.Clear();
        var searchLower = SearchText.ToLowerInvariant();

        try
        {
            var results = await Task.Run(() =>
            {
                var found = new List<InventorySearchResult>();
                SearchNode(Inventory.RootNode, searchLower, found, token);
                return found;
            }, token);

            if (token.IsCancellationRequested) return;

            foreach (var result in results)
            {
                SearchResults.Add(result);
            }

            StatusText = $"Found {SearchResults.Count} items matching '{SearchText}'";
            HasSearchResults = SearchResults.Count > 0;
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsSearching = false;
        }
    }

    private void SearchNode(OpenMetaverse.InventoryNode? node, string searchLower, List<InventorySearchResult> results, CancellationToken token)
    {
        if (node == null || token.IsCancellationRequested) return;

        if (node.Data is InventoryItem item)
        {
            bool nameMatch = item.Name?.ToLowerInvariant().Contains(searchLower) == true;
            bool descMatch = item.Description?.ToLowerInvariant().Contains(searchLower) == true;

            if (nameMatch || descMatch)
            {
                results.Add(new InventorySearchResult(
                    item.UUID,
                    item.Name ?? "(unnamed)",
                    GetInventoryTypeName(item),
                    item.ParentUUID));
            }
        }

        if (node.Nodes == null) return;

        List<OpenMetaverse.InventoryNode> children;
        try
        {
            children = node.Nodes.Values.ToList();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var child in children)
        {
            token.ThrowIfCancellationRequested();
            SearchNode(child, searchLower, results, token);
        }
    }

    [RelayCommand]
    private void WearItem()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var invItem)) return;

        if (invItem is InventoryWearable || invItem is InventoryObject)
        {
            if (_instance.COF != null)
            {
                _instance.COF.AddToOutfit(invItem, true, CancellationToken.None);
                _instance.ShowNotificationInChat($"Wearing {invItem.Name}...");
            }
            else
            {
                _instance.ShowNotificationInChat("Outfit manager not available yet.");
            }
        }
    }

    [RelayCommand]
    private void GiveItem()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        _instance.ShowNotificationInChat("Give item is not yet implemented.");
    }

    [RelayCommand]
    private async Task DeleteItem()
    {
        if (SelectedNode == null) return;

        Inventory.TryGetValue(SelectedNode.ItemId, out InventoryBase? invItem);

        if (invItem != null)
        {
            var trashId = Client.Inventory.FindFolderForType(FolderType.Trash);
            var node = SelectedNode;
            try
            {
                if (invItem is InventoryFolder)
                    await Client.Inventory.MoveFolderAsync(invItem.UUID, trashId);
                else
                    await Client.Inventory.MoveItemAsync(invItem.UUID, trashId, invItem.Name);

                _instance.ShowNotificationInChat($"Moved {invItem.Name} to trash.");

                // Remove from current parent and refresh trash
                node.Parent?.Children.Remove(node);
                RefreshFolderNode(RootNodes, trashId);
            }
            catch (Exception ex)
            {
                _instance.ShowNotificationInChat($"Failed to delete {invItem.Name}: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void ExpandFolder()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;

        // Request folder contents from server
        Client.Inventory.RequestFolderContents(
            SelectedNode.ItemId, Client.Self.AgentID,
            true, true, InventorySortOrder.ByDate);
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var node in _nodeCache.Values)
            if (node.IsFolder) node.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in _nodeCache.Values)
            node.IsExpanded = false;
        if (RootNodes.Count > 0)
            RootNodes[0].IsExpanded = true;
    }

    public event EventHandler<InventoryEditorRequestedEventArgs>? EditorRequested;

    [RelayCommand(CanExecute = nameof(CanOpenItem))]
    private void OpenItem()
    {
        if (SelectedNode == null) return;

        // Folder: show folder detail panel
        if (SelectedNode.IsFolder)
        {
            if (!Inventory.TryGetValue<InventoryFolder>(SelectedNode.ItemId, out var folderItem)) return;
            var folderVm = new FolderViewModel(_instance, folderItem, SelectedNode);
            HasActiveEditor = true;
            EditorRequested?.Invoke(this, new InventoryEditorRequestedEventArgs(folderItem, folderVm));
            return;
        }

        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var invItem)) return;

        CommunityToolkit.Mvvm.ComponentModel.ObservableObject editorVm;
        if (invItem is InventoryLSL lslItem)
            editorVm = new ScriptEditorViewModel(_instance, lslItem);
        else if (invItem is InventoryNotecard ncItem)
            editorVm = new NotecardViewModel(_instance, ncItem);
        else if (invItem is InventoryTexture texItem)
            editorVm = new TextureViewerViewModel(_instance, texItem);
        else if (invItem is InventorySnapshot snapItem)
            editorVm = new TextureViewerViewModel(_instance, snapItem);
        else if (invItem is InventoryLandmark lmItem)
            editorVm = new LandmarkViewModel(_instance, lmItem);
        else if (invItem is InventoryCallingCard cardItem)
            editorVm = new CallingCardViewModel(_instance, cardItem);
        else if (invItem is InventorySound sndItem)
            editorVm = new SoundViewModel(_instance, sndItem);
        else if (invItem is InventoryGesture gestItem)
            editorVm = new GestureViewModel(_instance, gestItem);
        else if (invItem is InventoryAnimation animItem)
            editorVm = new AnimationViewModel(_instance, animItem);
        else if (invItem is InventoryWearable wearItem)
            editorVm = new WearableViewModel(_instance, wearItem);
        else if (invItem is InventoryObject || invItem is InventoryAttachment)
            editorVm = new ObjectViewModel(_instance, invItem);
        else if (invItem is InventoryMaterial matItem)
            editorVm = new MaterialViewModel(_instance, matItem);
        else if (invItem is InventorySettings settItem)
            editorVm = new SettingsViewModel(_instance, settItem);
        else
            return;

        HasActiveEditor = true;
        EditorRequested?.Invoke(this, new InventoryEditorRequestedEventArgs(invItem, editorVm));
    }

    private bool CanOpenItem()
    {
        if (SelectedNode == null) return false;
        if (SelectedNode.IsFolder) return true;   // Show folder panel
        return SelectedNode.TypeName is "Script" or "Notecard" or
               "Texture" or "Snapshot" or "Landmark" or "Calling Card" or
               "Sound" or "Gesture" or "Animation" or "Object" or "Attachment" or
               "Material" or "Settings" ||
               IsWearableTypeName(SelectedNode.TypeName);
    }

    private static bool IsWearableTypeName(string typeName) => typeName is
        "Shape" or "Skin" or "Hair" or "Eyes" or "Shirt" or "Pants" or
        "Shoes" or "Socks" or "Jacket" or "Gloves" or "Undershirt" or
        "Underpants" or "Skirt" or "Alpha" or "Tattoo" or "Physics" or "Universal";

    // ── Double-click default action ──────────────────────────────────────────

    public void ExecuteDefaultAction()
    {
        if (SelectedNode == null) return;

        if (SelectedNode.IsFolder)
        {
            SelectedNode.IsExpanded = !SelectedNode.IsExpanded;
            return;
        }

        // Library items: open viewable types, rez objects — no wear/attach
        if (SelectedNode.IsLibrary)
        {
            if (SelectedNode.TypeName == "Object")
                RezObject();
            else if (CanOpenItem())
                OpenItem();
            return;
        }

        switch (SelectedNode.TypeName)
        {
            case "Landmark":
                TeleportToLandmark();
                break;
            case "Object":
            case "Attachment":
            case "Material":
            case "Settings":
                if (CanOpenItem()) OpenItem();
                break;
            case "Gesture":
            case "Animation":
            case "Notecard":
            case "Script":
            case "Texture":
            case "Snapshot":
            case "Sound":
            case "Calling Card":
                if (CanOpenItem()) OpenItem();
                break;
            default:
                // Wearables: wear on double-click, remove if already worn
                if (IsWearableTypeName(SelectedNode.TypeName))
                {
                    if (SelectedNode.IsWorn)
                        RemoveFromOutfit();
                    else
                        WearItem();
                }
                break;
        }
    }

    public bool TrySelectNode(UUID itemId)
    {
        if (_nodeCache.TryGetValue(itemId, out var node))
        {
            SelectedNode = node;
            return true;
        }
        return false;
    }

    // ── Rename ───────────────────────────────────────────────────────────────

    /// Called from code-behind after the rename dialog is confirmed.
    public async Task CommitRenameAsync(string newName)
    {
        if (SelectedNode == null) return;
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName)) return;

        var node = SelectedNode;
        if (node.IsFolder)
        {
            if (!Inventory.TryGetValue<InventoryFolder>(node.ItemId, out var folder)) return;
            node.Name = newName;
            // Pass FolderType.None to avoid AIS rejecting a type change (400 Bad Request)
            Client.Inventory.UpdateFolderProperties(folder.UUID, folder.ParentUUID, newName, FolderType.None);
        }
        else
        {
            if (!Inventory.TryGetValue<InventoryItem>(node.ItemId, out var item)) return;
            item.Name = newName;
            node.Name = newName;
            await Client.Inventory.MoveItemAsync(item.UUID, item.ParentUUID, newName);
        }

        ItemName = newName;
    }

    // ── Landmark ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void TeleportToLandmark()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryLandmark>(SelectedNode.ItemId, out var lm)) return;
        Client.Self.RequestTeleport(lm.AssetUUID);
        _instance.ShowNotificationInChat($"Teleporting to {lm.Name}...");
    }

    // ── Gestures ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void PlayGesture()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryGesture>(SelectedNode.ItemId, out var gesture)) return;
        Client.Self.PlayGesture(gesture.AssetUUID);
    }

    [RelayCommand]
    private void ToggleGesture()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryGesture>(SelectedNode.ItemId, out var gesture)) return;

        var node = SelectedNode;
        if (node.IsWorn)
        {
            Client.Self.DeactivateGesture(gesture.UUID);
            node.IsWorn   = false;
            node.WornSlot = string.Empty;
            _wornSlots.Remove(gesture.UUID);
        }
        else
        {
            Client.Self.ActivateGesture(gesture.UUID, gesture.AssetUUID);
            node.IsWorn   = true;
            node.WornSlot = "Active";
            _wornSlots[gesture.UUID] = "Active";
        }
        // Refresh after a short delay to sync with server response
        _ = Task.Delay(500).ContinueWith(t => RefreshAllWornState());
    }

    // ── Create items ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void CreateFolder()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        Client.Inventory.CreateFolder(SelectedNode.ItemId, "New Folder");
    }

    [RelayCommand]
    private void CreateNotecard()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        Client.Inventory.RequestCreateItem(
            SelectedNode.ItemId, "New Notecard", string.Empty,
            AssetType.Notecard, UUID.Random(), InventoryType.Notecard,
            PermissionMask.All, (_, _) => { });
    }

    [RelayCommand]
    private void CreateScript()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        Client.Inventory.RequestCreateItem(
            SelectedNode.ItemId, "New Script", string.Empty,
            AssetType.LSLText, UUID.Random(), InventoryType.LSL,
            PermissionMask.All, (_, _) => { });
    }

    // ── Trash ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void EmptyTrash()
    {
        _ = Client.Inventory.EmptyTrashAsync();
        _instance.ShowNotificationInChat("Emptying trash...");
    }

    [RelayCommand]
    private async Task EmptyFolder()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        var children = SelectedNode.Children.ToList();
        var trashId = Client.Inventory.FindFolderForType(FolderType.Trash);
        foreach (var child in children)
        {
            Inventory.TryGetValue(child.ItemId, out InventoryBase? inv);
            if (inv == null) continue;
            try
            {
                if (child.IsFolder)
                    await Client.Inventory.MoveFolderAsync(child.ItemId, trashId);
                else
                    await Client.Inventory.MoveItemAsync(child.ItemId, trashId, inv.Name);
            }
            catch (Exception ex)
            {
                _instance.ShowNotificationInChat($"Failed to move {inv.Name}: {ex.Message}");
            }
        }
        foreach (var child in children)
            _nodeCache.Remove(child.ItemId);
        SelectedNode.Children.Clear();
        RefreshFolderNode(RootNodes, trashId);
        _instance.ShowNotificationInChat($"Emptied folder {SelectedNode.Name}.");
    }

    [RelayCommand]
    private async Task RestoreFromTrash()
    {
        if (SelectedNode == null) return;
        var lostFoundId = Client.Inventory.FindFolderForType(FolderType.LostAndFound);
        var node = SelectedNode;
        try
        {
            if (node.IsFolder)
                await Client.Inventory.MoveFolderAsync(node.ItemId, lostFoundId);
            else
            {
                if (!Inventory.TryGetValue<InventoryItem>(node.ItemId, out var item)) return;
                await Client.Inventory.MoveItemAsync(node.ItemId, lostFoundId, item.Name);
            }
            node.Parent?.Children.Remove(node);
            RefreshFolderNode(RootNodes, lostFoundId);
            _instance.ShowNotificationInChat($"Restored {node.Name} to Lost and Found.");
        }
        catch (Exception ex)
        {
            _instance.ShowNotificationInChat($"Failed to restore {node.Name}: {ex.Message}");
        }
    }

    // ── Clipboard ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CutItem()
    {
        if (SelectedNode == null) return;
        InvClipboard.Cut(SelectedNode.ItemId, SelectedNode.Name, SelectedNode.IsFolder);
        _instance.ShowNotificationInChat($"Cut: {SelectedNode.Name}");
    }

    [RelayCommand]
    private void CopyItem()
    {
        if (SelectedNode == null) return;
        InvClipboard.Copy(SelectedNode.ItemId, SelectedNode.Name, SelectedNode.IsFolder);
        _instance.ShowNotificationInChat($"Copied: {SelectedNode.Name}");
    }

    [RelayCommand]
    private void CopyToInventory()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var libItem)) return;

        // Find the default folder for this asset type in the user's inventory
        var destFolderId = Client.Inventory.FindFolderForType(libItem.AssetType);
        if (destFolderId == UUID.Zero)
            destFolderId = Client.Inventory.FindFolderForType(AssetType.Unknown);

        Client.Inventory.RequestCopyItem(
            libItem.UUID, destFolderId, libItem.Name, Client.Self.AgentID,
            copied =>
            {
                if (copied != null)
                    _instance.ShowNotificationInChat($"Copied '{libItem.Name}' to inventory.");
                else
                    _instance.ShowNotificationInChat($"Failed to copy '{libItem.Name}' to inventory.");
            });
    }

    [RelayCommand]
    private async Task PasteItem()
    {
        if (!InvClipboard.HasContent) return;

        // Destination: selected folder, or parent of selected item
        var destNode = SelectedNode;
        if (destNode == null) return;
        if (!destNode.IsFolder) destNode = destNode.Parent;
        if (destNode == null) return;

        if (!Inventory.TryGetValue<InventoryFolder>(destNode.ItemId, out var destFolder)) return;

        if (InvClipboard.Mode == InvClipboardMode.Cut)
        {
            var movedId   = InvClipboard.ItemId;
            var movedName = InvClipboard.ItemName;   // capture before Clear()
            var isFolder  = InvClipboard.IsFolder;
            InvClipboard.Clear();

            try
            {
                if (isFolder)
                    await Client.Inventory.MoveFolderAsync(movedId, destFolder.UUID);
                else
                    await Client.Inventory.MoveItemAsync(movedId, destFolder.UUID, movedName);

                // Update visual tree after success
                var srcNode = FindNodeById(RootNodes, movedId);
                if (srcNode != null)
                {
                    srcNode.Parent?.Children.Remove(srcNode);
                    var destNode2 = FindNodeById(RootNodes, destFolder.UUID);
                    if (destNode2 != null)
                    {
                        srcNode.Parent = destNode2;
                        destNode2.Children.Add(srcNode);
                    }
                }
                else
                {
                    RefreshFolderNode(RootNodes, destFolder.UUID);
                }
                _instance.ShowNotificationInChat($"Moved {movedName} to {destFolder.Name}");
            }
            catch (Exception ex)
            {
                _instance.ShowNotificationInChat($"Failed to move {movedName}: {ex.Message}");
            }
        }
        else if (InvClipboard.Mode == InvClipboardMode.Copy)
        {
            if (InvClipboard.IsFolder)
                _instance.ShowNotificationInChat("Folder copy is not supported by the server.");
            else
                Client.Inventory.RequestCopyItem(
                    InvClipboard.ItemId, destFolder.UUID,
                    InvClipboard.ItemName, Client.Self.AgentID,
                    _ => { });
        }
    }

    [RelayCommand]
    private void PasteLinkItem()
    {
        if (!InvClipboard.HasContent || InvClipboard.IsFolder) return;

        var destNode = SelectedNode;
        if (destNode == null) return;
        if (!destNode.IsFolder) destNode = destNode.Parent;
        if (destNode == null) return;

        if (!Inventory.TryGetValue<InventoryFolder>(destNode.ItemId, out var destFolder)) return;
        if (!Inventory.TryGetValue<InventoryItem>(InvClipboard.ItemId, out var srcItem)) return;

        Client.Inventory.CreateLink(destFolder.UUID, srcItem,
            (_, created) =>
            {
                if (created != null)
                    _instance.ShowNotificationInChat($"Link to {srcItem.Name} created.");
            });
    }

    // ── Copy Asset UUID ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CopyAssetUUID()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        var clipboard = TopLevel;
        if (clipboard == null) return;
        await clipboard.SetTextAsync(SelectedNode.ItemId.ToString());
        _instance.ShowNotificationInChat($"UUID copied: {SelectedNode.ItemId}");
    }

    // TopLevel reference set by the code-behind so we can access the system clipboard
    public Avalonia.Input.Platform.IClipboard? TopLevel { get; set; }

    // ── Rez Object ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void RezObject()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var invItem)) return;
        if (invItem.AssetType != AssetType.Object) return;

        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        var pos = Client.Self.SimPosition + Client.Self.Movement.Camera.AtAxis * 2.0f;
        Client.Inventory.RequestRezFromInventory(sim, OpenMetaverse.Quaternion.Identity, pos, invItem);
        _instance.ShowNotificationInChat($"Rezzing {invItem.Name}...");
    }

    // ── Create Link ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void CreateLink()
    {
        if (SelectedNode == null || SelectedNode.IsFolder || SelectedNode.IsLink) return;
        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var srcItem)) return;

        // Create link in same folder as the source item
        Client.Inventory.CreateLink(srcItem.ParentUUID, srcItem,
            (_, created) =>
            {
                if (created != null)
                    _instance.ShowNotificationInChat($"Link to {srcItem.Name} created.");
            });
    }

    // ── Move to folder (drag-drop target) ────────────────────────────────────

    /// <summary>Moves or copies <paramref name="node"/> into <paramref name="targetFolderNode"/>.</summary>
    public async Task MoveNodeToFolder(InvTreeNode node, InvTreeNode targetFolderNode)
    {
        if (!targetFolderNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryFolder>(targetFolderNode.ItemId, out var destFolder)) return;

        try
        {
            if (node.IsFolder)
            {
                await Client.Inventory.MoveFolderAsync(node.ItemId, destFolder.UUID);
            }
            else
            {
                if (!Inventory.TryGetValue<InventoryItem>(node.ItemId, out var item)) return;
                await Client.Inventory.MoveItemAsync(node.ItemId, destFolder.UUID, item.Name);
            }
        }
        catch (Exception ex)
        {
            _instance.ShowNotificationInChat($"Failed to move {node.Name}: {ex.Message}");
            return;
        }

        // Update the visual tree only after successful server operation
        var oldParent = node.Parent;
        if (oldParent != null)
            Dispatcher.UIThread.Post(() => oldParent.Children.Remove(node));

        node.Parent = targetFolderNode;
        Dispatcher.UIThread.Post(() => targetFolderNode.Children.Add(node));
    }

    // ── Properties ───────────────────────────────────────────────────────────

    public event EventHandler<ItemPropertiesRequestedEventArgs>? PropertiesRequested;

    [RelayCommand]
    private void ShowProperties()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var invItem)) return;
        PropertiesRequested?.Invoke(this, new ItemPropertiesRequestedEventArgs(invItem));
    }

    // ── Take off / outfit folder ops ─────────────────────────────────────────

    [RelayCommand]
    private void RemoveFromOutfit()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var invItem)) return;
        if (_instance.COF != null)
        {
            _ = _instance.COF.RemoveFromOutfit(invItem, System.Threading.CancellationToken.None);
            _instance.ShowNotificationInChat($"Removing {invItem.Name}...");
        }
    }

    [RelayCommand]
    private async Task WearAtPoint(AttachmentPoint point)
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        if (!Inventory.TryGetValue<InventoryItem>(SelectedNode.ItemId, out var invItem)) return;
        if (_instance.COF == null) return;

        await _instance.COF.Attach(invItem, point, true, CancellationToken.None).ConfigureAwait(false);
        _instance.ShowNotificationInChat($"Wearing {invItem.Name} at {point}...");
    }

    [RelayCommand]
    private void WearFolder()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        if (_instance.COF == null) return;
        var items = GetFolderItems(SelectedNode.ItemId);
        if (items.Count == 0) return;
        _ = _instance.COF.AddToOutfit(items, false, System.Threading.CancellationToken.None);
        _instance.ShowNotificationInChat($"Adding {SelectedNode.Name} to outfit...");
    }

    [RelayCommand]
    private void ReplaceOutfit()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        if (_instance.COF == null) return;
        _ = _instance.COF.ReplaceOutfit(SelectedNode.ItemId, System.Threading.CancellationToken.None);
        _instance.ShowNotificationInChat($"Replacing outfit with {SelectedNode.Name}...");
    }

    [RelayCommand]
    private void RemoveFolderFromOutfit()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        if (_instance.COF == null) return;
        var items = GetFolderItems(SelectedNode.ItemId);
        if (items.Count == 0) return;
        _ = _instance.COF.RemoveFromOutfit(items, System.Threading.CancellationToken.None);
        _instance.ShowNotificationInChat($"Removing {SelectedNode.Name} from outfit...");
    }

    private List<InventoryItem> GetFolderItems(UUID folderId)
    {
        var result = new List<InventoryItem>();
        var storeNode = Inventory.GetNodeOrDefault(folderId);
        if (storeNode?.Nodes == null) return result;

        List<OpenMetaverse.InventoryNode> children;
        try { children = storeNode.Nodes.Values.ToList(); }
        catch (System.InvalidOperationException) { return result; }

        foreach (var child in children)
        {
            if (child.Data is InventoryItem item)
                result.Add(item);
        }
        return result;
    }

    // ── Special folder helpers ────────────────────────────────────────────────

    public bool IsTrashFolder(InvTreeNode node)
    {
        if (!node.IsFolder) return false;
        if (!Inventory.TryGetValue<InventoryFolder>(node.ItemId, out var folder)) return false;
        return folder.PreferredType == FolderType.Trash;
    }

    public bool IsInsideTrash(InvTreeNode node)
    {
        var trashId = Client.Inventory.FindFolderForType(FolderType.Trash);
        var current = node.Parent;
        while (current != null)
        {
            if (current.ItemId == trashId) return true;
            current = current.Parent;
        }
        return false;
    }

    public bool IsSystemFolder(InvTreeNode node)
    {
        if (!node.IsFolder) return false;
        if (!Inventory.TryGetValue<InventoryFolder>(node.ItemId, out var folder)) return false;
        return folder.PreferredType != FolderType.None;
    }

    #endregion

    #region Network Events

    private void Inventory_FolderUpdated(object? sender, FolderUpdatedEventArgs e)
    {
        if (e.Success)
        {
            _foldersNeedingUpdate.TryRemove(e.FolderID, out _);
        }
        else
        {
            // Increment retry count; traversal loop will retry
            if (_foldersNeedingUpdate.TryGetValue(e.FolderID, out var retries))
            {
                if (retries > 3)
                    _foldersNeedingUpdate.TryRemove(e.FolderID, out _);
                else
                    _foldersNeedingUpdate.TryUpdate(e.FolderID, retries + 1, retries);
            }
            return;
        }

        Dispatcher.UIThread.Post(() => RefreshFolderNode(RootNodes, e.FolderID));

        // Defer CountItems (full tree traversal) via the same batch timer used by
        // ItemReceived so that a burst of folder updates only causes one recount.
        _pendingItemFolders.TryAdd(e.FolderID, 0);
        lock (_itemBatchLock)
        {
            if (_itemReceivedBatchTimer == null)
                _itemReceivedBatchTimer = new Timer(OnItemBatchTimerFired, null,
                    TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
            else
                _itemReceivedBatchTimer.Change(
                    TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        }
    }

    private void Self_TeleportProgress(object? sender, TeleportEventArgs e)
    {
        if (e.Status == TeleportStatus.Finished || e.Status == TeleportStatus.Failed)
        {
            // Suppress inventory notifications for 8 seconds after teleport.
            // The server always pushes outfit/COF updates post-TP that look like new items.
            _teleportSuppressUntilTicks = Environment.TickCount + 8000;
        }
    }

    #endregion

    private void StartInventoryTraversal()
    {
        _traversalCts?.Cancel();
        _traversalCts?.Dispose();
        _traversalCts = new CancellationTokenSource();
        var cts = _traversalCts;

        _ = Task.Run(async () =>
        {
            // Restore from disk cache first (same as legacy Radegast)
            var cacheFile = _instance.InventoryCacheFileName;
            if (!string.IsNullOrEmpty(cacheFile) && File.Exists(cacheFile))
            {
                try { Inventory.RestoreFromDisk(cacheFile); }
                catch { /* corrupt or missing cache — proceed with full fetch */ }
                Dispatcher.UIThread.Post(() => { BuildTree(); });
            }

            try
            {
                await FetchAllFolders(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                    _instance.ShowNotificationInChat($"Inventory traversal error: {ex.Message}"));
            }
        });
    }

    private async Task FetchAllFolders(CancellationToken token)
    {
        var rootNode = Inventory.RootNode;
        if (rootNode == null) return;

        // Brief delay to let capabilities become available after login
        await Task.Delay(2000, token).ConfigureAwait(false);

        _foldersNeedingUpdate.Clear();
        TraverseAndQueueFolders(rootNode);

        while (!_foldersNeedingUpdate.IsEmpty)
        {
            token.ThrowIfCancellationRequested();

            var folderKeys = _foldersNeedingUpdate.Keys.ToList();

            Dispatcher.UIThread.Post(() =>
                StatusText = $"Loading inventory... {folderKeys.Count} folders remaining");

            var tasks = folderKeys.Select(folderId =>
                Client.Inventory.RequestFolderContents(
                    folderId, Client.Self.AgentID,
                    true, true, InventorySortOrder.ByDate, token));

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* individual folder failures handled by FolderUpdated event */ }

            // Re-traverse to find any newly-discovered folders that need updating
            TraverseAndQueueFolders(rootNode);

            // Individual folder updates are already handled by Inventory_FolderUpdated → RefreshFolderNode
            // No need for a full RebuildTree here; it would collapse user-expanded folders
        }

        Dispatcher.UIThread.Post(() =>
        {
            RebuildTree();
            int itemCount = CountItems(Inventory.RootNode);
            StatusText = $"Inventory ({itemCount} items)";
            _inventoryLoaded = true;
        });

        // Save to disk cache (background, non-blocking)
        var cacheFile = _instance.InventoryCacheFileName;
        if (!string.IsNullOrEmpty(cacheFile))
            _ = Task.Run(() => { try { Inventory.SaveToDisk(cacheFile); } catch { } });

        // Fetch library in the background after main inventory is ready
        _ = FetchLibraryFolders(token);
    }

    private async Task FetchLibraryFolders(CancellationToken token)
    {
        var libFolder = Inventory.LibraryFolder;
        if (libFolder == null) return;

        // Derive the library owner UUID from the skeleton sub-folders. The library root
        // itself has no OwnerID, but its direct children do (requires LibreMetaverse 2.6.2+).
        // When ownerID != Client.Self.AgentID, FolderContentsAsync routes via FetchLibDescendents2.
        List<InventoryBase> skeletonChildren;
        try { skeletonChildren = Inventory.GetContents(libFolder.UUID); }
        catch (Exception ex)
        {
            Logger.Warn($"Could not read library skeleton from store: {ex.Message}", Client);
            return;
        }

        var libraryOwnerID = skeletonChildren
            .OfType<InventoryFolder>()
            .Select(f => f.OwnerID)
            .FirstOrDefault(id => id != UUID.Zero);

        if (libraryOwnerID == UUID.Zero)
        {
            Logger.Warn("Could not determine library owner UUID; library items will not load", Client);
            return;
        }

        // Collect all library folders from the skeleton tree (all depths).
        var allLibFolders = new List<InventoryFolder> { libFolder };
        CollectLibraryFolders(Inventory.LibraryRootNode, allLibFolders);

        Dispatcher.UIThread.Post(() =>
            StatusText = $"Loading library... {allLibFolders.Count} folders");

        var tasks = allLibFolders.Select(f =>
            Client.Inventory.FolderContentsAsync(
                f.UUID, libraryOwnerID, true, true, InventorySortOrder.ByName, token));

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RebuildTree();
            StatusText = $"Inventory ({CountItems(Inventory.RootNode)} items)";
        });
    }

    private static void CollectLibraryFolders(OpenMetaverse.InventoryNode? node, List<InventoryFolder> result)
    {
        if (node?.Nodes == null) return;
        List<OpenMetaverse.InventoryNode> children;
        try { children = node.Nodes.Values.ToList(); }
        catch (InvalidOperationException) { return; }
        foreach (var child in children)
        {
            if (child.Data is InventoryFolder f) result.Add(f);
            CollectLibraryFolders(child, result);
        }
    }

    private void TraverseAndQueueFolders(OpenMetaverse.InventoryNode node)
    {
        if (node.NeedsUpdate && node.Data is InventoryFolder)
        {
            _foldersNeedingUpdate.TryAdd(node.Data.UUID, 0);
        }

        if (node.Nodes == null) return;

        List<OpenMetaverse.InventoryNode> children;
        try
        {
            children = node.Nodes.Values.ToList();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var child in children)
        {
            TraverseAndQueueFolders(child);
        }
    }

    private static void MarkAllNeedsUpdate(OpenMetaverse.InventoryNode node)
    {
        node.NeedsUpdate = true;

        if (node.Nodes == null) return;

        List<OpenMetaverse.InventoryNode> children;
        try
        {
            children = node.Nodes.Values.ToList();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var child in children)
        {
            MarkAllNeedsUpdate(child);
        }
    }

    private InvTreeNode? FindNodeById(ObservableCollection<InvTreeNode> nodes, UUID id)
    {
        foreach (var node in nodes)
        {
            if (node.ItemId == id) return node;
            if (node.Children.Count > 0)
            {
                var found = FindNodeById(node.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void RefreshFolderNode(ObservableCollection<InvTreeNode> nodes, UUID folderId)
    {
        foreach (var node in nodes)
        {
            if (node.ItemId == folderId && node.IsFolder)
            {
                RemoveChildrenFromCache(node);
                node.Children.Clear();
                var contents = Inventory.GetContents(folderId);
                foreach (var child in SortContents(contents))
                {
                    var childNode = CreateNode(child, node.IsLibrary);
                    childNode.Parent = node;
                    node.Children.Add(childNode);
                }
                return;
            }

            if (node.Children.Count > 0)
            {
                RefreshFolderNode(node.Children, folderId);
            }
        }
    }

    private void RemoveChildrenFromCache(InvTreeNode parent)
    {
        foreach (var child in parent.Children)
        {
            _nodeCache.Remove(child.ItemId);
            RemoveChildrenFromCache(child);
        }
    }

    private IEnumerable<InventoryBase> SortContents(IEnumerable<InventoryBase> contents)
    {
        return CurrentSort switch
        {
            InventorySortMode.ByDate =>
                contents.OrderByDescending(c => c is InventoryFolder)
                        .ThenByDescending(c => (c as InventoryItem)?.CreationDate ?? DateTime.MinValue)
                        .ThenBy(c => c.Name),
            InventorySortMode.ByName or _ =>
                contents.OrderByDescending(c => c is InventoryFolder)
                        .ThenBy(c => c.Name),
        };
    }

    private bool IsInCofFolder(UUID parentId)
    {
        // Prefer the known COF UUID from the OutfitManager — don't fall back to
        // FindFolderForType, which returns RootFolder.UUID when COF isn't registered yet.
        var cofId = _instance.COF?.COF?.UUID;
        if (cofId is { } id && id != UUID.Zero)
            return parentId == id;

        // Fallback: check the parent folder's preferred type directly in the store.
        return Inventory.TryGetValue<InventoryFolder>(parentId, out var folder)
            && folder.PreferredType == FolderType.CurrentOutfit;
    }

    private void Inventory_ItemReceived(object? sender, ItemReceivedEventArgs e)
    {
        var parentId = e.Item.ParentUUID;
        var itemName = e.Item.Name;
        bool isLink = e.Item is InventoryItem li && li.IsLink();

        // Capture before the Dispatcher.Post so it's evaluated on the event thread.
        bool inSuppressWindow = (Environment.TickCount - _teleportSuppressUntilTicks) < 0;

        // Show notification on UI thread; tree refresh is debounced below.
        Dispatcher.UIThread.Post(() =>
        {
            if (!isLink && !inSuppressWindow && !IsInCofFolder(parentId) && _inventoryLoaded)
                _instance.ShowNotificationInChat($"Received: {itemName}");
        });

        // Collect the dirty folder and (re)start the batch timer.
        // The timer fires 300 ms after the last item in a burst, keeping the
        // UI responsive during bulk operations such as teleport / outfit changes.
        _pendingItemFolders.TryAdd(parentId, 0);
        lock (_itemBatchLock)
        {
            if (_itemReceivedBatchTimer == null)
                _itemReceivedBatchTimer = new Timer(OnItemBatchTimerFired, null,
                    TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
            else
                _itemReceivedBatchTimer.Change(
                    TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        }
    }

    private void OnItemBatchTimerFired(object? state)
    {
        lock (_itemBatchLock)
        {
            _itemReceivedBatchTimer?.Dispose();
            _itemReceivedBatchTimer = null;
        }

        var folders = _pendingItemFolders.Keys.ToList();
        _pendingItemFolders.Clear();

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var folderId in folders)
                RefreshFolderNode(RootNodes, folderId);

            StatusText = $"Inventory ({CountItems(Inventory.RootNode)} items)";
        });
    }


    #region Tree Helpers

    private void RebuildTree()
    {
        _wornSlots = BuildWornSlots();

        // Snapshot expanded state before clearing
        var expandedIds = new HashSet<UUID>(
            _nodeCache.Where(kv => kv.Value.IsExpanded).Select(kv => kv.Key));

        _nodeCache.Clear();
        RootNodes.Clear();

        var root = Inventory.RootFolder;
        if (root == null) return;

        var rootNode = CreateNode(root);

        if (expandedIds.Count > 0)
        {
            foreach (var id in expandedIds)
            {
                if (_nodeCache.TryGetValue(id, out var n))
                    n.IsExpanded = true;
            }
        }
        else
        {
            rootNode.IsExpanded = true;
        }

        // Sync worn state
        foreach (var kv in _nodeCache)
        {
            if (_wornSlots.TryGetValue(kv.Key, out var directSlot))
            {
                kv.Value.IsWorn   = true;
                kv.Value.WornSlot = directSlot;
            }
            else if (kv.Value.IsLink
                     && Inventory.TryGetValue<InventoryItem>(kv.Key, out var linkInv)
                     && _wornSlots.TryGetValue(linkInv.AssetUUID, out var linkedSlot))
            {
                kv.Value.IsWorn   = true;
                kv.Value.WornSlot = linkedSlot;
            }
            else
            {
                kv.Value.IsWorn   = false;
                kv.Value.WornSlot = string.Empty;
            }
        }

        RootNodes.Add(rootNode);

        // Re-add library node below My Inventory
        AddLibraryNode();

        // Restore expansion state for library nodes too
        if (expandedIds.Count > 0)
        {
            foreach (var id in expandedIds)
            {
                if (_nodeCache.TryGetValue(id, out var n))
                    n.IsExpanded = true;
            }
        }

        int itemCount = CountItems(Inventory.RootNode);
        StatusText = $"Inventory ({itemCount} items)";
    }

    #endregion

    #region Worn State

    private Dictionary<UUID, string> BuildWornSlots()
    {
        var slots = new Dictionary<UUID, string>();

        try
        {
            foreach (var w in Client.Appearance.GetWearables())
                slots[w.ItemID] = w.WearableType.ToString();
        }
        catch { /* not yet available */ }

        try
        {
            foreach (var kvp in Client.Appearance.GetAttachmentsByItemId())
                slots[kvp.Key] = kvp.Value.ToString();
        }
        catch { /* not yet available */ }

        try
        {
            foreach (var kvp in Client.Self.ActiveGestures)
                slots[kvp.Key] = "Active";
        }
        catch { /* not yet available */ }

        // Supplement with a direct COF scan. GetWearables() silently skips items
        // whose link target wasn't in the inventory store when AppearanceSet fired.
        // By the time RebuildTree runs at the end of traversal, all items are loaded.
        try
        {
            // _instance.COF is OutfitManager (subclass of CurrentOutfitFolder) — its COF property
            // gives us the actual InventoryFolder directly, bypassing FindFolderForType which
            // returns RootFolder.UUID as a fallback when the COF folder isn't found.
            var cofFolder = _instance.COF?.COF;
            var cofId = cofFolder?.UUID
                ?? Client.Inventory.FindFolderForType(FolderType.CurrentOutfit);

            var rootId = Inventory.RootFolder?.UUID ?? UUID.Zero;
            if (cofId != UUID.Zero && cofId != rootId)
            {
                foreach (var item in Inventory.GetContents(cofId).OfType<InventoryItem>())
                {
                    if (!item.IsLink()) continue;
                    var originalId = item.AssetUUID;
                    if (slots.ContainsKey(originalId)) continue;

                    if (Inventory.TryGetValue<InventoryWearable>(originalId, out var wearable))
                        slots[originalId] = wearable.WearableType.ToString();
                    else if (Inventory.TryGetValue<InventoryItem>(originalId, out var resolved)
                             && (resolved is InventoryAttachment || resolved is InventoryObject))
                    {
                        // Look up the real attachment point if available
                        var attachPoints = Client.Appearance.GetAttachmentsByItemId();
                        slots[originalId] = attachPoints.TryGetValue(originalId, out var pt)
                            ? pt.ToString()
                            : "Attached";
                    }
                    else
                        slots[originalId] = "Worn";
                }
            }
        }
        catch { /* not yet available */ }

        return slots;
    }

    private void RefreshAllWornState()
    {
        var worn = BuildWornSlots();
        Dispatcher.UIThread.Post(() =>
        {
            _wornSlots = worn;
                foreach (var kv in _nodeCache)
                {
                    if (worn.TryGetValue(kv.Key, out var directSlot))
                    {
                        kv.Value.IsWorn   = true;
                        kv.Value.WornSlot = directSlot;
                    }
                    else if (kv.Value.IsLink
                             && Inventory.TryGetValue<InventoryItem>(kv.Key, out var linkInv)
                             && worn.TryGetValue(linkInv.AssetUUID, out var linkedSlot))
                    {
                        kv.Value.IsWorn   = true;
                        kv.Value.WornSlot = linkedSlot;
                    }
                    else
                    {
                        kv.Value.IsWorn   = false;
                        kv.Value.WornSlot = string.Empty;
                    }
                }
        });
    }

    private void Appearance_AppearanceSet(object? sender, AppearanceSetEventArgs e)
    {
        RefreshAllWornState();
    }

    private void Appearance_AgentWearablesReply(object? sender, AgentWearablesReplyEventArgs e)
    {
        RefreshAllWornState();
    }

    private void Objects_ObjectUpdate(object? sender, PrimEventArgs e)
    {
        var prim = e.Prim;

        if (Client.Self.LocalID == 0
            || prim.ParentID != Client.Self.LocalID
            || prim.NameValues == null)
        {
            return;
        }

        for (int i = 0; i < prim.NameValues.Length; i++)
        {
            if (prim.NameValues[i].Name != "AttachItemID") continue;

            if (!UUID.TryParse(prim.NameValues[i].Value?.ToString() ?? string.Empty, out var inventoryId))
                break;

            _attachmentObjects[prim.LocalID] = inventoryId;

            Dispatcher.UIThread.Post(() =>
            {
                // Look up the real attachment point name via AppearanceManager
                var attachPoints = Client.Appearance.GetAttachmentsByItemId();
                var pointStr = attachPoints.TryGetValue(inventoryId, out var ap)
                    ? ap.ToString()
                    : prim.PrimData.AttachmentPoint != AttachmentPoint.Default
                        ? prim.PrimData.AttachmentPoint.ToString()
                        : "Attached";

                _wornSlots.TryAdd(inventoryId, pointStr);
                if (_nodeCache.TryGetValue(inventoryId, out var node))
                {
                    node.IsWorn   = true;
                    if (string.IsNullOrEmpty(node.WornSlot))
                        node.WornSlot = pointStr;
                }

                // AttachItemID from the server may be a COF link UUID rather than the
                // original item UUID. Resolve one level so the real My Inventory item
                // also gets a worn indicator.
                var effectiveId = inventoryId;
                if (Inventory.TryGetValue<InventoryItem>(inventoryId, out var linkItem) && linkItem.IsLink())
                {
                    effectiveId = linkItem.AssetUUID;
                    _wornSlots.TryAdd(effectiveId, pointStr);
                    if (_nodeCache.TryGetValue(effectiveId, out var originalNode))
                    {
                        originalNode.IsWorn = true;
                        if (string.IsNullOrEmpty(originalNode.WornSlot))
                            originalNode.WornSlot = pointStr;
                    }
                }

                // Mark every other cached link that resolves to the same underlying item
                // (e.g. links in sub-folders that the user has created themselves).
                foreach (var kv in _nodeCache)
                {
                    if (!kv.Value.IsLink) continue;
                    if (Inventory.TryGetValue<InventoryItem>(kv.Key, out var possibleLink)
                        && possibleLink.AssetUUID == effectiveId)
                    {
                        _wornSlots.TryAdd(kv.Key, pointStr);
                        kv.Value.IsWorn = true;
                        if (string.IsNullOrEmpty(kv.Value.WornSlot))
                            kv.Value.WornSlot = pointStr;
                    }
                }
            });
            break;
        }
    }

    #endregion

    #region Filter

    public async Task ApplyFilterAsync(InventoryFilterViewModel filter)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        SearchResults.Clear();
        Func<UUID, string> nameResolver = uuid => _instance.Names.Get(uuid);

        List<InventorySearchResult> results;
        try
        {
            results = await Task.Run(() =>
            {
                var found = new List<InventorySearchResult>();
                FilterNode(Inventory.RootNode, filter, nameResolver, found, token);
                return found;
            }, token);
        }
        catch (OperationCanceledException) { return; }

        if (token.IsCancellationRequested) return;

        foreach (var result in results)
            SearchResults.Add(result);

        HasActiveFilter = true;
        IsSearching     = true;
        StatusText      = $"Filter: {SearchResults.Count} items";
    }

    public void ClearFilter()
    {
        _searchCts?.Cancel();
        SearchResults.Clear();
        HasActiveFilter = false;
        IsSearching     = false;
        int itemCount = CountItems(Inventory.RootNode);
        StatusText = $"Inventory ({itemCount} items)";
    }

    private void FilterNode(
        OpenMetaverse.InventoryNode? node,
        InventoryFilterViewModel filter,
        Func<UUID, string> nameResolver,
        List<InventorySearchResult> results,
        CancellationToken token)
    {
        if (node == null || token.IsCancellationRequested) return;

        if (node.Data is InventoryItem item && filter.Matches(item, nameResolver))
        {
            results.Add(new InventorySearchResult(
                item.UUID,
                item.Name ?? "(unnamed)",
                GetInventoryTypeName(item),
                item.ParentUUID));
        }

        if (node.Nodes == null) return;

        List<OpenMetaverse.InventoryNode> children;
        try { children = node.Nodes.Values.ToList(); }
        catch (InvalidOperationException) { return; }

        foreach (var child in children)
        {
            token.ThrowIfCancellationRequested();
            FilterNode(child, filter, nameResolver, results, token);
        }
    }

    #endregion
}

#region Data Models

public class InvTreeNode : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public UUID ItemId { get; set; }
    public bool IsFolder { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public FolderType FolderKind { get; set; } = FolderType.None;
    public bool IsLibrary { get; set; }
    public InvTreeNode? Parent { get; set; }

    public ObservableCollection<InvTreeNode> Children { get; } = [];

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isWorn;
    public bool IsWorn
    {
        get => _isWorn;
        set
        {
            if (SetProperty(ref _isWorn, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(WornLabel));
            }
        }
    }

    private string _wornSlot = string.Empty;
    public string WornSlot
    {
        get => _wornSlot;
        set
        {
            if (SetProperty(ref _wornSlot, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(WornLabel));
            }
        }
    }

    private bool _isLink;
    public bool IsLink
    {
        get => _isLink;
        set
        {
            if (SetProperty(ref _isLink, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => IsFolder
        ? $"{FolderIcon} {Name}"
        : IsLink
            ? $"{TypeIcon} {Name} \U0001F517"
            : $"{TypeIcon} {Name}";

    private string FolderIcon
    {
        get
        {
            if (IsLibrary) return "\U0001F4DA";  // 📚 library (always)
            return FolderKind switch
            {
                FolderType.Trash         => "\U0001F5D1",  // 🗑 wastebasket
                FolderType.LostAndFound  => "\U0001F4EE",  // 📮 lost+found
                FolderType.CurrentOutfit => "\U0001F455",  // 👕 t-shirt
                _                        => "\U0001F4C1",  // 📁 generic folder
            };
        }
    }

    public string WornLabel
    {
        get
        {
            if (!IsWorn || string.IsNullOrEmpty(WornSlot)) return string.Empty;
            if (Enum.TryParse<OpenMetaverse.WearableType>(WornSlot, true, out _))
                return $"\u00b7 Worn on: {WornSlot}";
            return string.Equals(WornSlot, "Attached", StringComparison.OrdinalIgnoreCase)
                ? "\u00b7 Attached"
                : $"\u00b7 Attached to: {WornSlot}";
        }
    }

    public string DisplayText => IsFolder
        ? $"{FolderIcon} {Name}"
        : IsWorn
            ? $"{TypeIcon} {Name} \U0001F4CC {WornSlot}"
            : IsLink
                ? $"{TypeIcon} {Name} \U0001F517"
                : $"{TypeIcon} {Name}";

    private string TypeIcon => TypeName switch
    {
        "Notecard"    => "\U0001F4DD",
        "Script"      => "\U0001F4DC",
        "Texture"     => "\U0001F5BC",
        "Object"      => "\U0001F4E6",
        "Attachment"  => "\U0001F4CE",   // 📎 attachment
        "Sound"       => "\U0001F50A",
        "Animation"   => "\U0001F3AC",
        "Gesture"     => "\U0001F44B",
        "Landmark"    => "\U0001F4CD",
        "Calling Card"=> "\U0001F4C7",
        "Snapshot"    => "\U0001F4F7",
        "Folder"      => "\U0001F4C1",
        "Material"    => "\U0001F48E",   // 💎 PBR material
        "Settings"    => "\U0001F324",   // 🌤 environment settings
        // WearableType names from InventoryWearable.WearableType.ToString()
        "Shape"       => "\U0001F464",   // 👤 body shape
        "Skin"        => "\U0001F642",   // 🙂 face (skin texture)
        "Hair"        => "\U0001F487",   // 💇 hair
        "Eyes"        => "\U0001F441",   // 👁 eyes
        "Shirt"       => "\U0001F455",   // 👕 shirt
        "Pants"       => "\U0001F456",   // 👖 pants
        "Shoes"       => "\U0001F45F",   // 👟 shoes
        "Socks"       => "\U0001F9E6",   // 🧦 socks
        "Jacket"      => "\U0001F9E5",   // 🧥 jacket
        "Gloves"      => "\U0001F9E4",   // 🧤 gloves
        "Undershirt"  => "\U0001F455",   // 👕 undershirt
        "Underpants"  => "\U0001F456",   // 👖 underpants
        "Skirt"       => "\U0001F457",   // 👗 skirt
        "Alpha"       => "\U0001F4A0",   // 💠 alpha layer
        "Tattoo"      => "\u270F",       // ✏ tattoo
        "Physics"     => "\u2699",       // ⚙ physics
        "Universal"   => "\u2728",       // ✨ universal
        _             => "\U0001F4C4"
    };
}

public record InventorySearchResult(
    UUID ItemId,
    string Name,
    string TypeName,
    UUID ParentId)
{
    public string DisplayText => $"{Name} ({TypeName})";
}

#endregion

public class InventoryEditorRequestedEventArgs : EventArgs
{
    public InventoryBase? Item { get; }
    public CommunityToolkit.Mvvm.ComponentModel.ObservableObject EditorViewModel { get; }

    public InventoryEditorRequestedEventArgs(InventoryBase? item,
        CommunityToolkit.Mvvm.ComponentModel.ObservableObject editorViewModel)
    {
        Item = item;
        EditorViewModel = editorViewModel;
    }
}

public class ItemPropertiesRequestedEventArgs : EventArgs
{
    public InventoryItem Item { get; }
    public ItemPropertiesRequestedEventArgs(InventoryItem item) => Item = item;
}

public enum InventorySortMode
{
    ByName,
    ByDate,
}
