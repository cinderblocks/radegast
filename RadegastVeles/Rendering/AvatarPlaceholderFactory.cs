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
using System.Numerics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Generates an invisible ellipsoid placeholder for an avatar that has not yet been
/// downloaded and built. The visible "cloud avatar" effect used by the Second Life C++
/// viewer is produced entirely by <see cref="AvatarCloudDriver"/>'s particle burst; this
/// mesh exists only so the avatar has a pick/touch target and a world-space footprint
/// (for the spatial-culling grid, etc.) while it loads.
/// <para>
/// The ellipsoid is a UV-sphere scaled to roughly human proportions (0.6 m wide,
/// 0.35 m deep, 1.8 m tall) centred at hip height, alpha-blended at zero opacity.
/// </para>
/// <para>
/// When <see cref="SceneAvatarStreamer"/> finishes its async build it calls
/// <see cref="GlViewportControl.SubmitSceneObject"/> again with the real geometry,
/// silently replacing this placeholder.
/// </para>
/// </summary>
internal static class AvatarPlaceholderFactory
{
    // Fully transparent: this mesh is now only a pick/touch target and world-footprint
    // placeholder (feeds the spatial-culling grid, TryApplyTexturePatch's "1 face = still
    // placeholder" check, etc.) — the visible cloud effect is entirely AvatarCloudDriver's
    // particle burst now, not this mesh. See RadegastVeles/Rendering/AvatarCloudDriver.cs.
    private static readonly Vector4 CloudColor = new(1.00f, 1.00f, 1.00f, 0.00f);

    // Ellipsoid radii (half-extents) in avatar-local space.
    // X = left/right (shoulder width), Y = front/back depth, Z = height.
    // Centre is placed at hip height (Z = AvatarHeight * 0.55) so the cloud
    // sits visually at the avatar's mid-point, matching SL behaviour.
    private const float RadiusX     = 0.30f;   // half shoulder width
    private const float RadiusY     = 0.175f;  // half body depth
    private const float RadiusZ     = 0.90f;   // half total height (1.8 m avatar)
    private const float CentreZ     = RadiusZ; // foot-origin → centre at RadiusZ above ground

    // UV-sphere tessellation: 10 stacks × 16 slices gives 256 quads (512 tris),
    // which is cheap enough to produce for every un-loaded avatar.
    private const int Stacks = 10;
    private const int Slices = 16;

    /// <summary>
    /// Builds a single-face cloud-puff ellipsoid for <paramref name="avatarLocalId"/>.
    /// </summary>
    /// <param name="label">Debug label (e.g. "ph:av:&lt;localId&gt;").</param>
    /// <param name="worldPosition">Avatar foot position in sim space.</param>
    /// <param name="worldRotation">Avatar world rotation (yaw only).</param>
    /// <param name="avatarLocalId">LocalID for pick / touch routing.</param>
    public static PrimRenderSubmission Build(
        string     label,
        Vector3    worldPosition,
        Quaternion worldRotation,
        uint       avatarLocalId)
    {
        // ── Build UV-sphere vertices ──────────────────────────────────────────
        // Vertex layout: Position(3) + Normal(3) + TexCoord(2) + Tangent(4) = 12 floats each.
        // Tangent is left as zero (w=0) — the placeholder has no material so the screen-space
        // TBN fallback in prim.frag is never actually invoked for it.
        int vertCount = (Stacks + 1) * (Slices + 1);
        var verts     = new float[vertCount * 12];
        int vi        = 0;

        for (int stack = 0; stack <= Stacks; stack++)
        {
            float phi    = MathF.PI * stack / Stacks;           // 0 … π  (top → bottom)
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);

            for (int slice = 0; slice <= Slices; slice++)
            {
                float theta    = 2f * MathF.PI * slice / Slices; // 0 … 2π
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                // Unit-sphere normal (outward).
                float nx = sinPhi * cosTheta;
                float ny = sinPhi * sinTheta;
                float nz = cosPhi;

                // Scale to ellipsoid + shift centre up to CentreZ.
                float px = nx * RadiusX;
                float py = ny * RadiusY;
                float pz = nz * RadiusZ + CentreZ;

                float u = (float)slice / Slices;
                float v = (float)stack / Stacks;

                verts[vi++] = px;
                verts[vi++] = py;
                verts[vi++] = pz;
                verts[vi++] = nx;
                verts[vi++] = ny;
                verts[vi++] = nz;
                verts[vi++] = u;
                verts[vi++] = v;
                verts[vi++] = 0f; // tangent x (no tangent data for placeholder)
                verts[vi++] = 0f; // tangent y
                verts[vi++] = 0f; // tangent z
                verts[vi++] = 0f; // handedness 0 = no tangent
            }
        }

        // ── Build index buffer ────────────────────────────────────────────────
        int triCount = Stacks * Slices * 2;
        var indices  = new ushort[triCount * 3];
        int ii       = 0;

        for (int stack = 0; stack < Stacks; stack++)
        {
            for (int slice = 0; slice < Slices; slice++)
            {
                int a = stack       * (Slices + 1) + slice;
                int b = a           + (Slices + 1);
                int c = a           + 1;
                int d = b           + 1;

                // Two CCW triangles per quad.
                indices[ii++] = (ushort)a;
                indices[ii++] = (ushort)b;
                indices[ii++] = (ushort)c;
                indices[ii++] = (ushort)c;
                indices[ii++] = (ushort)b;
                indices[ii++] = (ushort)d;
            }
        }

        // ── World transform ───────────────────────────────────────────────────
        var worldMatrix = Matrix4x4.CreateFromQuaternion(worldRotation)
                        * Matrix4x4.CreateTranslation(worldPosition);

        var centre = worldPosition + new Vector3(0f, 0f, CentreZ);

        var face = new PrimRenderFace
        {
            Vertices    = verts,
            Indices     = indices,
            Color       = CloudColor,
            PrimLocalId = avatarLocalId,
            FaceIndex   = 0,
            Transform   = worldMatrix,
            Centroid    = centre,
            HasAlpha    = true,       // render in the alpha / transparent pass
            IsTwoSided  = true,       // no back-face culling so the cloud is visible from inside
            AlphaMode   = FaceAlphaMode.Blend,
        };

        var boundsMin = worldPosition + new Vector3(-RadiusX, -RadiusY, 0f);
        var boundsMax = worldPosition + new Vector3( RadiusX,  RadiusY, CentreZ + RadiusZ);

        return new PrimRenderSubmission
        {
            Label     = label,
            Faces     = [face],
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
        };
    }
}
