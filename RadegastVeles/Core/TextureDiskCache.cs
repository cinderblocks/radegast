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
using System.IO;
using System.Threading.Tasks;
using LibreMetaverse;

namespace Radegast.Veles.Core;

/// <summary>
/// Persistent on-disk cache for raw JPEG 2000 texture assets.
/// Keyed by texture UUID; stored as <c>.j2k</c> files under a configurable
/// directory (default: <c>%AppData%\RadegastVeles\texturecache\</c>).
/// Files are written as-is from the asset server — no re-encoding occurs —
/// so a cache hit yields identical bytes to the original download and the
/// caller decodes them exactly once with <c>J2kImage.FromBytes</c>.
///
/// Server-side baked textures must NOT be cached here because their UUIDs
/// are reused across appearance changes with different pixel content.
/// Only standard asset-pipeline textures (passed through
/// <see cref="GridTextureHelper.DownloadSkBitmapAsync"/> or
/// <see cref="GridTextureHelper.Download"/>) should use this cache.
/// </summary>
internal static class TextureDiskCache
{
    private static readonly string DefaultCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RadegastVeles", "texturecache");

    private static string _cacheDir = DefaultCacheDir;
    private static int    _maxCachedFiles = 8192;
    private static bool   _enabled = true;

    /// <summary>
    /// Whether the disk cache is active.  When <c>false</c>, <see cref="TryGet"/>
    /// always returns <c>null</c> and <see cref="PutAsync"/> is a no-op.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Absolute path to the directory that stores cached <c>.j2k</c> files.
    /// Changing this at runtime causes subsequent reads and writes to use the new
    /// directory; the old directory is not migrated automatically.
    /// </summary>
    public static string CacheDir
    {
        get => _cacheDir;
        set
        {
            var dir = string.IsNullOrWhiteSpace(value) ? DefaultCacheDir : value;
            _cacheDir = dir;
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                Logger.Warn($"TextureDiskCache: failed to create cache directory '{dir}'; texture caching will silently fail.", ex);
            }
        }
    }

    /// <summary>
    /// Maximum number of <c>.j2k</c> files kept on disk before the oldest (by
    /// last-access time) are evicted.  The eviction pass removes 10 % of files
    /// at a time.  Minimum value is clamped to 64.
    /// </summary>
    public static int MaxCachedFiles
    {
        get => _maxCachedFiles;
        set => _maxCachedFiles = Math.Max(64, value);
    }

    static TextureDiskCache()
    {
        try { Directory.CreateDirectory(_cacheDir); }
        catch (Exception ex)
        {
            // non-fatal; cache writes will silently fail
            Logger.Warn($"TextureDiskCache: failed to create default cache directory '{_cacheDir}'.", ex);
        }
    }

    private static string FilePath(UUID textureId) =>
        Path.Combine(_cacheDir, textureId + ".j2k");

    /// <summary>
    /// Try to load raw JPEG 2000 bytes for <paramref name="textureId"/> from disk.
    /// Returns <c>null</c> if not cached, the cache is disabled, or any I/O error occurs.
    /// The caller is responsible for decoding the returned bytes (e.g. with
    /// <c>J2kImage.FromBytes</c>) and for any further memory management.
    /// </summary>
    public static byte[]? TryGet(UUID textureId)
    {
        if (!_enabled) return null;
        try
        {
            var path = FilePath(textureId);
            if (!File.Exists(path)) return null;

            // Touch the access time so LRU eviction keeps recently used files longer.
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);

            return File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            Logger.Debug($"TextureDiskCache: failed to read cached texture {textureId}.", ex);
            return null;
        }
    }

    /// <summary>
    /// Returns true when raw J2K bytes for <paramref name="textureId"/> are already on disk,
    /// without reading them. Used by prefetch paths that only need to know whether a
    /// download can be skipped.
    /// </summary>
    public static bool Contains(UUID textureId)
    {
        if (!_enabled) return false;
        try { return File.Exists(FilePath(textureId)); }
        catch { return false; }
    }

    /// <summary>
    /// Asynchronously write raw JPEG 2000 <paramref name="j2kData"/> bytes to disk.
    /// The byte array is captured by reference; the caller must not mutate or recycle
    /// the array after this call.
    /// Silently skips the write if the cache is disabled, already at capacity,
    /// or if the file already exists.
    /// Most callers discard the returned task (fire-and-forget); prefetch paths await
    /// it so that a subsequent <see cref="TryGet"/>/<see cref="Contains"/> is
    /// guaranteed to see the bytes.
    /// </summary>
    public static Task PutAsync(UUID textureId, byte[] j2kData)
    {
        if (!_enabled) return Task.CompletedTask;
        if (j2kData == null || j2kData.Length == 0) return Task.CompletedTask;

        return Task.Run(() =>
        {
            string? tmp = null;
            try
            {
                EvictIfNeeded();

                var path = FilePath(textureId);
                if (File.Exists(path)) return;  // already cached by a concurrent call

                // Write to a temp file then atomically move so a partial write is never
                // left behind if the process is killed mid-write.
                // The temp name must be unique PER WRITER: the same texture is often
                // stored concurrently (scene streamer + avatar viewer requesting the same
                // asset), and a shared "<id>.tmp" made the writers collide on WriteAllBytes
                // ("file in use by another process"). overwrite:true makes the move
                // race-tolerant too — both writers carry identical bytes, so last-in wins
                // harmlessly instead of throwing "file already exists".
                tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, j2kData);
                File.Move(tmp, path, overwrite: true);
                tmp = null; // moved successfully — nothing to clean up
            }
            catch (Exception ex)
            {
                Logger.Debug($"TextureDiskCache: failed to cache texture {textureId}.", ex);
            }
            finally
            {
                // Remove the orphaned temp file if the write or move failed.
                if (tmp != null)
                {
                    try { File.Delete(tmp); } catch { /* best effort */ }
                }
            }
        });
    }

    /// <summary>
    /// Returns the total size of all cached <c>.j2k</c> files in bytes.
    /// Returns 0 if the cache directory does not exist or cannot be read.
    /// </summary>
    public static long GetCacheSizeBytes()
    {
        try
        {
            var di = new DirectoryInfo(_cacheDir);
            if (!di.Exists) return 0L;
            long total = 0;
            foreach (var f in di.EnumerateFiles("*.j2k"))
                total += f.Length;
            return total;
        }
        catch (Exception ex)
        {
            Logger.Debug("TextureDiskCache: failed to compute cache size.", ex);
            return 0L;
        }
    }

    /// <summary>
    /// Returns the number of cached <c>.j2k</c> files on disk.
    /// </summary>
    public static int GetCacheFileCount()
    {
        try
        {
            var di = new DirectoryInfo(_cacheDir);
            if (!di.Exists) return 0;
            int count = 0;
            foreach (var _ in di.EnumerateFiles("*.j2k")) count++;
            return count;
        }
        catch (Exception ex)
        {
            Logger.Debug("TextureDiskCache: failed to count cache files.", ex);
            return 0;
        }
    }

    /// <summary>
    /// Deletes the cached <c>.j2k</c> file for <paramref name="textureId"/> from disk.
    /// Used to evict a single corrupt or truncated entry so the next request
    /// re-downloads and re-caches valid data.  Silently ignores any I/O errors.
    /// </summary>
    public static void Evict(UUID textureId)
    {
        try { File.Delete(FilePath(textureId)); }
        catch (Exception ex)
        {
            Logger.Debug($"TextureDiskCache: failed to evict cached texture {textureId}.", ex);
        }
    }

    /// <summary>
    /// Deletes all <c>.j2k</c> (and any orphaned legacy <c>.png</c>) files in the
    /// cache directory synchronously.
    /// </summary>
    public static void Clear()
    {
        try
        {
            var di = new DirectoryInfo(_cacheDir);
            if (!di.Exists) return;
            foreach (var f in di.EnumerateFiles("*.j2k"))
                try { f.Delete(); } catch (Exception ex) { Logger.Debug($"TextureDiskCache: failed to delete '{f.Name}'.", ex); }
            foreach (var f in di.EnumerateFiles("*.png"))  // sweep legacy files
                try { f.Delete(); } catch (Exception ex) { Logger.Debug($"TextureDiskCache: failed to delete legacy file '{f.Name}'.", ex); }
        }
        catch (Exception ex)
        {
            Logger.Warn("TextureDiskCache: failed to clear cache directory.", ex);
        }
    }

    // ── LRU eviction ─────────────────────────────────────────────────────────────

    private static void EvictIfNeeded()
    {
        try
        {
            var files = new DirectoryInfo(_cacheDir).GetFiles("*.j2k");
            if (files.Length < _maxCachedFiles) return;

            // Delete the oldest 10 % by last-access time.
            int toDelete = _maxCachedFiles / 10;
            Array.Sort(files, (a, b) =>
                a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));

            for (int i = 0; i < toDelete && i < files.Length; i++)
                try { files[i].Delete(); } catch (Exception ex) { Logger.Debug($"TextureDiskCache: eviction failed to delete '{files[i].Name}'.", ex); }
        }
        catch (Exception ex)
        {
            Logger.Debug("TextureDiskCache: eviction pass failed.", ex);
        }
    }
}
