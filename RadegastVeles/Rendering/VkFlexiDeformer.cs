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

// Vulkan port of GlFlexiDeformer.cs. Mirrors VkSkinDeformer.cs's shape exactly (own synchronous
// one-off command buffer, called from the top of VkViewportControl.RenderFrame before the main
// frame's command buffer is built, per-dispatch VkBufferMemoryBarrier -- see VkSkinDeformer.cs's
// own doc comment for the full reasoning, which applies unchanged here). Real difference from
// VkSkinDeformer: each queued job dispatches ONCE PER FACE of the flexi prim (a prim can have
// multiple faces sharing one spine simulation, see VkFlexiGpuData.cs's own doc comment), not
// once per job. The spine upload and the per-prim push-constant fields
// (SegmentCount/Scale/AttachTransform) are set ONCE per job and reused across that job's
// per-face dispatches -- only VertexCount and the bound descriptor set change per face, matching
// GL's own loop shape (spine BufferSubData and the three per-prim shader.Set calls happen
// BEFORE the `for fi` loop in GlFlexiDeformer.DispatchPending). The VkBufferMemoryBarrier after
// each dispatch is genuinely per-dispatch (inside the per-face loop), not batched after it,
// matching GlFlexiDeformer.cs's own per-face MemoryBarrier(VertexAttribArrayBarrierBit) call,
// which sits inside its own `for fi` loop too.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkFlexiDeformer : IDisposable
{
    private readonly VkContext _vk;
    private readonly VkFlexiPipeline _pipeline;

    // Keyed by target GPU object, not a FIFO queue -- see VkSkinDeformer.cs's identical field
    // for the full reasoning (unbounded-queue freeze this replaces). Each job is a full spine
    // snapshot for that tick, so coalescing to the latest per-prim entry is correct, not lossy.
    private readonly ConcurrentDictionary<VkFlexiGpuData, VkFlexiComputeJob> _pending = new();

    // Coalescing (above) bounds the pending SET size but not per-frame GPU cost -- and flexi is
    // worse than skin here, since one job can expand into gpu.BindPoseSSBOs.Length dispatches
    // (one per face sharing a spine). Capping how many JOBS drain per frame bounds that
    // expansion; leftovers stay in the map and coalesce with any newer tick before the next
    // frame's drain -- see VkSkinDeformer.cs's identical field for the full reasoning.
    private const int MaxJobsPerFrame = 128;

    // Same starvation hazard as VkSkinDeformer's identical field: plain ConcurrentDictionary
    // enumeration order has no fairness guarantee, so whichever jobs land past the cap can be
    // starved forever, not just delayed. Applied here too since flexi shares the identical
    // pending/cap shape and is more exposure-prone per the class doc comment above (one job can
    // expand into many face dispatches).
    private readonly ConcurrentDictionary<VkFlexiGpuData, long> _lastServiced = new();
    private long _serviceCounter;
    private long _lastPruneTicks;

    private bool _disposed;

    public VkFlexiDeformer(VkContext vk, VkFlexiPipeline pipeline)
    {
        _vk = vk;
        _pipeline = pipeline;
    }

    /// <summary>Thread-safe; called from the physics background thread. Coalesces with any
    /// still-undispatched job for the same GPU target -- only the latest snapshot matters.</summary>
    public void Enqueue(VkFlexiComputeJob job) => _pending[job.Gpu] = job;

    /// <summary>
    /// Processes all queued jobs. Must be called on the render thread, before any draw calls
    /// that use the deformed meshes this frame -- see this class's own doc comment for why it
    /// runs as a separate, synchronous one-off command buffer.
    /// </summary>
    public void DispatchPending()
    {
        // Runs even when nothing is pending -- see VkSkinDeformer.cs's identical block for why
        // (a prim fully serviced before its avatar rebuilds/disposes never passes back through
        // _pending, and the steady state is exactly when _pending.IsEmpty is true).
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
        int dispatchedJobs = 0;

        var api = _vk.Api;
        var cmd = _vk.Pool.CreateCommandBuffer("VkFlexiDeformer.DispatchPending");
        cmd.BeginRecording();
        api.CmdBindPipeline(cmd.InternalHandle, PipelineBindPoint.Compute, _pipeline.Pipeline);

        // Direct enumeration (not .Keys, which allocates a fresh copy every call) -- safe
        // against concurrent Enqueue calls per ConcurrentDictionary's enumerator contract; see
        // VkSkinDeformer.cs's identical drain loop for the full reasoning. Ordered by staleness
        // once the cap can actually bind -- see _lastServiced's own field comment and
        // VkSkinDeformer.cs's identical fix for why plain enumeration order isn't fair enough.
        //
        // NOT _pending.OrderBy(...) directly -- see VkSkinDeformer.cs's identical comment for
        // why that throws (LINQ's internal Count-then-CopyTo buffering races a concurrent
        // Enqueue). Snapshot through the safe foreach first, then sort the plain List.
        IEnumerable<KeyValuePair<VkFlexiGpuData, VkFlexiComputeJob>> drain;
        if (pendingAtStart > MaxJobsPerFrame)
        {
            var snapshot = new List<KeyValuePair<VkFlexiGpuData, VkFlexiComputeJob>>(pendingAtStart);
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
            if (dispatchedJobs >= MaxJobsPerFrame) break;
            if (!_pending.TryRemove(kv.Key, out var job)) continue;
            var gpu = job.Gpu;
            dispatchedJobs++;
            if (gpu.IsDisposed) { _lastServiced.TryRemove(gpu, out _); continue; }
            _lastServiced[gpu] = ++_serviceCounter;

            // Upload spine positions once per job -- shared across every face's dispatch below,
            // matching GL's own single BufferSubData call before its `for fi` loop.
            VkBufferHelper.UpdateHostVisible(_vk, gpu.SpineMemory, (ReadOnlySpan<float>)job.SpineFloats);

            for (int fi = 0; fi < gpu.BindPoseSSBOs.Length; fi++)
            {
                var set = gpu.Sets[fi];
                api.CmdBindDescriptorSets(cmd.InternalHandle, PipelineBindPoint.Compute, _pipeline.Layout,
                    0, 1, &set, 0, null);

                var pc = new VkFlexiPushConstants
                {
                    VertexCount = gpu.VertexCounts[fi],
                    SegmentCount = gpu.SegmentCount,
                    Scale = new System.Numerics.Vector3(gpu.ScaleX, gpu.ScaleY, gpu.ScaleZ),
                    AttachTransform = job.AttachTransform
                };
                api.CmdPushConstants(cmd.InternalHandle, _pipeline.Layout, ShaderStageFlags.ComputeBit,
                    0, (uint)sizeof(VkFlexiPushConstants), &pc);

                uint groups = ((uint)gpu.VertexCounts[fi] + 63u) / 64u;
                api.CmdDispatch(cmd.InternalHandle, groups, 1, 1);

                // Ensure this dispatch's writes to this face's mesh VBO are visible to the
                // vertex-input stage's subsequent reads. Per-dispatch, not batched after the
                // loop -- see this class's own doc comment for why that matches GL's shape.
                var barrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.ShaderWriteBit,
                    DstAccessMask = AccessFlags.VertexAttributeReadBit,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Buffer = gpu.Meshes[fi].Vbo,
                    Offset = 0,
                    Size = Vk.WholeSize
                };
                api.CmdPipelineBarrier(cmd.InternalHandle, PipelineStageFlags.ComputeShaderBit,
                    PipelineStageFlags.VertexInputBit, DependencyFlags.None, 0, null, 1, &barrier, 0, null);
            }
        }

        cmd.SubmitAndWait();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // _pipeline is owned by VkViewportControl (created/disposed alongside the other
        // pipelines), not by this class -- mirrors VkSkinDeformer's own identical note.
    }
}
