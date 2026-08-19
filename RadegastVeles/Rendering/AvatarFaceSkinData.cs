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
/// Per-face CPU skinning data for linear blend skinning animation.
/// Parallel arrays indexed by vertex number; empty arrays when a face has no skin weights.
/// </summary>
internal sealed class AvatarFaceSkinData
{
    /// <summary>Index into the face array of the parent <see cref="PrimRenderSubmission"/>.</summary>
    public int FaceIndex;

    /// <summary>
    /// T-pose vertex data (stride 12 floats: X,Y,Z, NX,NY,NZ, U,V, TX,TY,TZ, TW).
    /// These are world-space positions frozen at build time — the bind pose.
    /// </summary>
    public float[] BindVerts = [];

    /// <summary>Primary bone name per vertex.</summary>
    public string[] Bone1 = [];

    /// <summary>Primary bone weight per vertex (0–1).</summary>
    public float[] Weight1 = [];

    /// <summary>Secondary bone name per vertex.</summary>
    public string[] Bone2 = [];

    /// <summary>Secondary bone weight per vertex (0–1).</summary>
    public float[] Weight2 = [];

    /// <summary>
    /// True for a rigid attachment bound entirely to one bone with weight 1.0 and no second
    /// influence (see AvatarMeshBuilder's rigid-attachment branch, which sets this at the point
    /// it already knows the face is rigid rather than making AnimTick/ApplyPendingSubmission
    /// re-derive it by scanning Bone1/Weight2). Every vertex on such a face gets the identical
    /// invBind[Bone1] * animBones[Bone1] product, so LBS reduces to one whole-face transform --
    /// AnimTick skips per-vertex GPU/CPU skinning entirely for these and drives
    /// ISingleObjectViewport.ScheduleFaceTransformUpdate instead, avoiding per-vertex skinning
    /// dispatch for faces that don't need it -- an avatar with many rigid attachments (rings,
    /// hair strands, jewelry) can register hundreds of faces, well past VkSkinDeformer's
    /// MaxJobsPerFrame, starving whichever faces the dispatch cap leaves behind each frame.
    /// Only ever true on the 2-bone body/attachment path (JointNames == null) -- a rigged/fitted
    /// mesh face is never marked rigid here even if its own weights happen to be single-influence.
    /// </summary>
    public bool IsRigidSingleBone;

    // ── Rigged / fitted mesh path ────────────────────────────────────────────
    // When <see cref="JointNames"/> is non-null, AnimTick uses the 4-bone-influence
    // skinning path with per-face inverse bind matrices supplied by the mesh asset.
    // <see cref="Bone1"/>/<see cref="Bone2"/> and related arrays are ignored.

    /// <summary>Joint names referenced by this face's weights (from the mesh asset's skin section).</summary>
    public string[]? JointNames;

    /// <summary>
    /// Per-joint inverse bind matrix (row-major, row-vector), parallel to <see cref="JointNames"/>.
    /// Already combined with the mesh's <c>BindShapeMatrix</c> so that
    /// <c>v_anim = v_bind × invBind × animBone</c> works directly on the bind-space vertex.
    /// </summary>
    public Matrix4x4[]? InvBindMatrices;

    /// <summary>
    /// Interleaved joint indices: <c>Joints[vi * 4 + k]</c> is influence k of vertex vi.
    /// </summary>
    public int[]? Joints;

    /// <summary>
    /// Interleaved weights: <c>Weights[vi * 4 + k]</c> is weight k of vertex vi.
    /// </summary>
    public float[]? Weights;

    /// <summary>
    /// If true, AnimTick uses VP-deformed bone transforms when computing animBones for this face.
    /// Required for fitted mesh so body-shape visual parameters expand/contract the mesh.
    /// Body-mesh faces leave this false — VP deformation there comes from mesh morphs, not bones.
    /// </summary>
    public bool UseVpBoneTransforms;

    /// <summary>
    /// GPU compute resources for this face, assigned on the render thread after upload
    /// (<c>VkViewportControl.ApplyPendingSubmission</c>/<c>UploadSceneObjectNoRebuild</c>). Null
    /// until registered; AnimTick falls back to the CPU path for any tick where GpuData is null
    /// or disposed. Write-once by the registering call, read from the animation background
    /// thread -- <c>volatile</c> gives the necessary happens-before visibility.
    /// </summary>
    internal volatile VkAvatarSkinGpuData? GpuData;
}
