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

using LibreMetaverse;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Position and normal delta for one vertex within a named morph target.
/// </summary>
internal readonly record struct FaceMorphVertex(
    uint    VertexIndex,
    Vector3 CoordDelta,
    Vector3 NormalDelta);

/// <summary>
/// One named morph target — a list of per-vertex deltas to accumulate when the morph
/// is active.  Matches a single <see cref="LindenMesh.Morph"/> entry from the .llm file.
/// </summary>
internal sealed class DynamicMorphEntry(string name, FaceMorphVertex[] vertices)
{
    /// <summary>Morph name as stored in the .llm file (e.g. "Blink_Left", "Hands_Fist").</summary>
    public string            Name     { get; } = name;
    /// <summary>Per-vertex deltas to apply when this morph has non-zero weight.</summary>
    public FaceMorphVertex[] Vertices { get; } = vertices;
}

/// <summary>
/// CPU-side dynamic morph data for one body-mesh face.
/// <para>
/// <see cref="BaseVerts"/> is a copy of the vertex buffer after all static
/// shape/wearable VP morphs have been baked in.  Each AnimTick,
/// <see cref="AvatarViewerViewModel"/> copies <see cref="BaseVerts"/> into
/// <see cref="WorkBuf"/>, accumulates the deltas from any active animation-driven
/// morphs (facial expressions, hand poses), and assigns <see cref="WorkBuf"/> to
/// <see cref="AvatarFaceSkinData.BindVerts"/> before running linear blend skinning.
/// </para>
/// </summary>
internal sealed class AvatarFaceMorphData
{
    /// <summary>Index into the face array of the parent <see cref="PrimRenderSubmission"/>.</summary>
    public int FaceIndex;

    /// <summary>
    /// Vertex buffer after static (shape-wearable) VP morphs, before any animation-driven
    /// morphs.  Stride 8 floats per vertex: X,Y,Z, NX,NY,NZ, U,V.
    /// </summary>
    public float[] BaseVerts = [];

    /// <summary>
    /// Reusable per-tick scratch buffer — same size as <see cref="BaseVerts"/>.
    /// AnimTick copies <see cref="BaseVerts"/> here, applies weighted morph deltas,
    /// then points <see cref="AvatarFaceSkinData.BindVerts"/> at this array.
    /// </summary>
    public float[] WorkBuf = [];

    /// <summary>
    /// Animation-driven (facial expression / hand-pose) morph entries available for this face.
    /// </summary>
    public DynamicMorphEntry[] Morphs = [];
}
