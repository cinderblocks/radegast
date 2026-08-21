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

// Vulkan port of GlSkinDeformer.cs. Dispatches one compute shader invocation
// per queued VkSkinComputeJob, writing deformed vertices directly into each job's mesh VBO --
// same design GL uses (see GlSkinDeformer.cs's own class doc comment), no CPU vertex loop.
//
// Real difference from GL, a consequence of the Vulkan model rather than a style choice: GL's
// DispatchCompute calls execute against the driver's own internal command stream with no
// explicit command-buffer concept, so GlSkinDeformer.DispatchPending just issues GL calls
// directly. Vulkan needs an explicit command buffer. This runs as its own one-off command
// buffer (vk.Pool.CreateCommandBuffer -> record -> Submit -> FreeUsedCommandBuffers, the same
// synchronous submit-and-wait pattern VkTexture's upload and the pick pass already use),
// called from the TOP of VkViewportControl.RenderFrame, BEFORE the main render pass's command
// buffer is built -- guarantees every deformed VBO is fully written (fence-waited) before any
// draw call this frame could read it, without needing to interleave compute dispatch into the
// same command buffer as the main draw calls.
//
// The VkBufferMemoryBarrier after each dispatch (ComputeShaderBit/ShaderWriteBit ->
// VertexInputBit/VertexAttributeReadBit) mirrors GL's per-dispatch
// MemoryBarrier(VertexAttribArrayBarrierBit) call exactly (GL's own barrier is also per-
// dispatch, inside the loop, not batched after it). Strictly speaking the fence wait in
// FreeUsedCommandBuffers
// already establishes host-mediated visibility for whatever command buffer records the main
// draw calls next, but the barrier costs nothing and keeps this correct independent of that
// argument, matching GL's own equally-defensive-looking per-dispatch barrier.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkSkinDeformer : IDisposable
{
    private readonly VkContext _vk;
    private readonly VkSkinPipeline _pipeline;

    // Keyed by the target GPU object rather than a FIFO queue: each job is a full per-tick
    // snapshot (not a delta), so a face ticking again before the render thread drains its
    // previous job should overwrite the pending entry, not pile up behind it. A FIFO queue
    // would let every AnimTick call add a new entry regardless of how far behind
    // DispatchPending was, letting the backlog grow unbounded in a busy, avatar-dense region
    // where the single-threaded per-frame drain can't keep up. Keying by Gpu bounds pending
    // work to the number of currently-registered faces, not elapsed ticks.
    private readonly ConcurrentDictionary<VkAvatarSkinGpuData, VkSkinComputeJob> _pending = new();

    // Coalescing (above) bounds the pending SET size but not per-frame GPU cost -- dispatching
    // every registered face in one submission would make that frame's blocking fence-wait
    // scale with population (e.g. 60 avatars x 8 skinned faces = 480 dispatches under one
    // wait). Capping how many drain per frame bounds that wait; anything left over stays in
    // the map and coalesces with a newer tick by the time the next frame drains it, so no work
    // is lost, only deferred a frame or two under heavy load.
    private const int MaxJobsPerFrame = 256;

    private readonly ConcurrentDictionary<VkAvatarSkinGpuData, long> _lastServiced = new();
    private long _serviceCounter;
    private long _lastPruneTicks;

    private bool _disposed;

    public VkSkinDeformer(VkContext vk, VkSkinPipeline pipeline)
    {
        _vk = vk;
        _pipeline = pipeline;
    }

    /// <summary>Thread-safe; called from the animation background thread. Coalesces with any
    /// still-undispatched job for the same GPU target -- only the latest snapshot matters.</summary>
    public void Enqueue(VkSkinComputeJob job) => _pending[job.Gpu] = job;

    /// <summary>
    /// Processes all queued jobs. Must be called on the render thread, before any draw calls
    /// that use the deformed meshes this frame -- see this class's own doc comment for why it
    /// runs as a separate, synchronous one-off command buffer rather than being interleaved
    /// into the main frame's command buffer.
    /// </summary>
    public void DispatchPending()
    {
        // Runs even when nothing is pending -- a face fully serviced before its avatar
        // rebuilds/disposes never passes back through _pending, so its _lastServiced entry
        // would never be visited by the in-loop disposed-check below, and the common steady
        // state (animation settled, nothing queued) is exactly when _pending.IsEmpty is true.
        // Own throttle, independent of the dispatch log below, since that block doesn't run
        // when _pending is empty.
        {
            long nowPrune = Environment.TickCount64;
            if (nowPrune - _lastPruneTicks >= 1000)
            {
                _lastPruneTicks = nowPrune;
                foreach (var gpu in _lastServiced.Keys)
                    if (gpu.IsDisposed) _lastServiced.TryRemove(gpu, out _);
            }
        }

        if (_pending.IsEmpty) return;

        int pendingAtStart = _pending.Count;
        int dispatched = 0;

        var api = _vk.Api;
        var cmd = _vk.Pool.CreateCommandBuffer("VkSkinDeformer.DispatchPending");
        cmd.BeginRecording();
        api.CmdBindPipeline(cmd.InternalHandle, PipelineBindPoint.Compute, _pipeline.Pipeline);

        // Enumerating the dictionary directly (not .Keys, which copies into a fresh List every
        // call) is safe against concurrent Enqueue calls landing mid-drain per
        // ConcurrentDictionary's own contract -- the enumerator tolerates concurrent
        // modification without throwing. Safety comes from TryRemove being atomic, not from
        // any snapshot guarantee: either TryRemove wins and we process the newest value for
        // that key, or a fresh Enqueue arrives after removal and waits for next frame. Never
        // spins/blocks on a producer.
        //
        // Only pay for the ordering pass when the cap can actually bind -- below it, every
        // pending job drains this frame regardless of order, so plain enumeration (no sort) is
        // both correct and cheaper for the common case.
        //
        // NOT _pending.OrderBy(...) directly: LINQ's OrderBy buffers its source via
        // ICollection<T>.Count then ICollection<T>.CopyTo(array, 0) as two separate steps, and
        // ConcurrentDictionary only guarantees safety for its own enumerator, not for that
        // Count-then-CopyTo pair -- a concurrent Enqueue from the animation thread growing the
        // dictionary in between throws ArgumentException from CopyTo ("index is equal to or
        // greater than the length of the array"). Building the snapshot through the same safe
        // foreach used elsewhere in this method, then sorting the resulting plain (non-shared)
        // List, sidesteps the race entirely.
        IEnumerable<KeyValuePair<VkAvatarSkinGpuData, VkSkinComputeJob>> drain;
        if (pendingAtStart > MaxJobsPerFrame)
        {
            var snapshot = new List<KeyValuePair<VkAvatarSkinGpuData, VkSkinComputeJob>>(pendingAtStart);
            foreach (var kv in _pending) snapshot.Add(kv);
            snapshot.Sort((a, b) =>
            {
                long ta = _lastServiced.TryGetValue(a.Key, out var tta) ? tta : -1;
                long tb = _lastServiced.TryGetValue(b.Key, out var ttb) ? ttb : -1;
                return ta.CompareTo(tb);
            });
            drain = snapshot;
        }
        else
        {
            drain = _pending;
        }

        foreach (var kv in drain)
        {
            if (dispatched >= MaxJobsPerFrame) break;
            if (!_pending.TryRemove(kv.Key, out var job)) continue;
            var gpu = job.Gpu;
            dispatched++;
            if (gpu.IsDisposed) { _lastServiced.TryRemove(gpu, out _); continue; }
            _lastServiced[gpu] = ++_serviceCounter;

            // Upload skin matrices for this tick -- host-visible direct map, matches
            // GlSkinDeformer's BufferSubData call exactly (same StreamDraw-equivalent buffer).
            VkBufferHelper.UpdateHostVisible(_vk, gpu.SkinMatsMemory, (ReadOnlySpan<float>)job.SkinMats);

            var set = gpu.Set;
            api.CmdBindDescriptorSets(cmd.InternalHandle, PipelineBindPoint.Compute, _pipeline.Layout,
                0, 1, &set, 0, null);

            int vertexCount = gpu.VertexCount;
            api.CmdPushConstants(cmd.InternalHandle, _pipeline.Layout, ShaderStageFlags.ComputeBit,
                0, sizeof(int), &vertexCount);

            uint groups = ((uint)gpu.VertexCount + 63u) / 64u;
            api.CmdDispatch(cmd.InternalHandle, groups, 1, 1);

            // Ensure this dispatch's writes to the mesh VBO are visible to the vertex-input
            // stage's subsequent reads (any draw call later this frame or a future one).
            var barrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.VertexAttributeReadBit,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Buffer = gpu.Mesh.Vbo,
                Offset = 0,
                Size = Vk.WholeSize
            };
            api.CmdPipelineBarrier(cmd.InternalHandle, PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.VertexInputBit, DependencyFlags.None, 0, null, 1, &barrier, 0, null);
        }

        cmd.SubmitAndWait();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // _pipeline is owned by VkViewportControl (created/disposed alongside the other
        // pipelines), not by this class -- mirrors GlSkinDeformer owning its GlShader
        // directly, which doesn't carry over 1:1 since VkSkinPipeline is shared
        // infrastructure the same shape as VkPrimPipeline/VkWireframePipeline/VkPickPipeline,
        // not a per-deformer resource.
    }
}
