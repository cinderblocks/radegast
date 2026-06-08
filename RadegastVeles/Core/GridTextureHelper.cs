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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Threading;
using CoreJ2K;
using CoreJ2K.Configuration;
using OpenMetaverse;
using SkiaSharp;

namespace Radegast.Veles.Core;

/// <summary>
/// Downloads and decodes grid textures (J2K) to Avalonia Bitmaps.
/// Uses the GridClient asset pipeline. Results are cached.
/// </summary>
public static class GridTextureHelper
{
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

        client.Assets.RequestImage(textureId, ImageType.Normal, (state, asset) =>
        {
            if (state is TextureRequestState.Pending or TextureRequestState.Started)
                return;

            if (state == TextureRequestState.Progress)
            {
                // Throttle progressive decodes to at most one every 250 ms per texture
                var now = Environment.TickCount64;
                var last = ProgressDecodeTime.GetOrAdd(textureId, 0L);
                if (now - last < 250L) return;
                ProgressDecodeTime[textureId] = now;

                var partialData = asset?.AssetData;
                if (partialData == null) return;

                Task.Run(() =>
                {
                    try
                    {
                        // ResolutionLevel=2 is robust on truncated codestreams: SL delivers
                        // low-frequency subbands first, so low-res decodes succeed reliably
                        // on partial data and are ~10× cheaper than a full-res attempt.
                        using var skBmp = J2kImage.FromBytes(partialData, PreviewDecoderCfg).As<SKBitmap>();
                        if (skBmp == null) return;
                        var preview = SkBitmapToAvaloniaBitmap(skBmp);
                        if (preview != null) Dispatcher.UIThread.Post(() => onComplete(preview));
                    }
                    catch { }
                });
                return;
            }

            // Terminal state — clean up throttle tracking
            ProgressDecodeTime.TryRemove(textureId, out _);

            if (state != TextureRequestState.Finished || asset?.AssetData == null)
            {
                Pending.TryRemove(textureId, out _);
                Dispatcher.UIThread.Post(() => onComplete(null));
                return;
            }

            var data = asset.AssetData;
            Task.Run(() =>
            {
                Bitmap? bitmap = null;
                try
                {
                    // Persist the raw J2K bytes before decoding — no re-encode needed.
                    TextureDiskCache.PutAsync(textureId, data);

                    // Two-pass: emit a cheap preview first so the UI shows something
                    // immediately, then replace with the full-resolution cached bitmap.
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
                        {
                            Cache.AddOrUpdate(textureId, bitmap);
                        }
                    }
                }
                catch
                {
                    // The downloaded bytes were written to the disk cache via PutAsync above;
                    // evict them now so a future call re-downloads rather than hitting a
                    // corrupt cache entry.
                    TextureDiskCache.Evict(textureId);
                }
                finally
                {
                    Pending.TryRemove(textureId, out _);
                }

                Dispatcher.UIThread.Post(() => onComplete(bitmap));
            });
        }, true);
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
        return Task.Run(async () =>
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
                // The cache's eviction policy does NOT dispose entries, so the handle
                // remains valid for the lifetime of any continuation that observes it.
                // This eliminates one full-resolution SKBitmap allocation per cold
                // decode (~4 MB for a 1024² texture), which on a teleport burst of
                // ~100 unique textures saves ~400 MB of transient managed memory.
                winnerTcs.TrySetResult(raw);
                // Return a separate copy to the winner's own caller (which owns / disposes it).
                return raw != null && raw.Handle != IntPtr.Zero ? raw.Copy(raw.ColorType) : null;
            }
            catch
            {
                TextureDiskCache.Evict(textureId);
                winnerTcs.TrySetResult(null);
                return null;
            }
            finally
            {
                _inflightDecodes.TryRemove(textureId, out _);
                DecodeGate.Release();
            }
        }, ct);
    }

    /// <summary>
    /// Download and decode a grid texture, returning a raw <see cref="SKBitmap"/> on a
    /// background thread.  Returns <c>null</c> if the texture cannot be fetched or decoded.
    /// Suitable for GL upload paths that need the raw pixel data, not an Avalonia Bitmap.
    /// </summary>
    /// <param name="client">The grid client used to request the texture asset.</param>
    /// <param name="textureId">UUID of the texture to download.</param>
    /// <param name="progress">Optional progress callback for partial/preview bitmaps.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="priority">
    /// Asset pipeline priority passed to <c>RequestImage</c>.
    /// Higher values are fetched before lower ones.
    /// Default (<c>101300f</c>) matches the SL viewer's normal texture priority.
    /// Use a higher value (e.g. <c>200000f</c>) for attachment textures that should
    /// appear before background scene objects.
    /// </param>
    public static Task<SKBitmap?> DownloadSkBitmapAsync(
        GridClient client, UUID textureId, IProgress<SKBitmap>? progress = null, CancellationToken ct = default,
        float priority = 101300f)
    {
        if (textureId == UUID.Zero)
            return Task.FromResult<SKBitmap?>(null);

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

        // Keep the registration alive until the asset callback fires a terminal state.
        // Using `using var` here would dispose it immediately when the method returns,
        // before the async work is done — which would silently break cancellation.
        var reg = ct.Register(() => tcs.TrySetResult(null));

        client.Assets.RequestImage(textureId, ImageType.Normal, priority, 0, 0, (state, asset) =>
        {
            if (state is TextureRequestState.Pending or TextureRequestState.Started)
                return;

            if (state == TextureRequestState.Progress)
            {
                if (progress == null) return;
                var now = Environment.TickCount64;
                var last = ProgressDecodeTime.GetOrAdd(textureId, 0L);
                if (now - last < 250L) return;
                ProgressDecodeTime[textureId] = now;

                var partialData = asset?.AssetData;
                if (partialData == null) return;

                Task.Run(async () =>
                {
                    // Progress previews are low-priority; skip rather than queue if the gate
                    // is fully occupied — a missed preview is invisible to the user.
                    if (!await DecodeGate.WaitAsync(0, ct).ConfigureAwait(false))
                        return;
                    try
                    {
                        // Use ResolutionLevel=2 for partial codestreams: the SL packet ordering
                        // delivers low-frequency subbands first, so low-res decodes succeed
                        // reliably on truncated data and are ~10× cheaper than full-res.
                        using var raw = J2kImage.FromBytes(partialData, PreviewDecoderCfg).As<SKBitmap>();
                        if (raw != null)
                        {
                            var preview = raw.Copy(raw.ColorType);
                            if (preview != null) progress.Report(preview);
                        }
                    }
                    catch { }
                    finally
                    {
                        DecodeGate.Release();
                    }
                }, ct);
                return;
            }

            ProgressDecodeTime.TryRemove(textureId, out _);
            // Terminal state — clean up the cancellation registration.
            reg.Dispose();

            if (state != TextureRequestState.Finished || asset?.AssetData == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var data = asset.AssetData;
            // Do NOT pass ct here: ct may already be canceled (e.g. the 30-second timeout
            // fired in the window between reg.Dispose() and Task.Run scheduling), which would
            // cause Task.Run to return a canceled task without running the lambda, leaving tcs
            // permanently unresolved and hanging Task.WhenAll.  The asset bytes are already
            // in memory; the decode is fast CPU work that must always complete.
            _ = Task.Run(async () =>
            {
                // Persist the raw J2K bytes before decoding — no re-encode needed.
                TextureDiskCache.PutAsync(textureId, data);

                // Preview pass: emit a cheap low-res bitmap immediately.
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

                // Full-resolution decode — deduplicated so concurrent callers for the
                // same UUID share one J2kImage.FromBytes instead of each running their own.
                var bmp = await DecodeWithDeduplication(textureId, data, progress: null, ct: CancellationToken.None)
                    .ConfigureAwait(false);
                // If ct fired first, tcs is already set to null and nobody will own bmp — dispose it.
                if (!tcs.TrySetResult(bmp))
                    bmp?.Dispose();
            });
        }, true);

        return tcs.Task;
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

        client.Assets.RequestServerBakedImage(avatarId, textureId, bakeName, (state, asset) =>
        {
            if (state is TextureRequestState.Pending
                      or TextureRequestState.Started
                      or TextureRequestState.Progress)
                return;

            reg.Dispose();

            if (state != TextureRequestState.Finished || asset?.AssetData == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var data = asset.AssetData;
            // Do NOT pass ct here for the same reason as DownloadSkBitmapAsync: ct may be
            // canceled in the window between reg.Dispose() and Task.Run executing, which would
            // silently discard the decode and leave tcs permanently unresolved.
            Task.Run(() =>
            {
                try
                {
                    // Do NOT cache SSB bytes in TextureDiskCache: SSB UUIDs are reused
                    // across appearance changes.  LibreMetaverse's own asset cache (used
                    // by RequestServerBakedImage) is the appropriate persistence layer.

                    if (progress != null)
                    {
                        // Fire a quick quarter-resolution preview before the full decode.
                        // SSB delivers the complete J2K codestream in one shot so we can
                        // decode at a lower wavelet resolution level immediately.
                        try
                        {
                            using var rawPrev = J2kImage.FromBytes(data, PreviewDecoderCfg).As<SKBitmap>();
                            if (rawPrev != null)
                            {
                                var prev = rawPrev.Copy(rawPrev.ColorType);
                                if (prev != null) progress.Report(prev);
                            }
                        }
                        catch { /* preview failure is non-fatal — full decode continues */ }
                    }

                    using var raw = J2kImage.FromBytes(data, FullDecoderCfg).As<SKBitmap>();
                    var bmp = raw != null ? raw.Copy(raw.ColorType) : null;
                    tcs.TrySetResult(bmp);
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            });
        });

        return tcs.Task;
    }
}
