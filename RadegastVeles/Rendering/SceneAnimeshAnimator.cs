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

using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using LibreMetaverse;
using LibreMetaverse.Animesh;
using LibreMetaverse.Rendering;
using Vector4 = System.Numerics.Vector4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// CPU-side linear-blend skinning for a single animesh (standalone rigged mesh) object.
/// One instance is created per visible animesh object by <see cref="SceneAnimeshStreamer"/>.
/// <see cref="AnimTick"/> is called once per frame after
/// <see cref="LibreMetaverse.Animesh.AnimeshManager.Update"/> has advanced the player.
/// </summary>
internal sealed class SceneAnimeshAnimator
{
    private readonly uint                  _sceneKey;
    private readonly GlViewportControl     _viewport;
    private readonly AnimeshPlayer         _player;
    private readonly LindenSkeleton        _skeleton;
    private readonly AnimeshFaceSkinData[] _skinFaces;

    public SceneAnimeshAnimator(
        uint sceneKey,
        GlViewportControl viewport,
        AnimeshPlayer player,
        LindenSkeleton skeleton,
        AnimeshFaceSkinData[] skinFaces)
    {
        _sceneKey  = sceneKey;
        _viewport  = viewport;
        _player    = player;
        _skeleton  = skeleton;
        _skinFaces = skinFaces;
    }

    /// <summary>
    /// Evaluate the current pose and push updated vertex buffers to the viewport.
    /// Called by <see cref="SceneAnimeshStreamer"/> once per tick, AFTER
    /// <see cref="AnimeshManager.Update"/> has advanced the player.
    /// </summary>
    public void AnimTick()
    {
        var pose = _player.EvaluatePose();

        // Cache skinning matrices keyed by MeshSkinData reference to avoid
        // recomputing the FK walk when multiple faces share the same skin section.
        var matCache = new Dictionary<MeshSkinData, Matrix4x4[]>(
            ReferenceEqualityComparer.Instance);

        foreach (var face in _skinFaces)
        {
            if (!matCache.TryGetValue(face.SkinData, out var skinMats))
            {
                var lmvMats = AnimeshSkinning.ComputeSkinningMatrices(
                    pose, _skeleton, face.SkinData);
                skinMats = new Matrix4x4[lmvMats.Length];
                for (int i = 0; i < lmvMats.Length; i++)
                    skinMats[i] = ToMatrix4x4(lmvMats[i]);
                matCache[face.SkinData] = skinMats;
            }

            int nv      = face.BindVerts.Length / 8;
            int needed  = face.BindVerts.Length;
            var nvBuf   = ArrayPool<float>.Shared.Rent(needed);
            int jCount  = skinMats.Length;

            for (int vi = 0; vi < nv; vi++)
            {
                int o  = vi * 8;
                var bp = new Vector4(
                    face.BindVerts[o],     face.BindVerts[o + 1],
                    face.BindVerts[o + 2], 1f);
                var bn = new Vector4(
                    face.BindVerts[o + 3], face.BindVerts[o + 4],
                    face.BindVerts[o + 5], 0f);

                var ap = Vector4.Zero;
                var an = Vector4.Zero;
                float tw = 0f;

                for (int infl = 0; infl < 4; infl++)
                {
                    int   ji = face.Joints [vi * 4 + infl];
                    float w  = face.Weights[vi * 4 + infl];
                    if (w <= 1e-4f || (uint)ji >= (uint)jCount) continue;
                    ref var m = ref skinMats[ji];
                    ap += w * Vector4.Transform(bp, m);
                    an += w * Vector4.Transform(bn, m);
                    tw += w;
                }

                // Fallback for degenerate weights: leave vertex at bind position.
                if (tw <= 1e-4f) { ap = bp; an = bn; }

                nvBuf[o    ] = ap.X; nvBuf[o + 1] = ap.Y; nvBuf[o + 2] = ap.Z;
                nvBuf[o + 3] = an.X; nvBuf[o + 4] = an.Y; nvBuf[o + 5] = an.Z;
                nvBuf[o + 6] = face.BindVerts[o + 6];
                nvBuf[o + 7] = face.BindVerts[o + 7];
            }

            _viewport.ScheduleSceneVertexUpdate(
                _sceneKey, face.FaceIndex, nvBuf, needed, isPoolRented: true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Matrix4x4 ToMatrix4x4(LibreMetaverse.Matrix4 m) =>
        new(m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);
}
