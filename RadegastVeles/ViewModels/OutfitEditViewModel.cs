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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Represents a single item in the current outfit list.
/// </summary>
public class OutfitWornItem
{
    public string Name { get; init; } = string.Empty;
    public string TypeIcon { get; init; } = string.Empty;
    public string SlotLabel { get; init; } = string.Empty;
    public bool CanRemove { get; init; }
    public UUID ItemId { get; init; }
    public UUID ActualItemId { get; init; }
    public ICommand? RemoveCommand { get; init; }
}

/// <summary>
/// ViewModel for the current outfit editor panel.
/// Shows what the avatar is currently wearing, allows removing individual items,
/// and supports saving the current outfit as a named folder in My Outfits.
/// </summary>
public partial class OutfitEditViewModel : ClientAwareViewModelBase
{
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Current Outfit";

    public ObservableCollection<OutfitWornItem> WornItems { get; } = [];

    public OutfitEditViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        RegisterClientEvents(Client);
        _ = RefreshAsync();
    }

    protected override void RegisterClientEvents(GridClient client)
    {
        client.Inventory.FolderUpdated += Client_FolderUpdated;
        client.Appearance.AppearanceSet += Client_AppearanceSet;
    }

    protected override void UnregisterClientEvents(GridClient client)
    {
        client.Inventory.FolderUpdated -= Client_FolderUpdated;
        client.Appearance.AppearanceSet -= Client_AppearanceSet;
    }

    private async void Client_FolderUpdated(object? sender, FolderUpdatedEventArgs e)
    {
        var cofId = _instance.COF?.COF?.UUID ?? UUID.Zero;
        if (cofId != UUID.Zero && e.FolderID == cofId && e.Success)
            await RefreshAsync().ConfigureAwait(false);
    }

    private async void Client_AppearanceSet(object? sender, AppearanceSetEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var cof = _instance.COF;
        if (cof == null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Outfit manager not available.");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = true;
            StatusText = "Loading...";
        });

        try
        {
            var links = await cof.GetCurrentOutfitLinksAsync().ConfigureAwait(false);
            var items = new List<OutfitWornItem>(links.Count);

            foreach (var link in links)
            {
                var actual = cof.ResolveInventoryLink(link);
                if (actual == null) continue;

                var typeName = InventoryViewModel.GetInventoryTypeName(actual);
                bool isBodyPart = actual is InventoryWearable w &&
                    w.WearableType is WearableType.Shape or WearableType.Skin
                                  or WearableType.Hair or WearableType.Eyes;

                var capturedActual = actual;
                items.Add(new OutfitWornItem
                {
                    Name = actual.Name ?? "(unknown)",
                    TypeIcon = GetTypeIcon(typeName),
                    SlotLabel = GetSlotLabel(typeName, actual),
                    CanRemove = !isBodyPart,
                    ItemId = link.UUID,
                    ActualItemId = actual.UUID,
                    RemoveCommand = new AsyncRelayCommand(() => DoRemoveAsync(capturedActual))
                });
            }

            // Sort: body parts first, then clothing, then attached objects/gestures
            items.Sort((a, b) =>
            {
                static int Rank(OutfitWornItem x)
                {
                    if (!x.CanRemove) return 0;
                    if (x.TypeIcon == "📦" || x.TypeIcon == "🤌") return 2;
                    return 1;
                }
                return Rank(a).CompareTo(Rank(b));
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WornItems.Clear();
                foreach (var item in items)
                    WornItems.Add(item);
                StatusText = $"Wearing {items.Count} item{(items.Count != 1 ? "s" : "")}";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Error: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    private async Task DoRemoveAsync(InventoryBase actual)
    {
        var cof = _instance.COF;
        if (cof == null || actual is not InventoryItem invItem) return;
        await cof.RemoveFromOutfitAsync(invItem, CancellationToken.None).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the current outfit as a new named folder under My Outfits,
    /// populating it with links to each currently worn item.
    /// </summary>
    public async Task SaveCurrentOutfitAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var cof = _instance.COF;
        if (cof == null)
        {
            VelesNotificationService.Show("Outfit", "Outfit manager not available.");
            return;
        }
        var myOutfitsId = Client.Inventory.FindFolderForType(FolderType.MyOutfits);
        if (myOutfitsId == UUID.Zero)
        {
            VelesNotificationService.Show("Outfit", "My Outfits folder not found.");
            return;
        }
        var newFolderId = Client.Inventory.CreateFolder(myOutfitsId, name, FolderType.Outfit);
        if (newFolderId == UUID.Zero)
        {
            VelesNotificationService.Show("Outfit", "Failed to create outfit folder.");
            return;
        }
        // Brief delay for server to acknowledge the new folder
        await Task.Delay(500).ConfigureAwait(false);
        var links = await cof.GetCurrentOutfitLinksAsync().ConfigureAwait(false);
        foreach (var link in links)
        {
            var actual = cof.ResolveInventoryLink(link);
            if (actual == null) continue;
            await Client.Inventory.CreateLinkAsync(
                newFolderId, actual.UUID, actual.Name,
                string.Empty, actual.InventoryType, UUID.Random(),
                CancellationToken.None
            ).ConfigureAwait(false);
        }
        VelesNotificationService.Show("Outfit", $"Saved '{name}' to My Outfits.");
    }

    private static string GetTypeIcon(string typeName) => typeName switch
    {
        "Shape"      => "🧍",
        "Skin"       => "🎨",
        "Hair"       => "💇",
        "Eyes"       => "👁",
        "Shirt"      => "👕",
        "Pants"      => "👖",
        "Shoes"      => "👟",
        "Socks"      => "🧦",
        "Jacket"     => "🧥",
        "Gloves"     => "🧤",
        "Undershirt" => "👚",
        "Underpants" => "🩲",
        "Skirt"      => "👗",
        "Alpha"      => "⬜",
        "Tattoo"     => "✏",
        "Physics"    => "🔧",
        "Universal"  => "✨",
        "Object"     => "📦",
        "Gesture"    => "🤌",
        _            => "📎"
    };

    private static string GetSlotLabel(string typeName, InventoryBase item) => typeName switch
    {
        "Object" when item is InventoryObject obj =>
            obj.AttachPoint == AttachmentPoint.Default ? "Object" : obj.AttachPoint.ToString(),
        _ => typeName
    };
}
