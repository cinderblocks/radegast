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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using OpenMetaverse.Rendering;

namespace Radegast.Veles.Rendering;

/// <summary>
/// The result of <see cref="AvatarMeshBuilder.BuildAsync"/>.
/// Bundles the GPU-ready submission with the CPU-side skinning data
/// required to animate the avatar each frame.
/// </summary>
internal sealed record AvatarBuildResult(
    /// <summary>Geometry + textures ready for the GPU submission path.</summary>
    PrimRenderSubmission               Submission,

    /// <summary>Per-face skin weight arrays, indexed in the same order as <see cref="Submission.Faces"/>.</summary>
    AvatarFaceSkinData[]               SkinData,

    /// <summary>The avatar skeleton definition (joint hierarchy, T-pose rotations).</summary>
    LindenAvatarDefinition?            AvatarDef,

    /// <summary>VP-morphed bone transforms (position + scale) from the last build.</summary>
    Dictionary<string, BoneTransform>? BoneTransforms,

    /// <summary>T-pose bone world matrices, one per named joint. Null when the definition failed to load.</summary>
    Dictionary<string, Matrix4x4>?     TposeBoneWorldMatrices,

    /// <summary>
    /// VP-deformed bone transforms used by AnimTick when a face has
    /// <see cref="AvatarFaceSkinData.UseVpBoneTransforms"/> set (rigged / fitted mesh).
    /// Separate from <see cref="BoneTransforms"/> so body-mesh LBS can stay VP-less
    /// (body VP deformation comes from mesh morphs, not bone scale).
    /// </summary>
    Dictionary<string, BoneTransform>? FittedBoneTransforms,

    /// <summary>
    /// Animation-driven (facial expression, hand-pose) morph data for each body-mesh
    /// face that has at least one dynamic morph entry.
    /// Empty when no dynamic morphs were found in the .llm files.
    /// </summary>
    ImmutableArray<AvatarFaceMorphData> FaceMorphData);
