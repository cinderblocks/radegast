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
using SkiaSharp;

namespace Radegast.Veles.Rendering;

/// <summary>
/// A single particle ready for GPU upload.
/// Positions are in object (emitter-relative) space.
/// </summary>
public struct ParticleVertex
{
    /// <summary>Emitter-relative position.</summary>
    public Vector3 Position;

    /// <summary>Interpolated RGBA colour (linear [0,1]).</summary>
    public Vector4 Color;

    /// <summary>Half-extents of the billboard quad (metres).</summary>
    public float HalfW;
    public float HalfH;

    /// <summary>Glow / emissive intensity [0,1].</summary>
    public float Glow;
}

/// <summary>
/// A complete snapshot of particle data for one emitter, ready to be uploaded to the GPU.
/// Transferred from the simulation thread to the GL thread via
/// <see cref="GlViewportControl.SubmitParticles"/>.
/// </summary>
public sealed class ParticleRenderSubmission
{
    /// <summary>Emitter world transform (translation contains the emitter world position).</summary>
    public required Matrix4x4   EmitterTransform { get; init; }

    /// <summary>Particle data.  May be empty when the emitter is active but spawns nothing yet.</summary>
    public required ParticleVertex[] Particles { get; init; }

    /// <summary>Optional pre-uploaded particle texture (may be null for a solid-colour system).</summary>
    public          SKBitmap?   Texture { get; init; }

    /// <summary>
    /// OpenGL-compatible source blend factor (matches <c>Primitive.ParticleSystem.BlendFunc</c>).
    /// Defaults to SRC_ALPHA.
    /// </summary>
    public          int BlendSrc { get; init; } = (int)Silk.NET.OpenGL.BlendingFactor.SrcAlpha;

    /// <summary>Defaults to ONE_MINUS_SRC_ALPHA.</summary>
    public          int BlendDst { get; init; } = (int)Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha;
}
