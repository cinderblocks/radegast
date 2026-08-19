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
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Avalonia;

namespace Radegast.Veles.Rendering;

public interface ISceneViewport
{
    bool Wireframe { get; set; }
    bool SsaoEnabled { get; set; }
    bool ShadowsEnabled { get; set; }
    bool WaterReflectionsEnabled { get; set; }
    float WaterHeight { get; set; }
    bool AtmosphericsEnabled { get; set; }
    bool FrustumCullingEnabled { get; set; }
    bool ShowPerfOverlay { get; set; }

    /// <summary>Typed <see cref="IFrameStatsTracker"/>, not the concrete tracker type -- see
    /// that interface's own doc comment for why. <c>VkViewportControl</c> keeps its existing
    /// concrete-typed <c>Stats</c> property as its normal public API (unchanged) and
    /// additionally satisfies this member via explicit interface implementation.</summary>
    IFrameStatsTracker Stats { get; }
    Camera3D Camera { get; }
    Rect Bounds { get; }

    event Action<string>? InitFailed;
    /// <summary>Fires (on the UI thread) after the render backend finishes (re)initializing --
    /// on first open AND on every tab-switch re-attach. <c>VkViewportControl.OnDetachedFromLogicalTree</c>
    /// fully disposes per-panel scene state (<c>FreePanelResources</c>) and
    /// <c>OnAttachedToVisualTree</c> unconditionally re-runs <c>InitializeAsync</c> on every
    /// re-attach (no "already initialized" guard), so this fires every time a tab is switched
    /// back to. Subscribers use it to re-dirty/re-upload scene data against a freshly-ready
    /// viewport. Verified safe against a subscribe-after-fire race: every real consumer
    /// (<c>SceneViewerPanel</c>) is instantiated via an Avalonia <c>DataTemplate</c>, which sets
    /// <c>DataContext</c> -- triggering <c>SceneViewerViewModel.SetViewport</c>'s subscription --
    /// before the child viewport control is ever attached to the visual tree, so
    /// <c>InitializeAsync</c> cannot complete before the subscription exists.</summary>
    event Action? SceneReset;
    event Action<uint, int, FaceHitInfo>? FaceClicked;
    event Action<Vector3>? GroundClicked;
    event Action<bool>? MouselookChanged;

    Func<int, int, float?>? TerrainHeightProvider { get; set; }
    SceneLightStreamer? LightStreamer { get; set; }
    // See VkViewportControl.cs's own EnvironmentService property declaration for why this
    // member is wrapped in #if !VULKANSPIKE_BUILD (kept in lockstep with that class's own
    // conditional so the spike's VkViewportControl still satisfies this interface).
#if !VULKANSPIKE_BUILD
    SceneEnvironmentService? EnvironmentService { get; set; }
#endif
    bool MouselookActive { get; }
    void EnterMouselook();
    void ExitMouselook();

    void Submit(PrimRenderSubmission submission);
    void SubmitSceneObject(ulong sceneKey, PrimRenderSubmission submission);
    void RemoveSceneObject(ulong sceneKey);
    void ClearAllSceneObjects();
    void SetSceneObjectTransform(ulong sceneKey, Matrix4x4 transform);
    void SetSceneObjectMotion(ulong sceneKey, Vector3 scale, Quaternion rotation, Vector3 position,
        Vector3 velocity, Vector3 angularVelocity, Vector3 acceleration);
    void PatchSceneObjectTexture(SceneTexturePatch patch, CancellationToken ct = default);
    void ScheduleSceneVertexUpdate(uint rootId, int faceOffset, float[] verts, int vertsLength, bool isPoolRented = false);

    /// <summary>
    /// Replaces the model matrix of one face of a scene object for the next draw call --
    /// scene-object counterpart to <c>ISingleObjectViewport.ScheduleFaceTransformUpdate</c>.
    /// Used by <c>SceneAvatarAnimator</c>'s rigid-attachment fast path: a face bound entirely to
    /// one bone with weight 1.0 (<see cref="AvatarFaceSkinData.IsRigidSingleBone"/>) gets the
    /// identical <c>invBind * animBone</c> product on every vertex, so per-vertex GPU/CPU
    /// skinning is redundant -- one whole-face transform, read live at draw time via
    /// <c>face.Transform</c>, is equivalent and far cheaper. Same thread-safety contract as
    /// <see cref="ScheduleSceneVertexUpdate"/> -- queued and drained on the render thread, never
    /// written directly from the calling (background animation) thread.
    /// </summary>
    void ScheduleSceneFaceTransformUpdate(uint rootId, int faceOffset, Matrix4x4 transform);

    /// <summary>Enqueues a GPU compute-skinning dispatch for one avatar face, drained on the
    /// render thread by <c>VkViewportControl.RenderFrame</c>'s existing (previously inert)
    /// <c>VkSkinDeformer.DispatchPending</c> call. Safe to call from any thread -- backed by a
    /// <c>ConcurrentQueue</c>. No-op if compute is unavailable on this device.</summary>
    void ScheduleSkinCompute(VkSkinComputeJob job);

    /// <summary>Enqueues a GPU compute-deformation dispatch for one flexi prim, same
    /// thread-safety/drain contract as <see cref="ScheduleSkinCompute"/>.</summary>
    void ScheduleFlexiCompute(VkFlexiComputeJob job);

    void SubmitParticles(ulong key, ParticleRenderSubmission? sub);
    void RemoveParticles(ulong key);
    void RequestRender();
    void UpdateCameraFollow(Vector3 target);
    void SetCameraTarget(Vector3 target, float distance = -1f, float pitch = -1000f);
    void OrbitStep(float dyaw, float dpitch);
    void ZoomStep(float delta);

    /// <summary>Number of scene-object faces currently in the flat draw lists. Mirrors
    /// <c>VkViewportControl.SceneFaceCount</c>.</summary>
    int SceneFaceCount { get; }

    /// <summary>Number of scene-object uploads/removals still queued, not yet drained.
    /// Named to match <c>VkViewportControl.PendingSceneUploadCount</c>.</summary>
    int PendingSceneUploadCount { get; }

    /// <summary>Number of normal-priority scene texture patches still waiting to be drained.
    /// Mirrors <c>VkViewportControl.QueuedTexturePatchCount</c>.</summary>
    int QueuedTexturePatchCount { get; }

    /// <summary>Number of scene texture patches currently parked in a deferred-retry state.
    /// Mirrors <c>VkViewportControl.DeferredTexturePatchCount</c>.</summary>
    int DeferredTexturePatchCount { get; }

    /// <summary>Wall-clock time (ms) the last frame's <c>DrainPendingSceneObjects</c> call spent
    /// draining queued scene-object mesh/texture/skin-GPU-data uploads. See
    /// <c>VkViewportControl.DrainSceneObjectsMs</c>'s own doc comment for why this exists: the
    /// perf overlay's GPU timestamp only covers the main render pass, so a chronically-backlogged
    /// upload queue's cost is otherwise invisible except as an unexplained CPU/GPU gap.</summary>
    double DrainSceneObjectsMs { get; }

    /// <summary>Wall-clock time (ms) the last frame spent draining queued texture patches (both
    /// the single-submission and scene-object patch queues combined). See
    /// <see cref="DrainSceneObjectsMs"/>'s own doc comment for why this exists.</summary>
    double DrainTexturePatchesMs { get; }

    /// <summary>Wall-clock time (ms) the last frame's <c>VkSkinDeformer.DispatchPending</c> call
    /// spent -- a synchronous submit+wait, so its cost is baked into the frame's CPU ms with no
    /// visibility into how much of it this specific call accounts for. Added while scoping
    /// frame-in-flight pipelining: whether pipelining the main pass is worth it depends on how
    /// much of the frame these already-serialized deformer waits take up.</summary>
    double SkinDispatchMs { get; }

    /// <summary>Same as <see cref="SkinDispatchMs"/>, for <c>VkFlexiDeformer.DispatchPending</c>.</summary>
    double FlexiDispatchMs { get; }

    /// <summary>Wall-clock time (ms) the last frame spent draining queued per-face vertex/
    /// transform updates (CPU-LBS fallback, flexi CPU path). Unlike <see cref="DrainSceneObjectsMs"/>/
    /// <see cref="DrainTexturePatchesMs"/>, these drain loops have no time budget -- added
    /// alongside them to check whether that's where an otherwise-unexplained CPU-ms gap is
    /// going.</summary>
    double VertexUpdateDrainMs { get; }

    /// <summary>Wall-clock time (ms) spent recording the main pass this frame: particle drain,
    /// swapchain BeginDraw/image acquire, culling, every sub-pass (G-buffer/SSAO/shadow/water
    /// reflection), and all draw-call recording -- everything between the deformer dispatches
    /// and the final submit. See <see cref="MainPassSubmitWaitMs"/>'s doc comment for why this
    /// split exists.</summary>
    double MainPassRecordMs { get; }

    /// <summary>Wall-clock time (ms) spent in the main pass's own <c>cmd.Submit()</c> +
    /// <c>FreeUsedCommandBuffers()</c> call -- the literal CPU-side wait for the GPU to finish
    /// this frame. Added because a retest showed every OTHER instrumented bucket (drains,
    /// deformers, vertex updates) summing to a small fraction of multi-second total CPU ms;
    /// this pair (with <see cref="MainPassRecordMs"/>) narrows down whether the missing time is
    /// CPU-side recording or the GPU fence wait itself.</summary>
    double MainPassSubmitWaitMs { get; }

    /// <summary>Cumulative wall-clock time (ms), measured from the same clock as
    /// <see cref="MainPassRecordMs"/>, at the checkpoint right before the main pass's command
    /// buffer is created -- i.e. particle drain + swapchain BeginDraw/image acquire + depth
    /// target + culling (main/shadow/reflection frustum queries). Subtract from
    /// <see cref="SubPassMs"/> for the shadow/SSAO/water-reflection sub-pass recording cost, and
    /// from <see cref="MainPassRecordMs"/> for the main draw-call recording cost.</summary>
    double PreCullMs { get; }

    /// <summary>Cumulative wall-clock time (ms) at the checkpoint right before the main render
    /// pass begins -- i.e. after shadow/SSAO/water-reflection sub-pass recording. See
    /// <see cref="PreCullMs"/>'s doc comment for how to derive the three individual deltas.</summary>
    double SubPassMs { get; }

    /// <summary>Cumulative wall-clock time (ms) at the checkpoint right after
    /// <c>DrainPendingParticles</c> returns -- the first sub-phase inside the span
    /// <see cref="PreCullMs"/> measures.</summary>
    double ParticleDrainMs { get; }

    /// <summary>Cumulative wall-clock time (ms) at the checkpoint right after
    /// <c>VkInteropSwapchain.BeginDraw</c> returns. See <see cref="SwapchainFreeCmdBuffersMs"/>/
    /// <see cref="SwapchainBeginDrawCoreMs"/> for that call's own internal split.</summary>
    double BeginDrawMs { get; }

    /// <summary>How long the last <c>VkInteropSwapchain.BeginDraw</c> call's own
    /// <c>FreeUsedCommandBuffers()</c> took -- where the PREVIOUS frame's
    /// <c>VkInteropSwapchainImage.Present()</c> submit (never waited on within its own frame,
    /// by design) actually gets reaped. A backed-up compositor/present pipeline would show up
    /// here, one frame later than the Present call that triggered it.</summary>
    double SwapchainFreeCmdBuffersMs { get; }

    /// <summary>How long the last <c>VkInteropSwapchain.BeginDraw</c> call's own
    /// <c>BeginDrawCore</c> (swapchain image acquire) took.</summary>
    double SwapchainBeginDrawCoreMs { get; }

    /// <summary>Split of <see cref="MainPassSubmitWaitMs"/>: how long the main pass's own
    /// <c>cmd.Submit()</c> call took, i.e. time spent acquiring <c>VkCommandBufferPool</c>'s
    /// <c>_queueLock</c> plus the actual <c>vkQueueSubmit</c> call. Large here means lock
    /// contention with another submit in flight, not a GPU-side stall.</summary>
    double SubmitCallMs { get; }

    /// <summary>Split of <see cref="MainPassSubmitWaitMs"/>: how long the main pass's own
    /// <c>FreeUsedCommandBuffers()</c> call took -- the literal <c>WaitForFences</c> GPU wait.
    /// Large here is a genuine GPU/compositor-side stall (e.g. the DirectX keyed-mutex acquire
    /// blocking on the compositor), not a CPU-side lock.</summary>
    double FenceWaitMs { get; }

    /// <summary>How many <c>VkViewportControl</c> panels are alive process-wide right now. All
    /// panels share one <c>VkContext</c>/<c>VkQueue</c>, so &gt;1 during a <see cref="FenceWaitMs"/>
    /// spike means cross-panel queue contention is a live candidate.</summary>
    int LiveInstanceCount { get; }
}
