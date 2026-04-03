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
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Avalonia OpenGL control that renders a <see cref="PrimRenderSubmission"/>.
/// <para>
/// Camera controls:
/// <list type="bullet">
///   <item>Left-drag → orbit</item>
///   <item>Right-drag / Ctrl+left-drag → pan</item>
///   <item>Scroll wheel → zoom</item>
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
    }

    // ── Camera ───────────────────────────────────────────────────────────────────

    private readonly Camera3D _camera = new();

    // ── GPU resources (GL thread only) ───────────────────────────────────────────

    private GlShader? _primShader;
    private GlShader? _wireShader;
    private GlShader? _pickShader;
    private string?   _initError;
    // GL ES (ANGLE) does not support PolygonMode; detected at init time.
    private bool      _supportsPolygonMode;

    // Dedicated pick FBO — owned exclusively by the GL thread.
    private int _pickFbo, _pickRbo, _pickDepth;
    private int _pickFboW, _pickFboH;

    private readonly List<(GlMesh mesh, GlTexture? tex, PrimRenderFace face)> _opaque = new();
    private readonly List<(GlMesh mesh, GlTexture? tex, PrimRenderFace face)> _alpha  = new();
    // Maps 1-based pick index → (LocalId, FaceIndex). Built whenever render lists are refreshed.
    private readonly List<(uint LocalId, int FaceIndex)> _pickMap = new();

    // ── Cross-thread submission queue (lock-free single slot) ────────────────────

    private PrimRenderSubmission? _pendingSubmission;
    // When true, UploadSubmission will call FrameBoundsFront instead of FrameBounds.
    private volatile bool _frameFrontPending;

    // ── Mouse state ──────────────────────────────────────────────────────────────

    private Point _lastPointer;
    private Point _pressPointer;
    private bool  _leftDown;
    private bool  _rightDown;
    private bool  _dragged;
    private volatile bool _pickRequested;
    private Point _pickPoint;
    // ── Saved bounds for camera reset ────────────────────────────────────────────

    private Vector3 _lastBoundsMin = new Vector3(-0.5f);
    private Vector3 _lastBoundsMax = new Vector3( 0.5f);

    // ── Constructor ──────────────────────────────────────────────────────────────

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
        _lastPointer  = e.GetPosition(this);
        _pressPointer = _lastPointer;
        _dragged      = false;
        var props      = e.GetCurrentPoint(this).Properties;
        _leftDown      = props.IsLeftButtonPressed;
        _rightDown     = props.IsRightButtonPressed;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // If left button released without dragging, schedule a picking pass.
        if (_leftDown && !_dragged)
        {
            _pickPoint     = _pressPointer;
            _pickRequested = true;
            _core.RequestNextFrameRendering();
        }
        _leftDown  = false;
        _rightDown = false;
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

        // Compare total displacement from the press origin (not per-event delta) so that
        // accumulated subpixel jitter on trackpads/touchscreens doesn't falsely mark
        // a stationary tap as a drag.
        if (Math.Abs(pos.X - _pressPointer.X) > 4 || Math.Abs(pos.Y - _pressPointer.Y) > 4)
            _dragged = true;

        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        if (_leftDown && !ctrl)
            _camera.OrbitDrag(dx, dy);
        else if (_rightDown || (_leftDown && ctrl))
            _camera.PanDrag(dx, dy);

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
    /// Fired (on the UI thread) when the user clicks a face in the viewport.
    /// Arguments are (primLocalId, faceIndex).
    /// </summary>
    public event Action<uint, int>? FaceClicked;

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

    // ── OpenGL lifecycle ─────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Ensure a render is requested after the first layout pass sets real Bounds.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            _core.RequestNextFrameRendering,
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void GlInit(GlInterface gl)
    {
        try
        {
            GL.LoadBindings(new AvaloniaGlBindings(gl));
            _primShader = GlShader.Compile(
                ShaderLoader.Load("prim.vert"),
                ShaderLoader.Load("prim.frag"));
            _wireShader = GlShader.Compile(
                ShaderLoader.Load("wireframe.vert"),
                ShaderLoader.Load("wireframe.frag"));
            _pickShader = GlShader.Compile(
                ShaderLoader.Load("wireframe.vert"),
                ShaderLoader.Load("picking.frag"));
            // GL ES (ANGLE) doesn't expose PolygonMode; check version string.
            var version = GL.GetString(StringName.Version) ?? "";
            _supportsPolygonMode = !version.Contains("OpenGL ES");
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
        // Consume any pending geometry update — only upload if GL init succeeded.
        var pending = Interlocked.Exchange(ref _pendingSubmission, null);
        if (pending != null && _initError == null)
            UploadSubmission(pending);

        // Use physical pixels for the GL viewport to handle HiDPI correctly.
        var scaling = VisualRoot?.RenderScaling ?? 1.0;
        int w = (int)(Bounds.Width  * scaling);
        int h = (int)(Bounds.Height * scaling);
        if (w <= 0 || h <= 0)
        {
            // Bounds not yet set — request another render after layout.
            _core.RequestNextFrameRendering();
            return;
        }

        GL.Viewport(0, 0, w, h);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        // Use a visible error colour if init failed so the failure is obvious.
        GL.ClearColor(_initError != null ? 0.55f : 0.39f,
                      _initError != null ? 0.10f : 0.58f,
                      _initError != null ? 0.10f : 0.93f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_primShader == null) return;

        float aspect = (float)w / h;
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);

        // Opaque pass
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        DrawFaces(_opaque, _primShader, ref view, ref proj);

        // Alpha pass — depth-sorted back-to-front, two-sided.
        if (_alpha.Count > 0)
        {
            if (_alpha.Count > 1)
            {
                var eye = _camera.EyePosition;
                _alpha.Sort((a, b) =>
                {
                    float da = (a.face.Transform.Row3.Xyz - eye).LengthSquared;
                    float db = (b.face.Transform.Row3.Xyz - eye).LengthSquared;
                    return db.CompareTo(da);
                });
            }

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);
            DrawFaces(_alpha, _primShader, ref view, ref proj);
            GL.Enable(EnableCap.CullFace);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }

        // Wireframe overlay.
        // On desktop GL: use PolygonMode for true wireframe.
        // On ES/ANGLE: re-draw with the wireframe shader as solid triangles at slightly
        // reduced depth so edges are visible (barycentric wireframe would be ideal but
        // requires geometry shader support; this is a reasonable approximation).
        if (Wireframe && _wireShader != null)
        {
            GL.Disable(EnableCap.CullFace);
            if (_supportsPolygonMode)
            {
                GL.Enable(EnableCap.PolygonOffsetLine);
                GL.PolygonOffset(-1f, -1f);
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                DrawFacesWireframe(_opaque, _wireShader, ref view, ref proj);
                DrawFacesWireframe(_alpha,  _wireShader, ref view, ref proj);
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                GL.Disable(EnableCap.PolygonOffsetLine);
            }
            else
            {
                // ES fallback: draw edges via line EBO.
                // LEQUAL lets co-planar edges pass the depth test against the solid surface.
                GL.DepthFunc(DepthFunction.Lequal);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false);
                DrawFacesWireframeEs(_opaque, _wireShader, ref view, ref proj);
                DrawFacesWireframeEs(_alpha,  _wireShader, ref view, ref proj);
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
                GL.DepthFunc(DepthFunction.Less);
            }
            GL.Enable(EnableCap.CullFace);
        }

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
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pickFbo);
                GL.Viewport(0, 0, w, h);
                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                // Set explicit state — don't rely on inherited values from the main pass.
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Less);
                GL.Disable(EnableCap.Blend);
                GL.DepthMask(true);
                GL.Disable(EnableCap.CullFace);
                GL.ColorMask(true, true, true, true);

                DrawFacesPicking(_opaque, _pickShader, ref view, ref proj, 1);
                DrawFacesPicking(_alpha,  _pickShader, ref view, ref proj, 1 + _opaque.Count);

                GL.Flush(); // ensure all draw calls are complete before ReadPixels
                byte[] pixel = new byte[4];
                GL.ReadPixels(px, py, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

                // Restore Avalonia's framebuffer (main scene already rendered there).
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, fb);
                GL.Enable(EnableCap.CullFace);

                // R+G encode a 1-based index into _pickMap (background cleared to 0,0,0,0).
                // Non-zero R or G means a face was hit; look up the real LocalId/FaceIndex.
                if (pixel[0] != 0 || pixel[1] != 0)
                {
                    uint idx = (uint)(pixel[0] | (pixel[1] << 8)); // 1-based
                    if (idx >= 1 && (int)idx <= _pickMap.Count)
                    {
                        var (primLocalId, faceIndex) = _pickMap[(int)(idx - 1)];
                        Avalonia.Threading.Dispatcher.UIThread.Post(
                            () => FaceClicked?.Invoke(primLocalId, faceIndex));
                    }
                }
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

        FreeGpuResources();
        DeletePickFbo();
        _primShader?.Dispose(); _primShader = null;
        _wireShader?.Dispose(); _wireShader = null;
        _pickShader?.Dispose(); _pickShader = null;
    }

    private static void DisposePendingBitmaps(PrimRenderSubmission sub)
    {
        foreach (var face in sub.Faces)
            face.Texture?.Dispose();
    }

    private void EnsurePickFbo(int w, int h)
    {
        if (_pickFbo != 0 && _pickFboW == w && _pickFboH == h) return;

        DeletePickFbo();

        _pickFbo   = GL.GenFramebuffer();
        _pickRbo   = GL.GenRenderbuffer();
        _pickDepth = GL.GenRenderbuffer();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pickFbo);

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _pickRbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            RenderbufferStorage.Rgba8, w, h);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _pickRbo);

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _pickDepth);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            RenderbufferStorage.DepthComponent16, w, h);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _pickDepth);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        if (status != FramebufferErrorCode.FramebufferComplete)
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
        GL.DeleteFramebuffer(_pickFbo);
        GL.DeleteRenderbuffer(_pickRbo);
        GL.DeleteRenderbuffer(_pickDepth);
        _pickFbo = _pickRbo = _pickDepth = 0;
        _pickFboW = _pickFboH = 0;
    }

    // ── Rendering helpers ────────────────────────────────────────────────────────

    private void UploadSubmission(PrimRenderSubmission sub)
    {
        FreeGpuResources();
        _lastBoundsMin = sub.BoundsMin;
        _lastBoundsMax = sub.BoundsMax;

        if (_frameFrontPending)
        {
            _frameFrontPending = false;
            _camera.FrameBoundsFront(sub.BoundsMin, sub.BoundsMax);
        }
        // else: live re-tessellation — preserve current camera position.

        // Deduplicate textures: multiple faces may reference the same SKBitmap instance.
        // Upload each unique bitmap only once, then dispose all bitmaps together.
        var texCache    = new Dictionary<IntPtr, GlTexture>();
        var bmpToDispose = new HashSet<IntPtr>();

        foreach (var face in sub.Faces)
        {
            var mesh = new GlMesh(face.Vertices, face.Indices);
            GlTexture? tex = null;

            if (face.Texture != null)
            {
                var handle = face.Texture.Handle;
                bmpToDispose.Add(handle);

                if (!texCache.TryGetValue(handle, out tex))
                {
                    try
                    {
                        tex = new GlTexture(face.Texture);
                        texCache[handle] = tex;
                    }
                    catch { /* texture upload failed — render untextured */ }
                }
            }

            if (face.HasAlpha)
                _alpha.Add((mesh, tex, face));
            else
                _opaque.Add((mesh, tex, face));
        }

        // Dispose all source bitmaps now that they are on the GPU (or failed).
        foreach (var face in sub.Faces)
        {
            if (face.Texture != null && bmpToDispose.Remove(face.Texture.Handle))
                face.Texture.Dispose();
        }

        // Rebuild pick map: 1-based index → (LocalId, FaceIndex).
        // Opaque faces occupy slots 1..N, alpha faces occupy N+1..N+M.
        _pickMap.Clear();
        foreach (var (_, _, face) in _opaque) _pickMap.Add((face.PrimLocalId, face.FaceIndex));
        foreach (var (_, _, face) in _alpha)  _pickMap.Add((face.PrimLocalId, face.FaceIndex));
    }

    private void FreeGpuResources()
    {
        // Collect unique GlTexture instances to avoid double-dispose when faces share a texture.
        var textures = new HashSet<GlTexture>();
        foreach (var (mesh, tex, _) in _opaque) { mesh.Dispose(); if (tex != null) textures.Add(tex); }
        foreach (var (mesh, tex, _) in _alpha)  { mesh.Dispose(); if (tex != null) textures.Add(tex); }
        foreach (var tex in textures) tex.Dispose();
        _opaque.Clear();
        _alpha.Clear();
        _pickMap.Clear();
    }

    private static void DrawFaces(
        List<(GlMesh mesh, GlTexture? tex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4 view,
        ref Matrix4 proj)
    {
        shader.Use();
        foreach (var (mesh, tex, face) in list)
        {
            var model = face.Transform;
            var mv    = model * view;
            var mvp   = mv * proj;

            // Normal matrix = (MV^-1)^T — we pass the inverse and let GL transpose it.
            var mvInv     = Matrix4.Invert(mv);
            var normalMat = new Matrix3(
                mvInv.Row0.Xyz,
                mvInv.Row1.Xyz,
                mvInv.Row2.Xyz);

            shader.Set("uMvp",       ref mvp);
            shader.Set("uModelView", ref mv);
            shader.Set("uNormalMat", ref normalMat, transpose: true);
            shader.Set("uColor",     face.Color);
            shader.Set("uFullbright", face.Fullbright);
            shader.Set("uGlow",      face.Glow);

            bool hasTex = tex != null;
            shader.Set("uHasTexture", hasTex);
            if (hasTex)
            {
                tex!.Bind(TextureUnit.Texture0);
                shader.Set("uAlbedo", 0);
            }

            mesh.Draw();
        }
        shader.Unuse();
    }

    private static void DrawFacesWireframe(
        List<(GlMesh mesh, GlTexture? tex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4 view,
        ref Matrix4 proj)
    {
        shader.Use();
        foreach (var (mesh, _, face) in list)
        {
            var mvp = face.Transform * view * proj;
            shader.Set("uMvp", ref mvp);
            mesh.Draw();
        }
        shader.Unuse();
    }

    // ES fallback: draw each mesh's edges as GL_LINES derived from its triangle indices.
    private static void DrawFacesWireframeEs(
        List<(GlMesh mesh, GlTexture? tex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4 view,
        ref Matrix4 proj)
    {
        shader.Use();
        foreach (var (mesh, _, face) in list)
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
        List<(GlMesh mesh, GlTexture? tex, PrimRenderFace face)> list,
        GlShader shader,
        ref Matrix4 view,
        ref Matrix4 proj,
        int startIdx)
    {
        shader.Use();
        for (int i = 0; i < list.Count; i++)
        {
            var (mesh, _, face) = list[i];
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
