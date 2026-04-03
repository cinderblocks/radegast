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

using SkiaSharp;
using OpenTK.Mathematics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// CPU-side data for one rendered face of a prim, ready to be uploaded to the GPU.
/// Transferred from the VM thread to the GL thread via
/// <see cref="GlViewportControl.Submit"/>.
/// </summary>
public sealed class PrimRenderFace
{
    /// <summary>Interleaved floats: Position(3) + Normal(3) + TexCoord(2) per vertex.</summary>
    public required float[]  Vertices   { get; init; }

    /// <summary>Triangle indices into <see cref="Vertices"/>.</summary>
    public required ushort[] Indices    { get; init; }

    /// <summary>RGBA tint color (default: white / fully opaque).</summary>
    public          Vector4  Color      { get; init; } = Vector4.One;

    public          bool     Fullbright { get; init; }
    public          float    Glow       { get; init; }

    /// <summary>If true this face is rendered in the alpha pass.</summary>
    public          bool     HasAlpha   { get; init; }

    /// <summary>LocalID of the prim this face belongs to (for picking / touch).</summary>
    public          uint     PrimLocalId { get; init; }

    /// <summary>Zero-based face index within the prim (for picking / touch).</summary>
    public          int      FaceIndex   { get; init; }

    /// <summary>
    /// Optional texture bitmap. Consumed and disposed after the GL upload;
    /// callers must not use it afterward.
    /// </summary>
    public          SKBitmap? Texture   { get; init; }

    public          Matrix4   Transform { get; init; } = Matrix4.Identity;
}

/// <summary>
/// A complete set of faces for one object (prim or linkset), keyed by a caller-supplied label.
/// Submitting a new <see cref="PrimRenderSubmission"/> replaces any existing geometry.
/// </summary>
public sealed class PrimRenderSubmission
{
    /// <summary>Unique label — used for window title and future cache keying.</summary>
    public required string           Label     { get; init; }

    public required PrimRenderFace[] Faces     { get; init; }

    /// <summary>AABB minimum corner in object space (for auto-framing).</summary>
    public          Vector3          BoundsMin { get; init; } = Vector3.Zero;

    /// <summary>AABB maximum corner in object space (for auto-framing).</summary>
    public          Vector3          BoundsMax { get; init; } = Vector3.One;
}
