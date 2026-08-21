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

// Owns and writes the per-panel descriptor sets for prim.vert/prim.frag's set 0 (PerFrame
// UBO) and set 1 (per-pass samplers). One instance per viewer panel (each panel has its own
// camera/lighting state and its own SSAO/shadow inputs, or lack thereof). Per-material sets
// (set 2) are NOT owned here -- materials come and go per draw call/scene object, so
// they're allocated per-material via VkMaterialDescriptorSet instead.
//
// PrimViewer's pilot scope (single static mesh, no shadows/SSAO) means set 1 is written
// ONCE, at construction, entirely from VkPlaceholderTextures -- there is no real SSAO
// buffer or shadow map to bind yet. uSsaoMap gets the White placeholder specifically
// because prim.frag reads it as an occlusion factor (1.0 = fully lit, matching "no SSAO
// pass ran" correctly); the shadow samplers get the depth-comparison placeholders built for
// exactly this purpose (see VkPlaceholderTextures.cs).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkPrimDescriptorSets : IDisposable
{
    // this is the ONE resource in the whole descriptor-set inventory mutated in
    // place every single frame (via UpdatePerFrame's host-visible memcpy) rather than
    // infrequently replaced -- everything else (UpdateSsaoMap/UpdateShadowMap, material
    // descriptor rewrites) is rare enough to defer instead (plan Step 5). Real N-way buffering,
    // not deferral, is what makes this safe under frame-in-flight overlap: N buffers + N
    // FrameSet descriptor sets, indexed by VkContext.FramesInFlight so frame N+1's writes never
    // land in the same buffer a still-in-flight frame N's command buffer is reading through its
    // own FrameSet binding. FrameSet stays a property (not a field) specifically so every
    // existing CmdBindDescriptorSets call site (7+ of them across VkViewportControl -- grepped
    // exhaustively, not assumed) picks up the correct current-frame slot automatically, with no
    // call-site changes needed.
    private readonly DescriptorSet[] _frameSets;
    public DescriptorSet FrameSet => _frameSets[CurrentSlot];
    public DescriptorSet PassSet { get; }

    private readonly VkContext _vk;
    private readonly Buffer[] _frameUboBuffers;
    private readonly DeviceMemory[] _frameUboMemories;
    private long _frameIndex = -1;
    private bool _disposed;

    // _frameIndex starts at -1 (see UpdatePerFrame) so C#'s sign-preserving % needs an explicit
    // normalize-to-non-negative step -- harmless today at FramesInFlight=1 (-1 % 1 == 0
    // regardless), but would index _frameSets[-1] the moment Step 6 flips FramesInFlight to 2
    // if left unguarded (-1 % 2 == -1 in C#, not 1).
    private static int CurrentSlotFor(long frameIndex) =>
        (int)(((frameIndex % VkContext.FramesInFlight) + VkContext.FramesInFlight) % VkContext.FramesInFlight);
    private int CurrentSlot => CurrentSlotFor(_frameIndex);

    public VkPrimDescriptorSets(VkContext vk, VkPrimPipeline pipeline, VkPlaceholderTextures placeholders)
    {
        _vk = vk;

        _frameUboBuffers = new Buffer[VkContext.FramesInFlight];
        _frameUboMemories = new DeviceMemory[VkContext.FramesInFlight];
        _frameSets = new DescriptorSet[VkContext.FramesInFlight];
        var initialFrame = default(VkPerFrameUbo);
        for (int i = 0; i < VkContext.FramesInFlight; i++)
        {
            VkBufferHelper.AllocateHostVisible(vk, BufferUsageFlags.UniformBufferBit,
                out _frameUboBuffers[i], out _frameUboMemories[i], MemoryMarshal.CreateReadOnlySpan(ref initialFrame, 1));
            _frameSets[i] = AllocateSet(vk, pipeline.PerFrameLayout);
            WriteFrameSet(i);
        }

        PassSet = AllocateSet(vk, pipeline.PerPassSamplersLayout);
        WritePassSetPlaceholders(placeholders);
    }

    /// <summary>Advances to this frame's own buffer/set slot and overwrites its UBO contents --
    /// call once per rendered frame before recording draw commands (host-visible/coherent
    /// memory, no descriptor rewrite needed, only the buffer's bytes change). Every
    /// <see cref="FrameSet"/> read for the rest of this frame reflects the slot just written
    /// here.</summary>
    public void UpdatePerFrame(in VkPerFrameUbo data)
    {
        _frameIndex++;
        VkBufferHelper.UpdateHostVisible(_vk, _frameUboMemories[CurrentSlot], MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in data), 1));
    }

    /// <summary>
    /// Rewrites PassSet binding 0 (uSsaoMap) to point at the real SSAO-blur output. Call ONLY
    /// when the SSAO-blur target is (re)created (its ImageView changed on resize), NEVER
    /// per-frame: whether SSAO actually contributes this frame is controlled entirely by
    /// <see cref="VkPerFrameUbo.HasSsao"/>, not by which texture happens to be bound here
    /// (prim.frag never samples uSsaoMap when uHasSsao==0) -- rewriting a live descriptor set
    /// every frame regardless of need is exactly the "last-write-wins across every
    /// already-recorded draw" clobber class an earlier instance-buffer bug taught this
    /// codebase to avoid. Safe to call mid-command-buffer-recording under
    /// this control's current no-frames-in-flight invariant -- same one <c>PatchSubmissionTexture</c>
    /// (Milestone 11) documents; revisit both sites together if frame-in-flight pipelining is
    /// ever added.
    /// </summary>
    public void UpdateSsaoMap(DescriptorImageInfo ssaoMapInfo)
    {
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = PassSet,
            DstBinding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &ssaoMapInfo
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    /// <summary>
    /// Rewrites PassSet binding 1 (uShadowMap) to point at the real directional shadow map.
    /// Same rewrite-on-(re)creation-only discipline as
    /// <see cref="UpdateSsaoMap"/>'s own doc comment: call ONLY when the shadow depth target is
    /// (re)created (it's a fixed 2048x2048 resolution, so in practice this means "once, the
    /// first time shadows successfully initialize" -- never per-frame, and never on ordinary
    /// viewport resize, since this target's size is independent of the panel's own pixel size).
    /// Whether shadows actually contribute this frame is controlled entirely by
    /// <see cref="VkPerFrameUbo.ShadowsOn"/>, not by which texture happens to be bound here.
    /// </summary>
    public void UpdateShadowMap(DescriptorImageInfo shadowMapInfo)
    {
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = PassSet,
            DstBinding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &shadowMapInfo
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    private static DescriptorSet AllocateSet(VkContext vk, DescriptorSetLayout layout)
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

    private void WriteFrameSet(int slot)
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _frameUboBuffers[slot],
            Offset = 0,
            Range = (ulong)sizeof(VkPerFrameUbo)
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _frameSets[slot],
            DstBinding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &bufferInfo
        };
        _vk.Api.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
    }

    private void WritePassSetPlaceholders(VkPlaceholderTextures placeholders)
    {
        // Binding order must match shadow.glsl/prim.frag exactly -- a wrong binding index here
        // is a silent wrong-texture bug, not a build error: 0 = uSsaoMap (prim.frag),
        // 1 = uShadowMap, 2 = uPointShadowMap0, 3 = uPointShadowMap1 (shadow.glsl).
        var infos = stackalloc DescriptorImageInfo[4]
        {
            placeholders.White,
            placeholders.ShadowMap2D,
            placeholders.ShadowMapCube,
            placeholders.ShadowMapCube
        };
        var writes = stackalloc WriteDescriptorSet[4];
        for (uint i = 0; i < 4; i++)
        {
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = PassSet,
                DstBinding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &infos[i]
            };
        }
        _vk.Api.UpdateDescriptorSets(_vk.Device, 4, writes, 0, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = 0; i < VkContext.FramesInFlight; i++)
        {
            var set = _frameSets[i];
            _vk.Api.FreeDescriptorSets(_vk.Device, _vk.DescriptorPool, 1, &set);
            _vk.Api.DestroyBuffer(_vk.Device, _frameUboBuffers[i], null);
            _vk.Api.FreeMemory(_vk.Device, _frameUboMemories[i], null);
        }
        var passSet = PassSet;
        _vk.Api.FreeDescriptorSets(_vk.Device, _vk.DescriptorPool, 1, &passSet);
    }
}
