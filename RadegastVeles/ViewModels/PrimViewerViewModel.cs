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
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.Rendering;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// ViewModel for the 3D prim (object) viewer.
/// Fetches the root prim and its linkset children from the current simulator,
/// tessellates them with <c>MeshmerizerR</c>, fetches face textures from the
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

    [ObservableProperty] private string _objectName   = "Loading…";
    [ObservableProperty] private string _statusText   = string.Empty;
    [ObservableProperty] private bool   _isLoading    = true;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private string _errorText    = string.Empty;
    [ObservableProperty] private bool   _wireframe;

    partial void OnWireframeChanged(bool value)
    {
        _viewport?.Wireframe = value;
    }

    public PrimViewerViewModel(RadegastInstanceAvalonia instance, uint rootLocalId)
    {
        _instance    = instance;
        _rootLocalId = rootLocalId;
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
        _viewport          = viewport;
        _viewport.Wireframe = Wireframe;

        // If geometry was already loaded before the viewport was attached, send it now.
        if (_lastSubmission != null)
            _viewport.Submit(_lastSubmission);
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
    [RelayCommand] private void ZoomIn()     => _viewport?.ZoomStep( 1f);
    [RelayCommand] private void ZoomOut()    => _viewport?.ZoomStep(-1f);

    // ── Loading pipeline ────────────────────────────────────────────────────────

    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading  = true;
        StatusText = "Building mesh…";

        try
        {
            var sim = Client.Network.CurrentSim
                      ?? throw new InvalidOperationException("Not connected to a simulator.");

            var prims = new List<Primitive>();

            if (!sim.ObjectsPrimitives.TryGetValue(_rootLocalId, out var root))
                throw new InvalidOperationException($"Prim {_rootLocalId} not found in simulator.");

            prims.Add(root);
            foreach (var p in sim.ObjectsPrimitives.Values)
            {
                if (p.ParentID == _rootLocalId)
                    prims.Add(p);
            }

            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => StatusText = msg));

            var submission = await _builder.BuildAsync(prims, _rootLocalId, ObjectName, progress, ct)
                                           .ConfigureAwait(false);

            _lastSubmission = submission;

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

    // ── Disposal ─────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
