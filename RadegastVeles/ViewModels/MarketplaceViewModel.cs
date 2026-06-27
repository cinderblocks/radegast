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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.Marketplace;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// ViewModel for the Marketplace tab. Orchestrates between the LibreMetaverse
/// <see cref="MarketplaceManager"/> HTTP API and the Avalonia UI collections.
/// </summary>
public partial class MarketplaceViewModel : ClientAwareViewModelBase
{
    private readonly ConcurrentDictionary<UUID, MarketplaceListingRecord> _records = new();
    private CancellationTokenSource? _syncCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncListingsCommand))]
    private bool _isSyncing;

    [ObservableProperty]
    private string _statusText = "Not synced";

    [ObservableProperty]
    private MarketplaceListingRecord? _selectedListing;

    [ObservableProperty]
    private int _selectedFilterIndex;

    public ObservableCollection<MarketplaceListingRecord> AllListings { get; } = [];
    public ObservableCollection<MarketplaceListingRecord> ActiveListings { get; } = [];
    public ObservableCollection<MarketplaceListingRecord> InactiveListings { get; } = [];
    public ObservableCollection<MarketplaceListingRecord> UnassociatedListings { get; } = [];

    public MarketplaceViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        RegisterClientEvents(instance.Client);
    }

    protected override void RegisterClientEvents(GridClient client)
    {
        client.Inventory.FolderUpdated += Inventory_FolderUpdated;
        client.Marketplace.ListingsSynced += Marketplace_ListingsSynced;
        client.Marketplace.ListingChanged += Marketplace_ListingChanged;
        client.Marketplace.Error += Marketplace_Error;
    }

    protected override void UnregisterClientEvents(GridClient client)
    {
        client.Inventory.FolderUpdated -= Inventory_FolderUpdated;
        client.Marketplace.ListingsSynced -= Marketplace_ListingsSynced;
        client.Marketplace.ListingChanged -= Marketplace_ListingChanged;
        client.Marketplace.Error -= Marketplace_Error;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSyncListings))]
    private async Task SyncListings()
    {
        _syncCts?.Cancel();
        _syncCts = new CancellationTokenSource();
        IsSyncing = true;
        StatusText = "Syncing\u2026";
        try
        {
            await Client.Marketplace.FetchListingsAsync(_syncCts.Token).ConfigureAwait(false);
            // RebuildFromInventory is triggered by the ListingsSynced event handler
        }
        catch (OperationCanceledException) { }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsSyncing = false);
        }
    }

    private bool CanSyncListings() => !IsSyncing;

    [RelayCommand(CanExecute = nameof(CanActOnListing))]
    private async Task ActivateListing(MarketplaceListingRecord? record)
    {
        record ??= SelectedListing;
        if (record?.ListingId == null) return;
        record.IsUpdatePending = true;
        try
        {
            await Client.Marketplace.ActivateListingAsync(record.ListingId.Value).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => record.IsUpdatePending = false);
        }
    }

    private bool CanActOnListing(MarketplaceListingRecord? record)
        => (record ?? SelectedListing)?.IsUpdatePending == false;

    [RelayCommand(CanExecute = nameof(CanActOnListing))]
    private async Task DeactivateListing(MarketplaceListingRecord? record)
    {
        record ??= SelectedListing;
        if (record?.ListingId == null) return;
        record.IsUpdatePending = true;
        try
        {
            await Client.Marketplace.DeactivateListingAsync(record.ListingId.Value).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => record.IsUpdatePending = false);
        }
    }

    [RelayCommand]
    private async Task CreateListing(MarketplaceListingRecord? record)
    {
        record ??= SelectedListing;
        if (record == null) return;
        record.IsUpdatePending = true;
        try
        {
            await Client.Marketplace.CreateListingAsync(record.ListingFolderUUID).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => record.IsUpdatePending = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnListing))]
    private async Task DeleteListing(MarketplaceListingRecord? record)
    {
        record ??= SelectedListing;
        if (record?.ListingId == null) return;
        record.IsUpdatePending = true;
        try
        {
            await Client.Marketplace.DeleteListingAsync(record.ListingId.Value).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => record.IsUpdatePending = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenOnWeb))]
    private void OpenListingOnWeb(MarketplaceListingRecord? record)
    {
        record ??= SelectedListing;
        if (string.IsNullOrEmpty(record?.EditUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(record.EditUrl) { UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }

    private bool CanOpenOnWeb(MarketplaceListingRecord? record)
        => !string.IsNullOrEmpty((record ?? SelectedListing)?.EditUrl);

    // ── Inventory rebuild ─────────────────────────────────────────────────────

    private void RebuildFromInventory()
    {
        var inv = Client.Inventory.Store;
        if (inv == null) return;

        var folderIds = MarketplaceFolderClassifier.GetAllListingFolderIds(inv);
        var seenKeys = new HashSet<UUID>(folderIds);

        foreach (var folderId in folderIds)
        {
            if (!inv.TryGetValue<InventoryFolder>(folderId, out var folder)) continue;

            if (!_records.TryGetValue(folderId, out var record))
            {
                record = new MarketplaceListingRecord(folderId, folder!.Name);
                _records[folderId] = record;
            }
            else
            {
                record.FolderName = folder!.Name;
            }

            RevalidateRecord(record);

            // Apply cached backend data if available
            if (Client.Marketplace.ListingsByFolder.TryGetValue(folderId, out var listing))
                record.ApplyBackendData(listing);
        }

        // Remove stale records whose listing folders have been deleted from inventory
        var staleKeys = _records.Keys.Where(k => !seenKeys.Contains(k)).ToList();
        foreach (var key in staleKeys)
            _records.TryRemove(key, out _);

        Dispatcher.UIThread.Post(RefreshObservableCollections);
    }

    private void RevalidateRecord(MarketplaceListingRecord record)
    {
        var inv = Client.Inventory.Store;
        if (inv == null) return;
        record.IsValidationPending = true;
        record.ValidationFlags = MarketplaceFolderClassifier.ValidateListing(record.ListingFolderUUID, inv);
        var versionId = MarketplaceFolderClassifier.GetVersionFolder(record.ListingFolderUUID, inv);
        if (versionId != UUID.Zero)
        {
            record.VersionFolderUUID = versionId;
            record.LocalStockCount = MarketplaceFolderClassifier.GetStockCount(versionId, inv);
        }
        else
        {
            record.VersionFolderUUID = null;
            record.LocalStockCount = 0;
        }
        record.IsValidationPending = false;
    }

    private void RefreshObservableCollections()
    {
        ReplaceCollection(AllListings, _records.Values.OrderBy(r => r.FolderName));
        ReplaceCollection(ActiveListings, _records.Values.Where(r => r.IsListed).OrderBy(r => r.FolderName));
        ReplaceCollection(InactiveListings, _records.Values.Where(r => r.IsAssociated && !r.IsListed).OrderBy(r => r.FolderName));
        ReplaceCollection(UnassociatedListings, _records.Values.Where(r => !r.IsAssociated).OrderBy(r => r.FolderName));

        var total = AllListings.Count;
        var active = ActiveListings.Count;
        StatusText = total == 0 ? "No listing folders found" : $"{active} active / {total} total";
    }

    private static void ReplaceCollection(ObservableCollection<MarketplaceListingRecord> target,
        IEnumerable<MarketplaceListingRecord> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void Inventory_FolderUpdated(object? sender, FolderUpdatedEventArgs e)
    {
        if (!e.Success) return;
        var inv = Client.Inventory.Store;
        if (inv == null) return;
        var role = MarketplaceFolderClassifier.GetRole(e.FolderID, inv);
        if (role == MarketplaceFolderRole.None) return;
        RebuildFromInventory();
    }

    private void Marketplace_ListingsSynced(object? sender, MarketplaceListingsSyncedEventArgs e)
    {
        RebuildFromInventory();
    }

    private void Marketplace_ListingChanged(object? sender, MarketplaceListingChangedEventArgs e)
    {
        if (e.Listing == null)
        {
            // Listing deleted — clear association on the matching record
            var stale = _records.Values.FirstOrDefault(r => r.ListingId == e.ListingId);
            if (stale != null)
            {
                stale.ListingId = null;
                stale.Status = MarketplaceListingStatus.Unknown;
            }
        }
        else
        {
            if (_records.TryGetValue(e.Listing.ListingFolderUUID, out var record))
            {
                record.ApplyBackendData(e.Listing);
            }
            else
            {
                // New listing folder not yet tracked — do a full rebuild
                RebuildFromInventory();
                return;
            }
        }
        Dispatcher.UIThread.Post(RefreshObservableCollections);
    }

    private void Marketplace_Error(object? sender, MarketplaceErrorEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsSyncing = false;
            foreach (var r in _records.Values)
                r.IsUpdatePending = false;
            StatusText = $"Error: {e.Message}";
        });
    }

    public override void Dispose()
    {
        _syncCts?.Cancel();
        _syncCts?.Dispose();
        base.Dispose();
    }
}
