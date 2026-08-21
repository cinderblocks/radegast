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

using System;
using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Vulkan counterpart to <see cref="FrameStatsTracker"/> (GL). Publishes the same
/// backend-agnostic <see cref="FrameStats"/> record via the same <see cref="FrameCompleted"/>
/// event contract -- <c>FrameStatsTracker.cs</c> itself is GL-coupled at the source level
/// (<c>Silk.NET.OpenGL</c> query objects) and can't be reused as-is, so this is a genuinely new
/// class, not a port of that file's own body.
/// <para>
/// Query pool is sized <c>2 * VkContext.FramesInFlight</c> (plan Step 3): each frame writes into
/// its own <c>frameIndex % FramesInFlight</c> slot instead of always slot 0. At the pre-Step-2
/// <c>FramesInFlight=1</c> this is still exactly one slot, reused every frame, with the same
/// "no stall, CPU already blocked on this frame's own fence by the time EndFrame runs" property
/// the original single-pool design relied on. Step 6 flipping <c>FramesInFlight</c> to 2 is what
/// this sizing exists to make safe: once real frame overlap lands, frame N+1's command buffer
/// could otherwise reset/write the SAME query pool frame N's still-unread results live in,
/// before <see cref="EndFrame"/> for frame N has a chance to read them.
/// </para>
/// <para>
/// Integration is a 5-call-site shape, not GL's 2-call (<c>BeginFrame</c>/<c>EndFrame</c>)
/// shape, because Vulkan timestamp writes must be recorded INTO a command buffer
/// (<c>vkCmdWriteTimestamp</c>), not issued standalone the way GL's context-bound
/// <c>glBeginQuery</c>/<c>glEndQuery</c> are: <see cref="BeginFrame"/> (CPU stopwatch + counter
/// reset, before command-buffer recording starts), <see cref="WriteStartTimestamp"/> (first
/// thing recorded into the frame's command buffer), <see cref="WriteEndTimestamp"/> (last thing
/// recorded, before <c>cmd.Submit()</c>), <see cref="EndCpuWork"/> (right after
/// <c>cmd.Submit()</c>, no wait -- snapshots this frame's CPU time/counters into its own slot),
/// <see cref="EndFrame"/> (once this slot's PRIOR occupant is confirmed fence-signaled -- at
/// <c>FramesInFlight=1</c> that's still "this frame, right after its own fence wait"; at N&gt;1
/// it's a later frame's call, reading an older frame's snapshotted data -- see
/// <see cref="EndFrame"/>'s own doc comment).
/// </para>
/// </summary>
public sealed unsafe class VkFrameStatsTracker : IFrameStatsTracker, IDisposable
{
    private QueryPool _pool;
    private bool _supported;
    private bool _initialized;
    private bool _disposed;
    private float _timestampPeriodNs;
    private long _frameIndex = -1;

    private readonly Stopwatch _cpu = new();
    private int _drawCalls;
    private int _triangles;
    private int _facesSubmitted;
    private int _facesCulled;

    // Plan Step 6: CPU-side counters snapshotted per-slot by EndCpuWork (called right after
    // Submit, with no fence wait) so EndFrame -- now called at the point a slot's GPU work is
    // actually confirmed done, which under real overlap is a LATER frame than the one that
    // produced these numbers -- has something to pair the GPU timestamps with. Without this,
    // EndFrame would read the live _cpu/_drawCalls fields, which by then belong to whatever
    // frame is currently recording, not the frame whose slot just got reaped.
    private readonly double[] _cpuMsBuf = new double[VkContext.FramesInFlight];
    private readonly int[] _drawCallsBuf = new int[VkContext.FramesInFlight];
    private readonly int[] _trianglesBuf = new int[VkContext.FramesInFlight];
    private readonly int[] _facesSubmittedBuf = new int[VkContext.FramesInFlight];
    private readonly int[] _facesCulledBuf = new int[VkContext.FramesInFlight];

    // Frame-interval variance (plan Step 3): wall-clock gap between consecutive BeginFrame
    // calls, over a rolling window -- the metric that actually answers "is it choppy" (CPU/GPU
    // ms alone can drop once Step 6's pipelining lands whether or not real frame pacing
    // improves). Fixed-size circular buffer, no per-frame allocation: _intervalWindow holds the
    // raw samples in call order, _intervalScratch is a reusable buffer EndFrame sorts into to
    // compute max/p99 without disturbing the circular buffer's write position.
    private const int IntervalWindowSize = 120; // ~2s at 60fps
    private readonly double[] _intervalWindow = new double[IntervalWindowSize];
    private readonly double[] _intervalScratch = new double[IntervalWindowSize];
    private int _intervalCount;
    private int _intervalWriteIndex;
    private readonly Stopwatch _frameIntervalTimer = new();

    /// <summary>The most recent published <see cref="FrameStats"/> value.</summary>
    public FrameStats Last { get; private set; }

    /// <summary>Fired (on the render thread) once per frame after <see cref="EndFrame"/>.</summary>
    public event Action<FrameStats>? FrameCompleted;

    /// <summary>Allocates the <c>2 * VkContext.FramesInFlight</c>-slot timestamp query pool
    /// (frame-start, frame-end per slot) and checks this device/queue family actually supports
    /// timestamp queries. Must be called once after device creation, before the first
    /// <see cref="BeginFrame"/>.</summary>
    internal void Initialize(VkContext vk)
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        try
        {
            uint familyCount = 0;
            vk.Api.GetPhysicalDeviceQueueFamilyProperties(vk.PhysicalDevice, ref familyCount, null);
            var families = stackalloc QueueFamilyProperties[(int)familyCount];
            vk.Api.GetPhysicalDeviceQueueFamilyProperties(vk.PhysicalDevice, ref familyCount, families);
            if (vk.QueueFamilyIndex >= familyCount || families[(int)vk.QueueFamilyIndex].TimestampValidBits == 0)
            {
                _supported = false;
                return;
            }

            vk.Api.GetPhysicalDeviceProperties(vk.PhysicalDevice, out var props);
            _timestampPeriodNs = props.Limits.TimestampPeriod;
            if (_timestampPeriodNs <= 0f)
            {
                _supported = false;
                return;
            }

            var poolInfo = new QueryPoolCreateInfo
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = (uint)(2 * VkContext.FramesInFlight)
            };
            vk.Api.CreateQueryPool(vk.Device, in poolInfo, null, out _pool).ThrowOnError();
            _supported = true;
        }
        catch
        {
            _supported = false;
        }
    }

    /// <summary>Start CPU timing, advance to this frame's query-pool slot, reset per-frame
    /// counters, and sample the frame-interval timer. Call before command-buffer recording
    /// begins.</summary>
    public void BeginFrame()
    {
        if (_disposed) return;
        _frameIndex++;
        if (_frameIntervalTimer.IsRunning)
        {
            _intervalWindow[_intervalWriteIndex] = _frameIntervalTimer.Elapsed.TotalMilliseconds;
            _intervalWriteIndex = (_intervalWriteIndex + 1) % IntervalWindowSize;
            if (_intervalCount < IntervalWindowSize) _intervalCount++;
        }
        _frameIntervalTimer.Restart();
        _drawCalls = 0;
        _triangles = 0;
        _facesSubmitted = 0;
        _facesCulled = 0;
        _cpu.Restart();
    }

    private uint CurrentSlot => (uint)(_frameIndex % VkContext.FramesInFlight);

    /// <summary>Records a query-pool reset + the frame-start timestamp write into this frame's
    /// own slot. Must be the first thing recorded into the frame's command buffer (a query pool
    /// must be reset before reuse; resetting via the command buffer, not
    /// <c>vkResetQueryPool</c>, avoids requiring Vulkan 1.2 host query reset).</summary>
    internal void WriteStartTimestamp(VkContext vk, CommandBuffer cmd)
    {
        if (_disposed || !_supported) return;
        var slot = CurrentSlot;
        vk.Api.CmdResetQueryPool(cmd, _pool, slot * 2, 2);
        vk.Api.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, _pool, slot * 2);
    }

    /// <summary>Records the frame-end timestamp write into this frame's own slot. Must be the
    /// last thing recorded into the frame's command buffer, before <c>cmd.Submit()</c>.</summary>
    internal void WriteEndTimestamp(VkContext vk, CommandBuffer cmd)
    {
        if (_disposed || !_supported) return;
        vk.Api.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, _pool, CurrentSlot * 2 + 1);
    }

    /// <summary>Stops CPU timing and snapshots this frame's draw-call/triangle/face counters into
    /// its own slot. Call once, right after the frame's command buffer is submitted (no fence
    /// wait here -- under real overlap the GPU may still be working on it). This is what
    /// <see cref="EndFrame"/> later reads back once that slot's GPU work is confirmed done, which
    /// at <c>FramesInFlight&gt;1</c> is a LATER frame than the one that recorded these numbers --
    /// see this class's own doc comment.</summary>
    internal void EndCpuWork()
    {
        if (_disposed) return;
        _cpu.Stop();
        var slot = CurrentSlot;
        _cpuMsBuf[slot] = _cpu.Elapsed.TotalMilliseconds;
        _drawCallsBuf[slot] = _drawCalls;
        _trianglesBuf[slot] = _triangles;
        _facesSubmittedBuf[slot] = _facesSubmitted;
        _facesCulledBuf[slot] = _facesCulled;
    }

    /// <summary>Reads this frame's own query-pool slot (safe to do synchronously -- see this
    /// class's own doc comment for why no stall is introduced beyond what already happens),
    /// pairs it with the CPU-side counters <see cref="EndCpuWork"/> snapshotted into the same
    /// slot, computes frame-interval variance over the rolling window, and publishes a
    /// <see cref="FrameStats"/> value. Call once <see cref="VkFrameReapRing"/> has confirmed this
    /// slot's command buffers are fence-signaled (its early reap, at this frame's own top) --
    /// under real overlap that reaps the slot THIS frame is about to reuse, i.e. the data read
    /// here belongs to the frame that used this slot <c>FramesInFlight</c> frames ago, not the
    /// frame currently starting. The published <see cref="FrameStats.CpuTimeMs"/>/draw-call
    /// numbers are therefore that older frame's, paired with its own GPU timing -- decoupled from
    /// wall-clock "now" by design, since re-synchronizing them would mean waiting for the very
    /// thing overlap exists to avoid. Frame-interval variance (measured off wall-clock BeginFrame
    /// gaps, not this pairing) is the metric that stays meaningful in real time regardless.
    /// </summary>
    internal void EndFrame(VkContext vk)
    {
        if (_disposed) return;
        var slot = CurrentSlot;

        double gpuMs = 0.0;
        if (_supported)
        {
            var raw = stackalloc ulong[2];
            var result = vk.Api.GetQueryPoolResults(vk.Device, _pool, slot * 2, 2, (nuint)(sizeof(ulong) * 2),
                raw, sizeof(ulong), QueryResultFlags.Result64Bit);
            // Success (not NotReady/other) -- the fence wait already completed by the time this
            // runs, so results should always be available; a non-Success result just means "no
            // GPU timing this frame," not a hard failure.
            if (result == Result.Success && raw[1] >= raw[0])
                gpuMs = (raw[1] - raw[0]) * _timestampPeriodNs / 1_000_000.0;
        }

        double intervalMaxMs = 0, intervalP99Ms = 0;
        if (_intervalCount > 0)
        {
            Array.Copy(_intervalWindow, _intervalScratch, _intervalCount);
            Array.Sort(_intervalScratch, 0, _intervalCount);
            intervalMaxMs = _intervalScratch[_intervalCount - 1];
            int p99Index = (int)Math.Min(_intervalCount - 1, Math.Ceiling(_intervalCount * 0.99) - 1);
            intervalP99Ms = _intervalScratch[p99Index];
        }

        var stats = new FrameStats(
            CpuTimeMs: _cpuMsBuf[slot],
            GpuTimeMs: gpuMs,
            DrawCalls: _drawCallsBuf[slot],
            Triangles: _trianglesBuf[slot],
            FacesSubmitted: _facesSubmittedBuf[slot],
            FacesCulled: _facesCulledBuf[slot],
            IntervalMaxMs: intervalMaxMs,
            IntervalP99Ms: intervalP99Ms);
        Last = stats;
        FrameCompleted?.Invoke(stats);
    }

    /// <summary>Record a single draw call covering <paramref name="indexCount"/> indices.</summary>
    public void RecordDraw(int indexCount)
    {
        _drawCalls++;
        _triangles += indexCount / 3;
    }

    /// <summary>Increment the face-submitted counter (called regardless of cull result).</summary>
    public void RecordFaceConsidered() => _facesSubmitted++;

    /// <summary>Increment the face-culled counter when a face is rejected by the frustum test.</summary>
    public void RecordFaceCulled() => _facesCulled++;

    internal void Dispose(VkContext vk)
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized && _supported)
        {
            try { vk.Api.DestroyQueryPool(vk.Device, _pool, null); } catch { /* device may already be gone */ }
        }
    }

    // IDisposable.Dispose() has no VkContext to destroy the pool with -- matches this class's
    // own real teardown need: VkViewportControl's cleanup path already has a `vk` in scope and
    // should call the VkContext-taking overload above instead. This parameterless overload
    // exists only to satisfy IDisposable's contract cleanly (e.g. `using` in a future context
    // that doesn't have a live VkContext) and intentionally leaks the native pool handle rather
    // than risk calling Vulkan API functions against a possibly-destroyed device.
    void IDisposable.Dispose() => _disposed = true;
}
