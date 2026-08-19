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
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

/// <summary>
/// A one-time-submit command buffer pool: <see cref="CreateCommandBuffer"/> allocates a
/// buffer with its own fence, the caller records + submits it, and <see cref="FreeUsedCommandBuffers"/>
/// (called once per frame from the render core, mirroring <c>DispatchPending</c>'s per-frame
/// drain pattern elsewhere in this codebase) waits on each fence and frees the buffer.
/// Not a frame-in-flight ring buffer -- see plan Section 5 for why 2-frames-in-flight was
/// deferred until profiling shows this simpler model is actually a bottleneck.
/// </summary>
internal class VkCommandBufferPool : IDisposable
{
    private readonly Vk _api;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;

    private readonly List<VkCommandBuffer> _usedCommandBuffers = new();
    private readonly object _lock = new();

    // Guards ONLY ResetFences+QueueSubmit (VkCommandBuffer.Submit, below) -- separate from
    // _lock, which is also held across FreeUsedCommandBuffers' blocking (up to 5s) WaitForFences.
    // Reusing _lock here would let a submit from one thread block behind an unrelated fence wait
    // on another. Needed because Vulkan requires external synchronization on vkQueueSubmit calls
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
            FreeUsedCommandBuffers();
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

    /// <summary>Waits on and frees every command buffer submitted since the last call. Call once
    /// per frame (or per out-of-band upload) -- never mid-recording.</summary>
    public void FreeUsedCommandBuffers()
    {
        lock (_lock)
        {
            foreach (var usedCommandBuffer in _usedCommandBuffers) usedCommandBuffer.Dispose();
            _usedCommandBuffers.Clear();
        }
    }

    private void MarkUsed(VkCommandBuffer commandBuffer)
    {
        lock (_lock) { _usedCommandBuffers.Add(commandBuffer); }
    }

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
                return;
            }
            lock (_pool._lock)
            {
                var handle = InternalHandle;
                _api.FreeCommandBuffers(_device, _pool._commandPool, 1, in handle);
            }
            _api.DestroyFence(_device, _fence, null);
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

        public void Submit(
            ReadOnlySpan<Semaphore> waitSemaphores,
            ReadOnlySpan<PipelineStageFlags> waitDstStageMask = default,
            ReadOnlySpan<Semaphore> signalSemaphores = default,
            Fence? fence = null,
            KeyedMutexSubmitInfo? keyedMutex = null,
            IntPtr pNext = default)
        {
            SubmitCore(waitSemaphores, waitDstStageMask, signalSemaphores, fence, keyedMutex, pNext);
            _pool.MarkUsed(this);
        }

        /// <summary>Submits and immediately waits on (and frees) only THIS buffer's own fence,
        /// without adding it to the pool's shared used-buffer list. Unlike <see cref="Submit()"/>
        /// followed by the pool's <see cref="VkCommandBufferPool.FreeUsedCommandBuffers"/>, which
        /// waits on and frees EVERY other buffer currently outstanding in the shared pool too
        /// (including other panels' or other in-flight uploads' work) -- see that method's own
        /// doc comment. Use this for one-off uploads/dispatches that only need their own
        /// completion. Never call FreeUsedCommandBuffers for a buffer submitted this way; it was
        /// never tracked there.</summary>
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
            uint timeout = uint.MaxValue;
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
                lock (_pool._queueLock)
                {
                    _api.ResetFences(_device, 1, in fenceValue);
                    _api.QueueSubmit(_queue, 1, in submitInfo, fenceValue);
                }
            }
        }
    }
}
