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
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// GPU-side resources for one avatar face: static bind-pose, joint-index, and
/// weight SSBOs, plus a streaming skin-matrix SSBO updated each animation tick.
/// Created on the GL thread in UploadSubmission / UploadSceneObjectNoRebuild;
/// disposed when the avatar is removed or rebuilt.
/// </summary>
internal sealed class AvatarSkinGpuData : IDisposable
{
    // Static SSBOs — uploaded once.
    internal readonly uint BindVertsSSBO;
    internal readonly uint JointsSSBO;
    internal readonly uint WeightsSSBO;
    // Streaming SSBO — uploaded each tick via BufferSubData.
    internal readonly uint SkinMatsSSBO;

    internal readonly GlMesh Mesh;
    internal readonly int    VertexCount;
    internal readonly int    JointCount;

    /// <summary>
    /// Ordered bone names for the 2-bone body path, indexed by joint index in the SSBOs.
    /// Null for the rigged 4-bone path (JointNames are per-face and already indexed).
    /// </summary>
    internal readonly string[]? BoneNames;

    internal bool IsDisposed;

    private AvatarSkinGpuData(uint bindVertsSSBO, uint jointsSSBO, uint weightsSSBO,
        uint skinMatsSSBO, GlMesh mesh, int vertexCount, int jointCount, string[]? boneNames)
    {
        BindVertsSSBO = bindVertsSSBO;
        JointsSSBO    = jointsSSBO;
        WeightsSSBO   = weightsSSBO;
        SkinMatsSSBO  = skinMatsSSBO;
        Mesh          = mesh;
        VertexCount   = vertexCount;
        JointCount    = jointCount;
        BoneNames     = boneNames;
    }

    /// <summary>
    /// Allocates GPU buffers for one avatar skin face.  Must be called on the GL thread.
    /// </summary>
    internal static unsafe AvatarSkinGpuData Create(AvatarFaceSkinData skin, GlMesh mesh)
    {
        var gl = GlApi.Gl;

        int   nv          = skin.BindVerts.Length / 12;
        int[] joints4;
        float[] weights4;
        int   jointCount;
        string[]? boneNames = null;

        if (skin.JointNames != null && skin.Joints != null && skin.Weights != null)
        {
            // Rigged 4-bone path: joint indices and weights already in interleaved arrays.
            joints4    = skin.Joints;
            weights4   = skin.Weights;
            jointCount = skin.JointNames.Length;
        }
        else
        {
            // 2-bone body path: convert name lookups to compact joint indices.
            var nameList  = new List<string>();
            var nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int vi = 0; vi < nv; vi++)
            {
                var b1 = skin.Bone1.Length > vi ? skin.Bone1[vi] : string.Empty;
                var b2 = skin.Bone2.Length > vi ? skin.Bone2[vi] : string.Empty;
                if (b1.Length > 0 && !nameIndex.ContainsKey(b1)) { nameIndex[b1] = nameList.Count; nameList.Add(b1); }
                if (b2.Length > 0 && !nameIndex.ContainsKey(b2)) { nameIndex[b2] = nameList.Count; nameList.Add(b2); }
            }

            joints4    = new int  [nv * 4];
            weights4   = new float[nv * 4];
            for (int vi = 0; vi < nv; vi++)
            {
                var   b1 = skin.Bone1.Length  > vi ? skin.Bone1[vi]   : string.Empty;
                float w1 = skin.Weight1.Length > vi ? skin.Weight1[vi] : 0f;
                var   b2 = skin.Bone2.Length  > vi ? skin.Bone2[vi]   : string.Empty;
                float w2 = skin.Weight2.Length > vi ? skin.Weight2[vi] : 0f;

                joints4 [vi * 4]     = b1.Length > 0 ? nameIndex[b1] : 0;
                weights4[vi * 4]     = b1.Length > 0 ? w1 : 0f;
                joints4 [vi * 4 + 1] = (b2.Length > 0 && w2 > 0f) ? nameIndex[b2] : 0;
                weights4[vi * 4 + 1] = (b2.Length > 0 && w2 > 0f) ? w2 : 0f;
                // indices 2 and 3 stay 0 (no influence)
            }

            jointCount = nameList.Count;
            boneNames  = nameList.ToArray();
        }

        // Upload static SSBOs.
        uint bvSSBO = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, bvSSBO);
        fixed (float* p = skin.BindVerts)
            gl.BufferData(BufferTargetARB.ShaderStorageBuffer,
                (nuint)(skin.BindVerts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);

        uint jiSSBO = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, jiSSBO);
        fixed (int* p = joints4)
            gl.BufferData(BufferTargetARB.ShaderStorageBuffer,
                (nuint)(joints4.Length * sizeof(int)), p, BufferUsageARB.StaticDraw);

        uint wtSSBO = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, wtSSBO);
        fixed (float* p = weights4)
            gl.BufferData(BufferTargetARB.ShaderStorageBuffer,
                (nuint)(weights4.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);

        // Streaming skin-matrix SSBO: jointCount mat4s, updated via BufferSubData each tick.
        uint smSSBO = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, smSSBO);
        nint zero = IntPtr.Zero;
        gl.BufferData(BufferTargetARB.ShaderStorageBuffer,
            (nuint)(jointCount * 16 * sizeof(float)), in zero, BufferUsageARB.StreamDraw);

        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

        return new AvatarSkinGpuData(bvSSBO, jiSSBO, wtSSBO, smSSBO, mesh, nv, jointCount, boneNames);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        var gl = GlApi.Gl;
        gl.DeleteBuffer(BindVertsSSBO);
        gl.DeleteBuffer(JointsSSBO);
        gl.DeleteBuffer(WeightsSSBO);
        gl.DeleteBuffer(SkinMatsSSBO);
    }
}

/// <summary>
/// Work item queued by SceneAvatarAnimator (background thread) for
/// GlSkinDeformer to execute on the GL thread.
/// </summary>
internal readonly struct SkinComputeJob
{
    internal readonly AvatarSkinGpuData Gpu;
    /// <summary>Packed skin matrices: JointCount × 16 floats (raw OpenTK Matrix4 bytes).</summary>
    internal readonly float[] SkinMats;

    internal SkinComputeJob(AvatarSkinGpuData gpu, float[] skinMats)
    {
        Gpu      = gpu;
        SkinMats = skinMats;
    }
}
