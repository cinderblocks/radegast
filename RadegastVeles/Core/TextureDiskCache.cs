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
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using SkiaSharp;

namespace Radegast.Veles.Core;

/// <summary>
/// Persistent on-disk cache for grid textures, in two independent tiers:
/// <list type="bullet">
/// <item><description>
/// <b>J2K tier</b> (<c>.j2k</c> files) — raw JPEG 2000 asset bytes, written as-is from the
/// asset server with no re-encoding. A cache hit yields identical bytes to the original
/// download; the caller still pays a full <c>J2kImage.FromBytes</c> decode. This tier also
/// serves the byte-range LOD path (<see cref="GridTextureHelper"/>'s
/// <c>DownloadSkBitmapLodAsync</c>), which needs partial raw codestream bytes that a
/// decoded-pixel cache cannot provide.
/// </description></item>
/// <item><description>
/// <b>Pixel tier</b> (<c>.v1.ktx2</c> files, see <see cref="Ktx2Codec"/>) — fully decoded,
/// uncompressed RGBA8 pixels for the full-resolution image. Populated only after a real J2K
/// decode succeeds (<see cref="PutPixelsAsync"/>). A hit here (<see cref="TryGetPixels"/>)
/// skips the CoreJ2K decode entirely -- the CPU cost implicated in prior scene-viewer
/// decode-saturation freezes -- at the cost of more disk space per entry than the J2K tier,
/// so it is capped by byte budget (<see cref="MaxPixelCacheBytes"/>) rather than file count.
/// The version tag in the filename means a future format change (e.g. adding mip levels or
/// block compression) reads as a clean miss against old entries instead of misinterpreted
/// bytes.
/// </description></item>
/// <item><description>
/// <b>Compressed pixel tier</b> (<c>.v2.ktx2</c> files) — BC3 block-compressed, full mip-chain
/// pixels, written by <see cref="Ktx2Codec.EncodeCompressedBc3"/> and consumed only by the
/// Vulkan scene-object texture path (<c>VkViewportControl.ApplySubmissionPatchIfReady</c>) when
/// the GPU advertises BC3 support. A DIFFERENT version tag from the uncompressed tier is
/// deliberate, not incidental: this tier stores pixels already flipped into Vulkan upload
/// orientation (BC3 blocks can't be flipped after compression), whereas the <c>.v1.ktx2</c>
/// tier stores pixels in CoreJ2K's native top-down orientation. The two must never be read as
/// if they were the other -- see <see cref="Ktx2Codec.EncodeCompressedBc3"/>'s doc comment.
/// Shares the same byte budget and eviction pass as the uncompressed tier (both glob
/// <c>*.ktx2</c>); nothing distinguishes them for sizing/eviction purposes, only for reading.
/// </description></item>
/// </list>
/// Both tiers live under the same configurable directory (default:
/// <c>%AppData%\RadegastVeles\texturecache\</c>).
///
/// Server-side baked textures must NOT be cached in either tier because their UUIDs
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

    private static long _maxPixelCacheBytes = 3L * 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum total size, in bytes, of all <c>.ktx2</c> pixel-cache files kept on disk
    /// before the oldest (by last-access time) are evicted. Unlike <see cref="MaxCachedFiles"/>
    /// (a per-file count, appropriate for small compressed J2K files), the pixel tier stores
    /// uncompressed RGBA8, so entries vary widely in size with texture resolution -- a byte
    /// budget is the only cap that behaves sensibly. The eviction pass removes files, oldest
    /// first, until total size is back under 90% of this value. Default is 3 GiB.
    /// </summary>
    public static long MaxPixelCacheBytes
    {
        get => _maxPixelCacheBytes;
        set => _maxPixelCacheBytes = Math.Max(64L * 1024 * 1024, value);
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

    // Version-tagged so a future pixel-format change (mips, block compression, etc.) reads as
    // a clean cache miss against entries written by an older build, instead of misinterpreted
    // bytes -- see the class doc comment.
    private const string PixelCacheVersion = "v1";

    private static string PixelFilePath(UUID textureId) =>
        Path.Combine(_cacheDir, textureId + "." + PixelCacheVersion + ".ktx2");

    // Deliberately a different version tag from PixelCacheVersion, not the next number in the
    // same sequence -- see the class doc comment's "Compressed pixel tier" entry for why these
    // two store pixels in different orientations and must never alias.
    private const string CompressedPixelCacheVersion = "v2";

    private static string CompressedPixelFilePath(UUID textureId) =>
        Path.Combine(_cacheDir, textureId + "." + CompressedPixelCacheVersion + ".ktx2");

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

            // File.ReadAllBytes opens with the default FileShare.Read, which does NOT
            // include Delete/rename sharing. If a concurrent PutAsync call for the same
            // texture (two callers racing a cache miss for the same UUID, or a re-fetch
            // after eviction) is mid File.Move(tmp, path, overwrite: true) while this read
            // is open, Windows denies the move with UnauthorizedAccessException. Opening
            // with FileShare.Delete lets that concurrent replace proceed — this read still
            // completes against the original file's data.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[stream.Length];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) break; // shorter than reported length; return what we got
                offset += read;
            }
            return buffer;
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
    /// Try to load and decode fully-decoded pixels for <paramref name="textureId"/> from the
    /// pixel-cache tier. Returns <c>null</c> if not cached, the cache is disabled, the entry
    /// was written by an incompatible codec version, or any I/O/decode error occurs. On a hit
    /// this skips the CoreJ2K decode entirely -- callers own the returned <see cref="SKBitmap"/>
    /// and must dispose it (or hand ownership to a cache that will).
    /// </summary>
    // Diagnostic-only counters (2026-08-17, investigating a "cached textures don't render"
    // report): TryDecode/TryDecodeCompressed's own catch blocks return null silently on ANY
    // structural mismatch, with no logging at all -- indistinguishable from an ordinary "not
    // cached yet" miss from the caller's side. If an encode/decode round-trip bug exists (e.g.
    // BcEncoder's actual per-level byte counts not matching the ceil(w/4)*ceil(h/4)*16 formula
    // TryDecodeCompressed validates against), every entry silently "misses" forever and falls
    // back to the uncompressed/live-decode path -- these counters distinguish that from a
    // genuine file-not-found miss, which the existing code has no way to tell apart today.
    private static long _pixelFileFound, _pixelDecodeNull, _compressedFileFound, _compressedDecodeNull;
    private static long _lastCacheDiagLogTicks;

    private static void LogCacheDiagIfDue()
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastCacheDiagLogTicks) < 1000) return;
        Interlocked.Exchange(ref _lastCacheDiagLogTicks, now);
        Logger.Debug("[TextureDiskCache] pixel tier: fileFound=" + Interlocked.Read(ref _pixelFileFound) +
            " decodeNull=" + Interlocked.Read(ref _pixelDecodeNull) +
            " -- compressed tier: fileFound=" + Interlocked.Read(ref _compressedFileFound) +
            " decodeNull=" + Interlocked.Read(ref _compressedDecodeNull) +
            " (decodeNull>0 means the file existed but TryDecode[Compressed] rejected it -- a" +
            " silent round-trip mismatch, not an ordinary cache miss)");
    }

    public static SKBitmap? TryGetPixels(UUID textureId)
    {
        if (!_enabled) return null;
        try
        {
            var path = PixelFilePath(textureId);
            if (!File.Exists(path)) return null;
            Interlocked.Increment(ref _pixelFileFound);

            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);

            // See TryGet's own comment on FileShare.Delete: lets a concurrent PutPixelsAsync
            // overwrite-move proceed instead of throwing UnauthorizedAccessException.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[stream.Length];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) break;
                offset += read;
            }
            var result = Ktx2Codec.TryDecode(buffer);
            if (result == null) Interlocked.Increment(ref _pixelDecodeNull);
            LogCacheDiagIfDue();
            return result;
        }
        catch (Exception ex)
        {
            Logger.Debug($"TextureDiskCache: failed to read cached pixel texture {textureId}.", ex);
            return null;
        }
    }

    /// <summary>
    /// Returns true when decoded pixels for <paramref name="textureId"/> are already on disk
    /// in the pixel-cache tier, without reading/decoding them.
    /// </summary>
    public static bool ContainsPixels(UUID textureId)
    {
        if (!_enabled) return false;
        try { return File.Exists(PixelFilePath(textureId)); }
        catch { return false; }
    }

    /// <summary>
    /// Try to load and decode a BC3 compressed, full mip-chain texture for
    /// <paramref name="textureId"/> from the compressed pixel-cache tier. Returns <c>null</c>
    /// if not cached, the cache is disabled, the entry was written by an incompatible codec
    /// version, or any I/O/decode error occurs. Callers must only use the result for GPU
    /// upload -- see the class doc comment's "Compressed pixel tier" entry for the
    /// orientation contract this tier stores pixels under.
    /// </summary>
    public static CompressedKtx2? TryGetCompressedPixels(UUID textureId)
    {
        if (!_enabled) return null;
        try
        {
            var path = CompressedPixelFilePath(textureId);
            if (!File.Exists(path)) return null;
            Interlocked.Increment(ref _compressedFileFound);

            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);

            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[stream.Length];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) break;
                offset += read;
            }
            var result = Ktx2Codec.TryDecodeCompressed(buffer);
            if (result == null) Interlocked.Increment(ref _compressedDecodeNull);
            LogCacheDiagIfDue();
            return result;
        }
        catch (Exception ex)
        {
            Logger.Debug($"TextureDiskCache: failed to read cached compressed texture {textureId}.", ex);
            return null;
        }
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
    /// Asynchronously write pre-encoded KTX2 <paramref name="ktx2Data"/> bytes to the
    /// pixel-cache tier. Callers must encode on their own thread first (e.g. via
    /// <see cref="Ktx2Codec.Encode"/>) -- this method only backgrounds the file write, not the
    /// encode, so it never holds a reference to a live <see cref="SKBitmap"/> whose backing
    /// pixel buffer could be reused/pooled before a deferred read of it completes.
    /// Silently skips the write if the cache is disabled, the data is empty, or the file
    /// already exists. Most callers discard the returned task (fire-and-forget).
    /// </summary>
    public static Task PutPixelsAsync(UUID textureId, byte[] ktx2Data)
    {
        if (!_enabled) return Task.CompletedTask;
        if (ktx2Data == null || ktx2Data.Length == 0) return Task.CompletedTask;

        return Task.Run(() =>
        {
            string? tmp = null;
            try
            {
                EvictPixelCacheIfNeeded();

                var path = PixelFilePath(textureId);
                if (File.Exists(path)) return; // already cached by a concurrent call

                tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, ktx2Data);
                File.Move(tmp, path, overwrite: true);
                tmp = null;
            }
            catch (Exception ex)
            {
                Logger.Debug($"TextureDiskCache: failed to cache pixel texture {textureId}.", ex);
            }
            finally
            {
                if (tmp != null)
                {
                    try { File.Delete(tmp); } catch { /* best effort */ }
                }
            }
        });
    }

    /// <summary>
    /// Asynchronously write pre-encoded BC3 KTX2 <paramref name="ktx2Data"/> bytes to the
    /// compressed pixel-cache tier. Same encode-on-caller-thread, background-the-write-only
    /// contract as <see cref="PutPixelsAsync"/> -- pass the result of
    /// <see cref="Ktx2Codec.EncodeCompressedBc3"/>, not a bitmap to encode later.
    /// Silently skips the write if the cache is disabled, the data is empty, or the file
    /// already exists.
    /// </summary>
    public static Task PutCompressedPixelsAsync(UUID textureId, byte[] ktx2Data)
    {
        if (!_enabled) return Task.CompletedTask;
        if (ktx2Data == null || ktx2Data.Length == 0) return Task.CompletedTask;

        return Task.Run(() =>
        {
            string? tmp = null;
            try
            {
                EvictPixelCacheIfNeeded();

                var path = CompressedPixelFilePath(textureId);
                if (File.Exists(path)) return; // already cached by a concurrent call

                tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, ktx2Data);
                File.Move(tmp, path, overwrite: true);
                tmp = null;
            }
            catch (Exception ex)
            {
                Logger.Debug($"TextureDiskCache: failed to cache compressed texture {textureId}.", ex);
            }
            finally
            {
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
    /// Returns the total size of all cached <c>.ktx2</c> pixel-tier files in bytes.
    /// Returns 0 if the cache directory does not exist or cannot be read.
    /// </summary>
    public static long GetPixelCacheSizeBytes()
    {
        try
        {
            var di = new DirectoryInfo(_cacheDir);
            if (!di.Exists) return 0L;
            long total = 0;
            foreach (var f in di.EnumerateFiles("*.ktx2"))
                total += f.Length;
            return total;
        }
        catch (Exception ex)
        {
            Logger.Debug("TextureDiskCache: failed to compute pixel cache size.", ex);
            return 0L;
        }
    }

    /// <summary>
    /// Returns the number of cached <c>.ktx2</c> pixel-tier files on disk (any codec version).
    /// </summary>
    public static int GetPixelCacheFileCount()
    {
        try
        {
            var di = new DirectoryInfo(_cacheDir);
            if (!di.Exists) return 0;
            int count = 0;
            foreach (var _ in di.EnumerateFiles("*.ktx2")) count++;
            return count;
        }
        catch (Exception ex)
        {
            Logger.Debug("TextureDiskCache: failed to count pixel cache files.", ex);
            return 0;
        }
    }

    /// <summary>
    /// Deletes the cached <c>.j2k</c> AND <c>.ktx2</c> entries for <paramref name="textureId"/>
    /// from disk. Used to evict a single corrupt/truncated/stale entry so the next request
    /// re-downloads (or re-decodes) valid data. Both tiers must be cleared together -- e.g.
    /// server-baked textures whose UUID gets reused with different pixel content must not
    /// leave a stale pixel-tier entry behind after this is called. Silently ignores I/O errors.
    /// </summary>
    public static void Evict(UUID textureId)
    {
        try { File.Delete(FilePath(textureId)); }
        catch (Exception ex)
        {
            Logger.Debug($"TextureDiskCache: failed to evict cached texture {textureId}.", ex);
        }
        try { File.Delete(PixelFilePath(textureId)); }
        catch (Exception ex)
        {
            Logger.Debug($"TextureDiskCache: failed to evict cached pixel texture {textureId}.", ex);
        }
        try { File.Delete(CompressedPixelFilePath(textureId)); }
        catch (Exception ex)
        {
            Logger.Debug($"TextureDiskCache: failed to evict cached compressed pixel texture {textureId}.", ex);
        }
    }

    /// <summary>
    /// Deletes all <c>.j2k</c>, all <c>.ktx2</c> (any codec version), and any orphaned legacy
    /// <c>.png</c> files in the cache directory synchronously.
    /// </summary>
    public static void Clear()
    {
        try
        {
            var di = new DirectoryInfo(_cacheDir);
            if (!di.Exists) return;
            foreach (var f in di.EnumerateFiles("*.j2k"))
                try { f.Delete(); } catch (Exception ex) { Logger.Debug($"TextureDiskCache: failed to delete '{f.Name}'.", ex); }
            foreach (var f in di.EnumerateFiles("*.ktx2"))
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

    // A full directory listing + sort is too expensive to run on every single PutAsync
    // call (region entry / teleport can trigger thousands of these back to back). Only
    // actually check capacity once every EvictionCheckInterval puts; the bounded slack
    // this introduces (at most that many files over _maxCachedFiles) is negligible next
    // to the default 8192-file budget.
    private const int EvictionCheckInterval = 64;
    private static int _putsSinceEvictionCheck;

    private static void EvictIfNeeded()
    {
        if (Interlocked.Increment(ref _putsSinceEvictionCheck) < EvictionCheckInterval) return;
        Interlocked.Exchange(ref _putsSinceEvictionCheck, 0);

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

    // Same rate-limiting rationale as EvictIfNeeded above, but tracked with its own counter
    // since J2K and pixel-tier writes happen at different rates (every asset download vs.
    // only after a real decode).
    private const int PixelEvictionCheckInterval = 32;
    private static int _pixelPutsSinceEvictionCheck;

    private static void EvictPixelCacheIfNeeded()
    {
        if (Interlocked.Increment(ref _pixelPutsSinceEvictionCheck) < PixelEvictionCheckInterval) return;
        Interlocked.Exchange(ref _pixelPutsSinceEvictionCheck, 0);

        try
        {
            var files = new DirectoryInfo(_cacheDir).GetFiles("*.ktx2");
            long total = 0;
            foreach (var f in files) total += f.Length;
            if (total < _maxPixelCacheBytes) return;

            // Delete oldest-first (by last-access time) until back under 90% of budget --
            // unlike the J2K tier's flat "10% of file count", entry sizes vary widely here
            // (RGBA8 scales with texture resolution), so a byte-based target is the only one
            // that reliably converges.
            Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            long target = (long)(_maxPixelCacheBytes * 0.9);
            int i = 0;
            while (total > target && i < files.Length)
            {
                try
                {
                    total -= files[i].Length;
                    files[i].Delete();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"TextureDiskCache: pixel-cache eviction failed to delete '{files[i].Name}'.", ex);
                }
                i++;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("TextureDiskCache: pixel-cache eviction pass failed.", ex);
        }
    }
}
