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
using System.Threading.Tasks;
using LibreMetaverse;
using LibreMetaverse.Assets;
using Quaternion = System.Numerics.Quaternion;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Downloads BVH animation assets and advances their playback time each frame,
/// producing a per-joint delta-rotation dictionary for use with
/// <see cref="AvatarMeshBuilder.ComputeAnimatedBoneWorldMatrices"/>.
///
/// Mirrors the approach in the legacy Radegast RenderAvatar.addanimation / animate methods,
/// cross-referenced with the SL viewer llagent.cpp / llvoavatar.cpp animation pipeline.
/// </summary>
internal sealed class AvatarAnimationPlayer : IDisposable
{
    // ── Static BVH cache (shared across all player instances) ─────────────────────

    private static readonly Dictionary<UUID, BinBVHAnimationReader> s_cache    = new();
    private static readonly object                                   s_cacheLock = new();

    // ── Per-instance animation state ──────────────────────────────────────────────

    private sealed class AnimEntry
    {
        public UUID                  Id;
        public BinBVHAnimationReader Reader    = null!;
        public float                 CurrentTime;
        public bool                  Finished;   // true once a non-looping anim has reached outPt
    }

    private readonly GridClient       _client;
    private readonly List<AnimEntry>  _active     = new();
    private readonly HashSet<UUID>    _requested  = new();   // IDs we've asked the server for
    private readonly object           _lock       = new();
    private readonly HashSet<string>  _loggedJointNames = new(StringComparer.Ordinal); // diagnostic: log each BVH joint name once
    private bool                      _disposed;

    // Last non-empty Advance() result — returned as a fallback when the active list is
    // momentarily empty (e.g. an asset is still downloading or an animation transition
    // leaves a one-frame gap between remove and add).  Also used as the ease-in source
    // so blending goes from the previous pose → new pose rather than T-pose → new pose.
    private Dictionary<string, Quaternion>? _lastDeltas;
    private Dictionary<string, float>         _lastMorphWeights = new(StringComparer.Ordinal);

    // ── Advance() zero-allocation reuse buffers ───────────────────────────────────
    // Ping-pong result/morph buffers so the dict we're filling never aliases
    // _lastDeltas (which may still be read by ease-in logic on the same or next tick).
    private bool                                                              _advanceUseA = true;
    private readonly Dictionary<string, Quaternion>                         _resultA     = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Quaternion>                         _resultB     = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float>                                _morphA      = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float>                                _morphB      = new(StringComparer.Ordinal);
    // Per-joint contribution lists: cleared each tick, pooled by joint name to avoid new List<> per joint.
    private readonly Dictionary<string, List<(Quaternion q, int priority)>> _contribs   = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(Quaternion q, int priority)>> _listPool   = new(StringComparer.Ordinal);
    // Lock-minimised snapshot buffer — AddRange under lock, iterate outside.
    private readonly List<AnimEntry>                                           _snapshotBuf = new();

    public AvatarAnimationPlayer(GridClient client)
    {
        _client = client;
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot of one active animation's metadata — used for diagnostics.
    /// </summary>
    internal readonly struct ActiveAnimInfo
    {
        public UUID   Id           { get; init; }
        public float  CurrentTime  { get; init; }
        public float  Length       { get; init; }
        public float  InPoint      { get; init; }
        public float  OutPoint     { get; init; }
        public bool   Loop         { get; init; }
        public int    Priority     { get; init; }
        public int    JointCount   { get; init; }
        /// <summary>All joint names present in the BVH (with rotation keys).</summary>
        public string[] JointNames { get; init; }
    }

    /// <summary>
    /// Returns a diagnostic snapshot of all currently active animations.
    /// </summary>
    public IReadOnlyList<ActiveAnimInfo> GetActiveAnimationInfo()
    {
        List<AnimEntry> snapshot;
        lock (_lock) snapshot = new List<AnimEntry>(_active);

        var result = new List<ActiveAnimInfo>(snapshot.Count);
        foreach (var e in snapshot)
        {
            var r = e.Reader;
            var names = new List<string>();
            if (r.joints != null)
                foreach (var j in r.joints)
                    if (j.rotationkeys != null && j.rotationkeys.Length > 0)
                        names.Add(j.Name);

            result.Add(new ActiveAnimInfo
            {
                Id          = e.Id,

                CurrentTime = e.CurrentTime,

                InPoint     = r.InPoint,
                OutPoint    = r.OutPoint,
                Loop        = r.Loop,
                Priority    = r.Priority,
                JointCount  = r.joints?.Length ?? 0,
                JointNames  = names.ToArray(),
                Length      = r.Length,
            });
        }
        return result;
    }

    /// <summary>
    /// Replace the current active animation set.
    /// Already-cached animations are activated immediately; uncached ones are downloaded.
    /// </summary>
    public void SetActiveAnimations(IEnumerable<UUID> animIds)
    {
        if (_disposed) return;

        lock (_lock)
        {
            // Build the desired set.
            var desired = new HashSet<UUID>(animIds);

            // Remove animations that are no longer playing.
            _active.RemoveAll(e => !desired.Contains(e.Id));

            // Add new animations.
            foreach (var id in desired)
            {
                if (_active.Exists(e => e.Id == id)) continue;

                BinBVHAnimationReader? reader;
                lock (s_cacheLock) s_cache.TryGetValue(id, out reader);

                if (reader != null)
                {
                    _active.Add(new AnimEntry { Id = id, Reader = reader, CurrentTime = PickStartTime(reader, id) });
                }
                else if (_requested.Add(id))
                {
                    var capturedId = id;
                    _ = Task.Run(async () =>
                    {
                        var asset = await _client.Assets.RequestAssetAsync(capturedId, AssetType.Animation, false);
                        OnAnimationReceived(capturedId, asset);
                    });
                }
            }
        }
    }

    /// <summary>
    /// Advance all active animations by <paramref name="dt"/> seconds and return
    /// the winning per-joint rotation delta for this frame.
    ///
    /// Implements the priority-based blending from SL viewer llagentanimations.cpp /
    /// LLVOAvatar::updateMotions: the animation with the highest declared priority wins
    /// each joint slot; ties favour the entry that was added first.
    /// </summary>
    /// <param name="dt">Delta time in seconds.</param>
    /// <param name="morphWeights">
    /// Output dictionary mapping morph-param name (e.g. "Blink_Left", "Express_Smile")
    /// to the blended weight [0,1] driven by the active animations this frame.
    /// Morphs not referenced by any active animation are absent from the dictionary.
    /// </param>
    /// <returns>
    /// Dictionary mapping joint name → quaternion delta to apply on top of the T-pose
    /// rotation (<c>localRot = tposeRot * delta</c>).
    /// Returns an empty dictionary when no animations are active.
    /// </returns>
    public Dictionary<string, Quaternion> Advance(float dt,
        out Dictionary<string, float> morphWeights)
    {
        // Flip ping-pong so we never clear the dict that _lastDeltas still points at.
        _advanceUseA = !_advanceUseA;
        var result   = _advanceUseA ? _resultA : _resultB;
        var morphBuf = _advanceUseA ? _morphA  : _morphB;
        result.Clear();
        morphBuf.Clear();

        // Return all used contribution lists to the pool and reset the contributions map.
        foreach (var kv in _contribs) { kv.Value.Clear(); _listPool[kv.Key] = kv.Value; }
        _contribs.Clear();

        // Take a lock-minimised snapshot into the reuse buffer.
        _snapshotBuf.Clear();
        lock (_lock) _snapshotBuf.AddRange(_active);

        // Alias locals for readability — same names as before so the loop body is unchanged.
        var contributions = _contribs;
        morphWeights      = morphBuf;

        foreach (var entry in _snapshotBuf)
        {
            var reader = entry.Reader;

            // Advance time.
            entry.CurrentTime += dt;

            float inPt  = reader.InPoint;
            float outPt = reader.OutPoint > inPt ? reader.OutPoint : reader.Length;

            if (reader.Loop)
            {
                float span = outPt - inPt;
                if (span > 1e-4f && entry.CurrentTime > outPt)
                    entry.CurrentTime = inPt + (entry.CurrentTime - inPt) % span;
            }
            else
            {
                if (entry.CurrentTime >= outPt)
                {
                    entry.CurrentTime = outPt;
                    entry.Finished    = true;
                }
            }

            float t = entry.CurrentTime;

            // Ease-in / ease-out scale factor (mirrors LLKeyframeMotion::onUpdate).
            // Finished non-looping animations hold their final pose at full weight —
            // no ease-out — so the avatar stays in the end pose rather than snapping
            // back to T-pose.
            float easeScale = 1f;
            if (!entry.Finished)
            {
                if (reader.EaseInTime > 1e-4f && t < inPt + reader.EaseInTime)
                    easeScale = Math.Clamp((t - inPt) / reader.EaseInTime, 0f, 1f);
                else if (reader.EaseOutTime > 1e-4f && t > outPt - reader.EaseOutTime)
                    easeScale = Math.Clamp((outPt - t) / reader.EaseOutTime, 0f, 1f);
            }

            // Per-joint keyframe evaluation.
            foreach (var joint in reader.joints ?? [])
            {
                if (joint.rotationkeys == null || joint.rotationkeys.Length == 0) { continue; }

                int jointPriority = Math.Max(reader.Priority, joint.Priority);

                // Discard if a higher-priority animation already contributed to this joint.
                if (contributions.TryGetValue(joint.Name, out var existing) && existing.Count > 0
                    && existing[0].priority > jointPriority)
                {
                    continue;
                }

                var (li, ni) = FindKeyPair(joint.rotationkeys, t);
                var lk = joint.rotationkeys[li];
                var nk = joint.rotationkeys[ni];

                float frac = 0f;
                if (ni != li)
                {
                    float span = nk.time - lk.time;
                    if (span > 1e-6f)
                        frac = Math.Clamp((t - lk.time) / span, 0f, 1f);
                }

                // Lerp the xyz components of the unit quaternion (matches legacy RenderAvatar).
                float lx = lk.key_element.X + frac * (nk.key_element.X - lk.key_element.X);
                float ly = lk.key_element.Y + frac * (nk.key_element.Y - lk.key_element.Y);
                float lz = lk.key_element.Z + frac * (nk.key_element.Z - lk.key_element.Z);

                // Reconstruct unit quaternion (SL stores xyz; w = sqrt(max(0, 1 − x²−y²−z²))).
                float wsq = 1f - lx * lx - ly * ly - lz * lz;
                float w   = wsq > 0f ? MathF.Sqrt(wsq) : 0f;
                var   q   = new Quaternion(lx, ly, lz, w);

                // Apply ease-in/ease-out by slerping toward the previous frame's pose
                // for this joint (falling back to identity when no prior data exists).
                // Blending from the last known rotation instead of from T-pose means
                // animation transitions go previous-pose → new-pose, not T-pose → new-pose.
                if (easeScale < 1f)
                {
                    var easeFrom = (_lastDeltas != null && _lastDeltas.TryGetValue(joint.Name, out var prev))
                        ? prev : Quaternion.Identity;
                    q = Quaternion.Slerp(easeFrom, q, easeScale);
                }

                // If a same-priority contribution already exists, clear the list
                // (we accumulate; average is computed below).
                if (existing != null && existing.Count > 0 && existing[0].priority < jointPriority)
                    existing.Clear();

                if (!contributions.TryGetValue(joint.Name, out existing))
                {
                    // Borrow a pre-cleared list from the pool; allocate only on first encounter.
                    if (!_listPool.TryGetValue(joint.Name, out existing))
                        existing = new List<(Quaternion, int)>();
                    contributions[joint.Name] = existing;
                }
                existing.Add((q, jointPriority));
            }

            // ── Facial / hand morph weights ────────────────────────────────────────
            // SL BVH animations encode morph-param weights using position keys on
            // "virtual" joints whose names are the morph param names (e.g. "Blink_Left",
            // "Hands_Fist_L", "Express_Smile").  The X component of the position key at
            // the current time is the morph weight, mapped from [-0.5,1.5] by the BVH
            // decoder — so the raw value is already a [0,1]-clamped weight.
            // Reference: LLVOAvatar::updateVisualParams / LLKeyframeMotion::onUpdate
            // in the SL C++ viewer source.
            foreach (var joint in reader.joints ?? [])
            {
                if (joint.positionkeys == null || joint.positionkeys.Length == 0) continue;

                // Morph joints are those whose names do NOT start with "m" (bone joints
                // start with "m" by SL convention, e.g. "mHead", "mEyeLeft").
                if (joint.Name.Length > 0 && joint.Name[0] == 'm') continue;

                var (li2, ni2) = FindKeyPair(joint.positionkeys, t);
                var lk2 = joint.positionkeys[li2];
                var nk2 = joint.positionkeys[ni2];

                float frac2 = 0f;
                if (ni2 != li2)
                {
                    float span2 = nk2.time - lk2.time;
                    if (span2 > 1e-6f)
                        frac2 = Math.Clamp((t - lk2.time) / span2, 0f, 1f);
                }

                float morphW = lk2.key_element.X + frac2 * (nk2.key_element.X - lk2.key_element.X);
                morphW = Math.Clamp(morphW * easeScale, 0f, 1f);

                // Accumulate: take the maximum weight among all active animations for
                // each morph name (mirrors SL viewer additive-blending with clamp).
                if (!morphWeights.TryGetValue(joint.Name, out var prev) || morphW > prev)
                    morphWeights[joint.Name] = morphW;
            }
        }

        // Blend same-priority contributions for each joint using iterative slerp averaging.
        // Mirrors LLKeyframeMotion blending in the SL viewer: multiple animations at the
        // same priority level contribute equally, averaged via successive slerp at 1/N weight.
        foreach (var (jointName, list) in contributions)
        {
            if (list.Count == 0) continue;
            var blended = list[0].q;
            for (int i = 1; i < list.Count; i++)
                blended = Quaternion.Slerp(blended, list[i].q, 1f / (i + 1));
            result[jointName] = blended;
        }

        // Persist the last non-empty result so we can hold the pose during
        // animation-transition gaps and use it as the ease-in source.
        if (result.Count > 0)
        {
            _lastDeltas       = result;
            _lastMorphWeights = morphBuf;
        }
        else if (_lastDeltas != null)
        {
            // No animations active this frame — return the last known pose so the
            // avatar holds its position rather than snapping back to T-pose while
            // a new animation asset is still downloading.
            morphWeights = _lastMorphWeights;
            return _lastDeltas;
        }

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Chooses a starting playback time for a loop animation based on the animation
    /// UUID, spreading different animations across their cycle so Veles doesn't always
    /// land on the same initial keyframe.
    ///
    /// Rationale: the SL viewer has typically been running for minutes or hours before
    /// you view an avatar, so every loop animation on that avatar is at a pseudo-random
    /// phase relative to its <see cref="BinBVHAnimationReader.InPoint"/>.  Veles
    /// previously always started at InPoint, which caused high-amplitude AO poses
    /// (e.g. an elbow bent 96° at t≈2s of a 30s cycle) to be visible on every fresh
    /// load — producing the cross-weighted-vertex spike that is only occasionally
    /// visible in the SL viewer.
    ///
    /// Non-looping animations (one-shots) are always started from InPoint so they
    /// play through in full.
    /// </summary>
    private static float PickStartTime(BinBVHAnimationReader reader, UUID id)
    {
        float inPt  = reader.InPoint;
        float outPt = reader.OutPoint > inPt ? reader.OutPoint : reader.Length;
        float span  = outPt - inPt;

        if (!reader.Loop || span < 1e-4f)
            return inPt;

        // Mix the UUID bytes into a deterministic but well-distributed offset
        // using a Knuth multiplicative hash on the first 8 bytes of the UUID.
        var   b    = id.GetBytes();
        ulong seed = (ulong)(uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24))
                   | ((ulong)(uint)(b[4] | (b[5] << 8) | (b[6] << 16) | (b[7] << 24)) << 32);
        seed = (seed ^ (seed >> 33)) * 0xff51afd7ed558ccdUL;
        seed = (seed ^ (seed >> 33)) * 0xc4ceb9fe1a85ec53UL;
        seed ^= seed >> 33;

        float phase = (seed & 0xFFFFFF) / (float)0x1000000; // [0, 1)
        return inPt + phase * span;
    }

    private void OnAnimationReceived(UUID id, Asset? asset)
    {
        if (_disposed) return;
        if (asset?.AssetData == null) return;

        BinBVHAnimationReader reader;
        try { reader = new BinBVHAnimationReader(asset.AssetData); }
        catch { return; }

        lock (s_cacheLock) s_cache.TryAdd(id, reader);

        lock (_lock)
        {
            // Only start playing if it's still requested.
            if (!_requested.Contains(id)) return;
            if (_active.Exists(e => e.Id == id)) return;
            _active.Add(new AnimEntry { Id = id, Reader = reader, CurrentTime = PickStartTime(reader, id) });
        }
    }

    /// <summary>
    /// Return the last-frame / next-frame index pair that brackets time <paramref name="t"/>.
    /// Both indices are clamped to valid range.
    /// </summary>
    private static (int last, int next) FindKeyPair(binBVHJointKey[] keys, float t)
    {
        int last = 0;
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i].time > t) break;
            last = i;
        }
        int next = Math.Min(last + 1, keys.Length - 1);
        return (last, next);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _disposed = true;
        lock (_lock)
        {
            _active.Clear();
            _requested.Clear();
        }
    }
}
