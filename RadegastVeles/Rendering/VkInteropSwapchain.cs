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

// Ported from Avalonia's own samples/GpuInterop/VulkanDemo's VulkanSwapchain.cs (MIT licensed,
// https://github.com/AvaloniaUI/Avalonia). Bridges Veles's own rendering
// (a VkInteropImage Veles renders into) to Avalonia's compositor via SwapchainBase<T>'s
// double-buffering (Vendored/SwapchainBase.cs) and ICompositionGpuInterop's import/present
// calls. BeginDraw()/Present() bracket a single frame: BeginDraw transitions the image to
// ColorAttachmentOptimal (ready for Veles to render into via VkRenderPass), Present transitions
// it to TransferSrcOptimal (matching the render-pass layout-ownership decision in
// VkRenderPass.cs -- the render pass itself ends in ColorAttachmentOptimal, THIS class owns
// the follow-up transition) then hands it to the compositor.
//
// Three submission paths depending on how the image was actually created (VkInteropImage's
// handleType branch, itself driven by what ICompositionGpuInterop.SupportedImageHandleTypes
// advertises): Win32 keyed-mutex (Mode B, D3D11-backed -- what Veles's chosen integration mode
// actually exercises), semaphore pair (Mode A, native Vulkan opaque handle), or a bare
// first-frame submit with no semaphore (used once, before any semaphore has anything to wait
// on). No macOS timeline-semaphore path -- dropped for the same EXT-package reason as
// VkInteropImage's IOSurface path.

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkInteropSwapchain : SwapchainBase<VkInteropSwapchainImage>
{
    private readonly VkContext _vk;
    private readonly VkFrameReapRing _reapRing;

    public VkInteropSwapchain(VkContext vk, ICompositionGpuInterop interop, CompositionDrawingSurface target,
        VkFrameReapRing reapRing)
        : base(interop, target)
    {
        _vk = vk;
        _reapRing = reapRing;
    }

    protected override VkInteropSwapchainImage CreateImage(PixelSize size) => new(_vk, size, Interop, Target, _reapRing);

    // The reap here is where the PREVIOUS frame's VkInteropSwapchainImage.Present() submit --
    // never waited on within the frame it belongs to, by design (see that method's own doc
    // comment) -- actually gets reaped. If the compositor/present pipeline is backed up, that
    // deferred wait lands here, on the NEXT frame, not in RenderFrame's own main-pass SubmitWait.
    public double LastFreeUsedCommandBuffersMs { get; private set; }
    public double LastBeginDrawCoreMs { get; private set; }
    private readonly System.Diagnostics.Stopwatch _diagStopwatch = new();

    /// <summary>Begins a frame: returns an <see cref="IDisposable"/> whose <c>Dispose()</c>
    /// presents the frame -- matches <see cref="SwapchainBase{TImage}.BeginDrawCore"/>'s
    /// "record inside a using block" shape. <paramref name="image"/> is the render target to
    /// pass to <see cref="VkRenderPass"/>/framebuffer creation for this frame.</summary>
    public IDisposable BeginDraw(PixelSize size, out VkInteropImage image)
    {
        // BeginDrawCore runs FIRST, FreeUsed() second. BeginDrawCore -> img.BeginDraw() issues
        // THIS frame's own keyed-mutex acquire submit and advances the Veles-render/
        // compositor-consume handshake. At FramesInFlight=1 the reverse order would never
        // matter, since the slot FreeUsed reaps always holds exactly last frame's already-
        // fully-cycled buffers -- but under frame-in-flight overlap (N>1) that slot could hold
        // buffers from an earlier frame, including that frame's own present-path submission, and
        // reaping (waiting on) it before this frame's BeginDrawCore call risks blocking the one
        // call that lets the handshake advance, with nothing else able to make progress on this
        // thread. Running BeginDrawCore first removes that ordering hazard regardless of N.
        _diagStopwatch.Restart();
        var rv = BeginDrawCore(size, out var swapchainImage);
        LastBeginDrawCoreMs = _diagStopwatch.Elapsed.TotalMilliseconds;

        _diagStopwatch.Restart();
        _reapRing.FreeUsed();
        LastFreeUsedCommandBuffersMs = _diagStopwatch.Elapsed.TotalMilliseconds;

        image = swapchainImage.Image;
        return rv;
    }
}

internal sealed class VkInteropSwapchainImage : ISwapchainImage
{
    private readonly VkContext _vk;
    private readonly ICompositionGpuInterop _interop;
    private readonly CompositionDrawingSurface _target;
    private readonly VkInteropImage _image;
    private readonly VkSemaphorePair? _semaphorePair;
    private readonly VkFrameReapRing _reapRing;
    private ICompositionImportedGpuSemaphore? _availableSemaphore, _renderCompletedSemaphore;
    private ICompositionImportedGpuImage? _importedImage;
    private Task? _lastPresent;
    private bool _initial = true;

    public VkInteropImage Image => _image;
    public PixelSize Size { get; }
    public Task? LastPresent => _lastPresent;

    public VkInteropSwapchainImage(VkContext vk, PixelSize size, ICompositionGpuInterop interop,
        CompositionDrawingSurface target, VkFrameReapRing reapRing)
    {
        _vk = vk;
        _interop = interop;
        _target = target;
        _reapRing = reapRing;
        Size = size;
        _image = new VkInteropImage(vk, Format.R8G8B8A8Unorm, size, true, interop.SupportedImageHandleTypes);
        if (!_image.IsDirectXBacked)
            _semaphorePair = new VkSemaphorePair(vk, true);
    }

    public void BeginDraw()
    {
        var buffer = _vk.Pool.CreateCommandBuffer("VkInteropSwapchainImage.BeginDraw");
        buffer.BeginRecording();

        _image.TransitionLayout(buffer.InternalHandle, ImageLayout.Undefined, AccessFlags.None,
            ImageLayout.ColorAttachmentOptimal, AccessFlags.ColorAttachmentReadBit);

        if (_image.IsDirectXBacked)
            buffer.Submit(null, null, null, null, new VkCommandBufferPool.VkCommandBuffer.KeyedMutexSubmitInfo
            {
                AcquireKey = 0,
                DeviceMemory = _image.DeviceMemory
            });
        else if (_initial)
        {
            _initial = false;
            buffer.Submit();
        }
        else
            buffer.Submit(new[] { _semaphorePair!.ImageAvailableSemaphore }, new[] { PipelineStageFlags.AllGraphicsBit });
        _reapRing.MarkUsed(buffer);
    }

    public void Present()
    {
        var buffer = _vk.Pool.CreateCommandBuffer("VkInteropSwapchainImage.Present");
        buffer.BeginRecording();
        // Matches VkRenderPass.CreateMainScenePass's layout-ownership note: the render pass
        // ends the image in ColorAttachmentOptimal, and THIS transition (not the render pass)
        // is what moves it to TransferSrcOptimal for the blit the caller's render loop does
        // before calling Present().
        _image.TransitionLayout(buffer.InternalHandle, ImageLayout.TransferSrcOptimal, AccessFlags.TransferWriteBit);

        if (_image.IsDirectXBacked)
        {
            buffer.Submit(null, null, null, null, new VkCommandBufferPool.VkCommandBuffer.KeyedMutexSubmitInfo
            {
                DeviceMemory = _image.DeviceMemory,
                ReleaseKey = 1
            });
        }
        else
            buffer.Submit(null, null, new[] { _semaphorePair!.RenderFinishedSemaphore });
        _reapRing.MarkUsed(buffer);

        if (!_image.IsDirectXBacked)
        {
            _availableSemaphore ??= _interop.ImportSemaphore(_semaphorePair!.Export(false));
            _renderCompletedSemaphore ??= _interop.ImportSemaphore(_semaphorePair!.Export(true));
        }

        _importedImage ??= _interop.ImportImage(_image.Export(), new PlatformGraphicsExternalImageProperties
        {
            Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
            Width = Size.Width,
            Height = Size.Height,
            MemorySize = _image.MemorySize
        });

        _lastPresent = _image.IsDirectXBacked
            ? _target.UpdateWithKeyedMutexAsync(_importedImage, 1, 0)
            : _target.UpdateWithSemaphoresAsync(_importedImage, _renderCompletedSemaphore!, _availableSemaphore!);
    }

    public async ValueTask DisposeAsync()
    {
        if (LastPresent != null)
            await LastPresent;
        if (_importedImage != null)
            await _importedImage.DisposeAsync();
        if (_availableSemaphore != null)
            await _availableSemaphore.DisposeAsync();
        if (_renderCompletedSemaphore != null)
            await _renderCompletedSemaphore.DisposeAsync();
        _semaphorePair?.Dispose();
        _image.Dispose();
    }
}
