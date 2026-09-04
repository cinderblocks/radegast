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

// Pipeline layout + VkPipeline for the bloom bright-pass extract (quad.vert + bloom_extract.frag)
// -- first stage of the tonemap/bloom post-process chain (extract -> blur x2 -> tonemap composite,
// see VkTonemapPipeline's own doc comment for the chain overview). Structurally identical to
// VkSsaoBlurPipeline (one sampler set, one 8-byte fragment push constant) -- reuses
// VkDescriptorSetLayouts.CreateBlurSamplerLayout directly rather than a duplicate single-sampler
// layout method, since that layout's shape (one CombinedImageSampler, binding 0, fragment-only)
// is exactly what this pass needs too.

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkBloomExtractPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }
    public DescriptorSetLayout SamplerLayout { get; }

    private readonly VkContext _vk;

    private VkBloomExtractPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline, DescriptorSetLayout samplerLayout)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
        SamplerLayout = samplerLayout;
    }

    public static unsafe VkBloomExtractPipeline Create(VkContext vk, RenderPass renderPass)
    {
        var samplerLayout = VkDescriptorSetLayouts.CreateBlurSamplerLayout(vk);

        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, samplerLayout, out layout, out pipeline);
            return new VkBloomExtractPipeline(vk, layout, pipeline, samplerLayout);
        }
        catch
        {
            var api = vk.Api;
            if (pipeline.Handle != 0) api.DestroyPipeline(vk.Device, pipeline, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            api.DestroyDescriptorSetLayout(vk.Device, samplerLayout, null);
            throw;
        }
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, RenderPass renderPass,
        DescriptorSetLayout samplerLayout, out PipelineLayout layout, out Pipeline pipeline)
    {
        var setLayout = samplerLayout;
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = 8
        };
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/quad.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/bloom_extract.frag.spv");

        using var entryPoint = new VkByteString("main");
        var stages = stackalloc PipelineShaderStageCreateInfo[2]
        {
            new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vert.Handle,
                PName = entryPoint
            },
            new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = frag.Handle,
                PName = entryPoint
            }
        };

        var vertexInputState = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 0,
            VertexAttributeDescriptionCount = 0
        };

        var inputAssemblyState = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList
        };

        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        var rasterizationState = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.CounterClockwise,
            LineWidth = 1f
        };

        var multisampleState = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        var depthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = false,
            DepthWriteEnable = false,
            DepthCompareOp = CompareOp.Always
        };

        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = false,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
        };
        var colorBlendState = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachment
        };

        var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        var createInfo = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInputState,
            PInputAssemblyState = &inputAssemblyState,
            PViewportState = &viewportState,
            PRasterizationState = &rasterizationState,
            PMultisampleState = &multisampleState,
            PDepthStencilState = &depthStencilState,
            PColorBlendState = &colorBlendState,
            PDynamicState = &dynamicState,
            Layout = layout,
            RenderPass = renderPass,
            Subpass = 0
        };
        vk.Api.CreateGraphicsPipelines(vk.Device, default, 1, &createInfo, null, out pipeline).ThrowOnError();
    }

    public unsafe void Dispose()
    {
        var api = _vk.Api;
        api.DestroyPipeline(_vk.Device, Pipeline, null);
        api.DestroyPipelineLayout(_vk.Device, Layout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, SamplerLayout, null);
    }
}
