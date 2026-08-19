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

// Default placeholder textures for prim.frag's set 1 (per-pass shadow/SSAO samplers) and set 2
// (per-material samplers) -- required infrastructure, not optional, per plan Section 5's
// uber-shader decision: "bind a small set of default placeholder textures into the unused
// sampler slots instead of branching around missing bindings." Sampling an unbound Vulkan
// descriptor is undefined behavior, and PrimViewer's pilot (Section 8a) has no real shadow
// map / SSAO buffer / possibly-absent material textures to bind -- these placeholders are
// what makes that legal. Created once off the shared VkContext, not per-panel.
//
// Two of these need more than "a 1x1 solid-color image": uShadowMap/uPointShadowMap0/1 are
// sampler2DShadow/samplerCubeShadow (shadow.glsl) -- Vulkan depth-COMPARISON samplers, which
// need an actual depth-format image (not a color format) and a VkSampler with
// CompareEnable=true. uPointShadowMap0/1 additionally need a 6-layer CUBE image, not 2D.
// Placeholder depth value is 1.0 (far) with CompareOp=LessOrEqual, so every real fragment
// depth compares "not in shadow" against it -- the correct default for "no shadow caster here."

using System;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkPlaceholderTextures : IDisposable
{
    public DescriptorImageInfo White { get; }
    public DescriptorImageInfo FlatNormal { get; }
    public DescriptorImageInfo Black { get; }
    public DescriptorImageInfo ShadowMap2D { get; }
    public DescriptorImageInfo ShadowMapCube { get; }

    private readonly VkContext _vk;
    private readonly Flat2D _white;
    private readonly Flat2D _flatNormal;
    private readonly Flat2D _black;
    private readonly ShadowPlaceholder _shadow2D;
    private readonly ShadowPlaceholder _shadowCube;
    private bool _disposed;

    public VkPlaceholderTextures(VkContext vk)
    {
        _vk = vk;
        _white = CreateFlat2D(vk, 255, 255, 255, 255);
        _flatNormal = CreateFlat2D(vk, 128, 128, 255, 255); // tangent-space "no bump" normal
        _black = CreateFlat2D(vk, 0, 0, 0, 255);
        _shadow2D = CreateShadowPlaceholder(vk, cube: false);
        _shadowCube = CreateShadowPlaceholder(vk, cube: true);

        White = _white.DescriptorImageInfo;
        FlatNormal = _flatNormal.DescriptorImageInfo;
        Black = _black.DescriptorImageInfo;
        ShadowMap2D = _shadow2D.DescriptorImageInfo;
        ShadowMapCube = _shadowCube.DescriptorImageInfo;
    }

    private readonly struct Flat2D
    {
        public required Image Image { get; init; }
        public required DeviceMemory Memory { get; init; }
        public required ImageView View { get; init; }
        public required Sampler Sampler { get; init; }

        public DescriptorImageInfo DescriptorImageInfo => new()
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = View,
            Sampler = Sampler
        };
    }

    private readonly struct ShadowPlaceholder
    {
        public required Image Image { get; init; }
        public required DeviceMemory Memory { get; init; }
        public required ImageView View { get; init; }
        public required Sampler Sampler { get; init; }

        // ShaderReadOnlyOptimal, not DepthStencilReadOnlyOptimal -- this image is consumed by a
        // COMBINED_IMAGE_SAMPLER descriptor (sampled in a shader), not bound as a read-only
        // depth attachment in a render pass. DepthStencilReadOnlyOptimal is for the latter;
        // using it here is exactly the kind of layout mismatch that fails silently with no
        // validation layer installed (caught during review, not by a build error).
        public DescriptorImageInfo DescriptorImageInfo => new()
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = View,
            Sampler = Sampler
        };
    }

    private static Flat2D CreateFlat2D(VkContext vk, byte r, byte g, byte b, byte a)
    {
        var api = vk.Api;
        var device = vk.Device;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(1, 1, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        api.CreateImage(device, in imageInfo, null, out var image).ThrowOnError();

        api.GetImageMemoryRequirements(device, image, out var memReq);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(
                api, vk.PhysicalDevice, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        api.AllocateMemory(device, in allocInfo, null, out var memory).ThrowOnError();
        api.BindImageMemory(device, image, memory, 0).ThrowOnError();

        // 4 texels' worth of staging (host-visible) since a single-pixel BufferImageCopy is
        // simplest via a small staging buffer rather than a clear (clears need the image
        // already in TRANSFER_DST_OPTIMAL, which this reaches anyway -- copy is no more code).
        Span<byte> pixel = stackalloc byte[4] { r, g, b, a };
        UploadAndTransition(vk, image, pixel, ImageAspectFlags.ColorBit);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
                ComponentSwizzle.Identity, ComponentSwizzle.Identity),
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        api.CreateImageView(device, in viewInfo, null, out var view).ThrowOnError();

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MinLod = 0,
            MaxLod = 1
        };
        api.CreateSampler(device, in samplerInfo, null, out var sampler).ThrowOnError();

        return new Flat2D { Image = image, Memory = memory, View = view, Sampler = sampler };
    }

    private static void UploadAndTransition(VkContext vk, Image image, ReadOnlySpan<byte> pixelBytes, ImageAspectFlags aspect)
    {
        var api = vk.Api;
        var device = vk.Device;
        ulong size = (ulong)pixelBytes.Length;

        var stagingInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive
        };
        api.CreateBuffer(device, in stagingInfo, null, out var stagingBuffer).ThrowOnError();
        api.GetBufferMemoryRequirements(device, stagingBuffer, out var stagingReq);
        var stagingAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = stagingReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(api, vk.PhysicalDevice,
                stagingReq.MemoryTypeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };
        api.AllocateMemory(device, in stagingAlloc, null, out var stagingMemory).ThrowOnError();
        api.BindBufferMemory(device, stagingBuffer, stagingMemory, 0).ThrowOnError();

        void* mapped = null;
        api.MapMemory(device, stagingMemory, 0, size, 0, ref mapped).ThrowOnError();
        pixelBytes.CopyTo(new Span<byte>(mapped, (int)size));
        api.UnmapMemory(device, stagingMemory);

        try
        {
            var cmd = vk.Pool.CreateCommandBuffer("VkPlaceholderTextures.Upload");
            cmd.BeginRecording();

            var range = new ImageSubresourceRange(aspect, 0, 1, 0, 1);
            Transition(api, cmd.InternalHandle, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, range,
                AccessFlags.None, AccessFlags.TransferWriteBit);

            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(aspect, 0, 0, 1),
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(1, 1, 1)
            };
            api.CmdCopyBufferToImage(cmd.InternalHandle, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, in region);

            // ShaderReadOnlyOptimal regardless of aspect: every caller of this helper is
            // preparing an image for a COMBINED_IMAGE_SAMPLER descriptor, not a render-pass
            // depth attachment -- see the ShadowPlaceholder.DescriptorImageInfo note above for
            // why DepthStencilReadOnlyOptimal would be wrong here.
            Transition(api, cmd.InternalHandle, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, range,
                AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);

            cmd.SubmitAndWait();
        }
        finally
        {
            api.DestroyBuffer(device, stagingBuffer, null);
            api.FreeMemory(device, stagingMemory, null);
        }
    }

    private static void Transition(Vk api, CommandBuffer cmd, Image image, ImageLayout from, ImageLayout to,
        ImageSubresourceRange range, AccessFlags srcAccess, AccessFlags dstAccess)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = from,
            NewLayout = to,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess
        };
        api.CmdPipelineBarrier(cmd, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 1, in barrier);
    }

    /// <summary>D32_SFLOAT is used unconditionally -- it's the depth format the real shadow
    /// passes (plan Section 8c) are expected to use too, and is broadly supported, but this
    /// hasn't been cross-checked with vkGetPhysicalDeviceFormatProperties against
    /// VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT the way a production depth-format
    /// selection helper should. Fine for a 1x1 placeholder on this dev machine; revisit
    /// alongside the real shadow-pass depth format in Section 8c.</summary>
    private const Format ShadowDepthFormat = Format.D32Sfloat;

    private static ShadowPlaceholder CreateShadowPlaceholder(VkContext vk, bool cube)
    {
        var api = vk.Api;
        var device = vk.Device;
        uint layers = cube ? 6u : 1u;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = ShadowDepthFormat,
            Extent = new Extent3D(1, 1, 1),
            MipLevels = 1,
            ArrayLayers = layers,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
            Flags = cube ? ImageCreateFlags.CreateCubeCompatibleBit : ImageCreateFlags.None
        };
        api.CreateImage(device, in imageInfo, null, out var image).ThrowOnError();

        api.GetImageMemoryRequirements(device, image, out var memReq);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(
                api, vk.PhysicalDevice, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        api.AllocateMemory(device, in allocInfo, null, out var memory).ThrowOnError();
        api.BindImageMemory(device, image, memory, 0).ThrowOnError();

        // Depth = 1.0 (far plane) for every layer -- with CompareOp=LessOrEqual below, any
        // real fragment depth compares "not occluded" against this, the correct default for
        // "no shadow caster rendered into this slot."
        UploadDepthAndTransition(vk, image, layers);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = cube ? ImageViewType.TypeCube : ImageViewType.Type2D,
            Format = ShadowDepthFormat,
            Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
                ComponentSwizzle.Identity, ComponentSwizzle.Identity),
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, layers)
        };
        api.CreateImageView(device, in viewInfo, null, out var view).ThrowOnError();

        // CompareEnable is what makes this a depth-COMPARISON sampler, matching
        // sampler2DShadow/samplerCubeShadow's SPIR-V OpTypeImage(depth=1) requirement.
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
        api.CreateSampler(device, in samplerInfo, null, out var sampler).ThrowOnError();

        return new ShadowPlaceholder { Image = image, Memory = memory, View = view, Sampler = sampler };
    }

    /// <summary>Clears a freshly-created depth image to 1.0 (reads as "nothing occluded" against
    /// a LessOrEqual shadow-comparison sampler) and transitions it to ShaderReadOnlyOptimal.
    /// Made internal (not just used by this class's own placeholders) so
    /// VkViewportControl.CreateShadowTarget can call it too -- a real shadow-map image is created
    /// with InitialLayout=Undefined and, if ShadowsEnabled is false, may never have
    /// RenderShadowPass run to transition it via the render pass's own LoadOp=Clear -- without an
    /// explicit initial transition here, its descriptor would claim ShaderReadOnlyOptimal (see
    /// _shadowMapInfo) while the actual image stays Undefined, a real VUID-vkCmdDraw-None-09600
    /// validation error the first time prim.frag samples uShadowMap with shadows disabled --
    /// found via 8c-4's own self-test, not assumed.</summary>
    internal static void UploadDepthAndTransition(VkContext vk, Image image, uint layers)
    {
        var api = vk.Api;
        var device = vk.Device;

        var cmd = vk.Pool.CreateCommandBuffer("VkPlaceholderTextures.UploadDepthAndTransition");
        cmd.BeginRecording();

        var range = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, layers);
        Transition(api, cmd.InternalHandle, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, range,
            AccessFlags.None, AccessFlags.TransferWriteBit);

        var clearValue = new ClearDepthStencilValue(1.0f, 0);
        api.CmdClearDepthStencilImage(cmd.InternalHandle, image, ImageLayout.TransferDstOptimal, in clearValue, 1, in range);

        Transition(api, cmd.InternalHandle, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, range,
            AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);

        cmd.SubmitAndWait();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var api = _vk.Api;
        var device = _vk.Device;
        foreach (var (image, memory, view, sampler) in new[]
                 {
                     (_white.Image, _white.Memory, _white.View, _white.Sampler),
                     (_flatNormal.Image, _flatNormal.Memory, _flatNormal.View, _flatNormal.Sampler),
                     (_black.Image, _black.Memory, _black.View, _black.Sampler),
                     (_shadow2D.Image, _shadow2D.Memory, _shadow2D.View, _shadow2D.Sampler),
                     (_shadowCube.Image, _shadowCube.Memory, _shadowCube.View, _shadowCube.Sampler)
                 })
        {
            api.DestroySampler(device, sampler, null);
            api.DestroyImageView(device, view, null);
            api.DestroyImage(device, image, null);
            api.FreeMemory(device, memory, null);
        }
    }
}
