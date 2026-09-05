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

// Vulkan port of GlMesh.cs. Structural differences from the GL
// original, all consequences of Vulkan's model rather than style choices:
//   - No VAO equivalent: vertex attribute layout is part of the VkPipeline (see
//     VertexInputBindingDescription/VertexInputAttributeDescriptions below, consumed by
//     pipeline creation in Section 8), not the buffer object. A VkMesh is just two VkBuffers.
//   - Draw()/DrawLines() take an explicit command buffer -- Vulkan has no "immediate" draw
//     call, everything is recorded into a command buffer the caller owns and submits.
//   - STATIC_DRAW/DYNAMIC_DRAW become device-local-via-staging vs. host-visible-direct-map
//     (VkBufferHelper.AllocateDeviceLocal/AllocateHostVisible -- the staging
//     design). The index buffer and line-index buffer are always device-local, matching the
//     GL original's unconditional StaticDraw for both.
//   - Every device-local buffer here (ebo/lebo unconditionally, vbo when dynamic: false) is
//     suballocated from VkContext.MeshBufferPool rather than individually vkAllocateMemory'd --
//     see that pool's own field comment for why (the device's maxMemoryAllocationCount ceiling).
//     Each mesh still owns its own individual VkBuffer object exactly as before; only which
//     VkDeviceMemory backs it, and at what offset, is shared, so this is transparent to
//     Draw()/DrawLines() and every other caller -- see VkBufferSubAllocator's own header comment.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkMesh : IDisposable
{
    private readonly VkContext _vk;
    private readonly bool _dynamic;
    private Buffer _vbo, _ebo, _lebo;
    // _vboMemory backs the vbo ONLY when _dynamic (host-visible direct-map -- UpdateHostVisible
    // needs a raw DeviceMemory to map). ebo is always device-local, and vbo is device-local too
    // when !_dynamic, so both of those instead come from VkContext.MeshBufferPool -- see its own
    // field comment for why (the device's maxMemoryAllocationCount ceiling) -- tracked via
    // _vboAllocation/_eboAllocation/_leboAllocation, each meaningless (default) whenever the
    // corresponding buffer wasn't sourced from that pool (_vboAllocation when _dynamic; every
    // Allocation before BuildLineEbo() has ever run, since lebo is built lazily).
    private DeviceMemory _vboMemory;
    private VkBufferSubAllocator.Allocation _vboAllocation, _eboAllocation, _leboAllocation;
    private int _indexCount;
    private int _lineCount;
    private bool _disposed;

    // Kept in CPU memory so BuildLineEbo() can run without a GPU buffer readback (Vulkan has
    // no ES-style restriction here, but there's no need to round-trip through the GPU either).
    private readonly ushort[] _cpuIndices;

    internal Buffer Vbo => _vbo;
    internal Buffer Ebo => _ebo;
    internal int IndexCount => _indexCount;
    // Lets VkViewportControl's cross-object mesh pool detect a stale dictionary entry (its mesh
    // already fully released, _refCount hit zero via some other object's removal) and silently
    // replace it with a freshly-built one instead of handing out a disposed mesh with dead
    // native handles.
    internal bool IsDisposed => _disposed;
    internal const int VertexStride = 48; // 12 floats x 4 bytes

    /// <summary>Vertex input binding for this mesh's interleaved VBO, binding slot 0.</summary>
    public static VertexInputBindingDescription VertexInputBindingDescription => new()
    {
        Binding = 0,
        Stride = VertexStride,
        InputRate = VertexInputRate.Vertex
    };

    public static VertexInputAttributeDescription[] VertexInputAttributeDescriptions =>
    [
        new() { Binding = 0, Location = 0,  Format = Format.R32G32B32Sfloat,       Offset = 0 },  // aPosition
        new() { Binding = 0, Location = 1,  Format = Format.R32G32B32Sfloat,       Offset = 12 }, // aNormal
        new() { Binding = 0, Location = 2,  Format = Format.R32G32Sfloat,          Offset = 24 }, // aTexCoord
        new() { Binding = 0, Location = 14, Format = Format.R32G32B32A32Sfloat,    Offset = 32 }, // aTangent
    ];

    public VkMesh(VkContext vk, float[] vertices, ushort[] indices, bool dynamic = true, VkStagedUploadBatch? batch = null)
        : this(vk, vertices, vertices.Length, indices, dynamic, batch) { }

    /// <summary>
    /// Creates a mesh using only the first <paramref name="verticesLength"/> floats of
    /// <paramref name="vertices"/>, which may be an oversized ArrayPool-rented buffer.
    /// <paramref name="dynamic"/> selects host-visible-direct-map (fast to update, slower to
    /// sample) vs. device-local-via-staging (opposite tradeoff).
    /// <paramref name="batch"/>: when non-null and <paramref name="dynamic"/> is
    /// false, records this mesh's vbo/ebo staging copies into the batch's already-open command
    /// buffer instead of each doing its own submit+wait -- see
    /// <see cref="VkStagedUploadBatch"/>'s own doc comment. Caller owns submitting the batch and
    /// freeing its staging buffers once every mesh using it has been constructed; null (the
    /// default) preserves the original one-submit-per-buffer behavior unchanged.
    /// </summary>
    public VkMesh(VkContext vk, float[] vertices, int verticesLength, ushort[] indices, bool dynamic = true,
        VkStagedUploadBatch? batch = null)
    {
        _vk = vk;
        _dynamic = dynamic;
        _indexCount = indices.Length;
        _cpuIndices = indices;

        // StorageBufferBit alongside VertexBufferBit unconditionally, not just for skinned/
        // flexi meshes: Vulkan buffer usage flags are fixed at creation and validated against
        // how a buffer is later bound (unlike GL, where glBindBufferBase accepts any buffer
        // object regardless of how it was created -- see VUID-VkWriteDescriptorSet-
        // descriptorType-00331, which fires if an ordinary VkMesh's VBO is bound as a compute
        // shader's output SSBO without this flag). Every VkMesh gets this, not just ones marked
        // dynamic: true, so a mesh doesn't need to "know in advance" it'll later become a
        // compute target -- the flag costs nothing extra on any real driver.
        const BufferUsageFlags vboUsage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.StorageBufferBit;
        var vertSpan = new ReadOnlySpan<float>(vertices, 0, verticesLength);
        if (dynamic)
            VkBufferHelper.AllocateHostVisible(vk, vboUsage, out _vbo, out _vboMemory, vertSpan);
        else if (batch is { } vboBatch)
            VkBufferHelper.RecordDeviceLocalCopySuballocated(vk, vboBatch, vk.MeshBufferPool, vboUsage,
                out _vbo, out _vboAllocation, vertSpan);
        else
            VkBufferHelper.AllocateDeviceLocalSuballocated(vk, vk.MeshBufferPool, vboUsage,
                out _vbo, out _vboAllocation, vertSpan);

        if (!dynamic && batch is { } eboBatch)
            VkBufferHelper.RecordDeviceLocalCopySuballocated(vk, eboBatch, vk.MeshBufferPool, BufferUsageFlags.IndexBufferBit,
                out _ebo, out _eboAllocation, (ReadOnlySpan<ushort>)indices);
        else
            VkBufferHelper.AllocateDeviceLocalSuballocated(vk, vk.MeshBufferPool, BufferUsageFlags.IndexBufferBit,
                out _ebo, out _eboAllocation, (ReadOnlySpan<ushort>)indices);
    }

    /// <summary>Records a bind + indexed draw of the triangle-list index buffer. Caller owns
    /// command buffer recording/submission (see the frame-in-flight lifecycle).</summary>
    public void Draw(CommandBuffer cmd)
    {
        var api = _vk.Api;
        var vbo = _vbo;
        ulong offset = 0;
        api.CmdBindVertexBuffers(cmd, 0, 1, in vbo, in offset);
        api.CmdBindIndexBuffer(cmd, _ebo, 0, IndexType.Uint16);
        api.CmdDrawIndexed(cmd, (uint)_indexCount, 1, 0, 0, 0);
    }

    /// <summary>Replace the vertex buffer contents in-place (e.g. animated LBS update).
    /// <paramref name="verts"/> must have the same length as the original array passed to
    /// the constructor. Fast (direct map) only when this mesh was created with
    /// <c>dynamic: true</c>; on a device-local mesh this still works but re-runs the staging
    /// upload every call -- avoid calling this on a static mesh in a hot path.</summary>
    public void UpdateVertices(float[] verts) => UpdateVertices(verts, verts.Length);

    public void UpdateVertices(float[] verts, int vertsLength)
    {
        var span = new ReadOnlySpan<float>(verts, 0, vertsLength);
        if (_dynamic)
            VkBufferHelper.UpdateHostVisible(_vk, _vboMemory, span);
        else
        {
            // Re-stage: destroy and recreate rather than reuse the old allocation, since the
            // data size is assumed unchanged (same array length every call, matching the GL
            // original's BufferSubData-in-place contract) but this keeps the code path
            // identical to construction rather than adding a second, subtly-different copy path.
            // Confirmed dead in practice as of this writing (every caller of UpdateVertices only
            // ever targets a dynamic: true mesh -- see VkViewportControl's own construction sites,
            // dynamic: face.IsFlexi || subAnimated, exactly the flag this branch's condition
            // excludes), but kept suballocation-correct rather than left as a landmine: destroying
            // _vbo without returning _vboAllocation to VkContext.MeshBufferPool would silently
            // leak that range forever if this branch were ever actually exercised.
            VkBufferHelper.DestroySuballocated(_vk, _vk.MeshBufferPool, _vbo, in _vboAllocation);
            VkBufferHelper.AllocateDeviceLocalSuballocated(_vk, _vk.MeshBufferPool, BufferUsageFlags.VertexBufferBit,
                out _vbo, out _vboAllocation, span);
        }
    }

    /// <summary>Records a bind + indexed draw of the line-list edge buffer (wireframe, built
    /// lazily on first call). GL's ES fallback for GL_LINE polygon mode -- kept in the Vulkan
    /// port too since <c>VK_POLYGON_MODE_LINE</c> is not universally supported either
    /// (requires the <c>fillModeNonSolid</c> feature) and this avoids depending on it.</summary>
    public void DrawLines(CommandBuffer cmd)
    {
        if (_lebo.Handle == 0) BuildLineEbo();
        var api = _vk.Api;
        var vbo = _vbo;
        ulong offset = 0;
        api.CmdBindVertexBuffers(cmd, 0, 1, in vbo, in offset);
        api.CmdBindIndexBuffer(cmd, _lebo, 0, IndexType.Uint16);
        api.CmdDrawIndexed(cmd, (uint)_lineCount, 1, 0, 0, 0);
    }

    private void BuildLineEbo()
    {
        var edges = new HashSet<(ushort, ushort)>();
        var lineList = new List<ushort>();

        for (int i = 0; i < _cpuIndices.Length; i += 3)
        {
            ushort a = _cpuIndices[i], b = _cpuIndices[i + 1], c = _cpuIndices[i + 2];
            AddEdge(edges, lineList, a, b);
            AddEdge(edges, lineList, b, c);
            AddEdge(edges, lineList, c, a);
        }

        _lineCount = lineList.Count;
        VkBufferHelper.AllocateDeviceLocalSuballocated(_vk, _vk.MeshBufferPool, BufferUsageFlags.IndexBufferBit,
            out _lebo, out _leboAllocation, (ReadOnlySpan<ushort>)CollectionsMarshal.AsSpan(lineList));
    }

    private static void AddEdge(HashSet<(ushort, ushort)> edges, List<ushort> list, ushort a, ushort b)
    {
        var key = a < b ? (a, b) : (b, a);
        if (edges.Add(key)) { list.Add(a); list.Add(b); }
    }

    // Supports VkViewportControl's cross-object scene mesh pool (UploadSceneObjectNoRebuild's
    // _sharedMeshPool) -- a mesh shared by faces in two different scene objects must not be
    // freed when the first object is removed while the second is still live. _refCount starts
    // at 1 (the creator's own ownership); AddRef is called once per additional owner the pool
    // hands this same instance to, and Dispose below only frees native resources once every
    // owner has called it. A mesh that never has AddRef called on it (every non-pooled use:
    // AvatarViewer/PrimViewer/HudViewer's single-object path, any dynamic:true scene face)
    // behaves exactly as before -- one Dispose call, immediate free -- since _refCount never
    // leaves 1 until that call.
    private int _refCount = 1;

    /// <summary>Registers an additional owner. Caller must balance this with exactly one more
    /// <see cref="Dispose"/> call. See this class's own <see cref="_refCount"/> field comment.</summary>
    internal void AddRef() => _refCount++;

    public void Dispose()
    {
        if (_disposed) return;
        if (--_refCount > 0) return;
        _disposed = true;
        var api = _vk.Api;
        var device = _vk.Device;
        if (_dynamic)
        {
            api.DestroyBuffer(device, _vbo, null);
            api.FreeMemory(device, _vboMemory, null);
        }
        else
        {
            VkBufferHelper.DestroySuballocated(_vk, _vk.MeshBufferPool, _vbo, in _vboAllocation);
        }
        VkBufferHelper.DestroySuballocated(_vk, _vk.MeshBufferPool, _ebo, in _eboAllocation);
        if (_lebo.Handle != 0)
            VkBufferHelper.DestroySuballocated(_vk, _vk.MeshBufferPool, _lebo, in _leboAllocation);
    }
}
