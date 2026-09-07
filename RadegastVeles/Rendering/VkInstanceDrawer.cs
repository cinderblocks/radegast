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

// Vulkan port of GlInstanceDrawer.cs. Structural differences from the
// GL original:
//   - No VAO/VertexAttribDivisor: the instance buffer is vertex input binding 1
//     (VertexInputRate.Instance), declared once as part of pipeline creation via
//     the static description properties below, alongside VkMesh's binding 0
//     (VertexInputRate.Vertex). Both bindings are just bind calls at draw time here.
//   - No separate "DrawElementsInstanced" entry point: Vulkan's vkCmdDrawIndexed always takes
//     an instance count, so a single indexed draw with instanceCount>1 IS the instanced draw.
//   - The streaming per-instance buffer is host-visible + coherent (direct map every call,
//     no staging) matching GL's STREAM_DRAW semantics -- data changes every draw, so a
//     staging round-trip would only add overhead, not correctness.

using System;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkInstanceDrawer : IDisposable
{
    public const int InstanceFloats = 44;
    public const int InstanceStride = InstanceFloats * sizeof(float); // 176 bytes, unchanged from GL

    private readonly VkContext _vk;
    private Buffer _instanceBuffer;
    private DeviceMemory _instanceMemory;
    private int _capacityFloats;
    private bool _disposed;

    public VkInstanceDrawer(VkContext vk) => _vk = vk;

    /// <summary>Vertex input binding for the per-instance stream, binding slot 1
    /// (VkMesh's own vertex data occupies binding 0).</summary>
    public static VertexInputBindingDescription VertexInputBindingDescription => new()
    {
        Binding = 1,
        Stride = InstanceStride,
        InputRate = VertexInputRate.Instance
    };

    /// <summary>Attribute layout matching GL's per-instance locations exactly: aInstMvp
    /// (mat4, locations 3-6, one per column -- SPIR-V has no single "mat4 attribute" the way
    /// a uniform can be, each column is its own vertex-input location, same as GL required),
    /// aInstMv (mat4, 7-10), aInstColor (vec4, 11), aInstMisc (vec4, 12), aInstAlphaMode
    /// (float, 13).</summary>
    public static VertexInputAttributeDescription[] VertexInputAttributeDescriptions =>
    [
        new() { Binding = 1, Location = 3,  Format = Format.R32G32B32A32Sfloat, Offset = 0 },
        new() { Binding = 1, Location = 4,  Format = Format.R32G32B32A32Sfloat, Offset = 16 },
        new() { Binding = 1, Location = 5,  Format = Format.R32G32B32A32Sfloat, Offset = 32 },
        new() { Binding = 1, Location = 6,  Format = Format.R32G32B32A32Sfloat, Offset = 48 },
        new() { Binding = 1, Location = 7,  Format = Format.R32G32B32A32Sfloat, Offset = 64 },
        new() { Binding = 1, Location = 8,  Format = Format.R32G32B32A32Sfloat, Offset = 80 },
        new() { Binding = 1, Location = 9,  Format = Format.R32G32B32A32Sfloat, Offset = 96 },
        new() { Binding = 1, Location = 10, Format = Format.R32G32B32A32Sfloat, Offset = 112 },
        new() { Binding = 1, Location = 11, Format = Format.R32G32B32A32Sfloat, Offset = 128 }, // aInstColor
        new() { Binding = 1, Location = 12, Format = Format.R32G32B32A32Sfloat, Offset = 144 }, // aInstMisc
        new() { Binding = 1, Location = 13, Format = Format.R32Sfloat,          Offset = 160 }, // aInstAlphaMode
    ];

    /// <summary>Uploads per-instance data and records a single indexed instanced draw.
    /// Caller owns command buffer recording/submission.</summary>
    public void DrawInstanced(CommandBuffer cmd, VkMesh mesh, float[] instanceData, int instanceCount)
    {
        if (instanceCount <= 0) return;

        int floatCount = instanceCount * InstanceFloats;
        EnsureCapacity(floatCount);
        VkBufferHelper.UpdateHostVisible(_vk, _instanceMemory, new ReadOnlySpan<float>(instanceData, 0, floatCount));

        var api = _vk.Api;
        var vbo = mesh.Vbo;
        var instBuf = _instanceBuffer;
        var buffers = stackalloc Buffer[2] { vbo, instBuf };
        var offsets = stackalloc ulong[2] { 0, 0 };
        api.CmdBindVertexBuffers(cmd, 0, 2, buffers, offsets);
        api.CmdBindIndexBuffer(cmd, mesh.Ebo, 0, IndexType.Uint16);
        api.CmdDrawIndexed(cmd, (uint)mesh.IndexCount, (uint)instanceCount, 0, 0, 0);
    }

    /// <summary>
    /// Uploads a batch of per-instance blocks (<see cref="InstanceFloats"/> floats each),
    /// covering possibly-different meshes drawn afterward via <see cref="DrawBatchedInstance"/>.
    /// Batching into one write is required, not a micro-optimization: consecutive
    /// <c>vkCmdDrawIndexed</c> calls recorded into the same command buffer don't execute until
    /// the buffer is submitted, so calling <see cref="DrawInstanced"/> (which re-uploads to the
    /// same offset-0 slot on every call) once per face before a shared submit would leave every
    /// draw reading only the LAST face's data once the GPU actually runs them -- this method
    /// exists so the whole batch is written once, then each draw reads its own disjoint slice.
    /// </summary>
    public void UploadInstanceBatch(float[] data, int instanceCount)
    {
        int floatCount = instanceCount * InstanceFloats;
        EnsureCapacity(floatCount);
        VkBufferHelper.UpdateHostVisible(_vk, _instanceMemory, new ReadOnlySpan<float>(data, 0, floatCount));
    }

    /// <summary>Records a bind + indexed draw reading instance slot <paramref name="instanceIndex"/>
    /// from the batch most recently uploaded by <see cref="UploadInstanceBatch"/>. Caller owns
    /// command buffer recording/submission.</summary>
    public void DrawBatchedInstance(CommandBuffer cmd, VkMesh mesh, int instanceIndex)
    {
        var api = _vk.Api;
        var vbo = mesh.Vbo;
        var instBuf = _instanceBuffer;
        var buffers = stackalloc Buffer[2] { vbo, instBuf };
        var offsets = stackalloc ulong[2] { 0, (ulong)(instanceIndex * InstanceStride) };
        api.CmdBindVertexBuffers(cmd, 0, 2, buffers, offsets);
        api.CmdBindIndexBuffer(cmd, mesh.Ebo, 0, IndexType.Uint16);
        api.CmdDrawIndexed(cmd, (uint)mesh.IndexCount, 1, 0, 0, 0);
    }

    /// <summary>Records a bind + indexed draw covering <paramref name="instanceCount"/>
    /// CONSECUTIVE instance slots starting at <paramref name="baseIndex"/>, from the batch most
    /// recently uploaded by <see cref="UploadInstanceBatch"/> -- the real multi-instance path
    /// (<c>instanceCount &gt; 1</c> in a single <c>vkCmdDrawIndexed</c>) <see cref="DrawInstanced"/>
    /// already does against its own offset-0 slot, generalized to read from an arbitrary offset
    /// into the SHARED per-frame batch buffer instead. Valid only when the caller has already
    /// verified all <paramref name="instanceCount"/> slots really do belong to
    /// <paramref name="mesh"/> and share one descriptor-set-2 bind (both bound once for the whole
    /// draw call, not per-instance) -- see <c>VkViewportControl.DrawFaces</c>'s own
    /// same-(Mesh,Material)-run coalescing for the only current caller. Caller owns command
    /// buffer recording/submission.</summary>
    public void DrawBatchedInstances(CommandBuffer cmd, VkMesh mesh, int baseIndex, int instanceCount)
    {
        var api = _vk.Api;
        var vbo = mesh.Vbo;
        var instBuf = _instanceBuffer;
        var buffers = stackalloc Buffer[2] { vbo, instBuf };
        var offsets = stackalloc ulong[2] { 0, (ulong)(baseIndex * InstanceStride) };
        api.CmdBindVertexBuffers(cmd, 0, 2, buffers, offsets);
        api.CmdBindIndexBuffer(cmd, mesh.Ebo, 0, IndexType.Uint16);
        api.CmdDrawIndexed(cmd, (uint)mesh.IndexCount, (uint)instanceCount, 0, 0, 0);
    }

    private void EnsureCapacity(int floatCount)
    {
        if (floatCount <= _capacityFloats && _instanceBuffer.Handle != 0) return;

        // This method must be exception-safe: AllocateHostVisible's CreateBuffer/AllocateMemory
        // both call ThrowOnError(), and AllocateMemory can genuinely throw (e.g. hitting
        // VkPhysicalDeviceLimits.maxMemoryAllocationCount as a streamed scene's face count
        // grows). If the old buffer/memory were destroyed and _capacityFloats raised before the
        // replacement allocation succeeded, a throw would leave _instanceBuffer/_instanceMemory
        // holding stale, already-freed handles while _capacityFloats reflected the new (larger)
        // size -- the next call would then see floatCount <= _capacityFloats, early-return
        // without reallocating, and UpdateHostVisible would map an already-freed VkDeviceMemory.
        // Fixed by clearing the fields to a known-empty state BEFORE allocating the replacement,
        // and allocating into LOCAL variables first so a throw from AllocateHostVisible leaves
        // the fields honestly empty (next call reallocates cleanly) rather than dangling.
        int oldCapacityFloats = _capacityFloats;
        if (_instanceBuffer.Handle != 0)
        {
            _vk.Api.DestroyBuffer(_vk.Device, _instanceBuffer, null);
            _vk.Api.FreeMemory(_vk.Device, _instanceMemory, null);
        }
        _instanceBuffer = default;
        _instanceMemory = default;
        _capacityFloats = 0;

        // Grow with the same headroom-doubling pattern the GL buffer implicitly got from
        // BufferData reallocating only when floatCount > _vboFloatCap -- avoids reallocating
        // on every small instance-count fluctuation frame-to-frame. Doubles off
        // oldCapacityFloats (captured before the fields above were reset to empty) rather than
        // _capacityFloats itself, which would always see 0 here and never actually grow past
        // the requested floatCount, defeating the whole point of headroom and forcing a
        // reallocate on nearly every frame as counts fluctuate.
        int newCapacityFloats = Math.Max(floatCount, Math.Max(1, oldCapacityFloats) * 2);
        var placeholder = new float[newCapacityFloats];
        VkBufferHelper.AllocateHostVisible(_vk, BufferUsageFlags.VertexBufferBit,
            out var newBuffer, out var newMemory, (ReadOnlySpan<float>)placeholder);
        _instanceBuffer = newBuffer;
        _instanceMemory = newMemory;
        _capacityFloats = newCapacityFloats;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_instanceBuffer.Handle != 0)
        {
            _vk.Api.DestroyBuffer(_vk.Device, _instanceBuffer, null);
            _vk.Api.FreeMemory(_vk.Device, _instanceMemory, null);
        }
    }
}
