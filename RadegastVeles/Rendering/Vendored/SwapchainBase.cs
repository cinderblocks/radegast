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

// Vendored from Avalonia's own samples/GpuInterop reference code (MIT licensed,
// https://github.com/AvaloniaUI/Avalonia) -- explicitly "should not be a public API yet" per
// Avalonia's own source comment, hence vendoring rather than a package reference. Handles the
// double-buffering bookkeeping any composition-backed swapchain needs: find/reuse a pending
// image whose last present completed, or make a new one; track in-flight images so DisposeAsync
// can wait for outstanding presents. Not Veles-specific -- see VkInteropSwapchain.cs for the
// Veles-specific subclass.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Rendering.Composition;

namespace Radegast.Veles.Rendering;

internal abstract class SwapchainBase<TImage> : IAsyncDisposable where TImage : class, ISwapchainImage
{
    protected ICompositionGpuInterop Interop { get; }
    protected CompositionDrawingSurface Target { get; }
    private readonly List<TImage> _pendingImages = new();

    protected SwapchainBase(ICompositionGpuInterop interop, CompositionDrawingSurface target)
    {
        Interop = interop;
        Target = target;
    }

    private static bool IsBroken(TImage image) => image.LastPresent?.IsFaulted == true;
    private static bool IsReady(TImage image) => image.LastPresent == null || image.LastPresent.Status == TaskStatus.RanToCompletion;

    private TImage? CleanupAndFindNextImage(PixelSize size)
    {
        TImage? firstFound = null;
        var foundMultiple = false;

        for (var c = _pendingImages.Count - 1; c > -1; c--)
        {
            var image = _pendingImages[c];
            var ready = IsReady(image);
            var matches = image.Size == size;
            if (IsBroken(image) || (!matches && ready))
            {
                image.DisposeAsync();
                _pendingImages.RemoveAt(c);
            }

            if (matches && ready)
            {
                if (firstFound == null)
                    firstFound = image;
                else
                    foundMultiple = true;
            }
        }

        // At least one image of the same size must already be in flight before reusing one --
        // otherwise the UI thread can lock up waiting on a present that hasn't been issued yet.
        return foundMultiple ? firstFound : null;
    }

    protected abstract TImage CreateImage(PixelSize size);

    protected IDisposable BeginDrawCore(PixelSize size, out TImage image)
    {
        var img = CleanupAndFindNextImage(size) ?? CreateImage(size);

        img.BeginDraw();
        _pendingImages.Remove(img);
        image = img;
        return Disposable.Create(() =>
        {
            img.Present();
            _pendingImages.Add(img);
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var img in _pendingImages)
            await img.DisposeAsync();
    }
}

internal interface ISwapchainImage : IAsyncDisposable
{
    PixelSize Size { get; }
    Task? LastPresent { get; }
    void BeginDraw();
    void Present();
}
