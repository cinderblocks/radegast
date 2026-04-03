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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using OpenMetaverse.ImportExport;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class UploadMeshViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private readonly List<string> _filePaths = [];
    private CancellationTokenSource? _uploadCts;

    [ObservableProperty] private string _statusLog = string.Empty;
    [ObservableProperty] private bool _includeImages;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private bool _canStart;
    [ObservableProperty] private string _startButtonText = "Start Upload";
    [ObservableProperty] private string _fileCount = "No files queued";

    public ObservableCollection<string> QueuedFiles { get; } = [];

    public UploadMeshViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        instance.NetCom.ClientConnected += NetCom_ClientConnected;
        instance.NetCom.ClientDisconnected += NetCom_ClientDisconnected;
        RefreshStartButtonText();
    }

    partial void OnCanStartChanged(bool value) => StartUploadCommand.NotifyCanExecuteChanged();

    partial void OnIsUploadingChanged(bool value) => UpdateCanStart();

    private void UpdateCanStart()
    {
        CanStart = _filePaths.Count > 0
            && Client.Network.Connected
            && !IsUploading;
    }

    private void RefreshStartButtonText()
    {
        var cost = Client.Self.Benefits?.MeshUploadCost ?? -1;
        StartButtonText = cost > 0 ? $"Start Upload L${cost}" : "Start Upload";
    }

    public void AddFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            if (!_filePaths.Contains(path))
            {
                _filePaths.Add(path);
                QueuedFiles.Add(Path.GetFileName(path));
            }
        }
        UpdateFileCount();
        UpdateCanStart();
    }

    private void UpdateFileCount()
    {
        FileCount = _filePaths.Count == 0
            ? "No files queued"
            : $"{_filePaths.Count} file{(_filePaths.Count == 1 ? "" : "s")} queued";
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartUpload()
    {
        if (_filePaths.Count == 0) return;

        IsUploading = true;
        UpdateCanStart();

        _uploadCts = new CancellationTokenSource();
        var token = _uploadCts.Token;

        var filesToProcess = new List<string>(_filePaths);
        var includeImages = IncludeImages;

        try
        {
            foreach (var filePath in filesToProcess)
            {
                if (token.IsCancellationRequested) break;

                var fileName = Path.GetFileName(filePath);
                AppendLog($"Processing {fileName}...");

                try
                {
                    await Task.Run(async () =>
                    {
                        var parser = new ColladaLoader();
                        var prims = parser.Load(filePath, includeImages);

                        if (prims.Count == 0)
                        {
                            Dispatcher.UIThread.Post(() => AppendLog($"No geometry found in {fileName}."));
                            return;
                        }

                        var name = Path.GetFileNameWithoutExtension(filePath);
                        var description = $"Radegast {DateTime.Now.ToString(CultureInfo.InvariantCulture)}";

                        var uploader = new ModelUploader(Client, prims, name, description)
                        {
                            IncludePhysicsStub = true,
                            UseModelAsPhysics = false
                        };

                        await uploader.Upload(
                            res => Dispatcher.UIThread.Post(() =>
                                AppendLog(res == null
                                    ? $"Upload failed: {fileName}"
                                    : $"Upload success: {fileName}")),
                            token);

                    }, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => AppendLog($"Error uploading {fileName}: {ex.Message}"));
                }
            }
        }
        finally
        {
            _uploadCts.Dispose();
            _uploadCts = null;

            Dispatcher.UIThread.Post(() =>
            {
                IsUploading = false;
                UpdateCanStart();
                RefreshStartButtonText();
                AppendLog("Done.");
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
        StatusLog += message + Environment.NewLine;
    }

    private void NetCom_ClientConnected(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCanStart();
            RefreshStartButtonText();
        });
    }

    private void NetCom_ClientDisconnected(object? sender, DisconnectedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCanStart();
            RefreshStartButtonText();
        });
    }

    public void Dispose()
    {
        _instance.NetCom.ClientConnected -= NetCom_ClientConnected;
        _instance.NetCom.ClientDisconnected -= NetCom_ClientDisconnected;

        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        _uploadCts = null;
    }
}
