// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// Copyright (c) 2019-2025, Sjofn LLC
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Radegast", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id$
//

#region Usings
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using CoreJ2K;
using OpenTK.Graphics.OpenGL;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

#endregion Usings

namespace Radegast.Rendering
{
    /// <summary>
    /// Rendering state for the Object Viewer window
    /// </summary>
    public enum ObjectViewerState
    {
        Uninitialized,
        Initializing,
        Ready,
        Rendering,
        Disposed
    }

    public partial class frmPrimWorkshop : RadegastForm
    {
        #region Constants
        // OpenGL Configuration
        private const int GL_DEPTH_BITS = 24;
        private const int GL_STENCIL_BITS = 8;
        private const int GL_NO_SAMPLES = 0;
        private const int GL_MAX_AA_SAMPLES = 4;
        private const int GL_AA_STEP = 2;

        // Lighting
        private const float LIGHT_AMBIENT = 0.5f;
        private const float LIGHT_DIFFUSE = 0.3f;
        private const float LIGHT_SPECULAR = 0.8f;
        private const float LIGHT_ALPHA = 1.0f;

        // Background Colors
        private const float BACKGROUND_SKY_R = 0.39f;
        private const float BACKGROUND_SKY_G = 0.58f;
        private const float BACKGROUND_SKY_B = 0.93f;
        private const float BACKGROUND_ALPHA = 1.0f;
        private const float PICKING_BACKGROUND_COLOR = 1f;

        // Camera & View
        private const float CAMERA_FOV = 50f;
        private const float CAMERA_NEAR_PLANE = 0.1f;
        private const float CAMERA_FAR_PLANE = 100.0f;
        private const double ZOOM_SCALE_FACTOR = 0.1d;

        // Mouse & Input
        private const float PAN_SENSITIVITY_X = 100f;
        private const float PAN_SENSITIVITY_Z = 100f;
        private const float PAN_SENSITIVITY_Y = 25f;
        private const int ROTATION_ANGLE_MAX = 360;
        private const int MOUSE_WHEEL_DIVISOR = 10;

        // Default View Values
        private const int DEFAULT_YAW = 90;
        private const int DEFAULT_PITCH = 0;
        private const int DEFAULT_ROLL = 0;
        private const int DEFAULT_ZOOM = -30;

        // Text Rendering
        private const float TEXT_VERTICAL_OFFSET_MULTIPLIER = 0.8f;
        private const int TEXT_SHADOW_OFFSET = 1;
        private const float TEXT_FONT_SIZE = 10f;
        private const int ALPHA_CHANNEL_MAX = 255;

        // Shininess Materials
        private const float SHININESS_HIGH = 94f;
        private const float SHININESS_MEDIUM = 64f;
        private const float SHININESS_LOW = 24f;
        private const float SHININESS_NONE = 0f;

        // Alpha Thresholds
        private const float ALPHA_TEST_THRESHOLD = 0.5f;
        private const float ALPHA_PASS_THRESHOLD = 0.99f;
        private const float ALPHA_TRANSPARENT_THRESHOLD = 0.01f;

        // Thread Configuration
        private const int THREAD_JOIN_TIMEOUT_SECONDS = 2;
        private const int TEXTURE_THREAD_SLEEP_MS = 10;

        // Asset Loading
        private const int MESH_REQUEST_TIMEOUT_MS = 20000;
        private const int TEXTURE_REQUEST_TIMEOUT_MS = 30000;

        // Vertex Data Layout
        private const int VERTEX_COMPONENTS = 3;  // X, Y, Z
        private const int TEXCOORD_COMPONENTS = 2; // U, V
        private const int NORMAL_COMPONENTS = 3;   // X, Y, Z

        // Picking
        private const byte PICKING_ALPHA_CHANNEL = 255;
        #endregion Constants

        #region Public fields
        /// <summary>
        /// The OpenGL surface
        /// </summary>
        public OpenTK.GLControl glControl = null;

        /// <summary>
        /// Use multi sampling (anti aliasing)
        /// </summary>
        public bool UseMultiSampling = true;

        /// <summary>
        /// Is rendering engine ready and enabled
        /// </summary>
        public bool RenderingEnabled
        {
            get => renderingEnabled;
            private set
            {
                if (renderingEnabled != value)
                {
                    renderingEnabled = value;
                    UpdateState();
                }
            }
        }

        /// <summary>
        /// Render in wireframe mode
        /// </summary>
        public bool Wireframe = false;

        /// <summary>
        /// List of prims in the scene
        /// </summary>
        private readonly ConcurrentDictionary<uint, FacetedMesh> Prims = new ConcurrentDictionary<uint, FacetedMesh>();

        /// <summary>
        /// Local ID of the root prim
        /// </summary>
        public uint RootPrimLocalID = 0;

        /// <summary>
        /// Camera center
        /// </summary>
        public Vector3 Center = Vector3.Zero;

        /// <summary>
        /// Current state of the object viewer
        /// </summary>
        public ObjectViewerState State { get; private set; } = ObjectViewerState.Uninitialized;
        #endregion Public fields

        #region Private fields

        private readonly ConcurrentDictionary<UUID, TextureInfo> TexturesPtrMap = new ConcurrentDictionary<UUID, TextureInfo>();
        private readonly RadegastInstance instance;
        private readonly MeshmerizerR renderer;
        private OpenTK.Graphics.GraphicsMode GLMode = null;
        private readonly ConcurrentQueue<TextureLoadItem> PendingTextures = new ConcurrentQueue<TextureLoadItem>();
        private readonly float[] lightPos = new float[] { 0f, 0f, 1f, 0f };
        private TextRendering textRendering;
        private OpenTK.Matrix4 ModelMatrix;
        private OpenTK.Matrix4 ProjectionMatrix;
        private readonly int[] Viewport = new int[4];
        private bool disposed = false;
        private bool renderingEnabled = false;
        private Thread textureThread;
        private volatile bool textureThreadRunning = false;

        #endregion Private fields

        #region Construction and disposal
        public frmPrimWorkshop(RadegastInstanceForms instance, uint rootLocalID)
            : base(instance)
        {
            State = ObjectViewerState.Initializing;
            RootPrimLocalID = rootLocalID;

            InitializeComponent();
            Disposed += FrmPrimWorkshop_Disposed;
            AutoSavePosition = true;
            UseMultiSampling = cbAA.Checked = instance.GlobalSettings["use_multi_sampling"];
            cbAA.CheckedChanged += cbAA_CheckedChanged;

            this.instance = instance;

            renderer = new MeshmerizerR();
            textRendering = new TextRendering(instance);

            Client.Objects.TerseObjectUpdate += Objects_TerseObjectUpdate;
            Client.Objects.ObjectUpdate += Objects_ObjectUpdate;
            Client.Objects.ObjectDataBlockUpdate += Objects_ObjectDataBlockUpdate;

            GUI.GuiHelpers.ApplyGuiFixes(this);

            State = ObjectViewerState.Ready;
        }

        private void FrmPrimWorkshop_Disposed(object sender, EventArgs e)
        {
            PerformCustomDisposal();
        }

        private void PerformCustomDisposal()
        {
            if (disposed) return;

            State = ObjectViewerState.Disposed;
            disposed = true;
            RenderingEnabled = false;

            DisposeThreads();
            DisposeUIComponents();
            DisposePrims();
            DisposeGLControl();
            UnregisterEventHandlers();
        }

        private void UpdateState()
        {
            if (disposed)
            {
                State = ObjectViewerState.Disposed;
            }
            else if (RenderingEnabled)
            {
                State = ObjectViewerState.Rendering;
            }
            else if (State != ObjectViewerState.Initializing)
            {
                State = ObjectViewerState.Ready;
            }
        }

        private void DisposeThreads()
        {
            textureThreadRunning = false;

            // Clear pending textures
            while (PendingTextures.TryDequeue(out _)) { }

            // Wait for texture thread to exit
            if (textureThread != null && textureThread.IsAlive)
            {
                try
                {
                    if (!textureThread.Join(TimeSpan.FromSeconds(THREAD_JOIN_TIMEOUT_SECONDS)))
                    {
                        Logger.Debug("Texture thread did not exit in time", Client);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error waiting for texture thread: {ex.Message}", Client);
                }
                textureThread = null;
            }
        }

        private void DisposeUIComponents()
        {
            if (textRendering != null)
            {
                try
                {
                    textRendering.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error disposing textRendering: {ex.Message}", Client);
                }
                textRendering = null;
            }
        }

        private void DisposePrims()
        {
            // Clear the prims dictionary
            Prims.Clear();
        }

        private void DisposeGLControl()
        {
            if (glControl != null)
            {
                try
                {
                    glControl.Paint -= glControl_Paint;
                    glControl.Resize -= glControl_Resize;
                    glControl.MouseDown -= glControl_MouseDown;
                    glControl.MouseUp -= glControl_MouseUp;
                    glControl.MouseMove -= glControl_MouseMove;
                    glControl.MouseWheel -= glControl_MouseWheel;
                    glControl.Load -= glControl_Load;
                    glControl.Disposed -= glControl_Disposed;

                    if (!glControl.IsDisposed)
                    {
                        glControl.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error disposing glControl: {ex.Message}", Client);
                }
                glControl = null;
            }
        }

        private void UnregisterEventHandlers()
        {
            try
            {
                Client.Objects.TerseObjectUpdate -= Objects_TerseObjectUpdate;
                Client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
                Client.Objects.ObjectDataBlockUpdate -= Objects_ObjectDataBlockUpdate;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error unregistering event handlers: {ex.Message}", Client);
            }
        }
        #endregion Construction and disposal

        #region Network messaage handlers

        private void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (disposed) return;
            if (Prims.ContainsKey(e.Prim.LocalID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }

        private void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if (disposed) return;
            if (Prims.ContainsKey(e.Prim.LocalID) || Prims.ContainsKey(e.Prim.ParentID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }

        private void Objects_ObjectDataBlockUpdate(object sender, ObjectDataBlockUpdateEventArgs e)
        {
            if (disposed) return;
            if (Prims.ContainsKey(e.Prim.LocalID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }
        #endregion Network messaage handlers

        #region glControl setup and disposal
        public void SetupGLControl()
        {
            State = ObjectViewerState.Initializing;
            RenderingEnabled = false;

            glControl?.Dispose();
            glControl = null;

            GLMode = null;

            try
            {
                if (!UseMultiSampling)
                {
                    GLMode = new OpenTK.Graphics.GraphicsMode(
                        OpenTK.DisplayDevice.Default.BitsPerPixel, 
                        GL_DEPTH_BITS, 
                        GL_STENCIL_BITS, 
                        GL_NO_SAMPLES);
                }
                else
                {
                    for (int aa = GL_NO_SAMPLES; aa <= GL_MAX_AA_SAMPLES; aa += GL_AA_STEP)
                    {
                        var testMode = new OpenTK.Graphics.GraphicsMode(
                            OpenTK.DisplayDevice.Default.BitsPerPixel, 
                            GL_DEPTH_BITS, 
                            GL_STENCIL_BITS, 
                            aa);
                        if (testMode.Samples == aa)
                        {
                            GLMode = testMode;
                        }
                    }
                }
            }
            catch
            {
                GLMode = null;
            }


            try
            {
                // Try default mode
                glControl = GLMode == null ? new OpenTK.GLControl() : new OpenTK.GLControl(GLMode);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex.Message, ex, Client);
                glControl = null;
            }

            if (glControl == null)
            {
                Logger.Error("Failed to initialize OpenGL control, cannot continue", Client);
                State = ObjectViewerState.Ready;
                return;
            }

            Logger.Info("Initializing OpenGL mode: " + (GLMode == null ? "" : GLMode.ToString()));

            glControl.Paint += glControl_Paint;
            glControl.Resize += glControl_Resize;
            glControl.MouseDown += glControl_MouseDown;
            glControl.MouseUp += glControl_MouseUp;
            glControl.MouseMove += glControl_MouseMove;
            glControl.MouseWheel += glControl_MouseWheel;
            glControl.Load += glControl_Load;
            glControl.Disposed += glControl_Disposed;
            glControl.Dock = DockStyle.Fill;
            Controls.Add(glControl);
            glControl.BringToFront();
        }

        private void glControl_Disposed(object sender, EventArgs e)
        {
            textureThreadRunning = false;
            while (PendingTextures.TryDequeue(out _)) { }
        }

        private void glControl_Load(object sender, EventArgs e)
        {
            try
            {
                GL.ShadeModel(ShadingModel.Smooth);
                GL.ClearColor(0f, 0f, 0f, 0f);

                GL.Enable(EnableCap.Lighting);
                GL.Enable(EnableCap.Light0);
                GL.Light(LightName.Light0, LightParameter.Ambient, new float[] { LIGHT_AMBIENT, LIGHT_AMBIENT, LIGHT_AMBIENT, LIGHT_ALPHA });
                GL.Light(LightName.Light0, LightParameter.Diffuse, new float[] { LIGHT_DIFFUSE, LIGHT_DIFFUSE, LIGHT_DIFFUSE, LIGHT_ALPHA });
                GL.Light(LightName.Light0, LightParameter.Specular, new float[] { LIGHT_SPECULAR, LIGHT_SPECULAR, LIGHT_SPECULAR, LIGHT_ALPHA });
                GL.Light(LightName.Light0, LightParameter.Position, lightPos);

                GL.ClearDepth(1.0d);
                GL.Enable(EnableCap.DepthTest);
                GL.Enable(EnableCap.ColorMaterial);
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceMode.Back);
                GL.ColorMaterial(MaterialFace.Front, ColorMaterialParameter.AmbientAndDiffuse);
                GL.ColorMaterial(MaterialFace.Front, ColorMaterialParameter.Specular);

                GL.DepthMask(true);
                GL.DepthFunc(DepthFunction.Lequal);
                GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
                GL.MatrixMode(MatrixMode.Projection);

                GL.Enable(EnableCap.Blend);
                GL.AlphaFunc(AlphaFunction.Greater, ALPHA_TEST_THRESHOLD);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                #region Compatibility checks
                OpenTK.Graphics.IGraphicsContextInternal context = glControl.Context as OpenTK.Graphics.IGraphicsContextInternal;
                string glExtensions = GL.GetString(StringName.Extensions);

                // VBO
                RenderSettings.ARBVBOPresent = context.GetAddress("glGenBuffersARB") != IntPtr.Zero;
                RenderSettings.CoreVBOPresent = context.GetAddress("glGenBuffers") != IntPtr.Zero;
                RenderSettings.UseVBO = (RenderSettings.ARBVBOPresent || RenderSettings.CoreVBOPresent)
                    && instance.GlobalSettings["rendering_use_vbo"];

                // Occlusion Query
                RenderSettings.ARBQuerySupported = context.GetAddress("glGetQueryObjectivARB") != IntPtr.Zero;
                RenderSettings.CoreQuerySupported = context.GetAddress("glGetQueryObjectiv") != IntPtr.Zero;
                RenderSettings.OcclusionCullingEnabled = (RenderSettings.CoreQuerySupported || RenderSettings.ARBQuerySupported)
                    && instance.GlobalSettings["rendering_occlusion_culling_enabled2"];

                // Mipmap
                RenderSettings.HasMipmap = context.GetAddress("glGenerateMipmap") != IntPtr.Zero;

                // Shader support
                RenderSettings.HasShaders = glExtensions.Contains("vertex_shader") && glExtensions.Contains("fragment_shader");

                // Multi texture
                RenderSettings.HasMultiTexturing = context.GetAddress("glMultiTexCoord2f") != IntPtr.Zero;
                RenderSettings.WaterReflections = instance.GlobalSettings["water_reflections"];

                if (!RenderSettings.HasMultiTexturing || !RenderSettings.HasShaders)
                {
                    RenderSettings.WaterReflections = false;
                }

                // Do textures have to have dimensions that are powers of two
                RenderSettings.TextureNonPowerOfTwoSupported = glExtensions.Contains("texture_non_power_of_two");

                // Occlusion culling
                RenderSettings.OcclusionCullingEnabled = Instance.GlobalSettings["rendering_occlusion_culling_enabled2"]
                    && (RenderSettings.ARBQuerySupported || RenderSettings.CoreQuerySupported);

                // Shiny
                RenderSettings.EnableShiny = Instance.GlobalSettings["scene_viewer_shiny"];
                #endregion Compatibility checks

                RenderingEnabled = true;
                // Call the resizing function which sets up the GL drawing window
                // and will also invalidate the GL control
                glControl_Resize(null, null);

                var textureThread = new Thread(TextureThread)
                {
                    IsBackground = true,
                    Name = "TextureLoadingThread"
                };
                textureThread.Start();
            }
            catch (Exception ex)
            {
                RenderingEnabled = false;
                Logger.Warn("Failed to initialize OpenGL control", ex, Client);
            }
        }
        #endregion glControl setup and disposal

        #region glControl paint and resize events
        private void glControl_Paint(object sender, PaintEventArgs e)
        {
            if (!RenderingEnabled) return;

            Render(false);

            glControl.SwapBuffers();

            Primitive prim;
            string objName = string.Empty;

            if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(RootPrimLocalID, out prim))
            {
                if (prim.Properties != null)
                {
                    objName = prim.Properties.Name;
                }
            }

            string title = string.Format("Object Viewer - {0}", instance.Names.Get(Client.Self.AgentID, Client.Self.Name));
            if (!string.IsNullOrEmpty(objName))
            {
                title += string.Format(" - {0}", objName);
            }

            Text = title;
        }

        private void glControl_Resize(object sender, EventArgs e)
        {
            if (!RenderingEnabled) return;
            glControl.MakeCurrent();

            GL.ClearColor(BACKGROUND_SKY_R, BACKGROUND_SKY_G, BACKGROUND_SKY_B, BACKGROUND_ALPHA);

            GL.Viewport(0, 0, glControl.Width, glControl.Height);

            GL.PushMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            float dAspRat = (float)glControl.Width / (float)glControl.Height;
            GluPerspective(CAMERA_FOV, dAspRat, CAMERA_NEAR_PLANE, CAMERA_FAR_PLANE);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();
        }
        #endregion glControl paint and resize events

        #region Mouse handling

        private bool dragging = false;
        private int dragX, dragY, downX, downY;

        private void glControl_MouseWheel(object sender, MouseEventArgs e)
        {
            int newVal = Utils.Clamp(scrollZoom.Value + e.Delta / MOUSE_WHEEL_DIVISOR, scrollZoom.Minimum, scrollZoom.Maximum);

            if (scrollZoom.Value != newVal)
            {
                scrollZoom.Value = newVal;
                glControl_Resize(null, null);
                SafeInvalidate();
            }
        }

        private FacetedMesh RightclickedPrim;
        private int RightclickedFaceID;

        private void glControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                dragging = true;
                downX = dragX = e.X;
                downY = dragY = e.Y;
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (TryPick(e.X, e.Y, out RightclickedPrim, out RightclickedFaceID))
                {
                    ctxObjects.Show(glControl, e.X, e.Y);
                }
            }

        }

        private void glControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                int deltaX = e.X - dragX;
                int deltaY = e.Y - dragY;

                if (e.Button == MouseButtons.Left)
                {
                    if (ModifierKeys == Keys.Control || ModifierKeys == (Keys.Alt | Keys.Control | Keys.Shift))
                    {
                        Center.X -= deltaX / PAN_SENSITIVITY_X;
                        Center.Z += deltaY / PAN_SENSITIVITY_Z;
                    }

                    if (ModifierKeys == Keys.Alt)
                    {
                        Center.Y -= deltaY / PAN_SENSITIVITY_Y;

                        int newYaw = scrollYaw.Value + deltaX;
                        if (newYaw < 0) newYaw += ROTATION_ANGLE_MAX;
                        if (newYaw > ROTATION_ANGLE_MAX) newYaw -= ROTATION_ANGLE_MAX;

                        scrollYaw.Value = newYaw;

                    }

                    if (ModifierKeys == Keys.None || ModifierKeys == (Keys.Alt | Keys.Control))
                    {
                        int newRoll = scrollRoll.Value + deltaY;
                        if (newRoll < 0) newRoll += ROTATION_ANGLE_MAX;
                        if (newRoll > ROTATION_ANGLE_MAX) newRoll -= ROTATION_ANGLE_MAX;

                        scrollRoll.Value = newRoll;


                        int newYaw = scrollYaw.Value + deltaX;
                        if (newYaw < 0) newYaw += ROTATION_ANGLE_MAX;
                        if (newYaw > ROTATION_ANGLE_MAX) newYaw -= ROTATION_ANGLE_MAX;

                        scrollYaw.Value = newYaw;

                    }
                }
                else if (e.Button == MouseButtons.Middle)
                {
                    Center.X -= deltaX / PAN_SENSITIVITY_X;
                    Center.Z += deltaY / PAN_SENSITIVITY_Z;

                }

                dragX = e.X;
                dragY = e.Y;

                SafeInvalidate();
            }
        }

        private void glControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;

                if (e.X == downX && e.Y == downY) // click
                {
                    FacetedMesh picked;
                    int faceID;
                    if (TryPick(e.X, e.Y, out picked, out faceID))
                    {
                        Client.Self.Grab(picked.Prim.LocalID, Vector3.Zero, Vector3.Zero, Vector3.Zero, faceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                        Client.Self.DeGrab(picked.Prim.LocalID, Vector3.Zero, Vector3.Zero, faceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                    }
                }
                SafeInvalidate();
            }
        }
        #endregion Mouse handling

        #region Texture thread

        private void TextureThread()
        {
            Logger.DebugLog("Started Texture Thread");

            textureThreadRunning = true;

            while (textureThreadRunning)
            {
                if (!PendingTextures.TryDequeue(out var item))
                {
                    Thread.Sleep(TEXTURE_THREAD_SLEEP_MS);
                    continue;
                }

                if (disposed || Disposing || IsDisposed)
                    break;

                if (TexturesPtrMap.ContainsKey(item.TeFace.TextureID))
                {
                    item.Data.TextureInfo = TexturesPtrMap[item.TeFace.TextureID];
                    continue;
                }

                if (LoadTexture(item.TeFace.TextureID, ref item.Data.TextureInfo.Texture, false))
                {
                    Bitmap bitmap = null;
                    try
                    {
                        bitmap = item.Data.TextureInfo.Texture.ToBitmap();

                        bool hasAlpha = bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb;
                        
                        item.Data.TextureInfo.HasAlpha = hasAlpha;

                        bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);

                        var loadOnMainThread = new MethodInvoker(() =>
                        {
                            try
                            {
                                if (!disposed && !Disposing && !IsDisposed)
                                {
                                    item.Data.TextureInfo.TexturePointer = RHelp.GLLoadImage(bitmap, hasAlpha, RenderSettings.HasMipmap);
                                    TexturesPtrMap[item.TeFace.TextureID] = item.Data.TextureInfo;
                                    item.Data.TextureInfo.Texture = null;
                                    SafeInvalidate();
                                }
                            }
                            finally
                            {
                                bitmap?.Dispose();
                            }
                        });

                        if (disposed || Disposing || IsDisposed)
                        {
                            bitmap?.Dispose();
                            break;
                        }

                        if (!instance.MonoRuntime || IsHandleCreated)
                        {
                            BeginInvoke(loadOnMainThread);
                        }
                        else
                        {
                            bitmap?.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error processing texture: {ex.Message}", Client);
                        bitmap?.Dispose();
                    }
                }
            }
            Logger.DebugLog("Texture thread exited");
        }
        #endregion Texture thread

        private void FrmPrimWorkshop_Shown(object sender, EventArgs e)
        {
            SetupGLControl();

            ThreadPool.QueueUserWorkItem(sync =>
                {
                    if (Client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(RootPrimLocalID))
                    {
                        UpdatePrimBlocking(Client.Network.CurrentSim.ObjectsPrimitives[RootPrimLocalID]);
                        var children = (from p in Client.Network.CurrentSim.ObjectsPrimitives
                            where p.Value != null
                            where p.Value.ParentID == RootPrimLocalID
                            select p.Value).ToList();
                        children.ForEach(UpdatePrimBlocking);
                    }
                }
            );

        }

        #region Public methods
        public void SetView(Vector3 center, int roll, int pitch, int yaw, int zoom)
        {
            Center = center;
            scrollRoll.Value = roll;
            scrollPitch.Value = pitch;
            scrollYaw.Value = yaw;
            scrollZoom.Value = zoom;
        }
        #endregion Public methods

        #region Private methods (the meat)

        private void RenderText()
        {
            if (disposed) return;

            int primNr = 0;
            foreach (FacetedMesh mesh in Prims.Values)
            {
                primNr++;
                Primitive prim = mesh.Prim;
                if (string.IsNullOrEmpty(prim.Text)) continue;

                string text = System.Text.RegularExpressions.Regex.Replace(prim.Text, "(\r?\n)+", "\n");
                OpenTK.Vector3 screenPos = OpenTK.Vector3.Zero;
                OpenTK.Vector3 primPos = OpenTK.Vector3.Zero;

                // Is it child prim
                if (Prims.TryGetValue(prim.ParentID, out var parent))
                {
                    var newPrimPos = prim.Position * Matrix4.CreateFromQuaternion(parent.Prim.Rotation);
                    primPos = new OpenTK.Vector3(newPrimPos.X, newPrimPos.Y, newPrimPos.Z);
                }

                primPos.Z += prim.Scale.Z * TEXT_VERTICAL_OFFSET_MULTIPLIER;
                if (!GLU.Project(primPos, ModelMatrix, ProjectionMatrix, Viewport, out screenPos)) continue;
                screenPos.Y = glControl.Height - screenPos.Y;

                textRendering.Begin();

                Color color = Color.FromArgb(
                    (int)(prim.TextColor.A * ALPHA_CHANNEL_MAX), 
                    (int)(prim.TextColor.R * ALPHA_CHANNEL_MAX), 
                    (int)(prim.TextColor.G * ALPHA_CHANNEL_MAX), 
                    (int)(prim.TextColor.B * ALPHA_CHANNEL_MAX));
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;

                using (Font f = new Font(FontFamily.GenericSansSerif, TEXT_FONT_SIZE, FontStyle.Regular))
                {
                    var size = TextRendering.Measure(text, f, flags);
                    screenPos.X -= size.Width / 2;
                    screenPos.Y -= size.Height;

                    // Shadow
                    if (color != Color.Black)
                    {
                        textRendering.Print(text, f, Color.Black, 
                            new Rectangle((int)screenPos.X + TEXT_SHADOW_OFFSET, 
                                          (int)screenPos.Y + TEXT_SHADOW_OFFSET, 
                                          size.Width, size.Height), flags);
                    }
                    textRendering.Print(text, f, color, new Rectangle((int)screenPos.X, (int)screenPos.Y, size.Width, size.Height), flags);
                }
                textRendering.End();
            }
        }

        private void RenderObjects(RenderPass pass)
        {
            if (disposed) return;

            int primNr = 0;
            foreach (FacetedMesh mesh in Prims.Values)
            {
                primNr++;
                Primitive prim = mesh.Prim;
                // Individual prim matrix
                GL.PushMatrix();

                if (prim.ParentID == RootPrimLocalID)
                {
                    if (Prims.TryGetValue(prim.ParentID, out var parent))
                    {
                        // Apply prim translation and rotation relative to the root prim
                        GL.MultMatrix(Math3D.CreateRotationMatrix(parent.Prim.Rotation));
                    }

                    // Prim roation relative to root
                    GL.MultMatrix(Math3D.CreateTranslationMatrix(prim.Position));
                }

                // Prim roation
                GL.MultMatrix(Math3D.CreateRotationMatrix(prim.Rotation));

                // Prim scaling
                GL.Scale(prim.Scale.X, prim.Scale.Y, prim.Scale.Z);

                // Draw the prim faces
                for (int j = 0; j < mesh.Faces.Count; j++)
                {
                    Primitive.TextureEntryFace teFace = mesh.Prim.Textures.FaceTextures[j];
                    Face face = mesh.Faces[j];
                    FaceData data = (FaceData)face.UserData;

                    if (teFace == null)
                        teFace = mesh.Prim.Textures.DefaultTexture;

                    if (pass == RenderPass.Picking)
                    {
                        data.PickingID = primNr;
                        var primNrBytes = Utils.Int16ToBytes((short)primNr);
                        var faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)j, PICKING_ALPHA_CHANNEL };

                        GL.Color4(faceColor);
                    }
                    else
                    {
                        bool belongToAlphaPass = (teFace.RGBA.A < ALPHA_PASS_THRESHOLD) || data.TextureInfo.HasAlpha;

                        if (belongToAlphaPass && pass != RenderPass.Alpha) continue;
                        if (!belongToAlphaPass && pass == RenderPass.Alpha) continue;

                        // Don't render transparent faces
                        if (teFace.RGBA.A <= ALPHA_TRANSPARENT_THRESHOLD) continue;

                        switch (teFace.Shiny)
                        {
                            case Shininess.High:
                                GL.Material(MaterialFace.Front, MaterialParameter.Shininess, SHININESS_HIGH);
                                break;
                            case Shininess.Medium:
                                GL.Material(MaterialFace.Front, MaterialParameter.Shininess, SHININESS_MEDIUM);
                                break;
                            case Shininess.Low:
                                GL.Material(MaterialFace.Front, MaterialParameter.Shininess, SHININESS_LOW);
                                break;
                            case Shininess.None:
                            default:
                                GL.Material(MaterialFace.Front, MaterialParameter.Shininess, SHININESS_NONE);
                                break;
                        }

                        var faceColor = new float[] { teFace.RGBA.R, teFace.RGBA.G, teFace.RGBA.B, teFace.RGBA.A };

                        GL.Color4(faceColor);
                        GL.Material(MaterialFace.Front, MaterialParameter.AmbientAndDiffuse, faceColor);
                        GL.Material(MaterialFace.Front, MaterialParameter.Specular, faceColor);

                        if (data.TextureInfo.TexturePointer != 0)
                        {
                            GL.Enable(EnableCap.Texture2D);
                        }
                        else
                        {
                            GL.Disable(EnableCap.Texture2D);
                        }

                        // Bind the texture
                        GL.BindTexture(TextureTarget.Texture2D, data.TextureInfo.TexturePointer);
                    }

                    GL.TexCoordPointer(TEXCOORD_COMPONENTS, TexCoordPointerType.Float, 0, data.TexCoords);
                    GL.VertexPointer(VERTEX_COMPONENTS, VertexPointerType.Float, 0, data.Vertices);
                    GL.NormalPointer(NormalPointerType.Float, 0, data.Normals);
                    GL.DrawElements(PrimitiveType.Triangles, data.Indices.Length, DrawElementsType.UnsignedShort, data.Indices);

                }

                // Pop the prim matrix
                GL.PopMatrix();
            }
        }

        private void Render(bool picking)
        {
            glControl.MakeCurrent();
            if (picking)
            {
                GL.ClearColor(PICKING_BACKGROUND_COLOR, PICKING_BACKGROUND_COLOR, PICKING_BACKGROUND_COLOR, PICKING_BACKGROUND_COLOR);
            }
            else
            {
                GL.ClearColor(BACKGROUND_SKY_R, BACKGROUND_SKY_G, BACKGROUND_SKY_B, BACKGROUND_ALPHA);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.LoadIdentity();

            // Setup wireframe or solid fill drawing mode
            if (Wireframe && !picking)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }
            else
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }

            var mLookAt = OpenTK.Matrix4d.LookAt(
                    Center.X, (double)scrollZoom.Value * ZOOM_SCALE_FACTOR + Center.Y, Center.Z,
                    Center.X, Center.Y, Center.Z,
                    0d, 0d, 1d);
            GL.MultMatrix(ref mLookAt);

            // Push the world matrix
            GL.PushMatrix();

            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.TextureCoordArray);
            GL.EnableClientState(ArrayCap.NormalArray);

            // World rotations
            GL.Rotate((float)scrollRoll.Value, 1f, 0f, 0f);
            GL.Rotate((float)scrollPitch.Value, 0f, 1f, 0f);
            GL.Rotate((float)scrollYaw.Value, 0f, 0f, 1f);

            GL.GetInteger(GetPName.Viewport, Viewport);
            GL.GetFloat(GetPName.ModelviewMatrix, out ModelMatrix);
            GL.GetFloat(GetPName.ProjectionMatrix, out ProjectionMatrix);

            if (picking)
            {
                RenderObjects(RenderPass.Picking);
            }
            else
            {
                RenderObjects(RenderPass.Simple);
                RenderObjects(RenderPass.Alpha);
                RenderText();
            }

            // Pop the world matrix
            GL.PopMatrix();

            GL.DisableClientState(ArrayCap.TextureCoordArray);
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.NormalArray);

            GL.Flush();
        }

        private void GluPerspective(float fovy, float aspect, float zNear, float zFar)
        {
            float fH = (float)Math.Tan(fovy / 360 * (float)Math.PI) * zNear;
            float fW = fH * aspect;
            GL.Frustum(-fW, fW, -fH, fH, zNear, zFar);
        }

        private bool TryPick(int x, int y, out FacetedMesh picked, out int faceID)
        {
            picked = null;
            faceID = 0;

            if (disposed) return false;

            // Save old attributes
            GL.PushAttrib(AttribMask.AllAttribBits);

            // Disable some attributes to make the objects flat / solid color when they are drawn
            GL.Disable(EnableCap.Fog);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Dither);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.LineStipple);
            GL.Disable(EnableCap.PolygonStipple);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.AlphaTest);

            Render(true);

            byte[] color = new byte[4];
            GL.ReadPixels(x, glControl.Height - y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, color);

            GL.PopAttrib();

            int primID = Utils.BytesToUInt16(color, 0);
            faceID = color[2];

            foreach (var mesh in Prims.Values)
            {
                foreach (var face in mesh.Faces)
                {
                    if (((FaceData)face.UserData).PickingID == primID)
                    {
                        picked = mesh;
                        break;
                    }
                }

                if (picked != null) break;
            }

            return picked != null;
        }


        private void UpdatePrimBlocking(Primitive prim)
        {
            if (disposed) return;

            FacetedMesh mesh = null;
            FacetedMesh existingMesh = null;

            if (Prims.TryGetValue(prim.LocalID, out var existing))
            {
                existingMesh = existing;
            }

            if (prim.Textures == null)
                return;

            try
            {
                if (prim.Sculpt != null && prim.Sculpt.SculptTexture != UUID.Zero)
                {
                    if (prim.Sculpt.Type != SculptType.Mesh)
                    { // Regular sculptie
                        SKBitmap img = null;
                        if (!LoadTexture(prim.Sculpt.SculptTexture, ref img, true))
                            return;
                        mesh = renderer.GenerateFacetedSculptMesh(prim, img, DetailLevel.Highest);
                    }
                    else
                    { // Mesh
                        AutoResetEvent gotMesh = new AutoResetEvent(false);
                        bool meshSuccess = false;

                        Client.Assets.RequestMesh(prim.Sculpt.SculptTexture, (success, meshAsset) =>
                            {
                                if (!success || !FacetedMesh.TryDecodeFromAsset(prim, meshAsset, DetailLevel.Highest, out mesh))
                                {
                                    Logger.Warn("Failed to fetch or decode the mesh asset", Client);
                                }
                                else
                                {
                                    meshSuccess = true;
                                }
                                gotMesh.Set();
                            });

                        if (!gotMesh.WaitOne(MESH_REQUEST_TIMEOUT_MS, false)) return;
                        if (!meshSuccess) return;
                    }
                }
                else
                {
                    mesh = renderer.GenerateFacetedMesh(prim, DetailLevel.Highest);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error generating mesh: {ex.Message}", Client);
                return;
            }

            if (mesh == null) return;

            // Create a FaceData struct for each face that stores the 3D data
            // in a OpenGL friendly format
            for (int j = 0; j < mesh.Faces.Count; j++)
            {
                Face face = mesh.Faces[j];
                FaceData data = new FaceData
                {
                    Vertices = new float[face.Vertices.Count * VERTEX_COMPONENTS], 
                    Normals = new float[face.Vertices.Count * NORMAL_COMPONENTS]
                };

                // Vertices for this face
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.Vertices[k * VERTEX_COMPONENTS + 0] = face.Vertices[k].Position.X;
                    data.Vertices[k * VERTEX_COMPONENTS + 1] = face.Vertices[k].Position.Y;
                    data.Vertices[k * VERTEX_COMPONENTS + 2] = face.Vertices[k].Position.Z;

                    data.Normals[k * NORMAL_COMPONENTS + 0] = face.Vertices[k].Normal.X;
                    data.Normals[k * NORMAL_COMPONENTS + 1] = face.Vertices[k].Normal.Y;
                    data.Normals[k * NORMAL_COMPONENTS + 2] = face.Vertices[k].Normal.Z;
                }

                // Indices for this face
                data.Indices = face.Indices.ToArray();

                // Texture transform for this face
                Primitive.TextureEntryFace teFace = prim.Textures.GetFace((uint)j);
                renderer.TransformTexCoords(face.Vertices, face.Center, teFace, prim.Scale);

                // Texcoords for this face
                data.TexCoords = new float[face.Vertices.Count * TEXCOORD_COMPONENTS];
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.TexCoords[k * TEXCOORD_COMPONENTS + 0] = face.Vertices[k].TexCoord.X;
                    data.TexCoords[k * TEXCOORD_COMPONENTS + 1] = face.Vertices[k].TexCoord.Y;
                }

                // Set the UserData for this face to our FaceData struct
                face.UserData = data;
                mesh.Faces[j] = face;


                if (existingMesh != null &&
                    j < existingMesh.Faces.Count &&
                    existingMesh.Faces[j].TextureFace.TextureID == teFace.TextureID &&
                    ((FaceData)existingMesh.Faces[j].UserData).TextureInfo.TexturePointer != 0
                    )
                {
                    FaceData existingData = (FaceData)existingMesh.Faces[j].UserData;
                    data.TextureInfo.TexturePointer = existingData.TextureInfo.TexturePointer;
                }
                else
                {

                    var textureItem = new TextureLoadItem()
                    {
                        Data = data,
                        Prim = prim,
                        TeFace = teFace
                    };

                    PendingTextures.Enqueue(textureItem);
                }

            }

            Prims[prim.LocalID] = mesh;
            SafeInvalidate();
        }

        private bool LoadTexture(UUID textureID, ref SKBitmap texture, bool removeAlpha)
        {
            if (textureID == UUID.Zero) return false;

            ManualResetEvent gotImage = new ManualResetEvent(false);
            SKBitmap img = null;

            try
            {
                gotImage.Reset();
                instance.Client.Assets.RequestImage(textureID, (state, assetTexture) =>
                    {
                        try
                        {
                            if (state == TextureRequestState.Finished)
                            {
                                img = J2kImage.FromBytes(assetTexture.AssetData).As<SKBitmap>();
                            }
                        }
                        finally
                        {
                            gotImage.Set();                            
                        }
                    }
                );
                gotImage.WaitOne(TEXTURE_REQUEST_TIMEOUT_MS, false);
                if (img != null)
                {
                    texture = img;
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Logger.Error(e.Message, e, instance.Client);
                return false;
            }
        }

        private void SafeInvalidate()
        {
            if (disposed || glControl == null || !RenderingEnabled) return;

            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    try
                    {
                        BeginInvoke(new MethodInvoker(SafeInvalidate));
                    }
                    catch (ObjectDisposedException)
                    {
                        // Control already disposed
                    }
                    catch (InvalidOperationException)
                    {
                        // Handle already destroyed
                    }
                }
                return;
            }

            try
            {
                glControl?.Invalidate();
            }
            catch (ObjectDisposedException)
            {
                // Control already disposed
            }
        }
        #endregion Private methods (the meat)

        #region Form controls handlers
        private void Scroll_ValueChanged(object sender, EventArgs e)
        {
            SafeInvalidate();
        }

        private void ScrollZoom_ValueChanged(object sender, EventArgs e)
        {
            glControl_Resize(null, null);
            SafeInvalidate();
        }

        private void ChkWireFrame_CheckedChanged(object sender, EventArgs e)
        {
            Wireframe = chkWireFrame.Checked;
            SafeInvalidate();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            scrollYaw.Value = DEFAULT_YAW;
            scrollPitch.Value = DEFAULT_PITCH;
            scrollRoll.Value = DEFAULT_ROLL;
            scrollZoom.Value = DEFAULT_ZOOM;
            Center = Vector3.Zero;

            SafeInvalidate();
        }

        private void OBJToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog {Filter = "OBJ files (*.obj)|*.obj"};

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // Convert ConcurrentDictionary to Dictionary for MeshToOBJ
                var primsDict = new Dictionary<uint, FacetedMesh>();
                foreach (var kvp in Prims)
                {
                    primsDict[kvp.Key] = kvp.Value;
                }

                if (!MeshToOBJ.MeshesToOBJ(primsDict, dialog.FileName))
                {
                    MessageBox.Show("Failed to save file " + dialog.FileName +
                        ". Ensure that you have permission to write to that file and it is currently not in use");
                }
            }
        }

        private void cbAA_CheckedChanged(object sender, EventArgs e)
        {
            instance.GlobalSettings["use_multi_sampling"] = UseMultiSampling = cbAA.Checked;
            SetupGLControl();
        }

        #endregion Form controls handlers

        #region Context menu
        private void CtxObjects_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = false;

            if (instance.State.IsSitting)
            {
                sitToolStripMenuItem.Text = "Stand up";
            }
            else if (RightclickedPrim.Prim.Properties != null
                && !string.IsNullOrEmpty(RightclickedPrim.Prim.Properties.SitName))
            {
                sitToolStripMenuItem.Text = RightclickedPrim.Prim.Properties.SitName;
            }
            else
            {
                sitToolStripMenuItem.Text = "Sit";
            }

            if (RightclickedPrim.Prim.Properties != null
                && !string.IsNullOrEmpty(RightclickedPrim.Prim.Properties.TouchName))
            {
                touchToolStripMenuItem.Text = RightclickedPrim.Prim.Properties.TouchName;
            }
            else
            {
                touchToolStripMenuItem.Text = "Touch";
            }
        }

        private void TouchToolStripMenuItem_Click(object sender, EventArgs e)
        {

            Client.Self.Grab(RightclickedPrim.Prim.LocalID, Vector3.Zero, Vector3.Zero, Vector3.Zero, RightclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
            Client.Self.DeGrab(RightclickedPrim.Prim.LocalID, Vector3.Zero, Vector3.Zero, RightclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
        }

        private void SitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!instance.State.IsSitting)
            {
                instance.State.SetSitting(true, RightclickedPrim.Prim.ID);
            }
            else
            {
                instance.State.SetSitting(false, UUID.Zero);
            }
        }

        private void TakeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
            Client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID);
            Close();
        }

        private void ReturnToolStripMenuItem_Click(object sender, EventArgs e)
        {
            instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
            Client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID, DeRezDestination.ReturnToOwner, UUID.Zero, UUID.Random());
            Close();
        }

        private void DeleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (RightclickedPrim.Prim.Properties != null && RightclickedPrim.Prim.Properties.OwnerID != Client.Self.AgentID)
                ReturnToolStripMenuItem_Click(sender, e);
            else
            {
                instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
                Client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID, DeRezDestination.AgentInventoryTake, Client.Inventory.FindFolderForType(FolderType.Trash), UUID.Random());
            }
            Close();
        }
        #endregion Context menu



    }
}
