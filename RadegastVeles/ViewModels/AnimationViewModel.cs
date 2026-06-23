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
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class AnimationViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryAnimation _item;
    private byte[]? _animData;
    private bool _disposed;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _animationName = string.Empty;
    [ObservableProperty] private string _statusText = "Downloading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canSave;
    [ObservableProperty] private string _dataSizeText = string.Empty;

    /// <summary>Raised when the user wants to save; subscribers show a file-save dialog.</summary>
    public event EventHandler<byte[]>? SaveRequested;

    public AnimationViewModel(RadegastInstanceAvalonia instance, InventoryAnimation item)
    {
        _instance = instance;
        _item = item;
        AnimationName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);

        _ = Task.Run(async () =>
        {
            var asset = await Client.Assets.RequestAssetAsync(item.AssetUUID, AssetType.Animation, true);
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                if (asset?.AssetData == null)
                {
                    StatusText = "Failed to download animation.";
                    return;
                }
                _animData = asset.AssetData;
                CanSave = true;
                var kb = _animData.Length / 1024.0;
                DataSizeText = kb < 1 ? $"{_animData.Length} bytes" : $"{kb:F1} KB";
                StatusText = "Ready.";
                SaveCommand.NotifyCanExecuteChanged();
            });
        });
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Client.Self.AnimationStop(_item.AssetUUID, true);
            IsPlaying = false;
            StatusText = "Stopped.";
        }
        else
        {
            Client.Self.AnimationStart(_item.AssetUUID, true);
            IsPlaying = true;
            StatusText = "Playing...";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_animData != null)
            SaveRequested?.Invoke(this, _animData);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsPlaying)
            Client.Self.AnimationStop(_item.AssetUUID, true);
        Metadata.Dispose();
    }
}
