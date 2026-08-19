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

// VkBufferHelper.cs's own header comment states the deliberate project-wide design -- "no
// VMA/suballocator, one vkAllocateMemory call per resource" -- but a real busy region (~700
// objects / ~4900 faces) hit the device's own maxMemoryAllocationCount=4096 ceiling: every
// VkMaterialDescriptorSet allocated its own dedicated UBO (one vkAllocateMemory each,
// deliberately not deduplicated by material content -- see VkMaterialDescriptorSet.cs's own
// header comment), so material UBOs ALONE exceeded the device's total allocation budget before a
// single mesh vbo/ebo or texture image was even counted. This is a narrow, targeted exception to
// that design rule: one shared host-visible buffer, fixed-stride slots, no allocator abstraction
// -- suballocating VkMesh's per-face vbo/ebo is a separate, harder problem (variable sizes, the
// host-visible direct-map path in UpdateVertices) deliberately left alone here; this pool exists
// because VkMaterialUbo specifically is small, fixed-size, and already update-in-place (see
// VkMaterialDescriptorSet.Update), which is exactly the shape a fixed-stride slot pool wants.

using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

/// <summary>
/// A single host-visible buffer holding up to <see cref="Capacity"/> fixed-stride
/// <see cref="VkMaterialUbo"/> slots, each independently readable via a
/// <see cref="Silk.NET.Vulkan.DescriptorBufferInfo"/> at <c>slot * Stride</c>. Replaces one
/// dedicated <c>vkAllocateMemory</c> call per material with one allocation for the whole pool
/// -- see this file's header comment for why. Not thread-safe: like every other raw Vulkan
/// resource in this codebase, callers are expected to be confined to the render thread.
/// </summary>
internal sealed unsafe class VkMaterialUboPool : IDisposable
{
    public int Capacity { get; }
    public ulong Stride { get; }
    public Buffer Buffer => _buffer;

    private readonly VkContext _vk;
    private readonly DeviceMemory _memory;
    private readonly Buffer _buffer;
    private readonly byte* _mapped;
    private readonly Stack<int> _freeSlots;
    private bool _disposed;

    public VkMaterialUboPool(VkContext vk, int capacity)
    {
        _vk = vk;
        Capacity = capacity;

        // A DescriptorBufferInfo's Offset into a UniformBuffer-typed binding must itself be a
        // multiple of minUniformBufferOffsetAlignment (VUID-VkWriteDescriptorSet-descriptorType-
        // 00330 territory) -- sizeof(VkMaterialUbo)=304 already satisfies std140's own 16-byte
        // rule but not necessarily this stricter, GPU-specific one, so round up to it.
        vk.Api.GetPhysicalDeviceProperties(vk.PhysicalDevice, out var props);
        ulong align = Math.Max(1, props.Limits.MinUniformBufferOffsetAlignment);
        ulong rawSize = (ulong)sizeof(VkMaterialUbo);
        Stride = (rawSize + align - 1) / align * align;

        VkBufferHelper.AllocateEmpty(vk, BufferUsageFlags.UniformBufferBit, Stride * (ulong)capacity,
            out _buffer, out _memory);

        void* mapped = null;
        vk.Api.MapMemory(vk.Device, _memory, 0, Stride * (ulong)capacity, 0, ref mapped).ThrowOnError();
        _mapped = (byte*)mapped;

        // Host-coherent (see VkBufferHelper.AllocateEmpty), so this stays mapped for the pool's
        // whole lifetime -- no per-write Map/Unmap round trip needed for Write()/Rent() below.
        _freeSlots = new Stack<int>(capacity);
        for (int i = capacity - 1; i >= 0; i--) _freeSlots.Push(i);
    }

    /// <summary>Claims a free slot, writes <paramref name="data"/> into it, and returns the slot
    /// index (the caller combines it with <see cref="Stride"/> to build its
    /// <see cref="Silk.NET.Vulkan.DescriptorBufferInfo"/>). Throws if the pool is exhausted --
    /// callers are expected to size <see cref="Capacity"/> against the same DescriptorPool
    /// MaxSets ceiling a material descriptor set also has to fit under, so exhaustion here means
    /// the descriptor-set allocation immediately after would have failed anyway.</summary>
    public int Rent(in VkMaterialUbo data)
    {
        if (!_freeSlots.TryPop(out int slot))
            throw new InvalidOperationException(
                $"[VkMaterialUboPool] exhausted (capacity={Capacity})");
        Write(slot, data);
        return slot;
    }

    public void Write(int slot, in VkMaterialUbo data)
    {
        *(VkMaterialUbo*)(_mapped + (ulong)slot * Stride) = data;
    }

    public void Return(int slot) => _freeSlots.Push(slot);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.Api.UnmapMemory(_vk.Device, _memory);
        _vk.Api.DestroyBuffer(_vk.Device, _buffer, null);
        _vk.Api.FreeMemory(_vk.Device, _memory, null);
    }
}
