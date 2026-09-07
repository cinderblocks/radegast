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

    /// <summary>
    /// Not created by <see cref="Create"/> -- <see cref="VkViewportControl"/> creates the
    /// water reflection render pass itself much later during init (inside the best-effort,
    /// separately-failable water-init block), well after this pipeline object already exists.
    /// Building this variant against <c>_renderPass</c> (the only render pass in scope at
    /// <see cref="Create"/> time) like the other two variants used to do is what produced a
    /// real, validation-layer-confirmed render-pass-compatibility violation every time the
    /// reflection pass drew with it (dependencyCount mismatch: the reflection render pass has
    /// 2 subpass dependencies, the main scene pass this used to be built against has 1). Stays
    /// <c>default</c> (Handle == 0) until <see cref="CreateReflVariant"/> is called with the
    /// real reflection render pass; <see cref="Dispose"/> only destroys it if that happened.
    /// </summary>
    public Pipeline ReflOpaque { get; private set; }

    /// <summary>
    /// Not created by <see cref="Create"/>, for the same reason <see cref="ReflOpaque"/> isn't:
    /// see <see cref="CreateSsrVariant"/>'s own doc comment. Stays <c>default</c> (Handle == 0)
    /// until that's called; <see cref="Dispose"/> only destroys it if that happened.
    /// </summary>
    public Pipeline Ssr { get; private set; }

    public DescriptorSetLayout PerFrameLayout { get; }
    public DescriptorSetLayout PerPassSamplersLayout { get; }
    public DescriptorSetLayout PerMaterialLayout { get; }

    private readonly VkContext _vk;

    private VkPrimPipeline(VkContext vk, PipelineLayout layout, Pipeline opaque, Pipeline alpha,
        DescriptorSetLayout perFrameLayout, DescriptorSetLayout perPassSamplersLayout, DescriptorSetLayout perMaterialLayout)
    {
        _vk = vk;
        Layout = layout;
        Opaque = opaque;
        Alpha = alpha;
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
        Pipeline opaque = default, alpha = default;
        try
        {
            CreatePipelineInternal(vk, renderPass, perFrameLayout, perPassSamplersLayout, perMaterialLayout,
                out layout, out opaque, out alpha);
            return new VkPrimPipeline(vk, layout, opaque, alpha, perFrameLayout, perPassSamplersLayout, perMaterialLayout);
        }
        catch
        {
            var api = vk.Api;
            if (opaque.Handle != 0) api.DestroyPipeline(vk.Device, opaque, null);
            if (alpha.Handle != 0) api.DestroyPipeline(vk.Device, alpha, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            api.DestroyDescriptorSetLayout(vk.Device, perFrameLayout, null);
            api.DestroyDescriptorSetLayout(vk.Device, perPassSamplersLayout, null);
            api.DestroyDescriptorSetLayout(vk.Device, perMaterialLayout, null);
            throw;
        }
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, RenderPass renderPass,
        DescriptorSetLayout perFrameLayout, DescriptorSetLayout perPassSamplersLayout, DescriptorSetLayout perMaterialLayout,
        out PipelineLayout layout, out Pipeline opaque, out Pipeline alpha)
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

        // Viewport/scissor are dynamic state -- actual
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
    }

    /// <summary>
    /// Builds the <see cref="ReflOpaque"/> variant against the real water-reflection render
    /// pass, once it exists (see <see cref="ReflOpaque"/>'s own doc comment for why this can't
    /// happen inside <see cref="Create"/>). Reloads the same prim.vert/prim.frag modules and
    /// re-derives the same vertex-input/multisample/depth-stencil/blend/dynamic state
    /// <see cref="CreatePipelineInternal"/> builds for Opaque -- ReflOpaque uses that exact
    /// state, just against a different render pass and a mirrored FrontFace (see
    /// <c>reflRasterizationState</c> below) -- rather than threading extra out-parameters back
    /// out of that method for a variant it may never need to build. Caller (the water-init
    /// block) already wraps this in its own best-effort try/catch: if this throws, water and
    /// its reflection are disabled for the panel, matching every other failure in that block.
    /// </summary>
    public unsafe void CreateReflVariant(RenderPass reflRenderPass)
    {
        var vk = _vk;
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

        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        // Hardcoded to the FLIP of CreatePipelineInternal's own rasterizationState.FrontFace
        // (Clockwise there, so CounterClockwise here) -- NOT re-derived from that method's local
        // variable, since this now runs standalone, potentially never (water init is optional).
        // If that FrontFace value ever changes, this one must be updated by hand to match.
        // GL's DrawWaterReflection flips FrontFace from GL_CCW to GL_CW to compensate the
        // reflection matrix's Z-mirror (confirmed by reading DrawWaterReflection directly), a
        // winding-parity flip regardless of which convention is "the unflipped one." A mirrored
        // reflection that reads hollow/inside-out (visible backfaces, missing frontfaces) is the
        // tell this flip guessed wrong, not a re-derivation on paper.
        var reflRasterizationState = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.CounterClockwise,
            LineWidth = 1f
        };

        var multisampleState = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        // Same depth/blend state as Opaque (GL's DrawWaterReflection draws through its normal
        // opaque path, just with a mirrored view matrix and flipped FrontFace -- confirmed by
        // reading the call site, not a new draw mode).
        var opaqueDepthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.Less
        };
        var opaqueBlendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = false,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
        };
        var reflColorBlendState = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &opaqueBlendAttachment
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
            PRasterizationState = &reflRasterizationState,
            PMultisampleState = &multisampleState,
            PDepthStencilState = &opaqueDepthStencilState,
            PColorBlendState = &reflColorBlendState,
            PDynamicState = &dynamicState,
            Layout = Layout,
            RenderPass = reflRenderPass,
            Subpass = 0
        };
        vk.Api.CreateGraphicsPipelines(vk.Device, default, 1, &createInfo, null, out var reflOpaque).ThrowOnError();
        ReflOpaque = reflOpaque;
    }

    /// <summary>
    /// Builds the SSR redraw variant against the split-main-pass continuation render pass
    /// (<c>_mainScenePassContinuation</c>) -- same reasoning <see cref="CreateReflVariant"/>
    /// documents for why this can't happen inside <see cref="Create"/>: that render pass doesn't
    /// exist yet at that point. Unlike <see cref="ReflOpaque"/>, this draws from the REAL camera
    /// (not mirrored) using the exact same mesh/instance-MVP as pass 1's own Opaque draw for
    /// these faces -- it redraws them a second time, adding an SSR reflection term on top.
    /// <para>
    /// Depth-equal-OR-CLOSER, no depth WRITE: this must never corrupt what water/alpha/particles
    /// (drawn after it) test against, hence <c>DepthWriteEnable=false</c> -- same reasoning
    /// VkOcclusionQueryPipeline/VkOutlinePipeline already established for their own overlay
    /// passes this session. <c>DepthCompareOp=LessOrEqual</c>, not <c>Equal</c>: this is the SAME
    /// mesh/instance-MVP as pass 1's own draw, so <c>gl_Position</c> SHOULD come out
    /// bit-identical, but Equal has no safety margin if a driver ever produces even one ULP of
    /// difference between two distinct <c>Pipeline</c> objects built from the same source --
    /// LessOrEqual matches this codebase's own "redraw on top of what's already there" precedent
    /// rather than assuming bit-exact reproducibility across separate pipeline compilations.
    /// </para>
    /// <para>
    /// Blend: standard alpha-over. The SSR contribution's own alpha (computed in prim.frag's
    /// <c>ssrTrace</c>) carries the hit-confidence/edge-fade term -- a miss or a fully-faded-out
    /// ray writes alpha=0, leaving pass 1's own shading completely undisturbed underneath,
    /// mirroring how sampleDirShadow/samplePointShadowCube fall back to "fully lit" rather than a
    /// hard cutoff.
    /// </para>
    /// <para>
    /// Uses a GLSL specialization constant (<c>kIsSsrPass</c>, prim.frag) to select the SSR
    /// ray-march branch at PIPELINE-CREATION time, not a push constant or per-draw uniform --
    /// see this session's plan for the full reasoning (Layout has zero push-constant ranges, and
    /// a mid-frame second UpdatePerFrame write would corrupt already-recorded draws). This is
    /// the first use of specialization constants anywhere in this codebase.
    /// </para>
    /// </summary>
    public unsafe void CreateSsrVariant(RenderPass continuationRenderPass)
    {
        var vk = _vk;
        using var vert = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/prim.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/prim.frag.spv");
        using var entryPoint = new VkByteString("main");

        uint kIsSsrPassValue = 1;
        var specMapEntry = new SpecializationMapEntry { ConstantID = 0, Offset = 0, Size = (nuint)sizeof(uint) };
        var specInfo = new SpecializationInfo
        {
            MapEntryCount = 1,
            PMapEntries = &specMapEntry,
            DataSize = (nuint)sizeof(uint),
            PData = &kIsSsrPassValue
        };

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
                PName = entryPoint,
                // Only the fragment stage reads kIsSsrPass -- prim.vert never does.
                PSpecializationInfo = &specInfo
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

        // Same winding as Opaque (CullMode.BackBit, FrontFace.Clockwise) -- NOT mirrored like
        // ReflOpaque, since this draws from the real camera, not a reflected one.
        var rasterizationState = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.Clockwise,
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
            DepthWriteEnable = false,
            DepthCompareOp = CompareOp.LessOrEqual
        };

        var blendAttachment = new PipelineColorBlendAttachmentState
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
            PAttachments = &blendAttachment
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
            Layout = Layout,
            RenderPass = continuationRenderPass,
            Subpass = 0
        };
        vk.Api.CreateGraphicsPipelines(vk.Device, default, 1, &createInfo, null, out var ssr).ThrowOnError();
        Ssr = ssr;
    }

    public unsafe void Dispose()
    {
        var api = _vk.Api;
        api.DestroyPipeline(_vk.Device, Opaque, null);
        api.DestroyPipeline(_vk.Device, Alpha, null);
        if (ReflOpaque.Handle != 0) api.DestroyPipeline(_vk.Device, ReflOpaque, null);
        if (Ssr.Handle != 0) api.DestroyPipeline(_vk.Device, Ssr, null);
        api.DestroyPipelineLayout(_vk.Device, Layout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, PerFrameLayout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, PerPassSamplersLayout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, PerMaterialLayout, null);
    }
}
