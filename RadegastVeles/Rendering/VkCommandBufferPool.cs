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

// Adapted from Avalonia's own samples/GpuInterop/VulkanDemo (MIT licensed,
// https://github.com/AvaloniaUI/Avalonia), validated working against this project's
// pinned Avalonia version in experiments/VulkanEmbeddingSpike before porting here.

using System;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

/// <summary>
/// A one-time-submit command buffer pool: <see cref="CreateCommandBuffer"/> allocates a
/// buffer with its own fence, the caller records + submits it. Waiting on and freeing a
/// submitted buffer is the CALLER's responsibility (via a <see cref="VkFrameReapRing"/>) --
/// this pool only owns allocation/deallocation and serializing submits against the shared
/// <see cref="Queue"/>, plan Step 2 having moved the previous shared, process-wide
/// pending-buffer list out to one per panel (see <see cref="VkFrameReapRing"/>'s own doc
/// comment for why: no single process-wide "frame N" exists across independently-cadenced
/// panels). Not yet a frame-in-flight ring buffer in the reap sense either -- see plan Section 5
/// for why 2-frames-in-flight was deferred until profiling showed this simpler model was
/// actually a bottleneck (it was; see the plan's Step sequence).
/// </summary>
internal class VkCommandBufferPool : IDisposable
{
    private readonly Vk _api;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;

    // Guards command-buffer allocation/deallocation (AllocateCommandBuffer, VkCommandBuffer.
    // Dispose's FreeCommandBuffers call) -- native command-pool thread safety, legitimately
    // shared across panels since _commandPool itself is one shared native object. Distinct from
    // _queueLock below (submission), and no longer doubles as a reap-timing lock now that
    // pending-buffer bookkeeping lives per-panel in VkFrameReapRing instead of here.
    private readonly object _lock = new();

    // Guards ONLY ResetFences+QueueSubmit (VkCommandBuffer.Submit, below) -- separate from
    // _lock. Needed because Vulkan requires external synchronization on vkQueueSubmit calls
    // against the same VkQueue, and today nothing prevents two threads (e.g. InitializeAsync's
    // post-await continuation and another panel's RenderFrame) from calling Submit() concurrently.
    private readonly object _queueLock = new();

    public unsafe VkCommandBufferPool(Vk api, Device device, Queue queue, uint queueFamilyIndex)
    {
        _api = api;
        _device = device;
        _queue = queue;

        var commandPoolCreateInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamilyIndex
        };

        _api.CreateCommandPool(_device, in commandPoolCreateInfo, null, out _commandPool).ThrowOnError();
    }

    public unsafe void Dispose()
    {
        lock (_lock)
        {
            _api.DestroyCommandPool(_device, _commandPool, null);
        }
    }

    private CommandBuffer AllocateCommandBuffer()
    {
        var commandBufferAllocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            CommandBufferCount = 1,
            Level = CommandBufferLevel.Primary
        };

        lock (_lock)
        {
            _api.AllocateCommandBuffers(_device, in commandBufferAllocateInfo, out var commandBuffer);
            return commandBuffer;
        }
    }

    public VkCommandBuffer CreateCommandBuffer(string? debugTag = null) => new(_api, _device, _queue, this, debugTag);

    public class VkCommandBuffer : IDisposable
    {
        private readonly VkCommandBufferPool _pool;
        private readonly Vk _api;
        private readonly Device _device;
        private readonly Queue _queue;
        private readonly Fence _fence;
        private readonly string _debugTag;
        private bool _hasEnded;
        private bool _hasStarted;

        public IntPtr Handle => InternalHandle.Handle;
        internal CommandBuffer InternalHandle { get; }

        internal unsafe VkCommandBuffer(Vk api, Device device, Queue queue, VkCommandBufferPool pool, string? debugTag = null)
        {
            _api = api;
            _device = device;
            _queue = queue;
            _pool = pool;
            _debugTag = debugTag ?? "(untagged)";
            InternalHandle = _pool.AllocateCommandBuffer();

            var fenceCreateInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
            api.CreateFence(device, in fenceCreateInfo, null, out _fence);
        }

        // Bounded rather than an unbounded wait (was `ulong.MaxValue`) so a command-buffer fence
        // that never signals turns into a diagnosable, recoverable event -- logged with the
        // exact call site via _debugTag -- instead of a silent freeze indistinguishable from a
        // merely-slow GPU frame. This is a diagnostic stopgap, not a fix: revert to an unbounded
        // wait once the actual never-signaling call site is found and fixed. A timeout this
        // generous firing in normal operation would itself indicate a serious, still-unresolved
        // problem, not a tuning target.
        private const ulong WaitTimeoutNs = 5_000_000_000UL; // 5s

        public unsafe void Dispose()
        {
            if (!WaitForFenceCore()) return;
            lock (_pool._lock)
            {
                var handle = InternalHandle;
                _api.FreeCommandBuffers(_device, _pool._commandPool, 1, in handle);
            }
            _api.DestroyFence(_device, _fence, null);
        }

        /// <summary>Waits on this buffer's fence WITHOUT freeing the command buffer or destroying
        /// the fence -- for a caller that needs "is the GPU definitely done with whatever this
        /// buffer did" without taking over its lifetime (its owning <see cref="VkFrameReapRing"/>
        /// still disposes it on its own schedule; a subsequent <see cref="Dispose"/> call
        /// re-waits on an already-signaled fence, which returns immediately, so this is safe to
        /// call ahead of that with no double-free). Plan Step 6: the deformer-ordering fix uses
        /// this to wait for the immediately-preceding frame's MainPass specifically, a stricter
        /// guarantee than <see cref="VkFrameReapRing"/>'s own N-frames-ago per-slot reap.</summary>
        public void WaitOnly() => WaitForFenceCore();

        // Cached so a WaitOnly() call followed by the later Dispose() (both hitting the same
        // fence) waits at most once -- without this, a timed-out WaitOnly() would already have
        // destroyed _fence, and Dispose()'s own WaitForFences call would then run against a
        // dangling handle.
        private bool _fenceWaited;
        private bool _fenceWaitOk;

        private unsafe bool WaitForFenceCore()
        {
            if (_fenceWaited) return _fenceWaitOk;
            _fenceWaited = true;

            var result = _api.WaitForFences(_device, 1, in _fence, true, WaitTimeoutNs);
            if (result != Result.Success)
            {
                LibreMetaverse.Logger.Error(
                    $"[VkCommandBufferPool] WaitForFences timed out/failed (result={result}) on " +
                    $"command buffer tagged \"{_debugTag}\" -- this is the freeze/hang diagnostic; " +
                    "report this tag. Skipping the wait to avoid hanging the render thread; GPU " +
                    "state and/or this buffer's resources may now be unsafe to reuse.");
                // Don't free the command buffer back to the pool if its fence never signaled --
                // the driver may still consider it in flight. Leaking it here is strictly better
                // than freezing the whole app; the pool will simply allocate a fresh one next time.
                _api.DestroyFence(_device, _fence, null);
                _fenceWaitOk = false;
                return false;
            }
            _fenceWaitOk = true;
            return true;
        }

        public void BeginRecording()
        {
            if (_hasStarted) return;
            _hasStarted = true;
            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            _api.BeginCommandBuffer(InternalHandle, in beginInfo);
        }

        public void EndRecording()
        {
            if (!_hasStarted || _hasEnded) return;
            _hasEnded = true;
            _api.EndCommandBuffer(InternalHandle);
        }

        public void Submit() => Submit(null, null, null, _fence);

        /// <summary>Win32 keyed-mutex acquire/release for D3D11-shared-texture submissions
        /// (Mode B's cross-import path -- see plan Section 3's D3D11 cross-import decision).</summary>
        public class KeyedMutexSubmitInfo
        {
            public ulong? AcquireKey { get; set; }
            public ulong? ReleaseKey { get; set; }
            public DeviceMemory DeviceMemory { get; set; }
        }

        /// <summary>Submits this buffer. Unlike <see cref="SubmitAndWait"/>, does NOT wait on or
        /// free it -- the caller must register it with its own <see cref="VkFrameReapRing"/>
        /// (<c>ring.MarkUsed(this)</c>) right after calling this, or it will never be waited on
        /// or freed. Plan Step 2 moved this bookkeeping out of the pool (which had no way to know
        /// which panel's frame a buffer belonged to) to the caller, which does.</summary>
        public void Submit(
            ReadOnlySpan<Semaphore> waitSemaphores,
            ReadOnlySpan<PipelineStageFlags> waitDstStageMask = default,
            ReadOnlySpan<Semaphore> signalSemaphores = default,
            Fence? fence = null,
            KeyedMutexSubmitInfo? keyedMutex = null,
            IntPtr pNext = default)
        {
            SubmitCore(waitSemaphores, waitDstStageMask, signalSemaphores, fence, keyedMutex, pNext);
        }

        /// <summary>Submits and immediately waits on (and frees) only THIS buffer's own fence,
        /// without registering it with any <see cref="VkFrameReapRing"/>. Unlike
        /// <see cref="Submit()"/>, which requires the caller to separately mark it used on a
        /// ring, use this for one-off uploads/dispatches that only need their own completion --
        /// no ring involved at all.</summary>
        public void SubmitAndWait(
            ReadOnlySpan<Semaphore> waitSemaphores = default,
            ReadOnlySpan<PipelineStageFlags> waitDstStageMask = default,
            ReadOnlySpan<Semaphore> signalSemaphores = default,
            KeyedMutexSubmitInfo? keyedMutex = null,
            IntPtr pNext = default)
        {
            SubmitCore(waitSemaphores, waitDstStageMask, signalSemaphores, _fence, keyedMutex, pNext);
            Dispose();
        }

        private unsafe void SubmitCore(
            ReadOnlySpan<Semaphore> waitSemaphores,
            ReadOnlySpan<PipelineStageFlags> waitDstStageMask,
            ReadOnlySpan<Semaphore> signalSemaphores,
            Fence? fence,
            KeyedMutexSubmitInfo? keyedMutex,
            IntPtr pNext)
        {
            EndRecording();
            fence ??= _fence;

            ulong acquireKey = keyedMutex?.AcquireKey ?? 0, releaseKey = keyedMutex?.ReleaseKey ?? 0;
            DeviceMemory devMem = keyedMutex?.DeviceMemory ?? default;
            // Win32 keyed-mutex acquire timeout, in milliseconds (matches IDXGIKeyedMutex::
            // AcquireSync's dwMilliseconds semantics, per VK_KHR_win32_keyed_mutex). Was
            // uint.MaxValue (~49.7 days) -- an effectively unbounded, driver-level wait with no
            // C#-side bound and no log line on expiry, unlike every other wait in this file
            // (WaitForFenceCore's 5s WaitTimeoutNs). Two SceneViewer hangs under real
            // frame-in-flight overlap (plan Step 6) went completely silent because whatever
            // blocked wasn't wrapped in anything observable -- this is that observability, not a
            // fix for whatever's actually contending for the mutex. 5s matches WaitTimeoutNs's
            // own bound for consistency, not because 5s is independently meaningful here.
            const uint KeyedMutexAcquireTimeoutMs = 5000;
            uint timeout = keyedMutex != null ? KeyedMutexAcquireTimeoutMs : uint.MaxValue;
            Win32KeyedMutexAcquireReleaseInfoKHR mutex = default;
            if (keyedMutex != null)
                mutex = new Win32KeyedMutexAcquireReleaseInfoKHR
                {
                    SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
                    AcquireCount = keyedMutex.AcquireKey.HasValue ? 1u : 0u,
                    ReleaseCount = keyedMutex.ReleaseKey.HasValue ? 1u : 0u,
                    PAcquireKeys = &acquireKey,
                    PReleaseKeys = &releaseKey,
                    PAcquireSyncs = &devMem,
                    PReleaseSyncs = &devMem,
                    PAcquireTimeouts = &timeout,
                    PNext = (void*)pNext
                };

            fixed (Semaphore* pWaitSemaphores = waitSemaphores, pSignalSemaphores = signalSemaphores)
            fixed (PipelineStageFlags* pWaitDstStageMask = waitDstStageMask)
            {
                var commandBuffer = InternalHandle;
                var submitInfo = new SubmitInfo
                {
                    PNext = keyedMutex != null ? &mutex : (void*)pNext,
                    SType = StructureType.SubmitInfo,
                    WaitSemaphoreCount = waitSemaphores != null ? (uint)waitSemaphores.Length : 0,
                    PWaitSemaphores = pWaitSemaphores,
                    PWaitDstStageMask = pWaitDstStageMask,
                    CommandBufferCount = 1,
                    PCommandBuffers = &commandBuffer,
                    SignalSemaphoreCount = signalSemaphores != null ? (uint)signalSemaphores.Length : 0,
                    PSignalSemaphores = pSignalSemaphores,
                };

                var fenceValue = fence.Value;
                Result submitResult;
                lock (_pool._queueLock)
                {
                    _api.ResetFences(_device, 1, in fenceValue);
                    submitResult = _api.QueueSubmit(_queue, 1, in submitInfo, fenceValue);
                }
                // Previously discarded entirely -- a keyed-mutex acquire timing out (VK_TIMEOUT)
                // or any other QueueSubmit failure was silently swallowed, with nothing submitted
                // and this buffer's fence left unsignaled forever (it was ResetFences'd above,
                // never set since nothing actually ran). A later wait on that fence still
                // recovers via WaitForFenceCore's own 5s bound, but this line is what actually
                // names the failure instead of leaving it looking like a plain fence-wait
                // timeout with no explanation.
                if (submitResult != Result.Success)
                {
                    LibreMetaverse.Logger.Error(
                        $"[VkCommandBufferPool] QueueSubmit returned {submitResult} on command buffer "
                        + $"tagged \"{_debugTag}\"" + (keyedMutex != null
                            ? $" -- keyed-mutex acquire (timeout={timeout}ms) likely expired waiting on the D3D11 compositor interop handshake"
                            : "") + ". Nothing was submitted; this buffer's fence will never signal.");
                }
            }
        }
    }
}
