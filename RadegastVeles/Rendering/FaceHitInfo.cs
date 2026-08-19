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
/// Surface hit information computed during a face-pick ray-cast.
/// Mirrors the fields sent in the SL ObjectGrab / ObjectDeGrab SurfaceInfo block
/// (see LLPickInfo::getSurfaceInfo and send_ObjectGrab_message in the SL viewer).
/// </summary>
public readonly struct FaceHitInfo
{
    /// <summary>
    /// Texture-space UV at the hit point [0,1]×[0,1], with TE repeat/offset/rotate
    /// pre-baked (corresponds to SL's mUVCoords / ObjectGrab UVCoord).
    /// (-1,-1,0) when undetermined.
    /// </summary>
    public Vector3 UvCoord   { get; init; }

    /// <summary>
    /// Raw mesh UV at the hit point, equivalent to SL's mSTCoords / ObjectGrab STCoord.
    /// For Veles this is identical to <see cref="UvCoord"/> because the TE transform is
    /// pre-baked into the vertex buffer.  (-1,-1,0) when undetermined.
    /// </summary>
    public Vector3 StCoord   { get; init; }

    /// <summary>World-space intersection point (SL ObjectGrab Position).</summary>
    public Vector3 Position  { get; init; }

    /// <summary>World-space surface normal at the hit point (normalised).</summary>
    public Vector3 Normal    { get; init; }

    /// <summary>World-space binormal at the hit point (normalised).</summary>
    public Vector3 Binormal  { get; init; }

    /// <summary>Returns an instance with all fields at their "undetermined" defaults.</summary>
    public static FaceHitInfo Unknown => new()
    {
        UvCoord  = new Vector3(-1f, -1f, 0f),
        StCoord  = new Vector3(-1f, -1f, 0f),
        Position = Vector3.Zero,
        Normal   = Vector3.Zero,
        Binormal = Vector3.Zero,
    };
}
