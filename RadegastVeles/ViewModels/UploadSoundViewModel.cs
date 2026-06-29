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
using LibreMetaverse.Assets;
using NVorbis;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class UploadSoundViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private string _fileName = string.Empty;
    private byte[]? _uploadData;
    private CancellationTokenSource? _uploadCts;

    [ObservableProperty] private string _statusLog = string.Empty;
    [ObservableProperty] private string _assetId = UUID.Zero.ToString();
    [ObservableProperty] private string _soundName = string.Empty;
    [ObservableProperty] private string _fileInfo = string.Empty;
    [ObservableProperty] private bool _canUpload;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private string _uploadButtonText = "Upload Sound";

    public UploadSoundViewModel(RadegastInstanceAvalonia instance)
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
        var cost = Client.Self.Benefits?.SoundUploadCost ?? -1;
        UploadButtonText = cost > 0 ? $"Upload L${cost}" : "Upload Sound";
    }

    public void LoadFile(string filePath)
    {
        _fileName = filePath;
        IsLoading = true;
        _uploadData = null;
        UpdateCanUpload();

        AppendLog($"Loading {Path.GetFileName(filePath)}...");

        Task.Run(() =>
        {
            try
            {
                var raw  = File.ReadAllBytes(filePath);
                var data = PrepareForUpload(raw, filePath);

                Dispatcher.UIThread.Post(() =>
                {
                    _uploadData = data;
                    SoundName   = Path.GetFileNameWithoutExtension(filePath);
                    FileInfo    = $"{raw.Length / 1024.0:F1} KB source → {data.Length / 1024.0:F1} KB OGG";
                    AssetId     = UUID.Zero.ToString();
                    AppendLog("File loaded.");
                    IsLoading = false;
                    UpdateCanUpload();
                    RefreshUploadButtonText();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _uploadData = null;
                    AppendLog($"Failed to load file: {ex.Message}");
                    IsLoading = false;
                    UpdateCanUpload();
                });
            }
        });
    }

    /// <summary>
    /// Returns OGG bytes ready for upload.
    /// OGG files are passed through unchanged.
    /// WAV files are decoded and re-encoded to OGG (mono 44100 Hz).
    /// </summary>
    private byte[] PrepareForUpload(byte[] raw, string filePath)
    {
        // OGG Vorbis: already in SL's native format
        if (raw.Length >= 4 &&
            raw[0] == 0x4F && raw[1] == 0x67 && raw[2] == 0x67 && raw[3] == 0x53)
        {
            AppendLog("Detected OGG Vorbis — using directly.");
            return raw;
        }

        // WAV (RIFF/WAVE)
        if (raw.Length >= 12 &&
            raw[0] == 0x52 && raw[1] == 0x49 && raw[2] == 0x46 && raw[3] == 0x46 &&
            raw[8] == 0x57 && raw[9] == 0x41 && raw[10] == 0x56 && raw[11] == 0x45)
        {
            AppendLog("Detected WAV — converting to OGG Vorbis (mono 44100 Hz)…");
            var (pcm, rate, channels) = ParseWavPcm(raw);
            AppendLog($"  WAV: {channels}ch {rate}Hz 16-bit → encoding…");
            var ogg = AssetSound.PcmToOgg(pcm, rate, channels);
            if (channels > 1)
                AppendLog("  Note: downmixed to mono (SL plays all sounds in 3D mono).");
            return ogg;
        }

        // Try treating as raw OGG anyway — some files lack the right magic
        AppendLog($"Warning: unrecognised format for '{Path.GetFileName(filePath)}'. " +
                  "Attempting upload as-is (expected OGG Vorbis).");
        return raw;
    }

    // Minimal RIFF/WAV parser — returns raw interleaved PCM bytes (16-bit LE).
    private static (byte[] pcm, int sampleRate, int channels) ParseWavPcm(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        br.ReadUInt32(); // "RIFF"
        br.ReadUInt32(); // file size - 8
        br.ReadUInt32(); // "WAVE"

        int sampleRate    = 0;
        int channels      = 0;
        int bitsPerSample = 16;
        byte[]? pcm       = null;

        while (ms.Position < ms.Length - 8)
        {
            uint chunkId   = br.ReadUInt32();
            int  chunkSize = (int)br.ReadUInt32();
            long nextChunk = ms.Position + chunkSize;

            if (chunkId == 0x20746D66u) // "fmt "
            {
                int fmt = br.ReadUInt16();
                if (fmt != 1) throw new InvalidDataException(
                    $"WAV audioFormat {fmt} is not PCM. Only uncompressed PCM WAV is supported.");
                channels      = br.ReadUInt16();
                sampleRate    = (int)br.ReadUInt32();
                br.ReadUInt32(); // byteRate
                br.ReadUInt16(); // blockAlign
                bitsPerSample = br.ReadUInt16();
            }
            else if (chunkId == 0x61746164u) // "data"
            {
                pcm = br.ReadBytes(chunkSize);
            }

            ms.Position = Math.Min(nextChunk + (chunkSize & 1), ms.Length);
            if (pcm != null && sampleRate > 0) break;
        }

        if (pcm == null || sampleRate == 0)
            throw new InvalidDataException("WAV file is missing required fmt or data chunks.");

        // Normalise to 16-bit LE
        if (bitsPerSample == 8)
        {
            var pcm16 = new byte[pcm.Length * 2];
            for (int i = 0; i < pcm.Length; i++)
            {
                short s = (short)((pcm[i] - 128) << 8);
                pcm16[i * 2]     = (byte)(s & 0xFF);
                pcm16[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            pcm = pcm16;
        }
        else if (bitsPerSample != 16)
        {
            throw new InvalidDataException(
                $"WAV bit depth {bitsPerSample} is not supported. Use 8-bit or 16-bit PCM WAV.");
        }

        return (pcm, sampleRate, channels);
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task Upload()
    {
        if (_uploadData == null) return;

        IsUploading = true;
        UpdateCanUpload();

        _uploadCts = new CancellationTokenSource();
        var token = _uploadCts.Token;

        var soundName = string.IsNullOrWhiteSpace(SoundName)
            ? Path.GetFileNameWithoutExtension(_fileName)
            : SoundName;
        var description = $"Uploaded with Radegast on {DateTime.Now.ToString(CultureInfo.InvariantCulture)}";
        var perms = new Permissions { EveryoneMask = PermissionMask.All, NextOwnerMask = PermissionMask.All };
        var folder = Client.Inventory.FindFolderForType(AssetType.Sound);
        var data = _uploadData;

        AppendLog("Uploading...");

        try
        {
            var result = await Client.Inventory.CreateItemFromAssetAsync(
                data, soundName, description,
                AssetType.Sound, InventoryType.Sound,
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
