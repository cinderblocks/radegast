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

// Pipeline layout + VkPipeline for the sky-dome pass (sky.vert + sky.frag). Drawn as the FIRST
// draw call inside the existing main render pass (not a separate offscreen pass, unlike SSAO's
// G-buffer/blur passes). No vertex input at all: a full-screen triangle generated entirely from
// gl_VertexIndex (see sky.vert), drawn with vkCmdDraw(cmd, 3, 1, 0, 0) -- the first pipeline in
// this port with an EMPTY VkPipelineVertexInputStateCreateInfo.
//
// Set 0 is NOT owned here -- it's VkPrimPipeline.PerFrameLayout, passed in and reused directly
// so the SAME _frameSets.FrameSet descriptor set (and its VkDescriptorSetLayout object) can be
// bound against both this pipeline and the main geometry pipeline without a layout-compatibility
// mismatch (Vulkan requires the actual VkDescriptorSetLayout object to match at that set index,
// not just an equivalently-shaped one). Set 1 (SkyPass UBO + cloud-noise sampler) IS owned here.
using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkSkyPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }
    public DescriptorSetLayout SkyPassLayout { get; }

    private readonly VkContext _vk;

    private VkSkyPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline, DescriptorSetLayout skyPassLayout)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
        SkyPassLayout = skyPassLayout;
    }

    /// <summary>Exception-safe on partial failure, matching every other pipeline-creation
    /// factory in this port (see VkPrimPipeline.Create's doc comment for why). Does NOT clean
    /// up <paramref name="perFrameLayout"/> on failure -- that object is owned by
    /// <see cref="VkPrimPipeline"/>, not this class.</summary>
    public static unsafe VkSkyPipeline Create(VkContext vk, RenderPass renderPass, DescriptorSetLayout perFrameLayout)
    {
        var skyPassLayout = VkDescriptorSetLayouts.CreateSkyPassLayout(vk);

        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, perFrameLayout, skyPassLayout, out layout, out pipeline);
            return new VkSkyPipeline(vk, layout, pipeline, skyPassLayout);
        }
        catch
        {
            var api = vk.Api;
            if (pipeline.Handle != 0) api.DestroyPipeline(vk.Device, pipeline, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            api.DestroyDescriptorSetLayout(vk.Device, skyPassLayout, null);
            throw;
        }
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, RenderPass renderPass,
        DescriptorSetLayout perFrameLayout, DescriptorSetLayout skyPassLayout,
        out PipelineLayout layout, out Pipeline pipeline)
    {
        var setLayouts = stackalloc DescriptorSetLayout[2] { perFrameLayout, skyPassLayout };
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 0
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/sky.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/sky.frag.spv");

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

        // No vertex buffer at all -- sky.vert generates the full-screen triangle entirely from
        // gl_VertexIndex (see its own doc comment). An empty vertex-input state is legal and
        // required here: declaring a binding with nothing bound at draw time would be the
        // actual illegal state.
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

        // CullMode = None, matching GL's DrawSky (GlApi.Gl.Disable(EnableCap.CullFace)) --
        // the full-screen triangle's winding is whatever
        // it happens to be, and it doesn't matter with culling off either way.
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

        // DepthTestEnable = false, DepthWriteEnable = false -- matches GL's DrawSky exactly
        // (DepthMask(false) + Disable(DepthTest)). Depth testing
        // is disabled entirely for this draw, which is correct since it's drawn first, before
        // anything else has written the depth buffer this frame, and it covers every pixel.
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
        api.DestroyDescriptorSetLayout(_vk.Device, SkyPassLayout, null);
    }
}
