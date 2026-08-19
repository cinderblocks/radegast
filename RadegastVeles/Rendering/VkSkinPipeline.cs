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

// Compute pipeline for avatar GPU skin deformation (plan Section 8b, skin.comp). The FIRST
// compute pipeline anywhere in this migration -- every prior VkPipeline (VkPrimPipeline,
// VkWireframePipeline, VkPickPipeline) is a graphics pipeline. Structural differences from
// those, all consequences of it being a compute pipeline rather than style choices:
//   - One descriptor set layout (5 storage-buffer bindings, matching skin.comp's set=0
//     bindings 0-4 exactly) instead of the 3-set scheme the graphics pipelines share --
//     compute has no vertex/fragment stage split to justify multiple sets here.
//   - One push-constant range (a single int, uVertexCount) instead of the graphics
//     pipelines' vertex/fragment split -- compute is a single stage.
//   - No PipelineVertexInputState/InputAssemblyState/ViewportState/RasterizationState/
//     MultisampleState/DepthStencilState/ColorBlendState/DynamicState/RenderPass/Subpass at
//     all -- none of those concepts exist for a compute pipeline. ComputePipelineCreateInfo
//     is just {stage, layout}.

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkSkinPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }
    public DescriptorSetLayout SetLayout { get; }

    private readonly VkContext _vk;

    private VkSkinPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline, DescriptorSetLayout setLayout)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
        SetLayout = setLayout;
    }

    /// <summary>Exception-safe on partial failure, matching <see cref="VkPrimPipeline.Create"/>'s
    /// established pattern.</summary>
    public static unsafe VkSkinPipeline Create(VkContext vk)
    {
        var setLayout = CreateSetLayout(vk);
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, setLayout, out layout, out pipeline);
            return new VkSkinPipeline(vk, layout, pipeline, setLayout);
        }
        catch
        {
            var api = vk.Api;
            if (pipeline.Handle != 0) api.DestroyPipeline(vk.Device, pipeline, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            api.DestroyDescriptorSetLayout(vk.Device, setLayout, null);
            throw;
        }
    }

    /// <summary>Set 0: BindVerts/Joints/Weights/SkinMats/OutVerts, bindings 0-4, matching
    /// skin.comp's own declarations exactly. Compute-stage-only.</summary>
    private static unsafe DescriptorSetLayout CreateSetLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[5];
        for (uint i = 0; i < 5; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };
        }
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 5,
            PBindings = bindings
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, DescriptorSetLayout setLayout,
        out PipelineLayout layout, out Pipeline pipeline)
    {
        // skin.comp's PushConstants block is a single int (uVertexCount), compute-stage only.
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.ComputeBit,
            Offset = 0,
            Size = sizeof(int)
        };
        var setLayoutLocal = setLayout;
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayoutLocal,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var comp = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/skin.comp.spv");
        using var entryPoint = new VkByteString("main");
        var stage = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = comp.Handle,
            PName = entryPoint
        };

        var createInfo = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = stage,
            Layout = layout
        };
        vk.Api.CreateComputePipelines(vk.Device, default, 1, &createInfo, null, out pipeline).ThrowOnError();
    }

    public unsafe void Dispose()
    {
        var api = _vk.Api;
        api.DestroyPipeline(_vk.Device, Pipeline, null);
        api.DestroyPipelineLayout(_vk.Device, Layout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, SetLayout, null);
    }
}
