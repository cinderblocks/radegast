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

using System.Runtime.CompilerServices;
using System.Numerics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// View-frustum extraction and AABB intersection test.
/// <para>
/// Frustum planes are extracted directly from a <c>view * projection</c> matrix using
/// the Gribb-Hartmann method. Each plane is stored as <c>Vector4(nx, ny, nz, d)</c>
/// with the convention <c>nx·x + ny·y + nz·z + d &gt; 0</c> for points inside the frustum.
/// </para>
/// </summary>
public struct Frustum
{
    public Vector4 Left;
    public Vector4 Right;
    public Vector4 Bottom;
    public Vector4 Top;
    public Vector4 Near;
    public Vector4 Far;
}

/// <summary>Static frustum-AABB culling helpers.</summary>
public static class FrustumCuller
{
    /// <summary>
    /// Extracts the six clip-space planes from a combined <paramref name="viewProj"/> matrix.
    /// Planes are normalised so the <c>w</c> component is a true signed distance.
    /// </summary>
    /// <remarks>
    /// System.Numerics Matrix4x4 field names are Mrc (row, column). The Gribb-Hartmann
    /// derivation is expressed in math rows here.
    /// </remarks>
    public static Frustum ExtractPlanes(in Matrix4x4 viewProj)
    {
        // Rows of (V·P) as math vectors.
        var r0 = new Vector4(viewProj.M11, viewProj.M21, viewProj.M31, viewProj.M41);
        var r1 = new Vector4(viewProj.M12, viewProj.M22, viewProj.M32, viewProj.M42);
        var r2 = new Vector4(viewProj.M13, viewProj.M23, viewProj.M33, viewProj.M43);
        var r3 = new Vector4(viewProj.M14, viewProj.M24, viewProj.M34, viewProj.M44);

        Frustum f;
        f.Left   = Normalize(r3 + r0);
        f.Right  = Normalize(r3 - r0);
        f.Bottom = Normalize(r3 + r1);
        f.Top    = Normalize(r3 - r1);
        // Near uses r2 alone, NOT r3+r2: System.Numerics' CreatePerspectiveFieldOfView/
        // CreateOrthographicOffCenter (used everywhere a projection matrix is built in
        // this renderer) produce NDC z in [0,1] (D3D convention), where the near-plane
        // condition is z_clip >= 0, i.e. row2 alone. r3+r2 is the textbook Gribb-Hartmann
        // formula for OpenGL-style NDC z in [-1,1] and is wrong for this codebase's
        // matrices. For a typical camera frustum (near~0.1, far~1000) the wrong formula
        // degrades into an always-passing test, so it was silently harmless for camera/
        // SSAO/water culling; it is NOT harmless for a tight-near/far light frustum
        // (e.g. the shadow map's near=1/far=300 ortho volume), where it can reject real
        // geometry outright and leave the shadow map perpetually empty.
        f.Near   = Normalize(r2);
        f.Far    = Normalize(r3 - r2);
        return f;
    }

    /// <summary>
    /// Returns <c>true</c> when the AABB defined by <paramref name="min"/> /
    /// <paramref name="max"/> intersects the frustum, using the standard
    /// "positive vertex" optimisation. False negatives are impossible; false positives
    /// can occur for boxes that hug a frustum corner (acceptable for draw culling).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IntersectsAabb(in Frustum f, Vector3 min, Vector3 max)
    {
        return TestPlane(f.Left,   min, max)
            && TestPlane(f.Right,  min, max)
            && TestPlane(f.Bottom, min, max)
            && TestPlane(f.Top,    min, max)
            && TestPlane(f.Near,   min, max)
            && TestPlane(f.Far,    min, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TestPlane(Vector4 plane, Vector3 min, Vector3 max)
    {
        // Positive vertex: pick the corner farthest along the plane normal.
        float px = plane.X >= 0 ? max.X : min.X;
        float py = plane.Y >= 0 ? max.Y : min.Y;
        float pz = plane.Z >= 0 ? max.Z : min.Z;
        return plane.X * px + plane.Y * py + plane.Z * pz + plane.W >= 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 Normalize(Vector4 plane)
    {
        float len = (float)System.Math.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
        if (len <= 1e-8f) return plane;
        return plane / len;
    }
}
