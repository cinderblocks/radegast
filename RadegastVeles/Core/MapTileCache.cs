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
using LibreMetaverse;
using LibreMetaverse.StructuredData;

namespace Radegast.Veles.Core;

/// <summary>
/// Decoded world-map tiles, shared by every map view. A tile is addressed by the map server it
/// came from, a zoom level and the grid coordinates of its south-west region. A level 1 tile
/// covers one region; each further level doubles the regions covered per side, up to
/// <see cref="MaxLevel"/>. Every tile is the same pixel size whatever its level.
/// </summary>
public static class MapTileCache
{
    public const string SecondLifeMapServer = "https://map.secondlife.com/";

    public const int MinLevel = 1;
    public const int MaxLevel = 8;

    /// <summary>Pixels per side of a tile image.</summary>
    public const int TilePixels = 256;

    /// <summary>Tiles kept in memory unless the user chooses otherwise; about 130 MB when full.</summary>
    public const int DefaultCacheCapacity = 512;

    // A tile the server does not have (open water, or a grid without that level) is not
    // requested again until this much time has passed.
    private static readonly TimeSpan MissingTileRetryInterval = TimeSpan.FromMinutes(5);
    private const int MaxMissingTileEntries = 8192;

    private readonly record struct TileKey(string Server, int Level, uint X, uint Y);

    private static readonly LruCache<TileKey, Bitmap> Cache =
        new(DefaultCacheCapacity, onEvicted: static (_, bmp) => bmp.Dispose());
    private static readonly object _lock = new();
    private static readonly Dictionary<TileKey, List<Action>> _pendingCallbacks = new();
    private static readonly Dictionary<TileKey, long> _missingSince = new();

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

    /// <summary>Regions covered by one side of a tile at the given level.</summary>
    public static int TileSpan(int level) => 1 << (Math.Clamp(level, MinLevel, MaxLevel) - 1);

    /// <summary>
    /// The coarsest level whose tiles still have at least one image pixel per screen pixel
    /// when a region is drawn <paramref name="pixelsPerRegion"/> pixels wide.
    /// </summary>
    public static int LevelFor(double pixelsPerRegion)
    {
        if (!(pixelsPerRegion > 0)) return MaxLevel;
        int level = MinLevel;
        while (level < MaxLevel && TilePixels / (double)TileSpan(level + 1) >= pixelsPerRegion)
            level++;
        return level;
    }

    /// <summary>The south-west region coordinate of the tile that contains the given coordinate.</summary>
    public static uint TileOrigin(uint grid, int level)
    {
        uint span = (uint)TileSpan(level);
        return grid - grid % span;
    }

    /// <summary>
    /// The map tile server for the grid this session is logged in to, ending in a slash,
    /// or null when the grid does not advertise one.
    /// </summary>
    public static string? ServerFor(RadegastInstanceAvalonia instance)
    {
        if (instance.NetCom?.LoginOptions?.Grid?.Platform == "SecondLife")
            return SecondLifeMapServer;

        try
        {
            var extras = instance.Client.Network.CurrentSim?.Features?.Get("OpenSimExtras");
            if (extras is OSDMap map && map.TryGetValue("map-server-url", out var value))
                return NormalizeServer(value.AsString());
        }
        catch (Exception ex)
        {
            Logger.Debug("Could not read the map server address from the simulator features.", ex);
        }
        return null;
    }

    /// <summary>Returns the address with a trailing slash if it is an http or https URL, else null.</summary>
    public static string? NormalizeServer(string? url)
    {
        if (!WebLink.TryNormalize(url, out var normalized)) return null;
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    /// <summary>The cached single-region tile, or null.</summary>
    public static Bitmap? GetTile(string server, uint gridX, uint gridY) =>
        GetTile(server, MinLevel, gridX, gridY);

    /// <summary>The cached tile whose south-west region is (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
    public static Bitmap? GetTile(string server, int level, uint x, uint y) =>
        Cache.TryGetValue(new TileKey(server, level, x, y), out var bitmap) ? bitmap : null;

    /// <summary>Requests the single-region tile; see the level overload.</summary>
    public static void RequestTile(string server, uint gridX, uint gridY, Action? onComplete = null) =>
        RequestTile(server, MinLevel, gridX, gridY, onComplete);

    /// <summary>
    /// Downloads a tile unless it is cached, already on its way, or recently found missing.
    /// <paramref name="onComplete"/> is posted to the UI thread once the tile is available;
    /// it is not called for a tile that turns out to be missing.
    /// </summary>
    public static void RequestTile(string server, int level, uint x, uint y, Action? onComplete = null)
    {
        var key      = new TileKey(server, level, x, y);
        var queueKey = $"maptile:{server}:{level}:{x}:{y}";

        lock (_lock)
        {
            if (Cache.ContainsKey(key))
            {
                if (onComplete != null)
                    Dispatcher.UIThread.Post(onComplete);
                return;
            }

            if (_missingSince.TryGetValue(key, out var since))
            {
                if (Environment.TickCount64 - since < (long)MissingTileRetryInterval.TotalMilliseconds)
                    return;
                _missingSince.Remove(key);
            }

            if (onComplete != null)
            {
                if (!_pendingCallbacks.TryGetValue(key, out var list))
                {
                    list = [];
                    _pendingCallbacks[key] = list;
                }
                if (!list.Contains(onComplete))
                    list.Add(onComplete);
            }

            if (TextureDownloadQueue.Instance.IsPending(queueKey)) return;

            // cacheResult: false — this class must be the sole owner of the decoded tile bitmap.
            // If TextureDownloadQueue's own cache also held a reference, two independently
            // evicting LRU caches could dispose the same Bitmap out from under a live GetTile().
            var url = $"{server}map-{level}-{x}-{y}-objects.jpg";
            TextureDownloadQueue.Instance.Enqueue(queueKey, url, bitmap =>
            {
                if (bitmap != null)
                    Cache.AddOrUpdate(key, bitmap);

                List<Action>? toFire;
                lock (_lock)
                {
                    _pendingCallbacks.TryGetValue(key, out toFire);
                    _pendingCallbacks.Remove(key);

                    if (bitmap == null)
                    {
                        if (_missingSince.Count >= MaxMissingTileEntries)
                            _missingSince.Clear();
                        _missingSince[key] = Environment.TickCount64;
                    }
                }

                if (bitmap != null && toFire != null)
                    foreach (var cb in toFire)
                        Dispatcher.UIThread.Post(cb);
            }, TexturePriority.Low, cacheResult: false);
        }
    }
}
