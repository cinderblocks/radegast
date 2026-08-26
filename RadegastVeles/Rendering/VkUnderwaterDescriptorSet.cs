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

// Owns the underwater pass's one descriptor set (scene-color + water normal/dudv map samplers).
// Bindings 1/2 (the water normal/dudv maps) are written ONCE at construction -- those textures
// are loaded once at water-pipeline init and never change. Binding 0 (the copied scene-color
// image) is rewritten only when VkViewportControl.EnsureUnderwaterTarget (re)creates that image
// on resize, mirroring VkSsaoDescriptorSet.UpdateBlurInput's exact "never per-frame" contract.

using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkUnderwaterDescriptorSet : System.IDisposable
{
    public DescriptorSet Set { get; }

    private readonly VkContext _vk;
    private bool _disposed;

    public VkUnderwaterDescriptorSet(VkContext vk, VkUnderwaterPipeline pipeline,
        DescriptorImageInfo normalMapInfo, DescriptorImageInfo dudvMapInfo)
    {
        _vk = vk;

        var layout = pipeline.SamplerLayout;
        var alloc = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = vk.DescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };
        vk.Api.AllocateDescriptorSets(vk.Device, &alloc, out var set).ThrowOnError();
        Set = set;

        var infos = stackalloc DescriptorImageInfo[2] { normalMapInfo, dudvMapInfo };
        var writes = stackalloc WriteDescriptorSet[2];
        for (uint i = 0; i < 2; i++)
        {
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = Set,
                DstBinding = 1 + i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &infos[i]
            };
        }
        vk.Api.UpdateDescriptorSets(vk.Device, 2, writes, 0, null);
        // Binding 0 (uSceneColor) is left unwritten here -- VkViewportControl.
        // EnsureUnderwaterTarget writes it on first creation, via UpdateSceneColorInput below.
    }

    /// <summary>Rewrites binding 0 (uSceneColor) -- call ONLY when the underwater-source target
    /// is (re)created (its ImageView changed), never per-frame.</summary>
    public void UpdateSceneColorInput(DescriptorImageInfo sceneColorInfo)
    {
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = Set,
            DstBinding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &sceneColorInfo
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var set = Set;
        _vk.Api.FreeDescriptorSets(_vk.Device, _vk.DescriptorPool, 1, &set);
    }
}
