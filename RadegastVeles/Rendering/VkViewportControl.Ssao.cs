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
    /// Records the G-buffer-normal pass unconditionally, plus the SSAO and blur passes when
    /// <paramref name="ssaoOutputNeeded"/> -- up to three separate render-pass instances, all
    /// recorded into <paramref name="cmd"/> BEFORE the main render pass begins. Caller has
    /// already uploaded this frame's shared instance buffer
    /// (<see cref="_instanceDrawer"/>'s <c>UploadInstanceBatch</c>) covering
    /// <paramref name="opaqueCount"/>+<paramref name="sceneOpaqueCount"/> opaque faces at the
    /// SAME index ranges the main pass will read later -- required, not a convenience: Vulkan's
    /// always-instanced <c>prim.vert</c> (which <c>vulkan/gnorm.frag</c> pairs with) has no
    /// non-instanced code path, so this pass must read the identical per-face instance data the
    /// main pass reads, at the identical offsets (see <see cref="VkGNormPipeline"/>'s own doc
    /// comment) -- computing a SEPARATE opaque-only packing here would clobber the instance
    /// buffer across passes.
    /// <para>
    /// <paramref name="ssaoOutputNeeded"/> exists because SSR (see <c>doSsr</c> in
    /// <see cref="RenderFrame"/>) depends ONLY on the G-buffer normal/depth this method's first
    /// sub-pass produces, not on the SSAO texture itself -- calling this whenever
    /// <c>doSsao || doSsr</c> but skipping the SSAO/blur sub-passes when SSAO alone isn't needed
    /// avoids silently starving SSR of fresh depth/normal (disabling SSAO would otherwise leave
    /// SSR ray-marching against a stale prior frame's G-buffer, with no crash and no obvious
    /// visual tell). <see cref="VkPerFrameUbo.HasSsao"/> must still be gated on the real
    /// <c>doSsao</c> value by the caller, not this parameter -- the SSAO texture is genuinely
    /// unpopulated on an SSR-only frame.
    /// </para>
    /// </summary>
    private unsafe void RenderSsaoPasses(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        Matrix4x4 proj, int opaqueCount, int sceneOpaqueCount, bool ssaoOutputNeeded)
    {
        EnsureGBufferTarget(vk, pixelSize);

        // ── G-buffer normal pass -- unconditional, SSR's own dependency ────────────────────
        var gbufClear = stackalloc ClearValue[2]
        {
            new ClearValue { Color = new ClearColorValue { Float32_0 = 0f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 0f } },
            new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } }
        };
        var gbufBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _gbufferRenderPass,
            Framebuffer = _gbufFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 2,
            PClearValues = gbufClear
        };
        vk.Api.CmdBeginRenderPass(cmd, in gbufBegin, SubpassContents.Inline);
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _gnorm!.Pipeline);
        var frameSet = _frameSets!.FrameSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _gnorm.Layout, 0, 1, &frameSet, 0, null);
        // draws the same main-pass-filtered lists as the later opaque draw block
        // (_mainOpaqueVisible/_mainSceneOpaqueVisible), not the raw _opaqueFaces/_sceneOpaque --
        // matches GL's own DrawFacesNormal call sites, which pass the SAME frustum/
        // visibleSceneKeys as the main pass rather than computing their own.
        if (opaqueCount > 0) DrawFacesGNorm(vk, cmd, _mainOpaqueVisible, baseIndex: 0);
        if (sceneOpaqueCount > 0) DrawFacesGNorm(vk, cmd, _mainSceneOpaqueVisible, baseIndex: opaqueCount);
        vk.Api.CmdEndRenderPass(cmd);

        if (!ssaoOutputNeeded) return;
        EnsureSsaoTargets(vk, pixelSize);

        // ── SSAO pass ────────────────────────────────────────────────────────────
        var ssaoBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            Framebuffer = _ssaoFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 0
        };
        vk.Api.CmdBeginRenderPass(cmd, in ssaoBegin, SubpassContents.Inline);
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _ssaoPipeline!.Pipeline);
        var ssaoSet = _ssaoDescSet!.SsaoSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _ssaoPipeline.Layout, 0, 1, &ssaoSet, 0, null);

        // Radius/bias/strength match GL's hardcoded defaults exactly (SL viewer
        // llDrawPoolSimple SSAO pass defaults).
        var ssaoUbo = default(VkSsaoUbo);
        for (int i = 0; i < _ssaoKernel.Length && i < 64; i++) ssaoUbo.SetKernelSample(i, _ssaoKernel[i]);
        ssaoUbo.KernelSize = Math.Min(_ssaoKernel.Length, 64);
        ssaoUbo.NoiseScale = new Vector2(pixelSize.Width / 4.0f, pixelSize.Height / 4.0f);
        ssaoUbo.Proj = proj;
        ssaoUbo.ScreenSize = new Vector2(pixelSize.Width, pixelSize.Height);
        ssaoUbo.Radius = 0.5f;
        ssaoUbo.Bias = 0.025f;
        ssaoUbo.Strength = 1.2f;
        _ssaoDescSet.UpdateParams(ssaoUbo);

        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
        vk.Api.CmdEndRenderPass(cmd);

        // ── Blur pass ────────────────────────────────────────────────────────────
        var blurBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            Framebuffer = _ssaoBlurFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 0
        };
        vk.Api.CmdBeginRenderPass(cmd, in blurBegin, SubpassContents.Inline);
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _ssaoBlurPipeline!.Pipeline);
        var blurSet = _ssaoDescSet.BlurSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _ssaoBlurPipeline.Layout, 0, 1, &blurSet, 0, null);
        var texelSize = new Vector2(1.0f / pixelSize.Width, 1.0f / pixelSize.Height);
        vk.Api.CmdPushConstants(cmd, _ssaoBlurPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &texelSize);
        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
        vk.Api.CmdEndRenderPass(cmd);
    }

    /// <summary>Draws every face in <paramref name="faces"/> through <see cref="_gnorm"/> --
    /// no per-face material-set bind (gnorm.frag reads only varyings, no descriptors of its
    /// own beyond the already-bound set 0), unlike <see cref="DrawFaces"/>.</summary>
    private unsafe void DrawFacesGNorm(VkContext vk, CommandBuffer cmd,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces, int baseIndex)
    {
        for (int i = 0; i < faces.Count; i++)
            _instanceDrawer!.DrawBatchedInstance(cmd, faces[i].Mesh, baseIndex + i);
    }

    /// <summary>
    /// A hemisphere sample kernel
    /// in tangent space, distributed with an accelerating bias toward the origin.
    /// </summary>
    private static Vector3[] BuildSsaoKernel(int count)
    {
        var rng = new Random(42); // deterministic seed, matches GL
        var kernel = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = (float)(rng.NextDouble() * 2.0 - 1.0);
            float z = (float)rng.NextDouble(); // [0,1] -- upper hemisphere only
            var s = Vector3.Normalize(new Vector3(x, y, z));
            float scale = (float)i / count;
            scale = 0.1f + scale * scale * 0.9f; // lerp(0.1, 1.0, scale^2)
            kernel[i] = s * scale;
        }
        return kernel;
    }

    private unsafe void EnsureGBufferTarget(VkContext vk, PixelSize size)
    {
        if (_gbufSize == size && _gbufNormalView.Handle != 0) return;
        DestroyGBufferTarget(vk);

        var normalInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in normalInfo, null, out _gbufNormalImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _gbufNormalImage, out var normalReq);
        var normalAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = normalReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, normalReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in normalAlloc, null, out _gbufNormalMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _gbufNormalImage, _gbufNormalMemory, 0).ThrowOnError();
        var normalViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _gbufNormalImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in normalViewInfo, null, out _gbufNormalView).ThrowOnError();

        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            // SampledBit on top of the usual DepthStencilAttachmentBit -- unlike the main
            // pass's own _depthImage (write-only, never sampled), this one is read back by the
            // SSAO pass as uDepthTex.
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _gbufDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _gbufDepthImage, out var depthReq);
        var depthAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = depthReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, depthReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in depthAlloc, null, out _gbufDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _gbufDepthImage, _gbufDepthMemory, 0).ThrowOnError();
        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _gbufDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in depthViewInfo, null, out _gbufDepthView).ThrowOnError();

        var attachments = stackalloc ImageView[2] { _gbufNormalView, _gbufDepthView };
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _gbufferRenderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out _gbufFramebuffer).ThrowOnError();

        _gbufSize = size;

        // Rewrite the SSAO pass's depth/normal sampler bindings to point at the NEW images --
        // only fires on (re)creation, matching VkSsaoDescriptorSet.UpdateGbufferInputs' own
        // "never per-frame" contract.
        var depthImageInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _gbufDepthView, Sampler = _ssaoNearestSampler };
        var normalImageInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _gbufNormalView, Sampler = _ssaoNearestSampler };
        _ssaoDescSet!.UpdateGbufferInputs(depthImageInfo, normalImageInfo);

        // SSR's own consumer of this same G-buffer depth+normal pair (point-sampled, same
        // sampler SSAO/god-rays already use -- filtering across a depth/normal discontinuity
        // produces nonsense). Only fires on (re)creation, same discipline as UpdateGbufferInputs
        // just above.
        _frameSets!.UpdateSsrGBuffer(depthImageInfo, normalImageInfo);
        if (_frameSetsRefl != null) _frameSetsRefl.UpdateSsrGBuffer(depthImageInfo, normalImageInfo);
    }

    private unsafe void DestroyGBufferTarget(VkContext vk)
    {
        if (_gbufFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _gbufFramebuffer, null);
        if (_gbufNormalView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _gbufNormalView, null);
        if (_gbufNormalImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _gbufNormalImage, null);
        if (_gbufNormalMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _gbufNormalMemory, null);
        if (_gbufDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _gbufDepthView, null);
        if (_gbufDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _gbufDepthImage, null);
        if (_gbufDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _gbufDepthMemory, null);
        _gbufFramebuffer = default;
        _gbufNormalView = default; _gbufNormalImage = default; _gbufNormalMemory = default;
        _gbufDepthView = default; _gbufDepthImage = default; _gbufDepthMemory = default;
        _gbufSize = default;
    }

    private unsafe void EnsureSsaoTargets(VkContext vk, PixelSize size)
    {
        if (_ssaoTargetSize == size && _ssaoColorView.Handle != 0) return;
        DestroySsaoTargets(vk);

        // SSAO-raw colour texture -- Nearest filtering (matches GL's own TextureMinFilter/
        // MagFilter.Nearest for this target).
        CreateR8Target(vk, size, out _ssaoColorImage, out _ssaoColorMemory, out _ssaoColorView);
        // Blur colour texture -- Linear filtering, matching GL exactly (the ONE difference
        // between the two R8 targets' sampler state).
        CreateR8Target(vk, size, out _ssaoBlurImage, out _ssaoBlurMemory, out _ssaoBlurView);

        var ssaoColorViewLocal = _ssaoColorView;
        var ssaoFbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            AttachmentCount = 1,
            PAttachments = &ssaoColorViewLocal,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in ssaoFbInfo, null, out _ssaoFramebuffer).ThrowOnError();

        var ssaoBlurViewLocal = _ssaoBlurView;
        var blurFbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            AttachmentCount = 1,
            PAttachments = &ssaoBlurViewLocal,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in blurFbInfo, null, out _ssaoBlurFramebuffer).ThrowOnError();

        _ssaoTargetSize = size;

        // Rewrite the blur pass's uSsaoTex binding (reads _ssaoColorImage) and the main prim
        // pipeline's uSsaoMap binding (reads _ssaoBlurImage, the FINAL occlusion term) -- both
        // only fire on (re)creation, matching the "never per-frame" contract established above.
        var ssaoColorInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _ssaoColorView, Sampler = _ssaoNearestSampler };
        _ssaoDescSet!.UpdateBlurInput(ssaoColorInfo);
        var ssaoBlurInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _ssaoBlurView, Sampler = _ssaoLinearSampler };
        _frameSets!.UpdateSsaoMap(ssaoBlurInfo);
    }

    private unsafe void CreateR8Target(VkContext vk, PixelSize size, out Image image, out DeviceMemory memory, out ImageView view)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8Unorm,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in info, null, out image).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, image, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in alloc, null, out memory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, image, memory, 0).ThrowOnError();
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out view).ThrowOnError();
    }

    private unsafe void DestroySsaoTargets(VkContext vk)
    {
        if (_ssaoFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _ssaoFramebuffer, null);
        if (_ssaoColorView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _ssaoColorView, null);
        if (_ssaoColorImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _ssaoColorImage, null);
        if (_ssaoColorMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _ssaoColorMemory, null);
        if (_ssaoBlurFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _ssaoBlurFramebuffer, null);
        if (_ssaoBlurView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _ssaoBlurView, null);
        if (_ssaoBlurImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _ssaoBlurImage, null);
        if (_ssaoBlurMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _ssaoBlurMemory, null);
        _ssaoFramebuffer = default; _ssaoColorView = default; _ssaoColorImage = default; _ssaoColorMemory = default;
        _ssaoBlurFramebuffer = default; _ssaoBlurView = default; _ssaoBlurImage = default; _ssaoBlurMemory = default;
        _ssaoTargetSize = default;
    }
}
