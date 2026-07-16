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
using System.Collections.Generic;
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
/// Thread-safe LRU cache for decoded <see cref="Bitmap"/> instances.
/// Evicted entries are disposed immediately to release Skia pixel buffers.
/// </summary>
internal sealed class LruBitmapCache : IDisposable
{
    private int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Value)>> _map;
    private readonly LinkedList<(string Key, Bitmap Value)> _order = new();
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _map.Count; } }

    /// <summary>
    /// Gets or sets the maximum number of bitmaps the cache will hold.
    /// Setting a smaller value immediately evicts the least-recently-used
    /// entries until the count fits within the new capacity.
    /// Evicted bitmaps are disposed after the lock is released.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is less than 1.</exception>
    public int Capacity
    {
        get { lock (_lock) return _capacity; }
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Capacity must be at least 1.");
            var evicted = new List<Bitmap>();
            lock (_lock)
            {
                _capacity = value;
                while (_map.Count > _capacity)
                {
                    var lru = _order.Last!;
                    _order.RemoveLast();
                    _map.Remove(lru.Value.Key);
                    evicted.Add(lru.Value.Value);
                }
            }
            foreach (var bmp in evicted)
                bmp.Dispose();
        }
    }

    public LruBitmapCache(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _map = new Dictionary<string, LinkedListNode<(string, Bitmap)>>(capacity);
    }

    /// <summary>
    /// Try to get a cached bitmap, promoting it to most-recently-used.
    /// </summary>
    public bool TryGet(string key, out Bitmap? bitmap)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                bitmap = node.Value.Value;
                return true;
            }
        }
        bitmap = null;
        return false;
    }

    /// <summary>
    /// Add or refresh a bitmap. If at capacity, the least-recently-used entry is evicted and disposed.
    /// </summary>
    public void Add(string key, Bitmap bitmap)
    {
        Bitmap? replacedBitmap = null;
        Bitmap? lruBitmap = null;
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
                replacedBitmap = existing.Value.Value;
            }

            var node = new LinkedListNode<(string, Bitmap)>((key, bitmap));
            _order.AddFirst(node);
            _map[key] = node;

            if (_map.Count > _capacity)
            {
                var lru = _order.Last!;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
                lruBitmap = lru.Value.Value;
            }
        }
        replacedBitmap?.Dispose();
        lruBitmap?.Dispose();
    }

    /// <summary>
    /// Remove and dispose the cached bitmap for <paramref name="key"/>, if present.
    /// The next request for the same key will trigger a fresh download and decode.
    /// </summary>
    /// <returns><see langword="true"/> if an entry was found and removed.</returns>
    public bool Invalidate(string key)
    {
        Bitmap? evicted = null;
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var node))
                return false;
            _order.Remove(node);
            _map.Remove(key);
            evicted = node.Value.Value;
        }
        evicted.Dispose();
        return true;
    }

    /// <summary>
    /// Remove and dispose every cached bitmap at once.
    /// The collections are swapped out under the lock so the critical section
    /// is O(1) regardless of cache size; disposal happens outside the lock.
    /// </summary>
    public void InvalidateAll()
    {
        LinkedList<(string Key, Bitmap Value)> snapshot;
        lock (_lock)
        {
            snapshot = new LinkedList<(string Key, Bitmap Value)>(_order);
            _order.Clear();
            _map.Clear();
        }
        foreach (var entry in snapshot)
            entry.Value.Dispose();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var node in _order)
                node.Value.Dispose();
            _order.Clear();
            _map.Clear();
        }
    }
}

/// <summary>
/// Central throttled queue
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
    private readonly LruBitmapCache _cache;

    private const int DefaultCacheCapacity = 128;
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
        _cache = new LruBitmapCache(DefaultCacheCapacity);

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
        if (_cache.TryGet(key, out var cached)) { onComplete(cached); return; }
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
        if (_cache.TryGet(key, out var cached)) { onComplete(cached); return; }
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

    /// <summary>
    /// Gets or sets the maximum number of decoded bitmaps held in the LRU cache.
    /// Reducing the value immediately evicts least-recently-used entries and
    /// disposes their Skia pixel buffers. Default is 2500.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to less than 1.</exception>
    public int CacheCapacity
    {
        get => _cache.Capacity;
        set => _cache.Capacity = value;
    }

    /// <summary>Number of decoded bitmaps currently held in the LRU cache.</summary>
    public int CacheCount => _cache.Count;

    /// <summary>
    /// Try to retrieve a previously decoded bitmap from the cache without enqueueing a new request.
    /// </summary>
    public bool TryGetCached(string key, out Bitmap? bitmap) => _cache.TryGet(key, out bitmap);

    /// <summary>
    /// Evict the cached bitmap for <paramref name="key"/> and allow the next
    /// <see cref="Enqueue"/> or <see cref="EnqueueDecode"/> call to re-download
    /// and re-decode it. Use this when an asset update has been detected server-side.
    /// </summary>
    /// <returns><see langword="true"/> if a cached entry was found and removed.</returns>
    public bool InvalidateCache(string key)
    {
        _pending.TryRemove(key, out _);
        return _cache.Invalidate(key);
    }

    /// <summary>
    /// Evict and dispose every cached bitmap. Call this immediately before or
    /// after a region teleport so stale sim textures are not served from cache.
    /// Any in-flight pending keys are also cleared so re-enqueuing works immediately.
    /// </summary>
    public void InvalidateAll()
    {
        _pending.Clear();
        _cache.InvalidateAll();
    }

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

                // Fire-and-forget: ProcessItem's own download/decode semaphores already
                // bound true concurrency (4 downloads / 2 decodes). Blocking this dispatcher
                // thread on the result would cap in-flight items at WorkerCount instead,
                // defeating the point of having independently-tunable download/decode limits.
                _ = ProcessItemSafe(item, token);
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

    private async Task ProcessItemSafe(WorkItem item, CancellationToken token)
    {
        try
        {
            await ProcessItem(item, token);
        }
        catch (OperationCanceledException)
        {
            // Queue is shutting down; matches ProcessLoop's prior behavior of not
            // invoking OnComplete for in-flight items on cancellation.
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
                    data = await _httpClient.GetByteArrayAsync(item.Url, CancellationToken.None);
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
                    if (bitmap != null)
                        _cache.Add(item.Key, bitmap);
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

        // Do not dispose _httpClient — HttpClient is designed to be long-lived.
        // Disposing it races with the internal connection pool scavenger task
        // (CheckUsabilityOnScavenge), causing ObjectDisposedException on NetworkStream.
        _downloadSemaphore.Dispose();
        _decodeSemaphore.Dispose();
        _highQueue.Dispose();
        _normalQueue.Dispose();
        _lowQueue.Dispose();
        _cts.Dispose();
        _cache.Dispose();
    }

    private sealed record WorkItem(
        string Key,
        string? Url,
        byte[]? RawData,
        Action<Bitmap?> OnComplete);
}
