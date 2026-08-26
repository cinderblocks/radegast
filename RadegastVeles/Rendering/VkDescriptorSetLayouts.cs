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

// Descriptor set layouts for the prim pipeline family (set 0/1/2 scheme). Binding numbers here
// must match the layout(set=,binding=) declarations in shader_data/vulkan/prim.vert, prim.frag
// and shadow.glsl exactly.

using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal static class VkDescriptorSetLayouts
{
    /// <summary>Set 0: the per-frame UBO (camera/lighting/atmosphere/point-lights/shadow
    /// state), bound once per pass. Read by both prim.vert (first 3 fields) and prim.frag
    /// (the rest) -- both stages must be able to bind the same descriptor set, hence
    /// Vertex|Fragment stage flags rather than splitting per-stage.</summary>
    public static unsafe DescriptorSetLayout CreatePerFrameLayout(VkContext vk)
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
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

    /// <summary>Set 1: per-pass samplers -- uSsaoMap (binding 0, prim.frag), uShadowMap/
    /// uPointShadowMap0/uPointShadowMap1 (bindings 1-3, shadow.glsl). Fragment-only; bound
    /// once per pass alongside set 0 but kept as a separate set since a pass might rebind
    /// samplers (e.g. between main and water-reflection pre-pass) without touching the
    /// per-frame UBO.</summary>
    public static unsafe DescriptorSetLayout CreatePerPassSamplersLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[4];
        for (uint i = 0; i < 4; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            };
        }
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    /// <summary>Set 2: per-material -- 5 samplers (albedo/normal/specular/metallicRoughness/
    /// emissive, bindings 0-4) plus the Material UBO (binding 5), all prim.frag. Changes per
    /// material, not per draw; faces sharing a material can share this set's bind.</summary>
    public static unsafe DescriptorSetLayout CreatePerMaterialLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[6];
        for (uint i = 0; i < 5; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            };
        }
        bindings[5] = new DescriptorSetLayoutBinding
        {
            Binding = 5,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 6,
            PBindings = bindings
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    /// <summary>Set 1 of the sky pipeline -- NOT shared with the prim pipeline family's own set 1 (per-pass samplers): the sky
    /// pipeline's set 0 reuses <see cref="VkPrimPipeline.PerFrameLayout"/> directly, but its
    /// set 1 is sky-only data nothing else needs (uInvViewProj/cloud parameters, binding 0
    /// UBO; the tiling cloud-noise texture, binding 1 sampler). Fragment-only -- sky.vert
    /// reads neither binding.</summary>
    public static unsafe DescriptorSetLayout CreateSkyPassLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[2]
        {
            new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            },
            new DescriptorSetLayoutBinding
            {
                Binding = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            }
        };
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    /// <summary>Set 0 of the SSAO pipeline -- binding 0 = the <c>SsaoParams</c> UBO (kernel + projection + screen/noise/radius/
    /// bias/strength), bindings 1-3 = uDepthTex/uNormalTex/uNoiseTex. Fragment-only:
    /// quad.vert (this pipeline's vertex shader) has no uniforms of its own.</summary>
    public static unsafe DescriptorSetLayout CreateSsaoParamsLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[4];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        for (uint i = 1; i < 4; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            };
        }
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }

    /// <summary>Set 0 of the SSAO-blur pipeline -- a single uSsaoTex sampler binding. uTexelSize is a push constant instead (see
    /// vulkan/ssaoblur.frag), not worth a UBO for two floats.</summary>
    /// <summary>Set 0 of the underwater post-process pipeline: binding 0 = the copied scene-color
    /// image (post-process input), bindings 1/2 = the water pipeline's own normal/dudv maps,
    /// reused here for the distortion/caustic samples (see underwater.frag). Fragment-only --
    /// quad.vert reads none of these.</summary>
    public static unsafe DescriptorSetLayout CreateUnderwaterSamplerLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint i = 0; i < 3; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
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

    public static unsafe DescriptorSetLayout CreateBlurSamplerLayout(VkContext vk)
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

    /// <summary>Set 1 of the water pipeline -- NOT
    /// shared with the prim pipeline family's own set 1: the water pipeline's set 0 reuses
    /// <see cref="VkPrimPipeline.PerFrameLayout"/> directly (same pattern as the sky pipeline's
    /// set 0), but its set 1 is water-only data nothing else needs. Binding 0 = the
    /// <c>WaterPass</c> UBO, bindings 1-3 = uReflectionTex/uNormalMap/uDudvMap. Fragment-only --
    /// water.vert reads none of these.</summary>
    public static unsafe DescriptorSetLayout CreateWaterPassLayout(VkContext vk)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[4];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        for (uint i = 1; i < 4; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            };
        }
        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings
        };
        vk.Api.CreateDescriptorSetLayout(vk.Device, in createInfo, null, out var layout).ThrowOnError();
        return layout;
    }
}
