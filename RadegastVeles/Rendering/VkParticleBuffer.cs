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

// Vulkan port of GlParticleBuffer.cs. The CPU billboard-quad expansion math (ExpandToVertices)
// is copied verbatim from GlParticleBuffer.Upload -- pure C#/System.Numerics, no GL dependency,
// so there was never a reason to re-derive it.
//
// Real structural difference from GL, a consequence of the Vulkan command-buffer-recording model
// rather than a style choice: GL's DrawParticles does `foreach emitter { Upload(); Draw(); }`,
// reusing ONE buffer/VAO across every emitter -- safe there because GL's calls execute against
// the driver's internal stream in issue order (upload N, draw N, upload N+1, draw N+1, ...).
// Vulkan command-buffer recording defers everything to submission time, so the same per-emitter
// upload-then-draw loop into one buffer would leave every recorded draw call reading whatever the
// LAST emitter's upload left in that buffer once the GPU actually executes them -- the same
// buffer-clobber-across-draws hazard VkInstanceDrawer's batched instance draws also have to
// avoid. This class's own `Upload` therefore takes the FULL combined vertex array for every live
// emitter this frame (built by VkViewportControl via repeated ExpandToVertices calls into one
// shared array), uploaded ONCE; each emitter's draw then uses vkCmdDraw's own `firstVertex`
// parameter to read its own disjoint slice of the single bound buffer -- simpler than
// VkInstanceDrawer's per-draw buffer-offset rebind, since particles are literal per-vertex data
// (not per-instance), so no second vertex-input binding is needed at all.

using System;
using System.Numerics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkParticleBuffer : IDisposable
{
    // 6 vertices per particle, 9 floats per vertex (stride 36 bytes) -- matches
    // GlParticleBuffer's own constants and VkParticlePipeline's vertex-input binding exactly.
    public const int FloatsPerVertex = 9;
    public const int VerticesPerParticle = 6;

    private readonly VkContext _vk;
    private Buffer _vbo;
    private DeviceMemory _memory;
    private int _capacityFloats;
    private bool _disposed;

    public Buffer Vbo => _vbo;

    public VkParticleBuffer(VkContext vk) => _vk = vk;

    /// <summary>
    /// Expands one emitter's particles into camera-facing billboard-quad vertices, writing them
    /// into <paramref name="dest"/> starting at <paramref name="destVertexOffset"/> VERTICES
    /// (not floats). Pure CPU math, no GPU calls -- caller is responsible for sizing/growing
    /// <paramref name="dest"/> before calling (mirrors GL's own per-tick recompute; this is not
    /// cached across frames any more than GL caches it). Returns the vertex count written
    /// (<c>particles.Length * VerticesPerParticle</c>), for the caller to track this emitter's
    /// <c>firstVertex</c> offset for its own draw call.
    /// </summary>
    public static int ExpandToVertices(ReadOnlySpan<ParticleVertex> particles, Vector3 cameraRight, Vector3 cameraUp,
        float[] dest, int destVertexOffset)
    {
        if (particles.Length == 0) return 0;

        int idx = destVertexOffset * FloatsPerVertex;

        // Pre-compute UV corners -- identical order/values to GlParticleBuffer.Upload.
        ReadOnlySpan<Vector2> uvs = stackalloc Vector2[]
        {
            new(0f, 1f), // bottom-left
            new(1f, 1f), // bottom-right
            new(1f, 0f), // top-right
            new(0f, 1f), // bottom-left  (second tri)
            new(1f, 0f), // top-right
            new(0f, 0f), // top-left
        };

        foreach (var p in particles)
        {
            var r = cameraRight * p.HalfW;
            var u = cameraUp    * p.HalfH;

            // Four corners of the billboard quad.
            var c0 = p.Position - r - u; // BL
            var c1 = p.Position + r - u; // BR
            var c2 = p.Position + r + u; // TR
            var c3 = p.Position - r + u; // TL

            void WriteVertex(Vector3 c, Vector2 uv)
            {
                dest[idx++] = c.X; dest[idx++] = c.Y; dest[idx++] = c.Z;
                dest[idx++] = uv.X; dest[idx++] = uv.Y;
                dest[idx++] = p.Color.X; dest[idx++] = p.Color.Y; dest[idx++] = p.Color.Z; dest[idx++] = p.Color.W;
            }

            // Triangle 1: BL, BR, TR. Triangle 2: BL, TR, TL.
            WriteVertex(c0, uvs[0]); WriteVertex(c1, uvs[1]); WriteVertex(c2, uvs[2]);
            WriteVertex(c0, uvs[3]); WriteVertex(c2, uvs[4]); WriteVertex(c3, uvs[5]);
        }

        return particles.Length * VerticesPerParticle;
    }

    /// <summary>Uploads the combined per-frame vertex array to the GPU, growing the underlying
    /// buffer if needed. Call ONCE per frame, after every live emitter has been expanded via
    /// repeated <see cref="ExpandToVertices"/> calls into the same combined array -- see this
    /// class's own header comment for why per-emitter upload+draw (GL's own shape) doesn't carry
    /// over to Vulkan.</summary>
    public void Upload(ReadOnlySpan<float> data)
    {
        if (data.Length == 0) return;
        EnsureCapacity(data.Length);
        VkBufferHelper.UpdateHostVisible(_vk, _memory, data);
    }

    private void EnsureCapacity(int floatCount)
    {
        if (floatCount <= _capacityFloats && _vbo.Handle != 0) return;

        // Exception-safety mirrors VkInstanceDrawer.EnsureCapacity's own approach: capture the
        // old capacity, clear the fields to a known-empty state BEFORE allocating the
        // replacement, and allocate into locals first so a throw from AllocateHostVisible (a
        // real risk under memory pressure -- VkPhysicalDeviceLimits.maxMemoryAllocationCount,
        // device OOM) leaves the fields honestly empty rather than pointing at just-destroyed
        // handles that the next call's guard above would treat as still valid.
        int oldCapacityFloats = _capacityFloats;
        if (_vbo.Handle != 0)
        {
            _vk.Api.DestroyBuffer(_vk.Device, _vbo, null);
            _vk.Api.FreeMemory(_vk.Device, _memory, null);
        }
        _vbo = default;
        _memory = default;
        _capacityFloats = 0;

        // Same headroom-doubling growth pattern as VkInstanceDrawer.EnsureCapacity -- avoids
        // reallocating every frame as particle counts fluctuate tick to tick.
        int newCapacityFloats = Math.Max(floatCount, oldCapacityFloats * 2);
        var placeholder = new float[newCapacityFloats];
        VkBufferHelper.AllocateHostVisible(_vk, BufferUsageFlags.VertexBufferBit,
            out var newVbo, out var newMemory, (ReadOnlySpan<float>)placeholder);
        _vbo = newVbo;
        _memory = newMemory;
        _capacityFloats = newCapacityFloats;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_vbo.Handle != 0)
        {
            _vk.Api.DestroyBuffer(_vk.Device, _vbo, null);
            _vk.Api.FreeMemory(_vk.Device, _memory, null);
        }
    }
}
