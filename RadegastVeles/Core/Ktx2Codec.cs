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
using System.IO;
using System.Text;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using SkiaSharp;

namespace Radegast.Veles.Core;

/// <summary>
/// A decoded BC3 (S3TC DXT5) compressed texture: one block-compressed buffer per mip level,
/// level 0 = full resolution, ordered largest-to-smallest (matches Vulkan's own mip-level
/// indexing). Produced by <see cref="Ktx2Codec.TryDecodeCompressed"/>; consumed directly by
/// <c>VkTexture</c>'s compressed-upload constructor -- there is no pixel data to dispose here,
/// unlike the uncompressed tier's <see cref="SKBitmap"/> result.
/// </summary>
internal sealed record CompressedKtx2(int Width, int Height, byte[][] Levels);

/// <summary>
/// Minimal KTX2 container codec for a single, uncompressed R8G8B8A8_UNORM mip level.
/// Backs <see cref="TextureDiskCache"/>'s pixel-cache tier: fully-decoded texture pixels
/// are persisted here so a later cache hit skips the CoreJ2K decode entirely (the CPU cost
/// implicated in past scene-viewer decode-saturation freezes), instead of re-decoding raw
/// J2K bytes on every load.
/// <para>
/// The header, index, and level data written by <see cref="Encode"/> are spec-shaped
/// (correct KTX2 identifier, vkFormat, dimensions, and level index) so third-party KTX2
/// tooling can open these files. The Data Format Descriptor block is written but NOT parsed
/// back by <see cref="TryDecode"/> -- this codec is the only consumer of files it writes, so
/// round-tripping relies on the unambiguous header fields (vkFormat/width/height/level index)
/// plus one custom key-value entry recording source alpha type, not on re-deriving anything
/// from the DFD.
/// </para>
/// <para>
/// No mip levels and no supercompression -- both are legitimate future extensions but add
/// complexity (multi-level index, partial reads for the LOD path) not needed for the base
/// use case of skipping full-resolution decode on a cache hit.
/// </para>
/// </summary>
internal static class Ktx2Codec
{
    private static readonly byte[] Identifier =
    {
        0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A
    };

    private const uint VkFormatR8G8B8A8Unorm = 37;
    private const uint VkFormatBc3UnormBlock = 137; // Silk.NET.Vulkan.Format.BC3UnormBlock
    private const string AlphaPremulKey = "RDGT.alphaPremul";

    /// <summary>
    /// Encodes <paramref name="bitmap"/> (converted to RGBA8888 first if necessary) as a
    /// single-level, uncompressed KTX2 file. Runs synchronously and does not dispose
    /// <paramref name="bitmap"/> -- callers that want the disk write backgrounded should pass
    /// the returned bytes to <see cref="TextureDiskCache.PutPixelsAsync"/>, not defer this
    /// call itself onto another thread (the source bitmap's pixel buffer may be pooled/reused
    /// by the time a deferred encode reads it -- see the ArrayPool-aliasing bug this mirrors
    /// in TextureDiskCache's own doc comments).
    /// </summary>
    public static byte[] Encode(SKBitmap bitmap)
    {
        var (pixels, width, height, alphaPremul) = ExtractTightRgba8(bitmap);
        return BuildFile(width, height, pixels, alphaPremul);
    }

    /// <summary>
    /// Encodes <paramref name="bitmap"/> as a BC3 (S3TC DXT5) block-compressed, full mip-chain
    /// KTX2 file. Unlike <see cref="Encode"/>, this flips the image vertically first to match
    /// <c>VkTexture.Preprocess</c>'s upload convention -- BC3 blocks cannot be flipped after
    /// compression the way whole rows of raw pixels can, so this tier's on-disk contract is
    /// "already in Vulkan upload orientation", not "as CoreJ2K decoded it" like the uncompressed
    /// tier. That distinction is exactly why this uses the <c>.v2.ktx2</c> version tag, not
    /// <c>.v1.ktx2</c> -- the two tiers store pixels in genuinely different orientations and
    /// must never be read as if they were the other.
    /// <para>
    /// No alpha-premultiplication metadata is stored (unlike <see cref="Encode"/>'s KVD entry):
    /// the GPU upload path never distinguishes premul from straight alpha either (it uploads
    /// whatever bytes it's given and samples them back unmodified), so there is nothing to
    /// restore on decode.
    /// </para>
    /// Runs synchronously on the calling thread (BC3 encode is CPU work, unlike BC7 it is fast
    /// enough to run inline) -- callers that want the disk write backgrounded should pass the
    /// returned bytes to <see cref="TextureDiskCache.PutCompressedPixelsAsync"/>.
    /// </summary>
    public static byte[] EncodeCompressedBc3(SKBitmap bitmap)
    {
        var (pixels, width, height, _) = ExtractTightRgba8(bitmap);
        FlipVertically(pixels, width, height);

        var encoder = new BcEncoder(CompressionFormat.Bc3);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        byte[][] levels = encoder.EncodeToRawBytes(pixels, width, height, PixelFormat.Rgba32);

        return BuildCompressedFile(width, height, levels);
    }

    /// <summary>
    /// Converts <paramref name="bitmap"/> to RGBA8888 if necessary and copies its pixels into a
    /// tightly-packed (no row padding) byte buffer, the layout both <see cref="Encode"/> and
    /// <see cref="EncodeCompressedBc3"/> need. Does not dispose <paramref name="bitmap"/>.
    /// </summary>
    private static (byte[] pixels, int width, int height, bool alphaPremul) ExtractTightRgba8(SKBitmap bitmap)
    {
        SKBitmap rgba = bitmap;
        bool ownsRgba = false;
        if (bitmap.ColorType != SKColorType.Rgba8888)
        {
            rgba = bitmap.Copy(SKColorType.Rgba8888)
                ?? throw new ArgumentException("Cannot convert bitmap to RGBA8888 for KTX2 encode.");
            ownsRgba = true;
        }

        try
        {
            int width = rgba.Width, height = rgba.Height;
            int tightRow = width * 4;
            var pixels = new byte[tightRow * height];
            var src = rgba.GetPixelSpan();
            int rowBytes = rgba.RowBytes;
            for (int y = 0; y < height; y++)
                src.Slice(y * rowBytes, tightRow).CopyTo(pixels.AsSpan(y * tightRow, tightRow));

            return (pixels, width, height, rgba.AlphaType == SKAlphaType.Premul);
        }
        finally
        {
            if (ownsRgba) rgba.Dispose();
        }
    }

    /// <summary>
    /// Reverses row order of a tightly-packed RGBA8 buffer in place -- mirrors
    /// <c>VkTexture.Preprocess</c>'s own flip loop exactly (see that method's class-level
    /// comment for why the flip exists at all: Vulkan has no equivalent of GL's flipped texture
    /// upload convention, so mesh UV data generated once by backend-agnostic code needs the
    /// bitmap flipped to sample identically on both backends).
    /// </summary>
    private static void FlipVertically(byte[] pixels, int width, int height)
    {
        int rowBytes = width * 4;
        var row = new byte[rowBytes];
        for (int y = 0; y < height / 2; y++)
        {
            int top = y * rowBytes, bottom = (height - 1 - y) * rowBytes;
            Buffer.BlockCopy(pixels, top, row, 0, rowBytes);
            Buffer.BlockCopy(pixels, bottom, pixels, top, rowBytes);
            Buffer.BlockCopy(row, 0, pixels, bottom, rowBytes);
        }
    }

    /// <summary>
    /// Decodes a KTX2 file previously written by <see cref="Encode"/> back to a freshly
    /// allocated, independently-owned <see cref="SKBitmap"/>. Returns <c>null</c> on any
    /// structural mismatch (bad magic, unexpected format/level/dimension fields, truncated
    /// level data) instead of throwing, so a corrupt or foreign cache entry degrades to a
    /// clean cache miss rather than a crash.
    /// </summary>
    public static SKBitmap? TryDecode(byte[] data)
    {
        try
        {
            if (data.Length < 104) return null;
            for (int i = 0; i < Identifier.Length; i++)
                if (data[i] != Identifier[i]) return null;

            int o = 12;
            uint vkFormat = ReadU32(data, ref o);
            _ = ReadU32(data, ref o); // typeSize
            uint width  = ReadU32(data, ref o);
            uint height = ReadU32(data, ref o);
            uint depth  = ReadU32(data, ref o);
            uint layerCount = ReadU32(data, ref o);
            uint faceCount  = ReadU32(data, ref o);
            uint levelCount = ReadU32(data, ref o);
            uint supercompression = ReadU32(data, ref o);

            if (vkFormat != VkFormatR8G8B8A8Unorm || depth != 0 || layerCount != 0 ||
                faceCount != 1 || levelCount != 1 || supercompression != 0)
                return null;

            _ = ReadU32(data, ref o); // dfdByteOffset
            _ = ReadU32(data, ref o); // dfdByteLength
            uint kvdOffset = ReadU32(data, ref o);
            uint kvdLength = ReadU32(data, ref o);
            _ = ReadU64(data, ref o); // sgdByteOffset
            _ = ReadU64(data, ref o); // sgdByteLength

            ulong levelByteOffset = ReadU64(data, ref o);
            ulong levelByteLength = ReadU64(data, ref o);
            _ = ReadU64(data, ref o); // uncompressedByteLength

            if (width == 0 || height == 0) return null;
            long expected = (long)width * height * 4;
            if (levelByteLength != (ulong)expected) return null;
            if (levelByteOffset + levelByteLength > (ulong)data.Length) return null;

            bool alphaPremul = ReadAlphaPremulFlag(data, kvdOffset, kvdLength);

            var bmp = new SKBitmap((int)width, (int)height, SKColorType.Rgba8888,
                alphaPremul ? SKAlphaType.Premul : SKAlphaType.Unpremul);
            var dst = bmp.GetPixelSpan();
            data.AsSpan((int)levelByteOffset, (int)expected).CopyTo(dst);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes a multi-level BC3 KTX2 file previously written by <see cref="EncodeCompressedBc3"/>.
    /// Returns <c>null</c> on any structural mismatch, same failure contract as
    /// <see cref="TryDecode"/>: a corrupt or foreign entry degrades to a clean cache miss.
    /// </summary>
    public static CompressedKtx2? TryDecodeCompressed(byte[] data)
    {
        try
        {
            if (data.Length < 104) return null;
            for (int i = 0; i < Identifier.Length; i++)
                if (data[i] != Identifier[i]) return null;

            int o = 12;
            uint vkFormat = ReadU32(data, ref o);
            _ = ReadU32(data, ref o); // typeSize
            uint width  = ReadU32(data, ref o);
            uint height = ReadU32(data, ref o);
            uint depth  = ReadU32(data, ref o);
            uint layerCount = ReadU32(data, ref o);
            uint faceCount  = ReadU32(data, ref o);
            uint levelCount = ReadU32(data, ref o);
            uint supercompression = ReadU32(data, ref o);

            if (vkFormat != VkFormatBc3UnormBlock || depth != 0 || layerCount != 0 ||
                faceCount != 1 || levelCount == 0 || supercompression != 0)
                return null;
            if (width == 0 || height == 0) return null;

            _ = ReadU32(data, ref o); // dfdByteOffset
            _ = ReadU32(data, ref o); // dfdByteLength
            _ = ReadU32(data, ref o); // kvdByteOffset
            _ = ReadU32(data, ref o); // kvdByteLength
            _ = ReadU64(data, ref o); // sgdByteOffset
            _ = ReadU64(data, ref o); // sgdByteLength

            var levels = new byte[levelCount][];
            int w = (int)width, h = (int)height;
            for (int level = 0; level < levelCount; level++)
            {
                ulong byteOffset = ReadU64(data, ref o);
                ulong byteLength = ReadU64(data, ref o);
                _ = ReadU64(data, ref o); // uncompressedByteLength (equals byteLength -- no supercompression)

                long expected = BlockCount(w) * (long)BlockCount(h) * 16; // BC3: one 16-byte block per 4x4 texels
                if (byteLength != (ulong)expected) return null;
                if (byteOffset + byteLength > (ulong)data.Length) return null;

                levels[level] = data.AsSpan((int)byteOffset, (int)expected).ToArray();
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }

            return new CompressedKtx2((int)width, (int)height, levels);
        }
        catch
        {
            return null;
        }
    }

    private static int BlockCount(int pixels) => (pixels + 3) / 4;

    private static bool ReadAlphaPremulFlag(byte[] data, uint kvdOffset, uint kvdLength)
    {
        try
        {
            int pos = (int)kvdOffset;
            int end = (int)(kvdOffset + kvdLength);
            while (pos + 4 <= end)
            {
                uint entryLen = BitConverter.ToUInt32(data, pos);
                int entryStart = pos + 4;
                if (entryLen == 0 || entryStart + entryLen > end) break;

                int entryEnd = entryStart + (int)entryLen;
                int keyEnd = entryStart;
                while (keyEnd < entryEnd && data[keyEnd] != 0) keyEnd++;
                if (keyEnd < entryEnd)
                {
                    string key = Encoding.ASCII.GetString(data, entryStart, keyEnd - entryStart);
                    if (key == AlphaPremulKey && keyEnd + 1 < entryEnd)
                        return data[keyEnd + 1] != 0;
                }

                int padded = (4 + (int)entryLen + 3) & ~3;
                pos += padded;
            }
        }
        catch { /* malformed KVD -- fall back to the default below */ }
        return false;
    }

    private static uint ReadU32(byte[] data, ref int offset)
    {
        uint v = BitConverter.ToUInt32(data, offset);
        offset += 4;
        return v;
    }

    private static ulong ReadU64(byte[] data, ref int offset)
    {
        ulong v = BitConverter.ToUInt64(data, offset);
        offset += 8;
        return v;
    }

    private static byte[] BuildFile(int width, int height, byte[] pixels, bool alphaPremul)
    {
        // Basic Data Format Descriptor block: 4x8-bit UNORM RGBA channels, single plane,
        // uncompressed (1x1x1x1 texel block). Spec-shaped for third-party KTX2 tools;
        // TryDecode above does not parse it back (see class doc comment).
        var dfdBlock = new byte[88];
        {
            int w = 0;
            void PutU32(uint v) { BitConverter.GetBytes(v).CopyTo(dfdBlock, w); w += 4; }
            PutU32(0);               // vendorId(17) | descriptorType(15) = 0 (KHR basic format)
            PutU32(2 | (88u << 16)); // versionNumber=2 (1.3) | descriptorBlockSize=88
            dfdBlock[w++] = 1;        // colorModel = KHR_DF_MODEL_RGBSDA
            dfdBlock[w++] = 1;        // colorPrimaries = BT709
            dfdBlock[w++] = 1;        // transferFunction = LINEAR
            dfdBlock[w++] = 0;        // flags = ALPHA_STRAIGHT
            dfdBlock[w++] = 0; dfdBlock[w++] = 0; dfdBlock[w++] = 0; dfdBlock[w++] = 0; // texel block dims (1x1x1x1)
            dfdBlock[w++] = 4; dfdBlock[w++] = 0; dfdBlock[w++] = 0; dfdBlock[w++] = 0; // bytesPlane0..3
            dfdBlock[w++] = 0; dfdBlock[w++] = 0; dfdBlock[w++] = 0; dfdBlock[w++] = 0; // bytesPlane4..7

            void PutSample(int bitOffset, byte channelType)
            {
                uint word0 = (uint)(bitOffset & 0xFFFF) | (7u << 16) | ((uint)channelType << 24);
                PutU32(word0);
                PutU32(0);   // samplePositions (unused, single interleaved plane)
                PutU32(0);   // sampleLower = 0
                PutU32(255); // sampleUpper = 255
            }
            PutSample(0,  0);  // R
            PutSample(8,  1);  // G
            PutSample(16, 2);  // B
            PutSample(24, 15); // A
        }
        uint dfdTotalSize = (uint)(4 + dfdBlock.Length);

        // Key/value data: one custom entry recording source alpha type, so a read-back
        // reconstructs an SKBitmap with the same premultiplication as the original decode.
        var keyBytes = Encoding.ASCII.GetBytes(AlphaPremulKey + "\0");
        var kvEntry = new byte[keyBytes.Length + 1];
        keyBytes.CopyTo(kvEntry, 0);
        kvEntry[^1] = (byte)(alphaPremul ? 1 : 0);
        int kvEntryPadded = (4 + kvEntry.Length + 3) & ~3;
        var kvd = new byte[kvEntryPadded];
        BitConverter.GetBytes((uint)kvEntry.Length).CopyTo(kvd, 0);
        kvEntry.CopyTo(kvd, 4);

        const uint headerAndIndexSize = 80;
        const uint levelIndexSize = 24; // one level
        uint dfdOffset = headerAndIndexSize + levelIndexSize;
        uint dfdLength = dfdTotalSize;
        uint kvdOffset = dfdOffset + dfdLength;
        uint kvdLength = (uint)kvd.Length;
        uint levelDataOffset = kvdOffset + kvdLength;
        uint pad = (8 - (levelDataOffset % 8)) % 8; // 8-byte align the pixel payload
        levelDataOffset += pad;

        using var ms = new MemoryStream();
        using var w2 = new BinaryWriter(ms);

        w2.Write(Identifier);
        w2.Write(VkFormatR8G8B8A8Unorm);
        w2.Write((uint)1); // typeSize: 1 byte per channel component
        w2.Write((uint)width);
        w2.Write((uint)height);
        w2.Write((uint)0); // pixelDepth
        w2.Write((uint)0); // layerCount
        w2.Write((uint)1); // faceCount
        w2.Write((uint)1); // levelCount
        w2.Write((uint)0); // supercompressionScheme

        w2.Write(dfdOffset);
        w2.Write(dfdLength);
        w2.Write(kvdOffset);
        w2.Write(kvdLength);
        w2.Write((ulong)0); // sgdByteOffset
        w2.Write((ulong)0); // sgdByteLength

        w2.Write((ulong)levelDataOffset);
        w2.Write((ulong)pixels.Length);
        w2.Write((ulong)pixels.Length);

        w2.Write(dfdTotalSize);
        w2.Write(dfdBlock);
        w2.Write(kvd);
        if (pad > 0) w2.Write(new byte[pad]);
        w2.Write(pixels);

        return ms.ToArray();
    }

    /// <summary>
    /// Writes a multi-level BC3 KTX2 file. <paramref name="levels"/> must be ordered
    /// largest-to-smallest (index 0 = full resolution), each buffer already block-compressed
    /// (16 bytes per 4x4 texel block) -- exactly the shape <c>BcEncoder.EncodeToRawBytes</c>
    /// returns. No key/value data is written (see <see cref="EncodeCompressedBc3"/> for why
    /// alpha-premultiplication metadata isn't needed here).
    /// </summary>
    private static byte[] BuildCompressedFile(int width, int height, byte[][] levels)
    {
        // Basic Data Format Descriptor block: BC3 (S3TC DXT5), single plane, 4x4x1x1 texel
        // block, 16 bytes/block. Spec-shaped for third-party KTX2 tools; TryDecodeCompressed
        // does not parse it back -- same non-authoritative status as BuildFile's DFD above.
        var dfdBlock = new byte[24];
        {
            int w = 0;
            void PutU32(uint v) { BitConverter.GetBytes(v).CopyTo(dfdBlock, w); w += 4; }
            PutU32(0);               // vendorId(17) | descriptorType(15) = 0 (KHR basic format)
            PutU32(2 | (24u << 16)); // versionNumber=2 (1.3) | descriptorBlockSize=24
            dfdBlock[w++] = 0;        // colorModel -- BC3-specific KHR_DF_MODEL value, not parsed back
            dfdBlock[w++] = 1;        // colorPrimaries = BT709
            dfdBlock[w++] = 1;        // transferFunction = LINEAR
            dfdBlock[w++] = 0;        // flags = ALPHA_STRAIGHT
            dfdBlock[w++] = 3; dfdBlock[w++] = 3; dfdBlock[w++] = 0; dfdBlock[w++] = 0; // texel block dims (4x4x1x1, encoded as size-1)
            dfdBlock[w++] = 16; dfdBlock[w++] = 0; dfdBlock[w++] = 0; dfdBlock[w++] = 0; // bytesPlane0..3 (16 bytes/block)
            // bytesPlane4..7 left zero
        }
        uint dfdTotalSize = (uint)(4 + dfdBlock.Length);

        uint headerAndIndexSize = 80;
        uint levelIndexSize = (uint)(24 * levels.Length);
        uint dfdOffset = headerAndIndexSize + levelIndexSize;
        uint dfdLength = dfdTotalSize;
        // No key/value data -- per spec, kvdByteOffset must be 0 when kvdByteLength is 0.
        uint kvdOffset = 0;
        uint kvdLength = 0;
        uint levelDataStart = dfdOffset + dfdLength;
        uint pad = (16 - (levelDataStart % 16)) % 16; // 16-byte align the first BC3 block
        levelDataStart += pad;

        var levelOffsets = new uint[levels.Length];
        uint cursor = levelDataStart;
        for (int i = 0; i < levels.Length; i++)
        {
            levelOffsets[i] = cursor;
            cursor += (uint)levels[i].Length; // already a multiple of 16 (whole BC3 blocks)
        }

        using var ms = new MemoryStream();
        using var w2 = new BinaryWriter(ms);

        w2.Write(Identifier);
        w2.Write(VkFormatBc3UnormBlock);
        w2.Write((uint)1); // typeSize: compressed formats use 1 per KTX2 convention
        w2.Write((uint)width);
        w2.Write((uint)height);
        w2.Write((uint)0); // pixelDepth
        w2.Write((uint)0); // layerCount
        w2.Write((uint)1); // faceCount
        w2.Write((uint)levels.Length);
        w2.Write((uint)0); // supercompressionScheme

        w2.Write(dfdOffset);
        w2.Write(dfdLength);
        w2.Write(kvdOffset);
        w2.Write(kvdLength);
        w2.Write((ulong)0); // sgdByteOffset
        w2.Write((ulong)0); // sgdByteLength

        for (int i = 0; i < levels.Length; i++)
        {
            w2.Write((ulong)levelOffsets[i]);
            w2.Write((ulong)levels[i].Length);
            w2.Write((ulong)levels[i].Length); // uncompressedByteLength == byteLength, no supercompression
        }

        w2.Write(dfdTotalSize);
        w2.Write(dfdBlock);
        if (pad > 0) w2.Write(new byte[pad]);
        foreach (var level in levels)
            w2.Write(level);

        return ms.ToArray();
    }
}
