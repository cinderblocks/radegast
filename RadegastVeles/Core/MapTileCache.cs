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
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Radegast.Veles.Core;

public static class MapTileCache
{
    // Small JPEGs (map-1-*-objects.jpg); a generous bound is cheap and keeps a long-running
    // bot session (or one that visits many regions) from growing this cache unboundedly.
    private const int DefaultCacheCapacity = 512;

    private static readonly LruCache<(uint, uint), Bitmap> Cache =
        new(DefaultCacheCapacity, onEvicted: static (_, bmp) => bmp.Dispose());
    private static readonly object _lock = new();
    private static readonly Dictionary<string, List<Action>> _pendingCallbacks = new();

    /// <summary>
    /// Gets or sets the maximum number of decoded tile bitmaps held in memory. This is the
    /// sole owner of tile bitmaps (see the cacheResult:false note in RequestTile below), so
    /// this is what backs the Preferences "Texture Bitmap Memory Cache" setting.
    /// </summary>
    public static int CacheCapacity
    {
        get => Cache.Capacity;
        set => Cache.Capacity = value;
    }

    /// <summary>Number of decoded tile bitmaps currently held in memory.</summary>
    public static int CacheCount => Cache.Count;

    public static Bitmap? GetTile(uint gridX, uint gridY)
    {
        return Cache.TryGetValue((gridX, gridY), out var bitmap) ? bitmap : null;
    }

    public static void RequestTile(uint gridX, uint gridY, Action? onComplete = null)
    {
        var key      = (gridX, gridY);
        var queueKey = $"maptile:{gridX}:{gridY}";

        lock (_lock)
        {
            // If already cached, fire the callback immediately and return.
            if (Cache.ContainsKey(key))
            {
                if (onComplete != null)
                    Dispatcher.UIThread.Post(onComplete);
                return;
            }

            // Register this callback so it will fire when the download completes.
            if (onComplete != null)
            {
                if (!_pendingCallbacks.TryGetValue(queueKey, out var list))
                {
                    list = [];
                    _pendingCallbacks[queueKey] = list;
                }
                list.Add(onComplete);
            }

            // If a download is already in-flight, the callback is now registered; nothing else to do.
            if (TextureDownloadQueue.Instance.IsPending(queueKey)) return;

            // cacheResult: false — this class is the sole owner of the decoded tile bitmaps
            // (see the Cache field above). Letting TextureDownloadQueue's own internal cache
            // also hold a reference would give the same Bitmap instance to two independently
            // evicting/disposing LRU caches, which could dispose a tile out from under a live
            // GetTile() result mid-render (ObjectDisposedException in GridMapControl.Render).
            var url = $"https://map.secondlife.com/map-1-{gridX}-{gridY}-objects.jpg";
            TextureDownloadQueue.Instance.Enqueue(queueKey, url, bitmap =>
            {
                if (bitmap != null)
                    Cache.AddOrUpdate(key, bitmap);

                List<Action>? toFire;
                lock (_lock)
                {
                    _pendingCallbacks.TryGetValue(queueKey, out toFire);
                    _pendingCallbacks.Remove(queueKey);
                }

                if (toFire != null)
                    foreach (var cb in toFire)
                        Dispatcher.UIThread.Post(cb);
            }, TexturePriority.Low, cacheResult: false);
        }
    }
}
