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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class InventoryPickerViewModel : ObservableObject
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly HashSet<AssetType>? _allowedTypes;
    private readonly Func<InventoryItem, bool>? _itemFilter;
    private List<InventoryPickerEntry> _allItems = [];

    /// <summary>Raised when the user confirms a selection.</summary>
    public event EventHandler<InventoryPickerEntry>? Selected;

    /// <summary>Raised when the user dismisses the picker without making a selection.</summary>
    public event EventHandler? Cancelled;

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusText = "Loading\u2026";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDisplay))]
    private InventoryPickerEntry? _selectedItem;

    public ObservableCollection<InventoryPickerEntry> FilteredItems { get; } = [];

    public string SelectedDisplay => SelectedItem != null ? $"Selected: {SelectedItem.Name}" : string.Empty;

    public InventoryPickerViewModel(RadegastInstanceAvalonia instance, IEnumerable<AssetType>? allowedTypes = null, Func<InventoryItem, bool>? itemFilter = null)
    {
        _instance = instance;
        _allowedTypes = allowedTypes != null ? [..allowedTypes] : null;
        _itemFilter = itemFilter;
        _ = LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        var store = _instance.Client.Inventory.Store;
        if (store == null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                StatusText = "Inventory not available.";
            });
            return;
        }

        var items = await Task.Run(() =>
        {
            var found = new List<InventoryPickerEntry>();
            CollectMatchingItems(store.RootNode, found);
            CollectMatchingItems(store.LibraryRootNode, found);
            return found.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });

        Dispatcher.UIThread.Post(() =>
        {
            _allItems = items;
            ApplyFilter();
            IsLoading = false;
            StatusText = _allItems.Count == 1 ? "1 item" : $"{_allItems.Count} items";
        });
    }

    private void CollectMatchingItems(InventoryNode? node, List<InventoryPickerEntry> results)
    {
        if (node == null) return;

        if (node.Data is InventoryItem item &&
            (_allowedTypes == null || _allowedTypes.Contains(item.AssetType)) &&
            (_itemFilter == null || _itemFilter(item)))
            results.Add(new InventoryPickerEntry(item.UUID, item.AssetUUID, item.Name ?? "(unnamed)", item.AssetType));

        if (node.Nodes == null) return;

        List<InventoryNode> children;
        try { children = node.Nodes.Values.ToList(); }
        catch (InvalidOperationException) { return; }

        foreach (var child in children)
            CollectMatchingItems(child, results);
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var lower = FilterText.ToLowerInvariant();
        foreach (var entry in _allItems)
        {
            if (string.IsNullOrEmpty(lower) || entry.Name.ToLowerInvariant().Contains(lower))
                FilteredItems.Add(entry);
        }
    }

    partial void OnSelectedItemChanged(InventoryPickerEntry? value) =>
        SelectCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Select()
    {
        if (SelectedItem == null) return;
        Selected?.Invoke(this, SelectedItem);
    }

    private bool HasSelection() => SelectedItem != null;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}

/// <summary>An inventory item entry returned by the inventory picker.</summary>
public record InventoryPickerEntry(UUID ItemId, UUID AssetId, string Name, AssetType AssetType);
