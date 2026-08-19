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
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using LibreMetaverse.Rendering;
using OmVector3  = LibreMetaverse.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Vector3    = System.Numerics.Vector3;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Streams in-world prim objects from all connected simulators into the
/// scene-object layer of a <see cref="VkViewportControl"/>.
/// <para>
/// Objects from neighboring regions within <see cref="DrawDistance"/> metres of
/// the agent are automatically included and offset to world-space coordinates.
/// Scene keys encode the simulator index in the upper 32 bits and the prim
/// LocalID in the lower 32 bits, preventing collisions across region boundaries.
/// </para>
/// </summary>
internal sealed class SceneObjectStreamer : IDisposable
{
    private readonly GridClient          _client;
    private readonly ISceneViewport      _viewport;
    private readonly PrimMeshBuilder     _builder;
    private readonly SceneBuildScheduler _scheduler;

    // Network-bound prefetch stage. Wider than the CPU build scheduler because its slots
    // spend their time awaiting HTTP downloads, not computing; LibreMetaverse's download
    // manager provides the actual connection-level throttle.
    private readonly SceneBuildScheduler _fetchScheduler = new(maxConcurrent: 8);

    // sceneKey → the in-flight build attempt for that key. Completed is set on every exit path
    // of PrefetchThenScheduleBuildAsync/BuildObjectAsync (see MarkAttemptCompleted) -- it is what
    // lets EnqueueBuild tell "a build for this key is still genuinely running" apart from "a build
    // for this key finished a while ago and this entry is just stale bookkeeping" (the dictionary
    // entry itself outlives a *successful* build by design, per BuildObjectAsync's finally-block
    // comment, so ContainsKey/TryGetValue alone can never make that distinction).
    private sealed class BuildAttempt
    {
        public readonly CancellationTokenSource Cts;
        public readonly long StartedAtTicks;
        public volatile bool Completed;
        public BuildAttempt(CancellationTokenSource cts)
        {
            Cts            = cts;
            StartedAtTicks = Environment.TickCount64;
        }
    }

    // Safety valve for the defer gate in EnqueueBuild: SceneBuildScheduler.Enqueue silently
    // evicts the lowest-priority pending entry when its MaxQueueDepth (500) is exceeded -- that
    // factory then NEVER runs, so MarkAttemptCompleted never fires for it and Completed would
    // stay false forever without this, permanently starving that key (every future EnqueueBuild
    // would defer indefinitely, never actually rebuilding it -- silent and worse than the churn
    // this fix targets). Past this many ms with no completion, treat the attempt as abandoned
    // and fall through to cancel+restart instead of deferring forever. Far above any legitimate
    // build's observed duration (small objects near-instant, worst logged case ~350ms).
    private const long StuckAttemptTimeoutMs = 10_000;

    private readonly ConcurrentDictionary<ulong, BuildAttempt> _inflight = new();

    // Dirty roots queued for tessellation (sceneKey → timestamp of first enqueue).
    private readonly ConcurrentDictionary<ulong, long> _dirty = new();

    // Scene keys that currently have a live scene-object submission.
    private readonly ConcurrentDictionary<ulong, byte> _rendered = new();

    // Tracks the J2K resolution level of the textures currently applied to each rendered object.
    // -1 = full quality, 0 = preview, 1-4 = LOD levels.  Updated each time a texture patch arrives.
    // Used to detect when an object moved close enough to deserve a texture quality upgrade.
    private readonly ConcurrentDictionary<ulong, int> _textureLodLevel = new();

    // Reverse parent index: rootSceneKey → set of child scene keys.
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, byte>> _childrenByParent = new();

    // The pose this streamer last *completed* a full rebuild at, per multi-prim linkset -- see
    // OnTerseObjectUpdate's own epsilon-gate comment for why this exists. Seeded by
    // BuildObjectAsync on completion, not by the terse-update handler that decides to rebuild:
    // recording the pose at decision time would mark a pose "rebuilt" even when the rebuild it
    // triggered later fails (e.g. the viewport drops the GPU upload under OOM pressure), which
    // would wrongly suppress a later, genuinely-needed retry at that same pose.
    private readonly ConcurrentDictionary<ulong, (Vector3 Position, Quaternion Rotation, Vector3 Scale)> _lastRebuiltPose = new();

    // Diagnostic-only (2026-08-19, chasing a "1800+ Upload Q, Obj/SceneFaces bit-identical for
    // 28+ seconds, Build Q drains to 0 and stays there" churn session). EnqueueBuild's
    // "Progressive placeholder for first appearance" branch (below) fires every time this
    // method runs for a key not yet in _rendered -- including a key whose PREVIOUS build got
    // cancelled by this same call (see the _inflight.TryRemove+Cancel a few lines above that
    // branch) and is being retried. If something keeps re-dirtying the same small set of keys
    // faster than their builds can complete, each retry re-submits a fresh placeholder via
    // SubmitSceneObject -- real render-thread work with zero net effect on _rendered/Obj count,
    // which is exactly the shape logged above. totalCalls vs. distinctKeysSeen (same pattern as
    // GridTextureHelper's DownloadSkBitmapLodAsync dedup diagnostic) distinguishes "many
    // different objects each appearing once" from "a small set of objects re-entering this
    // branch over and over."
    private long _placeholderSubmitCalls;
    private readonly ConcurrentDictionary<ulong, byte> _placeholderSubmitSeenKeys = new();
    private long _lastPlaceholderDiagLogTicks;

    // Diagnostic-only (2026-08-19, fix for the churn above): EnqueueBuild now DEFERS instead of
    // cancelling when a build for the key is still genuinely running (BuildAttempt.Completed ==
    // false), re-marking the key dirty so ProcessDirty retries it once the running attempt
    // finishes -- this is the replacement signal for what used to be an inflightCancels counter
    // that fired on every cancel-and-restart; it should climb where that used to. The one way
    // this fix can go wrong is starvation: if Completed is ever left unset on some exit path,
    // that key defers FOREVER and never rebuilds again. _inflight.Values.Count(a => !a.Completed)
    // sampled alongside this counter (below, as "stillRunning") is the canary -- it must stay
    // bounded/draining, not grow monotonically. _staleAttemptTimeouts is the release valve for
    // the one concrete leak found: SceneBuildScheduler.Enqueue's queue-depth eviction drops a
    // factory without ever running it (see StuckAttemptTimeoutMs's own comment).
    private long _deferredBuildCount;
    private long _staleAttemptTimeouts;

    // ── Sim index registry ────────────────────────────────────────────────────────
    // Upper 32 bits of a scene key encode a sim index (0 = current sim, 1-N for neighbors).
    // This lets us distinguish objects with the same LocalID in different regions.
    private int _nextNeighborIndex = 0;
    private readonly ConcurrentDictionary<ulong, uint> _neighborSimIndex = new(); // handle → index
    private readonly ConcurrentDictionary<uint, Simulator> _simByIndex    = new(); // index → sim

    /// <summary>Number of object build tasks currently running.</summary>
    public int InflightCount => _inflight.Count;

    private const int DebounceMs = 50;

    private readonly Timer  _debounceTimer;
    // Fires every 10 s to upgrade textures on objects that the avatar walked closer to.
    private readonly Timer  _textureLodTimer;
    private bool            _disposed;

    private float _maxStreamRadius = 96f;

    /// <summary>Gets or sets the streaming radius in metres (default 96).</summary>
    public float DrawDistance
    {
        get => _maxStreamRadius;
        set
        {
            _maxStreamRadius = Math.Max(16f, Math.Min(512f, value));
            CullBeyondDrawDistance();
        }
    }

    public SceneObjectStreamer(GridClient client, ISceneViewport viewport,
        SceneBuildScheduler scheduler)
    {
        _client    = client;
        _viewport  = viewport;
        _builder   = new PrimMeshBuilder(client);
        _scheduler = scheduler;

        _debounceTimer   = new Timer(_ => ProcessDirty(), null,
            Timeout.Infinite, Timeout.Infinite);
        _textureLodTimer = new Timer(_ => CheckTextureLodUpgrades(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the thread-pool after a linkset is built and submitted to the viewport.
    /// Arguments: (rootLocalId, submission).  Subscribe before calling <see cref="OnObjectUpdate"/>.
    /// </summary>
    public event Action<uint, PrimRenderSubmission>? ObjectBuilt;

    /// <summary>
    /// Call when a prim update is received from any connected simulator.
    /// Skips attachments and avatars.  Objects outside the stream radius are culled.
    /// </summary>
    public void OnObjectUpdate(Simulator sim, Primitive prim, bool isAttachment)
    {
        if (_disposed) return;
        if (isAttachment) return;

        var currentSim = _client.Network.CurrentSim;
        var rootLocalId = prim.ParentID == 0 ? prim.LocalID : prim.ParentID;
        var sceneKey    = MakeSceneKey(sim, rootLocalId, currentSim);

        // Maintain the reverse parent index for child prims.
        if (prim.ParentID != 0)
        {
            var childKey = MakeSceneKey(sim, prim.LocalID, currentSim);
            _childrenByParent.GetOrAdd(sceneKey, _ => new ConcurrentDictionary<ulong, byte>())
                             .TryAdd(childKey, 0);
        }

        // World-space position includes region offset for neighbor sims.
        var rootPos  = GetRootWorldPosition(sim, rootLocalId, prim);
        var worldPos = ApplyRegionOffset(rootPos, sim, currentSim);
        var avatarPos = _client.Self.SimPosition;

        if (!IsWithinRadius(worldPos, avatarPos, _maxStreamRadius))
        {
            _viewport.RemoveSceneObject(sceneKey);
            return;
        }

        EnqueueDirty(sceneKey);
    }

    /// <summary>
    /// Call when a terse (position/rotation) update arrives for a prim.
    /// Fast-paths the translation for already-rendered objects in the current sim.
    /// Objects from neighbor sims always use the slow path (rebuild on transform change).
    /// </summary>
    public void OnTerseObjectUpdate(Simulator sim, Primitive prim)
    {
        if (_disposed) return;
        if (prim.PrimData.PCode == PCode.Avatar) return;

        var currentSim  = _client.Network.CurrentSim;
        var rootLocalId = prim.ParentID == 0 ? prim.LocalID : prim.ParentID;
        var sceneKey    = MakeSceneKey(sim, rootLocalId, currentSim);

        var rootPos  = GetRootWorldPosition(sim, rootLocalId, prim);
        var worldPos = ApplyRegionOffset(rootPos, sim, currentSim);
        var avatarPos = _client.Self.SimPosition;

        if (!IsWithinRadius(worldPos, avatarPos, _maxStreamRadius))
        {
            CancelAndRemove(sceneKey);
            return;
        }

        if (_rendered.ContainsKey(sceneKey))
        {
            if (prim.ParentID == 0)
            {
                var s   = prim.Scale;
                var r   = prim.Rotation;
                var p   = prim.Position;
                var off = RegionOffset(sim, currentSim);
                var scale    = new Vector3(s.X, s.Y, s.Z);
                var rotation = new Quaternion(r.X, r.Y, r.Z, r.W);
                var position = new Vector3(p.X + off.X, p.Y + off.Y, p.Z);
                var velocity        = new Vector3(prim.Velocity.X, prim.Velocity.Y, prim.Velocity.Z);
                var acceleration    = new Vector3(prim.Acceleration.X, prim.Acceleration.Y, prim.Acceleration.Z);
                var angularVelocity = new Vector3(prim.AngularVelocity.X, prim.AngularVelocity.Y, prim.AngularVelocity.Z);
                // Dead-reckon between terse updates (velocity/angular velocity carried by the
                // packet) rather than snapping directly to each update's position, so scripted
                // and physical object motion reads as continuous instead of teleporting.
                _viewport.SetSceneObjectMotion(sceneKey, scale, rotation, position, velocity, angularVelocity, acceleration);

                // Only rebuild a multi-prim linkset when its pose actually changed meaningfully
                // since the last rebuild -- SetSceneObjectMotion above already applies the cheap
                // whole-object transform on every terse update regardless (and
                // ExtrapolateMovingSceneObjects dead-reckons it every frame), so a linkset that's
                // stationary but still sending terse updates (a texture-anim object, a rotating
                // sign holding a keyframe, a physics object jittering at rest) was paying for a
                // full mesh/texture/material rebuild on every single packet for no visual benefit.
                // The original reason this rebuild exists at all -- child-face positions need
                // recalculating at the new root location -- is preserved for a REAL pose change;
                // this only skips the redundant rebuild when nothing actually moved. Scale
                // included alongside position/rotation since a resize should still trigger a
                // rebuild.
                bool isSinglePrim = !_childrenByParent.TryGetValue(sceneKey, out var ch) || ch.IsEmpty;
                if (!isSinglePrim)
                {
                    const float posEpsilon = 0.01f;   // 1cm
                    const float scaleEpsilon = 0.01f;
                    const float rotDotEpsilon = 1e-4f;
                    bool poseChanged = true;
                    if (_lastRebuiltPose.TryGetValue(sceneKey, out var last))
                    {
                        poseChanged =
                            Vector3.DistanceSquared(last.Position, position) > posEpsilon * posEpsilon ||
                            Vector3.DistanceSquared(last.Scale, scale) > scaleEpsilon * scaleEpsilon ||
                            MathF.Abs(1f - MathF.Abs(Quaternion.Dot(last.Rotation, rotation))) > rotDotEpsilon;
                    }
                    if (poseChanged)
                    {
                        // Not recorded here: BuildObjectAsync seeds _lastRebuiltPose itself once
                        // the rebuild this triggers actually completes -- recording the decision
                        // pose here instead would mark a pose "rebuilt" even when BuildObjectAsync
                        // throws or CollectLinkset comes back empty, permanently masking a real
                        // pose change from a later gate check.
                        EnqueueDirty(sceneKey);
                    }
                }

                // Check whether the object moved close enough to deserve a texture quality upgrade.
                // Use the world-space distance already computed above.
                var dx = worldPos.X - avatarPos.X;
                var dy = worldPos.Y - avatarPos.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy + (worldPos.Z - avatarPos.Z) * (worldPos.Z - avatarPos.Z));
                if (_textureLodLevel.TryGetValue(sceneKey, out int curTexLod))
                {
                    int desiredTexLod = TextureLodLevelForDistance(dist);
                    if (IsTextureLodHigherQuality(desiredTexLod, curTexLod))
                        EnqueueDirty(sceneKey);
                }
            }
        }
        else
        {
            EnqueueDirty(sceneKey);
        }
    }

    /// <summary>
    /// Call when a kill-object notification is received from any connected simulator.
    /// </summary>
    public void OnKillObject(Simulator sim, uint localId)
    {
        if (_disposed) return;

        var currentSim = _client.Network.CurrentSim;
        var sceneKey   = MakeSceneKey(sim, localId, currentSim);
        CancelAndRemove(sceneKey);

        if (sim.ObjectsPrimitives.TryGetValue(localId, out var prim) && prim.ParentID != 0)
        {
            var parentKey = MakeSceneKey(sim, prim.ParentID, currentSim);
            if (_childrenByParent.TryGetValue(parentKey, out var siblings))
                siblings.TryRemove(sceneKey, out _);
            EnqueueDirty(parentKey);
        }
        else
        {
            _childrenByParent.TryRemove(sceneKey, out _);
        }
    }

    /// <summary>
    /// Enqueues all currently rendered root IDs for a rebuild so they pick up
    /// the new LOD level (called when draw distance changes).
    /// </summary>
    public void DirtyAllRendered()
    {
        if (_disposed) return;
        var now = Environment.TickCount64;
        foreach (var key in _rendered.Keys)
            _dirty.AddOrUpdate(key, now, (_, _) => now);
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    /// <summary>
    /// Re-enqueues all currently rendered root IDs for a rebuild after a GL context reset.
    /// </summary>
    public void RebuildAllRendered()
    {
        if (_disposed) return;
        var now = Environment.TickCount64 - DebounceMs;
        foreach (var key in _rendered.Keys)
            _dirty.AddOrUpdate(key, now, (_, _) => now);
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(0, Timeout.Infinite);
    }

    /// <summary>
    /// Immediately removes any rendered objects that now lie outside the current
    /// <see cref="DrawDistance"/>.
    /// </summary>
    public void CullBeyondDrawDistance()
    {
        if (_disposed) return;
        var avatarPos  = _client.Self.SimPosition;
        var currentSim = _client.Network.CurrentSim;
        if (currentSim == null) return;

        foreach (var sceneKey in _rendered.Keys)
        {
            var sim         = SimForSceneKey(sceneKey) ?? currentSim;
            var rootLocalId = LocalIdForSceneKey(sceneKey);
            var objs        = sim.ObjectsPrimitives;
            if (objs == null || !objs.TryGetValue(rootLocalId, out var root)) continue;
            var worldPos = ApplyRegionOffset(new Vector3(root.Position.X, root.Position.Y, root.Position.Z), sim, currentSim);
            if (!IsWithinRadius(worldPos, avatarPos, _maxStreamRadius))
                CancelAndRemove(sceneKey);
        }
    }

    /// <summary>
    /// Remove all streamed objects from the viewport and cancel all builds.
    /// Called on sim change.
    /// </summary>
    public void Clear()
    {
        if (_disposed) return;
        _dirty.Clear();
        _rendered.Clear();
        _textureLodLevel.Clear();
        _childrenByParent.Clear();
        _neighborSimIndex.Clear();
        _simByIndex.Clear();
        Interlocked.Exchange(ref _nextNeighborIndex, 0);
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _fetchScheduler.Clear();

        foreach (var (_, attempt) in _inflight)
        {
            attempt.Cts.Cancel();
            attempt.Cts.Dispose();
        }
        _inflight.Clear();

        _viewport.ClearAllSceneObjects();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Dispose();
        _textureLodTimer.Dispose();
        _fetchScheduler.Dispose();
        foreach (var attempt in _inflight.Values) { attempt.Cts.Cancel(); attempt.Cts.Dispose(); }
        _inflight.Clear();
    }

    // ── Sim index helpers ─────────────────────────────────────────────────────────

    // Returns 0 for the current sim, 1-N for neighbor sims (stable per handle).
    private uint GetSimIndex(Simulator sim, Simulator? currentSim)
    {
        if (currentSim == null || sim == currentSim) return 0u;
        var handle = sim.Handle;
        if (_neighborSimIndex.TryGetValue(handle, out uint existing))
        {
            _simByIndex[existing] = sim;
            return existing;
        }
        uint newIdx = (uint)Interlocked.Increment(ref _nextNeighborIndex);
        uint idx    = _neighborSimIndex.GetOrAdd(handle, newIdx);
        _simByIndex[idx] = sim;
        return idx;
    }

    private ulong MakeSceneKey(Simulator sim, uint localId, Simulator? currentSim)
        => ((ulong)GetSimIndex(sim, currentSim) << 32) | localId;

    private Simulator? SimForSceneKey(ulong key)
    {
        uint simIndex = (uint)(key >> 32);
        if (simIndex == 0) return _client.Network.CurrentSim;
        return _simByIndex.TryGetValue(simIndex, out var s) ? s : null;
    }

    private static uint LocalIdForSceneKey(ulong key) => (uint)(key & 0xFFFF_FFFF);

    // ── Region offset helpers ─────────────────────────────────────────────────────

    // Returns the world-space offset of `sim` relative to `currentSim` in metres.
    private static Vector3 RegionOffset(Simulator sim, Simulator? currentSim)
    {
        if (currentSim == null || sim == currentSim) return Vector3.Zero;
        Utils.LongToUInts(currentSim.Handle, out uint cx, out uint cy);
        Utils.LongToUInts(sim.Handle,        out uint sx, out uint sy);
        return new Vector3((int)sx - (int)cx, (int)sy - (int)cy, 0f);
    }

    private static Vector3 ApplyRegionOffset(Vector3 localPos, Simulator sim, Simulator? currentSim)
    {
        if (currentSim == null || sim == currentSim) return localPos;
        Utils.LongToUInts(currentSim.Handle, out uint cx, out uint cy);
        Utils.LongToUInts(sim.Handle,        out uint sx, out uint sy);
        return new Vector3(localPos.X + (sx - cx), localPos.Y + (sy - cy), localPos.Z);
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private void EnqueueDirty(ulong sceneKey)
    {
        var now = Environment.TickCount64;
        _dirty.AddOrUpdate(sceneKey, now, (_, _) => now);
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void CancelAndRemove(ulong sceneKey)
    {
        if (_inflight.TryRemove(sceneKey, out var attempt))
        {
            attempt.Cts.Cancel();
            attempt.Cts.Dispose();
        }
        _dirty.TryRemove(sceneKey, out _);
        _rendered.TryRemove(sceneKey, out _);
        _textureLodLevel.TryRemove(sceneKey, out _);
        _lastRebuiltPose.TryRemove(sceneKey, out _);
        _viewport.RemoveSceneObject(sceneKey);
    }

    private const int MaxBuildsPerTick = 20;

    private float ScoreDue(
        ulong sceneKey,
        OmVector3 avatarPos,
        Vector3 eyePos,
        Vector3 camFwd)
    {
        float distSq    = DistanceSq(sceneKey, avatarPos);
        var sim         = SimForSceneKey(sceneKey);
        var rootLocalId = LocalIdForSceneKey(sceneKey);
        var ovPrims     = sim?.ObjectsPrimitives;
        if (ovPrims != null && ovPrims.TryGetValue(rootLocalId, out var rp))
        {
            var off    = RegionOffset(sim!, _client.Network.CurrentSim);
            var objPos = new Vector3(rp.Position.X + off.X, rp.Position.Y + off.Y, rp.Position.Z);
            return SceneBuildScheduler.ScoreWithFrustum(
                distSq, SceneBuildScheduler.PrimMultiplier, eyePos, camFwd, objPos);
        }
        return SceneBuildScheduler.Score(distSq, SceneBuildScheduler.PrimMultiplier);
    }

    private void ProcessDirty()
    {
        if (_disposed) return;

        var now = Environment.TickCount64;
        var due = new List<ulong>();

        foreach (var (key, enqueued) in _dirty)
        {
            if (now - enqueued >= DebounceMs)
                due.Add(key);
        }

        if (due.Count > 1)
        {
            var avatarPos = _client.Self.SimPosition;
            var cam       = _viewport.Camera;
            var eyePos    = cam.EyePosition;
            var camFwd    = cam.ForwardDirection;

            var scored = new List<(float score, ulong key)>(due.Count);
            foreach (var key in due)
                scored.Add((ScoreDue(key, avatarPos, eyePos, camFwd), key));

            for (int i = 1; i < scored.Count; i++)
            {
                var k = scored[i];
                int j = i - 1;
                while (j >= 0 && scored[j].score < k.score)
                {
                    scored[j + 1] = scored[j];
                    j--;
                }
                scored[j + 1] = k;
            }

            due.Clear();
            foreach (var (_, key) in scored)
                due.Add(key);
        }

        int dispatched = 0;
        foreach (var key in due)
        {
            if (dispatched >= MaxBuildsPerTick) break;
            _dirty.TryRemove(key, out _);
            EnqueueBuild(key);
            dispatched++;
        }

        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void EnqueueBuild(ulong sceneKey)
    {
        if (_disposed) return;

        // Fix for the churn confirmed on 2026-08-19 (see class-level diagnostic comments above):
        // a build for this key that is still genuinely running (not yet reached
        // PrefetchThenScheduleBuildAsync/BuildObjectAsync's completion) is left alone instead of
        // being cancelled-and-restarted. Killing it here unconditionally, as the old code did,
        // meant an object whose terse-update rate exceeds its own build completion rate (e.g. a
        // continuously moving/rotating linkset) had every attempt killed by the next one before
        // it could ever finish -- confirmed via inflightCancels climbing 179->438 over ~24s while
        // distinctKeysSeen stayed flat at 727-728 (same ~728 objects, never completing). Instead,
        // re-mark the key dirty so ProcessDirty retries it on a LATER tick once the running
        // attempt actually finishes -- bounded by real build throughput, not re-dirty rate.
        if (_inflight.TryGetValue(sceneKey, out var runningAttempt) && !runningAttempt.Completed)
        {
            long ageMs = Environment.TickCount64 - runningAttempt.StartedAtTicks;
            if (ageMs < StuckAttemptTimeoutMs)
            {
                Interlocked.Increment(ref _deferredBuildCount);
                _dirty.AddOrUpdate(sceneKey, Environment.TickCount64, (_, _) => Environment.TickCount64);
                _debounceTimer.Change(DebounceMs, Timeout.Infinite);
                return;
            }
            // Past the timeout with no completion -- most likely evicted from a scheduler queue
            // (see StuckAttemptTimeoutMs's comment) rather than genuinely still progressing.
            // Fall through to cancel+restart below instead of deferring forever.
            Interlocked.Increment(ref _staleAttemptTimeouts);
        }

        if (_inflight.TryRemove(sceneKey, out var oldAttempt))
        {
            // Either the build already COMPLETED, or it timed out above without completing
            // (most likely evicted from a scheduler queue before its factory ever ran). Either
            // way, cancelling here is safe: if the old factory somehow still runs later, its
            // MarkAttemptCompleted call is reference-equality-guarded against the NEW attempt
            // this call is about to install below, so it will safely no-op instead of clobbering
            // the new one's state.
            oldAttempt.Cts.Cancel();
            oldAttempt.Cts.Dispose();
        }

        var cts     = new CancellationTokenSource();
        var token   = cts.Token;
        var attempt = new BuildAttempt(cts);
        _inflight[sceneKey] = attempt;

        var avatarPos   = _client.Self.SimPosition;
        float distSq    = DistanceSq(sceneKey, avatarPos);
        var cam         = _viewport.Camera;
        var eyePos      = cam.EyePosition;
        var camFwd      = cam.ForwardDirection;

        Vector3 objPos        = default;
        bool hasPosForFrustum = false;
        var sim         = SimForSceneKey(sceneKey);
        var rootLocalId = LocalIdForSceneKey(sceneKey);
        var ovPrims     = sim?.ObjectsPrimitives;
        if (ovPrims != null && ovPrims.TryGetValue(rootLocalId, out var rp))
        {
            var off = RegionOffset(sim!, _client.Network.CurrentSim);
            objPos           = new Vector3(rp.Position.X + off.X, rp.Position.Y + off.Y, rp.Position.Z);
            hasPosForFrustum = true;
        }

        float priority = hasPosForFrustum
            ? SceneBuildScheduler.ScoreWithFrustum(distSq, SceneBuildScheduler.PrimMultiplier, eyePos, camFwd, objPos)
            : SceneBuildScheduler.Score(distSq, SceneBuildScheduler.PrimMultiplier);

        // Progressive placeholder for first appearance.
        if (!_rendered.ContainsKey(sceneKey) && sim != null)
        {
            if (ovPrims != null &&
                ovPrims.TryGetValue(rootLocalId, out var rootPrim) &&
                rootPrim.Scale.LengthSquared() > 0f)
            {
                var off     = RegionOffset(sim, _client.Network.CurrentSim);
                var scale   = new Vector3(rootPrim.Scale.X, rootPrim.Scale.Y, rootPrim.Scale.Z);
                var wPos    = new Vector3(rootPrim.Position.X + off.X, rootPrim.Position.Y + off.Y, rootPrim.Position.Z);
                var placeholder = PlaceholderMeshFactory.Build(
                    $"ph:{rootLocalId}", scale, wPos, rootPrimLocalId: rootLocalId);
                _viewport.SubmitSceneObject(sceneKey, placeholder);

                long totalCalls = Interlocked.Increment(ref _placeholderSubmitCalls);
                _placeholderSubmitSeenKeys.TryAdd(sceneKey, 0);
                long now2 = Environment.TickCount64;
                if (now2 - Interlocked.Read(ref _lastPlaceholderDiagLogTicks) >= 1000)
                {
                    Interlocked.Exchange(ref _lastPlaceholderDiagLogTicks, now2);
                    int stillRunning = 0;
                    foreach (var a in _inflight.Values)
                        if (!a.Completed) stillRunning++;
                    LibreMetaverse.Logger.Debug(
                        $"[SceneObjectStreamer] EnqueueBuild placeholder-path: totalCalls={totalCalls}, "
                        + $"distinctKeysSeen={_placeholderSubmitSeenKeys.Count}, "
                        + $"deferredBuilds={Interlocked.Read(ref _deferredBuildCount)}, "
                        + $"staleTimeouts={Interlocked.Read(ref _staleAttemptTimeouts)}, "
                        + $"stillRunning={stillRunning} -- deferredBuilds climbing (replacing the "
                        + "old cancel-and-restart churn, fixed 2026-08-19 by deferring instead of "
                        + "cancelling) is expected and fine. stillRunning must stay bounded/"
                        + "draining, not grow monotonically -- if it does, some exit path is "
                        + "failing to mark its BuildAttempt Completed and the affected keys will "
                        + "never rebuild again (a worse, silent regression) unless staleTimeouts "
                        + "is also climbing to match, which means the 10s safety valve is "
                        + "catching it and retrying instead.");
                }
            }
        }

        _fetchScheduler.Enqueue(priority, _ => PrefetchThenScheduleBuildAsync(sceneKey, priority, token, attempt));
    }

    // Marks a BuildAttempt as completed, guarded by reference equality: if a newer EnqueueBuild
    // (via CancelAndRemove or, after the churn fix, a build that genuinely finished and was later
    // replaced) already swapped _inflight[sceneKey] for a different BuildAttempt, this must not
    // touch it -- doing so would let a stale attempt's completion incorrectly mark a NEWER,
    // still-running attempt as done, which would let EnqueueBuild cancel it prematurely again.
    // Called from every exit path of the two build stages below -- see the class-level comment on
    // _deferredBuildCount for why a missed call site here is a silent, worse-than-churn regression.
    private void MarkAttemptCompleted(ulong sceneKey, BuildAttempt attempt)
    {
        if (_inflight.TryGetValue(sceneKey, out var current) && ReferenceEquals(current, attempt))
            current.Completed = true;
    }

    /// <summary>
    /// Stage 1 of the build pipeline (network-bound): prefetch the linkset's mesh assets
    /// and sculpt-texture bytes in parallel under <see cref="_fetchScheduler"/>, whose
    /// slots are cheap to hold across HTTP waits. Stage 2 (CPU-bound) then runs in the
    /// shared build scheduler against warm caches, so tessellation slots do pure CPU work
    /// instead of serialising on one download per prim.
    /// </summary>
    private async Task PrefetchThenScheduleBuildAsync(
        ulong sceneKey, float priority, CancellationToken token, BuildAttempt attempt)
    {
        if (_disposed || token.IsCancellationRequested)
        {
            MarkAttemptCompleted(sceneKey, attempt);
            return;
        }

        try
        {
            var sim = SimForSceneKey(sceneKey);
            if (sim != null)
            {
                // BuildObjectAsync re-collects the linkset when it runs: children that
                // arrive while the prefetch is in flight must be part of the build (the
                // prefetch for them is merely missed — the build path downloads on miss).
                var prims = CollectLinkset(sim, LocalIdForSceneKey(sceneKey));
                if (prims is { Count: > 0 })
                    await _builder.PrefetchLinksetAssetsAsync(prims, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            MarkAttemptCompleted(sceneKey, attempt);
            return;
        }
        catch { /* prefetch is best-effort; BuildObjectAsync downloads on cache miss */ }

        if (_disposed || token.IsCancellationRequested)
        {
            MarkAttemptCompleted(sceneKey, attempt);
            return;
        }
        // Ownership of marking Completed now passes to BuildObjectAsync's own finally block.
        _scheduler.Enqueue(priority, _ => BuildObjectAsync(sceneKey, token, attempt));
    }

    private async Task BuildObjectAsync(ulong sceneKey, CancellationToken token, BuildAttempt attempt)
    {
        if (_disposed)
        {
            MarkAttemptCompleted(sceneKey, attempt);
            return;
        }

        try
        {
            var sim         = SimForSceneKey(sceneKey);
            var rootLocalId = LocalIdForSceneKey(sceneKey);
            if (sim == null) return;

            var prims = CollectLinkset(sim, rootLocalId);
            if (prims == null || prims.Count == 0)
            {
                _viewport.RemoveSceneObject(sceneKey);
                return;
            }

            var rootPrimForLod = prims.Find(p => p.LocalID == rootLocalId) ?? prims[0];
            float dist    = OmVector3.Distance(rootPrimForLod.Position, _client.Self.SimPosition);
            var   lod     = LodForDistance(dist);
            int   texLod  = TextureLodLevelForDistance(dist);

            var submission = await _builder.BuildAsync(
                prims, rootLocalId,
                label:                 $"prim:{rootLocalId}",
                progress:              null,
                ct:                    token,
                detailLevel:           lod,
                textureResolutionLevel: texLod,
                texturePatch: new Progress<SceneTexturePatch>(patch =>
                {
                    if (token.IsCancellationRequested)
                    {
                        patch.Bitmap?.Dispose();
                        return;
                    }
                    try
                    {
                        // Record the quality level so we can detect upgrade opportunities later.
                        _textureLodLevel[sceneKey] = patch.ResolutionLevel;
                        // Stamp the full scene key so the viewport resolves the owning linkset by
                        // direct dictionary lookup: _sceneObjects is keyed by sceneKey (sim index
                        // << 32 | rootLocalId). Without this the lookup falls back to
                        // (ulong)RootLocalId -- the *child* prim's localId for linkset faces --
                        // which never matches a root sceneKey, so the patch is deferred and
                        // ultimately dropped (faces stay untextured).
                        _viewport.PatchSceneObjectTexture(patch with { SceneKey = sceneKey }, token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Token cancelled between the IsCancellationRequested check and
                        // SemaphoreSlim.Wait — bitmap already disposed inside
                        // PatchSceneObjectTexture; swallow here to avoid crashing the thread pool.
                    }
                }))
                .ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            // Apply world-space translation: sim-local position + region offset for neighbor sims.
            var rootPrim   = rootPrimForLod;
            var regionOff  = RegionOffset(sim, _client.Network.CurrentSim);
            var worldPos   = new Vector3(
                rootPrim.Position.X + regionOff.X,
                rootPrim.Position.Y + regionOff.Y,
                rootPrim.Position.Z);

            if (worldPos != Vector3.Zero)
            {
                var flexiFaceIndices = new System.Collections.Generic.HashSet<int>();
                foreach (var fp in submission.FlexiPrims)
                    for (int fi = fp.FaceStart; fi < fp.FaceStart + fp.FaceCount; fi++)
                        flexiFaceIndices.Add(fi);

                var worldTransMat = Matrix4x4.CreateTranslation(worldPos);

                var translated = new PrimRenderFace[submission.Faces.Length];
                for (int i = 0; i < submission.Faces.Length; i++)
                {
                    if (flexiFaceIndices.Contains(i))
                        translated[i] = submission.Faces[i];
                    else
                        translated[i] = submission.Faces[i].WithWorldTranslation(worldPos);
                }

                foreach (var fp in submission.FlexiPrims)
                    fp.ExternalTransform = worldTransMat;

                submission = new PrimRenderSubmission
                {
                    Label      = submission.Label,
                    Faces      = translated,
                    BoundsMin  = submission.BoundsMin  + worldPos,
                    BoundsMax  = submission.BoundsMax  + worldPos,
                    FlexiPrims = submission.FlexiPrims,
                    // AnimeshSkinData defaults to [] on PrimRenderSubmission -- must be carried
                    // forward explicitly here, or an animesh object's rigged faces lose their
                    // subAnimated dynamic:true flag and hit VkMesh.UpdateVertices's re-stage path
                    // every tick once PrimMeshBuilder's CPU-LBS animates them.
                    AnimeshSkinData = submission.AnimeshSkinData,
                };
            }

            _viewport.SubmitSceneObject(sceneKey, submission);
            _rendered[sceneKey] = 0;
            // Seeds the epsilon-gate baseline here too (not just for later rebuilds) so the very
            // first terse update after initial OnObjectUpdate doesn't immediately re-trigger a
            // redundant rebuild for a stationary object -- see OnTerseObjectUpdate's comment.
            // Caveat: SubmitSceneObject is fire-and-forget (the viewport drains and uploads to
            // the GPU later, on the render thread) -- this records that the CPU-side build
            // completed, not that the GPU upload actually succeeded. An object that OOMs during
            // upload still gets its pose recorded here, so it won't be retried again until it
            // genuinely moves; there is no upload-success feedback wired back from the viewport.
            var scaleV = new Vector3(rootPrim.Scale.X, rootPrim.Scale.Y, rootPrim.Scale.Z);
            var rotQ   = new Quaternion(rootPrim.Rotation.X, rootPrim.Rotation.Y, rootPrim.Rotation.Z, rootPrim.Rotation.W);
            _lastRebuiltPose[sceneKey] = (worldPos, rotQ, scaleV);
            ObjectBuilt?.Invoke(rootLocalId, submission);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Swallow per-object failures — the scene viewer continues working.
        }
        finally
        {
            // Do NOT unconditionally TryRemove here: EnqueueBuild may have already replaced
            // _inflight[sceneKey] with a NEWER build's CTS while this build was running. Removing
            // and disposing that CTS would corrupt the newer build -- its texture downloads would
            // observe ODsE from a disposed token. Lifecycle: each CTS is cancelled+disposed by the
            // NEXT EnqueueBuild for the same key (or by Dispose() on tear-down), not by the build
            // that owns it. StreamTexturesAsync is still live in the background and uses this
            // token, so disposing it here would also corrupt in-flight texture delivery.
            //
            // What DOES need to happen unconditionally (success, cancellation, or swallowed
            // exception alike) is marking this attempt Completed -- this is the last exit point
            // for a build that made it into this method, and it's what lets a later EnqueueBuild
            // for this key start a fresh attempt instead of deferring forever. See
            // MarkAttemptCompleted's own comment for why this is reference-equality-guarded.
            MarkAttemptCompleted(sceneKey, attempt);
        }
    }

    private static DetailLevel LodForDistance(float distance) => distance switch
    {
        < 20f  => DetailLevel.Highest,
        < 40f  => DetailLevel.High,
        < 80f  => DetailLevel.Medium,
        _      => DetailLevel.Low,
    };

    // Maps avatar–object distance to a J2K resolution level for texture LOD.
    // Mirrors the mesh LOD thresholds so both switch quality bands at the same distances.
    // -1 = full, 0 = preview (~1/32 linear), 1/2 = intermediate.
    private static int TextureLodLevelForDistance(float distance) => distance switch
    {
        < 20f => -1, // full quality
        < 40f =>  2, // ~1/8 linear
        < 80f =>  1, // ~1/16 linear
        _     =>  0, // preview
    };

    // Returns true when the desired level is higher quality than the current.
    // -1 (full) is the highest; 0 (preview) is the lowest.
    private static bool IsTextureLodHigherQuality(int desired, int current)
    {
        // Normalise -1 (full) to a large positive so comparison is straightforward.
        int d = desired == -1 ? int.MaxValue : desired;
        int c = current == -1 ? int.MaxValue : current;
        return d > c;
    }

    // Scans rendered objects for texture quality upgrades needed because the avatar
    // walked closer since the last build.  Rate-limited to 10 objects per tick so a
    // teleport into a dense region doesn't flood the build scheduler all at once.
    private void CheckTextureLodUpgrades()
    {
        if (_disposed) return;
        var avatarPos  = _client.Self.SimPosition;
        var currentSim = _client.Network.CurrentSim;
        if (currentSim == null) return;

        int upgraded = 0;
        foreach (var sceneKey in _rendered.Keys)
        {
            if (upgraded >= 10) break;
            if (!_textureLodLevel.TryGetValue(sceneKey, out int curLod)) continue;
            if (curLod == -1) continue; // already at full quality

            float distSq  = DistanceSq(sceneKey, avatarPos);
            float dist    = MathF.Sqrt(distSq);
            int   desired = TextureLodLevelForDistance(dist);

            if (IsTextureLodHigherQuality(desired, curLod))
            {
                EnqueueDirty(sceneKey);
                upgraded++;
            }
        }
    }

    private List<Primitive>? CollectLinkset(Simulator sim, uint rootLocalId)
    {
        var objs = sim.ObjectsPrimitives;
        if (objs == null) return null;
        if (!objs.TryGetValue(rootLocalId, out var root)) return null;

        var result   = new List<Primitive> { root };
        var sceneKey = MakeSceneKey(sim, rootLocalId, _client.Network.CurrentSim);

        if (_childrenByParent.TryGetValue(sceneKey, out var childKeys))
        {
            foreach (var childKey in childKeys.Keys)
            {
                var childLocalId = LocalIdForSceneKey(childKey);
                if (objs.TryGetValue(childLocalId, out var child))
                    result.Add(child);
            }
        }
        return result;
    }

    private static bool IsWithinRadius(Vector3 primPos, OmVector3 avatarPos, float radius)
    {
        if (primPos == Vector3.Zero) return false;
        var dx = primPos.X - avatarPos.X;
        var dy = primPos.Y - avatarPos.Y;
        var dz = primPos.Z - avatarPos.Z;
        return (dx * dx + dy * dy + dz * dz) <= radius * radius;
    }

    private Vector3 GetRootWorldPosition(Simulator sim, uint rootId, Primitive prim)
    {
        if (prim.ParentID == 0)
            return new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z);

        var objs = sim.ObjectsPrimitives;
        if (objs != null && objs.TryGetValue(rootId, out var root) && root.Position != OmVector3.Zero)
            return new Vector3(root.Position.X, root.Position.Y, root.Position.Z);

        return new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
    }

    private float DistanceSq(ulong sceneKey, OmVector3 avatarPos)
    {
        var sim         = SimForSceneKey(sceneKey);
        var rootLocalId = LocalIdForSceneKey(sceneKey);
        var objs        = sim?.ObjectsPrimitives;
        if (objs != null && objs.TryGetValue(rootLocalId, out var p))
        {
            var off = RegionOffset(sim!, _client.Network.CurrentSim);
            var dx  = p.Position.X + off.X - avatarPos.X;
            var dy  = p.Position.Y + off.Y - avatarPos.Y;
            var dz  = p.Position.Z          - avatarPos.Z;
            return dx * dx + dy * dy + dz * dz;
        }
        return float.MaxValue;
    }
}
