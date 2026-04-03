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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CoreJ2K;
using OpenMetaverse;
using SkiaSharp;

namespace Radegast.Veles.Core;

/// <summary>
/// Downloads and decodes grid textures (J2K) to Avalonia Bitmaps.
/// Uses the GridClient asset pipeline. Results are cached.
/// </summary>
public static class GridTextureHelper
{
    private static readonly ConcurrentDictionary<UUID, Bitmap?> Cache = new();
    private static readonly ConcurrentDictionary<UUID, byte> Pending = new();

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

        if (!Pending.TryAdd(textureId, 0))
            return;

        client.Assets.RequestImage(textureId, (state, asset) =>
        {
            // Non-terminal states — more callbacks will follow, do nothing yet
            if (state is TextureRequestState.Pending
                      or TextureRequestState.Started
                      or TextureRequestState.Progress)
                return;

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
                    using var skBitmap = J2kImage.FromBytes(data).As<SKBitmap>();
                    using var skData = skBitmap.Encode(SKEncodedImageFormat.Png, 90);
                    using var stream = new MemoryStream(skData.ToArray());
                    bitmap = new Bitmap(stream);
                    Cache[textureId] = bitmap;
                }
                catch
                {
                    // Decode failed
                }
                finally
                {
                    Pending.TryRemove(textureId, out _);
                }

                Dispatcher.UIThread.Post(() => onComplete(bitmap));
            });
        });
    }

    /// <summary>
    /// Download and decode a grid texture, returning a raw <see cref="SKBitmap"/> on a
    /// background thread.  Returns <c>null</c> if the texture cannot be fetched or decoded.
    /// Suitable for GL upload paths that need the raw pixel data, not an Avalonia Bitmap.
    /// </summary>
    public static Task<SKBitmap?> DownloadSkBitmapAsync(
        GridClient client, UUID textureId, CancellationToken ct = default)
    {
        if (textureId == UUID.Zero)
            return Task.FromResult<SKBitmap?>(null);

        var tcs = new TaskCompletionSource<SKBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Keep the registration alive until the asset callback fires a terminal state.
        // Using `using var` here would dispose it immediately when the method returns,
        // before the async work is done — which would silently break cancellation.
        var reg = ct.Register(() => tcs.TrySetResult(null));

        client.Assets.RequestImage(textureId, (state, asset) =>
        {
            if (state is TextureRequestState.Pending
                      or TextureRequestState.Started
                      or TextureRequestState.Progress)
                return;

            // Terminal state — clean up the cancellation registration.
            reg.Dispose();

            if (state != TextureRequestState.Finished || asset?.AssetData == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var data = asset.AssetData;
            Task.Run(() =>
            {
                try
                {
                    // J2kImage may own the underlying pixel memory; copy immediately
                    // so the returned SKBitmap has independent lifetime.
                    using var raw = J2kImage.FromBytes(data).As<SKBitmap>();
                    var bmp = raw != null ? raw.Copy(raw.ColorType) : null;
                    tcs.TrySetResult(bmp);
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            }, ct);
        });

        return tcs.Task;
    }

    /// <summary>
    /// Download a server-side baked texture as a raw <see cref="SKBitmap"/>.
    /// Requires the avatar's agent UUID and bake layer name (e.g. "head", "upper", "lower").
    /// Uses <c>RequestServerBakedImage</c> which is required for SSB textures — calling
    /// <see cref="DownloadSkBitmapAsync"/> for these UUIDs silently hangs.
    /// </summary>
    public static Task<SKBitmap?> DownloadServerBakedSkBitmapAsync(
        GridClient client, UUID avatarId, UUID textureId, string bakeName,
        CancellationToken ct = default)
    {
        if (textureId == UUID.Zero)
            return Task.FromResult<SKBitmap?>(null);

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
            Task.Run(() =>
            {
                try
                {
                    using var raw = J2kImage.FromBytes(data).As<SKBitmap>();
                    var bmp = raw != null ? raw.Copy(raw.ColorType) : null;
                    tcs.TrySetResult(bmp);
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            }, ct);
        });

        return tcs.Task;
    }
}
