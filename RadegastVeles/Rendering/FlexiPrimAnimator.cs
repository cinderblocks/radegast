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
    /// When false the physics loop still runs but skips all simulation and GPU upload work,
    /// leaving flexi prims frozen in their last-computed (or bind) pose.
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

        public FlexiState(FlexiPrimInfo info)
        {
            Info = info;
            int n    = info.PathSegments + 1;
            float sz = info.Scale.Z;
            Positions  = new Vector3[n];
            Velocities = new Vector3[n];
            // Rest pose: straight along +Z, anchor at −sz/2.
            for (int i = 0; i < n; i++)
                Positions[i] = new Vector3(0f, 0f, -sz * 0.5f + (float)i / info.PathSegments * sz);
        }
    }

    private readonly FlexiPrimInfo[]           _flexiPrims;
    // CPU path: delivers a pre-deformed vertex buffer for one face to the viewport.
    private readonly Action<int, float[]>      _scheduleUpdate;
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
    /// Delegate invoked each tick for each face of each flexi prim.  The first argument
    /// is the zero-based face index within the submission's <see cref="PrimRenderSubmission.Faces"/> array;
    /// the second is the pre-copied, deformed vertex buffer (ownership transferred to the delegate).
    /// Typically one of <see cref="GlViewportControl.ScheduleVertexUpdate(int, float[])"/> or a
    /// lambda wrapping <see cref="GlViewportControl.ScheduleSceneVertexUpdate"/>.
    /// </param>
    /// <param name="scheduleUpdate">CPU-path delegate (always required as fallback).</param>
    /// <param name="scheduleCompute">
    /// Optional GPU-path delegate.  When non-null and <see cref="FlexiPrimInfo.GpuData"/>
    /// is set (registered after GL upload), spine positions are enqueued for compute-shader
    /// deformation instead of being processed on the CPU.
    /// </param>
    public FlexiPrimAnimator(PrimRenderSubmission submission, Action<int, float[]> scheduleUpdate,
        Action<FlexiComputeJob>? scheduleCompute = null)
    {
        _flexiPrims      = submission.FlexiPrims;
        _scheduleUpdate  = scheduleUpdate;
        _scheduleCompute = scheduleCompute;
    }

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

    // ── Simulation loop ───────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        var states = new FlexiState[_flexiPrims.Length];
        for (int i = 0; i < _flexiPrims.Length; i++)
            states[i] = new FlexiState(_flexiPrims[i]);

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

                if (!AnimationEnabled) continue;

                foreach (var state in states)
                    TickAndUpload(state, dt, _scheduleUpdate, _scheduleCompute);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Simulation tick + vertex upload ──────────────────────────────────────────

    private static void TickAndUpload(FlexiState state, float dt,
        Action<int, float[]> scheduleUpdate, Action<FlexiComputeJob>? scheduleCompute)
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
                // Strip scale from the bone world matrix (same as AvatarMeshBuilder.StripScale).
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
            // Must be exactly src.Length: AvatarViewer / PrimViewer route this buffer
            // through GlMesh.UpdateVertices(float[]) which uploads verts.Length*sizeof(float)
            // bytes via glBufferSubData. ArrayPool.Rent returns an over-sized array
            // (next power-of-two bucket); a larger buffer triggers GL_INVALID_VALUE,
            // silently dropping the update and freezing the attachment in its bind pose.
            var dst    = new float[src.Length];
            int vCount = src.Length / 8;

            for (int vi = 0; vi < vCount; vi++)
            {
                int o = vi * 8;

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

                var d       = spineB - spineA;
                var tangent = d.LengthSquared() > 1e-12f ? Vector3.Normalize(d) : Vector3.UnitZ;
                var rot     = RotationFromTo(Vector3.UnitZ, tangent);

                var crossM   = new Vector3(bxM, byM, 0f);
                var rotCross = Vector3.TransformNormal(crossM, rot);

                float pxM = spine.X + rotCross.X;
                float pyM = spine.Y + rotCross.Y;
                float pzM = spine.Z + rotCross.Z;

                var normal    = new Vector3(src[o + 3], src[o + 4], src[o + 5]);
                var rotNormal = Vector3.TransformNormal(normal, rot);

                float pxN = (sx > 1e-6f) ? pxM / sx : pxM;
                float pyN = (sy > 1e-6f) ? pyM / sy : pyM;
                float pzN = (sz > 1e-6f) ? pzM / sz : pzM;
                var p4 = Vector4.Transform(new Vector4(pxN, pyN, pzN, 1f), attachTx);
                var n4 = Vector4.Transform(new Vector4(rotNormal.X, rotNormal.Y, rotNormal.Z, 0f), attachTx);
                dst[o]     = p4.X; dst[o + 1] = p4.Y; dst[o + 2] = p4.Z;
                dst[o + 3] = n4.X; dst[o + 4] = n4.Y; dst[o + 5] = n4.Z;
            }

            scheduleUpdate(info.FaceStart + fi, dst);
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
