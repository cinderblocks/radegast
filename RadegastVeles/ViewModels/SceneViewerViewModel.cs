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
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.Rendering;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// ViewModel for the in-world 3-D scene viewer tab.
/// <para>
/// The scene viewer is created lazily the first time the user opens the tab and is
/// disposable so the GL viewport, shaders, FBOs, and per-frame rendering loop can
/// be torn down completely when the user closes the tab (saving CPU/GPU/RAM).
/// </para>
/// <para>
/// Slice 1 only hosts an empty <see cref="GlViewportControl"/>; terrain, water,
/// prim streaming, avatars, and movement are added in later slices.
/// </para>
/// </summary>
public partial class SceneViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GlViewportControl? _viewport;
    private bool _disposed;

    private SceneTerrainBuilder? _terrainBuilder;
    private CancellationTokenSource? _terrainCts;
    private readonly object _terrainCtsLock = new();
    private SceneObjectStreamer?             _objectStreamer;
    private SceneAvatarStreamer?             _avatarStreamer;
    private SceneParticleStreamer?           _particleStreamer;
    private SceneFlexiStreamer?              _flexiStreamer;
    private SceneAvatarAnimationStreamer?    _avatarAnimStreamer;
    private SceneNameTagService?             _nameTagService;
    private SceneBuildScheduler?             _buildScheduler;

    // Periodically pushes the GL camera position/orientation into
    // Self.Movement.Camera so the server receives the correct view frustum in
    // AgentUpdate packets and can prioritise the right objects in its interest list.
    private Timer? _cameraSyncTimer;

    /// <summary>Live name-tag positions for the canvas overlay.</summary>
    public ObservableCollection<NameTagItem>   NameTags  { get; } = new();
    /// <summary>Live prim hover-text positions for the canvas overlay.</summary>
    public ObservableCollection<HoverTextItem> HoverTags { get; } = new();
    [ObservableProperty] private bool _showNameTags = true;

    [ObservableProperty] private string _statusText = "Scene viewer ready.";
    [ObservableProperty] private bool   _wireframe;
    [ObservableProperty] private bool   _ssaoEnabled;
    [ObservableProperty] private bool   _showPerfOverlay;
    [ObservableProperty] private bool   _showChatOverlay;
    [ObservableProperty] private bool   _frustumCullingEnabled = true;
    [ObservableProperty] private string _perfOverlayText = string.Empty;

    private const int ChatOverlayMaxLines = 10;
    /// <summary>Bounded list of the most recent nearby chat messages shown in the scene overlay.</summary>
    public ObservableCollection<ChatLine> ChatOverlayLines { get; } = new();
    [ObservableProperty] private bool   _isFlying;
    [ObservableProperty] private bool   _isRunning;
    /// <summary>Draw / stream distance in metres (16–512). Default matches SceneObjectStreamer.</summary>
    [ObservableProperty] private float  _drawDistance = 96f;
    /// <summary>LocalID of the prim or avatar the user last clicked, 0 when nothing selected.</summary>
    [ObservableProperty] private uint   _selectedLocalId;
    /// <summary>Face index of the last prim click, used to refresh <see cref="SelectedInfo"/> on property arrival.</summary>
    private int _selectedFaceIndex;
    /// <summary>Display label for the selected object (name + UUID), empty when nothing selected.</summary>
    [ObservableProperty] private string _selectedInfo = string.Empty;

    // ── Context-menu state ────────────────────────────────────────────────────────
    /// <summary>True when the context-menu target is an avatar (affects visible menu items).</summary>
    [ObservableProperty] private bool _contextIsAvatar;
    /// <summary>True when the context-menu target is a prim (affects visible menu items).</summary>
    [ObservableProperty] private bool _contextIsPrim;
    /// <summary>True when we are currently sitting (enables Stand Up menu item).</summary>
    [ObservableProperty] private bool _contextIsSitting;
    /// <summary>Header text shown at the top of the context menu (object/avatar name).</summary>
    [ObservableProperty] private string _contextLabel = string.Empty;

    // ── Movement key state ────────────────────────────────────────────────────────
    // Each entry is a reference count (>0 means key is held).
    private int _fwdHeld, _backHeld, _leftHeld, _rightHeld, _upHeld, _downHeld;
    private int _turnLeftHeld, _turnRightHeld;
    private Timer? _moveTimer;

    // Last avatar heading (degrees) seen by FollowAvatar, used to detect turns
    // so the third-person camera yaws to stay behind the avatar.
    private float _lastKnownHeadingDeg = float.NaN;

    /// <summary>Raised when the user clicks the tab's close (✕) button.</summary>
    public event EventHandler? CloseRequested;

    public SceneViewerViewModel(RadegastInstanceAvalonia instance)
    {
        _instance    = instance;
        _ssaoEnabled = instance.GlobalSettings["ssao_enabled"].Type != OpenMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["ssao_enabled"].AsBoolean() : true;
        _frustumCullingEnabled = instance.GlobalSettings["frustum_culling_enabled"].Type != OpenMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["frustum_culling_enabled"].AsBoolean() : true;
        _drawDistance = instance.GlobalSettings["scene_draw_distance"].Type != OpenMetaverse.StructuredData.OSDType.Unknown
            ? (float)instance.GlobalSettings["scene_draw_distance"].AsReal() : 96f;
    }

    partial void OnWireframeChanged(bool value)
    {
        if (_viewport != null) _viewport.Wireframe = value;
    }

    partial void OnSsaoEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.SsaoEnabled = value;
    }

    partial void OnFrustumCullingEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.FrustumCullingEnabled = value;
    }

    partial void OnShowPerfOverlayChanged(bool value)
    {
        if (!value) PerfOverlayText = string.Empty;
    }

    partial void OnDrawDistanceChanged(float value)
    {
        if (_objectStreamer != null) _objectStreamer.DrawDistance = value;
        if (_avatarStreamer != null) _avatarStreamer.DrawDistance = value;

        // Re-dirty all currently rendered objects so they rebuild at the
        // correct LOD for the new distance. The streamers' own debounce
        // will coalesce the burst into one tessellation pass per object.
        _objectStreamer?.DirtyAllRendered();
        _avatarStreamer?.DirtyAllRendered();
    }

    /// <summary>
    /// Attach the GL viewport so the VM can configure it and submit
    /// geometry to it. Called from the view's code-behind after the visual tree is ready.
    /// </summary>
    public void SetViewport(GlViewportControl viewport)
    {
        _viewport                       = viewport;
        _viewport.Wireframe              = Wireframe;
        _viewport.SsaoEnabled            = SsaoEnabled;
        _viewport.FrustumCullingEnabled  = FrustumCullingEnabled;
        _viewport.Stats.FrameCompleted  += OnFrameCompleted;
        _viewport.InitFailed += msg =>
        {
            StatusText = $"GL init failed: {msg}";
        };
        _viewport.SceneReset            += OnSceneReset;
        _viewport.FaceClicked           += OnFaceClicked;

        // Subscribe to land patch and sim-change events.
        _instance.Client.Terrain.LandPatchReceived        += OnLandPatchReceived;
        _instance.Client.Network.SimChanged               += OnSimChanged;
        _instance.Client.Network.SimConnected             += OnSimConnected;
        _instance.Client.Objects.ObjectUpdate             += OnObjectUpdate;
        _instance.Client.Objects.KillObject               += OnKillObject;
        _instance.Client.Objects.AvatarUpdate             += OnAvatarUpdate;
        _instance.Client.Objects.TerseObjectUpdate        += OnTerseObjectUpdate;
        _instance.Client.Objects.AvatarSitChanged         += OnAvatarSitChanged;
        _instance.Client.Appearance.AppearanceSet         += OnAppearanceSet;
        _instance.Client.Objects.ObjectProperties         += OnObjectProperties;
        _instance.Client.Avatars.AvatarAppearance         += OnAvatarAppearance;
        _instance.NetCom.ChatReceived                     += OnChatReceived;

        // 4 concurrent build slots: avatars use AvatarMultiplier=8 so they always drain
        // before prims at similar distances, but 4 slots lets near-distance prims and
        // avatars overlap without starving the render thread.
        _buildScheduler      = new SceneBuildScheduler(maxConcurrent: 4);
        _objectStreamer       = new SceneObjectStreamer(_instance.Client, viewport, _buildScheduler);
        _avatarStreamer       = new SceneAvatarStreamer(_instance.Client, viewport, _buildScheduler);
        _particleStreamer    = new SceneParticleStreamer(_instance.Client, viewport);
        _flexiStreamer       = new SceneFlexiStreamer(_instance.Client, viewport, _objectStreamer);
        _flexiStreamer.SetAvatarStreamer(_avatarStreamer);
        _avatarAnimStreamer  = new SceneAvatarAnimationStreamer(_instance.Client, viewport, _avatarStreamer);
        _flexiStreamer.SetAnimationStreamer(_avatarAnimStreamer);
        _avatarStreamer.SetAnimationStreamer(_avatarAnimStreamer);

        // Apply current draw distance to freshly created streamers.
        _objectStreamer.DrawDistance = DrawDistance;
        _avatarStreamer.DrawDistance = DrawDistance;

        // Name-tag overlay service.
        _nameTagService = new SceneNameTagService(_instance.Client, viewport);
        _nameTagService.TagsUpdated      += OnNameTagsUpdated;
        _nameTagService.HoverTagsUpdated += OnHoverTagsUpdated;
        _nameTagService.Start();

        // Sync the GL camera into Self.Movement.Camera at ~10 Hz so the server's
        // interest list receives the correct view frustum via AgentUpdate.
        _cameraSyncTimer = new Timer(_ => SyncCameraToServer(), null,
            TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1));

        // Build terrain immediately if we're already connected.
        if (_instance.Client.Network.Connected)
        {
            SyncMovementState();
            _ = RefreshTerrainAsync(centerCamera: true);

            // NOTE: SeedStreamersFromCurrentSim() is intentionally NOT called here.
            // GlInit fires SceneReset shortly after SetViewport, which calls
            // SeedStreamersFromCurrentSim() once GL is ready.  Seeding here AND
            // in OnSceneReset would double-seed every object, overflowing the
            // build scheduler queue and causing far objects to be permanently lost.
        }
    }

    /// <summary>
    /// Enqueues every prim and avatar that is already resident in the current
    /// simulator into the freshly created streamers.  Called once at the end of
    /// <see cref="SetViewport"/> when we are already connected.
    /// <para>
    /// Only root prims (ParentID == 0) are fed directly — each streamer collects
    /// the full linkset itself via CollectLinkset.  Feeding child prims too would
    /// dirty the same root thousands of extra times and cause a burst of redundant
    /// build tasks and placeholder GL submissions.
    /// </para>
    /// </summary>
    private void SeedStreamersFromCurrentSim()
    {
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null) return;

        // Seed only root prims to avoid dirtying the same linkset once per child.
        foreach (var prim in sim.ObjectsPrimitives.Values)
        {
            if (prim.ParentID != 0) continue;
            _objectStreamer?.OnObjectUpdate(sim, prim, isAttachment: false);
        }

        // Seed avatars.
        foreach (var avatar in sim.ObjectsAvatars.Values)
        {
            _avatarStreamer?.OnAvatarUpdate(sim, avatar);
        }

        // Seed particle streamer with all root prims that have emitters.
        _particleStreamer?.SeedFromCurrentSim();
    }

    /// <summary>Sync fly/run toggle state from the current AgentMovement flags.</summary>
    private void SyncMovementState()
    {
        if (!_instance.Client.Network.Connected) return;
        var mv = _instance.Client.Self.Movement;
        IsFlying  = mv.Fly;
        IsRunning = mv.AlwaysRun;
    }

    private void OnObjectUpdate(object? sender, PrimEventArgs e)
    {
        if (_objectStreamer == null || _disposed) return;
        _objectStreamer.OnObjectUpdate(e.Simulator, e.Prim, e.IsAttachment);
        _particleStreamer?.OnObjectUpdate(e.Simulator, e.Prim, e.IsAttachment);
    }

    private void OnAvatarUpdate(object? sender, AvatarUpdateEventArgs e)
    {
        if (_avatarStreamer == null || _disposed) return;
        // For already-rendered avatars use the terse (transform-only) path unless this
        // is the first time we've seen them (not yet rendered). Full rebuilds that include
        // wearable asset download and texture fetch are only triggered by appearance events.
        _avatarStreamer.OnTerseAvatarUpdate(e.Simulator, e.Avatar);

        // Soft-follow: only slide the camera target, preserve user's orbit angle and zoom.
        if (e.Avatar.LocalID == _instance.Client.Self.LocalID)
            FollowAvatar();
    }

    private void OnAvatarSitChanged(object? sender, AvatarSitChangedEventArgs e)
    {
        if (_avatarStreamer == null || _disposed) return;
        // Dirty the avatar so it rebuilds at the correct sit/stand offset.
        _avatarStreamer.OnAvatarUpdate(e.Simulator, e.Avatar);
    }

    private void OnAppearanceSet(object? sender, AppearanceSetEventArgs e)
    {
        if (_avatarStreamer == null || _disposed || !e.Success) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null) return;
        // Rebuild our own avatar mesh with the updated wearables/visual params.
        if (sim.ObjectsAvatars.TryGetValue(_instance.Client.Self.LocalID, out var self))
            _avatarStreamer.OnAvatarUpdate(sim, self);
    }

    private void OnAvatarAppearance(object? sender, AvatarAppearanceEventArgs e)
    {
        if (_avatarStreamer == null || _disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || e.Simulator != sim) return;
        // AvatarAppearance carries the real VisualParams and baked texture UUIDs.
        // Look up the avatar by UUID and trigger a mesh rebuild so morphs and
        // textures are applied — this is what AvatarViewerViewModel does via
        // ScheduleReload(), but here we go directly through the streamer.
        Avatar? av = null;
        foreach (var a in sim.ObjectsAvatars.Values)
            if (a?.ID == e.AvatarID) { av = a; break; }
        if (av != null)
            _avatarStreamer.OnAvatarUpdate(sim, av);
    }

    private void OnObjectProperties(object? sender, ObjectPropertiesEventArgs e)
    {
        if (_disposed) return;
        // Only update labels if this is the currently selected object.
        if (SelectedLocalId == 0) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || e.Simulator != sim) return;
        if (!sim.ObjectsPrimitives.TryGetValue(SelectedLocalId, out var selected)) return;
        if (selected.ID != e.Properties.ObjectID) return;

        var name = e.Properties.Name;
        if (string.IsNullOrEmpty(name)) return;

        Dispatcher.UIThread.Post(() =>
        {
            SelectedInfo = $"{name}  [{selected.ID}]  face {_selectedFaceIndex}";
            ContextLabel = name;
            StatusText   = $"Selected: {SelectedInfo}";
        });
    }

    private void OnTerseObjectUpdate(object? sender, TerseObjectUpdateEventArgs e)
    {
        if (_disposed) return;
        // Soft-follow and status bar on terse position updates.
        if (e.Prim.LocalID == _instance.Client.Self.LocalID)
        {
            FollowAvatar();
            UpdateStatusBar();
        }

        // Fast-path avatar position + rotation without a full mesh rebuild.
        if (e.Prim is Avatar av)
        {
            _avatarStreamer?.OnTerseAvatarUpdate(e.Simulator, av);
        }
        else
        {
            // Fast-path prim position (translation only) without a full mesh rebuild.
            _objectStreamer?.OnTerseObjectUpdate(e.Simulator, e.Prim);
            _particleStreamer?.OnTerseObjectUpdate(e.Simulator, e.Prim);

            // If this prim is a vehicle/seat root, re-push world transforms for any
            // avatars currently seated on it so they move with the vehicle.
            var rootId = e.Prim.ParentID == 0 ? e.Prim.LocalID : e.Prim.ParentID;
            _avatarStreamer?.OnSeatPrimMoved(e.Simulator, rootId);
        }
    }

    private void OnKillObject(object? sender, KillObjectEventArgs e)
    {
        if (_disposed) return;
        _objectStreamer?.OnKillObject(e.Simulator, e.ObjectLocalID);
        _avatarStreamer?.OnKillAvatar(e.Simulator, e.ObjectLocalID);
        _particleStreamer?.OnKillObject(e.Simulator, e.ObjectLocalID);
        _flexiStreamer?.OnKillObject(e.Simulator, e.ObjectLocalID);
        _avatarAnimStreamer?.OnKillAvatar(e.Simulator, e.ObjectLocalID);
    }

    private void OnLandPatchReceived(object? sender, LandPatchReceivedEventArgs e)
    {
        if (_disposed) return;
        if (e.Simulator != _instance.Client.Network.CurrentSim) return;
        _ = RefreshTerrainAsync(centerCamera: false);
    }

    private void OnSimChanged(object? sender, SimChangedEventArgs e)
    {
        if (_disposed) return;
        // Fired when the agent crosses into a new region — clear objects, full rebuild + re-center.
        // Also flush the decoded-bitmap cache: textures from the old sim are unlikely to be reused
        // and releasing them now reclaims the RAM before the new sim loads its own textures.
        GridTextureHelper.ClearSkBitmapCache();

        Dispatcher.UIThread.Post(() =>
        {
            _objectStreamer?.Clear();
            _avatarStreamer?.Clear();
            _particleStreamer?.Clear();
            _flexiStreamer?.Clear();
            _avatarAnimStreamer?.Clear();
            NameTags.Clear();
            SelectedLocalId = 0;
            SelectedInfo    = string.Empty;
            UpdateStatusBar();
            _ = RefreshTerrainAsync(centerCamera: true);

            // Re-seed from whatever is already present in the new sim's object cache.
            // Without this, avatars and objects that arrived before SimChanged fired
            // (or before network events resume) would never appear.
            SeedStreamersFromCurrentSim();
        });
    }

    private void OnSimConnected(object? sender, SimConnectedEventArgs e)
    {
        if (_disposed) return;
        if (e.Simulator != _instance.Client.Network.CurrentSim) return;
        SyncMovementState();
        UpdateStatusBar();
        _ = RefreshTerrainAsync(centerCamera: true);
        // Seed any objects/avatars already cached in the newly connected sim.
        SeedStreamersFromCurrentSim();
    }

    private async Task RefreshTerrainAsync(bool centerCamera = false)
    {
        if (_viewport == null || _disposed) return;

        // Cancel any in-flight build — lock so Dispose() can't race us.
        CancellationToken ct;
        lock (_terrainCtsLock)
        {
            if (_disposed) return;
            _terrainCts?.Cancel();
            _terrainCts?.Dispose();
            _terrainCts = new CancellationTokenSource();
            ct = _terrainCts.Token;
        }

        _terrainBuilder ??= new SceneTerrainBuilder(_instance.Client);

        StatusText = "Building terrain…";
        try
        {
            var submission = await _terrainBuilder.RebuildAsync(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || submission == null) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _viewport?.Submit(submission);
                CenterCameraOnAvatar();
                UpdateStatusBar();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Terrain error: {ex.Message}";
        }
    }

    // ── Object picking ────────────────────────────────────────────────────────────

    // The avatar scene-key offset used by SceneAvatarStreamer — picks with a localId
    // at or above this value are avatar hits; subtract to recover the real localId.
    private const uint AvatarKeyOffset = 0x8000_0000u;

    /// <summary>
    /// Called on the UI thread when the user clicks a face in the viewport.
    /// Updates selection state and, for prims, fires an ObjectClick touch packet.
    /// </summary>
    private void OnFaceClicked(uint sceneLocalId, int faceIndex, Radegast.Veles.Rendering.FaceHitInfo hit)
    {
        if (_disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null) return;

        bool isAvatar = sceneLocalId >= AvatarKeyOffset;
        uint realId   = isAvatar ? sceneLocalId - AvatarKeyOffset : sceneLocalId;

        SelectedLocalId    = realId;
        _selectedFaceIndex = faceIndex;

        if (isAvatar)
        {
            // Avatar hit — show name if available.
            if (sim.ObjectsAvatars.TryGetValue(realId, out var av))
            {
                SelectedInfo  = $"{av.Name}";
                StatusText    = $"Avatar: {av.Name}";
                ContextLabel  = av.Name;
            }
            else
            {
                SelectedInfo  = $"Avatar #{realId}";
                StatusText    = SelectedInfo;
                ContextLabel  = $"Avatar #{realId}";
            }
            ContextIsAvatar  = true;
            ContextIsPrim    = false;
            ContextIsSitting = _instance.Client.Self.SittingOn != 0;
        }
        else
        {
            // Prim hit — show name/UUID and send a touch.
            string primName;
            if (sim.ObjectsPrimitives.TryGetValue(realId, out var prim))
            {
                primName     = prim.Properties?.Name ?? "(unnamed)";
                SelectedInfo = $"{primName}  [{prim.ID}]  face {faceIndex}";
                ContextLabel = primName;
            }
            else
            {
                primName     = $"Prim #{realId}";
                SelectedInfo = primName;
                ContextLabel = primName;
            }
            StatusText       = $"Touched: {SelectedInfo}";
            ContextIsAvatar  = false;
            ContextIsPrim    = true;
            ContextIsSitting = _instance.Client.Self.SittingOn != 0;

            // Request full properties so the name is populated for the context label.
            if (prim?.Properties == null)
                _instance.Client.Objects.SelectObject(sim, realId);

            // Fire the grab/degrab touch
            // Convert OpenTK vectors to LibreMetaverse vectors for the packet.
            var uvCoord  = new OpenMetaverse.Vector3(hit.UvCoord.X,  hit.UvCoord.Y,  hit.UvCoord.Z);
            var stCoord  = new OpenMetaverse.Vector3(hit.StCoord.X,  hit.StCoord.Y,  hit.StCoord.Z);
            var position = new OpenMetaverse.Vector3(hit.Position.X, hit.Position.Y, hit.Position.Z);
            var normal   = new OpenMetaverse.Vector3(hit.Normal.X,   hit.Normal.Y,   hit.Normal.Z);
            var binormal = new OpenMetaverse.Vector3(hit.Binormal.X, hit.Binormal.Y, hit.Binormal.Z);
            _ = _instance.Client.Objects.ClickObjectAsync(
                    sim, realId, uvCoord, stCoord, faceIndex, position, normal, binormal);
        }
    }

    /// <summary>
    /// Composes a "Region (x, y, z) | Fly | Run" status string and posts it to
    /// <see cref="StatusText"/> on the UI thread.
    /// Safe to call from any thread.
    /// </summary>
    private void UpdateStatusBar()
    {
        if (_disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null) return;

        var pos = _instance.Client.Self.SimPosition;

        // Decode global region origin from the 64-bit handle (high dword = global X, low dword = global Y).
        // Divide by 256 to get the region grid column/row; the remainder is unused here.
        // We just display the sim-local integer coordinates (0-255).
        int lx = (int)MathF.Round(pos.X);
        int ly = (int)MathF.Round(pos.Y);
        int lz = (int)MathF.Round(pos.Z);

        string flyRun = IsFlying  ? " | Flying"
                      : IsRunning ? " | Running"
                      : string.Empty;

        string text = $"{sim.Name} ({lx}, {ly}, {lz}){flyRun}";

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) StatusText = text;
        });
    }

    /// <summary>
    /// Points the orbit camera behind and above the avatar at a comfortable
    /// follow distance, matching Second Life's default third-person camera.
    /// If no position is available the camera is left pointing at the region centre.
    /// </summary>
    private void CenterCameraOnAvatar()
    {
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || _viewport == null) return;

        var pos = _instance.Client.Self.SimPosition;

        // SimPosition is (0,0,0) before the first update — fall back to region centre.
        var target = (pos.X == 0f && pos.Y == 0f && pos.Z == 0f)
            ? new OpenTK.Mathematics.Vector3(128f, 128f, sim.WaterHeight + 2f)
            : new OpenTK.Mathematics.Vector3(pos.X, pos.Y, pos.Z + 1.0f); // eye-level target

        // Derive the camera yaw from the avatar's current heading so it starts
        // directly behind the avatar (heading + 180°), matching SL's follow-camera.
        // Avatar faces +X at rest; SimRotation is a Z-up quaternion.
        var rot = _instance.Client.Self.SimRotation;
        float headingRad = MathF.Atan2(
            2f * (rot.W * rot.Z + rot.X * rot.Y),
            1f - 2f * (rot.Y * rot.Y + rot.Z * rot.Z));
        float cameraYaw = OpenTK.Mathematics.MathHelper.RadiansToDegrees(headingRad) + 180f;

        // ~3.5 m distance, 15° pitch — matches SL's default third-person camera.
        // Reset the tracked heading so FollowAvatar doesn't apply a stale delta
        // after a teleport or manual camera reset.
        _lastKnownHeadingDeg = float.NaN;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _viewport.Camera.Target   = target;
            _viewport.Camera.Distance = 3.5f;
            _viewport.Camera.Yaw      = cameraYaw;
            _viewport.Camera.Pitch    = 15f;
            _viewport.RequestRender();
        });
    }

    /// <summary>
    /// Pushes the current GL camera position and orientation into
    /// <c>Self.Movement.Camera</c> so the next AgentUpdate packet reports
    /// the correct view frustum to the server.  The server uses this to
    /// prioritise which objects it sends updates for (server-side interest list).
    /// Safe to call from any thread; reads Camera3D without a lock (UI-thread
    /// writes are accepted here since camera values are primitive floats).
    /// </summary>
    private void SyncCameraToServer()
    {
        if (_disposed || _viewport == null) return;
        if (!_instance.Client.Network.Connected) return;

        var cam = _viewport.Camera;
        var eye = cam.EyePosition;               // OpenTK Vector3
        var fwd = cam.ForwardDirection;           // unit vector toward target

        // Convert to LibreMetaverse/SL coordinate types.
        var pos   = new OpenMetaverse.Vector3(eye.X, eye.Y, eye.Z);
        var atDir = new OpenMetaverse.Vector3(fwd.X, fwd.Y, fwd.Z);

        // Build a right-handed camera frame (SL: X=left, Y=at/forward, Z=up).
        // Up is always world-Z in the scene viewer.
        var worldUp = OpenMetaverse.Vector3.UnitZ;
        var left    = OpenMetaverse.Vector3.Cross(worldUp, atDir);
        if (left.LengthSquared() < 1e-6f)
        {
            // Camera is looking straight up/down — fall back to Y as up reference.
            left = OpenMetaverse.Vector3.Cross(OpenMetaverse.Vector3.UnitY, atDir);
        }
        left.Normalize();
        var up = OpenMetaverse.Vector3.Cross(atDir, left);
        up.Normalize();

        var mv = _instance.Client.Self.Movement;
        mv.Camera.Position = pos;
        mv.Camera.LookDirection(atDir, up);
        mv.Camera.Far = DrawDistance;
    }

    /// <summary>
    /// Slides the camera target to the avatar's current position without
    /// touching the orbit yaw, pitch, or zoom distance.
    /// Called on every terse/avatar update so the camera tracks movement smoothly.
    /// </summary>
    private void FollowAvatar()
    {
        if (_viewport == null) return;
        var pos = _instance.Client.Self.SimPosition;
        if (pos.X == 0f && pos.Y == 0f && pos.Z == 0f) return;

        // Compute the avatar's current heading in degrees.
        var rot = _instance.Client.Self.SimRotation;
        float headingRad = MathF.Atan2(
            2f * (rot.W * rot.Z + rot.X * rot.Y),
            1f - 2f * (rot.Y * rot.Y + rot.Z * rot.Z));
        float headingDeg = OpenTK.Mathematics.MathHelper.RadiansToDegrees(headingRad);

        // Compute the heading delta and rotate the camera yaw to stay behind the avatar.
        if (!float.IsNaN(_lastKnownHeadingDeg))
        {
            float delta = headingDeg - _lastKnownHeadingDeg;
            // Normalise to [-180, +180] to avoid wrap-around jumps.
            while (delta >  180f) delta -= 360f;
            while (delta < -180f) delta += 360f;
            if (MathF.Abs(delta) > 0.05f)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _viewport?.Camera.OrbitStep(delta, 0f);
                });
            }
        }
        _lastKnownHeadingDeg = headingDeg;

        // Use the same eye-level offset as CenterCameraOnAvatar so the camera
        // target stays at head height, not at the avatar's feet.
        _viewport.UpdateCameraFollow(new OpenTK.Mathematics.Vector3(pos.X, pos.Y, pos.Z + 1.0f));
    }

    [RelayCommand]
    private void ResetCamera()
    {
        CenterCameraOnAvatar();
        _viewport?.RequestRender();
    }

    [RelayCommand]
    private void OrbitUp()    => _viewport?.OrbitStep(0f, -10f);

    [RelayCommand]
    private void OrbitDown()  => _viewport?.OrbitStep(0f, 10f);

    [RelayCommand]
    private void OrbitLeft()  => _viewport?.OrbitStep(-15f, 0f);

    [RelayCommand]
    private void OrbitRight() => _viewport?.OrbitStep(15f, 0f);

    [RelayCommand]
    private void ZoomIn()  => _viewport?.ZoomStep(1.5f);

    [RelayCommand]
    private void ZoomOut() => _viewport?.ZoomStep(-1.5f);

    // ── Context-menu commands ─────────────────────────────────────────────────────

    /// <summary>
    /// Touch / click the selected prim (same as a left-click but accessible from the menu).
    /// </summary>
    [RelayCommand(CanExecute = nameof(ContextIsPrim))]
    private void TouchObject()
    {
        if (_disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || SelectedLocalId == 0) return;
        _ = _instance.Client.Objects.ClickObjectAsync(
                sim, SelectedLocalId,
                Vector3.Zero, Vector3.Zero, 0,
                Vector3.Zero, Vector3.Zero, Vector3.Zero);
    }

    /// <summary>
    /// Request to sit on the selected prim.
    /// </summary>
    [RelayCommand(CanExecute = nameof(ContextIsPrim))]
    private void SitOn()
    {
        if (_disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || SelectedLocalId == 0) return;
        if (!sim.ObjectsPrimitives.TryGetValue(SelectedLocalId, out var prim)) return;
        _instance.Client.Self.RequestSit(prim.ID, Vector3.Zero);
        _instance.Client.Self.Sit();
        ContextIsSitting = true;
    }

    /// <summary>
    /// Stand up from the current seat.
    /// </summary>
    [RelayCommand(CanExecute = nameof(ContextIsSitting))]
    private void StandUp()
    {
        if (_disposed) return;
        _instance.Client.Self.Stand();
        ContextIsSitting = false;
    }

    /// <summary>
    /// Point the orbit camera at the selected object / avatar.
    /// </summary>
    [RelayCommand]
    private void CameraFocus()
    {
        if (_disposed || _viewport == null || SelectedLocalId == 0) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null) return;

        bool isAvatar = ContextIsAvatar;
        Vector3 pos   = Vector3.Zero;

        if (isAvatar && sim.ObjectsAvatars.TryGetValue(SelectedLocalId, out var av))
            pos = av.Position;
        else if (!isAvatar && sim.ObjectsPrimitives.TryGetValue(SelectedLocalId, out var prim))
            pos = prim.Position;

        if (pos == Vector3.Zero) return;
        _viewport.SetCameraTarget(new OpenTK.Mathematics.Vector3(pos.X, pos.Y, pos.Z));
        _viewport.RequestRender();
    }

    /// <summary>
    /// Teleport to the selected avatar's current position.
    /// Only available when the context target is an avatar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(ContextIsAvatar))]
    private void TeleportToAvatar()
    {
        if (_disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || SelectedLocalId == 0) return;
        if (!sim.ObjectsAvatars.TryGetValue(SelectedLocalId, out var av)) return;

        var dest = av.Position + new Vector3(2f, 0f, 0f); // land next to, not on top of
        _ = Task.Run(() => _instance.Client.Self.Teleport(
                sim.Handle, dest, _instance.Client.Self.SimPosition));
    }

    /// <summary>
    /// Request the full properties of the selected prim (triggers ObjectProperties event).
    /// </summary>
    [RelayCommand(CanExecute = nameof(ContextIsPrim))]
    private void InspectObject()
    {
        if (_disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || SelectedLocalId == 0) return;
        _instance.Client.Objects.RequestObject(sim, SelectedLocalId);
    }

    // ── Avatar movement ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the view's KeyDown handler with a direction token.
    /// Each call increments the hold-counter for that direction so multiple
    /// simultaneous keys are handled correctly.
    /// </summary>
    public void BeginMove(MoveDirection dir)
    {
        if (_disposed || !_instance.Client.Network.Connected) return;
        switch (dir)
        {
            case MoveDirection.Forward:   Interlocked.Increment(ref _fwdHeld);       break;
            case MoveDirection.Backward:  Interlocked.Increment(ref _backHeld);      break;
            case MoveDirection.Left:      Interlocked.Increment(ref _leftHeld);      break;
            case MoveDirection.Right:     Interlocked.Increment(ref _rightHeld);     break;
            case MoveDirection.Up:
            case MoveDirection.Jump:      Interlocked.Increment(ref _upHeld);        break;
            case MoveDirection.Down:      Interlocked.Increment(ref _downHeld);      break;
            case MoveDirection.TurnLeft:  Interlocked.Increment(ref _turnLeftHeld);  break;
            case MoveDirection.TurnRight: Interlocked.Increment(ref _turnRightHeld); break;
        }
        EnsureMoveTimer();
    }

    /// <summary>
    /// Called by the view's KeyUp handler.
    /// </summary>
    public void EndMove(MoveDirection dir)
    {
        if (_disposed) return;
        switch (dir)
        {
            case MoveDirection.Forward:   Interlocked.Exchange(ref _fwdHeld,       0); break;
            case MoveDirection.Backward:  Interlocked.Exchange(ref _backHeld,      0); break;
            case MoveDirection.Left:      Interlocked.Exchange(ref _leftHeld,      0); break;
            case MoveDirection.Right:     Interlocked.Exchange(ref _rightHeld,     0); break;
            case MoveDirection.Up:
            case MoveDirection.Jump:      Interlocked.Exchange(ref _upHeld,        0); break;
            case MoveDirection.Down:      Interlocked.Exchange(ref _downHeld,      0); break;
            case MoveDirection.TurnLeft:  Interlocked.Exchange(ref _turnLeftHeld,  0); break;
            case MoveDirection.TurnRight: Interlocked.Exchange(ref _turnRightHeld, 0); break;
        }
        SendMovementUpdate();
    }

    [RelayCommand]
    private void ToggleFly()
    {
        if (!_instance.Client.Network.Connected) return;
        IsFlying = !IsFlying;
        _instance.Client.Self.Fly(IsFlying);
        UpdateStatusBar();
    }

    [RelayCommand]
    private void ToggleRun()
    {
        if (!_instance.Client.Network.Connected) return;
        IsRunning = !IsRunning;
        _instance.Client.Self.Movement.AlwaysRun = IsRunning;
        _instance.Client.Self.Movement.SendUpdate();
        UpdateStatusBar();
    }

    /// <summary>
    /// Called while Shift is held/released to enable fast (run-speed) movement
    /// without toggling the persistent AlwaysRun state.
    /// </summary>
    public void SetFastMove(bool fast)
    {
        if (_disposed || !_instance.Client.Network.Connected) return;
        var mv = _instance.Client.Self.Movement;
        mv.FastAt   = fast;
        mv.FastLeft = fast;
        mv.FastUp   = fast;
        mv.SendUpdate();
    }

    private void EnsureMoveTimer()
    {
        if (_moveTimer != null) return;
        // Fire at ~10 Hz — enough for smooth server-side movement without flooding.
        _moveTimer = new Timer(_ => SendMovementUpdate(), null, 0, 100);
    }

    private void StopMoveTimer()
    {
        var t = Interlocked.Exchange(ref _moveTimer, null);
        t?.Dispose();
    }

    private void SendMovementUpdate()
    {
        if (_disposed || !_instance.Client.Network.Connected) return;

        bool fwd       = _fwdHeld       > 0;
        bool back      = _backHeld      > 0;
        bool left      = _leftHeld      > 0;
        bool right     = _rightHeld     > 0;
        bool up        = _upHeld        > 0;
        bool down      = _downHeld      > 0;
        bool turnLeft  = _turnLeftHeld  > 0;
        bool turnRight = _turnRightHeld > 0;

        bool anyHeld = fwd || back || left || right || up || down || turnLeft || turnRight;
        if (!anyHeld) StopMoveTimer();

        var mv        = _instance.Client.Self.Movement;
        bool onVehicle = _instance.Client.Self.SittingOn != 0;

        mv.AtPos   = fwd;
        mv.AtNeg   = back;
        mv.LeftPos = left;
        mv.LeftNeg = right;
        mv.UpPos   = up;
        mv.UpNeg   = down;

        if (onVehicle)
        {
            // When seated on a vehicle the server uses AGENT_CONTROL_YAW_POS /
            // AGENT_CONTROL_YAW_NEG (same flags the SL C++ viewer sends in
            // LLAgent::propagateVehicleUpdate) to steer the vehicle.
            // BodyRotation is owned by the vehicle physics — do not mutate it.
            mv.TurnLeft  = false;
            mv.TurnRight = false;
            mv.YawPos    = turnLeft;
            mv.YawNeg    = turnRight;

            // Up/Down keys pitch the vehicle nose (aircraft climb/dive, boat trim).
            // AGENT_CONTROL_UP_POS/NEG are ignored by the vehicle physics engine;
            // AGENT_CONTROL_PITCH_POS/NEG are what LLAgent::propagateVehicleUpdate sends.
            mv.UpPos    = false;
            mv.UpNeg    = false;
            mv.PitchPos = up;
            mv.PitchNeg = down;
        }
        else
        {
            // On foot: AGENT_CONTROL_TURN_LEFT/RIGHT + advancing BodyRotation,
            // mirroring RadegastMovement.timer_Elapsed.
            // Timer interval is 100 ms → ~1 rad/s (~57°/s) turning speed.
            mv.YawPos    = false;
            mv.YawNeg    = false;
            mv.PitchPos  = false;
            mv.PitchNeg  = false;
            mv.TurnLeft  = turnLeft;
            mv.TurnRight = turnRight;

            const float TurnDelta = 0.1f; // seconds per tick
            if (turnLeft)
                mv.BodyRotation *= OpenMetaverse.Quaternion.CreateFromAxisAngle(
                    OpenMetaverse.Vector3.UnitZ, TurnDelta);
            else if (turnRight)
                mv.BodyRotation *= OpenMetaverse.Quaternion.CreateFromAxisAngle(
                    OpenMetaverse.Vector3.UnitZ, -TurnDelta);
        }

        mv.SendUpdate();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _instance.Client.Terrain.LandPatchReceived        -= OnLandPatchReceived;
        _instance.Client.Network.SimChanged               -= OnSimChanged;
        _instance.Client.Network.SimConnected             -= OnSimConnected;
        _instance.Client.Objects.ObjectUpdate             -= OnObjectUpdate;
        _instance.Client.Objects.KillObject               -= OnKillObject;
        _instance.Client.Objects.AvatarUpdate             -= OnAvatarUpdate;
        _instance.Client.Objects.TerseObjectUpdate        -= OnTerseObjectUpdate;
        _instance.Client.Objects.AvatarSitChanged         -= OnAvatarSitChanged;
        _instance.Client.Appearance.AppearanceSet         -= OnAppearanceSet;
        _instance.Client.Objects.ObjectProperties         -= OnObjectProperties;
        _instance.Client.Avatars.AvatarAppearance         -= OnAvatarAppearance;
        _instance.NetCom.ChatReceived                     -= OnChatReceived;

        if (_viewport != null)
        {
            _viewport.FaceClicked -= OnFaceClicked;
            _viewport.SceneReset  -= OnSceneReset;
        }

        // Release any held movement keys so the avatar doesn't keep moving.
        StopMoveTimer();

        _cameraSyncTimer?.Dispose();
        _cameraSyncTimer = null;
        if (_instance.Client.Network.Connected)
        {
            var mv = _instance.Client.Self.Movement;
            mv.AtPos = mv.AtNeg = mv.LeftPos = mv.LeftNeg = mv.UpPos = mv.UpNeg = false;
            mv.TurnLeft = mv.TurnRight = false;
            mv.SendUpdate();
        }

        lock (_terrainCtsLock)
        {
            _terrainCts?.Cancel();
            _terrainCts?.Dispose();
            _terrainCts = null;
        }

        _objectStreamer?.Dispose();
        _objectStreamer = null;

        _avatarStreamer?.Dispose();
        _avatarStreamer = null;

        _buildScheduler?.Dispose();
        _buildScheduler = null;

        _particleStreamer?.Dispose();
        _particleStreamer = null;

        _flexiStreamer?.Dispose();
        _flexiStreamer = null;

        _avatarAnimStreamer?.Dispose();
        _avatarAnimStreamer = null;

        _nameTagService?.Dispose();
        _nameTagService = null;

        // Release all decoded SKBitmaps from the GL texture cache — the scene is going
        // away so the RAM is no longer needed.
        GridTextureHelper.ClearSkBitmapCache();

        _viewport = null;
    }

    // ── GL context lifecycle ──────────────────────────────────────────────────────

    // Called on the UI thread by GlViewportControl after every successful GlInit.
    // Avalonia's TabControl detaches the visual tree on tab switch, which destroys
    // the GL context (GlDeinit) and frees all GPU scene data. When the user returns
    // to the SceneViewer tab, GlInit fires again — but the streamers' _rendered sets
    // still think all objects are uploaded. Re-dirtying everything triggers a full
    // re-upload into the fresh GL context.
    private void OnSceneReset()
    {
        if (_disposed) return;

        // Re-dirty everything that was rendered before the GL context teardown.
        _objectStreamer?.RebuildAllRendered();
        _avatarStreamer?.RebuildAllRendered();

        // Safety net: if _rendered was cleared (e.g. by a sim-change event that fired
        // while the SceneViewer tab was hidden), RebuildAllRendered above would enqueue
        // nothing. SeedStreamersFromCurrentSim covers those objects. Both paths key on
        // rootId in _dirty (a ConcurrentDictionary), so duplicates are simply
        // overwritten — the scheduler never sees duplicate entries.
        SeedStreamersFromCurrentSim();

        // Terrain geometry was disposed with the old GL context.  Rebuild it now.
        _ = RefreshTerrainAsync();
    }

    // ── Frame-stats callback ──────────────────────────────────────────────────────

    // Called on the GL thread by FrameStatsTracker; marshal to UI thread for binding.
    private void OnFrameCompleted(FrameStats stats)
    {
        if (_disposed || !ShowPerfOverlay) return;

        // Snapshot in-flight counters on the calling thread — all reads are O(1) atomic/lock-free.
        int buildQueue   = _buildScheduler?.QueueCount          ?? 0;
        int objBuilds    = _objectStreamer?.InflightCount        ?? 0;
        int avBuilds     = _avatarStreamer?.InflightCount        ?? 0;
        int pendingUpl   = _viewport?.PendingUploadCount        ?? 0;
        int texDecodes   = GridTextureHelper.InflightDecodeCount;

        var text = $"CPU {stats.CpuTimeMs:F1} ms" +
                   (stats.GpuTimeMs > 0 ? $"  GPU {stats.GpuTimeMs:F1} ms" : string.Empty) +
                   $"\nDraws {stats.DrawCalls}  Tris {stats.Triangles:#,0}" +
                   $"\nFaces {stats.FacesSubmitted}  Culled {stats.FacesCulled}" +
                   $"\nBuild Q:{buildQueue}  Obj:{objBuilds}  Av:{avBuilds}" +
                   $"\nUpload Q:{pendingUpl}  TexDec:{texDecodes}";
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) PerfOverlayText = text;
        });
    }

    // ── Name-tag callback ─────────────────────────────────────────────────────────

    private void OnNameTagsUpdated(System.Collections.Generic.IReadOnlyList<NameTagItem> tags)
    {
        if (_disposed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            NameTags.Clear();
            if (ShowNameTags)
                foreach (var t in tags) NameTags.Add(t);
        });
    }

    private void OnHoverTagsUpdated(System.Collections.Generic.IReadOnlyList<HoverTextItem> items)
    {
        if (_disposed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            HoverTags.Clear();
            foreach (var item in items) HoverTags.Add(item);
        });
    }

    // ── Nearby chat overlay ───────────────────────────────────────────────────────

    // Delivered on the UI thread by NetComAvalonia.PostToUI.
    private void OnChatReceived(object? sender, OpenMetaverse.ChatEventArgs e)
    {
        if (_disposed) return;
        if (e.Type is OpenMetaverse.ChatType.StartTyping or OpenMetaverse.ChatType.StopTyping) return;
        if (e.SourceType == OpenMetaverse.ChatSourceType.System && string.IsNullOrWhiteSpace(e.Message)) return;

        var lineType = e.SourceType switch
        {
            OpenMetaverse.ChatSourceType.Agent when e.FromName == _instance.Client.Self.Name => ChatLineType.Self,
            OpenMetaverse.ChatSourceType.Agent => ChatLineType.Normal,
            OpenMetaverse.ChatSourceType.Object => ChatLineType.Object,
            _ => ChatLineType.System
        };

        string prefix = e.Type == OpenMetaverse.ChatType.Shout   ? " shouts" :
                        e.Type == OpenMetaverse.ChatType.Whisper  ? " whispers" : "";
        string text;
        if (e.Message.StartsWith("/me ", StringComparison.OrdinalIgnoreCase))
        {
            text     = e.Message[4..];
            lineType = ChatLineType.Emote;
        }
        else
        {
            text = $"{prefix}: {e.Message}";
        }

        var line = new ChatLine(DateTime.Now, e.FromName, text, lineType, e.SourceID);

        // Already on the UI thread — no Post needed.
        while (ChatOverlayLines.Count >= ChatOverlayMaxLines)
            ChatOverlayLines.RemoveAt(0);
        ChatOverlayLines.Add(line);
    }
}
