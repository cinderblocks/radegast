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

public interface ISingleObjectViewport
{
    bool Wireframe { get; set; }
    bool SsaoEnabled { get; set; }
    bool ShowSky { get; set; }
    SkySettings Sky { get; set; }
    Camera3D Camera { get; }

    event Action<string>? InitFailed;
    event Action<uint, int, FaceHitInfo>? FaceClicked;

    void Submit(PrimRenderSubmission submission);
    void SubmitFront(PrimRenderSubmission submission);
    void SubmitAvatarFront(PrimRenderSubmission submission);
    void ResetCamera();
    void ResetCameraFront();
    void OrbitStep(float dyaw, float dpitch);
    void ZoomStep(float delta);
    void PatchSubmissionTexture(SceneTexturePatch patch);
    void ScheduleVertexUpdate(int faceIndex, ReadOnlySpan<float> verts);

    /// <summary>Enqueues a GPU compute-skinning dispatch for one avatar face -- see
    /// <c>ISceneViewport.ScheduleSkinCompute</c>'s own doc comment for the full thread-safety/
    /// drain contract (identical here). Only <c>AvatarViewerViewModel</c> calls this today.</summary>
    void ScheduleSkinCompute(VkSkinComputeJob job);

    /// <summary>Enqueues a GPU compute-deformation dispatch for one flexi prim -- see
    /// <see cref="ScheduleSkinCompute"/>.</summary>
    void ScheduleFlexiCompute(VkFlexiComputeJob job);

    /// <summary>
    /// Replaces face <paramref name="faceIndex"/>'s model matrix for the next draw call. Used by
    /// <c>AvatarViewerViewModel.AnimTick</c>'s rigid-attachment path: a face bound entirely to
    /// one bone with weight 1.0 (<see cref="AvatarFaceSkinData.IsRigidSingleBone"/>) gets the
    /// identical <c>invBind * animBone</c> product on every vertex, so per-vertex GPU/CPU
    /// skinning is redundant work -- one whole-face transform, read live at draw time via
    /// <c>face.Transform</c>, is mathematically equivalent and far cheaper (no compute dispatch,
    /// no vertex buffer rewrite). Thread-safe/queued like <see cref="ScheduleVertexUpdate"/> --
    /// <c>Matrix4x4</c> is 64 bytes, not atomic, so this must NOT write <c>face.Transform</c>
    /// directly from the calling (background animation) thread; the implementation queues and
    /// drains on the render thread only, same contract as every other Schedule* method here.
    /// </summary>
    void ScheduleFaceTransformUpdate(int faceIndex, Matrix4x4 transform);
}
