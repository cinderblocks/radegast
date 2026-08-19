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
/// Drives every scene-viewer <see cref="SceneAvatarAnimator"/> from one shared ~30 Hz timer.
/// Mirrors <see cref="FlexiSceneScheduler"/>: a busy region can have dozens of avatars, and
/// one timer/task per avatar is unnecessary overhead that scales with avatar count for no
/// benefit.
/// <para>
/// Also applies the same distance-based tick throttling as <see cref="FlexiSceneScheduler"/>
/// -- avatars far from the camera get their bone-hierarchy world matrices recomputed at a
/// fraction of the base rate (see <see cref="TickDivisor"/>) instead of paying full 30 Hz
/// skeleton-walk cost for something a few pixels wide or off to the side. Uses the same
/// 16 m / 32 m breakpoints as <see cref="FlexiSceneScheduler.TickDivisor"/> and
/// <c>SceneAvatarStreamer.AvatarLodForDistance</c> so an avatar's mesh LOD, skeletal update
/// rate, and flexi-attachment update rate all step down together at the same distances
/// rather than three independently-tuned schedules that could visibly disagree. Distance
/// uses <see cref="SceneAvatarAnimator.ApproximateWorldPosition"/>, cheap (no extra lookups)
/// but coarse -- fine for LOD bucketing, not for anything requiring precision.
/// </para>
/// </summary>
internal sealed class AvatarSceneScheduler : IDisposable
{
    private const float SimTickRate = 1f / 30f;

    private readonly GridClient _client;
    // Animator → consecutive skipped ticks since its last real Tick() call.
    private readonly ConcurrentDictionary<SceneAvatarAnimator, int> _registered = new();
    // Animators that have already had a Tick() exception logged -- avoids re-logging every
    // cycle for an avatar stuck throwing the same error, matching SceneAvatarAnimator's own
    // prior "log once" reasoning for exactly this failure mode (see this file's own doc
    // comment on the per-animator try/catch below).
    private readonly ConcurrentDictionary<SceneAvatarAnimator, bool> _loggedTickFailure = new();

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public AvatarSceneScheduler(GridClient client) => _client = client;

    public void Register(SceneAvatarAnimator animator) => _registered[animator] = 0;

    public void Unregister(SceneAvatarAnimator animator) => _registered.TryRemove(animator, out _);

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
    // Same breakpoints as FlexiSceneScheduler.TickDivisor and SceneAvatarStreamer.
    // AvatarLodForDistance -- see this class's own doc comment for why staying consistent
    // with those matters here, not just copy-paste convenience.
    private static int TickDivisor(float distance) => distance switch
    {
        < 16f => 1,  // full 30 Hz
        < 32f => 2,  // ~15 Hz
        < 64f => 4,  // ~7.5 Hz
        _     => 8,  // ~3.75 Hz -- still alive (no visible freeze/pop), close to free
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

                var self = _client.Self.SimPosition;
                var selfPos = new Vector3(self.X, self.Y, self.Z);

                foreach (var kv in _registered)
                {
                    var animator = kv.Key;

                    // Mirrors FlexiSceneScheduler's own pruning: an animator can be disposed
                    // (kill / rebuild) while its Tick() from a previous cycle is still running
                    // on this same loop -- by the time we get back here Unregister has already
                    // dropped it, but writing the counter back below would silently re-add (and
                    // permanently leak) a disposed entry. Prune instead.
                    if (animator.IsDisposed)
                    {
                        _registered.TryRemove(animator, out _);
                        _loggedTickFailure.TryRemove(animator, out _);
                        continue;
                    }

                    float dist    = Vector3.Distance(selfPos, animator.ApproximateWorldPosition);
                    int   divisor = TickDivisor(dist);
                    int   skipped = kv.Value + 1;

                    if (skipped >= divisor)
                    {
                        try
                        {
                            // Catch-up dt: the avatar animation player advances by dt, so a
                            // throttled animator needs the elapsed time since its last real
                            // tick, not just this cycle's slice, or its animations would
                            // visibly slow down rather than just update less often. Same
                            // reasoning and clamp as FlexiSceneScheduler's own catch-up dt.
                            animator.Tick(Math.Min(dt * skipped, 0.1f));
                        }
                        catch (Exception ex)
                        {
                            // Per-animator guard, not present in FlexiSceneScheduler's own
                            // identical loop (a pre-existing gap there, left alone -- out of
                            // scope here). Without this, one avatar's Tick() throwing would
                            // propagate out of this foreach and up through RunAsync's
                            // fire-and-forget Task, silently killing the whole shared
                            // scheduler -- every avatar in the scene would stop animating, not
                            // just this one. Log once per animator (not every cycle) and keep
                            // ticking everyone else.
                            if (_loggedTickFailure.TryAdd(animator, true))
                                LibreMetaverse.Logger.DebugLog($"[AnimTickFail] SceneAvatarAnimator.Tick threw: {ex}");
                        }
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
