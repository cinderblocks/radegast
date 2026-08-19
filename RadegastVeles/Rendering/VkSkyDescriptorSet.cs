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

// Owns the sky pipeline's set 1 (SkyPass UBO + cloud-noise sampler) -- plan Section 8c-2.
// Mirrors VkPrimDescriptorSets.cs's shape (host-visible UBO buffer + AllocateSet helper) but
// simpler: one set, one UBO binding whose CONTENTS change every frame (UpdateSky, host-visible
// memory rewrite, no descriptor rewrite needed) plus one sampler binding written ONCE at
// construction and never rewritten (the cloud-noise texture doesn't change after creation).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkSkyDescriptorSet : System.IDisposable
{
    public DescriptorSet Set { get; }

    private readonly VkContext _vk;
    private readonly Buffer _uboBuffer;
    private readonly DeviceMemory _uboMemory;
    private bool _disposed;

    public VkSkyDescriptorSet(VkContext vk, VkSkyPipeline pipeline, VkCloudNoiseTexture cloudNoise)
    {
        _vk = vk;

        var initial = default(VkSkyUbo);
        VkBufferHelper.AllocateHostVisible(vk, BufferUsageFlags.UniformBufferBit,
            out _uboBuffer, out _uboMemory, MemoryMarshal.CreateReadOnlySpan(ref initial, 1));

        var setLayout = pipeline.SkyPassLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = vk.DescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout
        };
        vk.Api.AllocateDescriptorSets(vk.Device, &allocInfo, out var set).ThrowOnError();
        Set = set;

        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _uboBuffer,
            Offset = 0,
            Range = (ulong)sizeof(VkSkyUbo)
        };
        var imageInfo = cloudNoise.DescriptorImageInfo;
        var writes = stackalloc WriteDescriptorSet[2]
        {
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = Set,
                DstBinding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = Set,
                DstBinding = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfo
            }
        };
        vk.Api.UpdateDescriptorSets(vk.Device, 2, writes, 0, null);
    }

    /// <summary>Overwrites the SkyPass UBO's contents -- call once per frame before recording
    /// the sky draw, same host-visible-rewrite-only pattern as
    /// <see cref="VkPrimDescriptorSets.UpdatePerFrame"/>.</summary>
    public void UpdateSky(in VkSkyUbo data)
    {
        VkBufferHelper.UpdateHostVisible(_vk, _uboMemory, MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in data), 1));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var set = Set;
        _vk.Api.FreeDescriptorSets(_vk.Device, _vk.DescriptorPool, 1, &set);
        _vk.Api.DestroyBuffer(_vk.Device, _uboBuffer, null);
        _vk.Api.FreeMemory(_vk.Device, _uboMemory, null);
    }
}
