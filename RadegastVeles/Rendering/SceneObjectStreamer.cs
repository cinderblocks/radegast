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
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using TkMatrix4 = OpenTK.Mathematics.Matrix4;
using TkVector3 = OpenTK.Mathematics.Vector3;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Streams in-world prim objects from the current simulator into the
/// scene-object layer of a <see cref="GlViewportControl"/>.
/// <para>
/// When an <see cref="ObjectManager.ObjectUpdate"/> event arrives the object is
/// added to a dirty set.  A lightweight debounce timer coalesces rapid bursts of
/// updates into a single tessellation run per root linkset.
/// </para>
/// </summary>
internal sealed class SceneObjectStreamer : IDisposable
{
    private readonly GridClient          _client;
    private readonly GlViewportControl   _viewport;
    private readonly PrimMeshBuilder     _builder;
    private readonly SceneBuildScheduler _scheduler;

    // rootLocalId → CancellationTokenSource for the in-flight build task.
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _inflight = new();

    // Dirty roots queued for tessellation (rootLocalId → timestamp of first enqueue).
    private readonly ConcurrentDictionary<uint, long> _dirty = new();

    // Root local IDs that currently have a live scene-object submission.
    private readonly ConcurrentDictionary<uint, byte> _rendered = new();

    // Reverse parent index: rootLocalId → set of child LocalIDs.
    // Maintained incrementally so CollectLinkset and IsSinglePrim checks are O(1)
    // instead of O(n) full-dict scans.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<uint, byte>> _childrenByParent = new();

    /// <summary>Number of object build tasks currently running.</summary>
    public int InflightCount => _inflight.Count;

    // How long to wait after the last update for a root before tessellating.
    // 50 ms is enough to coalesce rapid property updates within the same sim
    // tick without delaying first-appearance by a full 400 ms.
    private const int DebounceMs = 50;

    private readonly Timer  _debounceTimer;
    private bool            _disposed;

    // Maximum radius from the avatar (in metres) at which objects are streamed.
    // Prims outside this radius are ignored/culled.
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

        _debounceTimer = new Timer(_ => ProcessDirty(), null,
            Timeout.Infinite, Timeout.Infinite);
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the thread-pool after a linkset is built and submitted to the viewport.
    /// Arguments: (rootLocalId, submission).  Subscribe before calling <see cref="OnObjectUpdate"/>.
    /// </summary>
    public event Action<uint, PrimRenderSubmission>? ObjectBuilt;

    /// <summary>
    /// Call when a prim update is received from the simulator.
    /// Skips attachments, avatars, and objects outside the stream radius.
    /// </summary>
    public void OnObjectUpdate(Simulator sim, Primitive prim, bool isAttachment)
    {
        if (_disposed) return;
        if (isAttachment) return;

        var currentSim = _client.Network.CurrentSim;
        if (sim != currentSim) return;

        var rootId    = prim.ParentID == 0 ? prim.LocalID : prim.ParentID;
        var avatarPos = _client.Self.SimPosition;

        // Maintain the reverse parent index for child prims.
        if (prim.ParentID != 0)
        {
            _childrenByParent.GetOrAdd(rootId, _ => new ConcurrentDictionary<uint, byte>())
                             .TryAdd(prim.LocalID, 0);
        }

        // Child prims have parent-relative positions, not world positions.
        // Always use the root prim's world position for the radius check so that
        // property updates on child prims don't cause the linkset to be culled.
        var rootPos = GetRootWorldPosition(sim, rootId, prim);

        if (!IsWithinRadius(rootPos, avatarPos, _maxStreamRadius))
        {
            // If we previously had this object loaded, remove it.
            _viewport.RemoveSceneObject(rootId);
            return;
        }

        EnqueueDirty(rootId);
    }

    /// <summary>
    /// Call when a terse (position/rotation) update arrives for a prim.
    /// If the root linkset is already rendered, fast-paths the translation
    /// without a full mesh rebuild. Otherwise triggers a normal dirty enqueue.
    /// </summary>
    public void OnTerseObjectUpdate(Simulator sim, Primitive prim)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;
        if (prim.PrimData.PCode == PCode.Avatar) return;

        var rootId    = prim.ParentID == 0 ? prim.LocalID : prim.ParentID;
        var avatarPos = _client.Self.SimPosition;

        // Child prims have parent-relative positions; use the root prim's world
        // position for the radius check to avoid wrongly culling the linkset.
        var rootPos = GetRootWorldPosition(sim, rootId, prim);

        if (!IsWithinRadius(rootPos, avatarPos, _maxStreamRadius))
        {
            CancelAndRemove(rootId);
            return;
        }

        if (_rendered.ContainsKey(rootId))
        {
            if (prim.ParentID == 0)
            {
                // Root prim terse update: apply the new transform immediately so
                // the object moves visually on every packet (including keyframe
                // motion) without waiting for the debounced rebuild to finish.
                var s = prim.Scale;
                var r = prim.Rotation;
                var p = prim.Position;
                var scale = OpenTK.Mathematics.Matrix4.CreateScale(s.X, s.Y, s.Z);
                var rot   = OpenTK.Mathematics.Matrix4.CreateFromQuaternion(
                                new OpenTK.Mathematics.Quaternion(r.X, r.Y, r.Z, r.W));
                var trans = OpenTK.Mathematics.Matrix4.CreateTranslation(p.X, p.Y, p.Z);
                _viewport.SetSceneObjectTransform(rootId, scale * rot * trans);

                // Also queue a debounced rebuild for linksets so child-face world
                // positions are recalculated at the new root location.
                // Use the reverse-parent index for O(1) single-prim detection.
                bool isSinglePrim = !_childrenByParent.TryGetValue(rootId, out var ch) || ch.IsEmpty;
                if (!isSinglePrim)
                    EnqueueDirty(rootId);
            }
            // Child-prim terse updates do not carry a new world position for the
            // linkset root; the root's own terse update handles the transform.
        }
        else
        {
            EnqueueDirty(rootId);
        }
    }

    /// <summary>
    /// Call when a kill-object notification is received from the simulator.
    /// </summary>
    public void OnKillObject(Simulator sim, uint localId)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;

        // The killed object could be a root or a child.  Cancel any in-flight
        // build for it (in case it was a root) and remove from the viewport.
        CancelAndRemove(localId);

        // If the killed object was a child, prune the reverse index and
        // dirty-rebuild the parent root.
        if (_client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(localId, out var prim)
            && prim.ParentID != 0)
        {
            if (_childrenByParent.TryGetValue(prim.ParentID, out var siblings))
                siblings.TryRemove(localId, out _);
            EnqueueDirty(prim.ParentID);
        }
        else
        {
            // Root was killed — also remove its children entry from the index.
            _childrenByParent.TryRemove(localId, out _);
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
        foreach (var rootId in _rendered.Keys)
            _dirty.AddOrUpdate(rootId, now, (_, _) => now);
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    /// <summary>
    /// Re-enqueues all currently rendered root IDs for a rebuild after a GL
    /// context reset (tab switch). Routes through <see cref="EnqueueDirty"/> with
    /// a near-zero debounce so the back-pressure guard in <see cref="ProcessDirty"/>
    /// throttles the burst rather than flooding the scheduler with hundreds of
    /// simultaneous <see cref="EnqueueBuild"/> calls, which can overflow the queue
    /// and drop objects.
    /// </summary>
    public void RebuildAllRendered()
    {
        if (_disposed) return;
        var now = Environment.TickCount64 - DebounceMs; // mark as immediately due
        foreach (var rootId in _rendered.Keys)
            _dirty.AddOrUpdate(rootId, now, (_, _) => now);
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(0, Timeout.Infinite); // fire ProcessDirty immediately
    }

    /// <summary>
    /// Immediately removes any rendered objects that now lie outside the current
    /// <see cref="DrawDistance"/>. Called automatically when the draw distance is
    /// reduced so far objects disappear without waiting for the next network event.
    /// </summary>
    public void CullBeyondDrawDistance()
    {
        if (_disposed) return;
        var avatarPos = _client.Self.SimPosition;
        var sim       = _client.Network.CurrentSim;
        if (sim == null) return;

        foreach (var rootId in _rendered.Keys)
        {
            var objs = sim.ObjectsPrimitives;
            if (objs == null || !objs.TryGetValue(rootId, out var root)) continue;
            if (!IsWithinRadius(root.Position, avatarPos, _maxStreamRadius))
                CancelAndRemove(rootId);
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
        _childrenByParent.Clear();
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

        foreach (var (rootId, cts) in _inflight)
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
        foreach (var cts in _inflight.Values) { cts.Cancel(); cts.Dispose(); }
        _inflight.Clear();
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private void EnqueueDirty(uint rootId)
    {
        var now = Environment.TickCount64;
        _dirty.AddOrUpdate(rootId, now, (_, _) => now);
        // Restart debounce window.
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void CancelAndRemove(uint rootId)
    {
        if (_inflight.TryRemove(rootId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        _dirty.TryRemove(rootId, out _);
        _rendered.TryRemove(rootId, out _);
        _viewport.RemoveSceneObject(rootId);
    }

    // Maximum number of build tasks dispatched to the scheduler in one ProcessDirty
    // tick.  Priority sorting above ensures we always dispatch the nearest/in-frustum
    // objects first, so 20 well-chosen slots per tick gives good throughput without
    // flooding the GL command queue with placeholder submissions or saturating the
    // thread pool during the initial scene-load burst.
    private const int MaxBuildsPerTick = 20;

    // Quick priority score used only for sorting the ProcessDirty dispatch list.
    // Does not need to be identical to SceneBuildScheduler.ScoreWithFrustum; it just
    // needs to rank nearest/in-frustum objects higher than far/behind-camera ones.
    private float ScoreDue(
        uint rootId,
        OpenMetaverse.Vector3 avatarPos,
        TkVector3 eyePos,
        TkVector3 camFwd,
        ConcurrentDictionary<uint, Primitive>? ovPrims)
    {
        float distSq = DistanceSq(rootId, avatarPos);
        if (ovPrims != null && ovPrims.TryGetValue(rootId, out var rp))
        {
            var objPos = new TkVector3(rp.Position.X, rp.Position.Y, rp.Position.Z);
            return SceneBuildScheduler.ScoreWithFrustum(
                distSq, SceneBuildScheduler.PrimMultiplier, eyePos, camFwd, objPos);
        }
        return SceneBuildScheduler.Score(distSq, SceneBuildScheduler.PrimMultiplier);
    }

    private void ProcessDirty()
    {
        if (_disposed) return;

        // Note: we no longer bail early when the scheduler queue is deep.
        // The scheduler's own MaxQueueDepth (500) + priority-drop policy provides
        // sufficient throttling; bailing here caused ProcessDirty to stall
        // indefinitely after a tab-switch GL reset when leftover pending items
        // from the previous session kept QueueCount elevated.

        var now  = Environment.TickCount64;
        var due  = new List<uint>();

        foreach (var (rootId, enqueued) in _dirty)
        {
            if (now - enqueued >= DebounceMs)
                due.Add(rootId);
        }

        // Sort nearest/in-frustum objects first so that when MaxBuildsPerTick
        // caps the dispatch count we always build the most important objects.
        // Pre-score into a (score, id) list so each item pays exactly one dict
        // lookup instead of O(log n) lookups inside the Sort comparator.
        if (due.Count > 1)
        {
            var avatarPos = _client.Self.SimPosition;
            var cam       = _viewport.Camera;
            var eyePos    = cam.EyePosition;
            var camFwd    = cam.ForwardDirection;
            var ovPrims   = _client.Network.CurrentSim?.ObjectsPrimitives;

            var scored = new List<(float score, uint id)>(due.Count);
            foreach (var rootId in due)
                scored.Add((ScoreDue(rootId, avatarPos, eyePos, camFwd, ovPrims), rootId));

            // Insertion sort is fastest for the small N (≤ MaxBuildsPerTick) typical here.
            for (int i = 1; i < scored.Count; i++)
            {
                var key = scored[i];
                int j = i - 1;
                while (j >= 0 && scored[j].score < key.score)
                {
                    scored[j + 1] = scored[j];
                    j--;
                }
                scored[j + 1] = key;
            }

            due.Clear();
            foreach (var (_, rootId) in scored)
                due.Add(rootId);
        }

        int dispatched = 0;
        foreach (var rootId in due)
        {
            if (dispatched >= MaxBuildsPerTick) break;
            _dirty.TryRemove(rootId, out _);
            EnqueueBuild(rootId);
            dispatched++;
        }

        // If there are still pending dirty entries reschedule.
        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void EnqueueBuild(uint rootId)
    {
        if (_disposed) return;

        // Cancel any previous in-flight or pending build for this root.
        if (_inflight.TryRemove(rootId, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        var token = cts.Token;          // capture before exposing cts to other threads
        _inflight[rootId] = cts;

        // Compute priority: closer prims that are in front of the camera score highest.
        var avatarPos  = _client.Self.SimPosition;
        float distSq   = DistanceSq(rootId, avatarPos);

        var cam     = _viewport.Camera;
        var eyePos  = cam.EyePosition;
        var camFwd  = cam.ForwardDirection;

        // Best-effort object centre from the root prim; fall back to plain Score.
        TkVector3 objPos = default;
        bool hasPosForFrustum = false;
        var ovPrims = _client.Network.CurrentSim?.ObjectsPrimitives;
        if (ovPrims != null && ovPrims.TryGetValue(rootId, out var rp))
        {
            objPos = new TkVector3(rp.Position.X, rp.Position.Y, rp.Position.Z);
            hasPosForFrustum = true;
        }

        float priority = hasPosForFrustum
            ? SceneBuildScheduler.ScoreWithFrustum(distSq, SceneBuildScheduler.PrimMultiplier, eyePos, camFwd, objPos)
            : SceneBuildScheduler.Score(distSq, SceneBuildScheduler.PrimMultiplier);

        // Progressive geometry: submit a placeholder box immediately so the object
        // appears at the correct position while tessellation runs in the background.
        // Only do this for the first appearance — updates to already-visible objects
        // keep the existing (real) geometry until the new build finishes.
        if (!_rendered.ContainsKey(rootId))
        {
            var rootPrimOv = _client.Network.CurrentSim?.ObjectsPrimitives;
            if (rootPrimOv != null &&
                rootPrimOv.TryGetValue(rootId, out var rootPrim) &&
                rootPrim.Scale.LengthSquared() > 0f)
            {
                var scale    = new TkVector3(rootPrim.Scale.X, rootPrim.Scale.Y, rootPrim.Scale.Z);
                var worldPos = new TkVector3(rootPrim.Position.X, rootPrim.Position.Y, rootPrim.Position.Z);
                var placeholder = PlaceholderMeshFactory.Build(
                    $"ph:{rootId}", scale, worldPos, rootPrimLocalId: rootId);
                _viewport.SubmitSceneObject(rootId, placeholder);
            }
        }

        _scheduler.Enqueue(priority, _ => BuildObjectAsync(rootId, token));
    }

    private async Task BuildObjectAsync(uint rootId, CancellationToken token)
    {
        if (_disposed) return;

        try
        {
            var prims = CollectLinkset(rootId);
            if (prims == null || prims.Count == 0)
            {
                _viewport.RemoveSceneObject(rootId);
                return;
            }

            var rootPrimForLod = prims.Find(p => p.LocalID == rootId) ?? prims[0];
            float dist       = Vector3.Distance(rootPrimForLod.Position, _client.Self.SimPosition);
            var   lod        = LodForDistance(dist);

            var submission = await _builder.BuildAsync(
                prims, rootId,
                label:        $"prim:{rootId}",
                progress:     null,
                ct:           token,
                detailLevel:  lod,
                texturePatch: new Progress<SceneTexturePatch>(patch =>
                {
                    if (token.IsCancellationRequested)
                        patch.Bitmap?.Dispose();
                    else
                        _viewport.PatchSceneObjectTexture(patch, token);
                }))
                .ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            // PrimMeshBuilder produces geometry relative to the root prim's local
            // origin (scale × rotation, no world translation). Apply the root's
            // sim-local position so objects appear at the correct world-space
            // location in the scene viewer.
            var rootPrim = rootPrimForLod;
            var worldPos = new TkVector3(
                rootPrim.Position.X, rootPrim.Position.Y, rootPrim.Position.Z);

            if (worldPos != OpenTK.Mathematics.Vector3.Zero)
            {
                // Build a set of face indices that are owned by flexi prims so we
                // can clear their Transform (the animator owns vertex placement).
                var flexiFaceIndices = new System.Collections.Generic.HashSet<int>();
                foreach (var fp in submission.FlexiPrims)
                    for (int fi = fp.FaceStart; fi < fp.FaceStart + fp.FaceCount; fi++)
                        flexiFaceIndices.Add(fi);

                var worldTransMat = TkMatrix4.CreateTranslation(worldPos);

                var translated = new PrimRenderFace[submission.Faces.Length];
                for (int i = 0; i < submission.Faces.Length; i++)
                {
                    if (flexiFaceIndices.Contains(i))
                        // Flexi face: leave Transform = Identity; animator writes world-space verts.
                        translated[i] = submission.Faces[i];
                    else
                        translated[i] = submission.Faces[i].WithWorldTranslation(worldPos);
                }

                // Stash world translation on ExternalTransform so the animator
                // post-multiplies it without disturbing the prim-local AttachTransform.
                // (Scene objects have no live bone provider so this is mostly a
                // consistency change, but it mirrors the avatar-attachment path and
                // keeps AttachTransform purely prim-local.)
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

            _viewport.SubmitSceneObject(rootId, submission);
            _rendered[rootId] = 0;
            ObjectBuilt?.Invoke(rootId, submission);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Swallow per-object failures — the scene viewer continues working.
        }
        finally
        {
            if (_inflight.TryRemove(rootId, out var current))
                current.Dispose();
        }
    }

    /// <summary>
    /// Selects a <see cref="DetailLevel"/> appropriate for the given distance.
    /// Mirrors the SL viewer's LOD selection heuristic:
    /// &lt;20 m = Highest, &lt;40 m = High, &lt;80 m = Medium, else Low.
    /// </summary>
    private static DetailLevel LodForDistance(float distance) => distance switch
    {
        < 20f  => DetailLevel.Highest,
        < 40f  => DetailLevel.High,
        < 80f  => DetailLevel.Medium,
        _      => DetailLevel.Low,
    };

    /// <summary>
    /// Collect the root prim plus all children that reference it as their parent.
    /// Returns null if the root is no longer present in the sim's object dict.
    /// </summary>
    private List<Primitive>? CollectLinkset(uint rootId)
    {
        var objs = _client.Network.CurrentSim?.ObjectsPrimitives;
        if (objs == null) return null;

        if (!objs.TryGetValue(rootId, out var root)) return null;

        var result = new List<Primitive> { root };

        // Use the reverse-parent index for O(1) child lookup instead of an O(n) scan.
        if (_childrenByParent.TryGetValue(rootId, out var childIds))
        {
            foreach (var childId in childIds.Keys)
            {
                if (objs.TryGetValue(childId, out var child))
                    result.Add(child);
            }
        }
        return result;
    }

    private static bool IsWithinRadius(Vector3 primPos, Vector3 avatarPos, float radius)
    {
        if (primPos == Vector3.Zero) return false; // no position yet
        var dx = primPos.X - avatarPos.X;
        var dy = primPos.Y - avatarPos.Y;
        var dz = primPos.Z - avatarPos.Z;
        return (dx * dx + dy * dy + dz * dz) <= radius * radius;
    }

    /// <summary>
    /// Returns the world-space position to use for radius checks.
    /// For root prims this is <paramref name="prim"/>.Position directly.
    /// For child prims (ParentID != 0) the root's position is looked up in the
    /// simulator's object dictionary, falling back to the child's own position
    /// (which is parent-relative and therefore unreliable) only when the root
    /// is not yet known.
    /// </summary>
    private Vector3 GetRootWorldPosition(Simulator sim, uint rootId, Primitive prim)
    {
        if (prim.ParentID == 0)
            return prim.Position;

        var objs = sim.ObjectsPrimitives;
        if (objs != null && objs.TryGetValue(rootId, out var root) && root.Position != Vector3.Zero)
            return root.Position;

        // Root not yet in dictionary — treat child position as approximate world pos.
        return prim.Position;
    }

    /// <summary>
    /// Squared distance from the avatar to the root prim's current position.
    /// Returns a large value when the root is not found so it sorts to the back.
    /// </summary>
    private float DistanceSq(uint rootId, OpenMetaverse.Vector3 avatarPos)
    {
        var objs = _client.Network.CurrentSim?.ObjectsPrimitives;
        if (objs != null && objs.TryGetValue(rootId, out var p))
        {
            var dx = p.Position.X - avatarPos.X;
            var dy = p.Position.Y - avatarPos.Y;
            var dz = p.Position.Z - avatarPos.Z;
            return dx * dx + dy * dy + dz * dz;
        }
        return float.MaxValue;
    }
}
