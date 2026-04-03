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
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Radegast.Veles.Core;

/// <summary>
/// Priority levels for texture download requests.
/// </summary>
public enum TexturePriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

/// <summary>
/// Central throttled queue for downloading and decoding images.
/// Limits both concurrent HTTP downloads and concurrent bitmap decode operations
/// to prevent CPU/network spikes.
/// </summary>
public sealed class TextureDownloadQueue : IDisposable
{
    /// <summary>Shared application-wide instance.</summary>
    public static TextureDownloadQueue Instance { get; } = new();

    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _downloadSemaphore;
    private readonly SemaphoreSlim _decodeSemaphore;
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly BlockingCollection<WorkItem> _highQueue = new();
    private readonly BlockingCollection<WorkItem> _normalQueue = new();
    private readonly BlockingCollection<WorkItem> _lowQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workers;

    private const int MaxConcurrentDownloads = 4;
    private const int MaxConcurrentDecodes = 2;
    private const int WorkerCount = 4;

    public TextureDownloadQueue()
    {
        _downloadSemaphore = new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads);
        _decodeSemaphore = new SemaphoreSlim(MaxConcurrentDecodes, MaxConcurrentDecodes);

        _workers = new Task[WorkerCount];
        for (int i = 0; i < WorkerCount; i++)
        {
            _workers[i] = Task.Factory.StartNew(
                ProcessLoop,
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Enqueue a URL-based image download + decode.
    /// Returns immediately; the callback fires on completion with the decoded bitmap (or null on failure).
    /// Duplicate requests for the same key are silently ignored.
    /// </summary>
    /// <param name="key">Unique key to deduplicate requests (e.g. "maptile:1000:1000").</param>
    /// <param name="url">The URL to download from.</param>
    /// <param name="onComplete">Called with the decoded Bitmap (or null on failure).</param>
    /// <param name="priority">Download priority.</param>
    public void Enqueue(string key, string url, Action<Bitmap?> onComplete, TexturePriority priority = TexturePriority.Normal)
    {
        if (!_pending.TryAdd(key, 0)) return;

        var item = new WorkItem(key, url, null, onComplete);
        var queue = priority switch
        {
            TexturePriority.High => _highQueue,
            TexturePriority.Low => _lowQueue,
            _ => _normalQueue
        };
        queue.Add(item);
    }

    /// <summary>
    /// Enqueue a raw-bytes image decode (no download needed).
    /// </summary>
    public void EnqueueDecode(string key, byte[] data, Action<Bitmap?> onComplete, TexturePriority priority = TexturePriority.Normal)
    {
        if (!_pending.TryAdd(key, 0)) return;

        var item = new WorkItem(key, null, data, onComplete);
        var queue = priority switch
        {
            TexturePriority.High => _highQueue,
            TexturePriority.Low => _lowQueue,
            _ => _normalQueue
        };
        queue.Add(item);
    }

    /// <summary>
    /// Check whether a request with the given key is currently pending.
    /// </summary>
    public bool IsPending(string key) => _pending.ContainsKey(key);

    private void ProcessLoop()
    {
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            WorkItem? item = null;
            try
            {
                // Priority: High > Normal > Low
                // Try high queue first (non-blocking), then normal, then block on all
                if (_highQueue.TryTake(out item))
                { }
                else if (_normalQueue.TryTake(out item))
                { }
                else
                {
                    // Block until any queue has work, checking periodically
                    while (!token.IsCancellationRequested)
                    {
                        if (_highQueue.TryTake(out item, 50, token)) break;
                        if (_normalQueue.TryTake(out item, 0, token)) break;
                        if (_lowQueue.TryTake(out item, 50, token)) break;
                    }
                }

                if (item == null || token.IsCancellationRequested) continue;

                ProcessItem(item, token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Swallow unexpected exceptions to keep worker alive
                if (item != null)
                {
                    _pending.TryRemove(item.Key, out _);
                    try { item.OnComplete(null); } catch { }
                }
            }
        }
    }

    private async Task ProcessItem(WorkItem item, CancellationToken token)
    {
        byte[]? data = item.RawData;
        Bitmap? bitmap = null;

        try
        {
            // Download phase (if needed)
            if (data == null && item.Url != null)
            {
                await _downloadSemaphore.WaitAsync(token);
                try
                {
                    data = await _httpClient.GetByteArrayAsync(item.Url);
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }

            // Decode phase
            if (data != null)
            {
                await _decodeSemaphore.WaitAsync(token);
                try
                {
                    using var ms = new MemoryStream(data);
                    bitmap = new Bitmap(ms);
                }
                finally
                {
                    _decodeSemaphore.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            bitmap = null;
        }
        finally
        {
            _pending.TryRemove(item.Key, out _);
        }

        item.OnComplete(bitmap);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _highQueue.CompleteAdding();
        _normalQueue.CompleteAdding();
        _lowQueue.CompleteAdding();

        try { Task.WaitAll(_workers, TimeSpan.FromSeconds(2)); } catch { }

        _downloadSemaphore.Dispose();
        _decodeSemaphore.Dispose();
        _highQueue.Dispose();
        _normalQueue.Dispose();
        _lowQueue.Dispose();
        _httpClient.Dispose();
        _cts.Dispose();
    }

    private sealed record WorkItem(
        string Key,
        string? Url,
        byte[]? RawData,
        Action<Bitmap?> OnComplete);
}
