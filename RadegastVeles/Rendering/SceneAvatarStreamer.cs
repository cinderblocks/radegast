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
using OmVector3  = LibreMetaverse.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Vector3    = System.Numerics.Vector3;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Streams nearby avatar meshes into the scene-object layer of a
/// <see cref="GlViewportControl"/>.
/// <para>
/// Uses a negative key space to avoid collisions with prim LocalIDs managed
/// by <see cref="SceneObjectStreamer"/>: avatar scene keys are stored as
/// <c>uint.MaxValue - localId</c>.
/// </para>
/// </summary>
internal sealed class SceneAvatarStreamer : IDisposable
{
    private readonly GridClient          _client;
    private readonly GlViewportControl   _viewport;
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
    /// The sceneKey is the ulong used in <see cref="GlViewportControl.SubmitSceneObject"/>.
    /// </summary>
    public event Action<ulong, uint, AvatarBuildResult>? AvatarBuilt;

    private SceneAvatarAnimationStreamer? _animationStreamer;

    /// <summary>
    /// Wires the animation streamer so terse avatar position updates can push
    /// updated world matrices into flexi attachment animators.
    /// </summary>
    public void SetAnimationStreamer(SceneAvatarAnimationStreamer animationStreamer)
        => _animationStreamer = animationStreamer;

    /// <summary>
    /// Computes the current world matrix for the avatar with the given local ID.
    /// Returns <see cref="Matrix4x4"/> identity if the avatar is not found.
    /// Used by <see cref="SceneFlexiStreamer"/> to seed <c>ExternalTransform</c>
    /// on a freshly-built flexi animator so attachments appear at the correct
    /// world position from the very first tick rather than snapping from origin.
    /// </summary>
    public Matrix4x4 GetCurrentWorldMatrix(uint localId)
    {
        var sim = _client.Network.CurrentSim;
        if (sim == null) return Matrix4x4.Identity;

        if (localId == _client.Self.LocalID)
        {
            var p = _client.Self.SimPosition;
            var r = _client.Self.SimRotation;
            return AvatarWorldMatrix(new Vector3(p.X, p.Y, p.Z), r);
        }

        if (!sim.ObjectsAvatars.TryGetValue(localId, out var av))
            return Matrix4x4.Identity;

        var (wp, wr) = ResolveAvatarWorldTransform(sim, av);
        return AvatarWorldMatrix(new Vector3(wp.X, wp.Y, wp.Z), wr);
    }

    public SceneAvatarStreamer(GridClient client, GlViewportControl viewport,
        SceneBuildScheduler scheduler)
    {
        _client    = client;
        _viewport  = viewport;
        _builder   = new AvatarMeshBuilder(client);
        _scheduler = scheduler;

        _debounceTimer = new Timer(_ => ProcessDirty(), null,
            Timeout.Infinite, Timeout.Infinite);
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
            _viewport.SetSceneObjectTransform(SceneKey(localId), AvatarWorldMatrix(wp, resolvedRot));
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
                _viewport.SetSceneObjectTransform(SceneKey(selfId), AvatarWorldMatrix(wp, resolvedRot));
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
        // Self-avatar gets an immediate fire; everyone else waits for debounce.
        int delay = localId == _client.Self.LocalID ? SelfDebounceMs : DebounceMs;
        _debounceTimer.Change(delay, Timeout.Infinite);
    }

    private void RemoveAvatar(uint localId)
    {
        _dirty.TryRemove(localId, out _);
        _rendered.TryRemove(localId, out _);
        _lastVisualParamHash.TryRemove(localId, out _);
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
        var due = new List<uint>();
        foreach (var (id, enqueued) in _dirty)
        {
            if (now - enqueued >= DebounceMs)
                due.Add(id);
        }
        foreach (var id in due)
        {
            _dirty.TryRemove(id, out _);
            EnqueueBuild(id);
        }
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void EnqueueBuild(uint localId)
    {
        if (_disposed) return;

        if (_inflight.TryRemove(localId, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _inflight[localId] = cts;

        // Avatars use AvatarMultiplier so they outrank same-distance prims.
        // Additionally boost priority for avatars that are currently visible (in front of the camera).
        var sim       = _client.Network.CurrentSim;
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

        // Progressive geometry: submit a T-pose placeholder immediately so the
        // avatar occupies its correct world-space footprint while BuildAsync runs.
        // Only for first appearance — updates to already-visible avatars keep the
        // real geometry until the new build finishes.
        if (!_rendered.ContainsKey(localId) && sim != null)
        {
            Avatar? av = localId == _client.Self.LocalID
                ? (sim.ObjectsAvatars.TryGetValue(localId, out var selfAv) ? selfAv : null)
                  ?? new Avatar { LocalID = localId, Position = _client.Self.SimPosition,
                                  Rotation = _client.Self.SimRotation }
                : (sim.ObjectsAvatars.TryGetValue(localId, out var otherAv) ? otherAv : null);

            if (av != null)
            {
                var (rawPos, rawRot) = ResolveAvatarWorldTransform(sim, av);
                if (rawPos != OmVector3.Zero)
                {
                    var worldPos = new Vector3(rawPos.X, rawPos.Y, rawPos.Z);
                    var worldRot = new Quaternion(rawRot.X, rawRot.Y, rawRot.Z, rawRot.W);
                    var placeholder = AvatarPlaceholderFactory.Build(
                        $"ph:av:{localId}", worldPos, worldRot, avatarLocalId: localId);
                    _viewport.SubmitSceneObject(SceneKey(localId), placeholder);

                    // Start a particle cloud at the avatar position to show the
                    // SL-style "cloud" effect while appearance data is loading.
                    if (!_cloudDrivers.ContainsKey(localId))
                    {
                        var cloud = new AvatarCloudDriver(localId, worldPos, _viewport);
                        if (_cloudDrivers.TryAdd(localId, cloud))
                            cloud.Start();
                        else
                            cloud.Dispose(); // race: another thread already added one
                    }
                }
            }
        }

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

            var result = await _builder.BuildAsync(
                localId,
                visualParams,
                label:        $"av:{localId}",
                progress:     null,
                ct:           token,
                lodLevel:     AvatarLodForDistance(
                    OmVector3.Distance(rawWorldPos, _client.Self.SimPosition)),
                texturePatch: new Progress<SceneTexturePatch>(patch =>
                {
                    if (token.IsCancellationRequested)
                        patch.Bitmap?.Dispose();
                    else
                        _viewport.PatchSceneObjectTexture(patch, token);
                }))
                .ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

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
                    _viewport.SetSceneObjectTransform(SceneKey(localId), AvatarWorldMatrix(freshWorldPos, freshRot));
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
        catch (Exception)
        {
            // Swallow per-avatar failures — the scene viewer continues working.
        }
        finally
        {
            if (_inflight.TryRemove(localId, out var current))
                current.Dispose();
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

        if (avatar.LocalID == _client.Self.LocalID)
        {
            var pos = _client.Self.SimPosition;
            pos = new OmVector3(pos.X, pos.Y, pos.Z + hoverZ);
            return (pos, _client.Self.SimRotation);
        }

        if (avatar.ParentID == 0)
        {
            var pos = avatar.Position;
            pos = new OmVector3(pos.X, pos.Y, pos.Z + hoverZ);
            return (pos, avatar.Rotation);
        }

        // Seated: resolve full hierarchy like AgentManager.SimPosition.
        // Start with the immediate seat prim.
        if (!sim.ObjectsPrimitives.TryGetValue(avatar.ParentID, out var seatPrim))
        {
            var pos = avatar.Position;
            pos = new OmVector3(pos.X, pos.Y, pos.Z + hoverZ);
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
