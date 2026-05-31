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
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;
using TkMatrix4    = OpenTK.Mathematics.Matrix4;
using TkVector4    = OpenTK.Mathematics.Vector4;
using TkQuaternion = OpenTK.Mathematics.Quaternion;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Runs a 30 Hz LBS animation loop for a single avatar in the scene viewer.
/// One instance is created per avatar by <see cref="SceneAvatarAnimationStreamer"/>
/// whenever <see cref="SceneAvatarStreamer.AvatarBuilt"/> fires.
/// </summary>
internal sealed class SceneAvatarAnimator : IDisposable
{
    private readonly GridClient          _client;
    private readonly uint                _localId;   // avatar local ID
    private readonly uint                _sceneKey;  // key used in SubmitSceneObject
    private readonly GlViewportControl   _viewport;
    private readonly AvatarBuildResult   _buildResult;
    private readonly AvatarAnimationPlayer _player;

    // Pre-allocated bone matrices buffer (ComputeAnimatedBoneWorldMatrices target).
    private readonly Dictionary<string, TkMatrix4> _animBonesBuffer    = new(StringComparer.Ordinal);
    // Pre-allocated buffer for fitted/VP-bone path (avoids per-tick new Dictionary).
    private readonly Dictionary<string, TkMatrix4>    _vpAnimBonesBuffer = new(StringComparer.Ordinal);
    // Ping-pong buffers for flexi-attachment bone matrices: AnimTick writes into the inactive
    // buffer then publishes it via _attachBonesPublished so FlexiPrimAnimator's timer thread
    // always reads a stable, fully-written snapshot — no allocation needed each tick.
    private readonly Dictionary<string, TkMatrix4>    _attachBonesPing   = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TkMatrix4>    _attachBonesPong   = new(StringComparer.Ordinal);
    private bool                                       _attachUsePing     = true;
    // The last fully-written attach-bones snapshot, published atomically after each tick write.
    private volatile Dictionary<string, TkMatrix4>?   _attachBonesPublished;
    // Pre-computed combined skin matrix (invBind * animMat) per named bone for the body/2-bone path.
    private readonly Dictionary<string, TkMatrix4>    _skinMatByName     = new(StringComparer.Ordinal);
    // Inverted T-pose bone world matrices — computed once at construction, reused every tick.
    private readonly Dictionary<string, TkMatrix4>    _invBindMatrices   = new(StringComparer.Ordinal);

    private FlexiPrimAnimator? _flexi;

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public SceneAvatarAnimator(GridClient client, uint localId, uint sceneKey,
        GlViewportControl viewport, AvatarBuildResult buildResult)
    {
        _client      = client;
        _localId     = localId;
        _sceneKey    = sceneKey;
        _viewport    = viewport;
        _buildResult = buildResult;
        _player      = new AvatarAnimationPlayer(client);

        // Precompute inverted T-pose world matrices once — reused every AnimTick.
        // AvatarViewerViewModel does the same inversion after each build; doing it
        // here avoids per-tick TkMatrix4.Invert calls inside the hot skinning loop.
        if (buildResult.TposeBoneWorldMatrices != null)
        {
            foreach (var kv in buildResult.TposeBoneWorldMatrices)
                _invBindMatrices[kv.Key] = TkMatrix4.Invert(kv.Value);
        }

        // Seed with currently active animations so the first tick is not blank.
        SeedAnimations();
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    public void UpdateAnimations(IEnumerable<UUID> animIds)
        => _player.SetActiveAnimations(animIds);

    public void SetFlexiAnimator(FlexiPrimAnimator? flexi)
        => _flexi = flexi;

    /// <summary>
    /// Updates the world-placement matrix on the flexi attachment animator so
    /// flexi prims follow the avatar as it moves around the region.
    /// Called from <see cref="SceneAvatarAnimationStreamer"/> on each terse update.
    /// </summary>
    public void UpdateAvatarWorldMatrix(TkMatrix4 world)
        => _flexi?.SetExternalTransform(world);

    public void Start()
        => _ = RunAsync(_cts.Token);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _player.Dispose();
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private void SeedAnimations()
    {
        var sim = _client.Network.CurrentSim;
        if (sim == null) return;

        if (_localId == _client.Self.LocalID)
        {
            _player.SetActiveAnimations(_client.Self.SignaledAnimations.Keys);
        }
        else if (sim.ObjectsAvatars.TryGetValue(_localId, out var av) && av.Animations != null)
        {
            var ids = new List<UUID>(av.Animations.Count);
            foreach (var a in av.Animations) ids.Add(a.AnimationID);
            _player.SetActiveAnimations(ids);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 30.0));
        var sw   = System.Diagnostics.Stopwatch.StartNew();
        float prev = 0f;
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                float now = (float)sw.Elapsed.TotalSeconds;
                float dt  = Math.Min(now - prev, 0.1f);
                prev = now;
                AnimTick(dt);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void AnimTick(float dt)
    {
        if (_disposed) return;

        var sd        = _buildResult.SkinData;
        var avatarDef = _buildResult.AvatarDef;
        var vpBt      = _buildResult.BoneTransforms;
        var invBind   = _invBindMatrices;

        // Flexi attachments only need avatarDef + vpBt — they do NOT depend on rigged
        // skin data, fitted bone transforms, or the inverse-bind dictionary.  If we
        // gate the bone-provider push on those (as the rigged-skin path below must),
        // an avatar that has no rigged faces (or whose skin data is still building)
        // will never receive an AttachBoneProvider and its flexi attachments will
        // sit at the static T-pose AttachTransform forever.  Push the provider
        // unconditionally as soon as the skeleton is available.
        IReadOnlyDictionary<string, TkQuaternion>? liveDeltas = null;
        Dictionary<string, float>?                 morphWeights = null;

        if (_flexi != null && avatarDef != null && vpBt != null)
        {
            liveDeltas = _player.Advance(dt, out morphWeights);
            // Write into the currently-inactive ping-pong buffer.
            _attachUsePing = !_attachUsePing;
            var writeTarget = _attachUsePing ? _attachBonesPing : _attachBonesPong;
            AvatarMeshBuilder.ComputeAttachmentBoneWorldMatrices(
                avatarDef, vpBt, liveDeltas, writeTarget);
            // Publish the fully-written snapshot; FlexiPrimAnimator reads via the captured reference.
            _attachBonesPublished = writeTarget;
            var published = _attachBonesPublished; // local capture for the closure
            _flexi.SetBoneProvider(name => published!.TryGetValue(name, out var m) ? m : TkMatrix4.Identity);
        }

        if (sd == null || sd.Length == 0 || avatarDef == null || vpBt == null || invBind.Count == 0)
            return;

        // Advance the animation player exactly once per tick.  If the flexi branch
        // above already consumed this tick's deltas, reuse them; otherwise pull now.
        if (liveDeltas == null)
            liveDeltas = _player.Advance(dt, out morphWeights);

        AvatarMeshBuilder.ComputeAnimatedBoneWorldMatrices(avatarDef, vpBt, liveDeltas, _animBonesBuffer);
        var animBones = _animBonesBuffer;

        // Use pre-allocated _vpAnimBonesBuffer; pass live liveDeltas so attachments animate.
        Dictionary<string, TkMatrix4>? vpAnimBones = null;
        var fittedBt = _buildResult.FittedBoneTransforms;
        if (fittedBt != null)
        {
            foreach (var skin in sd)
            {
                if (skin.UseVpBoneTransforms)
                {
                    _vpAnimBonesBuffer.Clear();
                    AvatarMeshBuilder.ComputeAttachmentBoneWorldMatrices(
                        avatarDef, fittedBt, liveDeltas, _vpAnimBonesBuffer);
                    vpAnimBones = _vpAnimBonesBuffer;
                    break;
                }
            }
        }

        // Fix 3a: precompute combined skin matrix (invBind * animMat) for every named bone
        // so the body/2-bone vertex loop does 1 lookup + 1 TransformRow instead of 2 + 2.
        _skinMatByName.Clear();
        foreach (var kv in invBind)
        {
            if (animBones.TryGetValue(kv.Key, out var anim))
                _skinMatByName[kv.Key] = kv.Value * anim;
        }

        foreach (var skin in sd)
        {
            // Rigged / fitted mesh with per-face inverse bind matrices.
            if (skin.JointNames != null && skin.InvBindMatrices != null
                && skin.Joints != null && skin.Weights != null)
            {
                var bonesMap   = (skin.UseVpBoneTransforms && vpAnimBones != null)
                    ? vpAnimBones : animBones;
                var joints     = skin.JointNames;
                var ibms       = skin.InvBindMatrices;
                int jointCount = joints.Length;

                // Fix 3b: precompute per-joint skin matrix (invBind[ji] * animMat) before the
                // vertex loop — replaces 1 dict lookup + 2 TransformRow with 1 array read + 1 TransformRow.
                TkMatrix4[] skinMats = ArrayPool<TkMatrix4>.Shared.Rent(jointCount);
                bool[]      hasSkin  = ArrayPool<bool>.Shared.Rent(jointCount);
                for (int ji = 0; ji < jointCount; ji++)
                {
                    if (bonesMap.TryGetValue(joints[ji], out var bm))
                    { skinMats[ji] = ibms[ji] * bm; hasSkin[ji] = true; }
                    else hasSkin[ji] = false;
                }

                int     nvR    = skin.BindVerts.Length / 8;
                float[] nvBufR = ArrayPool<float>.Shared.Rent(skin.BindVerts.Length);

                for (int vi = 0; vi < nvR; vi++)
                {
                    int o  = vi * 8;
                    var bp = new TkVector4(skin.BindVerts[o],     skin.BindVerts[o + 1],
                                           skin.BindVerts[o + 2], 1f);
                    var bn = new TkVector4(skin.BindVerts[o + 3], skin.BindVerts[o + 4],
                                           skin.BindVerts[o + 5], 0f);
                    var ap = TkVector4.Zero;
                    var an = TkVector4.Zero;
                    float totalW = 0f;

                    for (int infl = 0; infl < 4; infl++)
                    {
                        int   ji = skin.Joints! [vi * 4 + infl];
                        float w  = skin.Weights![vi * 4 + infl];
                        if (w <= 1e-4f) continue;
                        if ((uint)ji >= (uint)jointCount) continue;
                        if (!hasSkin[ji]) { ap += w * bp; an += w * bn; totalW += w; continue; }
                        var sp = TkVector4.TransformRow(bp, skinMats[ji]);
                        var sn = TkVector4.TransformRow(bn, skinMats[ji]);
                        ap += w * sp; an += w * sn; totalW += w;
                    }

                    if (totalW <= 1e-4f) { ap = bp; an = bn; }
                    nvBufR[o]     = ap.X; nvBufR[o + 1] = ap.Y; nvBufR[o + 2] = ap.Z;
                    nvBufR[o + 3] = an.X; nvBufR[o + 4] = an.Y; nvBufR[o + 5] = an.Z;
                    nvBufR[o + 6] = skin.BindVerts[o + 6];
                    nvBufR[o + 7] = skin.BindVerts[o + 7];
                }

                _viewport.ScheduleSceneVertexUpdate(_sceneKey, skin.FaceIndex, nvBufR, skin.BindVerts.Length, isPoolRented: true);
                // nvBufR ownership transferred to viewport queue; it will be returned to ArrayPool after GL upload.
                ArrayPool<TkMatrix4>.Shared.Return(skinMats);
                ArrayPool<bool>.Shared.Return(hasSkin);
                continue;
            }

            if (skin.Bone1.Length == 0) continue;

            int     nv    = skin.BindVerts.Length / 8;
            float[] nvBuf = ArrayPool<float>.Shared.Rent(skin.BindVerts.Length);

            for (int vi = 0; vi < nv; vi++)
            {
                int o  = vi * 8;
                var bp = new TkVector4(skin.BindVerts[o],     skin.BindVerts[o + 1],
                                       skin.BindVerts[o + 2], 1f);
                var bn = new TkVector4(skin.BindVerts[o + 3], skin.BindVerts[o + 4],
                                       skin.BindVerts[o + 5], 0f);
                var ap = TkVector4.Zero;
                var an = TkVector4.Zero;

                var   b1 = skin.Bone1[vi];
                float w1 = skin.Weight1[vi];
                // Fix 3a applied: single lookup into _skinMatByName (= invBind * animMat)
                if (w1 > 1e-4f && _skinMatByName.TryGetValue(b1, out var sm1))
                {
                    ap += w1 * TkVector4.TransformRow(bp, sm1);
                    an += w1 * TkVector4.TransformRow(bn, sm1);
                }
                else { ap += w1 * bp; an += w1 * bn; }

                float w2 = skin.Weight2[vi];
                if (w2 > 1e-4f)
                {
                    var b2 = skin.Bone2[vi];
                    if (_skinMatByName.TryGetValue(b2, out var sm2))
                    {
                        ap += w2 * TkVector4.TransformRow(bp, sm2);
                        an += w2 * TkVector4.TransformRow(bn, sm2);
                    }
                    else { ap += w2 * bp; an += w2 * bn; }
                }

                nvBuf[o]     = ap.X; nvBuf[o + 1] = ap.Y; nvBuf[o + 2] = ap.Z;
                nvBuf[o + 3] = an.X; nvBuf[o + 4] = an.Y; nvBuf[o + 5] = an.Z;
                nvBuf[o + 6] = skin.BindVerts[o + 6];
                nvBuf[o + 7] = skin.BindVerts[o + 7];
            }

            _viewport.ScheduleSceneVertexUpdate(_sceneKey, skin.FaceIndex, nvBuf, skin.BindVerts.Length, isPoolRented: true);
            // nvBuf ownership transferred to viewport queue; it will be returned to ArrayPool after GL upload.
        }
    }
}
