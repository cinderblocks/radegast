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
/// scene-object layer of a <see cref="GlViewportControl"/>.
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
    private readonly GlViewportControl   _viewport;
    private readonly PrimMeshBuilder     _builder;
    private readonly SceneBuildScheduler _scheduler;

    // Network-bound prefetch stage. Wider than the CPU build scheduler because its slots
    // spend their time awaiting HTTP downloads, not computing; LibreMetaverse's download
    // manager provides the actual connection-level throttle.
    private readonly SceneBuildScheduler _fetchScheduler = new(maxConcurrent: 8);

    // sceneKey → CancellationTokenSource for the in-flight build task.
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _inflight = new();

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

    public SceneObjectStreamer(GridClient client, GlViewportControl viewport,
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

                bool isSinglePrim = !_childrenByParent.TryGetValue(sceneKey, out var ch) || ch.IsEmpty;
                if (!isSinglePrim)
                    EnqueueDirty(sceneKey);

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

        foreach (var (_, cts) in _inflight)
        {
            cts.Cancel();
            cts.Dispose();
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
        foreach (var cts in _inflight.Values) { cts.Cancel(); cts.Dispose(); }
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
        if (_inflight.TryRemove(sceneKey, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        _dirty.TryRemove(sceneKey, out _);
        _rendered.TryRemove(sceneKey, out _);
        _textureLodLevel.TryRemove(sceneKey, out _);
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

        if (_inflight.TryRemove(sceneKey, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts   = new CancellationTokenSource();
        var token = cts.Token;
        _inflight[sceneKey] = cts;

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

        _fetchScheduler.Enqueue(priority, _ => PrefetchThenScheduleBuildAsync(sceneKey, priority, token));
    }

    /// <summary>
    /// Stage 1 of the build pipeline (network-bound): prefetch the linkset's mesh assets
    /// and sculpt-texture bytes in parallel under <see cref="_fetchScheduler"/>, whose
    /// slots are cheap to hold across HTTP waits. Stage 2 (CPU-bound) then runs in the
    /// shared build scheduler against warm caches, so tessellation slots do pure CPU work
    /// instead of serialising on one download per prim.
    /// </summary>
    private async Task PrefetchThenScheduleBuildAsync(ulong sceneKey, float priority, CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested) return;

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
        catch (OperationCanceledException) { return; }
        catch { /* prefetch is best-effort; BuildObjectAsync downloads on cache miss */ }

        if (_disposed || token.IsCancellationRequested) return;
        _scheduler.Enqueue(priority, _ => BuildObjectAsync(sceneKey, token));
    }

    private async Task BuildObjectAsync(ulong sceneKey, CancellationToken token)
    {
        if (_disposed) return;

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
                        // Stamp the full scene key so the viewport resolves the owning
                        // linkset by direct dictionary lookup. _sceneObjects is keyed by
                        // sceneKey (sim index << 32 | rootLocalId); without this the lookup
                        // falls back to (ulong)RootLocalId — the *child* prim's localId for
                        // linkset faces — which never matches a root sceneKey, so the texture
                        // patch is deferred and ultimately dropped (faces stay untextured).
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
                };
            }

            _viewport.SubmitSceneObject(sceneKey, submission);
            _rendered[sceneKey] = 0;
            ObjectBuilt?.Invoke(rootLocalId, submission);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Swallow per-object failures — the scene viewer continues working.
        }
        finally
        {
            // Do NOT unconditionally TryRemove here: EnqueueBuild may have already
            // replaced _inflight[sceneKey] with a NEWER build's CTS while this build
            // was running.  Removing and disposing that CTS would corrupt the newer
            // build — its texture downloads would observe ODsE from a disposed token.
            // Lifecycle: each CTS is cancelled+disposed by the NEXT EnqueueBuild for
            // the same key (or by Dispose() on tear-down), not by the build that owns it.
            // StreamTexturesAsync is still live in the background and uses this token,
            // so disposing it here would also corrupt in-flight texture delivery.
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
