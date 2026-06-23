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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCompress.Readers;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// A single Piper/VITS model from the sherpa-onnx GitHub release assets.
/// Each tarball contains the .onnx, tokens.txt, and espeak-ng-data.
/// </summary>
public sealed class PiperVoiceEntry
{
    /// <summary>Asset file name, e.g. <c>vits-piper-en_US-lessac-medium.tar.bz2</c>.</summary>
    public string AssetName    { get; init; } = string.Empty;
    /// <summary>Human-readable name derived from the asset name.</summary>
    public string DisplayName  { get; init; } = string.Empty;
    public string Language     { get; init; } = string.Empty;
    public string Quality      { get; init; } = string.Empty;
    public string DownloadUrl  { get; init; } = string.Empty;
    public long   SizeBytes    { get; init; }

    public string SizeText
    {
        get
        {
            long b = SizeBytes;
            if (b >= 1_000_000) return $"{b / 1_000_000.0:F0} MB";
            if (b >= 1_000)     return $"{b / 1_000.0:F0} KB";
            return $"{b} B";
        }
    }
}

public sealed partial class PiperVoiceDownloaderViewModel : ObservableObject, IDisposable
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/k2-fsa/sherpa-onnx/releases/tags/tts-models";

    private readonly HttpClient _http;
    private CancellationTokenSource? _downloadCts;

    public PiperVoiceDownloaderViewModel()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Radegast-Veles", "1.0"));
    }

    // ── Observable properties ─────────────────────────────────────────────

    [ObservableProperty] private bool   _isFetching;
    [ObservableProperty] private bool   _isDownloading;
    [ObservableProperty] private string _statusText   = "Click 'Fetch Voices' to load the voice list.";
    [ObservableProperty] private double _progress;       // 0–100
    [ObservableProperty] private string _downloadedPath = string.Empty;

    [ObservableProperty] private string _languageFilter = string.Empty;
    [ObservableProperty] private string _qualityFilter  = string.Empty;

    [ObservableProperty] private PiperVoiceEntry? _selectedVoice;

    public bool CanDownload => SelectedVoice != null && !IsDownloading;

    partial void OnSelectedVoiceChanged(PiperVoiceEntry? value)  => OnPropertyChanged(nameof(CanDownload));
    partial void OnIsDownloadingChanged(bool value)              => OnPropertyChanged(nameof(CanDownload));

    public ObservableCollection<PiperVoiceEntry> Voices         { get; } = [];
    public ObservableCollection<PiperVoiceEntry> FilteredVoices { get; } = [];
    public ObservableCollection<string>          Languages      { get; } = [];
    public ObservableCollection<string>          Qualities      { get; } = ["(any)", "low", "medium", "high", "x_low"];

    private System.Collections.Generic.List<PiperVoiceEntry> _allVoices = [];

    partial void OnLanguageFilterChanged(string value) => ApplyFilter();
    partial void OnQualityFilterChanged(string value)  => ApplyFilter();

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task FetchVoicesAsync()
    {
        IsFetching  = true;
        StatusText  = "Fetching voice list from sherpa-onnx releases…";
        Voices.Clear();
        FilteredVoices.Clear();
        Languages.Clear();
        _allVoices.Clear();

        try
        {
            string json = await _http.GetStringAsync(ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);

            var entries = new System.Collections.Generic.List<PiperVoiceEntry>();

            if (doc.RootElement.TryGetProperty("assets", out var assetsEl))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                    if (!name.StartsWith("vits-piper-", StringComparison.Ordinal) ||
                        !name.EndsWith(".tar.bz2", StringComparison.Ordinal))
                        continue;

                    string url  = asset.TryGetProperty("browser_download_url", out var up) ? up.GetString() ?? "" : "";
                    long   size = asset.TryGetProperty("size",                  out var sp) ? sp.GetInt64()       : 0;

                    // Parse display metadata from the asset name:
                    // vits-piper-<lang>_<region>-<speaker>-<quality>.tar.bz2
                    // or  vits-piper-<lang>-<speaker>-<quality>.tar.bz2
                    string stem    = name[("vits-piper-".Length)..^(".tar.bz2".Length)];
                    string[] parts = stem.Split('-');

                    string langCode = parts.Length > 0 ? parts[0] : stem;
                    string quality  = parts.Length > 1 ? parts[^1] : "";
                    string speaker  = parts.Length > 2
                        ? string.Join("-", parts[1..^1])
                        : "";

                    // Normalise quality: only keep if it matches known labels
                    string[] knownQualities = ["low", "medium", "high", "x_low"];
                    if (!knownQualities.Contains(quality, StringComparer.OrdinalIgnoreCase))
                    {
                        speaker = string.Join("-", parts[1..]);
                        quality  = "";
                    }

                    string display = string.IsNullOrEmpty(speaker) ? stem : $"{speaker} [{langCode}]";

                    entries.Add(new PiperVoiceEntry
                    {
                        AssetName   = name,
                        DisplayName = display,
                        Language    = langCode,
                        Quality     = quality,
                        DownloadUrl = url,
                        SizeBytes   = size,
                    });
                }
            }

            _allVoices = entries;

            var langs = entries.Select(e => e.Language).Distinct().OrderBy(l => l).ToList();
            Languages.Add("(any)");
            foreach (var l in langs) Languages.Add(l);

            ApplyFilter();
            StatusText = $"Loaded {entries.Count} voices.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to fetch voice list: {ex.Message}";
        }
        finally
        {
            IsFetching = false;
        }
    }

    [RelayCommand]
    public async Task DownloadVoiceAsync(string targetDirectory)
    {
        if (SelectedVoice is not { } voice) return;

        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        Progress = 0;
        StatusText = $"Downloading {voice.AssetName}…";
        DownloadedPath = string.Empty;

        try
        {
            Directory.CreateDirectory(targetDirectory);
            string tmpFile = Path.Combine(Path.GetTempPath(), voice.AssetName);

            // ── Download ──────────────────────────────────────────────────
            using (var response = await _http.GetAsync(voice.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? voice.SizeBytes;

                await using var src  = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
                await using var dest = File.Create(tmpFile);

                var buffer = new byte[81920];
                long done  = 0;
                int  read;
                while ((read = await src.ReadAsync(buffer, _downloadCts.Token)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), _downloadCts.Token);
                    done += read;
                    Progress = total > 0 ? done * 50.0 / total : 0; // first 50% = download
                }
            }

            // ── Extract .tar.bz2 ─────────────────────────────────────────
            StatusText = $"Extracting {voice.AssetName}…";
            string destDir = await Task.Run(() =>
            {
                string? topDir = null;
                using var fileStream = File.OpenRead(tmpFile);
                using var reader     = ReaderFactory.OpenReader(fileStream);

                long total   = new FileInfo(tmpFile).Length;
                long read    = 0;

                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory) continue;

                    string entryKey = reader.Entry.Key ?? "";
                    // Strip the top-level folder prefix (vits-piper-xxx/)
                    int slash = entryKey.IndexOf('/');
                    string relative = slash >= 0 ? entryKey[(slash + 1)..] : entryKey;

                    if (topDir == null && slash >= 0)
                        topDir = entryKey[..slash];

                    if (string.IsNullOrEmpty(relative)) continue;

                    string outPath = Path.Combine(targetDirectory, relative);
                    string? dir    = Path.GetDirectoryName(outPath);
                    if (dir != null) Directory.CreateDirectory(dir);

                    using var entryStream = reader.OpenEntryStream();
                    using var outFile     = File.Create(outPath);
                    entryStream.CopyTo(outFile);

                    read += reader.Entry.CompressedSize;
                    Progress = 50 + (total > 0 ? read * 50.0 / total : 0);
                }

                return targetDirectory;
            }, _downloadCts.Token);

            try { File.Delete(tmpFile); } catch { /* best-effort */ }

            DownloadedPath = destDir;
            Progress       = 100;
            StatusText     = $"Done — saved to {destDir}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled.";
            Progress   = 0;
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts  = null;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        FilteredVoices.Clear();
        foreach (var v in _allVoices)
        {
            if (!string.IsNullOrEmpty(LanguageFilter) && LanguageFilter != "(any)" &&
                !v.Language.Equals(LanguageFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrEmpty(QualityFilter) && QualityFilter != "(any)" &&
                !v.Quality.Equals(QualityFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            FilteredVoices.Add(v);
        }
    }

    public void Dispose()
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _http.Dispose();
    }
}
