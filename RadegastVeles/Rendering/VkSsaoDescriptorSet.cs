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

// Owns the SSAO pass's set 0 (SsaoParams UBO + depth/normal/noise samplers) and the SSAO-blur
// pass's set 0 (uSsaoTex sampler). Kept as one class since both sets share a lifecycle
// (created/destroyed together with the rest of the SSAO pipeline) even though they belong to
// two different pipelines.
//
// Three different rewrite cadences, each with its own method:
//   - UpdateParams: every frame SSAO runs (host-visible buffer rewrite only, no descriptor
//     rewrite -- mirrors VkPrimDescriptorSets.UpdatePerFrame's pattern).
//   - UpdateGbufferInputs: only when the G-buffer target is (re)created (its ImageViews
//     change on resize) -- a real descriptor rewrite, called from
//     VkViewportControl.EnsureGBufferTarget's own (re)creation path, never per-frame.
//   - UpdateBlurInput: only when the SSAO-raw target is (re)created, same reasoning.
// The noise-texture binding is written ONCE at construction and never rewritten (the texture
// itself never changes).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkSsaoDescriptorSet : System.IDisposable
{
    public DescriptorSet SsaoSet { get; }
    public DescriptorSet BlurSet { get; }

    private readonly VkContext _vk;
    private readonly Buffer _paramsBuffer;
    private readonly DeviceMemory _paramsMemory;
    private bool _disposed;

    public VkSsaoDescriptorSet(VkContext vk, VkSsaoPipeline ssaoPipeline, VkSsaoBlurPipeline blurPipeline, VkSsaoNoiseTexture noise)
    {
        _vk = vk;

        var initial = default(VkSsaoUbo);
        VkBufferHelper.AllocateHostVisible(vk, BufferUsageFlags.UniformBufferBit,
            out _paramsBuffer, out _paramsMemory, MemoryMarshal.CreateReadOnlySpan(ref initial, 1));

        var ssaoLayout = ssaoPipeline.ParamsLayout;
        var ssaoAlloc = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = vk.DescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &ssaoLayout
        };
        vk.Api.AllocateDescriptorSets(vk.Device, &ssaoAlloc, out var ssaoSet).ThrowOnError();
        SsaoSet = ssaoSet;

        var blurLayout = blurPipeline.SamplerLayout;
        var blurAlloc = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = vk.DescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &blurLayout
        };
        vk.Api.AllocateDescriptorSets(vk.Device, &blurAlloc, out var blurSet).ThrowOnError();
        BlurSet = blurSet;

        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _paramsBuffer,
            Offset = 0,
            Range = (ulong)sizeof(VkSsaoUbo)
        };
        var noiseInfo = noise.DescriptorImageInfo;
        var writes = stackalloc WriteDescriptorSet[2]
        {
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = SsaoSet,
                DstBinding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = SsaoSet,
                DstBinding = 3,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &noiseInfo
            }
        };
        vk.Api.UpdateDescriptorSets(vk.Device, 2, writes, 0, null);
        // Bindings 1 (uDepthTex) / 2 (uNormalTex) on SsaoSet, and binding 0 (uSsaoTex) on
        // BlurSet, are left unwritten here -- VkViewportControl.EnsureGBufferTarget/
        // EnsureSsaoTargets write them on first creation (before this set is ever bound for a
        // real draw), via UpdateGbufferInputs/UpdateBlurInput below.
    }

    /// <summary>Per-frame numeric rewrite (host-visible memory only, no descriptor rewrite) --
    /// call once per frame SSAO runs, before recording the SSAO pass's draw.</summary>
    public void UpdateParams(in VkSsaoUbo data)
    {
        VkBufferHelper.UpdateHostVisible(_vk, _paramsMemory, MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in data), 1));
    }

    /// <summary>Rewrites SsaoSet's depth/normal sampler bindings -- call ONLY when the
    /// G-buffer target is (re)created (its ImageViews changed), never per-frame. Safe to call
    /// mid-command-buffer-recording sequence under this control's current no-frames-in-flight
    /// invariant -- same one <c>PatchSubmissionTexture</c> (Milestone 11) already relies on and
    /// documents; revisit both sites together if a future increment adds real frame-in-flight
    /// pipelining.</summary>
    public void UpdateGbufferInputs(DescriptorImageInfo depthInfo, DescriptorImageInfo normalInfo)
    {
        var infos = stackalloc DescriptorImageInfo[2] { depthInfo, normalInfo };
        var writes = stackalloc WriteDescriptorSet[2];
        for (uint i = 0; i < 2; i++)
        {
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = SsaoSet,
                DstBinding = 1 + i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &infos[i]
            };
        }
        _vk.Api.UpdateDescriptorSets(_vk.Device, 2, writes, 0, null);
    }

    /// <summary>Rewrites BlurSet's uSsaoTex binding -- call ONLY when the SSAO-raw target is
    /// (re)created, same timing/safety reasoning as <see cref="UpdateGbufferInputs"/>.</summary>
    public void UpdateBlurInput(DescriptorImageInfo ssaoColorInfo)
    {
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = BlurSet,
            DstBinding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &ssaoColorInfo
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var sets = stackalloc DescriptorSet[2] { SsaoSet, BlurSet };
        _vk.Api.FreeDescriptorSets(_vk.Device, _vk.DescriptorPool, 2, sets);
        _vk.Api.DestroyBuffer(_vk.Device, _paramsBuffer, null);
        _vk.Api.FreeMemory(_vk.Device, _paramsMemory, null);
    }
}
