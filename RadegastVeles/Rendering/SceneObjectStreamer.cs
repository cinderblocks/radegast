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
using Microsoft.Extensions.Logging;
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

    // Optimistic "already asked for this" tracker, separate from _textureLodLevel (which only
    // updates once a texture patch actually LANDS -- a real asset fetch+decode, not instant).
    // Both OnTerseObjectUpdate's per-update check and CheckTextureLodUpgrades' periodic scan
    // compare the CURRENT distance's desired LOD against _textureLodLevel alone; while an object
    // sits inside the full-quality (dist < 20m) band, every single terse update (and every
    // periodic scan tick) re-observed "-1 wanted, still 2 landed" and re-enqueued a rebuild for
    // it, since nothing recorded that a request for -1 was already in flight -- confirmed live
    // (2026-09-05 field report): one object logged the identical "reason=texture-lod" rebuild
    // trigger 30+ times in under 30 seconds, spaced roughly as fast as terse updates arrived. Set
    // the moment a request is made (optimistic, before the fetch completes); read alongside
    // _textureLodLevel via the higher of the two, so once the real fetch lands and updates
    // _textureLodLevel, this stops mattering on its own (no explicit sync needed) -- only cleared
    // on object removal, mirroring _textureLodLevel's own cleanup sites.
    private readonly ConcurrentDictionary<ulong, int> _requestedTexLod = new();

    // Reverse parent index: rootSceneKey → set of child scene keys.
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, byte>> _childrenByParent = new();

    // The pose this streamer last *completed* a full rebuild at, per multi-prim linkset -- see
    // OnTerseObjectUpdate's own epsilon-gate comment for why this exists. Seeded by
    // BuildObjectAsync on completion, not by the terse-update handler that decides to rebuild:
    // recording the pose at decision time would mark a pose "rebuilt" even when the rebuild it
    // triggered later fails (e.g. the viewport drops the GPU upload under OOM pressure), which
    // would wrongly suppress a later, genuinely-needed retry at that same pose.
    private readonly ConcurrentDictionary<ulong, (Vector3 Position, Quaternion Rotation, Vector3 Scale)> _lastRebuiltPose = new();

    // Whether the last completed build for this multi-prim linkset had any flexi prims -- see
    // OnTerseObjectUpdate's own epsilon-gate comment for why this gates the rebuild-on-pose-change
    // decision. Seeded alongside _lastRebuiltPose, same "record on completion, not on decision"
    // reasoning. A key absent from this map (no build has completed yet, or it was cleared on
    // removal/upload-failure) is treated as "might have flexi" by the read site -- rebuilding is
    // the safe default when this streamer doesn't yet know.
    private readonly ConcurrentDictionary<ulong, bool> _lastBuildHadFlexi = new();

    // EnqueueBuild defers (rather than cancels) a dirty re-trigger when a build for the key is
    // still running, re-marking the key dirty so ProcessDirty retries it once the running
    // attempt finishes. _staleAttemptTimeouts counts the release-valve case: SceneBuildScheduler.
    // Enqueue's queue-depth eviction can drop a factory without ever running it (see
    // StuckAttemptTimeoutMs's own comment), which would otherwise defer that key forever.
    private long _deferredBuildCount;
    private long _staleAttemptTimeouts;

    // ── Sim index registry ────────────────────────────────────────────────────────
    // Upper 32 bits of a scene key encode a sim index (0 = current sim, 1-N for neighbors).
    // This lets us distinguish objects with the same LocalID in different regions. Shared with
    // SceneAvatarStreamer and SceneViewerViewModel's terrain tracking so a given neighbor's
    // objects, avatars, and terrain all derive from the same index value.
    private readonly SceneNeighborSimIndex _neighborIndex;

    /// <summary>Number of object build tasks currently running.</summary>
    public int InflightCount => _inflight.Count;

    private const int DebounceMs = 50;

    private readonly Timer  _debounceTimer;
    // Fires every 10 s to upgrade textures on objects that the avatar walked closer to.
    private readonly Timer  _textureLodTimer;
    // Fires every 10 s to catch root prims that are sitting in a tracked simulator's
    // ObjectsPrimitives (current OR neighbor) but that this streamer never learned about --
    // see ReconcileUntrackedPrims's own doc comment for why that gap exists and what this
    // recovers. Self-healing regardless of the exact cause, and a diagnostic in its own right:
    // if this recovers a real in-range object, that proves the object reached LibreMetaverse's
    // side but never fired the event this streamer normally reacts to.
    private readonly Timer  _reconcileTimer;
    private bool            _disposed;

    // sceneKey -> tick of the last reconciliation-triggered EnqueueDirty for it. Bounds
    // ReconcileUntrackedPrims from re-attempting the same still-failing object every single
    // sweep forever (e.g. one whose CPU build keeps throwing, or whose GPU upload keeps getting
    // dropped by pool exhaustion -- OnSceneObjectUploadFailed already clears _rendered on drop,
    // which would otherwise make it immediately sweep-eligible again 10 s later).
    private readonly ConcurrentDictionary<ulong, long> _reconcileAttempts = new();
    private const long ReconcileRetryCooldownMs = 60_000;

    // sceneKeys ReconcileUntrackedPrims has already logged a recovery for -- logged once per key
    // (not once per sweep) so a persistently-failing recovered object doesn't spam every 10 s.
    private readonly ConcurrentDictionary<ulong, byte> _reconcileReported = new();

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
        SceneBuildScheduler scheduler, SceneNeighborSimIndex neighborIndex)
    {
        _client        = client;
        _viewport      = viewport;
        _builder       = new PrimMeshBuilder(client);
        _scheduler     = scheduler;
        _neighborIndex = neighborIndex;

        _debounceTimer   = new Timer(_ => ProcessDirty(), null,
            Timeout.Infinite, Timeout.Infinite);
        _textureLodTimer = new Timer(_ => CheckTextureLodUpgrades(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        _reconcileTimer  = new Timer(_ => ReconcileUntrackedPrims(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        _viewport.SceneObjectUploadFailed += OnSceneObjectUploadFailed;
    }

    /// <summary>
    /// A GPU upload was dropped after this streamer already recorded the build as complete (see
    /// <see cref="ISceneViewport.SceneObjectUploadFailed"/>'s own comment for why that recording
    /// happens before upload outcome is knowable at all). Undo that bookkeeping so the key looks
    /// exactly like it never finished building, then re-request a build -- the viewport's own
    /// short cooldown (<c>VkViewportControl.SceneUploadFailureCooldownMs</c>) already prevents
    /// this from tight-looping against a still-exhausted pool.
    /// <para>
    /// <see cref="ISceneViewport.SubmitSceneObject"/> is shared by every streamer that puts
    /// content into the scene layer -- <c>SceneAvatarStreamer</c> and <c>SceneViewerViewModel</c>'s
    /// terrain submission both go through the exact same event, not just this one -- so this MUST
    /// gate on actually owning <paramref name="sceneKey"/> before touching anything. Without the
    /// gate, an avatar or terrain upload failure would flow into <see cref="EnqueueDirty"/> ->
    /// <see cref="EnqueueBuild"/> -> <see cref="BuildObjectAsync"/>, whose <see cref="CollectLinkset"/>
    /// finds no prim for that (foreign-namespaced) local ID and calls
    /// <c>_viewport.RemoveSceneObject</c> -- actively deleting live avatar/terrain geometry this
    /// streamer never owned. <see cref="_rendered"/> is populated ONLY by this streamer's own
    /// <see cref="BuildObjectAsync"/>, immediately after a successful CPU-side build (before GPU
    /// upload outcome is even knowable -- see this method's own first paragraph), so it is a
    /// reliable "did I submit this key" check regardless of whether that upload later failed.
    /// </para>
    /// </summary>
    private void OnSceneObjectUploadFailed(ulong sceneKey)
    {
        if (_disposed) return;
        if (!_rendered.ContainsKey(sceneKey)) return;
        _rendered.TryRemove(sceneKey, out _);
        _lastRebuiltPose.TryRemove(sceneKey, out _);
        _lastBuildHadFlexi.TryRemove(sceneKey, out _);
        _textureLodLevel.TryRemove(sceneKey, out _);
        _requestedTexLod.TryRemove(sceneKey, out _);
        EnqueueDirty(sceneKey);
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the thread-pool after a linkset is built and submitted to the viewport, with
    /// the rootLocalId and submission. Subscribe before calling <see cref="OnObjectUpdate"/>.
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
        var rootPos  = GetRootWorldPosition(sim, rootLocalId, prim, out bool trustworthyPos);
        var worldPos = ApplyRegionOffset(rootPos, sim, currentSim);
        var avatarPos = _client.Self.SimPosition;

        if (trustworthyPos && !IsWithinRadius(worldPos, avatarPos, _maxStreamRadius))
        {
            // Must fully clear bookkeeping (not just the viewport draw), or this key is left
            // with _rendered still set while the viewport no longer has it. If the object later
            // comes back into range via OnTerseObjectUpdate, that path's _rendered.ContainsKey
            // branch assumes the object is still actually submitted and, for a stationary
            // linkset whose pose hasn't changed since _lastRebuiltPose, never re-enqueues a
            // build -- permanently hiding it. Single-prim objects are hit hardest: that branch
            // doesn't even have a pose-changed rebuild path, only SetSceneObjectMotion against a
            // viewport entry that no longer exists. CancelAndRemove clears _rendered/
            // _lastRebuiltPose/_textureLodLevel/_dirty/_inflight together so the object is
            // treated as genuinely new next time it's dirtied.
            //
            // Gated on trustworthyPos: an untrustworthy position means the root hasn't arrived
            // yet for this child, and its own parent-relative offset must never be read as a
            // real distance -- doing so would let a same-tick root-after-child arrival race
            // cancel/starve a linkset's build purely on placeholder data. Falling through to
            // EnqueueDirty below is safe either way: the real radius gate re-runs once the root
            // is known, in ProcessDirty/EnqueueBuild's own position lookups.
            CancelAndRemove(sceneKey);
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

        var rootPos  = GetRootWorldPosition(sim, rootLocalId, prim, out bool trustworthyPos);
        var worldPos = ApplyRegionOffset(rootPos, sim, currentSim);
        var avatarPos = _client.Self.SimPosition;

        // Gated on trustworthyPos for the same reason as OnObjectUpdate's identical check: an
        // untrustworthy position is a child's parent-relative offset, not a world position, and
        // must never be read as "confirmed out of range" -- see GetRootWorldPosition's own
        // comment.
        if (trustworthyPos && !IsWithinRadius(worldPos, avatarPos, _maxStreamRadius))
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
                //
                // Position/rotation alone no longer trigger a rebuild here either, for a linkset
                // confirmed flexi-free: SetSceneObjectMotion above already applies a full
                // Scale*Rotation*Translation rigid transform to every (non-flexi) face via
                // SetSceneObjectTransform/ApplyTransformToFaces, with no mesh/texture/material
                // work at all, so a rebuild for pure rigid motion was pure overhead -- and, worse,
                // visible overhead: each one is a real destroy-then-recreate of every GPU resource
                // the object owns (see RemoveSceneObjectGpuNoRebuild/UploadSceneObjectNoRebuild),
                // so a continuously-moving multi-prim object (a vehicle) rebuilt on every ~1cm of
                // travel, producing a visible flicker on every single terse update while it moved
                // (2026-09-05 field report). Flexi content is the one real exception:
                // ApplyTransformToFaces deliberately skips flexi faces (they simulate their own
                // world-space vertices instead), and BuildObjectAsync is the only place that
                // refreshes FlexiPrimInfo.ExternalTransform from the current root pose -- so a
                // flexi-bearing linkset still needs the real rebuild to track its own motion
                // correctly, exactly as before. Scale is left unconditionally rebuild-triggering
                // regardless of flexi -- unlike position/rotation, it's not yet confirmed whether
                // the transform-only path's uniform CreateScale is equivalent to a real resize for
                // every case (e.g. UV tiling baked into cut-face vertex data), so that half of the
                // original behavior is intentionally untouched here.
                bool isSinglePrim = !_childrenByParent.TryGetValue(sceneKey, out var ch) || ch.IsEmpty;
                if (!isSinglePrim)
                {
                    const float posEpsilon = 0.01f;   // 1cm
                    const float scaleEpsilon = 0.01f;
                    const float rotDotEpsilon = 1e-4f;
                    bool positionOrRotationChanged = true;
                    bool scaleChanged = true;
                    if (_lastRebuiltPose.TryGetValue(sceneKey, out var last))
                    {
                        positionOrRotationChanged =
                            Vector3.DistanceSquared(last.Position, position) > posEpsilon * posEpsilon ||
                            MathF.Abs(1f - MathF.Abs(Quaternion.Dot(last.Rotation, rotation))) > rotDotEpsilon;
                        scaleChanged = Vector3.DistanceSquared(last.Scale, scale) > scaleEpsilon * scaleEpsilon;
                    }
                    // Unknown (no completed build yet) defaults to "assume flexi" -- rebuilding is
                    // the safe fallback when this streamer doesn't yet know, matching poseChanged's
                    // own "no baseline yet" default of true above.
                    bool hasFlexi = !_lastBuildHadFlexi.TryGetValue(sceneKey, out var hadFlexi) || hadFlexi;
                    bool poseChanged = scaleChanged || (positionOrRotationChanged && hasFlexi);
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
                    // Effective baseline is the BETTER of what's actually landed (_textureLodLevel)
                    // and what's already been asked for but hasn't landed yet (_requestedTexLod) --
                    // see that field's own comment for why: without this, an object sitting inside
                    // the full-quality band re-requested the same upgrade on every single terse
                    // update while the real fetch was still in flight (confirmed live: 30+ identical
                    // triggers in under 30 seconds for one object, 2026-09-05).
                    int effectiveCurrent = curTexLod;
                    if (_requestedTexLod.TryGetValue(sceneKey, out int requested)
                        && IsTextureLodHigherQuality(requested, effectiveCurrent))
                        effectiveCurrent = requested;
                    if (IsTextureLodHigherQuality(desiredTexLod, effectiveCurrent))
                    {
                        _requestedTexLod[sceneKey] = desiredTexLod;
                        EnqueueDirty(sceneKey);
                    }
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
    /// Removes every currently-rendered object that belongs to <paramref name="sim"/>, without
    /// touching any other tracked sim. Used on <see cref="Network.SimDisconnected"/> so a
    /// disconnected neighbor's objects don't linger indefinitely -- previously the only way
    /// stale neighbor content was ever removed was a full <see cref="Clear"/> on the next sim
    /// change, which this streamer's lightweight region-crossing path is designed to skip.
    /// </summary>
    public void RemoveObjectsForSim(Simulator sim)
    {
        if (_disposed) return;
        foreach (var sceneKey in _rendered.Keys)
        {
            if (SimForSceneKey(sceneKey) == sim)
                CancelAndRemove(sceneKey);
        }
    }

    /// <summary>
    /// Shifts every currently-rendered object's already-baked world-space transform by
    /// <paramref name="delta"/>, without a rebuild. Used by the lightweight region-crossing path
    /// (<c>SceneViewerViewModel.OnSimChanged</c>) when the agent crosses into an already-tracked
    /// neighbor: that neighbor's scene key never changes (see <see cref="SceneNeighborSimIndex"/>'s
    /// header comment), but every object's <c>Transform</c> was baked using <c>RegionOffset</c>
    /// against the OLD current sim, so it needs correcting to the new one. <paramref name="delta"/>
    /// is the same constant vector for every object regardless of which sim it came from (old-
    /// current or any neighbor) -- see <c>RegionOffset</c>'s own reasoning for why.
    /// <para>
    /// Objects still mid-build when this runs don't need correction here: they resolve
    /// <c>RegionOffset</c> fresh against live <c>CurrentSim</c> right before they submit, so a
    /// build that lands after the crossing already bakes the correct position on its own.
    /// </para>
    /// </summary>
    public void RebaseAllForRegionPromotion(Vector3 delta)
    {
        if (_disposed || delta == Vector3.Zero) return;
        // Snapshot now -- _rendered can keep mutating on other threads while the viewport
        // processes this on the render thread later.
        var keys = new List<ulong>(_rendered.Keys);
        _viewport.RebaseSceneObjectTransforms(keys, delta);
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
        _requestedTexLod.Clear();
        _childrenByParent.Clear();
        _neighborIndex.Clear();
        _reconcileAttempts.Clear();
        _reconcileReported.Clear();
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
        _viewport.SceneObjectUploadFailed -= OnSceneObjectUploadFailed;
        _debounceTimer.Dispose();
        _textureLodTimer.Dispose();
        _reconcileTimer.Dispose();
        _fetchScheduler.Dispose();
        foreach (var attempt in _inflight.Values) { attempt.Cts.Cancel(); attempt.Cts.Dispose(); }
        _inflight.Clear();
    }

    // ── Sim index helpers ─────────────────────────────────────────────────────────
    // Thin delegations to the shared SceneNeighborSimIndex -- kept as members here so every
    // existing call site in this file is unchanged. currentSim parameters are accepted but
    // unused: they were needed when index 0 meant "whatever is current" (see
    // SceneNeighborSimIndex's header comment for why that broke region crossings); indices are
    // now permanent per-simulator and don't depend on which one is current.

    private uint GetSimIndex(Simulator sim, Simulator? currentSim = null)
        => _neighborIndex.GetSimIndex(sim);

    private ulong MakeSceneKey(Simulator sim, uint localId, Simulator? currentSim = null)
        => _neighborIndex.MakeSceneKey(sim, localId);

    private Simulator? SimForSceneKey(ulong key)
        => _neighborIndex.SimForSceneKey(key);

    private static uint LocalIdForSceneKey(ulong key)
        => SceneNeighborSimIndex.LocalIdForSceneKey(key);

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
        _requestedTexLod.TryRemove(sceneKey, out _);
        _lastRebuiltPose.TryRemove(sceneKey, out _);
        _lastBuildHadFlexi.TryRemove(sceneKey, out _);
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

        // A build for this key that is still genuinely running (not yet reached
        // PrefetchThenScheduleBuildAsync/BuildObjectAsync's completion) is left alone instead of
        // being cancelled-and-restarted. Killing it here unconditionally would mean an object
        // whose terse-update rate exceeds its own build completion rate (e.g. a continuously
        // moving/rotating linkset) has every attempt killed by the next one before it could ever
        // finish. Instead, re-mark the key dirty so ProcessDirty retries it on a LATER tick once
        // the running attempt actually finishes -- bounded by real build throughput, not
        // re-dirty rate.
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
                // Logs the case where a root local ID isn't in sim.ObjectsPrimitives when this
                // build actually runs -- either the object left the scene between being dirtied
                // and now, or its root never arrived at all. Both are worth telling apart from a
                // build that never ran in the first place.
                Logger.Log(
                    $"SceneObjectStreamer: BuildObjectAsync found no linkset for sceneKey {sceneKey:x} " +
                    $"(rootLocalId={rootLocalId}), removing", LogLevel.Debug);
                _viewport.RemoveSceneObject(sceneKey);
                return;
            }

            var rootPrimForLod = prims.Find(p => p.LocalID == rootLocalId) ?? prims[0];
            // World-space position (sim-local + region offset for neighbor sims), computed
            // up-front so dist below is in the SAME frame as the avatar's own position -- the
            // prim's own Position is local to ITS OWN sim, while Self.SimPosition is local to the
            // CURRENT sim, so for any object in a NEIGHBOR sim these must be reconciled with
            // RegionOffset before comparing, or the resulting distance (and therefore LOD/texture
            // LOD selection below) is off by roughly one region size.
            var regionOff = RegionOffset(sim, _client.Network.CurrentSim);
            var worldPos  = new Vector3(
                rootPrimForLod.Position.X + regionOff.X,
                rootPrimForLod.Position.Y + regionOff.Y,
                rootPrimForLod.Position.Z);
            var avatarWorldPos = new Vector3(
                _client.Self.SimPosition.X, _client.Self.SimPosition.Y, _client.Self.SimPosition.Z);
            float dist    = Vector3.Distance(worldPos, avatarWorldPos);
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

            // worldPos was already computed above (region-offset-corrected), reused here for the
            // actual face translation -- no need to recompute it.
            var rootPrim = rootPrimForLod;

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

            // Re-check immediately before the write -- everything above (world-space translation,
            // per-face transform) is synchronous CPU work with no await, so this is the last point
            // cancellation can be observed before the build lands as "committed". Narrows the
            // TOCTOU window between the check above and the actual submission from "one full
            // recompute" down to nothing (mirrors SceneAvatarStreamer's identical fix).
            if (token.IsCancellationRequested) return;

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
            _lastBuildHadFlexi[sceneKey] = submission.FlexiPrims.Length > 0;
            if (rootPrim.PrimData.PCode is PCode.Tree or PCode.NewTree or PCode.Grass)
                _viewport.RegisterWindSwayObject(sceneKey, worldPos, rotQ);
            ObjectBuilt?.Invoke(rootLocalId, submission);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Swallow per-object failures — the scene viewer continues working. Logged (not
            // silent) so a stuck-invisible large object is diagnosable instead of indistinguishable
            // from one that's simply out of range or hasn't been dirtied yet. Full exception (not
            // just ex.Message) so a recurring failure's actual throw site/stack is diagnosable --
            // ex.Message alone was not enough to pin down a real, repeating "does not accept
            // floating point Not-a-Number values" failure (2026-09-05 field report).
            Logger.Log($"SceneObjectStreamer: build failed for sceneKey {sceneKey:x}", LogLevel.Warning, ex);
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

            // See _requestedTexLod's own field comment: without this, this periodic scan
            // re-requests the same in-flight upgrade every tick until the real fetch lands.
            int effectiveCurLod = curLod;
            if (_requestedTexLod.TryGetValue(sceneKey, out int requestedLod)
                && IsTextureLodHigherQuality(requestedLod, effectiveCurLod))
                effectiveCurLod = requestedLod;
            if (effectiveCurLod == -1) continue; // already requested full quality, just not landed yet

            float distSq  = DistanceSq(sceneKey, avatarPos);
            float dist    = MathF.Sqrt(distSq);
            int   desired = TextureLodLevelForDistance(dist);

            if (IsTextureLodHigherQuality(desired, effectiveCurLod))
            {
                _requestedTexLod[sceneKey] = desired;
                EnqueueDirty(sceneKey);
                upgraded++;
            }
        }
    }

    /// <summary>
    /// Scans every tracked simulator (current AND neighbors -- neighbor prims never fire
    /// <c>ObjectUpdate</c> events on their own, only <c>SceneViewerViewModel.SeedNeighborSims</c>'s
    /// one-time pass at panel-open feeds them in) for root prims that are sitting in
    /// <c>Simulator.ObjectsPrimitives</c> and within stream radius, but that this streamer has no
    /// record of at all (not rendered, not dirty, not mid-build).
    /// <para>
    /// Exists because every path that gets an object INTO this streamer's pipeline is event-
    /// driven (<see cref="OnObjectUpdate"/>/<see cref="OnTerseObjectUpdate"/>, plus the one-time
    /// seed passes at panel open) -- there is no periodic fallback that cross-checks against what
    /// LibreMetaverse's own object cache actually holds. A root prim that reaches
    /// <c>ObjectsPrimitives</c> through a path that never raises the public <c>ObjectUpdate</c>
    /// event (the concrete suspect: <c>ObjectManager.ObjectUpdateCachedHandler</c> skips
    /// re-requesting an object whose local disk cache CRC already matches -- plausible for a
    /// large, unchanging building on a return visit to an already-cached parcel) is invisible to
    /// this streamer forever, with no error anywhere, regardless of how long the session runs or
    /// how close the avatar stands to it. This sweep is a lower-frequency, self-healing catch-all
    /// for that entire class of gap: it doesn't matter WHY the streamer never learned about an
    /// object, only that <c>ObjectsPrimitives</c> now disagrees with what this streamer thinks
    /// exists.
    /// </para>
    /// <para>
    /// Deliberately does not distinguish "genuinely new to me" from "I dropped this and haven't
    /// retried yet" -- both look identical (absent from <see cref="_rendered"/>/<see cref="_dirty"/>/
    /// live <see cref="_inflight"/>) and both are legitimately recoverable the same way.
    /// <see cref="_reconcileAttempts"/> bounds the retry rate per key regardless of which case it is.
    /// </para>
    /// </summary>
    private void ReconcileUntrackedPrims()
    {
        if (_disposed) return;
        var currentSim = _client.Network.CurrentSim;
        if (currentSim == null) return;
        var avatarPos = _client.Self.SimPosition;

        Simulator[] sims;
        lock (_client.Network.Simulators)
            sims = _client.Network.Simulators.ToArray();

        var now = Environment.TickCount64;

        foreach (var sim in sims)
        {
            // MakeSceneKey (via SceneNeighborSimIndex.GetSimIndex) assigns a NEW PERMANENT sim
            // index on first use for whichever sim it's called with -- a read-only diagnostic
            // scan must not be what first registers a sim this streamer has never actually
            // streamed from. Restrict to sims already tracked; an untracked sim has nothing
            // recoverable yet regardless (the normal streaming path registers it within one
            // real ObjectUpdate, well inside this sweep's own 10 s cadence).
            if (!_neighborIndex.IsTracked(sim.Handle)) continue;

            // A nonzero/climbing count here means this sim's own inbound UDP receive channel is
            // discarding datagrams because the decode loop can't keep up (see
            // UDPBase.ReceiveLoopAsync's bounded-channel drop-write) -- the one place packet loss
            // for this connection would show up, since nothing else surfaces the counter.
            var dropped = sim.Stats.GetDroppedPackets();
            if (dropped > 0)
                Logger.Log(
                    $"SceneObjectStreamer: sim {sim.Name} has dropped {dropped} inbound UDP packets this session",
                    LogLevel.Warning);

            var objs = sim.ObjectsPrimitives;
            if (objs == null) continue;

            foreach (var root in objs.Values)
            {
                if (root.ParentID != 0) continue; // roots only -- CollectLinkset pulls children in

                // A root whose position has never been filled in (still the zero placeholder) has
                // reached ObjectsPrimitives without a full update -- possible if
                // ObjectUpdateCachedHandler's CRC check decided a cached copy didn't need
                // re-requesting, or if the root arrived via a reference from one of its own
                // children before its own update packet did. There's no real position to enqueue a
                // build against yet, so instead of waiting for an event that may never come,
                // actively re-request the object -- the same request AlwaysRequestObjects would
                // otherwise have issued. A genuine ObjectUpdate response fills in Position for real
                // and fires the public event this streamer's own OnObjectUpdate reacts to normally,
                // so no further special-casing is needed once that lands. Reuses
                // _reconcileAttempts as the same 60 s per-key cooldown so a still-stuck prim isn't
                // re-requested every single 10 s sweep.
                if (root.Position == OmVector3.Zero)
                {
                    var zeroKey = MakeSceneKey(sim, root.LocalID, currentSim);
                    if (_reconcileAttempts.TryGetValue(zeroKey, out var lastZeroAttempt) &&
                        now - lastZeroAttempt < ReconcileRetryCooldownMs)
                        continue;
                    _reconcileAttempts[zeroKey] = now;

                    if (_reconcileReported.TryAdd(zeroKey, 0))
                        Logger.Log(
                            $"SceneObjectStreamer: reconcile sweep found root prim {root.LocalID} " +
                            $"in sim {sim.Name} with Position==Zero (never fully updated); re-requesting",
                            LogLevel.Warning);
                    _client.Objects.RequestObject(sim, root.LocalID);
                    continue;
                }

                var sceneKey = MakeSceneKey(sim, root.LocalID, currentSim);

                // Already known to this streamer in some form -- nothing to recover.
                if (_rendered.ContainsKey(sceneKey)) continue;
                if (_dirty.ContainsKey(sceneKey)) continue;
                if (_inflight.TryGetValue(sceneKey, out var attempt) && !attempt.Completed) continue;

                if (_reconcileAttempts.TryGetValue(sceneKey, out var lastAttempt) &&
                    now - lastAttempt < ReconcileRetryCooldownMs)
                    continue;

                var worldPos = ApplyRegionOffset(
                    new Vector3(root.Position.X, root.Position.Y, root.Position.Z), sim, currentSim);
                if (!IsWithinRadius(worldPos, avatarPos, _maxStreamRadius)) continue;

                _reconcileAttempts[sceneKey] = now;
                if (_reconcileReported.TryAdd(sceneKey, 0))
                {
                    var dx = worldPos.X - avatarPos.X;
                    var dy = worldPos.Y - avatarPos.Y;
                    var dz = worldPos.Z - avatarPos.Z;
                    Logger.Log(
                        $"SceneObjectStreamer: reconcile sweep recovered untracked root prim " +
                        $"{root.LocalID} (scale={root.Scale}) at dist=" +
                        $"{MathF.Sqrt(dx * dx + dy * dy + dz * dz):F1}m, worldPos={worldPos} -- " +
                        $"was never dirtied/built/rendered by any event path",
                        LogLevel.Warning);
                }
                EnqueueDirty(sceneKey);
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
        => GetRootWorldPosition(sim, rootId, prim, out _);

    /// <summary>
    /// Resolves the root's world position, and reports via <paramref name="trustworthy"/>
    /// whether that position is actually the root's (as opposed to the fallback below).
    /// <para>
    /// The fallback returns <paramref name="prim"/>'s own <c>Position</c> when the root hasn't
    /// arrived in <c>sim.ObjectsPrimitives</c> yet (or reports <see cref="OmVector3.Zero"/>,
    /// which the sim uses as a not-yet-known placeholder). For a CHILD prim that value is a
    /// parent-relative offset, not a world position -- often just a few metres from the
    /// origin. Callers that gate culling on this must not treat that as "confirmed far away":
    /// doing so is what let an ordinary root-arrives-after-children race incorrectly cancel a
    /// whole linkset's in-flight build (large multi-prim linksets stream the most children and
    /// are the most likely to hit this window).
    /// </para>
    /// </summary>
    private Vector3 GetRootWorldPosition(Simulator sim, uint rootId, Primitive prim, out bool trustworthy)
    {
        if (prim.ParentID == 0)
        {
            trustworthy = true;
            return new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
        }

        var objs = sim.ObjectsPrimitives;
        if (objs != null && objs.TryGetValue(rootId, out var root) && root.Position != OmVector3.Zero)
        {
            trustworthy = true;
            return new Vector3(root.Position.X, root.Position.Y, root.Position.Z);
        }

        trustworthy = false;
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
