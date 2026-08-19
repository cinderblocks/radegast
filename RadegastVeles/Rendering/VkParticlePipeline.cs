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

// Pipeline layout + a LAZILY-CREATED, CACHED set of VkPipeline blend variants for the particle
// pass (particle.vert/particle.frag). Real, deliberate design fork from every other pipeline in
// this migration: GL calls glBlendFunc((BlendingFactor)sub.BlendSrc, (BlendingFactor)sub.BlendDst)
// PER PARTICLE SUBMISSION -- sub.BlendSrc/BlendDst come from
// Primitive.ParticleSystem.BlendFuncSource/Dest, an SL-protocol byte that can in principle
// select any of 10 blend factors independently for source/dest (ParticleViewerDriver.
// MapBlendFunc), i.e. up to 100 legal combinations. Vulkan bakes blend factors into the
// VkPipeline object, not a per-draw dynamic call -- baking a pipeline per theoretically-possible
// combination is absurd, and VK_EXT_extended_dynamic_state3's per-draw blend-equation command is
// unconfirmed on this dev machine/driver. Real-world SL content only ever exercises a handful of
// distinct (Src,Dst) pairs (normal alpha + additive glow being the overwhelming majority) -- so
// this class creates pipeline variants ON DEMAND, keyed by the exact (Src,Dst) pair requested,
// and caches them: the variant count self-sizes to whatever the scene actually contains, never a
// fixed enumeration.
//
// CRITICAL CALLER CONSTRAINT: GetOrCreate must be called OUTSIDE an active render pass (creating
// a VkPipeline mid-render-pass is illegal) -- VkViewportControl's DrainPendingParticles calls it
// for every (Src,Dst) pair present in _particleMap BEFORE CmdBeginRenderPass, at the same point
// scene-object uploads are drained, not from inside DrawParticles itself.

using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkParticlePipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public DescriptorSetLayout SetLayout { get; }

    private readonly VkContext _vk;
    private readonly RenderPass _renderPass;
    private readonly Dictionary<(BlendFactor Src, BlendFactor Dst), Pipeline> _variants = new();

    private VkParticlePipeline(VkContext vk, RenderPass renderPass, PipelineLayout layout, DescriptorSetLayout setLayout)
    {
        _vk = vk;
        _renderPass = renderPass;
        Layout = layout;
        SetLayout = setLayout;
    }

    /// <summary>Exception-safe on partial failure, matching every other pipeline-creation
    /// factory in this port. Only builds the shared layout/set-layout -- no VkPipeline variant
    /// exists yet until <see cref="GetOrCreate"/> is first called for a given blend pair.</summary>
    public static VkParticlePipeline Create(VkContext vk, RenderPass renderPass)
    {
        var setLayout = CreateSetLayout(vk);
        PipelineLayout layout = default;
        try
        {
            layout = CreatePipelineLayout(vk, setLayout);
            return new VkParticlePipeline(vk, renderPass, layout, setLayout);
        }
        catch
        {
            if (layout.Handle != 0) vk.Api.DestroyPipelineLayout(vk.Device, layout, null);
            vk.Api.DestroyDescriptorSetLayout(vk.Device, setLayout, null);
            throw;
        }
    }

    /// <summary>Set 0, binding 0: uAlbedo, fragment-only -- particle.frag's only descriptor
    /// binding (uHasTexture/uGlow live in the push-constant block instead).</summary>
    private static DescriptorSetLayout CreateSetLayout(VkContext vk)
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    private static PipelineLayout CreatePipelineLayout(VkContext vk, DescriptorSetLayout setLayout)
    {
        // particle.vert/particle.frag's shared PushConstants block: mat4 uMvp (offset 0, 64B) +
        // int uHasTexture (offset 64, 4B) + float uGlow (offset 68, 4B) = 72 bytes total,
        // verified via Marshal.SizeOf/OffsetOf against VkParticlePushConstants the same way
        // every other push-constant/UBO struct in this port was checked. ONE range covering
        // both stages (Vertex|Fragment), matching wireframe.vert/picking.frag's established
        // "single range, disjoint per-stage active spans" pattern rather than two ranges.
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = (uint)sizeof(VkParticlePushConstants)
        };
        var setLayoutLocal = setLayout;
        var layoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayoutLocal,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    /// <summary>Returns the cached VkPipeline for this (src, dst) blend-factor pair, creating and
    /// caching it on first use. See this class's own header comment for why this must be called
    /// OUTSIDE an active render pass.</summary>
    public Pipeline GetOrCreate(BlendFactor src, BlendFactor dst)
    {
        var key = (src, dst);
        if (_variants.TryGetValue(key, out var existing)) return existing;

        var pipeline = CreatePipelineVariant(src, dst);
        _variants[key] = pipeline;
        return pipeline;
    }

    private Pipeline CreatePipelineVariant(BlendFactor src, BlendFactor dst)
    {
        using var vert = VkShaderModule.LoadFromFile(_vk, "Rendering/shader_data/vulkan/particle.vert.spv");
        using var frag = VkShaderModule.LoadFromFile(_vk, "Rendering/shader_data/vulkan/particle.frag.spv");

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

        // Single binding 0, 36-byte stride (9 floats), 3 attributes -- matches GlParticleBuffer's
        // own CPU-billboard-expansion vertex layout exactly: position(vec3)@0, texcoord(vec2)@12,
        // color(vec4)@20. No instance binding -- particles are NOT instanced, each is 6 literal
        // per-vertex billboard-quad vertices expanded on the CPU (see VkParticleBuffer.cs).
        var attributes = stackalloc VertexInputAttributeDescription[3]
        {
            new VertexInputAttributeDescription { Binding = 0, Location = 0, Format = Format.R32G32B32Sfloat, Offset = 0 },
            new VertexInputAttributeDescription { Binding = 0, Location = 1, Format = Format.R32G32Sfloat, Offset = 12 },
            new VertexInputAttributeDescription { Binding = 0, Location = 2, Format = Format.R32G32B32A32Sfloat, Offset = 20 }
        };
        var binding = new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = 36,
            InputRate = VertexInputRate.Vertex
        };
        var vertexInputState = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = 3,
            PVertexAttributeDescriptions = attributes
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

        // CullMode = None, matching GlApi.Gl.Disable(EnableCap.CullFace) in DrawParticles --
        // camera-facing billboards have no consistent winding to cull against anyway.
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

        // DepthTestEnable=true/DepthWriteEnable=false/CompareOp=Less matches VkPrimPipeline's
        // own Alpha variant exactly (GL's DepthMask(false) in DrawParticles, no explicit
        // DepthFunc call there -- inherits whatever the main pass already set, which is Less).
        var depthStencilState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = false,
            DepthCompareOp = CompareOp.Less
        };

        // The actual per-variant state: GL's glBlendFunc(src,dst) sets BOTH the RGB and alpha
        // blend factors to the SAME (src,dst) pair (glBlendFunc, not glBlendFuncSeparate) --
        // mirrored here by using src/dst for both Color and Alpha factors, not defaulting the
        // alpha channel to a different pair the way this migration's other blended pipelines
        // (which came from separate GL calls) do.
        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = true,
            SrcColorBlendFactor = src,
            DstColorBlendFactor = dst,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = src,
            DstAlphaBlendFactor = dst,
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
            Layout = Layout,
            RenderPass = _renderPass,
            Subpass = 0
        };
        _vk.Api.CreateGraphicsPipelines(_vk.Device, default, 1, &createInfo, null, out var pipeline).ThrowOnError();
        return pipeline;
    }

    public void Dispose()
    {
        var api = _vk.Api;
        foreach (var pipeline in _variants.Values)
            api.DestroyPipeline(_vk.Device, pipeline, null);
        _variants.Clear();
        api.DestroyPipelineLayout(_vk.Device, Layout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, SetLayout, null);
    }

}

/// <summary>C# mirror of particle.vert/particle.frag's shared PushConstants block. Offsets
/// hand-derived and verified via Marshal.SizeOf/OffsetOf: mat4(0,64B) + int(64,4B) + float(68,4B)
/// = 72 bytes, no padding needed since neither scalar requires 16-byte alignment.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 72)]
internal struct VkParticlePushConstants
{
    [System.Runtime.InteropServices.FieldOffset(0)] public System.Numerics.Matrix4x4 Mvp;
    [System.Runtime.InteropServices.FieldOffset(64)] public int HasTexture;
    [System.Runtime.InteropServices.FieldOffset(68)] public float Glow;
}
