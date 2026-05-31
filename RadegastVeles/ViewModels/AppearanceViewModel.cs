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
using OpenMetaverse.StructuredData;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class AppearanceViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private bool _disposed;

    public const double MinHoverHeight = -2.0;
    public const double MaxHoverHeight =  2.0;

    public ObservableCollection<WornItemEntry> WornItems { get; } = [];

    [ObservableProperty] private double _hoverHeight;
    [ObservableProperty] private string _hoverHeightText = "0.00";
    [ObservableProperty] private bool _hasWornItems;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;

    public AppearanceViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        _hoverHeight = instance.GlobalSettings["AvatarHoverOffsetZ"]?.AsReal() ?? 0.0;
        _hoverHeightText = _hoverHeight.ToString("F2");
        Client.Appearance.AgentWearablesReply += OnAgentWearablesReply;
        Client.Self.AgentPreferencesUpdated += Self_AgentPreferencesUpdated;
        Client.Inventory.ItemReceived += OnInventoryItemReceived;
        LoadWornItems();
    }

    partial void OnHoverHeightChanged(double value)
    {
        HoverHeightText = value.ToString("F2");
    }

    private void OnAgentWearablesReply(object? sender, AgentWearablesReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(LoadWornItems);
    }

    private void LoadWornItems()
    {
        WornItems.Clear();
        var seen = new HashSet<UUID>();
        foreach (var w in Client.Appearance.GetWearables())
        {
            if (!seen.Add(w.ItemID)) continue;
            WornItems.Add(new WornItemEntry(GetTypeIcon(w.WearableType), w.WearableType.ToString(), GetItemName(w)));
        }
        HasWornItems = WornItems.Count > 0;
    }

    private string GetItemName(AppearanceManager.WearableData w)
    {
        if (w.Asset?.Name is { Length: > 0 } assetName)
            return assetName;
        if (Client.Inventory.Store?.TryGetValue(w.ItemID, out var invBase) == true && invBase is InventoryItem item)
            return item.Name;
        return w.WearableType.ToString();
    }

    [RelayCommand]
    private void RefreshOutfit()
    {
        LoadWornItems();
        StatusText = "Outfit refreshed.";
    }

    [RelayCommand]
    private void RebakeTextures()
    {
        StatusText = "Rebaking textures...";
        Client.Appearance.RequestSetAppearance(true);
        StatusText = "Rebake requested.";
    }

    [RelayCommand]
    private async Task ApplyHoverHeight()
    {
        IsBusy = true;
        StatusText = "Applying hover height...";
        try
        {
            await Client.Self.SetHoverHeightAsync(HoverHeight);
            _instance.GlobalSettings["AvatarHoverOffsetZ"] = OSD.FromReal(HoverHeight);
            StatusText = $"Hover height set to {HoverHeight:F2}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string GetTypeIcon(WearableType type) => type switch
    {
        WearableType.Shape      => "\U0001F9D1",
        WearableType.Skin       => "\U0001F642",
        WearableType.Hair       => "\U0001F9B1",
        WearableType.Eyes       => "\U0001F441",
        WearableType.Shirt      => "\U0001F455",
        WearableType.Pants      => "\U0001F456",
        WearableType.Shoes      => "\U0001F45F",
        WearableType.Socks      => "\U0001F9E6",
        WearableType.Jacket     => "\U0001F9E5",
        WearableType.Gloves     => "\U0001F9E4",
        WearableType.Undershirt => "\U0001F455",
        WearableType.Underpants => "\U0001FA72",
        WearableType.Skirt      => "\U0001F457",
        WearableType.Alpha      => "\U0001F50D",
        WearableType.Tattoo     => "\U0001F3A8",
        WearableType.Physics    => "\u2699\uFE0F",
        WearableType.Universal  => "\u2728",
        _                       => "\U0001F457"
    };

    private void Self_AgentPreferencesUpdated(object? sender, AgentPreferencesEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            HoverHeight = e.Preferences.HoverHeight;
        });
    }

    private void OnInventoryItemReceived(object? sender, ItemReceivedEventArgs e)
    {
        // Refresh the worn list when an inventory item arrives so that names that
        // were previously missing (inventory not yet loaded) are filled in.
        var wearables = Client.Appearance.GetWearables();
        if (wearables.Any(w => w.ItemID == e.Item.UUID))
            Dispatcher.UIThread.Post(LoadWornItems);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Client.Appearance.AgentWearablesReply -= OnAgentWearablesReply;
        Client.Self.AgentPreferencesUpdated -= Self_AgentPreferencesUpdated;
        Client.Inventory.ItemReceived -= OnInventoryItemReceived;
    }
}

public record WornItemEntry(string Icon, string TypeLabel, string ItemName);
