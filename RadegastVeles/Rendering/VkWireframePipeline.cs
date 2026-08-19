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

// Pipeline layout + VkPipeline for the wireframe overlay pass (plan Section 5's pipeline
// table, row 2: wireframe.vert + wireframe.frag). Mirrors GL's ES/ANGLE fallback path
// (DrawFacesWireframeEs / DepthFunction.Lequal + DepthMask(false))
// rather than desktop GL's PolygonMode.Line path: PolygonMode.Line requires the
// fillModeNonSolid device feature, which VkContext.cs does not currently request (its
// PhysicalDeviceFeatures is default-constructed, i.e. every optional feature disabled).
// Drawing the mesh's own line-index buffer (VkMesh.DrawLines(), built in Section 6) as
// PrimitiveTopology.LineList needs no such feature -- it's supported unconditionally, and
// it's the same technique GL already falls back to on ANGLE. No per-instance batching:
// wireframe.vert takes a single mat4 push constant per draw, matching
// DrawFacesWireframeEs's own per-face shader.Set+mesh.DrawLines() loop.
using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkWireframePipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }

    private readonly VkContext _vk;

    private VkWireframePipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
    }

    /// <summary>Exception-safe on partial failure, matching <see cref="VkPrimPipeline.Create"/>'s
    /// established pattern -- every pipeline-creation factory in this port cleans up on the
    /// throw path.</summary>
    public static unsafe VkWireframePipeline Create(VkContext vk, RenderPass renderPass)
    {
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, out layout, out pipeline);
            return new VkWireframePipeline(vk, layout, pipeline);
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
        // wireframe.vert's PerDraw push-constant block is one mat4 (64 bytes), vertex-stage
        // only -- no descriptor sets at all (see wireframe.vert/wireframe.frag: no uniform
        // blocks, no samplers).
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = 64
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
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/wireframe.frag.spv");

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

        // Single binding 0 = VkMesh's own interleaved VBO (same 48-byte stride as the main
        // geometry pass -- DrawLines() binds the same mesh VBO the opaque/alpha passes use),
        // but only the position attribute is declared: wireframe.vert reads nothing else.
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

        // LineList, not TriangleList -- VkMesh.DrawLines() draws the lazily-built line-index
        // buffer (mesh edges, one line segment per unique triangle edge), not the triangle
        // index buffer.
        var inputAssemblyState = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.LineList
        };

        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        // CullMode = None, matching GlApi.Gl.Disable(EnableCap.CullFace) in
        // GL's wireframe block -- winding doesn't apply to a line topology
        // anyway, but kept explicit rather than left at whatever CullMode's default would be.
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

        // DepthCompareOp = LessOrEqual + DepthWriteEnable = false: matches GL's ES-fallback
        // wireframe path exactly (DepthFunc(LEQUAL) so coincident line/triangle depth values
        // don't z-fight, since the wireframe pass runs after the opaque/alpha passes wrote
        // depth for those same vertex positions; DepthMask(false) so the overlay doesn't
        // corrupt the depth buffer for anything drawn after it).
        var depthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = false,
            DepthCompareOp = CompareOp.LessOrEqual
        };

        // wireframe.frag always outputs alpha=1.0 (see its own doc comment), so blending is a
        // no-op either way -- left disabled rather than enabled-but-inert, for clarity.
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
