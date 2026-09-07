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
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using OmVector3 = LibreMetaverse.Vector3;
using Vector3   = System.Numerics.Vector3;
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
/// </summary>
public partial class SceneViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    // The shared Nearby-chat ViewModel — chat sent from the viewport is delegated to its
    // ProcessChatInput (gesture/command/RLV handling) rather than reimplemented here, and
    // its history buffer is shared so Ctrl+Up/Down recall works across both chat surfaces.
    private readonly NearbyViewModel _chat;
    private ISceneViewport? _viewport;
    private bool _disposed;

    private SceneTerrainBuilder? _terrainBuilder;
    private CancellationTokenSource? _terrainCts;
    private readonly object _terrainCtsLock = new();

    // Terrain builders and cancellation tokens for each neighbor simulator.
    // Keyed by sim.Handle so each region gets at most one in-flight build.
    private readonly Dictionary<ulong, SceneTerrainBuilder>      _neighborTerrainBuilders = new();
    private readonly Dictionary<ulong, CancellationTokenSource>  _neighborTerrainCts      = new();
    private readonly object                                       _neighborTerrainLock     = new();
    // Shared across the object/avatar streamers and neighbor-terrain tracking so a given
    // neighbor sim's objects, avatars, and terrain all derive from the same collision-free index.
    private SceneNeighborSimIndex?           _neighborIndex;
    private SceneObjectStreamer?             _objectStreamer;
    private SceneAvatarStreamer?             _avatarStreamer;
    private SceneParticleStreamer?           _particleStreamer;
    private SceneLightStreamer?              _lightStreamer;
    private SceneFlexiStreamer?              _flexiStreamer;
    private SceneAvatarAnimationStreamer?    _avatarAnimStreamer;
    private SceneAnimeshStreamer?            _animeshStreamer;
    private AvatarRenderInfoReporter?        _avatarRenderInfoReporter;
    private SceneNameTagService?             _nameTagService;
    private SceneBuildScheduler?             _buildScheduler;
    private SceneEnvironmentService?         _envService;

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
    [ObservableProperty] private bool   _waterReflectionsEnabled = false;
    [ObservableProperty] private bool   _atmosphericsEnabled = true;
    [ObservableProperty] private bool   _godRaysEnabled = true;
    [ObservableProperty] private bool   _shadowsEnabled = false;
    [ObservableProperty] private bool   _avatarRenderInfoReportingEnabled = true;
    /// <summary>
    /// Kill switch for the lightweight region-crossing path (see <see cref="OnSimChanged"/>):
    /// forces every crossing through the full clear+rebuild path regardless of whether the
    /// destination is already a tracked neighbor. Default off (lightweight path enabled); flip
    /// to true to isolate a crossing regression without reverting code.
    /// </summary>
    [ObservableProperty] private bool   _forceFullClearOnCrossing;
    [ObservableProperty] private bool   _showAvatarComplexityInNameTags = false;
    [ObservableProperty] private string _perfOverlayText = string.Empty;

    private const int ChatOverlayMaxLines = 10;
    /// <summary>Bounded list of the most recent nearby chat messages shown in the scene overlay.</summary>
    public ObservableCollection<ChatLine> ChatOverlayLines { get; } = new();
    /// <summary>Text currently typed into the viewport's own chat input box.</summary>
    [ObservableProperty] private string _chatInput = string.Empty;
    /// <summary>Whether the viewport's chat input box is shown (toggled by Enter / the chat button).</summary>
    [ObservableProperty] private bool   _chatInputVisible;
    /// <summary>True while the viewport is in first-person mouselook (see <see cref="ISceneViewport.MouselookActive"/>).</summary>
    [ObservableProperty] private bool   _mouselookActive;
    [ObservableProperty] private bool   _isFlying;
    [ObservableProperty] private bool   _isRunning;
    /// <summary>Draw / stream distance in metres (16–512). Default matches SceneObjectStreamer.</summary>
    [ObservableProperty] private float  _drawDistance = 96f;
    /// <summary>
    /// Avatar rendering-complexity threshold (0–<see cref="SceneAvatarStreamer.ComplexityThresholdMax"/>,
    /// which means "unlimited"). Avatars estimated over this render as a silhouette/cloud impostor.
    /// </summary>
    [ObservableProperty] private float  _avatarComplexityThreshold = 120f;
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

    public SceneViewerViewModel(RadegastInstanceAvalonia instance, NearbyViewModel chat)
    {
        _instance    = instance;
        _chat        = chat;
        _ssaoEnabled = instance.GlobalSettings["ssao_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["ssao_enabled"].AsBoolean() : true;
        _frustumCullingEnabled = instance.GlobalSettings["frustum_culling_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["frustum_culling_enabled"].AsBoolean() : true;
        _waterReflectionsEnabled = instance.GlobalSettings["water_reflections_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["water_reflections_enabled"].AsBoolean() : false;
        _atmosphericsEnabled = instance.GlobalSettings["atmospherics_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["atmospherics_enabled"].AsBoolean() : true;
        _godRaysEnabled = instance.GlobalSettings["god_rays_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["god_rays_enabled"].AsBoolean() : true;
        _shadowsEnabled = instance.GlobalSettings["shadows_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["shadows_enabled"].AsBoolean() : false;
        _drawDistance = instance.GlobalSettings["scene_draw_distance"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? (float)instance.GlobalSettings["scene_draw_distance"].AsReal() : 96f;
        _avatarComplexityThreshold = instance.GlobalSettings["avatar_complexity_threshold"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? (float)instance.GlobalSettings["avatar_complexity_threshold"].AsReal() : 120f;
        _avatarRenderInfoReportingEnabled = instance.GlobalSettings["avatar_render_info_reporting_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["avatar_render_info_reporting_enabled"].AsBoolean() : true;
        _showAvatarComplexityInNameTags = instance.GlobalSettings["show_avatar_complexity_in_nametags"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["show_avatar_complexity_in_nametags"].AsBoolean() : false;
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

    partial void OnWaterReflectionsEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.WaterReflectionsEnabled = value;
    }

    partial void OnAtmosphericsEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.AtmosphericsEnabled = value;
    }

    partial void OnGodRaysEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.GodRaysEnabled = value;
    }

    partial void OnShadowsEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.ShadowsEnabled = value;
    }

    partial void OnAvatarRenderInfoReportingEnabledChanged(bool value)
    {
        if (_avatarRenderInfoReporter != null) _avatarRenderInfoReporter.Enabled = value;
    }

    partial void OnShowAvatarComplexityInNameTagsChanged(bool value)
    {
        if (_nameTagService != null) _nameTagService.ShowComplexityCost = value;
    }

    partial void OnShowPerfOverlayChanged(bool value)
    {
        if (_viewport != null) _viewport.ShowPerfOverlay = value;
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

    partial void OnAvatarComplexityThresholdChanged(float value)
    {
        if (_avatarStreamer == null) return;
        _avatarStreamer.ComplexityThreshold = value;
        _avatarStreamer.RecomputeAllTiers();
    }

    /// <summary>
    /// Attach the GL viewport so the VM can configure it and submit
    /// geometry to it. Called from the view's code-behind after the visual tree is ready.
    /// </summary>
    public void SetViewport(ISceneViewport viewport)
    {
        _viewport                           = viewport;
        _viewport.Wireframe                  = Wireframe;
        _viewport.SsaoEnabled                = SsaoEnabled;
        _viewport.FrustumCullingEnabled      = FrustumCullingEnabled;
        _viewport.WaterReflectionsEnabled    = WaterReflectionsEnabled;
        _viewport.AtmosphericsEnabled        = AtmosphericsEnabled;
        _viewport.GodRaysEnabled              = GodRaysEnabled;
        _viewport.ShadowsEnabled              = ShadowsEnabled;
        _viewport.ShowPerfOverlay              = ShowPerfOverlay;
        _viewport.WaterHeight            = _instance.Client.Network.CurrentSim?.WaterHeight ?? float.NaN;

        LibreMetaverse.Logger.Info(
            $"[SceneViewer] Graphics settings at open: SSAO={SsaoEnabled} FrustumCulling={FrustumCullingEnabled} " +
            $"WaterReflections={WaterReflectionsEnabled} Atmospherics={AtmosphericsEnabled} Shadows={ShadowsEnabled} " +
            $"DrawDistance={DrawDistance}");
        _viewport.Stats.FrameCompleted  += OnFrameCompleted;
        _viewport.InitFailed += msg =>
        {
            // This event is the only place a panel-fatal RenderFrame/init failure (e.g. an
            // OOM not caught by a narrower per-object try/catch) surfaces at all, so it must
            // be logged, not just reflected in the StatusText UI label.
            LibreMetaverse.Logger.Error($"[SceneViewer] Viewport init failed: {msg}");
            StatusText = $"Viewport init failed: {msg}";
        };
        _viewport.SceneReset            += OnSceneReset;
        _viewport.FaceClicked           += OnFaceClicked;
        _viewport.ObjectRightClicked    += OnObjectRightClicked;
        _viewport.GroundClicked         += OnGroundClicked;
        _viewport.MouselookChanged      += OnMouselookChanged;
        _viewport.TerrainHeightProvider = (x, y) =>
        {
            var sim = _instance.Client.Network.CurrentSim;
            return sim != null && sim.TerrainHeightAtPoint(x, y, out var h) ? h : (float?)null;
        };

        // Subscribe to land patch and sim-change events.
        _instance.Client.Terrain.LandPatchReceived        += OnLandPatchReceived;
        _instance.Client.Network.SimChanged               += OnSimChanged;
        _instance.Client.Network.SimConnected             += OnSimConnected;
        _instance.Client.Network.SimDisconnected          += OnSimDisconnected;
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
        _neighborIndex        = new SceneNeighborSimIndex();
        _objectStreamer       = new SceneObjectStreamer(_instance.Client, viewport, _buildScheduler, _neighborIndex);
        _avatarStreamer       = new SceneAvatarStreamer(_instance.Client, viewport, _buildScheduler, _instance.AvatarRenderOverrides, _neighborIndex);
        _avatarRenderInfoReporter = new AvatarRenderInfoReporter(_instance.Client, _avatarStreamer, _instance)
        {
            Enabled = AvatarRenderInfoReportingEnabled
        };
        _avatarRenderInfoReporter.Start();
        _particleStreamer    = new SceneParticleStreamer(_instance.Client, viewport);
        _lightStreamer       = new SceneLightStreamer(_instance.Client);
        _viewport.LightStreamer = _lightStreamer;
        _flexiStreamer       = new SceneFlexiStreamer(_instance.Client, viewport, _objectStreamer);
        _flexiStreamer.SetAvatarStreamer(_avatarStreamer);
        _avatarAnimStreamer  = new SceneAvatarAnimationStreamer(_instance.Client, viewport, _avatarStreamer);
        _flexiStreamer.SetAnimationStreamer(_avatarAnimStreamer);
        _avatarStreamer.SetAnimationStreamer(_avatarAnimStreamer);
        _animeshStreamer     = new SceneAnimeshStreamer(_instance.Client, viewport, _objectStreamer);

        // Apply current draw distance to freshly created streamers.
        _objectStreamer.DrawDistance = DrawDistance;
        _avatarStreamer.DrawDistance = DrawDistance;
        _avatarStreamer.ComplexityThreshold = AvatarComplexityThreshold;

        // Name-tag overlay service.
        _nameTagService = new SceneNameTagService(_instance.Client, viewport, _avatarStreamer)
        {
            ShowComplexityCost = ShowAvatarComplexityInNameTags
        };
        _nameTagService.TagsUpdated      += OnNameTagsUpdated;
        _nameTagService.HoverTagsUpdated += OnHoverTagsUpdated;
        _nameTagService.Start();

        // EEP day-cycle service — feeds interpolated sky/water into the GL viewport once per frame.
        _envService = new SceneEnvironmentService(_instance);
        _viewport.EnvironmentService = _envService;

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
    /// Feeds BOTH root and child prims through <c>OnObjectUpdate</c>. Root-only would rely on each
    /// streamer's own <c>CollectLinkset</c> to pull children in, but <c>_childrenByParent</c> (which
    /// <c>CollectLinkset</c> depends on entirely) is populated ONLY inside
    /// <c>SceneObjectStreamer.OnObjectUpdate</c> -- so a child prim already resident in
    /// <c>ObjectsPrimitives</c> at seed time, whose own live <c>ObjectUpdate</c> event fired before
    /// this panel ever subscribed to it, would never be registered as anyone's child and would stay
    /// permanently absent from its linkset regardless of session length. Feeding children too is
    /// safe: <c>EnqueueDirty</c>'s backing store is a dictionary keyed by the LINKSET's (root's)
    /// scene key, not a queue, so every child of the same root collapses onto one dirty entry
    /// regardless of how many are fed here -- no per-child build multiplication.
    /// </para>
    /// </summary>
    private void SeedStreamersFromCurrentSim()
    {
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null) return;

        foreach (var prim in sim.ObjectsPrimitives.Values)
        {
            _objectStreamer?.OnObjectUpdate(sim, prim, isAttachment: false);
        }

        foreach (var avatar in sim.ObjectsAvatars.Values)
        {
            _avatarStreamer?.OnAvatarUpdate(sim, avatar);
        }

        // Seed particle streamer with all root prims that have emitters.
        _particleStreamer?.SeedFromCurrentSim();

        // Seed local-light streamer with all root prims that carry Light params.
        _lightStreamer?.SeedFromCurrentSim();
    }

    /// <summary>
    /// Seeds all NEIGHBOR simulators that are already connected when the viewer opens or resets.
    /// Objects cached in neighbor sims before the viewer subscribed to ObjectUpdate never fire
    /// new events, so we feed them through the streamer manually here.
    /// Also kicks off terrain builds for each reachable neighbor.
    /// <para>
    /// Feeds every prim, not just roots -- see <see cref="SeedStreamersFromCurrentSim"/>'s doc
    /// comment for why root-only silently drops any child prim whose own live event fired before
    /// this panel subscribed.
    /// </para>
    /// </summary>
    private void SeedNeighborSims()
    {
        if (_objectStreamer == null || _disposed) return;
        var currentSim = _instance.Client.Network.CurrentSim;
        if (currentSim == null) return;

        Simulator[] neighborSims;
        lock (_instance.Client.Network.Simulators)
            neighborSims = _instance.Client.Network.Simulators
                .Where(s => s != currentSim)
                .ToArray();

        foreach (var sim in neighborSims)
        {
            foreach (var prim in sim.ObjectsPrimitives.Values)
            {
                _objectStreamer.OnObjectUpdate(sim, prim, isAttachment: false);
            }
            _ = RefreshNeighborTerrainAsync(sim);
        }
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
        _lightStreamer?.OnObjectUpdate(e.Simulator, e.Prim, e.IsAttachment);
        if (e.IsAttachment)
            _avatarStreamer?.OnAttachmentObjectUpdate(e.Simulator, e.Prim, e.IsNew);

        // A full ObjectUpdate may carry a prim that is already (or will become) a
        // seat for one or more avatars.  When avatar ObjectUpdates arrive before
        // the seat prim's first ObjectUpdate (common at initial scene load), the
        // avatar builder falls back to an incorrect world position.  Nudge seated
        // avatar transforms here so they are corrected as soon as the prim arrives,
        // without waiting for a terse update (which may never come for static seats).
        var rootId = e.Prim.ParentID == 0 ? e.Prim.LocalID : e.Prim.ParentID;
        _avatarStreamer?.OnSeatPrimMoved(e.Simulator, rootId);
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
        // Diagnostic logging (2026-09-05) on every bail-out below, kept -- see InspectObject's
        // own comment for why.
        if (SelectedLocalId == 0)
        {
            LibreMetaverse.Logger.Debug(
                $"[SceneViewerViewModel] OnObjectProperties: discarded, nothing selected (reply objectId={e.Properties.ObjectID})");
            return;
        }
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || e.Simulator != sim)
        {
            LibreMetaverse.Logger.Debug(
                $"[SceneViewerViewModel] OnObjectProperties: discarded, wrong/no sim (reply from {e.Simulator?.Name ?? "null"}, current sim {sim?.Name ?? "null"})");
            return;
        }
        if (!sim.ObjectsPrimitives.TryGetValue(SelectedLocalId, out var selected))
        {
            LibreMetaverse.Logger.Debug(
                $"[SceneViewerViewModel] OnObjectProperties: discarded, SelectedLocalId={SelectedLocalId} not in sim.ObjectsPrimitives "
                + $"(reply objectId={e.Properties.ObjectID}, name=\"{e.Properties.Name}\")");
            return;
        }
        if (selected.ID != e.Properties.ObjectID)
        {
            LibreMetaverse.Logger.Debug(
                $"[SceneViewerViewModel] OnObjectProperties: discarded, reply is for a different object "
                + $"(selected localId={SelectedLocalId} has UUID {selected.ID}, reply UUID={e.Properties.ObjectID}, name=\"{e.Properties.Name}\")");
            return;
        }

        var name = e.Properties.Name;
        if (string.IsNullOrEmpty(name))
        {
            LibreMetaverse.Logger.Debug(
                $"[SceneViewerViewModel] OnObjectProperties: discarded, reply for {selected.ID} carried an empty name");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            SelectedInfo = $"{name}  [{selected.ID}]  face {_selectedFaceIndex}";
            ContextLabel = name;
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
            _lightStreamer?.OnTerseObjectUpdate(e.Simulator, e.Prim);

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
        // Harmless no-op unless this LocalID was a tracked avatar's attachment.
        _avatarStreamer?.OnAttachmentKilled(e.ObjectLocalID);
        _particleStreamer?.OnKillObject(e.Simulator, e.ObjectLocalID);
        _lightStreamer?.OnKillObject(e.Simulator, e.ObjectLocalID);
        _flexiStreamer?.OnKillObject(e.Simulator, e.ObjectLocalID);
        _avatarAnimStreamer?.OnKillAvatar(e.Simulator, e.ObjectLocalID);
        _animeshStreamer?.OnKillObject(e.Simulator, e.ObjectLocalID);
    }

    private void OnLandPatchReceived(object? sender, LandPatchReceivedEventArgs e)
    {
        if (_disposed) return;
        if (e.Simulator == _instance.Client.Network.CurrentSim)
            _ = RefreshTerrainAsync(centerCamera: false);
        else
            _ = RefreshNeighborTerrainAsync(e.Simulator);

        RefreshAdjacentNeighborTerrainIfBoundaryPatch(e.Simulator, e.X, e.Y);
    }

    /// <summary>
    /// When the patch that just arrived sits on <paramref name="sim"/>'s boundary (patch index
    /// 0 or 15 of the 16x16 patch grid), also refreshes whichever neighbor sits across that
    /// specific edge, so its terrain mesh rebuilds and re-runs <see cref="SceneTerrainBuilder"/>'s
    /// edge-blend pass against this newly-arrived data. Without this, a region's edge could sit
    /// un-blended until something unrelated happens to trigger that neighbor's own next rebuild.
    /// </summary>
    private void RefreshAdjacentNeighborTerrainIfBoundaryPatch(Simulator sim, int patchX, int patchY)
    {
        if (patchX == 0)  TryRefreshNeighborAcross(sim, -256, 0);
        if (patchX == 15) TryRefreshNeighborAcross(sim, 256, 0);
        if (patchY == 0)  TryRefreshNeighborAcross(sim, 0, -256);
        if (patchY == 15) TryRefreshNeighborAcross(sim, 0, 256);
    }

    private void TryRefreshNeighborAcross(Simulator sim, int dx, int dy)
    {
        var neighbor = TerrainSeamHelper.FindNeighborSim(_instance.Client, sim, dx, dy);
        if (neighbor == null) return;
        if (neighbor == _instance.Client.Network.CurrentSim)
            _ = RefreshTerrainAsync(centerCamera: false);
        else
            _ = RefreshNeighborTerrainAsync(neighbor);
    }

    /// <summary>
    /// Promotes an already-tracked neighbor sim to "current" in place, instead of the ordinary
    /// full clear + re-stream <see cref="OnSimChanged"/> otherwise does on every sim change.
    /// <para>
    /// <see cref="SceneNeighborSimIndex"/>'s indices are permanent per-simulator, so no scene key
    /// changes here -- only each already-built object's baked world-space transform needs
    /// correcting (<see cref="SceneObjectStreamer.RebaseAllForRegionPromotion"/>), by the
    /// constant grid-coordinate delta between the old and new current sim. The old-current region
    /// is demoted into an ordinary tracked neighbor (<see cref="RefreshNeighborTerrainAsync"/>)
    /// rather than having its content vanish.
    /// </para>
    /// <para>
    /// Avatars are deliberately NOT rebased here: <see cref="SceneAvatarStreamer"/> always
    /// resolves an avatar's world position fresh from live <c>Self.SimPosition</c>/
    /// <c>Avatar.Position</c> (see <c>ResolveAvatarWorldTransform</c>), never cached relative to a
    /// particular current sim, so they self-correct via their own next terse update with no
    /// action needed here -- and flexi attachments ride along with their owning avatar's
    /// transform for the same reason. Avatars belonging to the newly-current sim DO need an
    /// initial seed, though: unlike objects, <see cref="SeedNeighborSims"/> never tracked them
    /// while this sim was still a neighbor.
    /// </para>
    /// </summary>
    private void HandleLightweightRegionPromotion(Simulator oldSim, Simulator newSim)
    {
        Utils.LongToUInts(oldSim.Handle, out uint ocx, out uint ocy);
        Utils.LongToUInts(newSim.Handle, out uint ncx, out uint ncy);
        var rebaseDelta = new Vector3((int)ocx - (int)ncx, (int)ocy - (int)ncy, 0f);

        _objectStreamer?.RebaseAllForRegionPromotion(rebaseDelta);

        // Terrain lives outside SceneObjectStreamer's own tracking -- it's submitted directly to
        // the viewport by RefreshNeighborTerrainAsync/RefreshTerrainAsync under
        // ((ulong)simIndex<<32)|uint.MaxValue -- so RebaseAllForRegionPromotion above never
        // touches it. old-current's and new-current's terrain are handled below by a fresh
        // rebuild (already correct relative to the new current sim), but any OTHER
        // already-tracked neighbor's terrain was baked relative to the OLD current sim and needs
        // the exact same delta objects just got, or it's left offset by one region-width -- a
        // visible gap at that neighbor's border.
        if (_viewport != null && _neighborIndex != null && rebaseDelta != Vector3.Zero)
        {
            var otherTerrainKeys = new List<ulong>();
            foreach (var (simIndex, trackedSim) in _neighborIndex.TrackedSims)
            {
                if (trackedSim.Handle == oldSim.Handle || trackedSim.Handle == newSim.Handle) continue;
                otherTerrainKeys.Add(((ulong)simIndex << 32) | uint.MaxValue);
            }
            if (otherTerrainKeys.Count > 0)
                _viewport.RebaseSceneObjectTransforms(otherTerrainKeys, rebaseDelta);
        }

        _envService?.OnRegionChanged();
        _avatarRenderInfoReporter?.OnSimChanged();
        if (_viewport != null)
            _viewport.WaterHeight = newSim.WaterHeight;
        UpdateStatusBar();

        Dispatcher.UIThread.Post(() =>
        {
            _ = RefreshTerrainAsync(centerCamera: true); // rebuild terrain for the newly-current sim
            _ = RefreshNeighborTerrainAsync(oldSim);      // demote old-current into a tracked neighbor
            _ = Task.Run(() =>
            {
                foreach (var avatar in newSim.ObjectsAvatars.Values)
                    _avatarStreamer?.OnAvatarUpdate(newSim, avatar);
                SeedNeighborSims();
            });
        });
    }

    private void OnSimChanged(object? sender, SimChangedEventArgs e)
    {
        if (_disposed) return;

        // Lightweight path: the destination is already a tracked neighbor (its terrain and
        // objects are already built), so promote it in place instead of clearing and
        // re-streaming the whole scene. Covers both an actual region crossing AND a teleport to
        // an already-nearby region -- SimChanged doesn't distinguish the two at the source (both
        // funnel through the same NetworkManager.SetCurrentSim), and IsTracked's answer is what
        // actually matters here, not which packet triggered the change.
        var oldSim = e.PreviousSimulator;
        var newSim = _instance.Client.Network.CurrentSim;
        if (!ForceFullClearOnCrossing && oldSim != null && newSim != null && oldSim != newSim &&
            _neighborIndex != null && _neighborIndex.IsTracked(newSim.Handle))
        {
            HandleLightweightRegionPromotion(oldSim, newSim);
            return;
        }

        // Fallback: full clear + rebuild + re-center, for teleports into an untracked region (or
        // the very first connect, where oldSim is null). Also flush the decoded-bitmap cache:
        // textures from the old sim are unlikely to be reused and releasing them now reclaims the
        // RAM before the new sim loads its own textures.
        GridTextureHelper.ClearSkBitmapCache();
        CancelAllNeighborTerrainBuilds();

        // Re-fetch EEP for the new region. SimConnected (which normally triggers this) does not
        // reliably re-fire when crossing into an already-connected neighbor sim — the common case
        // for a seamless region crossing — so the sky/water settings were sticking with whatever
        // the old region had until a full teleport/relog forced a fresh SimConnected.
        _envService?.OnRegionChanged();

        Dispatcher.UIThread.Post(() =>
        {
            // Drop pending (not-yet-started) build entries for the old region before the
            // streamers themselves clear -- previously nothing purged this shared queue on sim
            // change, so old-region tessellation work that was still waiting for a concurrency
            // slot delayed new-region builds behind entries that would just be cancelled anyway
            // the moment they ran (each factory checks its own token once it starts).
            _buildScheduler?.Clear();
            _objectStreamer?.Clear();
            _avatarStreamer?.Clear();
            _particleStreamer?.Clear();
            _lightStreamer?.Clear();
            _flexiStreamer?.Clear();
            _avatarAnimStreamer?.Clear();
            _animeshStreamer?.Clear();
            _avatarRenderInfoReporter?.OnSimChanged();
            NameTags.Clear();
            SelectedLocalId = 0;
            SelectedInfo    = string.Empty;
            _viewport?.SetSelectedObject(0);
            UpdateStatusBar();
            if (_viewport != null)
                _viewport.WaterHeight = _instance.Client.Network.CurrentSim?.WaterHeight ?? float.NaN;
            _ = RefreshTerrainAsync(centerCamera: true);

            // Re-seed from whatever is already present in the new sim's object cache, then seed
            // any already-connected neighbor sims. Backgrounded: both walk every cached prim in
            // the current sim plus every connected neighbor sim's full cache, synchronously,
            // every crossing (Clear() above just wiped _rendered, and the coordinate frame
            // shifted, so everything genuinely needs re-enqueuing) -- at a busy border with
            // multiple neighbors connected that's tens of thousands of prims, enough to freeze
            // the UI thread for the better part of a minute if run inline. Safe to move off-
            // thread: every streamer method called here (OnObjectUpdate, OnAvatarUpdate,
            // SeedFromCurrentSim) is already invoked routinely from LibreMetaverse network I/O
            // threads during normal operation, and each one only touches ConcurrentDictionary-
            // backed streamer state -- no Avalonia UI-bound collection or dispatcher affinity involved.
            _ = Task.Run(() =>
            {
                SeedStreamersFromCurrentSim();
                SeedNeighborSims();
            });
        });
    }

    private void OnSimConnected(object? sender, SimConnectedEventArgs e)
    {
        if (_disposed) return;
        if (e.Simulator == _instance.Client.Network.CurrentSim)
        {
            SyncMovementState();
            UpdateStatusBar();
            if (_viewport != null)
                _viewport.WaterHeight = e.Simulator.WaterHeight;
            _envService?.OnRegionChanged();
            _ = RefreshTerrainAsync(centerCamera: true);
            // Backgrounded like the seed walks in OnSimChanged/OnSceneReset: this is the handler
            // for a FRESH connect (first login, or a neighbor sim finishing its own handshake),
            // not a crossing between already-known sims, but LibreMetaverse can have a
            // substantial on-disk object cache already populated for a previously-visited region
            // the moment SimConnected fires, so this must not run inline on the calling thread.
            _ = Task.Run(SeedStreamersFromCurrentSim);
        }
        else
        {
            // Neighbor simulator connected — build its terrain if we have patch data.
            _ = RefreshNeighborTerrainAsync(e.Simulator);
        }
    }

    /// <summary>
    /// Fired when any tracked simulator (current or neighbor) disconnects. Removes that specific
    /// sim's terrain and streamed objects without touching anything else -- previously nothing
    /// subscribed to this event at all, so a disconnected neighbor's content only ever went away
    /// via the next full <see cref="OnSimChanged"/> clear, which the lightweight region-crossing
    /// path is designed to skip.
    /// </summary>
    private void OnSimDisconnected(object? sender, SimDisconnectedEventArgs e)
    {
        if (_disposed) return;
        if (e.Simulator == _instance.Client.Network.CurrentSim) return; // full OnSimChanged/teardown handles this case

        lock (_neighborTerrainLock)
        {
            if (_neighborTerrainCts.TryGetValue(e.Simulator.Handle, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _neighborTerrainCts.Remove(e.Simulator.Handle);
            }
            _neighborTerrainBuilders.Remove(e.Simulator.Handle);
        }

        if (_neighborIndex != null && _neighborIndex.IsTracked(e.Simulator.Handle))
        {
            uint simIndex = _neighborIndex.GetSimIndex(e.Simulator);
            _viewport?.RemoveSceneObject(((ulong)simIndex << 32) | uint.MaxValue); // terrain
            _objectStreamer?.RemoveObjectsForSim(e.Simulator);
            _neighborIndex.Forget(e.Simulator.Handle);
        }
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
            var submission = await _terrainBuilder.RebuildAsync(ct: ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || submission == null) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _viewport?.Submit(submission);
                if (centerCamera) CenterCameraOnAvatar();
                UpdateStatusBar();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Terrain error: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds or rebuilds terrain for a neighbor simulator within draw distance of the
    /// agent.  Only triggers a build when the simulator is within one region length of
    /// the agent's position, so distant sims do not waste CPU on geometry we can't see.
    /// </summary>
    private async Task RefreshNeighborTerrainAsync(Simulator sim)
    {
        if (_viewport == null || _disposed) return;
        if (sim.Terrain == null) return;

        var currentSim = _instance.Client.Network.CurrentSim;
        if (currentSim == null) return;

        // Compute the region offset and skip if the entire neighbor region is beyond draw distance.
        Utils.LongToUInts(currentSim.Handle, out uint cx, out uint cy);
        Utils.LongToUInts(sim.Handle,        out uint sx, out uint sy);
        var regionOffset = new Vector3((int)sx - (int)cx, (int)sy - (int)cy, 0f);

        // Nearest point on the neighbor region's world-space footprint to the agent.
        // Clamp the agent's position to [regionOffset, regionOffset+255] on each axis.
        var agentPos = _instance.Client.Self.SimPosition;
        float nearestX = Math.Clamp(agentPos.X, regionOffset.X, regionOffset.X + 255f);
        float nearestY = Math.Clamp(agentPos.Y, regionOffset.Y, regionOffset.Y + 255f);
        float edgeDx = nearestX - agentPos.X;
        float edgeDy = nearestY - agentPos.Y;
        if (edgeDx * edgeDx + edgeDy * edgeDy > DrawDistance * DrawDistance)
            return; // entire neighbor region is beyond draw distance

        // Cancel any in-flight build for this sim and start a new one.
        CancellationToken ct;
        lock (_neighborTerrainLock)
        {
            if (_disposed) return;
            if (_neighborTerrainCts.TryGetValue(sim.Handle, out var old))
            {
                old.Cancel();
                old.Dispose();
            }
            var cts = new CancellationTokenSource();
            _neighborTerrainCts[sim.Handle] = cts;
            ct = cts.Token;

            if (!_neighborTerrainBuilders.TryGetValue(sim.Handle, out _))
                _neighborTerrainBuilders[sim.Handle] = new SceneTerrainBuilder(_instance.Client);
        }

        // Stable scene key for this neighbor's terrain, derived from the same collision-free
        // per-handle index SceneObjectStreamer uses for its own scene keys (previously this was
        // an anti-diagonal hash of grid coordinates that collided across cardinal neighbor pairs
        // -- e.g. N/E, S/W, NW/SE all hashed identically -- silently disposing/replacing one
        // neighbor's terrain with another's).
        // Upper 32 bits = the sim's stable, permanent index (assigned once, unrelated to
        // whether it's current -- see SceneNeighborSimIndex), lower 32 bits = uint.MaxValue
        // (reserved; real prims never use this LocalID).
        uint simIndex = _neighborIndex!.GetSimIndex(sim);
        ulong terrainKey = ((ulong)simIndex << 32) | uint.MaxValue;

        try
        {
            SceneTerrainBuilder builder;
            lock (_neighborTerrainLock)
                builder = _neighborTerrainBuilders[sim.Handle];

            var submission = await builder.RebuildAsync(sim, regionOffset, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || submission == null) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_viewport == null) return;
                _viewport.SubmitSceneObject(terrainKey, submission);

                // regionOffset above was captured against CurrentSim at the START of this
                // (potentially long-running, asset-fetch-bound) build. If a region crossing
                // landed while it was in flight, the just-submitted mesh is baked against the
                // OLD current sim's frame — correct it by the same delta
                // HandleLightweightRegionPromotion applies to every other already-tracked
                // neighbor's terrain, reusing the same rebase path (safe to call back-to-back
                // with the submit above: DrainPendingSceneObjects always runs before
                // ApplySceneRebases within a render frame, so this always finds a committed
                // entry to correct rather than silently no-op-ing against an uncommitted key).
                var currentSimNow = _instance.Client.Network.CurrentSim;
                if (currentSimNow != null && currentSimNow != currentSim)
                {
                    Utils.LongToUInts(currentSim.Handle,    out uint sox, out uint soy);
                    Utils.LongToUInts(currentSimNow.Handle, out uint snx, out uint sny);
                    var staleDelta = new Vector3((int)sox - (int)snx, (int)soy - (int)sny, 0f);
                    _viewport.RebaseSceneObjectTransforms(new[] { terrainKey }, staleDelta);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Non-fatal: neighbor terrain is a best-effort addition.
            System.Diagnostics.Debug.WriteLine($"Neighbor terrain error: {ex.Message}");
        }
    }

    private void CancelAllNeighborTerrainBuilds()
    {
        lock (_neighborTerrainLock)
        {
            foreach (var cts in _neighborTerrainCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _neighborTerrainCts.Clear();
            _neighborTerrainBuilders.Clear();
        }
    }

    // ── Object picking ────────────────────────────────────────────────────────────

    // The avatar scene-key offset used by SceneAvatarStreamer — picks with a localId
    // at or above this value are avatar hits; subtract to recover the real localId.
    private const uint AvatarKeyOffset = 0x8000_0000u;

    /// <summary>
    /// Called on the UI thread when a plain left-click picks a face -- real SL's touch
    /// gesture. Fires the touch packet only; no selection/highlight side effect (that's
    /// <see cref="OnObjectRightClicked"/>'s job now) and no avatar case, since avatars aren't
    /// touch-clicked in real SL.
    /// </summary>
    private void OnFaceClicked(uint sceneLocalId, int faceIndex, Radegast.Veles.Rendering.FaceHitInfo hit)
    {
        if (_disposed) return;
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null) return;
        if (sceneLocalId >= AvatarKeyOffset) return;

        // Convert OpenTK vectors to LibreMetaverse vectors for the packet.
        var uvCoord  = new LibreMetaverse.Vector3(hit.UvCoord.X,  hit.UvCoord.Y,  hit.UvCoord.Z);
        var stCoord  = new LibreMetaverse.Vector3(hit.StCoord.X,  hit.StCoord.Y,  hit.StCoord.Z);
        var position = new LibreMetaverse.Vector3(hit.Position.X, hit.Position.Y, hit.Position.Z);
        var normal   = new LibreMetaverse.Vector3(hit.Normal.X,   hit.Normal.Y,   hit.Normal.Z);
        var binormal = new LibreMetaverse.Vector3(hit.Binormal.X, hit.Binormal.Y, hit.Binormal.Z);
        _ = _instance.Client.Objects.ClickObjectAsync(
                sim, sceneLocalId, uvCoord, stCoord, faceIndex, position, normal, binormal);
    }

    /// <summary>
    /// Called on the UI thread when a plain right-click picks a face -- real SL's
    /// select-for-the-pie-menu gesture: updates selection state (and, for prims, the SL-style
    /// selection outline) without sending a touch packet.
    /// </summary>
    private void OnObjectRightClicked(uint sceneLocalId, int faceIndex, Radegast.Veles.Rendering.FaceHitInfo hit)
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
            // Avatar hit — show name if available. Self is tracked under a fixed sentinel
            // (SceneAvatarStreamer.SelfSceneId) rather than a real sim.ObjectsAvatars LocalID —
            // see that field's doc comment — so it needs its own name resolution here.
            if (realId == Radegast.Veles.Rendering.SceneAvatarStreamer.SelfSceneId)
            {
                var selfName = _instance.Client.Self.Name;
                SelectedInfo = string.IsNullOrEmpty(selfName) ? "You" : selfName;
                ContextLabel = SelectedInfo;
            }
            else if (sim.ObjectsAvatars.TryGetValue(realId, out var av))
            {
                SelectedInfo  = $"{av.Name}";
                ContextLabel  = av.Name;
            }
            else
            {
                SelectedInfo  = $"Avatar #{realId}";
                ContextLabel  = $"Avatar #{realId}";
            }
            ContextIsAvatar  = true;
            ContextIsPrim    = false;
            ContextIsSitting = _instance.Client.Self.SittingOn != 0;

            // SL doesn't build-highlight avatars on click — only prims get the
            // selection outline, so clear any outline left over from a prior prim pick.
            _viewport?.SetSelectedObject(0);
        }
        else
        {
            // Prim hit — show name/UUID.
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
            ContextIsAvatar  = false;
            ContextIsPrim    = true;
            ContextIsSitting = _instance.Client.Self.SittingOn != 0;
            _viewport?.SetSelectedObject(realId);

            // Request full properties so the name is populated for the context label.
            if (prim?.Properties == null)
                _instance.Client.Objects.SelectObject(sim, realId);
        }
    }

    /// <summary>Double-clicked empty ground: autopilot the avatar there, matching the minimap's walk-to.</summary>
    private void OnGroundClicked(Vector3 worldPos)
    {
        if (_disposed) return;
        _chat.WalkToPoint(worldPos.X, worldPos.Y);
        StatusText = $"Walking to ({worldPos.X:0}, {worldPos.Y:0})...";
    }

    /// <summary>
    /// Mirrors <see cref="ISceneViewport.MouselookActive"/> and switches the camera between
    /// third-person orbit and first-person mouselook. Only the view is first-person in this
    /// slice — avatar body rotation and movement direction still follow the third-person
    /// turn/strafe keys, not the look direction.
    /// </summary>
    private void OnMouselookChanged(bool active)
    {
        MouselookActive = active;
        if (_viewport == null) return;

        _viewport.Camera.MouselookMode = active;
        if (active)
        {
            var pos = _instance.Client.Self.SimPosition;
            _viewport.Camera.MouselookEye = new Vector3(pos.X, pos.Y, pos.Z + 1.0f);
            StatusText = "Mouselook (view only) — press Esc to exit";
        }
        else
        {
            CenterCameraOnAvatar();
        }
        _viewport.RequestRender();
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
            ? new Vector3(128f, 128f, sim.WaterHeight + 2f)
            : new Vector3(pos.X, pos.Y, pos.Z + 1.0f); // eye-level target

        // Derive the camera yaw from the avatar's current heading so it starts
        // directly behind the avatar (heading + 180°), matching SL's follow-camera.
        // Avatar faces +X at rest; SimRotation is a Z-up quaternion.
        var rot = _instance.Client.Self.SimRotation;
        float headingRad = MathF.Atan2(
            2f * (rot.W * rot.Z + rot.X * rot.Y),
            1f - 2f * (rot.Y * rot.Y + rot.Z * rot.Z));
        float cameraYaw = headingRad * (180f / MathF.PI) + 180f;

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
        var pos   = new LibreMetaverse.Vector3(eye.X, eye.Y, eye.Z);
        var atDir = new LibreMetaverse.Vector3(fwd.X, fwd.Y, fwd.Z);

        // Build a right-handed camera frame (SL: X=left, Y=at/forward, Z=up).
        // Up is always world-Z in the scene viewer.
        var worldUp = LibreMetaverse.Vector3.UnitZ;
        var left    = LibreMetaverse.Vector3.Cross(worldUp, atDir);
        if (left.LengthSquared() < 1e-6f)
        {
            // Camera is looking straight up/down — fall back to Y as up reference.
            left = LibreMetaverse.Vector3.Cross(LibreMetaverse.Vector3.UnitY, atDir);
        }
        left = LibreMetaverse.Vector3.Normalize(left);
        var up = LibreMetaverse.Vector3.Cross(atDir, left);
        up = LibreMetaverse.Vector3.Normalize(up);

        var mv = _instance.Client.Self.Movement;
        mv.Camera.Position = pos;
        mv.Camera.LookDirection(atDir, up);
        mv.Camera.Far = DrawDistance;

        // Keep the FMOD audio listener in sync with the scene camera so sounds
        // pan and attenuate relative to where the player is looking, not where
        // the avatar body is pointing.
        _instance.MediaManager?.SetCameraListenerState(pos, atDir, up);
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

        if (MouselookActive)
        {
            // In mouselook the user's mouse drives Yaw/Pitch directly (see
            // VkViewportControl.OnPointerMoved) — just keep the fixed eye position
            // tracking the avatar, don't auto-rotate yaw to face heading.
            var eye = new Vector3(pos.X, pos.Y, pos.Z + 1.0f);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_viewport != null) { _viewport.Camera.MouselookEye = eye; _viewport.RequestRender(); }
            });
            return;
        }

        // Compute the avatar's current heading in degrees.
        var rot = _instance.Client.Self.SimRotation;
        float headingRad = MathF.Atan2(
            2f * (rot.W * rot.Z + rot.X * rot.Y),
            1f - 2f * (rot.Y * rot.Y + rot.Z * rot.Z));
        float headingDeg = headingRad * (180f / MathF.PI);

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
        _viewport.UpdateCameraFollow(new Vector3(pos.X, pos.Y, pos.Z + 1.0f));
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
                OmVector3.Zero, OmVector3.Zero, 0,
                OmVector3.Zero, OmVector3.Zero, OmVector3.Zero);
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
        _instance.Client.Self.RequestSit(prim.ID, OmVector3.Zero);
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

        var dest = av.Position + new OmVector3(2f, 0f, 0f); // land next to, not on top of
        _ = _instance.Client.Self.TeleportAsync(sim.Handle, dest, _instance.Client.Self.SimPosition);
    }

    /// <summary>
    /// Request the full properties of the selected prim (triggers ObjectProperties event).
    /// </summary>
    [RelayCommand(CanExecute = nameof(ContextIsPrim))]
    private void InspectObject()
    {
        // Diagnostic logging added 2026-09-05 (kept, not removed): OnObjectProperties below
        // silently discards a reply that doesn't resolve or match, with zero visible feedback
        // either way, so a future "Inspect does nothing" report has nothing to go on without
        // this. Logging both ends (request sent vs. bailed here, reply matched vs. discarded
        // there) is what found the real bug this same investigation fixed (this method was
        // calling RequestObject, which never prompts a reply at all -- see below) and separately
        // showed a specific object's server-side properties reply never arriving even with the
        // correct request, which is likely unfixable from this side.
        if (_disposed)
        {
            LibreMetaverse.Logger.Debug("[SceneViewerViewModel] InspectObject: no-op, disposed");
            return;
        }
        var sim = _instance.Client.Network.CurrentSim;
        if (sim == null || SelectedLocalId == 0)
        {
            LibreMetaverse.Logger.Debug(
                $"[SceneViewerViewModel] InspectObject: no-op, sim={(sim == null ? "null" : "ok")}, SelectedLocalId={SelectedLocalId}");
            return;
        }
        // RequestObject (RequestMultipleObjectsPacket) is for streaming an object's geometry/data
        // into view, NOT its properties -- it never prompts an ObjectProperties reply. SelectObject
        // (ObjectSelectPacket) is what the server actually replies to; OnFaceClicked already uses
        // it for exactly the same reason ("Request full properties so the name is populated"), a
        // few hundred lines up in this same file. This was the real bug behind "Inspect does
        // nothing": the request this command sent could never have gotten a reply, for ANY object,
        // regardless of whether that object's own data was otherwise fine.
        LibreMetaverse.Logger.Debug($"[SceneViewerViewModel] InspectObject: requesting properties for localId={SelectedLocalId}");
        _instance.Client.Objects.SelectObject(sim, SelectedLocalId);
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
                mv.BodyRotation *= LibreMetaverse.Quaternion.CreateFromAxisAngle(
                    LibreMetaverse.Vector3.UnitZ, TurnDelta);
            else if (turnRight)
                mv.BodyRotation *= LibreMetaverse.Quaternion.CreateFromAxisAngle(
                    LibreMetaverse.Vector3.UnitZ, -TurnDelta);
        }

        mv.SendUpdate();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── In-viewport chat ──────────────────────────────────────────────────────────
    // Sends delegate to the shared NearbyViewModel.ProcessChatInput so gesture/slash-command/
    // RLV handling is not duplicated; only the read/write of ChatInput and the input box's
    // visibility are local to the scene viewer.

    [RelayCommand]
    private void ToggleChatInput() => ChatInputVisible = !ChatInputVisible;

    [RelayCommand]
    private void SendChatFromViewport() => SendFromViewport(LibreMetaverse.ChatType.Normal);

    [RelayCommand]
    private void SendWhisperFromViewport() => SendFromViewport(LibreMetaverse.ChatType.Whisper);

    [RelayCommand]
    private void SendShoutFromViewport() => SendFromViewport(LibreMetaverse.ChatType.Shout);

    private void SendFromViewport(LibreMetaverse.ChatType type)
    {
        if (string.IsNullOrEmpty(ChatInput)) return;
        _chat.NotifyTypingStopped();
        _chat.ProcessChatInput(ChatInput, type);
        ChatInput = string.Empty;
    }

    /// <summary>Recalls the previous entry from the shared chat history (Ctrl+Up).</summary>
    public void ChatHistoryPrevInViewport()
    {
        _chat.ChatHistoryPrev();
        ChatInput = _chat.ChatInput;
    }

    /// <summary>Recalls the next entry from the shared chat history (Ctrl+Down).</summary>
    public void ChatHistoryNextInViewport()
    {
        _chat.ChatHistoryNext();
        ChatInput = _chat.ChatInput;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _instance.Client.Terrain.LandPatchReceived        -= OnLandPatchReceived;
        _instance.Client.Network.SimChanged               -= OnSimChanged;
        _instance.Client.Network.SimConnected             -= OnSimConnected;
        _instance.Client.Network.SimDisconnected          -= OnSimDisconnected;
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
            _viewport.FaceClicked      -= OnFaceClicked;
            _viewport.ObjectRightClicked -= OnObjectRightClicked;
            _viewport.SceneReset       -= OnSceneReset;
            _viewport.GroundClicked    -= OnGroundClicked;
            _viewport.MouselookChanged -= OnMouselookChanged;
            _viewport.Stats.FrameCompleted -= OnFrameCompleted;
        }

        if (_nameTagService != null)
        {
            _nameTagService.TagsUpdated      -= OnNameTagsUpdated;
            _nameTagService.HoverTagsUpdated -= OnHoverTagsUpdated;
        }

        // Release any held movement keys so the avatar doesn't keep moving.
        StopMoveTimer();

        _cameraSyncTimer?.Dispose();
        _cameraSyncTimer = null;
        _instance.MediaManager?.ClearCameraListenerState();
        if (_instance.Client.Network.Connected)
        {
            var mv = _instance.Client.Self.Movement;
            mv.AtPos = mv.AtNeg = mv.LeftPos = mv.LeftNeg = mv.UpPos = mv.UpNeg = false;
            mv.TurnLeft = mv.TurnRight = false;

            // Undo SyncCameraToServer's continuous overrides of the agent's reported camera.
            // Without this, Position/LookDirection stay frozen wherever the free-look viewport
            // last pointed and Far stays at DrawDistance instead of LibreMetaverse's own default
            // (128m, AgentCamera's constructor), indefinitely after this tab closes. The
            // simulator uses that reported camera to help decide which neighbor regions to
            // keep connected as child agents — leaving it stuck correlates with the WebRTC
            // voice layer (which reacts purely to Client.Network.Simulators membership)
            // reprovisioning around Scene Viewer open/close.
            var pos = _instance.Client.Self.SimPosition;
            var rot = _instance.Client.Self.SimRotation;
            float headingRad = MathF.Atan2(
                2f * (rot.W * rot.Z + rot.X * rot.Y),
                1f - 2f * (rot.Y * rot.Y + rot.Z * rot.Z));
            mv.Camera.Position = pos;
            mv.Camera.LookDirection(new OmVector3(MathF.Cos(headingRad), MathF.Sin(headingRad), 0f));
            mv.Camera.Far = 128f;

            mv.SendUpdate();
        }

        lock (_terrainCtsLock)
        {
            _terrainCts?.Cancel();
            _terrainCts?.Dispose();
            _terrainCts = null;
        }
        CancelAllNeighborTerrainBuilds();

        _objectStreamer?.Dispose();
        _objectStreamer = null;

        _avatarStreamer?.Dispose();
        _avatarStreamer = null;
        _avatarRenderInfoReporter?.Dispose();
        _avatarRenderInfoReporter = null;

        _buildScheduler?.Dispose();
        _buildScheduler = null;

        _particleStreamer?.Dispose();
        _particleStreamer = null;

        _lightStreamer?.Dispose();
        _lightStreamer = null;

        _flexiStreamer?.Dispose();
        _flexiStreamer = null;

        _avatarAnimStreamer?.Dispose();
        _avatarAnimStreamer = null;

        _animeshStreamer?.Dispose();
        _animeshStreamer = null;

        _nameTagService?.Dispose();
        _nameTagService = null;

        _envService?.Dispose();
        _envService = null;

        // Release all decoded SKBitmaps from the GL texture cache — the scene is going
        // away so the RAM is no longer needed.
        GridTextureHelper.ClearSkBitmapCache();

        _viewport = null;
    }

    // ── GL context lifecycle ──────────────────────────────────────────────────────

    // Called on the UI thread by the viewport (VkViewportControl) via its SceneReset event
    // after every successful init, including tab-switch re-attaches. Avalonia's TabControl
    // detaches the visual tree on tab switch, which destroys the GPU context and frees all
    // GPU scene data. When the user returns to the SceneViewer tab, init fires again — but
    // the streamers' _rendered sets still think all objects are uploaded. Re-dirtying
    // everything triggers a full re-upload into the fresh GPU context.
    private void OnSceneReset()
    {
        if (_disposed) return;

        // Re-dirty everything that was rendered before the GPU context teardown.
        _objectStreamer?.RebuildAllRendered();
        _avatarStreamer?.RebuildAllRendered();

        // Safety net: if _rendered was cleared (e.g. by a sim-change event that fired
        // while the SceneViewer tab was hidden), RebuildAllRendered above would enqueue
        // nothing. SeedStreamersFromCurrentSim covers those objects. Both paths key on
        // rootId in _dirty (a ConcurrentDictionary), so duplicates are simply
        // overwritten — the scheduler never sees duplicate entries.
        // Backgrounded: this method itself runs on the UI thread (per its own header comment),
        // so the seed walk must not run inline on it -- same reasoning as OnSimChanged's Task.Run.
        _ = Task.Run(() =>
        {
            SeedStreamersFromCurrentSim();
            SeedNeighborSims();
        });

        // Terrain geometry was disposed with the old GL context.  Rebuild it now.
        _ = RefreshTerrainAsync();
    }

    // ── Frame-stats callback ──────────────────────────────────────────────────────

    private long _lastPerfOverlayLogTicks;

    // Called on the GL thread by FrameStatsTracker; marshal to UI thread for binding.
    private void OnFrameCompleted(FrameStats stats)
    {
        if (_disposed) return;

        // Snapshot in-flight counters on the calling thread — all reads are O(1) atomic/lock-free.
        int buildQueue   = _buildScheduler?.QueueCount          ?? 0;
        int objBuilds    = _objectStreamer?.InflightCount        ?? 0;
        int avBuilds     = _avatarStreamer?.InflightCount        ?? 0;
        int pendingUpl   = _viewport?.PendingSceneUploadCount   ?? 0;
        int texDecodes   = GridTextureHelper.InflightDecodeCount;
        int patchQueue   = _viewport?.QueuedTexturePatchCount   ?? 0;
        int patchDeferred = _viewport?.DeferredTexturePatchCount ?? 0;
        int sceneFaces   = _viewport?.SceneFaceCount            ?? 0;
        double drainObj  = _viewport?.DrainSceneObjectsMs      ?? 0;
        double drainPatch = _viewport?.DrainTexturePatchesMs    ?? 0;
        double skinMs    = _viewport?.SkinDispatchMs            ?? 0;
        double flexiMs   = _viewport?.FlexiDispatchMs           ?? 0;
        double vertUpdMs = _viewport?.VertexUpdateDrainMs       ?? 0;
        double recordMs  = _viewport?.MainPassRecordMs          ?? 0;
        double submitMs  = _viewport?.MainPassSubmitWaitMs      ?? 0;
        double preCullMs = _viewport?.PreCullMs                 ?? 0;
        double subPassCumMs = _viewport?.SubPassMs              ?? 0;
        double subPassMs = subPassCumMs - preCullMs;
        double mainDrawMs = recordMs - subPassCumMs;
        double particleDrainMs = _viewport?.ParticleDrainMs     ?? 0;
        double beginDrawCumMs = _viewport?.BeginDrawMs          ?? 0;
        double beginDrawMs = beginDrawCumMs - particleDrainMs;
        double depthCullMs = preCullMs - beginDrawCumMs;
        double swapFreeMs = _viewport?.SwapchainFreeCmdBuffersMs ?? 0;
        double swapAcquireMs = _viewport?.SwapchainBeginDrawCoreMs ?? 0;
        double submitCallMs = _viewport?.SubmitCallMs ?? 0;
        double fenceWaitMs = _viewport?.FenceWaitMs ?? 0;
        int liveInstances = _viewport?.LiveInstanceCount ?? 0;

        var text = $"CPU {stats.CpuTimeMs:F1} ms" +
                   (stats.GpuTimeMs > 0 ? $"  GPU {stats.GpuTimeMs:F1} ms" : string.Empty) +
                   // Frame-interval variance (plan Step 3) -- the metric that actually answers
                   // "is it choppy," since CPU/GPU ms alone can drop once frame-in-flight
                   // pipelining (Step 6) lands whether or not real frame pacing improves.
                   $"  Interval max:{stats.IntervalMaxMs:F1}ms p99:{stats.IntervalP99Ms:F1}ms" +
                   $"\nDraws {stats.DrawCalls}  Tris {stats.Triangles:#,0}" +
                   $"\nFaces {stats.FacesSubmitted}  Culled {stats.FacesCulled}" +
                   $"\nBuild Q:{buildQueue}  Obj:{objBuilds}  Av:{avBuilds}" +
                   $"\nUpload Q:{pendingUpl}  TexDec:{texDecodes}" +
                   $"\nPatches Q:{patchQueue}  Deferred:{patchDeferred}  SceneFaces:{sceneFaces:#,0}" +
                   $"\nDrain SObj:{drainObj:F1}ms Patch:{drainPatch:F1}ms" +
                   $"\nDeformer Skin:{skinMs:F1}ms Flexi:{flexiMs:F1}ms VertexUpd:{vertUpdMs:F1}ms" +
                   $"\nRecord:{recordMs:F1}ms SubmitWait:{submitMs:F1}ms(Call:{submitCallMs:F1}ms Fence:{fenceWaitMs:F1}ms) Panels:{liveInstances}" +
                   $"\nPreCull:{preCullMs:F1}ms SubPass:{subPassMs:F1}ms MainDraw:{mainDrawMs:F1}ms" +
                   $"\nParticles:{particleDrainMs:F1}ms BeginDraw:{beginDrawMs:F1}ms(Free:{swapFreeMs:F1}ms Acquire:{swapAcquireMs:F1}ms) DepthCull:{depthCullMs:F1}ms" +
                   $"\nMeshCache {PrimMeshBuilder.MeshCacheHits}h/{PrimMeshBuilder.MeshCacheMisses}m";

        if (!ShowPerfOverlay) return;

        // Throttled to 1/sec, and only while the overlay is actually being watched.
        long nowTicks = Environment.TickCount64;
        if (nowTicks - _lastPerfOverlayLogTicks >= 1000)
        {
            _lastPerfOverlayLogTicks = nowTicks;
            LibreMetaverse.Logger.Debug("[SceneViewerViewModel] Perf: " + text.Replace('\n', ' '));
        }

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
    private void OnChatReceived(object? sender, LibreMetaverse.ChatEventArgs e)
    {
        if (_disposed) return;
        if (e.Type is LibreMetaverse.ChatType.StartTyping or LibreMetaverse.ChatType.StopTyping) return;
        if (e.SourceType == LibreMetaverse.ChatSourceType.System && string.IsNullOrWhiteSpace(e.Message)) return;

        var lineType = e.SourceType switch
        {
            LibreMetaverse.ChatSourceType.Agent when e.FromName == _instance.Client.Self.Name => ChatLineType.Self,
            LibreMetaverse.ChatSourceType.Agent => ChatLineType.Normal,
            LibreMetaverse.ChatSourceType.Object => ChatLineType.Object,
            _ => ChatLineType.System
        };

        string prefix = e.Type == LibreMetaverse.ChatType.Shout   ? " shouts" :
                        e.Type == LibreMetaverse.ChatType.Whisper  ? " whispers" : "";
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
