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
    /// Selects up to <see cref="MaxLocalLightsShadowed"/> (2) shadow-casting lights as a
    /// range-filtered PREFIX of <see cref="_litLights"/> -- NOT a separate distance sort.
    /// <see cref="_litLights"/> is already nearest-first sorted by <see cref="SelectLocalLights"/>
    /// (top <see cref="MaxLocalLightsLit"/>), so the shadow casters are simply its first
    /// <c>min(_litLightCount, MaxLocalLightsShadowed)</c> entries, further filtered to
    /// <see cref="LocalLightShadowRange"/> (16m, half of <see cref="LocalLightRange"/>=32m) to
    /// bound the added per-frame culling/draw cost to only nearby/significant lights. Order here
    /// becomes slot assignment: caster[0] -&gt; slot 0 (uPointShadowMap0), caster[1] -&gt; slot 1.
    /// Must run AFTER <see cref="SelectLocalLights"/>.
    /// <para>
    /// No separate settings toggle: this reuses <see cref="ShadowsEnabled"/> (default off).
    /// Combined with the tier gate (Medium/High only) and this range/count cap, point shadows
    /// only ever cost anything for a user who already opted into shadows AND is within 16m of a
    /// lit prim -- a second toggle for that intersection would be settings-surface bloat for a
    /// case already opted out of by the first toggle.
    /// </para>
    /// </summary>
    private void SelectPointShadowCasters()
    {
        _pointShadowCasterCount = 0;
        for (int i = 0; i < _litLightCount && _pointShadowCasterCount < MaxLocalLightsShadowed; i++)
        {
            var d2 = Vector3.DistanceSquared(_litLights[i].WorldPosition, _camera.EyePosition);
            if (d2 > LocalLightShadowRange * LocalLightShadowRange) continue;
            // NOTE: since _litLights is nearest-first, once one entry fails the range check
            // every later entry is farther and would also fail -- `continue` (not `break`) here
            // is deliberately conservative in case a future caller reorders _litLights;
            // correctness doesn't depend on the early-exit optimization.
            _pointShadowCasters[_pointShadowCasterCount++] = _litLights[i];
        }
    }

    /// <summary>Fixed 2048x2048 depth-only target, created ONCE at init (unlike the G-buffer/
    /// SSAO targets, this has no "Ensure"/resize logic -- its resolution is independent of the
    /// panel's own pixel size, matching GL's own fixed <c>ShadowMapSize</c>). CompareEnable
    /// sampler (sampler2DShadow, shadow.glsl) built the same way
    /// <see cref="VkPlaceholderTextures"/>'s own shadow placeholder is.</summary>
    private unsafe void CreateShadowTarget(VkContext vk)
    {
        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D(ShadowMapSize, ShadowMapSize, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            // TransferDstBit added alongside the original DepthStencilAttachmentBit|SampledBit:
            // required by VkPlaceholderTextures.UploadDepthAndTransition's clear-via-
            // TransferDstOptimal approach below (VUID-vkCmdClearDepthStencilImage-pRanges-02660
            // -- found via this same self-test run, one edit after the layout-mismatch fix above,
            // not assumed correct on the first try). One-time init cost only, no runtime-path
            // consequence.
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _shadowDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _shadowDepthImage, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in alloc, null, out _shadowDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _shadowDepthImage, _shadowDepthMemory, 0).ThrowOnError();

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _shadowDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out _shadowDepthView).ThrowOnError();

        // Real bug found via 8c-4's own combined self-test (ShadowsEnabled=false is this
        // control's default, so RenderShadowPass never runs and never gets a chance to
        // transition this image via the shadow render pass's own LoadOp=Clear): without this
        // explicit initial transition, _shadowMapInfo below claims ShaderReadOnlyOptimal while
        // the actual image stays Undefined (its InitialLayout above) for the panel's whole
        // lifetime whenever shadows are off -- a real VUID-vkCmdDraw-None-09600 validation error
        // every time prim.frag samples uShadowMap in the main pass. Clearing to depth=1.0 also
        // means an un-rendered real shadow map reads as "nothing occluded" against the
        // LessOrEqual comparison sampler, matching VkPlaceholderTextures' own placeholder-shadow-
        // map intent (see its own doc comment) rather than leaving garbage/undefined values.
        VkPlaceholderTextures.UploadDepthAndTransition(vk, _shadowDepthImage, layers: 1);

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            CompareEnable = true,
            CompareOp = CompareOp.LessOrEqual,
            MinLod = 0,
            MaxLod = 1,
            BorderColor = BorderColor.FloatOpaqueWhite
        };
        vk.Api.CreateSampler(vk.Device, in samplerInfo, null, out _shadowSampler).ThrowOnError();

        var viewLocal = _shadowDepthView;
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _shadowRenderPass,
            AttachmentCount = 1,
            PAttachments = &viewLocal,
            Width = ShadowMapSize,
            Height = ShadowMapSize,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out _shadowFramebuffer).ThrowOnError();

        _shadowMapInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _shadowDepthView,
            Sampler = _shadowSampler
        };
        _frameSets!.UpdateShadowMap(_shadowMapInfo);
    }

    private unsafe void DestroyShadowTarget(VkContext vk)
    {
        if (_shadowFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _shadowFramebuffer, null);
        if (_shadowSampler.Handle != 0) vk.Api.DestroySampler(vk.Device, _shadowSampler, null);
        if (_shadowDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _shadowDepthView, null);
        if (_shadowDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _shadowDepthImage, null);
        if (_shadowDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _shadowDepthMemory, null);
        _shadowFramebuffer = default; _shadowSampler = default;
        _shadowDepthView = default; _shadowDepthImage = default; _shadowDepthMemory = default;
    }

    /// <summary>
    /// Creates PointShadowSlotCount (2) real cube shadow targets ONCE at init, reusing
    /// _shadowRenderPass/_shadowPipeline (already built by the directional-shadow init block that
    /// must run before this one -- see call site). Each slot is ArrayLayers=6,
    /// Flags=CreateCubeCompatibleBit, Format.D32Sfloat (same shape as
    /// VkPlaceholderTextures.CreateShadowPlaceholder(cube:true), EXCEPT Usage below adds
    /// DepthStencilAttachmentBit -- the placeholder is never rendered into, this target must be).
    /// Six per-face Type2D views (one per cube layer, framebuffer-attachable) plus one TypeCube
    /// sampling view per slot, over the SAME image -- Vulkan cannot render directly into a
    /// TypeCube view, only sample through one.
    /// </summary>
    private unsafe void CreatePointShadowTargets(VkContext vk)
    {
        for (int slot = 0; slot < PointShadowSlotCount; slot++)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.D32Sfloat,
                Extent = new Extent3D(PointShadowMapSize, PointShadowMapSize, 1),
                MipLevels = 1,
                ArrayLayers = 6,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                // DepthStencilAttachmentBit added vs. CreateShadowPlaceholder's shape -- this
                // target IS rendered into (6 framebuffers below), unlike the placeholder.
                // TransferDstBit needed for the initial UploadDepthAndTransition clear, same
                // reasoning as CreateShadowTarget's own comment on this flag.
                Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                Flags = ImageCreateFlags.CreateCubeCompatibleBit
            };
            vk.Api.CreateImage(vk.Device, in imageInfo, null, out _pointShadowImages[slot]).ThrowOnError();
            vk.Api.GetImageMemoryRequirements(vk.Device, _pointShadowImages[slot], out var req);
            var alloc = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = req.Size,
                MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(
                    vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
            };
            vk.Api.AllocateMemory(vk.Device, in alloc, null, out _pointShadowMemories[slot]).ThrowOnError();
            vk.Api.BindImageMemory(vk.Device, _pointShadowImages[slot], _pointShadowMemories[slot], 0).ThrowOnError();

            // Clear all 6 layers to depth=1.0 and transition to ShaderReadOnlyOptimal BEFORE any
            // per-face view/framebuffer exists -- same real bug CreateShadowTarget's own comment
            // documents (a descriptor claims ShaderReadOnlyOptimal while the real image stays
            // Undefined if this is skipped and RenderFrame never runs this slot's faces, e.g.
            // ShadowsEnabled=false all session).
            VkPlaceholderTextures.UploadDepthAndTransition(vk, _pointShadowImages[slot], layers: 6);

            for (int face = 0; face < PointShadowFaceCount; face++)
            {
                var faceViewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = _pointShadowImages[slot],
                    ViewType = ImageViewType.Type2D,
                    Format = Format.D32Sfloat,
                    SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, (uint)face, 1)
                };
                vk.Api.CreateImageView(vk.Device, in faceViewInfo, null,
                    out _pointShadowFaceViews[slot * PointShadowFaceCount + face]).ThrowOnError();
            }

            var cubeViewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _pointShadowImages[slot],
                ViewType = ImageViewType.TypeCube,
                Format = Format.D32Sfloat,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
                    ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 6)
            };
            vk.Api.CreateImageView(vk.Device, in cubeViewInfo, null, out _pointShadowCubeViews[slot]).ThrowOnError();

            var samplerInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                MipmapMode = SamplerMipmapMode.Nearest,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                CompareEnable = true,
                CompareOp = CompareOp.LessOrEqual,
                MinLod = 0,
                MaxLod = 1,
                BorderColor = BorderColor.FloatOpaqueWhite
            };
            vk.Api.CreateSampler(vk.Device, in samplerInfo, null, out _pointShadowSamplers[slot]).ThrowOnError();

            for (int face = 0; face < PointShadowFaceCount; face++)
            {
                var viewLocal = _pointShadowFaceViews[slot * PointShadowFaceCount + face];
                var fbInfo = new FramebufferCreateInfo
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _shadowRenderPass, // REUSED, not a new render pass
                    AttachmentCount = 1,
                    PAttachments = &viewLocal,
                    Width = PointShadowMapSize,
                    Height = PointShadowMapSize,
                    Layers = 1
                };
                vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null,
                    out _pointShadowFramebuffers[slot * PointShadowFaceCount + face]).ThrowOnError();
            }

            _pointShadowMapInfo[slot] = new DescriptorImageInfo
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = _pointShadowCubeViews[slot],
                Sampler = _pointShadowSamplers[slot]
            };
        }

        _frameSets!.UpdatePointShadowMap(0, _pointShadowMapInfo[0]);
        _frameSets.UpdatePointShadowMap(1, _pointShadowMapInfo[1]);
    }

    private unsafe void DestroyPointShadowTargets(VkContext vk)
    {
        for (int i = 0; i < _pointShadowFramebuffers.Length; i++)
        {
            if (_pointShadowFramebuffers[i].Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _pointShadowFramebuffers[i], null);
            _pointShadowFramebuffers[i] = default;
        }
        for (int slot = 0; slot < PointShadowSlotCount; slot++)
        {
            if (_pointShadowSamplers[slot].Handle != 0) vk.Api.DestroySampler(vk.Device, _pointShadowSamplers[slot], null);
            if (_pointShadowCubeViews[slot].Handle != 0) vk.Api.DestroyImageView(vk.Device, _pointShadowCubeViews[slot], null);
            for (int face = 0; face < PointShadowFaceCount; face++)
            {
                var v = _pointShadowFaceViews[slot * PointShadowFaceCount + face];
                if (v.Handle != 0) vk.Api.DestroyImageView(vk.Device, v, null);
                _pointShadowFaceViews[slot * PointShadowFaceCount + face] = default;
            }
            if (_pointShadowImages[slot].Handle != 0) vk.Api.DestroyImage(vk.Device, _pointShadowImages[slot], null);
            if (_pointShadowMemories[slot].Handle != 0) vk.Api.FreeMemory(vk.Device, _pointShadowMemories[slot], null);
            _pointShadowSamplers[slot] = default; _pointShadowCubeViews[slot] = default;
            _pointShadowImages[slot] = default; _pointShadowMemories[slot] = default;
        }
    }

    /// <summary>
    /// Computes this frame's directional light view-projection (texel-snapped ortho volume
    /// centred on the camera, NaN guards on Sky.SunDirection -- a
    /// NaN sun direction is a real, previously-hit hazard, not defensive paranoia -- and a
    /// degenerate-zenith up-vector guard). Returns false (leaving <see cref="_shadowLightVp"/>
    /// unchanged) if a finite matrix can't be produced this frame, mirroring GL's own
    /// early-return/_hasDirShadow=false posture. Deliberately does NOT replicate GL's
    /// _spatialGrid frustum-cull query for the shadow pass's own visible-set -- SceneSpatialGrid/
    /// frustum culling was already deferred out of its own scope (optimization on top
    /// of the streaming substrate, not a prerequisite), so this draws every opaque face
    /// unconditionally, same simplification.
    /// </summary>
    private bool TryComputeShadowLightVp(out Matrix4x4 lightView, out Matrix4x4 lightProj)
    {
        lightView = default; lightProj = default;
        var eye = _camera.EyePosition;
        if (!IsFinite(eye) || !IsFinite(Sky.SunDirection) || Sky.SunDirection.LengthSquared() < 1e-10f)
            return false;
        var sunDir = Vector3.Normalize(Sky.SunDirection);
        var up = MathF.Abs(Vector3.Dot(sunDir, Vector3.UnitZ)) > 0.999f ? Vector3.UnitY : Vector3.UnitZ;

        var lightRotOnly = Matrix4x4.CreateLookAt(Vector3.Zero, -sunDir, up);
        var centerLs = Vector3.Transform(eye, lightRotOnly);
        float texelSize = (2f * ShadowRadius) / ShadowMapSize;
        centerLs.X = MathF.Floor(centerLs.X / texelSize) * texelSize;
        centerLs.Y = MathF.Floor(centerLs.Y / texelSize) * texelSize;
        Matrix4x4.Invert(lightRotOnly, out var invRot);
        var snappedCenter = Vector3.Transform(centerLs, invRot);

        var lightEye = snappedCenter + sunDir * ShadowDepthMargin;
        lightView = Matrix4x4.CreateLookAt(lightEye, snappedCenter, up);
        lightProj = Matrix4x4.CreateOrthographicOffCenter(
            -ShadowRadius, ShadowRadius, -ShadowRadius, ShadowRadius, 1f, ShadowDepthMargin * 2f);
        var lightVp = lightView * lightProj;
        if (!IsFinite(lightVp)) return false;

        _shadowLightVp = lightVp;
        return true;
    }

    /// <summary>
    /// Records the directional shadow depth pass into <paramref name="cmd"/>. Caller has
    /// already uploaded this frame's shared instance buffer including the shadow region (light-
    /// space MVP, baked via <see cref="WriteInstanceData"/> with the light's view/proj) at
    /// <paramref name="shadowBase"/>/<paramref name="shadowSceneBase"/>. No material set bind
    /// needed (shadow_depth.frag declares no descriptors) -- reuses <see cref="DrawFacesGNorm"/>
    /// (plain batched-instance draw, no per-face bind), same shape SSAO's G-buffer pass already
    /// established for exactly this "no set 2" case. Restores viewport/scissor to
    /// <paramref name="pixelSize"/> before returning -- this target's fixed 2048x2048 resolution
    /// differs from the panel's own, and viewport/scissor are dynamic state shared across every
    /// render pass recorded into this command buffer, including the main pass that follows.
    /// </summary>
    private unsafe void RenderShadowPass(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        int shadowBase, int shadowSceneBase, int opaqueCount, int sceneOpaqueCount)
    {
        var clearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } };
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _shadowRenderPass,
            Framebuffer = _shadowFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(ShadowMapSize, ShadowMapSize)),
            ClearValueCount = 1,
            PClearValues = &clearValue
        };
        vk.Api.CmdBeginRenderPass(cmd, in beginInfo, SubpassContents.Inline);

        var shadowViewport = new Viewport { X = 0, Y = 0, Width = ShadowMapSize, Height = ShadowMapSize, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in shadowViewport);
        var shadowScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D(ShadowMapSize, ShadowMapSize) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &shadowScissor);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _shadowPipeline!.Pipeline);
        var frameSet = _frameSets!.FrameSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _shadowPipeline.Layout, 0, 1, &frameSet, 0, null);

        // draws this pass's own independently-culled lists (_shadowOpaqueVisible/
        // _shadowSceneOpaqueVisible), not the raw _opaqueFaces/_sceneOpaque -- the caller passes
        // this pass's own filtered counts too, matching GL's independent shadow-frustum grid
        // query (never gated on FrustumCullingEnabled).
        if (opaqueCount > 0) DrawFacesGNorm(vk, cmd, _shadowOpaqueVisible, baseIndex: shadowBase);
        if (sceneOpaqueCount > 0) DrawFacesGNorm(vk, cmd, _shadowSceneOpaqueVisible, baseIndex: shadowSceneBase);

        vk.Api.CmdEndRenderPass(cmd);

        RestoreMainViewportScissor(vk, cmd, pixelSize);
    }

    /// <summary>
    /// Records one cube-face depth pass for point-light shadows into <paramref name="cmd"/>.
    /// Caller has already uploaded this frame's shared instance buffer including this face's
    /// region (light-space MVP, baked via <see cref="WriteInstanceData"/> with that face's own
    /// view/proj -- see <see cref="PointShadowFaceDir"/>/<see cref="PointShadowFaceUp"/>) at
    /// <paramref name="baseIndex"/>/<paramref name="sceneBaseIndex"/>. Reuses
    /// <see cref="_shadowRenderPass"/>/<see cref="_shadowPipeline"/> -- render-pass compatibility
    /// only cares about attachment format/sample-count, not resolution or which framebuffer is
    /// bound, so no separate pipeline/render pass exists for point shadows at all. Restores
    /// viewport/scissor to <paramref name="pixelSize"/> before returning, same reasoning as
    /// <see cref="RenderShadowPass"/>'s own restore.
    /// </summary>
    private unsafe void RenderPointShadowFace(VkContext vk, CommandBuffer cmd, PixelSize pixelSize, int faceIndex,
        Framebuffer framebuffer, int baseIndex, int sceneBaseIndex, int opaqueCount, int sceneOpaqueCount)
    {
        var clearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } };
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _shadowRenderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(PointShadowMapSize, PointShadowMapSize)),
            ClearValueCount = 1,
            PClearValues = &clearValue
        };
        vk.Api.CmdBeginRenderPass(cmd, in beginInfo, SubpassContents.Inline);

        var faceViewport = new Viewport { X = 0, Y = 0, Width = PointShadowMapSize, Height = PointShadowMapSize, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in faceViewport);
        var faceScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D(PointShadowMapSize, PointShadowMapSize) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &faceScissor);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _shadowPipeline!.Pipeline);
        var frameSet = _frameSets!.FrameSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _shadowPipeline.Layout, 0, 1, &frameSet, 0, null);

        if (opaqueCount > 0) DrawFacesGNorm(vk, cmd, _pointShadowOpaqueVisible[faceIndex], baseIndex: baseIndex);
        if (sceneOpaqueCount > 0) DrawFacesGNorm(vk, cmd, _pointShadowSceneOpaqueVisible[faceIndex], baseIndex: sceneBaseIndex);

        vk.Api.CmdEndRenderPass(cmd);

        RestoreMainViewportScissor(vk, cmd, pixelSize);
    }
}
