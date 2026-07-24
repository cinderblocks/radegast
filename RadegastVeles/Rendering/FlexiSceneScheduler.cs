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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using Vector3 = System.Numerics.Vector3;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Drives every scene-viewer <see cref="FlexiPrimAnimator"/> from one shared ~30 Hz timer
/// instead of each animator owning its own <see cref="PeriodicTimer"/> and background
/// <see cref="Task"/>. A busy sim can easily have dozens of flexi objects (grass, trees,
/// flexi attachments on nearby avatars) — one timer/task per object is unnecessary
/// thread-pool and timer overhead that scales with flexi-object count for no benefit.
/// <para>
/// Also applies distance-based tick throttling: flexi prims far from the camera get
/// simulated at a fraction of the base rate (see <see cref="TickDivisor"/>) rather than
/// spending full 30 Hz spine-physics + vertex-deform cost on something a few pixels wide
/// or off to the side. Distance uses <see cref="FlexiPrimAnimator.ApproximateWorldPosition"/>,
/// which is cheap (no extra lookups) but coarse — fine for LOD bucketing, not for anything
/// requiring precision.
/// </para>
/// </summary>
internal sealed class FlexiSceneScheduler : IDisposable
{
    private const float SimTickRate = 1f / 30f;

    private readonly GridClient _client;
    // Animator → consecutive skipped ticks since its last real Tick() call.
    private readonly ConcurrentDictionary<FlexiPrimAnimator, int> _registered = new();

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public FlexiSceneScheduler(GridClient client) => _client = client;

    public void Register(FlexiPrimAnimator animator) => _registered[animator] = 0;

    public void Unregister(FlexiPrimAnimator animator) => _registered.TryRemove(animator, out _);

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _registered.Clear();
    }

    // ── Distance-based tick throttle ─────────────────────────────────────────────
    //
    // Mirrors the spirit of SceneAvatarStreamer.AvatarLodForDistance: things far from
    // the camera don't need full-rate simulation. A flexi prim that's simulated at
    // 7.5 Hz instead of 30 Hz looks identical at 48+ metres, but costs a quarter as much
    // CPU (or GPU dispatch) time.
    private static int TickDivisor(float distance) => distance switch
    {
        < 16f => 1,  // full 30 Hz
        < 32f => 2,  // ~15 Hz
        < 64f => 4,  // ~7.5 Hz
        _     => 8,  // ~3.75 Hz — still alive (no visible freeze/pop), close to free
    };

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SimTickRate));
        var sw   = Stopwatch.StartNew();
        float prev = 0f;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                float now = (float)sw.Elapsed.TotalSeconds;
                float dt  = Math.Min(now - prev, 0.1f);
                prev = now;

                // FlexiPrimAnimator.Tick() itself also gates on AnimationEnabled, but
                // skipping the whole distance-lookup pass here when animation is off
                // avoids paying for it every tick for no reason.
                if (!FlexiPrimAnimator.AnimationEnabled) continue;

                var self = _client.Self.SimPosition;
                var selfPos = new Vector3(self.X, self.Y, self.Z);

                foreach (var kv in _registered)
                {
                    var animator = kv.Key;

                    // RemoveAnimator (kill / rebuild) can dispose an animator while its Tick()
                    // from a previous cycle is still running on this same loop — by the time we
                    // get back here Unregister has already dropped it, but if we still wrote the
                    // counter back below it would silently re-add (and permanently leak) a
                    // disposed entry. Prune instead.
                    if (animator.IsDisposed)
                    {
                        _registered.TryRemove(animator, out _);
                        continue;
                    }

                    float dist    = Vector3.Distance(selfPos, animator.ApproximateWorldPosition);
                    int   divisor = TickDivisor(dist);
                    int   skipped = kv.Value + 1;

                    if (skipped >= divisor)
                    {
                        // Catch-up dt: the physics forces (gravity/wind/tension impulses)
                        // are scaled by dt, so a throttled animator needs the elapsed time
                        // since its last real tick, not just this cycle's slice, or it would
                        // settle into a visibly stiffer rest pose than an un-throttled one.
                        // Still clamped to the same 0.1s stability bound TickAndUpload's own
                        // per-cycle dt uses — the friction/tension terms are exponentiated by
                        // dt and were never meant to see a value this large.
                        animator.Tick(Math.Min(dt * skipped, 0.1f));
                        _registered[animator] = 0;
                    }
                    else
                    {
                        _registered[animator] = skipped;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
