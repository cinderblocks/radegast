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

using LibreMetaverse.Rendering;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Per-face skinning data for a standalone rigged mesh (animesh) object.
/// Extracted at build time and stored in <see cref="PrimRenderSubmission.AnimeshSkinData"/>
/// for consumption by <see cref="SceneAnimeshAnimator"/> each tick.
/// </summary>
internal sealed class AnimeshFaceSkinData
{
    /// <summary>
    /// Zero-based index into <see cref="PrimRenderSubmission.Faces"/>.
    /// Used as the <c>faceOffset</c> argument of
    /// <see cref="GlViewportControl.ScheduleSceneVertexUpdate"/>.
    /// </summary>
    public required int FaceIndex { get; init; }

    /// <summary>
    /// Bind-space vertex data with <see cref="MeshSkinData.BindShapeMatrix"/> already applied.
    /// Stride 8: Position(3) + Normal(3) + TexCoord(2) per vertex.
    /// </summary>
    public required float[] BindVerts { get; init; }

    /// <summary>
    /// LMV mesh skin section (JointNames, InverseBindMatrices, BindShapeMatrix).
    /// Passed directly to <see cref="AnimeshSkinning.ComputeSkinningMatrices"/>.
    /// Shared across all faces of the same mesh asset; must not be mutated.
    /// </summary>
    public required MeshSkinData SkinData { get; init; }

    /// <summary>
    /// Per-vertex joint indices, 4 per vertex.
    /// Layout: <c>Joints[vi * 4 + infl]</c> — influence <c>infl</c> of vertex <c>vi</c>.
    /// Values are indices into <see cref="MeshSkinData.JointNames"/>.
    /// </summary>
    public required int[] Joints { get; init; }

    /// <summary>
    /// Per-vertex skin weights, 4 per vertex (normalized, sum ≤ 1 per vertex).
    /// Layout: <c>Weights[vi * 4 + infl]</c>.
    /// </summary>
    public required float[] Weights { get; init; }
}
