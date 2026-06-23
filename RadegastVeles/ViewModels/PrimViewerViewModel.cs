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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Numerics;
using LibreMetaverse;
using Vector3 = System.Numerics.Vector3;
using Radegast.Veles.Core;
using Radegast.Veles.Rendering;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// ViewModel for the 3D prim (object) viewer.
/// Fetches the root prim and its linkset children from the current simulator,
/// tessellates them with <c>MeshFoundry</c>, fetches face textures from the
/// asset server, and submits a <see cref="PrimRenderSubmission"/> to the
/// <see cref="GlViewportControl"/> for rendering.
/// </summary>
public partial class PrimViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private readonly uint             _rootLocalId;
    private readonly PrimMeshBuilder   _builder;
    private          GlViewportControl? _viewport;
    private          CancellationTokenSource? _cts;
    private          bool          _disposed;

    // Saved so ResetCamera can re-submit without re-fetching.
    private PrimRenderSubmission? _lastSubmission;

    private ParticleViewerDriver? _particles;
    private FlexiPrimAnimator?    _flexi;

    [ObservableProperty] private string _objectName   = "Loading…";
    [ObservableProperty] private string _statusText   = string.Empty;
    [ObservableProperty] private bool   _isLoading    = true;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private string _errorText    = string.Empty;
    [ObservableProperty] private bool   _wireframe;
    [ObservableProperty] private bool   _ssaoEnabled;

    partial void OnWireframeChanged(bool value)
    {
        _viewport?.Wireframe = value;
    }

    partial void OnSsaoEnabledChanged(bool value)
    {
        if (_viewport != null) _viewport.SsaoEnabled = value;
    }

    public PrimViewerViewModel(RadegastInstanceAvalonia instance, uint rootLocalId)
    {
        _instance    = instance;
        _rootLocalId = rootLocalId;
        _ssaoEnabled = instance.GlobalSettings["ssao_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? instance.GlobalSettings["ssao_enabled"].AsBoolean() : true;
        _builder     = new PrimMeshBuilder(Client);

        if (Client.Network.CurrentSim?.ObjectsPrimitives.TryGetValue(rootLocalId, out var p) == true)
            ObjectName = p.Properties?.Name ?? $"Object {rootLocalId}";

        _cts = new CancellationTokenSource();
        _ = LoadAsync(_cts.Token);
    }

    /// <summary>
    /// Attach the GL viewport so the VM can submit geometry to it.
    /// Call this from the view's code-behind after the control tree is ready.
    /// </summary>
    public void SetViewport(GlViewportControl viewport)
    {
        if (_viewport != null)
            _viewport.FaceClicked -= OnFaceClicked;

        _viewport            = viewport;
        _viewport.Wireframe   = Wireframe;
        _viewport.SsaoEnabled = SsaoEnabled;
        _viewport.ShowSky     = false;
        _viewport.Sky         = SkySettings.Studio;
        _viewport.FaceClicked += OnFaceClicked;

        // If geometry was already loaded before the viewport was attached, send it now.
        if (_lastSubmission != null)
        {
            _viewport.Submit(_lastSubmission);

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
    }

    [RelayCommand]
    private void ResetCamera()
    {
        _viewport?.ResetCamera();
    }

    [RelayCommand] private void OrbitLeft()  => _viewport?.OrbitStep(-15f,   0f);
    [RelayCommand] private void OrbitRight() => _viewport?.OrbitStep( 15f,   0f);
    [RelayCommand] private void OrbitUp()    => _viewport?.OrbitStep(  0f, -10f);
    [RelayCommand] private void OrbitDown()  => _viewport?.OrbitStep(  0f,  10f);
    [RelayCommand] private void ZoomIn()     => _viewport?.ZoomStep( 1.5f);
    [RelayCommand] private void ZoomOut()    => _viewport?.ZoomStep(-1.5f);

    // ── Loading pipeline ────────────────────────────────────────────────────────

    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading  = true;
        StatusText = "Building mesh…";

        try
        {
            var sim = Client.Network.CurrentSim
                      ?? throw new InvalidOperationException("Not connected to a simulator.");

            if (!sim.ObjectsPrimitives.TryGetValue(_rootLocalId, out var root))
                throw new InvalidOperationException($"Prim {_rootLocalId} not found in simulator.");

            // Root first, then children sorted by LocalID for deterministic
            // tessellation order. ObjectsPrimitives is a ConcurrentDictionary
            // whose iteration order is undefined.
            var prims = sim.ObjectsPrimitives.Values
                .Where(p => p.LocalID == _rootLocalId || p.ParentID == _rootLocalId)
                .OrderBy(p => p.LocalID == _rootLocalId ? 0u : p.LocalID)
                .ToList();

            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => StatusText = msg));

            var texturePatch = new Progress<SceneTexturePatch>(patch =>
                _viewport?.PatchSubmissionTexture(patch));

            var submission = await _builder.BuildAsync(prims, _rootLocalId, ObjectName, progress, ct,
                                                       texturePatch: texturePatch)
                                           .ConfigureAwait(false);

            _lastSubmission = submission;

            // Start particle simulation for emitter prims.
            _particles?.Dispose();
            // Use rootLocalId as key; world pos is zero since the object viewer
            // renders in object-local space (no world translation needed).
            _particles = new ParticleViewerDriver(Client, prims, (ulong)_rootLocalId,
                Vector3.Zero);
            if (_viewport != null) _particles.SetViewport(_viewport);
            _particles.Start();

            // Start flexi-prim animation if the linkset contains any flexi prims.
            _flexi?.Dispose();
            _flexi = null;
            if (submission.FlexiPrims.Length > 0 && _viewport != null)
            {
                var vp = _viewport;
                _flexi = new FlexiPrimAnimator(submission, vp.ScheduleVertexUpdate);
                _flexi.Start();
            }

            Dispatcher.UIThread.Post(() =>
            {
                _viewport?.Submit(submission);
                StatusText = $"Ready — {submission.Faces.Length} face(s), {prims.Count} prim(s).";
                IsLoading  = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Normal on dispose — no UI update needed.
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                HasError   = true;
                ErrorText  = ex.Message;
                StatusText = $"Error: {ex.Message}";
                IsLoading  = false;
            });
        }
    }

    // ── Touch handling ────────────────────────────────────────────────────────────

    private void OnFaceClicked(uint primLocalId, int faceIndex, FaceHitInfo hit)
    {
        var sim = Client.Network.CurrentSim;
        string primLabel;
        if (sim != null && sim.ObjectsPrimitives.TryGetValue(primLocalId, out var clickedPrim))
        {
            var name = clickedPrim.Properties?.Name;
            var isRoot = primLocalId == _rootLocalId;
            primLabel = string.IsNullOrWhiteSpace(name)
                ? (isRoot ? "root" : "child prim")
                : $"\"{name}\"";
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

        static LibreMetaverse.Vector3 ToOmv(Vector3 v) => new(v.X, v.Y, v.Z);

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

    // ── Disposal ─────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_viewport != null)
            _viewport.FaceClicked -= OnFaceClicked;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _particles?.Dispose();
        _particles = null;
        _flexi?.Dispose();
        _flexi = null;
    }
}
