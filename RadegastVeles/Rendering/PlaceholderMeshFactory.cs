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

using System.Numerics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Generates a cheap axis-aligned box <see cref="PrimRenderSubmission"/> that can
/// be submitted to the viewport immediately while the real tessellation is still
/// running in the background.
/// <para>
/// The box uses the prim's sim-space scale and position so the placeholder occupies
/// roughly the correct footprint from frame one.  When <see cref="SceneObjectStreamer"/>
/// finishes its async build it calls <see cref="GlViewportControl.SubmitSceneObject"/>
/// again with the real geometry, replacing the placeholder in-place with no visible gap.
/// </para>
/// </summary>
internal static class PlaceholderMeshFactory
{
    // Placeholder tint: a slightly desaturated white so it is visually distinct
    // from fully-textured objects.  Matching the SL viewer's "grey box" convention.
    private static readonly Vector4 PlaceholderColor = new(0.55f, 0.55f, 0.55f, 1f);

    // Unit-cube corners (±0.5 on each axis, matching SL's default prim scale of 0.5 m
    // before the scale matrix is applied).
    private static readonly Vector3[] s_corners =
    [
        new(-0.5f, -0.5f, -0.5f), // 0
        new( 0.5f, -0.5f, -0.5f), // 1
        new( 0.5f,  0.5f, -0.5f), // 2
        new(-0.5f,  0.5f, -0.5f), // 3
        new(-0.5f, -0.5f,  0.5f), // 4
        new( 0.5f, -0.5f,  0.5f), // 5
        new( 0.5f,  0.5f,  0.5f), // 6
        new(-0.5f,  0.5f,  0.5f), // 7
    ];

    // Each face: corner indices (CCW winding), outward normal.
    private static readonly (int a, int b, int c, int d, Vector3 normal)[] s_facesDef =
    [
        (0, 1, 2, 3,  Vector3.UnitZ * -1), // -Z (bottom)
        (4, 7, 6, 5,  Vector3.UnitZ),       // +Z (top)
        (0, 4, 5, 1,  Vector3.UnitY * -1), // -Y (front)
        (3, 2, 6, 7,  Vector3.UnitY),       // +Y (back)
        (0, 3, 7, 4,  Vector3.UnitX * -1), // -X (left)
        (1, 5, 6, 2,  Vector3.UnitX),       // +X (right)
    ];

    /// <summary>
    /// Builds a single-prim placeholder box for <paramref name="rootPrimLocalId"/>.
    /// </summary>
    /// <param name="label">Debug label (e.g. "ph:&lt;localId&gt;").</param>
    /// <param name="scale">Prim scale in metres (from <c>Primitive.Scale</c>).</param>
    /// <param name="worldPosition">Prim sim-space position.</param>
    /// <param name="rootPrimLocalId">LocalID, used for pick / touch routing.</param>
    public static PrimRenderSubmission Build(
        string label,
        Vector3 scale,
        Vector3 worldPosition,
        uint rootPrimLocalId)
    {
        var faces = new PrimRenderFace[6];

        for (int fi = 0; fi < 6; fi++)
        {
            var (a, b, c, d, normal) = s_facesDef[fi];

            // 4 verts × 8 floats (pos3 + norm3 + uv2).
            var verts = new float[4 * 8];
            int[] ci = [a, b, c, d];
            for (int vi = 0; vi < 4; vi++)
            {
                var corner = s_corners[ci[vi]] * scale;
                int o = vi * 8;
                verts[o    ] = corner.X;
                verts[o + 1] = corner.Y;
                verts[o + 2] = corner.Z;
                verts[o + 3] = normal.X;
                verts[o + 4] = normal.Y;
                verts[o + 5] = normal.Z;
                // UVs — simple planar mapping, not critical for a placeholder.
                verts[o + 6] = vi == 0 || vi == 3 ? 0f : 1f;
                verts[o + 7] = vi < 2 ? 0f : 1f;
            }

            // Two triangles: 0-1-2 and 0-2-3.
            ushort[] indices = [0, 1, 2, 0, 2, 3];

            var transform = Matrix4x4.CreateTranslation(worldPosition);

            faces[fi] = new PrimRenderFace
            {
                Vertices    = verts,
                Indices     = indices,
                Color       = PlaceholderColor,
                PrimLocalId = rootPrimLocalId,
                FaceIndex   = fi,
                Transform   = transform,
                Centroid    = worldPosition,
            };
        }

        var halfScale = scale * 0.5f;
        return new PrimRenderSubmission
        {
            Label     = label,
            Faces     = faces,
            BoundsMin = worldPosition - halfScale,
            BoundsMax = worldPosition + halfScale,
        };
    }
}
