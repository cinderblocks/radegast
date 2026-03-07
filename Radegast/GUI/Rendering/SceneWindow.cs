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
//       this software without specific prior CreateReflectionTexture permission.
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
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using OpenTK.Graphics;
using OpenTK.Platform;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using OpenMetaverse.Assets;
using OpenMetaverse.Packets;
using SkiaSharp;
using static LibreMetaverse.DisposalHelper;

#endregion Usings

namespace Radegast.Rendering
{

    public partial class SceneWindow : RadegastTabControl
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
        /// Render in wireframe mode
        /// </summary>
        public bool Wireframe = false;

        /// <summary>
        /// Object from up to this distance from us will be rendered
        /// </summary>
        public float DrawDistance
        {
            get => drawDistance;
            set
            {
                drawDistance = value;
                drawDistanceSquared = value * value;
                if (Camera != null)
                    Camera.Far = value;
            }
        }

        /// <summary>
        /// List of prims in the scene
        /// </summary>
        private readonly Dictionary<uint, RenderPrimitive> Prims = new Dictionary<uint, RenderPrimitive>();

        private List<SceneObject> SortedObjects;
        private List<SceneObject> OccludedObjects;
        private List<RenderAvatar> VisibleAvatars;
        private readonly Dictionary<uint, RenderAvatar> Avatars = new Dictionary<uint, RenderAvatar>();

        /// <summary>
        /// Cache images after jpeg2000 decode. Uses a lot of disk space and can cause disk trashing
        /// </summary>
        public bool CacheDecodedTextures = false;

        /// <summary>
        /// Size of OpenGL window we're drawing on
        /// </summary>
        public int[] Viewport = new int[4];

        #endregion Public fields

        #region Private fields

        private ChatOverlay chatOverlay;
        private TextRendering textRendering;
        private readonly Camera Camera;
        private SceneObject trackedObject;
        private Vector3 lastTrackedObjectPos = RHelp.InvalidPosition;
        private RenderAvatar myself;

        private readonly Dictionary<UUID, TextureInfo> TexturesPtrMap = new Dictionary<UUID, TextureInfo>();
        private readonly MeshmerizerR renderer;

        private OpenTK.Graphics.GraphicsMode GLMode = null;

        // Worker context for background texture uploads
        private IGraphicsContext textureContext = null;
        private IWindowInfo sharedWindowInfo = null;
        private Thread textureDecodingThread = null;
        private readonly AutoResetEvent TextureThreadContextReady = new AutoResetEvent(false);
        private readonly SemaphoreSlim PendingTexturesAvailable = new SemaphoreSlim(0);
        private CancellationTokenSource cancellationTokenSource = null;

        private delegate void GenericTask();

        private readonly ConcurrentQueue<GenericTask> PendingTasks = new ConcurrentQueue<GenericTask>();
        private readonly SemaphoreSlim PendingTasksAvailable = new SemaphoreSlim(0);
        private Thread genericTaskThread;

        private readonly ConcurrentQueue<TextureLoadItem> PendingTextures = new ConcurrentQueue<TextureLoadItem>();

        private readonly Dictionary<UUID, int> AssetFetchFailCount = new Dictionary<UUID, int>();

        private Font HoverTextFont = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Regular);
        private Font AvatarTagFont = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold);
        private readonly Dictionary<UUID, SKBitmap> sculptCache = new Dictionary<UUID, SKBitmap>();
        private OpenTK.Matrix4 ModelMatrix;
        private OpenTK.Matrix4 ProjectionMatrix;
        private readonly System.Diagnostics.Stopwatch renderTimer;
        private float lastFrameTime = 0f;
        private float advTimerTick = 0f;
        private float minLODFactor = 0.0001f;

        private readonly float[] sunPos = new float[] { 128f, 128f, 5000f, 1f };
        private float ambient = RenderSettings.AmbientLight;
        private float diffuse = RenderSettings.DiffuseLight;
        private float specular = RenderSettings.SpecularLight;
        private OpenTK.Vector4 ambientColor;
        private OpenTK.Vector4 diffuseColor;
        private OpenTK.Vector4 specularColor;
        private ShaderManager shaderManager;
        private bool shadersAvailable = false;
        private float drawDistance;
        private float drawDistanceSquared;

        private readonly RenderTerrain terrain;
        private RenderSky sky;

        // Adjacent simulator rendering
        private readonly Dictionary<ulong, RenderAdjacentTerrain> adjacentTerrains = new Dictionary<ulong, RenderAdjacentTerrain>();
        private readonly Dictionary<ulong, Simulator> adjacentSimulators = new Dictionary<ulong, Simulator>();
        private readonly object adjacentSimsLock = new object();

        private readonly GridClient Client;
        private readonly RadegastInstanceForms Instance;

        #endregion Private fields

        #region Construction and disposal

        public SceneWindow(RadegastInstanceForms instance)
            : base(instance)
        {
            InitializeComponent();

            Instance = instance;
            Client = instance.Client;

            UseMultiSampling = Instance.GlobalSettings["use_multi_sampling"];

            cancellationTokenSource = new CancellationTokenSource();

            genericTaskThread = new Thread(GenericTaskRunner)
            {
                IsBackground = true,
                Name = "Generic task queue"
            };
            genericTaskThread.Start();

            renderer = new MeshmerizerR();
            renderTimer = new System.Diagnostics.Stopwatch();
            renderTimer.Start();

            // Camera initial setting
            Instance.State.CameraTracksOwnAvatar = false;
            Camera = new Camera();
            InitCamera();
            SetWaterPlanes();

            chatOverlay = new ChatOverlay(instance, this);
            textRendering = new TextRendering(instance);
            terrain = new RenderTerrain(instance);
            sky = new RenderSky(this);

            cbChatType.SelectedIndex = 1;

            DrawDistance = Instance.GlobalSettings["draw_distance"];
            pnlDebug.Visible = Instance.GlobalSettings["scene_viewer_debug_panel"];

            Client.Objects.TerseObjectUpdate += Objects_TerseObjectUpdate;
            Client.Objects.ObjectUpdate += Objects_ObjectUpdate;
            Client.Objects.AvatarUpdate += Objects_AvatarUpdate;

            Client.Network.RegisterCallback(PacketType.KillObject, KillObjectHandler);
            Client.Network.SimChanged += Network_SimChanged;
            Client.Terrain.LandPatchReceived += Terrain_LandPatchReceived;
            Client.Avatars.AvatarAnimation += AvatarAnimationChanged;
            Client.Avatars.AvatarAppearance += Avatars_AvatarAppearance;
            Client.Appearance.AppearanceSet += Appearance_AppearanceSet;
            Instance.NetCom.ClientDisconnected += Netcom_ClientDisconnected;
            Application.Idle += Application_Idle;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void DisposeInternal()
        {
            RenderingEnabled = false;
            Application.Idle -= Application_Idle;
            Instance.State.CameraTracksOwnAvatar = true;
            Instance.State.SetDefaultCamera();

            TextureThreadContextReady.Reset();
            // Signal texture thread to stop (flag lives in textures partial)
            try { TextureThreadRunning = false; } catch { }
            PendingTexturesAvailable?.Release();
            TextureThreadContextReady.WaitOne(TimeSpan.FromSeconds(5), false);

            // Attempt to acquire a valid GL context early so we can safely delete GL resources.
            // If we cannot make the context current, skip disposing objects that call GL to avoid
            // AccessViolationException when the native context/handle is already gone.
            bool glContextAcquired = false;
            if (glControl != null)
            {
                try
                {
                    var ctx = glControl.Context;
                    if (ctx != null && !glControl.IsDisposed && glControl.IsHandleCreated)
                    {
                        try
                        {
                            glControl.MakeCurrent();
                            glContextAcquired = true;
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"MakeCurrent failed during early dispose: {ex.Message}", ex, Client);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("Unexpected GL control state during early dispose: {ex.Message}", ex, Client);
                }
            }

            // Safely dispose overlays and render helpers
            SafeDispose(chatOverlay, "ChatOverlay", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
            chatOverlay = null;

            SafeDispose(textRendering, "TextRendering", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
            textRendering = null;

            if (glContextAcquired)
            {
                SafeDispose(sky, "RenderSky", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                sky = null;
            }
            else
            {
                // Avoid calling GL from managed code when native context is not available;
                // drop references so garbage collection can reclaim managed memory.
                sky = null;
            }

            // Dispose adjacent simulator terrains
            lock (adjacentSimsLock)
            {
                foreach (var adjTerrain in adjacentTerrains.Values)
                {
                    SafeDispose(adjTerrain, "AdjacentTerrain", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                }
                adjacentTerrains.Clear();
            }

            Client.Objects.TerseObjectUpdate -= Objects_TerseObjectUpdate;
            Client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
            Client.Objects.AvatarUpdate -= Objects_AvatarUpdate;
            Client.Network.UnregisterCallback(PacketType.KillObject, KillObjectHandler);
            Client.Network.SimChanged -= Network_SimChanged;
            Client.Terrain.LandPatchReceived -= Terrain_LandPatchReceived;
            Client.Avatars.AvatarAnimation -= AvatarAnimationChanged;
            Client.Avatars.AvatarAppearance -= Avatars_AvatarAppearance;
            Client.Appearance.AppearanceSet -= Appearance_AppearanceSet;

            // Cancel and dispose cancellation token source
            SafeCancelAndDispose(cancellationTokenSource, (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
            cancellationTokenSource = null;

            // Wait for generic task thread to exit
            SafeJoinThread(genericTaskThread, TimeSpan.FromSeconds(2), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
            genericTaskThread = null;

            if (Instance.NetCom != null)
            {
                Instance.NetCom.ClientDisconnected -= Netcom_ClientDisconnected;
            }

            // Dispose Font objects
            SafeDispose(HoverTextFont, "HoverTextFont", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
            SafeDispose(AvatarTagFont, "AvatarTagFont", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));

            // Dispose cached sculpt bitmaps
            lock (sculptCache)
            {
                SafeDisposeAll(sculptCache.Values.Cast<IDisposable>(), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                sculptCache.Clear();
            }

            // Deterministically dispose contained scene objects to free GL resources
            if (glContextAcquired)
            {
                lock (Prims)
                {
                    SafeDisposeAll(Prims.Values.Cast<IDisposable>(), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                    Prims.Clear();
                }

                lock (Avatars)
                {
                    SafeDisposeAll(Avatars.Values.Cast<IDisposable>(), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                    Avatars.Clear();
                }
            }
            else
            {
                lock (Prims)
                {
                    Prims.Clear();
                }

                lock (Avatars)
                {
                    Avatars.Clear();
                }
            }

            // Also dispose any lists of scene objects
            if (SortedObjects != null)
            {
                if (glContextAcquired)
                {
                    SafeDisposeAll(SortedObjects.Cast<IDisposable>(), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                }
                SortedObjects = null;
            }

            if (OccludedObjects != null)
            {
                if (glContextAcquired)
                {
                    SafeDisposeAll(OccludedObjects.Cast<IDisposable>(), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                }
                OccludedObjects = null;
            }

            if (VisibleAvatars != null)
            {
                if (glContextAcquired)
                {
                    SafeDisposeAll(VisibleAvatars.Cast<IDisposable>(), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                }
                VisibleAvatars = null;
            }

            TexturesPtrMap.Clear();

            if (glControl != null)
            {
                try
                {
                    glControl_UnhookEvents();

                    // Protect against null internal context / already-disposed control
                    var ctx = glControl.Context;
                    if (ctx != null && !glControl.IsDisposed && glControl.IsHandleCreated)
                    {
                        try
                        {
                            glControl.MakeCurrent();
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"MakeCurrent failed during dispose: {ex.Message}", ex, Client);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("Unexpected GL control state during dispose: {ex.Message}", ex, Client);
                }

                SafeDispose(glControl, "GLControl", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
            }

            glControl = null;

            // Ensure texture thread stopped and worker context disposed
            try
            {
                SafeJoinThread(textureDecodingThread, TimeSpan.FromSeconds(2), (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                textureDecodingThread = null;

                SafeDispose(textureContext, "TextureContext", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                textureContext = null;
                sharedWindowInfo = null;
            }
            catch { }

            try
            {
                SafeDispose(shaderManager, "ShaderManager", (m, ex) => Logger.Debug(m + (ex != null ? (": " + ex.Message) : ""), ex, Client));
                shaderManager = null;
            }
            catch { }

            GC.Collect();
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            if (RenderingEnabled && glControl != null && !glControl.IsDisposed)
            {
                try
                {
                    while (RenderingEnabled && glControl != null && glControl.IsIdle)
                    {
                        MainRenderLoop();
                        if (instance.MonoRuntime)
                        {
                            Application.DoEvents();
                        }
                    }
                }
                catch (ObjectDisposedException)
                { }
#if !DEBUG
                catch (NullReferenceException)
                { }
                catch (Exception ex)
                {
                    RenderingEnabled = false;
                    Logger.Log("Exception in 3D scene viewer", LogLevel.Error, ex);
                    Dispose();
                }
#endif
            }
        }

        #endregion Construction and disposal

        #region Tab Events

        public void RegisterTabEvents()
        {
            RadegastTab.TabAttached += RadegastTab_TabAttached;
            RadegastTab.TabDetached += RadegastTab_TabDetached;
            RadegastTab.TabClosed += RadegastTab_TabClosed;
        }

        public void UnregisterTabEvents()
        {
            RadegastTab.TabAttached -= RadegastTab_TabAttached;
            RadegastTab.TabDetached -= RadegastTab_TabDetached;
            RadegastTab.TabClosed -= RadegastTab_TabClosed;
        }

        private void RadegastTab_TabDetached(object sender, EventArgs e)
        {
            Instance.GlobalSettings["scene_window_docked"] = false;
        }

        private void RadegastTab_TabAttached(object sender, EventArgs e)
        {
            Instance.GlobalSettings["scene_window_docked"] = true;
        }

        private void RadegastTab_TabClosed(object sender, EventArgs e)
        {
            if (RadegastTab != null)
            {
                UnregisterTabEvents();
            }
        }

        #endregion Tab Events

        #region Network messaage handlers

        private void Terrain_LandPatchReceived(object sender, LandPatchReceivedEventArgs e)
        {
            if (e.Simulator.Handle == Client.Network.CurrentSim.Handle)
            {
                terrain.Modified = true;
            }
        }

        private void Netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            // Ensure dispose runs on UI thread
            ThreadingHelper.SafeInvoke(this, new Action(Dispose), Instance.MonoRuntime);
        }

        private void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => Network_SimChanged(sender, e)), Instance.MonoRuntime);
                return;
            }

            terrain.ResetTerrain();
            lock (sculptCache)
            {
                foreach (var img in sculptCache.Values)
                    img.Dispose();
                sculptCache.Clear();
            }

            lock (Prims) Prims.Clear();
            lock (Avatars) Avatars.Clear();
            
            // Clear adjacent simulator terrains
            lock (adjacentSimsLock)
            {
                foreach (var adjTerrain in adjacentTerrains.Values)
                {
                    adjTerrain.Dispose();
                }
                adjacentTerrains.Clear();
            }
            
            SetWaterPlanes();
            LoadCurrentPrims();
            InitCamera();
        }

        protected void KillObjectHandler(object sender, PacketReceivedEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => KillObjectHandler(sender, e)), Instance.MonoRuntime);
                return;
            }

            var kill = (KillObjectPacket)e.Packet;

            lock (Prims)
            {
                foreach (var obj in kill.ObjectData)
                {
                    var id = obj.ID;
                    if (Prims.ContainsKey(id))
                    {
                        Prims[id].Dispose();
                        Prims.Remove(id);
                    }
                }
            }

            lock (Avatars)
            {
                foreach (var ob in kill.ObjectData)
                {
                    var id = ob.ID;
                    if (Avatars.ContainsKey(id))
                    {
                        Avatars[id].Dispose();
                        Avatars.Remove(id);
                    }
                }
            }
        }

        private void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            if (e.Prim.ID == Client.Self.AgentID)
            {
                trackedObject = myself;
            }

            //If it is an avatar, we don't need to deal with the terse update stuff, unless it sends textures to us
            if (e.Prim.PrimData.PCode == PCode.Avatar && e.Update.Textures == null)
                return;

            UpdatePrimBlocking(e.Prim);
        }

        private void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            UpdatePrimBlocking(e.Prim);
        }

        private void Objects_AvatarUpdate(object sender, AvatarUpdateEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            AddAvatarToScene(e.Avatar);
        }


        // This is called when ever an animation play state changes, that might be a start/stop event etc
        // the entire list of animations is sent each time and it is our job to determine which are new and
        // which are deleted

        private void AvatarAnimationChanged(object sender, AvatarAnimationEventArgs e)
        {
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => AvatarAnimationChanged(sender, e)), Instance.MonoRuntime);
                return;
            }

            // We don't currently have UUID -> RenderAvatar mapping, so we need to walk the list
            foreach (var av in Avatars.Values.Where(av => av.avatar.ID == e.AvatarID))
            {
                UpdateAvatarAnimations(av);
                break;
            }
        }

        private void AnimReceivedCallback(AssetDownload transfer, Asset asset)
        {

            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => AnimReceivedCallback(transfer, asset)), Instance.MonoRuntime);
                return;
            }

            if (transfer.Success)
            {
                skeleton.addanimation(asset, transfer.ID, null, asset.AssetID);
            }
            else
            {
                if (AssetFetchFailCount.TryGetValue(transfer.AssetID, out var noFails))
                {
                    noFails++;
                }

                AssetFetchFailCount[transfer.AssetID] = noFails;

            }


        }

        private void Avatars_AvatarAppearance(object sender, AvatarAppearanceEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;

            var a = e.Simulator.ObjectsAvatars.FirstOrDefault(av => av.Value.ID == e.AvatarID);
            if (a.Value != null)
            {
                AddAvatarToScene(a.Value);
            }
        }

        private void Appearance_AppearanceSet(object sender, AppearanceSetEventArgs e)
        {
            if (e.Success)
            {
                if (Client.Network.CurrentSim.ObjectsAvatars.TryGetValue(Client.Self.LocalID, out var me))
                {
                    AddAvatarToScene(me);
                }
            }
        }

        #endregion Network messaage handlers

        #region glControl setup and disposal

        public void SetupGLControl()
        {
            // Crash fix for users with SDL2.dll in their path. OpenTK will attempt to use
            //   SDL2 if it's available, but SDL2 is unsupported and will crash users.
            OpenTK.Toolkit.Init(new OpenTK.ToolkitOptions
            {
                Backend = OpenTK.PlatformBackend.PreferNative
            });

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
                    for (var aa = 0; aa <= 4; aa += 2)
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
                Logger.Warn(ex.Message, Client);
                glControl = null;
            }

            if (glControl == null)
            {
                Logger.Error("Failed to initialize OpenGL control, cannot continue", Client);
                return;
            }

            Logger.Info($"Initializing OpenGL mode: {GLMode}");

            glControl.Paint += glControl_Paint;
            glControl.Resize += glControl_Resize;
            glControl.MouseDown += glControl_MouseDown;
            glControl.MouseUp += glControl_MouseUp;
            glControl.MouseMove += glControl_MouseMove;
            glControl.MouseWheel += glControl_MouseWheel;
            glControl.Load += glControl_Load;
            glControl.Disposed += glControl_Disposed;
            glControl.Dock = DockStyle.Fill;
            glControl.VSync = false;
            Controls.Add(glControl);
            glControl.BringToFront();
        }

        private void glControl_UnhookEvents()
        {
            glControl.Paint -= glControl_Paint;
            glControl.Resize -= glControl_Resize;
            glControl.MouseDown -= glControl_MouseDown;
            glControl.MouseUp -= glControl_MouseUp;
            glControl.MouseMove -= glControl_MouseMove;
            glControl.MouseWheel -= glControl_MouseWheel;
            glControl.Load -= glControl_Load;
            glControl.Disposed -= glControl_Disposed;

        }

        private void glControl_Disposed(object sender, EventArgs e)
        {
            glControl_UnhookEvents();
        }

        private void SetSun()
        {
            ambientColor = new OpenTK.Vector4(ambient, ambient, ambient, diffuse);
            diffuseColor = new OpenTK.Vector4(diffuse, diffuse, diffuse, diffuse);
            specularColor = new OpenTK.Vector4(specular, specular, specular, specular);
            GL.Light(LightName.Light0, LightParameter.Ambient, ambientColor);
            GL.Light(LightName.Light0, LightParameter.Diffuse, diffuseColor);
            GL.Light(LightName.Light0, LightParameter.Specular, specularColor);
            GL.Light(LightName.Light0, LightParameter.Position, sunPos);
        }

        /// <summary>
        /// Update lighting from RenderSettings
        /// </summary>
        public void UpdateLighting()
        {
            ambient = RenderSettings.AmbientLight;
            diffuse = RenderSettings.DiffuseLight;
            specular = RenderSettings.SpecularLight;
            SetSun();
            
            // Update sky sun direction when lighting changes
            if (sky != null)
            {
                Vector3 sunDir = new Vector3(sunPos[0], sunPos[1], sunPos[2]);
                sunDir.Normalize();
                sky.UpdateSunDirection(sunDir);
            }
        }

        private bool glControlLoaded = false;

        private void glControl_Load(object sender, EventArgs e)
        {
            if (glControlLoaded) return;

            try
            {
                // Initialize OpenGL using shared helper
                GLInitializer.InitializeGL(ambient, diffuse, specular, 1.0f, sunPos);
                SetSun();

                // Compatibility checks using shared detector
                var context = glControl.Context as OpenTK.Graphics.IGraphicsContextInternal;
                var glExtensions = GL.GetString(StringName.Extensions);
                GLCapabilityDetector.DetectCapabilities(context, glExtensions, instance.GlobalSettings);

                RenderingEnabled = true;
                // Call the resizing function which sets up the GL drawing window
                // and will also invalidate the GL control
                glControl_Resize(null, null);
                RenderingEnabled = false;
                // Attempt to create a shared worker GraphicsContext for OpenTK 3.3.3
                try
                {
                    // Try to obtain the GLControl's internal IWindowInfo via reflection and create a shared GraphicsContext on this thread
                    object winInfoObj = null;
                    var t = glControl.GetType();
                    var f = t.GetField("windowInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            ?? t.GetField("m_windowInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (f != null)
                    {
                        winInfoObj = f.GetValue(glControl);
                    }

                    if (winInfoObj == null)
                    {
                        foreach (var fi in t.GetFields(System.Reflection.BindingFlags.Instance |
                                                       System.Reflection.BindingFlags.NonPublic))
                        {
                            if (typeof(IWindowInfo).IsAssignableFrom(fi.FieldType))
                            {
                                winInfoObj = fi.GetValue(glControl);
                                if (winInfoObj != null) break;
                            }
                        }
                    }

                    sharedWindowInfo = winInfoObj as IWindowInfo;

                    if (sharedWindowInfo != null)
                    {
                        // Use reflection to find a compatible GraphicsContext constructor and create the worker context that shares with the GLControl
                        var gcType = typeof(GraphicsContext);
                        var ctors = gcType.GetConstructors(System.Reflection.BindingFlags.Public |
                                                           System.Reflection.BindingFlags.Instance);
                        object created = null;
                        foreach (var ctor in ctors)
                        {
                            var pars = ctor.GetParameters();
                            var args = new object[pars.Length];
                            var ok = true;
                            var intPadValue = 0;
                            for (var i = 0; i < pars.Length; i++)
                            {
                                var pType = pars[i].ParameterType;
                                if (pType == typeof(GraphicsMode) || pType.FullName.Contains("GraphicsMode"))
                                {
                                    args[i] = GLMode ?? new GraphicsMode();
                                }
                                else if (typeof(IWindowInfo).IsAssignableFrom(pType))
                                {
                                    args[i] = sharedWindowInfo;
                                }
                                else if (pType == typeof(int))
                                {
                                    // supply major/minor or placeholder
                                    if (intPadValue == 0)
                                    {
                                        args[i] = 3;
                                        intPadValue++;
                                    }
                                    else
                                    {
                                        args[i] = 0;
                                    }
                                }
                                else if (pType.FullName.Contains("GraphicsContextFlags"))
                                {
                                    args[i] = GraphicsContextFlags.Default;
                                }
                                else if (typeof(IGraphicsContext).IsAssignableFrom(pType) ||
                                         pType.FullName.Contains("IGraphicsContext"))
                                {
                                    args[i] = glControl.Context;
                                }
                                else
                                {
                                    ok = false;
                                    break;
                                }
                            }

                            if (!ok) continue;

                            try
                            {
                                created = ctor.Invoke(args);
                                if (created != null) break;
                            }
                            catch
                            {
                                created = null;
                            }
                        }

                        textureContext = created as IGraphicsContext;
                        if (textureContext == null)
                        {
                            Logger.Debug("No compatible GraphicsContext constructor found or creation failed on texture thread", Client);
                        }
                        else
                        {
                            try
                            {
                                textureContext.MakeCurrent(sharedWindowInfo);
                            }
                            catch (Exception ex)
                            {
                                Logger.Debug("Failed to make worker context current: " + ex.Message, ex);
                                try { textureContext.MakeCurrent(null); } catch { }
                                textureContext = null;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("Failed to prepare shared texture context: " + ex.Message, ex);
                    textureContext = null;
                    sharedWindowInfo = null;
                }

                // Start the texture decoding thread (it will use textureContext if available)
                textureDecodingThread = new Thread(TextureThread)
                {
                    IsBackground = true,
                    Name = "TextureDecodingThread"
                };
                textureDecodingThread.Start();
                TextureThreadContextReady.WaitOne(1000, false);
                // glControl.MakeCurrent();
                InitWater();
                InitShaders();
                RenderingEnabled = true;
                glControlLoaded = true;
                LoadCurrentPrims();
            }
            catch (Exception ex)
            {
                RenderingEnabled = false;
                Logger.Warn("Failed to initialize OpenGL control", ex, Client);
            }
        }

        private void InitShaders()
        {
            if (!RenderSettings.HasShaders)
            {
                shadersAvailable = false;
                return;
            }

            try
            {
                shaderManager = new ShaderManager();
                shadersAvailable = shaderManager.Initialize();

                if (!shadersAvailable)
                {
                    Logger.Debug("Shader system available but no shaders loaded");
                }
                else
                {
                    Logger.Debug("Shader system initialized successfully");
                    var prog = shaderManager.GetProgram("shiny");
                    // Bind default texture unit for colorMap uniform if present
                    prog?.SetUniform1("colorMap", 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to initialize shader system: {ex.Message}", ex);
                shadersAvailable = false;

                if (shaderManager != null)
                {
                    shaderManager.Dispose();
                    shaderManager = null;
                }
            }
        }

        // Return attribute location from current shiny program
        public int GetShaderAttr(string name)
        {
            if (!RenderSettings.HasShaders) return -1;
            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                return prog?.Attr(name) ?? -1;
            }
            catch { return -1; }
        }
        
        /// <summary>
        /// Get the shader manager for use by rendering sub-components
        /// </summary>
        public ShaderManager GetShaderManager()
        {
            return shaderManager;
        }

        // Update shader matrix uniforms (call when modelview has been modified)
        public void UpdateShaderMatrices()
        {
            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;

            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;

                // Verify a shader program is actually active before setting uniform
                GL.GetInteger(GetPName.CurrentProgram, out int activeProgram);
                if (activeProgram == 0 || activeProgram != prog.ID) return;

                var uMVP = prog.Uni("uMVP");
                var uModelView = prog.Uni("uModelView");
                var uNormal = prog.Uni("uNormalMatrix");

                if (uMVP == -1 && uModelView == -1 && uNormal == -1) return;

                GL.GetFloat(GetPName.ProjectionMatrix, out OpenTK.Matrix4 proj);
                GL.GetFloat(GetPName.ModelviewMatrix, out OpenTK.Matrix4 mv);

                if (uMVP != -1)
                {
                    var mvp = mv * proj;
                    GL.UniformMatrix4(uMVP, false, ref mvp);
                }

                if (uModelView != -1)
                {
                    GL.UniformMatrix4(uModelView, false, ref mv);
                }

                if (uNormal != -1)
                {
                    var normal = new OpenTK.Matrix3(
                        mv.M11, mv.M12, mv.M13,
                        mv.M21, mv.M22, mv.M23,
                        mv.M31, mv.M32, mv.M33);

                    // Compute inverse transpose for correct normal transformation
                    try
                    {
                        normal = normal.Inverted();
                        normal = new OpenTK.Matrix3(
                            normal.M11, normal.M21, normal.M31,
                            normal.M12, normal.M22, normal.M32,
                            normal.M13, normal.M23, normal.M33);
                        GL.UniformMatrix3(uNormal, false, ref normal);
                    }
                    catch
                    {
                        // If matrix is singular, use identity
                        var identity = OpenTK.Matrix3.Identity;
                        GL.UniformMatrix3(uNormal, false, ref identity);
                    }
                }
            }
            catch { }
        }

        // Set material color uniform for shader
        public void SetShaderMaterialColor(OpenMetaverse.Color4 color)
        {
            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;

            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;

                var uMaterialColor = prog.Uni("materialColor");
                if (uMaterialColor != -1)
                {
                    GL.Uniform4(uMaterialColor, color.R, color.G, color.B, color.A);
                }
            }
            catch { }
        }

        // Set hasTexture uniform for shader (works on currently active program)
        public void SetShaderHasTexture(bool hasTexture)
        {
            if (!RenderSettings.HasShaders) return;

            try
            {
                // Get the currently active shader program
                GL.GetInteger(GetPName.CurrentProgram, out int activeProgram);
                if (activeProgram == 0) return;

                // Try shiny program first
                var shinyProg = shaderManager?.GetProgram("shiny");
                if (shinyProg != null && shinyProg.ID == activeProgram)
                {
                    var uHasTexture = shinyProg.Uni("hasTexture");
                    if (uHasTexture != -1)
                    {
                        GL.Uniform1(uHasTexture, hasTexture ? 1 : 0);
                    }
                    return;
                }

                // Try avatar program
                var avatarProg = shaderManager?.GetProgram("avatar");
                if (avatarProg != null && avatarProg.ID == activeProgram)
                {
                    var uHasTexture = avatarProg.Uni("hasTexture");
                    if (uHasTexture != -1)
                    {
                        GL.Uniform1(uHasTexture, hasTexture ? 1 : 0);
                    }
                }
            }
            catch { }
        }

        // Set per-face glow uniform for currently active shader
        public void SetShaderGlow(float glow)
        {
            if (!RenderSettings.HasShaders) return;

            try
            {
                // Get the currently active shader program
                GL.GetInteger(GetPName.CurrentProgram, out int activeProgram);
                if (activeProgram == 0) return;

                // Try shiny program first
                var shinyProg = shaderManager?.GetProgram("shiny");
                if (shinyProg != null && shinyProg.ID == activeProgram)
                {
                    var uGlow = shinyProg.Uni("glow");
                    if (uGlow != -1)
                    {
                        GL.Uniform1(uGlow, glow);
                    }
                    return;
                }

                // Try avatar program
                var avatarProg = shaderManager?.GetProgram("avatar");
                if (avatarProg != null && avatarProg.ID == activeProgram)
                {
                    var uGlow = avatarProg.Uni("glow");
                    if (uGlow != -1)
                    {
                        GL.Uniform1(uGlow, glow);
                    }
                }
            }
            catch { }
        }

        // Set shadow uniforms for shiny shader
        public void SetShaderShadows(bool enable, float intensity)
        {
            // Update runtime settings
            RenderSettings.EnableShadows = enable;
            RenderSettings.ShadowIntensity = intensity;

            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;
            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;

                var uEnable = prog.Uni("enableShadows");
                if (uEnable != -1) GL.Uniform1(uEnable, enable ? 1 : 0);

                var uIntensity = prog.Uni("shadowIntensity");
                if (uIntensity != -1) GL.Uniform1(uIntensity, intensity);
            }
            catch { }
        }

        // Set gamma uniform on active shaders (shiny and avatar)
        public void SetShaderGamma(float gamma)
        {
            if (!RenderSettings.HasShaders) return;
            try
            {
                var shiny = shaderManager?.GetProgram("shiny");
                if (shiny != null)
                {
                    var ug = shiny.Uni("gamma");
                    if (ug != -1) GL.Uniform1(ug, gamma);
                }

                var avatar = shaderManager?.GetProgram("avatar");
                if (avatar != null)
                {
                    var ug2 = avatar.Uni("gamma");
                    if (ug2 != -1) GL.Uniform1(ug2, gamma);
                }
            }
            catch { }
        }

        // Set default shadow uniforms (optional)
        public void SetShaderShadows()
        {
            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;

            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;

                var uEnableShadows = prog.Uni("enableShadows");
                if (uEnableShadows != -1) GL.Uniform1(uEnableShadows, RenderSettings.EnableShadows ? 1 : 0);
                var uShadowIntensity = prog.Uni("shadowIntensity");
                if (uShadowIntensity != -1) GL.Uniform1(uShadowIntensity, RenderSettings.ShadowIntensity);
            }
            catch { }
        }

        // Set material layer uniforms for the shiny shader (exposed to RenderPrimitive)
        public void SetShaderMaterialLayer(bool hasMaterial, OpenTK.Vector3 specColor, float shininess, float strength)
        {
            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;
            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;

                var uHas = prog.Uni("hasMaterial");
                if (uHas != -1) GL.Uniform1(uHas, hasMaterial ? 1 : 0);

                var uSpec = prog.Uni("materialSpecularColor");
                if (uSpec != -1) GL.Uniform3(uSpec, specColor.X, specColor.Y, specColor.Z);

                var uSh = prog.Uni("materialShininess");
                if (uSh != -1) GL.Uniform1(uSh, shininess);

                var uStr = prog.Uni("materialSpecularStrength");
                if (uStr != -1) GL.Uniform1(uStr, strength);
            }
            catch { }
        }

        // Select shiny shader as the current shader
        public void StartShiny()
        {
            if (RenderSettings.EnableShiny)
            {
                if (shaderManager == null) return;
                if (!shaderManager.StartShader("shiny")) return;

                try
                {
                    var prog = shaderManager.GetProgram("shiny");
                    if (prog == null) return;

                    // Set light direction uniform
                    var uLight = prog.Uni("lightDir");
                    if (uLight != -1)
                    {
                        try
                        {
                            // Get current modelview matrix
                            GL.GetFloat(GetPName.ModelviewMatrix, out OpenTK.Matrix4 mv);
                            // Transform sun direction into eye space using w=0 to avoid translation
                            var sunDir = new OpenTK.Vector4(sunPos[0], sunPos[1], sunPos[2], 0f);
                            var trans = OpenTK.Vector4.Transform(sunDir, mv);
                            var ld = OpenTK.Vector3.Normalize(new OpenTK.Vector3(trans.X, trans.Y, trans.Z));
                            GL.Uniform3(uLight, ld);
                        }
                        catch
                        {
                            // fallback to sending raw sunPos if transform fails
                            GL.Uniform3(uLight, sunPos[0], sunPos[1], sunPos[2]);
                        }
                    }

                    // Set color uniforms
                    var ua = prog.Uni("ambientColor");
                    if (ua != -1) GL.Uniform4(ua, ambientColor.X, ambientColor.Y, ambientColor.Z, ambientColor.W);
                    var ud = prog.Uni("diffuseColor");
                    if (ud != -1) GL.Uniform4(ud, diffuseColor.X, diffuseColor.Y, diffuseColor.Z, diffuseColor.W);
                    var us = prog.Uni("specularColor");
                    if (us != -1) GL.Uniform4(us, specularColor.X, specularColor.Y, specularColor.Z, specularColor.W);

                    // Set default material color (white, fully opaque) - CRITICAL for correct coloring
                    var uMatColor = prog.Uni("materialColor");
                    if (uMatColor != -1) GL.Uniform4(uMatColor, 1.0f, 1.0f, 1.0f, 1.0f);

                    // Ensure colorMap sampler is bound to texture unit 0
                    var uColorMap = prog.Uni("colorMap");
                    if (uColorMap != -1)
                    {
                        GL.Uniform1(uColorMap, 0);
                    }

                    // Set default hasTexture to 0 (no texture)
                    var uHasTexture = prog.Uni("hasTexture");
                    if (uHasTexture != -1) GL.Uniform1(uHasTexture, 0);

                    // Set default glow to 0.0
                    var ug = prog.Uni("glow");
                    if (ug != -1) GL.Uniform1(ug, 0.0f);

                    // Set default emissive strength
                    var ues = prog.Uni("emissiveStrength");
                    if (ues != -1) GL.Uniform1(ues, 1.0f);

                    // Ensure glowStrength multiplier is set so per-face glow contributes
                    var uGlowStr = prog.Uni("glowStrength");
                    if (uGlowStr != -1) GL.Uniform1(uGlowStr, 1.0f);

                    // Set default shininess and specular strength
                    var ushexp = prog.Uni("shininessExp");
                    if (ushexp != -1) GL.Uniform1(ushexp, 24.0f);
                    var uspecstr = prog.Uni("specularStrength");
                    if (uspecstr != -1) GL.Uniform1(uspecstr, 1.0f);

                    // Set default gamma (1.0 = no correction)
                    var ugu = prog.Uni("gamma");
                    if (ugu != -1) GL.Uniform1(ugu, RenderSettings.Gamma);

                    // Set shadow uniforms
                    var uEnableShadows = prog.Uni("enableShadows");
                    if (uEnableShadows != -1) GL.Uniform1(uEnableShadows, RenderSettings.EnableShadows ? 1 : 0);
                    var uShadowIntensity = prog.Uni("shadowIntensity");
                    if (uShadowIntensity != -1) GL.Uniform1(uShadowIntensity, RenderSettings.ShadowIntensity);

                    // Update matrix uniforms for current modelview/projection
                    UpdateShaderMatrices();
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Update shader matrix uniforms for the avatar shader
        /// </summary>
        public void UpdateAvatarShaderMatrices()
        {
            if (!RenderSettings.HasShaders) return;

            try
            {
                var prog = shaderManager?.GetProgram("avatar");
                if (prog == null) return;

                // Verify avatar shader program is actually active before setting uniforms
                GL.GetInteger(GetPName.CurrentProgram, out int activeProgram);
                if (activeProgram == 0 || activeProgram != prog.ID) return;

                var uMVP = prog.Uni("uMVP");
                var uModelView = prog.Uni("uModelView");
                var uNormal = prog.Uni("uNormalMatrix");

                if (uMVP == -1 && uModelView == -1 && uNormal == -1) return;

                GL.GetFloat(GetPName.ProjectionMatrix, out ProjectionMatrix);
                GL.GetFloat(GetPName.ModelviewMatrix, out ModelMatrix);

                if (uMVP != -1)
                {
                    var mvp = ModelMatrix * ProjectionMatrix;
                    GL.UniformMatrix4(uMVP, false, ref mvp);
                }

                if (uModelView != -1)
                {
                    GL.UniformMatrix4(uModelView, false, ref ModelMatrix);
                }

                if (uNormal != -1)
                {
                    var normal = new OpenTK.Matrix3(
                        ModelMatrix.M11, ModelMatrix.M12, ModelMatrix.M13,
                        ModelMatrix.M21, ModelMatrix.M22, ModelMatrix.M23,
                        ModelMatrix.M31, ModelMatrix.M32, ModelMatrix.M33);

                    // Compute inverse transpose for correct normal transformation
                    try
                    {
                        normal = normal.Inverted();
                        normal = new OpenTK.Matrix3(
                            normal.M11, normal.M21, normal.M31,
                            normal.M12, normal.M22, normal.M32,
                            normal.M13, normal.M23, normal.M33);
                        GL.UniformMatrix3(uNormal, false, ref normal);
                    }
                    catch
                    {
                        // If matrix is singular, use identity
                        var identity = OpenTK.Matrix3.Identity;
                        GL.UniformMatrix3(uNormal, false, ref identity);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Stop the avatar shader and return to fixed-function pipeline
        /// </summary>
        public void StopAvatarShader()
        {
            if (RenderSettings.HasShaders && shaderManager != null)
            {
                try
                {
                    shaderManager.StopShader();
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Stop the shiny shader and return to fixed-function pipeline
        /// </summary>
        public void StopShiny()
        {
            if (RenderSettings.EnableShiny && shaderManager != null)
            {
                try
                {
                    shaderManager.StopShader();
                }
                catch { }
            }
        }

        public void SetShaderShininessExp(float exp)
        {
            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;
            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;
                var u = prog.Uni("shininessExp");
                if (u != -1) GL.Uniform1(u, exp);
            }
            catch { }
        }

        public void SetShaderSpecularStrength(float s)
        {
            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;
            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;
                var u = prog.Uni("specularStrength");
                if (u != -1) GL.Uniform1(u, s);
            }
            catch { }
        }

        public void SetShaderEmissiveStrength(float s)
        {
            if (!RenderSettings.HasShaders || !RenderSettings.EnableShiny) return;
            try
            {
                var prog = shaderManager?.GetProgram("shiny");
                if (prog == null) return;
                var u = prog.Uni("emissiveStrength");
                if (u != -1) GL.Uniform1(u, s);
            }
            catch { }
        }

        /// <summary>
        /// Start the avatar shader for avatar rendering
        /// </summary>
        public void StartAvatarShader()
        {
            if (!RenderSettings.HasShaders || !RenderSettings.AvatarRenderingEnabled) return;
            if (shaderManager == null) return;
            if (!shaderManager.StartShader("avatar")) return;

            try
            {
                var prog = shaderManager.GetProgram("avatar");
                if (prog == null) return;

                // Set light direction uniform
                var uLight = prog.Uni("lightDir");
                if (uLight != -1)
                {
                    try
                    {
                        GL.GetFloat(GetPName.ModelviewMatrix, out OpenTK.Matrix4 mv);
                        var sunDir = new OpenTK.Vector4(sunPos[0], sunPos[1], sunPos[2], 0f);
                        var trans = OpenTK.Vector4.Transform(sunDir, mv);
                        var ld = OpenTK.Vector3.Normalize(new OpenTK.Vector3(trans.X, trans.Y, trans.Z));
                        GL.Uniform3(uLight, ld);
                    }
                    catch
                    {
                        GL.Uniform3(uLight, sunPos[0], sunPos[1], sunPos[2]);
                    }
                }

                // Set color uniforms
                var ua = prog.Uni("ambientColor");
                if (ua != -1) GL.Uniform4(ua, ambientColor.X, ambientColor.Y, ambientColor.Z, ambientColor.W);
                var ud = prog.Uni("diffuseColor");
                if (ud != -1) GL.Uniform4(ud, diffuseColor.X, diffuseColor.Y, diffuseColor.Z, diffuseColor.W);
                var us = prog.Uni("specularColor");
                if (us != -1) GL.Uniform4(us, specularColor.X, specularColor.Y, specularColor.Z, specularColor.W);

                // Set default material color (white, fully opaque)
                var uMatColor = prog.Uni("materialColor");
                if (uMatColor != -1) GL.Uniform4(uMatColor, 1.0f, 1.0f, 1.0f, 1.0f);

                // Ensure colorMap sampler is bound to texture unit 0
                var uColorMap = prog.Uni("colorMap");
                if (uColorMap != -1) GL.Uniform1(uColorMap, 0);

                // Set default hasTexture to 0
                var uHasTexture = prog.Uni("hasTexture");
                if (uHasTexture != -1) GL.Uniform1(uHasTexture, 0);

                // Set default glow to 0.0
                var ug = prog.Uni("glow");
                if (ug != -1) GL.Uniform1(ug, 0.0f);

                // Set default emissive strength
                var ues = prog.Uni("emissiveStrength");
                if (ues != -1) GL.Uniform1(ues, 1.0f);

                // Ensure glowStrength multiplier is set so per-face glow contributes
                var uGlowStr = prog.Uni("glowStrength");
                if (uGlowStr != -1) GL.Uniform1(uGlowStr, 1.0f);

                // Set default shininess and specular strength
                var ushexp = prog.Uni("shininessExp");
                if (ushexp != -1) GL.Uniform1(ushexp, 24.0f);
                var uspecstr = prog.Uni("specularStrength");
                if (uspecstr != -1) GL.Uniform1(uspecstr, 1.0f);

                // Set default gamma (1.0 = no correction)
                var ugu = prog.Uni("gamma");
                if (ugu != -1) GL.Uniform1(ugu, RenderSettings.Gamma);

                // Set shadow uniforms
                var uEnableShadows = prog.Uni("enableShadows");
                if (uEnableShadows != -1) GL.Uniform1(uEnableShadows, RenderSettings.EnableShadows ? 1 : 0);
                var uShadowIntensity = prog.Uni("shadowIntensity");
                if (uShadowIntensity != -1) GL.Uniform1(uShadowIntensity, RenderSettings.ShadowIntensity);

                // Update matrix uniforms
                UpdateAvatarShaderMatrices();
            }
            catch
            {
            }
        }

        #endregion glControl setup and disposal

        #region glControl paint and resize events

        private void MainRenderLoop()
        {
            if (!RenderingEnabled) return;
            lastFrameTime = (float)renderTimer.Elapsed.TotalSeconds;

            // Something went horribly wrong
            if (lastFrameTime < 0) return;

            // Stopwatch loses resolution if it runs for a long time, reset it
            renderTimer.Reset();
            renderTimer.Start();

            // Determine if we need to throttle frame rate
            var throttle = false;

            // Some other app has focus
            if (Form.ActiveForm == null)
            {
                throttle = true;
            }
            else
            {
                // If we're docked but not active tab, throttle
                if (!RadegastTab.Selected && !RadegastTab.Detached)
                {
                    throttle = true;
                }
            }

            // Limit FPS to max 15
            if (throttle)
            {
                var msToSleep = 66 - ((int)(lastFrameTime / 1000));
                if (msToSleep < 10) msToSleep = 10;
                Thread.Sleep(msToSleep);
            }

            Render(false);

            glControl.SwapBuffers();
        }

        private void glControl_Paint(object sender, EventArgs e)
        {
            MainRenderLoop();
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

            SetPerspective();

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();
        }

        #endregion glControl paint and resize events

        // Switch to ortho display mode for drawing hud
        public void GLHUDBegin()
        {
            HUDRenderer.BeginHUD(glControl?.Width ?? 0, glControl?.Height ?? 0);
        }

        // Switch back to frustum display mode
        public void GLHUDEnd()
        {
            HUDRenderer.EndHUD();
        }

        private void GenericTaskRunner()
        {
            Logger.DebugLog("Started generic task thread");

            try
            {
                var token = cancellationTokenSource?.Token ?? throw new NullReferenceException();
                while (true)
                {
                    PendingTasksAvailable.Wait(token);
                    if (!PendingTasks.TryDequeue(out var task)) break;
                    task.Invoke();
                }
            }
            catch (ObjectDisposedException)
            {
                Logger.Debug("GenericTaskRunner was cancelled");
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("GenericTaskRunner was cancelled");
            }

            Logger.DebugLog("Generic task thread exited");
        }

        private void LoadCurrentPrims()
        {
            if (!Client.Network.Connected) return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                if (RenderSettings.PrimitiveRenderingEnabled)
                {
                    var mainPrims = (from p in Client.Network.CurrentSim.ObjectsPrimitives
                        where p.Value != null
                        where p.Value.ParentID == 0
                        select p.Value);
                    foreach (var mainPrim in mainPrims)
                    {
                        UpdatePrimBlocking(mainPrim);
                        var children = (from p in Client.Network.CurrentSim.ObjectsPrimitives
                            where p.Value != null
                            where p.Value.ParentID == mainPrim.LocalID
                            select p.Value).ToList();
                        children.ForEach(UpdatePrimBlocking);
                    }
                }

                if (RenderSettings.AvatarRenderingEnabled)
                {
                    var avis = (from a in Client.Network.CurrentSim.ObjectsAvatars
                        where a.Value != null
                        select a.Value);
                    foreach (var avatar in avis)
                    {
                        UpdatePrimBlocking(avatar);
                        var attachments = (from p in client.Network.CurrentSim.ObjectsPrimitives
                            where p.Value != null
                            where p.Value.ParentID == avatar.LocalID
                            select p.Value);
                        foreach (var attachment in attachments)
                        {
                            UpdatePrimBlocking(attachment);
                            var attachedChildren = (from p in client.Network.CurrentSim.ObjectsPrimitives
                                where p.Value != null
                                where p.Value.ParentID == attachment.LocalID
                                select p.Value);
                            foreach (var attachedChild in attachedChildren)
                            {
                                UpdatePrimBlocking(attachedChild);
                            }
                        }
                    }
                }
            });
        }

        private void ControlLoaded(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
            {
                InitAvatarData();
                AvatarDataInitialized();
            });
        }

        #region Private methods (the meat)

        private void AvatarDataInitialized()
        {
            if (IsDisposed) return;

            // Ensure that this is done on the main thread
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvokeSync(this, new Action(AvatarDataInitialized), Instance.MonoRuntime);
                return;
            }

            //FIX ME
            //foreach (VisualParamEx vpe in VisualParamEx.morphParams.Values)
            //{
            //    comboBox_morph.Items.Add(vpe.Name);
            //}

            //foreach (VisualParamEx vpe in VisualParamEx.drivenParams.Values)
            //{
            //    comboBox_driver.Items.Add(vpe.Name);
            //}

            SetupGLControl();
        }

        private void InitAvatarData()
        {
            GLAvatar.loadlindenmeshes2("avatar_lad.xml");
        }

        public void UpdateCamera()
        {
            if (Client != null)
            {
                Client.Self.Movement.Camera.LookAt(Camera.Position, Camera.FocalPoint);
                Client.Self.Movement.Camera.Far = 4 * (Camera.Far = DrawDistance);
            }
        }

        private void InitCamera()
        {
            var camPos = Client.Self.SimPosition + new Vector3(-4, 0, 1) * Client.Self.Movement.BodyRotation;
            Camera.Position = camPos;
            Camera.FocalPoint = Client.Self.SimPosition + new Vector3(5, 0, 0) * Client.Self.Movement.BodyRotation;
            Camera.Zoom = 1.0f;
            Camera.Far = DrawDistance;
        }

        private Vector3 PrimPos(Primitive prim)
        {
            PrimPosAndRot(GetSceneObject(prim.LocalID), out var pos, out _);
            return pos;
        }

        /// <summary>
        /// Gets attachment state of a prim
        /// </summary>
        /// <param name="parentLocalID">Parent id</param>
        /// <returns>True, if prim is part of an attachment</returns>
        private bool IsAttached(uint parentLocalID)
        {
            if (parentLocalID == 0) return false;
            try
            {
                if (Client.Network.CurrentSim.ObjectsAvatars.ContainsKey(parentLocalID))
                {
                    return true;
                }
                else if (Client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(parentLocalID))
                {
                    return IsAttached(Client.Network.CurrentSim.ObjectsPrimitives[parentLocalID].ParentID);
                }
            }
            catch { }
            return false;
        }

        private SceneObject GetSceneObject(uint localID)
        {
            if (Prims.TryGetValue(localID, out var parent))
            {
                return parent;
            }

            return Avatars.TryGetValue(localID, out var avi) ? avi : null;
        }

        /// <summary>
        /// Calculates finer rendering position for objects on the scene
        /// </summary>
        /// <param name="obj">SceneObject whose position is calculated</param>
        /// <param name="pos">Rendering position</param>
        /// <param name="rot">Rendering rotation</param>
        private void PrimPosAndRot(SceneObject obj, out Vector3 pos, out Quaternion rot)
        {
            // Sanity check
            if (obj == null)
            {
                pos = RHelp.InvalidPosition;
                rot = Quaternion.Identity;
                return;
            }

            if (obj.BasePrim.ParentID == 0)
            {
                // We are the root prim, return our interpolated position
                pos = obj.InterpolatedPosition;
                rot = obj.InterpolatedRotation;
            }
            else
            {
                pos = RHelp.InvalidPosition;
                rot = Quaternion.Identity;

                // Not root, find our parent
                var p = GetSceneObject(obj.BasePrim.ParentID);
                if (p == null) return;

                // If we don't know parent position, recursively find out
                if (!p.PositionCalculated)
                {
                    PrimPosAndRot(p, out p.RenderPosition, out p.RenderRotation);
                    p.DistanceSquared = Vector3.DistanceSquared(Camera.RenderPosition, p.RenderPosition);
                    p.PositionCalculated = true;
                }

                var parentPos = p.RenderPosition;
                var parentRot = p.RenderRotation;

                if (p is RenderPrimitive)
                {
                    // Child prim (our parent is another prim here)
                    pos = parentPos + obj.InterpolatedPosition * parentRot;
                    rot = parentRot * obj.InterpolatedRotation;
                }
                else if (p is RenderAvatar parentav) // Calculating position and rotation of the root prim of an attachment here
                                                     // (our parent is an avatar here)
                {

                    // Check for invalid attachment point
                    var attachment_index = (int)obj.BasePrim.PrimData.AttachmentPoint;
                    if (attachment_index >= GLAvatar.attachment_points.Count) return;
                    var apoint = GLAvatar.attachment_points[attachment_index];
                    var skel = parentav.glavatar.skel;
                    if (!skel.mBones.ContainsKey(apoint.joint)) return;

                    // Bone position and rotation
                    var bone = skel.mBones[apoint.joint];
                    var bpos = bone.getTotalOffset();
                    var brot = bone.getTotalRotation();

                    // Start with avatar position
                    pos = parentPos;
                    rot = parentRot;

                    // Move by pelvis offset
                    // FIXME 2 dictionary lookups via string key in render loop!
                    // pos -= (parentav.glavatar.skel.mBones["mPelvis"].animation_offset * parentav.RenderRotation) + parentav.glavatar.skel.getOffset("mPelvis") * rot;
                    //pos -= parentav.glavatar.skel.getOffset("mPelvis") * rot;
                    //rot = parentav.glavatar.skel.getRotation("mPelvis") * rot;
                    pos = parentav.AdjustedPosition(pos);
                    // Translate and rotate to the joint calculated position
                    pos += bpos * rot;
                    rot *= brot;

                    // Translate and rotate built in joint offset
                    pos += apoint.position * rot;
                    rot *= apoint.rotation;

                    // Translate and rotate from the offset from the attachment point
                    // set in teh appearance editor
                    pos += obj.BasePrim.Position * rot;
                    rot *= obj.BasePrim.Rotation;

                }
            }
        }

        /// <summary>
        /// Finds the closest distance between the given pos and an object
        /// (Assumes that the object is a box slightly)
        /// </summary>
        /// <param name="calcPos"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        private float FindClosestDistanceSquared(Vector3 calcPos, SceneObject p)
        {
            if (p.BoundingVolume == null
                || !RenderSettings.HeavierDistanceChecking
                || p.BoundingVolume.ScaledR < 10f)
            {
                return Vector3.DistanceSquared(calcPos, p.RenderPosition);
            }

            var posToCheckFrom = Vector3.Zero;
            //Get the bounding boxes for this prim
            var boundingBoxMin = p.RenderPosition + p.BoundingVolume.ScaledMin;
            var boundingBoxMax = p.RenderPosition + p.BoundingVolume.ScaledMax;
            posToCheckFrom.X = (calcPos.X < boundingBoxMin.X) ? boundingBoxMin.X : (calcPos.X > boundingBoxMax.X) ? boundingBoxMax.X : calcPos.X;
            posToCheckFrom.Y = (calcPos.Y < boundingBoxMin.Y) ? boundingBoxMin.Y : (calcPos.Y > boundingBoxMax.Y) ? boundingBoxMax.Y : calcPos.Y;
            posToCheckFrom.Z = (calcPos.Z < boundingBoxMin.Z) ? boundingBoxMin.Z : (calcPos.Z > boundingBoxMax.Z) ? boundingBoxMax.Z : calcPos.Z;
            return Vector3.DistanceSquared(calcPos, posToCheckFrom);
        }

        private float LODFactor(float distance, float radius)
        {
            return radius * radius / distance;
        }

        private float Abs(float p)
        {
            if (p < 0)
                p *= -1;
            return p;
        }

        private void SortCullInterpolate()
        {
            SortedObjects = new List<SceneObject>();
            VisibleAvatars = new List<RenderAvatar>();
            OccludedObjects = new List<SceneObject>();

            lock (Prims)
            {
                foreach (var obj in Prims.Values)
                {
                    obj.PositionCalculated = false;
                }

                // Calculate positions and rotations of root prims
                // Perform interpolation om objects that survive culling
                foreach (var obj in Prims.Values)
                {
                    if (obj.BasePrim.ParentID != 0) continue;
                    if (!obj.Initialized) obj.Initialize();

                    obj.Step(lastFrameTime);

                    PrimPosAndRot(obj, out obj.RenderPosition, out obj.RenderRotation);
                    obj.DistanceSquared = Vector3.DistanceSquared(Camera.RenderPosition, obj.RenderPosition);
                    obj.PositionCalculated = true;

                    if (!Frustum.ObjectInFrustum(obj.RenderPosition, obj.BoundingVolume)) continue;
                    if (LODFactor(obj.DistanceSquared, obj.BoundingVolume.ScaledR) < minLODFactor) continue;

                    if (!obj.Meshed)
                    {
                        if (!obj.Meshing && meshingsRequestedThisFrame < RenderSettings.MeshesPerFrame)
                        {
                            meshingsRequestedThisFrame++;
                            MeshPrim(obj);
                        }
                    }

                    if (obj.Faces == null) continue;

                    obj.Attached = false;
                    if (obj.Occluded())
                    {
                        OccludedObjects.Add(obj);
                    }
                    else
                    {
                        SortedObjects.Add(obj);
                    }
                }

                // Calculate avatar positions and perform interpolation tasks
                lock (Avatars)
                {
                    foreach (var obj in Avatars.Values)
                    {
                        if (!obj.Initialized) obj.Initialize();
                        if (RenderSettings.AvatarRenderingEnabled) obj.Step(lastFrameTime);
                        PrimPosAndRot(obj, out obj.RenderPosition, out obj.RenderRotation);
                        obj.DistanceSquared = Vector3.DistanceSquared(Camera.RenderPosition, obj.RenderPosition);
                        obj.PositionCalculated = true;

                        if (!Frustum.ObjectInFrustum(obj.RenderPosition, obj.BoundingVolume)) continue;

                        VisibleAvatars.Add(obj);
                        // SortedObjects.Add(obj);
                    }
                }

                // Calculate position and rotations of child objects
                foreach (var obj in Prims.Values.Where(obj => obj.BasePrim.ParentID != 0))
                {
                    if (!obj.Initialized) obj.Initialize();

                    obj.Step(lastFrameTime);

                    if (!obj.PositionCalculated)
                    {
                        PrimPosAndRot(obj, out obj.RenderPosition, out obj.RenderRotation);
                        obj.DistanceSquared = Vector3.DistanceSquared(Camera.RenderPosition, obj.RenderPosition);
                        obj.PositionCalculated = true;
                    }

                    if (!Frustum.ObjectInFrustum(obj.RenderPosition, obj.BoundingVolume)) continue;
                    if (LODFactor(obj.DistanceSquared, obj.BoundingVolume.ScaledR) < minLODFactor) continue;

                    if (!obj.Meshed)
                    {
                        if (!obj.Meshing && meshingsRequestedThisFrame < RenderSettings.MeshesPerFrame)
                        {
                            meshingsRequestedThisFrame++;
                            MeshPrim(obj);
                        }
                    }

                    if (obj.Faces == null) { continue; }

                    if (!obj.AttachedStateKnown)
                    {
                        obj.Attached = IsAttached(obj.BasePrim.ParentID);
                        obj.AttachedStateKnown = true;
                    }

                    if (obj.Occluded())
                    {
                        OccludedObjects.Add(obj);
                    }
                    else
                    {
                        SortedObjects.Add(obj);
                    }
                }
            }

            // RenderPrimitive class has IComparable implementation
            // that allows sorting by distance
            SortedObjects.Sort();
        }

        private void RenderBoundingBox(SceneObject prim)
        {
            var scale = prim.BasePrim.Scale;

            GL.PushMatrix();
            GL.MultMatrix(Math3D.CreateSRTMatrix(scale, prim.RenderRotation, prim.RenderPosition));

            if (RenderSettings.UseVBO && !occludedVBOFailed)
            {
                GL.DrawElements(PrimitiveType.Quads, RHelp.CubeIndices.Length, DrawElementsType.UnsignedShort, IntPtr.Zero);
            }
            else
            {
                GL.VertexPointer(3, VertexPointerType.Float, 0, RHelp.CubeVertices);
                GL.DrawElements(PrimitiveType.Quads, RHelp.CubeIndices.Length, DrawElementsType.UnsignedShort, RHelp.CubeIndices);
            }

            GL.PopMatrix();
        }

        private int boundingBoxVBO = -1;
        private int boundingBoxVIndexVBO = -1;
        private bool occludedVBOFailed = false;

        private void RenderOccludedObjects()
        {
            if (!RenderSettings.OcclusionCullingEnabled) return;

            GL.EnableClientState(ArrayCap.VertexArray);
            GL.ColorMask(false, false, false, false);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Lighting);

            if (RenderSettings.UseVBO && !occludedVBOFailed)
            {
                if (boundingBoxVBO == -1)
                {
                    Compat.GenBuffers(out boundingBoxVBO);
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, boundingBoxVBO);
                    Compat.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(sizeof(float) * RHelp.CubeVertices.Length), RHelp.CubeVertices, BufferUsageHint.StaticDraw);
                    if (Compat.BufferSize(BufferTarget.ArrayBuffer) != sizeof(float) * RHelp.CubeVertices.Length)
                    {
                        occludedVBOFailed = true;
                        Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                        boundingBoxVBO = -1;
                    }
                }
                else
                {
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, boundingBoxVBO);
                }

                if (boundingBoxVIndexVBO == -1)
                {
                    Compat.GenBuffers(out boundingBoxVIndexVBO);
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, boundingBoxVIndexVBO);
                    Compat.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(sizeof(ushort) * RHelp.CubeIndices.Length), RHelp.CubeIndices, BufferUsageHint.StaticDraw);
                    if (Compat.BufferSize(BufferTarget.ElementArrayBuffer) != sizeof(ushort) * RHelp.CubeIndices.Length)
                    {
                        occludedVBOFailed = true;
                        Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                        boundingBoxVIndexVBO = -1;
                    }
                }
                else
                {
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, boundingBoxVIndexVBO);
                }

                GL.VertexPointer(3, VertexPointerType.Float, 0, (IntPtr)0);
            }

            foreach (var obj in OccludedObjects.Where(obj => (obj.HasAlphaFaces || obj.HasSimpleFaces)))
            {
                obj.HasSimpleFaces = true;
                obj.HasAlphaFaces = false;
                obj.StartSimpleQuery();
                RenderBoundingBox(obj);
                obj.EndSimpleQuery();
            }

            if (RenderSettings.UseVBO)
            {
                Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            }

            GL.ColorMask(true, true, true, true);
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.Lighting);
        }

        private void RenderObjects(RenderPass pass)
        {
            if (!RenderSettings.PrimitiveRenderingEnabled) return;

            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.TextureCoordArray);
            GL.EnableClientState(ArrayCap.NormalArray);

            var myPos = Vector3.Zero;
            myPos = myself?.RenderPosition ?? Client.Self.SimPosition;

            if (pass == RenderPass.Invisible)
            {
                GL.ClearColor(1f, 1f, 1f, 1f);
                GL.Disable(EnableCap.Texture2D);
                GL.StencilFunc(StencilFunction.Always, 1, 1);
                GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
                GL.ColorMask(false, false, false, false);
                GL.StencilMask(0);
            }

            var nrPrims = SortedObjects.Count;
            for (var i = 0; i < nrPrims; i++)
            {
                //RenderBoundingBox(SortedPrims[i]);

                // When rendering alpha faces, draw from back towards the cameras
                // otherwise from those closest to camera, to the farthest
                var ix = pass == RenderPass.Alpha ? nrPrims - i - 1 : i;
                var obj = SortedObjects[ix];

                if (obj is RenderPrimitive)
                {
                    // Don't render objects that are outside the draw distance
                    if (FindClosestDistanceSquared(myPos, obj) > drawDistanceSquared) continue;
                    if (pass == RenderPass.Simple || pass == RenderPass.Alpha)
                    {
                        obj.StartQuery(pass);
                        obj.Render(pass, ix, this, lastFrameTime);
                        obj.EndQuery(pass);
                    }
                    else
                    {
                        obj.Render(pass, ix, this, lastFrameTime);
                    }
                }
            }

            if (pass == RenderPass.Invisible)
            {
                GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
                GL.ColorMask(true, true, true, true);
                GL.StencilFunc(StencilFunction.Notequal, 1, 1);
                GL.StencilMask(uint.MaxValue);
            }

            GL.Disable(EnableCap.Texture2D);
            GL.DisableClientState(ArrayCap.NormalArray);
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.TextureCoordArray);
        }

        private int texturesRequestedThisFrame;
        private int meshingsRequestedThisFrame;
        private int meshingsRequestedLastFrame;
        private int framesSinceReflection = 0;
        private float timeSinceReflection = 0f;

        private void Render(bool picking)
        {
            // If we have more than one active GL control on the screen, make this one active
            glControl.MakeCurrent();

            SortCullInterpolate();

            // Prepare GL frame: clear buffers and set up camera/modelview
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            // CRITICAL: ABSOLUTELY RESET ALL GL STATE AT FRAME START
            // This prevents ANY cross-frame pollution
            GL.LoadIdentity();
            
            // Reset ALL texture units to ensure no stale texture state
            for (int i = 0; i < 8; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.Disable(EnableCap.Texture2D);
            }
            GL.ActiveTexture(TextureUnit.Texture0); // Back to default unit
            
            // Explicitly disable everything that could interfere
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.ColorMaterial);
            GL.Disable(EnableCap.Blend);
            
            // Reset color to white
            GL.Color4(1f, 1f, 1f, 1f);
            
            // Reset to default shader (fixed-function)
            GL.UseProgram(0);
            
            // Update camera render positions and set view
            Camera.Step(lastFrameTime);
            Camera.LookAt();
            
            // CAPTURE 3D VIEW MATRICES HERE for name tag positioning
            // This must be done AFTER Camera.LookAt() but BEFORE any matrix modifications
            GL.GetFloat(GetPName.ModelviewMatrix, out ModelMatrix);
            GL.GetFloat(GetPName.ProjectionMatrix, out ProjectionMatrix);
            GL.GetInteger(GetPName.Viewport, Viewport);
            
            // Setup wireframe or solid fill drawing mode
            if (Wireframe && !picking)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }
            else
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
            
            // Push a matrix so Render can Pop at the end
            GL.PushMatrix();


            if (picking)
            {
                GL.Disable(EnableCap.Lighting);
                terrain.Render(RenderPass.Picking, 0, this, lastFrameTime);
                RenderAdjacentTerrains(RenderPass.Picking, lastFrameTime);
                RenderObjects(RenderPass.Picking);
                RenderAvatars(RenderPass.Picking);
                GLHUDBegin();
                RenderText(RenderPass.Picking);
                GLHUDEnd();
                GL.Enable(EnableCap.Lighting);
            }
            else
            {
                texturesRequestedThisFrame = 0;
                meshingsRequestedLastFrame = meshingsRequestedThisFrame;
                meshingsRequestedThisFrame = 0;

                CheckKeyboard(lastFrameTime);

                // CRITICAL: Guarantee clean state before sky rendering
                GL.Disable(EnableCap.Lighting);
                GL.Disable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.UseProgram(0); // Ensure no shader active
                
                // Render vibrant sky dome first, before anything else
                if (sky != null)
                {
                    sky.Render(RenderPass.Simple, Camera.RenderPosition);
                }
                
                // Re-enable lighting for objects that need it
                GL.Enable(EnableCap.Lighting);

                // Terrain rendering
                terrain.Render(RenderPass.Simple, 0, this, lastFrameTime);

                // Render adjacent simulator terrains
                UpdateAdjacentSimulators();
                RenderAdjacentTerrains(RenderPass.Simple, lastFrameTime);

                // Alpha mask elements, no blending, alpha test for A > 0.5
                GL.Enable(EnableCap.AlphaTest);

                RenderObjects(RenderPass.Simple);
                RenderAvatarsSkeleton(RenderPass.Simple);
                RenderObjects(RenderPass.Invisible);
                RenderAvatars(RenderPass.Simple);
                GL.Disable(EnableCap.AlphaTest);

                // Render water after opaque objects but before alpha blended objects
                RenderWater();

                // Ensure all shaders are stopped before HUD rendering (text, UI elements)
                if (RenderSettings.HasShaders && RenderSettings.EnableShiny)
                {
                    StopShiny();
                }

                GLHUDBegin();
                RenderText(RenderPass.Simple);
                RenderStats();
                chatOverlay.RenderChat(lastFrameTime, RenderPass.Simple);
                GLHUDEnd();
                GL.Disable(EnableCap.Blend);
            }

            // Pop the world matrix
            GL.PopMatrix();
        }

        public bool TryPick(int x, int y, out object picked, out int faceID)
        {
            Vector3 worldPos;
            return TryPick(x, y, out picked, out faceID, out worldPos);
        }

        public bool TryPick(int x, int y, out object picked, out int faceID, out Vector3 worldPos)
        {
            // Use PickingHelper to handle GL state
            byte[] color = PickingHelper.ExecutePicking(x, y, glControl.Height, () => Render(true));

            var depth = PickingHelper.ReadPixelDepth(x, y, glControl.Height);

            // Ensure we have up-to-date GL matrices and viewport before unprojecting
            try
            {
                GL.GetFloat(GetPName.ModelviewMatrix, out ModelMatrix);
                GL.GetFloat(GetPName.ProjectionMatrix, out ProjectionMatrix);
                GL.GetInteger(GetPName.Viewport, Viewport);
            }
            catch { }

            GLU.UnProject(x, glControl.Height - y, depth, ModelMatrix, ProjectionMatrix, Viewport, out var worldPosTK);
            worldPos = RHelp.OMVVector3(worldPosTK);

            int primID = Utils.BytesToUInt16(color, 0);
            faceID = color[2];

            picked = null;

            if (color[3] == 253) // Terrain
            {
                var vertexIndex = Utils.BytesToInt(new byte[] { color[0], color[1], color[2], 0 });
                if (terrain.TryGetVertex(vertexIndex, out var cv))
                {
                    picked = cv.Vertex.Position;
                    return true;
                }
            }
            else if (color[3] == 254) // Avatar
            {
                lock (VisibleAvatars)
                {
                    foreach (var avatar in VisibleAvatars.Where(
                                 avatar => avatar.data.Any(face => face != null && face.PickingID == primID)))
                    {
                        picked = avatar;
                    }
                }

                if (picked != null)
                {
                    return true;
                }
            }
            else if (color[3] == 255) // Prim
            {
                lock (SortedObjects)
                {
                    foreach (var prim in from obj 
                                 in SortedObjects.OfType<RenderPrimitive>() let prim = obj 
                             where obj.BasePrim.LocalID != 0 select prim)
                    {
                        if (prim.Faces.Where(face => face.UserData != null).Any(
                                face => ((FaceData)face.UserData).PickingID == primID))
                        {
                            picked = prim;
                        }

                        if (picked != null) { break; }
                    }
                }
            }

            return picked != null;
        }

        private void CalculateBoundingBox(RenderPrimitive rprim)
        {
            var prim = rprim.BasePrim;

            // Calculate bounding volumes for each prim and adjust textures
            rprim.BoundingVolume = new BoundingVolume();
            for (var j = 0; j < rprim.Faces.Count; j++)
            {
                var teFace = prim.Textures.GetFace((uint)j);
                if (teFace == null) continue;

                var face = rprim.Faces[j];
                var data = new FaceData();

                data.BoundingVolume.CreateBoundingVolume(face, prim.Scale);
                rprim.BoundingVolume.AddVolume(data.BoundingVolume, prim.Scale);

                // With linear texture animation in effect, texture repeats and offset are ignored
                // prim.TextureAnim.Face == 255 checks for all faces
                if ((prim.TextureAnim.Flags & Primitive.TextureAnimMode.ANIM_ON) != 0
                    && (prim.TextureAnim.Flags & Primitive.TextureAnimMode.ROTATE) == 0
                    && (prim.TextureAnim.Face == 255 || prim.TextureAnim.Face == j))
                {
                    teFace.RepeatU = 1;
                    teFace.RepeatV = 1;
                    teFace.OffsetU = 0;
                    teFace.OffsetV = 0;
                }

                // Sculpt UV vertically flipped compared to prims. Flip back
                if (prim.Sculpt != null && prim.Sculpt.SculptTexture != UUID.Zero && prim.Sculpt.Type != SculptType.Mesh)
                {
                    teFace = (Primitive.TextureEntryFace)teFace.Clone();
                    teFace.RepeatV *= -1;
                }

                // Texture transform for this face
                renderer.TransformTexCoords(face.Vertices, face.Center, teFace, prim.Scale);

                // Set the UserData for this face to our FaceData struct
                face.UserData = data;
                rprim.Faces[j] = face;
            }
        }

        private void MeshPrim(RenderPrimitive rprim)
        {
            if (rprim.Meshing) return;

            rprim.Meshing = true;
            var prim = rprim.BasePrim;

            // Regular prim
            if (prim.Sculpt == null || prim.Sculpt.SculptTexture == UUID.Zero)
            {
                var detailLevel = RenderSettings.PrimRenderDetail;
                if (RenderSettings.AllowQuickAndDirtyMeshing)
                {
                    if (prim.Flexible == null && prim.Type == PrimType.Box &&
                        prim.PrimData.ProfileHollow == 0 &&
                        prim.PrimData.PathTwist == 0 &&
                        prim.PrimData.PathTaperX == 0 &&
                        prim.PrimData.PathTaperY == 0 &&
                        prim.PrimData.PathSkew == 0 &&
                        prim.PrimData.PathShearX == 0 &&
                        prim.PrimData.PathShearY == 0 &&
                        prim.PrimData.PathRevolutions == 1.0f &&
                        prim.PrimData.PathRadiusOffset == 0)
                        detailLevel = DetailLevel.Low;// a box or something else that can use lower meshing
                }
                var mesh = renderer.GenerateFacetedMesh(prim, detailLevel);
                // Remove any degenerate faces with no verts or no indices
                if (mesh?.Faces != null)
                {
                    mesh.Faces = mesh.Faces.FindAll(f => f.Vertices != null && f.Vertices.Count > 0 && f.Indices != null && f.Indices.Count > 0);
                }
                rprim.Faces = mesh.Faces;
                CalculateBoundingBox(rprim);
                rprim.Meshing = false;
                rprim.Meshed = true;
            }
            else
            {
                PendingTasks.Enqueue(GenerateSculptOrMeshPrim(rprim, prim));
                PendingTasksAvailable.Release();
            }
        }

        private GenericTask GenerateSculptOrMeshPrim(RenderPrimitive rprim, Primitive prim)
        {
            return () =>
            {
                FacetedMesh mesh = null;

                try
                {
                    if (prim.Sculpt.Type != SculptType.Mesh)
                    { // Regular sculpty
                        SKBitmap img = null;

                        lock (sculptCache)
                        {
                            if (sculptCache.TryGetValue(prim.Sculpt.SculptTexture, out var value))
                            {
                                img = value;
                            }
                        }

                        if (img == null)
                        {
                            if (LoadTexture(prim.Sculpt.SculptTexture, ref img))
                            {
                                sculptCache[prim.Sculpt.SculptTexture] = img;
                            }
                            else
                            {
                                return;
                            }
                        }

                        mesh = renderer.GenerateFacetedSculptMesh(prim, img, RenderSettings.SculptRenderDetail);
                    }
                    else
                    { // Mesh
                        var gotMesh = new AutoResetEvent(false);

                        Client.Assets.RequestMesh(prim.Sculpt.SculptTexture, (success, meshAsset) =>
                        {
                            if (!success || !FacetedMesh.TryDecodeFromAsset(prim, meshAsset, RenderSettings.MeshRenderDetail, out mesh))
                            {
                                Logger.Warn("Failed to fetch or decode the mesh asset", Client);
                            }
                            gotMesh.Set();
                        });

                        gotMesh.WaitOne(20 * 1000, false);
                    }
                }
                catch
                { }

                if (mesh != null)
                {
                    // Remove degenerate faces produced by mesh generation
                    if (mesh.Faces != null)
                    {
                        mesh.Faces = mesh.Faces.FindAll(f => f.Vertices != null && f.Vertices.Count > 0 && f.Indices != null && f.Indices.Count > 0);
                    }
                    rprim.Faces = mesh.Faces;
                    CalculateBoundingBox(rprim);
                    rprim.Meshing = false;
                    rprim.Meshed = true;
                }
                else
                {
                    lock (Prims)
                    {
                        Prims.Remove(rprim.BasePrim.LocalID);
                    }
                }
            };
        }

        private void UpdatePrimBlocking(Primitive prim)
        {
            if (!RenderingEnabled) return;

            switch (prim.PrimData.PCode)
            {
                case PCode.Avatar:
                    AddAvatarToScene(Client.Network.CurrentSim.ObjectsAvatars[prim.LocalID]);
                    return;
                case PCode.Prim:
                    if (!RenderSettings.PrimitiveRenderingEnabled) return;

                    if (prim.Textures == null) return;

                    if (Prims.TryGetValue(prim.LocalID, out var rPrim))
                    {
                        rPrim.AttachedStateKnown = false;
                    }
                    else
                    {
                        rPrim = new RenderPrimitive
                        {
                            Meshed = false,
                            BoundingVolume = new BoundingVolume()
                        };
                        rPrim.BoundingVolume.FromScale(prim.Scale);
                    }

                    rPrim.BasePrim = prim;
                    lock (Prims) Prims[prim.LocalID] = rPrim;
                    break;
                case PCode.ParticleSystem:
                // todo
                default:
                    // unimplemented foliage
                    break;
            }
        }
        
        #region Adjacent Simulator Rendering

        /// <summary>
        /// Calculate the offset of a simulator relative to the current simulator based on region handles
        /// </summary>
        /// <param name="simHandle">Handle of the simulator to calculate offset for</param>
        /// <returns>Offset vector in meters</returns>
        private Vector3 CalculateSimOffset(ulong simHandle)
        {
            if (Client.Network.CurrentSim == null)
                return Vector3.Zero;

            ulong currentHandle = Client.Network.CurrentSim.Handle;
            
            // Extract region coordinates from handles (these are in 256m grid units)
            uint currentX, currentY, simX, simY;
            Utils.LongToUInts(currentHandle, out currentX, out currentY);
            Utils.LongToUInts(simHandle, out simX, out simY);

            // Calculate offset in meters (each grid unit is 256m)
            // The coordinates are already in meters, so we just need the difference
            float offsetX = (float)(simX - currentX);
            float offsetY = (float)(simY - currentY);

            return new Vector3(offsetX, offsetY, 0);
        }

        /// <summary>
        /// Update the list of adjacent simulators and their terrains
        /// </summary>
        private void UpdateAdjacentSimulators()
        {
            if (Client.Network.CurrentSim == null || !RenderSettings.PrimitiveRenderingEnabled)
                return;

            lock (adjacentSimsLock)
            {
                // Get all connected simulators except the current one
                var simulators = Client.Network.Simulators.Where(s => s.Handle != Client.Network.CurrentSim.Handle).ToList();
                
                // Remove terrains for sims we're no longer connected to
                var handlesToRemove = adjacentTerrains.Keys.Where(h => !simulators.Any(s => s.Handle == h)).ToList();
                foreach (var handle in handlesToRemove)
                {
                    if (adjacentTerrains.TryGetValue(handle, out var terrain))
                    {
                        terrain.Dispose();
                        adjacentTerrains.Remove(handle);
                    }
                    adjacentSimulators.Remove(handle);
                }

                // Add or update terrains for connected sims
                foreach (var sim in simulators)
                {
                    if (!adjacentTerrains.ContainsKey(sim.Handle))
                    {
                        // Create a new terrain renderer for this adjacent sim
                        adjacentTerrains[sim.Handle] = new RenderAdjacentTerrain(Instance, sim) { Modified = true };
                        adjacentSimulators[sim.Handle] = sim;
                    }
                }
            }
        }

        /// <summary>
        /// Render terrain from adjacent simulators with appropriate offset
        /// </summary>
        private void RenderAdjacentTerrains(RenderPass pass, float time)
        {
            if (Client.Network.CurrentSim == null)
                return;

            lock (adjacentSimsLock)
            {
                foreach (var kvp in adjacentTerrains)
                {
                    var simHandle = kvp.Key;
                    var terrain = kvp.Value;

                    // Calculate offset for this sim
                    Vector3 offset = CalculateSimOffset(simHandle);

                    // Push matrix and translate to sim position
                    GL.PushMatrix();
                    GL.Translate(offset.X, offset.Y, offset.Z);

                    try
                    {
                        terrain.Render(pass, 0, this, time);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error rendering adjacent terrain for handle {simHandle}: {ex.Message}", ex, Client);
                    }

                    GL.PopMatrix();
                }
            }
        }

        #endregion Adjacent Simulator Rendering
        #endregion Private methods (the meat)
    }
}
