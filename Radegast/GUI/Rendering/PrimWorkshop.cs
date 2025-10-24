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

    public partial class frmPrimWorkshop : RadegastForm
    {
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
        public bool RenderingEnabled = false;

        /// <summary>
        /// Rednder in wireframe mode
        /// </summary>
        public bool Wireframe = false;

        /// <summary>
        /// List of prims in the scene
        /// </summary>
        private readonly Dictionary<uint, FacetedMesh> Prims = new Dictionary<uint, FacetedMesh>();

        /// <summary>
        /// Local ID of the root prim
        /// </summary>
        public uint RootPrimLocalID = 0;

        /// <summary>
        /// Camera center
        /// </summary>
        public Vector3 Center = Vector3.Zero;
        #endregion Public fields

        #region Private fields

        private readonly Dictionary<UUID, TextureInfo> TexturesPtrMap = new Dictionary<UUID, TextureInfo>();
        private readonly RadegastInstance instance;
        private readonly MeshmerizerR renderer;
        private OpenTK.Graphics.GraphicsMode GLMode = null;
        private readonly ConcurrentQueue<TextureLoadItem> PendingTextures = new ConcurrentQueue<TextureLoadItem>();
        private readonly float[] lightPos = new float[] { 0f, 0f, 1f, 0f };
        private TextRendering textRendering;
        private OpenTK.Matrix4 ModelMatrix;
        private OpenTK.Matrix4 ProjectionMatrix;
        private readonly int[] Viewport = new int[4];

        #endregion Private fields

        #region Construction and disposal
        public frmPrimWorkshop(RadegastInstanceForms instance, uint rootLocalID)
            : base(instance)
        {
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
        }

        private void FrmPrimWorkshop_Disposed(object sender, EventArgs e)
        {
            if (textRendering != null)
            {
                textRendering.Dispose();
                textRendering = null;
            }

            glControl?.Dispose();
            glControl = null;
            Client.Objects.TerseObjectUpdate -= Objects_TerseObjectUpdate;
            Client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
            Client.Objects.ObjectDataBlockUpdate -= Objects_ObjectDataBlockUpdate;
        }
        #endregion Construction and disposal

        #region Network messaage handlers

        private void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (Prims.ContainsKey(e.Prim.LocalID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }

        private void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if (Prims.ContainsKey(e.Prim.LocalID) || Prims.ContainsKey(e.Prim.ParentID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }

        private void Objects_ObjectDataBlockUpdate(object sender, ObjectDataBlockUpdateEventArgs e)
        {
            if (Prims.ContainsKey(e.Prim.LocalID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }
        #endregion Network messaage handlers

        #region glControl setup and disposal
        public void SetupGLControl()
        {
            RenderingEnabled = false;

            glControl?.Dispose();
            glControl = null;

            GLMode = null;

            try
            {
                if (!UseMultiSampling)
                {
                    GLMode = new OpenTK.Graphics.GraphicsMode(OpenTK.DisplayDevice.Default.BitsPerPixel, 24, 8, 0);
                }
                else
                {
                    for (int aa = 0; aa <= 4; aa += 2)
                    {
                        var testMode = new OpenTK.Graphics.GraphicsMode(OpenTK.DisplayDevice.Default.BitsPerPixel, 24, 8, aa);
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
                Logger.Log(ex.Message, Helpers.LogLevel.Warning, Client);
                glControl = null;
            }

            if (glControl == null)
            {
                Logger.Log("Failed to initialize OpenGL control, cannot continue", Helpers.LogLevel.Error, Client);
                return;
            }

            Logger.Log("Initializing OpenGL mode: " + (GLMode == null ? "" : GLMode.ToString()), Helpers.LogLevel.Info);

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
            TextureThreadRunning = false;
            while (!PendingTextures.IsEmpty)
            {
                PendingTextures.TryDequeue(out _);
            }
        }

        private void glControl_Load(object sender, EventArgs e)
        {
            try
            {
                GL.ShadeModel(ShadingModel.Smooth);
                GL.ClearColor(0f, 0f, 0f, 0f);

                //GL.LightModel(LightModelParameter.LightModelAmbient, new float[] { 0.5f, 0.5f, 0.5f, 1.0f });

                GL.Enable(EnableCap.Lighting);
                GL.Enable(EnableCap.Light0);
                GL.Light(LightName.Light0, LightParameter.Ambient, new float[] { 0.5f, 0.5f, 0.5f, 1f });
                GL.Light(LightName.Light0, LightParameter.Diffuse, new float[] { 0.3f, 0.3f, 0.3f, 1f });
                GL.Light(LightName.Light0, LightParameter.Specular, new float[] { 0.8f, 0.8f, 0.8f, 1.0f });
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
                GL.AlphaFunc(AlphaFunction.Greater, 0.5f);
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
                Logger.Log("Failed to initialize OpenGL control", Helpers.LogLevel.Warning, Client, ex);
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

            GL.ClearColor(0.39f, 0.58f, 0.93f, 1.0f);

            GL.Viewport(0, 0, glControl.Width, glControl.Height);

            GL.PushMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            float dAspRat = (float)glControl.Width / (float)glControl.Height;
            GluPerspective(50f, dAspRat, 0.1f, 100.0f);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();
        }
        #endregion glControl paint and resize events

        #region Mouse handling

        private bool dragging = false;
        private int dragX, dragY, downX, downY;

        private void glControl_MouseWheel(object sender, MouseEventArgs e)
        {
            int newVal = Utils.Clamp(scrollZoom.Value + e.Delta / 10, scrollZoom.Minimum, scrollZoom.Maximum);

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
                        Center.X -= deltaX / 100f;
                        Center.Z += deltaY / 100f;
                    }

                    if (ModifierKeys == Keys.Alt)
                    {
                        Center.Y -= deltaY / 25f;

                        int newYaw = scrollYaw.Value + deltaX;
                        if (newYaw < 0) newYaw += 360;
                        if (newYaw > 360) newYaw -= 360;

                        scrollYaw.Value = newYaw;

                    }

                    if (ModifierKeys == Keys.None || ModifierKeys == (Keys.Alt | Keys.Control))
                    {
                        int newRoll = scrollRoll.Value + deltaY;
                        if (newRoll < 0) newRoll += 360;
                        if (newRoll > 360) newRoll -= 360;

                        scrollRoll.Value = newRoll;


                        int newYaw = scrollYaw.Value + deltaX;
                        if (newYaw < 0) newYaw += 360;
                        if (newYaw > 360) newYaw -= 360;

                        scrollYaw.Value = newYaw;

                    }
                }
                else if (e.Button == MouseButtons.Middle)
                {
                    Center.X -= deltaX / 100f;
                    Center.Z += deltaY / 100f;

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

        private bool TextureThreadRunning = true;

        private void TextureThread()
        {
            Logger.DebugLog("Started Texture Thread");

            while (TextureThreadRunning)
            {
                TextureLoadItem item = null;

                if (!PendingTextures.TryDequeue(out item)) continue;

                if (TexturesPtrMap.ContainsKey(item.TeFace.TextureID))
                {
                    item.Data.TextureInfo = TexturesPtrMap[item.TeFace.TextureID];
                    continue;
                }

                if (LoadTexture(item.TeFace.TextureID, ref item.Data.TextureInfo.Texture, false))
                {
                    Bitmap bitmap = item.Data.TextureInfo.Texture.ToBitmap();

                    bool hasAlpha;
                    hasAlpha = bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb;
                    
                    item.Data.TextureInfo.HasAlpha = hasAlpha;

                    bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);

                    var loadOnMainThread = new MethodInvoker(() =>
                    {
                        item.Data.TextureInfo.TexturePointer = RHelp.GLLoadImage(bitmap, hasAlpha, RenderSettings.HasMipmap);
                        TexturesPtrMap[item.TeFace.TextureID] = item.Data.TextureInfo;
                        bitmap.Dispose();
                        item.Data.TextureInfo.Texture = null;
                        SafeInvalidate();
                    });

                    if (Disposing || IsDisposed)
                    {
                        break;
                    }

                    if (!instance.MonoRuntime || IsHandleCreated)
                    {
                        BeginInvoke(loadOnMainThread);
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
            lock (Prims)
            {
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
                    FacetedMesh parent = null;
                    if (Prims.TryGetValue(prim.ParentID, out parent))
                    {
                        var newPrimPos = prim.Position * Matrix4.CreateFromQuaternion(parent.Prim.Rotation);
                        primPos = new OpenTK.Vector3(newPrimPos.X, newPrimPos.Y, newPrimPos.Z);
                    }

                    primPos.Z += prim.Scale.Z * 0.8f;
                    if (!GLU.Project(primPos, ModelMatrix, ProjectionMatrix, Viewport, out screenPos)) continue;
                    screenPos.Y = glControl.Height - screenPos.Y;

                    textRendering.Begin();

                    Color color = Color.FromArgb((int)(prim.TextColor.A * 255), (int)(prim.TextColor.R * 255), (int)(prim.TextColor.G * 255), (int)(prim.TextColor.B * 255));
                    TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;

                    using (Font f = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Regular))
                    {
                        var size = TextRendering.Measure(text, f, flags);
                        screenPos.X -= size.Width / 2;
                        screenPos.Y -= size.Height;

                        // Shadow
                        if (color != Color.Black)
                        {
                            textRendering.Print(text, f, Color.Black, new Rectangle((int)screenPos.X + 1, (int)screenPos.Y + 1, size.Width, size.Height), flags);
                        }
                        textRendering.Print(text, f, color, new Rectangle((int)screenPos.X, (int)screenPos.Y, size.Width, size.Height), flags);
                    }
                    textRendering.End();
                }
            }
        }

        private void RenderObjects(RenderPass pass)
        {
            lock (Prims)
            {
                int primNr = 0;
                foreach (FacetedMesh mesh in Prims.Values)
                {
                    primNr++;
                    Primitive prim = mesh.Prim;
                    // Individual prim matrix
                    GL.PushMatrix();

                    if (prim.ParentID == RootPrimLocalID)
                    {
                        FacetedMesh parent = null;
                        if (Prims.TryGetValue(prim.ParentID, out parent))
                        {
                            // Apply prim translation and rotation relative to the root prim
                            GL.MultMatrix(Math3D.CreateRotationMatrix(parent.Prim.Rotation));
                            //GL.MultMatrixf(Math3D.CreateTranslationMatrix(parent.Prim.Position));
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
                            var faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)j, 255 };

                            GL.Color4(faceColor);
                        }
                        else
                        {
                            bool belongToAlphaPass = (teFace.RGBA.A < 0.99) || data.TextureInfo.HasAlpha;

                            if (belongToAlphaPass && pass != RenderPass.Alpha) continue;
                            if (!belongToAlphaPass && pass == RenderPass.Alpha) continue;

                            // Don't render transparent faces
                            if (teFace.RGBA.A <= 0.01f) continue;

                            switch (teFace.Shiny)
                            {
                                case Shininess.High:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 94f);
                                    break;
                                case Shininess.Medium:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 64f);
                                    break;
                                case Shininess.Low:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 24f);
                                    break;
                                case Shininess.None:
                                default:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 0f);
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

                        GL.TexCoordPointer(2, TexCoordPointerType.Float, 0, data.TexCoords);
                        GL.VertexPointer(3, VertexPointerType.Float, 0, data.Vertices);
                        GL.NormalPointer(NormalPointerType.Float, 0, data.Normals);
                        GL.DrawElements(PrimitiveType.Triangles, data.Indices.Length, DrawElementsType.UnsignedShort, data.Indices);

                    }

                    // Pop the prim matrix
                    GL.PopMatrix();
                }
            }
        }

        private void Render(bool picking)
        {
            glControl.MakeCurrent();
            if (picking)
            {
                GL.ClearColor(1f, 1f, 1f, 1f);
            }
            else
            {
                GL.ClearColor(0.39f, 0.58f, 0.93f, 1.0f);
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
                    Center.X, (double)scrollZoom.Value * 0.1d + Center.Y, Center.Z,
                    Center.X, Center.Y, Center.Z,
                    0d, 0d, 1d);
            GL.MultMatrix(ref mLookAt);

            //OpenTK.Graphics.Glu.LookAt(
            //        Center.X, (double)scrollZoom.Value * 0.1d + Center.Y, Center.Z,
            //        Center.X, Center.Y, Center.Z,
            //        0d, 0d, 1d);

            //GL.Light(LightName.Light0, LightParameter.Position, lightPos);

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

            picked = null;

            lock (Prims)
            {
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
            }

            return picked != null;
        }


        private void UpdatePrimBlocking(Primitive prim)
        {

            FacetedMesh mesh = null;
            FacetedMesh existingMesh = null;

            lock (Prims)
            {
                if (Prims.TryGetValue(prim.LocalID, out var existing))
                {
                    existingMesh = existing;
                }
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
                                    Logger.Log("Failed to fetch or decode the mesh asset", Helpers.LogLevel.Warning, Client);
                                }
                                else
                                {
                                    meshSuccess = true;
                                }
                                gotMesh.Set();
                            });

                        if (!gotMesh.WaitOne(20 * 1000, false)) return;
                        if (!meshSuccess) return;
                    }
                }
                else
                {
                    mesh = renderer.GenerateFacetedMesh(prim, DetailLevel.Highest);
                }
            }
            catch
            {
                return;
            }

            // Create a FaceData struct for each face that stores the 3D data
            // in a OpenGL friendly format
            for (int j = 0; j < mesh.Faces.Count; j++)
            {
                Face face = mesh.Faces[j];
                FaceData data = new FaceData
                {
                    Vertices = new float[face.Vertices.Count * 3], 
                    Normals = new float[face.Vertices.Count * 3]
                };

                // Vertices for this face
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.Vertices[k * 3 + 0] = face.Vertices[k].Position.X;
                    data.Vertices[k * 3 + 1] = face.Vertices[k].Position.Y;
                    data.Vertices[k * 3 + 2] = face.Vertices[k].Position.Z;

                    data.Normals[k * 3 + 0] = face.Vertices[k].Normal.X;
                    data.Normals[k * 3 + 1] = face.Vertices[k].Normal.Y;
                    data.Normals[k * 3 + 2] = face.Vertices[k].Normal.Z;
                }

                // Indices for this face
                data.Indices = face.Indices.ToArray();

                // Texture transform for this face
                Primitive.TextureEntryFace teFace = prim.Textures.GetFace((uint)j);
                renderer.TransformTexCoords(face.Vertices, face.Center, teFace, prim.Scale);

                // Texcoords for this face
                data.TexCoords = new float[face.Vertices.Count * 2];
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.TexCoords[k * 2 + 0] = face.Vertices[k].TexCoord.X;
                    data.TexCoords[k * 2 + 1] = face.Vertices[k].TexCoord.Y;
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

            lock (Prims)
            {
                Prims[prim.LocalID] = mesh;
            }
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
                gotImage.WaitOne(30 * 1000, false);
                if (img != null)
                {
                    texture = img;
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Logger.Log(e.Message, Helpers.LogLevel.Error, instance.Client, e);
                return false;
            }
        }

        private void SafeInvalidate()
        {
            if (glControl == null || !RenderingEnabled) return;

            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(SafeInvalidate));
                }
                return;
            }

            glControl.Invalidate();
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
            scrollYaw.Value = 90;
            scrollPitch.Value = 0;
            scrollRoll.Value = 0;
            scrollZoom.Value = -30;
            Center = Vector3.Zero;

            SafeInvalidate();
        }

        private void OBJToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog {Filter = "OBJ files (*.obj)|*.obj"};

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (!MeshToOBJ.MeshesToOBJ(Prims, dialog.FileName))
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
            Thread.Sleep(100);
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
