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

// Owns the water pipeline's set 1 (WaterPass UBO + 3 samplers). Mirrors
// VkSkyDescriptorSet.cs's shape (host-visible UBO buffer + AllocateSet helper), extended to 3
// sampler bindings instead of 1. ALL FOUR bindings are written differently from SSAO's own
// per-target-recreation-only pattern: unlike the SSAO/G-buffer targets (which resize with the
// viewport), the water reflection target is a FIXED 512x512 resolution, created once at init and
// never recreated on resize -- so its ImageView is stable for the panel's whole lifetime, and
// (like the normal/dudv maps, which never change at all after load) can be written ONCE at
// construction here, same as VkSkyDescriptorSet's own cloud-noise sampler binding. Only the UBO
// (binding 0) is rewritten every frame, via UpdateWater.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkWaterDescriptorSet : System.IDisposable
{
    public DescriptorSet Set { get; }

    private readonly VkContext _vk;
    private readonly Buffer _uboBuffer;
    private readonly DeviceMemory _uboMemory;
    private bool _disposed;

    public VkWaterDescriptorSet(VkContext vk, VkWaterPipeline pipeline,
        DescriptorImageInfo reflectionTexInfo, DescriptorImageInfo normalMapInfo, DescriptorImageInfo dudvMapInfo)
    {
        _vk = vk;

        var initial = default(VkWaterUbo);
        VkBufferHelper.AllocateHostVisible(vk, BufferUsageFlags.UniformBufferBit,
            out _uboBuffer, out _uboMemory, MemoryMarshal.CreateReadOnlySpan(ref initial, 1));

        var setLayout = pipeline.WaterPassLayout;
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
            Range = (ulong)sizeof(VkWaterUbo)
        };
        var reflInfo = reflectionTexInfo;
        var normInfo = normalMapInfo;
        var dudvInfo = dudvMapInfo;
        var writes = stackalloc WriteDescriptorSet[4]
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
                PImageInfo = &reflInfo
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = Set,
                DstBinding = 2,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &normInfo
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = Set,
                DstBinding = 3,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &dudvInfo
            }
        };
        vk.Api.UpdateDescriptorSets(vk.Device, 4, writes, 0, null);
    }

    /// <summary>Overwrites the WaterPass UBO's contents -- call once per frame before recording
    /// the water draw, same host-visible-rewrite-only pattern as
    /// <see cref="VkSkyDescriptorSet.UpdateSky"/>.</summary>
    public void UpdateWater(in VkWaterUbo data)
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
