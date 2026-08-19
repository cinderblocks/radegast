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

// Adapted from Avalonia's own samples/GpuInterop/VulkanDemo (MIT licensed,
// https://github.com/AvaloniaUI/Avalonia), validated working against this project's
// pinned Avalonia version in experiments/VulkanEmbeddingSpike before porting here.
//
// No VMA/suballocator here by design -- one vkAllocateMemory call per resource, matching
// Avalonia's own sample. See plan Section 4's "Memory allocator" note for why this was
// chosen over pulling in a second Vulkan binding library just for VMA.

using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal static class VkMemoryHelper
{
    internal static int FindSuitableMemoryTypeIndex(Vk api, PhysicalDevice physicalDevice, uint memoryTypeBits,
        MemoryPropertyFlags flags)
    {
        api.GetPhysicalDeviceMemoryProperties(physicalDevice, out var properties);

        for (var i = 0; i < properties.MemoryTypeCount; i++)
        {
            var type = properties.MemoryTypes[i];
            if ((memoryTypeBits & (1 << i)) != 0 && type.PropertyFlags.HasFlag(flags)) return i;
        }

        return -1;
    }

    internal static unsafe void TransitionLayout(
        Vk api,
        CommandBuffer commandBuffer,
        Image image,
        ImageLayout sourceLayout,
        AccessFlags sourceAccessMask,
        ImageLayout destinationLayout,
        AccessFlags destinationAccessMask,
        uint mipLevels)
    {
        var subresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, mipLevels, 0, 1);

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = sourceAccessMask,
            DstAccessMask = destinationAccessMask,
            OldLayout = sourceLayout,
            NewLayout = destinationLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = subresourceRange
        };

        api.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 1, in barrier);
    }
}
