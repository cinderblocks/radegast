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
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class ObjectContentsViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    public UUID ObjectId { get; }
    public uint LocalId { get; }
    public string ObjectName { get; }

    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private ObjectContentItem? _selectedItem;

    public bool CanCopySelected => SelectedItem != null;
    public bool CanDeleteSelected => SelectedItem != null;
    public bool CanOpenSelected => SelectedItem is { CanOpen: true };

    public ObservableCollection<ObjectContentItem> Items { get; } = [];

    private CancellationTokenSource? _cts;

    public ObjectContentsViewModel(RadegastInstanceAvalonia instance, UUID objectId, uint localId, string objectName)
    {
        _instance = instance;
        ObjectId = objectId;
        LocalId = localId;
        ObjectName = objectName;
        _ = LoadContentsAsync();
    }

    partial void OnSelectedItemChanged(ObjectContentItem? value)
    {
        OnPropertyChanged(nameof(CanCopySelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanOpenSelected));
    }

    private async Task LoadContentsAsync()
    {
        _cts = new CancellationTokenSource();
        IsLoading = true;
        StatusText = "Loading...";
        try
        {
            var sim = Client.Network.CurrentSim;
            if (sim == null)
            {
                StatusText = "Not connected to a simulator.";
                IsLoading = false;
                return;
            }

            _cts.CancelAfter(TimeSpan.FromSeconds(30));
            var contents = await Client.Inventory.GetTaskInventoryAsync(
                ObjectId, LocalId, sim, _cts.Token).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                Items.Clear();
                if (contents == null || contents.Count == 0)
                {
                    StatusText = "Object contains no items.";
                }
                else
                {
                    foreach (var inv in contents.OfType<InventoryItem>()
                                                .OrderBy(i => i.AssetType.ToString())
                                                .ThenBy(i => i.Name))
                    {
                        Items.Add(new ObjectContentItem(inv));
                    }
                    StatusText = $"{Items.Count} item{(Items.Count != 1 ? "s" : "")}";
                }
                IsLoading = false;
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() => { StatusText = "Request timed out."; IsLoading = false; });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => { StatusText = $"Error: {ex.Message}"; IsLoading = false; });
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        var old = Interlocked.Exchange(ref _cts, null);
        old?.Cancel();
        old?.Dispose();
        Items.Clear();
        SelectedItem = null;
        _ = LoadContentsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCopySelected))]
    private void CopyToInventory()
    {
        if (SelectedItem == null) return;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        var destFolder = Client.Inventory.FindFolderForType(SelectedItem.AssetType);
        if (destFolder == UUID.Zero)
            destFolder = Client.Inventory.FindFolderForType(AssetType.Unknown);
        Client.Inventory.MoveTaskInventory(LocalId, SelectedItem.ItemId, destFolder, sim);
        StatusText = $"Copying {SelectedItem.Name} to inventory…";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteItem()
    {
        if (SelectedItem == null) return;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        var removing = SelectedItem;
        Client.Inventory.RemoveTaskInventory(LocalId, removing.ItemId, sim);
        Items.Remove(removing);
        SelectedItem = null;
        StatusText = $"Deleted {removing.Name}.";
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private void OpenItem()
    {
        if (SelectedItem == null) return;
        var item = SelectedItem.InventoryItem;

        if (item.AssetType == AssetType.LSLText)
        {
            var lsl = PromoteItem<InventoryLSL>(item);
            var vm = new ScriptEditorViewModel(_instance, lsl);
            Dispatcher.UIThread.Post(() =>
            {
                var panel = new Views.ScriptEditorPanel { DataContext = vm };
                var win = new Views.ProfileWindow($"Script - {lsl.Name}", panel);
                win.Closed += (_, _) => vm.Dispose();
                win.Show();
            });
        }
        else if (item.AssetType == AssetType.Notecard)
        {
            var nc = PromoteItem<InventoryNotecard>(item);
            var vm = new NotecardViewModel(_instance, nc);
            Dispatcher.UIThread.Post(() =>
            {
                var panel = new Views.NotecardPanel { DataContext = vm };
                var win = new Views.ProfileWindow($"Notecard - {nc.Name}", panel);
                win.Closed += (_, _) => vm.Dispose();
                win.Show();
            });
        }
    }

    private static T PromoteItem<T>(InventoryItem source) where T : InventoryItem
    {
        var typed = (T)Activator.CreateInstance(typeof(T), source.UUID)!;
        typed.Name = source.Name;
        typed.Description = source.Description;
        typed.AssetUUID = source.AssetUUID;
        typed.AssetType = source.AssetType;
        typed.Permissions = source.Permissions;
        typed.CreatorID = source.CreatorID;
        typed.OwnerID = source.OwnerID;
        typed.LastOwnerID = source.LastOwnerID;
        typed.GroupID = source.GroupID;
        typed.ParentUUID = source.ParentUUID;
        typed.Flags = source.Flags;
        return typed;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Copies an inventory item from the avatar's inventory into this object's task inventory.
    /// Scripts use CopyScriptToTask; all other asset types use UpdateTaskInventory.
    /// The object must be modifiable by the current user.
    /// </summary>
    public void DropInventoryItem(InvTreeNode node)
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        if (!Client.Inventory.Store!.TryGetValue<InventoryItem>(node.ItemId, out var item) || item == null)
        {
            StatusText = $"Could not find '{node.Name}' in inventory.";
            return;
        }

        if (item.AssetType == AssetType.LSLText)
            Client.Inventory.CopyScriptToTask(LocalId, item, true, sim);
        else
            Client.Inventory.UpdateTaskInventory(LocalId, item, sim);

        StatusText = $"Adding '{item.Name}'…";

        // Refresh the list after a short delay to pick up the new item
        _ = System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshCommand.Execute(null)));
    }
}

public class ObjectContentItem
{
    public UUID ItemId { get; }
    public string Name { get; }
    public AssetType AssetType { get; }
    public InventoryType InventoryType { get; }
    public PermissionMask OwnerMask { get; }
    public PermissionMask NextOwnerMask { get; }
    public InventoryItem InventoryItem { get; }

    public bool CanOpen => AssetType is AssetType.LSLText or AssetType.Notecard;

    public string TypeIcon => AssetType switch
    {
        AssetType.Notecard        => "\U0001F4DD", // 📝
        AssetType.LSLText         => "\U0001F4DC", // 📜 script
        AssetType.Texture         => "\U0001F5BC", // 🖼
        AssetType.Object          => "\U0001F4E6", // 📦
        AssetType.Sound           => "\U0001F50A", // 🔊
        AssetType.Animation       => "\U0001F3AC", // 🎬
        AssetType.Gesture         => "\U0001F44B", // 👋
        AssetType.Landmark        => "\U0001F4CD", // 📍
        AssetType.Bodypart        => "\U0001F9D1", // 🧑
        AssetType.Clothing        => "\U0001F457", // 👗
        AssetType.CallingCard     => "\U0001F4C7", // 📇
        AssetType.ImageTGA        => "\U0001F4F7", // 📷
        AssetType.ImageJPEG       => "\U0001F4F7",
        AssetType.Mesh            => "\U0001F7E6", // 🟦
        _                         => "\U0001F4C4"  // 📄
    };

    public string TypeName => AssetType switch
    {
        AssetType.Notecard    => "Notecard",
        AssetType.LSLText     => "Script",
        AssetType.Texture     => "Texture",
        AssetType.Object      => "Object",
        AssetType.Sound       => "Sound",
        AssetType.Animation   => "Animation",
        AssetType.Gesture     => "Gesture",
        AssetType.Landmark    => "Landmark",
        AssetType.Bodypart    => "Body Part",
        AssetType.Clothing    => "Clothing",
        AssetType.CallingCard => "Calling Card",
        AssetType.Mesh        => "Mesh",
        _                     => AssetType.ToString()
    };

    // Permission flags relative to next owner
    public bool NoModify   => (NextOwnerMask & PermissionMask.Modify)   == 0;
    public bool NoCopy     => (NextOwnerMask & PermissionMask.Copy)     == 0;
    public bool NoTransfer => (NextOwnerMask & PermissionMask.Transfer) == 0;

    // Owner's own permissions
    public bool OwnerCanModify   => (OwnerMask & PermissionMask.Modify)   != 0;
    public bool OwnerCanCopy     => (OwnerMask & PermissionMask.Copy)     != 0;
    public bool OwnerCanTransfer => (OwnerMask & PermissionMask.Transfer) != 0;

    public string PermissionSummary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (NoModify)   parts.Add("no modify");
            if (NoCopy)     parts.Add("no copy");
            if (NoTransfer) parts.Add("no transfer");
            return parts.Count > 0 ? string.Join(", ", parts) : "full perms";
        }
    }

    public ObjectContentItem(InventoryItem item)
    {
        InventoryItem = item;
        ItemId = item.UUID;
        Name = item.Name;
        AssetType = item.AssetType;
        InventoryType = item.InventoryType;
        OwnerMask = item.Permissions.OwnerMask;
        NextOwnerMask = item.Permissions.NextOwnerMask;
    }
}

