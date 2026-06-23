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
using System.Numerics;
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// GPU-side resources for one flexi prim: per-face bind-pose SSBOs, a
/// streaming spine SSBO, and references to the output mesh VBOs.
/// Created on the GL thread in UploadSceneObjectNoRebuild; disposed when
/// the scene object is removed.
/// </summary>
internal sealed class FlexiGpuData : IDisposable
{
    // All arrays are indexed [0..FaceCount).
    internal readonly uint[]   BindPoseSSBOs;
    internal readonly uint     SpineSSBO;
    internal readonly GlMesh[] Meshes;
    internal readonly int[]    VertexCounts;   // vertices (not floats) per face
    internal readonly int      SegmentCount;   // N (spine has N+1 entries)
    internal readonly float    ScaleX, ScaleY, ScaleZ;
    internal          bool     IsDisposed;

    private FlexiGpuData(uint[] bindPoseSSBOs, uint spineSSBO, GlMesh[] meshes,
        int[] vertexCounts, int segmentCount, float sx, float sy, float sz)
    {
        BindPoseSSBOs = bindPoseSSBOs;
        SpineSSBO     = spineSSBO;
        Meshes        = meshes;
        VertexCounts  = vertexCounts;
        SegmentCount  = segmentCount;
        ScaleX = sx; ScaleY = sy; ScaleZ = sz;
    }

    /// <summary>
    /// Allocates GPU buffers for a flexi prim and returns the handle.
    /// Must be called on the GL thread.
    /// </summary>
    internal static unsafe FlexiGpuData Create(FlexiPrimInfo info, GlMesh[] meshes)
    {
        var gl       = GlApi.Gl;
        int fc       = info.FaceCount;
        var bpSSBOs  = new uint[fc];
        var vCounts  = new int[fc];

        for (int fi = 0; fi < fc; fi++)
        {
            float[] bp = info.BaseVertices[fi];
            vCounts[fi] = bp.Length / 8;

            uint ssbo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, ssbo);
            fixed (float* p = bp)
                gl.BufferData(BufferTargetARB.ShaderStorageBuffer,
                    (nuint)(bp.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            bpSSBOs[fi] = ssbo;
        }

        // Spine SSBO: (N+1) vec4 entries; contents written each tick via BufferSubData.
        uint spineSSBO = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, spineSSBO);
        gl.BufferData(BufferTargetARB.ShaderStorageBuffer,
            (nuint)((info.PathSegments + 1) * 4 * sizeof(float)),
            IntPtr.Zero, BufferUsageARB.StreamDraw);

        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

        var s = info.Scale;
        return new FlexiGpuData(bpSSBOs, spineSSBO, meshes, vCounts,
            info.PathSegments, s.X, s.Y, s.Z);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        var gl = GlApi.Gl;
        foreach (uint b in BindPoseSSBOs) gl.DeleteBuffer(b);
        gl.DeleteBuffer(SpineSSBO);
    }
}

/// <summary>
/// Work item queued by FlexiPrimAnimator (background thread) for
/// GlFlexiDeformer to execute on the GL thread.
/// </summary>
internal readonly struct FlexiComputeJob
{
    internal readonly FlexiGpuData Gpu;
    internal readonly float[]      SpineFloats;   // (N+1)*4 floats, vec4 per segment
    internal readonly Matrix4x4    AttachTransform;

    internal FlexiComputeJob(FlexiGpuData gpu, float[] spineFloats, Matrix4x4 attachTx)
    {
        Gpu = gpu; SpineFloats = spineFloats; AttachTransform = attachTx;
    }
}
