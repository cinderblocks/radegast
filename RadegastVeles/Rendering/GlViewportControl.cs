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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WireframeProperty)
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
    // FlexiGpuData for scene objects, parallel to _sceneObjects (only populated when compute is available).
    private readonly Dictionary<ulong, FlexiGpuData[]> _flexiGpuDataMap = new();
    // Pending scene-object updates: (sceneKey, submission).  null submission means "remove".
    private readonly ConcurrentQueue<(ulong RootId, PrimRenderSubmission? Sub)> _pendingSceneObjects = new();
    // Flat draw lists rebuilt from _sceneObjects each time objects are added/removed.
    private readonly List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> _sceneOpaque = new();
    private readonly List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> _sceneAlpha  = new();
    // Parallel root-ID lists so the draw loop can apply per-root transform overrides.
    private readonly List<ulong> _sceneOpaqueRootIds = new();
    private readonly List<ulong> _sceneAlphaRootIds  = new();

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

    // Per-root model-matrix overrides (e.g. avatar position updates without a full re-upload).
    // When a sceneKey is present here its matrix replaces face.Transform for every face in that object.
    private readonly ConcurrentDictionary<ulong, Matrix4x4> _sceneObjectTransformOverrides = new();
    // Pending transform override updates enqueued from non-GL threads.
    private readonly ConcurrentQueue<(ulong RootId, Matrix4x4 Transform)> _pendingTransformOverrides = new();

    // Pending single-texture patches for already-live scene objects (progressive texture streaming).
    private readonly ConcurrentQueue<SceneTexturePatch> _pendingTexturePatches = new();
    // Back-pressure gate: limits _pendingTexturePatches to 200 entries without spin-waiting.
    // Each WaitAsync/Wait in PatchSceneObjectTexture consumes a permit; each dequeue in
    // ApplyTexturePatches releases one.  Initial count matches the queue-depth limit.
    private readonly SemaphoreSlim _texturePatchGate = new SemaphoreSlim(200, 200);
    // Pending texture patches for the single-submission (AvatarViewer / ObjectViewer) path.
    private readonly ConcurrentQueue<SceneTexturePatch> _pendingSubmissionPatches = new();
    // Patches that arrived before their scene object was uploaded; retried each frame for up to ~5 s.
    private readonly List<(SceneTexturePatch patch, int retriesLeft)> _deferredPatches = new();
    // Set when a texture patch upgraded a legacy face to the alpha pass; triggers a single
    // RebuildSceneFlatLists() at the end of ApplyTexturePatches so the face moves draw lists.
    private bool _alphaReclassNeeded;
    // Submission patches that arrived before UploadSubmission ran; retried for up to ~5 s.
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

    /// <summary>Number of scene object submissions waiting to be uploaded to the GPU this frame. Zero-cost snapshot.</summary>
    public int PendingUploadCount => _pendingSceneObjects.Count;

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
        _pendingSceneObjects.Enqueue((sceneKey, submission));
        Avalonia.Threading.Dispatcher.UIThread.Post(_core.RequestNextFrameRendering);
    }

    /// <summary>
    /// Queue removal of the scene object with the given scene key.
    /// Safe to call from any thread.
    /// </summary>
    public void RemoveSceneObject(ulong sceneKey)
    {
        _pendingSceneObjects.Enqueue((sceneKey, null));
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
        if (_heartbeat == null)
        {
            _heartbeat = new Avalonia.Threading.DispatcherTimer(
                System.TimeSpan.FromMilliseconds(33),
                Avalonia.Threading.DispatcherPriority.Render,
                (_, _) => _core.RequestNextFrameRendering());
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
        // Cap uploads per frame so a large burst (e.g. scene entry / teleport) is spread
        // across many frames instead of stalling the GL thread. Each upload can involve
        // several glTexImage2D calls, so even 5 per frame can take ~10 ms on a mid-range GPU.
        const int MaxSceneUploadsPerFrame = 5;
        if (_initError == null)
        {
            if (_pendingClearScene)
            {
                _pendingClearScene = false;
                FreeSceneObjectResources();
            }
            bool sceneListDirty = false;
            int uploadsThisFrame = 0;
            while (uploadsThisFrame < MaxSceneUploadsPerFrame && _pendingSceneObjects.TryDequeue(out var entry))
            {
                if (entry.Sub == null)
                    RemoveSceneObjectGpuNoRebuild(entry.RootId);
                else
                    UploadSceneObjectNoRebuild(entry.RootId, entry.Sub);
                sceneListDirty = true;
                uploadsThisFrame++;
            }
            // If we hit the cap, request another render tick to continue draining.
            if (!_pendingSceneObjects.IsEmpty)
                _core.RequestNextFrameRendering();
            if (sceneListDirty)
                RebuildSceneFlatLists();
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

        // Error: vivid red-tint.  Sky ready: clear to black (sky shader fills it).
        // Fallback: solid SL sky-blue so the viewer looks reasonable without the sky shader.
        float clearR = _initError != null ? 0.55f : (_skyReady ? 0f : 0.39f);
        float clearG = _initError != null ? 0.10f : (_skyReady ? 0f : 0.58f);
        float clearB = _initError != null ? 0.10f : (_skyReady ? 0f : 0.93f);
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

        // ── EEP day-cycle update ──────────────────────────────────────────
        // Sample the environment service once per frame so sky and water colour
        // track the in-world day/night cycle.  Done before DrawSky so the sky
        // shader already receives the updated parameters on the same frame.
        if (EnvironmentService != null)
        {
            Sky          = EnvironmentService.GetCurrentSky();
            WaterFogColor = EnvironmentService.GetCurrentWaterFogColor();
        }

        // ── Sky background ────────────────────────────────────────────────
        // Drawn before everything else so it fills pixels not covered by geometry.
        if (_skyReady)
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
                DrawFacesNormal(_opaque, _gnormShader!, ref view, ref proj);
                DrawFacesNormal(_sceneOpaque, _gnormShader!, ref view, ref proj);

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

                // Restore state for the main scene pass.
                GlApi.Gl.Enable(EnableCap.DepthTest);
                GlApi.Gl.DepthMask(true);
                GlApi.Gl.ActiveTexture(TextureUnit.Texture0);
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
        // Apply any pending transform overrides directly to face.Transform so all draw
        // paths (opaque, alpha, wireframe, picking) automatically use the updated position.
        ApplySceneTransformOverrides();
        // Apply any pending progressive texture patches (streamed bitmaps arriving after
        // the geometry was already submitted).
        ApplyTexturePatches();

        DrawFaces(_opaque,      _primShader, ref view, ref proj, ssaoTex: ssaoTex, screenSize: new Vector2(w, h), frustum: frustum, stats: _stats, sky: Sky, enableInstancing: true, sortForBatching: true);
        DrawFaces(_sceneOpaque, _primShader, ref view, ref proj, ssaoTex: ssaoTex, screenSize: new Vector2(w, h), frustum: frustum, stats: _stats, sky: Sky, enableInstancing: true);

        // ── Water surface ─────────────────────────────────────────────────
        // Drawn after opaque geometry (correct depth test) but before alpha
        // geometry (transparent objects above water render in front of it).
        if (doWater)
            DrawWater(ref view, ref proj, waterH);

        // Alpha pass — depth-sorted back-to-front, two-sided.
        var allAlpha = _alpha.Count > 0 || _sceneAlpha.Count > 0;
        if (allAlpha)
        {
            // Merge base alpha + scene-object alpha into a persistent list (reused each frame).
            _mergedAlpha.Clear();
            _mergedAlpha.AddRange(_alpha);
            _mergedAlpha.AddRange(_sceneAlpha);
            var mergedAlpha = _mergedAlpha;
            if (mergedAlpha.Count > 1)
            {
                var eye = _camera.EyePosition;
                // Precompute each face's squared eye-distance once (O(N)). The comparator
                // then reads the cached float, avoiding a vector subtraction + LengthSquared
                // on every one of the O(N log N) comparisons performed by Sort.
                for (int i = 0; i < mergedAlpha.Count; i++)
                    mergedAlpha[i].face.AlphaSortKey = (mergedAlpha[i].face.Centroid - eye).LengthSquared();
                mergedAlpha.Sort(static (a, b) => b.face.AlphaSortKey.CompareTo(a.face.AlphaSortKey));
            }

            GlApi.Gl.Enable(EnableCap.Blend);
            GlApi.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GlApi.Gl.DepthMask(false);
            GlApi.Gl.Disable(EnableCap.CullFace);
            // Alpha surfaces don't receive SSAO — matches SL viewer behaviour.
            DrawFaces(mergedAlpha, _primShader, ref view, ref proj, manageCulling: false, frustum: frustum, stats: _stats, sky: Sky);
            GlApi.Gl.Enable(EnableCap.CullFace);
            GlApi.Gl.DepthMask(true);
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

                // R+G encode a 1-based index into _pickMap (background cleared to 0,0,0,0).
                // Non-zero R or G means a face was hit; look up the real LocalId/FaceIndex.
                if (pixel[0] != 0 || pixel[1] != 0)
                {
                    uint idx = (uint)(pixel[0] | (pixel[1] << 8)); // 1-based
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
        while (_pendingTexturePatches.TryDequeue(out var tp))
        {
            tp.Bitmap?.Dispose();
            _texturePatchGate.Release();
        }
        _texturePatchGate.Dispose();

        // Drain scene-object queue and dispose pending bitmaps.
        while (_pendingSceneObjects.TryDequeue(out var entry))
        {
            if (entry.Sub != null)
                DisposePendingBitmaps(entry.Sub);
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
    /// Vertex layout (8 floats per vertex): pos(3) normal(3) uv(2).
    /// </summary>
    /// <summary>
    /// Extracts a positions-only buffer (3 floats per vertex) from an interleaved
    /// Position(3)+Normal(3)+TexCoord(2) buffer. Used to slim the CPU-side picker
    /// data after the full interleaved buffer has been uploaded to the GPU,
    /// reducing LOH retention by ~62%.
    /// </summary>
    private static float[] PickerFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 3)
            return Array.Empty<float>();
        int len    = length > 0 ? length : interleaved.Length;
        int vCount = len / 8;
        var pos = new float[vCount * 3];
        for (int i = 0, j = 0; i < len; i += 8, j += 3)
        {
            pos[j]     = interleaved[i];
            pos[j + 1] = interleaved[i + 1];
            pos[j + 2] = interleaved[i + 2];
        }
        return pos;
    }

    // Extracts a compact normal+UV buffer (5 floats/vertex: nx,ny,nz,u,v) from the full
    // interleaved buffer (8 floats/vertex: px,py,pz,nx,ny,nz,u,v).
    // Stored alongside PickerVertices so the full 8-wide buffer can be released post-upload.
    private static float[] NormalUvFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 8)
            return Array.Empty<float>();
        int len    = length > 0 ? length : interleaved.Length;
        int vCount = len / 8;
        var nuv = new float[vCount * 5];
        for (int i = 0, j = 0; i < len; i += 8, j += 5)
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

        // Object-local position from the compact picker buffer (stride 3: X,Y,Z).
        int p0 = indices[bestTri + 0] * PickerStride;
        int p1 = indices[bestTri + 1] * PickerStride;
        int p2 = indices[bestTri + 2] * PickerStride;
        float w0 = 1f - bestU - bestV, w1 = bestU, w2 = bestV;

        var lpos0 = new Vector3(pickerVerts[p0], pickerVerts[p0+1], pickerVerts[p0+2]);
        var lpos1 = new Vector3(pickerVerts[p1], pickerVerts[p1+1], pickerVerts[p1+2]);
        var lpos2 = new Vector3(pickerVerts[p2], pickerVerts[p2+1], pickerVerts[p2+2]);
        var localPos = lpos0 * w0 + lpos1 * w1 + lpos2 * w2;

        // Normal and UV from the compact normal/UV buffer (stride 5: nx,ny,nz,u,v).
        int n0i = indices[bestTri + 0] * NormalUvStride;
        int n1i = indices[bestTri + 1] * NormalUvStride;
        int n2i = indices[bestTri + 2] * NormalUvStride;

        var n0 = Vector3.Normalize(new Vector3(normalUvVerts[n0i], normalUvVerts[n0i+1], normalUvVerts[n0i+2]));
        var n1 = Vector3.Normalize(new Vector3(normalUvVerts[n1i], normalUvVerts[n1i+1], normalUvVerts[n1i+2]));
        var n2 = Vector3.Normalize(new Vector3(normalUvVerts[n2i], normalUvVerts[n2i+1], normalUvVerts[n2i+2]));
        var normal = Vector3.Normalize(n0 * w0 + n1 * w1 + n2 * w2);

        float uvX = normalUvVerts[n0i+3] * w0 + normalUvVerts[n1i+3] * w1 + normalUvVerts[n2i+3] * w2;
        float uvY = normalUvVerts[n0i+4] * w0 + normalUvVerts[n1i+4] * w1 + normalUvVerts[n2i+4] * w2;

        // Binormal: from local-space triangle edge/UV delta pair (Lengyel's method).
        var  edge1w  = lpos1 - lpos0;
        var  edge2w  = lpos2 - lpos0;
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
            Position = localPos,
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

        DrawFaces(_opaque,      _primShader, ref reflView, ref proj, frustum: reflFrustum, sky: Sky, enableInstancing: true, sortForBatching: true);
        DrawFaces(_sceneOpaque, _primShader, ref reflView, ref proj, frustum: reflFrustum, sky: Sky, enableInstancing: true);

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

        // Clean up texture units so subsequent passes start from a known state
        GlApi.Gl.ActiveTexture(TextureUnit.Texture2); GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
        GlApi.Gl.ActiveTexture(TextureUnit.Texture1); GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
        GlApi.Gl.ActiveTexture(TextureUnit.Texture0); GlApi.Gl.BindTexture(TextureTarget.Texture2D, 0);
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

        foreach (var face in sub.Faces)
        {
            int vLen = face.VerticesLength > 0 ? face.VerticesLength : face.Vertices!.Length;
            GlMesh mesh;
            if (!face.IsFlexi)
            {
                ulong h = VertexHash(face.Vertices!, vLen, face.Indices);
                if (!meshPool.TryGetValue(h, out mesh!))
                {
                    mesh = new GlMesh(face.Vertices!, vLen, face.Indices);
                    meshPool[h] = mesh;
                }
            }
            else
            {
                mesh = new GlMesh(face.Vertices!, vLen, face.Indices);
            }
            _faceMeshes.Add(mesh); // indexed by face position for animation updates
            // Extract compact CPU-side picker and normal/UV buffers, then release the full
            // interleaved float[] (8 floats/vertex) so the LOH array can be collected.
            // PickerVertices (3 floats/vertex) is used for ray–triangle intersection;
            // NormalUvVertices (5 floats/vertex: nx,ny,nz,u,v) is used by ComputeHitInfo.
            face.PickerVertices   = PickerFromInterleaved(face.Vertices, vLen);
            face.NormalUvVertices = NormalUvFromInterleaved(face.Vertices, vLen);
            if (face.VerticesLength > 0)
                ArrayPool<float>.Shared.Return(face.Vertices!);
            face.Vertices         = null; // release the full 8-wide LOH array
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

    private void FreeSceneObjectResources()
    {
        foreach (var gpuDatas in _flexiGpuDataMap.Values)
            foreach (var gd in gpuDatas) gd.Dispose();
        _flexiGpuDataMap.Clear();
        foreach (var gpuDatas in _skinGpuDataMap.Values)
            foreach (var gd in gpuDatas) gd.Dispose();
        _skinGpuDataMap.Clear();

        var textures = new HashSet<GlTexture>();
        foreach (var faces in _sceneObjects.Values)
        {
            foreach (var (mesh, tex, normalTex, specTex, mrTex, emTex, _) in faces)
            {
                mesh.Dispose();
                if (tex      != null) textures.Add(tex);
                if (normalTex != null) textures.Add(normalTex);
                if (specTex  != null) textures.Add(specTex);
                if (mrTex    != null) textures.Add(mrTex);
                if (emTex    != null) textures.Add(emTex);
            }
        }
        foreach (var tex in textures) tex.Dispose();
        _sceneObjects.Clear();
        _sceneObjectTransformOverrides.Clear();
        while (_pendingSceneVertexUpdates.TryDequeue(out _)) { }
        while (_pendingTransformOverrides.TryDequeue(out _)) { }
        // Discard deferred and pending texture patches — they belong to the old scene
        // and must not be applied to new objects that may share the same local IDs.
        foreach (var (patch, _) in _deferredPatches) patch.Bitmap?.Dispose();
        _deferredPatches.Clear();
        int purged = 0;
        while (_pendingTexturePatches.TryDequeue(out var p)) { p.Bitmap?.Dispose(); purged++; }
        // Release all consumed gate permits so future PatchSceneObjectTexture calls are not starved.
        if (purged > 0) _texturePatchGate.Release(purged);
        RebuildSceneFlatLists();
    }

    private void RemoveSceneObjectGpu(ulong rootId)
    {
        if (!_sceneObjects.TryGetValue(rootId, out var faces)) return;

        var textures = new HashSet<GlTexture>();
        foreach (var (mesh, tex, normalTex, specTex, mrTex, emTex, _) in faces)
        {
            mesh.Dispose();
            if (tex      != null) textures.Add(tex);
            if (normalTex != null) textures.Add(normalTex);
            if (specTex  != null) textures.Add(specTex);
            if (mrTex    != null) textures.Add(mrTex);
            if (emTex    != null) textures.Add(emTex);
        }
        // Don't dispose a texture that is still referenced by another object.
        foreach (var otherEntry in _sceneObjects)
        {
            if (otherEntry.Key == rootId) continue;
            foreach (var (_, tex, normalTex, specTex, mrTex, emTex, _) in otherEntry.Value)
            {
                textures.Remove(tex!);
                textures.Remove(normalTex!);
                textures.Remove(specTex!);
                textures.Remove(mrTex!);
                textures.Remove(emTex!);
            }
        }
        foreach (var tex in textures) tex.Dispose();
        _sceneObjects.Remove(rootId);
        _sceneObjectTransformOverrides.TryRemove(rootId, out _);
        if (_flexiGpuDataMap.Remove(rootId, out var gpuDatas))
            foreach (var gd in gpuDatas) gd.Dispose();
        if (_skinGpuDataMap.Remove(rootId, out var skinGpuDatas))
            foreach (var gd in skinGpuDatas) gd.Dispose();
        RebuildSceneFlatLists();
    }

    // No-rebuild variant used by the batched drain loop in GlRender.
    private void RemoveSceneObjectGpuNoRebuild(ulong rootId)
    {
        if (!_sceneObjects.TryGetValue(rootId, out var faces)) return;

        var textures = new HashSet<GlTexture>();
        foreach (var (mesh, tex, normalTex, specTex, mrTex, emTex, _) in faces)
        {
            mesh.Dispose();
            if (tex      != null) textures.Add(tex);
            if (normalTex != null) textures.Add(normalTex);
            if (specTex  != null) textures.Add(specTex);
            if (mrTex    != null) textures.Add(mrTex);
            if (emTex    != null) textures.Add(emTex);
        }
        foreach (var otherEntry in _sceneObjects)
        {
            if (otherEntry.Key == rootId) continue;
            foreach (var (_, tex, normalTex, specTex, mrTex, emTex, _) in otherEntry.Value)
            {
                textures.Remove(tex!);
                textures.Remove(normalTex!);
                textures.Remove(specTex!);
                textures.Remove(mrTex!);
                textures.Remove(emTex!);
            }
        }
        foreach (var tex in textures) tex.Dispose();
        _sceneObjects.Remove(rootId);
        _sceneObjectTransformOverrides.TryRemove(rootId, out _);
        if (_flexiGpuDataMap.Remove(rootId, out var gpuDatas))
            foreach (var gd in gpuDatas) gd.Dispose();
        if (_skinGpuDataMap.Remove(rootId, out var skinGpuDatas))
            foreach (var gd in skinGpuDatas) gd.Dispose();
    }

    private void UploadSceneObject(ulong rootId, PrimRenderSubmission sub)
    {
        UploadSceneObjectNoRebuild(rootId, sub);
        RebuildSceneFlatLists();
    }

    // No-rebuild variant: caller is responsible for calling RebuildSceneFlatLists once.
    private void UploadSceneObjectNoRebuild(ulong rootId, PrimRenderSubmission sub)
    {
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

        foreach (var face in sub.Faces)
        {
            int vLen = face.VerticesLength > 0 ? face.VerticesLength : face.Vertices!.Length;
            GlMesh mesh;
            if (!face.IsFlexi)
            {
                ulong h = VertexHash(face.Vertices!, vLen, face.Indices);
                if (!meshPool.TryGetValue(h, out mesh!))
                {
                    mesh = new GlMesh(face.Vertices!, vLen, face.Indices);
                    meshPool[h] = mesh;
                }
            }
            else
            {
                mesh = new GlMesh(face.Vertices!, vLen, face.Indices);
            }
            // Extract compact CPU-side picker and normal/UV buffers, then release the full
            // interleaved float[] (8 floats/vertex) so the LOH array can be collected.
            face.PickerVertices   = PickerFromInterleaved(face.Vertices, vLen);
            face.NormalUvVertices = NormalUvFromInterleaved(face.Vertices, vLen);
            if (face.VerticesLength > 0)
                ArrayPool<float>.Shared.Return(face.Vertices!);
            face.Vertices         = null; // release the full 8-wide LOH array
            var tex       = TryUpload(face.Texture);
            var normalTex = TryUpload(face.NormalMapTexture);
            var specTex   = TryUpload(face.SpecularMapTexture);
            var mrTex     = TryUpload(face.MetallicRoughnessTexture);
            var emTex     = TryUpload(face.EmissiveTexture);
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
    }

    private void RebuildSceneFlatLists()
    {
        _sceneOpaque.Clear();
        _sceneAlpha.Clear();
        _sceneOpaqueRootIds.Clear();
        _sceneAlphaRootIds.Clear();
        foreach (var (rootId, faces) in _sceneObjects)
        {
            foreach (var entry in faces)
            {
                if (entry.face.HasAlpha)
                {
                    _sceneAlpha.Add(entry);
                    _sceneAlphaRootIds.Add(rootId);
                }
                else
                {
                    _sceneOpaque.Add(entry);
                    _sceneOpaqueRootIds.Add(rootId);
                }
            }
        }
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
        try
        {
            _texturePatchGate.Wait(ct);
        }
        catch (OperationCanceledException)
        {
            // Permit was never acquired; bitmap was never transferred — dispose it now.
            patch.Bitmap?.Dispose();
            throw;
        }
        catch (ObjectDisposedException)
        {
            // Viewport is tearing down (GlDeinit already disposed the semaphore).
            // Treat as cancellation: dispose the bitmap and silently exit — throwing
            // here would propagate through Progress<T> onto the thread pool and crash.
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
            _texturePatchGate.Release();
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
    /// Drains <see cref="_pendingTransformOverrides"/>, stores the latest matrix per root,
    /// then patches <see cref="PrimRenderFace.Transform"/> on all flat-list entries whose
    /// root ID has an active override. Called once per render frame before draw calls.
    /// </summary>
    private void ApplySceneTransformOverrides()
    {
        // Drain the concurrent queue into the dictionary.
        while (_pendingTransformOverrides.TryDequeue(out var to))
            _sceneObjectTransformOverrides[to.RootId] = to.Transform;

        if (_sceneObjectTransformOverrides.IsEmpty) return;

        for (int i = 0; i < _sceneOpaque.Count; i++)
        {
            if (_sceneOpaque[i].face.IsFlexi) continue;            // never stomp flexi (see PrimRenderFace.IsFlexi)
            if (i < _sceneOpaqueRootIds.Count &&
                _sceneObjectTransformOverrides.TryGetValue(_sceneOpaqueRootIds[i], out var m))
                _sceneOpaque[i].face.Transform = m;
        }
        for (int i = 0; i < _sceneAlpha.Count; i++)
        {
            if (_sceneAlpha[i].face.IsFlexi) continue;
            if (i < _sceneAlphaRootIds.Count &&
                _sceneObjectTransformOverrides.TryGetValue(_sceneAlphaRootIds[i], out var m))
                _sceneAlpha[i].face.Transform = m;
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
        const int MaxPatchesPerFrame = 20;
        int budget = MaxPatchesPerFrame;

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
                if (TryApplyTexturePatch(patch))
                {
                    budget--;  // consumed a slot whether or not geometry was found
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

        // Drain from the incoming queue using remaining budget.
        while (budget > 0 && _pendingTexturePatches.TryDequeue(out var patch))
        {
            budget--;
            _texturePatchGate.Release();  // a slot is now free; wake any waiting producer
            if (!TryApplyTexturePatch(patch))
            {
                // Scene object not yet uploaded — defer for up to ~150 frames (~5 s at 30 Hz).
                _deferredPatches.Add((patch, 150));
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

        // Geometry not yet uploaded — defer for up to ~150 frames (~5 s at 30 Hz).
        _deferredSubmissionPatches.Add((patch, 150));
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

        var oldTex = ApplyPatchToList(_opaque, patch, newTex)
                  ?? ApplyPatchToList(_alpha,  patch, newTex);
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

        // Update the flat draw lists and capture the displaced texture.
        // The face lives in exactly one of the two lists; the other call returns null.
        var oldTex = ApplyPatchToList(_sceneOpaque, patch, newTex)
                  ?? ApplyPatchToList(_sceneAlpha,  patch, newTex);

        // Mirror the texture into _sceneObjects so re-flattening picks it up.
        // Search by PrimLocalId + FaceIndex rather than using patch.FaceIndex as a
        // direct index: attachment FaceIndex values are local to the attachment's own
        // face list, not the combined global index in the avatar+attachment submission.
        for (int fi = 0; fi < faceTuples.Count; fi++)
        {
            var (mesh, tex, normalTex, specTex, mrTex, emTex, face) = faceTuples[fi];
            if (face.PrimLocalId != patch.RootLocalId || face.FaceIndex != patch.FaceIndex) continue;
            var (updTex, updNorm, updSpec, updMr, updEm) =
                ReplacedSlot(patch.Slot, newTex, tex, normalTex, specTex, mrTex, emTex);
            faceTuples[fi] = (mesh, updTex, updNorm, updSpec, updMr, updEm, face);

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

        // Dispose the old GlTexture now that every reference has been replaced.
        // oldTex is null on the first-ever patch (initial slots are null from
        // BuildFacesWithoutTextures).  Guard against the degenerate case where a
        // patch re-applies the same GlTexture instance.
        if (oldTex != null && oldTex != newTex)
            oldTex.Dispose();

        return true;
    }

    // Returns the GlTexture that was displaced from the slot, or null if the face was
    // not found or the slot was already null.  Caller is responsible for disposing it.
    private static GlTexture? ApplyPatchToList(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        SceneTexturePatch patch,
        GlTexture newTex)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var (mesh, tex, normalTex, specTex, mrTex, emTex, face) = list[i];
            if (face.PrimLocalId != patch.RootLocalId || face.FaceIndex != patch.FaceIndex) continue;

            var (updTex, updNorm, updSpec, updMr, updEm) =
                ReplacedSlot(patch.Slot, newTex, tex, normalTex, specTex, mrTex, emTex);
            list[i] = (mesh, updTex, updNorm, updSpec, updMr, updEm, face);
            // Return the displaced texture so the caller can dispose it.
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
        bool enableInstancing = false,
        bool sortForBatching = false)
    {
        shader.Use();

        // Bind SSAO blur result to texture unit 4 (units 0–3 used by material textures).
        bool hasSsao = ssaoTex != 0 && screenSize != default;
        if (hasSsao)
        {
            GlApi.Gl.ActiveTexture(TextureUnit.Texture4);
            GlApi.Gl.BindTexture(TextureTarget.Texture2D, ssaoTex);
            shader.Set("uSsaoMap",    4);
            shader.Set("uHasSsao",    1);
            shader.Set("uScreenSize", screenSize);
        }
        else
        {
            shader.Set("uHasSsao", 0);
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
            shader.Set("uSunDir",       sunViewDir);
            shader.Set("uSunColor",     s.SunlightColor);
            shader.Set("uAmbientColor", s.Ambient);
        }

        // View inverse (upper-3×3) is constant for every face this frame. The per-face
        // normal matrix (MV⁻¹)ᵀ = (V⁻¹)₃ × (M⁻¹)₃, so combining this with the face's cached
        // model inverse avoids a full 4×4 Matrix4x4.Invert per face per frame (see
        // PrimRenderFace.ModelInverse3).
        Matrix4x4.Invert(view, out var viewInvFull);
        var viewInv3 = new Matrix3x3(new Vector3(viewInvFull.M11, viewInvFull.M12, viewInvFull.M13), new Vector3(viewInvFull.M21, viewInvFull.M22, viewInvFull.M23), new Vector3(viewInvFull.M31, viewInvFull.M32, viewInvFull.M33));

        bool canBatch = enableInstancing && _instanceDrawer != null;

        if (canBatch && sortForBatching)
        {
            // Sort opaque list by (mesh ref, tex ref) so identical-geometry faces are
            // consecutive, maximising instanced batch sizes while minimising texture binds.
            // Only done for lists without a parallel index array (_opaque, not _sceneOpaque).
            list.Sort(static (a, b) =>
            {
                int mh = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.mesh)
                         .CompareTo(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b.mesh));
                if (mh != 0) return mh;
                int tha = a.tex == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.tex);
                int thb = b.tex == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b.tex);
                return tha.CompareTo(thb);
            });
        }

        // Signal the vertex shader which draw path is active. Set to false once up-front;
        // toggled to true only around instanced batches, then restored.
        shader.Set("uInstanced", false);

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
                    shader.Set("uHasTexture",  hasTex0);
                    if (hasTex0) { tex!.Bind(0); shader.Set("uAlbedo", 0); }
                    shader.Set("uIsPBR",       false);
                    shader.Set("uHasMaterial", false);
                    shader.Set("uHasBump",     false);

                    // Grow the per-instance data buffer if needed.
                    int needed = batchCount * GlInstanceDrawer.InstanceFloats;
                    if (needed > _instanceDataBuf.Length)
                        _instanceDataBuf = new float[needed * 2];

                    // Build per-instance data with individual frustum culling.
                    shader.Set("uInstanced", true);
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

                    shader.Set("uInstanced", false);
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

            shader.Set("uMvp",       ref mvp);
            shader.Set("uModelView", ref mv);
            shader.Set("uNormalMat", ref normalMat, transpose: true);
            shader.Set("uColor",     face.Color);
            shader.Set("uFullbright", face.Fullbright);
            shader.Set("uGlow",      face.Glow);
            shader.Set("uAlphaCutoff", face.AlphaCutoff);
            shader.Set("uShiny",     face.Shiny);
            shader.Set("uHasBump",   face.HasBump);
            shader.Set("uAlphaMode", (int)face.AlphaMode);

            bool hasTex = tex != null;
            shader.Set("uHasTexture", hasTex);
            if (hasTex)
            {
                tex!.Bind(0);
                shader.Set("uAlbedo", 0);
            }

            // ── PBR path ─────────────────────────────────────────────────
            shader.Set("uIsPBR", face.IsPBR);
            if (face.IsPBR)
            {
                shader.Set("uBaseColorFactor",  face.BaseColorFactor);
                shader.Set("uMetallicFactor",   face.MetallicFactor);
                shader.Set("uRoughnessFactor",  face.RoughnessFactor);
                shader.Set("uEmissiveFactor",   face.EmissiveFactor);

                shader.Set("uBaseColorUvST",  new Vector4(
                    face.BaseColorUvXform.ScaleX, face.BaseColorUvXform.ScaleY,
                    face.BaseColorUvXform.OffsetX, face.BaseColorUvXform.OffsetY));
                shader.Set("uBaseColorUvRot", face.BaseColorUvXform.Rotation);

                bool hasNorm = normalTex != null;
                shader.Set("uHasNormalMap", hasNorm);
                if (hasNorm)
                {
                    normalTex!.Bind(1);
                    shader.Set("uNormalMap", 1);
                }
                shader.Set("uPbrNormalUvST",  new Vector4(
                    face.PbrNormalUvXform.ScaleX, face.PbrNormalUvXform.ScaleY,
                    face.PbrNormalUvXform.OffsetX, face.PbrNormalUvXform.OffsetY));
                shader.Set("uPbrNormalUvRot", face.PbrNormalUvXform.Rotation);

                bool hasMR = mrTex != null;
                shader.Set("uHasMRMap", hasMR);
                if (hasMR)
                {
                    mrTex!.Bind(2);
                    shader.Set("uMetallicRoughnessMap", 2);
                }
                shader.Set("uMRUvST",  new Vector4(
                    face.MetallicRoughnessUvXform.ScaleX, face.MetallicRoughnessUvXform.ScaleY,
                    face.MetallicRoughnessUvXform.OffsetX, face.MetallicRoughnessUvXform.OffsetY));
                shader.Set("uMRUvRot", face.MetallicRoughnessUvXform.Rotation);

                bool hasEm = emTex != null;
                shader.Set("uHasEmissiveMap", hasEm);
                if (hasEm)
                {
                    emTex!.Bind(3);
                    shader.Set("uEmissiveMap", 3);
                }
                shader.Set("uEmissiveUvST",  new Vector4(
                    face.EmissiveUvXform.ScaleX, face.EmissiveUvXform.ScaleY,
                    face.EmissiveUvXform.OffsetX, face.EmissiveUvXform.OffsetY));
                shader.Set("uEmissiveUvRot", face.EmissiveUvXform.Rotation);
            }
            else
            {
                // ── Legacy material path ──────────────────────────────────
                shader.Set("uHasMaterial", face.HasMaterial);

                bool hasNorm = normalTex != null;
                shader.Set("uHasNormalMap", hasNorm);
                if (hasNorm)
                {
                    normalTex!.Bind(1);
                    shader.Set("uNormalMap", 1);
                }
                shader.Set("uNormalUvST", new Vector4(
                    face.NormalUvXform.ScaleX, face.NormalUvXform.ScaleY,
                    face.NormalUvXform.OffsetX, face.NormalUvXform.OffsetY));
                shader.Set("uNormalUvRot", face.NormalUvXform.Rotation);

                bool hasSpec = specTex != null;
                shader.Set("uHasSpecularMap", hasSpec);
                if (hasSpec)
                {
                    specTex!.Bind(2);
                    shader.Set("uSpecularMap", 2);
                }
                shader.Set("uSpecUvST", new Vector4(
                    face.SpecularUvXform.ScaleX, face.SpecularUvXform.ScaleY,
                    face.SpecularUvXform.OffsetX, face.SpecularUvXform.OffsetY));
                shader.Set("uSpecUvRot", face.SpecularUvXform.Rotation);

                shader.Set("uSpecColor",      face.SpecularColor);
                shader.Set("uSpecExp",        face.SpecularExponent);
                shader.Set("uEnvIntensity",   face.EnvironmentIntensity);
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
    // Matrices are transposed from System.Numerics row-major to GL column-major order.
    private static unsafe void WriteInstanceData(
        float[] buf, int instanceIdx, PrimRenderFace face, ref Matrix4x4 view, ref Matrix4x4 proj)
    {
        int @base = instanceIdx * GlInstanceDrawer.InstanceFloats;
        var mv    = face.Transform * view;
        var mvp   = mv * proj;

        // Column-major upload: System.Numerics is row-major, so transpose before copying raw bytes.
        var mvpT = Matrix4x4.Transpose(mvp);
        var mvT  = Matrix4x4.Transpose(mv);
        var mvpSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref System.Runtime.CompilerServices.Unsafe.As<Matrix4x4, float>(ref mvpT), 16);
        var mvSpan  = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref System.Runtime.CompilerServices.Unsafe.As<Matrix4x4, float>(ref mvT),  16);
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

    // FNV-64 hash over raw vertex + index bytes.
    // Used for per-submission mesh deduplication: identical geometry → shared GlMesh.
    private static ulong VertexHash(float[] verts, int len, ushort[] indices)
    {
        const ulong Prime  = 0x00000100000001B3UL;
        const ulong Offset = 0xCBF29CE484222325UL;
        ulong hash = Offset;
        var vBytes = System.Runtime.InteropServices.MemoryMarshal.Cast<float, byte>(verts.AsSpan(0, len));
        foreach (byte b in vBytes) { hash = (hash ^ b) * Prime; }
        var iBytes = System.Runtime.InteropServices.MemoryMarshal.Cast<ushort, byte>(indices.AsSpan());
        foreach (byte b in iBytes) { hash = (hash ^ b) * Prime; }
        return hash;
    }

    /// <summary>
    /// G-buffer pass: render geometry into the gbuf FBO writing packed view-space
    /// normals to the colour attachment. Uses the same prim.vert so MVPs match.
    /// </summary>
    private static void DrawFacesNormal(
        List<(GlMesh mesh, GlTexture? tex, GlTexture? normalTex, GlTexture? specTex, GlTexture? mrTex, GlTexture? emTex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4x4 view,
        ref Matrix4x4 proj)
    {
        shader.Use();
        // Per-frame view inverse (upper-3×3); combined with each face's cached model
        // inverse to form the normal matrix without a per-face 4×4 invert.
        Matrix4x4.Invert(view, out var viewInvFull);
        var viewInv3 = new Matrix3x3(new Vector3(viewInvFull.M11, viewInvFull.M12, viewInvFull.M13), new Vector3(viewInvFull.M21, viewInvFull.M22, viewInvFull.M23), new Vector3(viewInvFull.M31, viewInvFull.M32, viewInvFull.M33));
        foreach (var (mesh, _, _, _, _, _, face) in list)
        {
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
    /// the pick map (R = bits 0–7, G = bits 8–15). Alpha is always 1.0 and acts as the
    /// hit sentinel (clear alpha = 0). The actual LocalId and FaceIndex are looked up
    /// from <see cref="_pickMap"/> after <c>ReadPixels</c>.
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
            shader.Set("uPickColor", new Vector4(r, g, 0f, 1f));

            mesh.Draw();
        }
        shader.Unuse();
    }

    }
