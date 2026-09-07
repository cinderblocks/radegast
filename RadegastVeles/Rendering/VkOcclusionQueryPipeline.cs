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

// Pipeline for drawing occlusion-query proxy boxes (occlusion.vert + occlusion.frag): a cheap
// expanded-AABB unit cube per candidate scene object, drawn with color writes disabled and
// wrapped in a hardware occlusion query (VkViewportControl.RecordOcclusionQueries) to test
// whether the object is visible against the depth buffer the opaque pass just wrote. Structurally
// close to VkOutlinePipeline.cs/VkWireframePipeline.cs (same push-constant-only layout, same
// position-only vertex input, same depth-test-without-write posture) with two deliberate
// differences: CullMode.None (not Front/Back -- the camera can be inside or immediately beside a
// proxy box, and culling either winding risks rasterizing zero fragments exactly when the object
// should read as visible, which would misreport as occluded) and ColorWriteMask=0 (not the
// fragment shader) is what makes the draw invisible -- occlusion.frag still needs to exist and
// run (a query result requires real fragment invocations), it just writes nothing anyone sees.
using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkOcclusionQueryPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }

    private readonly VkContext _vk;

    private VkOcclusionQueryPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
    }

    /// <summary>Exception-safe on partial failure, matching <see cref="VkOutlinePipeline.Create"/>'s
    /// established pattern. <paramref name="renderPass"/> is <c>_renderPass</c> -- render-pass
    /// COMPATIBILITY (identical attachment formats/subpass shape shared with
    /// <c>_mainScenePassOpaque</c>/<c>_mainScenePassContinuation</c>, the same contract
    /// <c>_prim</c> already relies on) means this one pipeline object is usable inside the
    /// water-refraction split main pass too -- no second variant needed.</summary>
    public static unsafe VkOcclusionQueryPipeline Create(VkContext vk, RenderPass renderPass)
    {
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, out layout, out pipeline);
            return new VkOcclusionQueryPipeline(vk, layout, pipeline);
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
        // occlusion.vert's PerDraw push-constant block is one mat4 (64 bytes), vertex-stage
        // only -- same layout as wireframe.vert/outline.vert, no descriptor sets.
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

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/occlusion.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/occlusion.frag.spv");

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

        // Binding 0 = VkMesh's own interleaved VBO, position only (location 0) -- the proxy mesh's
        // normal/uv/tangent floats are left zero and never read, same "declare a subset of the
        // shared stride" pattern VkWireframePipeline uses.
        var meshBinding = VkMesh.VertexInputBindingDescription;
        var meshAttrs = VkMesh.VertexInputAttributeDescriptions;
        var attrs = stackalloc VertexInputAttributeDescription[1] { meshAttrs[0] };
        var vertexInputState = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &meshBinding,
            VertexAttributeDescriptionCount = 1,
            PVertexAttributeDescriptions = attrs
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

        // CullMode.None: the camera can be standing inside or immediately beside a proxy box (an
        // object the player just walked up to) -- culling either winding risks rasterizing zero
        // fragments from exactly that viewpoint, which vkCmdEndQuery can't distinguish from "fully
        // occluded." Winding is therefore irrelevant, same reasoning VkWireframePipeline's own
        // CullMode.None already relies on for its LineList draw (though here it's a real
        // TriangleList, not just "winding is meaningless for lines").
        var rasterizationState = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.Clockwise,
            LineWidth = 1f
        };

        var multisampleState = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        // DepthWriteEnable=false: proxies must never corrupt the real depth buffer other passes
        // (water refraction's snapshot, particles, next frame) rely on. CompareOp.LessOrEqual
        // (not Less), matching VkOutlinePipeline's own reasoning: a proxy box exactly enclosing
        // real geometry must still pass the depth test at the boundary, not lose to it by
        // floating-point coincidence.
        var depthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = false,
            DepthCompareOp = CompareOp.LessOrEqual
        };

        // ColorWriteMask=0, not the shader, is what makes this draw invisible -- occlusion.frag
        // still runs (a query needs real fragment invocations to count samples), its output is
        // just masked off by this fixed-function stage.
        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = false,
            ColorWriteMask = 0
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
