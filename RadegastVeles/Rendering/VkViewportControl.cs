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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using SkiaSharp;
using Silk.NET.Vulkan;
using Radegast.Veles.Core;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;

namespace Radegast.Veles.Rendering;

public class VkViewportControl : Control, ISingleObjectViewport, ISceneViewport, ICustomHitTest
{
    /// <summary>Raised once, on the UI thread, if <see cref="VkApi.EnsureInitializedAsync"/>
    /// fails, so panel code-behind (e.g. <c>PrimViewerPanel.axaml.cs</c>) can surface it once
    /// this control is wired into a real panel.</summary>
    public event Action<string>? InitFailed;

    /// <summary>Fires, on the UI thread, every time <see cref="InitializeAsync"/> completes
    /// successfully, including tab-switch re-attaches (see <see cref="ISceneViewport.SceneReset"/>
    /// for the subscribe-before-fire race check).</summary>
    public event Action? SceneReset;

    /// <summary>See <see cref="ISceneViewport.SceneObjectUploadFailed"/>. Raised synchronously
    /// on the render thread from <see cref="UploadSceneObjectNoRebuild"/>'s failure path --
    /// subscribers must not block or touch UI-thread-only state directly.</summary>
    public event Action<ulong>? SceneObjectUploadFailed;

    public static readonly StyledProperty<bool> WireframeProperty =
        AvaloniaProperty.Register<VkViewportControl, bool>(nameof(Wireframe));

    /// <summary>When true an additional wireframe overlay pass is drawn over solid geometry.</summary>
    public bool Wireframe
    {
        get => GetValue(WireframeProperty);
        set => SetValue(WireframeProperty, value);
    }

    /// <summary>Default <c>true</c>. The real G-buffer-normal + SSAO + blur three-pass pipeline
    /// gates on this (see <see cref="RenderFrame"/>'s <c>doSsao</c> gate).
    /// <see cref="VkPerFrameUbo.HasSsao"/> is set to 1 only on frames where SSAO actually ran
    /// (this property true AND the pipeline initialized AND <see cref="SsaoMaxOpaqueFaces"/> not
    /// exceeded).</summary>
    public bool SsaoEnabled { get; set; } = true;

    /// <summary>Default <c>true</c>. Gates ONLY the main camera pass's culling (both the <see cref="SceneSpatialGrid"/> coarse
    /// pre-filter and the exact per-face AABB test share this one flag). The shadow and
    /// water-reflection passes always cull via their own independently-computed frustum/grid
    /// query regardless of this property's value -- neither <c>DrawWaterReflection</c> nor
    /// <c>RenderDirectionalShadow</c> reads it.</summary>
    public bool FrustumCullingEnabled { get; set; } = true;

    /// <summary>Gates two things: the
    /// visible sky-dome draw call (see <see cref="DrawSky"/>) and <c>fogDensity</c> in the main
    /// per-face lighting block (also gated on <see cref="AtmosphericsEnabled"/>, see
    /// below).</summary>
    public bool ShowSky { get; set; } = true;

    /// <summary>Real, load-bearing lighting input: unlike <see cref="ShowSky"/>'s sky-dome gate, the
    /// sun/ambient/atmosphere fields here feed <c>prim.frag</c>'s main shading path
    /// UNCONDITIONALLY (not gated by <c>ShowSky</c>) -- this is what actually lights
    /// non-fullbright faces. Populated into the PerFrame UBO every frame in
    /// <see cref="RenderFrame"/>.</summary>
    public SkySettings Sky { get; set; } = new SkySettings();

    /// <summary>Default <c>true</c>. Sub-gate WITHIN <see cref="ShowSky"/>=true: forces <c>FogDensity</c> to 0 regardless of
    /// <see cref="SkySettings.HazeDensity"/> when false -- the sky dome and EEP sampling are
    /// unaffected, only distance haze on prims/terrain is suppressed.</summary>
    public bool AtmosphericsEnabled { get; set; } = true;

    /// <summary>Plain CLR property, same <see cref="SceneLightStreamer"/> type -- already
    /// backend-agnostic. Consumed once per frame by <see cref="SelectLocalLights"/>.</summary>
    public SceneLightStreamer? LightStreamer { get; set; }

    /// <summary>The overlay itself needs zero render-thread work -- confirmed
    /// via research that GL's own overlay is pure VM string formatting + an Avalonia XAML
    /// binding, no GPU-side text draw at all -- only <see cref="Stats"/>/<see cref="_stats"/>'s
    /// FrameCompleted producer needed building.</summary>
    public bool ShowPerfOverlay { get; set; }

    // Constructed eagerly, not lazily on first Stats access -- a VM could subscribe to Stats.FrameCompleted before
    // InitializeAsync has ever run on the render thread, and a lazily-created instance would
    // never receive the Initialize(vk) call InitializeAsync makes once a real VkContext exists.
    // Constructing eagerly and calling Initialize(vk) separately (same two-step shape GL uses:
    // field-initializer construction, then a real OnOpenGlInit-time Initialize() call) means a
    // subscriber's FrameCompleted subscription is never silently missed.
    private readonly VkFrameStatsTracker _stats = new();

    /// <summary>Backed by
    /// <see cref="VkFrameStatsTracker"/> (a new class, not a port of GL's own
    /// <c>FrameStatsTracker</c> -- that file is GL-coupled at the source level and can't be
    /// reused). Best-effort like every other optional resource on this class: falls back to a
    /// functioning, just GPU-timing-less, tracker if timestamp queries aren't supported.</summary>
    public VkFrameStatsTracker Stats => _stats;

    /// <summary>explicit ISceneViewport interface satisfaction -- see
    /// ISceneViewport.Stats's own doc comment for why this is explicit rather than changing the
    /// public property above's return type.</summary>
    IFrameStatsTracker ISceneViewport.Stats => _stats;

    // EnvironmentService's type (SceneEnvironmentService) needs RadegastInstanceAvalonia, which
    // the self-contained VulkanEmbeddingSpike project doesn't pull in. Wrapped in
    // #if !VULKANSPIKE_BUILD (defined only by VulkanSpike.csproj) rather than excluding this
    // whole file from the spike, which would lose every other self-test this migration has
    // built up (SCENETEST/PICK/STRESS/SKINTEST/etc.) -- the real RadegastVeles.csproj build
    // (what actually ships) always includes this property; only the spike's own diagnostic copy
    // of this file is trimmed.
#if !VULKANSPIKE_BUILD
    /// <summary>Plain CLR property,
    /// same <see cref="SceneEnvironmentService"/> type -- already backend-agnostic, a pure poll
    /// API, confirmed via research before porting. Sampled once per frame in
    /// <see cref="RenderFrame"/>, gated on <see cref="ShowSky"/> exactly like GL.</summary>
    public SceneEnvironmentService? EnvironmentService { get; set; }
#endif

    // Local point-light forward-lighting selection (mirrors GL's own
    // MaxLocalLightsLit/LocalLightRange/_litLights/_litLightCount). Deliberately does NOT port
    // the shadow-casting half (_shadowLightCount/MaxLocalLightsShadowed/LocalLightShadowRange,
    // point-light shadow cubemap rendering) -- point lights are directional+water only;
    // VkPerFrameUbo.PointShadowCount stays 0 unconditionally.
    // PatchSceneObjectTexture's semaphore-gated back-pressure pipeline. The "semaphore" is a CPU-side
    // System.Threading.SemaphoreSlim throttling how many DECODED bitmaps can sit in the pending
    // queue at once, not a GPU/Vulkan synchronization primitive (confirmed via research before
    // porting). Genuinely different mechanics from the already-ported single-object
    // PatchSubmissionTexture (unbounded queue, no gate) -- SceneViewer's many-objects-streaming-
    // concurrently scenario needs real back-pressure; PrimViewer/AvatarViewer's bounded-by-one-
    // object volume never did.
    private SemaphoreSlim _texturePatchGate = new(200, 200);
    private const int TexturePatchQueueDepth = 200;
    private readonly ConcurrentQueue<SceneTexturePatch> _pendingScenePatches = new();
    private readonly ConcurrentQueue<SceneTexturePatch> _highPriorityScenePatches = new();
    private readonly List<(SceneTexturePatch Patch, int RetriesLeft)> _deferredScenePatches = new();
    private readonly List<(SceneTexturePatch Patch, int RetriesLeft)> _deferredHighPriorityScenePatches = new();
    // Per-scene-face texture-slot tracking, mirroring _faceTextureSlots' role for the
    // single-submission path exactly (indices match TextureSlot's own values, Albedo=0..
    // Emissive=4) -- populated in UploadSceneObjectNoRebuild, the only source of scene faces.
    // Keyed globally by (PrimLocalId, FaceIndex), not scoped per scene object -- patch lookups
    // resolve by RootLocalId/FaceIndex across ALL of _sceneObjects, not a single object's own
    // list -- real SL PrimLocalIds are simulator-unique, so this is safe in practice, not a
    // latent collision risk.
    private readonly Dictionary<(uint PrimLocalId, int FaceIndex), VkTexture?[]> _sceneFaceTextureSlots = new();
    // Index for TryApplyScenePatch's fallback lookup (patches keyed by the raw
    // avatar/attachment PrimLocalId, not the combined scene key -- see that method's own doc
    // comment) -- without this, that fallback is an O(total scene faces) nested scan over every
    // object's every face, run for every avatar texture patch, every frame a patch is pending.
    // Maintained at the same 3 call sites _sceneFaceTextureSlots already is (populate/remove/
    // clear), same key space (PrimLocalId is simulator-unique).
    private readonly Dictionary<uint, ulong> _scenePrimLocalIdToSceneKey = new();
    // Per-scene-object GPU skin/flexi compute-data disposal tracking, keyed by rootId -- mirrors
    // _sceneFaceTextureSlots' role but scoped per object (not global) since these must be
    // disposed together when THAT object is removed/rebuilt, not scanned/matched individually.
    // Populated in UploadSceneObjectNoRebuild (the only source of scene-object skin/flexi GPU
    // registration), cleared in RemoveSceneObjectGpuNoRebuild/FreeSceneObjectResources/
    // FreePanelResources, same 3 sites _sceneFaceTextureSlots already is. Entries assigned to
    // AvatarFaceSkinData.GpuData/FlexiPrimInfo.GpuData are the SAME objects tracked here -- these
    // lists exist purely for disposal, since _sceneObjects only retains the per-face
    // mesh/material/PrimRenderFace tuples, not the original submission's SkinData/FlexiPrims
    // arrays whose .GpuData fields were set.
    private readonly Dictionary<ulong, List<VkAvatarSkinGpuData>> _sceneSkinGpuDataMap = new();
    private readonly Dictionary<ulong, List<VkFlexiGpuData>> _sceneFlexiGpuDataMap = new();
    private bool _alphaSceneReclassNeeded;

    private const int MaxLocalLightsLit = 4;
    private const float LocalLightRange = 32f; // metres
    private readonly LocalLight[] _litLights = new LocalLight[MaxLocalLightsLit];
    private int _litLightCount;

    /// <summary>Same default,
    /// same role: the flat clear colour used when <see cref="ShowSky"/> is false. Not read at
    /// all when the sky dome is drawn -- see <see cref="RenderFrame"/>'s clear-colour selection.</summary>
    public Vector3 BackgroundColor { get; set; } = new Vector3(0.40f, 0.50f, 0.85f);

    private CompositionSurfaceVisual? _visual;
    private Compositor? _compositor;
    private CompositionDrawingSurface? _surface;
    private readonly Action _updateAction;
    private bool _updateQueued;
    private bool _attached;

    // Circuit breaker for RenderFrame's own catch block -- see that block's own comment for
    // why. A single transient failure resets this counter on the next successful frame (no
    // change from before); several in a row (most concretely: swapchain image creation hitting
    // real GPU VRAM exhaustion, which does not self-resolve frame-to-frame the way a one-off
    // hiccup would) latches _renderDisabledAfterFailure so RenderFrame stops attempting to
    // render at all -- a real terminal state, not an unbounded retry loop hammering the driver
    // every frame with the same failing allocation.
    private const int MaxConsecutiveRenderFailures = 3;
    private int _consecutiveRenderFailures;
    private bool _renderDisabledAfterFailure;

    // Separate backoff track for swapchain image creation (VkInteropSwapchain.BeginDraw ->
    // VkInteropImage's ctor) hitting real GPU VRAM exhaustion specifically
    // (ErrorOutOfDeviceMemory/ErrorOutOfHostMemory). Unlike a generic render failure, this one
    // is NOT "something is broken" -- the VRAM pressure comes from the shared main-scene
    // VkMaterialUboPool/texture usage competing for the same device memory, and that pressure is
    // transient: it eases as the pool evicts objects, textures finish streaming, or the user
    // pans away from a dense region. Routing it through MaxConsecutiveRenderFailures (a few
    // failing frames, i.e. tens of milliseconds at 60fps) meant opening this panel while the
    // main scene was already VRAM-starved killed it permanently before the scene had any chance
    // to free anything up -- observed 2026-09-03: AvatarViewer hit 3 straight OOM failures in
    // ~2s and stayed dead until manually reopened, even though the main SceneViewer's own
    // ErrorOutOfDeviceMemory occurrences around it were transient (pool free-slot count kept
    // recovering). Backed off instead: an OOM-class BeginDraw failure skips doing any GPU work
    // until an exponentially growing cooldown elapses (same shape as this file's own
    // scene-object-upload backoff, see UploadSceneObjectNoRebuild), rather than either hammering
    // the driver every frame or giving up for good after 3 frames. RenderFrame is still invoked
    // every frame the compositor schedules one (ongoing scene activity keeps calling
    // RequestRender) -- during backoff it just returns immediately without touching the
    // swapchain, which is cheap. Only escalates to the permanent _renderDisabledAfterFailure
    // latch after MaxSwapchainOomBackoffAttempts straight OOM failures despite backing off -- by
    // then the pressure has had roughly a minute to ease and it really is a dead end, not
    // transient contention.
    private const int SwapchainOomBaseBackoffMs = 500;
    private const int MaxSwapchainOomBackoffShift = 4; // 500ms * 2^4 = 8s cap per retry
    private const int MaxSwapchainOomBackoffAttempts = 10; // ~55s of escalating retries total
    private long _swapchainOomBackoffUntilTicks;
    private int _swapchainOomConsecutiveFailures;

    private static bool IsDeviceMemoryExhaustion(Exception e) =>
        e is InvalidOperationException
        && (e.Message.Contains("ErrorOutOfDeviceMemory", StringComparison.Ordinal)
            || e.Message.Contains("ErrorOutOfHostMemory", StringComparison.Ordinal));

    private RenderPass _renderPass;
    private VkInteropSwapchain? _swapchain;
    // this panel's own pending-command-buffer list, replacing the old
    // process-wide one VkCommandBufferPool used to own. Shared with _swapchain (and the
    // VkInteropSwapchainImage instances it creates) so BeginDraw/Present/MainPass submissions
    // all reap through the same per-panel ring, matching pre-Step-2 timing exactly.
    private readonly VkFrameReapRing _reapRing = new();
    // the reap ring's own per-slot reaping only guarantees
    // "FramesInFlight frames ago is done" -- too weak for the skin/flexi deformers' single
    // (not N-buffered) SSBOs, which every frame's MainPass reads and every frame's DispatchPending
    // overwrites, so the actual requirement is "the IMMEDIATELY PRECEDING frame's MainPass is
    // done," independent of FramesInFlight. Tracks the most recently submitted MainPass command
    // buffer so DispatchPending can wait on it specifically (WaitOnly -- the reap ring still owns
    // disposal). Null on the first frame (nothing to wait for yet). Deliberately NOT a full fix
    // for real overlap of deformer work itself -- see the wait call site's own comment for the
    // accepted trade-off.
    private VkCommandBufferPool.VkCommandBuffer? _previousMainPassCmd;
    private VkPrimPipeline? _prim;
    private VkPlaceholderTextures? _placeholders;
    private VkPrimDescriptorSets? _frameSets;
    private VkInstanceDrawer? _instanceDrawer;
    private VkWireframePipeline? _wireframe;
    private VkOutlinePipeline? _outline;
    private VkPickPipeline? _pick;

    /// <summary>PrimLocalId of the currently touch/selected prim to draw an SL-style
    /// selection outline around this frame, or 0 for none. Set via <see cref="SetSelectedObject"/>.</summary>
    private uint _selectedOutlineLocalId;

    // Best-effort -- created in
    // InitializeAsync inside a try/catch, _skyReady left false on any failure so RenderFrame
    // simply never draws the dome and falls back to a flat clear colour, mirroring GL's own
    // ShowSky && _skyReady gate.
    private VkSkyPipeline? _skyPipeline;
    private VkCloudNoiseTexture? _cloudNoiseTex;
    private VkSkyDescriptorSet? _skySet;
    private bool _skyReady;
    // _cloudTime/_cloudLastTick track real elapsed seconds
    // (not frame count), advanced each DrawSky call by the wall-clock delta since the previous
    // one, clamped to 0.1s so a long pause (tab hidden, breakpoint, etc.) doesn't jump the cloud
    // scroll position by an equally long amount when rendering resumes.
    private float _cloudTime;
    private long _cloudLastTick;

    // SSAO -- G-buffer-normal pre-pass + SSAO + blur, three offscreen passes recorded before the
    // main render pass begins (see RenderFrame's own comment on why). Best-effort, same posture
    // as sky: any creation failure leaves _ssaoReady false and RenderFrame simply never runs the
    // doSsao branch, mirroring GL's own EnsureGbufferFbo/EnsureSsaoFbos completeness-check-
    // failure posture -- a permanent-ish disable for this panel instance, not a per-frame skip
    // that would still retry every frame.
    private RenderPass _gbufferRenderPass;
    private RenderPass _ssaoOffscreenRenderPass; // shared shape, used for BOTH the raw and blur FBOs
    private VkGNormPipeline? _gnorm;
    private VkSsaoPipeline? _ssaoPipeline;
    private VkSsaoBlurPipeline? _ssaoBlurPipeline;
    private VkSsaoNoiseTexture? _ssaoNoiseTex;
    private VkSsaoDescriptorSet? _ssaoDescSet;
    private bool _ssaoReady;
    // Dedicated samplers for the G-buffer/SSAO targets -- NOT reused from
    // VkPlaceholderTextures (that class's samplers are each bundled 1:1 with their own
    // placeholder image, not general-purpose). ClampToEdge + Nearest matches GL's own
    // TexParameter calls for _gbufNormalTex/_gbufDepthTex/_ssaoColorTex exactly; Linear is the
    // ONE difference, matching GL's own _ssaoBlurTex sampler state.
    private Sampler _ssaoNearestSampler;
    private Sampler _ssaoLinearSampler;
    // Built once at init (GL uploads its own kernel once at init too, not per frame). 32 of
    // the UBO's 64 slots are populated, matching
    // GL's own BuildSsaoKernel(32) call exactly.
    private Vector3[] _ssaoKernel = Array.Empty<Vector3>();

    /// <summary>Above this opaque face count (default 1500) SSAO is skipped for the frame even when
    /// <see cref="SsaoEnabled"/> is true, since the G-buffer pre-pass re-renders every opaque
    /// face a second time.</summary>
    public int SsaoMaxOpaqueFaces { get; set; } = 1500;

    // ── Underwater post-process ─────────────────────────────────────────────────────────────
    // Same best-effort init posture as SSAO above: a creation failure leaves _underwaterReady
    // false and RenderFrame's `underwater` gate simply never fires the pass, rather than taking
    // the whole panel down. The render pass targets the swapchain image directly (LoadOp=Load,
    // see VkRenderPass.CreateUnderwaterPass's own doc comment) -- unlike SSAO/G-buffer, there is
    // no separate persistent color target for this pass's OUTPUT, only for its INPUT
    // (_underwaterSourceImage below, a copy of the swapchain image taken immediately before this
    // pass runs, since Vulkan can't sample and write the same image in one pass).
    private RenderPass _underwaterPass;
    private VkUnderwaterPipeline? _underwaterPipeline;
    private VkUnderwaterDescriptorSet? _underwaterDescSet;
    private bool _underwaterReady;
    private float _underwaterTime;
    private long _underwaterLastTick;
    private PixelSize _underwaterTargetSize;
    private Image _underwaterSourceImage;
    private DeviceMemory _underwaterSourceMemory;
    private ImageView _underwaterSourceView;
    // Tracked across frames so RenderUnderwaterPass's barriers always name the image's REAL
    // current layout/access as their source, the same discipline VkInteropImage.TransitionLayout
    // provides for the swapchain image itself -- this image has no such built-in wrapper (it's a
    // plain Image, not a VkInteropImage), so the tracking is hand-rolled here instead.
    private ImageLayout _underwaterSourceLayout = ImageLayout.Undefined;
    private AccessFlags _underwaterSourceAccess = AccessFlags.None;
    // Dedicated, not reused from _ssaoLinearSampler: that sampler's lifetime is owned by SSAO's
    // own (independent) best-effort init block, so it could be a null handle here even when
    // underwater init otherwise succeeds -- a WriteDescriptorSet with a null Sampler handle is
    // invalid Vulkan usage. Same rationale as _ssaoNearestSampler/_ssaoLinearSampler each being
    // dedicated rather than reused from VkPlaceholderTextures.
    private Sampler _underwaterLinearSampler;

    // ── Directional shadows ─────────────────────────────────────────────────────────────────
    // Point-light shadows explicitly OUT of scope -- point-light illumination itself was never
    // ported to Vulkan either (see shadow.glsl's own TODO note on samplePointShadowCube) --
    // uPointShadowCount stays 0 always, so only the directional path below is real.
    public bool ShadowsEnabled { get; set; } = false;
    /// <summary>Above this opaque face count (default 30000) the shadow pass is skipped for the frame even when
    /// <see cref="ShadowsEnabled"/> is true, since it re-renders every opaque face a second
    /// time (depth-only, cheaper than SSAO's G-buffer pass, hence the much larger budget).</summary>
    public int ShadowsMaxOpaqueFaces { get; set; } = 30000;

    private const int ShadowMapSize = 2048;
    private const float ShadowRadius = 96f;
    private const float ShadowDepthMargin = 150f;

    private RenderPass _shadowRenderPass;
    private VkShadowPipeline? _shadowPipeline;
    private Image _shadowDepthImage;
    private DeviceMemory _shadowDepthMemory;
    private ImageView _shadowDepthView;
    private Sampler _shadowSampler;
    private Framebuffer _shadowFramebuffer;
    private bool _shadowReady;
    // Retained so _frameSetsRefl (created afterward, in the water init block below) can be
    // patched with the real shadow map too -- CreateShadowTarget only writes _frameSets'
    // binding (the only descriptor set that exists at the time shadows initialize); the water
    // block below reads this field and calls _frameSetsRefl.UpdateShadowMap(_shadowMapInfo)
    // itself once _frameSetsRefl exists (both descriptor sets declare their own binding-1
    // shadow map, so a single write only patches one of them).
    private DescriptorImageInfo _shadowMapInfo;
    private Matrix4x4 _shadowLightVp;

    // ── Water: surface pass + reflection pre-pass ──────────────────────
    /// <summary>Default
    /// <c>false</c>: when false, <see cref="DrawWater"/> still draws the water surface (if
    /// <see cref="WaterHeight"/> is valid and the camera is above it) but with no reflection
    /// FBO sampled -- water.frag falls back to its analytic <c>atmSkyGradient</c> reflection.</summary>
    public bool WaterReflectionsEnabled { get; set; } = false;
    /// <summary>Default NaN, meaning "no
    /// water this scene". Set from the simulator's WaterHeight field.</summary>
    public float WaterHeight { get; set; } = float.NaN;
    /// <summary>Same SL-default value as the legacy client.</summary>
    public Vector4 WaterFogColor { get; set; } = new Vector4(0.09f, 0.28f, 0.63f, 0.84f);

    private const int WaterReflSize = 512;
    private const int kReflIntervalMs = 150; // ~7 fps for reflection updates

    private VkWaterPipeline? _waterPipeline;
    private VkWaterDescriptorSet? _waterDescSet;
    private VkTexture? _waterNormalTex;
    private VkTexture? _waterDudvTex;
    private bool _waterReady; // surface shader + textures ready (independent of the reflection FBO)

    private RenderPass _waterReflRenderPass;
    private Image _waterReflColorImage;
    private DeviceMemory _waterReflColorMemory;
    private ImageView _waterReflColorView;
    private Sampler _waterReflColorSampler;
    private Image _waterReflDepthImage;
    private DeviceMemory _waterReflDepthMemory;
    private ImageView _waterReflDepthView;
    private Framebuffer _waterReflFramebuffer;
    private bool _waterReflReady;

    // The reflection pass draws through the SAME prim.vert/prim.frag pipeline as the main pass,
    // but GL's own DrawFaces recomputes several VIEW-DEPENDENT uniforms fresh per call from
    // whichever `view` matrix was passed in (ViewInv, and world->view-space uSunDir). A single
    // shared PerFrame UBO written once per frame (as sky/SSAO/main all share) cannot hold two
    // different View/ViewInv/SunDir triples at once, so the reflection pass gets its OWN
    // VkPrimDescriptorSets instance, its OWN PerFrame UBO rewritten every frame the reflection
    // pass runs (View/ViewInv/SunDir relative to the REFLECTED camera, HasSsao unconditionally 0
    // -- GL's own ssaoTex-defaults-to-0-for-this-call behavior, since the SSAO buffer was
    // computed in main-camera screen space and would sample garbage in reflection space).
    // ShadowsOn/LightVp/atmosphere fields are IDENTICAL between the two sets (they're
    // world-space/view-independent, and shadows DO apply to the reflection pass in GL too).
    // PassSet (set 1, shadow/SSAO samplers) is NOT shared with _frameSets -- each descriptor set
    // instance owns its own binding-1 shadow map write, so both must be patched independently
    // when the shadow target is (re)created (see _shadowMapInfo's own doc comment for where that
    // happens).
    private VkPrimDescriptorSets? _frameSetsRefl;

    private long _reflLastTick;
    private Matrix4x4 _lastReflViewProj;
    private float _waterTime;
    private long _waterLastTick;

    // G-buffer target (resizable, mirrors _depthImage/EnsureDepthTarget's own pattern).
    private PixelSize _gbufSize;
    private Image _gbufNormalImage;
    private DeviceMemory _gbufNormalMemory;
    private ImageView _gbufNormalView;
    private Image _gbufDepthImage;
    private DeviceMemory _gbufDepthMemory;
    private ImageView _gbufDepthView;
    private Framebuffer _gbufFramebuffer;

    // SSAO-raw + blur targets (resizable, one size shared by both since they're always the
    // same full viewport resolution).
    private PixelSize _ssaoTargetSize;
    private Image _ssaoColorImage;
    private DeviceMemory _ssaoColorMemory;
    private ImageView _ssaoColorView;
    private Framebuffer _ssaoFramebuffer;
    private Image _ssaoBlurImage;
    private DeviceMemory _ssaoBlurMemory;
    private ImageView _ssaoBlurView;
    private Framebuffer _ssaoBlurFramebuffer;

    private VkSkinPipeline? _skinPipeline;
    private VkSkinDeformer? _skinDeformer;
    // Created/disposed alongside
    // _opaqueFaces/_alphaFaces on every ApplyPendingSubmission.
    private readonly List<VkAvatarSkinGpuData> _submissionSkinGpu = new();

    private VkFlexiPipeline? _flexiPipeline;
    private VkFlexiDeformer? _flexiDeformer;
    // Single-object-path counterpart to _submissionSkinGpu, for AvatarViewer's flexi
    // attachments. Same created/disposed-alongside-_opaqueFaces/_alphaFaces lifecycle.
    private readonly List<VkFlexiGpuData> _submissionFlexiGpu = new();

    // particle billboard rendering. Best-effort, same posture as the skin/flexi
    // pipelines above -- created in InitializeAsync inside a try/catch.
    private VkParticlePipeline? _particlePipeline;
    private VkParticleBuffer? _particleBuf;
    // Pending submissions from any thread. Null value means "remove". Mirrors GL's
    // _pendingParticleMap (ConcurrentDictionary<ulong, ParticleRenderSubmission?>), including its
    // plain-indexer-assignment SubmitParticles (does NOT dispose a displaced-but-not-yet-drained
    // submission's bitmap -- GL doesn't either).
    private readonly ConcurrentDictionary<ulong, ParticleRenderSubmission?> _pendingParticleMap = new();
    // Live submissions, consumed & drawn on the render thread. Mirrors GL's _particleMap
    // (Dictionary<ulong, (ParticleRenderSubmission Sub, GlTexture? Tex)>), extended with a
    // persistent per-emitter DescriptorSet -- GL has no descriptor-set concept (gl.BindTexture
    // is a raw per-draw bind call); Vulkan needs a real, reusable DescriptorSet per emitter,
    // rewritten only when that emitter's texture actually changes.
    private readonly Dictionary<ulong, (ParticleRenderSubmission Sub, VkTexture? Tex, DescriptorSet Set)> _particleMap = new();
    // CPU scratch for the combined per-frame billboard-vertex array, grown on demand -- mirrors
    // _instanceDataBuf's own grow-on-demand pattern.
    private float[] _particleDataBuf = Array.Empty<float>();

    // _pendingSubmission is handed off via Interlocked.Exchange and consumed at the top of
    // RenderFrame rather than inline in Submit(), preserving a thread-confinement invariant:
    // VkMesh's device-local upload path allocates/frees command buffers through vk.Pool's
    // shared allocator (VkCommandBufferPool's own _lock, still global across panels by design --
    // see that class's doc comment) -- building meshes from Submit's caller thread (the VM
    // thread) instead of the compositor's render thread would race on that allocator state.
    private PrimRenderSubmission? _pendingSubmission;
    // Alpha faces are
    // drawn through VkPrimPipeline.Alpha (depth-write off, blend on) after the opaque batch,
    // sorted back-to-front by GetWorldCentroid() distance-to-eye each frame -- the same approach
    // GL's own alpha pass uses (AlphaSortKey/GetWorldCentroid).
    // Each face now owns its own VkMaterialDescriptorSet (real per-face textures/materials, not
    // the single shared all-placeholder set every face used through the alpha-pass increment) --
    // NOT deduplicated by material this round, unlike the per-submission texture dedup (see
    // ApplyPendingSubmission's texCache), a deliberate scope narrowing: one draw call per face
    // already existed (DrawBatchedInstance takes a distinct VkMesh per face, so there was never
    // real hardware instancing across faces to preserve), so a distinct descriptor set per face
    // costs one extra CmdBindDescriptorSets call per face, not an extra draw call. Revisit only
    // if descriptor-pool exhaustion or per-frame bind-call count becomes a measured problem.
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _opaqueFaces = new();
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _alphaFaces = new();
    // Every VkTexture created for the CURRENT submission (dedup'd by SKBitmap.Handle within one
    // ApplyPendingSubmission call, see its texCache local) -- tracked here so the full set can
    // be disposed at the start of the next submission / on panel teardown. Patches applied via
    // PatchSubmissionTexture (below) also get added/removed here as they replace slots.
    private readonly List<VkTexture> _textures = new();
    // Per-face current texture reference for each of prim.frag's 5 slots (index = (int)
    // TextureSlot), rebuilt alongside _opaqueFaces/_alphaFaces on every ApplyPendingSubmission.
    // VkMaterialDescriptorSet bakes texture presence into its VkMaterialUbo at construction
    // time (unlike GL, which recomputes uHasTexture etc. fresh every DrawFaces call from the
    // live tuple), so PatchSubmissionTexture needs this side table to know which OTHER 4 slots
    // are currently populated when it rebuilds the UBO for the one slot actually being patched.
    private readonly Dictionary<(uint LocalId, int FaceIndex), VkTexture?[]> _faceTextureSlots = new();
    // Patches for the single-Submit() viewer path -- see PatchSubmissionTexture/
    // DrainSubmissionTexturePatches below. PatchSceneObjectTexture's semaphore-gated multi-
    // object path is SceneViewer scope, not ported here.
    private readonly ConcurrentQueue<SceneTexturePatch> _pendingSubmissionPatches = new();
    private readonly List<(SceneTexturePatch Patch, int RetriesLeft)> _deferredSubmissionPatches = new();

    // ── Scene-object layer ──────────────────────────────────────────────────────────────────
    // Additive over the single-submission base geometry above (_opaqueFaces/_alphaFaces) --
    // parallels the _sceneObjects/_pendingSceneObjects/_sceneOpaque/_sceneAlpha structure used
    // by the single-submission path above. Keyed by scene key (ulong: upper 32 bits
    // = sim index, lower 32 bits = localId), same convention as GL.
    private readonly Dictionary<ulong, List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)>> _sceneObjects = new();
    // Pending scene-object updates keyed by sceneKey. null value means "remove from GPU". At
    // most one pending entry per key -- AddOrUpdate atomically replaces an unlanded build,
    // mirroring GL's own ConcurrentDictionary contract exactly (see SubmitSceneObject).
    private readonly ConcurrentDictionary<ulong, PrimRenderSubmission?> _pendingSceneObjects = new();
    // Flat draw lists rebuilt from _sceneObjects whenever scene membership changes.
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _sceneOpaque = new();
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _sceneAlpha = new();
    // Reused every frame for the merged, back-to-front-sorted base-alpha + scene-alpha draw
    // list -- a persistent list that avoids a per-frame allocation; merge-then-sort happens
    // directly on it.
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _mergedAlpha = new();

    // Frustum culling. SceneSpatialGrid.cs/FrustumCuller.cs are both already fully
    // backend-agnostic (zero GL dependency, confirmed via research before porting) -- reused
    // as-is, not re-derived. Three independent HashSets, one per pass with its own frustum
    // (main/shadow/reflection), mirroring GL's _visibleSceneKeys/_shadowVisibleSceneKeys/
    // _reflVisibleSceneKeys exactly -- must not be shared across passes since the frustums
    // differ across passes.
    private readonly SceneSpatialGrid _spatialGrid = new();
    private readonly HashSet<ulong> _visibleSceneKeys = new();
    private readonly HashSet<ulong> _shadowVisibleSceneKeys = new();
    private readonly HashSet<ulong> _reflVisibleSceneKeys = new();

    // Reused per-frame filtered scratch lists (never reallocated, .Clear()+refill each frame)
    // -- one opaque/scene-opaque pair per pass. GNorm (SSAO G-buffer pre-pass) deliberately
    // reuses the MAIN pair rather than getting its own (GL's DrawFacesNormal call sites pass
    // the SAME frustum/visibleSceneKeys as the main pass, not a separately computed one).
    // Shadow/reflection passes ALWAYS cull via their own frustum+grid query regardless of
    // FrustumCullingEnabled -- that property only gates the MAIN pass (neither
    // DrawWaterReflection nor RenderDirectionalShadow reads it).
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _mainOpaqueVisible = new();
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _mainSceneOpaqueVisible = new();
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _shadowOpaqueVisible = new();
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _shadowSceneOpaqueVisible = new();
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _reflOpaqueVisible = new();
    private readonly List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> _reflSceneOpaqueVisible = new();

    // Dead-reckoning motion tracking
    // (pure System.Numerics math, no GL dependency) -- ConcurrentDictionary since
    // SetSceneObjectMotion can be called from any thread (network event handlers) while
    // ExtrapolateMovingSceneObjects only ever runs on the render thread.
    private readonly ConcurrentDictionary<ulong, SceneObjectMotion> _sceneObjectMotion = new();
    // Caps how far dead reckoning will extrapolate past the last received update, so an object
    // that stops sending updates (e.g. leaves the interest list just before a kill) doesn't fly
    // off under stale velocity forever.
    private const float MaxDeadReckoningSeconds = 2f;

    private readonly struct SceneObjectMotion
    {
        public readonly Vector3 Scale;
        public readonly Quaternion Rotation;
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly Vector3 AngularVelocity;
        public readonly Vector3 Acceleration;
        public readonly long UpdateTick;

        public SceneObjectMotion(Vector3 scale, Quaternion rotation, Vector3 position,
            Vector3 velocity, Vector3 angularVelocity, Vector3 acceleration, long updateTick)
        {
            Scale = scale;
            Rotation = rotation;
            Position = position;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Acceleration = acceleration;
            UpdateTick = updateTick;
        }
    }
    // Every VkTexture uploaded for a given scene object, keyed the same as _sceneObjects, so
    // they can be disposed exactly once when that object is removed. Unlike GL's _sceneTexRefs
    // (a single cross-object refcounted table letting two different linksets share one already-
    // uploaded GlTexture), this port does NOT dedupe/share textures ACROSS scene objects: each
    // object's own TryUpload dedupes only within that one object's own faces. Revisit only if
    // texture-memory duplication across many instances of the same prim (e.g. a forest of
    // identical trees) becomes a measured problem.
    private readonly Dictionary<ulong, List<VkTexture>> _sceneObjectTextures = new();
    // Set by ClearAllSceneObjects (any thread), consumed at the top of the next RenderFrame --
    // a plain bool, no stronger synchronization than request-driven eventual consistency
    // (same as GL).
    private bool _pendingClearScene;
    // Parked model-matrix overrides for roots whose upload has not landed yet -- mirrors GL's
    // _sceneObjectTransformOverrides/_pendingTransformOverrides pair exactly.
    private readonly ConcurrentDictionary<ulong, Matrix4x4> _sceneObjectTransformOverrides = new();
    private readonly ConcurrentQueue<(ulong RootId, Matrix4x4 Transform)> _pendingTransformOverrides = new();
    // Queued by RebaseSceneObjectTransforms (lightweight region-crossing path); drained alongside
    // _pendingTransformOverrides. Composed onto each face's EXISTING Transform rather than
    // replacing it (unlike _pendingTransformOverrides), since the caller only knows the world-
    // space delta to apply, not each object's current baked transform.
    private readonly ConcurrentQueue<(ulong SceneKey, Vector3 Delta)> _pendingSceneRebases = new();

    // AvatarViewer: faces in submission-array-POSITION order (NOT PrimRenderFace.
    // FaceIndex, which is caller-assigned and not guaranteed to match array position -- confirmed
    // via the original ScheduleVertexUpdate doc comment: "faceIndex is the index into
    // PrimRenderSubmission.Faces"), independent of the _opaqueFaces/_alphaFaces alpha split above.
    private readonly List<VkMesh?> _faceMeshesByPosition = new();
    // Parallel to _faceMeshesByPosition (same array-position indexing, same null-for-skipped-
    // face convention) -- lets ScheduleFaceTransformUpdate reach the actual PrimRenderFace object
    // by position without a second submission-position lookup mechanism. PrimRenderFace is a
    // sealed class (reference type), so mutating .Transform on the object held here is visible to
    // the same object referenced from _opaqueFaces/_alphaFaces at draw time -- no separate
    // "apply to the draw list too" step needed.
    private readonly List<PrimRenderFace?> _facesByPosition = new();
    // A ConcurrentQueue<(int,float[])>
    // queued by the CPU LBS animation loop (AvatarViewerViewModel.AnimTick via
    // ScheduleVertexUpdate), drained once per frame in RenderFrame via VkMesh.UpdateVertices.
    private readonly ConcurrentQueue<(int FaceIndex, float[] Verts)> _pendingVertexUpdates = new();
    // ScheduleFaceTransformUpdate's queue -- see that method's own doc comment on
    // ISingleObjectViewport for why this must be queued (not a direct field write) and drained
    // on the render thread, same contract as _pendingVertexUpdates immediately above.
    private readonly ConcurrentQueue<(int FaceIndex, Matrix4x4 Transform)> _pendingFaceTransformUpdates = new();
    // Coalesces ScheduleFaceTransformUpdate's RequestRender calls -- see that method's own doc
    // comment. Reset after each drain (below) so the next batch can request again. volatile
    // rather than a bare bool purely so the render thread's reset is promptly visible to the
    // background animation thread's next check -- even a stale read here is harmless (worst
    // case one extra dispatcher post, or one skipped post a later tick recovers from a frame
    // later), this just keeps the coalescing tight rather than approximate.
    private volatile bool _faceTransformRenderRequested;
    // Queued by SceneAvatarAnimator's
    // CPU-LBS fallback and FlexiPrimAnimator's scene-object CPU path via ScheduleSceneVertexUpdate,
    // drained once per frame in RenderFrame against the target scene object's OWN face list (see
    // ScheduleSceneVertexUpdate's own doc comment for why FaceOffset is a different indexing
    // namespace from _pendingVertexUpdates' FaceIndex above).
    private readonly ConcurrentQueue<(uint RootId, int FaceOffset, float[] Verts, int VertsLength, bool IsPoolRented)> _pendingSceneVertexUpdates = new();
    // Scene-object counterpart to _pendingFaceTransformUpdates -- ScheduleSceneFaceTransformUpdate's
    // queue, for SceneAvatarAnimator's rigid-attachment fast path. Same (RootId, FaceOffset)
    // indexing as _pendingSceneVertexUpdates immediately above, drained against the same
    // _sceneObjects list.
    private readonly ConcurrentQueue<(uint RootId, int FaceOffset, Matrix4x4 Transform)> _pendingSceneFaceTransformUpdates = new();
    // ScheduleSceneFaceColorUpdate's queue -- see that method's own doc comment (ISceneViewport)
    // for why this is addressed by (PrimLocalId, LocalFaceIndex) and resolved via a render-thread
    // scan, unlike the FaceOffset-addressed queues immediately above/below.
    private readonly ConcurrentQueue<(uint RootId, uint PrimLocalId, int LocalFaceIndex, Vector4 Color)> _pendingSceneFaceColorUpdates = new();
    // SubmitAvatarFront sets this, then
    // ApplyPendingSubmission (render thread) consumes it and calls Camera3D.FrameBoundsAvatarFront
    // instead of the plain bounds-refresh-only behavior a normal Submit() gets.
    private volatile bool _frameAvatarFrontPending;
    // HudViewer: same
    // flag-then-consume shape as _frameAvatarFrontPending above, but for the "front-on" framing
    // HudViewer/PrimViewer-style flat-panel views want instead of the avatar-specific framing.
    // The original ordering checks _frameAvatarFrontPending first, this second (an
    // else-if) -- ApplyPendingSubmission mirrors that same precedence below since nothing in
    // this control's callers can ever set both flags for the same submission, but the ordering
    // is preserved for exact parity rather than assumed irrelevant.
    private volatile bool _frameFrontPending;

    // Same backend-agnostic camera the GL path already used (Z-up, matching SL's
    // coordinate convention) -- reused rather than re-deriving view-matrix math, and it's the
    // class real submitted geometry (built against the GL path's own camera) is already
    // correct against. Default bounds match the GL path's own defaults (a 1-unit cube
    // centered on the origin) so an empty/not-yet-submitted viewport frames the same way.
    private readonly Camera3D _camera = new();
    private Vector3 _lastBoundsMin = new(-0.5f);
    private Vector3 _lastBoundsMax = new(0.5f);

    // Reused per-frame instance-data scratch buffer, grown on demand and never shrunk.
    // See RenderFrame's own comment for why a per-frame
    // `new float[]` here stopped being harmless once scene-object streaming could push totalCount
    // into the hundreds.
    private float[] _instanceDataBuf = Array.Empty<float>();

    private PixelSize _depthSize;
    private Image _depthImage;
    private DeviceMemory _depthMemory;
    private ImageView _depthView;

    // Pick render target -- mirrors _depthImage/_depthView's EnsureDepthTarget pattern (a
    // second, resizable, per-panel offscreen target), not a new class: this plays the exact
    // same role for the pick pass that the depth target plays for the main pass. Color needs
    // TransferSrcBit (the post-render-pass readback copy source, see RunPickPass) on top of
    // ColorAttachmentBit; depth is write-only, never read back.
    private PixelSize _pickSize;
    private Image _pickColorImage;
    private DeviceMemory _pickColorMemory;
    private ImageView _pickColorView;
    private Image _pickDepthImage;
    private DeviceMemory _pickDepthMemory;
    private ImageView _pickDepthView;
    private Buffer _pickReadbackBuffer;
    private DeviceMemory _pickReadbackMemory;

    private bool _pickRequested;
    private Point _pickPoint;
    // see RequestPick's own doc comment for the exact GL mirror this implements.
    private bool _groundPickRequested;
    private PickPurpose _pickPurpose = PickPurpose.Touch;

    /// <summary>What a pending <see cref="RequestPick"/> call should DO with its result once
    /// resolved -- distinguishes SL's three separate click behaviors, which all share the same
    /// underlying pick machinery. <c>Touch</c> (plain left-click): fires
    /// <see cref="FaceClicked"/>/<see cref="GroundClicked"/> as before, VM sends the touch
    /// packet. <c>Select</c> (plain right-click): fires <see cref="ObjectRightClicked"/>
    /// instead -- selects/highlights the object for the context menu without touching it, real
    /// SL's right-click behavior. <c>CameraFocus</c> (Alt+click, either button, no drag): never
    /// reaches the VM at all -- resolved directly against <c>_camera.Target</c> here, since
    /// that's a pure camera action with no object-interaction side effect in real SL.</summary>
    private enum PickPurpose { Touch, Select, CameraFocus }

    // Pointer-input/camera-drag-gesture state -- see OnPointerPressed/Released/Moved
    // below for the full gesture logic.
    private Point _lastPointer;
    private Point _pressPointer;
    private int _pressClickCount;
    private bool _cameraGesture;
    private bool _leftDown;
    private bool _rightDown;
    private bool _mouselookActive;

    public bool MouselookActive => _mouselookActive;

    public event Action<bool>? MouselookChanged;

    /// <summary>A bool flag, a cursor hide,
    /// and an event. Does NOT touch <see cref="Camera3D.MouselookMode"/> itself -- that's the
    /// subscriber's job (mirrors <c>SceneViewerViewModel.OnMouselookChanged</c>'s real GL
    /// behavior). No cursor warp/recenter/OS pointer-lock exists here either, matching GL's own
    /// real (if limited) implementation.</summary>
    public void EnterMouselook()
    {
        if (_mouselookActive) return;
        _mouselookActive = true;
        Cursor = new Cursor(StandardCursorType.None);
        MouselookChanged?.Invoke(true);
    }

    public void ExitMouselook()
    {
        if (!_mouselookActive) return;
        _mouselookActive = false;
        Cursor = Cursor.Default;
        MouselookChanged?.Invoke(false);
    }

    /// <summary>A
    /// plain delegate, no GL dependency at all. Sim-local integer (x,y) in, nullable terrain
    /// height in meters out (<c>null</c> = out of bounds / no terrain data yet). Consumed only
    /// by <see cref="TryGetGroundHit"/>'s ray march.</summary>
    public Func<int, int, float?>? TerrainHeightProvider { get; set; }

    /// <summary>Fired (UI
    /// thread) when a double-click pick misses every object in the pick buffer AND
    /// <see cref="TryGetGroundHit"/> finds a terrain intersection along the same screen-to-world
    /// ray. The world-space hit position is the sole payload, matching GL's own event shape.
    /// </summary>
    public event Action<Vector3>? GroundClicked;
    // Rebuilt on every pick request (not cached across frames) to match whatever draw order
    // was actually used, required because the alpha list's back-to-front sort changes every
    // frame the camera moves.
    private readonly List<(uint LocalId, int FaceIndex)> _pickMap = new();
    private readonly List<(float[] PickerVerts, float[] NormalUvVerts, ushort[] Indices, Matrix4x4 Transform)> _cpuFaceData = new();

    /// <summary>
    /// Fired (on the UI thread) when a requested pick hits a face. Reuses the same
    /// <see cref="FaceHitInfo"/> struct (already top-level in this namespace -- no new type
    /// needed).
    /// </summary>
    public event Action<uint, int, FaceHitInfo>? FaceClicked;

    /// <summary>
    /// Fired (on the UI thread) when a plain right-click (no drag, no Alt) picks a face --
    /// real SL's "select for the context menu" gesture, distinct from <see cref="FaceClicked"/>
    /// (left-click touch): selects/highlights the object without sending a touch packet. Same
    /// payload shape as <see cref="FaceClicked"/> for consistency, though subscribers typically
    /// only need the id.
    /// </summary>
    public event Action<uint, int, FaceHitInfo>? ObjectRightClicked;

    public VkViewportControl()
    {
        _updateAction = RenderFrame;
        // Needed for the pointer-event overrides below to receive keyboard focus on click. A
        // plain Control has no equivalent to GL's `Background = Brushes.Transparent` Panel-based
        // hit-test area, so ICustomHitTest.HitTest below provides one explicitly (see
        // Avalonia.Rendering.ICustomHitTest).
        Focusable = true;
    }

    /// <summary>
    /// <see cref="ICustomHitTest"/> implementation. Simple bounds containment, the same "whole
    /// control area is hit-testable" behavior GL gets implicitly from being a <c>Panel</c> with a
    /// painted (if transparent) <c>Background</c>.
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    /// <summary>Records press-state (position, click count, button-down flags) and captures the pointer
    /// so drag gestures keep receiving move events even if the cursor leaves this control's
    /// bounds.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _lastPointer = e.GetPosition(this);
        _pressPointer = _lastPointer;
        _pressClickCount = e.ClickCount;
        _cameraGesture = false;
        var props = e.GetCurrentPoint(this).Properties;
        _leftDown = props.IsLeftButtonPressed;
        _rightDown = props.IsRightButtonPressed;
        e.Pointer.Capture(this);
    }

    /// <summary>Fires
    /// a pick on release, or from the fixed screen-center reticle while in mouselook (aims from
    /// where the hidden cursor visually isn't, matching GL's own reticle convention). Dispatches
    /// to one of three real-SL click behaviors (see <see cref="PickPurpose"/>'s own doc
    /// comment): Alt+click (either button, no drag) is <c>CameraFocus</c> and takes priority
    /// over the button check -- SL's Alt+click focuses the camera regardless of which button,
    /// and must not ALSO touch/select; a plain left-click is <c>Touch</c>; a plain right-click
    /// is <c>Select</c>.
    /// <para>
    /// Only <c>_cameraGesture</c> gates Touch/Select -- there used to also be a "was this a
    /// drag" check here (a >4px pointer-movement threshold), removed because it doesn't match
    /// real SL: SL only treats an ALT-modified drag as a camera gesture, so a plain (non-Alt)
    /// click that wobbles a few pixels between press and release -- normal hand tremor, not a
    /// deliberate gesture -- is still a touch/select there, same as a dead-still click. The old
    /// drag check silently dropped a large fraction of real-world clicks (any click with a few
    /// px of incidental movement never reached <see cref="RequestPick"/> at all, with no
    /// visible error -- it just looked like the object ignored the click). This still correctly
    /// excludes an Alt+drag from being treated as Alt+click-to-focus: whenever Alt is held AND
    /// the pointer actually moves, <see cref="OnPointerMoved"/> sets <c>_cameraGesture</c> true
    /// in the same branch that reads Alt, so an Alt+drag is already excluded by the
    /// <c>!_cameraGesture</c> guard below before the inner <c>alt</c> check ever runs.
    /// </para>
    /// <see cref="RequestPick"/>'s own <c>isGroundPickCandidate</c> parameter is set from a
    /// double-click for <c>Touch</c> (matching GL's <c>_groundPickRequested =
    /// _pressClickCount == 2</c> exactly), and unconditionally true for <c>CameraFocus</c> --
    /// real SL's Alt+click focuses on terrain too, not just objects.</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        if (_mouselookActive)
        {
            if (_leftDown)
                RequestPick(new Point(Bounds.Width / 2, Bounds.Height / 2), isGroundPickCandidate: _pressClickCount == 2);
        }
        else if (!_cameraGesture)
        {
            if (alt && (_leftDown || _rightDown))
                RequestPick(_pressPointer, isGroundPickCandidate: true, purpose: PickPurpose.CameraFocus);
            else if (_leftDown)
                RequestPick(_pressPointer, isGroundPickCandidate: _pressClickCount == 2, purpose: PickPurpose.Touch);
            else if (_rightDown)
                RequestPick(_pressPointer, isGroundPickCandidate: false, purpose: PickPurpose.Select);
        }
        _leftDown = false;
        _rightDown = false;
        _cameraGesture = false;
        e.Pointer.Capture(null);
    }

    /// <summary>In
    /// mouselook, any pointer movement (no button/Alt required) looks around directly; otherwise
    /// the SL-style camera-gesture bindings (Alt+drag orbit, Alt+Ctrl+drag pan, Alt+Ctrl+Shift+
    /// drag dolly) apply, and a plain left-click with no modifier is left for
    /// <see cref="OnPointerReleased"/>'s own pick-trigger logic to handle instead.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastPointer.X);
        float dy = (float)(pos.Y - _lastPointer.Y);
        _lastPointer = pos;

        if (_mouselookActive)
        {
            _camera.OrbitDrag(dx, dy);
            RequestRender();
            return;
        }

        if (!_leftDown && !_rightDown) return;

        bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        // ── SL-style camera gesture bindings ───────────────────────────────────
        // Alt+drag              -> orbit  (left OR right button)
        // Alt+Ctrl+drag         -> pan
        // Alt+Ctrl+Shift+drag   -> dolly (zoom)
        // Plain left-click (no modifier, no drag) -> object interaction (pick)
        // Middle-button drag or right-drag alone  -> no-op here (reserved)
        if (alt)
        {
            _cameraGesture = true;
            if (ctrl && shift)
            {
                _camera.Zoom(dy * 0.15f);
            }
            else if (ctrl)
            {
                _camera.PanDrag(dx, dy);
            }
            else
            {
                _camera.OrbitDrag(dx, dy);
            }
        }

        if (dx != 0 || dy != 0)
            RequestRender();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Zoom((float)e.Delta.Y);
        RequestRender();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A MainWindow tab switch away-and-back re-fires this WITHOUT OnDetachedFromLogicalTree
        // ever running first (Avalonia detaches/reattaches the VISUAL tree on a tab switch, but
        // not the LOGICAL tree; see that method's own note). So on a redundant reattach,
        // _attached, _sceneObjects, and every render pipeline/descriptor set from the prior init
        // are all still fully live and valid -- FreePanelResources (the only thing that disposes
        // _prim/_wireframe/_frameSets/the sky/SSAO/shadow/water/skin/flexi/particle
        // pipelines/_instanceDrawer) never ran. Unconditionally re-running InitializeAsync would
        // leak the entire first copy of every one of those objects and fire SceneReset, which
        // re-submits every already-live scene object (UploadSceneObjectNoRebuild disposes-then-
        // rebuilds each one) -- a redundant GPU-memory spike that can trigger
        // ErrorOutOfDeviceMemory. Skip the whole re-init when a prior one is still live; still
        // refresh _visual's size in case the window was resized while this tab was hidden (the
        // only other per-attach state InitializeAsync would otherwise have refreshed).
        LibreMetaverse.Logger.Info($"[VkViewportControl] OnAttachedToVisualTree (_attached={_attached})");
        if (_attached)
        {
            if (_visual != null) _visual.Size = new Avalonia.Vector(Bounds.Width, Bounds.Height);
            RequestRender();
            return;
        }
        // _attached only flips true at the very END of InitializeAsync (after every
        // pipeline/render-pass/descriptor-set in the whole method has been created), so the
        // `if (_attached)` guard above only catches a reattach AFTER a prior init has fully
        // finished -- not one that fires WHILE a prior init is still awaiting/running. A quick
        // tab-switch away and back before the first init reaches _attached=true re-enters this
        // method, and with no guard here it could fire a SECOND concurrent InitializeAsync -- two
        // full pipeline-creation sequences (render passes, ~10 graphics/compute pipelines,
        // samplers, descriptor sets) racing each other and both writing the same instance fields,
        // on top of whatever GPU driver contention two concurrent pipeline builds cause.
        // _initializing blocks the second call outright instead of letting it race.
        if (_initializing) return;
        _initializing = true;
        _ = InitializeAsync();
    }

    private volatile bool _initializing;

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        // This is the only place _attached flips back to false, so a genuine full teardown vs.
        // a same-tab no-op is worth being able to tell apart in the log.
        LibreMetaverse.Logger.Info($"[VkViewportControl] OnDetachedFromLogicalTree (_attached={_attached})");
        if (_attached)
        {
            _surface?.Dispose();
            FreePanelResources();
        }
        if (_countedAsLiveInstance)
        {
            _countedAsLiveInstance = false;
            System.Threading.Interlocked.Decrement(ref s_liveInstanceCount);
        }
        _attached = false;
        base.OnDetachedFromLogicalTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == BoundsProperty || change.Property == WireframeProperty) RequestRender();
        base.OnPropertyChanged(change);
    }

    /// <summary>Backend-agnostic replacement for <c>OpenGlControlBase.RequestNextFrameRendering()</c>.
    /// Queues one composition update; safe to call redundantly (coalesces via <see cref="_updateQueued"/>).
    /// <para>
    /// Genuinely safe to call from any thread, matching every "any thread" contract documented
    /// on <see cref="Submit"/>/<see cref="PatchSubmissionTexture"/>/<see cref="SubmitSceneObject"/>
    /// and the rest of this class's public API: <c>Compositor.RequestCompositionUpdate</c>
    /// itself calls <c>Dispatcher.VerifyAccess()</c> and throws off the UI thread, so any
    /// background-thread caller (e.g. <c>AvatarViewerViewModel.LoadAsync</c>'s texture-download
    /// <c>Progress&lt;T&gt;</c> callback, which runs on a thread-pool thread when the download
    /// completes off the UI thread) needs this hop to avoid crashing.
    /// <see cref="PatchSceneObjectTexture"/>'s own background continuation already works around
    /// this correctly with an explicit <c>Dispatcher.UIThread.Post(RequestRender)</c> call; that
    /// is redundant with this method's own internal check but harmless to leave as-is.
    /// </para></summary>
    /// <summary>
    /// Sets (or clears, with 0) the PrimLocalId to draw an SL-style selection outline
    /// around, replacing whatever was previously selected. Called by
    /// <c>SceneViewerViewModel.OnFaceClicked</c> on touch/select. Same threading assumption
    /// as the <see cref="Wireframe"/> property -- a plain field, set from the UI thread and
    /// read by <see cref="DrawSelectionOutline"/> on the render thread; a uint write can't
    /// tear, so no lock is needed for this simple a flag.
    /// </summary>
    public void SetSelectedObject(uint primLocalId) => _selectedOutlineLocalId = primLocalId;

    public void RequestRender()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RequestRender);
            return;
        }
        // _renderDisabledAfterFailure: RenderFrame itself already no-ops immediately once this
        // is set (see its own top-of-method check), so this extra gate isn't needed for
        // correctness -- it's here so a disabled panel stops generating compositor round-trips
        // entirely instead of queuing (and instantly no-op'ing) an update every time something
        // would otherwise have requested a redraw.
        if (_attached && !_updateQueued && !_renderDisabledAfterFailure && _compositor != null)
        {
            _updateQueued = true;
            _compositor.RequestCompositionUpdate(_updateAction);
        }
    }

    /// <summary>
    /// Submit new geometry from any thread. Replaces previously-submitted geometry, consumed
    /// on the render thread -- see the <see cref="_pendingSubmission"/> field comment for why the actual
    /// <see cref="VkMesh"/> creation is deferred to <see cref="RenderFrame"/> rather than done
    /// here inline.
    /// </summary>
    public void Submit(PrimRenderSubmission submission)
    {
        var displaced = Interlocked.Exchange(ref _pendingSubmission, submission);
        if (displaced != null) DisposeFaceBitmaps(displaced);
        RequestRender();
    }

    /// <summary>AvatarViewer: read-only access to the underlying camera, needed by
    /// <c>AvatarViewerViewModel.AnimTick</c>'s LOD selection (projected pixel height against the
    /// current camera/viewport).</summary>
    public Camera3D Camera => _camera;

    /// <summary>Debug/test-only: number of scene-object faces currently in the flat draw lists
    /// (<see cref="_sceneOpaque"/> + <see cref="_sceneAlpha"/>), for self-verification (used by
    /// the spike's <c>VULKANSPIKE_SCENETEST</c> harness).</summary>
    public int SceneFaceCount => _sceneOpaque.Count + _sceneAlpha.Count;

    /// <summary>Debug/test-only: number of live scene objects (<see cref="_sceneObjects"/>'
    /// key count), independent of how many faces each carries. Mirrors GL's own diagnostic
    /// intent (its <c>PendingUploadCount</c>) applied to landed rather than pending objects.</summary>
    public int SceneObjectCount => _sceneObjects.Count;

    /// <summary>Debug/test-only: number of scene-object uploads/removals still queued, not yet
    /// drained by <see cref="DrainPendingSceneObjects"/>.</summary>
    public int PendingSceneUploadCount => _pendingSceneObjects.Count;

    /// <summary>number of normal-priority scene texture patches still waiting to be
    /// drained by <see cref="DrainScenePendingTexturePatches"/>.</summary>
    public int QueuedTexturePatchCount => _pendingScenePatches.Count;

    /// <summary>Number of scene texture patches currently parked in a deferred-retry state
    /// (target face not found yet).</summary>
    public int DeferredTexturePatchCount => _deferredScenePatches.Count;

    // CPU/GPU perf-overlay numbers (SceneViewerViewModel.OnFrameCompleted) only cover the main
    // render pass's own command buffer -- every OTHER synchronous submit-and-wait this frame
    // (scene-object mesh/texture/skin-GPU-data uploads, texture-patch applies) is invisible to
    // that GPU timestamp while its CPU-side fence wait still lands in the CPU number. When a
    // scene is chronically backlogged (Upload Q / Deferred staying nonzero instead of draining to
    // 0), these drains are the likely reason CPU time is much higher than the main pass's own GPU
    // time -- these two stopwatch readings turn that suspicion into a number instead of a guess.
    // Reused Stopwatch (not `System.Diagnostics.Stopwatch.StartNew()` per frame) to avoid adding
    // its own per-frame allocation to the very cost this is measuring.
    private readonly System.Diagnostics.Stopwatch _drainStopwatch = new();
    public double DrainSceneObjectsMs { get; private set; }
    public double DrainTexturePatchesMs { get; private set; }

    // Same reasoning as the two properties above, added while scoping frame-in-flight
    // pipelining: VkSkinDeformer/VkFlexiDeformer's own DispatchPending calls are each a
    // synchronous one-off submit+wait (SubmitAndWait), so their CPU-side fence-wait cost is
    // baked into the frame's CPU ms with no visibility into how much of it they actually are.
    // Whether pipelining the main pass is worth its risk depends heavily on how much of the
    // frame these two already-serialized waits account for -- measure before committing further.
    private readonly System.Diagnostics.Stopwatch _deformerStopwatch = new();
    public double SkinDispatchMs { get; private set; }
    public double FlexiDispatchMs { get; private set; }

    // Same reasoning again: the vertex/face-transform-update drain loops below call
    // VkMesh.UpdateVertices per queued item, whose non-dynamic (static-mesh) branch does a full
    // destroy+reallocate+SubmitAndWait EVERY call -- explicitly flagged as a hot-path hazard by
    // that method's own doc comment -- and neither loop has a time budget the way the other
    // drains above do. Added to find out whether this, not deformer waits, is where an
    // unexplained CPU-ms gap (frame CPU ms far exceeding the sum of every OTHER instrumented
    // section) is actually going.
    private readonly System.Diagnostics.Stopwatch _vertexUpdateStopwatch = new();
    public double VertexUpdateDrainMs { get; private set; }

    // Bisects everything the drains above don't cover: particle drain, BeginDraw/swapchain
    // image acquire, culling, every sub-pass (G-buffer/SSAO/shadow/water reflection), and all
    // draw-call recording for the main pass -- versus the final cmd.Submit()+
    // FreeUsedCommandBuffers() call, which is the literal GPU fence wait. Added because a prior
    // retest showed frames with EVERY other instrumented bucket near-zero (Drain SObj/Patch/
    // Deformer/VertexUpdateDrain summing to under 5% of total CPU ms) yet total CPU ms still hit
    // several SECONDS -- this pair narrows down which half of the remaining, previously totally
    // uninstrumented majority of the frame the missing time is actually in.
    private readonly System.Diagnostics.Stopwatch _renderStopwatch = new();
    public double MainPassRecordMs { get; private set; }
    public double MainPassSubmitWaitMs { get; private set; }

    // Further bisection of MainPassRecordMs: a retest showed Record dominating (SubmitWait
    // stayed under 60ms even on 3+ second frames), ruling out the GPU fence wait entirely --
    // these two are cumulative _renderStopwatch readings (not their own restart/elapsed pairs)
    // at two checkpoints inside the record span, so the log line can derive three deltas:
    // particle-drain+BeginDraw+culling, shadow/SSAO/water-reflection sub-pass recording, and
    // the main draw-call recording itself.
    public double PreCullMs { get; private set; }
    public double SubPassMs { get; private set; }

    // Finer split of the PreCull span itself, added after PreCull turned out to dominate: how
    // much is particle draining vs. swapchain BeginDraw (which, per VkInteropSwapchain's own
    // new diagnostic fields, further splits into the deferred previous-frame Present-buffer
    // reap vs. the actual image-acquire) vs. everything after (depth target + culling).
    public double ParticleDrainMs { get; private set; }
    public double BeginDrawMs { get; private set; }
    public double SwapchainFreeCmdBuffersMs { get; private set; }
    public double SwapchainBeginDrawCoreMs { get; private set; }

    // Split of MainPassSubmitWaitMs itself, added after a retest showed it spike to 2033.6ms on
    // a frame with an otherwise-idle queue (Drain SObj/Patch/Deformer all near-zero) -- nothing
    // was in front of this submit, so a backed-up queue can't explain it. This distinguishes two
    // very different causes that read identically as "SubmitWait" today: cmd.Submit() itself
    // (which now takes VkCommandBufferPool's _queueLock -- if another thread holds it, THIS
    // thread blocks here, before QueueSubmit even happens) vs. FreeUsedCommandBuffers()'s
    // WaitForFences (a genuine GPU-side stall, e.g. the DirectX keyed-mutex acquire in
    // VkInteropSwapchainImage.BeginDraw blocking on the compositor). If SubmitCallMs is large,
    // it's lock contention -- a regression from the Step 0 fix. If FenceWaitMs is large, it's
    // GPU/compositor-side and the keyed mutex is the next thing to instrument.
    public double SubmitCallMs { get; private set; }
    public double FenceWaitMs { get; private set; }

    // Set once per frame right before the main-pass submit: how many VkViewportControl panels
    // are currently alive process-wide. All panels share one VkContext/VkQueue, so if this is
    // >1 during a SubmitWait spike, cross-panel queue contention (another panel's render thread
    // submitting a heavy burst concurrently) is a live candidate, not just theory.
    private static int s_liveInstanceCount;
    public int LiveInstanceCount => s_liveInstanceCount;
    private bool _countedAsLiveInstance;

    // Shared per-frame budget for every render-thread drain loop that performs synchronous GPU
    // work (scene-object upload, texture-patch apply). VkTexture's constructor does one
    // command-buffer submit + WaitForFences(ulong.MaxValue) -- cheap-looking per call, but a
    // burst of expensive uploads (e.g. many avatar-bake or region-population patches landing in
    // the same frame) could still blow well past any real per-frame budget if capped by COUNT
    // instead of time. All three drain loops (this one and the two texture-patch ones) bound
    // themselves the same way: Stopwatch-timed, always admit at least one item so a single
    // expensive one can't stall progress entirely, request another render tick when the budget
    // runs out with work left.
    private const double SceneWorkBudgetMs = 6.0;

    /// <summary>
    /// AvatarViewer: like <see cref="Submit"/>, but frames the camera "avatar
    /// front" style once the submission is actually applied, instead of leaving the camera
    /// untouched. Works by setting
    /// a flag here and consuming it inside <see cref="ApplyPendingSubmission"/> (render thread),
    /// not calling <see cref="Camera3D.FrameBoundsAvatarFront"/> synchronously here -- this
    /// control's bounds aren't known until the submission is actually applied, same reasoning
    /// as every other <c>_lastBoundsMin</c>/<c>_lastBoundsMax</c> update in this class.
    /// </summary>
    public void SubmitAvatarFront(PrimRenderSubmission submission)
    {
        _frameAvatarFrontPending = true;
        Submit(submission);
    }

    /// <summary>
    /// HudViewer: like <see cref="Submit"/>, but frames the camera front-on
    /// (matching legacy Radegast's HUD viewer) once the submission is actually applied -- see
    /// <see cref="SubmitAvatarFront"/>'s
    /// own doc comment for why the flag-then-consume shape is used instead of a synchronous call.
    /// </summary>
    public void SubmitFront(PrimRenderSubmission submission)
    {
        _frameFrontPending = true;
        Submit(submission);
    }

    /// <summary>Reset the camera to frame the currently displayed object.</summary>
    public void ResetCamera()
    {
        _camera.FrameBounds(_lastBoundsMin, _lastBoundsMax);
        RequestRender();
    }

    /// <summary>Reset the camera to face the object head-on (for HUDs and flat panels).</summary>
    public void ResetCameraFront()
    {
        _camera.FrameBoundsFront(_lastBoundsMin, _lastBoundsMax);
        RequestRender();
    }

    /// <summary>Step-orbit by exact degree amounts (for button navigation).</summary>
    public void OrbitStep(float dyaw, float dpitch)
    {
        _camera.OrbitStep(dyaw, dpitch);
        RequestRender();
    }

    /// <summary>Zoom by the given number of scroll steps (positive = zoom in).</summary>
    public void ZoomStep(float delta)
    {
        _camera.Zoom(delta);
        RequestRender();
    }

    /// <summary>A
    /// "soft follow" that only slides the orbit target, preserving the caller's current orbit
    /// angle/zoom. Safe to call from any thread (network event handlers call this), marshals to
    /// the UI thread before touching <see cref="Camera3D"/>.</summary>
    public void UpdateCameraFollow(Vector3 target)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _camera.Target = target;
            RequestRender();
        });
    }

    /// <summary>A strict
    /// superset of <see cref="UpdateCameraFollow"/>: re-centers the orbit pivot, with optional
    /// distance/pitch snap (sentinel values <c>distance &lt;= 0</c> / <c>pitch &lt;= -999</c>
    /// mean "leave unchanged", matching GL's own sentinel convention exactly rather than an
    /// overload set, so existing callers built against that convention port unchanged).</summary>
    public void SetCameraTarget(Vector3 target, float distance = -1f, float pitch = -1000f)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _camera.Target = target;
            if (distance > 0f) _camera.Distance = distance;
            if (pitch > -999f) _camera.Pitch = pitch;
            RequestRender();
        });
    }

    /// <summary>
    /// Request a pick (click-to-select) at <paramref name="point"/>, in this control's own
    /// coordinate space (DIPs, top-left origin -- Avalonia's standard pointer-event
    /// convention). Resolved on the render thread inside the next <see cref="RenderFrame"/>;
    /// any hit is delivered via <see cref="FaceClicked"/> on the UI thread.
    /// </summary>
    /// <param name="point">Click point in this control's own coordinate space.</param>
    /// <param name="isGroundPickCandidate">Mirrors GL's own
    /// <c>_groundPickRequested = _pressClickCount == 2</c> -- set true for a double-click, so a
    /// miss against every object in the pick buffer falls through to
    /// <see cref="TryGetGroundHit"/>/<see cref="GroundClicked"/> instead of resolving to
    /// nothing. Defaults false since this method currently has no real pointer-event caller
    /// (a future pointer-input port is what will pass this for real).</param>
    public void RequestPick(Point point, bool isGroundPickCandidate = false)
        => RequestPick(point, isGroundPickCandidate, PickPurpose.Touch);

    private void RequestPick(Point point, bool isGroundPickCandidate, PickPurpose purpose)
    {
        _pickPoint = point;
        _pickRequested = true;
        _groundPickRequested = isGroundPickCandidate;
        _pickPurpose = purpose;
        RequestRender();
    }

    /// <summary>
    /// Enqueues a single decoded texture bitmap to be uploaded and stitched into an
    /// already-submitted face on the next render. Callable from
    /// any thread; ownership of
    /// <paramref name="patch"/>'s bitmap transfers to this control. Unlike <c>Submit</c>, the
    /// bitmap is preprocessed here on the CALLER's thread (keep the render thread doing only the
    /// actual GPU upload) -- safe because <see cref="VkTexture.Preprocess"/> is pure CPU-side
    /// SkiaSharp work with no VkContext dependency, unlike <see cref="VkTexture"/>'s constructor
    /// itself (GPU resource creation, which must stay render-thread-only).
    /// </summary>
    public void PatchSubmissionTexture(SceneTexturePatch patch)
    {
        if (patch.Bitmap != null)
            patch = patch with { Bitmap = VkTexture.Preprocess(patch.Bitmap) };
        _pendingSubmissionPatches.Enqueue(patch);
        RequestRender();
    }

    /// <summary>
    /// Includes a
    /// UI-thread-hop-to-threadpool guard (the gate's only permit producer,
    /// <see cref="DrainScenePendingTexturePatches"/>, runs on the render thread -- never the UI
    /// thread -- so a blocking <c>Wait</c> called directly from the UI thread could never be
    /// satisfied and would freeze the app; reachable via a <c>Progress&lt;T&gt;</c> constructed
    /// with the Avalonia SynchronizationContext delivering a patch callback there) and a
    /// gate-capture-into-a-local pattern (<see cref="_texturePatchGate"/> gets swapped for a
    /// fresh instance on teardown -- see this control's cleanup method -- and Wait/Release must
    /// operate on the SAME object instance throughout one call). Ownership of
    /// <paramref name="patch"/>'s bitmap transfers to this control; callers must not use or
    /// dispose it afterward. Safe to call from any thread.
    /// </summary>
    public void PatchSceneObjectTexture(SceneTexturePatch patch, CancellationToken ct = default)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            var deferred = patch;
            _ = Task.Run(() =>
            {
                try { PatchSceneObjectTexture(deferred, ct); }
                catch (OperationCanceledException) { /* bitmap already disposed inside */ }
            });
            return;
        }

        var gate = _texturePatchGate;
        try
        {
            gate.Wait(ct);
        }
        catch (OperationCanceledException)
        {
            patch.Bitmap?.Dispose();
            throw;
        }
        catch (ObjectDisposedException)
        {
            // The control was torn down between the field read and Wait (cleanup disposed this
            // gate instance). Treat as cancellation: dispose the bitmap and silently exit --
            // throwing here would propagate through Progress<T> onto the thread pool and crash.
            patch.Bitmap?.Dispose();
            return;
        }

        // Preprocess on this background thread so the render thread only performs the actual GPU
        // upload -- no Skia conversion/flip on the render loop. If Preprocess or Enqueue throw,
        // release the permit we just acquired so the semaphore stays balanced.
        try
        {
            if (patch.Bitmap != null)
                patch = patch with { Bitmap = VkTexture.Preprocess(patch.Bitmap) };
            if (patch.HighPriority)
                _highPriorityScenePatches.Enqueue(patch);
            else
                _pendingScenePatches.Enqueue(patch);
        }
        catch
        {
            patch.Bitmap?.Dispose();
            ReleasePatchGate(gate);
            throw;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(RequestRender);
    }

    private static void ReleasePatchGate(SemaphoreSlim gate)
    {
        try { gate.Release(); }
        catch (SemaphoreFullException) { /* teardown race, harmless -- see PatchSceneObjectTexture's own doc comment */ }
        catch (ObjectDisposedException) { /* gate already swapped out on teardown */ }
    }

    /// <summary>
    /// Queue a vertex-buffer update for a specific face (CPU-computed LBS skin deformation or
    /// flexi simulation), applied on the next render.
    /// <paramref name="faceIndex"/> is the face's POSITION in the most recent submission's
    /// <see cref="PrimRenderSubmission.Faces"/>, matching <see cref="_faceMeshesByPosition"/>'s
    /// own indexing, NOT <see cref="PrimRenderFace.FaceIndex"/> (a separate, caller-assigned
    /// label). Unlike GL, which relies on its ~30fps hidden-tab heartbeat timer to eventually
    /// pick up a queued update with no explicit render request, this control has no such
    /// heartbeat -- calling <see cref="RequestRender"/> here is therefore required, not optional,
    /// for animation to actually produce visible frames at animation rate.
    /// </summary>
    public void ScheduleVertexUpdate(int faceIndex, ReadOnlySpan<float> verts)
    {
        _pendingVertexUpdates.Enqueue((faceIndex, verts.ToArray()));
        RequestRender();
    }

    /// <summary>See <see cref="ISingleObjectViewport.ScheduleFaceTransformUpdate"/> for the full
    /// rationale. Queued (not written to <see cref="PrimRenderFace.Transform"/> directly) for the
    /// same reason and drained on the same cadence as <see cref="ScheduleVertexUpdate"/> --
    /// <paramref name="faceIndex"/> shares that method's indexing (submission-array position,
    /// see <see cref="_facesByPosition"/>'s own field comment).
    /// <para>
    /// Unlike <see cref="ScheduleVertexUpdate"/> (called at most once per face per tick, and
    /// only for whichever faces fall back to CPU skinning -- rare now that most faces are
    /// GPU-dispatched), this is AnimTick's ONLY path for every rigid-single-bone face, so a
    /// single tick can call this 100+ times. <see cref="RequestRender"/> posts to the Avalonia
    /// dispatcher when called off the UI thread (AnimTick's background thread) -- calling it
    /// unconditionally per-face would turn one animation tick into 100+ dispatcher posts instead
    /// of one. <see cref="_faceTransformRenderRequested"/> coalesces that down to one post per
    /// batch; a request from a later tick still gets through once the render thread's drain in
    /// <see cref="RenderFrame"/> resets the flag.
    /// </para></summary>
    public void ScheduleFaceTransformUpdate(int faceIndex, Matrix4x4 transform)
    {
        _pendingFaceTransformUpdates.Enqueue((faceIndex, transform));
        if (!_faceTransformRenderRequested)
        {
            _faceTransformRenderRequested = true;
            RequestRender();
        }
    }

    /// <summary>
    /// Queue a vertex-buffer update for one face of a scene object -- the scene-object
    /// counterpart to <see cref="ScheduleVertexUpdate"/>, for SceneAvatarAnimator's CPU-LBS
    /// fallback and FlexiPrimAnimator's scene-object CPU deformation path. The <paramref
    /// name="rootId"/> parameter stays <c>uint</c> even though <see cref="_sceneObjects"/> is
    /// keyed by <c>ulong</c> sceneKey -- inherited from GL's own signature (its real caller,
    /// SceneFlexiStreamer, already truncates via <c>(uint)sceneKey</c> before calling this), not
    /// a narrowing introduced by this port. <paramref name="faceOffset"/> indexes into that
    /// scene object's OWN face list (position within <see cref="_sceneObjects"/>[rootId]) -- a
    /// different indexing namespace from <see cref="ScheduleVertexUpdate"/>'s single-submission
    /// <c>faceIndex</c> (position in <see cref="_faceMeshesByPosition"/>), matching GL's own two
    /// separate queues/methods for the two cases. Like GL, no <see cref="RequestRender"/>-timing
    /// subtlety here beyond the same requirement <see cref="ScheduleVertexUpdate"/> already
    /// documents (this control has no heartbeat, so the call is required every time, not just
    /// opportunistic).
    /// </summary>
    /// <param name="isPoolRented"><c>true</c> if <paramref name="verts"/> was rented from
    /// <see cref="System.Buffers.ArrayPool{T}.Shared"/> and must be returned after the Vulkan
    /// upload (e.g. from SceneAvatarAnimator); <c>false</c> if allocated with <c>new[]</c> and
    /// must NOT be returned (e.g. from FlexiPrimAnimator, which needs an exact-size buffer).</param>
    public void ScheduleSceneVertexUpdate(uint rootId, int faceOffset, float[] verts, int vertsLength, bool isPoolRented = false)
    {
        _pendingSceneVertexUpdates.Enqueue((rootId, faceOffset, verts, vertsLength, isPoolRented));
        RequestRender();
    }

    /// <summary>See <see cref="ISceneViewport.ScheduleSceneFaceTransformUpdate"/> for the full
    /// rationale -- scene-object counterpart to <see cref="ScheduleFaceTransformUpdate"/>, same
    /// (rootId, faceOffset) indexing as <see cref="ScheduleSceneVertexUpdate"/> immediately
    /// above. Shares that method's coalesced-RequestRender flag (see
    /// <see cref="_faceTransformRenderRequested"/>) since a busy region can call this once per
    /// rigid attachment per avatar per tick -- the same flooding risk
    /// ScheduleFaceTransformUpdate's own doc comment describes, multiplied by avatar count
    /// instead of bounded to one avatar.</summary>
    public void ScheduleSceneFaceTransformUpdate(uint rootId, int faceOffset, Matrix4x4 transform)
    {
        _pendingSceneFaceTransformUpdates.Enqueue((rootId, faceOffset, transform));
        if (!_faceTransformRenderRequested)
        {
            _faceTransformRenderRequested = true;
            RequestRender();
        }
    }

    /// <summary>See <see cref="ISceneViewport.ScheduleSceneFaceColorUpdate"/> for the full
    /// rationale. Shares <see cref="_pendingSceneFaceTransformUpdates"/>'s coalesced-RequestRender
    /// flag -- both are drained together in the same per-frame pass, so there is no benefit to a
    /// second flag.</summary>
    public void ScheduleSceneFaceColorUpdate(uint rootId, uint primLocalId, int localFaceIndex, Vector4 color)
    {
        _pendingSceneFaceColorUpdates.Enqueue((rootId, primLocalId, localFaceIndex, color));
        if (!_faceTransformRenderRequested)
        {
            _faceTransformRenderRequested = true;
            RequestRender();
        }
    }

    /// <summary>
    /// SceneViewer streaming substrate: queue an additive scene-object
    /// submission for the given scene key. Replaces any previously queued submission for the
    /// same key. Safe to call from any thread. See the <see cref="_pendingSceneObjects"/> field comment for why the actual
    /// GPU upload is deferred to the render thread rather than done here inline.
    /// </summary>
    public void SubmitSceneObject(ulong sceneKey, PrimRenderSubmission submission)
    {
        _pendingSceneObjects.AddOrUpdate(
            sceneKey,
            submission,
            (_, displaced) => { if (displaced != null) DisposeFaceBitmaps(displaced); return submission; });
        RequestRender();
    }

    /// <summary>Queue removal of the scene object with the given scene key. Safe to call from
    /// any thread.</summary>
    public void RemoveSceneObject(ulong sceneKey)
    {
        _pendingSceneObjects.AddOrUpdate(
            sceneKey,
            (PrimRenderSubmission?)null,
            (_, displaced) => { if (displaced != null) DisposeFaceBitmaps(displaced); return null; });
        RequestRender();
    }

    /// <summary>Remove all scene objects (e.g. on sim change). Safe to call from any thread.</summary>
    public void ClearAllSceneObjects()
    {
        _pendingClearScene = true;
        RequestRender();
    }

    /// <summary>
    /// Queue a transform-only update for every (non-flexi) face belonging to
    /// <paramref name="sceneKey"/>, applied on the next render without a full mesh re-upload.
    /// Safe to call from any thread. <c>SetSceneObjectMotion</c>'s dead-reckoning wrapper around this is the
    /// later motion-extrapolation sub-phase, not ported here.
    /// </summary>
    public void SetSceneObjectTransform(ulong sceneKey, Matrix4x4 transform)
    {
        _pendingTransformOverrides.Enqueue((sceneKey, transform));
        RequestRender();
    }

    /// <inheritdoc cref="ISceneViewport.RebaseSceneObjectTransforms"/>
    public void RebaseSceneObjectTransforms(IReadOnlyCollection<ulong> sceneKeys, Vector3 delta)
    {
        if (delta == Vector3.Zero) return;
        foreach (var key in sceneKeys)
            _pendingSceneRebases.Enqueue((key, delta));
        RequestRender();
    }

    /// <summary>Records velocity/angular-velocity/acceleration for dead-reckoning extrapolation (see
    /// <see cref="ExtrapolateMovingSceneObjects"/>) when either is non-negligible, or drops the
    /// object from tracking (treated as at rest) when both are ~zero. Either way, the exact
    /// received pose is landed immediately via <see cref="SetSceneObjectTransform"/> so the
    /// object doesn't wait a frame to reflect the update -- extrapolation only diverges forward
    /// from this point.</summary>
    public void SetSceneObjectMotion(ulong sceneKey, Vector3 scale, Quaternion rotation, Vector3 position,
        Vector3 velocity, Vector3 angularVelocity, Vector3 acceleration)
    {
        const float epsilon = 1e-4f;
        if (velocity.LengthSquared() > epsilon || angularVelocity.LengthSquared() > epsilon)
        {
            _sceneObjectMotion[sceneKey] = new SceneObjectMotion(
                scale, rotation, position, velocity, angularVelocity, acceleration, Environment.TickCount64);
        }
        else
        {
            _sceneObjectMotion.TryRemove(sceneKey, out _);
        }

        var transform = Matrix4x4.CreateScale(scale)
                      * Matrix4x4.CreateFromQuaternion(rotation)
                      * Matrix4x4.CreateTranslation(position);
        SetSceneObjectTransform(sceneKey, transform);
    }

    /// <summary>Part of both <see cref="ISceneViewport"/> and <see cref="ISingleObjectViewport"/>:
    /// real consumers are <c>SceneAvatarAnimator.AnimTick</c> and
    /// <c>AvatarViewerViewModel.AnimTick</c>, wired as part of replacing their CPU-only fallback
    /// with real GPU compute dispatch. Thread-safe -- no-op when compute is unavailable on this
    /// device.</summary>
    public void ScheduleSkinCompute(VkSkinComputeJob job) => _skinDeformer?.Enqueue(job);

    /// <summary>Part of both <see cref="ISceneViewport"/> and <see cref="ISingleObjectViewport"/>:
    /// real consumers are <c>FlexiPrimAnimator.TickAndUpload</c>'s scene-object and AvatarViewer
    /// GPU paths. Thread-safe, no-op when <see cref="_flexiDeformer"/> is unavailable
    /// (best-effort init, same posture as <see cref="_skinDeformer"/>).</summary>
    public void ScheduleFlexiCompute(VkFlexiComputeJob job) => _flexiDeformer?.Enqueue(job);

    /// <summary>
    /// Submit (or, with <see langword="null"/>, remove) a particle emitter's current snapshot,
    /// keyed by <paramref name="key"/>. Safe to call from any thread. Uses a
    /// plain-indexer-assignment
    /// shape -- unlike <see cref="SubmitSceneObject"/>'s <c>AddOrUpdate</c>-with-dispose-callback,
    /// this does not dispose a displaced-but-not-yet-drained
    /// submission's <see cref="ParticleRenderSubmission.Texture"/> bitmap either -- this is a
    /// real, pre-existing GL characteristic (a rapid resubmit before the render thread drains the
    /// previous one could leak a bitmap), ported as-is rather than silently "fixed" here.
    /// </summary>
    public void SubmitParticles(ulong key, ParticleRenderSubmission? sub)
    {
        _pendingParticleMap[key] = sub;
        RequestRender();
    }

    /// <summary>Remove a previously submitted particle emitter by key.</summary>
    public void RemoveParticles(ulong key) => SubmitParticles(key, null);

    private static void DisposeFaceBitmaps(PrimRenderSubmission sub)
    {
        foreach (var face in sub.Faces)
        {
            face.Texture?.Dispose();
            face.NormalMapTexture?.Dispose();
            face.SpecularMapTexture?.Dispose();
            face.MetallicRoughnessTexture?.Dispose();
            face.EmissiveTexture?.Dispose();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var selfVisual = ElementComposition.GetElementVisual(this)!;
            _compositor = selfVisual.Compositor;
            _surface = _compositor.CreateDrawingSurface();
            _visual = _compositor.CreateSurfaceVisual();
            _visual.Size = new Avalonia.Vector(Bounds.Width, Bounds.Height);
            _visual.Surface = _surface;
            ElementComposition.SetElementChildVisual(this, _visual);

            // ConfigureAwait(false) on both awaits below: without it, this method's continuation
            // resumes on the UI thread (since OnAttachedToVisualTree calls InitializeAsync from
            // there), so every GPU-resource-creation call between here and the end of this method
            // (VkPlaceholderTextures, VkCloudNoiseTexture, VkSsaoNoiseTexture, etc. -- all of
            // which submit command buffers via vk.Pool) previously ran on the UI thread instead
            // of the render thread. That's a real, pre-existing hazard: those submits could race
            // another already-initialized panel's RenderFrame (which runs on the render thread)
            // calling Submit() concurrently on the same shared VkQueue -- now safe regardless
            // thanks to VkCommandBufferPool's new _queueLock, but there's no reason to keep this
            // continuation pinned to the UI thread once that's the case. InitFailed invocations
            // below are explicitly posted back to the UI thread (matching SceneReset's existing
            // pattern further down) since their subscribers (SceneViewerViewModel etc.) set
            // bound ViewModel properties, which Avalonia's binding system expects on the UI
            // thread -- everything else in this method is pure Vulkan/field-state, safe from any
            // thread.
            var interop = await _compositor.TryGetCompositionGpuInterop().ConfigureAwait(false);
            if (interop == null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    InitFailed?.Invoke("Compositor doesn't support GPU interop for the current rendering backend."));
                return;
            }

            var (success, info) = await VkApi.EnsureInitializedAsync(interop).ConfigureAwait(false);
            if (!success)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => InitFailed?.Invoke(info));
                return;
            }

            var vk = VkApi.Context;
            // best-effort, matching every other optional resource here -- Initialize
            // already swallows its own failures internally (falls back to a CPU-only-timing
            // tracker rather than throwing).
            _stats.Initialize(vk);
            _renderPass = VkRenderPass.CreateMainScenePass(vk, Format.R8G8B8A8Unorm, Format.D32Sfloat);
            _swapchain = new VkInteropSwapchain(vk, interop, _surface, _reapRing);
            _prim = VkPrimPipeline.Create(vk, _renderPass);
            _wireframe = VkWireframePipeline.Create(vk, _renderPass);
            _outline = VkOutlinePipeline.Create(vk, _renderPass);
            _pick = VkPickPipeline.Create(vk, _renderPass);
            _placeholders = new VkPlaceholderTextures(vk);
            _frameSets = new VkPrimDescriptorSets(vk, _prim, _placeholders);
            _instanceDrawer = new VkInstanceDrawer(vk);

            // Best-effort: a sky-pipeline/cloud-texture
            // creation failure shouldn't take down the whole panel, just leave the sky dome
            // unavailable (falls back to a flat BackgroundColor clear, same as GL's
            // ShowSky && !_skyReady branch).
            try
            {
                _skyPipeline = VkSkyPipeline.Create(vk, _renderPass, _prim.PerFrameLayout);
                _cloudNoiseTex = new VkCloudNoiseTexture(vk);
                _skySet = new VkSkyDescriptorSet(vk, _skyPipeline, _cloudNoiseTex);
                _skyReady = true;
            }
            catch
            {
                _skySet?.Dispose(); _skySet = null;
                _cloudNoiseTex?.Dispose(); _cloudNoiseTex = null;
                _skyPipeline?.Dispose(); _skyPipeline = null;
                _skyReady = false;
            }

 // best-effort matching GL's own EnsureGbufferFbo/EnsureSsaoFbos
            // completeness-check posture (see _ssaoReady's own field comment) -- a pipeline/
            // descriptor-set creation failure here disables SSAO for this panel instance,
            // not the whole panel. The per-size targets themselves (G-buffer/SSAO/blur images)
            // are created lazily in RenderFrame via EnsureGBufferTarget/EnsureSsaoTargets
            // (mirrors _depthImage/EnsureDepthTarget's own lazy-per-size pattern) -- only the
            // size-independent pipeline objects and the render pass objects are created here.
            try
            {
                _gbufferRenderPass = VkRenderPass.CreateGBufferPass(vk, Format.R8G8B8A8Unorm, Format.D32Sfloat);
                _ssaoOffscreenRenderPass = VkRenderPass.CreateOffscreenColorPass(vk, Format.R8Unorm);
                _gnorm = VkGNormPipeline.Create(vk, _gbufferRenderPass, _prim.PerFrameLayout);
                _ssaoPipeline = VkSsaoPipeline.Create(vk, _ssaoOffscreenRenderPass);
                _ssaoBlurPipeline = VkSsaoBlurPipeline.Create(vk, _ssaoOffscreenRenderPass);
                _ssaoNoiseTex = new VkSsaoNoiseTexture(vk);
                _ssaoDescSet = new VkSsaoDescriptorSet(vk, _ssaoPipeline, _ssaoBlurPipeline, _ssaoNoiseTex);
                _ssaoKernel = BuildSsaoKernel(32);

                var nearestInfo = new SamplerCreateInfo
                {
                    SType = StructureType.SamplerCreateInfo,
                    MagFilter = Filter.Nearest,
                    MinFilter = Filter.Nearest,
                    MipmapMode = SamplerMipmapMode.Nearest,
                    AddressModeU = SamplerAddressMode.ClampToEdge,
                    AddressModeV = SamplerAddressMode.ClampToEdge,
                    AddressModeW = SamplerAddressMode.ClampToEdge,
                    MinLod = 0,
                    MaxLod = 0
                };
                unsafe
                {
                    vk.Api.CreateSampler(vk.Device, in nearestInfo, null, out _ssaoNearestSampler).ThrowOnError();
                }
                var linearInfo = nearestInfo with { MagFilter = Filter.Linear, MinFilter = Filter.Linear, MipmapMode = SamplerMipmapMode.Linear };
                unsafe
                {
                    vk.Api.CreateSampler(vk.Device, in linearInfo, null, out _ssaoLinearSampler).ThrowOnError();
                }

                _ssaoReady = true;
            }
            catch
            {
                unsafe
                {
                    if (_ssaoNearestSampler.Handle != 0) { vk.Api.DestroySampler(vk.Device, _ssaoNearestSampler, null); _ssaoNearestSampler = default; }
                    if (_ssaoLinearSampler.Handle != 0) { vk.Api.DestroySampler(vk.Device, _ssaoLinearSampler, null); _ssaoLinearSampler = default; }
                    if (_ssaoOffscreenRenderPass.Handle != 0) { vk.Api.DestroyRenderPass(vk.Device, _ssaoOffscreenRenderPass, null); _ssaoOffscreenRenderPass = default; }
                    if (_gbufferRenderPass.Handle != 0) { vk.Api.DestroyRenderPass(vk.Device, _gbufferRenderPass, null); _gbufferRenderPass = default; }
                }
                _ssaoDescSet?.Dispose(); _ssaoDescSet = null;
                _ssaoNoiseTex?.Dispose(); _ssaoNoiseTex = null;
                _ssaoBlurPipeline?.Dispose(); _ssaoBlurPipeline = null;
                _ssaoPipeline?.Dispose(); _ssaoPipeline = null;
                _gnorm?.Dispose(); _gnorm = null;
                _ssaoReady = false;
            }

            // directional shadow pass. Best-effort, same posture as sky/SSAO --
            // a pipeline/target creation failure disables shadows for this panel instance
            // (ShadowsEnabled stays whatever the caller set, but _shadowReady gates every use).
            // Placed BEFORE the water block below so CreateShadowTarget's UpdateShadowMap call
            // has a real map to hand _frameSetsRefl if water init creates it afterward in the
            // SAME pass (see that block's own null-conditional patch call).
            try
            {
                _shadowRenderPass = VkRenderPass.CreateShadowDepthPass(vk, Format.D32Sfloat);
                _shadowPipeline = VkShadowPipeline.Create(vk, _shadowRenderPass, _prim!.PerFrameLayout);
                CreateShadowTarget(vk);
                _shadowReady = true;
            }
            catch
            {
                unsafe { DestroyShadowTarget(vk); }
                _shadowPipeline?.Dispose(); _shadowPipeline = null;
                if (_shadowRenderPass.Handle != 0) { unsafe { vk.Api.DestroyRenderPass(vk.Device, _shadowRenderPass, null); } }
                _shadowRenderPass = default;
                _shadowReady = false;
            }

            // water surface + reflection pre-pass. Best-effort, matching GL's own
            // InitWater posture (a reflection-FBO completeness failure still leaves the water
            // surface itself drawable via the analytic sky-gradient reflection fallback -- see
            // _waterReady vs. _waterReflReady's separate gates). Unlike GL, a Vulkan resource
            // creation failure throws rather than returning a status code, so this whole block
            // (textures, reflection target, pipeline, descriptor sets) is one try/catch; if
            // anything fails, water is disabled entirely for this panel rather than attempting
            // GL's more granular "reflection failed but surface still works" split -- an accepted
            // scope narrowing since Vulkan resource failures this early (missing embedded asset,
            // out of device memory) are not expected to be the reflection-FBO-specific
            // completeness failures GL's own split was designed around.
            try
            {
                _waterNormalTex = LoadEmbeddedWaterTexture(vk, "normalmap.png");
                _waterDudvTex = LoadEmbeddedWaterTexture(vk, "dudvmap.png");
                if (_waterNormalTex == null || _waterDudvTex == null)
                    throw new InvalidOperationException("Failed to decode an embedded water texture asset.");

                CreateWaterReflectionTarget(vk);
                _waterPipeline = VkWaterPipeline.Create(vk, _renderPass, _prim!.PerFrameLayout);
                var reflTexInfo = new DescriptorImageInfo
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = _waterReflColorView,
                    Sampler = _waterReflColorSampler
                };
                _waterDescSet = new VkWaterDescriptorSet(vk, _waterPipeline,
                    reflTexInfo, _waterNormalTex.DescriptorImageInfo, _waterDudvTex.DescriptorImageInfo);

                // Own PerFrame descriptor set for the reflection pass -- see this field's own
                // doc comment for why sharing _frameSets would be wrong. WritePassSetPlaceholders
                // (inside the constructor) writes the PLACEHOLDER shadow map to its binding 1;
                // patch it to the real one immediately if shadows already initialized above.
                _frameSetsRefl = new VkPrimDescriptorSets(vk, _prim, _placeholders!);
                if (_shadowReady) _frameSetsRefl.UpdateShadowMap(_shadowMapInfo);

                _waterLastTick = Environment.TickCount64;
                _waterReady = true;
                _waterReflReady = true;
            }
            catch (Exception waterInitEx)
            {
                // If any step above throws, _waterReady stays false and DrawWater is never
                // called again for the rest of the panel's life -- log so that's diagnosable.
                LibreMetaverse.Logger.Warn(
                    $"[VkViewportControl] Water init failed, water surface disabled for this panel: {waterInitEx}");
                _frameSetsRefl?.Dispose(); _frameSetsRefl = null;
                _waterDescSet?.Dispose(); _waterDescSet = null;
                _waterPipeline?.Dispose(); _waterPipeline = null;
                unsafe { DestroyWaterReflectionTarget(vk); }
                _waterNormalTex?.Dispose(); _waterNormalTex = null;
                _waterDudvTex?.Dispose(); _waterDudvTex = null;
                _waterReady = false;
                _waterReflReady = false;
            }

            // Underwater post-process pass. Best-effort, same posture as the blocks above.
            // Depends on water having initialized successfully (reuses _waterNormalTex/
            // _waterDudvTex for its distortion/caustic samples -- see underwater.frag), so this
            // must run after the water block above and is itself gated on _waterReady: without
            // real water textures, there's no water surface to be "under" in the first place.
            // The source-copy target (_underwaterSourceImage) is created lazily per-size in
            // RenderFrame via EnsureUnderwaterTarget, mirroring the SSAO targets' own lazy
            // pattern -- only the size-independent pipeline/render-pass/descriptor-set objects
            // are created here.
            if (_waterReady)
            {
                try
                {
                    _underwaterPass = VkRenderPass.CreateUnderwaterPass(vk, Format.R8G8B8A8Unorm);
                    _underwaterPipeline = VkUnderwaterPipeline.Create(vk, _underwaterPass);
                    var underwaterSamplerInfo = new SamplerCreateInfo
                    {
                        SType = StructureType.SamplerCreateInfo,
                        MagFilter = Filter.Linear,
                        MinFilter = Filter.Linear,
                        MipmapMode = SamplerMipmapMode.Linear,
                        AddressModeU = SamplerAddressMode.ClampToEdge,
                        AddressModeV = SamplerAddressMode.ClampToEdge,
                        AddressModeW = SamplerAddressMode.ClampToEdge,
                        MinLod = 0,
                        MaxLod = 0
                    };
                    unsafe
                    {
                        vk.Api.CreateSampler(vk.Device, in underwaterSamplerInfo, null, out _underwaterLinearSampler).ThrowOnError();
                    }
                    _underwaterDescSet = new VkUnderwaterDescriptorSet(vk, _underwaterPipeline,
                        _waterNormalTex!.DescriptorImageInfo, _waterDudvTex!.DescriptorImageInfo);
                    _underwaterReady = true;
                }
                catch (Exception underwaterInitEx)
                {
                    LibreMetaverse.Logger.Warn(
                        $"[VkViewportControl] Underwater post-process init failed, disabled for this panel: {underwaterInitEx}");
                    _underwaterDescSet?.Dispose(); _underwaterDescSet = null;
                    unsafe
                    {
                        if (_underwaterLinearSampler.Handle != 0) { vk.Api.DestroySampler(vk.Device, _underwaterLinearSampler, null); _underwaterLinearSampler = default; }
                        if (_underwaterPass.Handle != 0) { vk.Api.DestroyRenderPass(vk.Device, _underwaterPass, null); }
                    }
                    _underwaterPipeline?.Dispose(); _underwaterPipeline = null;
                    _underwaterPass = default;
                    _underwaterReady = false;
                }
            }

            // Best-effort: a compute-pipeline-creation failure (e.g. no
            // compute-capable queue on some driver) shouldn't take down the whole panel, just
            // leave GPU skin deformation unavailable (falls back to nothing, same as GL falling
            // back to CPU LBS -- AvatarViewer already always uses CPU LBS regardless, see this
            // field's own doc comment).
            try
            {
                _skinPipeline = VkSkinPipeline.Create(vk);
                _skinDeformer = new VkSkinDeformer(vk, _skinPipeline);
            }
            catch
            {
                _skinPipeline?.Dispose();
                _skinPipeline = null;
                _skinDeformer = null;
            }

            // same best-effort posture as the skin block immediately above.
            try
            {
                _flexiPipeline = VkFlexiPipeline.Create(vk);
                _flexiDeformer = new VkFlexiDeformer(vk, _flexiPipeline);
            }
            catch
            {
                _flexiPipeline?.Dispose();
                _flexiPipeline = null;
                _flexiDeformer = null;
            }

            // same best-effort posture as the skin/flexi blocks above.
            try
            {
                _particlePipeline = VkParticlePipeline.Create(vk, _renderPass);
                _particleBuf = new VkParticleBuffer(vk);
            }
            catch
            {
                _particlePipeline?.Dispose();
                _particlePipeline = null;
                _particleBuf = null;
            }

            _attached = true;
            if (!_countedAsLiveInstance)
            {
                _countedAsLiveInstance = true;
                System.Threading.Interlocked.Increment(ref s_liveInstanceCount);
            }
            RequestRender();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SceneReset?.Invoke());
        }
        catch (Exception e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => InitFailed?.Invoke(e.ToString()));
        }
        finally
        {
            // Pairs with OnAttachedToVisualTree's _initializing guard -- must clear on every exit
            // path (success, early return, or exception), not just the happy path, or a
            // failed/aborted init would permanently block every future reattach from ever trying
            // again.
            _initializing = false;
        }
    }

    /// <summary>
    /// Replaces <see cref="_opaqueFaces"/>/<see cref="_alphaFaces"/> with meshes, real
    /// uploaded textures, and a real per-face <see cref="VkMaterialDescriptorSet"/> built from
    /// <paramref name="submission"/>. Runs on the render thread only (called from
    /// <see cref="RenderFrame"/>) -- see the <see cref="_pendingSubmission"/> field comment
    /// (the same reasoning applies to <see cref="VkTexture"/>'s upload, which also submits a
    /// command buffer through <c>vk.Pool</c>). Picking is still a later increment.
    /// </summary>
    private unsafe void ApplyPendingSubmission(VkContext vk, PrimRenderSubmission submission)
    {
        // Cross-submission texture inheritance: a re-Submit() (e.g. AvatarViewerViewModel's
        // ScheduleReload, triggered by a mid-build appearance event) races its own
        // StreamTexturesAsync patches against ApplyPendingSubmission -- patches for the NEW
        // submission can land (via DrainSubmissionTexturePatches) on the OLD, about-to-be-replaced
        // _faceTextureSlots before this method's own rebuild runs, then get silently wiped when it
        // does, with the streaming layer never re-delivering (each UUID reports its patches once).
        // GL's own UploadSubmission carries an equivalent inheritedTex mechanism for the same
        // reason.
        //
        // Snapshot from the LIVE _opaqueFaces/_alphaFaces + _faceTextureSlots (not iterated
        // blindly from _faceTextureSlots' own keys) so a stale dict entry for a face no longer in
        // either draw list can't be inherited by mistake. Keyed on (PrimLocalId, FaceIndex, slot)
        // -- the key IS positionally stable across a same-roster rebuild, so is safe here.
        // inheritedAlpha (GL's AlphaAuto-reclassification carry-forward) is deliberately NOT
        // ported this round -- the incoming patch for a Blend face reclassifies it again once
        // redelivered, so this is at most a brief single-frame flash, not a lasting correctness
        // gap, and keeps this diff smaller.
        var inheritedTex = new Dictionary<(uint LocalId, int FaceIndex, int Slot), VkTexture>();
        void SnapshotInherited(List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> list)
        {
            foreach (var (_, _, f) in list)
            {
                if (!_faceTextureSlots.TryGetValue((f.PrimLocalId, f.FaceIndex), out var slots)) continue;
                for (int s = 0; s < slots.Length; s++)
                {
                    var t = slots[s];
                    if (t != null) inheritedTex[(f.PrimLocalId, f.FaceIndex, s)] = t;
                }
            }
        }
        SnapshotInherited(_opaqueFaces);
        SnapshotInherited(_alphaFaces);
        var inheritedSet = new HashSet<VkTexture>(inheritedTex.Values);
        var claimedInherited = new HashSet<VkTexture>();

        // Deferred, not disposed immediately below: this control's single-object viewers
        // (PrimViewer/AvatarViewer/HudViewer) share the same VkFrameReapRing/FramesInFlight=2
        // render loop the scene-streaming path does, so a mesh/material/texture/skin-or-flexi-
        // GPU resource still referenced by an in-flight command buffer up to FramesInFlight-1
        // frames back is exactly as unsafe to free here as it would be in
        // RemoveSceneObjectGpuNoRebuild -- see that method's own MarkPendingDestroy comment for
        // the full reasoning. Snapshotting into local lists BEFORE the live fields are Clear()'d
        // (and, for _opaqueFaces/_alphaFaces/_faceMeshesByPosition, repopulated by the face loop
        // below) is required: a closure over the live fields would instead free the NEW
        // submission's own resources once this frame's slot is reaped.
        var oldOpaque = new List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)>(_opaqueFaces);
        var oldAlpha = new List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)>(_alphaFaces);
        _opaqueFaces.Clear();
        _alphaFaces.Clear();
        // Inherited textures' disposal is deferred until after the face loop below (once it's
        // known which ones were actually claimed by the new submission) -- disposing them here
        // unconditionally would destroy a texture InheritIfNeeded is about to hand to a new face.
        var oldTextures = new List<VkTexture>();
        foreach (var tex in _textures) { if (!inheritedSet.Contains(tex)) oldTextures.Add(tex); }
        _textures.Clear();
        _faceTextureSlots.Clear();
        _faceMeshesByPosition.Clear();
        _facesByPosition.Clear();
        var oldSkinGpu = new List<VkAvatarSkinGpuData>(_submissionSkinGpu);
        _submissionSkinGpu.Clear();
        var oldFlexiGpu = new List<VkFlexiGpuData>(_submissionFlexiGpu);
        _submissionFlexiGpu.Clear();
        _reapRing.MarkPendingDestroy(() =>
        {
            foreach (var (mesh, material, _) in oldOpaque) { mesh.Dispose(); material.Dispose(); }
            foreach (var (mesh, material, _) in oldAlpha) { mesh.Dispose(); material.Dispose(); }
            foreach (var tex in oldTextures) tex.Dispose();
            foreach (var gd in oldSkinGpu) gd.Dispose();
            foreach (var gd in oldFlexiGpu) gd.Dispose();
        });
        // Any updates still queued target the OLD _faceMeshesByPosition/_facesByPosition array-
        // position indexing -- meaningless (or worse, silently wrong: applying to an unrelated
        // face at the same position) against the NEW lists about to be built.
        while (_pendingVertexUpdates.TryDequeue(out _)) { }
        while (_pendingFaceTransformUpdates.TryDequeue(out _)) { }

        // submissions carrying skin/animesh data get their vertex buffers rewritten
        // every animation tick (AvatarViewerViewModel.AnimTick -> ScheduleVertexUpdate), so
        // those faces' meshes need host-visible-direct-map (dynamic: true) rather than the
        // device-local-via-staging default -- VkMesh.UpdateVertices on a dynamic:false mesh
        // re-stages (destroy+recreate+fence-wait) on EVERY call, which would turn per-frame
        // animation into a per-frame full GPU-buffer-recreation stall. The subAnimated flag
        // corresponds to GL's own equivalent flag -- GL's reasoning (VBO usage hint) doesn't carry over 1:1 since GL's
        // STATIC_DRAW/DYNAMIC_DRAW is just a hint with no hard cost difference for BufferSubData,
        // but the flag and its face-level application (face.IsFlexi || subAnimated) are the same.
        bool subAnimated = submission.SkinData.Length > 0 || submission.AnimeshSkinData.Length > 0;

        // Dedup uploads: multiple faces may reference the same SKBitmap instance (e.g. a
        // linkset's faces sharing one prim texture), handled via the texCache/TryUpload pattern
        // below. VkTexture's constructor takes ownership of (and
        // disposes) whatever bitmap it's given, so the first upload for a given handle consumes
        // it; later faces referencing the same handle just reuse the already-created VkTexture
        // and never see their own (by-value-identical, since the builder shares one SKBitmap
        // instance across such faces) Texture field disposed a second time.
        var texCache = new Dictionary<IntPtr, VkTexture>();
        VkTexture? TryUpload(SKBitmap? bmp)
        {
            if (bmp is null) return null;
            var handle = bmp.Handle;
            if (texCache.TryGetValue(handle, out var cached)) return cached;
            try
            {
                var t = new VkTexture(vk, VkTexture.Preprocess(bmp));
                texCache[handle] = t;
                _textures.Add(t);
                return t;
            }
            catch
            {
                // A malformed/
                // unsupported bitmap shouldn't take down the whole submission, just that
                // face's texture (falls back to the placeholder). VkTexture.Preprocess disposes
                // bmp on every path (including its own failure path), so nothing further to
                // clean up here.
                return null;
            }
        }

        foreach (var face in submission.Faces)
        {
            // Preserves _faceMeshesByPosition's array-position alignment with submission.Faces
            // even when a face is skipped -- ScheduleVertexUpdate's faceIndex is a raw array
            // position (see its own doc comment), so a skipped entry must still occupy a slot,
            // not silently shift every later face's index down by one. _facesByPosition gets
            // the same treatment (added unconditionally, unlike _faceMeshesByPosition which
            // still adds null below for a Vertices==null face) since ScheduleFaceTransformUpdate
            // shares this exact indexing and a face can be transform-updateable even when it has
            // no mesh of its own to skin.
            _facesByPosition.Add(face);
            if (face.Vertices == null) { _faceMeshesByPosition.Add(null); continue; }

            int vLen = face.VerticesLength > 0 ? face.VerticesLength : face.Vertices.Length;
            var mesh = new VkMesh(vk, face.Vertices, vLen, face.Indices, dynamic: face.IsFlexi || subAnimated);
            _faceMeshesByPosition.Add(mesh);
            // Required for picking's CPU-side ray-triangle intersection (ComputeHitInfo) --
            // must run BEFORE face.Vertices is released below, since PickerFromInterleaved/
            // NormalUvFromInterleaved both read from that same source data.
            face.PickerVertices = PickerFromInterleaved(face.Vertices, vLen);
            face.NormalUvVertices = NormalUvFromInterleaved(face.Vertices, vLen);
            face.Vertices = null; // release the LOH array once it's no longer needed

            VkTexture? InheritIfNeeded(VkTexture? fresh, int slot)
            {
                if (fresh != null) return fresh;
                if (!inheritedTex.TryGetValue((face.PrimLocalId, face.FaceIndex, slot), out var inherited)) return null;
                claimedInherited.Add(inherited);
                return inherited;
            }

            var albedo = InheritIfNeeded(TryUpload(face.Texture), 0);
            var normal = InheritIfNeeded(TryUpload(face.NormalMapTexture), 1);
            var specular = InheritIfNeeded(TryUpload(face.SpecularMapTexture), 2);
            var metallicRoughness = InheritIfNeeded(TryUpload(face.MetallicRoughnessTexture), 3);
            var emissive = InheritIfNeeded(TryUpload(face.EmissiveTexture), 4);

            var material = new VkMaterialDescriptorSet(vk, _prim!,
                albedo?.DescriptorImageInfo ?? _placeholders!.White,
                normal?.DescriptorImageInfo ?? _placeholders!.FlatNormal,
                specular?.DescriptorImageInfo ?? _placeholders!.White,
                metallicRoughness?.DescriptorImageInfo ?? _placeholders!.White,
                emissive?.DescriptorImageInfo ?? _placeholders!.Black,
                BuildMaterialUbo(face, albedo != null, normal != null, specular != null,
                    metallicRoughness != null, emissive != null));

            // Indices match TextureSlot's own values (Albedo=0..Emissive=4) and
            // VkMaterialDescriptorSet's binding order -- see PatchSubmissionTexture's
            // ApplySubmissionPatchIfReady, the only reader of this table.
            _faceTextureSlots[(face.PrimLocalId, face.FaceIndex)] =
                new[] { albedo, normal, specular, metallicRoughness, emissive };

            (face.HasAlpha ? _alphaFaces : _opaqueFaces).Add((mesh, material, face));
        }

        // Claimed inherited textures are re-added to _textures so a future ApplyPendingSubmission's
        // disposal sweep accounts for them. Whatever's left in inheritedSet was NOT claimed (its
        // face no longer exists in this submission) -- deferred disposal, not immediate, for the
        // same in-flight-command-buffer reason as this method's own earlier MarkPendingDestroy
        // (the new submission's faces built just above may already be live in a command buffer
        // this same frame by the time this slot's reap actually runs).
        foreach (var tex in claimedInherited) _textures.Add(tex);
        var unclaimedInherited = new List<VkTexture>();
        foreach (var tex in inheritedSet) { if (!claimedInherited.Contains(tex)) unclaimedInherited.Add(tex); }
        if (unclaimedInherited.Count > 0)
            _reapRing.MarkPendingDestroy(() => { foreach (var tex in unclaimedInherited) tex.Dispose(); });

        // Register avatar skin GPU resources for compute LBS. Runs after the face loop above
        // (not interleaved into it) since a skinned face's FaceIndex can reference any position
        // in submission.Faces, not just faces already processed.
        //
        // Bounds-checks skin.FaceIndex before indexing and skips a null entry (a face whose
        // Vertices were null, see the face loop's own placeholder-null comment) -- a deliberate,
        // low-risk robustness improvement, not a behavior difference for any valid submission.
        // Each Create() gets its own try/catch -- a pool-exhaustion failure on one face should
        // leave that face on the CPU fallback, not propagate out of ApplyPendingSubmission and
        // fail the whole submission.
        if (_skinDeformer != null)
        {
            // Rigid single-bone attachment faces (rings, hair strands, jewelry -- see
            // AvatarFaceSkinData.IsRigidSingleBone's own doc comment) are skipped entirely here --
            // AnimTick drives them via ScheduleFaceTransformUpdate instead of per-vertex LBS, so
            // registering GPU skin-compute resources for them would waste a descriptor set and
            // inflate VkSkinDeformer's per-frame pending set past MaxJobsPerFrame, starving
            // whichever faces the cap left behind each frame.
            foreach (var skin in submission.SkinData)
            {
                if (skin.IsRigidSingleBone) continue;

                if (skin.FaceIndex < 0 || skin.FaceIndex >= _faceMeshesByPosition.Count) continue;
                var faceMesh = _faceMeshesByPosition[skin.FaceIndex];
                if (faceMesh == null) continue;

                try
                {
                    var gpuData = VkAvatarSkinGpuData.Create(vk, _skinPipeline!, skin, faceMesh);
                    skin.GpuData = gpuData;
                    _submissionSkinGpu.Add(gpuData);
                }
                catch (Exception e)
                {
                    LibreMetaverse.Logger.Warn($"[VkViewportControl] Face {skin.FaceIndex} skin GPU "
                        + $"registration failed, staying on CPU skinning: {e.Message}");
                }
            }
        }

        // Flexi GPU compute registration. Same placement/bounds-checking/try-catch reasoning as
        // the skin registration immediately above.
        if (_flexiDeformer != null)
        {
            foreach (var fp in submission.FlexiPrims)
            {
                if (fp.FaceStart < 0 || fp.FaceStart + fp.FaceCount > _faceMeshesByPosition.Count) continue;
                var meshes = new VkMesh[fp.FaceCount];
                bool allPresent = true;
                for (int fi = 0; fi < fp.FaceCount; fi++)
                {
                    var m = _faceMeshesByPosition[fp.FaceStart + fi];
                    if (m == null) { allPresent = false; break; }
                    meshes[fi] = m;
                }
                if (!allPresent) continue;
                try
                {
                    var gpuData = VkFlexiGpuData.Create(vk, _flexiPipeline!, fp, meshes);
                    fp.GpuData = gpuData;
                    _submissionFlexiGpu.Add(gpuData);
                }
                catch (Exception e)
                {
                    LibreMetaverse.Logger.Warn($"[VkViewportControl] Flexi prim (faces {fp.FaceStart}.."
                        + $"{fp.FaceStart + fp.FaceCount}) GPU registration failed, staying on CPU deformation: {e.Message}");
                }
            }
        }

        // Bounds are
        // always refreshed, but the camera itself is only reframed on an explicit ResetCamera
        // call (or SubmitAvatarFront, handled immediately below) -- a live re-tessellation of
        // the same object shouldn't yank the camera back to a default framing.
        _lastBoundsMin = submission.BoundsMin;
        _lastBoundsMax = submission.BoundsMax;

        // Consumes _frameAvatarFrontPending/_frameFrontPending, including the
        // if/else-if precedence (avatar-front wins if somehow both were set) -- only
        // SubmitAvatarFront/SubmitFront set these flags, and they're consumed (and reset) here
        // rather than acted on synchronously inside those methods, since this control's bounds
        // aren't known until the submission is actually applied on the render thread.
        if (_frameAvatarFrontPending)
        {
            _frameAvatarFrontPending = false;
            _camera.FrameBoundsAvatarFront(submission.BoundsMin, submission.BoundsMax);
        }
        else if (_frameFrontPending)
        {
            _frameFrontPending = false;
            _camera.FrameBoundsFront(submission.BoundsMin, submission.BoundsMax);
        }
    }

    /// <summary>
    /// drains <see cref="_pendingSceneObjects"/> under a 6ms-per-frame time
    /// budget (always admits at least one upload so progress is guaranteed even when a single
    /// upload is expensive), including requesting another render tick when
    /// the budget is exhausted with work still pending. Runs on the render thread only, called
    /// from the top of <see cref="RenderFrame"/>.
    /// </summary>
    private void DrainPendingSceneObjects(VkContext vk)
    {
        if (_pendingClearScene)
        {
            _pendingClearScene = false;
            // FreeSceneObjectResources only enqueues removals (actual per-object Vulkan
            // disposal is spread across subsequent frames via DrainPendingSceneObjects'
            // budget) -- this should stay a small, fast enqueue loop regardless of scene size.
            int objCountBeforeClear = _sceneObjects.Count;
            var clearStopwatch = System.Diagnostics.Stopwatch.StartNew();
            LibreMetaverse.Logger.Debug(
                $"[VkViewportControl] FreeSceneObjectResources starting, {objCountBeforeClear} objects.");
            FreeSceneObjectResources();
            LibreMetaverse.Logger.Debug(
                $"[VkViewportControl] FreeSceneObjectResources done (enqueue only now), "
                + $"{objCountBeforeClear} objects, {clearStopwatch.Elapsed.TotalMilliseconds:F1}ms.");
        }

        bool sceneListDirty = false;
        if (!_pendingSceneObjects.IsEmpty)
        {
            long uploadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
            int uploadsThisFrame = 0;
            // ConcurrentDictionary.Keys is a snapshot enumerable; TryRemove is safe mid-loop.
            foreach (var key in _pendingSceneObjects.Keys)
            {
                if (uploadsThisFrame > 0 &&
                    (System.Diagnostics.Stopwatch.GetTimestamp() - uploadStart) / ticksPerMs >= SceneWorkBudgetMs)
                    break;
                if (!_pendingSceneObjects.TryRemove(key, out var sub)) continue;
                if (sub == null)
                {
                    RemoveSceneObjectGpuNoRebuild(key);
                    sceneListDirty = true;
                }
                // A cooldown-parked re-add (see UploadSceneObjectNoRebuild's own comment) puts
                // `key` back into _pendingSceneObjects, but .Keys above is a fixed snapshot taken
                // before this loop started, so it is not revisited this frame -- no risk of a
                // same-frame infinite loop. Only mark the flat draw lists dirty when something
                // actually changed (return true), not on a no-op park.
                else if (UploadSceneObjectNoRebuild(vk, key, sub))
                {
                    sceneListDirty = true;
                }
                uploadsThisFrame++;
            }
        }
        // If the budget cap was hit, request another render tick to continue draining.
        if (!_pendingSceneObjects.IsEmpty)
            RequestRender();
        if (sceneListDirty)
            RebuildSceneFlatLists();
    }

    /// <summary>
    /// drains <see cref="_pendingParticleMap"/> into <see cref="_particleMap"/>,
    /// including its texture-
    /// inheritance behavior (keep the old texture when this tick brings none -- load-bearing for
    /// an emitter whose texture download hasn't landed yet). Called from the top of RenderFrame,
    /// BEFORE the render pass begins, for the same reason <see cref="_particlePipeline"/>'s own
    /// header comment states: <see cref="VkParticlePipeline.GetOrCreate"/> creates a VkPipeline
    /// on cache miss, which is illegal to do mid-render-pass, so every blend-pair variant this
    /// frame's live emitters need is pre-warmed here rather than lazily inside DrawParticles.
    /// </summary>
    private unsafe void DrainPendingParticles(VkContext vk)
    {
        if (_particlePipeline == null || _particleBuf == null) return;
        if (_pendingParticleMap.IsEmpty) return;

        // ConcurrentDictionary.Keys is a snapshot enumerable; TryRemove is safe mid-loop (same
        // idiom DrainPendingSceneObjects above already uses).
        foreach (var key in _pendingParticleMap.Keys)
        {
            if (!_pendingParticleMap.TryRemove(key, out var sub)) continue;

            if (sub == null)
            {
                if (_particleMap.TryGetValue(key, out var old))
                {
                    old.Tex?.Dispose();
                    if (old.Set.Handle != 0)
                    {
                        var set = old.Set;
                        vk.Api.FreeDescriptorSets(vk.Device, vk.DescriptorPool, 1, &set);
                    }
                    _particleMap.Remove(key);
                }
                continue;
            }

            VkTexture? newTex = null;
            if (sub.Texture != null)
            {
                try { newTex = new VkTexture(vk, VkTexture.Preprocess(sub.Texture)); }
                catch { }
            }

            DescriptorSet set2;
            if (_particleMap.TryGetValue(key, out var existing))
            {
                // Keep old texture if this tick has no new one.
                newTex ??= existing.Tex;
                bool textureChanged = newTex != existing.Tex;
                if (textureChanged) existing.Tex?.Dispose();
                set2 = existing.Set;
                if (textureChanged)
                {
                    var imageInfo = newTex?.DescriptorImageInfo ?? _placeholders!.White;
                    var write = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = set2,
                        DstBinding = 0,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        DescriptorCount = 1,
                        PImageInfo = &imageInfo
                    };
                    vk.Api.UpdateDescriptorSets(vk.Device, 1, &write, 0, null);
                }
            }
            else
            {
                var setLayout = _particlePipeline.SetLayout;
                var allocInfo = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = vk.DescriptorPool,
                    DescriptorSetCount = 1,
                    PSetLayouts = &setLayout
                };
                // Same reasoning as UploadSceneObjectNoRebuild's own whole-object try/catch
                // (see its doc comment): the shared DescriptorPool is fixed-capacity, and this
                // call ThrowOnError()s at VK_ERROR_OUT_OF_POOL_MEMORY. Unguarded, a single new
                // emitter arriving after the pool is full would propagate out through
                // RenderFrame and take the whole panel down ("Viewport init failed") instead of
                // just dropping the one emitter that didn't fit this tick.
                var allocResult = vk.Api.AllocateDescriptorSets(vk.Device, &allocInfo, out set2);
                if (allocResult != Result.Success)
                {
                    // Dropped silently otherwise: SceneParticleStreamer's ParticleViewerDriver
                    // resubmits every emitter on a 30Hz tick (see its own SubmitEmitter caller),
                    // so this key will simply be retried next tick rather than needing explicit
                    // recovery here -- but a dropped-forever emitter would look identical to one
                    // recovering next frame without this log line to tell them apart.
                    LibreMetaverse.Logger.Warn($"[VkViewportControl] Particle emitter {key} descriptor set alloc failed ({allocResult}), dropping this tick");
                    newTex?.Dispose();
                    continue;
                }
                var imageInfo = newTex?.DescriptorImageInfo ?? _placeholders!.White;
                var write = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set2,
                    DstBinding = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfo
                };
                vk.Api.UpdateDescriptorSets(vk.Device, 1, &write, 0, null);
            }

            _particleMap[key] = (sub, newTex, set2);

            // Pre-warm this submission's blend-pair pipeline variant -- see this method's own
            // doc comment for why it must happen here, not inside DrawParticles.
            _particlePipeline.GetOrCreate(sub.BlendSrc, sub.BlendDst);
        }
    }

    /// <summary>
    /// draws every live particle emitter as camera-facing billboard quads.
    /// Matches GL's DrawParticles for the camera-basis
    /// extraction and per-emitter draw order, but builds ONE combined vertex array covering
    /// every emitter before uploading -- see VkParticleBuffer's own header comment for why GL's
    /// per-emitter upload-then-draw loop doesn't carry over to Vulkan's deferred command-buffer
    /// execution model. Called from the main render pass, after the wireframe overlay (matching
    /// GL's own last-in-the-main-pass placement, right before its BlitSceneToFb call).
    /// </summary>
    private unsafe void DrawParticles(VkContext vk, CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj)
    {
        if (_particlePipeline == null || _particleBuf == null || _particleMap.Count == 0) return;

        // Build camera basis vectors (world space) for CPU billboarding -- copied verbatim from
        // GL, not re-derived: in this Z-up view matrix, right is column 0 and up is column 1
        // (System.Numerics.Matrix4x4's row-major memory layout is the same identity every other
        // matrix upload in this port already relies on needing no transpose).
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up    = new Vector3(view.M12, view.M22, view.M32);

        // Pass 1: compute the total vertex count and each emitter's (firstVertex, vertexCount),
        // growing the CPU scratch array if needed, then expand every emitter into it.
        int totalVerts = 0;
        foreach (var (_, entry) in _particleMap)
            totalVerts += entry.Sub.Particles.Length * VkParticleBuffer.VerticesPerParticle;
        if (totalVerts == 0) return;

        int neededFloats = totalVerts * VkParticleBuffer.FloatsPerVertex;
        if (_particleDataBuf.Length < neededFloats)
            _particleDataBuf = new float[neededFloats];

        var draws = new List<(ulong Key, int FirstVertex, int VertexCount)>(_particleMap.Count);
        int cursor = 0;
        foreach (var (key, entry) in _particleMap)
        {
            var sub = entry.Sub;
            if (sub.Particles.Length == 0) continue;
            int written = VkParticleBuffer.ExpandToVertices(sub.Particles, right, up, _particleDataBuf, cursor);
            draws.Add((key, cursor, written));
            cursor += written;
        }
        if (draws.Count == 0) return;

        // `cursor` is a VERTEX count (it doubles as every draw's FirstVertex, computed in
        // vertex units above) -- Upload takes a FLOAT span, so it must be scaled by
        // FloatsPerVertex here. Uploading `cursor` floats directly under-filled (and
        // under-sized, via EnsureCapacity) the GPU buffer to roughly 1/9th of the real data
        // for any frame with more than one emitter, so every draw after the first read
        // FirstVertex offsets (correct, real vertex units) far past the buffer's actual
        // allocation -- the GPU then fetched whatever memory followed it, rendering as
        // garbage-position/garbage-color triangle bursts.
        _particleBuf.Upload(new ReadOnlySpan<float>(_particleDataBuf, 0, cursor * VkParticleBuffer.FloatsPerVertex));

        var api = vk.Api;
        var vbo = _particleBuf.Vbo;
        ulong offset = 0;
        api.CmdBindVertexBuffers(cmd, 0, 1, &vbo, &offset);

        foreach (var (key, firstVertex, vertexCount) in draws)
        {
            var entry = _particleMap[key];
            var sub = entry.Sub;

            var pipeline = _particlePipeline.GetOrCreate(sub.BlendSrc, sub.BlendDst);
            api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

            var set = entry.Set;
            api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _particlePipeline.Layout, 0, 1, &set, 0, null);

            var mvp = sub.EmitterTransform * view * proj;
            var pc = new VkParticlePushConstants
            {
                Mvp = mvp,
                HasTexture = entry.Tex != null ? 1 : 0,
                // uGlow is hardcoded to 0 here too -- matches GL's own hardcoded
                // _particleShader.Set("uGlow", 0f) exactly (see particle.frag's own doc comment
                // for why this stays a dead uniform, not "fixed" into working glow).
                Glow = 0f
            };
            api.CmdPushConstants(cmd, _particlePipeline.Layout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0, (uint)sizeof(VkParticlePushConstants), &pc);

            api.CmdDraw(cmd, (uint)vertexCount, 1, (uint)firstVertex, 0);
        }
    }

    /// <summary>
    /// Builds real meshes/textures/materials for one scene object's faces, using a per-face
    /// loop (mesh pooling via
    /// <see cref="VertexHash"/>, subAnimated dynamic-mesh selection, picker/normal-UV
    /// extraction) but WITHOUT GL's cross-rebuild texture-inheritance snapshot (deliberately out
    /// of scope, see <see cref="_sceneObjectTextures"/>'s own doc comment) and without
    /// registering flexi/skin GPU compute resources here.
    /// Caller is responsible for calling <see cref="RebuildSceneFlatLists"/> once after a batch.
    /// </summary>
    // The streamer's build task marks an object _rendered as soon as it hands the
    // submission to SubmitSceneObject (enqueue, fire-and-forget) -- it never learns whether the
    // GPU upload drained from that queue actually succeeded, so nothing on the streamer side
    // backs off a chronically-failing object. In a scene dense enough to exceed
    // maxMemoryAllocationCount (see VkMaterialUboPool.cs's header comment for the ceiling this
    // hits), a burst of terse updates / rebuild triggers can resubmit the same doomed object
    // repeatedly, each attempt burning render-thread time on a guaranteed-to-fail alloc before
    // catching the exception. Cooldown is enforced here, self-contained, rather than plumbing a
    // failure callback back through ISceneViewport to two different streamer classes.
    //
    // Exponential, not fixed (2026-08-31 field data: a genuinely saturated VkMaterialUboPool can
    // stay pinned near-capacity for a whole session -- one dense-region log showed 10198 failed-
    // upload log lines from only 668 DISTINCT objects, i.e. each retried ~15x on average, up to
    // 39x, with a flat 5s cooldown doing nothing to stop it since the pool never actually
    // recovered enough headroom in that window). A flat cooldown gives every object, including
    // ones that have already failed dozens of times, equal standing to compete for the same
    // handful of slots that free up each cycle -- exactly the mechanism starving out genuinely
    // new content (and, per the report that motivated this, the self avatar) in a saturated
    // region. Backoff pushes a chronically-failing object further back each time instead,
    // without ever giving up on it outright (ConsecutiveFailures resets to 0 -- entry removed
    // entirely -- on the next successful upload, see the Remove() call sites below).
    private const int SceneUploadFailureBaseCooldownMs = 5000;
    // 5000 * 2^4 = 80000ms (80s) cap -- deliberately a bit above ReconcileUntrackedPrims' own
    // 60s cooldown (SceneObjectStreamer.ReconcileRetryCooldownMs) so a chronically-failing
    // object's own backoff window is never the SHORTER of the two ceilings on how often it gets
    // re-attempted.
    private const int MaxSceneUploadFailureBackoffShift = 4;
    private readonly Dictionary<ulong, (long FailedAt, int ConsecutiveFailures)> _recentSceneUploadFailures = new();

    // Same convention as SceneAvatarStreamer.AvatarKeyOffset / SceneViewerViewModel.AvatarKeyOffset
    // (duplicated there too, not reusable across these classes) -- SceneAvatarStreamer.SceneKey
    // computes an avatar/attachment-owner's rootId as AvatarKeyOffset + localId, so any rootId at
    // or above this offset reached UploadSceneObjectNoRebuild via that path, not a regular prim's
    // MakeSceneKey (which OR's a sim index into the upper 32 bits -- nonzero for every sim except
    // whichever one happened to register first as index 0, where it degenerates to the bare
    // localId too; real SL local IDs never get remotely close to 2^31 in practice, so this
    // boundary check is unambiguous for real content either way).
    private const ulong AvatarUploadSceneKeyOffset = 0x8000_0000UL;

    // Avatar/attachment keys are EXEMPT from the escalation below (always treated as failure #1,
    // i.e. capped at the base cooldown) even though their own ConsecutiveFailures is still
    // tracked and stored normally. Without this, the exact fix meant to stop chronically-failing
    // PRIMS from crowding out the self avatar would also push the avatar's OWN retry further
    // back every time its own upload lost the same pool-contention race -- one object (the
    // avatar) competing against however many hundred prims are also mid-backoff, punished the
    // same way they are for having already failed, when what actually helps it is exactly the
    // opposite: keep trying it often, since freeing it a slot is the whole point of this change.
    private static long SceneUploadFailureCooldownMs(ulong rootId, int consecutiveFailures) =>
        SceneUploadFailureBaseCooldownMs *
        (1L << Math.Min(Math.Max(rootId >= AvatarUploadSceneKeyOffset ? 1 : consecutiveFailures, 1) - 1,
            MaxSceneUploadFailureBackoffShift));

    // Hoisted to a field (rather than a Dictionary local to each UploadSceneObjectNoRebuild
    // call) so identical geometry is shared across every scene object that ever needs it, not
    // just faces within one build -- real SL regions duplicate geometry across objects
    // constantly (many separate copies of the same tree/rock/window prim, each its own
    // linkset), and a per-call dedup dictionary would give each duplicate its own independent
    // VkMesh: two full vkAllocateMemory allocations (vbo+ebo) per duplicate.
    // VkMesh.AddRef/Dispose is now ref-counted specifically to make this safe (see VkMesh's own
    // field comment) -- a mesh two different objects both reference must not be freed when only
    // one of them is removed. The existing `!face.IsFlexi && !subAnimated` exclusion below is
    // unchanged and just as necessary here as it was per-object: a shared VBO across animated
    // faces would make them stomp each other's per-tick deformation regardless of whether the
    // sharing is within one object or across several.
    //
    // Lookup checks IsDisposed and treats a hit on an already-fully-released mesh as a miss
    // (silently rebuilds) rather than proactively removing dead entries when a mesh's last
    // reference is released -- simpler, and the cost of a stale dictionary entry is a few dozen
    // bytes of managed memory (the disposed VkMesh's own now-empty shell), not a GPU resource;
    // only actually freed native handles matter for the allocation-count ceiling this exists to
    // relieve. Cleared wholesale in FreeSceneObjectResources.
    private readonly Dictionary<ulong, VkMesh> _sharedMeshPool = new();

    /// <summary>Returns true if this object actually landed in <see cref="_sceneObjects"/> (a
    /// real change the caller must fold into <see cref="RebuildSceneFlatLists"/>), false if the
    /// submission was parked for a later retry (cooldown) or dropped (genuine failure -- see
    /// <see cref="SceneObjectUploadFailed"/>).</summary>
    private bool UploadSceneObjectNoRebuild(VkContext vk, ulong rootId, PrimRenderSubmission sub)
    {
        // Captured BEFORE RemoveSceneObjectGpuNoRebuild below, which unconditionally clears
        // _recentSceneUploadFailures[rootId] as part of ITS OWN, legitimate "object actually
        // left the scene" cleanup -- but this rebuild-in-place call site invokes it on every
        // retry attempt too, success or failure. Without capturing this first, every retry
        // would see an empty dictionary entry (its own immediately-prior failure just wiped by
        // the very call chain about to fail again) and record itself as failure #1 forever,
        // silently defeating the whole backoff below -- confirmed live: a 2026-09-01 dense-
        // region session logged 942 consecutive drops that ALL reported "consecutive failures:
        // 1", never escalating.
        int priorConsecutiveFailures = _recentSceneUploadFailures.TryGetValue(rootId, out var priorFailureForCount)
            ? priorFailureForCount.ConsecutiveFailures
            : 0;

        if (_recentSceneUploadFailures.TryGetValue(rootId, out var recentFailure)
            && Environment.TickCount64 - recentFailure.FailedAt < SceneUploadFailureCooldownMs(rootId, recentFailure.ConsecutiveFailures))
        {
            // Re-park rather than drop outright: this submission didn't fail, a PRIOR one for
            // this key did, and we're just still inside that failure's cooldown window. Simply
            // returning here (the original behavior) discarded the submission with no record of
            // it anywhere -- SceneObjectStreamer had already marked the key as rendered when it
            // called SubmitSceneObject, so nothing would ever resubmit it, and the object stayed
            // invisible forever the instant a rebuild happened to race the cooldown window.
            // Re-parking costs nothing (no GPU work, no re-tessellation) and DrainPendingSceneObjects
            // naturally retries it every subsequent frame until the window clears.
            _pendingSceneObjects[rootId] = sub;
            return false;
        }

        // Diagnostic for the ghost-avatar/scene-key-collision class of bug. A key that already
        // holds a committed object being overwritten is routine (rebuild-in-place always reuses
        // the same key) -- only log when the identity actually changed (different PrimLocalId or
        // terrain-ness), which is the signature an unrelated object/avatar landed on a key it
        // shouldn't have. Turns an unreproducible field report into "which key got overwritten by
        // what, and when" if this recurs -- see SceneNeighborSimIndex's own header comment on why
        // sceneKey's upper 32 bits (sim index) should make a genuine collision unlikely.
        if (_sceneObjects.TryGetValue(rootId, out var existingFaces) && existingFaces.Count > 0
            && sub.Faces.Length > 0)
        {
            var (_, _, existingFace) = existingFaces[0];
            var newFace = sub.Faces[0];
            if (existingFace.PrimLocalId != newFace.PrimLocalId || existingFace.IsTerrain != newFace.IsTerrain)
            {
                LibreMetaverse.Logger.Debug(
                    $"[VkViewportControl] SceneKey 0x{rootId:X16} identity changed on overwrite: "
                    + $"old(IsTerrain={existingFace.IsTerrain}, PrimLocalId={existingFace.PrimLocalId}) -> "
                    + $"new(IsTerrain={newFace.IsTerrain}, PrimLocalId={newFace.PrimLocalId}, Label=\"{sub.Label}\").");
            }
        }

        RemoveSceneObjectGpuNoRebuild(rootId);

        var faces = new List<(VkMesh, VkMaterialDescriptorSet, PrimRenderFace)>(sub.Faces.Length);
        var objectTextures = new List<VkTexture>();

        // Cross-object mesh pool -- see _sharedMeshPool's own field comment. Animated
        // submissions never dedupe (a shared VBO would make faces stomp each other's per-tick
        // deformation, same reasoning as the single-submission path's subAnimated flag).
        bool subAnimated = sub.SkinData.Length > 0 || sub.AnimeshSkinData.Length > 0;

        // Position (in sub.Faces) -> mesh, for the skin/flexi GPU registration pass
        // below. `faces` itself is NOT positionally aligned to sub.Faces (a skipped null-vertex
        // face has no placeholder there, see the loop's own long-standing comment on this) --
        // this map is populated alongside `faces` in the same loop instead, so
        // skin.FaceIndex/fp.FaceStart+fi (positions in sub.Faces) resolve correctly regardless
        // of any skipped faces earlier in the object.
        var faceIndexToMesh = new Dictionary<int, VkMesh>(sub.Faces.Length);

        // A whole-object try/catch: GL has no equivalent failure mode here -- it has no
        // descriptor pool to exhaust -- but this port's per-face VkMaterialDescriptorSet allocates from
        // VkContext's shared, FIXED-capacity DescriptorPool (see its own sizing comment), and
        // AllocateDescriptorSets ThrowOnError()s at VK_ERROR_OUT_OF_POOL_MEMORY. Without this
        // guard, hitting that ceiling mid-object would leave already-built VkMesh/VkTexture/
        // VkMaterialDescriptorSet objects unreachable (never assigned into _sceneObjects, so
        // never disposed -- a real native-handle leak) AND propagate out through RenderFrame's
        // catch, firing InitFailed for what should be "this one streamed object didn't fit,"
        // not a panel-fatal error. Caught here: dispose whatever this object already built,
        // drop the object (it is simply never added to _sceneObjects/_sceneObjectTextures,
        // exactly as if its submission had never arrived), log, and let the caller's drain loop
        // continue with the next queued object rather than taking the whole panel down.
        //
        // Each static (non-flexi, non-animated) face's VkMesh would otherwise do its own
        // AllocateDeviceLocal submit+wait for its vbo AND its ebo -- two synchronous GPU
        // round-trips per face, the dominant cost of a large linkset's single-object upload.
        // meshBatch accumulates staging copies into shared command buffers (see
        // MeshBatchFlushEvery below for why not just one for the whole object) --
        // far fewer fence waits than one per face. The current batch is submitted at the END of
        // the try block (success path) AND at the START of the catch block (failure path) -- NOT
        // in a single shared finally -- because the catch block's own mesh.Dispose() calls
        // destroy each mesh's vbo/ebo handle, and a pending (recorded-but-not-yet-submitted)
        // CmdCopyBuffer still targets that handle; submitting only after Dispose runs (which a
        // finally attached after catch would do) would copy into freed memory.
        //
        // Shared by mesh AND per-face texture uploads (see TryUpload below) -- both share this
        // batch/flush-threshold mechanism instead of each doing its own independent submit+wait.
        var meshCmd = vk.Pool.CreateCommandBuffer("VkViewportControl.UploadSceneObjectNoRebuild.meshBatch");
        meshCmd.BeginRecording();
        var meshBatch = new VkStagedUploadBatch(meshCmd, new List<(Buffer, DeviceMemory)>());

        // Batching every mesh/texture staging copy into ONE command buffer for the whole
        // object would hold every staging buffer alive simultaneously until the object's one
        // shared submit completes, instead of the ~2-outstanding-at-a-time lifetime a fully
        // per-item submit+wait gives. MeshBatchFlushEvery bounds the peak: submit+wait+free every
        // N items' worth of staging, then start a fresh command buffer for the rest -- still a
        // large reduction in round-trips versus fully unbatched (1 per item vs. 1 per 16), just
        // bounded rather than unbounded. Shared across mesh AND texture uploads so an object
        // with many textured faces but few/cached meshes still gets bounded batch growth.
        const int MeshBatchFlushEvery = 16;
        int facesInCurrentMeshBatch = 0;
        void FlushMeshBatch()
        {
            meshCmd.SubmitAndWait();
            VkBufferHelper.FreeBatchStagingBuffers(vk, meshBatch);
            meshCmd = vk.Pool.CreateCommandBuffer("VkViewportControl.UploadSceneObjectNoRebuild.meshBatch");
            meshCmd.BeginRecording();
            meshBatch = new VkStagedUploadBatch(meshCmd, new List<(Buffer, DeviceMemory)>());
            facesInCurrentMeshBatch = 0;
        }

        var texCache = new Dictionary<IntPtr, VkTexture>();
        VkTexture? TryUpload(SKBitmap? bmp)
        {
            if (bmp is null) return null;
            var handle = bmp.Handle;
            if (texCache.TryGetValue(handle, out var cached)) return cached;
            try
            {
                var t = new VkTexture(vk, VkTexture.Preprocess(bmp), batch: meshBatch);
                texCache[handle] = t;
                objectTextures.Add(t);
                // Same accounting as the mesh-construction path above: count only actual
                // staging-allocating work, flush AFTER so the item that trips the cap isn't
                // immediately flushed for nothing.
                if (++facesInCurrentMeshBatch >= MeshBatchFlushEvery) FlushMeshBatch();
                return t;
            }
            catch
            {
                return null;
            }
        }

        try
        {
            for (int faceI = 0; faceI < sub.Faces.Length; faceI++)
            {
                var face = sub.Faces[faceI];
                face.RootSceneKey = rootId;
                // Defensive: GL assumes face.Vertices is always non-null for scene-object faces
                // (real prim geometry from PrimMeshBuilder, not the AvatarViewer-only placeholder-
                // face scenario ScheduleVertexUpdate's alignment relies on) and skips this check
                // entirely -- kept here as a low-risk robustness improvement, not a behavior
                // difference for any valid submission. NOTE: unlike _faceMeshesByPosition (which
                // keeps null placeholders so ScheduleVertexUpdate's array-position indexing
                // survives skipped faces), `faces` here has NO such placeholder -- a skipped face
                // means `faces` no longer aligns 1:1 with sub.Faces by position. The skin/flexi
                // GPU registration pass below indexes by sub.Faces position via faceIndexToMesh
                // instead, exactly to sidestep this misalignment.
                if (face.Vertices == null) continue;

                int vLen = face.VerticesLength > 0 ? face.VerticesLength : face.Vertices.Length;
                VkMesh mesh;
                if (!face.IsFlexi && !subAnimated)
                {
                    ulong h = VertexHash(face.Vertices, vLen, face.Indices);
                    if (_sharedMeshPool.TryGetValue(h, out mesh!) && !mesh.IsDisposed)
                    {
                        mesh.AddRef();
                    }
                    else
                    {
                        mesh = new VkMesh(vk, face.Vertices, vLen, face.Indices, dynamic: false, batch: meshBatch);
                        _sharedMeshPool[h] = mesh;
                        // Counts only actual staging-allocating construction, not _sharedMeshPool
                        // cache hits (AddRef above) -- those add no staging buffers to the batch.
                        // Flushed AFTER construction so the face that trips the cap doesn't have
                        // its own staging allocated and then immediately flushed for nothing.
                        if (++facesInCurrentMeshBatch >= MeshBatchFlushEvery) FlushMeshBatch();
                    }
                }
                else
                {
                    mesh = new VkMesh(vk, face.Vertices, vLen, face.Indices, dynamic: true);
                }
                faceIndexToMesh[faceI] = mesh;

                face.PickerVertices = PickerFromInterleaved(face.Vertices, vLen);
                face.NormalUvVertices = NormalUvFromInterleaved(face.Vertices, vLen);
                face.Vertices = null;

                var albedo = TryUpload(face.Texture);
                var normal = TryUpload(face.NormalMapTexture);
                var specular = TryUpload(face.SpecularMapTexture);
                var metallicRoughness = TryUpload(face.MetallicRoughnessTexture);
                var emissive = TryUpload(face.EmissiveTexture);

                var material = new VkMaterialDescriptorSet(vk, _prim!,
                    albedo?.DescriptorImageInfo ?? _placeholders!.White,
                    normal?.DescriptorImageInfo ?? _placeholders!.FlatNormal,
                    specular?.DescriptorImageInfo ?? _placeholders!.White,
                    metallicRoughness?.DescriptorImageInfo ?? _placeholders!.White,
                    emissive?.DescriptorImageInfo ?? _placeholders!.Black,
                    BuildMaterialUbo(face, albedo != null, normal != null, specular != null,
                        metallicRoughness != null, emissive != null));

                // Mirrors _faceTextureSlots' own population in ApplyPendingSubmission -- see
                // PatchSceneObjectTexture/TryApplyScenePatch, the only reader.
                _sceneFaceTextureSlots[(face.PrimLocalId, face.FaceIndex)] =
                    new[] { albedo, normal, specular, metallicRoughness, emissive };
                _scenePrimLocalIdToSceneKey[face.PrimLocalId] = rootId;

                faces.Add((mesh, material, face));
            }

            // Register skin/flexi GPU compute data for this object's scene faces. Runs after the
            // face loop (not interleaved) for the same reason ApplyPendingSubmission's own
            // registration does: skin.FaceIndex/fp.FaceStart can reference any position in
            // sub.Faces, not just faces already processed. Each Create() call gets its OWN
            // try/catch (not the whole-object catch below) so a pool-exhaustion failure on ONE
            // face's GPU registration leaves that face on the CPU fallback (GpuData stays null)
            // instead of dropping the entire avatar/object via the outer catch.
            if (_skinDeformer != null && sub.SkinData.Length > 0)
            {
                var skinGpuList = new List<VkAvatarSkinGpuData>(sub.SkinData.Length);
                foreach (var skin in sub.SkinData)
                {
                    // Rigid single-bone attachment faces (rings, hair strands, jewelry -- see
                    // AvatarFaceSkinData.IsRigidSingleBone's own doc comment) are skipped here,
                    // not registered for per-vertex GPU compute at all -- SceneAvatarAnimator.Tick
                    // drives them via ScheduleSceneFaceTransformUpdate instead, same as
                    // ApplyPendingSubmission's single-object registration. Roughly half of any
                    // real avatar's face count is rigid, so skipping them here matters just as
                    // much for the scene-object path, and more so once several accessorized
                    // avatars are in range at once.
                    if (skin.IsRigidSingleBone) continue;

                    if (!faceIndexToMesh.TryGetValue(skin.FaceIndex, out var faceMesh)) continue;
                    try
                    {
                        var gpuData = VkAvatarSkinGpuData.Create(vk, _skinPipeline!, skin, faceMesh);
                        skin.GpuData = gpuData;
                        skinGpuList.Add(gpuData);
                    }
                    catch (Exception e)
                    {
                        LibreMetaverse.Logger.Warn($"[VkViewportControl] Scene object {rootId} face {skin.FaceIndex} "
                            + $"skin GPU registration failed, staying on CPU skinning: {e.Message}");
                    }
                }
                if (skinGpuList.Count > 0) _sceneSkinGpuDataMap[rootId] = skinGpuList;
            }

            if (_flexiDeformer != null && sub.FlexiPrims.Length > 0)
            {
                var flexiGpuList = new List<VkFlexiGpuData>(sub.FlexiPrims.Length);
                foreach (var fp in sub.FlexiPrims)
                {
                    var meshes = new VkMesh[fp.FaceCount];
                    bool allPresent = true;
                    for (int fi = 0; fi < fp.FaceCount; fi++)
                    {
                        if (!faceIndexToMesh.TryGetValue(fp.FaceStart + fi, out var m)) { allPresent = false; break; }
                        meshes[fi] = m;
                    }
                    if (!allPresent) continue;
                    try
                    {
                        var gpuData = VkFlexiGpuData.Create(vk, _flexiPipeline!, fp, meshes);
                        fp.GpuData = gpuData;
                        flexiGpuList.Add(gpuData);
                    }
                    catch (Exception e)
                    {
                        LibreMetaverse.Logger.Warn($"[VkViewportControl] Scene object {rootId} flexi prim "
                            + $"GPU registration failed, staying on CPU deformation: {e.Message}");
                    }
                }
                if (flexiGpuList.Count > 0) _sceneFlexiGpuDataMap[rootId] = flexiGpuList;
            }

            // Submit every static face's batched vbo/ebo copy now, as one fence wait, and free
            // the staging buffers that fed it -- see meshBatch's own comment above for why this
            // beats one submit+wait per buffer. Must happen before the method returns
            // successfully (a draw call could sample these buffers as soon as next frame).
            meshCmd.SubmitAndWait();
            VkBufferHelper.FreeBatchStagingBuffers(vk, meshBatch);
        }
        catch (Exception e)
        {
            // Submit whatever mesh-batch copies were recorded so far, THEN free the staging
            // buffers, BEFORE disposing any face's mesh below -- a pending (recorded-but-not-
            // yet-submitted) CmdCopyBuffer still targets each mesh's vbo/ebo handle, so
            // destroying those handles first would leave the eventual submit copying into freed
            // memory (corrupted geometry or a device-lost, and it would look intermittent, not
            // like a clean failure). Wrapped in its own try/catch: a second failure here (e.g.
            // the original exception already left the device in a bad state) must not mask the
            // real error logged below or crash the render thread.
            try
            {
                // SubmitAndWait waits on/frees only meshCmd's own fence -- it was never added to
                // the shared pool's tracked list, so there's nothing left for a fallback
                // FreeUsedCommandBuffers() sweep to rescue if this throws (unlike the old
                // Submit()+FreeUsedCommandBuffers() shape this replaced).
                meshCmd.SubmitAndWait();
            }
            catch (Exception submitEx)
            {
                LibreMetaverse.Logger.Warn(
                    $"[VkViewportControl] Scene object {rootId} mesh-batch cleanup submit failed: {submitEx.Message}");
            }
            finally
            {
                VkBufferHelper.FreeBatchStagingBuffers(vk, meshBatch);
            }

            // Ref-count-safe (see _sharedMeshPool's own field comment): `faces` has exactly one
            // tuple per face this object successfully built before the failure, and
            // each tuple's mesh got exactly one AddRef (or its initial creation ref) for that
            // specific face -- whether that mesh is exclusively this object's or shared with
            // others already live in the scene. One Dispose() call per tuple here exactly
            // balances those refs, so a shared mesh another live object still references is
            // safely decremented rather than freed, while a mesh this failed object solely
            // created still gets torn down. No unreachable leftovers either way.
            foreach (var (mesh, material, _) in faces) { mesh.Dispose(); material.Dispose(); }
            foreach (var tex in objectTextures) tex.Dispose();
            // SceneFaceCount/_sceneObjects.Count logged alongside every drop so a pool-exhaustion
            // report carries the actual live counts, not just "it happened" --
            // see VkContext's own DescriptorPool sizing comment for what ceiling each number
            // corresponds to (CombinedImageSampler/5 vs. UniformBuffer, both ~1:1 with face count).
            // Label/requested face count included so a specific dropped object (e.g. a reported
            // "never renders" building) can actually be identified in the log -- every other
            // field here is scene-wide aggregate, useless for confirming whether THIS report's
            // object was even among the drops versus failing via some earlier, unrelated path
            // (e.g. never completing its CPU-side build at all, so it never reached here).
            // Uses the count captured at method entry, NOT a fresh lookup -- see that capture's
            // own comment for why a fresh lookup here would always see 0 (RemoveSceneObjectGpuNoRebuild,
            // above, already cleared this key's entry earlier in this same call).
            int consecutiveFailures = priorConsecutiveFailures + 1;
            LibreMetaverse.Logger.Warn($"[VkViewportControl] Scene object {rootId} (\"{sub.Label}\", "
                + $"{sub.Faces.Length} faces requested) upload failed, dropping it "
                + $"(live: {SceneFaceCount} faces / {_sceneObjects.Count} objects, "
                + $"pool free slots: {vk.MaterialUboPool.FreeSlots}/{vk.MaterialUboPool.Capacity}, "
                + $"consecutive failures: {consecutiveFailures}, next retry in "
                + $"{SceneUploadFailureCooldownMs(rootId, consecutiveFailures) / 1000}s): {e.Message}");
            _recentSceneUploadFailures[rootId] = (Environment.TickCount64, consecutiveFailures);
            // Without this, the drop is permanent: the caller that queued this submission
            // (SceneObjectStreamer.BuildObjectAsync) already recorded the object as rendered
            // before this method ever ran (it has no other way to learn upload outcome -- see
            // ISceneViewport.SceneObjectUploadFailed's own doc comment), so nothing re-requests
            // a build for a stationary object that never gets another terse update.
            SceneObjectUploadFailed?.Invoke(rootId);
            return false;
        }

        _recentSceneUploadFailures.Remove(rootId);
        _sceneObjects[rootId] = faces;
        _sceneObjectTextures[rootId] = objectTextures;

        // A transform override may have arrived while this object was still building (terse
        // update racing the mesh build) -- apply the parked matrix now so the object first
        // appears at its current position rather than its build-time position. Mirrors GL's
        // own parked-override consumption in UploadSceneObjectNoRebuild exactly.
        if (_sceneObjectTransformOverrides.TryRemove(rootId, out var parkedTransform))
            ApplyTransformToFaces(faces, parkedTransform);

        // Feed the spatial grid after any parked transform has landed, so the entry reflects the
        // object's final position rather than its build-time pose.
        if (faces.Count > 0)
        {
            var (aabbMin, aabbMax) = ComputeObjectWorldAabb(faces);
            _spatialGrid.Upsert(rootId, aabbMin, aabbMax);
        }
        return true;
    }

    /// <summary>
    /// Frees one scene object's GPU resources (meshes, materials, textures) and removes it from
    /// <see cref="_sceneObjects"/>. Caller is responsible for calling <see cref="RebuildSceneFlatLists"/> once after a
    /// batch of removals.
    /// </summary>
    private void RemoveSceneObjectGpuNoRebuild(ulong rootId)
    {
        // Explicit removal (object actually left the scene) clears any lingering upload-failure
        // cooldown too, so a rootId that's reused later (unlikely but not impossible) doesn't
        // inherit a stale block.
        _recentSceneUploadFailures.Remove(rootId);
        if (!_sceneObjects.TryGetValue(rootId, out var faces)) return;

        foreach (var (_, _, face) in faces)
        {
            _sceneFaceTextureSlots.Remove((face.PrimLocalId, face.FaceIndex));
            _scenePrimLocalIdToSceneKey.Remove(face.PrimLocalId);
        }
        _sceneObjects.Remove(rootId);
        _sceneObjectTransformOverrides.TryRemove(rootId, out _);
        _spatialGrid.Remove(rootId);
        _sceneObjectMotion.TryRemove(rootId, out _);

        _sceneObjectTextures.Remove(rootId, out var textures);
        _sceneSkinGpuDataMap.Remove(rootId, out var skinGpuList);
        _sceneFlexiGpuDataMap.Remove(rootId, out var flexiGpuList);

        // Deferred, not disposed here directly: every one of these frees a GPU-visible resource
        // (mesh VBO/EBO device memory, material descriptor set/UBO slot, textures, skin/flexi
        // compute SSBOs) that a still-in-flight frame's command buffer (up to FramesInFlight-1
        // frames back) may still be reading. Bundled into one action -- see
        // VkFrameReapRing.MarkPendingDestroy's own doc comment. The CPU-side bookkeeping above
        // (dictionary/spatial-grid removal) runs immediately since a reused rootId shouldn't
        // see stale state, but nothing GPU-visible depends on removal timing, only on when the
        // actual native Vulkan destroy calls happen.
        _reapRing.MarkPendingDestroy(() =>
        {
            // mesh.Dispose() is ref-counted (see VkMesh._refCount's own field comment) --
            // correctly decrements-only when this mesh is still shared with another live scene
            // object, and only actually frees native resources once this was the last reference.
            foreach (var (mesh, material, _) in faces)
            {
                mesh.Dispose();
                material.Dispose();
            }
            if (textures != null) foreach (var tex in textures) tex.Dispose();
            if (skinGpuList != null) foreach (var gpu in skinGpuList) gpu.Dispose();
            if (flexiGpuList != null) foreach (var gpu in flexiGpuList) gpu.Dispose();
        });
    }

    /// <summary>
    /// Discards every scene object and all pending scene-object work (e.g. on sim change).
    /// The actual per-object Vulkan disposal is NOT done here (see the comment below for why)
    /// -- it's spread across subsequent frames via the same budgeted drain
    /// <see cref="RemoveSceneObject"/> already uses for an ordinary single-object removal.
    /// </summary>
    private void FreeSceneObjectResources()
    {
        // Discard all pending scene-object submissions FIRST -- they belong to the old scene
        // and must not be uploaded to the new one (object local-IDs may be reused). Must run
        // before the enqueue-for-removal loop below so that loop's AddOrUpdate calls are the
        // only entries left in _pendingSceneObjects afterward, not intermixed with stale
        // old-scene submissions this same dictionary held a moment ago.
        foreach (var key in _pendingSceneObjects.Keys)
        {
            if (_pendingSceneObjects.TryRemove(key, out var sub) && sub != null)
                DisposeFaceBitmaps(sub);
        }
        _recentSceneUploadFailures.Clear();
        _sceneObjectTransformOverrides.Clear();

        // Disposing every live scene object's mesh/material/textures/skin-flexi-GPU-data inline,
        // right here, would do so synchronously and unbudgeted, unlike every OTHER drain-style
        // operation in this file (scene-object upload, texture patches, vertex updates), which
        // all admit-then-check-SceneWorkBudgetMs-and-yield -- a large scene (hundreds of
        // objects, each several Vulkan destroy/free calls) would pay for all of it in one frame
        // with zero opportunity to render or process input in between.
        //
        // Instead this reuses RemoveSceneObject's own mechanism rather than duplicating it:
        // enqueue a null (removal) submission for every currently-live object into the SAME
        // _pendingSceneObjects queue, so DrainPendingSceneObjects' existing per-frame budget
        // drains them via RemoveSceneObjectGpuNoRebuild over as many frames as it takes -- byte
        // for byte the same per-object disposal code an ordinary KillObject-driven removal
        // already runs. This is not a new class of visual state: a KillObject burst already
        // produces "some old objects gone, some not yet" for a few frames via that same
        // per-object path, and new-region content only starts arriving after a background
        // Task.Run seed walk (see OnSimChanged's own comment) -- a brief window of old-scene
        // objects still fading out during that gap is an acceptable trade against a multi-
        // second synchronous freeze.
        foreach (var key in _sceneObjects.Keys)
            _pendingSceneObjects.AddOrUpdate(
                key,
                (PrimRenderSubmission?)null,
                (_, displaced) => { if (displaced != null) DisposeFaceBitmaps(displaced); return null; });

        // _sharedMeshPool is deliberately NOT cleared here (previously was, inline, immediately
        // after the now-removed synchronous dispose loop). Its entries become stale as each
        // chunk's RemoveSceneObjectGpuNoRebuild disposes the last reference to a pooled VkMesh
        // (ref-counted Dispose -- see VkMesh._refCount's own field comment); the pool's only
        // read site (UploadSceneObjectNoRebuild's cache-hit check) already guards with
        // `&& !mesh.IsDisposed`, so a stale entry is harmless and gets naturally overwritten the
        // next time that exact vertex hash is built again, in this region or a future one --
        // clearing it here would only force needless rebuilds of shapes the new region reuses.
        //
        // _sceneObjects/_sceneObjectTextures/_spatialGrid/_sceneFaceTextureSlots/
        // _scenePrimLocalIdToSceneKey/_sceneObjectMotion/_sceneSkinGpuDataMap/
        // _sceneFlexiGpuDataMap are likewise no longer bulk-cleared here -- each is removed
        // incrementally, per object, inside RemoveSceneObjectGpuNoRebuild as the chunked drain
        // reaches it (identical bookkeeping to today's single-object removal path). Bulk-clearing
        // any of them here would race ahead of that drain and desync from objects not yet
        // processed. RebuildSceneFlatLists() is likewise not called here -- DrainPendingSceneObjects
        // already calls it once per frame whenever this loop's removals mark the scene dirty.
    }

    /// <summary>Rebuilds <see cref="_sceneOpaque"/>/<see cref="_sceneAlpha"/> from
    /// <see cref="_sceneObjects"/>, minus GL's batching-order sort: that sort exists to help GL's runtime instancing find
    /// adjacent identical-mesh/texture faces across different objects, but this port's
    /// <see cref="DrawFaces"/> already issues one draw call per face regardless of order (see
    /// <see cref="_opaqueFaces"/>'s own "not deduplicated" doc comment) -- draw order doesn't
    /// affect correctness here, so there is nothing for a sort to buy.</summary>
    private void RebuildSceneFlatLists()
    {
        _sceneOpaque.Clear();
        _sceneAlpha.Clear();
        foreach (var faces in _sceneObjects.Values)
        {
            foreach (var entry in faces)
            {
                if (entry.Face.HasAlpha) _sceneAlpha.Add(entry);
                else _sceneOpaque.Add(entry);
            }
        }
    }

    /// <summary>
    /// Drains <see cref="_pendingTransformOverrides"/> and patches <see cref="PrimRenderFace.
    /// Transform"/> on the faces of each dequeued root directly (the flat draw lists share the
    /// same face instances, so they pick the change up automatically). Overrides for roots
    /// whose upload has not landed yet are parked in <see cref="_sceneObjectTransformOverrides"/>
    /// and applied by <see cref="UploadSceneObjectNoRebuild"/> when the object appears. Includes
    /// the spatial-grid upsert.
    /// </summary>
    private void ApplySceneTransformOverrides()
    {
        while (_pendingTransformOverrides.TryDequeue(out var to))
        {
            if (_sceneObjects.TryGetValue(to.RootId, out var faces))
            {
                ApplyTransformToFaces(faces, to.Transform);
                _sceneObjectTransformOverrides.TryRemove(to.RootId, out _);

                if (faces.Count > 0)
                {
                    var (aabbMin, aabbMax) = ComputeObjectWorldAabb(faces);
                    _spatialGrid.Upsert(to.RootId, aabbMin, aabbMax);
                }
            }
            else
            {
                _sceneObjectTransformOverrides[to.RootId] = to.Transform;
            }
        }
    }

    /// <summary>
    /// Drains <see cref="_pendingSceneRebases"/>: composes a constant world-space translation
    /// onto each listed root's EXISTING <see cref="PrimRenderFace.Transform"/> (rather than
    /// replacing it, unlike <see cref="ApplySceneTransformOverrides"/> -- the caller only knows
    /// the delta, not each object's current baked transform). Keys with no committed entry are
    /// silently skipped: an object still mid-build resolves its position fresh against live
    /// <c>CurrentSim</c> once it lands, so it never needed correction here in the first place
    /// (see <c>SceneObjectStreamer.RebaseAllForRegionPromotion</c>'s own doc comment). Includes
    /// the spatial-grid upsert, same as <see cref="ApplySceneTransformOverrides"/>.
    /// </summary>
    private void ApplySceneRebases()
    {
        while (_pendingSceneRebases.TryDequeue(out var r))
        {
            if (!_sceneObjects.TryGetValue(r.SceneKey, out var faces) || faces.Count == 0) continue;
            ApplyTranslationToFaces(faces, r.Delta);
            var (aabbMin, aabbMax) = ComputeObjectWorldAabb(faces);
            _spatialGrid.Upsert(r.SceneKey, aabbMin, aabbMax);
        }
    }

    private static void ApplyTransformToFaces(
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces, Matrix4x4 transform)
    {
        foreach (var entry in faces)
        {
            if (entry.Face.IsFlexi) continue; // never stomp flexi, matches GL exactly
            entry.Face.Transform = transform;
        }
    }

    // Composes (post-multiplies, translation outermost -- matches PrimRenderFace.
    // WithWorldTranslation's convention) rather than replacing, unlike ApplyTransformToFaces:
    // the caller only supplies a delta, so the existing local rotation/scale/world-position baked
    // into Transform must be preserved, not overwritten.
    private static void ApplyTranslationToFaces(
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces, Vector3 delta)
    {
        var translate = Matrix4x4.CreateTranslation(delta);
        foreach (var entry in faces)
        {
            if (entry.Face.IsFlexi) continue; // never stomp flexi, matches ApplyTransformToFaces
            entry.Face.Transform *= translate;
        }
    }

    /// <summary>
    /// Selects up to <see cref="MaxLocalLightsLit"/> nearest local "Light" prims to
    /// the camera (within <see cref="LocalLightRange"/>) for forward-lighting. Runs
    /// unconditionally (not gated on anything) because local-light lighting is an always-on
    /// correctness addition in GL -- ported that way here too. Cost is bounded by how many lit
    /// prims <see cref="LightStreamer"/> is currently tracking (already limited to its own
    /// stream radius), not scene size. Excludes GL's
    /// shadow-casting half -- see <see cref="_litLights"/>'s own doc comment for why.
    /// </summary>
    private void SelectLocalLights()
    {
        _litLightCount = 0;
        var streamer = LightStreamer;
        if (streamer == null) return;

        var eye = _camera.EyePosition;
        Span<float> distSq = stackalloc float[MaxLocalLightsLit];

        // Top-K selection via linear replacement of the current farthest pick -- K is tiny
        // (MaxLocalLightsLit), so this stays cheap even with dozens of candidates.
        foreach (var light in streamer.Lights)
        {
            float d2 = Vector3.DistanceSquared(light.WorldPosition, eye);
            if (d2 > LocalLightRange * LocalLightRange) continue;

            if (_litLightCount < MaxLocalLightsLit)
            {
                _litLights[_litLightCount] = light;
                distSq[_litLightCount] = d2;
                _litLightCount++;
            }
            else
            {
                int worst = 0;
                for (int i = 1; i < MaxLocalLightsLit; i++)
                    if (distSq[i] > distSq[worst]) worst = i;
                if (d2 < distSq[worst])
                {
                    _litLights[worst] = light;
                    distSq[worst] = d2;
                }
            }
        }

        // Insertion sort nearest-first (N <= MaxLocalLightsLit).
        for (int i = 1; i < _litLightCount; i++)
        {
            var lightI = _litLights[i];
            float dI = distSq[i];
            int j = i - 1;
            while (j >= 0 && distSq[j] > dI)
            {
                _litLights[j + 1] = _litLights[j];
                distSq[j + 1] = distSq[j];
                j--;
            }
            _litLights[j + 1] = lightI;
            distSq[j + 1] = dI;
        }
    }

    /// <summary>
    /// dead-reckons every scene object tracked in <see cref="_sceneObjectMotion"/>
    /// forward from its last terse update using the velocity/angular-velocity/acceleration the
    /// simulator reported, and writes the extrapolated pose straight into the faces' <see
    /// cref="PrimRenderFace.Transform"/>. Called once per rendered frame (after
    /// <see cref="ApplySceneTransformOverrides"/>, which lands the exact received pose at t=0,
    /// and before any culling/draw pass so they see the same extrapolated <c>face.Transform</c>
    /// this frame) so continuous scripted/physical motion is smooth even though the sim only
    /// sends terse updates intermittently. Objects come off the tracking list the moment a terse
    /// update reports them at rest (see <see cref="SetSceneObjectMotion"/>), so cost is
    /// proportional to the number of objects actually moving right now (pure System.Numerics
    /// math, no GL dependency).
    /// </summary>
    private void ExtrapolateMovingSceneObjects()
    {
        if (_sceneObjectMotion.IsEmpty) return;

        long now = Environment.TickCount64;
        foreach (var (sceneKey, m) in _sceneObjectMotion)
        {
            // Still building -- the parked override from SetSceneObjectMotion's immediate
            // SetSceneObjectTransform call will land it once the upload completes.
            if (!_sceneObjects.TryGetValue(sceneKey, out var faces)) continue;

            float dt = MathF.Min((now - m.UpdateTick) / 1000f, MaxDeadReckoningSeconds);

            var position = m.Position + m.Velocity * dt + 0.5f * m.Acceleration * dt * dt;

            var rotation = m.Rotation;
            float angSpeedSq = m.AngularVelocity.LengthSquared();
            if (angSpeedSq > 1e-8f)
            {
                float angSpeed = MathF.Sqrt(angSpeedSq);
                var axis = m.AngularVelocity / angSpeed;
                var delta = Quaternion.CreateFromAxisAngle(axis, angSpeed * dt);
                rotation = Quaternion.Normalize(delta * rotation);
            }

            var transform = Matrix4x4.CreateScale(m.Scale)
                          * Matrix4x4.CreateFromQuaternion(rotation)
                          * Matrix4x4.CreateTranslation(position);
            ApplyTransformToFaces(faces, transform);
            if (faces.Count > 0)
            {
                var (aabbMin, aabbMax) = ComputeObjectWorldAabb(faces);
                _spatialGrid.Upsert(sceneKey, aabbMin, aabbMax);
            }
        }
    }

    /// <summary>unions <see cref="PrimRenderFace.GetWorldAabb"/> over every face in
    /// <paramref name="faces"/> (including flexi faces -- an object's overall visibility should
    /// account for their extents even though individual flexi faces bypass the fine-grained
    /// per-face cull, see <see cref="IsFaceCulled"/>). Used to feed <see cref="_spatialGrid"/>.
    /// Pure math; only the
    /// tuple shape differs to match this port's own <c>(VkMesh,VkMaterialDescriptorSet,
    /// PrimRenderFace)</c> entries). Returns a degenerate (min &gt; max) box for an empty list;
    /// callers must check <c>faces.Count</c> first.</summary>
    private static (Vector3 Min, Vector3 Max) ComputeObjectWorldAabb(
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var entry in faces)
        {
            entry.Face.GetWorldAabb(out var fMin, out var fMax);
            min = Vector3.Min(min, fMin);
            max = Vector3.Max(max, fMax);
        }
        return (min, max);
    }

    /// <summary>true if <paramref name="face"/> should be skipped this frame --
    /// either its owning scene object isn't in <paramref name="visibleSceneKeys"/>, or its
    /// world-space bounds don't intersect <paramref name="frustum"/>. Flexi faces never update
    /// <see cref="PrimRenderFace.Transform"/> (their vertices are written directly into the VBO
    /// each tick via compute), so the normal cached-AABB × Transform test would test the stale
    /// bind pose and always reject them -- for those faces this uses the flexi animator's live
    /// <c>FlexiPrimInfo.WorldBounds</c> instead, treating the face as visible (no cull) until
    /// that bounds value exists (e.g. the first tick or two after a rebuild) rather than risk a
    /// false-negative pop.</summary>
    private static bool IsFaceCulled(PrimRenderFace face, in Frustum frustum, HashSet<ulong>? visibleSceneKeys)
    {
        if (visibleSceneKeys != null && !visibleSceneKeys.Contains(face.RootSceneKey))
            return true;

        if (face.IsFlexi)
        {
            var bounds = face.FlexiOwner?.WorldBounds;
            if (bounds == null) return false;
            return !FrustumCuller.IntersectsAabb(frustum, bounds.Min, bounds.Max);
        }

        face.GetWorldAabb(out var min, out var max);
        return !FrustumCuller.IntersectsAabb(frustum, min, max);
    }

    /// <summary>fills <paramref name="dest"/> (cleared first) with the subset of
    /// <paramref name="source"/> that survives <see cref="IsFaceCulled"/> against
    /// <paramref name="frustum"/>/<paramref name="visibleSceneKeys"/>. When
    /// <paramref name="frustum"/> is <c>null</c> (culling disabled for this pass), copies
    /// <paramref name="source"/> through unfiltered -- matches GL's own "null frustum means no
    /// per-face test at all" contract exactly (<see cref="IsFaceCulled"/> is never even called
    /// in that case, not called-and-always-returning-false, though the observable result is the
    /// same either way).</summary>
    private void FilterVisible(
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> source,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> dest,
        Frustum? frustum, HashSet<ulong>? visibleSceneKeys)
    {
        dest.Clear();
        if (!frustum.HasValue) { dest.AddRange(source); return; }
        var f = frustum.Value;
        foreach (var entry in source)
        {
            // mirrors GL's own DrawFaces call sites (RecordFaceConsidered
            // unconditionally, RecordFaceCulled only on an actual cull) -- only recorded when
            // frustum.HasValue, matching that IsFaceCulled itself is never invoked otherwise.
            _stats.RecordFaceConsidered();
            if (!IsFaceCulled(entry.Face, f, visibleSceneKeys))
                dest.Add(entry);
            else
                _stats.RecordFaceCulled();
        }
    }

    /// <summary>like <see cref="FilterVisible"/> but APPENDS survivors to
    /// <paramref name="dest"/> without clearing it first -- used to merge two culled sources
    /// (base-alpha + scene-alpha) into one combined list, mirroring GL's own
    /// <c>AppendVisibleAlpha</c> (called twice per frame into the same <c>_mergedAlpha</c>,
    /// once per source list, both against the MAIN pass's frustum/visibleSceneKeys -- alpha
    /// faces are never drawn in the shadow or reflection passes at all, so there is no
    /// per-pass alpha visibility to compute).</summary>
    private void AppendVisible(
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> source,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> dest,
        Frustum? frustum, HashSet<ulong>? visibleSceneKeys)
    {
        if (!frustum.HasValue) { dest.AddRange(source); return; }
        var f = frustum.Value;
        foreach (var entry in source)
        {
            _stats.RecordFaceConsidered();
            if (!IsFaceCulled(entry.Face, f, visibleSceneKeys))
                dest.Add(entry);
            else
                _stats.RecordFaceCulled();
        }
    }

    /// <summary>Pure
    /// math, no GL dependency -- FNV-1a over a face's vertex+index bytes, used by
    /// <see cref="UploadSceneObjectNoRebuild"/>'s per-object mesh pool to detect identical
    /// geometry.</summary>
    private static ulong VertexHash(float[] verts, int len, ushort[] indices)
    {
        ulong hash = 0xCBF29CE484222325UL;
        hash = HashBytes(hash, System.Runtime.InteropServices.MemoryMarshal.Cast<float, byte>(verts.AsSpan(0, len)));
        hash = HashBytes(hash, System.Runtime.InteropServices.MemoryMarshal.Cast<ushort, byte>(indices.AsSpan()));
        return hash;
    }

    private static ulong HashBytes(ulong hash, ReadOnlySpan<byte> bytes)
    {
        const ulong Prime = 0x00000100000001B3UL;
        var chunks = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(bytes);
        foreach (ulong c in chunks) { hash = (hash ^ c) * Prime; }
        for (int i = chunks.Length * sizeof(ulong); i < bytes.Length; i++)
            hash = (hash ^ bytes[i]) * Prime;
        return hash;
    }

    /// <summary>
    /// Drains <see cref="_pendingSubmissionPatches"/> and retries <see cref="_deferredSubmissionPatches"/>.
    /// Implements the retry semantics (900-frame/~30s deferral for
    /// patches that arrive before the target face's geometry has been submitted), but --
    /// unlike GL's version, and unlike this class's own <see cref="DrainScenePendingTexturePatches"/>
    /// -- deliberately does NOT drain unboundedly per frame. Each applied patch's
    /// <see cref="ApplySubmissionPatchIfReady"/> -&gt; <c>new VkTexture(...)</c> does a full
    /// synchronous one-off command-buffer submit + <c>WaitForFences(ulong.MaxValue)</c>
    /// (VkTexture.cs's upload path) -- cheap on GL (an async <c>glTexImage2D</c> call) but a
    /// real CPU&lt;-&gt;GPU round-trip here. A burst of patches (e.g. a multi-attachment avatar's
    /// bakes all finishing within the same second, delivered via arbitrary thread-pool threads
    /// through <see cref="PatchSubmissionTexture"/>'s <c>Progress&lt;T&gt;</c> callback) previously
    /// meant N sequential blocking round-trips in one <see cref="RenderFrame"/> call -- the
    /// live-reported "momentary freeze, then recovers, laggy" symptom. Budgeted the same
    /// Stopwatch-timed way <see cref="DrainScenePendingTexturePatches"/> budgets its own
    /// sections (see <see cref="SceneWorkBudgetMs"/>), so a large burst spreads across several
    /// frames instead of stalling one.
    /// Runs on the render thread only (called from the top of <see cref="RenderFrame"/>).
    /// </summary>
    private void DrainSubmissionTexturePatches(VkContext vk)
    {
        // One shared Stopwatch clock spans both loops below (so the two loops split one
        // SceneWorkBudgetMs total, not 6ms each); each loop still always admits its own first
        // item regardless of remaining budget, so a single expensive patch in either queue can't
        // starve that queue's progress entirely. Each patch's VkTexture upload does its own
        // submit+wait (still covered by ApplySubmissionPatchIfReady's own try/catch) --
        // VkTexture's optional batch parameter and VkBufferHelper.RecordDeviceLocalCopy are left
        // in place, unused here, in case a differently-shaped batching approach (e.g. a real
        // staging arena instead of a shared command buffer) is worth revisiting later.
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
        bool BudgetExceeded(int processedInThisLoop) =>
            processedInThisLoop > 0 &&
            (System.Diagnostics.Stopwatch.GetTimestamp() - start) / ticksPerMs >= SceneWorkBudgetMs;

        int applied = 0;
        while (!BudgetExceeded(applied) && _pendingSubmissionPatches.TryDequeue(out var patch))
        {
            ApplySubmissionPatch(vk, patch);
            applied++;
        }

        int deferredApplied = 0;
        for (int i = _deferredSubmissionPatches.Count - 1; i >= 0; i--)
        {
            if (BudgetExceeded(deferredApplied)) break;

            var (patch, retriesLeft) = _deferredSubmissionPatches[i];
            deferredApplied++;
            if (ApplySubmissionPatchIfReady(vk, patch))
            {
                _deferredSubmissionPatches.RemoveAt(i);
            }
            else if (retriesLeft <= 1)
            {
                patch.Bitmap?.Dispose();
                _deferredSubmissionPatches.RemoveAt(i);
            }
            else
            {
                _deferredSubmissionPatches[i] = (patch, retriesLeft - 1);
            }
        }

        if (!_pendingSubmissionPatches.IsEmpty || _deferredSubmissionPatches.Count > 0)
            RequestRender();
    }

    /// <summary>
    /// Builds the <see cref="VkTexture"/> for a texture patch, preferring the BC3 compressed
    /// pixel-cache tier over <paramref name="patch"/>'s decoded RGBA8 <see cref="SKBitmap"/>
    /// when all three are true: this GPU supports BC3 (<see cref="VkContext.SupportsBc3"/>),
    /// the patch carries a known source UUID (<see cref="SceneTexturePatch.TextureId"/> !=
    /// zero -- avatar bake patches and previews leave this unset, see that field's own doc
    /// comment), and it's a full-quality patch (<c>ResolutionLevel == -1</c> -- an LOD preview
    /// isn't worth a compressed-cache round trip since it will be replaced shortly anyway).
    /// Falls back to the uncompressed <see cref="SceneTexturePatch.Bitmap"/> whenever the
    /// compressed tier misses, or when constructing the compressed texture itself fails (e.g.
    /// a structurally-valid but Vulkan-rejected stale entry) -- this method never surfaces a
    /// compressed-path failure to the caller, only genuine uncompressed-path failures.
    /// <paramref name="patch"/>'s <see cref="SceneTexturePatch.Bitmap"/> is disposed by
    /// whichever branch consumes it: on the compressed-success path that happens here (the
    /// bitmap is no longer needed once the compressed upload succeeds); on the uncompressed
    /// path, <see cref="VkTexture"/>'s own constructor disposes it as usual.
    /// </summary>
    private static VkTexture BuildTextureForPatch(VkContext vk, SceneTexturePatch patch, VkStagedUploadBatch? batch)
    {
        if (patch.ResolutionLevel == -1 && patch.TextureId != LibreMetaverse.UUID.Zero && vk.SupportsBc3)
        {
            var compressed = TextureDiskCache.TryGetCompressedPixels(patch.TextureId);
            if (compressed != null)
            {
                try
                {
                    var compressedTex = new VkTexture(vk, compressed.Width, compressed.Height, compressed.Levels, batch);
                    patch.Bitmap!.Dispose();
                    return compressedTex;
                }
                catch (Exception ex)
                {
                    // Falls through to the uncompressed path below.
                    // RootLocalId/FaceIndex/SceneKey included to distinguish "one face re-patched
                    // every frame" (a producer-side loop bug) from "many different objects failing"
                    // (genuine VRAM pressure) when this repeats rapidly in the log.
                    LibreMetaverse.Logger.Debug(
                        $"[VkViewportControl] Compressed-tier VkTexture construction failed for {patch.TextureId} " +
                        $"(root={patch.RootLocalId}, face={patch.FaceIndex}, slot={patch.Slot}, sceneKey={patch.SceneKey:X}), falling back: {ex.Message}");
                }
            }
        }

        return new VkTexture(vk, patch.Bitmap!, batch);
    }

    /// <summary>Apply immediately if the
    /// target face's geometry is already present, otherwise defer and retry (a patch can arrive
    /// before <see cref="Submit"/>'s geometry has finished streaming in).</summary>
    private void ApplySubmissionPatch(VkContext vk, SceneTexturePatch patch, VkStagedUploadBatch? batch = null,
        List<VkTexture>? deferredOldTextures = null)
    {
        if (patch.Bitmap == null) return;

        if (_opaqueFaces.Count > 0 || _alphaFaces.Count > 0)
        {
            if (ApplySubmissionPatchIfReady(vk, patch, batch, deferredOldTextures)) return;
        }

        _deferredSubmissionPatches.Add((patch, 900));
        RequestRender();
    }

    /// <summary>
    /// Adapted to Vulkan's descriptor-set-per-material shape: GL just swaps a <c>GlTexture?</c>
    /// reference in a tuple and lets the next <c>DrawFaces</c> call recompute <c>uHasTexture</c>
    /// etc. live from it; Vulkan bakes texture presence into <see cref="VkMaterialUbo"/> at
    /// construction time, so this rewrites the one image binding via
    /// <see cref="VkMaterialDescriptorSet.UpdateTexture"/> AND rebuilds/re-uploads the UBO
    /// (via <see cref="BuildMaterialUbo"/>) from <see cref="_faceTextureSlots"/>' current
    /// per-slot presence, in case this patch changes a HasX flag (e.g. introduces an albedo
    /// where the original submission had none). Returns <c>true</c> if the face was found (and
    /// the bitmap was consumed either way), <c>false</c> if the face isn't present yet.
    /// </summary>
    private bool ApplySubmissionPatchIfReady(VkContext vk, SceneTexturePatch patch, VkStagedUploadBatch? batch = null,
        List<VkTexture>? deferredOldTextures = null)
    {
        var key = (patch.RootLocalId, patch.FaceIndex);
        if (!_faceTextureSlots.TryGetValue(key, out var slots)) return false;

        VkMaterialDescriptorSet? material = null;
        PrimRenderFace? face = null;
        VkMesh? mesh = null;
        int foundIndex = -1;
        bool foundInOpaque = false;
        for (int i = 0; i < _opaqueFaces.Count; i++)
        {
            var f = _opaqueFaces[i];
            if (f.Face.PrimLocalId == patch.RootLocalId && f.Face.FaceIndex == patch.FaceIndex)
            { material = f.Material; face = f.Face; mesh = f.Mesh; foundIndex = i; foundInOpaque = true; break; }
        }
        if (material == null)
        {
            for (int i = 0; i < _alphaFaces.Count; i++)
            {
                var f = _alphaFaces[i];
                if (f.Face.PrimLocalId == patch.RootLocalId && f.Face.FaceIndex == patch.FaceIndex)
                { material = f.Material; face = f.Face; mesh = f.Mesh; foundIndex = i; foundInOpaque = false; break; }
            }
        }
        // _faceTextureSlots and _opaqueFaces/_alphaFaces are rebuilt together in
        // ApplyPendingSubmission, so finding a slots entry but no matching face tuple would
        // mean the two tables desynced -- shouldn't happen, but fail closed (defer/drop the
        // patch like a not-yet-ready face) rather than dereference a null material below.
        if (material == null || face == null || mesh == null) return false;

        if (patch.Bitmap == null) return true;

        VkTexture newTex;
        try
        {
            newTex = BuildTextureForPatch(vk, patch, batch);
        }
        catch
        {
            // VkTexture's constructor only disposes its bitmap argument on its own
            // full-success path (see VkTexture.cs's Dispose-at-end-of-constructor shape) --
            // dispose defensively here to match GlTexture's equivalent catch block, not
            // because this port introduces the gap.
            patch.Bitmap.Dispose();
            return true;
        }

        int slotIndex = (int)patch.Slot;
        var oldTex = slots[slotIndex];
        slots[slotIndex] = newTex;
        _textures.Add(newTex);

        // Deferred, not called directly here: vkUpdateDescriptorSets on a set a still-in-flight
        // command buffer references is itself a spec violation (independent of whether the old
        // image behind it gets freed), and destroying oldTex before that rewrite has actually
        // taken effect would be a use-after-free of whatever that same in-flight command buffer
        // is still sampling through the current binding. Bundled into one action -- see
        // VkFrameReapRing.MarkPendingDestroy's own doc comment -- so the rewrite and the old
        // texture's disposal share the same "no longer possibly in use" guarantee and can never
        // run out of order relative to each other.
        var materialForPatch = material;
        var newTexForPatch = newTex;
        _reapRing.MarkPendingDestroy(() =>
        {
            materialForPatch.UpdateTexture((uint)slotIndex, newTexForPatch.DescriptorImageInfo);
            if (oldTex != null)
            {
                if (deferredOldTextures != null) deferredOldTextures.Add(oldTex);
                else oldTex.Dispose();
            }
        });
        material.Update(BuildMaterialUbo(face,
            slots[0] != null, slots[1] != null, slots[2] != null, slots[3] != null, slots[4] != null));

        // SL's "Alpha Mode: Auto" material setting means alpha genuinely can't be known until the
        // real texture is decoded, which happens asynchronously via exactly this patch path
        // (PrimMeshBuilder's initial submission has no way to set HasAlpha correctly for an
        // Auto-mode face). Without this reclassification, such a face renders through the Opaque
        // pipeline (depth-write on, blend off) forever, regardless of what its real texture's
        // alpha channel contains. Mirrors GL's AlphaAuto reclassification and
        // TryApplyScenePatch's own version of the same fix (this file, above) -- the only
        // difference is HOW the reclassified face's home list changes: scene objects rebuild
        // their flat draw lists from _sceneObjects wholesale (RebuildSceneFlatLists), but
        // _opaqueFaces/_alphaFaces have no backing dictionary to rebuild from, so the single
        // matching tuple is moved directly between the two lists instead.
        if (patch.Slot == TextureSlot.Albedo && patch.TextureHasAlpha
            && face.AlphaAuto && face.AlphaMode == FaceAlphaMode.None)
        {
            face.AlphaMode = FaceAlphaMode.Blend;
            face.HasAlpha = true;
            if (foundInOpaque)
            {
                _opaqueFaces.RemoveAt(foundIndex);
                _alphaFaces.Add((mesh, material, face));
            }
        }

        // CPU-side bookkeeping only -- the actual native Dispose() (batched-drain-safe and
        // frame-in-flight-safe both) is bundled into the MarkPendingDestroy action above.
        if (oldTex != null) _textures.Remove(oldTex);
        return true;
    }

    /// <summary>
    /// Drains <see cref="_highPriorityScenePatches"/>/<see cref="_pendingScenePatches"/>
    /// (plus their deferred-retry lists), high-priority first, then deferred retries, then new
    /// patches, all against a single shared <see cref="SceneWorkBudgetMs"/> time budget.
    /// <see cref="_pendingScenePatches"/>' own dequeue loop still releases one gate permit per
    /// dequeued entry up to
    /// <see cref="TexturePatchQueueDepth"/> regardless of the time budget, so producers keep
    /// flowing even on a frame where the budget is already spent. Called once per frame on the
    /// render thread, mirroring GL's own call-site placement inside its per-frame texture-patch
    /// drain.
    /// </summary>
    private void DrainScenePendingTexturePatches(VkContext vk)
    {
        // A COUNT-based budget (this method used to enforce MaxDeferredPerFrame/
        // MaxIncomingPerFrame = 10 each) doesn't bound the actual synchronous GPU time these
        // items cost, which is what the render thread can't afford to stall on. One shared
        // Stopwatch clock spans every section below; each section still always admits its own
        // first item regardless of remaining budget, so a single expensive patch can't starve
        // that section's progress entirely. _deferredHighPriorityScenePatches' retry pass just
        // above is left as a plain foreach (unbounded) -- it normally stays small (a patch only
        // lands there when its target face genuinely isn't built yet); revisit if it shows up in
        // a future perf-overlay log the way the counted loops did here. Each patch's VkTexture
        // upload does its own submit+wait, covered by TryApplyScenePatch's own try/catch below.
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
        bool BudgetExceeded(int processedInThisSection) =>
            processedInThisSection > 0 &&
            (System.Diagnostics.Stopwatch.GetTimestamp() - start) / ticksPerMs >= SceneWorkBudgetMs;

        if (_deferredHighPriorityScenePatches.Count > 0)
        {
            var stillDeferredHp = new List<(SceneTexturePatch, int)>(_deferredHighPriorityScenePatches.Count);
            foreach (var (patch, retriesLeft) in _deferredHighPriorityScenePatches)
            {
                if (TryApplyScenePatch(vk, patch)) { /* applied -- drop it */ }
                else if (retriesLeft > 0) stillDeferredHp.Add((patch, retriesLeft - 1));
                else patch.Bitmap?.Dispose();
            }
            _deferredHighPriorityScenePatches.Clear();
            _deferredHighPriorityScenePatches.AddRange(stillDeferredHp);
        }
        int appliedHp = 0;
        while (!BudgetExceeded(appliedHp) && _highPriorityScenePatches.TryDequeue(out var hpPatch))
        {
            ReleasePatchGate(_texturePatchGate);
            if (!TryApplyScenePatch(vk, hpPatch))
                _deferredHighPriorityScenePatches.Add((hpPatch, 900));
            appliedHp++;
        }

        int deferredApplied = 0;
        if (_deferredScenePatches.Count > 0)
        {
            var stillDeferred = new List<(SceneTexturePatch, int)>(_deferredScenePatches.Count);
            foreach (var (patch, retriesLeft) in _deferredScenePatches)
            {
                if (BudgetExceeded(deferredApplied))
                {
                    stillDeferred.Add((patch, retriesLeft));
                    continue;
                }
                deferredApplied++;
                if (TryApplyScenePatch(vk, patch)) { /* applied -- drop it */ }
                else if (retriesLeft > 0) stillDeferred.Add((patch, retriesLeft - 1));
                else patch.Bitmap?.Dispose();
            }
            _deferredScenePatches.Clear();
            _deferredScenePatches.AddRange(stillDeferred);
        }

        // drained (queue-dequeue cap, TexturePatchQueueDepth) and incomingApplied (GPU-work time
        // cap) are deliberately separate: every dequeued patch releases its gate permit and
        // either gets applied or punted straight to _deferredScenePatches, so producers in
        // PatchSceneObjectTexture keep flowing at the full TexturePatchQueueDepth rate even on a
        // frame where the time budget is already spent by the sections above.
        int drained = 0;
        int incomingApplied = 0;
        while (drained < TexturePatchQueueDepth && _pendingScenePatches.TryDequeue(out var patch))
        {
            drained++;
            ReleasePatchGate(_texturePatchGate);
            if (!BudgetExceeded(incomingApplied) && TryApplyScenePatch(vk, patch))
            {
                incomingApplied++;
            }
            else
            {
                _deferredScenePatches.Add((patch, 900));
            }
        }
        if (!_pendingScenePatches.IsEmpty || _deferredScenePatches.Count > 0
            || !_highPriorityScenePatches.IsEmpty || _deferredHighPriorityScenePatches.Count > 0)
            RequestRender();

        if (_alphaSceneReclassNeeded)
        {
            _alphaSceneReclassNeeded = false;
            RebuildSceneFlatLists();
        }
    }

    /// <summary>
    /// Adapted to Vulkan's
    /// descriptor-set-per-material shape, the same way <see cref="ApplySubmissionPatchIfReady"/>
    /// already does for the single-object path -- rewrites one image binding via
    /// <see cref="VkMaterialDescriptorSet.UpdateTexture"/> and rebuilds/re-uploads the UBO.
    /// Simpler than GL's own version: no cross-object texture refcounting to maintain (this
    /// port's own documented "no cross-submission texture inheritance" scope narrowing, already
    /// established for scene-object textures in general -- see <see cref="_sceneObjectTextures"/>'s
    /// own doc comment), so a displaced texture is just disposed outright rather than refcount-
    /// released. Resolves the target face by direct <see cref="_sceneObjects"/> lookup via
    /// <c>patch.SceneKey</c> when set, falling back to an O(1) <see cref="_scenePrimLocalIdToSceneKey"/>
    /// lookup by PrimLocalId (mirrors GL's own avatar-patch fallback path, since avatar patches
    /// arrive keyed by the raw avatar/attachment localId, not the combined scene key). Returns
    /// <c>true</c> if resolved (bitmap consumed either way), <c>false</c> if the target isn't
    /// uploaded yet.
    /// <para>
    /// The <see cref="_scenePrimLocalIdToSceneKey"/> index makes this fallback O(1) instead of a
    /// nested scan over every scene object's every face -- avatar patches are the case that
    /// always takes this fallback, so an O(total scene faces) cost here would be paid per pending
    /// avatar-texture patch, every frame one was pending.
    /// </para></summary>
    private bool TryApplyScenePatch(VkContext vk, SceneTexturePatch patch, VkStagedUploadBatch? batch = null,
        List<VkTexture>? deferredOldTextures = null)
    {
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)>? faceTuples = null;
        ulong lookupKey = patch.SceneKey != 0 ? patch.SceneKey : patch.RootLocalId;
        if (!_sceneObjects.TryGetValue(lookupKey, out faceTuples))
        {
            if (_scenePrimLocalIdToSceneKey.TryGetValue(patch.RootLocalId, out var sceneKey))
                _sceneObjects.TryGetValue(sceneKey, out faceTuples);
        }
        if (faceTuples == null) return false;

        // Placeholder-not-yet-replaced case, mirrors GL exactly: a 1-face placeholder can't
        // satisfy a patch targeting a non-zero FaceIndex -- the real multi-face body hasn't
        // landed yet.
        if (faceTuples.Count == 1 && patch.FaceIndex > 0) return false;

        if (patch.Bitmap == null) return true;

        var key = (patch.RootLocalId, patch.FaceIndex);
        if (!_sceneFaceTextureSlots.TryGetValue(key, out var slots)) return false;

        VkMaterialDescriptorSet? material = null;
        PrimRenderFace? face = null;
        foreach (var entry in faceTuples)
        {
            if (entry.Face.PrimLocalId == patch.RootLocalId && entry.Face.FaceIndex == patch.FaceIndex)
            { material = entry.Material; face = entry.Face; break; }
        }
        // _sceneFaceTextureSlots and _sceneObjects are populated together in
        // UploadSceneObjectNoRebuild, so finding a slots entry but no matching face tuple would
        // mean the two tables desynced -- fail closed (defer, like a not-yet-ready face) rather
        // than dereference a null material below.
        if (material == null || face == null) return false;

        VkTexture newTex;
        try
        {
            newTex = BuildTextureForPatch(vk, patch, batch);
        }
        catch
        {
            patch.Bitmap.Dispose();
            return true;
        }

        int slotIndex = (int)patch.Slot;
        var oldTex = slots[slotIndex];
        slots[slotIndex] = newTex;
        _sceneObjectTextures.TryGetValue(lookupKey, out var objectTextureList);
        objectTextureList?.Add(newTex);

        // Deferred, not called directly here -- same hazard and same fix as
        // ApplySubmissionPatchIfReady's identical bundle (this file, above): rewriting this
        // descriptor binding and destroying the texture it used to point to both need the SAME
        // "no still-in-flight command buffer might still read the old binding" guarantee.
        var materialForPatch = material;
        var newTexForPatch = newTex;
        _reapRing.MarkPendingDestroy(() =>
        {
            materialForPatch.UpdateTexture((uint)slotIndex, newTexForPatch.DescriptorImageInfo);
            if (oldTex != null)
            {
                if (deferredOldTextures != null) deferredOldTextures.Add(oldTex);
                else oldTex.Dispose();
            }
        });
        material.Update(BuildMaterialUbo(face,
            slots[0] != null, slots[1] != null, slots[2] != null, slots[3] != null, slots[4] != null));

        // Mirrors GL's AlphaAuto reclassification exactly --
        // unlike the single-submission path (which has no rebuild hook and documents this as a
        // gap), scene objects already have RebuildSceneFlatLists as a natural rebuild point, so
        // this is implemented here, not skipped.
        if (patch.Slot == TextureSlot.Albedo && patch.TextureHasAlpha
            && face.AlphaAuto && face.AlphaMode == FaceAlphaMode.None)
        {
            face.AlphaMode = FaceAlphaMode.Blend;
            face.HasAlpha = true;
            _alphaSceneReclassNeeded = true;
        }

        // CPU-side bookkeeping only -- the actual native Dispose() is bundled into the
        // MarkPendingDestroy action above.
        if (oldTex != null)
        {
            _sceneObjectTextures.TryGetValue(lookupKey, out var owningList);
            owningList?.Remove(oldTex);
        }
        return true;
    }

    /// <summary>
    /// Translates one face's material fields into a std140 <see cref="VkMaterialUbo"/>,
    /// field-for-field matching GL's per-face uniform sets in its <c>DrawFaces</c> inner loop --
    /// same PBR/legacy branch split, same
    /// UV-transform packing (ScaleX/ScaleY/OffsetX/OffsetY into one vec4 + a separate rotation
    /// float, matching prim.frag's *UvST/*UvRot uniform pairs). <c>HasMaterial</c> is set
    /// unconditionally rather than only in the legacy branch (as GL's uniform-cache-based code
    /// does): prim.frag only reads uHasMaterial when uIsPBR == 0 (confirmed by reading
    /// prim.frag:682,754, both guarded by uIsTerrain/uIsPBR checks), so an always-set value is
    /// equivalent to GL's branch-conditional set, and simpler given each face gets its own UBO
    /// (no stale-value-from-a-previous-face concern a shared uniform cache would have).
    /// </summary>
    private static VkMaterialUbo BuildMaterialUbo(PrimRenderFace face,
        bool hasTexture, bool hasNormalMap, bool hasSpecularMap, bool hasMRMap, bool hasEmissiveMap)
    {
        var ubo = default(VkMaterialUbo);
        ubo.HasTexture = hasTexture ? 1 : 0;
        ubo.HasBump = face.HasBump ? 1 : 0;
        ubo.IsTerrain = face.IsTerrain ? 1 : 0;
        ubo.HasMaterial = face.HasMaterial ? 1 : 0;

        ubo.IsPBR = face.IsPBR ? 1 : 0;
        if (face.IsPBR)
        {
            ubo.BaseColorFactor = face.BaseColorFactor;
            ubo.MetallicFactor = face.MetallicFactor;
            ubo.RoughnessFactor = face.RoughnessFactor;
            ubo.EmissiveFactor = face.EmissiveFactor;

            ubo.BaseColorUvST = UvToST(face.BaseColorUvXform);
            ubo.BaseColorUvRot = face.BaseColorUvXform.Rotation;

            ubo.HasNormalMap = hasNormalMap ? 1 : 0;
            ubo.PbrNormalUvST = UvToST(face.PbrNormalUvXform);
            ubo.PbrNormalUvRot = face.PbrNormalUvXform.Rotation;

            ubo.HasMRMap = hasMRMap ? 1 : 0;
            ubo.MRUvST = UvToST(face.MetallicRoughnessUvXform);
            ubo.MRUvRot = face.MetallicRoughnessUvXform.Rotation;

            ubo.HasEmissiveMap = hasEmissiveMap ? 1 : 0;
            ubo.EmissiveUvST = UvToST(face.EmissiveUvXform);
            ubo.EmissiveUvRot = face.EmissiveUvXform.Rotation;
        }
        else
        {
            ubo.HasNormalMap = hasNormalMap ? 1 : 0;
            ubo.NormalUvST = UvToST(face.NormalUvXform);
            ubo.NormalUvRot = face.NormalUvXform.Rotation;

            ubo.HasSpecularMap = hasSpecularMap ? 1 : 0;
            ubo.SpecUvST = UvToST(face.SpecularUvXform);
            ubo.SpecUvRot = face.SpecularUvXform.Rotation;

            ubo.SpecColor = face.SpecularColor;
            ubo.SpecExp = face.SpecularExponent;
            ubo.EnvIntensity = face.EnvironmentIntensity;

            // Terrain's remaining two detail-texture slots (metallicRoughness = detail3,
            // emissive = layer-select map) -- matches GL's IsTerrain-only mrTex/emTex bind.
            // HasMRMap/HasEmissiveMap were already set above
            // from whether TryUpload actually produced a texture for this face's MR/Emissive
            // slots (true for terrain, since SceneTerrainBuilder populates those fields).
            if (face.IsTerrain)
            {
                ubo.HasMRMap = hasMRMap ? 1 : 0;
                ubo.HasEmissiveMap = hasEmissiveMap ? 1 : 0;
            }
        }

        return ubo;
    }

    private static Vector4 UvToST(UvTransform xform) =>
        new(xform.ScaleX, xform.ScaleY, xform.OffsetX, xform.OffsetY);

    private unsafe void RenderFrame()
    {
        _updateQueued = false;
        // Latched by this method's own catch block below after MaxConsecutiveRenderFailures in
        // a row -- see _renderDisabledAfterFailure's own field comment. Nothing currently clears
        // this short of reopening the panel (a fresh VkViewportControl instance): the observed
        // failure (real GPU VRAM exhaustion) does not self-resolve just because rendering stops,
        // it resolves when the SCENE's own resource usage drops, which this control has no
        // signal for.
        if (_renderDisabledAfterFailure) return;
        // See _swapchainOomBackoffUntilTicks's own field comment -- a cheap early-out while an
        // OOM-class BeginDraw failure is backing off, distinct from the permanent latch above.
        if (_swapchainOomConsecutiveFailures > 0 && Environment.TickCount64 < _swapchainOomBackoffUntilTicks)
            return;
        if (!_attached || _swapchain == null || _prim == null || _frameSets == null
            || _placeholders == null || _instanceDrawer == null)
            return;

        var source = this.GetPresentationSource();
        if (source == null) return;
        _visual!.Size = new Avalonia.Vector(Bounds.Width, Bounds.Height);
        var pixelSize = PixelSize.FromSize(Bounds.Size, source.RenderScaling);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0) return;

        // This placement covers CPU time
        // for the whole frame, not just command-buffer recording.
        _stats.BeginFrame();
        // Must run before any MarkUsed/FreeUsed call this frame -- see VkFrameReapRing's own doc
        // comment for why "this frame's slot" and "the slot due for reaping" are the same index.
        _reapRing.BeginFrame();

        // MUST run before anything below that disposes or rewrites a live GPU
        // resource (ApplyPendingSubmission's mesh/material/texture/skin-GPU/flexi-GPU disposal,
        // DrainPendingSceneObjects' scene-object removal path, the texture-patch drains'
        // descriptor rewrites, the deformer SSBO overwrites) -- every one of those can run on
        // ANY frame, and every one of them requires the IMMEDIATELY PRECEDING frame's MainPass to
        // be confirmed done before touching something it might still be reading. The reap ring's
        // own per-slot reaping (VkFrameReapRing, below) only guarantees "FramesInFlight frames
        // ago is done" -- too weak the moment real overlap lands, since these resources are
        // single-buffered (there's no per-slot copy the way VkPrimDescriptorSets' UBO has).
        // Placed once, here, rather than at each individual call site: simpler than re-deriving
        // "is this the first mutating operation this frame" at every site, and waiting slightly
        // earlier than strictly necessary costs nothing extra (the fence is either already
        // signaled or isn't; checking early doesn't make the GPU slower). See
        // _previousMainPassCmd's own field comment for why this is a targeted wait instead of
        // Step 5's general deferred-operation queue.
        _previousMainPassCmd?.WaitOnly();

        var vk = VkApi.Context;
        Framebuffer framebuffer = default;
        try
        {
            var pendingSubmission = Interlocked.Exchange(ref _pendingSubmission, null);
            if (pendingSubmission != null)
            {
                try
                {
                    ApplyPendingSubmission(vk, pendingSubmission);
                }
                catch (Exception e)
                {
                    // Unlike UploadSceneObjectNoRebuild's own per-object try/catch
                    // (drops just that one scene object on failure), this single-object path
                    // (AvatarViewer/PrimViewer/HudViewer) had no guard at all -- a
                    // VkMaterialUboPool/DescriptorPool exhaustion here would propagate to this
                    // method's own outer catch below and take the whole panel down permanently
                    // (InitFailed) instead of just failing to show this one submission. Any
                    // partial state left behind is the same risk any ThrowOnError() inside that
                    // loop already carried pre-existing (e.g. AllocateDescriptorSets) -- not a
                    // new hazard, just now non-fatal.
                    LibreMetaverse.Logger.Warn(
                        $"[VkViewportControl] ApplyPendingSubmission failed, submission dropped: {e.Message}");
                }
            }

            // drains queued scene-object uploads/removals under a per-frame time
            // budget, then applies any queued whole-object transform overrides -- both run
            // before the draw section below reads _sceneOpaque/_sceneAlpha/face.Transform.
            // Placed early in the frame, before other per-frame updates and before any pass
            // that draws scene faces.
            _drainStopwatch.Restart();
            DrainPendingSceneObjects(vk);
            DrainSceneObjectsMs = _drainStopwatch.Elapsed.TotalMilliseconds;
            ApplySceneTransformOverrides();
            ApplySceneRebases();

            // dead-reckon any scene objects currently in motion forward from their
            // last terse update. Must run after ApplySceneTransformOverrides (which lands the
            // exact received pose at t=0) and before any culling/draw pass so they see the same
            // extrapolated face.Transform this frame.
            ExtrapolateMovingSceneObjects();

            // local point-light selection, once per frame, before frameUbo is
            // populated below (its PointLightPos/Color/Radius/Falloff/Count fields read
            // straight from _litLights/_litLightCount).
            SelectLocalLights();

            // pull the current EEP-driven Sky/WaterFogColor sample once per frame,
            // before either is read below (Sky feeds frameUbo just past this point; WaterFogColor
            // feeds DrawWater later in this same method). Includes the ShowSky gate
            // (EnvironmentService
            // is only sampled when the sky dome itself is wanted). See EnvironmentService's own
            // property declaration for why this is wrapped in #if !VULKANSPIKE_BUILD.
#if !VULKANSPIKE_BUILD
            if (ShowSky && EnvironmentService != null)
            {
                Sky = EnvironmentService.GetCurrentSky();
                WaterFogColor = EnvironmentService.GetCurrentWaterFogColor();
            }
#endif

            // Runs AFTER ApplyPendingSubmission so a patch queued for a face that arrived in
            // this very frame's submission is applied immediately rather than deferred one
            // frame. Safe to rewrite descriptor sets here (not just safe to enqueue): covered by
            // the _previousMainPassCmd.WaitOnly() call near the top of this method -- see that
            // call site's own comment for why every mutating operation this frame needs it, not
            // just this one.
            _drainStopwatch.Restart();
            DrainSubmissionTexturePatches(vk);
            DrainScenePendingTexturePatches(vk);
            DrainTexturePatchesMs = _drainStopwatch.Elapsed.TotalMilliseconds;

            // apply per-face vertex updates queued by the CPU LBS animation loop
            // (AvatarViewerViewModel.AnimTick -> ScheduleVertexUpdate) or flexi-prim animation.
            // Includes the bounds check (a
            // stale faceIndex from a since-replaced submission, or one beyond the current face
            // count, is silently dropped rather than throwing -- matches GL's own behavior).
            _vertexUpdateStopwatch.Restart();
            while (_pendingVertexUpdates.TryDequeue(out var vertUpd))
            {
                if (vertUpd.FaceIndex >= 0 && vertUpd.FaceIndex < _faceMeshesByPosition.Count)
                    _faceMeshesByPosition[vertUpd.FaceIndex]?.UpdateVertices(vertUpd.Verts);
            }

            // Apply per-face model-matrix updates queued by AnimTick's/
            // SceneAvatarAnimator's rigid-attachment fast path (see
            // ScheduleFaceTransformUpdate/ScheduleSceneFaceTransformUpdate's own doc comments)
            // -- same indexing/bounds-check/silent-drop contract as the vertex-update drains
            // around this block, just writing PrimRenderFace.Transform (read live at draw time)
            // instead of rewriting a VBO. One reset covers both queues/callers -- see
            // _faceTransformRenderRequested's own field comment for why resetting BEFORE (not
            // after) draining matters: a Schedule*FaceTransformUpdate call landing mid-drain
            // (background thread, no synchronization with this loop) must still see the flag
            // clear and request a render for its own (now-unqueued-until-next-drain) entry, or
            // that update could sit unrendered until some unrelated later call happens to
            // request one.
            if (!_pendingFaceTransformUpdates.IsEmpty || !_pendingSceneFaceTransformUpdates.IsEmpty
                || !_pendingSceneFaceColorUpdates.IsEmpty)
                _faceTransformRenderRequested = false;

            while (_pendingFaceTransformUpdates.TryDequeue(out var xformUpd))
            {
                if (xformUpd.FaceIndex >= 0 && xformUpd.FaceIndex < _facesByPosition.Count
                    && _facesByPosition[xformUpd.FaceIndex] is { } targetFace)
                    targetFace.Transform = xformUpd.Transform;
            }

            // Apply per-scene-object vertex updates (SceneAvatarAnimator's CPU-LBS
            // fallback, FlexiPrimAnimator's scene-object CPU path), including the ArrayPool
            // return for rented buffers (VertsLength
            // carries the true logical length since a rented buffer's own .Length may be a larger
            // next-power-of-two bucket size) and the same silent-drop-on-stale-index behavior as
            // the single-submission drain above (a since-removed scene object, or a faceOffset
            // beyond that object's current face count, is dropped rather than throwing).
            while (_pendingSceneVertexUpdates.TryDequeue(out var su))
            {
                if (_sceneObjects.TryGetValue(su.RootId, out var scFaces)
                    && (uint)su.FaceOffset < (uint)scFaces.Count)
                {
                    scFaces[su.FaceOffset].Mesh.UpdateVertices(su.Verts, su.VertsLength);
                }
                if (su.IsPoolRented)
                    System.Buffers.ArrayPool<float>.Shared.Return(su.Verts);
            }

            // Scene-object counterpart to the single-submission face-transform drain
            // above -- SceneAvatarAnimator's rigid-attachment fast path for other avatars in the
            // scene. Same (rootId, faceOffset) indexing as _pendingSceneVertexUpdates immediately
            // above, same silent-drop-on-stale-index contract.
            while (_pendingSceneFaceTransformUpdates.TryDequeue(out var sxu))
            {
                if (_sceneObjects.TryGetValue(sxu.RootId, out var scXformFaces)
                    && (uint)sxu.FaceOffset < (uint)scXformFaces.Count)
                {
                    scXformFaces[sxu.FaceOffset].Face.Transform = sxu.Transform;
                }
            }

            // ScheduleSceneFaceColorUpdate's drain -- see that method's own doc comment
            // (ISceneViewport) for why this is a linear scan by (PrimLocalId, FaceIndex) rather
            // than the FaceOffset-indexed dictionary lookup the two drains above use: the caller
            // (SceneAvatarStreamer.OnAttachmentObjectUpdate) only knows the attachment prim's own
            // protocol identity, not that prim's position within the avatar's combined face list.
            // Cheap in practice -- at most a few hundred faces, on an event that fires at most a
            // few times a second. Silently drops a stale/no-longer-live rootId or an unmatched
            // face, same contract as every other Schedule* drain in this method.
            while (_pendingSceneFaceColorUpdates.TryDequeue(out var scu))
            {
                if (_sceneObjects.TryGetValue(scu.RootId, out var scColorFaces))
                {
                    foreach (var entry in scColorFaces)
                    {
                        if (entry.Face.PrimLocalId == scu.PrimLocalId && entry.Face.FaceIndex == scu.LocalFaceIndex)
                            entry.Face.Color = scu.Color;
                    }
                }
            }
            VertexUpdateDrainMs = _vertexUpdateStopwatch.Elapsed.TotalMilliseconds;

            // Both run their own synchronous one-off command buffer (see VkSkinDeformer/
            // VkFlexiDeformer's own doc comments for why) BEFORE the main frame's command
            // buffer is built below, so any GPU-deformed mesh this frame's draw calls read is
            // guaranteed fully written -- flexi before skin, after the per-face vertex-update
            // drains above.
            //
            // DispatchPending overwrites the skin/flexi
            // SSBOs, which the PREVIOUS frame's MainPass may still be reading under real overlap
            // (those SSBOs are single-buffered, not N-buffered like VkPrimDescriptorSets' UBO) --
            // a write-after-read hazard the reap ring's own per-slot reaping does NOT cover (it
            // only guarantees "FramesInFlight frames ago is done," not "the immediately preceding
            // frame is done"). Already covered by the _previousMainPassCmd.WaitOnly() call near
            // the top of this method (see that call site's own comment) -- nothing between there
            // and here submits a new MainPass that would invalidate the wait. Trade-off: deformer
            // dispatch can no longer overlap with the previous frame's own draw -- avatar/flexi-
            // heavy content gets less of the pipelining win than static-geometry-heavy content.
            // Revisit by N-buffering the deformer SSBOs instead, if that trade-off turns out to
            // matter in practice.

            _deformerStopwatch.Restart();
            _flexiDeformer?.DispatchPending();
            FlexiDispatchMs = _deformerStopwatch.Elapsed.TotalMilliseconds;

            _deformerStopwatch.Restart();
            _skinDeformer?.DispatchPending();
            SkinDispatchMs = _deformerStopwatch.Elapsed.TotalMilliseconds;

            _renderStopwatch.Restart();

            // must run before the render pass begins (see DrainPendingParticles'
            // own doc comment for why -- lazy pipeline creation on a blend-pair cache miss is
            // illegal mid-render-pass).
            DrainPendingParticles(vk);
            ParticleDrainMs = _renderStopwatch.Elapsed.TotalMilliseconds;

            using var draw = _swapchain.BeginDraw(pixelSize, out var image);
            BeginDrawMs = _renderStopwatch.Elapsed.TotalMilliseconds;
            SwapchainFreeCmdBuffersMs = _swapchain.LastFreeUsedCommandBuffersMs;
            SwapchainBeginDrawCoreMs = _swapchain.LastBeginDrawCoreMs;

            // BeginDraw's own FreeUsed() call (timed above as
            // SwapchainFreeCmdBuffersMs) is what confirms this frame's slot is fence-signaled --
            // moved here from the old tail-of-frame call site, since the tail no longer waits on
            // anything (see the Submit block below). See VkFrameStatsTracker.EndFrame's own doc
            // comment for what "this frame's slot" means under real overlap (an older frame's
            // data, not this one's).
            _stats.EndFrame(vk);

            EnsureDepthTarget(vk, pixelSize);

            var view = _camera.GetViewMatrix();
            var proj = _camera.GetProjectionMatrix((float)pixelSize.Width / pixelSize.Height);
            // proj.M22 *= -1f (the textbook GL-Y-up -> Vulkan-Y-down NDC fix) is intentionally
            // NOT applied here -- see VkPrimPipeline.cs's matching FrontFace comment for why.

            // computed early (before frameUbo, since HasSsao must land in the
            // SAME UpdatePerFrame call below), using a
            // three-way gate (SsaoEnabled && pipeline ready && face-count budget).
            int opaqueCount = _opaqueFaces.Count;
            int sceneOpaqueCount = _sceneOpaque.Count;
            int opaqueFaceCount = opaqueCount + sceneOpaqueCount;
            bool doSsao = SsaoEnabled && _ssaoReady && opaqueFaceCount > 0 && opaqueFaceCount <= SsaoMaxOpaqueFaces;

            // Main-pass frustum culling. FrustumCullingEnabled gates ONLY this pass
            // (shadow/reflection below always cull via their own independent frustum,
            // regardless of this flag -- see the property's own doc comment). Filtered
            // into reused scratch lists, never reallocated. The G-buffer/SSAO pre-pass below
            // reuses these SAME lists rather than computing its own (matches GL's
            // DrawFacesNormal call sites, which pass the same frustum/visibleSceneKeys as the
            // main pass). opaqueCount/sceneOpaqueCount/opaqueFaceCount above stay RAW (used only
            // for the SsaoMaxOpaqueFaces/ShadowsMaxOpaqueFaces face-count BUDGET gates, matching
            // GL's own `_opaque.Count + _sceneOpaque.Count` there exactly) -- everything drawn
            // below uses these new "main"-prefixed FILTERED counts/lists instead.
            Frustum? mainFrustum = FrustumCullingEnabled
                ? FrustumCuller.ExtractPlanes(view * proj)
                : (Frustum?)null;
            HashSet<ulong>? mainVisibleSceneKeys = null;
            if (mainFrustum.HasValue)
            {
                _spatialGrid.QueryVisible(mainFrustum.Value, _visibleSceneKeys);
                mainVisibleSceneKeys = _visibleSceneKeys;
            }
            FilterVisible(_opaqueFaces, _mainOpaqueVisible, mainFrustum, null);
            FilterVisible(_sceneOpaque, _mainSceneOpaqueVisible, mainFrustum, mainVisibleSceneKeys);
            int mainOpaqueCount = _mainOpaqueVisible.Count;
            int mainSceneOpaqueCount = _mainSceneOpaqueVisible.Count;
            int mainOpaqueFaceCount = mainOpaqueCount + mainSceneOpaqueCount;

            // directional shadow gate + light-VP computation. Mirrors GL's
            // RenderShadowPasses/RenderDirectionalShadow gating exactly -- ShadowsEnabled &&
            // ready && face-count budget first, then a separate NaN-guarded computation that
            // can still bail per-frame (EEP transitions, degenerate camera state).
            bool doShadow = ShadowsEnabled && _shadowReady && opaqueFaceCount > 0 && opaqueFaceCount <= ShadowsMaxOpaqueFaces;
            Matrix4x4 shadowLightView = default, shadowLightProj = default;
            if (doShadow) doShadow = TryComputeShadowLightVp(out shadowLightView, out shadowLightProj);

            // Shadow pass's own independent frustum/grid query, always run when doShadow is true
            // regardless of FrustumCullingEnabled (mirrors GL's RenderDirectionalShadow, which
            // computes lightFrustum/queries the grid unconditionally). Must not reuse
            // _visibleSceneKeys/mainFrustum -- the two frustums differ.
            int shadowOpaqueCount = 0, shadowSceneOpaqueCount = 0;
            if (doShadow)
            {
                var shadowFrustum = FrustumCuller.ExtractPlanes(shadowLightView * shadowLightProj);
                _spatialGrid.QueryVisible(shadowFrustum, _shadowVisibleSceneKeys);
                FilterVisible(_opaqueFaces, _shadowOpaqueVisible, shadowFrustum, null);
                FilterVisible(_sceneOpaque, _shadowSceneOpaqueVisible, shadowFrustum, _shadowVisibleSceneKeys);
                shadowOpaqueCount = _shadowOpaqueVisible.Count;
                shadowSceneOpaqueCount = _shadowSceneOpaqueVisible.Count;
            }

            // water surface + reflection gates. Mirrors GL's own doWater/
            // WaterReflectionsEnabled split exactly -- the
            // surface can draw with no reflection FBO at all (water.frag's analytic
            // atmSkyGradient fallback), so its gate is independent of the reflection pass's own,
            // which additionally needs the kReflIntervalMs wall-clock throttle to actually fire
            // this frame (reused/cached texture otherwise, same as GL's DrawWaterReflection
            // early-return).
            float waterHeightVal = WaterHeight;
            // No longer requires EyePosition.Z >= waterHeightVal - 0.05f: water now draws from
            // BELOW the surface too (see water.frag's eyeBelow branch), needed for the
            // underwater post-process pass below to have a real water surface visible from
            // underneath, not just a color tint.
            bool doWater = _waterReady && !float.IsNaN(waterHeightVal);
            bool underwater = doWater && _camera.EyePosition.Z < waterHeightVal - 0.05f;
            long nowTick = Environment.TickCount64;
            // Reflections stay above-water only: the reflection FBO's own camera setup mirrors
            // about the water plane assuming the real camera is above it, and nothing underwater
            // should show a reflection of itself through the surface from below anyway.
            bool doWaterReflThisFrame = doWater && !underwater && WaterReflectionsEnabled && _waterReflReady
                                        && (nowTick - _reflLastTick >= kReflIntervalMs);
            Matrix4x4 reflView = default, reflViewProj = default;
            int reflOpaqueCount = 0, reflSceneOpaqueCount = 0;
            if (doWaterReflThisFrame)
            {
                // Z-mirror about z = waterHeightVal in world space, row-vector convention:
                // (x,y,z,1) * reflMat = (x,y,2*wh-z,1).
                var reflMat = new Matrix4x4(
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, -1f, 0f,
                    0f, 0f, 2f * waterHeightVal, 1f);
                reflView = reflMat * view;
                reflViewProj = reflView * proj;

                // Reflection pass's own independent frustum/grid query, always run regardless of
                // FrustumCullingEnabled (mirrors GL's DrawWaterReflection, which computes
                // reflFrustum/queries the grid unconditionally). Must not reuse
                // _visibleSceneKeys/_shadowVisibleSceneKeys -- the frustum differs from both.
                var reflFrustum = FrustumCuller.ExtractPlanes(reflViewProj);
                _spatialGrid.QueryVisible(reflFrustum, _reflVisibleSceneKeys);
                FilterVisible(_opaqueFaces, _reflOpaqueVisible, reflFrustum, null);
                FilterVisible(_sceneOpaque, _reflSceneOpaqueVisible, reflFrustum, _reflVisibleSceneKeys);
                reflOpaqueCount = _reflOpaqueVisible.Count;
                reflSceneOpaqueCount = _reflSceneOpaqueVisible.Count;
            }

            var frameUbo = default(VkPerFrameUbo);
            frameUbo.View = view;
            frameUbo.Proj = proj;
            Matrix4x4.Invert(view, out var viewInv);
            frameUbo.ViewInv = viewInv;

            // Sun/ambient/atmosphere lighting -- ported verbatim from GL's DrawFaces uniform
            // block, the first time this migration populates
            // anything past View/Proj/ViewInv here. uSunDir is transformed from world space
            // into view space via dot products against the view matrix's rows (not a plain
            // matrix multiply) -- copied exactly, not re-derived, since System.Numerics.
            // Matrix4x4 has the identical row-major memory layout on both backends (see
            // VkPerFrameUbo.cs's own "Matrix upload convention" note).
            var sky = Sky;
            var worldSun = sky.SunDirection;
            frameUbo.SunDir = new Vector3(
                Vector3.Dot(new Vector3(view.M11, view.M12, view.M13), worldSun),
                Vector3.Dot(new Vector3(view.M21, view.M22, view.M23), worldSun),
                Vector3.Dot(new Vector3(view.M31, view.M32, view.M33), worldSun));
            frameUbo.SunColor = sky.SunlightColor;
            frameUbo.AmbientColor = sky.Ambient;

            // atmosphere.glsl's atmHazeColor() (the only atmosphere function prim.frag's fog
            // blend actually calls) reads just these 5 fields -- BlueDensity/SunDirection/
            // SunGlowFocus/SunGlowSize only feed atmSkyGradient(), the sky-DOME-only function,
            // so they're correctly left at 0 here exactly as GL leaves them unset for prim.frag
            // too (only _waterShader/_skyShader ever set them, and PrimViewer draws neither).
            frameUbo.BlueHorizon = sky.BlueHorizon;
            frameUbo.HazeHorizon = sky.HazeHorizon;
            frameUbo.HazeDensity = sky.HazeDensity;
            frameUbo.SunlightColor = sky.SunlightColor;
            frameUbo.Ambient = sky.Ambient;

            // BlueDensity/SunDirection(world-space)/SunGlowFocus/SunGlowSize: unlike
            // atmHazeColor() (used elsewhere in this UBO), sky.frag's atmSkyGradient() and its
            // own main() (sun disc / night sky) DO read these, so they're real inputs here --
            // populated unconditionally (matching every other atmosphere field's own always-set
            // pattern) since prim.frag never reads them either way, so there's nothing to guard.
            frameUbo.BlueDensity = sky.BlueDensity;
            var worldSunNorm = sky.SunDirection.LengthSquared() > 1e-10f ? Vector3.Normalize(sky.SunDirection) : Vector3.UnitZ;
            frameUbo.SunDirection = worldSunNorm;
            frameUbo.SunGlowFocus = sky.SunGlowFocus;
            frameUbo.SunGlowSize = sky.SunGlowSize;

            frameUbo.FogDensity = ShowSky && AtmosphericsEnabled
                ? 0.0012f * Math.Clamp(sky.HazeDensity, 0f, 2f) : 0f;
            // set BEFORE UpdatePerFrame so the SAME write carries it -- 1 only on
            // frames where SSAO actually ran (doSsao), matching GL's own ssaoTex!=0 -> HasSsao
            // gate. prim.frag never samples uSsaoMap when this is 0, so PassSet's binding 0 can
            // stay pointed at whatever it was last written to (see EnsureSsaoTargets' own note
            // on why that binding is rewritten only on target (re)creation, never per-frame).
            frameUbo.HasSsao = doSsao ? 1 : 0;

            // ShadowsOn/LightVp are shared IDENTICALLY across the main pass and the reflection
            // pass -- both must read the same world-space light transform regardless of which
            // camera is drawing (GL sets uShadowsOn from class-level ShadowsEnabled/_hasDirShadow
            // state unconditionally, not per-call, unlike uHasSsao -- see _frameSetsRefl's own
            // doc comment for the SSAO contrast).
            frameUbo.ShadowsOn = doShadow ? 1 : 0;
            if (doShadow) frameUbo.LightVp = _shadowLightVp;

            // Local point-light forward-lighting array, populated from SelectLocalLights' result
            // above, field-for-field in the same
            // ordering. PointShadowCount is left at its default 0 -- see _litLights' own doc
            // comment for why.
            frameUbo.PointLightCount = _litLightCount;
            if (_litLightCount > 0) frameUbo.PointLightPos0 = _litLights[0].WorldPosition;
            if (_litLightCount > 0) frameUbo.PointLightColor0 = _litLights[0].Color;
            if (_litLightCount > 0) frameUbo.PointLightRadius0 = _litLights[0].Radius;
            if (_litLightCount > 0) frameUbo.PointLightFalloff0 = _litLights[0].Falloff;
            if (_litLightCount > 1) frameUbo.PointLightPos1 = _litLights[1].WorldPosition;
            if (_litLightCount > 1) frameUbo.PointLightColor1 = _litLights[1].Color;
            if (_litLightCount > 1) frameUbo.PointLightRadius1 = _litLights[1].Radius;
            if (_litLightCount > 1) frameUbo.PointLightFalloff1 = _litLights[1].Falloff;
            if (_litLightCount > 2) frameUbo.PointLightPos2 = _litLights[2].WorldPosition;
            if (_litLightCount > 2) frameUbo.PointLightColor2 = _litLights[2].Color;
            if (_litLightCount > 2) frameUbo.PointLightRadius2 = _litLights[2].Radius;
            if (_litLightCount > 2) frameUbo.PointLightFalloff2 = _litLights[2].Falloff;
            if (_litLightCount > 3) frameUbo.PointLightPos3 = _litLights[3].WorldPosition;
            if (_litLightCount > 3) frameUbo.PointLightColor3 = _litLights[3].Color;
            if (_litLightCount > 3) frameUbo.PointLightRadius3 = _litLights[3].Radius;
            if (_litLightCount > 3) frameUbo.PointLightFalloff3 = _litLights[3].Falloff;

            _frameSets.UpdatePerFrame(frameUbo);

            // the reflection pass's OWN PerFrame UBO, written only on frames it
            // actually runs. View/ViewInv/SunDir are recomputed relative to reflView (mirrors
            // GL's DrawFaces recomputing these fresh from whichever `view` parameter that
            // specific call received); HasSsao is forced to 0 regardless of doSsao (the SSAO
            // buffer is in main-camera screen space, meaningless from the reflected camera --
            // matches GL's own ssaoTex-defaults-to-0-for-this-call behavior). Every other field
            // (atmosphere, ShadowsOn/LightVp, point-light state) is identical to frameUbo, so
            // this starts as a struct copy rather than rebuilding from scratch.
            if (doWaterReflThisFrame)
            {
                var reflFrameUbo = frameUbo;
                reflFrameUbo.View = reflView;
                Matrix4x4.Invert(reflView, out var reflViewInv);
                reflFrameUbo.ViewInv = reflViewInv;
                reflFrameUbo.SunDir = new Vector3(
                    Vector3.Dot(new Vector3(reflView.M11, reflView.M12, reflView.M13), worldSun),
                    Vector3.Dot(new Vector3(reflView.M21, reflView.M22, reflView.M23), worldSun),
                    Vector3.Dot(new Vector3(reflView.M31, reflView.M32, reflView.M33), worldSun));
                reflFrameUbo.HasSsao = 0;
                _frameSetsRefl!.UpdatePerFrame(reflFrameUbo);
            }

            // Merges alpha and uploads ONE combined instance buffer for every pass this frame --
            // base-opaque, scene-opaque, then merged-alpha, in that fixed order. Moved here
            // (before the G-buffer pre-pass below, and before cmd.BeginRecording --
            // UploadInstanceBatch needs no command buffer) so BOTH the G-buffer pass AND the main
            // pass later in this method read from the SAME already-uploaded buffer at the SAME
            // index ranges -- required by Vulkan's always-instanced prim.vert (see
            // VkGNormPipeline's own doc comment), not a micro-optimization. Recording one pass's
            // draw commands, then re-uploading the shared instance buffer for a later pass, would
            // overwrite the earlier pass's slots before the GPU ever executes its draws
            // (command-buffer recording happens before submission).
            int alphaCount = 0, totalCount = mainOpaqueFaceCount;
            // Shadow/reflection instance-data regions, appended AFTER opaque+scene+alpha and
            // sized by EACH PASS'S OWN filtered count (each pass culls independently, so this is
            // not always equal to opaqueFaceCount). Both passes draw ONLY the opaque draw lists,
            // never alpha (matches GL's own DrawFacesDepth(_opaque,...)/DrawFaces(_opaque,...)
            // calls, which never touch _alpha/_sceneAlpha). Bases default to 0 when their pass
            // doesn't run this frame -- safe, since RenderShadowPass/RenderWaterReflectionPass's
            // own opaqueCount>0/sceneOpaqueCount>0 guards mean a stale/zero base index is never
            // actually read through DrawBatchedInstance. Computed from the ACTUAL regions included
            // this frame: on a throttled-skip frame doWaterReflThisFrame is false and its region
            // is simply omitted, so reflBase must never be a fixed offset assuming both regions
            // are always present -- that would silently read another pass's matrices.
            int shadowBase = 0, shadowSceneBase = 0, reflBase = 0, reflSceneBase = 0;
            bool needInstanceBuffer = mainOpaqueFaceCount > 0 || _alphaFaces.Count > 0 || _sceneAlpha.Count > 0
                                       || doShadow || doWaterReflThisFrame;
            if (needInstanceBuffer)
            {
                // Merge the single-submission path's own alpha faces with
                // scene-object alpha faces into one back-to-front sorted list, frustum-culling
                // during the merge (against the MAIN pass's frustum -- alpha is never drawn in
                // the shadow/reflection passes, so there is no per-pass alpha visibility to
                // compute), so an object streamed in via SubmitSceneObject depth-sorts correctly
                // against (e.g.) PrimViewer/AvatarViewer's own alpha geometry rather than each
                // list sorting and drawing independently -- recomputed every frame
                // since the camera moves.
                _mergedAlpha.Clear();
                AppendVisible(_alphaFaces, _mergedAlpha, mainFrustum, null);
                AppendVisible(_sceneAlpha, _mergedAlpha, mainFrustum, mainVisibleSceneKeys);
                if (_mergedAlpha.Count > 1)
                {
                    var eye = _camera.EyePosition;
                    for (int i = 0; i < _mergedAlpha.Count; i++)
                        _mergedAlpha[i].Face.AlphaSortKey = (_mergedAlpha[i].Face.GetWorldCentroid() - eye).LengthSquared();
                    _mergedAlpha.Sort(static (a, b) => b.Face.AlphaSortKey.CompareTo(a.Face.AlphaSortKey));
                }
                alphaCount = _mergedAlpha.Count;
                totalCount = mainOpaqueFaceCount + alphaCount;

                if (doShadow)
                {
                    shadowBase = totalCount;
                    shadowSceneBase = shadowBase + shadowOpaqueCount;
                    totalCount += shadowOpaqueCount + shadowSceneOpaqueCount;
                }
                if (doWaterReflThisFrame)
                {
                    reflBase = totalCount;
                    reflSceneBase = reflBase + reflOpaqueCount;
                    totalCount += reflOpaqueCount + reflSceneOpaqueCount;
                }

                // a reused, grow-on-demand field, not a fresh `new float[]` every
                // frame -- at PrimViewer/AvatarViewer's own small face counts a per-frame
                // allocation here was harmless (a few hundred bytes, why this went unnoticed
                // through 8a/8b), but a streamed scene's totalCount can run into the hundreds of
                // faces, pushing this well past the 85KB LOH threshold and allocating/abandoning
                // it every composition tick.
                int neededFloats = totalCount * VkInstanceDrawer.InstanceFloats;
                if (_instanceDataBuf.Length < neededFloats)
                    _instanceDataBuf = new float[Math.Max(neededFloats, _instanceDataBuf.Length * 2)];
                var instanceData = _instanceDataBuf;
                WriteInstanceData(instanceData, 0, _mainOpaqueVisible, view, proj);
                WriteInstanceData(instanceData, mainOpaqueCount, _mainSceneOpaqueVisible, view, proj);
                WriteInstanceData(instanceData, mainOpaqueFaceCount, _mergedAlpha, view, proj);
                if (doShadow)
                {
                    WriteInstanceData(instanceData, shadowBase, _shadowOpaqueVisible, shadowLightView, shadowLightProj);
                    WriteInstanceData(instanceData, shadowSceneBase, _shadowSceneOpaqueVisible, shadowLightView, shadowLightProj);
                }
                if (doWaterReflThisFrame)
                {
                    WriteInstanceData(instanceData, reflBase, _reflOpaqueVisible, reflView, proj);
                    WriteInstanceData(instanceData, reflSceneBase, _reflSceneOpaqueVisible, reflView, proj);
                }
                _instanceDrawer!.UploadInstanceBatch(instanceData, totalCount);
            }

            var attachments = stackalloc ImageView[2] { new ImageView(image.ViewHandle), _depthView };
            var fbInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = (uint)pixelSize.Width,
                Height = (uint)pixelSize.Height,
                Layers = 1
            };
            vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out framebuffer).ThrowOnError();

            PreCullMs = _renderStopwatch.Elapsed.TotalMilliseconds;

            var cmd = vk.Pool.CreateCommandBuffer("VkViewportControl.RenderFrame.MainPass");
            cmd.BeginRecording();
            _stats.WriteStartTimestamp(vk, cmd.InternalHandle);

            var viewport = new Viewport { X = 0, Y = 0, Width = pixelSize.Width, Height = pixelSize.Height, MinDepth = 0, MaxDepth = 1 };
            vk.Api.CmdSetViewport(cmd.InternalHandle, 0, 1, in viewport);
            var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height) };
            vk.Api.CmdSetScissor(cmd.InternalHandle, 0, 1, &scissor);

            // shadow pass first, matching GL's own GlRenderCore ordering
            // (RenderShadowPasses precedes the SSAO block). Own fixed-2048 viewport/scissor,
            // restored to pixelSize internally before returning.
            if (doShadow)
                RenderShadowPass(vk, cmd.InternalHandle, pixelSize, shadowBase, shadowSceneBase, shadowOpaqueCount, shadowSceneOpaqueCount);

            // recorded as three SEPARATE render-pass instances, entirely before
            // the main render pass begins -- see RenderSsaoPasses' own doc comment for why this
            // ordering (relative to the instance-data upload above) is load-bearing, not
            // incidental. Viewport/scissor set above already match every offscreen target's own
            // full-viewport resolution, so no separate set/restore is needed around this call.
            if (doSsao)
                RenderSsaoPasses(vk, cmd.InternalHandle, pixelSize, proj, mainOpaqueCount, mainSceneOpaqueCount);

            // water reflection pre-pass, after SSAO and before the main pass --
            // matches GL's own ordering exactly (GlRenderCore: shadow -> SSAO -> water
            // reflection -> main pass). Must run before DrawWater (inside the main pass, below)
            // samples _lastReflViewProj/the reflection texture. Own fixed-512 viewport/scissor,
            // restored to pixelSize internally before returning.
            if (doWaterReflThisFrame)
            {
                RenderWaterReflectionPass(vk, cmd.InternalHandle, pixelSize, reflBase, reflSceneBase, reflOpaqueCount, reflSceneOpaqueCount);
                _lastReflViewProj = reflViewProj;
                _reflLastTick = nowTick;
            }

            // clear-colour
            // selection (minus GL's "_initError -> vivid red-tint" branch, which has no
            // equivalent here -- an init failure leaves _attached false, and RenderFrame's own
            // top-of-method guard already returns before reaching this code in that case).
            // No sky: flat BackgroundColor. Sky ready: clear to black (the sky shader fills
            // every pixel, so the clear colour is never actually visible -- black just avoids a
            // stray coloured flash on the very first frame before the sky draw executes).
            // Sky wanted but not ready (init failed): solid SL-sky-blue fallback.
            float clearR, clearG, clearB;
            if (!ShowSky)        { clearR = BackgroundColor.X; clearG = BackgroundColor.Y; clearB = BackgroundColor.Z; }
            else if (_skyReady)  { clearR = 0f;    clearG = 0f;    clearB = 0f;    }
            else                 { clearR = 0.39f; clearG = 0.58f; clearB = 0.93f; }

            var clearValues = stackalloc ClearValue[2]
            {
                new ClearValue { Color = new ClearColorValue { Float32_0 = clearR, Float32_1 = clearG, Float32_2 = clearB, Float32_3 = 1f } },
                new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } }
            };
            var beginInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass,
                Framebuffer = framebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
                ClearValueCount = 2,
                PClearValues = clearValues
            };
            SubPassMs = _renderStopwatch.Elapsed.TotalMilliseconds;
            vk.Api.CmdBeginRenderPass(cmd.InternalHandle, in beginInfo, SubpassContents.Inline);

            // drawn before everything else so it fills pixels not covered by
            // geometry -- placed
            // immediately after the per-frame UBO is up to date, before the shadow/main
            // geometry passes). Depends on frameUbo already being written to _frameSets'
            // buffer above: the sky pipeline's set 0 reuses that SAME descriptor set.
            if (ShowSky && _skyReady)
                DrawSky(vk, cmd.InternalHandle, view, proj);

            if (mainOpaqueFaceCount > 0 || alphaCount > 0)
            {
                // Sets 0+1 (per-frame UBO, per-pass samplers) bound once for the whole frame;
                // set 2 (per-material) is bound per-face inside DrawFaces now that every face
                // carries its own real VkMaterialDescriptorSet -- binding set 2 alone via
                // firstSet=2 doesn't disturb sets 0/1 as long as the pipeline layout used stays
                // compatible (it does: every draw this frame uses _prim.Layout).
                var frameAndPassSets = stackalloc DescriptorSet[2] { _frameSets.FrameSet, _frameSets.PassSet };
                vk.Api.CmdBindDescriptorSets(cmd.InternalHandle, PipelineBindPoint.Graphics, _prim.Layout, 0, 2, frameAndPassSets, 0, null);

                if (mainOpaqueFaceCount > 0)
                {
                    vk.Api.CmdBindPipeline(cmd.InternalHandle, PipelineBindPoint.Graphics, _prim.Opaque);
                    if (mainOpaqueCount > 0) DrawFaces(vk, cmd.InternalHandle, _mainOpaqueVisible, baseIndex: 0);
                    if (mainSceneOpaqueCount > 0) DrawFaces(vk, cmd.InternalHandle, _mainSceneOpaqueVisible, baseIndex: mainOpaqueCount);
                }

                // water surface, drawn after opaque geometry (correct depth test
                // against real terrain/objects) but before the alpha pass (transparent objects
                // above water render in front of it).
                if (doWater)
                    DrawWater(vk, cmd.InternalHandle, view, proj, waterHeightVal, underwater);

                if (alphaCount > 0)
                {
                    // Re-bind sets 0/1: if doWater ran above, DrawWater bound _waterPipeline's
                    // OWN set 1 (WaterPassLayout, a different VkDescriptorSetLayout object than
                    // prim's PerPassSamplersLayout) at the same set index -- Vulkan considers
                    // that binding no longer "compatible" for a later draw through a DIFFERENT
                    // pipeline layout at that set index, so the alpha pass can't assume sets 0/1
                    // are still whatever the opaque block bound. Rebinding unconditionally here
                    // (cheap, one CmdBindDescriptorSets call) removes any doubt rather than
                    // conditioning it on doWater specifically.
                    var frameAndPassSetsAlpha = stackalloc DescriptorSet[2] { _frameSets.FrameSet, _frameSets.PassSet };
                    vk.Api.CmdBindDescriptorSets(cmd.InternalHandle, PipelineBindPoint.Graphics, _prim.Layout, 0, 2, frameAndPassSetsAlpha, 0, null);
                    vk.Api.CmdBindPipeline(cmd.InternalHandle, PipelineBindPoint.Graphics, _prim.Alpha);
                    DrawFaces(vk, cmd.InternalHandle, _mergedAlpha, baseIndex: mainOpaqueFaceCount);
                }
            }
            else if (doWater)
            {
                // water can still draw even with zero opaque/alpha faces this
                // frame (e.g. an empty scene over a water plane) -- the outer `if` above only
                // guards the opaque+alpha block, so this mirrors that same doWater draw for the
                // "nothing else to draw" case. No extra descriptor-set bind needed here: DrawWater
                // binds its own sets 0+1 through _waterPipeline.Layout unconditionally, regardless
                // of whether the opaque block ran first.
                DrawWater(vk, cmd.InternalHandle, view, proj, waterHeightVal, underwater);
            }

            if (Wireframe && _wireframe != null)
                DrawWireframeOverlay(vk, cmd.InternalHandle, view, proj);

            // Selection outline is independent of the Wireframe toggle -- it's touch/select
            // feedback, not a debug view, so it draws whenever something is selected.
            if (_selectedOutlineLocalId != 0 && _outline != null)
                DrawSelectionOutline(vk, cmd.InternalHandle, view, proj);

            // last thing drawn in the main pass, matching GL's own placement
            // (DrawParticles is called immediately before BlitSceneToFb in GlRenderCore).
            DrawParticles(vk, cmd.InternalHandle, view, proj);

            vk.Api.CmdEndRenderPass(cmd.InternalHandle);
            _stats.WriteEndTimestamp(vk, cmd.InternalHandle);
            MainPassRecordMs = _renderStopwatch.Elapsed.TotalMilliseconds;

            // Underwater post-process, recorded into this SAME command buffer immediately after
            // the main pass ends and before submission -- see RenderUnderwaterPass's own doc
            // comment for why (copy-out + re-entry as a color attachment, both needed because
            // Vulkan can't sample and write the same image within one render pass).
            if (underwater && _underwaterReady)
                RenderUnderwaterPass(vk, cmd.InternalHandle, pixelSize, image, waterHeightVal);

            _renderStopwatch.Restart();
            cmd.Submit();
            _reapRing.MarkUsed(cmd);
            _previousMainPassCmd = cmd;
            SubmitCallMs = _renderStopwatch.Elapsed.TotalMilliseconds;
            // no fence wait here anymore -- this frame's slot is reaped at the NEXT
            // frame's BeginDraw instead (timed as SwapchainFreeCmdBuffersMs there), which is what
            // actually lets CPU work for the next frame start before this frame's GPU work is
            // confirmed done. FenceWaitMs stays 0 by construction; it isn't measuring "no wait
            // happened," it's measuring "this call site doesn't wait anymore."
            FenceWaitMs = 0;
            MainPassSubmitWaitMs = SubmitCallMs;
            _stats.EndCpuWork();

            if (_pickRequested && _pick != null)
            {
                // Convert from control-local DIPs to physical pixels, same scaling RenderFrame
                // already uses for pixelSize -- and clamp so an edge-of-viewport click can't
                // produce an out-of-bounds ImageOffset for the 1x1 readback copy below.
                int px = Math.Clamp((int)(_pickPoint.X * source.RenderScaling), 0, pixelSize.Width - 1);
                int py = Math.Clamp((int)(_pickPoint.Y * source.RenderScaling), 0, pixelSize.Height - 1);
                RunPickPass(vk, pixelSize, view, proj, px, py);
            }

            // Reached only if nothing above threw -- a real completed frame, not just "got past
            // the early-out guards above." See _consecutiveRenderFailures's own field comment.
            _consecutiveRenderFailures = 0;
            _swapchainOomConsecutiveFailures = 0;
        }
        catch (Exception e)
        {
            // A render-time failure shouldn't crash the whole app (the compositor invokes this
            // callback outside any caller-provided try/catch) -- surface it the same way an
            // init failure is surfaced, rather than let it propagate uncaught or swallow it
            // silently. Root-causing render failures (vs. init failures) is out of scope for
            // this pilot; both funnel through the same event for now.
            //
            // OOM-class failures (swapchain image creation hitting real GPU VRAM exhaustion) get
            // their own backoff-then-give-up track instead of the generic circuit breaker below
            // -- see _swapchainOomBackoffUntilTicks's own field comment for why. Checked first
            // and returns either way, so an OOM failure never also increments
            // _consecutiveRenderFailures.
            if (IsDeviceMemoryExhaustion(e))
            {
                _swapchainOomConsecutiveFailures++;
                if (_swapchainOomConsecutiveFailures >= MaxSwapchainOomBackoffAttempts)
                {
                    _renderDisabledAfterFailure = true;
                    InitFailed?.Invoke(
                        $"RenderFrame hit GPU out-of-memory {_swapchainOomConsecutiveFailures} times "
                        + "in a row despite backing off, rendering disabled for this panel (close "
                        + $"and reopen to retry): {e}");
                    return;
                }
                long backoffMs = SwapchainOomBaseBackoffMs *
                    (1L << Math.Min(_swapchainOomConsecutiveFailures - 1, MaxSwapchainOomBackoffShift));
                _swapchainOomBackoffUntilTicks = Environment.TickCount64 + backoffMs;
                InitFailed?.Invoke(
                    "RenderFrame failed (GPU out of memory -- likely transient VRAM pressure from "
                    + $"the main scene), retrying in {backoffMs}ms (attempt "
                    + $"{_swapchainOomConsecutiveFailures}/{MaxSwapchainOomBackoffAttempts}): {e.Message}");
                return;
            }

            // Circuit breaker: MaxConsecutiveRenderFailures in a row (not just one) latches
            // _renderDisabledAfterFailure and stops this method from attempting to render again
            // at all. A single failure alone does NOT latch -- a one-off hiccup should still get
            // to retry next frame exactly as before. What prompted this: swapchain image
            // creation hitting real GPU VRAM exhaustion (ErrorOutOfDeviceMemory) does not
            // self-resolve frame-to-frame, so without this it retried the SAME failing
            // allocation every single frame (55169 failures logged in one ~6s session) instead
            // of failing once, visibly, and stopping. That specific OOM case is now handled by
            // the backoff track above instead -- this path is for every OTHER kind of render
            // failure, which really does warrant giving up quickly.
            _consecutiveRenderFailures++;
            if (_consecutiveRenderFailures >= MaxConsecutiveRenderFailures && !_renderDisabledAfterFailure)
            {
                _renderDisabledAfterFailure = true;
                InitFailed?.Invoke(
                    $"RenderFrame failed {_consecutiveRenderFailures} times in a row, rendering "
                    + $"disabled for this panel (close and reopen to retry): {e}");
                return;
            }
            InitFailed?.Invoke($"RenderFrame failed: {e}");
        }
        finally
        {
            // Deferred, not destroyed here directly: this framebuffer wraps the swapchain image
            // the main-pass command buffer just (non-waited, see the "no fence wait here
            // anymore" comment above cmd.Submit()) submitted -- destroying it unconditionally,
            // synchronously in this finally block gave no guarantee the GPU had actually
            // finished reading it, a real Vulkan spec violation caught live by
            // VK_LAYER_KHRONOS_validation ("vkDestroyFramebuffer(): can't be called on
            // VkFramebuffer ... currently in use by VkCommandBuffer ..."). MarkPendingDestroy
            // ties this framebuffer's actual destruction to the SAME slot-reap guarantee
            // _reapRing.MarkUsed(cmd) above already relies on for the command buffer itself --
            // both wait for confirmation that whatever last used this slot (FramesInFlight
            // frames ago) is done before either runs again.
            if (framebuffer.Handle != 0)
            {
                var fbToDestroy = framebuffer;
                _reapRing.MarkPendingDestroy(() => vk.Api.DestroyFramebuffer(vk.Device, fbToDestroy, null));
            }
        }
    }

    /// <summary>
    /// Draws the sky dome as a full-screen triangle into the currently-recording render pass.
    /// Must be called before any opaque geometry so it fills pixels not covered by terrain,
    /// including dt/uTime
    /// bookkeeping. Set 0 is the SAME <see cref="_frameSets"/> descriptor set the main geometry
    /// pass binds (this pipeline's layout reuses <c>VkPrimPipeline.PerFrameLayout</c> directly),
    /// so the caller must have already written this frame's <see cref="VkPerFrameUbo"/> via
    /// <c>_frameSets.UpdatePerFrame</c> before calling this.
    /// </summary>
    private unsafe void DrawSky(VkContext vk, CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj)
    {
        var vp = view * proj;
        Matrix4x4.Invert(vp, out var invVp);

        long now = Environment.TickCount64;
        float dt = _cloudLastTick == 0 ? 0f : MathF.Min((now - _cloudLastTick) / 1000f, 0.1f);
        _cloudLastTick = now;
        _cloudTime += dt;

        var skyUbo = default(VkSkyUbo);
        skyUbo.InvViewProj = invVp;
        skyUbo.CloudColor = Sky.CloudColor;
        skyUbo.CloudPosDensity1 = Sky.CloudPosDensity1;
        skyUbo.CloudPosDensity2 = Sky.CloudPosDensity2;
        skyUbo.CloudScale = Sky.CloudScale;
        skyUbo.CloudShadow = Sky.CloudShadow;
        skyUbo.CloudScrollRate = Sky.CloudScrollRate;
        skyUbo.CloudVariance = Sky.CloudVariance;
        skyUbo.CameraPos = _camera.EyePosition;
        skyUbo.Time = _cloudTime;
        _skySet!.UpdateSky(skyUbo);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _skyPipeline!.Pipeline);
        var sets = stackalloc DescriptorSet[2] { _frameSets!.FrameSet, _skySet.Set };
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _skyPipeline.Layout, 0, 2, sets, 0, null);
        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
    }

    /// <summary>
    /// Records the G-buffer-normal, SSAO, and blur passes -- three separate render-pass
    /// instances, all recorded into <paramref name="cmd"/> BEFORE the main render pass begins
    ///. Caller has already uploaded this frame's shared instance buffer
    /// (<see cref="_instanceDrawer"/>'s <c>UploadInstanceBatch</c>) covering
    /// <paramref name="opaqueCount"/>+<paramref name="sceneOpaqueCount"/> opaque faces at the
    /// SAME index ranges the main pass will read later -- required, not a convenience: Vulkan's
    /// always-instanced <c>prim.vert</c> (which <c>vulkan/gnorm.frag</c> pairs with) has no
    /// non-instanced code path, so this pass must read the identical per-face instance data the
    /// main pass reads, at the identical offsets (see <see cref="VkGNormPipeline"/>'s own doc
    /// comment) -- computing a SEPARATE opaque-only packing here would clobber the instance
    /// buffer across passes.
    /// </summary>
    private unsafe void RenderSsaoPasses(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        Matrix4x4 proj, int opaqueCount, int sceneOpaqueCount)
    {
        EnsureGBufferTarget(vk, pixelSize);
        EnsureSsaoTargets(vk, pixelSize);

        // ── G-buffer normal pass ────────────────────────────────────────────────
        var gbufClear = stackalloc ClearValue[2]
        {
            new ClearValue { Color = new ClearColorValue { Float32_0 = 0f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 0f } },
            new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } }
        };
        var gbufBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _gbufferRenderPass,
            Framebuffer = _gbufFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 2,
            PClearValues = gbufClear
        };
        vk.Api.CmdBeginRenderPass(cmd, in gbufBegin, SubpassContents.Inline);
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _gnorm!.Pipeline);
        var frameSet = _frameSets!.FrameSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _gnorm.Layout, 0, 1, &frameSet, 0, null);
        // draws the same main-pass-filtered lists as the later opaque draw block
        // (_mainOpaqueVisible/_mainSceneOpaqueVisible), not the raw _opaqueFaces/_sceneOpaque --
        // matches GL's own DrawFacesNormal call sites, which pass the SAME frustum/
        // visibleSceneKeys as the main pass rather than computing their own.
        if (opaqueCount > 0) DrawFacesGNorm(vk, cmd, _mainOpaqueVisible, baseIndex: 0);
        if (sceneOpaqueCount > 0) DrawFacesGNorm(vk, cmd, _mainSceneOpaqueVisible, baseIndex: opaqueCount);
        vk.Api.CmdEndRenderPass(cmd);

        // ── SSAO pass ────────────────────────────────────────────────────────────
        var ssaoBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            Framebuffer = _ssaoFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 0
        };
        vk.Api.CmdBeginRenderPass(cmd, in ssaoBegin, SubpassContents.Inline);
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _ssaoPipeline!.Pipeline);
        var ssaoSet = _ssaoDescSet!.SsaoSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _ssaoPipeline.Layout, 0, 1, &ssaoSet, 0, null);

        // Radius/bias/strength match GL's hardcoded defaults exactly (SL viewer
        // llDrawPoolSimple SSAO pass defaults).
        var ssaoUbo = default(VkSsaoUbo);
        for (int i = 0; i < _ssaoKernel.Length && i < 64; i++) ssaoUbo.SetKernelSample(i, _ssaoKernel[i]);
        ssaoUbo.KernelSize = Math.Min(_ssaoKernel.Length, 64);
        ssaoUbo.NoiseScale = new Vector2(pixelSize.Width / 4.0f, pixelSize.Height / 4.0f);
        ssaoUbo.Proj = proj;
        ssaoUbo.ScreenSize = new Vector2(pixelSize.Width, pixelSize.Height);
        ssaoUbo.Radius = 0.5f;
        ssaoUbo.Bias = 0.025f;
        ssaoUbo.Strength = 1.2f;
        _ssaoDescSet.UpdateParams(ssaoUbo);

        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
        vk.Api.CmdEndRenderPass(cmd);

        // ── Blur pass ────────────────────────────────────────────────────────────
        var blurBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            Framebuffer = _ssaoBlurFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
            ClearValueCount = 0
        };
        vk.Api.CmdBeginRenderPass(cmd, in blurBegin, SubpassContents.Inline);
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _ssaoBlurPipeline!.Pipeline);
        var blurSet = _ssaoDescSet.BlurSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _ssaoBlurPipeline.Layout, 0, 1, &blurSet, 0, null);
        var texelSize = new Vector2(1.0f / pixelSize.Width, 1.0f / pixelSize.Height);
        vk.Api.CmdPushConstants(cmd, _ssaoBlurPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 8, &texelSize);
        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
        vk.Api.CmdEndRenderPass(cmd);
    }

    /// <summary>Draws every face in <paramref name="faces"/> through <see cref="_gnorm"/> --
    /// no per-face material-set bind (gnorm.frag reads only varyings, no descriptors of its
    /// own beyond the already-bound set 0), unlike <see cref="DrawFaces"/>.</summary>
    private unsafe void DrawFacesGNorm(VkContext vk, CommandBuffer cmd,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces, int baseIndex)
    {
        for (int i = 0; i < faces.Count; i++)
            _instanceDrawer!.DrawBatchedInstance(cmd, faces[i].Mesh, baseIndex + i);
    }

    /// <summary>
    /// A hemisphere sample kernel
    /// in tangent space, distributed with an accelerating bias toward the origin.
    /// </summary>
    private static Vector3[] BuildSsaoKernel(int count)
    {
        var rng = new Random(42); // deterministic seed, matches GL
        var kernel = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = (float)(rng.NextDouble() * 2.0 - 1.0);
            float z = (float)rng.NextDouble(); // [0,1] -- upper hemisphere only
            var s = Vector3.Normalize(new Vector3(x, y, z));
            float scale = (float)i / count;
            scale = 0.1f + scale * scale * 0.9f; // lerp(0.1, 1.0, scale^2)
            kernel[i] = s * scale;
        }
        return kernel;
    }

    private unsafe void EnsureGBufferTarget(VkContext vk, PixelSize size)
    {
        if (_gbufSize == size && _gbufNormalView.Handle != 0) return;
        DestroyGBufferTarget(vk);

        var normalInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in normalInfo, null, out _gbufNormalImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _gbufNormalImage, out var normalReq);
        var normalAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = normalReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, normalReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in normalAlloc, null, out _gbufNormalMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _gbufNormalImage, _gbufNormalMemory, 0).ThrowOnError();
        var normalViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _gbufNormalImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in normalViewInfo, null, out _gbufNormalView).ThrowOnError();

        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            // SampledBit on top of the usual DepthStencilAttachmentBit -- unlike the main
            // pass's own _depthImage (write-only, never sampled), this one is read back by the
            // SSAO pass as uDepthTex.
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _gbufDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _gbufDepthImage, out var depthReq);
        var depthAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = depthReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, depthReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in depthAlloc, null, out _gbufDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _gbufDepthImage, _gbufDepthMemory, 0).ThrowOnError();
        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _gbufDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in depthViewInfo, null, out _gbufDepthView).ThrowOnError();

        var attachments = stackalloc ImageView[2] { _gbufNormalView, _gbufDepthView };
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _gbufferRenderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out _gbufFramebuffer).ThrowOnError();

        _gbufSize = size;

        // Rewrite the SSAO pass's depth/normal sampler bindings to point at the NEW images --
        // only fires on (re)creation, matching VkSsaoDescriptorSet.UpdateGbufferInputs' own
        // "never per-frame" contract.
        var depthImageInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _gbufDepthView, Sampler = _ssaoNearestSampler };
        var normalImageInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _gbufNormalView, Sampler = _ssaoNearestSampler };
        _ssaoDescSet!.UpdateGbufferInputs(depthImageInfo, normalImageInfo);
    }

    private unsafe void DestroyGBufferTarget(VkContext vk)
    {
        if (_gbufFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _gbufFramebuffer, null);
        if (_gbufNormalView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _gbufNormalView, null);
        if (_gbufNormalImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _gbufNormalImage, null);
        if (_gbufNormalMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _gbufNormalMemory, null);
        if (_gbufDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _gbufDepthView, null);
        if (_gbufDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _gbufDepthImage, null);
        if (_gbufDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _gbufDepthMemory, null);
        _gbufFramebuffer = default;
        _gbufNormalView = default; _gbufNormalImage = default; _gbufNormalMemory = default;
        _gbufDepthView = default; _gbufDepthImage = default; _gbufDepthMemory = default;
        _gbufSize = default;
    }

    private unsafe void EnsureSsaoTargets(VkContext vk, PixelSize size)
    {
        if (_ssaoTargetSize == size && _ssaoColorView.Handle != 0) return;
        DestroySsaoTargets(vk);

        // SSAO-raw colour texture -- Nearest filtering (matches GL's own TextureMinFilter/
        // MagFilter.Nearest for this target).
        CreateR8Target(vk, size, out _ssaoColorImage, out _ssaoColorMemory, out _ssaoColorView);
        // Blur colour texture -- Linear filtering, matching GL exactly (the ONE difference
        // between the two R8 targets' sampler state).
        CreateR8Target(vk, size, out _ssaoBlurImage, out _ssaoBlurMemory, out _ssaoBlurView);

        var ssaoColorViewLocal = _ssaoColorView;
        var ssaoFbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            AttachmentCount = 1,
            PAttachments = &ssaoColorViewLocal,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in ssaoFbInfo, null, out _ssaoFramebuffer).ThrowOnError();

        var ssaoBlurViewLocal = _ssaoBlurView;
        var blurFbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _ssaoOffscreenRenderPass,
            AttachmentCount = 1,
            PAttachments = &ssaoBlurViewLocal,
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in blurFbInfo, null, out _ssaoBlurFramebuffer).ThrowOnError();

        _ssaoTargetSize = size;

        // Rewrite the blur pass's uSsaoTex binding (reads _ssaoColorImage) and the main prim
        // pipeline's uSsaoMap binding (reads _ssaoBlurImage, the FINAL occlusion term) -- both
        // only fire on (re)creation, matching the "never per-frame" contract established above.
        var ssaoColorInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _ssaoColorView, Sampler = _ssaoNearestSampler };
        _ssaoDescSet!.UpdateBlurInput(ssaoColorInfo);
        var ssaoBlurInfo = new DescriptorImageInfo { ImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageView = _ssaoBlurView, Sampler = _ssaoLinearSampler };
        _frameSets!.UpdateSsaoMap(ssaoBlurInfo);
    }

    private unsafe void CreateR8Target(VkContext vk, PixelSize size, out Image image, out DeviceMemory memory, out ImageView view)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8Unorm,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in info, null, out image).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, image, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in alloc, null, out memory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, image, memory, 0).ThrowOnError();
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out view).ThrowOnError();
    }

    /// <summary>
    /// Creates (or resizes) <see cref="_underwaterSourceImage"/>: a persistent copy target the
    /// underwater pass copies the just-rendered swapchain image into immediately before running,
    /// since Vulkan can't sample and write the same image within one pass. Unlike the SSAO/
    /// G-buffer targets, this image is never a render-pass color attachment -- only a
    /// <c>CmdCopyImage</c> destination and a sampled texture -- so it carries
    /// <c>TransferDstBit | SampledBit</c> usage, not <c>ColorAttachmentBit</c>, and has no
    /// framebuffer of its own. Called every frame the underwater pass actually runs (mirrors
    /// EnsureSsaoTargets' own "only when SSAO is enabled this frame" call site), early-returning
    /// once the size matches.
    /// </summary>
    private unsafe void EnsureUnderwaterTarget(VkContext vk, PixelSize size)
    {
        if (_underwaterTargetSize == size && _underwaterSourceView.Handle != 0) return;
        DestroyUnderwaterTarget(vk);

        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in info, null, out _underwaterSourceImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _underwaterSourceImage, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in alloc, null, out _underwaterSourceMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _underwaterSourceImage, _underwaterSourceMemory, 0).ThrowOnError();
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _underwaterSourceImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out _underwaterSourceView).ThrowOnError();

        _underwaterTargetSize = size;
        // Fresh image -- caller's first barrier must transition FROM Undefined/None, not a stale
        // prior layout/access left over from the target this just destroyed.
        _underwaterSourceLayout = ImageLayout.Undefined;
        _underwaterSourceAccess = AccessFlags.None;

        // Rewrite the underwater pass's uSceneColor binding to point at the NEW image -- only
        // fires on (re)creation, matching VkUnderwaterDescriptorSet.UpdateSceneColorInput's own
        // "never per-frame" contract.
        var sceneColorInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _underwaterSourceView,
            Sampler = _underwaterLinearSampler
        };
        _underwaterDescSet!.UpdateSceneColorInput(sceneColorInfo);
    }

    private unsafe void DestroyUnderwaterTarget(VkContext vk)
    {
        if (_underwaterSourceView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _underwaterSourceView, null);
        if (_underwaterSourceImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _underwaterSourceImage, null);
        if (_underwaterSourceMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _underwaterSourceMemory, null);
        _underwaterSourceView = default; _underwaterSourceImage = default; _underwaterSourceMemory = default;
        _underwaterTargetSize = default;
        _underwaterSourceLayout = ImageLayout.Undefined;
        _underwaterSourceAccess = AccessFlags.None;
    }

    private unsafe void DestroySsaoTargets(VkContext vk)
    {
        if (_ssaoFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _ssaoFramebuffer, null);
        if (_ssaoColorView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _ssaoColorView, null);
        if (_ssaoColorImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _ssaoColorImage, null);
        if (_ssaoColorMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _ssaoColorMemory, null);
        if (_ssaoBlurFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _ssaoBlurFramebuffer, null);
        if (_ssaoBlurView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _ssaoBlurView, null);
        if (_ssaoBlurImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _ssaoBlurImage, null);
        if (_ssaoBlurMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _ssaoBlurMemory, null);
        _ssaoFramebuffer = default; _ssaoColorView = default; _ssaoColorImage = default; _ssaoColorMemory = default;
        _ssaoBlurFramebuffer = default; _ssaoBlurView = default; _ssaoBlurImage = default; _ssaoBlurMemory = default;
        _ssaoTargetSize = default;
    }

    // ── Directional shadow target/pass ─────────────────────────────────

    /// <summary>Fixed 2048x2048 depth-only target, created ONCE at init (unlike the G-buffer/
    /// SSAO targets, this has no "Ensure"/resize logic -- its resolution is independent of the
    /// panel's own pixel size, matching GL's own fixed <c>ShadowMapSize</c>). CompareEnable
    /// sampler (sampler2DShadow, shadow.glsl) built the same way
    /// <see cref="VkPlaceholderTextures"/>'s own shadow placeholder is.</summary>
    private unsafe void CreateShadowTarget(VkContext vk)
    {
        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D(ShadowMapSize, ShadowMapSize, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            // TransferDstBit added alongside the original DepthStencilAttachmentBit|SampledBit:
            // required by VkPlaceholderTextures.UploadDepthAndTransition's clear-via-
            // TransferDstOptimal approach below (VUID-vkCmdClearDepthStencilImage-pRanges-02660
            // -- found via this same self-test run, one edit after the layout-mismatch fix above,
            // not assumed correct on the first try). One-time init cost only, no runtime-path
            // consequence.
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _shadowDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _shadowDepthImage, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in alloc, null, out _shadowDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _shadowDepthImage, _shadowDepthMemory, 0).ThrowOnError();

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _shadowDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in viewInfo, null, out _shadowDepthView).ThrowOnError();

        // Real bug found via 8c-4's own combined self-test (ShadowsEnabled=false is this
        // control's default, so RenderShadowPass never runs and never gets a chance to
        // transition this image via the shadow render pass's own LoadOp=Clear): without this
        // explicit initial transition, _shadowMapInfo below claims ShaderReadOnlyOptimal while
        // the actual image stays Undefined (its InitialLayout above) for the panel's whole
        // lifetime whenever shadows are off -- a real VUID-vkCmdDraw-None-09600 validation error
        // every time prim.frag samples uShadowMap in the main pass. Clearing to depth=1.0 also
        // means an un-rendered real shadow map reads as "nothing occluded" against the
        // LessOrEqual comparison sampler, matching VkPlaceholderTextures' own placeholder-shadow-
        // map intent (see its own doc comment) rather than leaving garbage/undefined values.
        VkPlaceholderTextures.UploadDepthAndTransition(vk, _shadowDepthImage, layers: 1);

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            CompareEnable = true,
            CompareOp = CompareOp.LessOrEqual,
            MinLod = 0,
            MaxLod = 1,
            BorderColor = BorderColor.FloatOpaqueWhite
        };
        vk.Api.CreateSampler(vk.Device, in samplerInfo, null, out _shadowSampler).ThrowOnError();

        var viewLocal = _shadowDepthView;
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _shadowRenderPass,
            AttachmentCount = 1,
            PAttachments = &viewLocal,
            Width = ShadowMapSize,
            Height = ShadowMapSize,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out _shadowFramebuffer).ThrowOnError();

        _shadowMapInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _shadowDepthView,
            Sampler = _shadowSampler
        };
        _frameSets!.UpdateShadowMap(_shadowMapInfo);
    }

    private unsafe void DestroyShadowTarget(VkContext vk)
    {
        if (_shadowFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _shadowFramebuffer, null);
        if (_shadowSampler.Handle != 0) vk.Api.DestroySampler(vk.Device, _shadowSampler, null);
        if (_shadowDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _shadowDepthView, null);
        if (_shadowDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _shadowDepthImage, null);
        if (_shadowDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _shadowDepthMemory, null);
        _shadowFramebuffer = default; _shadowSampler = default;
        _shadowDepthView = default; _shadowDepthImage = default; _shadowDepthMemory = default;
    }

    /// <summary>
    /// Computes this frame's directional light view-projection (texel-snapped ortho volume
    /// centred on the camera, NaN guards on Sky.SunDirection -- a
    /// NaN sun direction is a real, previously-hit hazard, not defensive paranoia -- and a
    /// degenerate-zenith up-vector guard). Returns false (leaving <see cref="_shadowLightVp"/>
    /// unchanged) if a finite matrix can't be produced this frame, mirroring GL's own
    /// early-return/_hasDirShadow=false posture. Deliberately does NOT replicate GL's
    /// _spatialGrid frustum-cull query for the shadow pass's own visible-set -- SceneSpatialGrid/
    /// frustum culling was already deferred out of its own scope (optimization on top
    /// of the streaming substrate, not a prerequisite), so this draws every opaque face
    /// unconditionally, same simplification.
    /// </summary>
    private bool TryComputeShadowLightVp(out Matrix4x4 lightView, out Matrix4x4 lightProj)
    {
        lightView = default; lightProj = default;
        var eye = _camera.EyePosition;
        if (!IsFinite(eye) || !IsFinite(Sky.SunDirection) || Sky.SunDirection.LengthSquared() < 1e-10f)
            return false;
        var sunDir = Vector3.Normalize(Sky.SunDirection);
        var up = MathF.Abs(Vector3.Dot(sunDir, Vector3.UnitZ)) > 0.999f ? Vector3.UnitY : Vector3.UnitZ;

        var lightRotOnly = Matrix4x4.CreateLookAt(Vector3.Zero, -sunDir, up);
        var centerLs = Vector3.Transform(eye, lightRotOnly);
        float texelSize = (2f * ShadowRadius) / ShadowMapSize;
        centerLs.X = MathF.Floor(centerLs.X / texelSize) * texelSize;
        centerLs.Y = MathF.Floor(centerLs.Y / texelSize) * texelSize;
        Matrix4x4.Invert(lightRotOnly, out var invRot);
        var snappedCenter = Vector3.Transform(centerLs, invRot);

        var lightEye = snappedCenter + sunDir * ShadowDepthMargin;
        lightView = Matrix4x4.CreateLookAt(lightEye, snappedCenter, up);
        lightProj = Matrix4x4.CreateOrthographicOffCenter(
            -ShadowRadius, ShadowRadius, -ShadowRadius, ShadowRadius, 1f, ShadowDepthMargin * 2f);
        var lightVp = lightView * lightProj;
        if (!IsFinite(lightVp)) return false;

        _shadowLightVp = lightVp;
        return true;
    }

    private static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool IsFinite(in Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
        float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
        float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
        float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);

    /// <summary>
    /// Records the directional shadow depth pass into <paramref name="cmd"/>. Caller has
    /// already uploaded this frame's shared instance buffer including the shadow region (light-
    /// space MVP, baked via <see cref="WriteInstanceData"/> with the light's view/proj) at
    /// <paramref name="shadowBase"/>/<paramref name="shadowSceneBase"/>. No material set bind
    /// needed (shadow_depth.frag declares no descriptors) -- reuses <see cref="DrawFacesGNorm"/>
    /// (plain batched-instance draw, no per-face bind), same shape SSAO's G-buffer pass already
    /// established for exactly this "no set 2" case. Restores viewport/scissor to
    /// <paramref name="pixelSize"/> before returning -- this target's fixed 2048x2048 resolution
    /// differs from the panel's own, and viewport/scissor are dynamic state shared across every
    /// render pass recorded into this command buffer, including the main pass that follows.
    /// </summary>
    private unsafe void RenderShadowPass(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        int shadowBase, int shadowSceneBase, int opaqueCount, int sceneOpaqueCount)
    {
        var clearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } };
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _shadowRenderPass,
            Framebuffer = _shadowFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(ShadowMapSize, ShadowMapSize)),
            ClearValueCount = 1,
            PClearValues = &clearValue
        };
        vk.Api.CmdBeginRenderPass(cmd, in beginInfo, SubpassContents.Inline);

        var shadowViewport = new Viewport { X = 0, Y = 0, Width = ShadowMapSize, Height = ShadowMapSize, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in shadowViewport);
        var shadowScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D(ShadowMapSize, ShadowMapSize) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &shadowScissor);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _shadowPipeline!.Pipeline);
        var frameSet = _frameSets!.FrameSet;
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _shadowPipeline.Layout, 0, 1, &frameSet, 0, null);

        // draws this pass's own independently-culled lists (_shadowOpaqueVisible/
        // _shadowSceneOpaqueVisible), not the raw _opaqueFaces/_sceneOpaque -- the caller passes
        // this pass's own filtered counts too, matching GL's independent shadow-frustum grid
        // query (never gated on FrustumCullingEnabled).
        if (opaqueCount > 0) DrawFacesGNorm(vk, cmd, _shadowOpaqueVisible, baseIndex: shadowBase);
        if (sceneOpaqueCount > 0) DrawFacesGNorm(vk, cmd, _shadowSceneOpaqueVisible, baseIndex: shadowSceneBase);

        vk.Api.CmdEndRenderPass(cmd);

        RestoreMainViewportScissor(vk, cmd, pixelSize);
    }

    private static unsafe void RestoreMainViewportScissor(VkContext vk, CommandBuffer cmd, PixelSize pixelSize)
    {
        var viewport = new Viewport { X = 0, Y = 0, Width = pixelSize.Width, Height = pixelSize.Height, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in viewport);
        var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &scissor);
    }

    // ── Water: reflection target/pass + surface pass ───────────────────

    private static VkTexture? LoadEmbeddedWaterTexture(VkContext vk, string filename)
    {
        var uri = new Uri("avares://RadegastVeles/Rendering/shader_data/" + filename);
        using var stream = Avalonia.Platform.AssetLoader.Open(uri);
        var bitmap = SKBitmap.Decode(stream);
        if (bitmap == null) return null;
        return new VkTexture(vk, VkTexture.Preprocess(bitmap));
    }

    /// <summary>Fixed 512x512 colour+depth target, created ONCE at init (like
    /// <see cref="CreateShadowTarget"/>, no per-frame resize logic -- GL's own
    /// <c>WaterReflSize</c> is independent of the panel's pixel size too). Reuses
    /// <see cref="VkRenderPass.CreateGBufferPass"/>'s shape: the reflection colour texture is
    /// SAMPLED by water.frag later the same frame, unlike the main
    /// scene pass's own colour attachment, which only ever gets blitted to the swapchain -- it
    /// needs the entry/exit subpass dependencies and ShaderReadOnlyOptimal final layout
    /// CreateGBufferPass already provides, not CreateMainScenePass's shape). The depth
    /// attachment gets SampledBit too even though nothing ever samples it -- CreateGBufferPass's
    /// shared shape sets FinalLayout=ShaderReadOnlyOptimal on BOTH attachments, and that layout
    /// transition is illegal without the matching usage flag regardless of whether anything
    /// reads through it.</summary>
    private unsafe void CreateWaterReflectionTarget(VkContext vk)
    {
        _waterReflRenderPass = VkRenderPass.CreateGBufferPass(vk, Format.R8G8B8A8Unorm, Format.D32Sfloat);

        var colorInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(WaterReflSize, WaterReflSize, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in colorInfo, null, out _waterReflColorImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _waterReflColorImage, out var colorReq);
        var colorAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = colorReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, colorReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in colorAlloc, null, out _waterReflColorMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _waterReflColorImage, _waterReflColorMemory, 0).ThrowOnError();
        var colorViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _waterReflColorImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in colorViewInfo, null, out _waterReflColorView).ThrowOnError();

        var colorSamplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0,
            MaxLod = 0
        };
        vk.Api.CreateSampler(vk.Device, in colorSamplerInfo, null, out _waterReflColorSampler).ThrowOnError();

        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D(WaterReflSize, WaterReflSize, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _waterReflDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _waterReflDepthImage, out var depthReq);
        var depthAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = depthReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, depthReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in depthAlloc, null, out _waterReflDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _waterReflDepthImage, _waterReflDepthMemory, 0).ThrowOnError();
        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _waterReflDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in depthViewInfo, null, out _waterReflDepthView).ThrowOnError();

        var attachments = stackalloc ImageView[2] { _waterReflColorView, _waterReflDepthView };
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _waterReflRenderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = WaterReflSize,
            Height = WaterReflSize,
            Layers = 1
        };
        vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out _waterReflFramebuffer).ThrowOnError();
    }

    private unsafe void DestroyWaterReflectionTarget(VkContext vk)
    {
        if (_waterReflFramebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, _waterReflFramebuffer, null);
        if (_waterReflColorSampler.Handle != 0) vk.Api.DestroySampler(vk.Device, _waterReflColorSampler, null);
        if (_waterReflColorView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _waterReflColorView, null);
        if (_waterReflColorImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _waterReflColorImage, null);
        if (_waterReflColorMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _waterReflColorMemory, null);
        if (_waterReflDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _waterReflDepthView, null);
        if (_waterReflDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _waterReflDepthImage, null);
        if (_waterReflDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _waterReflDepthMemory, null);
        if (_waterReflRenderPass.Handle != 0) vk.Api.DestroyRenderPass(vk.Device, _waterReflRenderPass, null);
        _waterReflFramebuffer = default; _waterReflColorSampler = default;
        _waterReflColorView = default; _waterReflColorImage = default; _waterReflColorMemory = default;
        _waterReflDepthView = default; _waterReflDepthImage = default; _waterReflDepthMemory = default;
        _waterReflRenderPass = default;
    }

    /// <summary>
    /// Records the water-reflection pre-pass: the opaque scene, drawn through
    /// <see cref="VkPrimPipeline.ReflOpaque"/> (same shaders/layout as the main opaque pass,
    /// FrontFace flipped to compensate the Z-mirror's winding flip) with the REFLECTED camera's
    /// own <see cref="_frameSetsRefl"/> descriptor set (see that field's own doc comment for why
    /// it can't share <see cref="_frameSets"/>). Caller has already uploaded this frame's shared
    /// instance buffer including the reflection region (baked via <see cref="WriteInstanceData"/>
    /// with <paramref name="reflView"/>/<paramref name="proj"/>) at
    /// <paramref name="reflBase"/>/<paramref name="reflSceneBase"/>, and has already written
    /// <paramref name="reflView"/>-relative View/ViewInv/SunDir (HasSsao=0) into
    /// <see cref="_frameSetsRefl"/>'s UBO. Restores viewport/scissor to
    /// <paramref name="pixelSize"/> before returning, same reasoning as
    /// <see cref="RenderShadowPass"/>.
    /// </summary>
    private unsafe void RenderWaterReflectionPass(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        int reflBase, int reflSceneBase, int opaqueCount, int sceneOpaqueCount)
    {
        var clearValues = stackalloc ClearValue[2]
        {
            new ClearValue { Color = new ClearColorValue { Float32_0 = 0.39f, Float32_1 = 0.58f, Float32_2 = 0.93f, Float32_3 = 1f } },
            new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } }
        };
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _waterReflRenderPass,
            Framebuffer = _waterReflFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(WaterReflSize, WaterReflSize)),
            ClearValueCount = 2,
            PClearValues = clearValues
        };
        vk.Api.CmdBeginRenderPass(cmd, in beginInfo, SubpassContents.Inline);

        var reflViewport = new Viewport { X = 0, Y = 0, Width = WaterReflSize, Height = WaterReflSize, MinDepth = 0, MaxDepth = 1 };
        vk.Api.CmdSetViewport(cmd, 0, 1, in reflViewport);
        var reflScissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D(WaterReflSize, WaterReflSize) };
        vk.Api.CmdSetScissor(cmd, 0, 1, &reflScissor);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _prim!.ReflOpaque);
        var frameAndPassSets = stackalloc DescriptorSet[2] { _frameSetsRefl!.FrameSet, _frameSetsRefl.PassSet };
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _prim.Layout, 0, 2, frameAndPassSets, 0, null);

        // draws this pass's own independently-culled lists (_reflOpaqueVisible/
        // _reflSceneOpaqueVisible), not the raw _opaqueFaces/_sceneOpaque -- the caller passes
        // this pass's own filtered counts too, matching GL's independent reflection-frustum
        // grid query (never gated on FrustumCullingEnabled).
        if (opaqueCount > 0) DrawFaces(vk, cmd, _reflOpaqueVisible, baseIndex: reflBase);
        if (sceneOpaqueCount > 0) DrawFaces(vk, cmd, _reflSceneOpaqueVisible, baseIndex: reflSceneBase);

        vk.Api.CmdEndRenderPass(cmd);

        RestoreMainViewportScissor(vk, cmd, pixelSize);
    }

    /// <summary>
    /// Draws the water surface as a full-screen triangle into the currently-recording main
    /// render pass. Ray-plane intersection done
    /// entirely in water.frag; this method only advances the animation clock and uploads
    /// uniforms. Set 0 is the SAME <see cref="_frameSets"/> descriptor set the main geometry
    /// pass binds (main camera, not the reflected one -- the water SURFACE itself is real
    /// geometry seen from the real camera; only the reflection PRE-PASS uses the mirrored
    /// camera), so the caller must have already written this frame's real
    /// <see cref="VkPerFrameUbo"/> (the SECOND write, with the correct HasSsao) via
    /// <c>_frameSets.UpdatePerFrame</c> before calling this.
    /// </summary>
    private unsafe void DrawWater(VkContext vk, CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj, float waterHeight, bool underwater)
    {
        long now = Environment.TickCount64;
        float dt = _waterLastTick == 0 ? 0f : MathF.Min((now - _waterLastTick) / 1000f, 0.1f);
        _waterLastTick = now;
        _waterTime += dt;

        var eye = _camera.EyePosition;
        var viewProj = view * proj;
        Matrix4x4.Invert(viewProj, out var invVp);

        var waterUbo = default(VkWaterUbo);
        waterUbo.ViewProj = viewProj;
        waterUbo.ReflViewProj = _lastReflViewProj;
        waterUbo.InvViewProj = invVp;
        waterUbo.EyePos = eye;
        waterUbo.WaterHeight = waterHeight;
        waterUbo.Time = _waterTime;
        waterUbo.WaterColor = WaterFogColor;
        // Forced off underwater regardless of WaterReflectionsEnabled -- no reflection pass runs
        // for an underwater frame (see doWaterReflThisFrame's own !underwater gate), so
        // sampling uReflectionTex here would show a stale reflection from the last above-water
        // frame rather than anything real.
        waterUbo.HasReflection = (!underwater && _waterReflReady && WaterReflectionsEnabled) ? 1 : 0;
        _waterDescSet!.UpdateWater(waterUbo);

        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _waterPipeline!.Pipeline);
        var sets = stackalloc DescriptorSet[2] { _frameSets!.FrameSet, _waterDescSet.Set };
        vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _waterPipeline.Layout, 0, 2, sets, 0, null);
        vk.Api.CmdDraw(cmd, 3, 1, 0, 0);
    }

    /// <summary>
    /// Composites the underwater post-process effect (screen-space refraction wobble + soft
    /// caustic highlight + depth-based fog tint, see underwater.frag) on top of the just-rendered
    /// frame. Called from RenderFrame's main pass ONLY when <c>underwater</c> is true -- when
    /// false this entire method, including the copy below, is skipped, so the feature costs
    /// nothing above water.
    /// <para>
    /// Recorded into the SAME command buffer as the main pass, immediately after its
    /// <c>CmdEndRenderPass</c> and before <c>cmd.Submit()</c> -- see <see cref="VkRenderPass.
    /// CreateUnderwaterPass"/>'s own doc comment for why this needs a copy-out rather than the
    /// subpass-dependency synchronization SSAO/blur use: this pass samples the ALREADY-COMPOSITED
    /// swapchain image the main pass just wrote, which Vulkan cannot do within the same render
    /// pass a color attachment is written in (no feedback-loop sampling).
    /// </para>
    /// <para>
    /// The swapchain image's own layout transitions go through <see cref="VkInteropImage.
    /// TransitionLayout"/> (same helper <see cref="VkInteropSwapchain"/> itself uses for
    /// BeginDraw/Present), which tracks the image's current layout/access internally -- so this
    /// method never has to guess or hardcode what layout the image is already in, only where it
    /// needs to end up. <see cref="_underwaterSourceImage"/> has no such wrapper (it's a plain
    /// <see cref="Image"/>), so its layout/access are tracked by hand in
    /// <see cref="_underwaterSourceLayout"/>/<see cref="_underwaterSourceAccess"/> instead.
    /// </para>
    /// </summary>
    private unsafe void RenderUnderwaterPass(VkContext vk, CommandBuffer cmd, PixelSize pixelSize,
        VkInteropImage swapchainImage, float waterHeightVal)
    {
        EnsureUnderwaterTarget(vk, pixelSize);

        long now = Environment.TickCount64;
        float dt = _underwaterLastTick == 0 ? 0f : MathF.Min((now - _underwaterLastTick) / 1000f, 0.1f);
        _underwaterLastTick = now;
        _underwaterTime += dt;

        // ── Copy the just-rendered frame out so it can be sampled as this pass's input ────────
        swapchainImage.TransitionLayout(cmd, ImageLayout.TransferSrcOptimal, AccessFlags.TransferReadBit);
        VkMemoryHelper.TransitionLayout(vk.Api, cmd, _underwaterSourceImage,
            _underwaterSourceLayout, _underwaterSourceAccess,
            ImageLayout.TransferDstOptimal, AccessFlags.TransferWriteBit, 1);

        var copyRegion = new ImageCopy
        {
            SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            SrcOffset = new Offset3D(0, 0, 0),
            DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            DstOffset = new Offset3D(0, 0, 0),
            Extent = new Extent3D((uint)pixelSize.Width, (uint)pixelSize.Height, 1)
        };
        vk.Api.CmdCopyImage(cmd, swapchainImage.InternalHandle, ImageLayout.TransferSrcOptimal,
            _underwaterSourceImage, ImageLayout.TransferDstOptimal, 1, in copyRegion);

        // ── Transition both images back to how this pass (and everything after it) needs them ─
        VkMemoryHelper.TransitionLayout(vk.Api, cmd, _underwaterSourceImage,
            ImageLayout.TransferDstOptimal, AccessFlags.TransferWriteBit,
            ImageLayout.ShaderReadOnlyOptimal, AccessFlags.ShaderReadBit, 1);
        _underwaterSourceLayout = ImageLayout.ShaderReadOnlyOptimal;
        _underwaterSourceAccess = AccessFlags.ShaderReadBit;

        // Re-enter as a render target -- CreateUnderwaterPass's InitialLayout=ColorAttachmentOptimal
        // requires this to be accurate (LoadOp=Load reads whatever's actually there).
        swapchainImage.TransitionLayout(cmd, ImageLayout.ColorAttachmentOptimal, AccessFlags.ColorAttachmentWriteBit);

        // ── Draw the full-screen composite directly into the swapchain image ───────────────────
        var attachment = new ImageView(swapchainImage.ViewHandle);
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _underwaterPass,
            AttachmentCount = 1,
            PAttachments = &attachment,
            Width = (uint)pixelSize.Width,
            Height = (uint)pixelSize.Height,
            Layers = 1
        };
        Framebuffer framebuffer = default;
        try
        {
            vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out framebuffer).ThrowOnError();

            var viewport = new Viewport { X = 0, Y = 0, Width = pixelSize.Width, Height = pixelSize.Height, MinDepth = 0, MaxDepth = 1 };
            vk.Api.CmdSetViewport(cmd, 0, 1, in viewport);
            var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height) };
            vk.Api.CmdSetScissor(cmd, 0, 1, &scissor);

            var beginInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _underwaterPass,
                Framebuffer = framebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
                ClearValueCount = 0,
                PClearValues = null
            };
            vk.Api.CmdBeginRenderPass(cmd, in beginInfo, SubpassContents.Inline);

            vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _underwaterPipeline!.Pipeline);
            var set = _underwaterDescSet!.Set;
            vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _underwaterPipeline.Layout, 0, 1, &set, 0, null);

            // Depth-based tint intensity: fully tinted a few metres below the surface, matching
            // the depth falloff a real underwater "visibility" would have -- tuned by eye, not
            // derived from any EEP parameter (SL doesn't expose an underwater visibility-distance
            // setting).
            float depthBelow = MathF.Max(0f, waterHeightVal - _camera.EyePosition.Z);
            float intensity = Math.Clamp(depthBelow / 4f, 0f, 1f);
            var waterFog = WaterFogColor;
            var pushData = stackalloc float[5] { waterFog.X, waterFog.Y, waterFog.Z, intensity, _underwaterTime };
            vk.Api.CmdPushConstants(cmd, _underwaterPipeline.Layout, ShaderStageFlags.FragmentBit, 0, 20, pushData);

            vk.Api.CmdDraw(cmd, 3, 1, 0, 0);

            vk.Api.CmdEndRenderPass(cmd);
        }
        finally
        {
            if (framebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, framebuffer, null);
        }
    }

    /// <summary>
    /// Draws every currently-submitted face's edges via <see cref="VkWireframePipeline"/>:
    /// one push-constant MVP
    /// + <see cref="VkMesh.DrawLines"/> call per face, no instance batching -- wireframe.vert
    /// has no per-instance attributes, see <see cref="VkWireframePipeline"/>'s own comment.
    /// </summary>
    private unsafe void DrawWireframeOverlay(VkContext vk, CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj)
    {
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _wireframe!.Pipeline);
        DrawWireframeList(vk, cmd, _opaqueFaces, view, proj);
        DrawWireframeList(vk, cmd, _alphaFaces, view, proj);
        DrawWireframeList(vk, cmd, _sceneOpaque, view, proj);
        DrawWireframeList(vk, cmd, _sceneAlpha, view, proj);
    }

    private unsafe void DrawWireframeList(VkContext vk, CommandBuffer cmd,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces, Matrix4x4 view, Matrix4x4 proj)
    {
        Span<float> mvp = stackalloc float[16];
        foreach (var (mesh, _, face) in faces)
        {
            var faceMvp = face.Transform * view * proj;
            CopyMatrixSpan(faceMvp, mvp);
            fixed (float* p = mvp)
                vk.Api.CmdPushConstants(cmd, _wireframe!.Layout, ShaderStageFlags.VertexBit, 0, 64, p);
            mesh.DrawLines(cmd);
        }
    }

    /// <summary>
    /// Draws the SL-style selection outline (see <see cref="VkOutlinePipeline"/>'s own doc
    /// comment for the inverted-hull technique) around every currently-submitted face whose
    /// <see cref="PrimRenderFace.PrimLocalId"/> matches <see cref="_selectedOutlineLocalId"/>.
    /// Walks the same four draw lists <see cref="DrawWireframeOverlay"/> does, since that's
    /// every face this frame could possibly need outlining.
    /// </summary>
    private unsafe void DrawSelectionOutline(VkContext vk, CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj)
    {
        vk.Api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _outline!.Pipeline);
        DrawSelectionOutlineList(vk, cmd, _opaqueFaces, view, proj);
        DrawSelectionOutlineList(vk, cmd, _alphaFaces, view, proj);
        DrawSelectionOutlineList(vk, cmd, _sceneOpaque, view, proj);
        DrawSelectionOutlineList(vk, cmd, _sceneAlpha, view, proj);
    }

    private unsafe void DrawSelectionOutlineList(VkContext vk, CommandBuffer cmd,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces, Matrix4x4 view, Matrix4x4 proj)
    {
        Span<float> mvp = stackalloc float[16];
        foreach (var (mesh, _, face) in faces)
        {
            if (face.PrimLocalId != _selectedOutlineLocalId) continue;
            var faceMvp = face.Transform * view * proj;
            CopyMatrixSpan(faceMvp, mvp);
            fixed (float* p = mvp)
                vk.Api.CmdPushConstants(cmd, _outline!.Layout, ShaderStageFlags.VertexBit, 0, 64, p);
            mesh.Draw(cmd);
        }
    }

    private static void CopyMatrixSpan(Matrix4x4 m, Span<float> dest)
    {
        dest[0] = m.M11; dest[1] = m.M12; dest[2] = m.M13; dest[3] = m.M14;
        dest[4] = m.M21; dest[5] = m.M22; dest[6] = m.M23; dest[7] = m.M24;
        dest[8] = m.M31; dest[9] = m.M32; dest[10] = m.M33; dest[11] = m.M34;
        dest[12] = m.M41; dest[13] = m.M42; dest[14] = m.M43; dest[15] = m.M44;
    }

    /// <summary>
    /// Renders every face's flat-color pick ID into the offscreen pick target, reads back the
    /// single pixel at (<paramref name="px"/>,<paramref name="py"/>), decodes it against
    /// <see cref="_pickMap"/>, and raises <see cref="FaceClicked"/> on a hit. Runs as its own
    /// one-off command buffer (create/record/submit/wait), separate from the main frame's
    /// swapchain-present command buffer -- avoids entangling this deliberate CPU stall with the
    /// swapchain's keyed-mutex/present cycle, same reasoning <see cref="VkTexture"/>'s
    /// synchronous upload already uses for its own one-off buffer.
    /// </summary>
    private unsafe void RunPickPass(VkContext vk, PixelSize pixelSize, Matrix4x4 view, Matrix4x4 proj, int px, int py)
    {
        _pickRequested = false;
        if (_pick == null) return;

        // Flip py once, here, before BOTH the readback coordinate below and the ComputeHitInfo
        // call, so the two can't disagree on which screen row was clicked -- mirrors GL's own
        // single already-flipped py, used identically by its ReadPixels and its ComputeHitInfo
        // call. Flipping py inside ComputeHitInfo ALONE (leaving the readback unflipped) desyncs
        // the two, so the ray no longer lands on the face the readback selected.
        py = Math.Clamp(pixelSize.Height - py, 0, pixelSize.Height - 1);

        EnsurePickTarget(vk, pixelSize);

        // Rebuilt every pick to match whatever draw order was actually used this frame --
        // required, not just tidy, since the alpha list's back-to-front sort changes whenever
        // the camera moves.
        _pickMap.Clear();
        _cpuFaceData.Clear();
        foreach (var (_, _, face) in _opaqueFaces)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }
        foreach (var (_, _, face) in _alphaFaces)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }
        foreach (var (_, _, face) in _sceneOpaque)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }
        foreach (var (_, _, face) in _sceneAlpha)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }

        var cmd = vk.Pool.CreateCommandBuffer("VkViewportControl.RunPickPass");
        cmd.BeginRecording();

        Framebuffer framebuffer = default;
        try
        {
            var attachments = stackalloc ImageView[2] { _pickColorView, _pickDepthView };
            var fbInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = (uint)pixelSize.Width,
                Height = (uint)pixelSize.Height,
                Layers = 1
            };
            vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out framebuffer).ThrowOnError();

            var viewport = new Viewport { X = 0, Y = 0, Width = pixelSize.Width, Height = pixelSize.Height, MinDepth = 0, MaxDepth = 1 };
            vk.Api.CmdSetViewport(cmd.InternalHandle, 0, 1, in viewport);
            var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height) };
            vk.Api.CmdSetScissor(cmd.InternalHandle, 0, 1, &scissor);

            // Clear color alpha=0 acts as the "no hit" sentinel -- matches GL's
            // ClearColor(0,0,0,0) exactly; a real face's pick color always has alpha=1.
            var clearValues = stackalloc ClearValue[2]
            {
                new ClearValue { Color = new ClearColorValue { Float32_0 = 0f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 0f } },
                new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } }
            };
            var beginInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass,
                Framebuffer = framebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
                ClearValueCount = 2,
                PClearValues = clearValues
            };
            vk.Api.CmdBeginRenderPass(cmd.InternalHandle, in beginInfo, SubpassContents.Inline);

            vk.Api.CmdBindPipeline(cmd.InternalHandle, PipelineBindPoint.Graphics, _pick.Pipeline);
            DrawPickList(vk, cmd.InternalHandle, _opaqueFaces, view, proj, 1);
            DrawPickList(vk, cmd.InternalHandle, _alphaFaces, view, proj, 1 + _opaqueFaces.Count);
            DrawPickList(vk, cmd.InternalHandle, _sceneOpaque, view, proj, 1 + _opaqueFaces.Count + _alphaFaces.Count);
            DrawPickList(vk, cmd.InternalHandle, _sceneAlpha, view, proj, 1 + _opaqueFaces.Count + _alphaFaces.Count + _sceneOpaque.Count);

            vk.Api.CmdEndRenderPass(cmd.InternalHandle);

            // Render loop owns this transition, same established pattern as the main swapchain
            // image (VkRenderPass.cs's layout-ownership decision): FinalLayout=ColorAttachmentOptimal
            // needs an explicit barrier to TransferSrcOptimal before the copy below.
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.ColorAttachmentOptimal,
                NewLayout = ImageLayout.TransferSrcOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _pickColorImage,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit
            };
            vk.Api.CmdPipelineBarrier(cmd.InternalHandle, PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, in barrier);

            // 1x1 region at the click point, not the whole image -- matches GL's single-pixel
            // ReadPixels, keeps the readback buffer tiny regardless of viewport size.
            var copyRegion = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(px, py, 0),
                ImageExtent = new Extent3D(1, 1, 1)
            };
            vk.Api.CmdCopyImageToBuffer(cmd.InternalHandle, _pickColorImage, ImageLayout.TransferSrcOptimal,
                _pickReadbackBuffer, 1, in copyRegion);

            // Forces the wait (fence) before this method returns -- synchronous, matching
            // VkTexture's own upload pattern, required since the map+read below needs the copy
            // to have actually completed. SubmitAndWait waits only on this buffer's own fence,
            // not every other buffer outstanding in the shared pool.
            cmd.SubmitAndWait();
        }
        finally
        {
            if (framebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, framebuffer, null);
        }

        void* mapped = null;
        vk.Api.MapMemory(vk.Device, _pickReadbackMemory, 0, 4, 0, ref mapped).ThrowOnError();
        var pixel = new byte[4];
        new ReadOnlySpan<byte>(mapped, 4).CopyTo(pixel);
        vk.Api.UnmapMemory(vk.Device, _pickReadbackMemory);

        // R+G+B encode a 1-based index into _pickMap (clear alpha=0 / non-zero RGB = a real
        // hit). Ported verbatim from GL's own decode.
        var purpose = _pickPurpose;
        if (pixel[0] != 0 || pixel[1] != 0 || pixel[2] != 0)
        {
            uint idx = (uint)(pixel[0] | (pixel[1] << 8) | (pixel[2] << 16));
            if (idx >= 1 && (int)idx <= _pickMap.Count)
            {
                var (primLocalId, faceIndex) = _pickMap[(int)(idx - 1)];
                var hitInfo = ComputeHitInfo((int)(idx - 1), px, py, pixelSize.Width, pixelSize.Height, view, proj);
                if (purpose == PickPurpose.CameraFocus)
                {
                    // Real SL: Alt+click is a pure camera action, resolved directly against the
                    // camera here -- it never reaches the VM/object-interaction layer at all.
                    var focusPos = hitInfo.Position;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { _camera.Target = focusPos; RequestRender(); });
                }
                else if (purpose == PickPurpose.Select)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ObjectRightClicked?.Invoke(primLocalId, faceIndex, hitInfo));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => FaceClicked?.Invoke(primLocalId, faceIndex, hitInfo));
                }
            }
        }
        // A double-click (Touch) or an Alt+click (CameraFocus, always a ground-pick candidate --
        // see RequestPick's own doc comment) that missed every object falls through to the
        // ground-hit ray march, mirroring GL's own miss-branch for the double-click case.
        else if (_groundPickRequested)
        {
            if (TryGetGroundHit(px, py, pixelSize.Width, pixelSize.Height, view, proj, out var groundPos))
            {
                if (purpose == PickPurpose.CameraFocus)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { _camera.Target = groundPos; RequestRender(); });
                else
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => GroundClicked?.Invoke(groundPos));
            }
        }
    }

    /// <summary>
    /// Draws each face with a unique flat pick color encoding its 1-based index (R=bits 0-7,
    /// G=bits 8-15, B=bits 16-23, alpha always 1). No instance batching --
    /// wireframe.vert has no per-instance attributes, matches <see cref="DrawWireframeList"/>'s
    /// own per-face push-constant loop.
    /// </summary>
    private unsafe void DrawPickList(VkContext vk, CommandBuffer cmd,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces,
        Matrix4x4 view, Matrix4x4 proj, int startIdx)
    {
        Span<float> pushData = stackalloc float[20]; // 16 (mat4 uMvp) + 4 (vec4 uPickColor)
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i].Face;
            var mvp = face.Transform * view * proj;
            CopyMatrixSpan(mvp, pushData[..16]);

            uint idx = (uint)(startIdx + i); // 1-based
            pushData[16] = (idx & 0xFF) / 255f;
            pushData[17] = ((idx >> 8) & 0xFF) / 255f;
            pushData[18] = ((idx >> 16) & 0xFF) / 255f;
            pushData[19] = 1f;

            fixed (float* p = pushData)
                vk.Api.CmdPushConstants(cmd, _pick!.Layout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, 80, p);
            faces[i].Mesh.Draw(cmd);
        }
    }

    /// <summary>
    /// CPU ray-vs-heightfield intersection for a double-click that missed every
    /// object in the pick buffer. Marches the same screen-to-world ray used by
    /// <see cref="ComputeHitInfo"/> in 1m steps via <see cref="TerrainHeightProvider"/> until it
    /// crosses the terrain surface, then linearly interpolates between the two straddling
    /// samples (pure
    /// System.Numerics math) with the same view/proj-as-parameters and no-Y-flip adaptations
    /// <see cref="ComputeHitInfo"/>'s own doc comment already documents for this port.
    /// </summary>
    private bool TryGetGroundHit(int px, int py, int w, int h, Matrix4x4 view, Matrix4x4 proj, out Vector3 worldPos)
    {
        worldPos = default;
        var heightAt = TerrainHeightProvider;
        if (heightAt == null) return false;

        float ndcX = (2f * px / w) - 1f;
        float ndcY = (2f * py / h) - 1f;
        Matrix4x4.Invert(view * proj, out var invVP);
        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVP);
        var farH = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invVP);
        nearH /= nearH.W;
        farH /= farH.W;
        var rayOrigin = new Vector3(nearH.X, nearH.Y, nearH.Z);
        var rayDir = Vector3.Normalize(new Vector3(farH.X, farH.Y, farH.Z) - rayOrigin);

        const float step = 1f;
        const float maxDist = 512f;
        float prevDiff = float.NaN;
        Vector3 prevPos = rayOrigin;
        for (float t = 0f; t <= maxDist; t += step)
        {
            var p = rayOrigin + rayDir * t;
            float? terrainH = heightAt((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
            if (terrainH == null)
            {
                if (!float.IsNaN(prevDiff)) break; // left the region after a valid sample
                continue;
            }
            float diff = p.Z - terrainH.Value;
            if (!float.IsNaN(prevDiff) && prevDiff > 0f && diff <= 0f)
            {
                float denom = prevDiff - diff;
                float frac = denom > 1e-6f ? prevDiff / denom : 0f;
                worldPos = Vector3.Lerp(prevPos, p, frac);
                return true;
            }
            prevDiff = diff;
            prevPos = p;
        }
        return false;
    }

    /// <summary>
    /// Pure CPU Möller-Trumbore
    /// ray-triangle intersection + barycentric normal/UV interpolation + Lengyel's-method
    /// binormal, with two adaptations: takes <paramref name="view"/>/<paramref name="proj"/>
    /// as parameters (this class computes them as locals each frame rather than caching them
    /// in fields the way GL does), and takes <paramref name="px"/>/<paramref name="py"/> already
    /// flipped by the caller (<see cref="RunPickPass"/>, see its own comment on why the flip must
    /// happen once, shared with the pixel readback).
    /// </summary>
    private FaceHitInfo ComputeHitInfo(int pickIdx, int px, int py, int w, int h, Matrix4x4 view, Matrix4x4 proj)
    {
        if (pickIdx < 0 || pickIdx >= _cpuFaceData.Count)
            return FaceHitInfo.Unknown;

        var (pickerVerts, normalUvVerts, indices, model) = _cpuFaceData[pickIdx];
        const int PickerStride = 3;
        const int NormalUvStride = 5;

        float ndcX = (2f * px / w) - 1f;
        float ndcY = (2f * py / h) - 1f;

        Matrix4x4.Invert(view * proj, out var invVP);
        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVP);
        var farH = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invVP);
        nearH /= nearH.W;
        farH /= farH.W;
        var rayOrigin = new Vector3(nearH.X, nearH.Y, nearH.Z);
        var rayDir = Vector3.Normalize(new Vector3(farH.X, farH.Y, farH.Z) - rayOrigin);

        float bestT = float.MaxValue;
        float bestU = 0f, bestV = 0f;
        int bestTri = -1;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i + 0] * PickerStride;
            int i1 = indices[i + 1] * PickerStride;
            int i2 = indices[i + 2] * PickerStride;

            var t0 = Vector4.Transform(new Vector4(pickerVerts[i0], pickerVerts[i0 + 1], pickerVerts[i0 + 2], 1f), model); var p0w = new Vector3(t0.X, t0.Y, t0.Z);
            var t1 = Vector4.Transform(new Vector4(pickerVerts[i1], pickerVerts[i1 + 1], pickerVerts[i1 + 2], 1f), model); var p1w = new Vector3(t1.X, t1.Y, t1.Z);
            var t2 = Vector4.Transform(new Vector4(pickerVerts[i2], pickerVerts[i2 + 1], pickerVerts[i2 + 2], 1f), model); var p2w = new Vector3(t2.X, t2.Y, t2.Z);

            var edge1 = p1w - p0w;
            var edge2 = p2w - p0w;
            var h2 = Vector3.Cross(rayDir, edge2);
            float det = Vector3.Dot(edge1, h2);
            if (MathF.Abs(det) < 1e-7f) continue;
            float invDet = 1f / det;
            var s = rayOrigin - p0w;
            float u = Vector3.Dot(s, h2) * invDet;
            if (u < 0f || u > 1f) continue;
            var q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(rayDir, q) * invDet;
            if (v < 0f || u + v > 1f) continue;
            float t = Vector3.Dot(edge2, q) * invDet;
            if (t < 0f || t >= bestT) continue;
            bestT = t;
            bestU = u;
            bestV = v;
            bestTri = i;
        }

        if (bestTri < 0)
            return FaceHitInfo.Unknown; // no intersection (shouldn't happen after the GPU pick)

        int p0 = indices[bestTri + 0] * PickerStride;
        int p1 = indices[bestTri + 1] * PickerStride;
        int p2 = indices[bestTri + 2] * PickerStride;
        float w0 = 1f - bestU - bestV, w1 = bestU, w2 = bestV;

        var lpos0 = new Vector3(pickerVerts[p0], pickerVerts[p0 + 1], pickerVerts[p0 + 2]);
        var lpos1 = new Vector3(pickerVerts[p1], pickerVerts[p1 + 1], pickerVerts[p1 + 2]);
        var lpos2 = new Vector3(pickerVerts[p2], pickerVerts[p2 + 1], pickerVerts[p2 + 2]);

        // World-space hit point straight from the ray parameter -- the intersection loop
        // above already ran in world space, matches SL's ObjectGrab Position contract
        // (world/region space, not object-local).
        var worldPos = rayOrigin + rayDir * bestT;

        int n0i = indices[bestTri + 0] * NormalUvStride;
        int n1i = indices[bestTri + 1] * NormalUvStride;
        int n2i = indices[bestTri + 2] * NormalUvStride;

        var n0 = Vector3.Normalize(new Vector3(normalUvVerts[n0i], normalUvVerts[n0i + 1], normalUvVerts[n0i + 2]));
        var n1 = Vector3.Normalize(new Vector3(normalUvVerts[n1i], normalUvVerts[n1i + 1], normalUvVerts[n1i + 2]));
        var n2 = Vector3.Normalize(new Vector3(normalUvVerts[n2i], normalUvVerts[n2i + 1], normalUvVerts[n2i + 2]));
        var localNormal = Vector3.Normalize(n0 * w0 + n1 * w1 + n2 * w2);

        // Inverse-transpose of the model matrix (row-vector convention: n_world = n_local *
        // (M^-1)^T), correct under non-uniform scale. Falls back to the plain matrix for a
        // singular model.
        Vector3 normal;
        if (Matrix4x4.Invert(model, out var invModel))
            normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, Matrix4x4.Transpose(invModel)));
        else
            normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, model));

        float uvX = normalUvVerts[n0i + 3] * w0 + normalUvVerts[n1i + 3] * w1 + normalUvVerts[n2i + 3] * w2;
        float uvY = normalUvVerts[n0i + 4] * w0 + normalUvVerts[n1i + 4] * w1 + normalUvVerts[n2i + 4] * w2;

        // Binormal from world-space triangle edge/UV delta pair (Lengyel's method), so the
        // resulting frame matches the world-space normal above.
        var w0h = Vector4.Transform(new Vector4(lpos0, 1f), model);
        var w1h = Vector4.Transform(new Vector4(lpos1, 1f), model);
        var w2h = Vector4.Transform(new Vector4(lpos2, 1f), model);
        var edge1w = new Vector3(w1h.X, w1h.Y, w1h.Z) - new Vector3(w0h.X, w0h.Y, w0h.Z);
        var edge2w = new Vector3(w2h.X, w2h.Y, w2h.Z) - new Vector3(w0h.X, w0h.Y, w0h.Z);
        float duv1x = normalUvVerts[n1i + 3] - normalUvVerts[n0i + 3];
        float duv1y = normalUvVerts[n1i + 4] - normalUvVerts[n0i + 4];
        float duv2x = normalUvVerts[n2i + 3] - normalUvVerts[n0i + 3];
        float duv2y = normalUvVerts[n2i + 4] - normalUvVerts[n0i + 4];
        float denom = duv1x * duv2y - duv2x * duv1y;
        Vector3 binormal;
        if (MathF.Abs(denom) > 1e-7f)
        {
            float r = 1f / denom;
            var tangent = Vector3.Normalize((edge1w * duv2y - edge2w * duv1y) * r);
            binormal = Vector3.Normalize(Vector3.Cross(normal, tangent));
        }
        else
        {
            binormal = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX));
        }

        var uvVec = new Vector3(uvX, uvY, 0f);
        return new FaceHitInfo
        {
            UvCoord = uvVec,
            StCoord = uvVec,
            Position = worldPos,
            Normal = normal,
            Binormal = binormal,
        };
    }

    /// <summary>Pure math,
    /// no GL dependency -- slices the interleaved Position(3)+Normal(3)+TexCoord(2)+Tangent(4)
    /// buffer <see cref="VkMesh"/> already consumes into a position-only buffer for
    /// <see cref="ComputeHitInfo"/>'s ray-triangle intersection.</summary>
    private static float[] PickerFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 3)
            return Array.Empty<float>();
        int len = length > 0 ? Math.Min(length, interleaved.Length) : interleaved.Length;
        len -= len % 12; // align down to a whole-vertex boundary
        int vCount = len / 12;
        var pos = new float[vCount * 3];
        for (int i = 0, j = 0; i < len; i += 12, j += 3)
        {
            pos[j] = interleaved[i];
            pos[j + 1] = interleaved[i + 1];
            pos[j + 2] = interleaved[i + 2];
        }
        return pos;
    }

    /// <summary>Slices
    /// the same interleaved buffer into a compact normal+UV buffer for
    /// <see cref="ComputeHitInfo"/>'s barycentric interpolation.</summary>
    private static float[] NormalUvFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 12)
            return Array.Empty<float>();
        int len = length > 0 ? Math.Min(length, interleaved.Length) : interleaved.Length;
        len -= len % 12;
        int vCount = len / 12;
        var nuv = new float[vCount * 5];
        for (int i = 0, j = 0; i < len; i += 12, j += 5)
        {
            nuv[j] = interleaved[i + 3];
            nuv[j + 1] = interleaved[i + 4];
            nuv[j + 2] = interleaved[i + 5];
            nuv[j + 3] = interleaved[i + 6];
            nuv[j + 4] = interleaved[i + 7];
        }
        return nuv;
    }

    /// <summary>
    /// Creates/resizes the pick pass's offscreen render target (color + depth) and its 4-byte
    /// readback buffer. Mirrors <see cref="EnsureDepthTarget"/>'s resize-on-demand pattern
    /// exactly -- same role, a second per-panel offscreen target, just for a different pass.
    /// </summary>
    private unsafe void EnsurePickTarget(VkContext vk, PixelSize size)
    {
        if (_pickSize == size && _pickColorView.Handle != 0) return;
        DestroyPickTarget(vk);

        var colorInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            // TransferSrcBit on top of the usual ColorAttachmentBit -- this image is the
            // source of the post-render-pass readback copy (see RunPickPass).
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in colorInfo, null, out _pickColorImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _pickColorImage, out var colorReq);
        var colorAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = colorReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, colorReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in colorAlloc, null, out _pickColorMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _pickColorImage, _pickColorMemory, 0).ThrowOnError();

        var colorViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _pickColorImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in colorViewInfo, null, out _pickColorView).ThrowOnError();

        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _pickDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _pickDepthImage, out var depthReq);
        var depthAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = depthReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, depthReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in depthAlloc, null, out _pickDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _pickDepthImage, _pickDepthMemory, 0).ThrowOnError();

        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _pickDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in depthViewInfo, null, out _pickDepthView).ThrowOnError();

        VkBufferHelper.AllocateEmpty(vk, BufferUsageFlags.TransferDstBit, 4, out _pickReadbackBuffer, out _pickReadbackMemory);

        _pickSize = size;
    }

    private unsafe void DestroyPickTarget(VkContext vk)
    {
        if (_pickColorView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _pickColorView, null);
        if (_pickColorImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _pickColorImage, null);
        if (_pickColorMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _pickColorMemory, null);
        if (_pickDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _pickDepthView, null);
        if (_pickDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _pickDepthImage, null);
        if (_pickDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _pickDepthMemory, null);
        if (_pickReadbackBuffer.Handle != 0) vk.Api.DestroyBuffer(vk.Device, _pickReadbackBuffer, null);
        if (_pickReadbackMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _pickReadbackMemory, null);
        _pickColorView = default; _pickColorImage = default; _pickColorMemory = default;
        _pickDepthView = default; _pickDepthImage = default; _pickDepthMemory = default;
        _pickReadbackBuffer = default; _pickReadbackMemory = default;
        _pickSize = default;
    }

    /// <summary>
    /// Writes <paramref name="faces"/>' instance data into <paramref name="dest"/> starting at
    /// slot <paramref name="baseIndex"/> (i.e. float offset <c>baseIndex * InstanceFloats</c>).
    /// Pure data-fill, no GPU calls -- lets the caller combine multiple face lists (opaque +
    /// alpha) into ONE buffer region each, uploaded together via a single
    /// <see cref="VkInstanceDrawer.UploadInstanceBatch"/> call. See that call site's comment
    /// for why splitting this across two separate upload calls (one per pass) is wrong.
    /// </summary>
    private static void WriteInstanceData(float[] dest, int baseIndex,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces,
        Matrix4x4 view, Matrix4x4 proj)
    {
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i].Face;
            var faceMv = face.Transform * view;
            var faceMvp = faceMv * proj;
            int off = (baseIndex + i) * VkInstanceDrawer.InstanceFloats;
            CopyMatrix(faceMvp, dest, off);
            CopyMatrix(faceMv, dest, off + 16);
            dest[off + 32] = face.Color.X;
            dest[off + 33] = face.Color.Y;
            dest[off + 34] = face.Color.Z;
            dest[off + 35] = face.Color.W;
            dest[off + 36] = face.Fullbright ? 1f : 0f;
            dest[off + 37] = face.Glow;
            dest[off + 38] = face.Shiny;
            dest[off + 39] = face.AlphaCutoff;
            dest[off + 40] = (float)face.AlphaMode;
        }
    }

    /// <summary>
    /// Draws every face in <paramref name="faces"/>, each reading its own slot in the shared
    /// instance buffer starting at <paramref name="baseIndex"/> (matching where
    /// <see cref="WriteInstanceData"/> wrote it). Caller has already uploaded the combined
    /// instance buffer and bound the correct pipeline (Opaque/Alpha) and sets 0/1.
    /// </summary>
    private unsafe void DrawFaces(VkContext vk, CommandBuffer cmd,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces, int baseIndex)
    {
        for (int i = 0; i < faces.Count; i++)
        {
            // Set 2 (per-material) bound per-face now that every face has its own real
            // material, right before that face's draw call -- sets 0/1 were already bound
            // once for the frame and stay valid (see the caller's comment on why binding set
            // 2 alone doesn't disturb them).
            var materialSet = faces[i].Material.Set;
            vk.Api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _prim!.Layout, 2, 1, &materialSet, 0, null);
            _instanceDrawer!.DrawBatchedInstance(cmd, faces[i].Mesh, baseIndex + i);
            _stats.RecordDraw(faces[i].Mesh.IndexCount); // mirrors GL's own DrawFaces call site
        }
    }

    private static void CopyMatrix(Matrix4x4 m, float[] dest, int offset)
    {
        dest[offset + 0] = m.M11; dest[offset + 1] = m.M12; dest[offset + 2] = m.M13; dest[offset + 3] = m.M14;
        dest[offset + 4] = m.M21; dest[offset + 5] = m.M22; dest[offset + 6] = m.M23; dest[offset + 7] = m.M24;
        dest[offset + 8] = m.M31; dest[offset + 9] = m.M32; dest[offset + 10] = m.M33; dest[offset + 11] = m.M34;
        dest[offset + 12] = m.M41; dest[offset + 13] = m.M42; dest[offset + 14] = m.M43; dest[offset + 15] = m.M44;
    }

    private unsafe void EnsureDepthTarget(VkContext vk, PixelSize size)
    {
        if (_depthSize == size && _depthView.Handle != 0) return;
        DestroyDepthTarget(vk);

        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _depthImage).ThrowOnError();

        vk.Api.GetImageMemoryRequirements(vk.Device, _depthImage, out var memReq);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in allocInfo, null, out _depthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _depthImage, _depthMemory, 0).ThrowOnError();

        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in depthViewInfo, null, out _depthView).ThrowOnError();

        _depthSize = size;
    }

    private unsafe void DestroyDepthTarget(VkContext vk)
    {
        if (_depthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _depthView, null);
        if (_depthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _depthImage, null);
        if (_depthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _depthMemory, null);
        _depthView = default;
        _depthImage = default;
        _depthMemory = default;
    }

    private void FreePanelResources()
    {
        if (!VkApi.IsInitialized) return;
        var vk = VkApi.Context;
        // MUST run before anything below -- every mesh/material/target this method
        // disposes could still be referenced by an in-flight command buffer under real overlap
        // (RenderFrame's own tail no longer waits on its own submissions; only this panel's own
        // per-slot reaping and the RenderFrame-top wait do). Draining every slot here, before the
        // first Dispose() call, closes that window regardless of which slot(s) still have pending
        // work -- unlike the RenderFrame-top wait (which only covers the immediately preceding
        // frame), teardown only gets one chance to do this, so it must cover ALL slots.
        _reapRing.FreeAll();
        DestroyDepthTarget(vk);
        DestroyPickTarget(vk);
        foreach (var (mesh, material, _) in _opaqueFaces) { mesh.Dispose(); material.Dispose(); }
        foreach (var (mesh, material, _) in _alphaFaces) { mesh.Dispose(); material.Dispose(); }
        _opaqueFaces.Clear();
        _alphaFaces.Clear();
        foreach (var tex in _textures) tex.Dispose();
        _textures.Clear();
        _faceTextureSlots.Clear();
        _faceMeshesByPosition.Clear();
        _facesByPosition.Clear();
        // drain the
        // OLD queues (disposing bitmaps that will now never render) and install a FRESH
        // SemaphoreSlim BEFORE the old one is at risk of disposal, so any PatchSceneObjectTexture
        // caller still mid-Wait() on the old instance sees ObjectDisposedException (handled,
        // treated as cancellation) rather than this teardown racing a live producer thread.
        // Without this ordering, a producer that read the field just before this method ran
        // could Wait() on an instance nothing will ever Release() again.
        var oldPatchGate = _texturePatchGate;
        _texturePatchGate = new SemaphoreSlim(TexturePatchQueueDepth, TexturePatchQueueDepth);
        while (_pendingScenePatches.TryDequeue(out var sp)) sp.Bitmap?.Dispose();
        while (_highPriorityScenePatches.TryDequeue(out var hsp)) hsp.Bitmap?.Dispose();
        foreach (var (patch, _) in _deferredScenePatches) patch.Bitmap?.Dispose();
        _deferredScenePatches.Clear();
        foreach (var (patch, _) in _deferredHighPriorityScenePatches) patch.Bitmap?.Dispose();
        _deferredHighPriorityScenePatches.Clear();
        oldPatchGate.Dispose();
        _sceneFaceTextureSlots.Clear();
        _scenePrimLocalIdToSceneKey.Clear();
        foreach (var skinGpuList in _sceneSkinGpuDataMap.Values)
            foreach (var gpu in skinGpuList) gpu.Dispose();
        _sceneSkinGpuDataMap.Clear();
        foreach (var flexiGpuList in _sceneFlexiGpuDataMap.Values)
            foreach (var gpu in flexiGpuList) gpu.Dispose();
        _sceneFlexiGpuDataMap.Clear();
        // scene-object layer, same teardown shape as the single-submission
        // fields immediately above (mesh+material per face, textures tracked per object,
        // pending queues drained and their bitmaps disposed rather than silently dropped).
        foreach (var faces in _sceneObjects.Values)
            foreach (var (mesh, material, _) in faces) { mesh.Dispose(); material.Dispose(); }
        _sceneObjects.Clear();
        _sharedMeshPool.Clear(); // every ref released above; see FreeSceneObjectResources's own comment
        _sceneOpaque.Clear();
        _sceneAlpha.Clear();
        _mergedAlpha.Clear();
        foreach (var textures in _sceneObjectTextures.Values)
            foreach (var tex in textures) tex.Dispose();
        _sceneObjectTextures.Clear();
        _sceneObjectTransformOverrides.Clear();
        while (_pendingTransformOverrides.TryDequeue(out _)) { }
        foreach (var key in _pendingSceneObjects.Keys)
        {
            if (_pendingSceneObjects.TryRemove(key, out var pendingSceneSub) && pendingSceneSub != null)
                DisposeFaceBitmaps(pendingSceneSub);
        }
        foreach (var gd in _submissionSkinGpu) gd.Dispose();
        _submissionSkinGpu.Clear();
        foreach (var gd in _submissionFlexiGpu) gd.Dispose();
        _submissionFlexiGpu.Clear();
        while (_pendingVertexUpdates.TryDequeue(out _)) { }
        // Patches queued or deferred for a face that will
        // never render now (panel torn down) still own a bitmap that must be disposed, not
        // silently dropped.
        while (_pendingSubmissionPatches.TryDequeue(out var sp)) sp.Bitmap?.Dispose();
        foreach (var (patch, _) in _deferredSubmissionPatches) patch.Bitmap?.Dispose();
        _deferredSubmissionPatches.Clear();
        var displacedSubmission = Interlocked.Exchange(ref _pendingSubmission, null);
        if (displacedSubmission != null) DisposeFaceBitmaps(displacedSubmission);
        _instanceDrawer?.Dispose(); _instanceDrawer = null;
        _frameSets?.Dispose(); _frameSets = null;
        _placeholders?.Dispose(); _placeholders = null;
        _wireframe?.Dispose(); _wireframe = null;
        _outline?.Dispose(); _outline = null;
        _selectedOutlineLocalId = 0;
        _pick?.Dispose(); _pick = null;
        _skySet?.Dispose(); _skySet = null;
        _cloudNoiseTex?.Dispose(); _cloudNoiseTex = null;
        _skyPipeline?.Dispose(); _skyPipeline = null;
        _skyReady = false;
        // SSAO. Targets/framebuffers first (they reference the pipeline objects'
        // render passes), then the pipeline/descriptor-set objects, then the render passes
        // themselves, then the dedicated samplers -- unwinding roughly in dependency order,
        // though Vulkan permits destroying a layout/render-pass while dependent objects still
        // exist as long as no further allocations reference it (not load-bearing order, but
        // kept tidy regardless).
        unsafe
        {
            DestroyGBufferTarget(vk);
            DestroySsaoTargets(vk);
        }
        _ssaoDescSet?.Dispose(); _ssaoDescSet = null;
        _ssaoNoiseTex?.Dispose(); _ssaoNoiseTex = null;
        _ssaoBlurPipeline?.Dispose(); _ssaoBlurPipeline = null;
        _ssaoPipeline?.Dispose(); _ssaoPipeline = null;
        _gnorm?.Dispose(); _gnorm = null;
        if (_ssaoOffscreenRenderPass.Handle != 0) { unsafe { vk.Api.DestroyRenderPass(vk.Device, _ssaoOffscreenRenderPass, null); } }
        _ssaoOffscreenRenderPass = default;
        if (_gbufferRenderPass.Handle != 0) { unsafe { vk.Api.DestroyRenderPass(vk.Device, _gbufferRenderPass, null); } }
        _gbufferRenderPass = default;
        if (_ssaoNearestSampler.Handle != 0) { unsafe { vk.Api.DestroySampler(vk.Device, _ssaoNearestSampler, null); } }
        _ssaoNearestSampler = default;
        if (_ssaoLinearSampler.Handle != 0) { unsafe { vk.Api.DestroySampler(vk.Device, _ssaoLinearSampler, null); } }
        _ssaoLinearSampler = default;
        _ssaoReady = false;
        // shadow target/pipeline/render-pass, same unwind-order convention as
        // SSAO immediately above (targets first, then pipeline, then render pass).
        unsafe { DestroyShadowTarget(vk); }
        _shadowPipeline?.Dispose(); _shadowPipeline = null;
        if (_shadowRenderPass.Handle != 0) { unsafe { vk.Api.DestroyRenderPass(vk.Device, _shadowRenderPass, null); } }
        _shadowRenderPass = default;
        _shadowReady = false;
        // water -- reflection target (which also owns its own render pass, see
        // DestroyWaterReflectionTarget), then descriptor sets/pipeline, then the standalone
        // normal/dudv textures.
        unsafe { DestroyWaterReflectionTarget(vk); }
        _frameSetsRefl?.Dispose(); _frameSetsRefl = null;
        _waterDescSet?.Dispose(); _waterDescSet = null;
        _waterPipeline?.Dispose(); _waterPipeline = null;
        // underwater post-process, disposed before water's own normal/dudv textures just below
        // (this pass's descriptor set references them) -- descriptor-set free doesn't actually
        // dereference the images it points to, so the order isn't load-bearing, but disposing
        // the referencing set first keeps teardown order matching dependency order regardless.
        unsafe { DestroyUnderwaterTarget(vk); }
        _underwaterDescSet?.Dispose(); _underwaterDescSet = null;
        if (_underwaterLinearSampler.Handle != 0) { unsafe { vk.Api.DestroySampler(vk.Device, _underwaterLinearSampler, null); } }
        _underwaterLinearSampler = default;
        _underwaterPipeline?.Dispose(); _underwaterPipeline = null;
        if (_underwaterPass.Handle != 0) { unsafe { vk.Api.DestroyRenderPass(vk.Device, _underwaterPass, null); } }
        _underwaterPass = default;
        _underwaterReady = false;
        _waterNormalTex?.Dispose(); _waterNormalTex = null;
        _waterDudvTex?.Dispose(); _waterDudvTex = null;
        _waterReady = false;
        _waterReflReady = false;
        _skinDeformer?.Dispose(); _skinDeformer = null;
        _skinPipeline?.Dispose(); _skinPipeline = null;
        _flexiDeformer?.Dispose(); _flexiDeformer = null;
        _flexiPipeline?.Dispose(); _flexiPipeline = null;
        _stats.Dispose(vk);
        // Mirrors GL's own GlDeinit disposal of _pendingParticleMap/_particleMap -- unlike
        // SubmitParticles' own mid-session behavior, full teardown DOES dispose every
        // still-queued/still-live bitmap and descriptor set, since nothing will ever drain them
        // after this point.
        foreach (var kv in _pendingParticleMap)
            kv.Value?.Texture?.Dispose();
        _pendingParticleMap.Clear();
        foreach (var kv in _particleMap)
        {
            kv.Value.Tex?.Dispose();
            if (kv.Value.Set.Handle != 0)
            {
                var set = kv.Value.Set;
                unsafe { vk.Api.FreeDescriptorSets(vk.Device, vk.DescriptorPool, 1, &set); }
            }
        }
        _particleMap.Clear();
        _particleBuf?.Dispose(); _particleBuf = null;
        _particlePipeline?.Dispose(); _particlePipeline = null;
        _prim?.Dispose(); _prim = null;
        _swapchain?.DisposeAsync(); _swapchain = null;
        if (_renderPass.Handle != 0) { unsafe { vk.Api.DestroyRenderPass(vk.Device, _renderPass, null); } }
        _renderPass = default;
    }
}
