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

// Builds the tiling single-channel cloud-density noise texture sky.frag samples.
// NOT built on VkTexture (that class is hardcoded to RGBA8888/SKBitmap input, per its own
// class-level note); this is its own small, self-contained class since the pixel-generation
// math, format (R8 vs RGBA8), and mip-count policy (4 explicit levels, not a full log2 chain
// -- see below) all genuinely differ, and nothing else in this port needs a single-channel
// procedural texture.
//
// The noise-generation algorithm (tileable value noise, 6 octaves, smoothstep-interpolated
// lattice) is pure C# math with no GPU dependency.

using System;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkCloudNoiseTexture : IDisposable
{
    private const int Size = 256;
    // Matches GL's TextureMaxLod=3.0: the sampler never samples past mip 3, so only 4 physical
    // levels need to exist (256 -> 128 -> 64 -> 32) rather than a full log2(256)+1=9-level chain
    // down to 1x1 -- same sampled result, less mip-chain work to build.
    private const uint MipLevels = 4;

    private readonly VkContext _vk;
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _view;
    private Sampler _sampler;
    private bool _disposed;

    public VkCloudNoiseTexture(VkContext vk)
    {
        _vk = vk;
        var api = vk.Api;
        var device = vk.Device;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8Unorm,
            Extent = new Extent3D(Size, Size, 1),
            MipLevels = MipLevels,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        api.CreateImage(device, in imageInfo, null, out _image).ThrowOnError();

        api.GetImageMemoryRequirements(device, _image, out var memReq);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(
                api, vk.PhysicalDevice, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        api.AllocateMemory(device, in allocInfo, null, out _memory).ThrowOnError();
        api.BindImageMemory(device, _image, _memory, 0).ThrowOnError();

        UploadBaseLevelAndBuildMips(BuildNoiseData());

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8Unorm,
            Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
                ComponentSwizzle.Identity, ComponentSwizzle.One),
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, MipLevels, 0, 1)
        };
        api.CreateImageView(device, in viewInfo, null, out _view).ThrowOnError();

        // Matches GL's TexParameter calls exactly: LinearMipmapLinear min, Linear mag, Repeat
        // S/T, MaxLod=3.0 (see the class-level note on why only 4 physical levels are built).
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MinLod = 0,
            MaxLod = 3f,
            BorderColor = BorderColor.IntOpaqueBlack
        };
        api.CreateSampler(device, in samplerInfo, null, out _sampler).ThrowOnError();
    }

    /// <summary>CPU-side pixel generation: 6-octave tileable value noise,
    /// smoothstep-interpolated between wrapping lattice points, normalized by accumulated
    /// amplitude.</summary>
    private static byte[] BuildNoiseData()
    {
        const int octaves = 6;
        var rng = new Random(1337);

        var accum = new float[Size * Size];
        float amp = 0.5f;
        float ampSum = 0f;
        for (int o = 0; o < octaves; o++)
        {
            int lattice = 4 << o; // 4 .. 128 -- all divide 256 evenly for a clean seam
            var grid = new float[lattice, lattice];
            for (int y = 0; y < lattice; y++)
                for (int x = 0; x < lattice; x++)
                    grid[y, x] = (float)rng.NextDouble();

            for (int y = 0; y < Size; y++)
            {
                float gy = (float)y / Size * lattice;
                int y0 = (int)gy % lattice, y1 = (y0 + 1) % lattice;
                float fy = gy - MathF.Floor(gy);
                fy = fy * fy * (3f - 2f * fy); // smoothstep

                for (int x = 0; x < Size; x++)
                {
                    float gx = (float)x / Size * lattice;
                    int x0 = (int)gx % lattice, x1 = (x0 + 1) % lattice;
                    float fx = gx - MathF.Floor(gx);
                    fx = fx * fx * (3f - 2f * fx);

                    float v00 = grid[y0, x0], v10 = grid[y0, x1];
                    float v01 = grid[y1, x0], v11 = grid[y1, x1];
                    float vx0 = v00 + (v10 - v00) * fx;
                    float vx1 = v01 + (v11 - v01) * fx;
                    accum[y * Size + x] += (vx0 + (vx1 - vx0) * fy) * amp;
                }
            }
            ampSum += amp;
            amp *= 0.5f;
        }

        var data = new byte[Size * Size];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(Math.Clamp(accum[i] / ampSum, 0f, 1f) * 255f);
        return data;
    }

    private void UploadBaseLevelAndBuildMips(byte[] data)
    {
        var api = _vk.Api;
        var device = _vk.Device;
        ulong size = (ulong)data.Length;

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
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(api, _vk.PhysicalDevice,
                stagingReq.MemoryTypeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };
        api.AllocateMemory(device, in stagingAlloc, null, out var stagingMemory).ThrowOnError();
        api.BindBufferMemory(device, stagingBuffer, stagingMemory, 0).ThrowOnError();

        void* mapped = null;
        api.MapMemory(device, stagingMemory, 0, size, 0, ref mapped).ThrowOnError();
        fixed (byte* src = data)
            new ReadOnlySpan<byte>(src, (int)size).CopyTo(new Span<byte>(mapped, (int)size));
        api.UnmapMemory(device, stagingMemory);

        try
        {
            var cmd = _vk.Pool.CreateCommandBuffer("VkCloudNoiseTexture");
            cmd.BeginRecording();

            var toDst = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, MipLevels, 0, 1);
            TransitionLayout(cmd.InternalHandle, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, toDst,
                AccessFlags.None, AccessFlags.TransferWriteBit);

            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(Size, Size, 1)
            };
            api.CmdCopyBufferToImage(cmd.InternalHandle, stagingBuffer, _image, ImageLayout.TransferDstOptimal, 1, in region);

            BuildMipChain(cmd.InternalHandle);

            cmd.SubmitAndWait();
        }
        finally
        {
            api.DestroyBuffer(device, stagingBuffer, null);
            api.FreeMemory(device, stagingMemory, null);
        }
    }

    /// <summary>Manual mip chain, same technique as VkTexture.BuildMipChain (GL's
    /// glGenerateMipmap has no Vulkan equivalent) -- see that method's own doc comment for the
    /// per-level barrier shape. Only 4 levels here (not a full log2 chain), matching this
    /// class's MipLevels constant.</summary>
    private void BuildMipChain(CommandBuffer cmd)
    {
        var api = _vk.Api;
        int mipWidth = Size, mipHeight = Size;

        for (uint level = 1; level < MipLevels; level++)
        {
            var prevRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, level - 1, 1, 0, 1);
            TransitionLayout(cmd, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal, prevRange,
                AccessFlags.TransferWriteBit, AccessFlags.TransferReadBit);

            int nextWidth = Math.Max(1, mipWidth / 2);
            int nextHeight = Math.Max(1, mipHeight / 2);

            var blit = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level - 1, 0, 1),
                SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                {
                    Element0 = new Offset3D(0, 0, 0),
                    Element1 = new Offset3D(mipWidth, mipHeight, 1)
                },
                DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level, 0, 1),
                DstOffsets = new ImageBlit.DstOffsetsBuffer
                {
                    Element0 = new Offset3D(0, 0, 0),
                    Element1 = new Offset3D(nextWidth, nextHeight, 1)
                }
            };
            api.CmdBlitImage(cmd, _image, ImageLayout.TransferSrcOptimal, _image, ImageLayout.TransferDstOptimal,
                1, in blit, Filter.Linear);

            TransitionLayout(cmd, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal, prevRange,
                AccessFlags.TransferReadBit, AccessFlags.ShaderReadBit);

            mipWidth = nextWidth;
            mipHeight = nextHeight;
        }

        var lastRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, MipLevels - 1, 1, 0, 1);
        TransitionLayout(cmd, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, lastRange,
            AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
    }

    private void TransitionLayout(CommandBuffer cmd, ImageLayout from, ImageLayout to, ImageSubresourceRange range,
        AccessFlags srcAccess, AccessFlags dstAccess)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = from,
            NewLayout = to,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _image,
            SubresourceRange = range,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess
        };
        _vk.Api.CmdPipelineBarrier(cmd, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 1, in barrier);
    }

    public DescriptorImageInfo DescriptorImageInfo => new()
    {
        ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        ImageView = _view,
        Sampler = _sampler
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var api = _vk.Api;
        var device = _vk.Device;
        api.DestroySampler(device, _sampler, null);
        api.DestroyImageView(device, _view, null);
        api.DestroyImage(device, _image, null);
        api.FreeMemory(device, _memory, null);
    }
}
