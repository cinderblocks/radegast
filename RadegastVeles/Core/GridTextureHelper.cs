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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;
using CoreJ2K;
using CoreJ2K.Configuration;
using LibreMetaverse;
using SkiaSharp;

namespace Radegast.Veles.Core;

/// <summary>
/// Downloads and decodes grid textures (J2K) to Avalonia Bitmaps.
/// Uses the GridClient asset pipeline. Results are cached.
/// </summary>
public static class GridTextureHelper
{
    // CoreJ2K.Util.SKBitmapImageCreator (needed for the J2kImage...As<SKBitmap>() calls in
    // this file) is registered by CoreJ2K.Skia's [ModuleInitializer], which Program.Main
    // forces to run at startup — see the comment there for why.

    private static readonly LruCache<UUID, Bitmap> Cache = new(256);
    private static readonly ConcurrentDictionary<UUID, byte> Pending = new();
    private static readonly ConcurrentDictionary<UUID, long> ProgressDecodeTime = new();

    // In-memory cache of fully-decoded SKBitmaps for the GL upload path.
    // A cache hit avoids re-running J2kImage.FromBytes (43 ms / texture) and instead
    // pays only an SKBitmap.Copy (1.6 ms) — a 27× reduction per repeated texture.
    // RAM is bounded by SkBitmapCacheCap: each 1024×1024 RGBA bitmap ≈ 4 MB, so the
    // default cap of 64 entries ≈ 256 MB worst case. (Previous defaults of 128 and
    // 512 reached 512 MB and 2 GB respectively with full-resolution textures.) Users
    // with plenty of RAM can raise this via SkBitmapCacheCap or the preferences UI.
    //
    // Eviction policy: least-recently used. Evicted SKBitmaps are disposed on the
    // ThreadPool after a 500 ms grace period — long enough for any concurrent
    // TryGetValue + Copy() call that obtained a reference just before eviction to
    // finish its synchronous Copy(), but short enough to promptly reclaim native
    // (unmanaged) SkiaSharp pixel memory that the CLR GC cannot otherwise observe.
    private static readonly LruCache<UUID, SKBitmap> SkBitmapCache = new(64,
        onEvicted: static (_, bmp) =>
        {
            Interlocked.Increment(ref _cacheEvictions);
            var b = bmp;
            ThreadPool.QueueUserWorkItem(static s =>
            {
                Thread.Sleep(500);
                try { ((SKBitmap)s!).Dispose(); } catch { }
            }, b);
        });
    private static int _skBitmapCacheCap = 64;

    // Deduplicates concurrent J2K decodes for the same texture UUID.
    // When N callers simultaneously miss SkBitmapCache for the same UUID, only the
    // first starts a real J2kImage.FromBytes decode; the rest await the same Task.
    // This turns N × 52 MB of LOH pressure into 1 × 52 MB per unique texture.
    private static readonly ConcurrentDictionary<UUID, Task<SKBitmap?>> _inflightDecodes = new();

    // Deduplicates concurrent LOD (non-full-resolution) decodes for the same (UUID, level) pair.
    // These results are NOT cached in SkBitmapCache so full-res callers always get full quality.
    private static readonly ConcurrentDictionary<(UUID, int), Task<SKBitmap?>> _inflightLodDecodes = new();

    // Shared HttpClient for HTTP byte-range texture requests.  A single instance is correct for
    // long-running processes — it reuses connections and avoids socket exhaustion.
    private static readonly HttpClient _http = new();

    // Approximate J2K byte budget per resolution level, tuned to SL's typical codestream layout
    // (resolution-progression order, 1-tile per image, ~5 DWT levels).  Requesting only the first
    // N bytes is enough for CoreJ2K to reconstruct the corresponding resolution tier.  The server
    // returns HTTP 206 Partial Content; if it ignores the Range header it returns 200 with the full
    // payload and we decode from that instead.
    private static int ResolutionLevelToByteTarget(int level) => level switch
    {
        0 =>    600, // ~32×32  (just the J2K headers + LL0 subband)
        1 =>  2_000, // ~64×64
        2 =>  7_500, // ~128×128
        3 => 25_000, // ~256×256
        4 => 60_000, // ~512×512
        _ => int.MaxValue,
    };

    // Atomic counters — incremented with Interlocked so they are safe to read from
    // any thread at any time.  Zero overhead on the hot path (single interlocked add).
    private static long _cacheMisses;
    private static long _cacheHits;
    private static long _cacheEvictions;

    /// <summary>
    /// Snapshot of decoded-bitmap cache activity since the last <see cref="ResetCacheStats"/> call.
    /// </summary>
    public record CacheStats(
        long Hits,
        long Misses,
        long Evictions,
        int  CurrentSize,
        int  Cap)
    {
        /// <summary>Hit rate as a fraction in [0, 1].  Returns 0 when no calls have been made.</summary>
        public double HitRate => (Hits + Misses) == 0 ? 0.0 : (double)Hits / (Hits + Misses);

        /// <summary>
        /// Recommended cap: smallest power-of-two ≥ the unique-UUID high-water mark observed,
        /// clamped to [8, 2048].  Use this as a starting point for <see cref="SkBitmapCacheCap"/>.
        /// </summary>
        public int RecommendedCap
        {
            get
            {
                int hwm = (int)(Hits + Misses > 0 ? Misses : CurrentSize);
                int v = Math.Max(8, hwm);
                // Round up to next power of two.
                v--;
                v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16;
                return Math.Min(v + 1, 2048);
            }
        }
    }

    /// <summary>Returns a point-in-time snapshot of cache hit/miss/eviction counts.</summary>
    public static CacheStats GetCacheStats() => new(
        Hits:        Interlocked.Read(ref _cacheHits),
        Misses:      Interlocked.Read(ref _cacheMisses),
        Evictions:   Interlocked.Read(ref _cacheEvictions),
        CurrentSize: SkBitmapCache.Count,
        Cap:         _skBitmapCacheCap);

    /// <summary>Number of J2K texture decodes currently in-progress. Zero-cost snapshot.</summary>
    public static int InflightDecodeCount => _inflightDecodes.Count;

    /// <summary>Resets hit/miss/eviction counters to zero (does not affect cached entries).</summary>
    public static void ResetCacheStats()
    {
        Interlocked.Exchange(ref _cacheHits,      0);
        Interlocked.Exchange(ref _cacheMisses,    0);
        Interlocked.Exchange(ref _cacheEvictions, 0);
    }

    /// <summary>
    /// Maximum number of decoded <see cref="SKBitmap"/> entries held in the in-memory
    /// GL-texture cache.  When the cap is exceeded the cache is trimmed to 80% of this
    /// value by evicting arbitrary entries.  Default is 512.
    /// </summary>
    public static int SkBitmapCacheCap
    {
        get => _skBitmapCacheCap;
        set
        {
            var next = Math.Max(8, value);
            _skBitmapCacheCap   = next;
            SkBitmapCache.Capacity = next;
        }
    }

    // Unbounded Avalonia-Bitmap cache used by the UI path (texture viewer, profiles, etc.).
    // Without a cap this grows indefinitely in long-running sessions.  We cap it at
    // AvBitmapCacheCap and evict down to 80 % on each fill — same strategy as SkBitmapCache.
    private static int _avBitmapCacheCap = 256;

    /// <summary>
    /// Maximum number of decoded Avalonia <see cref="Bitmap"/> entries held in the UI
    /// texture cache.  When exceeded the cache is trimmed to 80 % of this value.
    /// Default is 256.
    /// </summary>
    public static int AvBitmapCacheCap
    {
        get => _avBitmapCacheCap;
        set
        {
            var next = Math.Max(8, value);
            _avBitmapCacheCap = next;
            Cache.Capacity    = next;
        }
    }

    /// <summary>Removes all entries from the decoded SKBitmap GL-texture cache and disposes them.</summary>
    public static void ClearSkBitmapCache()
    {
        foreach (var kv in SkBitmapCache.DrainAll())
            kv.Value.Dispose();
    }

    // Limits the number of J2K decodes running concurrently on the ThreadPool.
    // Each cold decode allocates ~21.5 MB of managed working memory (CoreJ2K DWT buffers);
    // without a cap the GC is buried under hundreds of MB of pressure on region entry.
    // The default is tuned at startup by TuneDecodeGateForAvailableRam(); the fallback
    // of ProcessorCount / 2 is used only if the caller never invokes that method.
    private static SemaphoreSlim _decodeGate =
        new(Math.Max(1, Environment.ProcessorCount / 2),
            Math.Max(1, Environment.ProcessorCount / 2));
    private static int _maxConcurrentDecodes = Math.Max(1, Environment.ProcessorCount / 2);
    private static readonly object DecodeGateLock = new();

    // Private accessor so every call site in this file always reads the current gate,
    // even after TuneDecodeGateForAvailableRam() replaces it.
    private static SemaphoreSlim DecodeGate
    {
        get { lock (DecodeGateLock) { return _decodeGate; } }
    }

    /// <summary>
    /// Maximum number of J2K decodes that may run concurrently on the ThreadPool.
    /// Reducing this value lowers peak managed-heap pressure on cold scene loads at the
    /// cost of slightly higher total decode time.
    /// Call <see cref="TuneDecodeGateForAvailableRam"/> at startup to set this automatically.
    /// </summary>
    public static int MaxConcurrentDecodes
    {
        get => Volatile.Read(ref _maxConcurrentDecodes);
        set
        {
            var next = Math.Max(1, value);
            lock (DecodeGateLock)
            {
                if (next == _maxConcurrentDecodes) return;
                var old = _decodeGate;
                _decodeGate = new SemaphoreSlim(next, next);
                Volatile.Write(ref _maxConcurrentDecodes, next);
                // Drain the old semaphore so any thread currently blocked on it unblocks.
                // Release up to the old cap to flush all waiters; safe to over-release
                // because SemaphoreSlim clamps at its initialCount maximum.
                try { old.Release(next); } catch (SemaphoreFullException) { }
                old.Dispose();
            }
        }
    }

    /// <summary>Megabytes reserved for the application when auto-tuning the decode gate. Default 512.</summary>
    public const double DefaultDecodeReservedMb = 512.0;

    /// <summary>Expected peak managed-heap cost per concurrent J2K decode in megabytes. Default 21.5.</summary>
    public const double DefaultDecodePerDecodeMb = 21.5;

    /// <summary>
    /// Sets <see cref="MaxConcurrentDecodes"/> based on the amount of available managed
    /// memory reported by the GC.  Each cold J2K decode requires ≈21.5 MB of working
    /// memory (CoreJ2K DWT coefficient buffers).  The method reserves
    /// <paramref name="reservedMb"/> MB for the rest of the application and divides the
    /// remainder by the per-decode budget, then clamps the result to
    /// [1, <c>ProcessorCount</c>] so the CPU is never the bottleneck.
    /// </summary>
    /// <param name="reservedMb">
    /// Megabytes to reserve for the rest of the application.  Default is <see cref="DefaultDecodeReservedMb"/>.
    /// </param>
    /// <param name="perDecodeMb">
    /// Expected peak managed-heap cost per concurrent decode in megabytes.
    /// Default is <see cref="DefaultDecodePerDecodeMb"/>, measured by memory profiling CoreJ2K.
    /// </param>
    public static void TuneDecodeGateForAvailableRam(
        double reservedMb = DefaultDecodeReservedMb,
        double perDecodeMb = DefaultDecodePerDecodeMb)
    {
        var info = GC.GetGCMemoryInfo();
        // TotalAvailableMemoryBytes is the GC-visible memory limit (respects container
        // limits and the process working set), reported in bytes.
        var availableMb = info.TotalAvailableMemoryBytes / (1024.0 * 1024.0);
        var budget      = Math.Max(0.0, availableMb - reservedMb);
        var fromRam     = (int)Math.Floor(budget / perDecodeMb);
        // Never starve the CPU: cap at ProcessorCount so no CPU core sits idle.
        var tuned = Math.Clamp(fromRam, 1, Environment.ProcessorCount);
        MaxConcurrentDecodes = tuned;
    }

    /// <summary>Megabytes reserved for the app, GPU driver, and OS when sizing the texture cache. Default 1536.</summary>
    public const double DefaultCacheReservedMb = 1536.0;

    /// <summary>Approximate managed cost of one cached decoded texture in megabytes (a 1024² RGBA bitmap). Default 4.</summary>
    public const double DefaultBytesPerCachedTextureMb = 4.0;

    /// <summary>Lower bound for the auto-sized decoded-texture cache cap.</summary>
    public const int MinRecommendedCacheCap = 256;

    /// <summary>Upper bound for the auto-sized decoded-texture cache cap.</summary>
    public const int MaxRecommendedCacheCap = 1024;

    /// <summary>
    /// Recommends a value for <see cref="SkBitmapCacheCap"/> scaled to the memory available
    /// to the process, instead of a flat default.  A small cap on a busy region thrashes —
    /// evicted textures must be re-decoded (~43 ms each through the gated decoder) — while a
    /// large flat cap (512 × 4 MB ≈ 2 GB) is unsafe on low-memory machines.  This reserves
    /// <paramref name="reservedMb"/> MB for the rest of the process, divides the remainder by
    /// the per-texture budget, and clamps to
    /// [<see cref="MinRecommendedCacheCap"/>, <see cref="MaxRecommendedCacheCap"/>].
    /// </summary>
    /// <param name="reservedMb">Megabytes to reserve for the rest of the process. Default <see cref="DefaultCacheReservedMb"/>.</param>
    /// <param name="perTextureMb">Managed cost per cached texture in megabytes. Default <see cref="DefaultBytesPerCachedTextureMb"/>.</param>
    public static int RecommendSkBitmapCacheCap(
        double reservedMb   = DefaultCacheReservedMb,
        double perTextureMb = DefaultBytesPerCachedTextureMb)
    {
        var info        = GC.GetGCMemoryInfo();
        var availableMb = info.TotalAvailableMemoryBytes / (1024.0 * 1024.0);
        var budget      = Math.Max(0.0, availableMb - reservedMb);
        var fromRam     = (int)Math.Floor(budget / Math.Max(0.1, perTextureMb));
        return Math.Clamp(fromRam, MinRecommendedCacheCap, MaxRecommendedCacheCap);
    }

    // Low-resolution decode config for progressive previews.
    // ResolutionLevel = 0 drops all DWT levels → smallest/fastest decode (~1/64 size for a 6-level wavelet).
    // The low-frequency subbands arrive first in the SL packet ordering so this is robust on
    // truncated codestreams and ~10× faster than full resolution.
    // ResolutionLevel = -1 means "highest available" (full resolution) in CoreJ2K.
    private static readonly J2KDecoderConfiguration PreviewDecoderCfg = new() { ResolutionLevel = 0 };
    private static readonly J2KDecoderConfiguration FullDecoderCfg    = new() { ResolutionLevel = -1 };

    /// <summary>
    /// Download a grid texture by UUID and deliver an Avalonia Bitmap to the callback.
    /// The callback is always invoked on the UI thread.
    /// </summary>
    public static void Download(GridClient client, UUID textureId, Action<Bitmap?> onComplete)
    {
        if (textureId == UUID.Zero)
        {
            Dispatcher.UIThread.Post(() => onComplete(null));
            return;
        }

        if (Cache.TryGetValue(textureId, out var cached))
        {
            Dispatcher.UIThread.Post(() => onComplete(cached));
            return;
        }

        // Check the persistent on-disk cache before hitting the asset server.
        {
            var diskJ2k = TextureDiskCache.TryGet(textureId);
            if (diskJ2k != null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        // Two-pass: cheap low-res preview first so the UI shows something
                        // immediately, then replace with the full-resolution bitmap.
                        using (var previewRaw = J2kImage.FromBytes(diskJ2k, PreviewDecoderCfg).As<SKBitmap>())
                        {
                            var previewBmp = previewRaw != null ? SkBitmapToAvaloniaBitmap(previewRaw) : null;
                            if (previewBmp != null)
                                Dispatcher.UIThread.Post(() => onComplete(previewBmp));
                        }

                        using var raw = J2kImage.FromBytes(diskJ2k, FullDecoderCfg).As<SKBitmap>();
                        if (raw == null) return;
                        var bitmap = SkBitmapToAvaloniaBitmap(raw);
                        if (bitmap == null) return;
                        Cache.AddOrUpdate(textureId, bitmap);
                        Dispatcher.UIThread.Post(() => onComplete(bitmap));
                    }
                    catch
                    {
                        TextureDiskCache.Evict(textureId);
                    }
                });
                return;
            }
        }

        if (!Pending.TryAdd(textureId, 0))
            return;

        _ = Task.Run(async () =>
        {
            var asset = await client.Assets.RequestImageAsync(textureId, ImageType.Normal);
            ProgressDecodeTime.TryRemove(textureId, out _);
            if (asset?.AssetData == null)
            {
                Pending.TryRemove(textureId, out _);
                Dispatcher.UIThread.Post(() => onComplete(null));
                return;
            }
            var data = asset.AssetData;
            Bitmap? bitmap = null;
            try
            {
                TextureDiskCache.PutAsync(textureId, data);
                using (var previewRaw = J2kImage.FromBytes(data, PreviewDecoderCfg).As<SKBitmap>())
                {
                    var previewBmp = previewRaw != null ? SkBitmapToAvaloniaBitmap(previewRaw) : null;
                    if (previewBmp != null)
                        Dispatcher.UIThread.Post(() => onComplete(previewBmp));
                }
                using var skBitmap = J2kImage.FromBytes(data, FullDecoderCfg).As<SKBitmap>();
                if (skBitmap != null)
                {
                    bitmap = SkBitmapToAvaloniaBitmap(skBitmap);
                    if (bitmap != null)
                        Cache.AddOrUpdate(textureId, bitmap);
                }
            }
            catch
            {
                TextureDiskCache.Evict(textureId);
            }
            finally
            {
                Pending.TryRemove(textureId, out _);
            }
            Dispatcher.UIThread.Post(() => onComplete(bitmap));
        });
    }

    /// Converts an <see cref="SKBitmap"/> to an Avalonia <see cref="Bitmap"/> by copying
    /// the raw pixel buffer directly, bypassing PNG encode/decode.
    private static Bitmap? SkBitmapToAvaloniaBitmap(SKBitmap skBmp)
    {
        // Avalonia's ToPixelFormat() only handles a subset of SKColorType values.
        // Unsupported types (e.g. Rgb888x from opaque J2K textures) must be
        // converted to a known-supported format first.
        SKBitmap? converted = null;
        if (skBmp.ColorType is not SKColorType.Rgba8888
                           and not SKColorType.Bgra8888
                           and not SKColorType.Gray8)
        {
            converted = new SKBitmap(skBmp.Width, skBmp.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            skBmp.CopyTo(converted, SKColorType.Rgba8888);
            skBmp = converted;
        }

        try
        {
            var ptr = skBmp.GetPixels();
            if (ptr == IntPtr.Zero) return null;
            return new Bitmap(
                skBmp.ColorType.ToPixelFormat(),
                skBmp.AlphaType.ToAlphaFormat(),
                ptr,
                new PixelSize(skBmp.Width, skBmp.Height),
                new Vector(96, 96),
                skBmp.RowBytes);
        }
        finally
        {
            converted?.Dispose();
        }
    }

    /// <summary>
    /// Decodes <paramref name="j2kBytes"/> to a full-resolution <see cref="SKBitmap"/>,
    /// deduplicating concurrent calls for the same <paramref name="textureId"/>.
    /// <para>
    /// If another caller is already decoding the same UUID, this method awaits that
    /// in-flight task and returns an independent <see cref="SKBitmap.Copy"/> of the
    /// result — so N concurrent callers for the same texture run exactly one
    /// <c>J2kImage.FromBytes</c> decode instead of N, reducing LOH pressure by N-fold.
    /// </para>
    /// The optional low-res <paramref name="progress"/> preview is only emitted by the
    /// winning caller (the one that starts the actual decode); late-joiners skip it
    /// since the full-res result arrives moments later anyway.
    /// </summary>
    private static Task<SKBitmap?> DecodeWithDeduplication(
        UUID textureId, byte[] j2kBytes, IProgress<SKBitmap>? progress, CancellationToken ct)
    {
        // Fast path: already cached — return a copy without touching _inflightDecodes.
        if (SkBitmapCache.TryGetValue(textureId, out var cached))
        {
            Interlocked.Increment(ref _cacheHits);
            return Task.FromResult<SKBitmap?>(cached.Copy(cached.ColorType));
        }

        // Try to register as the winner (first caller) for this UUID.
        TaskCompletionSource<SKBitmap?>? winnerTcs = null;
        Task<SKBitmap?> sharedTask;

        // Loop to handle the race where two threads both see a miss and try to insert.
        while (true)
        {
            if (_inflightDecodes.TryGetValue(textureId, out sharedTask!))
            {
                // Another caller already started decoding — join it.
                break;
            }
            winnerTcs = new TaskCompletionSource<SKBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_inflightDecodes.TryAdd(textureId, winnerTcs.Task))
            {
                sharedTask = winnerTcs.Task;
                break;
            }
            // TryAdd lost the race — loop back and pick up the winner's task.
        }

        if (winnerTcs == null)
        {
            // Late-joiner: await the winner's result then copy.
            return sharedTask.ContinueWith(t =>
            {
                var src = t.Result;
                // Guard against a disposed bitmap: the winner's caller may have disposed
                // its copy before this continuation runs, which would cause an
                // ExecutionEngineException in native SkiaSharp code via a null handle.
                if (src == null || src.Handle == IntPtr.Zero)
                    return null;
                return src.Copy(src.ColorType);
            }, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        // Winner: run the actual decode.
        //
        // Scheduled with CancellationToken.None — NOT `ct` — so the lambda body always runs.
        // If we passed a `ct` that was already cancelled, Task.Run would hand back a cancelled
        // task and never execute the body: winnerTcs would stay uncompleted and the
        // _inflightDecodes entry would be orphaned forever. Every later request for this UUID
        // joins that dead task (see TryGetValue above) and hangs permanently — the whole
        // texture pipeline appears to deadlock on one stuck texture. Cancellation is instead
        // observed cooperatively via DecodeGate.WaitAsync(ct) inside the body, where the
        // catch/finally guarantee winnerTcs completes and the inflight entry is removed.
        return Task.Run(async () =>
        {
            try
            {
                await DecodeGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (progress != null)
                    {
                        using var previewRaw = J2kImage.FromBytes(j2kBytes, PreviewDecoderCfg).As<SKBitmap>();
                        if (previewRaw != null)
                        {
                            var previewBmp = previewRaw.Copy(previewRaw.ColorType);
                            if (previewBmp != null) progress.Report(previewBmp);
                        }
                    }

                    // Store the decoded bitmap directly in the cache (no second copy).
                    // raw is NOT wrapped in using — ownership transfers to SkBitmapCache.
                    var raw = J2kImage.FromBytes(j2kBytes, FullDecoderCfg).As<SKBitmap>();
                    if (raw != null)
                    {
                        SkBitmapCache.AddOrUpdate(textureId, raw);
                    }
                    // Signal late-joiners with the cached bitmap directly — no extra Copy.
                    // This eliminates one full-resolution SKBitmap allocation per cold
                    // decode (~4 MB for a 1024² texture), which on a teleport burst of
                    // ~100 unique textures saves ~400 MB of transient managed memory.
                    winnerTcs.TrySetResult(raw);
                    // Return a separate copy to the winner's own caller (which owns / disposes it).
                    return raw != null && raw.Handle != IntPtr.Zero ? raw.Copy(raw.ColorType) : null;
                }
                finally
                {
                    // Released only when WaitAsync above succeeded (we hold a permit here).
                    DecodeGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Build cancelled (object moved, LOD change, 30 s timeout) before/while waiting
                // on the decode gate. The J2K bytes are still valid, so do NOT evict the disk
                // cache — just unblock any late-joiners with null rather than leaving them hung.
                winnerTcs.TrySetResult(null);
                return null;
            }
            catch
            {
                // Genuine decode failure (corrupt/truncated codestream): drop the bad bytes.
                TextureDiskCache.Evict(textureId);
                winnerTcs.TrySetResult(null);
                return null;
            }
            finally
            {
                // Always runs — guarantees the UUID is never left poisoned in _inflightDecodes.
                _inflightDecodes.TryRemove(textureId, out _);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Download and decode a grid texture, returning a raw <see cref="SKBitmap"/> on a
    /// background thread.  Returns <c>null</c> if the texture cannot be fetched or decoded.
    /// Suitable for GL upload paths that need the raw pixel data, not an Avalonia Bitmap.
    /// </summary>
    /// <param name="client">The grid client used to request the texture asset.</param>
    /// <param name="textureId">UUID of the texture to download.</param>
    /// <param name="progress">Optional progress callback for partial/preview bitmaps. Ignored when <paramref name="resolutionLevel"/> != -1.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="priority">
    /// Asset pipeline priority passed to <c>RequestImage</c>.
    /// Higher values are fetched before lower ones.
    /// Default (<c>101300f</c>) matches the SL viewer's normal texture priority.
    /// Use a higher value (e.g. <c>200000f</c>) for attachment textures that should
    /// appear before background scene objects.
    /// </param>
    /// <param name="resolutionLevel">
    /// J2K decode resolution level. -1 (default) = full resolution, cached in <see cref="SkBitmapCache"/>.
    /// 0 = lowest (preview), 1–4 = intermediate LOD levels. Non-full results are not cached so the
    /// caller always receives a quality appropriate for its distance, without polluting the full-res cache.
    /// </param>
    public static Task<SKBitmap?> DownloadSkBitmapAsync(
        GridClient client, UUID textureId, IProgress<SKBitmap>? progress = null, CancellationToken ct = default,
        float priority = 101300f, int resolutionLevel = -1)
    {
        if (textureId == UUID.Zero)
            return Task.FromResult<SKBitmap?>(null);

        // LOD path: caller wants a reduced-resolution version.
        // Does not use SkBitmapCache for storage so full-res callers remain unaffected.
        if (resolutionLevel != -1)
            return DownloadSkBitmapLodAsync(client, textureId, resolutionLevel, ct, priority);

        // Fast path: return a copy of the already-decoded bitmap — avoids a 43 ms J2K
        // decode and pays only the ~1.6 ms SKBitmap.Copy cost per cache hit.
        if (SkBitmapCache.TryGetValue(textureId, out var cachedBmp))
        {
            Interlocked.Increment(ref _cacheHits);
            return Task.FromResult<SKBitmap?>(cachedBmp.Copy(cachedBmp.ColorType));
        }
        Interlocked.Increment(ref _cacheMisses);

        var tcs = new TaskCompletionSource<SKBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Check the persistent on-disk cache before hitting the asset server.
        var diskJ2k = TextureDiskCache.TryGet(textureId);
        if (diskJ2k != null)
        {
            return DecodeWithDeduplication(textureId, diskJ2k, progress, ct);
        }

        var reg = ct.Register(() => tcs.TrySetResult(null));

        _ = Task.Run(async () =>
        {
            var asset = await client.Assets.RequestImageAsync(textureId, ImageType.Normal, ct).ConfigureAwait(false);
            ProgressDecodeTime.TryRemove(textureId, out _);
            reg.Dispose();

            if (asset?.AssetData == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var data = asset.AssetData;
            TextureDiskCache.PutAsync(textureId, data);

            if (progress != null)
            {
                await DecodeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    using var previewRaw = J2kImage.FromBytes(data, PreviewDecoderCfg).As<SKBitmap>();
                    if (previewRaw != null)
                    {
                        var previewBmp = previewRaw.Copy(previewRaw.ColorType);
                        if (previewBmp != null) progress.Report(previewBmp);
                    }
                }
                catch { }
                finally { DecodeGate.Release(); }
            }

            var bmp = await DecodeWithDeduplication(textureId, data, progress: null, ct: CancellationToken.None)
                .ConfigureAwait(false);
            if (!tcs.TrySetResult(bmp))
                bmp?.Dispose();
        });

        return tcs.Task;
    }

    // Approximate pixel divisor for each J2K resolution level, assuming 5 DWT levels
    // (common for SL 512² and 1024² textures).  Level 0 → 1/32, 1 → 1/16, 2 → 1/8, etc.
    // Used only for the fast downscale path (cache-hit → resize) where exact sizing matters
    // less than avoiding a full J2K re-decode.
    private static int ResolutionLevelToDivisor(int level) => level switch
    {
        0 => 32, 1 => 16, 2 => 8, 3 => 4, 4 => 2, _ => 1
    };

    /// <summary>
    /// Downloads and decodes a texture at a reduced J2K resolution level.
    /// Results are NOT stored in <see cref="SkBitmapCache"/> so the full-quality cache entry
    /// is unaffected.  Concurrent calls for the same <c>(UUID, level)</c> are deduplicated
    /// via <c>_inflightLodDecodes</c>.
    /// </summary>
    private static Task<SKBitmap?> DownloadSkBitmapLodAsync(
        GridClient client, UUID textureId, int resolutionLevel, CancellationToken ct, float priority)
    {
        // If full-res is already cached, downscale cheaply — one pixel-copy instead of a J2K decode.
        if (SkBitmapCache.TryGetValue(textureId, out var fullRes))
        {
            Interlocked.Increment(ref _cacheHits);
            int divisor = ResolutionLevelToDivisor(resolutionLevel);
            int w = Math.Max(1, fullRes.Width  / divisor);
            int h = Math.Max(1, fullRes.Height / divisor);
            var thumb = new SKBitmap(w, h, fullRes.ColorType, fullRes.AlphaType);
            fullRes.ScalePixels(thumb, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            return Task.FromResult<SKBitmap?>(thumb);
        }

        var lodKey = (textureId, resolutionLevel);

        // Dedup: if another task is already decoding at this level, join it.
        TaskCompletionSource<SKBitmap?>? winnerTcs = null;
        Task<SKBitmap?> sharedTask;
        while (true)
        {
            if (_inflightLodDecodes.TryGetValue(lodKey, out sharedTask!)) break;
            winnerTcs = new TaskCompletionSource<SKBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_inflightLodDecodes.TryAdd(lodKey, winnerTcs.Task)) { sharedTask = winnerTcs.Task; break; }
        }

        if (winnerTcs == null)
        {
            // Late-joiner: copy the winner's result.
            return sharedTask.ContinueWith(t =>
            {
                var src = t.Result;
                if (src == null || src.Handle == IntPtr.Zero) return null;
                return src.Copy(src.ColorType);
            }, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        // Winner: fetch bytes and decode at the requested level.
        return Task.Run(async () =>
        {
            try
            {
                byte[]? j2kBytes = TextureDiskCache.TryGet(textureId);

                if (j2kBytes == null)
                {
                    // Attempt a byte-range HTTP GET against the ViewerAsset/GetTexture capability.
                    // The SL CDN supports RFC 7233 Range headers; only the first N bytes are needed
                    // for lower wavelet levels, saving network bandwidth proportional to the LOD gap.
                    // Partial bytes are NOT written to TextureDiskCache — that cache is full-file only
                    // to avoid corrupting later full-quality builds.
                    var capUri = client.Network.CurrentSim?.Caps?.GetTextureCapURI();
                    if (capUri != null)
                    {
                        int byteTarget = ResolutionLevelToByteTarget(resolutionLevel);
                        try
                        {
                            using var req = new HttpRequestMessage(HttpMethod.Get,
                                $"{capUri}?texture_id={textureId}");
                            req.Headers.Range = new RangeHeaderValue(0, byteTarget - 1);

                            using var resp = await _http.SendAsync(req,
                                HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

                            if (resp.IsSuccessStatusCode) // 200 full or 206 partial
                            {
                                j2kBytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

                                // If the server ignored the Range header and returned the full file,
                                // seed the disk cache so a future full-res build avoids a re-download.
                                if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent
                                    && j2kBytes.Length > byteTarget * 4)
                                {
                                    TextureDiskCache.PutAsync(textureId, j2kBytes);
                                }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { /* Range request failed — fall through to full download below. */ }
                    }

                    if (j2kBytes == null)
                    {
                        // Cap unavailable or Range request failed: full download seeds the disk cache.
                        var asset = await client.Assets.RequestImageAsync(textureId, ImageType.Normal, ct)
                                                       .ConfigureAwait(false);
                        if (asset?.AssetData == null) { winnerTcs.TrySetResult(null); return null; }
                        j2kBytes = asset.AssetData;
                        TextureDiskCache.PutAsync(textureId, j2kBytes);
                    }
                }

                // Re-check: a concurrent full-res build may have finished while we fetched.
                if (SkBitmapCache.TryGetValue(textureId, out var raceHit))
                {
                    int divisor = ResolutionLevelToDivisor(resolutionLevel);
                    int w = Math.Max(1, raceHit.Width  / divisor);
                    int h = Math.Max(1, raceHit.Height / divisor);
                    var thumb = new SKBitmap(w, h, raceHit.ColorType, raceHit.AlphaType);
                    raceHit.ScalePixels(thumb, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    var thumbCopy = thumb.Copy(thumb.ColorType);
                    winnerTcs.TrySetResult(thumb); // shared with late-joiners; they copy
                    return thumbCopy;              // winner's own copy
                }

                // Decode at the requested LOD level — pays only for the wavelet levels needed.
                await DecodeGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var cfg = new J2KDecoderConfiguration { ResolutionLevel = resolutionLevel };
                    var raw = J2kImage.FromBytes(j2kBytes, cfg).As<SKBitmap>();
                    if (raw == null) { winnerTcs.TrySetResult(null); return null; }
                    var shared = raw.Copy(raw.ColorType); // late-joiners copy from this
                    winnerTcs.TrySetResult(shared);
                    using (raw) return raw.Copy(raw.ColorType); // winner's own copy
                }
                finally { DecodeGate.Release(); }
            }
            catch (OperationCanceledException)
            {
                winnerTcs.TrySetResult(null);
                return null;
            }
            catch
            {
                winnerTcs.TrySetResult(null);
                return null;
            }
            finally
            {
                _inflightLodDecodes.TryRemove(lodKey, out _);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Download a server-side baked texture as a raw <see cref="SKBitmap"/>.
    /// Requires the avatar's agent UUID and bake layer name (e.g. "head", "upper", "lower").
    /// Uses <c>RequestServerBakedImage</c> which is required for SSB textures — calling
    /// <see cref="DownloadSkBitmapAsync"/> for these UUIDs silently hangs.
    /// <para>
    /// When <paramref name="progress"/> is supplied the method reports a fast
    /// quarter-resolution preview bitmap (via <see cref="J2KDecoderConfiguration.ResolutionLevel"/>
    /// = 2) as soon as the J2K payload arrives, before the full-resolution decode completes.
    /// This lets the caller display a blurry-but-correct bake layer immediately and refine
    /// it once the task resolves.
    /// </para>
    /// </summary>
    public static Task<SKBitmap?> DownloadServerBakedSkBitmapAsync(
        GridClient client, UUID avatarId, UUID textureId, string bakeName,
        CancellationToken ct = default,
        IProgress<SKBitmap>? progress = null)
    {
        if (textureId == UUID.Zero)
            return Task.FromResult<SKBitmap?>(null);

        // SSB textures must NOT be cached in TextureDiskCache — their UUIDs are reused
        // across appearance changes with different pixel content.  The LibreMetaverse
        // asset cache (Client.Assets.Cache) handles SSB caching in RequestServerBakedImage.
        // Evict any stale entry a prior (buggy) session may have written.
        TextureDiskCache.Evict(textureId);

        var tcs = new TaskCompletionSource<SKBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reg = ct.Register(() => tcs.TrySetResult(null));

        _ = Task.Run(async () =>
        {
            LibreMetaverse.Assets.AssetTexture? asset;
            try
            {
                asset = await client.Assets.RequestServerBakedImageAsync(avatarId, textureId, bakeName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // RequestServerBakedImageAsync's own Cache.HasAsset/TryGetCachedAssetBytes
                // call is outside its try/catch, so a corrupt LMV asset-cache entry throws
                // here uncaught instead of returning null. Without this catch the exception
                // faulted the Task.Run and tcs was never completed, so the caller silently
                // hung until its own timeout — with nothing in the log to explain why.
                reg.Dispose();
                Logger.Warn($"GridTextureHelper: RequestServerBakedImageAsync threw for bake {bakeName} ({textureId}).", ex, client);
                tcs.TrySetResult(null);
                return;
            }
            reg.Dispose();

            if (asset?.AssetData == null)
            {
                Logger.Debug($"GridTextureHelper: no asset data returned for bake {bakeName} ({textureId}) " +
                             "(RequestServerBakedImageAsync returned null — see its own Warn log for the HTTP-level reason, if any).", client);
                tcs.TrySetResult(null);
                return;
            }

            var data = asset.AssetData;
            try
            {
                if (progress != null)
                {
                    try
                    {
                        using var rawPrev = J2kImage.FromBytes(data, PreviewDecoderCfg).As<SKBitmap>();
                        if (rawPrev != null)
                        {
                            var prev = rawPrev.Copy(rawPrev.ColorType);
                            if (prev != null) progress.Report(prev);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"GridTextureHelper: preview decode failed for bake {bakeName} ({textureId}), {data.Length} byte(s).", ex, client);
                    }
                }
                using var raw = J2kImage.FromBytes(data, FullDecoderCfg).As<SKBitmap>();
                if (raw == null)
                    Logger.Warn($"GridTextureHelper: J2K decode returned null for bake {bakeName} ({textureId}), {data.Length} byte(s) — likely a stale/corrupt entry in the LibreMetaverse asset cache.", client);
                var bmp = raw != null ? raw.Copy(raw.ColorType) : null;
                tcs.TrySetResult(bmp);
            }
            catch (Exception ex)
            {
                Logger.Warn($"GridTextureHelper: J2K decode threw for bake {bakeName} ({textureId}), {data.Length} byte(s).", ex, client);
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }
}
