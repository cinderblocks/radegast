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

// Loosely adapted from Avalonia's samples/GpuInterop/VulkanDemo's VulkanBufferHelper.cs
// (MIT licensed) -- extended with a device-local + staging-buffer path, which that sample
// didn't need. No VMA/suballocator by design: one vkAllocateMemory call per resource,
// matching the sample's own approach.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Bundles an already-open, externally-owned command buffer with the list of staging buffers
/// recorded into it so far -- see <see cref="VkBufferHelper.RecordDeviceLocalCopy{T}"/>. Exists
/// so a caller uploading many device-local buffers at once (e.g. every static face's vbo+ebo in
/// one scene-object build) can batch them into ONE command-buffer submission/fence wait instead
/// of one per buffer -- <see cref="VkBufferHelper.AllocateDeviceLocal{T}"/>'s own per-call
/// submit+wait is fine in isolation, but for a large linkset with dozens of static faces, two
/// synchronous GPU round-trips per face (vbo+ebo) becomes the dominant upload cost, on the
/// order of 500-1000ms+.
/// </summary>
internal readonly struct VkStagedUploadBatch
{
    public readonly VkCommandBufferPool.VkCommandBuffer Cmd;
    public readonly List<(Buffer Buffer, DeviceMemory Memory)> StagingBuffers;

    public VkStagedUploadBatch(VkCommandBufferPool.VkCommandBuffer cmd, List<(Buffer, DeviceMemory)> stagingBuffers)
    {
        Cmd = cmd;
        StagingBuffers = stagingBuffers;
    }
}

internal static unsafe class VkBufferHelper
{
    /// <summary>
    /// Allocates a host-visible + host-coherent buffer and copies <paramref name="initialData"/>
    /// into it directly (no staging buffer). Use for buffers that are updated frequently from
    /// the CPU (e.g. GPU-skinned/flexi-deformed meshes' vertex buffers, matching <c>GlMesh</c>'s
    /// <c>dynamic: true</c> path) -- slower to sample from than device-local memory, but avoids
    /// a staging round-trip on every update.
    /// </summary>
    public static void AllocateHostVisible<T>(VkContext vk, BufferUsageFlags usage,
        out Buffer buffer, out DeviceMemory memory, ReadOnlySpan<T> initialData) where T : unmanaged
    {
        var size = (ulong)(Unsafe.SizeOf<T>() * initialData.Length);
        CreateBuffer(vk, size, usage, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out buffer, out memory);
        UpdateHostVisible(vk, memory, initialData);
    }

    /// <summary>Overwrites a host-visible buffer's contents in place (the Vulkan equivalent of
    /// <c>GlMesh.UpdateVertices</c>/<c>glBufferSubData</c> for a <c>DYNAMIC_DRAW</c> buffer).</summary>
    public static void UpdateHostVisible<T>(VkContext vk, DeviceMemory memory, ReadOnlySpan<T> data) where T : unmanaged
    {
        var size = (ulong)(Unsafe.SizeOf<T>() * data.Length);
        void* pointer = null;
        vk.Api.MapMemory(vk.Device, memory, 0, size, 0, ref pointer).ThrowOnError();
        data.CopyTo(new Span<T>(pointer, data.Length));
        vk.Api.UnmapMemory(vk.Device, memory);
    }

    /// <summary>
    /// Allocates a host-visible + host-coherent buffer with NO initial data -- the opposite
    /// direction from <see cref="AllocateHostVisible{T}"/>: a destination the GPU writes into
    /// (e.g. <c>vkCmdCopyImageToBuffer</c> for a picking readback), not one the CPU seeds.
    /// Every other allocator here is upload-oriented (CPU -> GPU with initial data); this is
    /// the missing "empty, GPU writes, CPU later reads" shape.
    /// </summary>
    public static void AllocateEmpty(VkContext vk, BufferUsageFlags usage, ulong sizeBytes,
        out Buffer buffer, out DeviceMemory memory)
    {
        CreateBuffer(vk, sizeBytes, usage, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out buffer, out memory);
    }

    /// <summary>
    /// Allocates a device-local buffer and populates it via a staging buffer + one-off command
    /// buffer copy, matching <c>GlMesh</c>'s <c>dynamic: false</c> (<c>STATIC_DRAW</c>) path and
    /// the always-static index buffer. Device-local memory is faster to sample from during
    /// rendering; the staging round-trip only happens once here (this is upload, not a
    /// per-frame update path -- a dedicated transfer queue isn't worth the added complexity
    /// for a one-time copy, so this reuses the single graphics+transfer queue).
    /// </summary>
    public static void AllocateDeviceLocal<T>(VkContext vk, BufferUsageFlags usage,
        out Buffer buffer, out DeviceMemory memory, ReadOnlySpan<T> initialData) where T : unmanaged
    {
        var size = (ulong)(Unsafe.SizeOf<T>() * initialData.Length);

        CreateBuffer(vk, size, usage | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit,
            out buffer, out memory);

        CreateBuffer(vk, size, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out var stagingBuffer, out var stagingMemory);
        try
        {
            UpdateHostVisible(vk, stagingMemory, initialData);

            var cmd = vk.Pool.CreateCommandBuffer("VkBufferHelper.AllocateDeviceLocal");
            cmd.BeginRecording();
            var copyRegion = new BufferCopy { SrcOffset = 0, DstOffset = 0, Size = size };
            vk.Api.CmdCopyBuffer(cmd.InternalHandle, stagingBuffer, buffer, 1, in copyRegion);
            // Synchronous: waits on the copy's fence before returning, matching GlMesh's own
            // synchronous glBufferData semantics -- the caller can use `buffer` immediately
            // after this method returns. Revisit only if profiling shows upload-stall pressure.
            // SubmitAndWait waits only on this buffer's own fence, not every other buffer
            // outstanding in the shared pool.
            cmd.SubmitAndWait();
        }
        finally
        {
            vk.Api.DestroyBuffer(vk.Device, stagingBuffer, null);
            vk.Api.FreeMemory(vk.Device, stagingMemory, null);
        }
    }

    /// <summary>
    /// Same shape and result as <see cref="AllocateDeviceLocal{T}"/> (device-local destination
    /// buffer, filled via a staging buffer + <c>CmdCopyBuffer</c>), but records the copy into
    /// <paramref name="batch"/>'s already-open command buffer instead of opening, submitting,
    /// and waiting on its own -- see <see cref="VkStagedUploadBatch"/>'s own doc comment for why.
    /// <para>
    /// The staging buffer created here is NOT destroyed before returning (unlike
    /// <see cref="AllocateDeviceLocal{T}"/>'s own <c>finally</c> block) -- its memory is the
    /// copy's source, and the copy command hasn't executed yet. It's appended to
    /// <paramref name="batch"/>'s <see cref="VkStagedUploadBatch.StagingBuffers"/> list instead;
    /// the CALLER is responsible for keeping every entry in that list alive until AFTER
    /// <see cref="VkStagedUploadBatch.Cmd"/>'s <c>Submit()</c> has returned (its fence signaled),
    /// then freeing them all -- freeing early risks the copy reading disposed/reused memory,
    /// which reads as intermittent corrupted geometry or a device-lost, not a clean failure.
    /// </para>
    /// </summary>
    public static void RecordDeviceLocalCopy<T>(VkContext vk, VkStagedUploadBatch batch, BufferUsageFlags usage,
        out Buffer buffer, out DeviceMemory memory, ReadOnlySpan<T> initialData) where T : unmanaged
    {
        var size = (ulong)(Unsafe.SizeOf<T>() * initialData.Length);

        CreateBuffer(vk, size, usage | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit,
            out buffer, out memory);

        CreateBuffer(vk, size, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out var stagingBuffer, out var stagingMemory);
        UpdateHostVisible(vk, stagingMemory, initialData);

        var copyRegion = new BufferCopy { SrcOffset = 0, DstOffset = 0, Size = size };
        vk.Api.CmdCopyBuffer(batch.Cmd.InternalHandle, stagingBuffer, buffer, 1, in copyRegion);

        batch.StagingBuffers.Add((stagingBuffer, stagingMemory));
    }

    /// <summary>Destroys every staging buffer recorded via <see cref="RecordDeviceLocalCopy{T}"/>
    /// into one batch. Call only after that batch's command buffer has been submitted AND its
    /// fence waited on (i.e. after <see cref="VkCommandBufferPool.FreeUsedCommandBuffers"/>, not
    /// merely after <c>Submit()</c> returns -- <c>Submit()</c> itself does not block).</summary>
    public static void FreeBatchStagingBuffers(VkContext vk, VkStagedUploadBatch batch)
    {
        foreach (var (buf, mem) in batch.StagingBuffers)
        {
            vk.Api.DestroyBuffer(vk.Device, buf, null);
            vk.Api.FreeMemory(vk.Device, mem, null);
        }
        batch.StagingBuffers.Clear();
    }

    private static void CreateBuffer(VkContext vk, ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties,
        out Buffer buffer, out DeviceMemory memory)
    {
        var api = vk.Api;
        var device = vk.Device;

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };
        api.CreateBuffer(device, in bufferInfo, null, out buffer).ThrowOnError();

        api.GetBufferMemoryRequirements(device, buffer, out var memoryRequirements);
        var memoryAllocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(
                api, vk.PhysicalDevice, memoryRequirements.MemoryTypeBits, properties)
        };
        api.AllocateMemory(device, in memoryAllocateInfo, null, out memory).ThrowOnError();
        api.BindBufferMemory(device, buffer, memory, 0).ThrowOnError();
    }
}
