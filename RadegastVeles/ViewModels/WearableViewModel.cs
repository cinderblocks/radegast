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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using OpenMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class WearableViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryWearable _item;
    private bool _disposed;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _wearableName = string.Empty;
    [ObservableProperty] private string _wearableTypeText = string.Empty;
    [ObservableProperty] private string _wearableCategoryText = string.Empty;
    [ObservableProperty] private string _wearableTypeIcon = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isWorn;

    public ObservableCollection<WearableTextureSlot> Textures { get; } = [];
    [ObservableProperty] private bool _isLoadingTextures;
    [ObservableProperty] private bool _hasTextures;

    public WearableViewModel(RadegastInstanceAvalonia instance, InventoryWearable item)
    {
        _instance = instance;
        _item = item;
        WearableName = item.Name;
        WearableTypeText = item.WearableType.ToString();
        WearableCategoryText = GetCategory(item.WearableType);
        WearableTypeIcon = GetTypeIcon(item.WearableType);
        Metadata = new ItemMetadataViewModel(instance, item);

        Client.Appearance.AppearanceSet += Appearance_AppearanceSet;
        RefreshWornState();
        LoadWearableTextures();
    }

    private void Appearance_AppearanceSet(object? sender, AppearanceSetEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshWornState);
    }

    private void RefreshWornState()
    {
        foreach (var w in Client.Appearance.GetWearables())
        {
            if (w.ItemID == _item.UUID || (_item.IsLink() && w.ItemID == _item.AssetUUID))
            {
                IsWorn = true;
                StatusText = $"Currently worn ({w.WearableType}).";
                return;
            }
        }
        IsWorn = false;
        StatusText = "Not currently worn.";
    }

    [RelayCommand]
    private void Wear()
    {
        _ = _instance.COF.AddToOutfit(new List<InventoryItem> { _item }, false, CancellationToken.None);
        StatusText = "Adding to outfit...";
    }

    [RelayCommand]
    private void TakeOff()
    {
        _ = _instance.COF.RemoveFromOutfit(_item, CancellationToken.None);
        StatusText = "Removing from outfit...";
    }

    private void LoadWearableTextures()
    {
        IsLoadingTextures = true;
        Textures.Clear();

        Client.Assets.RequestInventoryAsset(_item, true, UUID.Random(),
            (transfer, asset) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingTextures = false;
                    if (asset is not AssetWearable wearable) return;
                    if (!wearable.Decode()) return;

                    Textures.Clear();
                    var defaultTex = AppearanceManager.DEFAULT_AVATAR_TEXTURE;
                    foreach (var kvp in wearable.Textures)
                    {
                        if (kvp.Value == UUID.Zero || kvp.Value == defaultTex) continue;
                        var slotName = kvp.Key.ToString().Replace("_", " ");
                        Textures.Add(new WearableTextureSlot(slotName, kvp.Value));
                    }
                    HasTextures = Textures.Count > 0;
                });
            });
    }

    [RelayCommand]
    private void ViewTexture(WearableTextureSlot slot)
    {
        _instance.ShowTextureViewer(slot.TextureId, slot.SlotName);
    }

    private static string GetCategory(WearableType type) => type switch
    {
        WearableType.Shape       or
        WearableType.Skin        or
        WearableType.Hair        or
        WearableType.Eyes        => "Body Part",

        WearableType.Shirt       or
        WearableType.Pants       or
        WearableType.Shoes       or
        WearableType.Socks       or
        WearableType.Jacket      or
        WearableType.Gloves      or
        WearableType.Undershirt  or
        WearableType.Underpants  or
        WearableType.Skirt       => "Clothing",

        WearableType.Alpha       => "Alpha Mask",
        WearableType.Tattoo      => "Tattoo Layer",
        WearableType.Physics     => "Physics Layer",
        WearableType.Universal   => "Universal Layer",
        _                        => "Wearable"
    };

    private static string GetTypeIcon(WearableType type) => type switch
    {
        WearableType.Shape      => "\U0001F9D1", // 🧑 person silhouette
        WearableType.Skin       => "\U0001F642", // 🙂 face (skin texture)
        WearableType.Hair       => "\U0001F9B1", // 🦱 curly hair
        WearableType.Eyes       => "\U0001F441", // 👁 eye
        WearableType.Shirt      => "\U0001F455", // 👕 t-shirt
        WearableType.Pants      => "\U0001F456", // 👖 jeans
        WearableType.Shoes      => "\U0001F45F", // 👟 sneaker
        WearableType.Socks      => "\U0001F9E6", // 🧦 socks
        WearableType.Jacket     => "\U0001F9E5", // 🧥 coat
        WearableType.Gloves     => "\U0001F9E4", // 🧤 gloves
        WearableType.Undershirt => "\U0001F455", // 👕 (undershirt = shirt-like)
        WearableType.Underpants => "\U0001FA72", // 🩲 briefs
        WearableType.Skirt      => "\U0001F457", // 👗 dress/skirt
        WearableType.Alpha      => "\U0001F50D", // 🔍 transparency mask
        WearableType.Tattoo     => "\U0001F3A8", // 🎨 tattoo layer
        WearableType.Physics    => "\u2699\uFE0F", // ⚙️ physics
        WearableType.Universal  => "\u2728",     // ✨ universal
        _                       => "\U0001F457"  // 👗 fallback
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Client.Appearance.AppearanceSet -= Appearance_AppearanceSet;
        Metadata.Dispose();
    }
}

public record WearableTextureSlot(string SlotName, UUID TextureId);
