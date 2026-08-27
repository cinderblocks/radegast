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

// Pipeline layout + VkPipeline for the SL-style selection-outline pass (outline.vert +
// outline.frag): an "inverted hull" -- the selected object's own faces redrawn with vertices
// pushed out along their normals (outline.vert), with only the expanded shell's BACK faces
// rasterized (CullMode.Front, the opposite of the main opaque pass). Everywhere except the
// silhouette rim, those back faces sit behind the real (un-expanded) surface the opaque pass
// already drew and lose the depth test; right at the silhouette the expanded shell pokes out
// past the real geometry's edge, showing as a thin colored rim. Structurally identical to
// VkWireframePipeline.cs (same push-constant-only layout, same depth-test-without-write
// choice) except for the vertex input (position+normal, not position-only -- outline.vert
// needs the normal to expand along) and TriangleList/CullMode.Front instead of
// LineList/CullMode.None.
using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkOutlinePipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }

    private readonly VkContext _vk;

    private VkOutlinePipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
    }

    /// <summary>Exception-safe on partial failure, matching <see cref="VkWireframePipeline.Create"/>'s
    /// established pattern.</summary>
    public static unsafe VkOutlinePipeline Create(VkContext vk, RenderPass renderPass)
    {
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, out layout, out pipeline);
            return new VkOutlinePipeline(vk, layout, pipeline);
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
        // outline.vert's PerDraw push-constant block is one mat4 (64 bytes), vertex-stage
        // only -- same layout as wireframe.vert, no descriptor sets.
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

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/outline.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/outline.frag.spv");

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

        // Binding 0 = VkMesh's own interleaved VBO, position (location 0) + normal
        // (location 1) -- outline.vert reads both; texcoord/tangent are left undeclared,
        // same as VkWireframePipeline only declaring position.
        var meshBinding = VkMesh.VertexInputBindingDescription;
        var meshAttrs = VkMesh.VertexInputAttributeDescriptions;
        var attrs = stackalloc VertexInputAttributeDescription[2] { meshAttrs[0], meshAttrs[1] };
        var vertexInputState = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &meshBinding,
            VertexAttributeDescriptionCount = 2,
            PVertexAttributeDescriptions = attrs
        };

        // TriangleList, not LineList -- this draws the mesh's real triangle index buffer
        // (VkMesh.Draw()), not the wireframe edge buffer.
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

        // CullMode.Front -- the inverted-hull trick: cull the expanded shell's FRONT faces so
        // only its back faces rasterize. Those sit behind the real surface everywhere except
        // right at the silhouette, where the expansion pokes out past the real edge.
        // FrontFace = Clockwise (NOT CounterClockwise like VkWireframePipeline) -- this must
        // match VkPrimPipeline's own empirically-derived winding (VkPrimPipeline.cs's opaque
        // rasterizationState uses FrontFace.Clockwise + CullMode.BackBit), since "front" here
        // is meaningless in isolation -- it only means whatever this render target's actual
        // winding convention is. Getting this backwards culls the true back faces and keeps
        // the true front faces instead: the expanded shell's near side then sits IN FRONT of
        // the real surface everywhere (not just the silhouette) and passes the depth test
        // across the whole visible face, painting it solid instead of a thin rim.
        // VkWireframePipeline's own CounterClockwise choice never had to get this right, since
        // CullMode.None there means winding is irrelevant to a LineList draw.
        var rasterizationState = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.FrontBit,
            FrontFace = FrontFace.Clockwise,
            LineWidth = 1f
        };

        var multisampleState = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        // DepthCompareOp = LessOrEqual + DepthWriteEnable = false, matching
        // VkWireframePipeline: the outline needs to lose the depth test against the real
        // surface everywhere but the silhouette rim, and must not corrupt the depth buffer
        // for particles (drawn immediately after) or the next frame.
        var depthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = false,
            DepthCompareOp = CompareOp.LessOrEqual
        };

        // outline.frag always outputs alpha=1.0, so blending is a no-op either way -- left
        // disabled rather than enabled-but-inert, matching VkWireframePipeline.
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
