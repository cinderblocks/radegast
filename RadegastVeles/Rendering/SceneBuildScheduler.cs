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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast.Veles.Rendering;

/// <summary>
/// A shared SL-style interest-list scheduler for scene build tasks.
/// <para>
/// Both <see cref="SceneObjectStreamer"/> and <see cref="SceneAvatarStreamer"/>
/// submit work items here instead of spawning unbounded background tasks.
/// The scheduler maintains a max-priority queue ordered by
/// <c>priority = (1 / (distanceSq + 1)) × typeMultiplier</c> and drains it
/// with a bounded concurrency pool so background tessellation never saturates
/// all CPU cores.
/// </para>
/// <para>
/// Avatars pass <c>typeMultiplier = 8f</c>; prims pass <c>1f</c>.
/// This means an avatar at 30 m outranks a prim at 5 m
/// (avatar: 1/901×8 ≈ 0.00888; prim: 1/26 ≈ 0.0385) — prims very close
/// to the camera still win over distant avatars, but the nearest avatars always
/// beat nearby prims.
/// </para>
/// </summary>
internal sealed class SceneBuildScheduler : IDisposable
{
    /// <summary>Avatar type multiplier — gives avatars higher priority than same-distance prims.</summary>
    public const float AvatarMultiplier = 8f;

    /// <summary>Prim type multiplier.</summary>
    public const float PrimMultiplier = 1f;

    // Max simultaneous build tasks (tessellation + GPU upload).
    // Keeps background work from consuming all CPU cores while the render
    // loop and animation threads run concurrently.
    private readonly SemaphoreSlim _concurrencySlots;

    private readonly object _queueLock = new();

    // Priority queue: highest-priority items dequeued first.
    // Key: unique entry id (scheduler-assigned). Value: (priority, factory).
    // We use a sorted list so the highest entry is always at the tail.
    private readonly SortedList<float, Func<CancellationToken, Task>> _queue = new(PriorityComparer.Instance);

    private bool _disposed;

    /// <summary>Number of pending (not yet started) build tasks in the queue. Cheap snapshot; safe to call from any thread.</summary>
    public int QueueCount { get { lock (_queueLock) return _queue.Count; } }

    /// <param name="maxConcurrent">Max simultaneous build tasks (default 2).</param>
    public SceneBuildScheduler(int maxConcurrent = 2)
    {
        _concurrencySlots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    /// <summary>Frustum boost factor — objects squarely in front of the camera score up to this many times higher than objects at the same distance behind the camera.</summary>
    public const float FrustumBoost = 3f;

    /// <summary>
    /// Computes the interest-list priority score for an object.
    /// Higher is more important.
    /// </summary>
    /// <param name="distanceSq">Squared distance from camera to object centre.</param>
    /// <param name="typeMultiplier">
    /// Use <see cref="AvatarMultiplier"/> for avatars, <see cref="PrimMultiplier"/> for prims.
    /// </param>
    public static float Score(float distanceSq, float typeMultiplier)
        => typeMultiplier / (distanceSq + 1f);

    /// <summary>
    /// Frustum-aware priority score.  Objects in the camera's forward half-space
    /// receive a boost proportional to how directly they are in front of the camera
    /// (dot product of object direction with camera forward vector, clamped 0–1).
    /// Objects exactly behind the camera get no boost; objects dead-ahead get
    /// <see cref="FrustumBoost"/> times the base score.
    /// </summary>
    /// <param name="distanceSq">Squared distance from camera eye to object centre.</param>
    /// <param name="typeMultiplier">Use <see cref="AvatarMultiplier"/> for avatars, <see cref="PrimMultiplier"/> for prims.</param>
    /// <param name="eyePos">Camera eye position in world space (from <see cref="Camera3D.EyePosition"/>).</param>
    /// <param name="cameraForward">Camera forward direction (from <see cref="Camera3D.ForwardDirection"/>).</param>
    /// <param name="objectPos">Object centre in world space.</param>
    public static float ScoreWithFrustum(
        float distanceSq,
        float typeMultiplier,
        OpenTK.Mathematics.Vector3 eyePos,
        OpenTK.Mathematics.Vector3 cameraForward,
        OpenTK.Mathematics.Vector3 objectPos)
    {
        float baseScore = typeMultiplier / (distanceSq + 1f);

        // Direction from eye to object (unnormalised is fine — we only need the sign).
        var toObj = objectPos - eyePos;
        float dot = OpenTK.Mathematics.Vector3.Dot(toObj, cameraForward);
        // dot > 0 → object is in front; normalise to [0,1] so directly-ahead = 1.
        float forward01 = distanceSq > 0.01f
            ? Math.Clamp(dot / MathF.Sqrt(distanceSq), 0f, 1f)
            : 1f;

        return baseScore * (1f + (FrustumBoost - 1f) * forward01);
    }

    // Maximum number of pending (not yet started) entries in the priority queue.
    // When the cap is exceeded the lowest-priority entry is dropped so high-priority
    // objects (nearby, in-frustum) are never blocked by a flood of far-away roots.
    // 500 entries comfortably handles a full 96 m draw-distance scene without
    // silently dropping objects during the initial seed burst or after a tab switch.
    private const int MaxQueueDepth = 500;

    /// <summary>
    /// Enqueue a build task.  The factory is called when the scheduler dequeues
    /// the entry and a concurrency slot becomes available.
    /// </summary>
    /// <param name="priority">Score from <see cref="Score"/>.</param>
    /// <param name="factory">Async factory that performs the actual build.</param>
    public void Enqueue(float priority, Func<CancellationToken, Task> factory)
    {
        if (_disposed) return;

        lock (_queueLock)
        {
            // SortedList requires unique keys — nudge duplicates by a tiny epsilon.
            while (_queue.ContainsKey(priority))
                priority += 1e-7f;
            _queue[priority] = factory;

            // Drop the lowest-priority entry when the queue overflows so we never
            // accumulate hundreds of pending tasks during a burst scene load.
            if (_queue.Count > MaxQueueDepth)
                _queue.RemoveAt(0); // index 0 is the minimum (lowest priority)
        }

        // Try to drain immediately if a slot is free.
        TryDrain();
    }

    /// <summary>Remove all pending (not yet started) entries from the queue.</summary>
    public void Clear()
    {
        lock (_queueLock)
            _queue.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_queueLock) _queue.Clear();
        _concurrencySlots.Dispose();
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private void TryDrain()
    {
        if (_disposed) return;
        // Non-blocking: only proceed if a slot is immediately available.
        bool acquired;
        try { acquired = _concurrencySlots.Wait(0); }
        catch (ObjectDisposedException) { return; }
        if (!acquired) return;

        Func<CancellationToken, Task>? factory;
        lock (_queueLock)
        {
            if (_queue.Count == 0) { try { _concurrencySlots.Release(); } catch (ObjectDisposedException) { } return; }
            // Dequeue the highest-priority entry (last in the sorted list).
            var idx = _queue.Count - 1;
            factory = _queue.Values[idx];
            _queue.RemoveAt(idx);
        }

        _ = RunAsync(factory);
    }

    private async Task RunAsync(Func<CancellationToken, Task> factory)
    {
        try
        {
            await factory(CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* each factory is responsible for its own error handling */ }
        finally
        {
            if (!_disposed)
            {
                try
                {
                    _concurrencySlots.Release();
                    // A slot opened — try to drain the next entry.
                    TryDrain();
                }
                catch (ObjectDisposedException) { /* scheduler was disposed mid-flight */ }
            }
        }
    }

    // IComparer that orders floats ascending so the SortedList tail is the max.
    private sealed class PriorityComparer : IComparer<float>
    {
        public static readonly PriorityComparer Instance = new();
        public int Compare(float x, float y) => x.CompareTo(y);
    }
}
