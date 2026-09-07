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

// Owns the four descriptor sets the tonemap/bloom post-process chain binds, one per stage (see
// VkTonemapPipeline's own doc comment for the chain overview). Kept as one class since all four
// share a lifecycle (created/destroyed together with the rest of this pass group), same
// rationale as VkSsaoDescriptorSet bundling its SSAO-pass and SSAO-blur sets.
//
// Every binding here is rewritten only when the image it points at is (re)created -- on first
// creation and again on any panel resize (VkViewportControl.EnsureHdrColorTarget/
// EnsureBloomTargets), never per-frame -- same "rewrite rarely" contract
// VkSsaoDescriptorSet.UpdateGbufferInputs/UpdateBlurInput already document and rely on.

using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkPostProcessDescriptorSet : System.IDisposable
{
    public DescriptorSet ExtractSet { get; }
    public DescriptorSet BlurHSet { get; }
    public DescriptorSet BlurVSet { get; }
    public DescriptorSet TonemapSet { get; }
    public DescriptorSet GodRayMaskSet { get; }
    public DescriptorSet GodRayBlurSet { get; }

    private readonly VkContext _vk;
    private bool _disposed;

    public VkPostProcessDescriptorSet(VkContext vk, VkBloomExtractPipeline extractPipeline,
        VkBloomBlurPipeline blurPipeline, VkTonemapPipeline tonemapPipeline,
        VkGodRayMaskPipeline godRayMaskPipeline, VkGodRayBlurPipeline godRayBlurPipeline)
    {
        _vk = vk;

        ExtractSet = Allocate(vk, extractPipeline.SamplerLayout);
        BlurHSet = Allocate(vk, blurPipeline.SamplerLayout);
        BlurVSet = Allocate(vk, blurPipeline.SamplerLayout);
        TonemapSet = Allocate(vk, tonemapPipeline.SamplerLayout);
        GodRayMaskSet = Allocate(vk, godRayMaskPipeline.SamplerLayout);
        GodRayBlurSet = Allocate(vk, godRayBlurPipeline.SamplerLayout);
        // Every binding on all six sets is left unwritten here -- VkViewportControl's
        // EnsureHdrColorTarget/EnsureBloomTargets/EnsureGodRayTargets write them on first creation
        // (before any of these sets is ever bound for a real draw), via UpdateHdrInput/
        // UpdateBloomTargets/UpdateGodRayDepthInput/UpdateGodRayTargets below.
    }

    private static DescriptorSet Allocate(VkContext vk, DescriptorSetLayout layout)
    {
        var setLayout = layout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = vk.DescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout
        };
        vk.Api.AllocateDescriptorSets(vk.Device, &allocInfo, out var set).ThrowOnError();
        return set;
    }

    /// <summary>Rewrites ExtractSet's, TonemapSet's, and GodRayMaskSet's uSceneColor binding (all
    /// three sample the SAME full-res HDR buffer) -- call ONLY when that buffer is
    /// (re)created.</summary>
    public void UpdateHdrInput(DescriptorImageInfo hdrColorInfo)
    {
        var info = hdrColorInfo;
        var writes = stackalloc WriteDescriptorSet[3]
        {
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = ExtractSet,
                DstBinding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &info
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = TonemapSet,
                DstBinding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &info
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = GodRayMaskSet,
                DstBinding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &info
            }
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 3, writes, 0, null);
    }

    /// <summary>Rewrites GodRayMaskSet's uDepthTex binding (SSAO's G-buffer depth) -- call ONLY
    /// when that target is (re)created (<c>VkViewportControl.EnsureGBufferTarget</c>), same
    /// "rewrite rarely" contract as every other binding in this class. Separate from
    /// <see cref="UpdateHdrInput"/> since the G-buffer depth target has its own, independent
    /// resize/recreation lifecycle (it's SSAO's, not this chain's own).</summary>
    public void UpdateGodRayDepthInput(DescriptorImageInfo gbufDepthInfo)
    {
        var info = gbufDepthInfo;
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = GodRayMaskSet,
            DstBinding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &info
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    /// <summary>Rewrites the god-ray mask -> blur binding, and TonemapSet's uGodRayTex binding --
    /// call ONLY when the god-ray targets are (re)created (mirrors <see cref="UpdateBloomTargets"/>
    /// exactly, just a single target instead of a ping-pong pair, since the radial blur is one
    /// pass, not separable like bloom's Gaussian).</summary>
    public void UpdateGodRayTargets(DescriptorImageInfo maskInfo, DescriptorImageInfo blurInfo)
    {
        var mask = maskInfo;
        var blur = blurInfo;
        var writes = stackalloc WriteDescriptorSet[2]
        {
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = GodRayBlurSet,
                DstBinding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &mask
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = TonemapSet,
                DstBinding = 2,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &blur
            }
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 2, writes, 0, null);
    }

    /// <summary>VkGraphicsTier.Medium only: binds TonemapSet's binding 2 (uGodRayTex) to a shared
    /// placeholder -- mirrors <see cref="BindNoBloomPlaceholder"/> exactly (that tier never calls
    /// VkViewportControl.EnsureGodRayTargets either, god rays being High-only same as bloom).
    /// Call once, right after construction, ONLY on that tier.</summary>
    public void BindNoGodRayPlaceholder(DescriptorImageInfo blackInfo)
    {
        var info = blackInfo;
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = TonemapSet,
            DstBinding = 2,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &info
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    /// <summary>Rewrites the bloom ping-pong bindings -- call ONLY when the bloom targets are
    /// (re)created. <c>pingInfo</c>/<c>pongInfo</c> match VkViewportControl's own naming: extract
    /// writes ping, blur-H reads ping and writes pong, blur-V reads pong and writes BACK to ping
    /// (so the final blurred result always ends up in ping, which is why TonemapSet's binding 1
    /// points at ping here, not pong).</summary>
    public void UpdateBloomTargets(DescriptorImageInfo pingInfo, DescriptorImageInfo pongInfo)
    {
        var ping = pingInfo;
        var pong = pongInfo;
        var writes = stackalloc WriteDescriptorSet[3]
        {
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = BlurHSet,
                DstBinding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &ping
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = BlurVSet,
                DstBinding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &pong
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = TonemapSet,
                DstBinding = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &ping
            }
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 3, writes, 0, null);
    }

    /// <summary>VkGraphicsTier.Medium only: binds TonemapSet's binding 1 (uBloomTex) to a shared
    /// placeholder instead of a real bloom target -- that tier never calls
    /// VkViewportControl.EnsureBloomTargets (bloom is High-only), so without this the binding
    /// would stay permanently unwritten. Call once, right after construction, ONLY on that tier
    /// -- UpdateBloomTargets above overwrites this same binding on High, the only other tier
    /// that ever touches it.</summary>
    public void BindNoBloomPlaceholder(DescriptorImageInfo blackInfo)
    {
        var info = blackInfo;
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = TonemapSet,
            DstBinding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &info
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var sets = stackalloc DescriptorSet[6]
            { ExtractSet, BlurHSet, BlurVSet, TonemapSet, GodRayMaskSet, GodRayBlurSet };
        _vk.Api.FreeDescriptorSets(_vk.Device, _vk.DescriptorPool, 6, sets);
    }
}
