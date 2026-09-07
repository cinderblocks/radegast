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
    /// <summary>Volumetric god rays (screen-space light shafts toward the sun). High-tier-only
    /// and requires <see cref="SsaoEnabled"/> too (the mask stage reuses SSAO's G-buffer depth as
    /// its occlusion source) -- see <c>VkViewportControl.RenderTonemapChain</c>'s own god-ray
    /// stages for the full gate.</summary>
    bool GodRaysEnabled { get; set; }
    bool FrustumCullingEnabled { get; set; }
    /// <summary>Hardware occlusion-query culling for scene objects fully hidden behind other
    /// geometry (e.g. inside a closed room). Medium/High-tier-only and has no effect when
    /// <see cref="FrustumCullingEnabled"/> is false -- see
    /// <c>VkViewportControl.OcclusionCullingEnabled</c>'s own doc comment for the full gate.</summary>
    bool OcclusionCullingEnabled { get; set; }
    /// <summary>Screen-space reflections on low-roughness PBR surfaces (metal floors, glossy/wet
    /// materials) -- ray-marches the just-shaded opaque scene's own G-buffer depth to add a real
    /// reflection term on top of prim.frag's existing flat ambient-Fresnel specular. Default
    /// false: unlike SSAO/occlusion culling (which degrade gracefully), a wrong SSR
    /// implementation produces visually obvious ray-march artefacts, so this defaults off pending
    /// real-world visual validation. Medium/High-tier only (requires the split main pass's
    /// shared opaque snapshot -- see <c>VkViewportControl.RenderFrame</c>'s own
    /// <c>doOpaqueSnapshot</c> gate).</summary>
    bool SsrEnabled { get; set; }
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
    /// <summary>Plain right-click (select-for-menu, no touch) -- see
    /// <c>VkViewportControl.ObjectRightClicked</c>'s own doc comment.</summary>
    event Action<uint, int, FaceHitInfo>? ObjectRightClicked;
    event Action<Vector3>? GroundClicked;
    event Action<bool>? MouselookChanged;

    /// <summary>
    /// Fires (on the render thread) when a scene object's GPU upload is dropped rather than
    /// completing -- e.g. <c>VkMaterialUboPool</c>/descriptor-pool exhaustion in a dense scene.
    /// The dropped submission is gone; nothing on this side retries it. Without a subscriber,
    /// the object stays invisible for the rest of the session even after the transient
    /// exhaustion clears, because the streamer that owns retry logic has no other way to learn
    /// the "success" it already recorded (see <c>SceneObjectStreamer.BuildObjectAsync</c>'s own
    /// comment on why it can't tell upload success from GPU failure any other way) never
    /// actually happened. <paramref name="sceneKey"/> is the same key passed to
    /// <see cref="SubmitSceneObject"/>. Subscribers should clear whatever "this key is live"
    /// bookkeeping they hold for it and re-request a build -- the implementation's own
    /// short cooldown before the same key can be retried (see
    /// <c>VkViewportControl.SceneUploadFailureCooldownMs</c>) already prevents a tight
    /// resubmit/fail loop, so subscribers don't need their own debounce on top of it.
    /// </summary>
    event Action<ulong>? SceneObjectUploadFailed;

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
    /// <summary>
    /// Shifts each listed scene object's already-committed <c>Transform</c> by a constant
    /// world-space translation (composed as the outermost operation, on top of whatever local
    /// rotation/scale/world-position is already baked in), without a rebuild or GPU re-upload.
    /// Keys with no committed entry (not yet built, or already removed) are silently skipped.
    /// Used by the lightweight region-crossing path to correct already-built geometry into the
    /// new current-sim-relative frame -- see <c>SceneObjectStreamer.RebaseAllForRegionPromotion</c>.
    /// Safe to call from any thread; the actual mutation happens on the render thread.
    /// </summary>
    void RebaseSceneObjectTransforms(IReadOnlyCollection<ulong> sceneKeys, Vector3 delta);
    void SetSceneObjectMotion(ulong sceneKey, Vector3 scale, Quaternion rotation, Vector3 position,
        Vector3 velocity, Vector3 angularVelocity, Vector3 acceleration);
    /// <summary>
    /// Registers a Linden tree/grass object for the purely cosmetic per-frame wind tilt (see
    /// <c>VkViewportControl.ApplyWindSway</c>). <paramref name="position"/>/<paramref name="rotation"/>
    /// are the object's rest pose (no scale -- foliage mesh vertices already bake the prim's
    /// Scale in themselves). Call once at build time; re-registering the same
    /// <paramref name="sceneKey"/> just replaces the stored rest pose.
    /// </summary>
    void RegisterWindSwayObject(ulong sceneKey, Vector3 position, Quaternion rotation);
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

    /// <summary>
    /// Live-patches one face's tint color on an already-built scene object -- no rebuild, no
    /// GPU texture upload. <c>PrimRenderFace.Color</c> is read fresh every frame by
    /// <c>VkViewportControl.WriteInstanceData</c>'s per-frame instance-buffer pack, so mutating
    /// it is the entire fix; there is nothing to re-upload.
    /// <para>
    /// Unlike <see cref="ScheduleSceneFaceTransformUpdate"/>, addressed by (rootId, GLOBAL
    /// faceOffset into the object's combined face list -- known to callers that themselves built
    /// that list, like <c>SceneAvatarAnimator</c>), this is addressed by
    /// (<paramref name="primLocalId"/>, <paramref name="localFaceIndex"/>) -- the SL-protocol
    /// face identity carried on every <see cref="PrimRenderFace"/> as
    /// <c>PrimRenderFace.PrimLocalId</c>/<c>PrimRenderFace.FaceIndex</c>. This lets
    /// <c>SceneAvatarStreamer.OnAttachmentObjectUpdate</c> (which only knows the attachment
    /// prim's own protocol identity, not that prim's position within the avatar's combined face
    /// list) address a face without maintaining its own global-index cache -- a cache that would
    /// need re-deriving on every avatar rebuild anyway. The render thread resolves it against
    /// the live face list at drain time via a linear scan (cheap: at most a few hundred faces,
    /// on an event that fires at most a few times a second).
    /// </para>
    /// Silently dropped if <paramref name="rootId"/> no longer has a committed scene object, or
    /// no face matches -- same stale-key contract as the other Schedule* methods on this
    /// interface. Same thread-safety contract as <see cref="ScheduleSceneVertexUpdate"/> --
    /// queued and drained on the render thread, never written directly from the calling thread.
    /// </summary>
    void ScheduleSceneFaceColorUpdate(uint rootId, uint primLocalId, int localFaceIndex, Vector4 color);

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
    /// <summary>Sets (or clears, with 0) the PrimLocalId to draw an SL-style selection
    /// outline around -- see <c>VkOutlinePipeline</c>'s own doc comment for the technique.
    /// Called on touch/select; replaces whatever was previously selected.</summary>
    void SetSelectedObject(uint primLocalId);

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
