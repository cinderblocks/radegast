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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Quaternion = System.Numerics.Quaternion;
using Vector3    = System.Numerics.Vector3;
using Vector4    = System.Numerics.Vector4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Simulates flexi-prim spine physics at ~30 Hz and pushes deformed vertex
/// buffers to a <see cref="GlViewportControl"/> via a caller-supplied delegate.
///
/// <para>
/// The algorithm mirrors <c>LLVolumeImplFlexible::doFlexibleUpdate()</c> in the
/// SL C++ viewer (indra/llprimitive/llvolume.cpp).  The spine is modelled as a
/// chain of <c>N</c> segments; each segment carries a position and velocity that
/// is advanced every tick using a semi-implicit Euler step:
/// <list type="bullet">
///   <item>Spring / tension force pulls each segment toward the prim rest axis.</item>
///   <item>Gravity (negative Z in prim-local space) and user-defined force act on each segment.</item>
///   <item>Wind (from the simulator wind grid) adds a lateral impulse scaled by the
///         prim's <c>Wind</c> parameter.</item>
///   <item>Drag damps velocity.</item>
/// </list>
/// </para>
/// <para>
/// Vertex deformation: vertices are stored in prim-local space with Z ∈ [−0.5, 0.5]
/// (the path axis).  For each vertex we compute its normalised path parameter
/// <c>t</c> = Z + 0.5, look up the interpolated spine position and tangent
/// for that <c>t</c>, build a local-to-world rotation from the tangent, and apply
/// it in place of the original Z-axis transform so that the cross-section follows
/// the deformed path.
/// </para>
/// </summary>
internal sealed class FlexiPrimAnimator : IDisposable
{
    // ── Constants matching LLVolumeImplFlexible ──────────────────────────────────

    private const float SimTickRate = 1f / 30f;  // ~30 Hz

    /// <summary>
    /// When false, spring/gravity/wind spine physics are skipped — flexi prims stay rigid in
    /// their rest pose (or wherever simulation last left them, if this was just turned off
    /// mid-motion) rather than swaying. This does <em>not</em> stop the prim from being placed
    /// in world space: <see cref="TickAndUpload"/> still runs the placement step whenever the
    /// avatar/bone/world transform actually changes (and once unconditionally, the first time),
    /// just skipping the redundant re-upload otherwise. A flexi attachment on a stationary
    /// avatar with this off costs effectively nothing after its first tick.
    /// </summary>
    public static volatile bool AnimationEnabled = false;

    // ── Per-flexi-prim state ──────────────────────────────────────────────────────

    private sealed class FlexiState
    {
        public readonly FlexiPrimInfo Info;
        // Spine positions in PHYSICAL prim-local space (metres).
        // [0] is the fixed anchor at the bottom of the prim (−Scale.Z/2).
        // X/Y deflection is in metres; segment spacing along Z is Scale.Z/n.
        public readonly Vector3[] Positions;
        public readonly Vector3[] Velocities;
        // The attachTx last used to deform+upload this state's vertices, or null if it has
        // never been placed yet. Lets TickAndUpload skip the (comparatively expensive)
        // per-vertex deform/upload when physics simulation is disabled and nothing has moved,
        // while still catching every case where it actually needs to run: first placement,
        // and any avatar/bone/world-transform change even while frozen. See AnimationEnabled.
        public Matrix4x4? LastAttachTx;

        /// <param name="carryOver">
        /// Spine state cloned from the same underlying prim's previous <see cref="FlexiState"/>
        /// (matched by <see cref="LibreMetaverse.Primitive.LocalID"/> — see
        /// <c>FlexiPrimAnimator.CaptureCarryOverState</c>), reused verbatim instead of resetting
        /// to the rest pose when a rebuild (LOD change, draw-distance change, appearance rebake,
        /// tab-switch/GL reset, ...) replaces the animator for a still-present flexi prim. Null
        /// — or a length mismatch, e.g. the prim's Softness was edited and the segment count
        /// changed — falls back to the straight rest pose.
        /// </param>
        public FlexiState(FlexiPrimInfo info, (Vector3[] Positions, Vector3[] Velocities)? carryOver = null)
        {
            Info = info;
            int n    = info.PathSegments + 1;
            float sz = info.Scale.Z;

            if (carryOver is { } prior && prior.Positions.Length == n && prior.Velocities.Length == n)
            {
                Positions  = prior.Positions;
                Velocities = prior.Velocities;
                return;
            }

            Positions  = new Vector3[n];
            Velocities = new Vector3[n];
            // Rest pose: straight along +Z, anchor at −sz/2.
            for (int i = 0; i < n; i++)
                Positions[i] = new Vector3(0f, 0f, -sz * 0.5f + (float)i / info.PathSegments * sz);
        }
    }

    private readonly FlexiPrimInfo[]           _flexiPrims;
    private readonly FlexiState[]              _states;
    // CPU path: delivers a pre-deformed vertex buffer for one face to the viewport.
    // Args: faceIndex, buffer, logical length (buffer may be ArrayPool-oversized), isPoolRented.
    private readonly Action<int, float[], int, bool> _scheduleUpdate;
    // GPU compute path (optional): delivers spine positions + transform for GL dispatch.
    // When non-null and GpuData is set on the FlexiPrimInfo, the compute path is taken;
    // otherwise the CPU path is used as a fallback.
    private readonly Action<FlexiComputeJob>?  _scheduleCompute;
    private          CancellationTokenSource?  _cts;
    private          bool                      _disposed;

    // ── Constructor / lifecycle ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a new animator for the flexi prims in <paramref name="submission"/>.
    /// </summary>
    /// <param name="submission">The render submission whose <see cref="PrimRenderSubmission.FlexiPrims"/> will be animated.</param>
    /// <param name="scheduleUpdate">
    /// CPU-path delegate (always required as fallback), invoked each tick for each face of
    /// each flexi prim: face index, the deformed vertex buffer, its logical length (the
    /// buffer itself may be an ArrayPool-oversized rental), and whether it must be returned
    /// to <see cref="ArrayPool{T}.Shared"/> after use. Use
    /// <see cref="CreateSingleObjectScheduler"/> for PrimViewer / AvatarViewer, or a lambda
    /// wrapping <see cref="GlViewportControl.ScheduleSceneVertexUpdate"/> for the scene viewer.
    /// </param>
    /// <param name="scheduleCompute">
    /// Optional GPU-path delegate.  When non-null and <see cref="FlexiPrimInfo.GpuData"/>
    /// is set (registered after GL upload), spine positions are enqueued for compute-shader
    /// deformation instead of being processed on the CPU.
    /// </param>
    /// <param name="priorAnimator">
    /// The animator this one is replacing, if any (e.g. <c>SceneFlexiStreamer.StartAnimator</c>
    /// rebuilding a linkset/avatar after a LOD change, draw-distance change, appearance rebake,
    /// or tab-switch/GL reset). Spine state is carried over per-prim (matched by LocalID) instead
    /// of always starting at the straight rest pose, so a rebuild unrelated to the flexi prim's
    /// own motion doesn't visibly snap it back and make it re-settle. Safe to pass an already
    /// disposed animator — disposal doesn't clear its state arrays.
    /// </param>
    public FlexiPrimAnimator(PrimRenderSubmission submission, Action<int, float[], int, bool> scheduleUpdate,
        Action<FlexiComputeJob>? scheduleCompute = null, FlexiPrimAnimator? priorAnimator = null)
    {
        _flexiPrims      = submission.FlexiPrims;
        _scheduleUpdate  = scheduleUpdate;
        _scheduleCompute = scheduleCompute;

        var carryOver = priorAnimator?.CaptureCarryOverState();
        _states = new FlexiState[_flexiPrims.Length];
        for (int i = 0; i < _flexiPrims.Length; i++)
        {
            var fi = _flexiPrims[i];
            (Vector3[], Vector3[])? prior = carryOver != null &&
                carryOver.TryGetValue(fi.Prim.LocalID, out var p) ? p : null;
            _states[i] = new FlexiState(fi, prior);
        }
    }

    /// <summary>
    /// Snapshots this animator's current spine state, keyed by the owning prim's LocalID, for
    /// a replacement animator to carry over (see the <c>priorAnimator</c> constructor parameter).
    /// Arrays are cloned rather than handed over by reference so the outgoing animator's own
    /// physics thread — which may still have an in-flight <see cref="Tick"/> racing this capture,
    /// since disposal doesn't cancel a tick already in progress — can't mutate state the new
    /// animator has started reading. At worst that race yields one tick's stale/torn read; the
    /// damped spring physics self-corrects within a tick or two, so it isn't worth a lock.
    /// </summary>
    private Dictionary<uint, (Vector3[] Positions, Vector3[] Velocities)> CaptureCarryOverState()
    {
        var map = new Dictionary<uint, (Vector3[], Vector3[])>(_states.Length);
        foreach (var state in _states)
            map[state.Info.Prim.LocalID] = ((Vector3[])state.Positions.Clone(), (Vector3[])state.Velocities.Clone());
        return map;
    }

    /// <summary>
    /// Builds a CPU-path scheduler delegate for the single-object viewers (PrimViewer,
    /// AvatarViewer). Those route through <see cref="GlViewportControl.ScheduleVertexUpdate(int, ReadOnlySpan{float})"/>,
    /// which copies into an exact-size array itself, so the pooled buffer this animator
    /// rents can be returned immediately after that copy.
    /// </summary>
    public static Action<int, float[], int, bool> CreateSingleObjectScheduler(GlViewportControl vp)
        => (faceIndex, verts, vertsLength, isPoolRented) =>
        {
            vp.ScheduleVertexUpdate(faceIndex, verts.AsSpan(0, vertsLength));
            if (isPoolRented) ArrayPool<float>.Shared.Return(verts);
        };

    /// <summary>
    /// Starts this animator's own self-driven ~30 Hz loop. Used by PrimViewer / AvatarViewer,
    /// where each viewer owns exactly one flexi-carrying object and a dedicated timer per
    /// instance is cheap. Do not call this for scene-viewer objects — <see cref="SceneFlexiStreamer"/>
    /// instead registers the animator with a single shared <see cref="FlexiSceneScheduler"/> and
    /// drives it via <see cref="Tick"/>, so dozens/hundreds of flexi objects in a scene don't each
    /// spin up their own timer and background task.
    /// </summary>
    public void Start()
    {
        if (_flexiPrims.Length == 0) return;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    /// <summary>
    /// Wires a live animated-bone provider into every flexi prim info that has an
    /// <see cref="FlexiPrimInfo.AttachJointName"/>, so the attachment transform is
    /// recomputed from the current skeleton pose each tick instead of using the
    /// static T-pose baked at build time.
    /// </summary>
    /// <param name="provider">
    /// Function that maps a joint name to the current animated world matrix for
    /// that bone.  Should be <see cref="AvatarViewerViewModel._animBonesBuffer"/> or
    /// <see cref="AvatarViewerViewModel._vpAnimBonesBuffer"/> via a simple lambda.
    /// Pass <c>null</c> to clear the provider (e.g. on avatar viewer close).
    /// </param>
    public void SetBoneProvider(Func<string, Matrix4x4>? provider)
    {
        foreach (var fi in _flexiPrims)
        {
            if (fi.AttachJointName != null)
                fi.AttachBoneProvider = provider;
        }
    }

    /// <summary>
    /// Updates the world-placement matrix applied after attachment recomputation.
    /// Call this whenever the avatar (or linkset) moves so flexi geometry follows
    /// the owner's world position rather than staying at the build-time location.
    /// Thread-safe: the value is read each tick inside <see cref="TickAndUpload"/>.
    /// </summary>
    public void SetExternalTransform(Matrix4x4 world)
    {
        foreach (var fi in _flexiPrims)
            fi.ExternalTransform = world;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Lets <see cref="FlexiSceneScheduler"/> prune a disposed animator from its registry
    /// instead of writing its throttle counter back — a plain "is it still registered" check
    /// isn't enough because disposal can race a <see cref="Tick"/> already in flight (see
    /// <c>SceneFlexiStreamer.RemoveAnimator</c>, which unregisters and disposes together).
    /// </summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Cheap proxy for "where is this flexi object right now" — the world-space translation
    /// baked into its first flexi prim's <see cref="FlexiPrimInfo.ExternalTransform"/>
    /// (kept current by <see cref="SetExternalTransform"/> / the avatar-follow fixes in
    /// <c>SceneAvatarStreamer</c>). Used by <see cref="FlexiSceneScheduler"/> to bucket
    /// animators into distance-based tick rates — not precise enough for anything else.
    /// </summary>
    internal Vector3 ApproximateWorldPosition =>
        _flexiPrims.Length > 0 ? _flexiPrims[0].ExternalTransform.Translation : Vector3.Zero;

    // ── Simulation loop ───────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SimTickRate));
        var sw   = Stopwatch.StartNew();
        float prev = 0f;
        bool loggedError = false;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                float now = (float)sw.Elapsed.TotalSeconds;
                float dt  = Math.Min(now - prev, 0.1f);
                prev = now;
                try
                {
                    Tick(dt);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // This loop is started via "_ = RunAsync(...)" (fire-and-forget, no
                    // observer). Previously an unhandled exception here would silently kill
                    // the whole loop forever: every flexi prim this animator owns freezes at
                    // whatever pose it last reached, with no error shown anywhere — the same
                    // failure mode already found and fixed in AvatarViewerViewModel.AnimTick's
                    // driving loop. Catch, report once (avoid log spam if it throws every
                    // tick), and keep ticking instead.
                    if (!loggedError)
                    {
                        loggedError = true;
                        LibreMetaverse.Logger.Error(
                            "FlexiPrimAnimator: Tick threw; flexi prims owned by this animator " +
                            "will be frozen until fixed.", ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Runs one simulation step for every flexi prim in this animator and pushes the
    /// deformed geometry to the viewport. Called at ~30 Hz either by this instance's own
    /// loop (<see cref="Start"/>) or externally by <see cref="FlexiSceneScheduler"/>, which
    /// may pass a larger <paramref name="dt"/> when it has throttled this animator to less
    /// than every tick (see the scheduler's distance-based tick divisor).
    /// <para>
    /// <see cref="AnimationEnabled"/> gates the spine <em>physics</em> (spring/gravity/wind
    /// integration) — not whether the prim gets placed in world space at all. World placement
    /// (avatar/bone/world transform → vertex buffer) always runs via <see cref="TickAndUpload"/>;
    /// with animation disabled it just skips re-uploading when nothing has actually moved, so a
    /// flexi prim still tracks its avatar/linkset correctly while frozen instead of sitting
    /// wherever the mesh builder's raw local-space coordinates happened to leave it. See
    /// <see cref="FlexiState.LastAttachTx"/>.
    /// </para>
    /// </summary>
    public void Tick(float dt)
    {
        if (_disposed) return;
        foreach (var state in _states)
            TickAndUpload(state, dt, _scheduleUpdate, _scheduleCompute, simulate: AnimationEnabled);
    }

    // ── Simulation tick + vertex upload ──────────────────────────────────────────

    private static void TickAndUpload(FlexiState state, float dt,
        Action<int, float[], int, bool> scheduleUpdate, Action<FlexiComputeJob>? scheduleCompute,
        bool simulate)
    {
        var info   = state.Info;
        var flex   = info.Prim.Flexible!;
        int n      = info.PathSegments;  // number of segments; positions array has n+1 entries

        // Resolve the effective attachment transform for this tick.
        // If a live bone provider is wired (avatar attachment), recompute the full
        // prim-to-avatar-local matrix from the current animated bone so the flexi
        // prim follows the skeleton.  Otherwise use the baked static AttachTransform.
        var attachTx = info.AttachTransform;
        if (info.AttachJointName != null)
        {
            var provider = info.AttachBoneProvider;
            if (provider != null)
            {
                var boneMatrix = provider(info.AttachJointName);
                // Strip scale from the bone world matrix (same as AvatarBoneMath.StripScale).
                var r0 = Vector3.Normalize(new Vector3(boneMatrix.M11, boneMatrix.M12, boneMatrix.M13));
                var r1 = Vector3.Normalize(new Vector3(boneMatrix.M21, boneMatrix.M22, boneMatrix.M23));
                var r2 = Vector3.Normalize(new Vector3(boneMatrix.M31, boneMatrix.M32, boneMatrix.M33));
                var stripped = new Matrix4x4(
                    r0.X, r0.Y, r0.Z, 0f,
                    r1.X, r1.Y, r1.Z, 0f,
                    r2.X, r2.Y, r2.Z, 0f,
                    boneMatrix.M41, boneMatrix.M42, boneMatrix.M43, boneMatrix.M44);
                attachTx = info.PrimLocalMatrix
                         * Matrix4x4.CreateFromQuaternion(info.AttachJointRotation)
                         * Matrix4x4.CreateTranslation(info.AttachJointOffset)
                         * stripped;
            }
        }

        // Apply the external (world-placement) transform AFTER attachment recomputation
        // so it survives the dynamic-branch rebuild above.  For PrimViewer / AvatarViewer
        // this is Identity; for SceneViewer it carries the avatar / linkset world matrix.
        attachTx = attachTx * info.ExternalTransform;

        // ── Physics constants matching LLVolumeImplFlexible::doFlexibleUpdate() ───
        //
        // Simulation runs in PHYSICAL prim-local space (metres).
        // The prim's scale is (sx, sy, sz):
        //   • sz  = length along the path (Z axis).
        //   • segLen = sz / n  (metres per segment, matching SL's section_length).
        // After deformation the metre positions are divided back by scale before
        // writing to the VBO so the GPU's face.Transform (Scale × Rotation) lands
        // the vertices in the right place without double-scaling.
        //
        // World-space forces (gravity) are rotated into prim-local space via the
        // transpose of the rotation sub-matrix of attachTx.

        float sx = info.Scale.X;
        float sy = info.Scale.Y;
        float sz = info.Scale.Z;

        // Physics integration only runs when animation is enabled — see AnimationEnabled and
        // the Tick() doc comment. World placement below (WorldBounds + deform/upload) always
        // runs regardless, using whatever state.Positions currently holds (the rest pose if
        // simulation has never run, or the last-simulated pose if it was just turned off).
        if (simulate)
        {
            // Prim world-orientation axes (normalised rows of attachTx rotation block).
            var primRight = Vector3.Normalize(new Vector3(attachTx.M11, attachTx.M12, attachTx.M13));
            var primFwd   = Vector3.Normalize(new Vector3(attachTx.M21, attachTx.M22, attachTx.M23));
            var primUp    = Vector3.Normalize(new Vector3(attachTx.M31, attachTx.M32, attachTx.M33));

            // World gravity (0,0,-1) rotated into prim-local space.
            var wg = new Vector3(0f, 0f, -1f);
            var localGrav = new Vector3(
                Vector3.Dot(wg, primRight),
                Vector3.Dot(wg, primFwd),
                Vector3.Dot(wg, primUp));

            float segLen     = sz / n;                  // metres per spine segment
            float forceFactor = segLen * dt;             // SL: section_length * secondsThisFrame

            // Tension: SL uses  t_factor = tension*0.1 * (1 - 0.85^(dt*30)), capped at 0.1
            const float MaxTension = 0.1f;
            float tFactor = flex.Tension * 0.1f * (1f - MathF.Pow(0.85f, dt * 30f));
            if (tFactor > MaxTension) tFactor = MaxTension;

            // Air-friction momentum: momentum = 1 / 10^((drag*2+1)*dt)
            float frictionCoeff = MathF.Pow(10f, (flex.Drag * 2f + 1f) * dt);
            if (frictionCoeff < 1f) frictionCoeff = 1f;
            float momentum = 1f / frictionCoeff;

            // Wind (sinusoidal stand-in, in metres, prim-local XY).
            float windPhase  = (float)(Environment.TickCount64 * 0.001);
            float windFactor = flex.Wind * 0.1f * segLen * dt;
            float windX = MathF.Sin(windPhase * 0.7f) * windFactor;
            float windY = MathF.Cos(windPhase * 0.5f) * windFactor;

            // Per-tick position impulses (metres).
            var gravImpulse = localGrav  * (flex.Gravity * forceFactor);
            var userImpulse = new Vector3(flex.Force.X, flex.Force.Y, flex.Force.Z) * forceFactor;

            // ── Anchor (segment 0): fixed at bottom of prim ──────────────────────────
            state.Positions[0]  = new Vector3(0f, 0f, -sz * 0.5f);
            state.Velocities[0] = Vector3.Zero;
            var dir0 = Vector3.UnitZ;  // prim rest-axis direction

            for (int i = 1; i <= n; i++)
            {
                ref Vector3 pos = ref state.Positions[i];
                ref Vector3 vel = ref state.Velocities[i];
                var lastPos = pos;

                // Apply position impulses (SL style — forces directly displace position).
                pos += gravImpulse;
                pos += new Vector3(windX, windY, 0f);
                pos += userImpulse;

                // Tension toward parent segment direction.
                var parentPos = state.Positions[i - 1];
                var parentDir = (i == 1)
                    ? dir0
                    : Vector3.Normalize(state.Positions[i - 1] - state.Positions[i - 2]);
                var currentVec = pos - parentPos;
                var diff       = parentDir * segLen - currentVec;
                pos += diff * tFactor;

                // Inertia (carry-over velocity).
                pos += vel * momentum;

                // Clamp to segment length.
                var d = pos - parentPos;
                float dLen = d.Length();
                if (dLen > 1e-6f)
                    pos = parentPos + d * (segLen / dLen);

                // Velocity = positional displacement this tick.
                vel = pos - lastPos;
                if (vel.LengthSquared() > 1f) vel = Vector3.Normalize(vel);
            }
        }

        // Nothing left to do once simulation is disabled and attachTx hasn't changed since
        // the last time we placed this prim: WorldBounds and the vertex buffer are already
        // correct (the last-published WorldBounds stays valid — it was computed from this
        // same, unchanged, attachTx/state.Positions pair). Still runs on the first tick ever
        // (LastAttachTx is null) and on every tick where attachTx did change (avatar moved,
        // bone moved, seat/build correction, ...) even with physics simulation off, so a
        // frozen flexi prim still tracks its owner correctly instead of sitting wherever the
        // mesh builder's raw local-space coordinates happened to leave it. See AnimationEnabled.
        if (!simulate && state.LastAttachTx == attachTx)
            return;
        state.LastAttachTx = attachTx;

        // ── Publish a live world-space AABB for the frustum-culler ───────────────
        //
        // Flexi faces write their deformed vertices directly into the VBO and never
        // update PrimRenderFace.Transform, so GlViewportControl can't use the normal
        // cached-AABB × Transform cull test for them (see PrimRenderFace.IsFlexi). It
        // reads FlexiPrimInfo.WorldBounds instead, computed here from the spine —
        // exact for the centerline (spine positions are already in physical metres and
        // normalizing by scale before applying attachTx exactly undoes the scaling
        // AttachTransform re-applies, matching the per-vertex convention below), then
        // padded by the profile's worst-case half-diagonal so any cross-section vertex
        // (which the shader/CPU path additionally rotates away from the centerline by
        // the local spine tangent) is guaranteed to still land inside the box. A little
        // loose beats culling something that's actually on screen.
        {
            var spineWorldMin = new Vector3(float.MaxValue);
            var spineWorldMax = new Vector3(float.MinValue);
            for (int i = 0; i <= n; i++)
            {
                var sp = state.Positions[i];
                var spN = new Vector3(
                    sx > 1e-6f ? sp.X / sx : sp.X,
                    sy > 1e-6f ? sp.Y / sy : sp.Y,
                    sz > 1e-6f ? sp.Z / sz : sp.Z);
                var wp = Vector3.Transform(spN, attachTx);
                spineWorldMin = Vector3.Min(spineWorldMin, wp);
                spineWorldMax = Vector3.Max(spineWorldMax, wp);
            }
            float pad = 0.5f * MathF.Sqrt(sx * sx + sy * sy);
            var padVec = new Vector3(pad);
            info.WorldBounds = new FlexiWorldBounds
            {
                Min = spineWorldMin - padVec,
                Max = spineWorldMax + padVec,
            };
        }

        // ── Deform vertex buffers ────────────────────────────────────────────────
        //
        // GPU compute path: if GpuData is registered (set by GlViewportControl on the
        // GL thread after upload), pack the spine positions as a flat float[] and enqueue
        // a FlexiComputeJob.  The compute shader (flexi.comp) does the per-vertex math
        // in parallel directly on the GPU, writing into the mesh VBO.
        //
        // CPU fallback: identical logic to the GPU shader, used for the first frame or
        // two before GpuData is set, or permanently when compute is unavailable.
        if (scheduleCompute != null && info.GpuData is { IsDisposed: false } gpuData)
        {
            // Pack spine positions into a vec4 array (x,y,z,0 per segment).
            int spineCount  = n + 1;
            var spineFloats = new float[spineCount * 4];
            for (int i = 0; i < spineCount; i++)
            {
                var p = state.Positions[i];
                spineFloats[i * 4 + 0] = p.X;
                spineFloats[i * 4 + 1] = p.Y;
                spineFloats[i * 4 + 2] = p.Z;
                // [i*4+3] = 0 (zero-initialised)
            }
            scheduleCompute(new FlexiComputeJob(gpuData, spineFloats, attachTx));
            return;
        }

        // CPU path — mirrors the GPU shader exactly so the two paths produce
        // the same result for correctness during the GPU warm-up window.
        //
        // BaseVertices are raw prim-local (normalised) coordinates:
        //   X ∈ [≈-0.5, 0.5],  Y ∈ [≈-0.5, 0.5],  Z ∈ [-0.5, 0.5]
        //
        // Strategy:
        //   1. Convert base vertex to physical metres: (bx*sx, by*sy, bz*sz).
        //   2. Compute path parameter t from the physical Z.
        //   3. Look up the deformed spine position (metres) and tangent.
        //   4. Rotate the physical cross-section XY by the spine rotation.
        //   5. Write result back in NORMALISED coordinates (divide by scale)
        //      so AttachTransform's scale step produces the correct metre positions.
        for (int fi = 0; fi < info.FaceCount; fi++)
        {
            var src    = info.BaseVertices[fi];
            // Rented, not `new float[]`: this runs at ~30 Hz per face per flexi prim, and a
            // scene can have dozens of them, so a fresh allocation each tick is steady GC
            // pressure. ArrayPool.Rent returns an over-sized (next power-of-two) array, which
            // is why scheduleUpdate takes the true logical length (src.Length) as a separate
            // argument instead of relying on the buffer's own .Length — GlMesh.UpdateVertices
            // would otherwise upload the oversized tail as garbage vertex data.
            var dst    = ArrayPool<float>.Shared.Rent(src.Length);
            int vCount = src.Length / 12;

            for (int vi = 0; vi < vCount; vi++)
            {
                int o = vi * 12;

                float bxN = src[o];
                float byN = src[o + 1];
                float bzN = src[o + 2];

                float bxM = bxN * sx;
                float byM = byN * sy;
                float bzM = bzN * sz;

                float t    = Math.Clamp(bzM / sz + 0.5f, 0f, 1f);
                float segF = t * n;
                int   segI = Math.Min((int)segF, n - 1);
                float segT = segF - segI;

                var spineA = state.Positions[segI];
                var spineB = state.Positions[segI + 1];
                var spine  = Vector3.Lerp(spineA, spineB, segT);

                var d          = spineB - spineA;
                var splineTang = d.LengthSquared() > 1e-12f ? Vector3.Normalize(d) : Vector3.UnitZ;
                var rot        = RotationFromTo(Vector3.UnitZ, splineTang);

                var crossM   = new Vector3(bxM, byM, 0f);
                var rotCross = Vector3.TransformNormal(crossM, rot);

                float pxM = spine.X + rotCross.X;
                float pyM = spine.Y + rotCross.Y;
                float pzM = spine.Z + rotCross.Z;

                var normal    = new Vector3(src[o + 3], src[o + 4], src[o + 5]);
                var rotNormal = Vector3.TransformNormal(normal, rot);

                var tangentXyz  = new Vector3(src[o + 8], src[o + 9], src[o + 10]);
                var rotTangent  = Vector3.TransformNormal(tangentXyz, rot);

                float pxN = (sx > 1e-6f) ? pxM / sx : pxM;
                float pyN = (sy > 1e-6f) ? pyM / sy : pyM;
                float pzN = (sz > 1e-6f) ? pzM / sz : pzM;
                var p4 = Vector4.Transform(new Vector4(pxN, pyN, pzN, 1f), attachTx);
                var n4 = Vector4.Transform(new Vector4(rotNormal.X, rotNormal.Y, rotNormal.Z, 0f), attachTx);
                var t4 = Vector4.Transform(new Vector4(rotTangent.X, rotTangent.Y, rotTangent.Z, 0f), attachTx);
                dst[o]      = p4.X; dst[o + 1]  = p4.Y;        dst[o + 2]  = p4.Z;
                dst[o + 3]  = n4.X; dst[o + 4]  = n4.Y;        dst[o + 5]  = n4.Z;
                dst[o + 6]  = src[o + 6];                       // UV pass-through
                dst[o + 7]  = src[o + 7];
                dst[o + 8]  = t4.X; dst[o + 9]  = t4.Y;        dst[o + 10] = t4.Z;
                dst[o + 11] = src[o + 11];                      // handedness invariant
            }

            scheduleUpdate(info.FaceStart + fi, dst, src.Length, true);
        }
    }

    // ── Public helpers

    /// <summary>
    /// Returns the number of spine segments for a flexi prim.
    /// Matches <c>LLVolumeImplFlexible::getSegmentCount()</c>:
    /// <c>(softness + 1) * 10</c>.
    /// </summary>
    public static int ComputeSegmentCount(int softness) => (Math.Clamp(softness, 0, 3) + 1) * 10;

    // ── Math helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the shortest-arc rotation matrix that maps unit vector <paramref name="from"/>
    /// onto unit vector <paramref name="to"/>.
    /// </summary>
    private static Matrix4x4 RotationFromTo(Vector3 from, Vector3 to)
    {
        float dot = Vector3.Dot(from, to);
        if (dot >= 1f - 1e-6f)
            return Matrix4x4.Identity;

        if (dot <= -1f + 1e-6f)
        {
            // 180-degree flip — pick an arbitrary perpendicular axis.
            var perp = MathF.Abs(from.X) < 0.9f
                ? new Vector3(1f, 0f, 0f)
                : new Vector3(0f, 1f, 0f);
            var axis = Vector3.Normalize(Vector3.Cross(from, perp));
            return Matrix4x4.CreateFromAxisAngle(axis, MathF.PI);
        }

        var cross = Vector3.Cross(from, to);
        float s    = MathF.Sqrt((1f + dot) * 2f);
        float invS = 1f / s;
        var q = new Quaternion(
            cross.X * invS,
            cross.Y * invS,
            cross.Z * invS,
            s * 0.5f);
        return Matrix4x4.CreateFromQuaternion(q);
    }
}
