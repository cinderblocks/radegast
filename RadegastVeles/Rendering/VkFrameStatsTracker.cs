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
/// Structurally simpler than GL's own 4-deep non-blocking query ring: GL's ring exists
/// specifically so the CPU never stalls waiting on a GPU query result while the driver may be
/// several frames ahead. <c>VkViewportControl</c> has no such pipelining today --
/// <c>VkCommandBufferPool.FreeUsedCommandBuffers</c>, called synchronously at the end of every
/// <see cref="RenderFrame"/>-equivalent call, does a blocking <c>WaitForFences</c>. Since the CPU
/// is already blocked until the GPU finishes the frame by the time <see cref="EndFrame"/> runs,
/// reading the timestamp query pool synchronously right there adds no additional stall beyond
/// what already happens -- no ring buffer needed.
/// </para>
/// <para>
/// Integration is a 4-call-site shape, not GL's 2-call (<c>BeginFrame</c>/<c>EndFrame</c>)
/// shape, because Vulkan timestamp writes must be recorded INTO a command buffer
/// (<c>vkCmdWriteTimestamp</c>), not issued standalone the way GL's context-bound
/// <c>glBeginQuery</c>/<c>glEndQuery</c> are: <see cref="BeginFrame"/> (CPU stopwatch + counter
/// reset, before command-buffer recording starts), <see cref="WriteStartTimestamp"/> (first
/// thing recorded into the frame's command buffer), <see cref="WriteEndTimestamp"/> (last thing
/// recorded, before <c>cmd.Submit()</c>), <see cref="EndFrame"/> (after the fence wait
/// completes, reads the query pool and publishes <see cref="FrameStats"/>).
/// </para>
/// </summary>
public sealed unsafe class VkFrameStatsTracker : IFrameStatsTracker, IDisposable
{
    private QueryPool _pool;
    private bool _supported;
    private bool _initialized;
    private bool _disposed;
    private float _timestampPeriodNs;

    private readonly Stopwatch _cpu = new();
    private int _drawCalls;
    private int _triangles;
    private int _facesSubmitted;
    private int _facesCulled;

    /// <summary>The most recent published <see cref="FrameStats"/> value.</summary>
    public FrameStats Last { get; private set; }

    /// <summary>Fired (on the render thread) once per frame after <see cref="EndFrame"/>.</summary>
    public event Action<FrameStats>? FrameCompleted;

    /// <summary>Allocates the 2-slot timestamp query pool (frame-start, frame-end) and checks
    /// this device/queue family actually supports timestamp queries. Must be called once after
    /// device creation, before the first <see cref="BeginFrame"/>.</summary>
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
                QueryCount = 2
            };
            vk.Api.CreateQueryPool(vk.Device, in poolInfo, null, out _pool).ThrowOnError();
            _supported = true;
        }
        catch
        {
            _supported = false;
        }
    }

    /// <summary>Start CPU timing and reset per-frame counters. Call before command-buffer
    /// recording begins.</summary>
    public void BeginFrame()
    {
        if (_disposed) return;
        _drawCalls = 0;
        _triangles = 0;
        _facesSubmitted = 0;
        _facesCulled = 0;
        _cpu.Restart();
    }

    /// <summary>Records a query-pool reset + the frame-start timestamp write. Must be the first
    /// thing recorded into the frame's command buffer (a query pool must be reset before reuse;
    /// resetting via the command buffer, not <c>vkResetQueryPool</c>, avoids requiring Vulkan
    /// 1.2 host query reset).</summary>
    internal void WriteStartTimestamp(VkContext vk, CommandBuffer cmd)
    {
        if (_disposed || !_supported) return;
        vk.Api.CmdResetQueryPool(cmd, _pool, 0, 2);
        vk.Api.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, _pool, 0);
    }

    /// <summary>Records the frame-end timestamp write. Must be the last thing recorded into the
    /// frame's command buffer, before <c>cmd.Submit()</c>.</summary>
    internal void WriteEndTimestamp(VkContext vk, CommandBuffer cmd)
    {
        if (_disposed || !_supported) return;
        vk.Api.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, _pool, 1);
    }

    /// <summary>Stops CPU timing, reads the timestamp query pool (safe to do synchronously --
    /// see this class's own doc comment for why no stall is introduced beyond what already
    /// happens), and publishes a <see cref="FrameStats"/> value. Call after the frame's command
    /// buffer has been submitted AND fence-waited (i.e. after
    /// <c>VkCommandBufferPool.FreeUsedCommandBuffers</c> returns).</summary>
    internal void EndFrame(VkContext vk)
    {
        if (_disposed) return;
        _cpu.Stop();

        double gpuMs = 0.0;
        if (_supported)
        {
            var raw = stackalloc ulong[2];
            var result = vk.Api.GetQueryPoolResults(vk.Device, _pool, 0, 2, (nuint)(sizeof(ulong) * 2),
                raw, sizeof(ulong), QueryResultFlags.Result64Bit);
            // Success (not NotReady/other) -- the fence wait already completed by the time this
            // runs, so results should always be available; a non-Success result just means "no
            // GPU timing this frame," not a hard failure.
            if (result == Result.Success && raw[1] >= raw[0])
                gpuMs = (raw[1] - raw[0]) * _timestampPeriodNs / 1_000_000.0;
        }

        var stats = new FrameStats(
            CpuTimeMs: _cpu.Elapsed.TotalMilliseconds,
            GpuTimeMs: gpuMs,
            DrawCalls: _drawCalls,
            Triangles: _triangles,
            FacesSubmitted: _facesSubmitted,
            FacesCulled: _facesCulled);
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
