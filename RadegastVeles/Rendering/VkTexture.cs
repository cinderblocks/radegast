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

// Vulkan port of GlTexture.cs -- see plan Section 6. Structural differences from the GL
// original, all consequences of Vulkan's model:
//   - No glGenerateMipmap equivalent: mip chain is built manually via a vkCmdBlitImage chain
//     (see BuildMipChain below). Verify VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT is
//     advertised for R8G8B8A8_UNORM on target hardware before relying on linear blits --
//     flagged in the plan, not yet checked against real target-hardware diversity beyond
//     this dev machine's NVIDIA GTX 760.
//   - No global texture-unit "Bind()": Vulkan textures are attached to draws via descriptor
//     sets, not a bind-to-unit call. DescriptorImageInfo() replaces Bind(unit) -- the caller
//     (Section 8's per-material descriptor set assembly) writes it into a
//     VkWriteDescriptorSet at whatever binding that material's layout assigns this texture.
//
// Vulkan has no vertical-flip equivalent to GL's texture upload convention: without a
// Vulkan-side flip, VK_sample(u,v) = sourceBitmap(u, v) directly (v=0 -> uploaded-row-0 ->
// bitmap's FIRST row = visual top), whereas GL's own flip makes GL_sample(u,v) =
// sourceBitmap(u, 1-v) (v=0 -> bitmap's LAST row = visual bottom). These are NOT the same
// function unless the bitmap happens to be vertically symmetric, and mesh UV data is generated
// once by backend-agnostic CPU code and fed to whichever backend is active -- so this class
// mirrors GlTexture.Preprocess()'s vertical-flip step to keep both backends sampling
// identically for identical mesh UV data; see the flip loop below, copied from that method.

using System;
using System.Runtime.InteropServices;
using SkiaSharp;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkTexture : IDisposable
{
    private readonly VkContext _vk;
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _view;
    private Sampler _sampler;
    private readonly uint _mipLevels;
    private bool _disposed;

    internal Image Image => _image;
    internal ImageView View => _view;
    internal Sampler Sampler => _sampler;

    /// <summary>
    /// Prepares an <see cref="SKBitmap"/> for Vulkan upload: converts to RGBA8888 AND flips
    /// vertically -- see the class-level correction comment for why the flip is required to
    /// match GL's sampling for the same mesh UV data, not skipped. Safe to call from any
    /// background thread -- does no GPU work. <paramref name="source"/> is disposed; the
    /// returned bitmap is always a new allocation owned by the caller.
    /// </summary>
    public static SKBitmap Preprocess(SKBitmap source)
    {
        SKBitmap rgba;
        if (source.ColorType == SKColorType.Rgba8888)
        {
            rgba = source;
        }
        else if (source.ColorType == SKColorType.Rg88)
        {
            // 2-component J2K (grayscale+alpha) arrives as Rg88: R = luminance, G = alpha.
            // Same remap GlTexture.Preprocess uses: R=G=B=R_src, A=G_src.
            rgba = ConvertRg88ToRgba8888(source);
            source.Dispose();
        }
        else
        {
            var converted = source.Copy(SKColorType.Rgba8888);
            source.Dispose();
            rgba = converted ?? throw new ArgumentException("Cannot convert bitmap to RGBA8888.");
        }

        // Vertical flip, mirroring GlTexture.Preprocess()'s own row-copy logic exactly (bypass
        // the Skia rasterizer entirely -- RGBA8888 pixels are copied verbatim, alpha preserved).
        var flipped = new SKBitmap(rgba.Width, rgba.Height, rgba.ColorType, rgba.AlphaType);
        int rowBytes = rgba.RowBytes;
        int height = rgba.Height;
        var src = rgba.GetPixelSpan();
        var dst = flipped.GetPixelSpan();
        for (int y = 0; y < height; y++)
        {
            src.Slice((height - 1 - y) * rowBytes, rowBytes)
               .CopyTo(dst.Slice(y * rowBytes, rowBytes));
        }
        rgba.Dispose();
        return flipped;
    }

    private static SKBitmap ConvertRg88ToRgba8888(SKBitmap src)
    {
        int w = src.Width, h = src.Height;
        var dst = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var s = src.GetPixels();
        var d = dst.GetPixels();
        var sBytes = (byte*)s.ToPointer();
        var dBytes = (byte*)d.ToPointer();
        for (int y = 0; y < h; y++)
        {
            byte* sRow = sBytes + (nint)y * src.RowBytes;
            byte* dRow = dBytes + (nint)y * dst.RowBytes;
            for (int x = 0; x < w; x++)
            {
                byte luma  = sRow[x * 2];
                byte alpha = sRow[x * 2 + 1];
                dRow[x * 4]     = luma;
                dRow[x * 4 + 1] = luma;
                dRow[x * 4 + 2] = luma;
                dRow[x * 4 + 3] = alpha;
            }
        }
        return dst;
    }

    /// <summary>
    /// Creates a Vulkan texture (image + view + sampler, full mip chain) from a
    /// <em>pre-processed</em> bitmap (RGBA8888, NOT flipped -- see <see cref="Preprocess"/>).
    /// Ownership of <paramref name="bitmap"/> transfers to this constructor; it is disposed
    /// before the constructor returns.
    /// <paramref name="batch"/> (2026-08-13): when non-null, records this texture's base-level
    /// copy + full mip-chain build into the batch's already-open command buffer instead of
    /// doing its own submit+wait -- see <see cref="VkStagedUploadBatch"/>'s own doc comment.
    /// Every recorded command here only ever references THIS texture's own <see cref="_image"/>
    /// handle (no barrier or blit touches another texture's image), so batching many textures'
    /// upload sequences into one command buffer needs no cross-texture ordering care -- unlike
    /// <see cref="VkMesh"/>'s vbo/ebo case there's no risk category here beyond the same staging-
    /// buffer-lifetime rule the batch already documents. Caller owns submitting the batch and
    /// freeing its staging buffers once every texture using it has been constructed; null (the
    /// default) preserves the original one-submit-per-texture behavior unchanged.
    /// </summary>
    public VkTexture(VkContext vk, SKBitmap bitmap, VkStagedUploadBatch? batch = null)
    {
        _vk = vk;
        uint width = (uint)bitmap.Width, height = (uint)bitmap.Height;
        _mipLevels = (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;

        CreateImageAndMemory(Format.R8G8B8A8Unorm, width, height, _mipLevels);
        UploadBaseLevelAndBuildMips(bitmap, width, height, batch);
        CreateViewAndSampler(Format.R8G8B8A8Unorm);

        bitmap.Dispose();
    }

    /// <summary>
    /// Creates a Vulkan texture directly from precomputed BC3 block-compressed mip levels (see
    /// <c>Ktx2Codec.EncodeCompressedBc3</c>/<c>TextureDiskCache.TryGetCompressedPixels</c> --
    /// this is the compressed-tier counterpart to the RGBA8 constructor above). Unlike that
    /// constructor, there is no <see cref="Preprocess"/> step: <paramref name="bc3Levels"/> must
    /// already be in Vulkan upload orientation (already flipped) and already RGBA8888-normalized
    /// before compression -- both are the compressed pixel-cache tier's on-disk contract, not
    /// something this constructor can fix up after the fact (BC3 blocks can't be re-flipped).
    /// <para>
    /// No <see cref="BuildMipChain"/> call: block-compressed formats can't be linearly
    /// blit-filtered the way <c>R8G8B8A8_UNORM</c> can (no
    /// <c>VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT</c> requirement is placed on
    /// compressed formats), so every mip level must arrive precomputed. <paramref name="bc3Levels"/>
    /// must be ordered largest-to-smallest starting at the full-resolution level -- exactly
    /// <see cref="CompressedKtx2.Levels"/>'s own shape.
    /// </para>
    /// Callers must check <see cref="VkContext.SupportsBc3"/> before using this constructor;
    /// it does not check the capability itself.
    /// </summary>
    public VkTexture(VkContext vk, int width, int height, byte[][] bc3Levels, VkStagedUploadBatch? batch = null)
    {
        _vk = vk;
        _mipLevels = (uint)bc3Levels.Length;

        CreateImageAndMemory(Format.BC3UnormBlock, (uint)width, (uint)height, _mipLevels);
        UploadPrecomputedMipLevels(bc3Levels, (uint)width, (uint)height, batch);
        CreateViewAndSampler(Format.BC3UnormBlock);
    }

    private void CreateImageAndMemory(Format format, uint width, uint height, uint mipLevels)
    {
        var api = _vk.Api;
        var device = _vk.Device;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(width, height, 1),
            MipLevels = mipLevels,
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
                api, _vk.PhysicalDevice, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        api.AllocateMemory(device, in allocInfo, null, out _memory).ThrowOnError();
        api.BindImageMemory(device, _image, _memory, 0).ThrowOnError();
    }

    private void CreateViewAndSampler(Format format)
    {
        var api = _vk.Api;
        var device = _vk.Device;

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
                ComponentSwizzle.Identity, ComponentSwizzle.Identity),
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, _mipLevels, 0, 1)
        };
        api.CreateImageView(device, in viewInfo, null, out _view).ThrowOnError();

        // Matches GlTexture's TexParameter calls: LinearMipmapLinear min, Linear mag, Repeat S/T.
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
            MaxLod = _mipLevels,
            BorderColor = BorderColor.IntOpaqueBlack,
        };
        api.CreateSampler(device, in samplerInfo, null, out _sampler).ThrowOnError();
    }

    private void UploadBaseLevelAndBuildMips(SKBitmap bitmap, uint width, uint height, VkStagedUploadBatch? batch)
    {
        var api = _vk.Api;
        var device = _vk.Device;
        ulong size = (ulong)(width * height * 4);

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
        var srcSpan = new ReadOnlySpan<byte>(bitmap.GetPixels().ToPointer(), (int)size);
        srcSpan.CopyTo(new Span<byte>(mapped, (int)size));
        api.UnmapMemory(device, stagingMemory);

        if (batch is { } b)
        {
            // Record into the externally-owned, already-open command buffer -- no submit here,
            // caller does that once for every texture (and mesh, if sharing the same batch)
            // recorded into it. The staging buffer is NOT destroyed (unlike the standalone path
            // below): its memory is this copy's source and the copy hasn't executed yet. It's
            // handed to the batch's own list instead -- caller frees it only after the batch's
            // command buffer has been submitted AND waited on.
            RecordUploadCommands(b.Cmd.InternalHandle, stagingBuffer, width, height);
            b.StagingBuffers.Add((stagingBuffer, stagingMemory));
            return;
        }

        try
        {
            var cmd = _vk.Pool.CreateCommandBuffer("VkTexture.Upload");
            cmd.BeginRecording();
            RecordUploadCommands(cmd.InternalHandle, stagingBuffer, width, height);
            cmd.SubmitAndWait();
        }
        finally
        {
            api.DestroyBuffer(device, stagingBuffer, null);
            api.FreeMemory(device, stagingMemory, null);
        }
    }

    /// <summary>
    /// Uploads every precomputed BC3 mip level in one staging buffer + one set of per-level
    /// <c>BufferImageCopy</c> regions -- the compressed-texture counterpart to
    /// <see cref="UploadBaseLevelAndBuildMips"/>, minus the blit-based mip build (see this
    /// class's other compressed-constructor doc comment for why that step doesn't apply here).
    /// Shares the same batched-vs-standalone submit split as the RGBA8 path.
    /// </summary>
    private void UploadPrecomputedMipLevels(byte[][] bc3Levels, uint width, uint height, VkStagedUploadBatch? batch)
    {
        var api = _vk.Api;
        var device = _vk.Device;

        var levelOffsets = new ulong[bc3Levels.Length];
        ulong totalSize = 0;
        for (int i = 0; i < bc3Levels.Length; i++)
        {
            levelOffsets[i] = totalSize;
            totalSize += (ulong)bc3Levels[i].Length; // already 16-byte block-aligned per level
        }

        var stagingInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = totalSize,
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
        api.MapMemory(device, stagingMemory, 0, totalSize, 0, ref mapped).ThrowOnError();
        for (int i = 0; i < bc3Levels.Length; i++)
        {
            new ReadOnlySpan<byte>(bc3Levels[i])
                .CopyTo(new Span<byte>((byte*)mapped + levelOffsets[i], bc3Levels[i].Length));
        }
        api.UnmapMemory(device, stagingMemory);

        if (batch is { } b)
        {
            RecordPrecomputedUploadCommands(b.Cmd.InternalHandle, stagingBuffer, levelOffsets, width, height);
            b.StagingBuffers.Add((stagingBuffer, stagingMemory));
            return;
        }

        try
        {
            var cmd = _vk.Pool.CreateCommandBuffer("VkTexture.UploadCompressed");
            cmd.BeginRecording();
            RecordPrecomputedUploadCommands(cmd.InternalHandle, stagingBuffer, levelOffsets, width, height);
            cmd.SubmitAndWait();
        }
        finally
        {
            api.DestroyBuffer(device, stagingBuffer, null);
            api.FreeMemory(device, stagingMemory, null);
        }
    }

    /// <summary>Records one UNDEFINED -> TRANSFER_DST_OPTIMAL transition over the whole mip
    /// range, one <c>BufferImageCopy</c> per level (each level's dimensions computed by the
    /// standard halving-with-floor(1) formula -- the same one <see cref="BuildMipChain"/> uses),
    /// then one TRANSFER_DST_OPTIMAL -> SHADER_READ_ONLY_OPTIMAL transition over the whole
    /// range. No per-level barrier pairs and no blit: every level's data already exists in the
    /// staging buffer, unlike the RGBA8 path where only the base level is uploaded and the rest
    /// are generated on the GPU.</summary>
    private void RecordPrecomputedUploadCommands(
        CommandBuffer cmd, Buffer stagingBuffer, ulong[] levelOffsets, uint width, uint height)
    {
        var fullRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, _mipLevels, 0, 1);
        TransitionLayout(cmd, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, fullRange,
            AccessFlags.None, AccessFlags.TransferWriteBit);

        uint levelWidth = width, levelHeight = height;
        for (uint level = 0; level < _mipLevels; level++)
        {
            var region = new BufferImageCopy
            {
                BufferOffset = levelOffsets[level],
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level, 0, 1),
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(levelWidth, levelHeight, 1)
            };
            _vk.Api.CmdCopyBufferToImage(cmd, stagingBuffer, _image, ImageLayout.TransferDstOptimal, 1, in region);

            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
        }

        TransitionLayout(cmd, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, fullRange,
            AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
    }

    /// <summary>Records the base-level UNDEFINED -> TRANSFER_DST_OPTIMAL transition, the staging
    /// buffer -> image copy, and the full mip-chain build -- shared between the standalone
    /// (submit-immediately) and batched (caller submits) upload paths.</summary>
    private void RecordUploadCommands(CommandBuffer cmd, Buffer stagingBuffer, uint width, uint height)
    {
        var toDst = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, _mipLevels, 0, 1);
        TransitionLayout(cmd, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, toDst,
            AccessFlags.None, AccessFlags.TransferWriteBit);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1)
        };
        _vk.Api.CmdCopyBufferToImage(cmd, stagingBuffer, _image, ImageLayout.TransferDstOptimal, 1, in region);

        BuildMipChain(cmd, width, height);
    }

    /// <summary>
    /// Manual mip chain: GL's <c>glGenerateMipmap</c> has no Vulkan equivalent, so each level
    /// is produced by a linear <c>vkCmdBlitImage</c> from the previous one, with the barriers
    /// needed to make each level readable as the next blit's source and finally sampleable by
    /// every level at once. Requires <c>VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT</c>
    /// for R8G8B8A8_UNORM -- see the class-level note; not yet verified across the range of
    /// hardware Veles ships to.
    /// </summary>
    private void BuildMipChain(CommandBuffer cmd, uint width, uint height)
    {
        var api = _vk.Api;
        int mipWidth = (int)width, mipHeight = (int)height;

        for (uint level = 1; level < _mipLevels; level++)
        {
            // Previous level: TRANSFER_DST_OPTIMAL -> TRANSFER_SRC_OPTIMAL (it's about to be
            // read from as this blit's source).
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

            // Previous level done with transfer duty -> SHADER_READ_ONLY_OPTIMAL for sampling.
            TransitionLayout(cmd, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal, prevRange,
                AccessFlags.TransferReadBit, AccessFlags.ShaderReadBit);

            mipWidth = nextWidth;
            mipHeight = nextHeight;
        }

        // Last level never went through the blit-source path above -> straight to SHADER_READ_ONLY_OPTIMAL.
        var lastRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, _mipLevels - 1, 1, 0, 1);
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

    /// <summary>Descriptor info for writing this texture into a per-material descriptor set
    /// (plan Section 5's set 2) -- the Vulkan replacement for <c>GlTexture.Bind(unit)</c>.</summary>
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
