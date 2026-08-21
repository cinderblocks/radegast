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

// Pipeline layout + VkPipeline for the picking pass (wireframe.vert + picking.frag).
// Structurally close to VkWireframePipeline.cs (same
// single-binding, position-only vertex input; reuses the main _renderPass) but with real
// differences, not a copy-paste: TriangleList (not LineList -- picking rasterizes solid
// faces, not edges), CullMode.None (GL's pick block explicitly disables
// face culling during the pick pass -- reusing the main opaque pipeline's back-face culling
// here would silently make back-facing geometry unpickable), and real depth test/write
// (matches GL's Enable(DepthTest)/DepthFunc(Less)/DepthMask(true) -- picking needs correct
// occlusion between faces, unlike the wireframe overlay which deliberately reads depth
// without writing it).
using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkPickPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }

    private readonly VkContext _vk;

    private VkPickPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
    }

    /// <summary>Exception-safe on partial failure, matching <see cref="VkPrimPipeline.Create"/>'s
    /// established pattern.</summary>
    public static unsafe VkPickPipeline Create(VkContext vk, RenderPass renderPass)
    {
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, out layout, out pipeline);
            return new VkPickPipeline(vk, layout, pipeline);
        }
        catch
        {
            var api = vk.Api;
            if (pipeline.Handle != 0) api.DestroyPipeline(vk.Device, pipeline, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            throw;
        }
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, RenderPass renderPass,
        out PipelineLayout layout, out Pipeline pipeline)
    {
        // ONE push-constant range covering both stages' actually-used bytes, not two separate
        // per-stage ranges: vulkan/wireframe.vert's PerDraw{mat4 uMvp} only touches bytes
        // 0-63; vulkan/picking.frag's PerDraw{mat4 uMvp; vec4 uPickColor} touches bytes 0-79.
        // A single {offset:0, size:80, stageFlags:Vertex|Fragment} range is a legal superset
        // for both (Vulkan only requires that every byte a stage's shader statically accesses
        // fall within SOME declared range for that stage) and is simpler than splitting.
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = 80
        };
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 0,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/wireframe.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/picking.frag.spv");

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

        // Single binding 0 = VkMesh's own interleaved VBO, position attribute only --
        // wireframe.vert reads nothing else. Same shape as VkWireframePipeline.cs.
        var meshBinding = VkMesh.VertexInputBindingDescription;
        var positionAttr = new VertexInputAttributeDescription
        {
            Binding = 0, Location = 0, Format = Format.R32G32B32Sfloat, Offset = 0
        };
        var vertexInputState = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &meshBinding,
            VertexAttributeDescriptionCount = 1,
            PVertexAttributeDescriptions = &positionAttr
        };

        // TriangleList: picking rasterizes solid faces (via VkMesh.Draw's triangle index
        // buffer), not edges.
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

        // CullMode = None: GL's pick block explicitly calls Disable(CullFace), unlike the main
        // opaque pipeline (which DOES cull). Copying BackBit here would be a silent regression:
        // back-facing geometry would become unpickable.
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

        // Real depth test AND write, matching GL's Enable(DepthTest)/DepthFunc(Less)/
        // DepthMask(true) -- picking needs correct near-face-wins occlusion, unlike the
        // wireframe overlay (LessOrEqual/no write) which deliberately reads depth without
        // corrupting it for later draws.
        var depthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.Less
        };

        // picking.frag always outputs the pick color with alpha=1 (see its own source); blend
        // is irrelevant to a readback target, left disabled.
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
    }
}
