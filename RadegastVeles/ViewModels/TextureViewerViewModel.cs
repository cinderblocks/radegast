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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreJ2K;
using OpenMetaverse;
using OpenMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class TextureViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly UUID _assetId;

    public ItemMetadataViewModel? Metadata { get; }

    [ObservableProperty] private string _textureName = string.Empty;
    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private int _downloadProgress;
    [ObservableProperty] private int _imageWidth;
    [ObservableProperty] private int _imageHeight;
    [ObservableProperty] private Bitmap? _textureBitmap;

    public event EventHandler? SaveToFileRequested;

    public TextureViewerViewModel(RadegastInstanceAvalonia instance, InventoryTexture item)
        : this(instance, (InventoryItem)item) { }

    public TextureViewerViewModel(RadegastInstanceAvalonia instance, InventorySnapshot item)
        : this(instance, (InventoryItem)item) { }

    public TextureViewerViewModel(RadegastInstanceAvalonia instance, UUID assetId, string name)
    {
        _instance = instance;
        _assetId = assetId;
        TextureName = name;
        Metadata = null;
        LoadTexture();
    }

    private TextureViewerViewModel(RadegastInstanceAvalonia instance, InventoryItem item)
    {
        _instance = instance;
        _assetId = item.AssetUUID;
        TextureName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);
        LoadTexture();
    }

    private void LoadTexture()
    {
        IsLoading = true;
        DownloadProgress = 0;
        StatusText = "Downloading...";

        Client.Assets.ImageReceiveProgress += OnImageReceiveProgress;
        Client.Assets.RequestImage(_assetId, ImageType.Normal, OnImageReceived, true);
    }

    private void OnImageReceiveProgress(object? sender, ImageReceiveProgressEventArgs e)
    {
        if (e.ImageID != _assetId || e.Total <= 0) return;
        int pct = (int)(e.Received * 100L / e.Total);
        Dispatcher.UIThread.Post(() =>
        {
            DownloadProgress = pct;
            StatusText = $"Downloading... {pct}%";
        });
    }

    private async void OnImageReceived(TextureRequestState state, AssetTexture? assetTexture)
    {
        if (state == TextureRequestState.Timeout || state == TextureRequestState.NotFound)
        {
            Client.Assets.ImageReceiveProgress -= OnImageReceiveProgress;
            Dispatcher.UIThread.Post(() => { IsLoading = false; StatusText = "Failed to load texture."; });
            return;
        }

        if (state != TextureRequestState.Finished) return;

        Client.Assets.ImageReceiveProgress -= OnImageReceiveProgress;

        if (assetTexture == null) return;
        var data = assetTexture.AssetData;
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() => J2kImage.DecodeToImage<WriteableBitmap>(data));
        }
        catch
        {
            // ignore decode failures
        }

        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            if (bitmap == null)
            {
                StatusText = "Failed to decode texture.";
                return;
            }
            TextureBitmap = bitmap;
            ImageWidth = (int)bitmap.Size.Width;
            ImageHeight = (int)bitmap.Size.Height;
            StatusText = $"{ImageWidth} \u00d7 {ImageHeight}";
            DownloadProgress = 100;
        });
    }

    [RelayCommand]
    private void SaveToFile() => SaveToFileRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Metadata?.Dispose();
}
