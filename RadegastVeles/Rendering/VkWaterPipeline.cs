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

// Pipeline layout + VkPipeline for the water-surface pass (pipeline table
// row 10: water.vert + water.frag). Drawn as a full-screen triangle into the main render pass
// (same family as VkSkyPipeline -- an EMPTY vertex-input state, no VkMesh/VkInstanceDrawer
// bindings), AFTER the opaque geometry pass and BEFORE the alpha pass (mirrors
// GL's own DrawWater call-site placement, "correct depth test... before alpha
// geometry").
//
// Set 0 is NOT owned here -- it's VkPrimPipeline.PerFrameLayout, passed in and reused directly,
// same pattern as VkSkyPipeline/VkShadowPipeline (one shared VkDescriptorSetLayout object, one
// shared _frameSets.FrameSet buffer, bound at set 0 for the main geometry pass, the sky pass,
// and this one). Set 1 (WaterPass UBO + 3 samplers) IS owned here.

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkWaterPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }
    public DescriptorSetLayout WaterPassLayout { get; }

    private readonly VkContext _vk;

    private VkWaterPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline, DescriptorSetLayout waterPassLayout)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
        WaterPassLayout = waterPassLayout;
    }

    /// <summary>Exception-safe on partial failure, matching every other pipeline-creation
    /// factory in this port. Does NOT clean up <paramref name="perFrameLayout"/> on failure --
    /// that object is owned by <see cref="VkPrimPipeline"/>, not this class.</summary>
    public static unsafe VkWaterPipeline Create(VkContext vk, RenderPass renderPass, DescriptorSetLayout perFrameLayout)
    {
        var waterPassLayout = VkDescriptorSetLayouts.CreateWaterPassLayout(vk);

        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, perFrameLayout, waterPassLayout, out layout, out pipeline);
            return new VkWaterPipeline(vk, layout, pipeline, waterPassLayout);
        }
        catch
        {
            var api = vk.Api;
            if (pipeline.Handle != 0) api.DestroyPipeline(vk.Device, pipeline, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            api.DestroyDescriptorSetLayout(vk.Device, waterPassLayout, null);
            throw;
        }
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, RenderPass renderPass,
        DescriptorSetLayout perFrameLayout, DescriptorSetLayout waterPassLayout,
        out PipelineLayout layout, out Pipeline pipeline)
    {
        var setLayouts = stackalloc DescriptorSetLayout[2] { perFrameLayout, waterPassLayout };
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 0
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/water.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/water.frag.spv");

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

        // No vertex buffer at all -- water.vert generates the full-screen triangle entirely
        // from gl_VertexIndex, same as sky.vert/quad.vert.
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

        // CullMode = None, matching GL's DrawWater (disables cull for the full-screen triangle,
        // same reasoning as DrawSky -- winding doesn't matter with culling off).
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

        // DepthTestEnable/DepthWriteEnable = true, DepthCompareOp = LessOrEqual -- matches GL's
        // DepthFunc(Lequal) for DrawWater (confirmed by reading the call site).
        // DepthWriteEnable=true is required here, not just "on by default": water.frag
        // writes gl_FragDepth explicitly (the real ray-plane hit depth, not the full-screen
        // triangle's own depth=1 clip position), and that written value must land in the depth
        // buffer so the LATER alpha pass correctly tests against the water surface's true depth.
        var depthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.LessOrEqual
        };

        // Standard alpha blend, matching GL's Enable(Blend)/BlendFunc(SrcAlpha,OneMinusSrcAlpha)
        // for DrawWater.
        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = true,
            SrcColorBlendFactor = BlendFactor.SrcAlpha,
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add,
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
        api.DestroyDescriptorSetLayout(_vk.Device, WaterPassLayout, null);
    }
}
