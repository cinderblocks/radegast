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

// Targeted exception to VkBufferHelper.cs's own "no VMA/suballocator, one vkAllocateMemory call
// per resource" design rule -- see VkContext.MeshBufferPool's own field comment for why VkMesh's
// per-face vbo/ebo specifically needed one (the device's maxMemoryAllocationCount ceiling, hit
// historically -- see that comment for the log evidence).
//
// Deliberately a general byte-range free-list, not a VkMaterialUboPool-style fixed-stride slot
// pool: unlike a VkMaterialUbo (one fixed 304-byte struct), a mesh's vertex/index buffer size
// varies per-face (vertex count x 48 bytes, index count x 2 bytes), so fixed-size slots would
// either waste a lot of space (every slot sized for the largest mesh ever seen) or need many
// separate size-class pools. A sorted free-list with coalescing-on-return is the standard answer
// for variable-size suballocation and is what this implements, one per (memory type index, since
// different buffer usage combinations can in principle resolve to different Vulkan memory types
// even when both request DeviceLocalBit only).
//
// Important: this pool only ever backs the MEMORY a buffer is bound to (via vkBindBufferMemory's
// own offset parameter) -- it does NOT create or own the VkBuffer objects themselves, and callers
// keep their own individual VkBuffer handle per resource exactly as before (see
// VkBufferHelper.AllocateDeviceLocalSuballocated). VkBuffer objects are cheap, plentiful, and NOT
// subject to maxMemoryAllocationCount; only the vkAllocateMemory calls backing them are. This is
// what makes the fix low-risk: VkMesh.Draw()/DrawLines() and every other caller need zero changes
// -- a suballocated mesh's buffers still bind and draw exactly like a dedicated one, they just
// don't own their own VkDeviceMemory anymore.
//
// Content is assumed write-once for anything rented from this pool -- see VkMesh's own comment on
// why every buffer routed through this pool (ebo unconditionally, vbo only when dynamic: false)
// never has UpdateVertices called on it in practice. A pool entry is safe to reuse for a
// completely different buffer the moment Return() is called; nothing here tracks "was this
// content ever read this frame" the way VkFrameReapRing does, so CALLERS remain responsible for
// deferring Return() until no in-flight command buffer could still be reading the old buffer --
// same discipline VkMesh.Dispose()'s ref-counting already requires of ITS callers today.

using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkBufferSubAllocator : IDisposable
{
    /// <summary>Opaque handle to a rented byte range. Callers store this alongside the VkBuffer
    /// it backs and pass it back to <see cref="Return"/> when that buffer is destroyed.</summary>
    internal readonly struct Allocation
    {
        internal readonly uint MemoryTypeIndex;
        internal readonly int BlockIndex;
        internal readonly ulong Offset;
        internal readonly ulong Size;

        internal Allocation(uint memoryTypeIndex, int blockIndex, ulong offset, ulong size)
        {
            MemoryTypeIndex = memoryTypeIndex;
            BlockIndex = blockIndex;
            Offset = offset;
            Size = size;
        }
    }

    private sealed class Block
    {
        public DeviceMemory Memory;
        public ulong Size;
        // Sorted by Offset, non-overlapping, coalesced on every Return -- see InsertFreeRange.
        public readonly List<(ulong Offset, ulong Size)> FreeRanges = new();
    }

    // One block list per resolved Vulkan memory type index -- see this file's header comment for
    // why two different buffer usages CAN (rarely, in principle) resolve to different types even
    // when both request the same MemoryPropertyFlags.
    private readonly Dictionary<uint, List<Block>> _blocksByType = new();
    private readonly VkContext _vk;
    private readonly MemoryPropertyFlags _properties;
    private readonly ulong _blockSize;
    private bool _disposed;

    /// <param name="blockSize">Size of each underlying vkAllocateMemory call. A single request
    /// larger than this gets its own oversized block (rare -- would mean an unusually large mesh
    /// face) rather than failing.</param>
    public VkBufferSubAllocator(VkContext vk, MemoryPropertyFlags properties, ulong blockSize = 16UL * 1024 * 1024)
    {
        _vk = vk;
        _properties = properties;
        _blockSize = blockSize;
    }

    /// <summary>Rents <paramref name="size"/> bytes (rounded up to satisfy <paramref name="alignment"/>,
    /// per <c>VkMemoryRequirements</c>) from an existing block if one has room, otherwise allocates
    /// a new block (one more vkAllocateMemory call -- rare relative to the number of Rent calls,
    /// since one block hosts many meshes' worth of buffers). <paramref name="memoryTypeBits"/> is
    /// the requesting buffer's own <c>VkMemoryRequirements.MemoryTypeBits</c>, used to resolve
    /// which Vulkan memory type (and therefore which block list) this allocation belongs to.</summary>
    public Allocation Rent(ulong size, ulong alignment, uint memoryTypeBits, out DeviceMemory memory)
    {
        uint typeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(
            _vk.Api, _vk.PhysicalDevice, memoryTypeBits, _properties);

        if (!_blocksByType.TryGetValue(typeIndex, out var blocks))
        {
            blocks = new List<Block>();
            _blocksByType[typeIndex] = blocks;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            if (TryRentFromBlock(blocks[i], size, alignment, out ulong offset))
            {
                memory = blocks[i].Memory;
                return new Allocation(typeIndex, i, offset, size);
            }
        }

        // No existing block had room -- allocate a new one. Oversized single requests get a
        // block sized exactly to them (still usable for smaller allocations later, once whatever
        // rented the oversized chunk returns it).
        ulong newBlockSize = Math.Max(_blockSize, size);
        var newBlock = AllocateBlock(newBlockSize, typeIndex);
        blocks.Add(newBlock);
        int newIndex = blocks.Count - 1;
        if (!TryRentFromBlock(newBlock, size, alignment, out ulong newOffset))
            throw new InvalidOperationException(
                $"[VkBufferSubAllocator] freshly allocated block of {newBlockSize} bytes could not satisfy a {size}-byte request (alignment {alignment}) -- alignment/size math bug.");
        memory = newBlock.Memory;
        return new Allocation(typeIndex, newIndex, newOffset, size);
    }

    /// <summary>Releases a previously-rented range back to its block's free-list, coalescing with
    /// adjacent free ranges. Does NOT free any vkAllocateMemory-backed memory -- blocks live for
    /// this pool's whole lifetime (see this class's own header comment on why: Vulkan gives no way
    /// to "shrink" a VkDeviceMemory, and blocks are few/large relative to individual meshes, so
    /// giving one back is not worth the added bookkeeping of tracking whether a whole block has
    /// gone fully idle).</summary>
    public void Return(in Allocation allocation)
    {
        var blocks = _blocksByType[allocation.MemoryTypeIndex];
        InsertFreeRange(blocks[allocation.BlockIndex], allocation.Offset, allocation.Size);
    }

    private static bool TryRentFromBlock(Block block, ulong size, ulong alignment, out ulong offset)
    {
        for (int i = 0; i < block.FreeRanges.Count; i++)
        {
            var (rangeOffset, rangeSize) = block.FreeRanges[i];
            ulong alignedOffset = AlignUp(rangeOffset, alignment);
            ulong padding = alignedOffset - rangeOffset;
            if (rangeSize < padding + size) continue;

            ulong rangeEnd = rangeOffset + rangeSize;
            ulong consumedEnd = alignedOffset + size;
            block.FreeRanges.RemoveAt(i);
            int insertAt = i;
            if (padding > 0)
            {
                // Leading gap too small to matter for anything but bookkeeping -- kept as its own
                // free range rather than discarded, since a later small request can still use it.
                block.FreeRanges.Insert(insertAt, (rangeOffset, padding));
                insertAt++;
            }
            if (consumedEnd < rangeEnd)
                block.FreeRanges.Insert(insertAt, (consumedEnd, rangeEnd - consumedEnd));

            offset = alignedOffset;
            return true;
        }
        offset = 0;
        return false;
    }

    private static void InsertFreeRange(Block block, ulong offset, ulong size)
    {
        var ranges = block.FreeRanges;
        int i = 0;
        while (i < ranges.Count && ranges[i].Offset < offset) i++;

        ulong newOffset = offset, newSize = size;
        if (i > 0)
        {
            var (prevOffset, prevSize) = ranges[i - 1];
            if (prevOffset + prevSize == newOffset)
            {
                newOffset = prevOffset;
                newSize += prevSize;
                i--;
                ranges.RemoveAt(i);
            }
        }
        if (i < ranges.Count)
        {
            var (nextOffset, nextSize) = ranges[i];
            if (newOffset + newSize == nextOffset)
            {
                newSize += nextSize;
                ranges.RemoveAt(i);
            }
        }
        ranges.Insert(i, (newOffset, newSize));
    }

    private static ulong AlignUp(ulong value, ulong alignment) =>
        alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;

    private Block AllocateBlock(ulong size, uint memoryTypeIndex)
    {
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryTypeIndex
        };
        _vk.Api.AllocateMemory(_vk.Device, in allocInfo, null, out var memory).ThrowOnError();
        var block = new Block { Memory = memory, Size = size };
        block.FreeRanges.Add((0, size));
        return block;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var blocks in _blocksByType.Values)
            foreach (var block in blocks)
                _vk.Api.FreeMemory(_vk.Device, block.Memory, null);
        _blocksByType.Clear();
    }
}
