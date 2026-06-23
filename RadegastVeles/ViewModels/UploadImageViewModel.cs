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
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CoreJ2K;
using CoreJ2K.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Pfim;
using Radegast.Veles.Core;
using SkiaSharp;

namespace Radegast.Veles.ViewModels;

public partial class UploadImageViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private string _fileName = string.Empty;
    private byte[]? _uploadData;
    private CancellationTokenSource? _uploadCts;

    [ObservableProperty] private string _statusLog = string.Empty;
    [ObservableProperty] private string _assetId = UUID.Zero.ToString();
    [ObservableProperty] private string _imageName = string.Empty;
    [ObservableProperty] private string _imageSize = string.Empty;
    [ObservableProperty] private bool _lossless;
    [ObservableProperty] private bool _canUpload;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private string _uploadButtonText = "Upload Image";
    [ObservableProperty] private Bitmap? _previewImage;

    public UploadImageViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        instance.NetCom.ClientConnected += NetCom_ClientConnected;
        instance.NetCom.ClientDisconnected += NetCom_ClientDisconnected;
        Client.Self.ViewerBenefitsUpdated += Self_ViewerBenefitsUpdated;
        RefreshUploadButtonText();
    }

    partial void OnCanUploadChanged(bool value) => UploadCommand.NotifyCanExecuteChanged();

    partial void OnLosslessChanged(bool value)
    {
        if (!string.IsNullOrEmpty(_fileName))
            _ = LoadImageAsync(_fileName);
    }

    partial void OnIsUploadingChanged(bool value) => UpdateCanUpload();

    partial void OnIsLoadingChanged(bool value) => UpdateCanUpload();

    private void UpdateCanUpload()
    {
        CanUpload = _uploadData != null
            && Client.Network.Connected
            && !IsUploading
            && !IsLoading;
    }

    private void RefreshUploadButtonText()
    {
        var cost = Client.Self.Benefits?.TextureUploadCost ?? -1;
        UploadButtonText = cost > 0 ? $"Upload L${cost}" : "Upload Image";
    }

    public async Task LoadImageAsync(string filePath)
    {
        _fileName = filePath;
        IsLoading = true;
        _uploadData = null;
        UpdateCanUpload();

        AppendLog($"Loading {Path.GetFileName(filePath)}...");

        try
        {
            var (data, bitmap) = await Task.Run(() => LoadAndEncode(filePath, Lossless));
            _uploadData = data;

            Bitmap previewBitmap;
            int w, h;
            double kb;
            try
            {
                previewBitmap = await Task.Run(() =>
                {
                    using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    using var ms = new MemoryStream(encoded.ToArray());
                    return new Bitmap(ms);
                });
                w = bitmap.Width;
                h = bitmap.Height;
                kb = data.Length / 1024.0;
            }
            finally
            {
                bitmap.Dispose();
            }

            Dispatcher.UIThread.Post(() =>
            {
                PreviewImage?.Dispose();
                PreviewImage = previewBitmap;
                ImageSize = $"{w}\u00d7{h}  {kb:F1} KB";
                ImageName = Path.GetFileNameWithoutExtension(filePath);
                AssetId = UUID.Zero.ToString();
                AppendLog("Image loaded and encoded.");
                IsLoading = false;
                UpdateCanUpload();
                RefreshUploadButtonText();
            });
        }
        catch (Exception ex)
        {
            _uploadData = null;
            Dispatcher.UIThread.Post(() =>
            {
                AppendLog($"Failed to load image: {ex.Message}");
                IsLoading = false;
                UpdateCanUpload();
            });
        }
    }

    private static (byte[] data, SKBitmap bitmap) LoadAndEncode(string filePath, bool lossless)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        SKBitmap skBitmap;

        if (ext is ".jp2" or ".j2c")
        {
            var raw = File.ReadAllBytes(filePath);
            skBitmap = J2kImage.DecodeToImage<SKBitmap>(raw);

            skBitmap = ResizeToNearestPow2(skBitmap, out _);
            var encBuilder = new CompleteEncoderConfigurationBuilder();
            byte[] encodedData;
            if (lossless) { var c = encBuilder.Build(); c.Lossless = true; encodedData = J2kImage.ToBytes(skBitmap, c); }
            else { encodedData = J2kImage.ToBytes(skBitmap, encBuilder.ForStreaming().Build()); }
            return (encodedData, skBitmap);
        }

        if (ext == ".tga")
        {
            using var tga = Pfimage.FromFile(filePath);
            skBitmap = TgaToSkBitmap(tga);
        }
        else
        {
            using var fileStream = File.OpenRead(filePath);
            skBitmap = SKBitmap.Decode(fileStream)
                ?? throw new InvalidOperationException("Failed to decode image.");
        }

        skBitmap = ResizeToNearestPow2(skBitmap, out _);

        var builder = new CompleteEncoderConfigurationBuilder();
        byte[] j2kData;
        if (lossless) { var c = builder.Build(); c.Lossless = true; j2kData = J2kImage.ToBytes(skBitmap, c); }
        else { j2kData = J2kImage.ToBytes(skBitmap, builder.ForStreaming().Build()); }
        return (j2kData, skBitmap);
    }

    private static SKBitmap TgaToSkBitmap(IImage tga)
    {
        var skBitmap = new SKBitmap(tga.Width, tga.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var dest = skBitmap.GetPixels();
        Marshal.Copy(tga.Data, 0, dest, Math.Min(tga.Data.Length, skBitmap.ByteCount));
        return skBitmap;
    }

    private static SKBitmap ResizeToNearestPow2(SKBitmap source, out bool wasResized)
    {
        int w = NearestPow2(source.Width);
        int h = NearestPow2(source.Height);

        if (w == source.Width && h == source.Height)
        {
            wasResized = false;
            return source;
        }

        wasResized = true;
        var resized = source.Resize(new SKSizeI(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        source.Dispose();
        return resized ?? throw new InvalidOperationException("Failed to resize image.");
    }

    private static int NearestPow2(int n)
    {
        // Find largest power of 2 that is <= n, clamped to 1024
        int result = 1;
        while (result * 2 <= n) result <<= 1;
        return Math.Min(result, 1024);
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task Upload()
    {
        if (_uploadData == null) return;

        IsUploading = true;
        UpdateCanUpload();

        _uploadCts = new CancellationTokenSource();
        var token = _uploadCts.Token;

        var textureName = string.IsNullOrWhiteSpace(ImageName)
            ? Path.GetFileNameWithoutExtension(_fileName)
            : ImageName;
        var description = $"Uploaded with Radegast on {DateTime.Now.ToString(CultureInfo.InvariantCulture)}";
        var perms = new Permissions { EveryoneMask = PermissionMask.All, NextOwnerMask = PermissionMask.All };
        var folder = Client.Inventory.FindFolderForType(AssetType.Texture);
        var data = _uploadData;

        AppendLog("Uploading...");

        try
        {
            var result = await Client.Inventory.CreateItemFromAssetAsync(
                data, textureName, description,
                AssetType.Texture, InventoryType.Texture,
                folder, perms, token);

            Dispatcher.UIThread.Post(() =>
            {
                if (result != null && result.Success)
                {
                    AssetId = result.AssetID.ToString();
                    AppendLog($"Upload success. Asset ID: {result.AssetID}");
                }
                else
                {
                    var status = result?.Status ?? "Unknown error";
                    AppendLog($"Upload failed: {status}");
                    if (result?.Error != null)
                        AppendLog($"Error: {result.Error.Message}");
                }

                IsUploading = false;
                UpdateCanUpload();
                RefreshUploadButtonText();
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AppendLog("Upload cancelled.");
                IsUploading = false;
                UpdateCanUpload();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AppendLog($"Upload failed: {ex.Message}");
                IsUploading = false;
                UpdateCanUpload();
            });
        }
        finally
        {
            _uploadCts.Dispose();
            _uploadCts = null;
        }
    }

    [RelayCommand]
    private void CancelUpload()
    {
        _uploadCts?.Cancel();
    }

    private void AppendLog(string message)
    {
        StatusLog += message + Environment.NewLine;
    }

    private void Self_ViewerBenefitsUpdated(object? sender, ViewerBenefitsEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshUploadButtonText);
    }

    private void NetCom_ClientConnected(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCanUpload();
            RefreshUploadButtonText();
        });
    }

    private void NetCom_ClientDisconnected(object? sender, DisconnectedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCanUpload();
            RefreshUploadButtonText();
        });
    }

    public void Dispose()
    {
        _instance.NetCom.ClientConnected -= NetCom_ClientConnected;
        _instance.NetCom.ClientDisconnected -= NetCom_ClientDisconnected;
        Client.Self.ViewerBenefitsUpdated -= Self_ViewerBenefitsUpdated;

        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        _uploadCts = null;

        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}
