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

using System.Collections.Generic;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Per-panel replacement for the pending-buffer list <see cref="VkCommandBufferPool"/> used to
/// own directly. One instance per <see cref="VkViewportControl"/> (and its owned
/// <see cref="VkInteropSwapchain"/>/<see cref="VkInteropSwapchainImage"/>), so one panel's reap
/// can never incidentally wait on or free another panel's still-in-flight work the way the old
/// shared list did -- each panel renders on its own cadence, so there is no single process-wide
/// "frame N" to reap against.
/// <para>
/// <see cref="VkContext.FramesInFlight"/> slots, round-robin by frame index.
/// <see cref="BeginFrame"/> (called once, at the very top of <c>RenderFrame</c>, before any
/// <see cref="MarkUsed"/> call for that frame) advances to this frame's slot -- the SAME
/// physical slot index this frame will reuse N frames from now, which is exactly why reaping it
/// here is correct: whatever's still in that slot was left by the frame that last used it (N
/// frames ago), and this frame is about to overwrite it. <see cref="MarkUsed"/> always appends to
/// the CURRENT frame's own slot; <see cref="FreeUsed"/> still just drains "the slot" -- it hasn't
/// been told to target anything other than <c>CurrentSlot</c>, so both of today's call sites
/// (early in <see cref="VkInteropSwapchain.BeginDraw"/>, late at <c>RenderFrame</c>'s tail) keep
/// their exact existing behavior. At <c>FramesInFlight=1</c> there is only ever one slot, so this
/// is byte-for-byte identical to the old single-list design -- <see cref="BeginFrame"/> not being
/// called yet vs. being called doesn't matter until a second slot exists to advance into. Real
/// overlap (frame F's tail no longer blocking on frame F's own submissions) is a separate,
/// later change to WHICH slot the tail targets -- this class only provides the slots; it doesn't
/// yet decide to skip waiting on the current one.
/// </para>
/// </summary>
internal sealed class VkFrameReapRing
{
    private readonly List<VkCommandBufferPool.VkCommandBuffer>[] _slots;
    private readonly object _lock = new();
    private long _frameIndex = -1;

    public VkFrameReapRing()
    {
        _slots = new List<VkCommandBufferPool.VkCommandBuffer>[VkContext.FramesInFlight];
        for (int i = 0; i < _slots.Length; i++) _slots[i] = new List<VkCommandBufferPool.VkCommandBuffer>();
    }

    // Normalized the same way VkPrimDescriptorSets/VkFrameStatsTracker's own CurrentSlot is --
    // _frameIndex starts at -1, and C#'s sign-preserving % would otherwise index _slots[-1] the
    // instant FramesInFlight > 1.
    private int CurrentSlot => (int)(((_frameIndex % VkContext.FramesInFlight) + VkContext.FramesInFlight) % VkContext.FramesInFlight);

    /// <summary>Advance to this frame's own slot. Call exactly once per frame, before any
    /// <see cref="MarkUsed"/> or <see cref="FreeUsed"/> call for that frame -- everything marked
    /// after this point until the next <see cref="BeginFrame"/> call lands in the same slot.
    /// </summary>
    public void BeginFrame()
    {
        _frameIndex++;
    }

    /// <summary>Register a just-submitted buffer to be waited on and freed by the next
    /// <see cref="FreeUsed"/> call targeting this same slot. Call once, right after
    /// <c>Submit(...)</c>.</summary>
    public void MarkUsed(VkCommandBufferPool.VkCommandBuffer buffer)
    {
        lock (_lock) _slots[CurrentSlot].Add(buffer);
    }

    /// <summary>Waits on and frees every command buffer marked used into the current slot since
    /// it was last reaped. Call once per frame (or per out-of-band upload) -- never
    /// mid-recording.</summary>
    public void FreeUsed()
    {
        lock (_lock)
        {
            var slot = _slots[CurrentSlot];
            foreach (var buffer in slot) buffer.Dispose();
            slot.Clear();
        }
    }

    /// <summary>Waits on and frees every command buffer pending in EVERY slot, not just the
    /// current one -- panel teardown only, where nothing will ever reap the other
    /// <c>FramesInFlight - 1</c> slots again once this instance is discarded. A no-op beyond
    /// <see cref="FreeUsed"/> at <c>FramesInFlight=1</c> (there is only the one slot), but real at
    /// N&gt;1: a panel can close mid-flight with a still-pending older slot FreeUsed() alone
    /// would never reach.</summary>
    public void FreeAll()
    {
        lock (_lock)
        {
            foreach (var slot in _slots)
            {
                foreach (var buffer in slot) buffer.Dispose();
                slot.Clear();
            }
        }
    }
}
