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

// Pipeline layout + VkPipeline for the directional shadow depth-caster pass (prim.vert +
// shadow_depth.frag). Reuses the EXISTING vulkan/prim.vert (not a new vertex shader), same
// reasoning as VkGNormPipeline.cs: GL's directional/point shadow passes draw through prim.vert's
// non-instanced push-constant path, which Vulkan's prim.vert no longer has, so this pass goes
// through the SAME VkInstanceDrawer batched-instance path the main opaque pass uses -- a
// SEPARATE instance-data region (baked with the light's view/proj instead of the main camera's),
// uploaded in the SAME combined per-frame instance buffer before this pass's draw commands are
// recorded (see VkViewportControl.RenderFrame's own comment on why). Vertex input state is
// therefore IDENTICAL to VkPrimPipeline's/VkGNormPipeline's (VkMesh binding 0 + VkInstanceDrawer
// binding 1).
//
// Set 0 is NOT owned here -- it's VkPrimPipeline.PerFrameLayout, passed in and reused directly,
// same pattern as VkGNormPipeline/VkSkyPipeline. shadow_depth.frag itself declares no
// descriptors at all (empty main(), see its own doc comment) but the pipeline layout still needs
// set 0 bound for prim.vert's sake (specifically its vWorldPos computation, which is unused by
// this pass but still present in the compiled SPIR-V) -- reusing the same VkDescriptorSetLayout
// OBJECT the main pipeline uses is what makes that legal without a second, redundant layout.

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkShadowPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }

    private readonly VkContext _vk;

    private VkShadowPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
    }

    /// <summary>Exception-safe on partial failure, matching every other pipeline-creation
    /// factory in this port. Does NOT clean up <paramref name="perFrameLayout"/> on failure --
    /// that object is owned by <see cref="VkPrimPipeline"/>, not this class.</summary>
    public static unsafe VkShadowPipeline Create(VkContext vk, RenderPass shadowRenderPass, DescriptorSetLayout perFrameLayout)
    {
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, shadowRenderPass, perFrameLayout, out layout, out pipeline);
            return new VkShadowPipeline(vk, layout, pipeline);
        }
        catch
        {
            var api = vk.Api;
            if (pipeline.Handle != 0) api.DestroyPipeline(vk.Device, pipeline, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            throw;
        }
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, RenderPass shadowRenderPass,
        DescriptorSetLayout perFrameLayout, out PipelineLayout layout, out Pipeline pipeline)
    {
        var setLayout = perFrameLayout;
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 0
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/prim.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/shadow_depth.frag.spv");

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

        var meshBinding = VkMesh.VertexInputBindingDescription;
        var instBinding = VkInstanceDrawer.VertexInputBindingDescription;
        var bindings = stackalloc VertexInputBindingDescription[2] { meshBinding, instBinding };

        var meshAttrs = VkMesh.VertexInputAttributeDescriptions;
        var instAttrs = VkInstanceDrawer.VertexInputAttributeDescriptions;
        var attrCount = meshAttrs.Length + instAttrs.Length;
        var attrs = stackalloc VertexInputAttributeDescription[attrCount];
        for (var i = 0; i < meshAttrs.Length; i++) attrs[i] = meshAttrs[i];
        for (var i = 0; i < instAttrs.Length; i++) attrs[meshAttrs.Length + i] = instAttrs[i];

        var vertexInputState = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 2,
            PVertexBindingDescriptions = bindings,
            VertexAttributeDescriptionCount = (uint)attrCount,
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

        // CullMode = None: GL's RenderDirectionalShadow/RenderPointShadows both explicitly
        // Disable(CullFace) -- a single-sided plane must still cast a shadow from whichever
        // face the light hits, regardless of which side faces the shadow camera.
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
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.Less
        };

        // No PColorBlendAttachments at all -- this render pass has zero colour attachments
        // (see VkRenderPass.CreateShadowDepthPass), matching that a
        // PipelineColorBlendStateCreateInfo with AttachmentCount=0 is the correct, legal
        // counterpart rather than a 1-attachment state with BlendEnable=false.
        var colorBlendState = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 0
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
            RenderPass = shadowRenderPass,
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
