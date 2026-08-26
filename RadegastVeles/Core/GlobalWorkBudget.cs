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
using System.Threading;

namespace Radegast.Veles.Core;

/// <summary>
/// Process-wide ceiling on concurrently-running CPU-bound background work, shared across every
/// otherwise-independent gate in the asset/scene pipeline: <c>SceneBuildScheduler</c> (used both
/// for CPU tessellation and, via its own second instance, network-bound prefetch),
/// <c>GridTextureHelper</c>'s J2K decode/BC3-KTX2 encode gates, and <c>TextureDownloadQueue</c>'s
/// download/decode gates.
/// <para>
/// Each of those already caps its own concurrency, and <c>GridTextureHelper.DecodeGate</c>
/// already reserves cores for the render/UI thread when sizing itself
/// (<c>DefaultDecodeReservedCores</c>) -- but the gates are otherwise independent, so their peaks
/// can stack: a large region-entry burst can have all of them saturated simultaneously, well past
/// what any single gate's own reservation intended to leave for the render/UI thread. This gate
/// gives them a shared ceiling in addition to (not instead of) their own per-subsystem caps, so
/// the SUM of concurrently-running CPU-bound work across all of them is bounded by one number.
/// </para>
/// <para>
/// Acquire with <c>await GlobalWorkBudget.Gate.WaitAsync(...)</c> immediately before starting the
/// actual CPU-bound work (not before any of the subsystem's own gate/queue admission), and
/// release right after. Never <c>Wait(0)</c> here and give up -- that would need something else to
/// notice the budget freed up and retry; <c>WaitAsync</c> queues properly and any release from any
/// subsystem wakes the next waiter regardless of which subsystem it belongs to.
/// </para>
/// </summary>
internal static class GlobalWorkBudget
{
    // Reserve 2 cores for the render/UI thread, same rationale as GridTextureHelper's own
    // DecodeGate sizing (DefaultDecodeReservedCores) -- kept in sync conceptually, not by shared
    // code, since the two gates serve different roles (this is a ceiling across ALL subsystems;
    // DecodeGate is J2K decode's own local cap).
    private static readonly int Capacity = Math.Max(2, Environment.ProcessorCount - 2);

    public static readonly SemaphoreSlim Gate = new(Capacity, Capacity);
}
