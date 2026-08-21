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

// Compute pipeline for flexi-prim GPU vertex deformation (flexi.comp).
// Mirrors VkSkinPipeline.cs's shape exactly (the second compute pipeline in this migration) --
// two real differences from it, both mechanical, driven by flexi.comp's own declarations:
//   - 3 storage-buffer bindings (BaseVerts/SpineData/OutVerts), not 5 -- flexi has no per-vertex
//     joint-index/weight buffers the way skinning does.
//   - A 96-byte push-constant range (int+int+vec3+mat4, std430-packed -- see flexi.comp's own
//     header comment for the byte-offset derivation), not a single 4-byte int -- flexi.comp
//     takes per-prim uniforms (segment count, scale, attach transform) that skin.comp doesn't
//     need, since skin's equivalent per-vertex/per-joint data already lives in SSBOs.

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed class VkFlexiPipeline : IDisposable
{
    public PipelineLayout Layout { get; }
    public Pipeline Pipeline { get; }
    public DescriptorSetLayout SetLayout { get; }

    private readonly VkContext _vk;

    private VkFlexiPipeline(VkContext vk, PipelineLayout layout, Pipeline pipeline, DescriptorSetLayout setLayout)
    {
        _vk = vk;
        Layout = layout;
        Pipeline = pipeline;
        SetLayout = setLayout;
    }

    /// <summary>Exception-safe on partial failure, matching <see cref="VkSkinPipeline.Create"/>'s
    /// established pattern.</summary>
    public static unsafe VkFlexiPipeline Create(VkContext vk)
    {
        var setLayout = CreateSetLayout(vk);
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        try
        {
            CreatePipelineInternal(vk, setLayout, out layout, out pipeline);
            return new VkFlexiPipeline(vk, layout, pipeline, setLayout);
        }
        catch
        {
            var api = vk.Api;
            if (pipeline.Handle != 0) api.DestroyPipeline(vk.Device, pipeline, null);
            if (layout.Handle != 0) api.DestroyPipelineLayout(vk.Device, layout, null);
            api.DestroyDescriptorSetLayout(vk.Device, setLayout, null);
            throw;
        }
    }

    /// <summary>Set 0: BaseVerts/SpineData/OutVerts, bindings 0-2, matching flexi.comp's own
    /// declarations exactly. Compute-stage-only.</summary>
    private static unsafe DescriptorSetLayout CreateSetLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint i = 0; i < 3; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };
        }
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    private static unsafe void CreatePipelineInternal(VkContext vk, DescriptorSetLayout setLayout,
        out PipelineLayout layout, out Pipeline pipeline)
    {
        // flexi.comp's PushConstants block: int uVertexCount, int uSegmentCount, vec3 uScale,
        // mat4 uAttachTransform -- 96 bytes total (see flexi.comp's own header comment for the
        // std430 offset derivation: 0/4/16/32), matching VkFlexiPushConstants's C# layout.
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.ComputeBit,
            Offset = 0,
            Size = (uint)sizeof(VkFlexiPushConstants)
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
        vk.Api.CreatePipelineLayout(vk.Device, in layoutCreateInfo, null, out layout).ThrowOnError();

        using var comp = VkShaderModule.LoadFromFile(vk, "Rendering/shader_data/vulkan/flexi.comp.spv");
        using var entryPoint = new VkByteString("main");
        var stage = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = comp.Handle,
            PName = entryPoint
        };

        var createInfo = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = stage,
            Layout = layout
        };
        vk.Api.CreateComputePipelines(vk.Device, default, 1, &createInfo, null, out pipeline).ThrowOnError();
    }

    public unsafe void Dispose()
    {
        var api = _vk.Api;
        api.DestroyPipeline(_vk.Device, Pipeline, null);
        api.DestroyPipelineLayout(_vk.Device, Layout, null);
        api.DestroyDescriptorSetLayout(_vk.Device, SetLayout, null);
    }
}

/// <summary>
/// C# mirror of flexi.comp's PushConstants block. std430 offsets hand-derived and documented in
/// both flexi.comp's own header comment and VkFlexiPipeline's -- int(0)/int(4)/vec3(16, size 12,
/// padded to 16)/mat4(32, size 64), total 96 bytes. Field order matches the shader's declaration
/// order exactly (unlike VkSkyUbo.cs, which reordered for tighter packing -- not needed here
/// since this layout has no slack to reclaim either way).
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 96)]
internal struct VkFlexiPushConstants
{
    [System.Runtime.InteropServices.FieldOffset(0)] public int VertexCount;
    [System.Runtime.InteropServices.FieldOffset(4)] public int SegmentCount;
    [System.Runtime.InteropServices.FieldOffset(16)] public System.Numerics.Vector3 Scale;
    [System.Runtime.InteropServices.FieldOffset(32)] public System.Numerics.Matrix4x4 AttachTransform;
}
