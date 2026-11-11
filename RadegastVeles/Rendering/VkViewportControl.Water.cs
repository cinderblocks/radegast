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
    /// Creates (or resizes) <see cref="_underwaterSourceImage"/>: a persistent copy target the
    /// underwater pass copies the just-rendered swapchain image into immediately before running,
    /// since Vulkan can't sample and write the same image within one pass. Unlike the SSAO/
    /// G-buffer targets, this image is never a render-pass color attachment -- only a
    /// <c>CmdCopyImage</c> destination and a sampled texture -- so it carries
    /// <c>TransferDstBit | SampledBit</c> usage, not <c>ColorAttachmentBit</c>, and has no
    /// framebuffer of its own. Called every frame the underwater pass actually runs (mirrors
    /// EnsureSsaoTargets' own "only when SSAO is enabled this frame" call site), early-returning
    /// once the size matches.
    /// </summary>
    private unsafe void EnsureUnderwaterTarget(VkContext vk, PixelSize size)
    {
        if (_underwaterTargetSize == size && _underwaterSourceView.Handle != 0) return;
        DestroyUnderwaterTarget(vk);

        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            // The swapchain image's own format, not a hardcoded RGBA8: vkCmdCopyImage is a raw
            // texel copy, so copying macOS's BGRA8 swapchain image into an RGBA8 image would
            // hand underwater.frag red and blue swapped.
            Format = _swapchainFormat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in info, null, out _underwaterSourceImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _underwaterSourceImage, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in alloc, null, out _underwaterSourceMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _underwaterSourceImage, _underwaterSourceMemory, 0).ThrowOnError();
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _underwaterSourceImage,
            ViewType = ImageViewType.Type2D,
            Format = _swapchainFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out _underwaterSourceView).ThrowOnError();

        _underwaterTargetSize = size;
        // Fresh image -- caller's first barrier must transition FROM Undefined/None, not a stale
        // prior layout/access left over from the target this just destroyed.
        _underwaterSourceLayout = ImageLayout.Undefined;
        _underwaterSourceAccess = AccessFlags.None;

        // Rewrite the underwater pass's uSceneColor binding to point at the NEW image -- only
        // fires on (re)creation, matching VkUnderwaterDescriptorSet.UpdateSceneColorInput's own
        // "never per-frame" contract.
        var sceneColorInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _underwaterSourceView,
            Sampler = _underwaterLinearSampler
        };
        _underwaterDescSet!.UpdateSceneColorInput(sceneColorInfo);
    }

    private unsafe void DestroyUnderwaterTarget(VkContext vk)
    {
        if (_underwaterSourceView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _underwaterSourceView, null);
        if (_underwaterSourceImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _underwaterSourceImage, null);
        if (_underwaterSourceMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _underwaterSourceMemory, null);
        _underwaterSourceView = default; _underwaterSourceImage = default; _underwaterSourceMemory = default;
        _underwaterTargetSize = default;
        _underwaterSourceLayout = ImageLayout.Undefined;
        _underwaterSourceAccess = AccessFlags.None;
    }

    /// <summary>
    /// Creates (or resizes) <see cref="_opaqueSnapshotImage"/>: a persistent copy target the
    /// split main pass copies the finished opaque scene colour into, between the opaque-only
    /// pass and the water/SSR/alpha continuation pass. Originally built for water refraction
    /// alone; now shared with SSR (see this field group's own header comment) -- both features
    /// need exactly the same snapshot, so this one target/one copy serves both, patched into
    /// both consumers' descriptor sets below. Mirrors <see cref="EnsureUnderwaterTarget"/>'s own
    /// shape exactly (see its doc comment) -- same "never a render-pass attachment itself, only a
    /// CmdCopyImage destination and a sampled texture" role -- except format
    /// <c>_hdrColorFormat</c> (matching <see cref="_hdrColorImage"/>'s own HDR format, not
    /// <see cref="_underwaterSourceImage"/>'s LDR <c>R8G8B8A8Unorm</c>: this copies pre-tonemap
    /// data). Called only from the split-main-pass branch of <c>RenderFrame</c>, i.e. only on
    /// frames where water OR SSR is actually active on Medium/High tier (see
    /// <c>doOpaqueSnapshot</c>).
    /// </summary>
    private unsafe void EnsureOpaqueSnapshotTarget(VkContext vk, PixelSize size)
    {
        if (_opaqueSnapshotSize == size && _opaqueSnapshotView.Handle != 0) return;
        DestroyOpaqueSnapshotTarget(vk);

        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _hdrColorFormat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in info, null, out _opaqueSnapshotImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _opaqueSnapshotImage, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in alloc, null, out _opaqueSnapshotMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _opaqueSnapshotImage, _opaqueSnapshotMemory, 0).ThrowOnError();
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _opaqueSnapshotImage,
            ViewType = ImageViewType.Type2D,
            Format = _hdrColorFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out _opaqueSnapshotView).ThrowOnError();

        _opaqueSnapshotSize = size;
        // Fresh image -- caller's first barrier must transition FROM Undefined/None, not a stale
        // prior layout/access left over from the target this just destroyed.
        _opaqueSnapshotLayout = ImageLayout.Undefined;
        _opaqueSnapshotAccess = AccessFlags.None;

        var refractionInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _opaqueSnapshotView,
            Sampler = _postProcessLinearSampler
        };
        _waterDescSet!.UpdateRefractionInput(refractionInfo);
        // SSR's own consumer of this same snapshot -- see VkPrimDescriptorSets.UpdateSsrSceneColor's
        // own "rewrite only on (re)creation" contract, identical to UpdateSsaoMap's.
        _frameSets!.UpdateSsrSceneColor(refractionInfo);
        if (_frameSetsRefl != null) _frameSetsRefl.UpdateSsrSceneColor(refractionInfo);
    }

    private unsafe void DestroyOpaqueSnapshotTarget(VkContext vk)
    {
        if (_opaqueSnapshotView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _opaqueSnapshotView, null);
        if (_opaqueSnapshotImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _opaqueSnapshotImage, null);
        if (_opaqueSnapshotMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _opaqueSnapshotMemory, null);
        _opaqueSnapshotView = default; _opaqueSnapshotImage = default; _opaqueSnapshotMemory = default;
        _opaqueSnapshotSize = default;
        _opaqueSnapshotLayout = ImageLayout.Undefined;
        _opaqueSnapshotAccess = AccessFlags.None;
    }

    private static VkTexture? LoadEmbeddedWaterTexture(VkContext vk, string filename)
    {
        var uri = new Uri("avares://RadegastVeles/Rendering/shader_data/" + filename);
        using var stream = Avalonia.Platform.AssetLoader.Open(uri);
        var bitmap = SKBitmap.Decode(stream);
        if (bitmap == null) return null;
        return new VkTexture(vk, VkTexture.Preprocess(bitmap));
    }

    /// <summary>Fixed 512x512 colour+depth target, created ONCE at init (like
    /// <see cref="CreateShadowTarget"/>, no per-frame resize logic -- GL's own
    /// <c>WaterReflSize</c> is independent of the panel's pixel size too). Reuses
    /// <see cref="VkRenderPass.CreateGBufferPass"/>'s shape: the reflection colour texture is
    /// SAMPLED by water.frag later the same frame, unlike the main
    /// scene pass's own colour attachment, which only ever gets blitted to the swapchain -- it
    /// needs the entry/exit subpass dependencies and ShaderReadOnlyOptimal final layout
    /// CreateGBufferPass already provides, not CreateMainScenePass's shape). The depth
    /// attachment gets SampledBit too even though nothing ever samples it -- CreateGBufferPass's
    /// shared shape sets FinalLayout=ShaderReadOnlyOptimal on BOTH attachments, and that layout
    /// transition is illegal without the matching usage flag regardless of whether anything
    /// reads through it.</summary>
    private unsafe void CreateWaterReflectionTarget(VkContext vk)
    {
        _waterReflRenderPass = VkRenderPass.CreateGBufferPass(vk, Format.R8G8B8A8Unorm, Format.D32Sfloat);

        var colorInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(WaterReflSize, WaterReflSize, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in colorInfo, null, out _waterReflColorImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _waterReflColorImage, out var colorReq);
        var colorAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = colorReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, colorReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in colorAlloc, null, out _waterReflColorMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _waterReflColorImage, _waterReflColorMemory, 0).ThrowOnError();
        var colorViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _waterReflColorImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in colorViewInfo, null, out _waterReflColorView).ThrowOnError();

        var colorSamplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0,
            MaxLod = 0
        };
        vk.Api.CreateSampler(vk.Device, in colorSamplerInfo, null, out _waterReflColorSampler).ThrowOnError();

        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D(WaterReflSize, WaterReflSize, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _waterReflDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _waterReflDepthImage, out var depthReq);
        var depthAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = depthReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, depthReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in depthAlloc, null, out _waterReflDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _waterReflDepthImage, _waterReflDepthMemory, 0).ThrowOnError();
        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _waterReflDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in depthViewInfo, null, out _waterReflDepthView).ThrowOnError();

        var attachments = stackalloc ImageView[2] { _waterReflColorView, _waterReflDepthView };
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _waterReflRenderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = WaterReflSize,
            Height = WaterReflSize,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out _waterReflFramebuffer).ThrowOnError();
    }

    private unsafe void DestroyWaterReflectionTarget(VkContext vk)
    {
        if (_waterReflFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _waterReflFramebuffer, null);
        if (_waterReflColorSampler.Handle != 0) vk.Api.DestroySampler(vk.Device, _waterReflColorSampler, null);
        if (_waterReflColorView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _waterReflColorView, null);
        if (_waterReflColorImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _waterReflColorImage, null);
        if (_waterReflColorMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _waterReflColorMemory, null);
        if (_waterReflDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _waterReflDepthView, null);
        if (_waterReflDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _waterReflDepthImage, null);
        if (_waterReflDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _waterReflDepthMemory, null);
        if (_waterReflRenderPass.Handle != 0) vk.Api.DestroyRenderPass(vk.Device, _waterReflRenderPass, null);
        _waterReflFramebuffer = default; _waterReflColorSampler = default;
        _waterReflColorView = default; _waterReflColorImage = default; _waterReflColorMemory = default;
        _waterReflDepthView = default; _waterReflDepthImage = default; _waterReflDepthMemory = default;
        _waterReflRenderPass = default;
    }

    /// <summary>
    /// Records the water-reflection pre-pass: the opaque scene, drawn through
    /// <see cref="VkPrimPipeline.ReflOpaque"/> (same shaders/layout as the main opaque pass,
    /// FrontFace flipped to compensate the Z-mirror's winding flip) with the REFLECTED camera's
    /// own <see cref="_frameSetsRefl"/> descriptor set (see that field's own doc comment for why
    /// it can't share <see cref="_frameSets"/>). Caller has already uploaded this frame's shared
    /// instance buffer including the reflection region (baked via <see cref="WriteInstanceData"/>
    /// with <paramref name="reflView"/>/<paramref name="proj"/>) at
    /// <paramref name="reflBase"/>/<paramref name="reflSceneBase"/>, and has already written
    /// <paramref name="reflView"/>-relative View/ViewInv/SunDir (HasSsao=0) into
    /// <see cref="_frameSetsRefl"/>'s UBO. Restores viewport/scissor to
    /// <paramref name="pixelSize"/> before returning, same reasoning as
    /// <see cref="RenderShadowPass"/>.
    /// </summary>
    private unsafe void RenderWaterReflectionPass(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        int reflBase, int reflSceneBase, int opaqueCount, int sceneOpaqueCount)
    {
        var clearValues = stackalloc ClearValue[2]
        {
            new ClearValue { Color = new ClearColorValue { Float32_0 = 0.39f, Float32_1 = 0.58f, Float32_2 = 0.93f, Float32_3 = 1f } },
            new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } }
        };
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _waterReflRenderPass,
            Framebuffer = _waterReflFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(WaterReflSize, WaterReflSize)),
            ClearValueCount = 2,
            PClearValues = clearValues
        };
        vk.Api.CmdBeginRenderPass(cmd, in beginInfo, SubpassContents.Inline);

        var reflViewport = new Viewport { X = 0, Y = 0, Width = WaterReflSize, Height = WaterReflSize, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in reflViewport);
        var reflScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D(WaterReflSize, WaterReflSize) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &reflScissor);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _prim!.ReflOpaque);
        var frameAndPassSets = stackalloc DescriptorSet[2] { _frameSetsRefl!.FrameSet, _frameSetsRefl.PassSet };
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _prim.Layout, 0, 2, frameAndPassSets, 0, null);

        // draws this pass's own independently-culled lists (_reflOpaqueVisible/
        // _reflSceneOpaqueVisible), not the raw _opaqueFaces/_sceneOpaque -- the caller passes
        // this pass's own filtered counts too, matching GL's independent reflection-frustum
        // grid query (never gated on FrustumCullingEnabled).
        if (opaqueCount > 0) DrawFaces(vk, cmd, _reflOpaqueVisible, baseIndex: reflBase);
        if (sceneOpaqueCount > 0) DrawFaces(vk, cmd, _reflSceneOpaqueVisible, baseIndex: reflSceneBase);

        vk.Api.CmdEndRenderPass(cmd);

        RestoreMainViewportScissor(vk, cmd, pixelSize);
    }

    /// <summary>
    /// Draws the water surface as a full-screen triangle into the currently-recording main
    /// render pass. Ray-plane intersection done
    /// entirely in water.frag; this method only advances the animation clock and uploads
    /// uniforms. Set 0 is the SAME <see cref="_frameSets"/> descriptor set the main geometry
    /// pass binds (main camera, not the reflected one -- the water SURFACE itself is real
    /// geometry seen from the real camera; only the reflection PRE-PASS uses the mirrored
    /// camera), so the caller must have already written this frame's real
    /// <see cref="VkPerFrameUbo"/> (the SECOND write, with the correct HasSsao) via
    /// <c>_frameSets.UpdatePerFrame</c> before calling this.
    /// </summary>
    private unsafe void DrawWater(VkContext vk, CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj, float waterHeight, bool underwater,
        bool hasRefraction)
    {
        long now = Environment.TickCount64;
        float dt = _waterLastTick == 0 ? 0f : MathF.Min((now - _waterLastTick) / 1000f, 0.1f);
        _waterLastTick = now;
        _waterTime += dt;

        var eye = _camera.EyePosition;
        var viewProj = view * proj;
        Matrix4x4.Invert(viewProj, out var invVp);

        var waterUbo = default(VkWaterUbo);
        waterUbo.ViewProj = viewProj;
        waterUbo.ReflViewProj = _lastReflViewProj;
        waterUbo.InvViewProj = invVp;
        waterUbo.EyePos = eye;
        waterUbo.WaterHeight = waterHeight;
        waterUbo.Time = _waterTime;
        waterUbo.WaterColor = WaterFogColor;
        // Forced off underwater regardless of WaterReflectionsEnabled -- no reflection pass runs
        // for an underwater frame (see doWaterReflThisFrame's own !underwater gate), so
        // sampling uReflectionTex here would show a stale reflection from the last above-water
        // frame rather than anything real. Also forced off on VkGraphicsTier.Low, for a
        // stronger reason than "stale": doWaterReflThisFrame's own tier gate (see
        // _graphicsTier's own field comment) means the reflection pass NEVER runs at all on
        // that tier, not even once -- _waterReflColorImage's own layout never leaves whatever
        // CreateWaterReflectionTarget's initial transition left it in, so sampling it here would
        // be a real, validation-catchable layout-mismatch read, not merely a stale one.
        waterUbo.HasReflection = (!underwater && _waterReflReady && WaterReflectionsEnabled
            && _graphicsTier != VkGraphicsTier.Low) ? 1 : 0;
        // Caller has already run the split-main-pass snapshot (EnsureOpaqueSnapshotTarget + the
        // copy sequence) before this call whenever hasRefraction is true -- see RenderFrame's own
        // doOpaqueSnapshot branch. Independent of HasReflection: refraction doesn't depend on
        // the reflection pre-pass at all, just on the mid-frame colour snapshot. Unlike
        // HasReflection, deliberately NOT forced off when underwater: pass 1 always renders from
        // the actual current camera (underwater or not), so the snapshot is correct either way --
        // above water it's the scene behind the surface; from below (water.frag's eyeBelow branch,
        // looking up) it's whatever opaque geometry/sky sits beyond the surface in that direction,
        // which is the right thing to show through the wave-distorted UV in both cases.
        waterUbo.HasRefraction = hasRefraction ? 1 : 0;
        _waterDescSet!.UpdateWater(waterUbo);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _waterPipeline!.Pipeline);
        var sets = stackalloc DescriptorSet[2] { _frameSets!.FrameSet, _waterDescSet.Set };
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _waterPipeline.Layout, 0, 2, sets, 0, null);
        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
    }

    /// <summary>
    /// Composites the underwater post-process effect (screen-space refraction wobble + soft
    /// caustic highlight + depth-based fog tint, see underwater.frag) on top of the just-rendered
    /// frame. Called from RenderFrame's main pass ONLY when <c>underwater</c> is true -- when
    /// false this entire method, including the copy below, is skipped, so the feature costs
    /// nothing above water.
    /// <para>
    /// Recorded into the SAME command buffer as the main pass, immediately after its
    /// <c>CmdEndRenderPass</c> and before <c>cmd.Submit()</c> -- see <see cref="VkRenderPass.
    /// CreateUnderwaterPass"/>'s own doc comment for why this needs a copy-out rather than the
    /// subpass-dependency synchronization SSAO/blur use: this pass samples the ALREADY-COMPOSITED
    /// swapchain image the main pass just wrote, which Vulkan cannot do within the same render
    /// pass a color attachment is written in (no feedback-loop sampling).
    /// </para>
    /// <para>
    /// The swapchain image's own layout transitions go through <see cref="VkInteropImage.
    /// TransitionLayout"/> (same helper <see cref="VkInteropSwapchain"/> itself uses for
    /// BeginDraw/Present), which tracks the image's current layout/access internally -- so this
    /// method never has to guess or hardcode what layout the image is already in, only where it
    /// needs to end up. <see cref="_underwaterSourceImage"/> has no such wrapper (it's a plain
    /// <see cref="Image"/>), so its layout/access are tracked by hand in
    /// <see cref="_underwaterSourceLayout"/>/<see cref="_underwaterSourceAccess"/> instead.
    /// </para>
    /// </summary>
    private unsafe void RenderUnderwaterPass(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        VkInteropImage swapchainImage, float waterHeightVal)
    {
        EnsureUnderwaterTarget(vk, pixelSize);

        long now = Environment.TickCount64;
        float dt = _underwaterLastTick == 0 ? 0f : MathF.Min((now - _underwaterLastTick) / 1000f, 0.1f);
        _underwaterLastTick = now;
        _underwaterTime += dt;

        // ── Copy the just-rendered frame out so it can be sampled as this pass's input ────────
        swapchainImage.TransitionLayout(cmd, ImageLayout.TransferSrcOptimal, AccessFlags.TransferReadBit);
        VkMemoryHelper.TransitionLayout(vk.Api, cmd, _underwaterSourceImage,
            _underwaterSourceLayout, _underwaterSourceAccess,
            ImageLayout.TransferDstOptimal, AccessFlags.TransferWriteBit, 1);

        var copyRegion = new ImageCopy
        {
            SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            SrcOffset = new Offset3D(0, 0, 0),
            DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            DstOffset = new Offset3D(0, 0, 0),
            Extent = new Extent3D((uint)pixelSize.Width, (uint)pixelSize.Height, 1)
        };
        vk.Api.CmdCopyImage(cmd, swapchainImage.InternalHandle, ImageLayout.TransferSrcOptimal,
            _underwaterSourceImage, ImageLayout.TransferDstOptimal, 1, in copyRegion);

        // ── Transition both images back to how this pass (and everything after it) needs them ─
        VkMemoryHelper.TransitionLayout(vk.Api, cmd, _underwaterSourceImage,
            ImageLayout.TransferDstOptimal, AccessFlags.TransferWriteBit,
            ImageLayout.ShaderReadOnlyOptimal, AccessFlags.ShaderReadBit, 1);
        _underwaterSourceLayout = ImageLayout.ShaderReadOnlyOptimal;
        _underwaterSourceAccess = AccessFlags.ShaderReadBit;

        // Re-enter as a render target -- CreateUnderwaterPass's InitialLayout=ColorAttachmentOptimal
        // requires this to be accurate (LoadOp=Load reads whatever's actually there).
        swapchainImage.TransitionLayout(cmd, ImageLayout.ColorAttachmentOptimal, AccessFlags.ColorAttachmentWriteBit);

        // ── Draw the full-screen composite directly into the swapchain image ───────────────────
        var framebuffer = swapchainImage.GetFramebuffer(_underwaterPass);

        var viewport = new Viewport { X = 0, Y = 0, Width = pixelSize.Width, Height = pixelSize.Height, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in viewport);
        var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &scissor);

        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _underwaterPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 0,
            PClearValues = null
        };
        vk.Api.CmdBeginRenderPass(cmd, in beginInfo, SubpassContents.Inline);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _underwaterPipeline!.Pipeline);
        var set = _underwaterDescSet!.Set;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _underwaterPipeline.Layout, 0, 1, &set, 0, null);

        // Depth-based tint intensity: fully tinted a few metres below the surface, matching
        // the depth falloff a real underwater "visibility" would have -- tuned by eye, not
        // derived from any EEP parameter (SL doesn't expose an underwater visibility-distance
        // setting).
        float depthBelow = MathF.Max(0f, waterHeightVal - _camera.EyePosition.Z);
        float intensity = Math.Clamp(depthBelow / 4f, 0f, 1f);
        var waterFog = WaterFogColor;
        var pushData = stackalloc float[5] { waterFog.X, waterFog.Y, waterFog.Z, intensity, _underwaterTime };
        vk.Api.CmdPushConstants(cmd, _underwaterPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 20, pushData);

        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);

        vk.Api.CmdEndRenderPass(cmd);
    }

    private static bool WaterPlaneInView(Matrix4x4 view, Matrix4x4 proj, Vector3 eye, float waterHeight)
    {
        if (!Matrix4x4.Invert(view * proj, out var invViewProj)) return true;

        bool eyeAbove = eye.Z >= waterHeight;
        for (int i = 0; i < 4; i++)
        {
            var ndc = new Vector4((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, 1f, 1f);
            var far = Vector4.Transform(ndc, invViewProj);
            if (MathF.Abs(far.W) < 1e-6f) return true;
            float dz = far.Z / far.W - eye.Z;
            if (eyeAbove ? dz < 0f : dz > 0f) return true;
        }
        return false;
    }
}
