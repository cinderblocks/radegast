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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class UploadAnimationViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private string _fileName = string.Empty;
    private byte[]? _uploadData;
    private CancellationTokenSource? _uploadCts;

    [ObservableProperty] private string _statusLog = string.Empty;
    [ObservableProperty] private string _assetId = UUID.Zero.ToString();
    [ObservableProperty] private string _animationName = string.Empty;
    [ObservableProperty] private string _fileInfo = string.Empty;
    [ObservableProperty] private bool _canUpload;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private string _uploadButtonText = "Upload Animation";

    public UploadAnimationViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        instance.NetCom.ClientConnected += NetCom_ClientConnected;
        instance.NetCom.ClientDisconnected += NetCom_ClientDisconnected;
        RefreshUploadButtonText();
    }

    partial void OnCanUploadChanged(bool value) => UploadCommand.NotifyCanExecuteChanged();
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
        var cost = Client.Self.Benefits?.AnimationUploadCost ?? -1;
        UploadButtonText = cost > 0 ? $"Upload L${cost}" : "Upload Animation";
    }

    public void LoadFile(string filePath)
    {
        _fileName = filePath;
        IsLoading = true;
        _uploadData = null;
        UpdateCanUpload();

        AppendLog($"Loading {Path.GetFileName(filePath)}...");

        try
        {
            var data = File.ReadAllBytes(filePath);
            var info = new FileInfo(filePath);
            _uploadData = data;

            Dispatcher.UIThread.Post(() =>
            {
                AnimationName = Path.GetFileNameWithoutExtension(filePath);
                FileInfo = $"{info.Length / 1024.0:F1} KB";
                AssetId = UUID.Zero.ToString();
                AppendLog("File loaded.");
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
                AppendLog($"Failed to load file: {ex.Message}");
                IsLoading = false;
                UpdateCanUpload();
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task Upload()
    {
        if (_uploadData == null) return;

        IsUploading = true;
        UpdateCanUpload();

        _uploadCts = new CancellationTokenSource();
        var token = _uploadCts.Token;

        var animName = string.IsNullOrWhiteSpace(AnimationName)
            ? Path.GetFileNameWithoutExtension(_fileName)
            : AnimationName;
        var description = $"Uploaded with Radegast on {DateTime.Now.ToString(CultureInfo.InvariantCulture)}";
        var perms = new Permissions { EveryoneMask = PermissionMask.All, NextOwnerMask = PermissionMask.All };
        var folder = Client.Inventory.FindFolderForType(AssetType.Animation);
        var data = _uploadData;

        AppendLog("Uploading...");

        try
        {
            var result = await Client.Inventory.CreateItemFromAssetAsync(
                data, animName, description,
                AssetType.Animation, InventoryType.Animation,
                folder, perms, token);

            Dispatcher.UIThread.Post(() =>
            {
                if (result.Success)
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
                AppendLog($"Upload error: {ex.Message}");
                IsUploading = false;
                UpdateCanUpload();
            });
        }
    }

    [RelayCommand]
    private void CancelUpload()
    {
        _uploadCts?.Cancel();
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        StatusLog = string.IsNullOrEmpty(StatusLog) ? line : StatusLog + "\n" + line;
    }

    private void NetCom_ClientConnected(object? sender, EventArgs e) => UpdateCanUpload();
    private void NetCom_ClientDisconnected(object? sender, DisconnectedEventArgs e) => UpdateCanUpload();

    public void Dispose()
    {
        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        _instance.NetCom.ClientConnected -= NetCom_ClientConnected;
        _instance.NetCom.ClientDisconnected -= NetCom_ClientDisconnected;
    }
}
