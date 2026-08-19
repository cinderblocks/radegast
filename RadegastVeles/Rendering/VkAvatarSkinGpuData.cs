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

// Vulkan port of AvatarSkinGpuData.cs (plan Section 8b). The joint/weight conversion logic
// (2-bone-name-lookup vs. rigged-4-bone-passthrough) is copied byte-identical from the GL
// original -- pure CPU-side C# with no GL dependency, so there was never a reason to
// re-derive it. The GPU-resource shape differs, all Vulkan-model consequences:
//   - GL's 4 raw uint buffer handles become 4 VkBuffer+VkDeviceMemory pairs.
//   - The 3 "uploaded once" SSBOs (BindVerts/Joints/Weights) use AllocateDeviceLocal (matches
//     GL's StaticDraw hint -- device-local memory is the actual Vulkan analogue of "the driver
//     may place this wherever's fastest to read," not just a hint the driver may ignore).
//   - The streaming SkinMats SSBO uses AllocateEmpty + UpdateHostVisible each tick (matches
//     GL's StreamDraw + BufferSubData -- host-visible so VkSkinDeformer.DispatchPending's
//     per-tick CPU write doesn't need a staging round-trip).
//   - GL has no descriptor-set concept (gl.BindBufferBase is a raw per-dispatch binding call);
//     Vulkan needs a real DescriptorSet bound to these 5 buffers, allocated ONCE here (not
//     rewritten per dispatch -- only the SkinMatsSSBO's CONTENTS change per tick, via
//     UpdateHostVisible, not its descriptor binding, so the set stays valid for this object's
//     whole lifetime).

using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkAvatarSkinGpuData : IDisposable
{
    private readonly Buffer _bindVertsSSBO, _jointsSSBO, _weightsSSBO, _skinMatsSSBO;
    private readonly DeviceMemory _bindVertsMemory, _jointsMemory, _weightsMemory, _skinMatsMemory;

    internal DeviceMemory SkinMatsMemory => _skinMatsMemory;
    internal DescriptorSet Set { get; }

    internal readonly VkMesh Mesh;
    internal readonly int VertexCount;
    internal readonly int JointCount;

    // Two buffers, not one in-place buffer: DispatchPending reads job.SkinMats synchronously on
    // the render thread (TryRemove then VkBufferHelper.UpdateHostVisible) with no lock against
    // AnimTick's background thread. A single shared buffer mutated in place could, in principle,
    // be re-written by a new tick while the render thread is mid-read of the previous tick's
    // contents -- a torn-matrix race. Ping-ponging between two buffers means AnimTick always
    // writes into the one NOT referenced by whatever job is either sitting in VkSkinDeformer's
    // pending map or already claimed by an in-flight DispatchPending read, since that job (if
    // any) was necessarily built from the other slot.
    private readonly float[] _skinMatsBufferA, _skinMatsBufferB;
    private int _skinMatsWriteSlot;

    /// <summary>Returns the scratch buffer for this tick's packed skin matrices, alternating
    /// with the previous call so the render thread's synchronous read of the last-enqueued job's
    /// array (see this class's own field comment above) is never concurrently overwritten. Not
    /// thread-safe against concurrent calls for the same instance -- matches the existing
    /// single-writer-per-avatar contract AnimTick's other per-tick state already relies on.</summary>
    internal float[] AcquireSkinMatsWriteBuffer()
    {
        _skinMatsWriteSlot ^= 1;
        return _skinMatsWriteSlot == 0 ? _skinMatsBufferA : _skinMatsBufferB;
    }

    /// <summary>Ordered bone names for the 2-bone body path, indexed by joint index in the
    /// SSBOs. Null for the rigged 4-bone path (JointNames are per-face and already indexed).
    /// Mirrors AvatarSkinGpuData.BoneNames -- carried over even though nothing in this port
    /// reads it yet, matching the GL field's own scope (SceneAvatarAnimator's consumer).</summary>
    internal readonly string[]? BoneNames;

    internal bool IsDisposed;

    private readonly VkContext _vk;

    private VkAvatarSkinGpuData(VkContext vk, Buffer bindVertsSSBO, DeviceMemory bindVertsMemory,
        Buffer jointsSSBO, DeviceMemory jointsMemory, Buffer weightsSSBO, DeviceMemory weightsMemory,
        Buffer skinMatsSSBO, DeviceMemory skinMatsMemory, DescriptorSet set,
        VkMesh mesh, int vertexCount, int jointCount, string[]? boneNames)
    {
        _vk = vk;
        _bindVertsSSBO = bindVertsSSBO; _bindVertsMemory = bindVertsMemory;
        _jointsSSBO = jointsSSBO; _jointsMemory = jointsMemory;
        _weightsSSBO = weightsSSBO; _weightsMemory = weightsMemory;
        _skinMatsSSBO = skinMatsSSBO; _skinMatsMemory = skinMatsMemory;
        Set = set;
        Mesh = mesh;
        VertexCount = vertexCount;
        JointCount = jointCount;
        BoneNames = boneNames;
        _skinMatsBufferA = new float[jointCount * 16];
        _skinMatsBufferB = new float[jointCount * 16];
    }

    /// <summary>
    /// Allocates GPU buffers + a descriptor set for one avatar skin face. Must be called on the
    /// render thread (same reasoning as every other VkContext.Pool-submitting constructor in
    /// this port -- AllocateDeviceLocal submits a one-off command buffer through vk.Pool).
    /// </summary>
    internal static VkAvatarSkinGpuData Create(VkContext vk, VkSkinPipeline pipeline, AvatarFaceSkinData skin, VkMesh mesh)
    {
        int nv = skin.BindVerts.Length / 12;
        int[] joints4;
        float[] weights4;
        int jointCount;
        string[]? boneNames = null;

        if (skin.JointNames != null && skin.Joints != null && skin.Weights != null)
        {
            // Rigged 4-bone path: joint indices and weights already in interleaved arrays.
            joints4 = skin.Joints;
            weights4 = skin.Weights;
            jointCount = skin.JointNames.Length;
        }
        else
        {
            // 2-bone body path: convert name lookups to compact joint indices.
            var nameList = new List<string>();
            var nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int vi = 0; vi < nv; vi++)
            {
                var b1 = skin.Bone1.Length > vi ? skin.Bone1[vi] : string.Empty;
                var b2 = skin.Bone2.Length > vi ? skin.Bone2[vi] : string.Empty;
                if (b1.Length > 0 && !nameIndex.ContainsKey(b1)) { nameIndex[b1] = nameList.Count; nameList.Add(b1); }
                if (b2.Length > 0 && !nameIndex.ContainsKey(b2)) { nameIndex[b2] = nameList.Count; nameList.Add(b2); }
            }

            joints4 = new int[nv * 4];
            weights4 = new float[nv * 4];
            for (int vi = 0; vi < nv; vi++)
            {
                var b1 = skin.Bone1.Length > vi ? skin.Bone1[vi] : string.Empty;
                float w1 = skin.Weight1.Length > vi ? skin.Weight1[vi] : 0f;
                var b2 = skin.Bone2.Length > vi ? skin.Bone2[vi] : string.Empty;
                float w2 = skin.Weight2.Length > vi ? skin.Weight2[vi] : 0f;

                joints4[vi * 4] = b1.Length > 0 ? nameIndex[b1] : 0;
                weights4[vi * 4] = b1.Length > 0 ? w1 : 0f;
                joints4[vi * 4 + 1] = (b2.Length > 0 && w2 > 0f) ? nameIndex[b2] : 0;
                weights4[vi * 4 + 1] = (b2.Length > 0 && w2 > 0f) ? w2 : 0f;
                // indices 2 and 3 stay 0 (no influence)
            }

            jointCount = nameList.Count;
            boneNames = nameList.ToArray();
        }

        VkBufferHelper.AllocateDeviceLocal(vk, BufferUsageFlags.StorageBufferBit,
            out var bvSSBO, out var bvMem, (ReadOnlySpan<float>)skin.BindVerts);
        VkBufferHelper.AllocateDeviceLocal(vk, BufferUsageFlags.StorageBufferBit,
            out var jiSSBO, out var jiMem, (ReadOnlySpan<int>)joints4);
        VkBufferHelper.AllocateDeviceLocal(vk, BufferUsageFlags.StorageBufferBit,
            out var wtSSBO, out var wtMem, (ReadOnlySpan<float>)weights4);
        // Streaming skin-matrix SSBO: jointCount mat4s, no initial data (written every tick via
        // UpdateHostVisible in VkSkinDeformer.DispatchPending) -- matches GL's StreamDraw +
        // zero-filled BufferData call exactly.
        VkBufferHelper.AllocateEmpty(vk, BufferUsageFlags.StorageBufferBit,
            (ulong)(jointCount * 16 * sizeof(float)), out var smSSBO, out var smMem);

        // Output buffer (binding 4) is the mesh's own VBO, sized VertexStride bytes/vertex --
        // compute writes deformed verts directly into it, same as GL's binding 4 = gpu.Mesh.Vbo.
        ulong outVertsBytes = (ulong)nv * VkMesh.VertexStride;

        var set = AllocateAndWriteSet(vk, pipeline,
            bvSSBO, (ulong)(skin.BindVerts.Length * sizeof(float)),
            jiSSBO, (ulong)(joints4.Length * sizeof(int)),
            wtSSBO, (ulong)(weights4.Length * sizeof(float)),
            smSSBO, (ulong)(jointCount * 16 * sizeof(float)),
            mesh.Vbo, outVertsBytes);

        return new VkAvatarSkinGpuData(vk, bvSSBO, bvMem, jiSSBO, jiMem, wtSSBO, wtMem, smSSBO, smMem, set,
            mesh, nv, jointCount, boneNames);
    }

    private static DescriptorSet AllocateAndWriteSet(VkContext vk, VkSkinPipeline pipeline,
        Buffer bindVerts, ulong bindVertsBytes, Buffer joints, ulong jointsBytes,
        Buffer weights, ulong weightsBytes, Buffer skinMats, ulong skinMatsBytes,
        Buffer outVerts, ulong outVertsBytes)
    {
        var setLayout = pipeline.SetLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = vk.DescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout
        };
        vk.Api.AllocateDescriptorSets(vk.Device, &allocInfo, out var set).ThrowOnError();

        // Binding order matches skin.comp exactly: 0=BindVerts, 1=Joints, 2=Weights,
        // 3=SkinMats, 4=OutVerts.
        var buffers = stackalloc Buffer[5] { bindVerts, joints, weights, skinMats, outVerts };
        var sizes = stackalloc ulong[5] { bindVertsBytes, jointsBytes, weightsBytes, skinMatsBytes, outVertsBytes };
        var bufferInfos = stackalloc DescriptorBufferInfo[5];
        var writes = stackalloc WriteDescriptorSet[5];
        for (uint i = 0; i < 5; i++)
        {
            bufferInfos[i] = new DescriptorBufferInfo { Buffer = buffers[i], Offset = 0, Range = sizes[i] };
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = i,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfos[i]
            };
        }
        vk.Api.UpdateDescriptorSets(vk.Device, 5, writes, 0, null);
        return set;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        var api = _vk.Api;
        var device = _vk.Device;
        var set = Set;
        api.FreeDescriptorSets(device, _vk.DescriptorPool, 1, &set);
        api.DestroyBuffer(device, _bindVertsSSBO, null); api.FreeMemory(device, _bindVertsMemory, null);
        api.DestroyBuffer(device, _jointsSSBO, null); api.FreeMemory(device, _jointsMemory, null);
        api.DestroyBuffer(device, _weightsSSBO, null); api.FreeMemory(device, _weightsMemory, null);
        api.DestroyBuffer(device, _skinMatsSSBO, null); api.FreeMemory(device, _skinMatsMemory, null);
    }
}

/// <summary>
/// Work item queued by SceneAvatarAnimator/AvatarViewerViewModel's AnimTick (background thread)
/// for VkSkinDeformer to execute on the render thread. Public (2026-08-09) -- it's a parameter
/// type on ISceneViewport.ScheduleSkinCompute/ISingleObjectViewport.ScheduleSkinCompute, both
/// public interface members; its own fields/constructor stay internal (assembly-accessible from
/// every real caller, all in RadegastVeles), only the type itself needed widening.
/// </summary>
public readonly struct VkSkinComputeJob
{
    internal readonly VkAvatarSkinGpuData Gpu;
    /// <summary>Packed skin matrices: JointCount x 16 floats (raw System.Numerics.Matrix4x4
    /// bytes, row-major -- see skin.comp's own SkinMats binding comment for why no
    /// pre-transpose is needed).</summary>
    internal readonly float[] SkinMats;

    internal VkSkinComputeJob(VkAvatarSkinGpuData gpu, float[] skinMats)
    {
        Gpu = gpu;
        SkinMats = skinMats;
    }
}
