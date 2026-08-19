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

// Vulkan port of FlexiGpuData.cs. Real structural difference from VkAvatarSkinGpuData.cs, driven
// by the source data's own shape: a skin "entry" is already one face (AvatarFaceSkinData), so
// VkAvatarSkinGpuData owns exactly ONE descriptor set. A flexi prim can have MULTIPLE faces
// sharing ONE spine simulation (FlexiPrimInfo.FaceCount/BaseVertices are both per-face arrays,
// but PathSegments/Scale are per-PRIM) -- mirroring GL's own FlexiGpuData shape (arrays indexed
// [0..FaceCount)), this class owns one descriptor set PER FACE, each pairing that face's own
// BindPoseSSBO + output mesh VBO with the SAME shared SpineSSBO (binding 1 is identical across
// all of a prim's per-face sets).
//
// GL's raw buffer handles become VkBuffer+VkDeviceMemory pairs: AllocateDeviceLocal for the
// static bind-pose data (matches GL's StaticDraw hint), AllocateEmpty+UpdateHostVisible for the
// streaming spine data (matches GL's StreamDraw + zero-filled BufferData + per-tick
// BufferSubData). Descriptor sets are allocated ONCE here, not rewritten per dispatch -- only
// the SpineSSBO's CONTENTS change per tick (via VkFlexiDeformer.DispatchPending's
// UpdateHostVisible call), not any set's buffer bindings.

using System;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkFlexiGpuData : IDisposable
{
    // All arrays are indexed [0..FaceCount).
    internal readonly Buffer[] BindPoseSSBOs;
    internal readonly DeviceMemory[] BindPoseMemories;
    internal readonly DescriptorSet[] Sets;
    internal readonly VkMesh[] Meshes;
    internal readonly int[] VertexCounts;   // vertices (not floats) per face

    private readonly Buffer _spineSSBO;
    private readonly DeviceMemory _spineMemory;

    internal DeviceMemory SpineMemory => _spineMemory;
    internal readonly int SegmentCount;      // N (spine has N+1 entries)
    internal readonly float ScaleX, ScaleY, ScaleZ;
    internal bool IsDisposed;

    private readonly VkContext _vk;

    private VkFlexiGpuData(VkContext vk, Buffer[] bindPoseSSBOs, DeviceMemory[] bindPoseMemories,
        Buffer spineSSBO, DeviceMemory spineMemory, DescriptorSet[] sets, VkMesh[] meshes,
        int[] vertexCounts, int segmentCount, float sx, float sy, float sz)
    {
        _vk = vk;
        BindPoseSSBOs = bindPoseSSBOs;
        BindPoseMemories = bindPoseMemories;
        _spineSSBO = spineSSBO;
        _spineMemory = spineMemory;
        Sets = sets;
        Meshes = meshes;
        VertexCounts = vertexCounts;
        SegmentCount = segmentCount;
        ScaleX = sx; ScaleY = sy; ScaleZ = sz;
    }

    /// <summary>
    /// Allocates GPU buffers + one descriptor set per face for a flexi prim. Must be called on
    /// the render thread (same reasoning as every other VkContext.Pool-submitting constructor in
    /// this port -- AllocateDeviceLocal submits a one-off command buffer through vk.Pool).
    /// </summary>
    internal static VkFlexiGpuData Create(VkContext vk, VkFlexiPipeline pipeline, FlexiPrimInfo info, VkMesh[] meshes)
    {
        int fc = info.FaceCount;
        var bpSSBOs = new Buffer[fc];
        var bpMemories = new DeviceMemory[fc];
        var vCounts = new int[fc];

        for (int fi = 0; fi < fc; fi++)
        {
            float[] bp = info.BaseVertices[fi];
            vCounts[fi] = bp.Length / 12;
            VkBufferHelper.AllocateDeviceLocal(vk, BufferUsageFlags.StorageBufferBit,
                out bpSSBOs[fi], out bpMemories[fi], (ReadOnlySpan<float>)bp);
        }

        // Spine SSBO: (N+1) vec4 entries; contents written each tick via UpdateHostVisible in
        // VkFlexiDeformer.DispatchPending -- matches GL's StreamDraw + zero-filled BufferData.
        ulong spineBytes = (ulong)((info.PathSegments + 1) * 4 * sizeof(float));
        VkBufferHelper.AllocateEmpty(vk, BufferUsageFlags.StorageBufferBit, spineBytes,
            out var spineSSBO, out var spineMemory);

        var sets = new DescriptorSet[fc];
        for (int fi = 0; fi < fc; fi++)
        {
            ulong bindPoseBytes = (ulong)(info.BaseVertices[fi].Length * sizeof(float));
            ulong outVertsBytes = (ulong)vCounts[fi] * VkMesh.VertexStride;
            sets[fi] = AllocateAndWriteSet(vk, pipeline,
                bpSSBOs[fi], bindPoseBytes, spineSSBO, spineBytes, meshes[fi].Vbo, outVertsBytes);
        }

        var s = info.Scale;
        return new VkFlexiGpuData(vk, bpSSBOs, bpMemories, spineSSBO, spineMemory, sets, meshes,
            vCounts, info.PathSegments, s.X, s.Y, s.Z);
    }

    private static DescriptorSet AllocateAndWriteSet(VkContext vk, VkFlexiPipeline pipeline,
        Buffer bindVerts, ulong bindVertsBytes, Buffer spine, ulong spineBytes,
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

        // Binding order matches flexi.comp exactly: 0=BaseVerts, 1=SpineData, 2=OutVerts.
        var buffers = stackalloc Buffer[3] { bindVerts, spine, outVerts };
        var sizes = stackalloc ulong[3] { bindVertsBytes, spineBytes, outVertsBytes };
        var bufferInfos = stackalloc DescriptorBufferInfo[3];
        var writes = stackalloc WriteDescriptorSet[3];
        for (uint i = 0; i < 3; i++)
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
        vk.Api.UpdateDescriptorSets(vk.Device, 3, writes, 0, null);
        return set;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        var api = _vk.Api;
        var device = _vk.Device;
        for (int i = 0; i < Sets.Length; i++)
        {
            var set = Sets[i];
            api.FreeDescriptorSets(device, _vk.DescriptorPool, 1, &set);
        }
        for (int i = 0; i < BindPoseSSBOs.Length; i++)
        {
            api.DestroyBuffer(device, BindPoseSSBOs[i], null);
            api.FreeMemory(device, BindPoseMemories[i], null);
        }
        api.DestroyBuffer(device, _spineSSBO, null);
        api.FreeMemory(device, _spineMemory, null);
    }
}

/// <summary>
/// Work item queued by FlexiPrimAnimator (background thread) for VkFlexiDeformer to execute on
/// the render thread. Public -- a parameter type on the public ScheduleFlexiCompute interface
/// members.
/// </summary>
public readonly struct VkFlexiComputeJob
{
    internal readonly VkFlexiGpuData Gpu;
    internal readonly float[] SpineFloats;   // (N+1)*4 floats, vec4 per segment
    internal readonly System.Numerics.Matrix4x4 AttachTransform;

    internal VkFlexiComputeJob(VkFlexiGpuData gpu, float[] spineFloats, System.Numerics.Matrix4x4 attachTx)
    {
        Gpu = gpu;
        SpineFloats = spineFloats;
        AttachTransform = attachTx;
    }
}
