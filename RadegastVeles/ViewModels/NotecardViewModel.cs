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
using LibreMetaverse;
using LibreMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class NotecardViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryNotecard _item;
    private bool _isSettingText;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _notecardName = string.Empty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isModified;

    public ObservableCollection<EmbeddedNotecardItem> EmbeddedItems { get; } = [];
    public bool HasEmbeddedItems => EmbeddedItems.Count > 0;

    public NotecardViewModel(RadegastInstanceAvalonia instance, InventoryNotecard item)
    {
        _instance = instance;
        _item = item;
        NotecardName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);
        EmbeddedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEmbeddedItems));
        LoadNotecard();
    }

    private void LoadNotecard()
    {
        if (_item.AssetUUID == UUID.Zero)
        {
            IsLoading = false;
            StatusText = "Blank notecard";
            return;
        }
        IsLoading = true;
        StatusText = "Loading...";
        _ = Task.Run(async () =>
        {
            var asset = await Client.Assets.RequestInventoryAssetAsync(_item, true, UUID.Random());
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                if (asset is not AssetNotecard notecard)
                {
                    StatusText = "Failed to load";
                    return;
                }
                notecard.Decode();
                _isSettingText = true;
                Content = notecard.BodyText ?? string.Empty;
                _isSettingText = false;
                EmbeddedItems.Clear();
                if (notecard.EmbeddedItems != null)
                {
                    foreach (var embItem in notecard.EmbeddedItems)
                        EmbeddedItems.Add(new EmbeddedNotecardItem(embItem));
                }
                IsModified = false;
                StatusText = "Ready";
                SaveCommand.NotifyCanExecuteChanged();
            });
        });
    }

    partial void OnContentChanged(string value)
    {
        if (!_isSettingText)
            IsModified = true;
    }

    partial void OnIsLoadingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        IsSaving = true;
        StatusText = "Saving...";

        var notecard = new AssetNotecard
        {
            BodyText = Content,
            EmbeddedItems = EmbeddedItems.Select(e => e.Item).ToList()
        };
        notecard.Encode();

        var (success, status, _, _) = await Client.Inventory.RequestUploadNotecardAssetAsync(notecard.AssetData, _item.UUID);
        Dispatcher.UIThread.Post(() =>
        {
            IsSaving = false;
            if (success)
            {
                IsModified = false;
                StatusText = "Saved";
            }
            else
            {
                StatusText = $"Save failed: {status ?? "Unknown error"}";
            }
            SaveCommand.NotifyCanExecuteChanged();
        });
    }

    public void HandleDroppedNode(InvTreeNode node)
    {
        if (Client.Inventory.Store == null ||
            !Client.Inventory.Store.TryGetValue(node.ItemId, out InventoryBase? invBase) ||
            invBase is not InventoryItem item)
            return;

        if ((item.Permissions.OwnerMask & PermissionMask.Transfer) == 0) return;
        if (EmbeddedItems.Any(e => e.ItemId == item.UUID)) return;
        EmbeddedItems.Add(new EmbeddedNotecardItem(item));
        IsModified = true;
    }

    [RelayCommand]
    private async Task CopyEmbeddedItemToInventory(EmbeddedNotecardItem entry, CancellationToken ct)
    {
        var folderId = Client.Inventory.FindFolderForType(entry.Item.AssetType);
        StatusText = $"Saving '{entry.Name}' to inventory\u2026";
        var copied = await Client.Inventory.RequestCopyItemFromNotecardAsync(
            UUID.Zero, _item.UUID, folderId, entry.Item.UUID, ct);
        Dispatcher.UIThread.Post(() =>
            StatusText = copied != null
                ? $"'{entry.Name}' saved to inventory."
                : "Failed to save item to inventory.");
    }

    [RelayCommand]
    private void RemoveEmbeddedItem(EmbeddedNotecardItem entry)
    {
        EmbeddedItems.Remove(entry);
        IsModified = true;
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_item.AssetUUID == UUID.Zero) return;
        LoadNotecard();
    }

    private bool CanSave() => !IsLoading && !IsSaving;

    public void Dispose() => Metadata.Dispose();
}

public record EmbeddedNotecardItem(InventoryItem Item)
{
    public UUID ItemId => Item.UUID;
    public string Name => Item.Name ?? "(unnamed)";
    public string TypeLabel => Item.AssetType.ToString();
}
