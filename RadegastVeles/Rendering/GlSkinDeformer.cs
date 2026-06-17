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
using System.Collections.Concurrent;
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Dispatches GPU compute-shader Linear Blend Skinning for avatar faces.
/// <para>
/// SceneAvatarAnimator runs the LBS math on a background thread, computes the
/// per-joint skin matrices, and enqueues a <see cref="SkinComputeJob"/> via
/// <see cref="Enqueue"/>.  GlViewportControl calls <see cref="DispatchPending"/>
/// each frame on the GL thread, which uploads the skin matrices to the streaming
/// SSBO and issues one DispatchCompute per face.  The shader writes the deformed
/// vertices directly into the mesh VBO, eliminating both the CPU vertex loop and
/// the per-frame large vertex buffer upload.
/// </para>
/// </summary>
internal sealed class GlSkinDeformer : IDisposable
{
    private readonly GlShader _shader;
    private readonly ConcurrentQueue<SkinComputeJob> _pending = new();
    private bool _disposed;

    public GlSkinDeformer(string computeSrc)
        => _shader = GlShader.CompileCompute(computeSrc);

    /// <summary>Thread-safe; called from the animation background thread.</summary>
    public void Enqueue(SkinComputeJob job) => _pending.Enqueue(job);

    /// <summary>
    /// Processes all queued jobs.  Must be called on the GL thread, before
    /// draw calls that use the deformed meshes.
    /// </summary>
    public unsafe void DispatchPending()
    {
        if (_pending.IsEmpty) return;

        var gl = GlApi.Gl;
        _shader.Use();

        while (_pending.TryDequeue(out var job))
        {
            var gpu = job.Gpu;
            if (gpu.IsDisposed) continue;

            // Upload skin matrices for this tick.
            int smBytes = gpu.JointCount * 16 * sizeof(float);
            gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, gpu.SkinMatsSSBO);
            fixed (float* p = job.SkinMats)
                gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, 0, (nuint)smBytes, p);

            _shader.Set("uVertexCount", gpu.VertexCount);

            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, gpu.BindVertsSSBO);
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, gpu.JointsSSBO);
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 2, gpu.WeightsSSBO);
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 3, gpu.SkinMatsSSBO);
            // Mesh VBO is the output — compute writes deformed verts directly into it.
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 4, gpu.Mesh.Vbo);

            uint groups = ((uint)gpu.VertexCount + 63u) / 64u;
            gl.DispatchCompute(groups, 1, 1);

            // Ensure compute writes are visible to subsequent vertex attribute reads.
            gl.MemoryBarrier(MemoryBarrierMask.VertexAttribArrayBarrierBit);
        }

        _shader.Unuse();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shader.Dispose();
    }
}
