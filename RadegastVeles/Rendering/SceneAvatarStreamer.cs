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
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using LibreMetaverse.Rendering;
using OmVector3  = LibreMetaverse.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Vector3    = System.Numerics.Vector3;
using Vector4    = System.Numerics.Vector4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Streams nearby avatar meshes into the scene-object layer of a
/// <see cref="VkViewportControl"/>.
/// <para>
/// Uses a negative key space to avoid collisions with prim LocalIDs managed
/// by <see cref="SceneObjectStreamer"/>: avatar scene keys are stored as
/// <c>uint.MaxValue - localId</c>.
/// </para>
/// </summary>
internal sealed class SceneAvatarStreamer : IDisposable
{
    private readonly GridClient          _client;
    private readonly ISceneViewport      _viewport;
    private readonly AvatarMeshBuilder   _builder;
    private readonly SceneBuildScheduler _scheduler;

    // avatar LocalID → CancellationTokenSource for in-flight build
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _inflight = new();

    // dirty set: avatar LocalID → enqueue timestamp
    private readonly ConcurrentDictionary<uint, long> _dirty = new();

    // set of avatar LocalIDs that currently have a live scene-object submission
    private readonly ConcurrentDictionary<uint, byte> _rendered = new();

    // Cloud drivers: avatar LocalID → active cloud driver (shown while avatar mesh loads)
    private readonly ConcurrentDictionary<uint, AvatarCloudDriver> _cloudDrivers = new();

    // avatar LocalID → hash of visual params at last successful build.
    // Used to skip rebuilds when only position/rotation changed.
    private readonly ConcurrentDictionary<uint, int> _lastVisualParamHash = new();

    // avatar LocalID → cached ground-anchor correction from the most recent successful
    // mesh build (see AvatarBoneMath.ComputeGroundAdjustment). SL's network position
    // is not feet height; ResolveAvatarWorldTransform subtracts this to place the avatar
    // on the ground instead of floating. 0 until the first build completes.
    private readonly ConcurrentDictionary<uint, float> _groundAdjustment = new();

    // ── Avatar rendering complexity (Veles-local "jellydoll") ───────────────────────

    // Superset of _rendered's keys that also includes Cloud-tier avatars — used so a
    // threshold change (RecomputeAllTiers) can re-evaluate avatars currently hidden as
    // a cloud, which _rendered alone would miss.
    private readonly ConcurrentDictionary<uint, byte> _trackedLocalIds = new();
    // Tier decided by EnqueueBuild, read once by BuildAvatarAsync and removed. Avoids
    // recomputing (and risking a different answer from) DetermineRenderTier on the
    // scheduler thread using a possibly-stale sim snapshot.
    private readonly ConcurrentDictionary<uint, AvatarRenderTier> _pendingTier = new();
    // Attachment prim LocalID → owning avatar LocalID, so a kill/update on an
    // attachment can find its avatar and re-trigger a cost re-evaluation.
    private readonly ConcurrentDictionary<uint, uint> _attachmentOwner = new();
    // avatar LocalID → last-estimated complexity cost, invalidated only by
    // OnAttachmentObjectUpdate/OnAttachmentKilled. DetermineRenderTier runs on every
    // EnqueueBuild call (i.e. every dirty-settle, including position-only ones, not
    // just real appearance changes) — without this cache each of those would rescan
    // this avatar's attachments via AvatarComplexityEstimator for no reason, since a
    // position update can't change what an avatar is wearing.
    private readonly ConcurrentDictionary<uint, float> _cachedCost = new();
    // avatar LocalID → last-determined render tier, written by DetermineRenderTier
    // alongside _cachedCost. Read by SnapshotReportableAvatars for network reporting.
    private readonly ConcurrentDictionary<uint, AvatarRenderTier> _cachedTier = new();

    private readonly AvatarRenderOverrideStore _overrides;

    /// <summary>
    /// Avatars whose estimated Veles-local render cost (see
    /// <see cref="AvatarComplexityEstimator"/>) exceeds this are shown as a silhouette;
    /// well over it (<see cref="AvatarComplexityEstimator.CloudTierMultiplier"/>×), as a
    /// particle cloud. Self, friends, and per-avatar "Always Render Fully" overrides are
    /// always exempt.
    /// </summary>
    public float ComplexityThreshold { get; set; } = 120f;

    /// <summary>Threshold value (and slider maximum) that means "unlimited" — always Full.</summary>
    public const float ComplexityThresholdMax = 500f;

    /// <summary>Number of avatar build tasks currently running.</summary>
    public int InflightCount => _inflight.Count;

    private const int   DebounceMs        = 600;
    private const int   SelfDebounceMs    = 0;   // self-avatar gets immediate scheduling
    private float _maxStreamRadius = 64f;

    /// <summary>Gets or sets the streaming radius in metres (default 64).</summary>
    public float DrawDistance
    {
        get => _maxStreamRadius;
        set
        {
            _maxStreamRadius = Math.Max(16f, Math.Min(512f, value));
            CullBeyondDrawDistance();
        }
    }
    // LOD thresholds for avatar meshes (metres).
    // < 16 m → LOD 0 (highest), < 32 m → LOD 1, else → LOD 2.
    private static int AvatarLodForDistance(float dist) => dist switch
    {
        < 16f => 0,
        < 32f => 1,
        _     => 2,
    };

    // Key offset so avatar scene-layer IDs never collide with prim IDs.
    // Avatars use the range [0x8000_0000, 0xFFFF_FFFF] (top uint half, cast to ulong).
    private const uint  AvatarKeyOffset   = 0x8000_0000u;

    private readonly Timer _debounceTimer;
    private bool           _disposed;

    /// <summary>
    /// Raised after a successful avatar build.
    /// Arguments: (sceneKey, localId, buildResult).
    /// The sceneKey is the ulong used in <see cref="VkViewportControl.SubmitSceneObject"/>.
    /// </summary>
    public event Action<ulong, uint, AvatarBuildResult>? AvatarBuilt;

    private SceneAvatarAnimationStreamer? _animationStreamer;

    /// <summary>
    /// Wires the animation streamer so terse avatar position updates can push
    /// updated world matrices into flexi attachment animators.
    /// </summary>
    public void SetAnimationStreamer(SceneAvatarAnimationStreamer animationStreamer)
        => _animationStreamer = animationStreamer;

    public SceneAvatarStreamer(GridClient client, ISceneViewport viewport,
        SceneBuildScheduler scheduler, AvatarRenderOverrideStore overrides)
    {
        _client    = client;
        _viewport  = viewport;
        _builder   = new AvatarMeshBuilder(client);
        _scheduler = scheduler;
        _overrides = overrides;

        _debounceTimer = new Timer(_ => ProcessDirty(), null,
            Timeout.Infinite, Timeout.Infinite);

        _overrides.OverrideChanged            += OnOverrideChanged;
        _client.Friends.FriendshipResponse    += OnFriendshipResponse;
        _client.Friends.FriendshipTerminated  += OnFriendshipTerminated;
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a prim in the scene has moved (terse update).
    /// If any rendered avatars are seated on a prim in the same linkset
    /// (root = <paramref name="rootLocalId"/>), their scene-object world
    /// transforms are updated immediately without a full mesh rebuild.
    /// </summary>
    public void OnSeatPrimMoved(Simulator sim, uint rootLocalId)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;

        foreach (var localId in _rendered.Keys)
        {
            if (!sim.ObjectsAvatars.TryGetValue(localId, out var av)) continue;
            if (av.ParentID == 0) continue;

            // Check whether this avatar is seated on the moving linkset.
            // Walk from the avatar's immediate parent up to find the root.
            uint parentId = av.ParentID;
            uint rootOfSeat = parentId;
            while (sim.ObjectsPrimitives.TryGetValue(parentId, out var p) && p.ParentID != 0)
            {
                parentId  = p.ParentID;
                rootOfSeat = parentId;
            }

            if (rootOfSeat != rootLocalId) continue;

            // Re-resolve and push the updated world transform.
            var (resolvedPos, resolvedRot) = ResolveAvatarWorldTransform(sim, av);
            var wp = new Vector3(resolvedPos.X, resolvedPos.Y, resolvedPos.Z);
            var worldMatrix = AvatarWorldMatrix(wp, resolvedRot);
            _viewport.SetSceneObjectTransform(SceneKey(localId), worldMatrix);
            // Keep flexi attachments (hair, skirts, tails, ...) in sync with the seat's
            // motion — without this they stay frozen at the last terse-update position
            // while the body rides along with the moving prim (vehicle / animated seat).
            _animationStreamer?.OnFlexiWorldUpdate(localId, worldMatrix);
        }

        // Also update self-avatar if seated on this linkset.
        var selfId = _client.Self.LocalID;
        if (_rendered.ContainsKey(selfId) && _client.Self.SittingOn != 0)
        {
            uint parentId  = _client.Self.SittingOn;
            uint rootOfSeat = parentId;
            while (sim.ObjectsPrimitives.TryGetValue(parentId, out var p) && p.ParentID != 0)
            {
                parentId   = p.ParentID;
                rootOfSeat = parentId;
            }
            if (rootOfSeat == rootLocalId)
            {
                var resolvedPos = _client.Self.SimPosition;
                var resolvedRot = _client.Self.SimRotation;
                var wp = new Vector3(resolvedPos.X, resolvedPos.Y, resolvedPos.Z);
                var selfWorldMatrix = AvatarWorldMatrix(wp, resolvedRot);
                _viewport.SetSceneObjectTransform(SceneKey(selfId), selfWorldMatrix);
                _animationStreamer?.OnFlexiWorldUpdate(selfId, selfWorldMatrix);
            }
        }
    }

    /// <summary>
    /// Called when an avatar's terse update arrives (position/rotation change).
    /// If the avatar is already rendered in the scene, fast-paths the world
    /// transform without triggering a full mesh rebuild.
    /// Otherwise falls back to <see cref="OnAvatarUpdate"/>.
    /// </summary>
    public void OnTerseAvatarUpdate(Simulator sim, Avatar avatar)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;

        var (resolvedPos, resolvedRot) = ResolveAvatarWorldTransform(sim, avatar);
        var avatarPos = _client.Self.SimPosition;
        if (!IsWithinRadius(resolvedPos, avatarPos, _maxStreamRadius))
        {
            RemoveAvatar(avatar.LocalID);
            return;
        }

        if (_rendered.ContainsKey(avatar.LocalID))
        {
            // Fast-path: update world transform (translation + yaw) without a mesh rebuild.
            var wp = new Vector3(resolvedPos.X, resolvedPos.Y, resolvedPos.Z);
            var worldMatrix = AvatarWorldMatrix(wp, resolvedRot);
            _viewport.SetSceneObjectTransform(
                SceneKey(avatar.LocalID),
                worldMatrix);
            // Also update the flexi attachment animator so its ExternalTransform
            // stays in sync with the avatar's new world position.
            _animationStreamer?.OnFlexiWorldUpdate(avatar.LocalID, worldMatrix);
        }
        else
        {
            // Avatar not yet rendered — update cloud driver position if active.
            if (_cloudDrivers.TryGetValue(avatar.LocalID, out var cloud))
                cloud.UpdateWorldPos(new Vector3(resolvedPos.X, resolvedPos.Y, resolvedPos.Z));
            // Trigger a full build.
            OnAvatarUpdate(sim, avatar);
        }
    }

    /// <summary>
    /// Call when an avatar update arrives.  Skips avatars outside the stream radius.
    /// </summary>
    public void OnAvatarUpdate(Simulator sim, Avatar avatar)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;

        var (resolvedPos, _) = ResolveAvatarWorldTransform(sim, avatar);
        var avatarPos = _client.Self.SimPosition;
        if (!IsWithinRadius(resolvedPos, avatarPos, _maxStreamRadius))
        {
            RemoveAvatar(avatar.LocalID);
            return;
        }

        // Skip a full mesh rebuild when the avatar is already rendered and its visual
        // params haven't changed — a pure position/rotation update is handled by
        // OnTerseAvatarUpdate with a fast matrix-only path.
        if (_rendered.ContainsKey(avatar.LocalID))
        {
            int hash = ComputeVisualParamHash(avatar.VisualParameters);
            if (_lastVisualParamHash.TryGetValue(avatar.LocalID, out int prev) && prev == hash)
                return;
        }

        EnqueueDirty(avatar.LocalID);
    }

    /// <summary>
    /// Call when an avatar is killed / leaves the sim.
    /// </summary>
    public void OnKillAvatar(Simulator sim, uint localId)
    {
        if (_disposed) return;
        // Accept kills from any sim: after a region crossing CurrentSim is already
        // the new sim, so the old-sim guard would wrongly ignore kill packets from
        // the previous region and leave the avatar visible.  RemoveAvatar is a
        // no-op when the localId isn't tracked.
        //
        // EXCEPTION for self: LocalIDs are scoped per simulator, not globally unique -- a
        // neighbor/child sim (this client stays connected to several for region-crossing
        // lookahead) can send a kill for some unrelated object whose LocalID happens to collide
        // with whatever the CURRENT sim assigned to the local agent's own avatar. Unlike the
        // any-sim case above (which exists for OTHER avatars legitimately killed by their origin
        // sim after a region crossing), self never receives a real kill for itself this way -- so
        // a foreign-sim kill naming self's LocalID is always a coincidental collision, not a real
        // event.
        if (localId == _client.Self.LocalID && sim != _client.Network.CurrentSim) return;
        RemoveAvatar(localId);
    }

    /// <summary>
    /// Enqueues all currently rendered avatars for a rebuild so they pick up
    /// the new LOD level (called when draw distance changes).
    /// </summary>
    public void DirtyAllRendered()
    {
        if (_disposed) return;
        var now = Environment.TickCount64;
        foreach (var localId in _rendered.Keys)
            _dirty.AddOrUpdate(localId, now, (_, _) => now);
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    /// <summary>
    /// Re-enqueues all currently rendered avatar local IDs for a rebuild after a GL
    /// context reset (tab switch). Routes through the dirty queue with an immediate
    /// debounce so the back-pressure guard in ProcessDirty throttles the burst rather
    /// than calling EnqueueBuild for every avatar at once.
    /// </summary>
    public void RebuildAllRendered()
    {
        if (_disposed) return;
        var now = Environment.TickCount64 - DebounceMs; // mark as immediately due
        foreach (var localId in _rendered.Keys)
            _dirty.AddOrUpdate(localId, now, (_, _) => now);
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(0, Timeout.Infinite); // fire ProcessDirty immediately
    }

    /// <summary>
    /// Re-evaluates render tier for every tracked avatar — including ones currently
    /// hidden as a Cloud (which <see cref="DirtyAllRendered"/>'s <c>_rendered</c>-only
    /// iteration would miss) — so a raised <see cref="ComplexityThreshold"/> can pull an
    /// avatar back from Cloud to Silhouette/Full, or a lowered one can push it down,
    /// without waiting for an unrelated appearance change. Called when the threshold
    /// slider changes.
    /// </summary>
    public void RecomputeAllTiers()
    {
        if (_disposed) return;
        var now = Environment.TickCount64;
        foreach (var localId in _trackedLocalIds.Keys)
            _dirty.AddOrUpdate(localId, now, (_, _) => now);
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    /// <summary>
    /// Called when an attachment prim belonging to a tracked avatar is added or its
    /// metadata updated, so a new attachment's cost is picked up by a re-evaluation
    /// without waiting for an unrelated position/appearance update.
    /// </summary>
    public void OnAttachmentObjectUpdate(Simulator sim, Primitive prim, bool isNew)
    {
        if (_disposed || prim.ParentID == 0) return;
        if (!sim.ObjectsAvatars.ContainsKey(prim.ParentID)) return;
        if (!_trackedLocalIds.ContainsKey(prim.ParentID)) return;
        _attachmentOwner[prim.LocalID] = prim.ParentID;
        if (isNew)
        {
            _cachedCost.TryRemove(prim.ParentID, out _);
            EnqueueDirty(prim.ParentID);
        }
    }

    /// <summary>Called when a prim is killed, in case it was a tracked avatar's attachment.</summary>
    public void OnAttachmentKilled(uint killedLocalId)
    {
        if (_disposed) return;
        if (_attachmentOwner.TryRemove(killedLocalId, out var owner) && _trackedLocalIds.ContainsKey(owner))
        {
            _cachedCost.TryRemove(owner, out _);
            EnqueueDirty(owner);
        }
    }

    /// <summary>
    /// Immediately removes any rendered avatars that now lie outside the current
    /// <see cref="DrawDistance"/>. Called automatically when the draw distance is
    /// reduced.
    /// </summary>
    public void CullBeyondDrawDistance()
    {
        if (_disposed) return;
        var sim       = _client.Network.CurrentSim;
        var avatarPos = _client.Self.SimPosition;
        if (sim == null) return;

        foreach (var localId in _rendered.Keys)
        {
            if (!sim.ObjectsAvatars.TryGetValue(localId, out var av)) continue;
            var (resolvedPos, _) = ResolveAvatarWorldTransform(sim, av);
            if (!IsWithinRadius(resolvedPos, avatarPos, _maxStreamRadius))
                RemoveAvatar(localId);
        }
    }

    /// <summary>
    /// Clear all avatar meshes and cancel all in-flight builds (e.g. on sim change).
    /// </summary>
    public void Clear()
    {
        if (_disposed) return;
        _dirty.Clear();
        _rendered.Clear();
        _lastVisualParamHash.Clear();
        _groundAdjustment.Clear();
        _trackedLocalIds.Clear();
        _pendingTier.Clear();
        _attachmentOwner.Clear();
        _cachedCost.Clear();
        _cachedTier.Clear();
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        foreach (var (id, cts) in _inflight)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _inflight.Clear();

        // Stop all cloud drivers.
        foreach (var kv in _cloudDrivers) kv.Value.Dispose();
        _cloudDrivers.Clear();

        // Remove all avatar scene objects from the viewport.
        // We can't enumerate previously submitted IDs cheaply, so call
        // ClearAllSceneObjects — the terrain base submission is unaffected
        // because it lives in the separate _opaque/_alpha base lists.
        // Object streamer objects will be re-streamed by their own Clear() call.
        // (Both streamers call this at the same time on sim change, so the result
        // is that both layers are cleared together.)
        _viewport.ClearAllSceneObjects();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _overrides.OverrideChanged           -= OnOverrideChanged;
        _client.Friends.FriendshipResponse   -= OnFriendshipResponse;
        _client.Friends.FriendshipTerminated -= OnFriendshipTerminated;
        _debounceTimer.Dispose();
        foreach (var cts in _inflight.Values) { cts.Cancel(); cts.Dispose(); }
        _inflight.Clear();
        foreach (var kv in _cloudDrivers) kv.Value.Dispose();
        _cloudDrivers.Clear();
    }

    /// <summary>
    /// Downloads any wearable assets that are referenced but not yet fetched,
    /// mirroring <c>AvatarViewerViewModel.EnsureWearableAssetsLoadedAsync</c>.
    /// </summary>
    private async Task EnsureWearableAssetsLoadedAsync(CancellationToken ct)
    {
        var wearables = _client.Appearance.GetWearables()
            .Where(w => w.Asset == null && w.AssetID != UUID.Zero)
            .ToList();

        if (wearables.Count == 0) return;

        var tasks = wearables.Select(wearable => Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                var asset = await _client.Assets.RequestAssetAsync(wearable.AssetID, wearable.AssetType, true, linked.Token)
                    .ConfigureAwait(false);
                if (asset is LibreMetaverse.Assets.AssetWearable assetWearable && assetWearable.Decode())
                    wearable.Asset = assetWearable;
            }
            catch { }
        }, ct)).ToList();

        try   { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { /* individual failures are non-fatal */ }
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private static ulong SceneKey(uint localId) => (ulong)AvatarKeyOffset + localId;

    private void EnqueueDirty(uint localId)
    {
        var now = Environment.TickCount64;
        _dirty.AddOrUpdate(localId, now, (_, _) => now);
        // Show the cloud placeholder immediately — not gated by the debounce or the
        // scheduler back-pressure check in ProcessDirty, which only need to protect the
        // expensive real mesh build below. Matches SL's near-instant cloud appearance.
        EnsurePlaceholderVisible(localId);
        // Self-avatar gets an immediate fire; everyone else waits for debounce.
        int delay = localId == _client.Self.LocalID ? SelfDebounceMs : DebounceMs;
        _debounceTimer.Change(delay, Timeout.Infinite);
    }

    private void RemoveAvatar(uint localId)
    {
        _dirty.TryRemove(localId, out _);
        _rendered.TryRemove(localId, out _);
        _lastVisualParamHash.TryRemove(localId, out _);
        _groundAdjustment.TryRemove(localId, out _);
        _trackedLocalIds.TryRemove(localId, out _);
        _pendingTier.TryRemove(localId, out _);
        _cachedCost.TryRemove(localId, out _);
        _cachedTier.TryRemove(localId, out _);
        foreach (var (attId, owner) in _attachmentOwner)
            if (owner == localId) _attachmentOwner.TryRemove(attId, out _);
        if (_inflight.TryRemove(localId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        StopCloudDriver(localId);
        _viewport.RemoveSceneObject(SceneKey(localId));
    }

    private void StopCloudDriver(uint localId)
    {
        if (_cloudDrivers.TryRemove(localId, out var cloud))
            cloud.Dispose();
    }

    private void ProcessDirty()
    {
        if (_disposed) return;

        // Back-pressure: if the scheduler queue is already deep, hold off to avoid
        // flooding it with avatar builds on top of a burst from RebuildAllRendered or
        // a scene seed.  Avatars use a higher threshold than prims so that a burst of
        // prim updates does not lock out avatar scheduling indefinitely.
        if (_scheduler.QueueCount >= 60)
        {
            if (!_dirty.IsEmpty)
                _debounceTimer.Change(DebounceMs, Timeout.Infinite);
            return;
        }

        var now = Environment.TickCount64;
        var due = new List<(uint Id, long Enqueued)>();
        foreach (var (id, enqueued) in _dirty)
        {
            int delay = id == _client.Self.LocalID ? SelfDebounceMs : DebounceMs;
            if (now - enqueued >= delay)
                due.Add((id, enqueued));
        }

        // Cap dispatch per tick, mirroring SceneObjectStreamer.MaxBuildsPerTick. Without this, a
        // burst where dozens of avatars become due in the same tick (region crossing, initial
        // scene seed) enqueues all of their builds at once; since asset fetches for already-cached
        // avatars can return near-instantly, those builds tend to complete in the same clustered
        // window too, flooding the render thread's DrainPendingSceneObjects queue and turning
        // into a multi-second freeze. Oldest-due first so no avatar starves indefinitely; anything
        // left over stays in _dirty and is picked up on the next debounce tick below.
        due.Sort((a, b) => a.Enqueued.CompareTo(b.Enqueued));
        int dispatched = 0;
        foreach (var (id, _) in due)
        {
            if (dispatched >= MaxBuildsPerTick) break;
            _dirty.TryRemove(id, out _);
            EnqueueBuild(id);
            dispatched++;
        }
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private const int MaxBuildsPerTick = 20;

    /// <summary>
    /// Submits a T-pose placeholder mesh (invisible; pick/touch + world-footprint target
    /// only — see <see cref="AvatarPlaceholderFactory"/>) and starts the SL-style particle
    /// cloud (<see cref="AvatarCloudDriver"/>) for <paramref name="localId"/>'s first
    /// appearance, so the avatar has a visible presence immediately while the real
    /// appearance loads. Idempotent — safe to call repeatedly (from both
    /// <see cref="EnqueueDirty"/> and <see cref="EnqueueBuild"/>); no-ops once the avatar
    /// is rendered or a cloud driver already exists.
    /// </summary>
    private void EnsurePlaceholderVisible(uint localId)
    {
        if (_disposed || _rendered.ContainsKey(localId)) return;
        // Once a cloud driver already exists, both the (invisible) placeholder mesh and the
        // particle cloud are already showing -- skip resubmitting the placeholder. This matters
        // beyond tidiness: EnqueueDirty calls this un-debounced on every terse update via
        // OnAvatarUpdate, and an avatar permanently in the Cloud complexity tier would otherwise
        // resubmit a full scene-object every single terse update for as long as it stays in view
        // and keeps moving. The cloud driver's own position (and so the visible particle effect)
        // is kept current separately via UpdateWorldPos from OnTerseAvatarUpdate; only the
        // invisible pick-footprint mesh goes stale here.
        if (_cloudDrivers.ContainsKey(localId)) return;

        var sim = _client.Network.CurrentSim;
        if (sim == null) return;

        Avatar? av = localId == _client.Self.LocalID
            ? (sim.ObjectsAvatars.TryGetValue(localId, out var selfAv) ? selfAv : null)
              ?? new Avatar { LocalID = localId, Position = _client.Self.SimPosition,
                              Rotation = _client.Self.SimRotation }
            : (sim.ObjectsAvatars.TryGetValue(localId, out var otherAv) ? otherAv : null);
        if (av == null) return;

        var (rawPos, rawRot) = ResolveAvatarWorldTransform(sim, av);
        if (rawPos == OmVector3.Zero) return;

        var worldPos = new Vector3(rawPos.X, rawPos.Y, rawPos.Z);
        var worldRot = new Quaternion(rawRot.X, rawRot.Y, rawRot.Z, rawRot.W);
        var placeholder = AvatarPlaceholderFactory.Build(
            $"ph:av:{localId}", worldPos, worldRot, avatarLocalId: localId);
        _viewport.SubmitSceneObject(SceneKey(localId), placeholder);

        // Start a particle cloud at the avatar position to show the
        // SL-style "cloud" effect while appearance data is loading.
        var cloud = new AvatarCloudDriver(localId, worldPos, _viewport);
        if (_cloudDrivers.TryAdd(localId, cloud))
            cloud.Start();
        else
            cloud.Dispose(); // race: another thread already added one
    }

    /// <summary>
    /// Decides how <paramref name="avatarObj"/> should render given its estimated
    /// Veles-local complexity (<see cref="AvatarComplexityEstimator"/>) relative to
    /// <see cref="ComplexityThreshold"/>. Self, friends, and per-avatar overrides are
    /// always Full — but their cost is still computed and cached (see below), it just
    /// doesn't affect the tier decision.
    /// </summary>
    private AvatarRenderTier DetermineRenderTier(Simulator sim, Avatar avatarObj)
    {
        // Cost is always computed/cached, even for exempt avatars -- mirrors SL's own
        // isTooComplex()/getVisualComplexity() split: complexity is computed for every character
        // regardless of exemption, only the muting decision short-circuits for
        // self/friend/always-render. Needed so AvatarRenderInfoReporter can report an honest
        // weight for exempt avatars too, not just the ones Veles actually tiers down. Cheap due
        // to the cache -- see _cachedCost's declaration comment.
        float cost = _cachedCost.GetOrAdd(avatarObj.LocalID,
            id => AvatarComplexityEstimator.EstimateCost(sim, id));

        var tier = DetermineRenderTierCore(avatarObj, cost);
        _cachedTier[avatarObj.LocalID] = tier;
        return tier;
    }

    private AvatarRenderTier DetermineRenderTierCore(Avatar avatarObj, float cost)
    {
        if (avatarObj.LocalID == _client.Self.LocalID) return AvatarRenderTier.Full;
        if (avatarObj.ID != UUID.Zero)
        {
            if (_client.Friends.FriendList.ContainsKey(avatarObj.ID)) return AvatarRenderTier.Full;
            if (_overrides.IsAlwaysRender(avatarObj.ID)) return AvatarRenderTier.Full;
        }
        return AvatarComplexityEstimator.TierForCost(cost, ComplexityThreshold);
    }

    /// <summary>
    /// Looks up the last-computed Veles complexity cost/tier for a single avatar, for UI
    /// display (nameplate cost label). Returns false if the avatar hasn't been through
    /// <see cref="DetermineRenderTier"/> yet (no cache entry). Read-only — never triggers
    /// a compute, matching this cache's existing "avoid recompute-every-tick" intent.
    /// </summary>
    public bool TryGetCachedComplexity(uint localId, out float cost, out AvatarRenderTier tier)
    {
        if (!_cachedCost.TryGetValue(localId, out cost))
        {
            tier = AvatarRenderTier.Full;
            return false;
        }
        tier = _cachedTier.TryGetValue(localId, out var t) ? t : AvatarRenderTier.Full;
        return true;
    }

    /// <summary>
    /// Snapshot of every currently-tracked avatar's last-known tier, for
    /// <see cref="AvatarRenderInfoReporter"/>'s network reporting pass. Weight is always
    /// reported as 0: Veles's complexity-points estimate runs 0-~500 while SL's real ARC
    /// weights run in the tens to hundreds of thousands, and this is a crowd-sourced
    /// capability -- the region combines every present viewer's report for the same avatar, so
    /// sending our number as-is risks corrupting other viewers' "how others see you" readout,
    /// depending on how the region aggregates (average/sum/max). 0 is a deliberately inert
    /// placeholder until the real aggregation behaviour is confirmed in-world. TooComplex mirrors
    /// what SL's own isTooComplex() means: whether *this* viewer has judged the avatar too complex
    /// to render fully -- always false for exempt avatars, same as SL -- and is scale-independent,
    /// so it's reported honestly. Avatars not yet evaluated at least once are skipped.
    /// </summary>
    public IReadOnlyList<(UUID AgentId, int Weight, bool TooComplex)> SnapshotReportableAvatars()
    {
        var sim = _client.Network.CurrentSim;
        if (sim == null) return Array.Empty<(UUID, int, bool)>();

        var list = new List<(UUID, int, bool)>();
        foreach (var localId in _trackedLocalIds.Keys)
        {
            if (!sim.ObjectsAvatars.TryGetValue(localId, out var av) || av.ID == UUID.Zero) continue;
            if (!_cachedCost.TryGetValue(localId, out _)) continue;
            var tier = _cachedTier.TryGetValue(localId, out var t) ? t : AvatarRenderTier.Full;
            list.Add((av.ID, 0, tier != AvatarRenderTier.Full));
        }
        return list;
    }

    /// <summary>
    /// Puts <paramref name="localId"/> into the Cloud render tier: removes any real
    /// scene-object mesh in favour of the invisible pick/footprint placeholder (if one
    /// wasn't already showing from the loading-time cloud), and starts or recolors its
    /// particle cloud with a per-avatar identity tint. Never touches the build
    /// scheduler — that's the actual point of this tier.
    /// </summary>
    private void EnterCloudTier(Simulator sim, uint localId, Avatar av)
    {
        if (_rendered.TryRemove(localId, out _))
        {
            var (rawPos, rawRot) = ResolveAvatarWorldTransform(sim, av);
            var worldPos = new Vector3(rawPos.X, rawPos.Y, rawPos.Z);
            var worldRot = new Quaternion(rawRot.X, rawRot.Y, rawRot.Z, rawRot.W);
            var placeholder = AvatarPlaceholderFactory.Build(
                $"ph:av:{localId}", worldPos, worldRot, avatarLocalId: localId);
            _viewport.SubmitSceneObject(SceneKey(localId), placeholder);
        }

        var tint = AvatarIdentityColor.FromUuid(av.ID);
        if (_cloudDrivers.TryGetValue(localId, out var existing))
        {
            existing.SetTint(tint);
        }
        else
        {
            var (rawPos2, _) = ResolveAvatarWorldTransform(sim, av);
            var worldPos2 = new Vector3(rawPos2.X, rawPos2.Y, rawPos2.Z);
            var cloud = new AvatarCloudDriver(localId, worldPos2, _viewport, tint);
            if (_cloudDrivers.TryAdd(localId, cloud))
                cloud.Start();
            else
                cloud.Dispose(); // race: another thread already added one
        }
    }

    private void OnOverrideChanged(UUID agentId) => HandleTrustChange(agentId);
    private void OnFriendshipResponse(object? sender, FriendshipResponseEventArgs e) => HandleTrustChange(e.AgentID);
    private void OnFriendshipTerminated(object? sender, FriendshipTerminatedEventArgs e) => HandleTrustChange(e.AgentID);

    /// <summary>
    /// Re-evaluates one avatar's tier after something that only affects exemption status
    /// (friend added/removed, "Always Render Fully" toggled) changes for them — O(1),
    /// only touches an avatar already being tracked. An avatar not yet tracked gets
    /// correct exemption treatment for free the next time it comes into range.
    /// </summary>
    private void HandleTrustChange(UUID agentId)
    {
        if (_disposed) return;
        var sim = _client.Network.CurrentSim;
        if (sim == null) return;
        foreach (var av in sim.ObjectsAvatars.Values)
        {
            if (av != null && av.ID == agentId && _trackedLocalIds.ContainsKey(av.LocalID))
            {
                EnqueueDirty(av.LocalID);
                return;
            }
        }
    }

    private void EnqueueBuild(uint localId)
    {
        if (_disposed) return;
        _trackedLocalIds[localId] = 0;

        // Decide render tier before paying any build cost — Cloud tier must never reach
        // the build scheduler (no asset fetch, no GPU upload for it), which is the whole
        // point of tiering. Full/Silhouette fall through to the normal build pipeline
        // below, with the pre-computed tier stashed for BuildAvatarAsync to pick up.
        var sim = _client.Network.CurrentSim;
        if (sim != null && sim.ObjectsAvatars.TryGetValue(localId, out var avForTier))
        {
            var tier = DetermineRenderTier(sim, avForTier);
            if (tier == AvatarRenderTier.Cloud)
            {
                if (_inflight.TryRemove(localId, out var staleCts))
                {
                    staleCts.Cancel();
                    staleCts.Dispose();
                }
                EnterCloudTier(sim, localId, avForTier);
                return;
            }
            _pendingTier[localId] = tier;
        }

        if (_inflight.TryRemove(localId, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _inflight[localId] = cts;

        // Avatars use AvatarMultiplier so they outrank same-distance prims.
        // Additionally boost priority for avatars that are currently visible (in front of the camera).
        var avatarPos = _client.Self.SimPosition;
        float distSq  = AvatarDistanceSq(sim, localId, avatarPos);

        var cam    = _viewport.Camera;
        var eyePos = cam.EyePosition;
        var camFwd = cam.ForwardDirection;

        // Resolve avatar world position for frustum test.
        Vector3 avObjPos = default;
        bool hasPosForFrustum = false;
        if (sim != null && sim.ObjectsAvatars.TryGetValue(localId, out var avForFrustum))
        {
            var (fp, _) = ResolveAvatarWorldTransform(sim, avForFrustum);
            if (fp != OmVector3.Zero)
            {
                avObjPos = new Vector3(fp.X, fp.Y, fp.Z);
                hasPosForFrustum = true;
            }
        }

        float priority = hasPosForFrustum
            ? SceneBuildScheduler.ScoreWithFrustum(distSq, SceneBuildScheduler.AvatarMultiplier, eyePos, camFwd, avObjPos)
            : SceneBuildScheduler.Score(distSq, SceneBuildScheduler.AvatarMultiplier);

        // Progressive geometry: normally already submitted immediately from EnqueueDirty
        // (see EnsurePlaceholderVisible); this is a retry in case that first attempt had
        // no avatar position data yet (e.g. the very first packet).
        EnsurePlaceholderVisible(localId);

        var token = cts.Token;
        _scheduler.Enqueue(priority, _ => BuildAvatarAsync(localId, token));
    }

    private async Task BuildAvatarAsync(uint localId, CancellationToken token)
    {
        if (_disposed) return;

        try
        {
            var sim = _client.Network.CurrentSim;
            if (sim == null) return;

            // Collect visual params for this avatar.
            IReadOnlyDictionary<int, float> visualParams;
            if (localId == _client.Self.LocalID)
            {
                // Ensure all wearable assets are downloaded before reading params.
                // Without this, GetCurrentParamValues() returns default values for any
                // wearable whose asset hasn't been fetched yet (= T-pose / default shape).
                await EnsureWearableAssetsLoadedAsync(token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                visualParams = _client.Appearance.GetCurrentParamValues();
            }
            else
            {
                if (!sim.ObjectsAvatars.TryGetValue(localId, out var av)) return;
                visualParams = av.DecodeVisualParams();
            }

            // Resolve world-space transform early — needed for both LOD distance and submission placement.
            Avatar? avatarObj = localId == _client.Self.LocalID
                ? (sim.ObjectsAvatars.TryGetValue(localId, out var selfAv2) ? selfAv2 : null)
                  ?? new Avatar { LocalID = localId, ParentID = 0, Position = _client.Self.SimPosition, Rotation = _client.Self.SimRotation }
                : (sim.ObjectsAvatars.TryGetValue(localId, out var otherAv2) ? otherAv2 : null)
                  ?? new Avatar { LocalID = localId };

            var (rawWorldPos, worldRot) = ResolveAvatarWorldTransform(sim, avatarObj);
            var worldPos = new Vector3(rawWorldPos.X, rawWorldPos.Y, rawWorldPos.Z);

            // Picked by EnqueueBuild before this build was ever scheduled — Cloud tier
            // never reaches this method at all. Default Full covers the (defensive-only)
            // case where no entry was stashed.
            var renderTier = _pendingTier.TryRemove(localId, out var pt) ? pt : AvatarRenderTier.Full;
            Vector4? overrideColor = renderTier == AvatarRenderTier.Silhouette
                ? AvatarIdentityColor.FromUuid(avatarObj.ID)
                : null;

            var result = await _builder.BuildAsync(
                localId,
                visualParams,
                label:        $"av:{localId}",
                progress:     null,
                ct:           token,
                lodLevel:     AvatarLodForDistance(
                    OmVector3.Distance(rawWorldPos, _client.Self.SimPosition)),
                renderTier:    renderTier,
                overrideColor: overrideColor,
                texturePatch: new Progress<SceneTexturePatch>(patch =>
                {
                    if (token.IsCancellationRequested)
                    {
                        patch.Bitmap?.Dispose();
                        return;
                    }
                    try
                    {
                        // The self-avatar's own bake textures get priority so they aren't
                        // stuck waiting behind the rest of the scene's texture-patch backlog
                        // (which can run into the tens of thousands of entries during a busy
                        // scene load) — see SceneTexturePatch.HighPriority.
                        if (localId == _client.Self.LocalID)
                            patch = patch with { HighPriority = true };
                        _viewport.PatchSceneObjectTexture(patch, token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Token was cancelled between the check above and PatchSceneObjectTexture's
                        // internal gate.Wait(ct) — the bitmap was already disposed inside that call.
                        // Progress<T> invokes this callback on a raw ThreadPool thread when
                        // constructed off the UI thread, so an unhandled exception here would
                        // crash the whole process rather than just failing this build.
                    }
                }))
                .ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            // Cache this avatar's ground-anchor correction from the freshly-built skeleton,
            // then re-resolve worldPos/worldRot so this build places the mesh using the fresh
            // value instead of whatever was cached (or 0) when the position was first read above.
            _groundAdjustment[localId] = AvatarBoneMath.ComputeGroundAdjustment(result.BoneTransforms);
            (rawWorldPos, worldRot) = ResolveAvatarWorldTransform(sim, avatarObj);
            worldPos = new Vector3(rawWorldPos.X, rawWorldPos.Y, rawWorldPos.Z);

            // Apply world-space position so the avatar stands at its sim-local coords.
            var submission = result.Submission;
            if (rawWorldPos != OmVector3.Zero)
            {
                var worldMatrix = AvatarWorldMatrix(worldPos, worldRot);

                // Flexi faces have Transform = Identity (set by AvatarMeshBuilder) and are
                // fully positioned by FlexiPrimAnimator each tick via AttachTransform.
                // Multiplying worldMatrix in here would double-apply the world placement.
                var flexiFaceIndices = new System.Collections.Generic.HashSet<int>();
                foreach (var fp in submission.FlexiPrims)
                    for (int fi = fp.FaceStart; fi < fp.FaceStart + fp.FaceCount; fi++)
                        flexiFaceIndices.Add(fi);

                var translated  = new PrimRenderFace[submission.Faces.Length];
                for (int i = 0; i < submission.Faces.Length; i++)
                {
                    if (flexiFaceIndices.Contains(i))
                    {
                        translated[i] = submission.Faces[i];
                        continue;
                    }
                    var f = submission.Faces[i];
                    translated[i] = f.WithWorldTransform(
                        f.Transform * worldMatrix,
                        f.Centroid + worldPos);
                }

                // Stash worldMatrix on ExternalTransform so the animator post-multiplies
                // it after the dynamic-branch rebuild of attachTx — baking it into
                // AttachTransform would be wiped out as soon as SetBoneProvider triggers
                // the per-tick recomputation from PrimLocalMatrix and the bone matrix.
                foreach (var fp in submission.FlexiPrims)
                    fp.ExternalTransform = worldMatrix;

                submission = new PrimRenderSubmission
                {
                    Label      = submission.Label,
                    Faces      = translated,
                    BoundsMin  = submission.BoundsMin + worldPos,
                    BoundsMax  = submission.BoundsMax + worldPos,
                    FlexiPrims = submission.FlexiPrims,
                    // SkinData/AnimeshSkinData both default to [] on PrimRenderSubmission and
                    // must be carried forward explicitly here, or every scene avatar's
                    // subAnimated check in UploadSceneObjectNoRebuild comes back false and its
                    // skinned body faces get built dynamic:false -- SceneAvatarAnimator's CPU-LBS
                    // fallback then hits VkMesh.UpdateVertices's destroy+recreate re-stage path on
                    // every tick for every such face.
                    SkinData        = submission.SkinData,
                    AnimeshSkinData = submission.AnimeshSkinData,
                };
            }

            _viewport.SubmitSceneObject(SceneKey(localId), submission);
            _rendered[localId] = 0;

            // For other-avatars: the world position was resolved before the async build.
            // If the seat prim arrived during that time, a fresh resolve now returns the
            // correct position.  Issue a fast transform override so the avatar doesn't
            // linger at the fallback sit-offset-as-world-position.
            if (localId != _client.Self.LocalID &&
                sim.ObjectsAvatars.TryGetValue(localId, out var postBuildAv) &&
                postBuildAv.ParentID != 0)
            {
                var (freshPos, freshRot) = ResolveAvatarWorldTransform(sim, postBuildAv);
                var freshWorldPos = new Vector3(freshPos.X, freshPos.Y, freshPos.Z);
                if (Vector3.DistanceSquared(freshWorldPos, worldPos) > 0.01f)
                {
                    var freshWorldMatrix = AvatarWorldMatrix(freshWorldPos, freshRot);
                    _viewport.SetSceneObjectTransform(SceneKey(localId), freshWorldMatrix);
                    // AvatarBuilt hasn't fired yet at this point in the build, so the
                    // SceneAvatarAnimator this avatar's flexi prims will be driven by doesn't
                    // exist yet -- OnFlexiWorldUpdate would silently no-op. Write ExternalTransform
                    // directly onto the FlexiPrims the animator will read once it's created.
                    foreach (var fp in submission.FlexiPrims)
                        fp.ExternalTransform = freshWorldMatrix;
                }
            }

            // Record the visual-param hash so OnAvatarUpdate can skip redundant rebuilds
            // when only position/rotation changes (no appearance change).
            _lastVisualParamHash[localId] = ComputeVisualParamHash(
                localId == _client.Self.LocalID
                    ? null   // self-avatar always rebuilds on AppearanceSet events, not OnAvatarUpdate
                    : (sim.ObjectsAvatars.TryGetValue(localId, out var builtAv)
                        ? builtAv.VisualParameters : null));

            // Avatar fully loaded — stop the cloud particle effect.
            StopCloudDriver(localId);

            AvatarBuilt?.Invoke(SceneKey(localId), localId, result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Swallowing here means AvatarBuilt never fires, so OnAvatarBuilt never
            // constructs/starts a SceneAvatarAnimator for this avatar, and AnimTick is never
            // entered at all -- indistinguishable from a healthy avatar sitting frozen at whatever
            // pose BuildAsync last submitted. Log so a failure here is visible.
            Logger.DebugLog($"[AvatarBuildFail] avatar build failed after submission for localId={localId}: {ex}");
        }
        finally
        {
            // Same reasoning as SceneObjectStreamer: do not TryRemove here.
            // EnqueueBuild may have installed a newer CTS in _inflight[localId];
            // disposing that one would corrupt the newer build's texture delivery.
        }
    }

    /// <summary>
    /// Builds a world-space model matrix for an avatar: rotate around Z by the
    /// yaw component of <paramref name="rotation"/>, then translate to <paramref name="worldPos"/>.
    /// Avatar geometry is built in bind pose oriented along +Y, so a yaw (rotation
    /// around the up-axis / Z) is all that is needed to face the correct heading.
    /// </summary>
    private static Matrix4x4 AvatarWorldMatrix(Vector3 worldPos, LibreMetaverse.Quaternion rotation)
    {
        var q   = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        var rot = Matrix4x4.CreateFromQuaternion(q);
        return rot * Matrix4x4.CreateTranslation(worldPos);
    }

    /// <summary>
    /// Resolves an avatar's world-space position and rotation.
    /// When an avatar is seated (<see cref="Avatar.ParentID"/> != 0), their
    /// Position/Rotation are relative to the seat prim; this method walks the
    /// full linkset hierarchy up to the root prim to compute the world-space
    /// transform (mirrors <c>AgentManager.SimPosition</c> logic).
    /// </summary>
    private (OmVector3 worldPos, LibreMetaverse.Quaternion worldRot) ResolveAvatarWorldTransform(
        Simulator sim, Avatar avatar)
    {
        float hoverZ = avatar.HoverHeight.Z;
        // SL's network position is not feet height — see AvatarBoneMath.ComputeGroundAdjustment.
        // Subtracted uniformly (self, other, seated) so the avatar stands on the ground instead
        // of floating by roughly its own pelvis-to-head-top distance. 0 (no correction) until this
        // avatar's first mesh build completes and caches its real value.
        float groundAdj = _groundAdjustment.TryGetValue(avatar.LocalID, out var adj) ? adj : 0f;

        if (avatar.LocalID == _client.Self.LocalID)
        {
            // SimPosition already folds in self.HoverHeight.Z for the not-sitting case
            // (see AgentManager.SimPosition) — adding hoverZ again here would double it.
            var selfPos = _client.Self.SimPosition;
            selfPos = new OmVector3(selfPos.X, selfPos.Y, selfPos.Z - groundAdj);
            return (selfPos, _client.Self.SimRotation);
        }

        if (avatar.ParentID == 0)
        {
            var pos = avatar.Position;
            pos = new OmVector3(pos.X, pos.Y, pos.Z + hoverZ - groundAdj);
            return (pos, avatar.Rotation);
        }

        // Seated: resolve full hierarchy like AgentManager.SimPosition.
        // Start with the immediate seat prim.
        if (!sim.ObjectsPrimitives.TryGetValue(avatar.ParentID, out var seatPrim))
        {
            var pos = avatar.Position;
            pos = new OmVector3(pos.X, pos.Y, pos.Z + hoverZ - groundAdj);
            return (pos, avatar.Rotation);
        }

        // pos = seat.Position + avatar.Position rotated by seat.Rotation
        var worldPos = seatPrim.Position + avatar.Position * seatPrim.Rotation;
        var worldRot = seatPrim.Rotation * avatar.Rotation;

        // Walk up the linkset toward the root.
        var p = seatPrim;
        while (p.ParentID != 0)
        {
            if (sim.ObjectsPrimitives.TryGetValue(p.ParentID, out var parentPrim))
            {
                worldPos += parentPrim.Position;
                p = parentPrim;
            }
            else
            {
                break;
            }
        }

        worldPos = new OmVector3(worldPos.X, worldPos.Y, worldPos.Z - groundAdj);
        return (worldPos, worldRot);
    }

    private static bool IsWithinRadius(OmVector3 pos, OmVector3 origin, float radius)
    {
        if (pos == OmVector3.Zero) return false;
        var dx = pos.X - origin.X;
        var dy = pos.Y - origin.Y;
        var dz = pos.Z - origin.Z;
        return (dx * dx + dy * dy + dz * dz) <= radius * radius;
    }
    private float AvatarDistanceSq(Simulator? sim, uint localId, LibreMetaverse.Vector3 avatarPos)
    {
        if (sim != null && sim.ObjectsAvatars.TryGetValue(localId, out var av))
        {
            var (pos, _) = ResolveAvatarWorldTransform(sim, av);
            var dx = pos.X - avatarPos.X;
            var dy = pos.Y - avatarPos.Y;
            var dz = pos.Z - avatarPos.Z;
            return dx * dx + dy * dy + dz * dz;
        }
        // Self avatar or unknown — treat as distance 0 (highest priority).
        if (localId == _client.Self.LocalID) return 0f;
        return float.MaxValue;
    }

    /// <summary>
    /// Computes a cheap hash of an avatar's visual parameter byte array.
    /// Used to detect appearance changes so position-only updates don't
    /// trigger an unnecessary full mesh rebuild.
    /// Returns 0 for null/empty arrays so unset params always compare equal
    /// to themselves (self-avatar path deliberately passes null to force
    /// appearance-driven rebuilds via <see cref="OnAppearanceSet"/> instead).
    /// </summary>
    private static int ComputeVisualParamHash(byte[]? vp)
    {
        if (vp == null || vp.Length == 0) return 0;
        // FNV-1a 32-bit — fast, no allocations, good distribution for byte arrays.
        uint hash = 2166136261u;
        foreach (byte b in vp)
            hash = (hash ^ b) * 16777619u;
        return (int)hash;
    }
}
