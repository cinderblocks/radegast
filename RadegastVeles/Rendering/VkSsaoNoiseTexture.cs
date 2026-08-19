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

// Builds a tiny 4x4 tiling random-rotation texture (RG8) used to break up SSAO banding
// artefacts. Small and self-contained like
// VkCloudNoiseTexture.cs, but simpler: no mip chain at all (a 4x4 texture has nothing
// meaningful to downsample to), Nearest filtering (matches GL exactly -- this is a per-pixel
// rotation lookup, not something that should blend across texels).

using System;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkSsaoNoiseTexture : IDisposable
{
    private const int Size = 4;

    private readonly VkContext _vk;
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _view;
    private Sampler _sampler;
    private bool _disposed;

    public VkSsaoNoiseTexture(VkContext vk)
    {
        _vk = vk;
        var api = vk.Api;
        var device = vk.Device;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8Unorm,
            Extent = new Extent3D(Size, Size, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
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

        UploadData(BuildNoiseData());

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8Unorm,
            Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
                ComponentSwizzle.Zero, ComponentSwizzle.One),
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        api.CreateImageView(device, in viewInfo, null, out _view).ThrowOnError();

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
            MaxLod = 0,
            BorderColor = BorderColor.IntOpaqueBlack
        };
        api.CreateSampler(device, in samplerInfo, null, out _sampler).ThrowOnError();
    }

    /// <summary>Pixel generation: each texel stores a random 2D unit rotation vector,
    /// packed [-1,1] -> [0,1] for RG8 storage.</summary>
    private static byte[] BuildNoiseData()
    {
        var rng = new Random(7);
        var data = new byte[Size * Size * 2];
        for (int i = 0; i < Size * Size; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
            data[i * 2 + 0] = (byte)((MathF.Cos(angle) * 0.5f + 0.5f) * 255f);
            data[i * 2 + 1] = (byte)((MathF.Sin(angle) * 0.5f + 0.5f) * 255f);
        }
        return data;
    }

    private void UploadData(byte[] data)
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
            var cmd = _vk.Pool.CreateCommandBuffer("VkSsaoNoiseTexture");
            cmd.BeginRecording();

            var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
            var toDst = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _image,
                SubresourceRange = range,
                SrcAccessMask = AccessFlags.None,
                DstAccessMask = AccessFlags.TransferWriteBit
            };
            api.CmdPipelineBarrier(cmd.InternalHandle, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
                0, 0, null, 0, null, 1, in toDst);

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

            var toRead = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.TransferDstOptimal,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _image,
                SubresourceRange = range,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            };
            api.CmdPipelineBarrier(cmd.InternalHandle, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
                0, 0, null, 0, null, 1, in toRead);

            cmd.SubmitAndWait();
        }
        finally
        {
            api.DestroyBuffer(device, stagingBuffer, null);
            api.FreeMemory(device, stagingMemory, null);
        }
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
