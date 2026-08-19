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

// Pipeline layout + VkPipeline objects for the main geometry pass (prim.vert + prim.frag).
// Two variants -- Opaque and Alpha -- matching the GL original's _opaque/_alpha draw-list
// split. Both share one vertex-input state (VkMesh binding 0 + VkInstanceDrawer binding 1,
// always both bound) since the always-instanced decision (see prim.vert's own doc comment)
// collapsed what would otherwise have needed a 4-way Instanced/NonInstanced split.

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkPrimPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Opaque { get; }
    public Pipeline Alpha { get; }
    public Pipeline ReflOpaque { get; }
    public DescriptorSetLayout PerFrameLayout { get; }
    public DescriptorSetLayout PerPassSamplersLayout { get; }
    public DescriptorSetLayout PerMaterialLayout { get; }

    private readonly VkContext _vk;

    private VkPrimPipeline(VkContext vk, PipelineLayout layout, Pipeline opaque, Pipeline alpha, Pipeline reflOpaque,
        DescriptorSetLayout perFrameLayout, DescriptorSetLayout perPassSamplersLayout, DescriptorSetLayout perMaterialLayout)
    {
        _vk = vk;
        Layout = layout;
        Opaque = opaque;
        Alpha = alpha;
        ReflOpaque = reflOpaque;
        PerFrameLayout = perFrameLayout;
        PerPassSamplersLayout = perPassSamplersLayout;
        PerMaterialLayout = perMaterialLayout;
    }

    /// <summary>
    /// Every resource created here on a path that later throws is cleaned up before
    /// rethrowing -- confirmed necessary, not defensive-programming paranoia: the first live
    /// run of this method threw partway through (missing .spv files, an unrelated harness
    /// gap) and the validation layer reported 5 leaked objects (the pipeline layout + all 3
    /// descriptor set layouts) on device destruction, because the original version had no
    /// cleanup path for a mid-method failure. This is exactly the class of bug
    /// validation-layer testing exists to catch.
    /// </summary>
    public static unsafe VkPrimPipeline Create(VkContext vk, RenderPass renderPass)
    {
        var perFrameLayout = VkDescriptorSetLayouts.CreatePerFrameLayout(vk);
        var perPassSamplersLayout = VkDescriptorSetLayouts.CreatePerPassSamplersLayout(vk);
        var perMaterialLayout = VkDescriptorSetLayouts.CreatePerMaterialLayout(vk);

        PipelineLayout layout = default;
        Pipeline opaque = default, alpha = default, reflOpaque = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, perFrameLayout, perPassSamplersLayout, perMaterialLayout,
                out layout, out opaque, out alpha, out reflOpaque);
            return new VkPrimPipeline(vk, layout, opaque, alpha, reflOpaque, perFrameLayout, perPassSamplersLayout, perMaterialLayout);
        }
        catch
        {
            var api = vk.Api;
            if (opaque.Handle != 0) api.DestroyPipeline(vk.Device, opaque, null);
            if (alpha.Handle != 0) api.DestroyPipeline(vk.Device, alpha, null);
            if (reflOpaque.Handle != 0) api.DestroyPipeline(vk.Device, reflOpaque, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            api.DestroyDescriptorSetLayout(vk.Device, perFrameLayout, null);
            api.DestroyDescriptorSetLayout(vk.Device, perPassSamplersLayout, null);
            api.DestroyDescriptorSetLayout(vk.Device, perMaterialLayout, null);
            throw;
        }
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, RenderPass renderPass,
        DescriptorSetLayout perFrameLayout, DescriptorSetLayout perPassSamplersLayout, DescriptorSetLayout perMaterialLayout,
        out PipelineLayout layout, out Pipeline opaque, out Pipeline alpha, out Pipeline reflOpaque)
    {
        var setLayouts = stackalloc DescriptorSetLayout[3] { perFrameLayout, perPassSamplersLayout, perMaterialLayout };
        // No push-constant range: prim.vert/prim.frag read everything through descriptor
        // sets now that the non-instanced push-constant path was collapsed away (see
        // prim.vert's own doc comment). wireframe/picking pipelines have their own,
        // unrelated pipeline layouts with their own push-constant ranges -- not built here.
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 3,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 0
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/prim.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/prim.frag.spv");

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

        // Viewport/scissor are dynamic state (plan Section 5's pipeline-table note) -- actual
        // values set per-frame via vkCmdSetViewport/vkCmdSetScissor, not baked in here.
        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        // FrontFace = Clockwise here is empirically derived, not analytically re-derivable from
        // the projection/view matrix math: earlier attempts were tuned against a single
        // hand-picked throwaway test triangle in isolation, and a self-consistent wrong pair of
        // FrontFace + Y-flip can look identical to a right pair when that's the only geometry
        // ever checked. Real SL mesh data (built by PrimMeshBuilder/the mesh decoder to GL's own
        // winding convention) is the first independent witness this value has been checked
        // against. If winding turns out wrong again, look at the projection/view matrix
        // construction itself (Camera3D.GetViewMatrix / GetProjectionMatrix) before changing
        // this value again.
        var rasterizationState = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.Clockwise,
            LineWidth = 1f
        };

        // Water reflection pass: the SAME state as rasterizationState above except FrontFace
        // flipped -- GL's DrawWaterReflection flips FrontFace from GL_CCW to GL_CW to
        // compensate the reflection matrix's Z-mirror (confirmed by reading DrawWaterReflection
        // directly), which is a winding-parity flip regardless of which
        // convention is "the unflipped one" -- so this variant is simply "whatever
        // rasterizationState's own FrontFace is, flipped," derived FROM that field rather than
        // hardcoded, so the two can never drift out of sync if the main convention ever changes
        // again. Same empirical standard as rasterizationState's own FrontFace above: a mirrored
        // reflection that reads hollow/inside-out (visible backfaces, missing frontfaces) is the
        // tell that this flip guessed wrong, not a re-derivation on paper.
        var reflRasterizationState = rasterizationState with
        {
            FrontFace = rasterizationState.FrontFace == FrontFace.CounterClockwise
                ? FrontFace.Clockwise
                : FrontFace.CounterClockwise
        };

        var multisampleState = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        var opaqueDepthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.Less
        };
        var alphaDepthStencilState = opaqueDepthStencilState with { DepthWriteEnable = false };

        var opaqueBlendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = false,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
        };
        var alphaBlendAttachment = new PipelineColorBlendAttachmentState
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

        var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        {
            var opaqueColorBlendState = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &opaqueBlendAttachment
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
                PDepthStencilState = &opaqueDepthStencilState,
                PColorBlendState = &opaqueColorBlendState,
                PDynamicState = &dynamicState,
                Layout = layout,
                RenderPass = renderPass,
                Subpass = 0
            };
            vk.Api.CreateGraphicsPipelines(vk.Device, default, 1, &createInfo, null, out opaque).ThrowOnError();
        }
        {
            var alphaColorBlendState = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &alphaBlendAttachment
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
                PDepthStencilState = &alphaDepthStencilState,
                PColorBlendState = &alphaColorBlendState,
                PDynamicState = &dynamicState,
                Layout = layout,
                RenderPass = renderPass,
                Subpass = 0
            };
            vk.Api.CreateGraphicsPipelines(vk.Device, default, 1, &createInfo, null, out alpha).ThrowOnError();
        }
        {
            // ReflOpaque: same depth/blend state as Opaque (GL's DrawWaterReflection draws
            // through its normal opaque path, just with a mirrored view matrix and flipped
            // FrontFace -- confirmed by reading the call site, not a new draw mode) --
            // only rasterizationState differs (reflRasterizationState, defined above).
            var reflColorBlendState = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &opaqueBlendAttachment
            };
            var createInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInputState,
                PInputAssemblyState = &inputAssemblyState,
                PViewportState = &viewportState,
                PRasterizationState = &reflRasterizationState,
                PMultisampleState = &multisampleState,
                PDepthStencilState = &opaqueDepthStencilState,
                PColorBlendState = &reflColorBlendState,
                PDynamicState = &dynamicState,
                Layout = layout,
                RenderPass = renderPass,
                Subpass = 0
            };
            vk.Api.CreateGraphicsPipelines(vk.Device, default, 1, &createInfo, null, out reflOpaque).ThrowOnError();
        }
    }

    public unsafe void Dispose()
    {
        var api = _vk.Api;
        api.DestroyPipeline(_vk.Device, Opaque, null);
        api.DestroyPipeline(_vk.Device, Alpha, null);
        api.DestroyPipeline(_vk.Device, ReflOpaque, null);
        api.DestroyPipelineLayout(_vk.Device, Layout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, PerFrameLayout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, PerPassSamplersLayout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, PerMaterialLayout, null);
    }
}
