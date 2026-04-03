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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using OpenMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class GestureViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryGesture _item;
    private bool _disposed;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _gestureName = string.Empty;
    [ObservableProperty] private string _statusText = "Downloading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isActive;

    public ObservableCollection<string> Steps { get; } = [];

    public GestureViewModel(RadegastInstanceAvalonia instance, InventoryGesture item)
    {
        _instance = instance;
        _item = item;
        GestureName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);
        _isActive = Metadata.IsWorn;

        Client.Assets.RequestAsset(item.AssetUUID, AssetType.Gesture, true, OnAssetReceived);
    }

    private void OnAssetReceived(AssetDownload transfer, Asset? asset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            if (!transfer.Success || asset is not AssetGesture gestureAsset)
            {
                StatusText = "Failed to download gesture.";
                return;
            }

            if (gestureAsset.Decode())
            {
                foreach (var step in gestureAsset.Sequence)
                {
                    var text = step.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        Steps.Add(text);
                }
                StatusText = Steps.Count == 1 ? "1 step" : $"{Steps.Count} steps";
            }
            else
            {
                StatusText = "Could not decode gesture sequence.";
            }
        });
    }

    [RelayCommand]
    private void Play()
    {
        Client.Self.PlayGesture(_item.AssetUUID);
        StatusText = "Playing...";
    }

    [RelayCommand]
    private void ToggleActive()
    {
        if (IsActive)
        {
            Client.Self.DeactivateGesture(_item.UUID);
            IsActive = false;
            StatusText = "Deactivated.";
        }
        else
        {
            Client.Self.ActivateGesture(_item.UUID, _item.AssetUUID);
            IsActive = true;
            StatusText = "Activated.";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Metadata.Dispose();
    }
}
