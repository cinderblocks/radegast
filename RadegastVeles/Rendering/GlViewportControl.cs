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
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.OpenGL;
using System.Numerics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Surface hit information computed during a face-pick ray-cast.
/// Mirrors the fields sent in the SL ObjectGrab / ObjectDeGrab SurfaceInfo block
/// (see LLPickInfo::getSurfaceInfo and send_ObjectGrab_message in the SL viewer).
/// </summary>
public readonly struct FaceHitInfo
{
    /// <summary>
    /// Texture-space UV at the hit point [0,1]×[0,1], with TE repeat/offset/rotate
    /// pre-baked (corresponds to SL's mUVCoords / ObjectGrab UVCoord).
    /// (-1,-1,0) when undetermined.
    /// </summary>
    public Vector3 UvCoord   { get; init; }

    /// <summary>
    /// Raw mesh UV at the hit point, equivalent to SL's mSTCoords / ObjectGrab STCoord.
    /// For Veles this is identical to <see cref="UvCoord"/> because the TE transform is
    /// pre-baked into the vertex buffer.  (-1,-1,0) when undetermined.
    /// </summary>
    public Vector3 StCoord   { get; init; }

    /// <summary>World-space intersection point (SL ObjectGrab Position).</summary>
    public Vector3 Position  { get; init; }

    /// <summary>World-space surface normal at the hit point (normalised).</summary>
    public Vector3 Normal    { get; init; }

    /// <summary>World-space binormal at the hit point (normalised).</summary>
    public Vector3 Binormal  { get; init; }

    /// <summary>Returns an instance with all fields at their "undetermined" defaults.</summary>
    public static FaceHitInfo Unknown => new()
    {
        UvCoord  = new Vector3(-1f, -1f, 0f),
        StCoord  = new Vector3(-1f, -1f, 0f),
        Position = Vector3.Zero,
        Normal   = Vector3.Zero,
        Binormal = Vector3.Zero,
    };
}

/// <summary>
/// Avalonia OpenGL control that renders a <see cref="PrimRenderSubmission"/>.
/// <para>
/// Camera controls (SL conventions — leaves plain left/right click for object interaction):
/// <list type="bullet">
///   <item>Alt+left-drag → orbit</item>
///   <item>Alt+Ctrl+drag → pan target</item>
///   <item>Alt+Ctrl+Shift+drag → dolly (zoom)</item>
///   <item>Scroll wheel → zoom</item>
///   <item>Plain left-click (no drag, no modifier) → pick / interact with object</item>
/// </list>
/// </para>
/// Designed to be the low-level rendering leaf; higher-level viewers (prim, avatar, scene)
/// all submit <see cref="PrimRenderSubmission"/> objects and wire up their own UI.
/// </summary>
public class GlViewportControl : Panel
{
    // ── Inner GL render core ─────────────────────────────────────────────────────
    // OpenGlControlBase does not receive pointer events in all Avalonia/ANGLE
    // configurations (ANGLE uses a native EGL surface that intercepts raw Win32
    // mouse messages before Avalonia's input system sees them).  Wrapping it in a
    // Grid gives us a normal Avalonia hit-test target whose virtual overrides fire
    // reliably, while the RenderCore handles the GL lifecycle unchanged.
    private sealed class RenderCore(GlViewportControl owner) : OpenGlControlBase
    {
        protected override void OnOpenGlInit(GlInterface gl)           => owner.GlInit(gl);
        protected override void OnOpenGlRender(GlInterface gl, int fb) => owner.GlRender(gl, fb);
        protected override void OnOpenGlDeinit(GlInterface gl)         => owner.GlDeinit(gl);
    }

    private readonly RenderCore _core;

    // ── Styled properties ────────────────────────────────────────────────────────

    public static readonly StyledProperty<bool> WireframeProperty =
        AvaloniaProperty.Register<GlViewportControl, bool>(nameof(Wireframe));

    /// <summary>When true an additional wireframe pass is drawn over solid geometry.</summary>
    public bool Wireframe
    {
        get => GetValue(WireframeProperty);
        set => SetValue(WireframeProperty, value);
    }

    public static readonly StyledProperty<bool> NavMeshOverlayEnabledProperty =
        AvaloniaProperty.Register<GlViewportControl, bool>(nameof(NavMeshOverlayEnabled));

    /// <summary>When true, scene objects are tinted by their navmesh walkability type.</summary>
    public bool NavMeshOverlayEnabled
    {
        get => GetValue(NavMeshOverlayEnabledProperty);
        set => SetValue(NavMeshOverlayEnabledProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WireframeProperty || change.Property == NavMeshOverlayEnabledProperty)
            _core.RequestNextFrameRendering();
        // When the control becomes visible again (tab switched back in), the
        // OpenGlControlBase won't repaint unless we explicitly request a frame.
        if (change.Property == IsVisibleProperty && IsVisible)
            _core.RequestNextFrameRendering();
    }

    // ── Camera ───────────────────────────────────────────────────────────────────

    private readonly Camera3D _camera = new();

    // ── GPU resources (GL thread only) ───────────────────────────────────────────

    private GlShader? _primShader;
    private GlShader? _wireShader;
    private GlShader? _pickShader;
    private GlShader? _particleShader;
    private GlShader? _gnormShader;    // G-buffer normal pass
    private GlShader? _navMeshOverlayShader;

    // Navmesh walkability types, updated via UpdateNavMeshTypes from any thread.
    // Replaced wholesale (volatile reference swap) rather than mutated in place so a
    // frame rendered mid-update never sees a partially cleared table.
    private volatile ConcurrentDictionary<uint, NavMeshWalkabilityType> _navMeshTypes = new();
    private GlShader? _ssaoShader;     // SSAO pass
    private GlShader? _ssaoBlurShader; // SSAO blur pass
    private string?   _initError;
    // GL ES (ANGLE) does not support PolygonMode; detected at init time.
    private bool      _supportsPolygonMode;

    // ── SSAO resources (GL thread only) ──────────────────────────────────────────
    // GBuffer FBO: depth24 attachment used as texture input for SSAO.
    private uint _gbufFbo, _gbufNormalTex, _gbufDepthTex;
    private int _gbufW, _gbufH;
    // SSAO FBO: single R8 colour attachment.
    private uint _ssaoFbo, _ssaoColorTex;
    // Blur FBO: single R8 colour attachment.
    private uint _ssaoBlurFbo, _ssaoBlurTex;
    private int _ssaoFboW, _ssaoFboH;
    // Empty VAO for full-screen triangle draw (no vertex data needed).
    private uint _quadVao;
    // Precomputed hemisphere kernel and 4×4 noise texture.
    private Vector3[]? _ssaoKernel;
    private uint _ssaoNoiseTex;
    // Whether SSAO compiled successfully.
    private bool _ssaoReady;
    /// <summary>Enable or disable SSAO. Change takes effect on the next frame.</summary>
    public bool SsaoEnabled { get; set; } = true;

    /// <summary>
    /// Above this opaque face count SSAO is skipped for the frame even when
    /// <see cref="SsaoEnabled"/> is true. The G-buffer pre-pass re-renders every opaque
    /// face a second time, so in dense scenes (the SceneViewer) the cost outweighs the
    /// subtle ambient-occlusion benefit — which is also least noticeable when the screen
    /// is busy. Light scenes (avatar / prim viewers, sparse regions) stay under the cap
    /// and keep SSAO. Set to <see cref="int.MaxValue"/> to never auto-skip.
    /// </summary>
    public int SsaoMaxOpaqueFaces { get; set; } = 1500;

    // ── Water resources (GL thread only) ─────────────────────────────────────────
    private GlShader? _waterShader;
    private uint _waterReflFbo, _waterReflColorTex, _waterReflDepthRb;
    private uint _waterNormalmapTex, _waterDudvmapTex;
    private bool _waterReady;
    private float _waterTime;
    private long    _waterLastTick;
    // Timestamp of the last reflection FBO render. 0 = never. Used to throttle the
    // reflection pre-pass so it fires at most once every kReflIntervalMs milliseconds.
    private long    _reflLastTick;
    // Last reflected-camera view-projection matrix (saved in DrawWaterReflection,
    // consumed in DrawWater for correct planar-reflection UV lookup).
    private Matrix4x4 _lastReflViewProj;
    private const int WaterReflSize     = 512;
    private const int kReflIntervalMs   = 150; // ~7 fps for reflection updates

    /// <summary>
    /// World-space Z height of the water surface.
    /// <see cref="float.NaN"/> disables water rendering (default).
    /// Set this from the simulator's <c>WaterHeight</c> field.
    /// </summary>
    public float WaterHeight { get; set; } = float.NaN;

    /// <summary>
    /// Windlight water fog colour used as the deep-water tint.
    /// Defaults to SL's default water colour; update from EEP water track.
    /// </summary>
    public Vector4 WaterFogColor { get; set; } = new Vector4(0.09f, 0.28f, 0.63f, 0.84f);

    // ── Sky resources (GL thread only) ───────────────────────────────────────────
    private GlShader? _skyShader;
    private bool _skyReady;

    /// <summary>
    /// Windlight / EEP sky and atmosphere parameters.
    /// Update from <see cref="SceneViewerViewModel"/> when EEP environment data arrives.
    /// </summary>
    public SkySettings Sky { get; set; } = new SkySettings();

    /// <summary>
    /// When set, <see cref="GlRender"/> queries this service once per frame to
    /// obtain the interpolated sky and water parameters for the current day-cycle time.
    /// </summary>
    public SceneEnvironmentService? EnvironmentService { get; set; }

    /// <summary>
    /// When false, the sky dome is not drawn and <see cref="EnvironmentService"/>
    /// is not sampled for sky/water. <see cref="Sky"/> retains whatever value was
    /// assigned directly (e.g. <see cref="SkySettings.Studio"/> for isolated viewers).
    /// Defaults to true.
    /// </summary>
    public bool ShowSky { get; set; } = true;

    /// <summary>
    /// GL clear color used when <see cref="ShowSky"/> is false.
    /// </summary>
    public Vector3 BackgroundColor { get; set; } = new Vector3(0.40f, 0.50f, 0.85f);

    // ── Particle state ────────────────────────────────────────────────────────────
    // Pending submissions from any thread: key → submission (null = remove).
    private readonly ConcurrentDictionary<ulong, ParticleRenderSubmission?> _pendingParticleMap = new();
    // Current live submissions, consumed & drawn on the GL thread.
    private readonly Dictionary<ulong, (ParticleRenderSubmission Sub, GlTexture? Tex)> _particleMap = new();
    private GlParticleBuffer? _particleBuf;

    // Dedicated pick FBO — owned exclusively by the GL thread.
    private uint _pickFbo, _pickRbo, _pickDepth;
    private int _pickFboW, _pickFboH;

    // Scene FBO — we own this FBO with RGBA8 colour + DEPTH16 depth renderbuffers.
    // We render the entire scene here, then blit colour to Avalonia's compositing FBO.
    // This guarantees depth-testing is active regardless of what Avalonia's FBO contains.
    private uint _sceneFbo, _sceneColor, _sceneDepth;
    private int _sceneFboW, _sceneFboH;

    private readonly List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> _opaque = new();
    private readonly List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> _alpha  = new();
    // Reused each frame to avoid per-frame allocation during the alpha-sort merge.
    private readonly List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> _mergedAlpha = new();
    // Maps 1-based pick index → (LocalId, FaceIndex). Built whenever render lists are refreshed.
    private readonly List<(uint LocalId, int FaceIndex)> _pickMap = new();
    // CPU-side vertex data for ray-triangle intersection and hit-attribute fetch.
    // PickerVerts: positions only, stride 3 (X,Y,Z) — used for the fast Möller–Trumbore loop.
    // NormalUvVerts: normals+UVs, stride 5 (nx,ny,nz,u,v) — used for normals/UV after the hit.
    private readonly List<(float[] PickerVerts, float[] NormalUvVerts, ushort[] Indices, Matrix4x4 Model)> _cpuFaceData = new();

    // ── Scene-object layer (additive over the base terrain submission) ────────────
    // keyed by scene key (ulong: upper 32 bits = sim index, lower 32 bits = localId)
    private readonly Dictionary<ulong, List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)>> _sceneObjects = new();
    // Reference counts for every GlTexture held by scene-object face slots, one count per
    // slot occurrence. Replaces the previous "scan every other object's faces on removal"
    // approach (O(total scene faces) per removed object) with O(faces of removed object),
    // and makes it safe to dispose a texture shared across faces exactly when the last
    // slot referencing it goes away. GL thread only.
    private readonly Dictionary<GlTexture, int> _sceneTexRefs = new();
    // FlexiGpuData for scene objects, parallel to _sceneObjects (only populated when compute is available).
    private readonly Dictionary<ulong, FlexiGpuData[]> _flexiGpuDataMap = new();
    // Pending scene-object updates keyed by sceneKey.  null value means "remove from GPU".
    // ConcurrentDictionary ensures at most one pending entry per key: when SceneObjectStreamer
    // rebuilds the same object multiple times (e.g. from SelectObject-triggered ObjectUpdate
    // floods from the Objects panel), each new submission atomically replaces the previous one
    // rather than accumulating duplicate float[] vertex arrays in the LOH.
    private readonly ConcurrentDictionary<ulong, PrimRenderSubmission?> _pendingSceneObjects = new();
    // Flat draw lists rebuilt from _sceneObjects each time objects are added/removed.
    private readonly List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> _sceneOpaque = new();
    private readonly List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> _sceneAlpha  = new();

    // Ordered list of every GlMesh uploaded in the current submission, indexed by face order.
    // Used to apply per-face vertex updates submitted from the animation thread.
    private readonly List<GlMesh> _faceMeshes = new();
    private readonly ConcurrentQueue<(int FaceIndex, float[] Verts)> _pendingVertexUpdates = new();

    // Instanced rendering support.
    private GlInstanceDrawer?  _instanceDrawer;
    private GlFlexiDeformer?   _flexiDeformer;
    private GlSkinDeformer?    _skinDeformer;
    // FlexiGpuData objects for the current base submission (PrimViewer/AvatarViewer).
    private readonly List<FlexiGpuData>       _submissionFlexiGpu = new();
    // AvatarSkinGpuData for the current base submission (AvatarViewer single-model path).
    private readonly List<AvatarSkinGpuData>  _submissionSkinGpu  = new();
    // AvatarSkinGpuData per scene object (scene viewer avatar path).
    private readonly Dictionary<ulong, AvatarSkinGpuData[]> _skinGpuDataMap = new();
    // Reused per-frame buffer for per-instance float data; grown on demand, never shrunk.
    private float[] _instanceDataBuf = Array.Empty<float>();

    // Per-scene-object vertex updates: (rootId, face-within-root offset, verts, vert count, pool-rented flag).
    // When IsPoolRented is true the Verts buffer was rented from ArrayPool<float>.Shared by the producer
    // (SceneAvatarAnimator) and must be returned after the GL upload.  Flexi-prim buffers are allocated
    // with new[] (exact size required by glBufferSubData) and must NOT be returned to the pool.
    private readonly ConcurrentQueue<(uint RootId, int FaceOffset, float[] Verts, int VertsLength, bool IsPoolRented)> _pendingSceneVertexUpdates = new();

    // Parked model-matrix overrides for roots whose upload has not landed yet.
    // Dequeued overrides are applied directly to the faces of live objects; only overrides
    // that arrive while the object is still building wait here, and UploadSceneObjectNoRebuild
    // consumes them the moment the object's faces exist. (GL thread only.)
    private readonly ConcurrentDictionary<ulong, Matrix4x4> _sceneObjectTransformOverrides = new();
    // Pending transform override updates enqueued from non-GL threads.
    private readonly ConcurrentQueue<(ulong RootId, Matrix4x4 Transform)> _pendingTransformOverrides = new();

    // Kinematic state for scene objects currently in motion (nonzero velocity/angular velocity),
    // keyed by scene key. Populated by SetSceneObjectMotion and consumed every frame by
    // ExtrapolateMovingSceneObjects to dead-reckon a smooth transform between the sim's terse
    // update packets. Entries are removed once an object comes to rest or is torn down, so cost
    // is proportional to the number of objects actually moving, not total scene size.
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

    // Pending single-texture patches for already-live scene objects (progressive texture streaming).
    private readonly ConcurrentQueue<SceneTexturePatch> _pendingTexturePatches = new();
    // Back-pressure gate: limits _pendingTexturePatches to 200 entries without spin-waiting.
    // Each WaitAsync/Wait in PatchSceneObjectTexture consumes a permit; each dequeue in
    // ApplyTexturePatches releases one.  Initial count matches the queue-depth limit.
    // NOT readonly: GlDeinit disposes the gate to unblock waiting producers, then installs
    // a fresh instance so texture streaming keeps working after Avalonia recreates the GL
    // context (tab switch). Producers capture the field into a local before Wait/Release.
    private SemaphoreSlim _texturePatchGate = new SemaphoreSlim(200, 200);
    private const int TexturePatchQueueDepth = 200;

    // Releases permits on the given gate instance, tolerating the deinit/reinit races:
    // a producer may have consumed its permit from a previous (now disposed) gate instance,
    // in which case releasing on the current one could exceed maxCount.
    private static void ReleasePatchGate(SemaphoreSlim gate, int count = 1)
    {
        try { gate.Release(count); }
        catch (SemaphoreFullException) { /* permit was consumed on a previous gate instance */ }
        catch (ObjectDisposedException) { /* gate torn down mid-release; nothing to balance */ }
    }
    // Pending texture patches for the single-submission (AvatarViewer / ObjectViewer) path.
    private readonly ConcurrentQueue<SceneTexturePatch> _pendingSubmissionPatches = new();
    // Patches that arrived before their scene object was uploaded; retried each frame for up to ~30 s.
    private readonly List<(SceneTexturePatch patch, int retriesLeft)> _deferredPatches = new();
    // Set when a texture patch upgraded a legacy face to the alpha pass; triggers a single
    // RebuildSceneFlatLists() at the end of ApplyTexturePatches so the face moves draw lists.
    private bool _alphaReclassNeeded;
    // Frame counter for the periodic [SceneLoad] pipeline telemetry log.
    private int _loadTelemetryFrame;
    // Submission patches that arrived before UploadSubmission ran; retried for up to ~30 s.
    private readonly List<(SceneTexturePatch patch, int retriesLeft)> _deferredSubmissionPatches = new();

    // ── Last-frame camera state (GL thread only) — used for pick ray construction ──
    private Matrix4x4 _lastView = Matrix4x4.Identity;
    private Matrix4x4 _lastProj = Matrix4x4.Identity;
    private int     _lastW, _lastH;

    // ── Cross-thread submission queue (lock-free single slot) ────────────────────

    private PrimRenderSubmission? _pendingSubmission;
    // When true, UploadSubmission will call FrameBoundsFront instead of FrameBounds.
    private volatile bool _frameFrontPending;
    // When true, UploadSubmission will call FrameBoundsAvatarFront.
    private volatile bool _frameAvatarFrontPending;
    // When true, GlRender will free all scene-object GPU resources before processing any queued uploads.
    private volatile bool _pendingClearScene;

    // ── Mouse state ──────────────────────────────────────────────────────────────

    private Point _lastPointer;
    private Point _pressPointer;
    private bool  _leftDown;
    private bool  _rightDown;
    // True when an Alt-drag camera gesture is active (no pick on release).
    private bool  _cameraGesture;
    private bool  _dragged;
    private volatile bool _pickRequested;
    private Point _pickPoint;
    // ── Saved bounds for camera reset ────────────────────────────────────────────

    private Vector3 _lastBoundsMin = new Vector3(-0.5f);
    private Vector3 _lastBoundsMax = new Vector3( 0.5f);

    // ── Performance instrumentation ──────────────────────────────────────────────
    private readonly FrameStatsTracker _stats = new();

    /// <summary>
    /// Per-frame statistics published after each rendered frame. CPU/GPU times,
    /// draw-call and triangle counts, and frustum cull metrics. Subscribe to
    /// <see cref="FrameStatsTracker.FrameCompleted"/> on the GL thread; raise events
    /// to the UI thread yourself if you need them there.
    /// </summary>
    public FrameStatsTracker Stats => _stats;

    // When false the frustum cull pass is bypassed (debug aid).
    /// <summary>
    /// Enables CPU-side frustum culling of submitted faces. Defaults to <c>true</c>.
    /// Set to <c>false</c> to verify that culling is not removing visible geometry.
    /// </summary>
    public bool FrustumCullingEnabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c> the reflection pre-pass runs and the water shader samples the
    /// reflection FBO. When <c>false</c> the pre-pass is skipped and the water shader
    /// uses the deep-water colour instead. Defaults to <c>false</c>.
    /// </summary>
    public bool WaterReflectionsEnabled { get; set; } = false;

    /// <summary>
    /// Enables instanced batching (<see cref="GlInstanceDrawer"/>) for opaque faces that
    /// share a deduplicated mesh. The 2026-07-11 "exploded geometry" regression that
    /// forced this off was root-caused to WriteInstanceData double-transposing the
    /// per-instance matrices (the raw System.Numerics bytes already read as the correct
    /// column-vector matrix on the GL side, matching the uniform path); with that fixed,
    /// the instanced path is back on. History: the defect was latent because mesh dedup
    /// never produced shared meshes until VerticesLength survived face translation, so
    /// the path first ran against real workloads only after that fix.
    /// </summary>
    public bool InstancingEnabled { get; set; } = true;

    /// <summary>Number of scene object submissions waiting to be uploaded to the GPU this frame. Zero-cost snapshot.</summary>
    public int PendingUploadCount => _pendingSceneObjects.Count;

    // True when any cross-thread queue holds work that only the GL thread can drain.
    // Read from the UI-thread heartbeat; all members are safe to probe cross-thread.
    private bool HasPendingGpuWork =>
        _pendingSubmission != null
        || _pendingClearScene
        || !_pendingSceneObjects.IsEmpty
        || !_pendingVertexUpdates.IsEmpty
        || !_pendingSceneVertexUpdates.IsEmpty
        || !_pendingTransformOverrides.IsEmpty
        || !_pendingTexturePatches.IsEmpty
        || !_pendingSubmissionPatches.IsEmpty
        || !_pendingParticleMap.IsEmpty
        || !_sceneObjectMotion.IsEmpty;

    // ── Constructor ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Provides direct access to the camera for external controllers (e.g. SceneViewerViewModel).
    /// The camera is not thread-safe; it must only be mutated on the UI thread.
    /// </summary>
    public Camera3D Camera => _camera;

    // ── Render heartbeat ─────────────────────────────────────────────────────────
    // Pumps RequestNextFrameRendering at ~30 fps while the control is in the visual
    // tree so the scene reappears immediately after a tab switch.  Avalonia hides the
    // ancestor ContentPresenter rather than this control itself on tab changes, so
    // IsVisible never flips; a timer is the reliable cross-platform solution.
    private Avalonia.Threading.DispatcherTimer? _heartbeat;

    public GlViewportControl()
    {
        _core = new RenderCore(this) { IsHitTestVisible = false };
        Children.Add(_core);
        Background = Brushes.Transparent;  // makes the Grid hit-testable
        Focusable  = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _lastPointer  = e.GetPosition(this);
        _pressPointer = _lastPointer;
        _dragged       = false;
        _cameraGesture = false;
        var props = e.GetCurrentPoint(this).Properties;
        _leftDown  = props.IsLeftButtonPressed;
        _rightDown = props.IsRightButtonPressed;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // Only fire a pick if it was a plain left-click with no drag and no Alt held
        // (Alt+drag is a camera gesture and must not trigger object interaction).
        bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        if (_leftDown && !_dragged && !alt && !_cameraGesture)
        {
            _pickPoint     = _pressPointer;
            _pickRequested = true;
            _core.RequestNextFrameRendering();
        }
        _leftDown      = false;
        _rightDown     = false;
        _cameraGesture = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var   pos  = e.GetPosition(this);
        float dx   = (float)(pos.X - _lastPointer.X);
        float dy   = (float)(pos.Y - _lastPointer.Y);
        _lastPointer = pos;

        if (!_leftDown && !_rightDown) return;

        // Mark as dragged once the pointer moves more than 4 px from the press origin.
        if (Math.Abs(pos.X - _pressPointer.X) > 4 || Math.Abs(pos.Y - _pressPointer.Y) > 4)
            _dragged = true;

        bool alt   = (e.KeyModifiers & KeyModifiers.Alt)     != 0;
        bool ctrl  = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift)   != 0;

        // ── SL-style camera gesture bindings ───────────────────────────────────
        // Alt+drag              → orbit  (left OR right button)
        // Alt+Ctrl+drag         → pan
        // Alt+Ctrl+Shift+drag   → dolly (zoom)
        // Plain left-click (no modifier, no drag) → object interaction (pick)
        // Middle-button drag or right-drag alone  → no-op here (reserved)
        if (alt)
        {
            _cameraGesture = true;
            if (ctrl && shift)
            {
                // Dolly: vertical drag zooms, horizontal drag does nothing.
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
            _core.RequestNextFrameRendering();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Zoom((float)e.Delta.Y);
        _core.RequestNextFrameRendering();
    }

    // ── Public API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired (on the UI thread) if GL initialisation fails.
    /// The argument is the exception message.
    /// </summary>
    public event Action<string>? InitFailed;

    /// <summary>
    /// Fired on the UI thread after every successful GL context initialization (including
    /// re-initializations caused by Avalonia detaching and reattaching the visual tree on
    /// tab switches). Subscribers should re-dirty their streamer state so GPU data that was
    /// freed during <c>GlDeinit</c> gets re-uploaded on the next render cycle.
    /// </summary>
    public event Action? SceneReset;

    /// <summary>
    /// Fired (on the UI thread) when the user clicks a face in the viewport.
    /// Arguments are (primLocalId, faceIndex, hitInfo) where <see cref="FaceHitInfo"/> carries
    /// the world-space intersection point, UV coordinates, normal, and binormal computed via
    /// CPU ray-triangle intersection (mirrors LLPickInfo::getSurfaceInfo in the SL viewer).
    /// </summary>
    public event Action<uint, int, FaceHitInfo>? FaceClicked;

    /// <summary>
    /// Submit an updated particle snapshot from any thread.
    /// <paramref name="key"/> uniquely identifies the emitter (root prim LocalID or avatar key).
    /// Pass <see langword="null"/> for <paramref name="sub"/> to remove that emitter's particles.
    /// </summary>
    public void SubmitParticles(ulong key, ParticleRenderSubmission? sub)
    {
        _pendingParticleMap[key] = sub;
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>Remove a previously submitted particle emitter by key.</summary>
    public void RemoveParticles(ulong key) => SubmitParticles(key, null);

    /// <summary>
    /// Submit new geometry from any thread.  The data is consumed and uploaded to the
    /// GPU on the next render frame, replacing any previously displayed geometry.
    /// </summary>
    public void Submit(PrimRenderSubmission submission)
    {
        // If a previous submission was never consumed by the GL thread, its SKBitmaps
        // won't reach UploadSubmission — dispose them here to avoid a leak.
        var displaced = Interlocked.Exchange(ref _pendingSubmission, submission);
        if (displaced != null)
            DisposePendingBitmaps(displaced);
        _core.RequestNextFrameRendering();
    }

    /// <summary>
    /// Like <see cref="Submit"/> but also resets the camera to a front-on view
    /// (matching legacy Radegast's HUD viewer) once the geometry is uploaded.
    /// </summary>
    public void SubmitFront(PrimRenderSubmission submission)
    {
        _frameFrontPending = true;
        Submit(submission);
    }

    /// <summary>
    /// Like <see cref="Submit"/> but resets the camera to face the front of an avatar
    /// once the geometry is uploaded.
    /// </summary>
    public void SubmitAvatarFront(PrimRenderSubmission submission)
    {
        _frameAvatarFrontPending = true;
        Submit(submission);
    }

    /// <summary>Reset the camera to frame the currently displayed object.</summary>
    public void ResetCamera()
    {
        _camera.FrameBounds(_lastBoundsMin, _lastBoundsMax);
        _core.RequestNextFrameRendering();
    }

    /// <summary>Reset the camera to face the object head-on (for HUDs and flat panels).</summary>
    public void ResetCameraFront()
    {
        _camera.FrameBoundsFront(_lastBoundsMin, _lastBoundsMax);
        _core.RequestNextFrameRendering();
    }

    /// <summary>
    /// Set the orbit target in world space and optionally adjust distance/pitch,
    /// without reframing from the submission bounds.  Safe to call from any thread.
    /// </summary>
    public void SetCameraTarget(Vector3 target, float distance = -1f, float pitch = -1000f)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _camera.Target = target;
            if (distance > 0f)  _camera.Distance = distance;
            if (pitch > -999f)  _camera.Pitch    = pitch;
            _core.RequestNextFrameRendering();
        });
    }

    /// <summary>
    /// Smoothly reposition the camera target to follow the avatar without
    /// resetting the user's current orbit angle or zoom distance.
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateCameraFollow(Vector3 target)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _camera.Target = target;
            _core.RequestNextFrameRendering();
        });
    }

    /// <summary>Step-orbit by exact degree amounts (for button navigation).</summary>
    public void OrbitStep(float dyaw, float dpitch)
    {
        _camera.OrbitStep(dyaw, dpitch);
        _core.RequestNextFrameRendering();
    }

    /// <summary>Zoom by the given number of scroll steps (positive = zoom in).</summary>
    public void ZoomStep(float delta)
    {
        _camera.Zoom(delta);
        _core.RequestNextFrameRendering();
    }

    /// <summary>
    /// Request a re-render without submitting new geometry.
    /// Used by the scene camera controller to update the view when only the camera has moved.
    /// </summary>
    public void RequestRender() => _core.RequestNextFrameRendering();

    /// <summary>
    /// Queue an additive scene-object submission for the given scene key.
    /// Replaces any previously queued submission for the same key.
    /// Safe to call from any thread.
    /// </summary>
    public void SubmitSceneObject(ulong sceneKey, PrimRenderSubmission submission)
    {
        // Atomically replace any previous pending submission for this key.
        // If an older build was displaced, its embedded bitmaps must be disposed here
        // since they will never reach UploadSceneObjectNoRebuild on the GL thread.
        _pendingSceneObjects.AddOrUpdate(
            sceneKey,
            submission,
            (_, displaced) => { if (displaced != null) DisposePendingBitmaps(displaced); return submission; });
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>
    /// Queue removal of the scene object with the given scene key.
    /// Safe to call from any thread.
    /// </summary>
    public void RemoveSceneObject(ulong sceneKey)
    {
        // null = GPU removal request.  Dispose any displaced pending submission.
        _pendingSceneObjects.AddOrUpdate(
            sceneKey,
            (PrimRenderSubmission?)null,
            (_, displaced) => { if (displaced != null) DisposePendingBitmaps(displaced); return null; });
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>
    /// Replace the navmesh walkability table used by the overlay pass.
    /// Call this after <see cref="NavMeshManager.RefreshAsync"/> completes.
    /// Safe to call from any thread; takes effect on the next rendered frame.
    /// </summary>
    public void UpdateNavMeshTypes(IReadOnlyDictionary<uint, NavMeshWalkabilityType> types)
    {
        var fresh = new ConcurrentDictionary<uint, NavMeshWalkabilityType>();
        foreach (var kvp in types)
            fresh[kvp.Key] = kvp.Value;
        _navMeshTypes = fresh; // atomic swap — renderer sees old or new table, never partial
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>
    /// Remove all scene objects (e.g. on sim change).
    /// Safe to call from any thread.
    /// </summary>
    public void ClearAllSceneObjects()
    {
        _pendingClearScene = true;
        _core.RequestNextFrameRendering();
    }

    // ── OpenGL lifecycle ─────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Ensure a render is requested after the first layout pass sets real Bounds.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            _core.RequestNextFrameRendering,
            Avalonia.Threading.DispatcherPriority.Loaded);
        // Start a ~30 fps heartbeat so the scene repaints after returning from
        // another tab (Avalonia hides the parent ContentPresenter, not this
        // control, so IsVisibleProperty never fires on tab switch).
        // Skip the frame request while the control is hidden by an ancestor
        // (IsEffectivelyVisible accounts for the hidden ContentPresenter) UNLESS
        // cross-thread queues have pending GPU work — draining those keeps the
        // producers (streamers, animators, ArrayPool rentals) flowing exactly as
        // before. The timer keeps ticking so the first tick after the tab returns
        // repaints immediately.
        if (_heartbeat == null)
        {
            _heartbeat = new Avalonia.Threading.DispatcherTimer(
                System.TimeSpan.FromMilliseconds(33),
                Avalonia.Threading.DispatcherPriority.Render,
                (_, _) =>
                {
                    if (IsEffectivelyVisible || HasPendingGpuWork)
                        _core.RequestNextFrameRendering();
                });
            _heartbeat.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _heartbeat?.Stop();
        _heartbeat = null;
    }

    private void GlInit(GlInterface gl)
    {
        try
        {
            // Bind the Silk.NET GL API to Avalonia's managed GL context. All renderer classes
            // resolve GL entry points through GlApi.Gl from here on (OpenTK GL is fully retired).
            GlApi.Initialize(gl.GetProcAddress);
            _primShader = GlShader.Compile(
                ShaderLoader.Load("prim.vert"),
                ShaderLoader.Load("prim.frag"));
            _wireShader = GlShader.Compile(
                ShaderLoader.Load("wireframe.vert"),
                ShaderLoader.Load("wireframe.frag"));
            _pickShader = GlShader.Compile(
                ShaderLoader.Load("wireframe.vert"),
                ShaderLoader.Load("picking.frag"));
            _particleShader = GlShader.Compile(
                ShaderLoader.Load("particle.vert"),
                ShaderLoader.Load("particle.frag"));
            _navMeshOverlayShader = GlShader.Compile(
                ShaderLoader.Load("navmesh_overlay.vert"),
                ShaderLoader.Load("navmesh_overlay.frag"));
            _particleBuf    = new GlParticleBuffer();
            _instanceDrawer = new GlInstanceDrawer();
            // GL ES (ANGLE) doesn't expose PolygonMode; check version string.
            var version = GlApi.Gl.GetStringS(StringName.Version) ?? "";
            _supportsPolygonMode = !version.Contains("OpenGL ES");

            // Per-frame perf instrumentation (GL_TIME_ELAPSED queries + counters).
            // Reset first so a re-init after a tab-switch deinit cycle works correctly.
            _stats.Reset();
            _stats.Initialize();

            // ── SSAO shaders (best-effort; SSAO disabled if unsupported) ────────
            try
            {
                _gnormShader    = GlShader.Compile(ShaderLoader.Load("prim.vert"),      ShaderLoader.Load("gnorm.frag"));
                _ssaoShader     = GlShader.Compile(ShaderLoader.Load("quad.vert"),      ShaderLoader.Load("ssao.frag"));
                _ssaoBlurShader = GlShader.Compile(ShaderLoader.Load("quad.vert"),      ShaderLoader.Load("ssaoblur.frag"));
                _quadVao        = GlApi.Gl.GenVertexArray();
                _ssaoKernel     = BuildSsaoKernel(32);
                _ssaoNoiseTex   = BuildSsaoNoiseTex();
                // Upload the kernel once at init — it never changes at runtime.
                _ssaoShader.Use();
                _ssaoShader.SetVec3Array("uKernel", _ssaoKernel);
                _ssaoShader.Set("uKernelSize", _ssaoKernel.Length);
                _ssaoShader.Unuse();
                _ssaoReady      = true;
            }
            catch
            {
                // SSAO is a nice-to-have; swallow any compilation failure so the
                // viewer still works on minimal GL ES 3.0 hardware.
                _ssaoReady = false;
            }

            // Flexi-prim compute deformer (best-effort; falls back to CPU when unavailable).
            try
            {
                _flexiDeformer = new GlFlexiDeformer(ShaderLoader.Load("flexi.comp"));
            }
            catch
            {
                _flexiDeformer = null;
            }

            // Avatar LBS compute deformer (best-effort; falls back to CPU when unavailable).
            try
            {
                _skinDeformer = new GlSkinDeformer(ShaderLoader.Load("skin.comp"));
            }
            catch
            {
                _skinDeformer = null;
            }

            // Water rendering (best-effort; viewer works fine without it)
            try { InitWater(); }
            catch { _waterReady = false; }

            // Sky rendering (best-effort; falls back to solid clear colour)
            try { InitSky(); }
            catch { _skyReady = false; }

            // Notify listeners (on the UI thread) that a fresh GL context is ready.
            // On a first open this fires immediately; on tab-switch re-attaches it
            // triggers streamers to re-dirty and re-upload their scene data.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SceneReset?.Invoke());
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            // Post to UI thread so subscribers (e.g. PrimViewerPanel) can update error UI.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => InitFailed?.Invoke(_initError));
        }
    }

    private void GlRender(GlInterface gl, int fb)
    {
        _stats.BeginFrame();
        try
        {
            GlRenderCore(gl, fb);
        }
        finally
        {
            _stats.EndFrame();
        }
    }

    private void GlRenderCore(GlInterface gl, int fb)
    {
        // Consume any pending geometry update — only upload if GL init succeeded.
        var pending = Interlocked.Exchange(ref _pendingSubmission, null);
        if (pending != null && _initError == null)
            UploadSubmission(pending);

        // Process scene-object layer updates (additive on top of the base submission).
        // Time-budget uploads per frame so a large burst (e.g. scene entry / teleport) is
        // spread across frames without capping throughput artificially: a fixed count cap
        // (formerly 5/frame) throttled scene loading to ~150 objects/sec at 30 fps even
        // when the uploads were cheap single prims, while a heavy mesh linkset could still
        // blow the frame at count 1. The budget always admits at least one upload per
        // frame so progress is guaranteed.
        const double MaxSceneUploadMillis = 6.0;
        if (_initError == null)
        {
            if (_pendingClearScene)
            {
                _pendingClearScene = false;
                FreeSceneObjectResources();
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
                        (System.Diagnostics.Stopwatch.GetTimestamp() - uploadStart) / ticksPerMs >= MaxSceneUploadMillis)
                        break;
                    if (!_pendingSceneObjects.TryRemove(key, out var sub)) continue;
                    if (sub == null)
                        RemoveSceneObjectGpuNoRebuild(key);
                    else
                        UploadSceneObjectNoRebuild(key, sub);
                    sceneListDirty = true;
                    uploadsThisFrame++;
                }
            }
            // If we hit the cap, request another render tick to continue draining.
            if (!_pendingSceneObjects.IsEmpty)
                _core.RequestNextFrameRendering();
            if (sceneListDirty)
                RebuildSceneFlatLists();

            // Periodic scene-load pipeline telemetry (~every 5 s at 30 fps) while work is
            // pending, so "loading feels slow" is diagnosable from Veles.log: it shows
            // which stage is deep — GPU upload queue vs texture patches vs deferrals —
            // and whether the decoded-mesh cache is earning its keep.
            if (++_loadTelemetryFrame >= 150)
            {
                _loadTelemetryFrame = 0;
                int up = _pendingSceneObjects.Count;
                int qp = _pendingTexturePatches.Count;
                int dp = _deferredPatches.Count;
                if (up > 0 || qp > 50 || dp > 50)
                    LibreMetaverse.Logger.Debug(
                        $"[SceneLoad] pendingUploads={up} queuedPatches={qp} deferredPatches={dp} " +
                        $"sceneFaces={_sceneOpaque.Count + _sceneAlpha.Count} " +
                        $"meshCache={PrimMeshBuilder.MeshCacheHits}h/{PrimMeshBuilder.MeshCacheMisses}m");
            }
        }

        // Apply per-face vertex updates queued by the animation thread.
        while (_pendingVertexUpdates.TryDequeue(out var upd))
        {
            if (upd.FaceIndex >= 0 && upd.FaceIndex < _faceMeshes.Count)
                _faceMeshes[upd.FaceIndex].UpdateVertices(upd.Verts);
        }

        // Apply per-scene-object vertex updates (e.g. LBS-animated avatars).
        // SceneAvatarAnimator rents buffers from ArrayPool and sets IsPoolRented=true;
        // FlexiPrimAnimator allocates exact-size buffers with new[] (IsPoolRented=false).
        while (_pendingSceneVertexUpdates.TryDequeue(out var su))
        {
            if (_sceneObjects.TryGetValue((ulong)su.RootId, out var scFaces))
            {
                int idx = su.FaceOffset;
                if ((uint)idx < (uint)scFaces.Count)
                    scFaces[idx].mesh.UpdateVertices(su.Verts, su.VertsLength);
            }
            if (su.IsPoolRented)
                ArrayPool<float>.Shared.Return(su.Verts);
        }

        // Dispatch GPU compute jobs for flexi prims and avatar LBS (after CPU vertex
        // updates so the GPU path is always the last write into each mesh VBO each frame).
        _flexiDeformer?.DispatchPending();
        _skinDeformer?.DispatchPending();

        // Use physical pixels for the GL viewport to handle HiDPI correctly.
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int w = (int)(Bounds.Width  * scaling);
        int h = (int)(Bounds.Height * scaling);
        if (w <= 0 || h <= 0)
        {
            // Bounds not yet set — request another render after layout.
            _core.RequestNextFrameRendering();
            return;
        }

        // Render into our own FBO (colour + depth) then blit to Avalonia's FBO.
        EnsureSceneFbo(w, h);
        if (_sceneFbo == 0) return;
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
        GlApi.Gl.Viewport(0, 0, (uint)w, (uint)h);

        // Full GL state reset — Avalonia's compositing engine may leave masks,
        // scissor, or blend state in any configuration between our render calls.
        // The SL viewer similarly resets all state before each avatar draw pass.
        // Texture-unit bindings may also have been touched by Avalonia, so drop the
        // redundant-bind cache used by GlTexture.Bind.
        GlTexture.ResetBindCache();
        GlApi.Gl.ColorMask(true, true, true, true);
        GlApi.Gl.DepthMask(true);
        GlApi.Gl.Disable(EnableCap.ScissorTest);
        GlApi.Gl.Disable(EnableCap.Blend);
        GlApi.Gl.Disable(EnableCap.StencilTest);
        GlApi.Gl.Enable(EnableCap.DepthTest);
        GlApi.Gl.DepthFunc(DepthFunction.Less);
        GlApi.Gl.Enable(EnableCap.CullFace);
        GlApi.Gl.CullFace(TriangleFace.Back);
        GlApi.Gl.FrontFace(FrontFaceDirection.Ccw);

        // Error: vivid red-tint.  No sky: use BackgroundColor.  Sky ready: clear to black
        // (sky shader fills it).  Fallback: solid SL sky-blue without the sky shader.
        float clearR, clearG, clearB;
        if (_initError != null)        { clearR = 0.55f; clearG = 0.10f; clearB = 0.10f; }
        else if (!ShowSky)             { clearR = BackgroundColor.X; clearG = BackgroundColor.Y; clearB = BackgroundColor.Z; }
        else if (_skyReady)            { clearR = 0f;    clearG = 0f;    clearB = 0f;    }
        else                           { clearR = 0.39f; clearG = 0.58f; clearB = 0.93f; }
        GlApi.Gl.ClearColor(clearR, clearG, clearB, 1f);
        GlApi.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_primShader == null)
        {
            BlitSceneToFb(fb, w, h);
            return;
        }

        float aspect = (float)w / h;
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);
        _lastView = view; _lastProj = proj; _lastW = w; _lastH = h;

        // Build a view-frustum once per frame for CPU culling of submitted faces.
        // Pass `null` if culling is disabled so DrawFaces skips the per-face test entirely.
        var viewProj = view * proj;
        Frustum? frustum = FrustumCullingEnabled
            ? FrustumCuller.ExtractPlanes(viewProj)
            : (Frustum?)null;

        // Apply pending transform overrides BEFORE any pass that culls or draws scene
        // faces (G-buffer pre-pass, water reflection, main pass) so every pass this
        // frame sees the same up-to-date face.Transform.
        ApplySceneTransformOverrides();

        // Dead-reckon any scene objects currently in motion forward from their last terse
        // update. Must run after ApplySceneTransformOverrides (which lands the exact received
        // pose at t=0) and before any culling/draw pass so they see the same extrapolated
        // face.Transform this frame.
        ExtrapolateMovingSceneObjects();

        // ── EEP day-cycle update ──────────────────────────────────────────
        // Sample the environment service once per frame so sky and water colour
        // track the in-world day/night cycle.  Done before DrawSky so the sky
        // shader already receives the updated parameters on the same frame.
        if (ShowSky && EnvironmentService != null)
        {
            Sky           = EnvironmentService.GetCurrentSky();
            WaterFogColor = EnvironmentService.GetCurrentWaterFogColor();
        }

        // ── Sky background ────────────────────────────────────────────────
        // Drawn before everything else so it fills pixels not covered by geometry.
        if (ShowSky && _skyReady)
            DrawSky(ref view, ref proj, w, h);

        // ── SSAO pre-pass ─────────────────────────────────────────────────
        // 1. G-buffer: render opaque geometry to extract view-space normals
        //    and scene depth as samplable textures.
        // 2. SSAO: full-screen hemisphere sampling pass → raw occlusion texture.
        // 3. Blur: 4×4 box blur to remove noise.
        int opaqueFaceCount = _opaque.Count + _sceneOpaque.Count;
        bool doSsao = SsaoEnabled && _ssaoReady
                      && opaqueFaceCount > 0
                      && opaqueFaceCount <= SsaoMaxOpaqueFaces;
        uint ssaoTex = 0; // 0 = no SSAO this frame

        if (doSsao)
        {
            EnsureGbufferFbo(w, h);
            EnsureSsaoFbos(w, h);
            if (_gbufFbo != 0 && _ssaoFbo != 0)
            {
                // — G-buffer pass —
                GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufFbo);
                GlApi.Gl.Viewport(0, 0, (uint)w, (uint)h);
                GlApi.Gl.ClearColor(0f, 0f, 0f, 0f);
                GlApi.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GlApi.Gl.Enable(EnableCap.DepthTest);
                GlApi.Gl.DepthMask(true);
                GlApi.Gl.DepthFunc(DepthFunction.Less);
                GlApi.Gl.Enable(EnableCap.CullFace);
                GlApi.Gl.Disable(EnableCap.Blend);
                DrawFacesNormal(_opaque,      _gnormShader!, ref view, ref proj, frustum);
                DrawFacesNormal(_sceneOpaque, _gnormShader!, ref view, ref proj, frustum);

                // — SSAO pass —
                GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoFbo);
                GlApi.Gl.Viewport(0, 0, (uint)w, (uint)h);
                GlApi.Gl.Disable(EnableCap.DepthTest);
                GlApi.Gl.Disable(EnableCap.Blend);
                GlApi.Gl.DepthMask(false);
                _ssaoShader!.Use();
                GlApi.Gl.ActiveTexture(TextureUnit.Texture0);
                GlApi.Gl.BindTexture(TextureTarget.Texture2D, _gbufDepthTex);
                _ssaoShader.Set("uDepthTex", 0);
                GlApi.Gl.ActiveTexture(TextureUnit.Texture1);
                GlApi.Gl.BindTexture(TextureTarget.Texture2D, _gbufNormalTex);
                _ssaoShader.Set("uNormalTex", 1);
                GlApi.Gl.ActiveTexture(TextureUnit.Texture2);
                GlApi.Gl.BindTexture(TextureTarget.Texture2D, _ssaoNoiseTex);
                _ssaoShader.Set("uNoiseTex", 2);
                _ssaoShader.Set("uNoiseScale",   new Vector2(w / 4.0f, h / 4.0f));
                _ssaoShader.Set("uProj",         ref proj);
                _ssaoShader.Set("uScreenSize",   new Vector2(w, h));
                _ssaoShader.Set("uRadius",       0.5f);
                _ssaoShader.Set("uBias",         0.025f);
                _ssaoShader.Set("uStrength",     1.2f);
                GlApi.Gl.BindVertexArray(_quadVao);
                GlApi.Gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                _ssaoShader.Unuse();

                // — Blur pass —
                GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoBlurFbo);
                _ssaoBlurShader!.Use();
                GlApi.Gl.ActiveTexture(TextureUnit.Texture0);
                GlApi.Gl.BindTexture(TextureTarget.Texture2D, _ssaoColorTex);
                _ssaoBlurShader.Set("uSsaoTex",   0);
                _ssaoBlurShader.Set("uTexelSize", new Vector2(1.0f / w, 1.0f / h));
                GlApi.Gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                _ssaoBlurShader.Unuse();
                GlApi.Gl.BindVertexArray(0);

                // Restore state for the main scene pass. The SSAO passes bound raw
                // textures on units 0–2 behind GlTexture.Bind's back — invalidate its cache.
                GlApi.Gl.Enable(EnableCap.DepthTest);
                GlApi.Gl.DepthMask(true);
                GlApi.Gl.ActiveTexture(TextureUnit.Texture0);
                GlTexture.ResetBindCache();
                ssaoTex = _ssaoBlurTex;
            }
        }

        // ── Water reflection pre-pass ─────────────────────────────────────
        // Render the opaque scene from a camera mirrored over the water plane
        // into a fixed-resolution FBO. Must happen before the main scene pass
        // so the reflection texture is ready when DrawWater samples it.
        float waterH = WaterHeight;
        bool  doWater = _waterReady && !float.IsNaN(waterH)
                        && _camera.EyePosition.Z >= waterH - 0.05f;
        if (doWater && WaterReflectionsEnabled)
            DrawWaterReflection(ref view, ref proj, waterH, w, h);

        // ── Main scene pass ───────────────────────────────────────────────
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
        GlApi.Gl.Viewport(0, 0, (uint)w, (uint)h);

        // Opaque pass
        GlApi.Gl.Disable(EnableCap.Blend);
        GlApi.Gl.DepthMask(true);
        // Apply any pending progressive texture patches (streamed bitmaps arriving after
        // the geometry was already submitted). Transform overrides were already applied
        // right after frustum construction, before the G-buffer/reflection pre-passes.
        ApplyTexturePatches();

        DrawFaces(_opaque,      _primShader, ref view, ref proj, ssaoTex: ssaoTex, screenSize: new Vector2(w, h), frustum: frustum, stats: _stats, sky: Sky, enableInstancing: InstancingEnabled);
        DrawFaces(_sceneOpaque, _primShader, ref view, ref proj, ssaoTex: ssaoTex, screenSize: new Vector2(w, h), frustum: frustum, stats: _stats, sky: Sky, enableInstancing: InstancingEnabled);

        // ── Water surface ─────────────────────────────────────────────────
        // Drawn after opaque geometry (correct depth test) but before alpha
        // geometry (transparent objects above water render in front of it).
        if (doWater)
            DrawWater(ref view, ref proj, waterH);

        // Alpha pass — depth-sorted back-to-front, two-sided.
        var allAlpha = _alpha.Count > 0 || _sceneAlpha.Count > 0;
        if (allAlpha)
        {
            // Merge base alpha + scene-object alpha into a persistent list (reused each
            // frame), frustum-culling during the merge so off-screen faces never enter
            // the O(N log N) sort. DrawFaces then receives frustum: null — survivors are
            // known-visible (it still records them in the frame stats).
            _mergedAlpha.Clear();
            AppendVisibleAlpha(_alpha,      frustum);
            AppendVisibleAlpha(_sceneAlpha, frustum);
            var mergedAlpha = _mergedAlpha;
            if (mergedAlpha.Count > 1)
            {
                var eye = _camera.EyePosition;
                // Precompute each face's squared eye-distance once (O(N)). The comparator
                // then reads the cached float, avoiding a vector subtraction + LengthSquared
                // on every one of the O(N log N) comparisons performed by Sort.
                // GetWorldCentroid derives the reference point from the CURRENT Transform,
                // so faces moved via transform overrides (walking avatars) sort by where
                // they are now rather than their build-time Centroid.
                for (int i = 0; i < mergedAlpha.Count; i++)
                    mergedAlpha[i].face.AlphaSortKey = (mergedAlpha[i].face.GetWorldCentroid() - eye).LengthSquared();
                mergedAlpha.Sort(static (a, b) => b.face.AlphaSortKey.CompareTo(a.face.AlphaSortKey));
            }

            GlApi.Gl.Enable(EnableCap.Blend);
            GlApi.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GlApi.Gl.DepthMask(false);
            GlApi.Gl.Disable(EnableCap.CullFace);
            // Alpha surfaces don't receive SSAO — matches SL viewer behaviour.
            DrawFaces(mergedAlpha, _primShader, ref view, ref proj, manageCulling: false, frustum: null, stats: _stats, sky: Sky);
            GlApi.Gl.Enable(EnableCap.CullFace);
            GlApi.Gl.DepthMask(true);
            GlApi.Gl.Disable(EnableCap.Blend);
        }

        // NavMesh walkability overlay — semi-transparent tint by pathfinding type.
        // Capture the volatile table once so both passes render from the same snapshot.
        var navTypes = _navMeshTypes;
        if (NavMeshOverlayEnabled && _navMeshOverlayShader != null && !navTypes.IsEmpty)
        {
            GlApi.Gl.Enable(EnableCap.Blend);
            GlApi.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GlApi.Gl.Disable(EnableCap.CullFace);
            GlApi.Gl.DepthMask(false);
            GlApi.Gl.DepthFunc(DepthFunction.Lequal);
            DrawFacesNavMeshOverlay(_sceneOpaque, _navMeshOverlayShader, ref view, ref proj, navTypes);
            DrawFacesNavMeshOverlay(_sceneAlpha,  _navMeshOverlayShader, ref view, ref proj, navTypes);
            GlApi.Gl.DepthFunc(DepthFunction.Less);
            GlApi.Gl.DepthMask(true);
            GlApi.Gl.Enable(EnableCap.CullFace);
            GlApi.Gl.Disable(EnableCap.Blend);
        }

        // Wireframe overlay.
        // On desktop GL: use PolygonMode for true wireframe.
        // On ES/ANGLE: re-draw with the wireframe shader as solid triangles at slightly
        // reduced depth so edges are visible (barycentric wireframe would be ideal but
        // requires geometry shader support; this is a reasonable approximation).
        if (Wireframe && _wireShader != null)
        {
            GlApi.Gl.Disable(EnableCap.CullFace);
            if (_supportsPolygonMode)
            {
                GlApi.Gl.Enable(EnableCap.PolygonOffsetLine);
                GlApi.Gl.PolygonOffset(-1f, -1f);
                GlApi.Gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                DrawFacesWireframe(_opaque,      _wireShader, ref view, ref proj);
                DrawFacesWireframe(_alpha,       _wireShader, ref view, ref proj);
                DrawFacesWireframe(_sceneOpaque, _wireShader, ref view, ref proj);
                DrawFacesWireframe(_sceneAlpha,  _wireShader, ref view, ref proj);
                GlApi.Gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                GlApi.Gl.Disable(EnableCap.PolygonOffsetLine);
            }
            else
            {
                // ES fallback: draw edges via line EBO.
                // LEQUAL lets co-planar edges pass the depth test against the solid surface.
                GlApi.Gl.DepthFunc(DepthFunction.Lequal);
                GlApi.Gl.Enable(EnableCap.Blend);
                GlApi.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GlApi.Gl.DepthMask(false);
                DrawFacesWireframeEs(_opaque,      _wireShader, ref view, ref proj);
                DrawFacesWireframeEs(_alpha,       _wireShader, ref view, ref proj);
                DrawFacesWireframeEs(_sceneOpaque, _wireShader, ref view, ref proj);
                DrawFacesWireframeEs(_sceneAlpha,  _wireShader, ref view, ref proj);
                GlApi.Gl.DepthMask(true);
                GlApi.Gl.Disable(EnableCap.Blend);
                GlApi.Gl.DepthFunc(DepthFunction.Less);
            }
            GlApi.Gl.Enable(EnableCap.CullFace);
        }

        // ── Particle pass ─────────────────────────────────────────────────
        // Drain pending submissions from all threads into the GL-thread map.
        foreach (var key in _pendingParticleMap.Keys)
        {
            if (!_pendingParticleMap.TryRemove(key, out var sub)) continue;
            if (sub == null)
            {
                if (_particleMap.TryGetValue(key, out var old))
                {
                    old.Tex?.Dispose();
                    _particleMap.Remove(key);
                }
            }
            else
            {
                GlTexture? newTex = null;
                if (sub.Texture != null)
                {
                    try { newTex = new GlTexture(GlTexture.Preprocess(sub.Texture)); } catch { }
                    sub.Texture.Dispose();
                }
                if (_particleMap.TryGetValue(key, out var existing))
                {
                    // keep old texture if this tick has no new one
                    newTex ??= existing.Tex;
                    if (newTex != existing.Tex) existing.Tex?.Dispose();
                }
                _particleMap[key] = (sub, newTex);
            }
        }

        DrawParticles(ref view, ref proj);

        // Copy our rendered scene to Avalonia's compositing FBO.
        BlitSceneToFb(fb, w, h);

        // ── Picking pass (dedicated offscreen FBO) ────────────────────────
        if (_pickRequested && _pickShader != null)
        {
            _pickRequested = false;

            int px = (int)(_pickPoint.X * scaling);
            int py = h - (int)(_pickPoint.Y * scaling); // flip Y for GL

            // Render into a dedicated RGBA8 FBO so we never disturb Avalonia's
            // compositing surface and have a guaranteed-readable RGBA8 buffer.
            EnsurePickFbo(w, h);
            // Guard: only proceed if the pick FBO was successfully created.
            if (_pickFbo != 0)
            {
                GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _pickFbo);
                GlApi.Gl.Viewport(0, 0, (uint)w, (uint)h);
                GlApi.Gl.ClearColor(0f, 0f, 0f, 0f);
                GlApi.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                // Set explicit state — don't rely on inherited values from the main pass.
                GlApi.Gl.Enable(EnableCap.DepthTest);
                GlApi.Gl.DepthFunc(DepthFunction.Less);
                GlApi.Gl.Disable(EnableCap.Blend);
                GlApi.Gl.DepthMask(true);
                GlApi.Gl.Disable(EnableCap.CullFace);
                GlApi.Gl.ColorMask(true, true, true, true);

                // Rebuild _pickMap/_cpuFaceData to match the exact draw order used
                // here, including any back-to-front sort applied to _alpha this frame.
                _pickMap.Clear();
                _cpuFaceData.Clear();
                foreach (var (_, _, _, _, _, _, face) in _opaque)
                {
                    _pickMap.Add((face.PrimLocalId, face.FaceIndex));
                    _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
                }
                foreach (var (_, _, _, _, _, _, face) in _alpha)
                {
                    _pickMap.Add((face.PrimLocalId, face.FaceIndex));
                    _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
                }
                foreach (var (_, _, _, _, _, _, face) in _sceneOpaque)
                {
                    _pickMap.Add((face.PrimLocalId, face.FaceIndex));
                    _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
                }
                foreach (var (_, _, _, _, _, _, face) in _sceneAlpha)
                {
                    _pickMap.Add((face.PrimLocalId, face.FaceIndex));
                    _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
                }

                DrawFacesPicking(_opaque,      _pickShader, ref view, ref proj, 1);
                DrawFacesPicking(_alpha,       _pickShader, ref view, ref proj, 1 + _opaque.Count);
                DrawFacesPicking(_sceneOpaque, _pickShader, ref view, ref proj, 1 + _opaque.Count + _alpha.Count);
                DrawFacesPicking(_sceneAlpha,  _pickShader, ref view, ref proj, 1 + _opaque.Count + _alpha.Count + _sceneOpaque.Count);

                GlApi.Gl.Flush(); // ensure all draw calls are complete before ReadPixels
                byte[] pixel = new byte[4];
                GlApi.Gl.ReadPixels(px, py, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

                // Restore Avalonia's framebuffer (main scene already rendered there).
                GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                GlApi.Gl.Enable(EnableCap.CullFace);

                // R+G+B encode a 1-based index into _pickMap (background cleared to 0,0,0,0).
                // Non-zero RGB means a face was hit; look up the real LocalId/FaceIndex.
                if (pixel[0] != 0 || pixel[1] != 0 || pixel[2] != 0)
                {
                    uint idx = (uint)(pixel[0] | (pixel[1] << 8) | (pixel[2] << 16)); // 1-based
                    if (idx >= 1 && (int)idx <= _pickMap.Count)
                    {
                        var (primLocalId, faceIndex) = _pickMap[(int)(idx - 1)];
                        var hitInfo = ComputeHitInfo((int)(idx - 1), px, py, w, h);
                        Avalonia.Threading.Dispatcher.UIThread.Post(
                            () => FaceClicked?.Invoke(primLocalId, faceIndex, hitInfo));
                    }
                }
                // Release CPU vertex/index arrays immediately after pick — they are
                // only needed inside ComputeHitInfo above.  Keeping them allocated
                // until the next pick holds all scene face geometry in managed RAM.
                _cpuFaceData.Clear();
            }
        }
    }

    private void GlDeinit(GlInterface gl)
    {
        // Drain any submission that arrived after the last GlRender — its SKBitmaps
        // will never reach UploadSubmission now that the GL context is tearing down.
        var pending = Interlocked.Exchange(ref _pendingSubmission, null);
        if (pending != null)
            DisposePendingBitmaps(pending);

        // Drain any lingering submission texture patches.
        while (_pendingSubmissionPatches.TryDequeue(out var sp)) sp.Bitmap?.Dispose();

        // Drain scene-object texture patches and release the gate so any blocked
        // producers unblock and observe cancellation instead of hanging forever.
        var oldGate = _texturePatchGate;
        while (_pendingTexturePatches.TryDequeue(out var tp))
        {
            tp.Bitmap?.Dispose();
            ReleasePatchGate(oldGate);
        }
        // Install a fresh gate BEFORE disposing the old one so producers that observe the
        // field after this line get a live instance. Without this, the first tab-switch
        // teardown left a permanently disposed gate and every later texture patch was
        // silently dropped (progressive texture streaming died for the session).
        _texturePatchGate = new SemaphoreSlim(TexturePatchQueueDepth, TexturePatchQueueDepth);
        oldGate.Dispose();

        // Drain pending scene-object submissions and dispose embedded bitmaps.
        foreach (var key in _pendingSceneObjects.Keys)
        {
            if (_pendingSceneObjects.TryRemove(key, out var sub) && sub != null)
                DisposePendingBitmaps(sub);
        }

        foreach (var kv in _pendingParticleMap)
            kv.Value?.Texture?.Dispose();
        _pendingParticleMap.Clear();
        foreach (var kv in _particleMap)
            kv.Value.Tex?.Dispose();
        _particleMap.Clear();

        FreeSceneObjectResources();
        FreeGpuResources();
        DeleteSceneFbo();
        DeletePickFbo();
        DeleteSsaoFbos();
        if (_gbufDepthTex != 0) { GlApi.Gl.DeleteTexture(_gbufDepthTex); _gbufDepthTex = 0; }
        if (_gbufNormalTex != 0) { GlApi.Gl.DeleteTexture(_gbufNormalTex); _gbufNormalTex = 0; }
        if (_gbufFbo != 0)       { GlApi.Gl.DeleteFramebuffer(_gbufFbo); _gbufFbo = 0; }
        if (_ssaoNoiseTex != 0)  { GlApi.Gl.DeleteTexture(_ssaoNoiseTex); _ssaoNoiseTex = 0; }
        if (_quadVao != 0)       { GlApi.Gl.DeleteVertexArray(_quadVao); _quadVao = 0; }
        DeleteWaterResources();
        DeleteSkyResources();
        _primShader?.Dispose();     _primShader     = null;
        _primLocs = null;           _primLocsShader = null;
        _wireShader?.Dispose();     _wireShader     = null;
        _pickShader?.Dispose();     _pickShader     = null;
        _particleShader?.Dispose(); _particleShader = null;
        _gnormShader?.Dispose();    _gnormShader    = null;
        _ssaoShader?.Dispose();     _ssaoShader     = null;
        _ssaoBlurShader?.Dispose(); _ssaoBlurShader = null;
        _particleBuf?.Dispose();    _particleBuf    = null;
        _instanceDrawer?.Dispose(); _instanceDrawer = null;
        _flexiDeformer?.Dispose();  _flexiDeformer  = null;
        _skinDeformer?.Dispose();   _skinDeformer   = null;
        _stats.Dispose();
    }

    // ── Particle upload & draw ────────────────────────────────────────────────────



    /// <summary>
    /// CPU ray-triangle intersection for the face at <paramref name="pickIdx"/> (0-based),
    /// computing world-space position, UV, normal, and binormal at the hit point.
    ///
    /// Algorithm mirrors LLPickInfo::getSurfaceInfo in the SL viewer:
    /// 1. Unproject the screen pixel to a world-space ray.
    /// 2. Möller–Trumbore intersection against every triangle of the face.
    /// 3. Barycentric interpolation of vertex UV and normal; binormal from cross(tangent, normal).
    ///
    /// Vertex layout (12 floats per vertex): pos(3) normal(3) uv(2) tangent(4).
    /// </summary>
    /// <summary>
    /// Extracts a positions-only buffer (3 floats per vertex) from an interleaved
    /// Position(3)+Normal(3)+TexCoord(2)+Tangent(4) buffer. Used to slim the CPU-side picker
    /// data after the full interleaved buffer has been uploaded to the GPU,
    /// reducing LOH retention by ~75%.
    /// </summary>
    private static float[] PickerFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 3)
            return Array.Empty<float>();
        int len    = length > 0 ? Math.Min(length, interleaved.Length) : interleaved.Length;
        len       -= len % 12; // align down to a whole-vertex boundary
        int vCount = len / 12;
        var pos = new float[vCount * 3];
        for (int i = 0, j = 0; i < len; i += 12, j += 3)
        {
            pos[j]     = interleaved[i];
            pos[j + 1] = interleaved[i + 1];
            pos[j + 2] = interleaved[i + 2];
        }
        return pos;
    }

    // Extracts a compact normal+UV buffer (5 floats/vertex: nx,ny,nz,u,v) from the full
    // interleaved buffer (12 floats/vertex: px,py,pz,nx,ny,nz,u,v,tx,ty,tz,tw).
    // Stored alongside PickerVertices so the full 12-wide buffer can be released post-upload.
    private static float[] NormalUvFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 12)
            return Array.Empty<float>();
        int len    = length > 0 ? Math.Min(length, interleaved.Length) : interleaved.Length;
        len       -= len % 12;
        int vCount = len / 12;
        var nuv = new float[vCount * 5];
        for (int i = 0, j = 0; i < len; i += 12, j += 5)
        {
            nuv[j]     = interleaved[i + 3]; // nx
            nuv[j + 1] = interleaved[i + 4]; // ny
            nuv[j + 2] = interleaved[i + 5]; // nz
            nuv[j + 3] = interleaved[i + 6]; // u
            nuv[j + 4] = interleaved[i + 7]; // v
        }
        return nuv;
    }

    private FaceHitInfo ComputeHitInfo(int pickIdx, int px, int py, int w, int h)
    {
        if (pickIdx < 0 || pickIdx >= _cpuFaceData.Count)
            return FaceHitInfo.Unknown;

        var (pickerVerts, normalUvVerts, indices, model) = _cpuFaceData[pickIdx];
        const int PickerStride  = 3; // floats per vertex in the slim picker buffer (X,Y,Z)
        const int NormalUvStride = 5; // floats per vertex in the compact normal/UV buffer (nx,ny,nz,u,v)

        // Build world-space ray from clip-space click position.
        // NDC: x in [-1,1], y in [-1,1] (GL: bottom-left origin).
        float ndcX =  (2f * px / w) - 1f;
        float ndcY =  (2f * py / h) - 1f; // py already flipped to GL coords in caller

        // Unproject: row-vector convention (v' = v * M), so world→clip is
        // v_clip = v_world * view * proj.  Invert: v_world = v_clip * inv(view * proj).
        Matrix4x4.Invert(_lastView * _lastProj, out var invVP);
        var nearH  = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVP);
        var farH   = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVP);
        nearH /= nearH.W;
        farH  /= farH.W;
        var rayOrigin = new Vector3(nearH.X, nearH.Y, nearH.Z);
        var rayDir    = Vector3.Normalize(new Vector3(farH.X, farH.Y, farH.Z) - rayOrigin);

        float bestT  = float.MaxValue;
        float bestU  = 0f, bestV = 0f;
        int   bestTri = -1;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i + 0] * PickerStride;
            int i1 = indices[i + 1] * PickerStride;
            int i2 = indices[i + 2] * PickerStride;

            // Transform vertex positions to world space.
            var _t0 = Vector4.Transform(new Vector4(pickerVerts[i0], pickerVerts[i0+1], pickerVerts[i0+2], 1f), model); var p0w = new Vector3(_t0.X, _t0.Y, _t0.Z);
            var _t1 = Vector4.Transform(new Vector4(pickerVerts[i1], pickerVerts[i1+1], pickerVerts[i1+2], 1f), model); var p1w = new Vector3(_t1.X, _t1.Y, _t1.Z);
            var _t2 = Vector4.Transform(new Vector4(pickerVerts[i2], pickerVerts[i2+1], pickerVerts[i2+2], 1f), model); var p2w = new Vector3(_t2.X, _t2.Y, _t2.Z);

            // Möller–Trumbore.
            var edge1 = p1w - p0w;
            var edge2 = p2w - p0w;
            var h2    = Vector3.Cross(rayDir, edge2);
            float det = Vector3.Dot(edge1, h2);
            if (MathF.Abs(det) < 1e-7f) continue; // parallel
            float invDet = 1f / det;
            var   s      = rayOrigin - p0w;
            float u      = Vector3.Dot(s, h2) * invDet;
            if (u < 0f || u > 1f) continue;
            var   q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(rayDir, q) * invDet;
            if (v < 0f || u + v > 1f) continue;
            float t = Vector3.Dot(edge2, q) * invDet;
            if (t < 0f || t >= bestT) continue;
            bestT   = t;
            bestU   = u;
            bestV   = v;
            bestTri = i;
        }

        if (bestTri < 0)
            return FaceHitInfo.Unknown; // no intersection (shouldn't happen after GPU pick)

        // Vertex indices of the hit triangle into the compact picker buffer (stride 3: X,Y,Z).
        int p0 = indices[bestTri + 0] * PickerStride;
        int p1 = indices[bestTri + 1] * PickerStride;
        int p2 = indices[bestTri + 2] * PickerStride;
        float w0 = 1f - bestU - bestV, w1 = bestU, w2 = bestV;

        var lpos0 = new Vector3(pickerVerts[p0], pickerVerts[p0+1], pickerVerts[p0+2]);
        var lpos1 = new Vector3(pickerVerts[p1], pickerVerts[p1+1], pickerVerts[p1+2]);
        var lpos2 = new Vector3(pickerVerts[p2], pickerVerts[p2+1], pickerVerts[p2+2]);

        // World-space hit point straight from the ray parameter — the intersection loop
        // above already ran in world space, so this is exact and matches the documented
        // contract (SL ObjectGrab Position is world/region space, not object-local).
        var worldPos = rayOrigin + rayDir * bestT;

        // Normal and UV from the compact normal/UV buffer (stride 5: nx,ny,nz,u,v).
        int n0i = indices[bestTri + 0] * NormalUvStride;
        int n1i = indices[bestTri + 1] * NormalUvStride;
        int n2i = indices[bestTri + 2] * NormalUvStride;

        var n0 = Vector3.Normalize(new Vector3(normalUvVerts[n0i], normalUvVerts[n0i+1], normalUvVerts[n0i+2]));
        var n1 = Vector3.Normalize(new Vector3(normalUvVerts[n1i], normalUvVerts[n1i+1], normalUvVerts[n1i+2]));
        var n2 = Vector3.Normalize(new Vector3(normalUvVerts[n2i], normalUvVerts[n2i+1], normalUvVerts[n2i+2]));
        var localNormal = Vector3.Normalize(n0 * w0 + n1 * w1 + n2 * w2);

        // Transform the interpolated normal into world space with the inverse-transpose of
        // the model matrix (correct under non-uniform scale). Row-vector convention:
        // n_world = n_local · (M⁻¹)ᵀ. Falls back to the plain 3×3 for a singular model.
        Vector3 normal;
        if (Matrix4x4.Invert(model, out var invModel))
            normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, Matrix4x4.Transpose(invModel)));
        else
            normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, model));

        float uvX = normalUvVerts[n0i+3] * w0 + normalUvVerts[n1i+3] * w1 + normalUvVerts[n2i+3] * w2;
        float uvY = normalUvVerts[n0i+4] * w0 + normalUvVerts[n1i+4] * w1 + normalUvVerts[n2i+4] * w2;

        // Binormal: from world-space triangle edge/UV delta pair (Lengyel's method), so the
        // resulting frame matches the world-space normal above.
        var _w0 = Vector4.Transform(new Vector4(lpos0, 1f), model);
        var _w1 = Vector4.Transform(new Vector4(lpos1, 1f), model);
        var _w2 = Vector4.Transform(new Vector4(lpos2, 1f), model);
        var  edge1w  = new Vector3(_w1.X, _w1.Y, _w1.Z) - new Vector3(_w0.X, _w0.Y, _w0.Z);
        var  edge2w  = new Vector3(_w2.X, _w2.Y, _w2.Z) - new Vector3(_w0.X, _w0.Y, _w0.Z);
        float duv1x  = normalUvVerts[n1i+3] - normalUvVerts[n0i+3];
        float duv1y  = normalUvVerts[n1i+4] - normalUvVerts[n0i+4];
        float duv2x  = normalUvVerts[n2i+3] - normalUvVerts[n0i+3];
        float duv2y  = normalUvVerts[n2i+4] - normalUvVerts[n0i+4];
        float denom  = duv1x * duv2y - duv2x * duv1y;
        Vector3 binormal;
        if (MathF.Abs(denom) > 1e-7f)
        {
            float r = 1f / denom;
            var tangent = Vector3.Normalize((edge1w * duv2y - edge2w * duv1y) * r);
            binormal    = Vector3.Normalize(Vector3.Cross(normal, tangent));
        }
        else
        {
            binormal = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX));
        }

        var uvVec = new Vector3(uvX, uvY, 0f);
        return new FaceHitInfo
        {
            UvCoord  = uvVec,
            StCoord  = uvVec,
            Position = worldPos,
            Normal   = normal,
            Binormal = binormal,
        };
    }

    private void DrawParticles(ref Matrix4x4 view, ref Matrix4x4 proj)
    {
        if (_particleBuf == null || _particleShader == null || _particleMap.Count == 0) return;

        // Build camera basis vectors (world space) for CPU billboarding.
        // In our Z-up view matrix the right vector is column 0 and up is column 1.
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up    = new Vector3(view.M12, view.M22, view.M32);

        GlApi.Gl.Enable(EnableCap.Blend);
        GlApi.Gl.DepthMask(false);
        GlApi.Gl.Disable(EnableCap.CullFace);
        _particleShader.Use();

        foreach (var kv in _particleMap)
        {
            var (sub, tex) = kv.Value;
            if (sub.Particles.Length == 0) continue;

            _particleBuf.Upload(sub.Particles, right, up);

            // Per-system blend function.
            GlApi.Gl.BlendFunc((BlendingFactor)sub.BlendSrc, (BlendingFactor)sub.BlendDst);

            var model = sub.EmitterTransform;
            var mvp   = model * view * proj;
            _particleShader.Set("uMvp", ref mvp);

            bool hasTex = tex != null;
            _particleShader.Set("uHasTexture", hasTex);
            if (hasTex)
            {
                tex!.Bind(0);
                _particleShader.Set("uAlbedo", 0);
            }
            _particleShader.Set("uGlow", 0f);

            _particleBuf.Draw();
        }

        _particleShader.Unuse();
        GlApi.Gl.Enable(EnableCap.CullFace);
        GlApi.Gl.DepthMask(true);
        GlApi.Gl.Disable(EnableCap.Blend);
    }

    private static void DisposePendingBitmaps(PrimRenderSubmission sub)
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

    private void EnsurePickFbo(int w, int h)
    {
        if (_pickFbo != 0 && _pickFboW == w && _pickFboH == h) return;

        DeletePickFbo();

        _pickFbo   = GlApi.Gl.GenFramebuffer();
        _pickRbo   = GlApi.Gl.GenRenderbuffer();
        _pickDepth = GlApi.Gl.GenRenderbuffer();

        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _pickFbo);

        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _pickRbo);
        GlApi.Gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            InternalFormat.Rgba8, (uint)w, (uint)h);
        GlApi.Gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _pickRbo);

        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _pickDepth);
        GlApi.Gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            InternalFormat.DepthComponent24, (uint)w, (uint)h);
        GlApi.Gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _pickDepth);

        var status = GlApi.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        if (status != GLEnum.FramebufferComplete)
        {
            // FBO unsupported on this driver/backend — picking disabled.
            DeletePickFbo();
            return;
        }

        _pickFboW = w;
        _pickFboH = h;
    }

    private void DeletePickFbo()
    {
        if (_pickFbo == 0) return;
        GlApi.Gl.DeleteFramebuffer(_pickFbo);
        GlApi.Gl.DeleteRenderbuffer(_pickRbo);
        GlApi.Gl.DeleteRenderbuffer(_pickDepth);
        _pickFbo = _pickRbo = _pickDepth = 0;
        _pickFboW = _pickFboH = 0;
    }

    private void EnsureSceneFbo(int w, int h)
    {
        if (_sceneFbo != 0 && _sceneFboW == w && _sceneFboH == h) return;

        DeleteSceneFbo();

        _sceneFbo   = GlApi.Gl.GenFramebuffer();
        _sceneColor = GlApi.Gl.GenRenderbuffer();
        _sceneDepth = GlApi.Gl.GenRenderbuffer();

        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);

        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _sceneColor);
        GlApi.Gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            InternalFormat.Rgba8, (uint)w, (uint)h);
        GlApi.Gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _sceneColor);

        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _sceneDepth);
        // 24-bit depth is essential — 16-bit only gives 65536 values over the
        // near:far range (0.01–500 = 50000:1) which causes severe z-fighting on
        // closely layered avatar body parts (eyelashes, clothing, overlapping prims).
        // GL ES 3.0 guarantees DEPTH_COMPONENT24 support.
        GlApi.Gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            InternalFormat.DepthComponent24, (uint)w, (uint)h);
        GlApi.Gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _sceneDepth);

        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        var status = GlApi.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            DeleteSceneFbo();
            return;
        }

        _sceneFboW = w;
        _sceneFboH = h;
    }

    private void DeleteSceneFbo()
    {
        if (_sceneFbo == 0) return;
        GlApi.Gl.DeleteFramebuffer(_sceneFbo);
        GlApi.Gl.DeleteRenderbuffer(_sceneColor);
        GlApi.Gl.DeleteRenderbuffer(_sceneDepth);
        _sceneFbo = _sceneColor = _sceneDepth = 0;
        _sceneFboW = _sceneFboH = 0;
    }

    // ── SSAO FBO management ───────────────────────────────────────────────────────

    private unsafe void EnsureGbufferFbo(int w, int h)
    {
        if (_gbufFbo != 0 && _gbufW == w && _gbufH == h) return;

        // Delete old resources.
        if (_gbufFbo      != 0) { GlApi.Gl.DeleteFramebuffer(_gbufFbo);   _gbufFbo = 0; }
        if (_gbufNormalTex != 0) { GlApi.Gl.DeleteTexture(_gbufNormalTex); _gbufNormalTex = 0; }
        if (_gbufDepthTex  != 0) { GlApi.Gl.DeleteTexture(_gbufDepthTex);  _gbufDepthTex = 0; }

        // Normal texture — RGBA8, packed [0,1].
        _gbufNormalTex = GlApi.Gl.GenTexture();
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _gbufNormalTex);
        GlApi.Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Depth texture — DEPTH_COMPONENT24 samplable texture.
        _gbufDepthTex = GlApi.Gl.GenTexture();
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _gbufDepthTex);
        GlApi.Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
            (uint)w, (uint)h, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);

        GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);

        _gbufFbo = GlApi.Gl.GenFramebuffer();
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufFbo);
        GlApi.Gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _gbufNormalTex, 0);
        GlApi.Gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _gbufDepthTex, 0);

        var status = GlApi.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        if (status != GLEnum.FramebufferComplete)
        {
            GlApi.Gl.DeleteFramebuffer(_gbufFbo);  _gbufFbo = 0;
            GlApi.Gl.DeleteTexture(_gbufNormalTex); _gbufNormalTex = 0;
            GlApi.Gl.DeleteTexture(_gbufDepthTex);  _gbufDepthTex = 0;
            _ssaoReady = false;
            return;
        }

        _gbufW = w;
        _gbufH = h;
    }

    private unsafe void EnsureSsaoFbos(int w, int h)
    {
        if (_ssaoFbo != 0 && _ssaoFboW == w && _ssaoFboH == h) return;

        DeleteSsaoFbos();

        // SSAO colour texture.
        _ssaoColorTex = GlApi.Gl.GenTexture();
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _ssaoColorTex);
        GlApi.Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8,
            (uint)w, (uint)h, 0, PixelFormat.Red, PixelType.UnsignedByte, null);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Blur colour texture.
        _ssaoBlurTex = GlApi.Gl.GenTexture();
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _ssaoBlurTex);
        GlApi.Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8,
            (uint)w, (uint)h, 0, PixelFormat.Red, PixelType.UnsignedByte, null);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);

        // SSAO FBO.
        _ssaoFbo = GlApi.Gl.GenFramebuffer();
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoFbo);
        GlApi.Gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _ssaoColorTex, 0);
        var s1 = GlApi.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        // Blur FBO.
        _ssaoBlurFbo = GlApi.Gl.GenFramebuffer();
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoBlurFbo);
        GlApi.Gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _ssaoBlurTex, 0);
        var s2 = GlApi.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        if (s1 != GLEnum.FramebufferComplete ||
            s2 != GLEnum.FramebufferComplete)
        {
            DeleteSsaoFbos();
            _ssaoReady = false;
            return;
        }

        _ssaoFboW = w;
        _ssaoFboH = h;
    }

    private void DeleteSsaoFbos()
    {
        if (_ssaoFbo != 0)     { GlApi.Gl.DeleteFramebuffer(_ssaoFbo);    _ssaoFbo = 0; }
        if (_ssaoBlurFbo != 0) { GlApi.Gl.DeleteFramebuffer(_ssaoBlurFbo); _ssaoBlurFbo = 0; }
        if (_ssaoColorTex != 0){ GlApi.Gl.DeleteTexture(_ssaoColorTex);   _ssaoColorTex = 0; }
        if (_ssaoBlurTex != 0) { GlApi.Gl.DeleteTexture(_ssaoBlurTex);    _ssaoBlurTex = 0; }
        _ssaoFboW = _ssaoFboH = 0;
    }

    // ── Water rendering ───────────────────────────────────────────────────────────

    private unsafe void InitWater()
    {
        _waterShader = GlShader.Compile(
            ShaderLoader.Load("water.vert"),
            ShaderLoader.Load("water.frag"));

        // Load normalmap and dudvmap from embedded resources
        _waterNormalmapTex = LoadEmbeddedTexture("normalmap.png", repeat: true);
        _waterDudvmapTex   = LoadEmbeddedTexture("dudvmap.png",   repeat: true);

        // No per-water VAO needed — the fullscreen triangle uses _quadVao.

        // Reflection FBO — fixed WaterReflSize × WaterReflSize
        _waterReflColorTex = GlApi.Gl.GenTexture();
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _waterReflColorTex);
        GlApi.Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            WaterReflSize, WaterReflSize, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);

        _waterReflDepthRb = GlApi.Gl.GenRenderbuffer();
        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _waterReflDepthRb);
        GlApi.Gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, WaterReflSize, WaterReflSize);
        GlApi.Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        _waterReflFbo = GlApi.Gl.GenFramebuffer();
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _waterReflFbo);
        GlApi.Gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _waterReflColorTex, 0);
        GlApi.Gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _waterReflDepthRb);
        var fbStatus = GlApi.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        if (fbStatus != GLEnum.FramebufferComplete)
        {
            // Reflection FBO unsupported — water still renders with water colour fallback
            GlApi.Gl.DeleteFramebuffer(_waterReflFbo);
            GlApi.Gl.DeleteTexture(_waterReflColorTex);
            GlApi.Gl.DeleteRenderbuffer(_waterReflDepthRb);
            _waterReflFbo = _waterReflColorTex = _waterReflDepthRb = 0;
        }

        _waterLastTick = Environment.TickCount64;
        _waterReady    = true;
    }

    /// <summary>
    /// Loads an embedded shader-data PNG as a GL texture with mipmaps.
    /// <paramref name="repeat"/> controls wrap mode (Repeat for tiling, ClampToEdge for one-shot).
    /// </summary>
    private static unsafe uint LoadEmbeddedTexture(string filename, bool repeat = false)
    {
        var uri = new Uri("avares://RadegastVeles/Rendering/shader_data/" + filename);
        using var stream = Avalonia.Platform.AssetLoader.Open(uri);
        using var bitmap = SkiaSharp.SKBitmap.Decode(stream);
        if (bitmap == null) return 0;

        using var processed = GlTexture.Preprocess(bitmap);

        uint tex = GlApi.Gl.GenTexture();
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, tex);

        var span = processed.GetPixelSpan();
        fixed (byte* ptr = span)
        {
            GlApi.Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)processed.Width, (uint)processed.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }

        GlApi.Gl.GenerateMipmap(TextureTarget.Texture2D);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        int wrap = repeat ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge;
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    /// <summary>
    /// Renders the opaque scene into the reflection FBO using a camera mirrored about
    /// the water plane (Z = <paramref name="waterHeight"/>).
    /// Flips front-face winding to correct back-face culling after the Z mirror.
    /// Leaves FBO state unbound; the caller must rebind the scene FBO before continuing.
    /// </summary>
    private void DrawWaterReflection(ref Matrix4x4 view, ref Matrix4x4 proj, float waterHeight, int w, int h)
    {
        if (_waterReflFbo == 0 || _primShader == null) return;

        long nowTick = Environment.TickCount64;
        if (nowTick - _reflLastTick < kReflIntervalMs)
            return; // reuse the cached reflection texture

        // Build reflected view: Z mirror about z = waterHeight in world space.
        // Row-vector convention: reflMat transforms v_world -> v_reflected_world.
        //   (x, y, z, 1) * reflMat = (x, y, 2*wh - z, 1)
        var reflMat = new Matrix4x4(
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, -1f, 0f,
            0f, 0f, 2f * waterHeight, 1f);
        var reflView     = reflMat * view;
        var reflViewProj = reflView * proj;
        var reflFrustum  = FrustumCuller.ExtractPlanes(reflViewProj);
        _lastReflViewProj = reflViewProj;

        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, _waterReflFbo);
        GlApi.Gl.Viewport(0, 0, WaterReflSize, WaterReflSize);
        GlApi.Gl.ClearColor(0.39f, 0.58f, 0.93f, 1f);
        GlApi.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GlApi.Gl.Enable(EnableCap.DepthTest);
        GlApi.Gl.DepthMask(true);
        GlApi.Gl.DepthFunc(DepthFunction.Less);
        GlApi.Gl.Enable(EnableCap.CullFace);
        GlApi.Gl.Disable(EnableCap.Blend);
        // Z mirror flips winding: what was CCW becomes CW from the reflected camera
        GlApi.Gl.FrontFace(FrontFaceDirection.CW);

        DrawFaces(_opaque,      _primShader, ref reflView, ref proj, frustum: reflFrustum, sky: Sky, enableInstancing: InstancingEnabled);
        DrawFaces(_sceneOpaque, _primShader, ref reflView, ref proj, frustum: reflFrustum, sky: Sky, enableInstancing: InstancingEnabled);

        GlApi.Gl.FrontFace(FrontFaceDirection.Ccw);
        // Unbind reflection FBO; main scene pass will rebind _sceneFbo
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GlApi.Gl.Viewport(0, 0, (uint)w, (uint)h);
        _reflLastTick = nowTick;
    }

    /// <summary>
    /// Draws the infinite water surface at <paramref name="waterHeight"/>.
    /// Called after the opaque pass so depth is written for underwater geometry.
    /// </summary>
    private void DrawWater(ref Matrix4x4 view, ref Matrix4x4 proj, float waterHeight)
    {
        if (_waterShader == null) return;

        // Advance animation clock
        long  now  = Environment.TickCount64;
        float dt   = MathF.Min((now - _waterLastTick) / 1000f, 0.1f);
        _waterLastTick = now;
        _waterTime    += dt;

        var eye       = _camera.EyePosition;
        var viewProj  = view * proj;
        Matrix4x4.Invert(viewProj, out var invVP);
        var lightDir  = Vector3.Normalize(Sky.SunDirection);

        _waterShader.Use();
        _waterShader.Set("uViewProj",      ref viewProj);
        _waterShader.Set("uInvViewProj",   ref invVP);
        _waterShader.Set("uReflViewProj",  ref _lastReflViewProj);
        _waterShader.Set("uEyePos",        eye);
        _waterShader.Set("uWaterHeight",   waterHeight);
        _waterShader.Set("uTime",          _waterTime);
        _waterShader.Set("uWaterColor",    WaterFogColor);
        _waterShader.Set("uLightDir",      lightDir);
        _waterShader.Set("uHasReflection", (_waterReflFbo != 0 && WaterReflectionsEnabled) ? 1 : 0);

        GlApi.Gl.ActiveTexture(TextureUnit.Texture0);
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _waterReflColorTex);
        _waterShader.Set("uReflectionTex", 0);

        GlApi.Gl.ActiveTexture(TextureUnit.Texture1);
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _waterNormalmapTex);
        _waterShader.Set("uNormalMap", 1);

        GlApi.Gl.ActiveTexture(TextureUnit.Texture2);
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, _waterDudvmapTex);
        _waterShader.Set("uDudvMap", 2);

        GlApi.Gl.Enable(EnableCap.Blend);
        GlApi.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GlApi.Gl.Disable(EnableCap.CullFace);
        // LessOrEqual: water surface (gl_FragDepth = actual surface hit depth) passes when
        // the surface is closer to the camera than existing geometry, covering underwater
        // terrain.  Also passes at the horizon where the hit is beyond the far plane and
        // gl_FragDepth clamps to 1.0 (== cleared buffer value), keeping horizon water visible.
        GlApi.Gl.DepthFunc(DepthFunction.Lequal);

        GlApi.Gl.BindVertexArray(_quadVao);
        GlApi.Gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GlApi.Gl.BindVertexArray(0);

        GlApi.Gl.DepthFunc(DepthFunction.Less);
        GlApi.Gl.Enable(EnableCap.CullFace);
        GlApi.Gl.Disable(EnableCap.Blend);
        _waterShader.Unuse();

        // Clean up texture units so subsequent passes start from a known state.
        // These raw binds bypass GlTexture.Bind, so its redundant-bind cache must be dropped.
        GlApi.Gl.ActiveTexture(TextureUnit.Texture2); GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
        GlApi.Gl.ActiveTexture(TextureUnit.Texture1); GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
        GlApi.Gl.ActiveTexture(TextureUnit.Texture0); GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
        GlTexture.ResetBindCache();
    }

    private void DeleteWaterResources()
    {
        _waterShader?.Dispose();     _waterShader = null;
        if (_waterReflFbo != 0)      { GlApi.Gl.DeleteFramebuffer(_waterReflFbo);      _waterReflFbo = 0; }
        if (_waterReflColorTex != 0) { GlApi.Gl.DeleteTexture(_waterReflColorTex);     _waterReflColorTex = 0; }
        if (_waterReflDepthRb != 0)  { GlApi.Gl.DeleteRenderbuffer(_waterReflDepthRb); _waterReflDepthRb = 0; }
        if (_waterNormalmapTex != 0) { GlApi.Gl.DeleteTexture(_waterNormalmapTex);     _waterNormalmapTex = 0; }
        if (_waterDudvmapTex != 0)   { GlApi.Gl.DeleteTexture(_waterDudvmapTex);       _waterDudvmapTex = 0; }
        _reflLastTick = 0;
        _waterReady = false;
    }

    // ── Sky rendering ─────────────────────────────────────────────────────────────

    private void InitSky()
    {
        var vert = ShaderLoader.Load("sky.vert");
        var frag = ShaderLoader.Load("sky.frag");
        _skyShader = GlShader.Compile(vert, frag);
        _skyReady  = true;
    }

    /// <summary>
    /// Draws the sky background as a full-screen triangle into the currently-bound FBO.
    /// Must be called before any opaque geometry so it fills pixels not covered by terrain.
    /// </summary>
    private void DrawSky(ref Matrix4x4 view, ref Matrix4x4 proj, int w, int h)
    {
        if (_skyShader == null) return;

        var vp    = view * proj;
        Matrix4x4.Invert(vp, out var invVP);

        GlApi.Gl.DepthMask(false);
        GlApi.Gl.Disable(EnableCap.DepthTest);
        GlApi.Gl.Disable(EnableCap.CullFace);

        _skyShader.Use();
        _skyShader.Set("uInvViewProj",   ref invVP);
        _skyShader.Set("uBlueHorizon",   Sky.BlueHorizon);
        _skyShader.Set("uBlueDensity",   Sky.BlueDensity);
        _skyShader.Set("uHazeHorizon",   Sky.HazeHorizon);
        _skyShader.Set("uHazeDensity",   Sky.HazeDensity);
        _skyShader.Set("uSunlightColor", Sky.SunlightColor);
        _skyShader.Set("uAmbient",       Sky.Ambient);
        _skyShader.Set("uSunDirection",  Vector3.Normalize(Sky.SunDirection));
        _skyShader.Set("uSunGlowFocus",  Sky.SunGlowFocus);
        _skyShader.Set("uSunGlowSize",   Sky.SunGlowSize);

        GlApi.Gl.BindVertexArray(_quadVao);
        GlApi.Gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GlApi.Gl.BindVertexArray(0);

        _skyShader.Unuse();

        // Restore depth state for subsequent geometry passes
        GlApi.Gl.Enable(EnableCap.DepthTest);
        GlApi.Gl.DepthMask(true);
        GlApi.Gl.Enable(EnableCap.CullFace);
    }

    private void DeleteSkyResources()
    {
        _skyShader?.Dispose(); _skyShader = null;
        _skyReady = false;
    }

    // ── SSAO kernel and noise ─────────────────────────────────────────────────────

    /// <summary>
    /// Build a hemisphere sample kernel in tangent space.
    /// Samples are distributed with an accelerating bias toward the origin,
    /// matching the SL viewer's SSAO kernel generation.
    /// </summary>
    private static Vector3[] BuildSsaoKernel(int count)
    {
        var rng    = new Random(42); // deterministic seed
        var kernel = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            // Random direction in hemisphere (Z > 0).
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = (float)(rng.NextDouble() * 2.0 - 1.0);
            float z = (float)rng.NextDouble(); // [0,1] — upper hemisphere only
            var s = Vector3.Normalize(new Vector3(x, y, z));
            // Accelerating scale: more samples close to origin.
            float scale = (float)i / count;
            scale = 0.1f + scale * scale * 0.9f; // lerp(0.1, 1.0, scale²)
            kernel[i] = s * scale;
        }
        return kernel;
    }

    /// <summary>
    /// Build a 4×4 tiling random-rotation noise texture (RG8).
    /// Each texel stores a random 2D vector used to rotate the SSAO kernel
    /// in the tangent plane, breaking banding artefacts.
    /// </summary>
    private static unsafe uint BuildSsaoNoiseTex()
    {
        const int size = 4;
        var rng  = new Random(7);
        var data = new byte[size * size * 2]; // RG8
        for (int i = 0; i < size * size; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
            data[i * 2 + 0] = (byte)((MathF.Cos(angle) * 0.5f + 0.5f) * 255f);
            data[i * 2 + 1] = (byte)((MathF.Sin(angle) * 0.5f + 0.5f) * 255f);
        }
        uint tex = GlApi.Gl.GenTexture();
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, tex);
        fixed (byte* p = data)
            GlApi.Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.RG8,
                size, size, 0, PixelFormat.RG, PixelType.UnsignedByte, p);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GlApi.Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    private void BlitSceneToFb(int fb, int w, int h)
    {
        GlApi.Gl.Disable(EnableCap.ScissorTest);
        GlApi.Gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _sceneFbo);
        GlApi.Gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
        GlApi.Gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        GlApi.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
    }

    // ── Rendering helpers ────────────────────────────────────────────────────────

    private void UploadSubmission(PrimRenderSubmission sub)
    {
        // Before freeing the current draw lists, snapshot any textures that were
        // applied via PatchSubmissionTexture so they can be inherited by the
        // new submission.  This is the key fix for the "textures briefly appear
        // then go white" race: the streaming path returns faces with Texture=null
        // and patches them asynchronously; when the final submission is uploaded
        // those patches have already been applied to the old draw lists but the
        // new submission faces still carry null — without this snapshot the
        // textures would be lost when FreeGpuResources disposes the old lists.
        // Key: (PrimLocalId, FaceIndex, slot)  Value: GlTexture (ownership transferred)
        var inheritedTex = new Dictionary<(uint primId, int faceIdx, int slot), GlTexture>();
        static void Snapshot(
            List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex,
                  GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
            Dictionary<(uint, int, int), GlTexture> dict)
        {
            foreach (var (_, tex, normalTex, specTex, mrTex, emTex, face) in list)
            {
                if (tex       != null) dict.TryAdd((face.PrimLocalId, face.FaceIndex, 0), tex);
                if (normalTex != null) dict.TryAdd((face.PrimLocalId, face.FaceIndex, 1), normalTex);
                if (specTex   != null) dict.TryAdd((face.PrimLocalId, face.FaceIndex, 2), specTex);
                if (mrTex     != null) dict.TryAdd((face.PrimLocalId, face.FaceIndex, 3), mrTex);
                if (emTex     != null) dict.TryAdd((face.PrimLocalId, face.FaceIndex, 4), emTex);
            }
        }
        Snapshot(_opaque, inheritedTex);
        Snapshot(_alpha,  inheritedTex);

        // FreeGpuResources disposes meshes and textures and clears the draw lists.
        // We remove the inherited textures from the sets that FreeGpuResources would
        // dispose so we can hand them to the new draw lists instead.
        var inheritedSet = new HashSet<GlTexture>(inheritedTex.Values);
        FreeGpuResourcesExcept(inheritedSet);

        _lastBoundsMin = sub.BoundsMin;
        _lastBoundsMax = sub.BoundsMax;

        if (_frameAvatarFrontPending)
        {
            _frameAvatarFrontPending = false;
            _camera.FrameBoundsAvatarFront(sub.BoundsMin, sub.BoundsMax);
        }
        else if (_frameFrontPending)
        {
            _frameFrontPending = false;
            _camera.FrameBoundsFront(sub.BoundsMin, sub.BoundsMax);
        }
        // else: live re-tessellation — preserve current camera position.

        // Deduplicate textures: multiple faces may reference the same SKBitmap instance.
        // Upload each unique bitmap only once, then dispose all bitmaps together.
        var texCache    = new Dictionary<IntPtr, GlTexture>();
        var bmpToDispose = new HashSet<IntPtr>();

        GlTexture? TryUpload(SkiaSharp.SKBitmap? bmp)
        {
            if (bmp is null) return null;
            var handle = bmp.Handle;
            bmpToDispose.Add(handle);
            if (texCache.TryGetValue(handle, out var cached)) return cached;
            try
            {
                // Bitmaps embedded in PrimRenderFace are pre-processed by the builder
                // thread (RGBA8888 + vertical flip), so upload directly.
                var t = new GlTexture(bmp);
                texCache[handle] = t;
                return t;
            }
            catch { return null; }
        }

        // Per-submission mesh pool: identical non-flexi geometry shares a single GlMesh.
        // Halves GPU memory for linksets with copy-pasted faces and enables instanced draws.
        var meshPool = new Dictionary<ulong, GlMesh>(sub.Faces.Length);

        // VBO usage hint: submissions carrying skin/animesh data get their vertex buffers
        // rewritten every animation tick; everything else is static after upload.
        // Such submissions also must NOT deduplicate meshes: the dedup hash covers only
        // vertices+indices, not rigging, and per-face animation updates write into the
        // mesh VBO — a shared VBO would make one face's deformation stomp another's.
        bool subAnimated = sub.SkinData.Length > 0 || sub.AnimeshSkinData.Length > 0;

        foreach (var face in sub.Faces)
        {
            int vLen = face.VerticesLength > 0 ? face.VerticesLength : face.Vertices!.Length;
            GlMesh mesh;
            if (!face.IsFlexi && !subAnimated)
            {
                ulong h = VertexHash(face.Vertices!, vLen, face.Indices);
                if (!meshPool.TryGetValue(h, out mesh!))
                {
                    mesh = new GlMesh(face.Vertices!, vLen, face.Indices, dynamic: false);
                    meshPool[h] = mesh;
                }
            }
            else
            {
                mesh = new GlMesh(face.Vertices!, vLen, face.Indices, dynamic: true);
            }
            _faceMeshes.Add(mesh); // indexed by face position for animation updates
            // Extract compact CPU-side picker and normal/UV buffers, then release the full
            // interleaved float[] (12 floats/vertex) so the LOH array can be collected.
            // PickerVertices (3 floats/vertex) is used for ray–triangle intersection;
            // NormalUvVertices (5 floats/vertex: nx,ny,nz,u,v) is used by ComputeHitInfo.
            face.PickerVertices   = PickerFromInterleaved(face.Vertices, vLen);
            face.NormalUvVertices = NormalUvFromInterleaved(face.Vertices, vLen);
            // Deliberately NOT returned to ArrayPool even when pool-rented (VerticesLength > 0):
            // the upload cannot prove exclusive ownership of this buffer. Builder outputs alias
            // it from several places (AvatarFaceSkinData.BindVerts shares the face's vertex
            // array, background texture-stream tasks hold the pre-translation face list).
            // Returning a still-referenced buffer lets a concurrent renter — e.g. the avatar
            // animators, which rent from the same pool every tick — write into live vertex
            // data, uploading avatar-space verts into unrelated meshes (objects rendering as
            // corrupt geometry at the avatar's position). Dropping the reference and letting
            // the GC collect it is always safe; the pool just allocates fresh arrays.
            face.Vertices         = null; // release the full 12-wide LOH array
            // Fall back to an inherited texture from the previous draw lists so that
            // faces whose textures were already patched in don't go white on reload.
            var tex       = TryUpload(face.Texture)
                            ?? inheritedTex.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 0));
            var normalTex = TryUpload(face.NormalMapTexture)
                            ?? inheritedTex.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 1));
            var specTex   = TryUpload(face.SpecularMapTexture)
                            ?? inheritedTex.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 2));
            var mrTex     = TryUpload(face.MetallicRoughnessTexture)
                            ?? inheritedTex.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 3));
            var emTex     = TryUpload(face.EmissiveTexture)
                            ?? inheritedTex.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 4));

            if (face.HasAlpha)
                _alpha.Add((mesh, tex, normalTex, specTex, mrTex, emTex, face));
            else
                _opaque.Add((mesh, tex, normalTex, specTex, mrTex, emTex, face));
        }

        // Dispose all source bitmaps now that they are on the GPU (or failed).
        foreach (var face in sub.Faces)
        {
            if (face.Texture != null && bmpToDispose.Remove(face.Texture.Handle))
                face.Texture.Dispose();
            if (face.NormalMapTexture != null && bmpToDispose.Remove(face.NormalMapTexture.Handle))
                face.NormalMapTexture.Dispose();
            if (face.SpecularMapTexture != null && bmpToDispose.Remove(face.SpecularMapTexture.Handle))
                face.SpecularMapTexture.Dispose();
            if (face.MetallicRoughnessTexture != null && bmpToDispose.Remove(face.MetallicRoughnessTexture.Handle))
                face.MetallicRoughnessTexture.Dispose();
            if (face.EmissiveTexture != null && bmpToDispose.Remove(face.EmissiveTexture.Handle))
                face.EmissiveTexture.Dispose();
        }

        // Sort the opaque draw list by (mesh ref, tex ref) once at upload so identical-
        // geometry faces are consecutive for instanced batching. Order only changes when
        // geometry is re-uploaded, so DrawFaces never needs to re-sort per frame.
        // (_faceMeshes keeps submission order for animation updates; the pick map is
        // rebuilt from list order at pick time, so sorting here stays consistent.)
        if (_opaque.Count > 1)
            _opaque.Sort(static (a, b) => CompareBatchOrder(a, b));

        // Register flexi-prim GPU resources for compute deformation.
        if (_flexiDeformer != null)
        {
            foreach (var fp in sub.FlexiPrims)
            {
                var meshes = new GlMesh[fp.FaceCount];
                for (int fi = 0; fi < fp.FaceCount; fi++)
                    meshes[fi] = _faceMeshes[fp.FaceStart + fi];
                var gpuData = FlexiGpuData.Create(fp, meshes);
                _submissionFlexiGpu.Add(gpuData);
                fp.GpuData = gpuData;
            }
        }

        // Register avatar skin GPU resources for compute LBS.
        if (_skinDeformer != null)
        {
            foreach (var skin in sub.SkinData)
            {
                var gpuData = AvatarSkinGpuData.Create(skin, _faceMeshes[skin.FaceIndex]);
                _submissionSkinGpu.Add(gpuData);
                skin.GpuData = gpuData;
            }
        }

        // _pickMap and _cpuFaceData are rebuilt at pick time (inside the pick pass)
        // so they always reflect the current draw order, including any per-frame
        // back-to-front sort applied to alpha faces.
        _pickMap.Clear();
        _cpuFaceData.Clear();
    }

    private void FreeGpuResources() => FreeGpuResourcesExcept(null);

    private void FreeGpuResourcesExcept(HashSet<GlTexture>? preserve)
    {
        while (_pendingVertexUpdates.TryDequeue(out _)) { }
        foreach (var gd in _submissionFlexiGpu) gd.Dispose();
        _submissionFlexiGpu.Clear();
        foreach (var gd in _submissionSkinGpu) gd.Dispose();
        _submissionSkinGpu.Clear();
        _faceMeshes.Clear();

        // Collect unique GlTexture instances to avoid double-dispose when faces share a texture.
        var textures = new HashSet<GlTexture>();
        foreach (var (mesh, tex, normalTex, specTex, mrTex, emTex, _) in _opaque)
        {
            mesh.Dispose();
            if (tex != null) textures.Add(tex);
            if (normalTex != null) textures.Add(normalTex);
            if (specTex != null) textures.Add(specTex);
            if (mrTex != null) textures.Add(mrTex);
            if (emTex != null) textures.Add(emTex);
        }
        foreach (var (mesh, tex, normalTex, specTex, mrTex, emTex, _) in _alpha)
        {
            mesh.Dispose();
            if (tex != null) textures.Add(tex);
            if (normalTex != null) textures.Add(normalTex);
            if (specTex != null) textures.Add(specTex);
            if (mrTex != null) textures.Add(mrTex);
            if (emTex != null) textures.Add(emTex);
        }
        foreach (var tex in textures)
        {
            if (preserve == null || !preserve.Contains(tex))
                tex.Dispose();
        }
        _opaque.Clear();
        _alpha.Clear();
        _pickMap.Clear();

        // Discard any deferred submission patches — they targeted the old submission.
        foreach (var (patch, _) in _deferredSubmissionPatches) patch.Bitmap?.Dispose();
        _deferredSubmissionPatches.Clear();
        // Note: scene-object resources are freed separately by FreeSceneObjectResources.
    }

    // ── Scene-object layer GPU helpers ────────────────────────────────────────────

    // One increment per face slot that stores the texture. Null-safe no-op.
    private void SceneTexAddRef(GlTexture? tex)
    {
        if (tex == null) return;
        _sceneTexRefs.TryGetValue(tex, out int count);
        _sceneTexRefs[tex] = count + 1;
    }

    // One decrement per face slot released; disposes the texture when the last slot goes.
    // A texture missing from the table (defensive) is disposed immediately.
    private void SceneTexRelease(GlTexture? tex)
    {
        if (tex == null) return;
        if (!_sceneTexRefs.TryGetValue(tex, out int count) || count <= 1)
        {
            _sceneTexRefs.Remove(tex);
            tex.Dispose();
        }
        else
        {
            _sceneTexRefs[tex] = count - 1;
        }
    }

    private void FreeSceneObjectResources()
    {
        foreach (var gpuDatas in _flexiGpuDataMap.Values)
            foreach (var gd in gpuDatas) gd.Dispose();
        _flexiGpuDataMap.Clear();
        foreach (var gpuDatas in _skinGpuDataMap.Values)
            foreach (var gd in gpuDatas) gd.Dispose();
        _skinGpuDataMap.Clear();

        foreach (var faces in _sceneObjects.Values)
            foreach (var (mesh, _, _, _, _, _, _) in faces)
                mesh.Dispose();
        // Every scene texture is tracked in the refcount table; dispose each once.
        foreach (var tex in _sceneTexRefs.Keys) tex.Dispose();
        _sceneTexRefs.Clear();
        _sceneObjects.Clear();
        _sceneObjectTransformOverrides.Clear();
        _sceneObjectMotion.Clear();
        while (_pendingSceneVertexUpdates.TryDequeue(out _)) { }
        while (_pendingTransformOverrides.TryDequeue(out _)) { }
        // Discard all pending scene-object submissions — they belong to the old scene
        // and must not be uploaded to the new one (object local-IDs may be reused).
        foreach (var key in _pendingSceneObjects.Keys)
        {
            if (_pendingSceneObjects.TryRemove(key, out var sub) && sub != null)
                DisposePendingBitmaps(sub);
        }

        // Discard deferred and pending texture patches — they belong to the old scene
        // and must not be applied to new objects that may share the same local IDs.
        foreach (var (patch, _) in _deferredPatches) patch.Bitmap?.Dispose();
        _deferredPatches.Clear();
        int purged = 0;
        while (_pendingTexturePatches.TryDequeue(out var p)) { p.Bitmap?.Dispose(); purged++; }
        // Release all consumed gate permits so future PatchSceneObjectTexture calls are not starved.
        if (purged > 0) ReleasePatchGate(_texturePatchGate, purged);
        RebuildSceneFlatLists();
    }

    // No-rebuild variant used by the batched drain loop in GlRender; the caller invokes
    // RebuildSceneFlatLists once after processing the whole batch.
    // Texture disposal goes through the refcount table, so cost is proportional to this
    // object's faces (the previous implementation scanned every other object's faces to
    // detect sharing — O(total scene faces) per removal during interest-list churn).
    private void RemoveSceneObjectGpuNoRebuild(ulong rootId)
    {
        if (!_sceneObjects.TryGetValue(rootId, out var faces)) return;

        foreach (var (mesh, tex, normalTex, specTex, mrTex, emTex, _) in faces)
        {
            mesh.Dispose();
            SceneTexRelease(tex);
            SceneTexRelease(normalTex);
            SceneTexRelease(specTex);
            SceneTexRelease(mrTex);
            SceneTexRelease(emTex);
        }
        _sceneObjects.Remove(rootId);
        _sceneObjectTransformOverrides.TryRemove(rootId, out _);
        _sceneObjectMotion.TryRemove(rootId, out _);
        if (_flexiGpuDataMap.Remove(rootId, out var gpuDatas))
            foreach (var gd in gpuDatas) gd.Dispose();
        if (_skinGpuDataMap.Remove(rootId, out var skinGpuDatas))
            foreach (var gd in skinGpuDatas) gd.Dispose();
    }

    // Caller is responsible for calling RebuildSceneFlatLists once after a batch of uploads.
    private void UploadSceneObjectNoRebuild(ulong rootId, PrimRenderSubmission sub)
    {
        // Snapshot already-patched textures from the outgoing faces, keyed by
        // (PrimLocalId, FaceIndex, slot), so the replacement submission inherits them —
        // mirroring UploadSubmission's inheritedTex logic. This is what keeps an avatar's
        // baked textures across the placeholder→real-mesh replacement and across LOD
        // rebuilds: with the disk cache, bakes often arrive (and get patched) BEFORE the
        // real body upload lands, and were previously destroyed along with the placeholder,
        // leaving the avatar permanently grey. Each snapshot entry takes a +1 refcount hold
        // so the removal below cannot dispose it; all holds are released at the end
        // (a consumed entry keeps living via the +1 its new face slot added).
        Dictionary<(uint primId, int faceIdx, int slot), GlTexture>? inheritedTex = null;
        if (_sceneObjects.TryGetValue(rootId, out var outgoingFaces))
        {
            inheritedTex = new Dictionary<(uint, int, int), GlTexture>();
            foreach (var (_, oTex, oNorm, oSpec, oMr, oEm, oFace) in outgoingFaces)
            {
                SnapshotSlot(inheritedTex, oFace, 0, oTex);
                SnapshotSlot(inheritedTex, oFace, 1, oNorm);
                SnapshotSlot(inheritedTex, oFace, 2, oSpec);
                SnapshotSlot(inheritedTex, oFace, 3, oMr);
                SnapshotSlot(inheritedTex, oFace, 4, oEm);
            }
        }

        // Free existing GPU resources for this root before uploading the new ones.
        RemoveSceneObjectGpuNoRebuild(rootId);

        var faces = new List<(GlMesh, GlTexture?, GlTexture?, GlTexture?, GlTexture?, GlTexture?, PrimRenderFace)>(sub.Faces.Length);

        var texCache     = new Dictionary<IntPtr, GlTexture>();
        var bmpToDispose = new HashSet<IntPtr>();

        GlTexture? TryUpload(SkiaSharp.SKBitmap? bmp)
        {
            if (bmp is null) return null;
            var handle = bmp.Handle;
            bmpToDispose.Add(handle);
            if (texCache.TryGetValue(handle, out var cached)) return cached;
            try
            {
                // Bitmaps embedded in PrimRenderFace are pre-processed by the builder
                // thread (RGBA8888 + vertical flip), so upload directly.
                var t = new GlTexture(bmp);
                texCache[handle] = t;
                return t;
            }
            catch { return null; }
        }

        // Per-submission mesh pool: same dedup as UploadSubmission.
        var meshPool = new Dictionary<ulong, GlMesh>(sub.Faces.Length);

        // VBO usage hint: skinned/animesh scene objects (avatars) get per-tick vertex
        // rewrites; plain prims are static after upload.
        // Animated submissions must NOT deduplicate meshes — the dedup hash covers only
        // vertices+indices (not rigging), and per-face animation updates write into the
        // mesh VBO, so a shared VBO would make faces stomp each other's deformation.
        bool subAnimated = sub.SkinData.Length > 0 || sub.AnimeshSkinData.Length > 0;

        foreach (var face in sub.Faces)
        {
            int vLen = face.VerticesLength > 0 ? face.VerticesLength : face.Vertices!.Length;
            GlMesh mesh;
            if (!face.IsFlexi && !subAnimated)
            {
                ulong h = VertexHash(face.Vertices!, vLen, face.Indices);
                if (!meshPool.TryGetValue(h, out mesh!))
                {
                    mesh = new GlMesh(face.Vertices!, vLen, face.Indices, dynamic: false);
                    meshPool[h] = mesh;
                }
            }
            else
            {
                mesh = new GlMesh(face.Vertices!, vLen, face.Indices, dynamic: true);
            }
            // Extract compact CPU-side picker and normal/UV buffers, then release the full
            // interleaved float[] (12 floats/vertex) so the LOH array can be collected.
            face.PickerVertices   = PickerFromInterleaved(face.Vertices, vLen);
            face.NormalUvVertices = NormalUvFromInterleaved(face.Vertices, vLen);
            // NOT returned to ArrayPool — see the matching comment in UploadSubmission:
            // the buffer may still be referenced by skin data or background build tasks,
            // and returning it lets concurrent renters corrupt live vertex data.
            face.Vertices         = null; // release the full 12-wide LOH array
            // Slots with no embedded bitmap inherit the previous incarnation's patched
            // texture (progressive streaming submits faces textureless and patches later).
            var tex       = TryUpload(face.Texture)
                            ?? inheritedTex?.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 0));
            var normalTex = TryUpload(face.NormalMapTexture)
                            ?? inheritedTex?.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 1));
            var specTex   = TryUpload(face.SpecularMapTexture)
                            ?? inheritedTex?.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 2));
            var mrTex     = TryUpload(face.MetallicRoughnessTexture)
                            ?? inheritedTex?.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 3));
            var emTex     = TryUpload(face.EmissiveTexture)
                            ?? inheritedTex?.GetValueOrDefault((face.PrimLocalId, face.FaceIndex, 4));
            // One refcount per occupied slot — SceneTexRelease mirrors this on removal.
            SceneTexAddRef(tex);
            SceneTexAddRef(normalTex);
            SceneTexAddRef(specTex);
            SceneTexAddRef(mrTex);
            SceneTexAddRef(emTex);
            faces.Add((mesh, tex, normalTex, specTex, mrTex, emTex, face));
        }

        // Dispose all source bitmaps now that they are on the GPU.
        foreach (var face in sub.Faces)
        {
            if (face.Texture                  != null && bmpToDispose.Remove(face.Texture.Handle))                  face.Texture.Dispose();
            if (face.NormalMapTexture         != null && bmpToDispose.Remove(face.NormalMapTexture.Handle))         face.NormalMapTexture.Dispose();
            if (face.SpecularMapTexture       != null && bmpToDispose.Remove(face.SpecularMapTexture.Handle))       face.SpecularMapTexture.Dispose();
            if (face.MetallicRoughnessTexture != null && bmpToDispose.Remove(face.MetallicRoughnessTexture.Handle)) face.MetallicRoughnessTexture.Dispose();
            if (face.EmissiveTexture          != null && bmpToDispose.Remove(face.EmissiveTexture.Handle))          face.EmissiveTexture.Dispose();
        }

        _sceneObjects[rootId] = faces;

        // A transform override may have arrived while this object was still building
        // (terse update racing the mesh build). Apply the parked matrix now so the object
        // first appears at its current position rather than its build-time position.
        if (_sceneObjectTransformOverrides.TryRemove(rootId, out var parkedTransform))
            ApplyTransformToFaces(faces, parkedTransform);

        // Register flexi-prim GPU resources when compute deformation is available.
        if (_flexiDeformer != null && sub.FlexiPrims.Length > 0)
        {
            var gpuDatas = new FlexiGpuData[sub.FlexiPrims.Length];
            for (int pi = 0; pi < sub.FlexiPrims.Length; pi++)
            {
                var fp     = sub.FlexiPrims[pi];
                var meshes = new GlMesh[fp.FaceCount];
                for (int fi = 0; fi < fp.FaceCount; fi++)
                    meshes[fi] = faces[fp.FaceStart + fi].Item1;
                var gpuData = FlexiGpuData.Create(fp, meshes);
                gpuDatas[pi] = gpuData;
                fp.GpuData   = gpuData;
            }
            _flexiGpuDataMap[rootId] = gpuDatas;
        }

        // Register avatar skin GPU resources when compute LBS is available.
        if (_skinDeformer != null && sub.SkinData.Length > 0)
        {
            var skinGpuDatas = new AvatarSkinGpuData[sub.SkinData.Length];
            for (int si = 0; si < sub.SkinData.Length; si++)
            {
                var skin        = sub.SkinData[si];
                var gpuData     = AvatarSkinGpuData.Create(skin, faces[skin.FaceIndex].Item1);
                skinGpuDatas[si] = gpuData;
                skin.GpuData    = gpuData;
            }
            _skinGpuDataMap[rootId] = skinGpuDatas;
        }

        // Release the snapshot holds. Entries consumed by a new face slot stay alive via
        // the slot's own refcount; unconsumed entries (faces that no longer exist in the
        // new submission) drop to zero here and are disposed.
        if (inheritedTex != null)
            foreach (var held in inheritedTex.Values)
                SceneTexRelease(held);
    }

    // Adds one snapshot entry per (face, slot) with a +1 refcount hold, for texture
    // inheritance across a scene-object re-upload. TryAdd keeps the first texture seen
    // for a key (duplicate (PrimLocalId, FaceIndex) pairs should not occur within one object).
    private void SnapshotSlot(
        Dictionary<(uint primId, int faceIdx, int slot), GlTexture> snapshot,
        PrimRenderFace face, int slot, GlTexture? tex)
    {
        if (tex == null) return;
        if (snapshot.TryAdd((face.PrimLocalId, face.FaceIndex, slot), tex))
            SceneTexAddRef(tex);
    }

    // Batching order: group by mesh reference, then texture reference, so DrawFaces'
    // batch detection finds maximal runs of identical (mesh, texture) faces to instance.
    // Identity hash codes are stable for an object's lifetime, which is all a grouping
    // key needs (the relative order of groups is irrelevant).
    private static int CompareBatchOrder(
        (GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face) a,
        (GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face) b)
    {
        int mh = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.mesh)
                 .CompareTo(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b.mesh));
        if (mh != 0) return mh;
        int tha = a.tex == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.tex);
        int thb = b.tex == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b.tex);
        return tha.CompareTo(thb);
    }

    private void RebuildSceneFlatLists()
    {
        _sceneOpaque.Clear();
        _sceneAlpha.Clear();
        foreach (var faces in _sceneObjects.Values)
        {
            foreach (var entry in faces)
            {
                if (entry.face.HasAlpha)
                    _sceneAlpha.Add(entry);
                else
                    _sceneOpaque.Add(entry);
            }
        }

        // Sort the opaque flat list into batching order. This is what lets identical
        // trees/rocks from DIFFERENT linksets fall into one instanced draw — previously
        // only faces that happened to be adjacent in upload order ever batched. Runs only
        // on scene membership changes (batched per frame), never per rendered frame.
        // The alpha list is skipped: it is re-sorted back-to-front every frame anyway.
        if (_sceneOpaque.Count > 1)
            _sceneOpaque.Sort(static (a, b) => CompareBatchOrder(a, b));
    }

    /// <summary>
    /// Queue a vertex buffer update for a specific face, to be applied on the next GL frame.
    /// Safe to call from any thread. <paramref name="faceIndex"/> is the index into
    /// <see cref="PrimRenderSubmission.Faces"/> from the most recent submission.
    /// The heartbeat timer ensures the GL thread wakes up within ~33 ms without
    /// an extra UI-thread dispatch per update.
    /// </summary>
    public void ScheduleVertexUpdate(int faceIndex, float[] verts)
    {
        _pendingVertexUpdates.Enqueue((faceIndex, verts));
        // No UI-thread post needed: the 30 fps heartbeat will pick this up.
    }

    /// <summary>
    /// Copies <paramref name="verts"/> into a new array and enqueues it for upload.
    /// Allows the caller to use a pooled buffer and return it immediately after this call.
    /// </summary>
    public void ScheduleVertexUpdate(int faceIndex, ReadOnlySpan<float> verts)
    {
        var copy = verts.ToArray();
        _pendingVertexUpdates.Enqueue((faceIndex, copy));
        // No UI-thread post needed: the 30 fps heartbeat will pick this up.
    }

    /// <summary>
    /// Enqueues a model-matrix override for a scene object.
    /// On the next rendered frame the supplied matrix replaces <see cref="PrimRenderFace.Transform"/>
    /// for every face belonging to <paramref name="rootId"/>, allowing cheap position-only
    /// updates (e.g. walking avatars) without a full mesh re-upload.
    /// Safe to call from any thread.
    /// </summary>
    public void SetSceneObjectTransform(ulong sceneKey, Matrix4x4 transform)
    {
        _pendingTransformOverrides.Enqueue((sceneKey, transform));
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>
    /// Records the kinematic state (position, rotation, velocity, angular velocity,
    /// acceleration) reported by a terse update for a scene object's root, so the render loop
    /// can dead-reckon its transform forward every frame instead of only snapping to each
    /// (comparatively infrequent, throttled) terse-update packet. This is what keeps scripted
    /// and physical object motion looking continuous rather than teleporting.
    /// <para>
    /// When both <paramref name="velocity"/> and <paramref name="angularVelocity"/> are ~zero
    /// the object is treated as at rest: any previous motion tracking is cleared and the exact
    /// transform is applied once, same as <see cref="SetSceneObjectTransform"/>.
    /// </para>
    /// Safe to call from any thread.
    /// </summary>
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

        // Land the exact received pose immediately so the object doesn't wait a frame to
        // reflect the update; ExtrapolateMovingSceneObjects takes over from here for movers.
        var transform = Matrix4x4.CreateScale(scale)
                      * Matrix4x4.CreateFromQuaternion(rotation)
                      * Matrix4x4.CreateTranslation(position);
        SetSceneObjectTransform(sceneKey, transform);
    }

    /// <summary>
    /// Enqueues a single decoded texture bitmap to be uploaded and stitched into an
    /// already-live scene-object face on the next GL frame.  Called by
    /// <see cref="SceneObjectStreamer"/> as each texture download completes during
    /// progressive streaming.  Ownership of <paramref name="patch"/>'s bitmap
    /// transfers to the viewport; callers must not use or dispose it afterward.
    /// Safe to call from any thread.
    /// </summary>
    public void PatchSceneObjectTexture(SceneTexturePatch patch, CancellationToken ct = default)
    {
        // Back-pressure: block (without spinning) until the GL thread has drained a slot.
        // _texturePatchGate starts with 200 permits; each enqueue consumes one and each
        // dequeue in ApplyTexturePatches releases one, keeping the queue at ≤ 200 entries
        // without stalling the thread-pool via Thread.Sleep.
        // Passing ct means a cancelled build task unblocks immediately rather than
        // waiting for the GL thread to drain an available slot.
        // Ownership of patch.Bitmap transfers to the viewport only after Enqueue succeeds.
        // Dispose it here if we exit early so the caller is never responsible for cleanup.
        // NEVER block on the gate from the UI thread: ApplyTexturePatches — the only permit
        // producer — runs on that same thread, so a blocking Wait here can never be
        // satisfied and freezes the whole app. Reachable when a Progress<T> constructed
        // with the Avalonia SynchronizationContext delivers a patch callback to the
        // dispatcher. Hop to the pool and apply back-pressure there instead.
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            var deferred = patch;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try { PatchSceneObjectTexture(deferred, ct); }
                catch (OperationCanceledException) { /* bitmap already disposed inside */ }
            });
            return;
        }

        // Capture the gate into a local: GlDeinit swaps the field for a fresh instance,
        // and Wait/Release must operate on the same object.
        var gate = _texturePatchGate;
        try
        {
            gate.Wait(ct);
        }
        catch (OperationCanceledException)
        {
            // Permit was never acquired; bitmap was never transferred — dispose it now.
            patch.Bitmap?.Dispose();
            throw;
        }
        catch (ObjectDisposedException)
        {
            // The GL context was torn down between the field read and Wait (GlDeinit
            // disposed this gate instance). Treat as cancellation: dispose the bitmap and
            // silently exit — throwing here would propagate through Progress<T> onto the
            // thread pool and crash. The streamer will re-request the texture after the
            // SceneReset re-dirty cycle.
            patch.Bitmap?.Dispose();
            return;
        }

        // Preprocess the bitmap on this (background) thread so the GL thread only
        // performs the actual OpenGL upload — no Skia conversion/flip on the render loop.
        // If Preprocess or Enqueue throw, release the permit we just acquired so the
        // semaphore stays balanced (the GL drain will never see this entry).
        try
        {
            if (patch.Bitmap != null)
                patch = patch with { Bitmap = GlTexture.Preprocess(patch.Bitmap) };
            _pendingTexturePatches.Enqueue(patch);
        }
        catch
        {
            patch.Bitmap?.Dispose();
            ReleasePatchGate(gate);
            throw;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>
    /// Enqueues a texture patch for the single-submission viewer path (<see cref="Submit"/> /
    /// <see cref="SubmitAvatarFront"/>).  Patches are applied to the <c>_opaque</c> / <c>_alpha</c>
    /// draw lists rather than the scene-object lists used by <see cref="PatchSceneObjectTexture"/>.
    /// Ownership of <paramref name="patch"/>'s bitmap transfers to the viewport.
    /// Safe to call from any thread.
    /// </summary>
    public void PatchSubmissionTexture(SceneTexturePatch patch)
    {
        // Preprocess the bitmap on this (background) thread so the GL thread only
        // performs the actual OpenGL upload — no Skia conversion/flip on the render loop.
        if (patch.Bitmap != null)
            patch = patch with { Bitmap = GlTexture.Preprocess(patch.Bitmap) };
        _pendingSubmissionPatches.Enqueue(patch);
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }
    /// Safe to call from any thread.
    /// </summary>
    /// <param name="rootId">The root local-ID passed to <see cref="SubmitSceneObject"/>.</param>
    /// <param name="faceOffset">Zero-based index of the face within that object's face list.</param>
    /// <param name="verts">Vertex buffer (ownership transferred to viewport).</param>
    /// <param name="vertsLength">Number of valid floats in <paramref name="verts"/>.</param>
    /// <param name="isPoolRented">
    /// <c>true</c> if <paramref name="verts"/> was rented from <see cref="ArrayPool{T}.Shared"/>
    /// and must be returned after the GL upload (e.g. from <see cref="SceneAvatarAnimator"/>).
    /// <c>false</c> (default) if the buffer was allocated with <c>new[]</c> and must not be
    /// returned to the pool (e.g. from <see cref="FlexiPrimAnimator"/> which needs an exact-size
    /// buffer to avoid <c>GL_INVALID_VALUE</c> in <c>glBufferSubData</c>).
    /// </param>
    public void ScheduleSceneVertexUpdate(uint rootId, int faceOffset, float[] verts, int vertsLength, bool isPoolRented = false)
    {
        _pendingSceneVertexUpdates.Enqueue((rootId, faceOffset, verts, vertsLength, isPoolRented));
        // No UI-thread post needed: the 30 fps heartbeat will pick this up.
    }

    /// <summary>
    /// Queues a GPU compute-shader deformation job for a flexi prim.
    /// Thread-safe; called from the FlexiPrimAnimator physics background thread.
    /// No-op when compute deformation is not available.
    /// </summary>
    internal void ScheduleFlexiCompute(FlexiComputeJob job) => _flexiDeformer?.Enqueue(job);

    /// <summary>
    /// Enqueues an avatar skin compute job for the GL thread.
    /// Thread-safe; called from the SceneAvatarAnimator background thread.
    /// No-op when compute deformation is not available.
    /// </summary>
    internal void ScheduleSkinCompute(SkinComputeJob job) => _skinDeformer?.Enqueue(job);

    /// <summary>
    /// Drains <see cref="_pendingTransformOverrides"/> and patches
    /// <see cref="PrimRenderFace.Transform"/> on the faces of each dequeued root directly
    /// (the flat draw lists share the same face instances, so they pick the change up
    /// automatically). Faces retain the patched transform, so — unlike the previous
    /// implementation — nothing is re-applied per frame: cost is proportional to the
    /// number of overrides that actually arrived, not to total scene size.
    /// Overrides for roots whose upload has not landed yet are parked in
    /// <see cref="_sceneObjectTransformOverrides"/> and applied by
    /// <see cref="UploadSceneObjectNoRebuild"/> when the object appears.
    /// </summary>
    private void ApplySceneTransformOverrides()
    {
        while (_pendingTransformOverrides.TryDequeue(out var to))
        {
            if (_sceneObjects.TryGetValue(to.RootId, out var faces))
            {
                ApplyTransformToFaces(faces, to.Transform);
                // A fresher matrix supersedes any parked value for this root.
                _sceneObjectTransformOverrides.TryRemove(to.RootId, out _);
            }
            else
            {
                // Object still building/queued — park the latest matrix until upload.
                _sceneObjectTransformOverrides[to.RootId] = to.Transform;
            }
        }
    }

    /// <summary>
    /// Dead-reckons every scene object tracked in <see cref="_sceneObjectMotion"/> forward from
    /// its last terse update using the velocity/angular-velocity/acceleration the simulator
    /// reported, and writes the extrapolated pose straight into the faces' <see
    /// cref="PrimRenderFace.Transform"/>. Called once per rendered frame so continuous
    /// scripted/physical motion is smooth even though the sim only sends terse updates
    /// intermittently (throttled, priority-based). Objects come off the tracking list the
    /// moment a terse update reports them at rest (see <see cref="SetSceneObjectMotion"/>), so
    /// cost is proportional to the number of objects actually moving right now.
    /// </summary>
    private void ExtrapolateMovingSceneObjects()
    {
        if (_sceneObjectMotion.IsEmpty) return;

        long now = Environment.TickCount64;
        foreach (var (sceneKey, m) in _sceneObjectMotion)
        {
            // Still building — the parked override from SetSceneObjectMotion's immediate
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
        }
    }

    private static void ApplyTransformToFaces(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> faces,
        Matrix4x4 transform)
    {
        foreach (var entry in faces)
        {
            if (entry.face.IsFlexi) continue; // never stomp flexi (see PrimRenderFace.IsFlexi)
            entry.face.Transform = transform;
        }
    }

    /// <summary>
    /// Drains <see cref="_pendingTexturePatches"/> and for each patch uploads the bitmap
    /// to a new <see cref="GlTexture"/>, then replaces the corresponding slot in the
    /// relevant flat draw-list entry.  The old texture is disposed.  Called once per
    /// render frame on the GL thread.
    /// </summary>
    private void ApplyTexturePatches()
    {
        // Cap texture uploads per frame across BOTH deferred retries and new patches.
        // Each TexImage2D call can take ~0.5–2 ms; 20/frame ≈ 10–40 ms max.
        // Split into two independent halves rather than one shared counter: a single
        // shared budget let a large deferred backlog (built up during a scene-load
        // burst) exhaust the whole frame's budget on retries — which run first — before
        // the incoming-queue loop below ever got a look. That starved brand-new patches
        // of their (often successful, since their object may have *just* finished
        // uploading) first attempt, forcing them straight into the deferred list too and
        // compounding the backlog every frame instead of letting it drain.
        const int MaxDeferredPerFrame = 10;
        const int MaxIncomingPerFrame = 10;
        int budget = MaxDeferredPerFrame;

        // Retry deferred patches from previous frames first (they may have geometry now).
        if (_deferredPatches.Count > 0)
        {
            var stillDeferred = new List<(SceneTexturePatch patch, int retriesLeft)>(_deferredPatches.Count);
            foreach (var (patch, retriesLeft) in _deferredPatches)
            {
                if (budget <= 0)
                {
                    // Out of budget — keep all remaining deferred patches for next frame.
                    stillDeferred.Add((patch, retriesLeft));
                    continue;
                }
                // Consume a budget slot for this attempt whether or not it succeeds: a
                // miss still runs TryApplyTexturePatch's fallback scan over every scene
                // face (see below), so letting failures go unmetered turned this foreach
                // into an unbounded full-list, full-scene rescan every single frame once
                // the deferred backlog grew past a few thousand entries (observed
                // deferredPatches climbing past 5000 during a scene-load burst) — the
                // budget<=0 bailout above almost never tripped because successes, not
                // attempts, were what decremented it.
                budget--;
                if (TryApplyTexturePatch(patch))
                {
                    // applied — drop it, do not re-add to stillDeferred
                }
                else
                {
                    if (retriesLeft > 0)
                        stillDeferred.Add((patch, retriesLeft - 1));
                    else
                        patch.Bitmap?.Dispose();
                }
            }
            _deferredPatches.Clear();
            _deferredPatches.AddRange(stillDeferred);
        }

        // Drain the incoming queue every frame, releasing one gate permit per dequeued
        // patch, so permits keep flowing even when the apply budget is spent: producers
        // block on the gate, and during a scene-load burst the deferred backlog can
        // consume the entire budget for many consecutive frames — leaving patches in the
        // queue starved the gate and stalled every texture-streaming thread on Wait.
        // Bounded to TexturePatchQueueDepth (the gate's own capacity) per frame rather
        // than looping until the queue is momentarily empty: releasing a permit here can
        // immediately wake a waiting producer, which re-enqueues before this loop's next
        // TryDequeue check, so an unbounded "drain completely" loop can chase a fast
        // producer burst (e.g. mesh-cache hits during scene load) and hold the GL thread
        // for the whole burst instead of one frame — the exact freeze this queue depth
        // was meant to bound. Dequeuing is cheap; only TryApplyTexturePatch's GL upload
        // costs real time, and that still respects the budget — over-budget patches are
        // parked in _deferredPatches, where over-budget work lives anyway. Any remainder
        // left in the queue is picked up next frame via the re-request below.
        int drained = 0;
        int incomingBudget = MaxIncomingPerFrame;
        while (drained < TexturePatchQueueDepth && _pendingTexturePatches.TryDequeue(out var patch))
        {
            drained++;
            ReleasePatchGate(_texturePatchGate);  // a slot is now free; wake any waiting producer
            if (incomingBudget > 0 && TryApplyTexturePatch(patch))
            {
                incomingBudget--;
            }
            else
            {
                // Out of budget, or scene object not yet uploaded — defer and retry each
                // frame. The retry allowance is generous (~30 s at 30 Hz): with the disk
                // cache, textures often decode long before their object's mesh build
                // finishes (login bursts, avatar wearable fetches), and a dropped patch
                // leaves the face permanently untextured.
                _deferredPatches.Add((patch, 900));
            }
        }
        // If the queue still has patches, request another render tick to continue draining.
        if (!_pendingTexturePatches.IsEmpty || _deferredPatches.Count > 0)
            Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);

        // One or more faces were upgraded to the alpha pass after their texture decoded.
        // Rebuild the flat draw lists once so they move from _sceneOpaque to _sceneAlpha.
        if (_alphaReclassNeeded)
        {
            _alphaReclassNeeded = false;
            RebuildSceneFlatLists();
        }

        // Drain patches for the single-submission viewer path (_opaque / _alpha).
        while (_pendingSubmissionPatches.TryDequeue(out var subPatch))
            ApplySubmissionPatch(subPatch);

        // Retry deferred submission patches (arrived before UploadSubmission).
        for (int i = _deferredSubmissionPatches.Count - 1; i >= 0; i--)
        {
            var (patch, retries) = _deferredSubmissionPatches[i];
            if (ApplySubmissionPatchIfReady(patch))
            {
                _deferredSubmissionPatches.RemoveAt(i);
            }
            else if (retries <= 1)
            {
                patch.Bitmap?.Dispose();
                _deferredSubmissionPatches.RemoveAt(i);
            }
            else
            {
                _deferredSubmissionPatches[i] = (patch, retries - 1);
                Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
            }
        }
    }

    /// <summary>
    /// Applies a texture patch to the <c>_opaque</c> / <c>_alpha</c> draw lists used
    /// by the single-submission viewers (AvatarViewer, ObjectViewer).
    /// If the target faces are not yet uploaded (patch arrived before UploadSubmission),
    /// the patch is deferred and retried on subsequent frames.
    /// </summary>
    private void ApplySubmissionPatch(SceneTexturePatch patch)
    {
        if (patch.Bitmap == null) return;

        // If geometry is already uploaded, apply immediately.
        if (_opaque.Count > 0 || _alpha.Count > 0)
        {
            if (ApplySubmissionPatchIfReady(patch)) return;
        }

        // Geometry not yet uploaded — defer for up to ~900 frames (~30 s at 30 Hz);
        // cached textures can decode long before the avatar/object mesh build finishes.
        _deferredSubmissionPatches.Add((patch, 900));
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>
    /// Tries to apply a submission patch immediately. Returns <c>true</c> if the face
    /// was found (and the bitmap was consumed), <c>false</c> if the face is not yet present.
    /// </summary>
    private bool ApplySubmissionPatchIfReady(SceneTexturePatch patch)
    {
        // Check whether any face with the matching PrimLocalId + FaceIndex exists.
        bool found = false;
        for (int i = 0; i < _opaque.Count; i++)
        {
            var f = _opaque[i].face;
            if (f.PrimLocalId == patch.RootLocalId && f.FaceIndex == patch.FaceIndex)
            { found = true; break; }
        }
        if (!found)
        {
            for (int i = 0; i < _alpha.Count; i++)
            {
                var f = _alpha[i].face;
                if (f.PrimLocalId == patch.RootLocalId && f.FaceIndex == patch.FaceIndex)
                { found = true; break; }
            }
        }
        if (!found) return false;

        if (patch.Bitmap == null) return true;
        GlTexture? newTex = null;
        try { newTex = new GlTexture(patch.Bitmap); }
        catch { patch.Bitmap.Dispose(); return true; }
        patch.Bitmap.Dispose();

        var oldTex = ApplyPatchToList(_opaque, patch, newTex, out _)
                  ?? ApplyPatchToList(_alpha,  patch, newTex, out _);
        if (oldTex != null && oldTex != newTex)
            oldTex.Dispose();
        return true;
    }

    /// <summary>
    /// Attempts to apply a single texture patch to the matching scene-object face.
    /// Returns <c>true</c> if the patch was consumed (applied or bitmap was null),
    /// <c>false</c> if the target scene object is not yet present and the patch should be deferred.
    /// </summary>
    private bool TryApplyTexturePatch(SceneTexturePatch patch)
    {
        if (patch.Bitmap == null) return true;

        // Resolve the face-tuple list for this patch.
        // Direct lookup works for prims; avatars are keyed by AvatarKeyOffset|localId
        // but emit patches with the raw avatar localId (body) or an attachment prim
        // localId (attachments), so fall back to scanning all faces in every entry.
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)>? faceTuples = null;
        ulong lookupKey = patch.SceneKey != 0 ? patch.SceneKey : (ulong)patch.RootLocalId;
        if (!_sceneObjects.TryGetValue(lookupKey, out faceTuples))
        {
            foreach (var kv in _sceneObjects)
            {
                // Check all faces — not just face[0] — because avatar scene objects
                // combine body faces (PrimLocalId = avatarLocalId) and attachment faces
                // (PrimLocalId = attachmentPrimLocalId) in the same list.
                for (int fi = 0; fi < kv.Value.Count; fi++)
                {
                    if (kv.Value[fi].face.PrimLocalId == patch.RootLocalId)
                    {
                        faceTuples = kv.Value;
                        break;
                    }
                }
                if (faceTuples != null) break;
            }
        }

        // If the face list is found but face 0 is the placeholder (only 1 face and
        // FaceIndex 0 is an ellipsoid), the real submission hasn't replaced it yet.
        // Defer until the real body-mesh submission arrives (more than 1 face).
        if (faceTuples != null && faceTuples.Count == 1
            && patch.FaceIndex > 0)
        {
            // Placeholder has 1 face; real avatar body has several — not yet uploaded.
            return false;
        }

        if (faceTuples == null) return false;

        GlTexture? newTex = null;
        try { newTex = new GlTexture(patch.Bitmap); }
        catch { patch.Bitmap.Dispose(); return true; }
        patch.Bitmap.Dispose();

        // Update the flat draw lists (same face-slot instances as _sceneObjects entries).
        // The face lives in at most one of the two lists; the other call returns null.
        ApplyPatchToList(_sceneOpaque, patch, newTex, out bool foundInOpaque);
        ApplyPatchToList(_sceneAlpha,  patch, newTex, out bool foundInAlpha);
        bool flatUpdated = foundInOpaque || foundInAlpha;

        // Mirror the texture into _sceneObjects so re-flattening picks it up.
        // Search by PrimLocalId + FaceIndex rather than using patch.FaceIndex as a
        // direct index: attachment FaceIndex values are local to the attachment's own
        // face list, not the combined global index in the avatar+attachment submission.
        // The object list is the source of truth for texture refcounts, so the displaced
        // texture is captured here rather than from the flat-list update above.
        bool       applied  = false;
        GlTexture? displaced = null;
        for (int fi = 0; fi < faceTuples.Count; fi++)
        {
            var (mesh, tex, normalTex, specTex, mrTex, emTex, face) = faceTuples[fi];
            if (face.PrimLocalId != patch.RootLocalId || face.FaceIndex != patch.FaceIndex) continue;
            displaced = patch.Slot switch
            {
                TextureSlot.Albedo            => tex,
                TextureSlot.Normal            => normalTex,
                TextureSlot.Specular          => specTex,
                TextureSlot.MetallicRoughness => mrTex,
                TextureSlot.Emissive          => emTex,
                _                             => null,
            };
            var (updTex, updNorm, updSpec, updMr, updEm) =
                ReplacedSlot(patch.Slot, newTex, tex, normalTex, specTex, mrTex, emTex);
            faceTuples[fi] = (mesh, updTex, updNorm, updSpec, updMr, updEm, face);
            applied = true;

            // A legacy face whose alpha was inferred from face colour alone (AlphaAuto) renders
            // opaque until we learn its albedo texture is transparent. Upgrade it to the alpha
            // pass now. The flat draw lists bucket by HasAlpha, so flag a rebuild (batched once
            // per frame in ApplyTexturePatches) to move the face from the opaque to the alpha list.
            if (patch.Slot == TextureSlot.Albedo && patch.TextureHasAlpha
                && face.AlphaAuto && face.AlphaMode == FaceAlphaMode.None)
            {
                face.AlphaMode = FaceAlphaMode.Blend;
                face.HasAlpha  = true;
                _alphaReclassNeeded = true;
            }
            break;
        }

        if (applied)
        {
            // Transfer the slot's refcount from the displaced texture to the new one.
            // The displaced texture is only disposed when no other face slot still uses
            // it (initial texture uploads can be shared across faces via the per-upload
            // bitmap dedup cache). Guard against a degenerate same-instance re-apply.
            if (!ReferenceEquals(displaced, newTex))
            {
                SceneTexAddRef(newTex);
                SceneTexRelease(displaced);
            }
        }
        else if (flatUpdated)
        {
            // Degenerate: a flat-list entry (possibly from another object sharing the
            // same PrimLocalId+FaceIndex) took the texture but the resolved object list
            // didn't. Track the texture so scene teardown disposes it, but do NOT release
            // the displaced flat-list texture — the object list still references it and
            // the next RebuildSceneFlatLists restores it to the draw lists.
            SceneTexAddRef(newTex);
        }
        else
        {
            // Face vanished between the lookup above and here (shouldn't happen) —
            // the new texture was installed nowhere that removal tracks; drop it.
            newTex.Dispose();
        }

        return true;
    }

    // Returns the GlTexture that was displaced from the slot, or null if the face was
    // not found or the slot was already null. <paramref name="found"/> distinguishes the
    // two: true when a face matched and the slot was replaced (even if it held null).
    // Texture lifetime is managed by the caller (refcounts for the scene path, direct
    // disposal for the single-submission path).
    private static GlTexture? ApplyPatchToList(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        SceneTexturePatch patch,
        GlTexture newTex,
        out bool found)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var (mesh, tex, normalTex, specTex, mrTex, emTex, face) = list[i];
            if (face.PrimLocalId != patch.RootLocalId || face.FaceIndex != patch.FaceIndex) continue;

            var (updTex, updNorm, updSpec, updMr, updEm) =
                ReplacedSlot(patch.Slot, newTex, tex, normalTex, specTex, mrTex, emTex);
            list[i] = (mesh, updTex, updNorm, updSpec, updMr, updEm, face);
            found = true;
            // Return the displaced texture so the caller can manage its lifetime.
            return patch.Slot switch
            {
                TextureSlot.Albedo            => tex,
                TextureSlot.Normal            => normalTex,
                TextureSlot.Specular          => specTex,
                TextureSlot.MetallicRoughness => mrTex,
                TextureSlot.Emissive          => emTex,
                _                             => null,
            };
        }
        found = false;
        return null;
    }

    private static (GlTexture? tex, GlTexture? norm, GlTexture? spec, GlTexture? mr, GlTexture? em)
        ReplacedSlot(TextureSlot slot, GlTexture newTex,
            GlTexture? tex, GlTexture? norm, GlTexture? spec, GlTexture? mr, GlTexture? em)
        => slot switch
        {
            TextureSlot.Albedo            => (newTex, norm, spec, mr,     em),
            TextureSlot.Normal            => (tex,    newTex, spec, mr,   em),
            TextureSlot.Specular          => (tex,    norm, newTex, mr,   em),
            TextureSlot.MetallicRoughness => (tex,    norm, spec,  newTex, em),
            TextureSlot.Emissive          => (tex,    norm, spec,  mr,    newTex),
            _                             => (tex,    norm, spec,  mr,    em),
        };

    /// <summary>
    /// Appends the faces of <paramref name="src"/> that intersect <paramref name="frustum"/>
    /// to <see cref="_mergedAlpha"/>, recording culled faces in the frame stats. Flexi faces
    /// bypass the test for the same reason as in <see cref="DrawFaces"/> (stale bind-pose AABB).
    /// Faces that survive are counted by DrawFaces itself, so totals stay consistent.
    /// </summary>
    private void AppendVisibleAlpha(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> src,
        Frustum? frustum)
    {
        if (!frustum.HasValue)
        {
            _mergedAlpha.AddRange(src);
            return;
        }
        var f = frustum.Value;
        for (int i = 0; i < src.Count; i++)
        {
            var face = src[i].face;
            if (!face.IsFlexi)
            {
                face.GetWorldAabb(out var amin, out var amax);
                if (!FrustumCuller.IntersectsAabb(f, amin, amax))
                {
                    _stats.RecordFaceConsidered();
                    _stats.RecordFaceCulled();
                    continue;
                }
            }
            _mergedAlpha.Add(src[i]);
        }
    }

    /// <summary>
    /// Pre-resolved uniform locations for the prim shader's hot draw loop.
    /// <see cref="GlShader.Set(string, int)"/> costs a string-keyed dictionary lookup per
    /// call; DrawFaces writes ~25 uniforms per face per frame, so the lookups alone were
    /// measurable in dense scenes. Locations are resolved once per compiled shader
    /// (-1 entries — uniforms optimised out by the driver — are no-ops in the int Set overloads).
    /// </summary>
    private sealed class PrimShaderLocations
    {
        public readonly int SsaoMap, HasSsao, ScreenSize, SunDir, SunColor, AmbientColor, Instanced;
        public readonly int Mvp, ModelView, NormalMat, Color, Fullbright, Glow, AlphaCutoff, Shiny, HasBump, AlphaMode;
        public readonly int HasTexture, Albedo, IsPBR, HasMaterial;
        public readonly int BaseColorFactor, MetallicFactor, RoughnessFactor, EmissiveFactor, BaseColorUvST, BaseColorUvRot;
        public readonly int HasNormalMap, NormalMap, PbrNormalUvST, PbrNormalUvRot;
        public readonly int HasMRMap, MetallicRoughnessMap, MRUvST, MRUvRot;
        public readonly int HasEmissiveMap, EmissiveMap, EmissiveUvST, EmissiveUvRot;
        public readonly int NormalUvST, NormalUvRot, HasSpecularMap, SpecularMap, SpecUvST, SpecUvRot, SpecColor, SpecExp, EnvIntensity;

        public PrimShaderLocations(GlShader s)
        {
            SsaoMap              = s.GetLocation("uSsaoMap");
            HasSsao              = s.GetLocation("uHasSsao");
            ScreenSize           = s.GetLocation("uScreenSize");
            SunDir               = s.GetLocation("uSunDir");
            SunColor             = s.GetLocation("uSunColor");
            AmbientColor         = s.GetLocation("uAmbientColor");
            Instanced            = s.GetLocation("uInstanced");
            Mvp                  = s.GetLocation("uMvp");
            ModelView            = s.GetLocation("uModelView");
            NormalMat            = s.GetLocation("uNormalMat");
            Color                = s.GetLocation("uColor");
            Fullbright           = s.GetLocation("uFullbright");
            Glow                 = s.GetLocation("uGlow");
            AlphaCutoff          = s.GetLocation("uAlphaCutoff");
            Shiny                = s.GetLocation("uShiny");
            HasBump              = s.GetLocation("uHasBump");
            AlphaMode            = s.GetLocation("uAlphaMode");
            HasTexture           = s.GetLocation("uHasTexture");
            Albedo               = s.GetLocation("uAlbedo");
            IsPBR                = s.GetLocation("uIsPBR");
            HasMaterial          = s.GetLocation("uHasMaterial");
            BaseColorFactor      = s.GetLocation("uBaseColorFactor");
            MetallicFactor       = s.GetLocation("uMetallicFactor");
            RoughnessFactor      = s.GetLocation("uRoughnessFactor");
            EmissiveFactor       = s.GetLocation("uEmissiveFactor");
            BaseColorUvST        = s.GetLocation("uBaseColorUvST");
            BaseColorUvRot       = s.GetLocation("uBaseColorUvRot");
            HasNormalMap         = s.GetLocation("uHasNormalMap");
            NormalMap            = s.GetLocation("uNormalMap");
            PbrNormalUvST        = s.GetLocation("uPbrNormalUvST");
            PbrNormalUvRot       = s.GetLocation("uPbrNormalUvRot");
            HasMRMap             = s.GetLocation("uHasMRMap");
            MetallicRoughnessMap = s.GetLocation("uMetallicRoughnessMap");
            MRUvST               = s.GetLocation("uMRUvST");
            MRUvRot              = s.GetLocation("uMRUvRot");
            HasEmissiveMap       = s.GetLocation("uHasEmissiveMap");
            EmissiveMap          = s.GetLocation("uEmissiveMap");
            EmissiveUvST         = s.GetLocation("uEmissiveUvST");
            EmissiveUvRot        = s.GetLocation("uEmissiveUvRot");
            NormalUvST           = s.GetLocation("uNormalUvST");
            NormalUvRot          = s.GetLocation("uNormalUvRot");
            HasSpecularMap       = s.GetLocation("uHasSpecularMap");
            SpecularMap          = s.GetLocation("uSpecularMap");
            SpecUvST             = s.GetLocation("uSpecUvST");
            SpecUvRot            = s.GetLocation("uSpecUvRot");
            SpecColor            = s.GetLocation("uSpecColor");
            SpecExp              = s.GetLocation("uSpecExp");
            EnvIntensity         = s.GetLocation("uEnvIntensity");
        }
    }

    // Lazy per-shader cache: rebuilt only when the shader instance changes (GL re-init).
    private GlShader?            _primLocsShader;
    private PrimShaderLocations? _primLocs;

    private void DrawFaces(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4x4 view,
        ref Matrix4x4 proj,
        bool manageCulling = true,
        uint ssaoTex = 0,
        Vector2 screenSize = default,
        Frustum? frustum = null,
        FrameStatsTracker? stats = null,
        SkySettings? sky = null,
        bool enableInstancing = false)
    {
        shader.Use();

        if (!ReferenceEquals(_primLocsShader, shader))
        {
            _primLocs       = new PrimShaderLocations(shader);
            _primLocsShader = shader;
        }
        var L = _primLocs!;

        // Bind SSAO blur result to texture unit 4 (units 0–3 used by material textures).
        bool hasSsao = ssaoTex != 0 && screenSize != default;
        if (hasSsao)
        {
            GlApi.Gl.ActiveTexture(TextureUnit.Texture4);
            GlApi.Gl.BindTexture(TextureTarget.Texture2D, ssaoTex);
            shader.Set(L.SsaoMap,    4);
            shader.Set(L.HasSsao,    1);
            shader.Set(L.ScreenSize, screenSize);
        }
        else
        {
            shader.Set(L.HasSsao, 0);
        }

        // Set EEP sun/ambient uniforms — constant for all faces in this draw call.
        // uSunDir is transformed from world space into view space so the lighting
        // computation in prim.frag uses the correct coordinate frame.
        {
            SkySettings s  = sky ?? SkySettings.Default;
            var worldSun   = s.SunDirection;
            var sunViewDir = new Vector3(
                Vector3.Dot(new Vector3(view.M11, view.M12, view.M13), worldSun),
                Vector3.Dot(new Vector3(view.M21, view.M22, view.M23), worldSun),
                Vector3.Dot(new Vector3(view.M31, view.M32, view.M33), worldSun));
            shader.Set(L.SunDir,       sunViewDir);
            shader.Set(L.SunColor,     s.SunlightColor);
            shader.Set(L.AmbientColor, s.Ambient);
        }

        // View inverse (upper-3×3) is constant for every face this frame. The per-face
        // normal matrix (MV⁻¹)ᵀ = (V⁻¹)₃ × (M⁻¹)₃, so combining this with the face's cached
        // model inverse avoids a full 4×4 Matrix4x4.Invert per face per frame (see
        // PrimRenderFace.ModelInverse3).
        Matrix4x4.Invert(view, out var viewInvFull);
        var viewInv3 = new Matrix3x3(new Vector3(viewInvFull.M11, viewInvFull.M12, viewInvFull.M13), new Vector3(viewInvFull.M21, viewInvFull.M22, viewInvFull.M23), new Vector3(viewInvFull.M31, viewInvFull.M32, viewInvFull.M33));

        bool canBatch = enableInstancing && _instanceDrawer != null;

        // Note: draw lists are pre-sorted by (mesh ref, tex ref) where batching matters —
        // _opaque at upload time (UploadSubmission) and _sceneOpaque at rebuild time
        // (RebuildSceneFlatLists) — so identical-geometry faces are already consecutive.
        // Sorting per frame here would be O(N log N) of wasted work: list order only
        // changes when geometry is (re)uploaded.

        // Signal the vertex shader which draw path is active. Set to false once up-front;
        // toggled to true only around instanced batches, then restored.
        shader.Set(L.Instanced, false);

        bool cullActive = true;
        int _i = 0;
        while (_i < list.Count)
        {
            var (mesh, tex, normalTex, specTex, mrTex, emTex, face) = list[_i];

            // ── Instanced batch detection ─────────────────────────────────
            // Batch condition: non-PBR, non-material, non-flexi, same mesh+texture+IsTwoSided.
            // UV transforms for plain TE faces are baked into vertex UVs so per-instance
            // data is just the transform matrices + color + misc scalars.
            if (canBatch && !face.IsFlexi && !face.IsPBR && !face.HasMaterial)
            {
                int batchEnd = _i + 1;
                while (batchEnd < list.Count)
                {
                    var (bMesh, bTex, _, _, _, _, bFace) = list[batchEnd];
                    if (!ReferenceEquals(bMesh, mesh) || !ReferenceEquals(bTex, tex) ||
                        bFace.IsFlexi || bFace.IsPBR || bFace.HasMaterial ||
                        bFace.IsTwoSided != face.IsTwoSided)
                        break;
                    batchEnd++;
                }

                int batchCount = batchEnd - _i;
                if (batchCount >= 2)
                {
                    // Set cull state for the whole batch.
                    if (manageCulling)
                    {
                        if (face.IsTwoSided && cullActive)     { GlApi.Gl.Disable(EnableCap.CullFace); cullActive = false; }
                        else if (!face.IsTwoSided && !cullActive) { GlApi.Gl.Enable(EnableCap.CullFace);  cullActive = true;  }
                    }

                    // Set shared batch uniforms (texture + material-class flags).
                    bool hasTex0 = tex != null;
                    shader.Set(L.HasTexture,  hasTex0);
                    if (hasTex0) { tex!.Bind(0); shader.Set(L.Albedo, 0); }
                    shader.Set(L.IsPBR,       false);
                    shader.Set(L.HasMaterial, false);
                    shader.Set(L.HasBump,     false);

                    // Grow the per-instance data buffer if needed.
                    int needed = batchCount * GlInstanceDrawer.InstanceFloats;
                    if (needed > _instanceDataBuf.Length)
                        _instanceDataBuf = new float[needed * 2];

                    // Build per-instance data with individual frustum culling.
                    shader.Set(L.Instanced, true);
                    int written = 0;
                    for (int j = _i; j < batchEnd; j++)
                    {
                        var (_, _, _, _, _, _, jFace) = list[j];
                        stats?.RecordFaceConsidered();
                        if (frustum.HasValue)
                        {
                            jFace.GetWorldAabb(out var jMin, out var jMax);
                            if (!FrustumCuller.IntersectsAabb(frustum.Value, jMin, jMax))
                            { stats?.RecordFaceCulled(); continue; }
                        }
                        WriteInstanceData(_instanceDataBuf, written, jFace, ref view, ref proj);
                        written++;
                        stats?.RecordDraw(mesh.IndexCount);
                    }

                    if (written > 0)
                        _instanceDrawer!.DrawInstanced(mesh, _instanceDataBuf, written);

                    shader.Set(L.Instanced, false);
                    _i = batchEnd;
                    continue;
                }
            }

            // Frustum cull using the face's world-space AABB. Faces with no geometry
            // (empty vertex buffer) fall through with a zero-extent AABB at the face origin,
            // which the positive-vertex test handles correctly.
            stats?.RecordFaceConsidered();
            // Flexi faces are deformed every tick by FlexiPrimAnimator and end up in world
            // space, but their cached local AABB and Transform reflect the bind pose / prim-local
            // frame (Identity for avatar attachments). Frustum culling against that stale AABB
            // would always reject them. Skip the cull for flexi faces — they are small and rare.
            if (frustum.HasValue && !face.IsFlexi)
            {
                face.GetWorldAabb(out var amin, out var amax);
                if (!FrustumCuller.IntersectsAabb(frustum.Value, amin, amax))
                {
                    stats?.RecordFaceCulled();
                    _i++;
                    continue;
                }
            }

            if (manageCulling)
            {
                if (face.IsTwoSided && cullActive)
                {
                    GlApi.Gl.Disable(EnableCap.CullFace);
                    cullActive = false;
                }
                else if (!face.IsTwoSided && !cullActive)
                {
                    GlApi.Gl.Enable(EnableCap.CullFace);
                    cullActive = true;
                }
            }

            Matrix4x4 model = face.Transform;
            var mv  = model * view;
            var mvp = mv * proj;

            // Normal matrix = (MV^-1)^T. Matrix3x3 is row-major, so sending with transpose:true
            // makes GL swap rows and columns before loading, giving GLSL the correct (MV^-1)^T
            // transform. Without the transpose, normals end up in world space rather
            // than view space, breaking lighting as the camera orbits.
            //
            // upper3x3((MV)^-1) = (V^-1)₃ × (M^-1)₃, so reuse the per-frame view inverse and
            // the face's cached model inverse instead of inverting the full MV every face.
            var normalMat = viewInv3 * face.ModelInverse3;

            shader.Set(L.Mvp,       ref mvp);
            shader.Set(L.ModelView, ref mv);
            shader.Set(L.NormalMat, ref normalMat, transpose: true);
            shader.Set(L.Color,     face.Color);
            shader.Set(L.Fullbright, face.Fullbright);
            shader.Set(L.Glow,      face.Glow);
            shader.Set(L.AlphaCutoff, face.AlphaCutoff);
            shader.Set(L.Shiny,     face.Shiny);
            shader.Set(L.HasBump,   face.HasBump);
            shader.Set(L.AlphaMode, (int)face.AlphaMode);

            bool hasTex = tex != null;
            shader.Set(L.HasTexture, hasTex);
            if (hasTex)
            {
                tex!.Bind(0);
                shader.Set(L.Albedo, 0);
            }

            // ── PBR path ─────────────────────────────────────────────────
            shader.Set(L.IsPBR, face.IsPBR);
            if (face.IsPBR)
            {
                shader.Set(L.BaseColorFactor,  face.BaseColorFactor);
                shader.Set(L.MetallicFactor,   face.MetallicFactor);
                shader.Set(L.RoughnessFactor,  face.RoughnessFactor);
                shader.Set(L.EmissiveFactor,   face.EmissiveFactor);

                shader.Set(L.BaseColorUvST,  new Vector4(
                    face.BaseColorUvXform.ScaleX, face.BaseColorUvXform.ScaleY,
                    face.BaseColorUvXform.OffsetX, face.BaseColorUvXform.OffsetY));
                shader.Set(L.BaseColorUvRot, face.BaseColorUvXform.Rotation);

                bool hasNorm = normalTex != null;
                shader.Set(L.HasNormalMap, hasNorm);
                if (hasNorm)
                {
                    normalTex!.Bind(1);
                    shader.Set(L.NormalMap, 1);
                }
                shader.Set(L.PbrNormalUvST,  new Vector4(
                    face.PbrNormalUvXform.ScaleX, face.PbrNormalUvXform.ScaleY,
                    face.PbrNormalUvXform.OffsetX, face.PbrNormalUvXform.OffsetY));
                shader.Set(L.PbrNormalUvRot, face.PbrNormalUvXform.Rotation);

                bool hasMR = mrTex != null;
                shader.Set(L.HasMRMap, hasMR);
                if (hasMR)
                {
                    mrTex!.Bind(2);
                    shader.Set(L.MetallicRoughnessMap, 2);
                }
                shader.Set(L.MRUvST,  new Vector4(
                    face.MetallicRoughnessUvXform.ScaleX, face.MetallicRoughnessUvXform.ScaleY,
                    face.MetallicRoughnessUvXform.OffsetX, face.MetallicRoughnessUvXform.OffsetY));
                shader.Set(L.MRUvRot, face.MetallicRoughnessUvXform.Rotation);

                bool hasEm = emTex != null;
                shader.Set(L.HasEmissiveMap, hasEm);
                if (hasEm)
                {
                    emTex!.Bind(3);
                    shader.Set(L.EmissiveMap, 3);
                }
                shader.Set(L.EmissiveUvST,  new Vector4(
                    face.EmissiveUvXform.ScaleX, face.EmissiveUvXform.ScaleY,
                    face.EmissiveUvXform.OffsetX, face.EmissiveUvXform.OffsetY));
                shader.Set(L.EmissiveUvRot, face.EmissiveUvXform.Rotation);
            }
            else
            {
                // ── Legacy material path ──────────────────────────────────
                shader.Set(L.HasMaterial, face.HasMaterial);

                bool hasNorm = normalTex != null;
                shader.Set(L.HasNormalMap, hasNorm);
                if (hasNorm)
                {
                    normalTex!.Bind(1);
                    shader.Set(L.NormalMap, 1);
                }
                shader.Set(L.NormalUvST, new Vector4(
                    face.NormalUvXform.ScaleX, face.NormalUvXform.ScaleY,
                    face.NormalUvXform.OffsetX, face.NormalUvXform.OffsetY));
                shader.Set(L.NormalUvRot, face.NormalUvXform.Rotation);

                bool hasSpec = specTex != null;
                shader.Set(L.HasSpecularMap, hasSpec);
                if (hasSpec)
                {
                    specTex!.Bind(2);
                    shader.Set(L.SpecularMap, 2);
                }
                shader.Set(L.SpecUvST, new Vector4(
                    face.SpecularUvXform.ScaleX, face.SpecularUvXform.ScaleY,
                    face.SpecularUvXform.OffsetX, face.SpecularUvXform.OffsetY));
                shader.Set(L.SpecUvRot, face.SpecularUvXform.Rotation);

                shader.Set(L.SpecColor,      face.SpecularColor);
                shader.Set(L.SpecExp,        face.SpecularExponent);
                shader.Set(L.EnvIntensity,   face.EnvironmentIntensity);
            }

            mesh.Draw();
            stats?.RecordDraw(face.Indices?.Length ?? 0);
            _i++;
        }
        if (manageCulling && !cullActive)
            GlApi.Gl.Enable(EnableCap.CullFace);
        shader.Unuse();
    }

    // Writes one instance's data into buf starting at instanceIdx * GlInstanceDrawer.InstanceFloats.
    private static unsafe void WriteInstanceData(
        float[] buf, int instanceIdx, PrimRenderFace face, ref Matrix4x4 view, ref Matrix4x4 proj)
    {
        int @base = instanceIdx * GlInstanceDrawer.InstanceFloats;
        var mv    = face.Transform * view;
        var mvp   = mv * proj;

        // Copy the System.Numerics matrices RAW — no transpose. SN stores row-major with
        // row-vector convention; GL reads attribute mat4 columns from consecutive vec4s,
        // so the raw bytes arrive as the transposed (column-vector) matrix, which is
        // exactly what prim.vert's `aInstMvp * vec4(pos,1)` needs. This mirrors the
        // uniform path (GlShader.Set uploads raw with transpose:false). Transposing here
        // fed the shader the row-vector matrix — translation in the bottom row made w
        // position-dependent and exploded instanced geometry into fans (2026-07-11
        // instancing regression, root cause).
        var mvpSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref System.Runtime.CompilerServices.Unsafe.As<Matrix4x4, float>(ref mvp), 16);
        var mvSpan  = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref System.Runtime.CompilerServices.Unsafe.As<Matrix4x4, float>(ref mv),  16);
        mvpSpan.CopyTo(buf.AsSpan(@base,      16));
        mvSpan.CopyTo( buf.AsSpan(@base + 16, 16));

        var c = face.Color;
        buf[@base + 32] = c.X;  buf[@base + 33] = c.Y;
        buf[@base + 34] = c.Z;  buf[@base + 35] = c.W;

        buf[@base + 36] = face.Fullbright ? 1f : 0f;
        buf[@base + 37] = face.Glow;
        buf[@base + 38] = face.Shiny;
        buf[@base + 39] = face.AlphaCutoff;
        buf[@base + 40] = (float)(int)face.AlphaMode;
        buf[@base + 41] = 0f; buf[@base + 42] = 0f; buf[@base + 43] = 0f;
    }

    // FNV-1a-style hash over raw vertex + index bytes, folded 8 bytes per step (with a
    // byte-wise tail) instead of byte-at-a-time — ~8× fewer iterations over what can be
    // hundreds of KB per face during upload bursts. Only used as a grouping key for
    // per-submission mesh deduplication (identical geometry → shared GlMesh), so the
    // weaker per-chunk mixing versus canonical FNV-1a is irrelevant.
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
    /// G-buffer pass: render geometry into the gbuf FBO writing packed view-space
    /// normals to the colour attachment. Uses the same prim.vert so MVPs match.
    /// Frustum-culls with the same test as the main pass so off-screen faces don't
    /// cost a draw call each; screen-space AO never needs off-screen occluders anyway.
    /// </summary>
    private static void DrawFacesNormal(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4x4 view,
        ref Matrix4x4 proj,
        Frustum? frustum)
    {
        shader.Use();
        // Per-frame view inverse (upper-3×3); combined with each face's cached model
        // inverse to form the normal matrix without a per-face 4×4 invert.
        Matrix4x4.Invert(view, out var viewInvFull);
        var viewInv3 = new Matrix3x3(new Vector3(viewInvFull.M11, viewInvFull.M12, viewInvFull.M13), new Vector3(viewInvFull.M21, viewInvFull.M22, viewInvFull.M23), new Vector3(viewInvFull.M31, viewInvFull.M32, viewInvFull.M33));
        foreach (var (mesh, _, _, _, _, _, face) in list)
        {
            // Same flexi exemption as DrawFaces: their cached AABB is bind-pose.
            if (frustum.HasValue && !face.IsFlexi)
            {
                face.GetWorldAabb(out var amin, out var amax);
                if (!FrustumCuller.IntersectsAabb(frustum.Value, amin, amax))
                    continue;
            }
            var mv  = face.Transform * view;
            var mvp = mv * proj;
            var normalMat = viewInv3 * face.ModelInverse3;
            shader.Set("uMvp",       ref mvp);
            shader.Set("uModelView", ref mv);
            shader.Set("uNormalMat", ref normalMat, transpose: true);
            mesh.Draw();
        }
        shader.Unuse();
    }

    private static readonly Vector4 s_navColorWalkable        = new(0.00f, 0.80f, 0.00f, 0.50f);
    private static readonly Vector4 s_navColorStaticObstacle  = new(0.80f, 0.00f, 0.00f, 0.50f);
    private static readonly Vector4 s_navColorDynamicObstacle = new(0.80f, 0.50f, 0.00f, 0.50f);
    private static readonly Vector4 s_navColorExclusion       = new(0.00f, 0.00f, 0.80f, 0.50f);

    private static void DrawFacesNavMeshOverlay(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4x4 view,
        ref Matrix4x4 proj,
        ConcurrentDictionary<uint, NavMeshWalkabilityType> types)
    {
        shader.Use();
        foreach (var (mesh, _, _, _, _, _, face) in list)
        {
            if (!types.TryGetValue(face.PrimLocalId, out var navType)) continue;
            var color = navType switch
            {
                NavMeshWalkabilityType.Walkable        => s_navColorWalkable,
                NavMeshWalkabilityType.StaticObstacle  => s_navColorStaticObstacle,
                NavMeshWalkabilityType.DynamicObstacle => s_navColorDynamicObstacle,
                NavMeshWalkabilityType.ExclusionZone   => s_navColorExclusion,
                _                                      => Vector4.Zero,
            };
            if (color == Vector4.Zero) continue;
            var mvp = face.Transform * view * proj;
            shader.Set("uMvp", ref mvp);
            shader.Set("uColor", color);
            mesh.Draw();
        }
        shader.Unuse();
    }

    private static void DrawFacesWireframe(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4x4 view,
        ref Matrix4x4 proj)
    {
        shader.Use();
        foreach (var (mesh, _, _, _, _, _, face) in list)
        {
            var mvp = face.Transform * view * proj;
            shader.Set("uMvp", ref mvp);
            mesh.Draw();
        }
        shader.Unuse();
    }

    // ES fallback: draw each mesh's edges as GL_LINES derived from its triangle indices.
    private static void DrawFacesWireframeEs(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4x4 view,
        ref Matrix4x4 proj)
    {
        shader.Use();
        foreach (var (mesh, _, _, _, _, _, face) in list)
        {
            var mvp = face.Transform * view * proj;
            shader.Set("uMvp", ref mvp);
            mesh.DrawLines();
        }
        shader.Unuse();
    }

    /// <summary>
    /// Render each face with a unique flat colour that encodes its 1-based position in
    /// the pick map (R = bits 0–7, G = bits 8–15, B = bits 16–23; ~16.7M faces). Alpha is
    /// always 1.0 and acts as the hit sentinel (clear alpha = 0). The actual LocalId and
    /// FaceIndex are looked up from <see cref="_pickMap"/> after <c>ReadPixels</c>.
    /// </summary>
    private static void DrawFacesPicking(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4x4 view,
        ref Matrix4x4 proj,
        int startIdx)
    {
        shader.Use();
        for (int i = 0; i < list.Count; i++)
        {
            var (mesh, _, _, _, _, _, face) = list[i];
            var mvp = face.Transform * view * proj;
            shader.Set("uMvp", ref mvp);

            uint idx = (uint)(startIdx + i); // 1-based
            float r = ( idx        & 0xFF) / 255f;
            float g = ((idx >> 8)  & 0xFF) / 255f;
            float b = ((idx >> 16) & 0xFF) / 255f;
            shader.Set("uPickColor", new Vector4(r, g, b, 1f));

            mesh.Draw();
        }
        shader.Unuse();
    }

    }
