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
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using SkiaSharp;
using Silk.NET.Vulkan;
using Radegast.Veles.Core;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;

namespace Radegast.Veles.Rendering;

public partial class VkViewportControl
{
    /// <summary>
    /// The tonemap/bloom/god-ray composite chain -- see <see cref="VkTonemapPipeline"/>'s own
    /// doc comment for the bloom stage overview, and <see cref="VkGodRayBlurPipeline"/>'s for the
    /// 2 god-ray stages this method now also records (before bloom, gated on
    /// <paramref name="sunIntensity"/>). Recorded into the SAME command buffer as the main
    /// pass, immediately after it ends (<c>vk.Api.CmdEndRenderPass</c> for <see cref="_renderPass"/>
    /// already ran by the time this is called) and BEFORE underwater, which now runs after this
    /// method instead of directly after the main pass -- see <c>RenderFrame</c>'s own call site
    /// comment for why. <paramref name="image"/> is the real swapchain interop image; every
    /// earlier stage in this method targets the persistent, full/half-res offscreen targets
    /// (<see cref="_hdrColorView"/>/<see cref="_bloomPingView"/>/<see cref="_bloomPongView"/>/
    /// <see cref="_godRayMaskView"/>/<see cref="_godRayBlurView"/>) instead, only the FINAL
    /// (tonemap) stage writes into <paramref name="image"/>. <paramref name="sunScreenUv"/>/
    /// <paramref name="sunIntensity"/> are computed once per frame in <c>RenderFrame</c>, see its
    /// own comment on that computation.
    /// </summary>
    private unsafe void RenderTonemapChain(VkContext vk, CommandBuffer cmd, PixelSize pixelSize, VkInteropImage image,
        Vector2 sunScreenUv, float sunIntensity)
    {
        // Stages 1-3 (bloom) are VkGraphicsTier.High only -- see _graphicsTier's own field
        // comment. On Medium, TonemapSet's bloom binding was already pointed at a shared black
        // placeholder once, at init (VkPostProcessDescriptorSet.BindNoBloomPlaceholder), so stage
        // 4 below is correct either way without needing to know which tier it's running under.
        if (_graphicsTier == VkGraphicsTier.High)
        {
            // Bloom's targets are half the panel's resolution -- the viewport/scissor RenderFrame
            // set for the main pass (full pixelSize) is still bound as dynamic state at this
            // point in the command buffer, so it must be narrowed here and restored below before
            // the tonemap stage, which writes at full resolution. Same "own fixed viewport,
            // restored before returning" convention RenderShadowPass/RenderWaterReflectionPass
            // already use for their own non-pixelSize offscreen targets.
            var bloomViewport = new Viewport { X = 0, Y = 0, Width = _bloomSize.Width, Height = _bloomSize.Height, MinDepth = 0, MaxDepth = 1 };
            vk.Api.CmdSetViewport(cmd, 0, 1, in bloomViewport);
            var bloomScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)_bloomSize.Width, (uint)_bloomSize.Height) };
            vk.Api.CmdSetScissor(cmd, 0, 1, &bloomScissor);

            // ── God rays (2 stages, before bloom) -- sunIntensity already folds in every gate
            // (tier/GodRaysEnabled/doSsao/horizon/screen-edge fade, see RenderFrame's own
            // computation), so a plain > 0 check here is the complete early-out: skip both draws
            // entirely on a frame where the sun's down, off-screen, or the feature's off, rather
            // than running them and relying on the tonemap composite alone to zero the result.
            // _godRaySize == _bloomSize always (both derived from the same halved pixelSize), so
            // the viewport/scissor just set above is already correct for these too.
            if (sunIntensity > 0f)
            {
                // ── Stage 0a: occlusion mask, full-res HDR + G-buffer depth -> half-res ──────
                var maskBegin = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _bloomOffscreenRenderPass,
                    Framebuffer = _godRayMaskFramebuffer,
                    RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)_godRaySize.Width, (uint)_godRaySize.Height)),
                    ClearValueCount = 0
                };
                vk.Api.CmdBeginRenderPass(cmd, in maskBegin, SubpassContents.Inline);
                vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _godRayMaskPipeline!.Pipeline);
                var maskSet = _postProcessDescSet!.GodRayMaskSet;
                vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _godRayMaskPipeline.Layout, 0, 1, &maskSet, 0, null);
                var maskSrcTexelSize = new Vector2(1.0f / pixelSize.Width, 1.0f / pixelSize.Height);
                vk.Api.CmdPushConstants(cmd, _godRayMaskPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &maskSrcTexelSize);
                vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
                vk.Api.CmdEndRenderPass(cmd);
            }

            // ── Stage 0b: radial blur toward the sun, mask -> half-res streak ───────────────
            // Also run ONCE with intensity 0 when the god-ray stages are skipped and nothing has
            // rendered into the target since it was created: godray_radial_blur.frag writes plain
            // zeros in that case, so the image tonemap.frag has bound is never undefined memory
            // (and is in ShaderReadOnlyOptimal, which the descriptor claims). Once defined, a
            // skipped frame leaves the previous streak in place; tonemap.frag doesn't sample it
            // at intensity 0.
            if (sunIntensity > 0f || !_godRayBlurDefined)
            {
                var blurBegin = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _bloomOffscreenRenderPass,
                    Framebuffer = _godRayBlurFramebuffer,
                    RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)_godRaySize.Width, (uint)_godRaySize.Height)),
                    ClearValueCount = 0
                };
                vk.Api.CmdBeginRenderPass(cmd, in blurBegin, SubpassContents.Inline);
                vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _godRayBlurPipeline!.Pipeline);
                var godRayBlurSet = _postProcessDescSet!.GodRayBlurSet;
                vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _godRayBlurPipeline.Layout, 0, 1, &godRayBlurSet, 0, null);
                Span<float> godRayPc = stackalloc float[4] { sunScreenUv.X, sunScreenUv.Y, MathF.Max(sunIntensity, 0f), 0.955f };
                fixed (float* p = godRayPc)
                    vk.Api.CmdPushConstants(cmd, _godRayBlurPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 16, p);
                vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
                vk.Api.CmdEndRenderPass(cmd);
                _godRayBlurDefined = true;
            }

            // ── Stage 1: bright-pass extract, full-res HDR -> half-res Ping ──────────────
            var extractBegin = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _bloomOffscreenRenderPass,
                Framebuffer = _bloomPingFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)_bloomSize.Width, (uint)_bloomSize.Height)),
                ClearValueCount = 0
            };
            vk.Api.CmdBeginRenderPass(cmd, in extractBegin, SubpassContents.Inline);
            vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _bloomExtractPipeline!.Pipeline);
            var extractSet = _postProcessDescSet!.ExtractSet;
            vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _bloomExtractPipeline.Layout, 0, 1, &extractSet, 0, null);
            var srcTexelSize = new Vector2(1.0f / pixelSize.Width, 1.0f / pixelSize.Height);
            vk.Api.CmdPushConstants(cmd, _bloomExtractPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &srcTexelSize);
            vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
            vk.Api.CmdEndRenderPass(cmd);

            // ── Stage 2: separable blur, horizontal, Ping -> Pong ─────────────────────────
            var blurBeginH = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _bloomOffscreenRenderPass,
                Framebuffer = _bloomPongFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)_bloomSize.Width, (uint)_bloomSize.Height)),
                ClearValueCount = 0
            };
            vk.Api.CmdBeginRenderPass(cmd, in blurBeginH, SubpassContents.Inline);
            vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _bloomBlurPipeline!.Pipeline);
            var blurHSet = _postProcessDescSet.BlurHSet;
            vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _bloomBlurPipeline.Layout, 0, 1, &blurHSet, 0, null);
            var dirH = new Vector2(1.0f / _bloomSize.Width, 0f);
            vk.Api.CmdPushConstants(cmd, _bloomBlurPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &dirH);
            vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
            vk.Api.CmdEndRenderPass(cmd);

            // ── Stage 3: separable blur, vertical, Pong -> Ping (final blurred bloom) ─────
            var blurBeginV = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _bloomOffscreenRenderPass,
                Framebuffer = _bloomPingFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)_bloomSize.Width, (uint)_bloomSize.Height)),
                ClearValueCount = 0
            };
            vk.Api.CmdBeginRenderPass(cmd, in blurBeginV, SubpassContents.Inline);
            vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _bloomBlurPipeline.Pipeline);
            var blurVSet = _postProcessDescSet.BlurVSet;
            vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _bloomBlurPipeline.Layout, 0, 1, &blurVSet, 0, null);
            var dirV = new Vector2(0f, 1.0f / _bloomSize.Height);
            vk.Api.CmdPushConstants(cmd, _bloomBlurPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &dirV);
            vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
            vk.Api.CmdEndRenderPass(cmd);

            // ── Stage 3b: wide level. Half-res Ping -> quarter-res WidePong (H) -> WidePing (V).
            // The tap step is one DESTINATION texel in both passes, so the H pass doubles as the
            // 2x downsample (bilinear taps two source texels apart).
            var wideViewport = new Viewport { X = 0, Y = 0, Width = _bloomWideSize.Width, Height = _bloomWideSize.Height, MinDepth = 0, MaxDepth = 1 };
            vk.Api.CmdSetViewport(cmd, 0, 1, in wideViewport);
            var wideExtent = new Extent2D((uint)_bloomWideSize.Width, (uint)_bloomWideSize.Height);
            var wideScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = wideExtent };
            vk.Api.CmdSetScissor(cmd, 0, 1, &wideScissor);

            var wideBeginH = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _bloomOffscreenRenderPass,
                Framebuffer = _bloomWidePongFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), wideExtent),
                ClearValueCount = 0
            };
            vk.Api.CmdBeginRenderPass(cmd, in wideBeginH, SubpassContents.Inline);
            vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _bloomBlurPipeline.Pipeline);
            var wideHSet = _postProcessDescSet.BlurWideHSet;
            vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _bloomBlurPipeline.Layout, 0, 1, &wideHSet, 0, null);
            var wideDirH = new Vector2(1.0f / _bloomWideSize.Width, 0f);
            vk.Api.CmdPushConstants(cmd, _bloomBlurPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &wideDirH);
            vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
            vk.Api.CmdEndRenderPass(cmd);

            var wideBeginV = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _bloomOffscreenRenderPass,
                Framebuffer = _bloomWidePingFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), wideExtent),
                ClearValueCount = 0
            };
            vk.Api.CmdBeginRenderPass(cmd, in wideBeginV, SubpassContents.Inline);
            var wideVSet = _postProcessDescSet.BlurWideVSet;
            vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _bloomBlurPipeline.Layout, 0, 1, &wideVSet, 0, null);
            var wideDirV = new Vector2(0f, 1.0f / _bloomWideSize.Height);
            vk.Api.CmdPushConstants(cmd, _bloomBlurPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &wideDirV);
            vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
            vk.Api.CmdEndRenderPass(cmd);
        }

        // ── Stage 4: tonemap composite, full-res HDR + blurred bloom -> the REAL swapchain image ──
        var tonemapViewport = new Viewport { X = 0, Y = 0, Width = pixelSize.Width, Height = pixelSize.Height, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in tonemapViewport);
        var tonemapScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &tonemapScissor);

        var tonemapFramebuffer = image.GetFramebuffer(_tonemapRenderPass);

        var tonemapBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _tonemapRenderPass,
            Framebuffer = tonemapFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 0
        };
        vk.Api.CmdBeginRenderPass(cmd, in tonemapBegin, SubpassContents.Inline);
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _tonemapPipeline!.Pipeline);
        var tonemapSet = _postProcessDescSet!.TonemapSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _tonemapPipeline.Layout, 0, 1, &tonemapSet, 0, null);
        // Redundant with the sunIntensity>0 gate around the god-ray stages above (a skipped frame
        // passes 0 here too, see tonemap.frag's own doc comment on this push constant) --
        // guarantees correctness independent of that early-out, not just an optimization mirror.
        float godRayIntensityPc = sunIntensity;
        vk.Api.CmdPushConstants(cmd, _tonemapPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 4, &godRayIntensityPc);
        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
        vk.Api.CmdEndRenderPass(cmd);
    }

    /// <summary>
    /// True when at least one view ray inside the frustum heads toward the plane z =
    /// <paramref name="waterHeight"/>. water.frag draws the plane out to the horizon with no far
    /// limit, so this tests ray direction only: the vertical component of a frustum ray is
    /// extremal at one of the four corner rays.
    /// </summary>
    private static Format ChooseHdrColorFormat(VkContext vk)
    {
        // B10G11R11UfloatPack32: 4 bytes/pixel, unsigned, no alpha (nothing reads the HDR
        // buffer's alpha). R16G16B16A16Sfloat is the 8-byte fallback when the device can't
        // render to or linearly filter the packed format, and can be forced with
        // VELES_VK_HDR_FORMAT=rgba16f to compare the two on the same scene.
        const FormatFeatureFlags needed = FormatFeatureFlags.ColorAttachmentBit
            | FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.SampledImageFilterLinearBit;
        vk.Api.GetPhysicalDeviceFormatProperties(vk.PhysicalDevice, Format.B10G11R11UfloatPack32, out var props);
        bool packedSupported = (props.OptimalTilingFeatures & needed) == needed;
        bool forceWide = string.Equals(Environment.GetEnvironmentVariable("VELES_VK_HDR_FORMAT"),
            "rgba16f", StringComparison.OrdinalIgnoreCase);

        var format = packedSupported && !forceWide ? Format.B10G11R11UfloatPack32 : Format.R16G16B16A16Sfloat;
        LibreMetaverse.Logger.Info(
            $"[VkViewportControl] HDR colour format: {format} (packed supported={packedSupported}, forced rgba16f={forceWide})");
        return format;
    }

    /// <summary>Lazily (re)creates <see cref="_hdrColorImage"/> at the panel's full pixel size --
    /// mirrors <see cref="EnsureDepthTarget"/>'s own pattern exactly (this is the OTHER
    /// attachment of the same main-pass framebuffer, so it needs the same per-size lifecycle).
    /// Rewrites the tonemap chain's HDR-input bindings on (re)creation, matching
    /// <see cref="EnsureGBufferTarget"/>'s own "rewrite descriptors only here, never per-frame"
    /// contract.</summary>
    private unsafe void EnsureHdrColorTarget(VkContext vk, PixelSize size)
    {
        if (_hdrColorSize == size && _hdrColorView.Handle != 0) return;
        DestroyHdrColorTarget(vk);

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _hdrColorFormat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            // TransferSrcBit on top of the usual ColorAttachmentBit|SampledBit: water refraction's
            // split main pass copies this image out mid-frame (see EnsureOpaqueSnapshotTarget) --
            // added unconditionally rather than only on the tiers that actually use it, since this
            // method itself is already Medium/High-only (see its call site in RenderFrame).
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in imageInfo, null, out _hdrColorImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _hdrColorImage, out var memReq);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in allocInfo, null, out _hdrColorMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _hdrColorImage, _hdrColorMemory, 0).ThrowOnError();

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _hdrColorImage,
            ViewType = ImageViewType.Type2D,
            Format = _hdrColorFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out _hdrColorView).ThrowOnError();

        _hdrColorSize = size;

        var hdrImageInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _hdrColorView,
            Sampler = _postProcessLinearSampler
        };
        _postProcessDescSet!.UpdateHdrInput(hdrImageInfo);
    }

    private unsafe void DestroyHdrColorTarget(VkContext vk)
    {
        DestroyMainFramebuffers(vk);
        if (_hdrColorView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _hdrColorView, null);
        if (_hdrColorImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _hdrColorImage, null);
        if (_hdrColorMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _hdrColorMemory, null);
        _hdrColorView = default;
        _hdrColorImage = default;
        _hdrColorMemory = default;
        _hdrColorSize = default;
    }

    /// <summary>Lazily (re)creates the half-resolution bloom ping/pong targets. Half of
    /// <paramref name="fullSize"/>, floored at 1x1 so a degenerate (0-sized, e.g. a not-yet-
    /// laid-out panel) full size can't produce a zero-extent image -- mirrors
    /// <see cref="EnsureSsaoTargets"/>'s own size-independent-of-full-res-zero-guard posture.
    /// Rewrites the bloom-blur/tonemap chain's bindings on (re)creation, same "never per-frame"
    /// contract as <see cref="EnsureHdrColorTarget"/>.</summary>
    private unsafe void EnsureBloomTargets(VkContext vk, PixelSize fullSize)
    {
        var size = new PixelSize(Math.Max(1, fullSize.Width / 2), Math.Max(1, fullSize.Height / 2));
        if (_bloomSize == size && _bloomPingView.Handle != 0) return;
        DestroyBloomTargets(vk);

        CreateBloomTarget(vk, size, _hdrColorFormat, out _bloomPingImage, out _bloomPingMemory, out _bloomPingView);
        CreateBloomTarget(vk, size, _hdrColorFormat, out _bloomPongImage, out _bloomPongMemory, out _bloomPongView);
        _bloomPingFramebuffer = CreateColorFramebuffer(vk, _bloomOffscreenRenderPass, _bloomPingView, size);
        _bloomPongFramebuffer = CreateColorFramebuffer(vk, _bloomOffscreenRenderPass, _bloomPongView, size);
        _bloomSize = size;

        var wideSize = new PixelSize(Math.Max(1, size.Width / 2), Math.Max(1, size.Height / 2));
        CreateBloomTarget(vk, wideSize, _hdrColorFormat, out _bloomWidePingImage, out _bloomWidePingMemory, out _bloomWidePingView);
        CreateBloomTarget(vk, wideSize, _hdrColorFormat, out _bloomWidePongImage, out _bloomWidePongMemory, out _bloomWidePongView);
        _bloomWidePingFramebuffer = CreateColorFramebuffer(vk, _bloomOffscreenRenderPass, _bloomWidePingView, wideSize);
        _bloomWidePongFramebuffer = CreateColorFramebuffer(vk, _bloomOffscreenRenderPass, _bloomWidePongView, wideSize);
        _bloomWideSize = wideSize;

        DescriptorImageInfo Sampled(ImageView view) => new()
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = view, Sampler = _postProcessLinearSampler
        };
        _postProcessDescSet!.UpdateBloomTargets(Sampled(_bloomPingView), Sampled(_bloomPongView),
            Sampled(_bloomWidePingView), Sampled(_bloomWidePongView));
    }

    private static unsafe Framebuffer CreateColorFramebuffer(VkContext vk, RenderPass renderPass, ImageView view, PixelSize size)
    {
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out var framebuffer).ThrowOnError();
        return framebuffer;
    }

    private static unsafe void CreateBloomTarget(VkContext vk, PixelSize size, Format hdrFormat, out Image image, out DeviceMemory memory, out ImageView view)
    {
        // Matches _hdrColorImage's own format choice (_hdrColorFormat -- see its field comment).
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = hdrFormat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in imageInfo, null, out image).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, image, out var memReq);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in allocInfo, null, out memory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, image, memory, 0).ThrowOnError();

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = hdrFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out view).ThrowOnError();
    }

    private unsafe void DestroyBloomTargets(VkContext vk)
    {
        if (_bloomPingFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _bloomPingFramebuffer, null);
        if (_bloomPongFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _bloomPongFramebuffer, null);
        if (_bloomPingView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _bloomPingView, null);
        if (_bloomPingImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _bloomPingImage, null);
        if (_bloomPingMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _bloomPingMemory, null);
        if (_bloomPongView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _bloomPongView, null);
        if (_bloomPongImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _bloomPongImage, null);
        if (_bloomPongMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _bloomPongMemory, null);
        _bloomPingFramebuffer = default; _bloomPongFramebuffer = default;
        _bloomPingView = default; _bloomPingImage = default; _bloomPingMemory = default;
        _bloomPongView = default; _bloomPongImage = default; _bloomPongMemory = default;
        _bloomSize = default;

        if (_bloomWidePingFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _bloomWidePingFramebuffer, null);
        if (_bloomWidePongFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _bloomWidePongFramebuffer, null);
        if (_bloomWidePingView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _bloomWidePingView, null);
        if (_bloomWidePingImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _bloomWidePingImage, null);
        if (_bloomWidePingMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _bloomWidePingMemory, null);
        if (_bloomWidePongView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _bloomWidePongView, null);
        if (_bloomWidePongImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _bloomWidePongImage, null);
        if (_bloomWidePongMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _bloomWidePongMemory, null);
        _bloomWidePingFramebuffer = default; _bloomWidePongFramebuffer = default;
        _bloomWidePingView = default; _bloomWidePingImage = default; _bloomWidePingMemory = default;
        _bloomWidePongView = default; _bloomWidePongImage = default; _bloomWidePongMemory = default;
        _bloomWideSize = default;
    }

    /// <summary>Mirrors <see cref="EnsureBloomTargets"/> exactly (half-resolution,
    /// <c>_hdrColorFormat</c>, reuses <see cref="_bloomOffscreenRenderPass"/>'s shape) -- two
    /// dedicated targets (mask output, radial-blur output) rather than a ping-pong pair, since the
    /// god-ray chain is mask -&gt; blur -&gt; done, not separable like bloom's two-pass Gaussian.
    /// Called from the same resize path as <see cref="EnsureBloomTargets"/>, High tier only.</summary>
    private unsafe void EnsureGodRayTargets(VkContext vk, PixelSize fullSize)
    {
        var size = new PixelSize(Math.Max(1, fullSize.Width / 2), Math.Max(1, fullSize.Height / 2));
        if (_godRaySize == size && _godRayMaskView.Handle != 0) return;
        DestroyGodRayTargets(vk);

        CreateBloomTarget(vk, size, _hdrColorFormat, out _godRayMaskImage, out _godRayMaskMemory, out _godRayMaskView);
        CreateBloomTarget(vk, size, _hdrColorFormat, out _godRayBlurImage, out _godRayBlurMemory, out _godRayBlurView);

        var maskViewLocal = _godRayMaskView;
        var maskFbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _bloomOffscreenRenderPass,
            AttachmentCount = 1,
            PAttachments = &maskViewLocal,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in maskFbInfo, null, out _godRayMaskFramebuffer).ThrowOnError();

        var blurViewLocal = _godRayBlurView;
        var blurFbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _bloomOffscreenRenderPass,
            AttachmentCount = 1,
            PAttachments = &blurViewLocal,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in blurFbInfo, null, out _godRayBlurFramebuffer).ThrowOnError();

        _godRaySize = size;

        var maskInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _godRayMaskView, Sampler = _postProcessLinearSampler };
        var blurInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _godRayBlurView, Sampler = _postProcessLinearSampler };
        _postProcessDescSet!.UpdateGodRayTargets(maskInfo, blurInfo);
    }

    private unsafe void DestroyGodRayTargets(VkContext vk)
    {
        if (_godRayMaskFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _godRayMaskFramebuffer, null);
        if (_godRayBlurFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _godRayBlurFramebuffer, null);
        if (_godRayMaskView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _godRayMaskView, null);
        if (_godRayMaskImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _godRayMaskImage, null);
        if (_godRayMaskMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _godRayMaskMemory, null);
        if (_godRayBlurView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _godRayBlurView, null);
        if (_godRayBlurImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _godRayBlurImage, null);
        if (_godRayBlurMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _godRayBlurMemory, null);
        _godRayMaskFramebuffer = default; _godRayBlurFramebuffer = default;
        _godRayMaskView = default; _godRayMaskImage = default; _godRayMaskMemory = default;
        _godRayBlurView = default; _godRayBlurImage = default; _godRayBlurMemory = default;
        _godRaySize = default;
        _godRayBlurDefined = false;
    }
}
