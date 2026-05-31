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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using Radegast.Veles.Core;
using Radegast.Veles.Rendering;
using TkMatrix4    = OpenTK.Mathematics.Matrix4;
using TkVector3    = OpenTK.Mathematics.Vector3;
using TkVector4    = OpenTK.Mathematics.Vector4;
using TkQuaternion = OpenTK.Mathematics.Quaternion;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// ViewModel for the 3D avatar viewer.
/// Supports both the local agent (self) and any other avatar in the current region.
/// Subscribes to appearance and attachment-change events to keep the view current.
/// </summary>
public partial class AvatarViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private readonly UUID              _avatarId;
    private readonly bool              _isSelf;
    private readonly AvatarMeshBuilder _builder;

    private GlViewportControl?        _viewport;
    private CancellationTokenSource?  _cts;
    private Timer?                    _debounceTimer;
    private volatile uint             _avatarLocalId;
    private PrimRenderSubmission?     _lastSubmission;
    private bool                      _firstLoad = true;
    private bool                      _disposed;
    // Set for the entire duration of a LoadAsync call (including streaming).
    // Event handlers that would trigger a reload instead set _pendingReload so
    // that the reload is deferred until after the current build completes.
    private volatile bool             _buildInProgress;
    private volatile bool             _pendingReload;

    private AvatarFaceSkinData[]?              _skinData;
    private LindenAvatarDefinition?            _avatarDef;
    private Dictionary<string, BoneTransform>? _vpBoneTransforms;
    private Dictionary<string, BoneTransform>? _fittedBoneTransforms;
    private Dictionary<string, TkMatrix4>?     _invBindMatrices;
    private AvatarAnimationPlayer?             _animPlayer;
    private CancellationTokenSource?           _animCts;
    private ImmutableArray<AvatarFaceMorphData> _faceMorphData = ImmutableArray<AvatarFaceMorphData>.Empty;

    // ── Physics wearable simulation ───────────────────────────────────────────────
    private readonly AvatarPhysicsSimulator    _physics = new();
    private float                              _physicsElapsed;
    private IReadOnlyDictionary<int, float>    _physicsVpWeights = new Dictionary<int, float>();

    // ── LOD management ────────────────────────────────────────────────────────────
    // LOD is selected based on the avatar's on-screen pixel height.
    // MinPixelWidth thresholds from avatar_lad.xml:  lod0≥320, lod1≥160, lod2≥80.
    private int _currentLod = 0;
    // Projected screen-height thresholds for LOD selection (pixels).
    // Thresholds match avatar_lad.xml: lod0 ≥ 320 px, lod1 ≥ 160 px, lod2 ≥ 80 px.
    // Driven by Camera3D.ComputeProjectedPixelHeight so zoom/distance changes
    // trigger LOD switches without requiring a panel resize.
    private static int PixelHeightToLod(double pixelHeight) => pixelHeight switch
    {
        >= 320 => 0,
        >= 160 => 1,
        >= 80  => 2,
        _      => 3,
    };
    // Counter used to throttle the LOD check inside AnimTick to ~3 Hz.
    private int _lodTickCounter;

    private readonly ConcurrentDictionary<uint, byte> _knownAttachmentIds = new();
    // Tracks whether the static VP pose (no animation) has already been applied so
    // AnimTick can skip recomputation each tick when nothing is changing.
    // -1 = not yet applied; 0 = applied with no deltas; >0 = animation was playing.
    private int _prevLiveDeltaCount = -1;
    private volatile bool _forceSkinUpdate;

    // Pre-allocated bone matrix buffers reused across AnimTick frames to avoid
    // per-frame Dictionary allocations in ComputeAnimatedBoneWorldMatrices.
    private Dictionary<string, TkMatrix4> _animBonesBuffer   = new(StringComparer.Ordinal);
    private Dictionary<string, TkMatrix4> _vpAnimBonesBuffer = new(StringComparer.Ordinal);
    // Attachment-flavour bones (no VP scale propagation through the hierarchy) —
    // used to keep flexi attachment prims aligned with the static AttachTransform
    // built at mesh-build time.
    private Dictionary<string, TkMatrix4> _attachBonesBuffer = new(StringComparer.Ordinal);

    private ParticleViewerDriver? _particles;
    private FlexiPrimAnimator?    _flexi;

    // ── Static A-pose rotation deltas ────────────────────────────────────────────
    // Arm bones extend along ±Y (lateral axis); rotating around local +X by -θ swings
    // the +Y arm direction toward -Z (floor), producing the SL reference A-pose.
    // Right-arm bones need the opposite sign to also drop toward -Z.
    private static readonly IReadOnlyDictionary<string, TkQuaternion> s_aPoseDeltas =
        new Dictionary<string, TkQuaternion>(StringComparer.Ordinal)
        {
            ["mCollarLeft"]    = TkQuaternion.FromEulerAngles(-4f  * MathF.PI / 180f, 0f, 0f),
            ["mShoulderLeft"]  = TkQuaternion.FromEulerAngles(-39f * MathF.PI / 180f, 0f, 0f),
            ["mCollarRight"]   = TkQuaternion.FromEulerAngles( 4f  * MathF.PI / 180f, 0f, 0f),
            ["mShoulderRight"] = TkQuaternion.FromEulerAngles( 39f * MathF.PI / 180f, 0f, 0f),
        };

    [ObservableProperty] private string _avatarName  = "Loading…";
    [ObservableProperty] private string _statusText  = string.Empty;
    [ObservableProperty] private bool   _isLoading   = true;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private string _errorText   = string.Empty;
    [ObservableProperty] private bool   _wireframe;
    [ObservableProperty] private bool   _ssaoEnabled;
    [ObservableProperty] private AvatarPoseMode _poseMode = AvatarPoseMode.LiveAnimation;

    /// <summary>
    /// Integer index into the pose-mode ComboBox (order matches <see cref="AvatarPoseMode"/> values).
    /// Exists so the AXAML ComboBox can use <c>SelectedIndex</c> without a converter.
    /// </summary>
    public int PoseModeIndex
    {
        get => (int)PoseMode;
        set => PoseMode = (AvatarPoseMode)value;
    }

    /// <summary>Short hint shown in the status bar reflecting the active pose mode.</summary>
    public string PoseHintText => PoseMode switch
    {
        AvatarPoseMode.TPose         => "T-pose · shape driven by visual parameters",
        AvatarPoseMode.APose         => "A-pose · shape driven by visual parameters",
        AvatarPoseMode.LiveAnimation => "Live animation · 30 Hz",
        _                            => string.Empty,
    };

    partial void OnWireframeChanged(bool value)
    {
        if (_viewport != null) _viewport.Wireframe = value;
    }

    partial void OnSsaoEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.SsaoEnabled = value;
    }

    partial void OnPoseModeChanged(AvatarPoseMode value)
    {
        OnPropertyChanged(nameof(PoseModeIndex));
        OnPropertyChanged(nameof(PoseHintText));
        // Force one LBS recompute when switching pose modes so fitted attachments
        // do not keep the previously uploaded vertex buffer.
        _prevLiveDeltaCount = -1;
        _forceSkinUpdate = true;
    }

    public AvatarViewerViewModel(RadegastInstanceAvalonia instance, UUID avatarId)
    {
        _instance = instance;
        _avatarId = avatarId;
        _ssaoEnabled = instance.GlobalSettings["ssao_enabled"].Type != OpenMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["ssao_enabled"].AsBoolean() : true;
        _isSelf   = avatarId == Client.Self.AgentID;
        _builder  = new AvatarMeshBuilder(Client);

        UpdateAvatarName();

        _debounceTimer = new Timer(OnDebounceElapsed, null,
            Timeout.Infinite, Timeout.Infinite);

        if (_isSelf)
            Client.Appearance.AppearanceSet += OnSelfAppearanceSet;
        Client.Avatars.AvatarAppearance += OnAvatarAppearance;
        Client.Objects.ObjectUpdate     += OnObjectUpdate;
        Client.Objects.KillObject       += OnKillObject;
        Client.Avatars.AvatarAnimation  += OnAvatarAnimation;

        _animPlayer = new AvatarAnimationPlayer(Client);
        _animCts    = new CancellationTokenSource();
        _ = AnimationLoopAsync(_animCts.Token);

        _cts = new CancellationTokenSource();
        _ = LoadAsync(_cts.Token);
    }

    /// <summary>
    /// Attach the GL viewport so the VM can submit geometry to it.
    /// Call from the view's code-behind once the visual tree is ready.
    /// </summary>
    public void SetViewport(GlViewportControl viewport)
    {
        if (_viewport != null) _viewport.FaceClicked -= OnFaceClicked;
        _viewport           = viewport;
        _viewport.Wireframe  = Wireframe;
        _viewport.SsaoEnabled = SsaoEnabled;
        _viewport.FaceClicked += OnFaceClicked;

        // A fresh viewport always needs SubmitAvatarFront so the camera gets framed,
        // regardless of whether loading already completed (_firstLoad is false).
        if (_lastSubmission != null)
        {
            _viewport.SubmitAvatarFront(_lastSubmission);

            // (Re)start flexi-prim animation if it was not started during LoadAsync
            // because the viewport was not yet attached at that time.
            if (_flexi == null && _lastSubmission.FlexiPrims.Length > 0)
            {
                var vp = _viewport;
                _flexi = new FlexiPrimAnimator(_lastSubmission, vp.ScheduleVertexUpdate);
                _flexi.Start();
            }
        }

        _particles?.SetViewport(viewport);

        // LOD is driven by Camera3D.ComputeProjectedPixelHeight inside AnimTick
        // so that zoom / orbit changes are reflected at ~3 Hz without needing a resize.
        // A resize still resets the tick counter so the next tick rechecks immediately.
        _viewport.SizeChanged += (_, _) => _lodTickCounter = 0;
    }

    private void OnViewportBoundsChanged(double pixelHeight)
    {
        // LOD switching is handled inside AnimTick via projected pixel height.
        // This method is kept for any future direct callers.
        _lodTickCounter = 0;
    }

    private void OnFaceClicked(uint primLocalId, int faceIndex, FaceHitInfo hit)
    {
        var sim = Client.Network.CurrentSim;
        string primLabel;
        if (primLocalId == _avatarLocalId)
        {
            primLabel = "avatar body";
        }
        else if (sim != null && sim.ObjectsPrimitives.TryGetValue(primLocalId, out var clickedPrim))
        {
            var name = clickedPrim.Properties?.Name;
            primLabel = string.IsNullOrWhiteSpace(name) ? "attachment" : $"\"{name}\"";
        }
        else
        {
            primLabel = $"prim {primLocalId}";
        }
        StatusText = $"Touched face {faceIndex} of {primLabel}.";
        _ = GrabFaceAsync(primLocalId, faceIndex, hit);
    }

    private async Task GrabFaceAsync(uint localId, int faceIndex, FaceHitInfo hit)
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        static OpenMetaverse.Vector3 ToOmv(OpenTK.Mathematics.Vector3 v) => new(v.X, v.Y, v.Z);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Client.Objects.ClickObjectAsync(
                sim, localId,
                ToOmv(hit.UvCoord),
                ToOmv(hit.StCoord),
                faceIndex,
                ToOmv(hit.Position),
                ToOmv(hit.Normal),
                ToOmv(hit.Binormal),
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand] private void ResetCamera()  => _viewport?.ResetCamera();
    [RelayCommand] private void OrbitLeft()    => _viewport?.OrbitStep(-15f,   0f);
    [RelayCommand] private void OrbitRight()   => _viewport?.OrbitStep( 15f,   0f);
    [RelayCommand] private void OrbitUp()      => _viewport?.OrbitStep(  0f, -10f);
    [RelayCommand] private void OrbitDown()    => _viewport?.OrbitStep(  0f,  10f);
    [RelayCommand] private void ZoomIn()       => _viewport?.ZoomStep( 1.5f);
    [RelayCommand] private void ZoomOut()      => _viewport?.ZoomStep(-1.5f);

    [RelayCommand]
    private void Refresh()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = LoadAsync(_cts.Token);
    }

    // ── Loading pipeline ──────────────────────────────────────────────────────────

    private async Task LoadAsync(CancellationToken ct)
    {
        _buildInProgress = true;
        _pendingReload   = false;
        IsLoading = true;
        HasError  = false;

        try
        {
            var sim = Client.Network.CurrentSim
                ?? throw new InvalidOperationException("Not connected to a simulator.");

            uint localId = FindAvatarLocalId(sim);
            if (localId == 0)
                throw new InvalidOperationException("Avatar not found in current region.");

            _avatarLocalId = localId;
            UpdateAvatarName();

            // Seed the animation player with whatever is already playing — the
            // AvatarAnimation event only fires on changes, so the stand/idle
            // animation active before the viewer opened would otherwise be missed.
            if (_isSelf)
            {
                _animPlayer?.SetActiveAnimations(
                    Client.Self.SignaledAnimations.Keys.ToList());
            }
            else
            {
                var av = sim.ObjectsAvatars.Values.FirstOrDefault(a => a?.ID == _avatarId);
                if (av?.Animations != null)
                    _animPlayer?.SetActiveAnimations(
                        av.Animations.Select(a => a.AnimationID));
            }

            if (_isSelf)
                await EnsureWearableAssetsLoadedAsync(ct).ConfigureAwait(false);

            var visualParams = GetVisualParams(sim);

            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => StatusText = msg));

            bool wasFirst  = _firstLoad;
            bool greyShown = false;

            // onGeometryReady fires after Phase 1 (morph) completes but before the
            // texture download and attachment builds start, so the user sees a grey
            // body shell immediately instead of a blank window for the whole load.
            // On reloads (wasFirst == false) we skip the grey shell and keep the
            // previous textured submission visible until the new one is ready.
            Action<PrimRenderSubmission> onGeometryReady = greySub =>
            {
                greyShown = true;
                if (!wasFirst) return;   // keep showing the last textured state on reloads
                Dispatcher.UIThread.Post(() =>
                {
                    if (_viewport == null || ct.IsCancellationRequested) return;
                    _lastSubmission = greySub;
                    _viewport.SubmitAvatarFront(greySub);
                    // Do NOT set _firstLoad = false here; keep it true until the full
                    // build (including texture streaming) completes so that appearance
                    // events during the download phase do not trigger a premature reload.
                });
            };

            var result = await _builder.BuildAsync(
                localId, visualParams, AvatarName, progress, ct, _currentLod,
                onGeometryReady,
                texturePatch: new Progress<SceneTexturePatch>(patch =>
                {
                    // Drop patches that belong to a superseded build.  When a reload is
                    // triggered, _cts is cancelled before the new LoadAsync starts, so
                    // any streaming callbacks still in-flight from the old BuildAsync will
                    // see ct.IsCancellationRequested == true and silently discard the
                    // bitmap instead of forwarding it to the viewport — preventing stale
                    // patches from overwriting fresh geometry or leaking SKBitmaps.
                    if (ct.IsCancellationRequested) { patch.Bitmap?.Dispose(); return; }
                    _viewport?.PatchSubmissionTexture(patch);
                })).ConfigureAwait(false);

            var submission = result.Submission;
            _lastSubmission = submission;

            // Pre-populate known attachment IDs so OnKillObject can detect detaches
            // that arrive before the next AvatarAppearance event.
            _knownAttachmentIds.Clear();
            var attachments = new List<Primitive>();
            foreach (var p in sim.ObjectsPrimitives.Values)
            {
                if (p != null && p.ParentID == localId)
                {
                    _knownAttachmentIds.TryAdd(p.LocalID, 0);
                    attachments.Add(p);
                }
            }

            // (Re)start particle simulation for any emitter attachments.
            _particles?.Dispose();
            if (attachments.Count > 0)
            {
                _particles = new ParticleViewerDriver(Client, attachments,
                    (ulong)localId, OpenTK.Mathematics.Vector3.Zero);
                if (_viewport != null) _particles.SetViewport(_viewport);
                _particles.Start();
            }
            else
            {
                _particles = null;
            }

            // (Re)start flexi-prim animation for any flexi attachments.
            _flexi?.Dispose();
            if (result.Submission.FlexiPrims.Length > 0 && _viewport != null)
            {
                var vp = _viewport;
                _flexi = new FlexiPrimAnimator(result.Submission, vp.ScheduleVertexUpdate);
                _flexi.Start();
            }
            else
            {
                _flexi = null;
            }

            Dispatcher.UIThread.Post(() =>
            {
                // Guard: if a newer build was started while this one was finishing,
                // discard this stale submission so it cannot overwrite fresh geometry.
                if (ct.IsCancellationRequested) return;
                if (_viewport != null)
                {
                    // On first load without a grey preview shown, frame the camera.
                    // On reloads, simply swap the geometry without re-framing.
                    if (wasFirst && !greyShown)
                        _viewport.SubmitAvatarFront(submission);
                    else
                        _viewport.Submit(submission);
                }
                _firstLoad       = false;
                _buildInProgress = false;
                // Defer any reload that was triggered by an event during the build.
                if (_pendingReload)
                {
                    _pendingReload = false;
                    ScheduleReload();
                }
                // Update skin/anim fields only after the new submission has been
                // queued for upload.  FreeGpuResources() inside UploadSubmission
                // drains any stale _pendingVertexUpdates, so AnimTick writes from
                // here onward will land on the correct new _faceMeshes.
                _skinData             = result.SkinData;
                _faceMorphData        = result.FaceMorphData;
                _prevLiveDeltaCount   = -1;
                _avatarDef            = result.AvatarDef;
                _vpBoneTransforms     = result.BoneTransforms;
                _fittedBoneTransforms = result.FittedBoneTransforms;
                _physicsVpWeights     = visualParams;
                _physics.SetWearableParams(visualParams);
                _physicsElapsed       = 0f;
                _invBindMatrices      = result.TposeBoneWorldMatrices != null
                    ? result.TposeBoneWorldMatrices.ToDictionary(
                        kv => kv.Key,
                        kv => TkMatrix4.Invert(kv.Value))
                    : null;
                int attachCount = CountAttachments(sim, localId);
                StatusText = $"Ready — {submission.Faces.Length} face(s), {attachCount} attachment(s).";
                IsLoading  = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Normal on debounce cancel or disposal.
            _buildInProgress = false;
        }
        catch (Exception ex)
        {
            _buildInProgress = false;
            Dispatcher.UIThread.Post(() =>
            {
                HasError   = true;
                ErrorText  = ex.Message;
                StatusText = $"Error: {ex.Message}";
                IsLoading  = false;
            });
        }
    }

    private uint FindAvatarLocalId(Simulator sim)
    {
        if (_isSelf && Client.Self.LocalID != 0)
            return Client.Self.LocalID;
        var av = sim.ObjectsAvatars.Values.FirstOrDefault(a => a?.ID == _avatarId);
        return av?.LocalID ?? 0;
    }

    /// <summary>
    /// Downloads any wearable assets that are registered but not yet fetched.
    /// Mirrors the download loop in AppearanceManager.Baking.cs so that
    /// <see cref="AppearanceManager.GetCurrentParamValues"/> returns the actual
    /// worn wearable values instead of <see cref="VisualParamEx.DefaultValue"/>.
    /// </summary>
    private async Task EnsureWearableAssetsLoadedAsync(CancellationToken ct)
    {
        var wearables = Client.Appearance.GetWearables()
            .Where(w => w.Asset == null && w.AssetID != UUID.Zero)
            .ToList();

        if (wearables.Count == 0) return;

        var tasks = wearables.Select(wearable => Task.Run(async () =>
        {
            var tcs = new TaskCompletionSource<OpenMetaverse.Assets.Asset?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Client.Assets.RequestAsset(wearable.AssetID, wearable.AssetType, true,
                (_, asset) => tcs.TrySetResult(asset));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            linked.Token.Register(() => tcs.TrySetCanceled());

            OpenMetaverse.Assets.Asset? asset;
            try   { asset = await tcs.Task.ConfigureAwait(false); }
            catch { return; }

            if (asset is OpenMetaverse.Assets.AssetWearable assetWearable && assetWearable.Decode())
                wearable.Asset = assetWearable;
        }, ct)).ToList();

        try   { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { /* individual failures are non-fatal */ }
    }

    private IReadOnlyDictionary<int, float> GetVisualParams(Simulator sim)
    {
        if (_isSelf)
            return Client.Appearance.GetCurrentParamValues();

        var av = sim.ObjectsAvatars.Values.FirstOrDefault(a => a?.ID == _avatarId);
        if (av != null)
        {
            var decoded = av.DecodeVisualParams();
            if (decoded.Count > 0) return decoded;
        }
        return new Dictionary<int, float>();
    }

    private void UpdateAvatarName()
    {
        if (_isSelf)
        {
            var name = $"{Client.Self.FirstName} {Client.Self.LastName}".Trim();
            if (!string.IsNullOrEmpty(name)) AvatarName = name;
            return;
        }

        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        var av = sim.ObjectsAvatars.Values.FirstOrDefault(a => a?.ID == _avatarId);
        if (av == null) return;

        var avName = $"{av.FirstName} {av.LastName}".Trim();
        if (!string.IsNullOrEmpty(avName)) AvatarName = avName;
    }

    private static int CountAttachments(Simulator sim, uint avatarLocalId) =>
        sim.ObjectsPrimitives.Values.Count(p => p?.ParentID == avatarLocalId);

    // ── Debounced reload ──────────────────────────────────────────────────────────

    private void ScheduleReload()
    {
        _debounceTimer?.Change(500, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = LoadAsync(_cts.Token);
    }

    // ── Event handlers ────────────────────────────────────────────────────────────

    private void OnAvatarAppearance(object? sender, AvatarAppearanceEventArgs e)
    {
        if (e.AvatarID != _avatarId) return;
        // Ignore appearance packets that arrive while a build is running —
        // the active load already captures the latest appearance state.
        // Queue the reload so it fires as soon as the current build finishes.
        if (_firstLoad || _buildInProgress) { _pendingReload = true; return; }
        ScheduleReload();
    }

    private void OnSelfAppearanceSet(object? sender, AppearanceSetEventArgs e)
    {
        if (!_isSelf || !e.Success) return;
        if (_firstLoad || _buildInProgress) { _pendingReload = true; return; }
        ScheduleReload();
    }

    private void OnObjectUpdate(object? sender, PrimEventArgs e)
    {
        if (_avatarLocalId == 0) return;
        if (_firstLoad) return;
        if (e.Simulator != Client.Network.CurrentSim) return;
        if (e.IsAttachment && e.Prim?.ParentID == _avatarLocalId)
        {
            // Always track the ID so OnKillObject can detect its removal later.
            _knownAttachmentIds.TryAdd(e.Prim.LocalID, 0);
            // Only rebuild for genuinely new attachments; position updates during
            // animation would otherwise reset the debounce timer continuously.
            if (e.IsNew)
            {
                if (_buildInProgress) _pendingReload = true;
                else ScheduleReload();
            }
        }
    }

    private void OnKillObject(object? sender, KillObjectEventArgs e)
    {
        if (e.Simulator != Client.Network.CurrentSim) return;
        // AvatarAppearance is not reliably fired on detach, so we track known
        // attachment IDs ourselves and rebuild whenever one disappears.
        if (_knownAttachmentIds.TryRemove(e.ObjectLocalID, out _))
        {
            if (_buildInProgress) _pendingReload = true;
            else ScheduleReload();
        }
    }

    private void OnAvatarAnimation(object? sender, AvatarAnimationEventArgs e)
    {
        if (e.AvatarID != _avatarId) return;
        _animPlayer?.SetActiveAnimations(e.Animations.Select(a => a.AnimationID));
    }

    // ── Animation loop ────────────────────────────────────────────────────────────

    private async Task AnimationLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 30.0));
        var sw    = System.Diagnostics.Stopwatch.StartNew();
        float prev = 0f;
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                float now = (float)sw.Elapsed.TotalSeconds;
                float dt  = Math.Min(now - prev, 0.1f);
                prev = now;
                AnimTick(dt);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void AnimTick(float dt)
    {
        var sd        = _skinData;
        var invBind   = _invBindMatrices;
        var avatarDef = _avatarDef;
        var vpBt      = _vpBoneTransforms;
        var viewport  = _viewport;
        var player    = _animPlayer;
        var mode      = PoseMode;

        // ── Distance-driven LOD check
        // Run every 10 ticks so the overhead is negligible at 30 Hz.
        // Uses Camera3D.ComputeProjectedPixelHeight so zoom changes trigger LOD
        // switches independently of panel resize events.
        if (viewport != null && !_firstLoad && ++_lodTickCounter >= 10)
        {
            _lodTickCounter = 0;
            var last = _lastSubmission;
            if (last != null)
            {
                float objHeight = (last.BoundsMax - last.BoundsMin).Y;
                float projPx    = viewport.Camera.ComputeProjectedPixelHeight(
                    objHeight, (float)viewport.Bounds.Height);
                int newLod = PixelHeightToLod(projPx);
                if (newLod != _currentLod)
                {
                    _currentLod = newLod;
                    ScheduleReload();
                }
            }
        }

        if (sd == null || sd.Length == 0 || viewport == null) return;
        var skinData = sd; // non-null from here

        // Always advance the animation player to keep playback in sync regardless of pose mode.
        Dictionary<string, float> morphWeights = new(StringComparer.Ordinal);
        var liveDeltas = player?.Advance(dt, out morphWeights);

        // Apply dynamic (animation-driven) morphs — facial expressions, hand poses.
        // Only runs when there are morph-capable faces and at least one morph is active.
        var fmd = _faceMorphData;
        if (fmd.Length > 0 && sd != null && morphWeights.Count > 0)
        {
            foreach (var morphFace in fmd)
            {
                var baseV = morphFace.BaseVerts;
                var workV = morphFace.WorkBuf;
                Buffer.BlockCopy(baseV, 0, workV, 0, baseV.Length * sizeof(float));

                foreach (var entry in morphFace.Morphs)
                {
                    if (!morphWeights.TryGetValue(entry.Name, out var w) || w <= 1e-5f) continue;
                    foreach (var mv in entry.Vertices)
                    {
                        int vi = (int)mv.VertexIndex;
                        int o  = vi * 8;
                        if ((uint)(o + 5) >= (uint)workV.Length) continue;
                        workV[o + 0] += mv.CoordDelta.X  * w;
                        workV[o + 1] += mv.CoordDelta.Y  * w;
                        workV[o + 2] += mv.CoordDelta.Z  * w;
                        workV[o + 3] += mv.NormalDelta.X * w;
                        workV[o + 4] += mv.NormalDelta.Y * w;
                        workV[o + 5] += mv.NormalDelta.Z * w;
                    }
                }

                // Point the skin data's BindVerts at the morph-applied work buffer.
                if (morphFace.FaceIndex < sd!.Length)
                    sd[morphFace.FaceIndex].BindVerts = workV;
            }
        }
        else if (fmd.Length > 0 && sd != null && morphWeights.Count == 0)
        {
            // No morphs active this frame — restore the static base verts.
            foreach (var morphFace in fmd)
            {
                if (morphFace.FaceIndex < sd.Length)
                    sd[morphFace.FaceIndex].BindVerts = morphFace.BaseVerts;
            }
        }

        if (mode == AvatarPoseMode.TPose)
        {
            // Bind pose: upload the original rest-position vertices without any LBS.
            foreach (var skin in skinData)
            {
                if (skin.Bone1.Length == 0 && skin.JointNames == null) continue;
                viewport.ScheduleVertexUpdate(skin.FaceIndex, skin.BindVerts);
            }
            return;
        }

        if (invBind == null || avatarDef == null || vpBt == null) return;

        IReadOnlyDictionary<string, TkQuaternion> rotDeltas;
            if (mode == AvatarPoseMode.APose)
                rotDeltas = s_aPoseDeltas;
            else  // LiveAnimation
            {
                // Always run LBS — even with empty animation deltas — so VP bone scale and
                // position effects (height, body mass, proportions) are applied immediately
                // without waiting for the stand animation asset to finish downloading.
                var count = liveDeltas?.Count ?? 0;
                rotDeltas = liveDeltas ?? new Dictionary<string, TkQuaternion>(StringComparer.Ordinal);

                // Always refresh flexi attachment bone provider — even when LBS is skipped
                // below — so attachments keep tracking the body while idle.  Uses the
                // attachment-flavour matrices (no VP scale propagation) to match the
                // static AttachTransform built by AvatarMeshBuilder.
                if (_flexi != null)
                {
                    AvatarMeshBuilder.ComputeAttachmentBoneWorldMatrices(
                        avatarDef, vpBt, rotDeltas, _attachBonesBuffer);
                    var attachBones = _attachBonesBuffer;
                    _flexi.SetBoneProvider(name => attachBones.TryGetValue(name, out var m) ? m : TkMatrix4.Identity);
                }

                // Optimisation: once the static VP pose has been applied and no animation is
                // playing, skip recomputation until something changes.
                bool force = _forceSkinUpdate;
                _forceSkinUpdate = false;
                if (!force && count == 0 && _prevLiveDeltaCount == 0) return;
                _prevLiveDeltaCount = count;
            }

        AvatarMeshBuilder.ComputeAnimatedBoneWorldMatrices(avatarDef, vpBt, rotDeltas, _animBonesBuffer);
        var animBones = _animBonesBuffer;

        // Flexi attachment provider was already refreshed above for the LiveAnimation
        // path; if we got here via APose we still need to set it once with the pose's
        // attachment-flavour bones.
        if (_flexi != null && mode == AvatarPoseMode.APose)
        {
            AvatarMeshBuilder.ComputeAttachmentBoneWorldMatrices(
                avatarDef, vpBt, rotDeltas, _attachBonesBuffer);
            var attachBones = _attachBonesBuffer;
            _flexi.SetBoneProvider(name => attachBones.TryGetValue(name, out var m) ? m : TkMatrix4.Identity);
        }

        // Rigged/fitted mesh attachments deform with real VP-driven bone transforms
        // (including collision volumes) rather than the body-mesh's VP-less set.
        // Compute the alternate animBones lazily only if any face requests them.
        Dictionary<string, TkMatrix4>? vpAnimBones = null;
        bool needVpAnim = false;
        foreach (var s in skinData) { if (s.UseVpBoneTransforms) { needVpAnim = true; break; } }
        if (needVpAnim && _fittedBoneTransforms != null)
        {
            // Advance avatar physics wearable simulation (breast/butt/belly bounce) and
            // apply driven-param outputs as collision-volume bone position offsets.
            // Only runs in LiveAnimation mode to match SL behaviour.
            Dictionary<string, BoneTransform>? physicsPatched = null;
            if (mode == AvatarPoseMode.LiveAnimation)
            {
                _physicsElapsed += dt;
                _physics.SetBonePositionProvider(name =>
                {
                    if (vpAnimBones != null && vpAnimBones.TryGetValue(name, out var m))
                        return new TkVector3(m.Row3.X, m.Row3.Y, m.Row3.Z);
                    return TkVector3.Zero;
                });
                var drivenWeights = _physics.Tick(_physicsElapsed);
                var boneOffsets   = PhysicsVolumeMorphs.ComputeBoneOffsets(drivenWeights, _physicsVpWeights);
                if (boneOffsets.Count > 0)
                {
                    physicsPatched = new Dictionary<string, BoneTransform>(_fittedBoneTransforms, StringComparer.Ordinal);
                    foreach (var (boneName, offset) in boneOffsets)
                    {
                        if (!physicsPatched.TryGetValue(boneName, out var bt)) continue;
                        bt.Position = new OpenMetaverse.Vector3(
                            bt.Position.X + offset.X,
                            bt.Position.Y + offset.Y,
                            bt.Position.Z + offset.Z);
                        physicsPatched[boneName] = bt;
                    }
                }
            }
            AvatarMeshBuilder.ComputeAttachmentBoneWorldMatrices(
                avatarDef, physicsPatched ?? _fittedBoneTransforms, rotDeltas, _vpAnimBonesBuffer);
            vpAnimBones = _vpAnimBonesBuffer;
        }

        foreach (var skin in skinData)
        {
            // Rigged / fitted mesh path: per-face inverse bind matrices, 4-bone LBS,
            // joint indices reference skin.JointNames[] (not the global invBind dict).
            if (skin.JointNames != null && skin.InvBindMatrices != null
                && skin.Joints != null && skin.Weights != null)
            {
                var bonesMap = (skin.UseVpBoneTransforms && vpAnimBones != null)
                    ? vpAnimBones : animBones;

                int nvR = skin.BindVerts.Length / 8;
                float[] nvBufR = ArrayPool<float>.Shared.Rent(skin.BindVerts.Length);
                var joints = skin.JointNames;
                var ibms   = skin.InvBindMatrices;
                int jointCount = joints.Length;

                for (int vi = 0; vi < nvR; vi++)
                {
                    int o = vi * 8;
                    var bp = new TkVector4(skin.BindVerts[o],     skin.BindVerts[o + 1],
                                           skin.BindVerts[o + 2], 1f);
                    var bn = new TkVector4(skin.BindVerts[o + 3], skin.BindVerts[o + 4],
                                           skin.BindVerts[o + 5], 0f);

                    var ap = TkVector4.Zero;
                    var an = TkVector4.Zero;
                    float totalW = 0f;

                    for (int infl = 0; infl < 4; infl++)
                    {
                        int   ji = skin.Joints![vi * 4 + infl];
                        float w  = skin.Weights![vi * 4 + infl];
                        if (w <= 1e-4f) continue;
                        if ((uint)ji >= (uint)jointCount) continue;

                        var jointName = joints[ji];
                        if (!bonesMap.TryGetValue(jointName, out var m))
                        {
                            ap += w * bp;
                            an += w * bn;
                            totalW += w;
                            continue;
                        }

                        var ib = ibms[ji];
                        var sp = TkVector4.TransformRow(TkVector4.TransformRow(bp, ib), m);
                        var sn = TkVector4.TransformRow(TkVector4.TransformRow(bn, ib), m);
                        ap += w * sp;
                        an += w * sn;
                        totalW += w;
                    }

                    if (totalW <= 1e-4f) { ap = bp; an = bn; }

                    nvBufR[o]     = ap.X; nvBufR[o + 1] = ap.Y; nvBufR[o + 2] = ap.Z;
                    nvBufR[o + 3] = an.X; nvBufR[o + 4] = an.Y; nvBufR[o + 5] = an.Z;
                    nvBufR[o + 6] = skin.BindVerts[o + 6];
                    nvBufR[o + 7] = skin.BindVerts[o + 7];
                }

                viewport.ScheduleVertexUpdate(skin.FaceIndex, nvBufR.AsSpan(0, skin.BindVerts.Length));
                ArrayPool<float>.Shared.Return(nvBufR);
                continue;
            }

            if (skin.Bone1.Length == 0) continue;

            int     nv    = skin.BindVerts.Length / 8;
            float[] nvBuf = ArrayPool<float>.Shared.Rent(skin.BindVerts.Length);

            for (int vi = 0; vi < nv; vi++)
            {
                int o  = vi * 8;
                var bp = new TkVector4(
                    skin.BindVerts[o],     skin.BindVerts[o + 1],
                    skin.BindVerts[o + 2], 1f);
                var bn = new TkVector4(
                    skin.BindVerts[o + 3], skin.BindVerts[o + 4],
                    skin.BindVerts[o + 5], 0f);

                var ap = TkVector4.Zero;
                var an = TkVector4.Zero;

                var   b1 = skin.Bone1[vi];
                float w1 = skin.Weight1[vi];
                if (w1 > 1e-4f && animBones.TryGetValue(b1, out var m1)
                               && invBind.TryGetValue(b1, out var ib1))
                {
                    ap += w1 * TkVector4.TransformRow(TkVector4.TransformRow(bp, ib1), m1);
                    an += w1 * TkVector4.TransformRow(TkVector4.TransformRow(bn, ib1), m1);
                }
                else
                {
                    ap += w1 * bp;
                    an += w1 * bn;
                }

                float w2 = skin.Weight2[vi];
                if (w2 > 1e-4f)
                {
                    var b2 = skin.Bone2[vi];
                    if (animBones.TryGetValue(b2, out var m2) && invBind.TryGetValue(b2, out var ib2))
                    {
                        ap += w2 * TkVector4.TransformRow(TkVector4.TransformRow(bp, ib2), m2);
                        an += w2 * TkVector4.TransformRow(TkVector4.TransformRow(bn, ib2), m2);
                    }
                    else
                    {
                        ap += w2 * bp;
                        an += w2 * bn;
                    }
                }

                nvBuf[o]     = ap.X; nvBuf[o + 1] = ap.Y; nvBuf[o + 2] = ap.Z;
                nvBuf[o + 3] = an.X; nvBuf[o + 4] = an.Y; nvBuf[o + 5] = an.Z;
                nvBuf[o + 6] = skin.BindVerts[o + 6];
                nvBuf[o + 7] = skin.BindVerts[o + 7];
            }

            viewport.ScheduleVertexUpdate(skin.FaceIndex, nvBuf.AsSpan(0, skin.BindVerts.Length));
            ArrayPool<float>.Shared.Return(nvBuf);
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isSelf)
            Client.Appearance.AppearanceSet -= OnSelfAppearanceSet;
        Client.Avatars.AvatarAppearance -= OnAvatarAppearance;
        Client.Objects.ObjectUpdate     -= OnObjectUpdate;
        Client.Objects.KillObject       -= OnKillObject;
        Client.Avatars.AvatarAnimation  -= OnAvatarAnimation;

        _animCts?.Cancel();
        _animCts?.Dispose();
        _animCts = null;
        _animPlayer?.Dispose();
        _animPlayer = null;

        _particles?.Dispose();
        _particles = null;

        _flexi?.Dispose();
        _flexi = null;

        if (_viewport != null) _viewport.FaceClicked -= OnFaceClicked;

        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
