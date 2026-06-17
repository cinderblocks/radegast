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
using System.Numerics;
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Dispatches GPU compute-shader deformation for flexi prims.
/// <para>
/// FlexiPrimAnimator runs physics on a background thread and enqueues
/// <see cref="FlexiComputeJob"/> items via <see cref="Enqueue"/>.
/// GlViewportControl calls <see cref="DispatchPending"/> each frame on the
/// GL thread, which uploads the updated spine positions to the spine SSBO
/// and issues one DispatchCompute per face.  The shader writes the deformed
/// vertices directly into the mesh VBO, eliminating both the CPU vertex math
/// and the per-frame large vertex buffer upload.
/// </para>
/// </summary>
internal sealed class GlFlexiDeformer : IDisposable
{
    private readonly GlShader _shader;
    private readonly ConcurrentQueue<FlexiComputeJob> _pending = new();
    private bool _disposed;

    public GlFlexiDeformer(string computeSrc)
        => _shader = GlShader.CompileCompute(computeSrc);

    /// <summary>Thread-safe; called from the physics background thread.</summary>
    public void Enqueue(FlexiComputeJob job) => _pending.Enqueue(job);

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

            // Upload spine positions (streaming, orphaned automatically when cap grows).
            int spineBytes = (gpu.SegmentCount + 1) * 4 * sizeof(float);
            gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, gpu.SpineSSBO);
            fixed (float* p = job.SpineFloats)
                gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, 0,
                    (nuint)spineBytes, p);

            // Per-prim uniforms (constant across all faces of this prim).
            var attachTx = job.AttachTransform;
            _shader.Set("uAttachTransform", ref attachTx);
            _shader.Set("uSegmentCount", gpu.SegmentCount);
            _shader.Set("uScale", new Vector3(gpu.ScaleX, gpu.ScaleY, gpu.ScaleZ));

            for (int fi = 0; fi < gpu.BindPoseSSBOs.Length; fi++)
            {
                _shader.Set("uVertexCount", gpu.VertexCounts[fi]);

                gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, gpu.BindPoseSSBOs[fi]);
                gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, gpu.SpineSSBO);
                // Bind the mesh VBO as the output SSBO — compute writes directly into
                // the buffer that the VAO reads as vertex attribute data.
                gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 2, gpu.Meshes[fi].Vbo);

                uint groups = ((uint)gpu.VertexCounts[fi] + 63u) / 64u;
                gl.DispatchCompute(groups, 1, 1);

                // Ensure compute writes are visible to subsequent vertex attribute reads.
                gl.MemoryBarrier(MemoryBarrierMask.VertexAttribArrayBarrierBit);
            }
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
