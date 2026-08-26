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

// Set 2 (per-material) for prim.frag. One instance per distinct material a
// scene actually uses (not per face/draw call): faces sharing a material share this set's
// bind, since this is the biggest set and changes per material, not per draw.
// Callers pass VkPlaceholderTextures' DescriptorImageInfo for any of the 5 texture slots a
// given material doesn't use -- required, not optional, since sampling an unbound descriptor
// is undefined behavior (see VkPlaceholderTextures.cs).

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkMaterialDescriptorSet : IDisposable
{
    public DescriptorSet Set { get; }

    private readonly VkContext _vk;
    private readonly int _uboSlot;
    private bool _disposed;

    /// <summary>
    /// Allocates the descriptor set and writes all 6 bindings in one call: 5 samplers
    /// (binding order must match prim.frag exactly: albedo=0, normal=1, specular=2,
    /// metallicRoughness=3, emissive=4 -- grep-verified against the shader before writing
    /// VkDescriptorSetLayouts.cs) plus the Material UBO at binding 5.
    /// </summary>
    public VkMaterialDescriptorSet(VkContext vk, VkPrimPipeline pipeline,
        DescriptorImageInfo albedo, DescriptorImageInfo normal, DescriptorImageInfo specular,
        DescriptorImageInfo metallicRoughness, DescriptorImageInfo emissive, in VkMaterialUbo data)
    {
        _vk = vk;

        _uboSlot = vk.MaterialUboPool.Rent(data);

        // Everything past Rent() succeeding can still throw (most concretely
        // AllocateDescriptorSets.ThrowOnError() on VK_ERROR_OUT_OF_POOL_MEMORY -- the shared
        // VkContext.DescriptorPool has its own independent MaxSets/per-type budget, contended by
        // skin/flexi compute sets and per-frame/per-pass sets too, so it can exhaust even while
        // MaterialUboPool itself still has free slots). Without this try/catch, that throw
        // propagates out of the constructor with _uboSlot already claimed from the pool but this
        // object never fully constructed -- never assigned to a caller variable, never reachable,
        // never Dispose()'d, so the slot it rented is never returned. UploadSceneObjectNoRebuild's
        // own catch only disposes the faces list it already built, which never contains this
        // half-constructed instance. One leaked slot per occurrence sounds small, but a busy
        // scene generates thousands of pool-exhaustion drops per session (each drop attempt can
        // trigger this on faces built before the one that exhausted the pool), and every leaked
        // slot is gone for the rest of the session -- this is what pins the pool at capacity
        // permanently rather than just under sustained load.
        try
        {
            var setLayout = pipeline.PerMaterialLayout;
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = vk.DescriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout
            };
            vk.Api.AllocateDescriptorSets(vk.Device, &allocInfo, out var set).ThrowOnError();
            Set = set;

            var imageInfos = stackalloc DescriptorImageInfo[5] { albedo, normal, specular, metallicRoughness, emissive };
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = vk.MaterialUboPool.Buffer,
                Offset = (ulong)_uboSlot * vk.MaterialUboPool.Stride,
                Range = (ulong)sizeof(VkMaterialUbo)
            };

            var writes = stackalloc WriteDescriptorSet[6];
            for (uint i = 0; i < 5; i++)
            {
                writes[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = Set,
                    DstBinding = i,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[i]
                };
            }
            writes[5] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = Set,
                DstBinding = 5,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };
            vk.Api.UpdateDescriptorSets(vk.Device, 6, writes, 0, null);
        }
        catch
        {
            vk.MaterialUboPool.Return(_uboSlot);
            throw;
        }
    }

    /// <summary>Overwrites this material's UBO contents in place (e.g. a UV-scroll animation
    /// or a runtime material-property change) -- no descriptor rewrite needed, same pattern as
    /// <see cref="VkPrimDescriptorSets.UpdatePerFrame"/>.</summary>
    public void Update(in VkMaterialUbo data)
    {
        _vk.MaterialUboPool.Write(_uboSlot, in data);
    }

    /// <summary>Rewrites a single sampler binding in place (0=albedo..4=emissive, matching the
    /// constructor's binding order) -- for <c>VkViewportControl.PatchSubmissionTexture</c>, which
    /// swaps one already-live face's texture after an async decode completes without rebuilding
    /// the whole descriptor set. Caller is responsible for keeping <see cref="Update"/>'s
    /// <c>HasX</c> flags in sync if this changes whether a slot is populated at all -- this call
    /// only rewrites the image binding itself. Only safe when no in-flight command buffer is
    /// still reading this set (see the call site's own frame-in-flight/fence-wait note).</summary>
    public void UpdateTexture(uint binding, DescriptorImageInfo imageInfo)
    {
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = Set,
            DstBinding = binding,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var set = Set;
        _vk.Api.FreeDescriptorSets(_vk.Device, _vk.DescriptorPool, 1, &set);
        _vk.MaterialUboPool.Return(_uboSlot);
    }
}
