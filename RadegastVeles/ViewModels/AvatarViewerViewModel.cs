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
using OpenMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.Rendering;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// ViewModel for the 3D avatar viewer.
/// Collects all non-HUD attachments worn by the specified avatar, tessellates them
/// with correct avatar-relative transforms, and submits the result to a
/// <see cref="GlViewportControl"/> for rendering.
/// Body-mesh and animation rendering are pending future implementation.
/// </summary>
public partial class AvatarViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly UUID   _agentId;
    private readonly string _agentName;
    private readonly PrimMeshBuilder _builder;
    // Skeleton is loaded once and reused; Lazy<T> makes the initialisation
    // thread-safe so concurrent retessellation calls never double-load.
    private readonly Lazy<AvatarSkeleton> _skeletonLazy =
        new(() => AvatarSkeleton.Load(), LazyThreadSafetyMode.ExecutionAndPublication);

    private GlViewportControl?       _viewport;
    private CancellationTokenSource? _cts;
    private bool                     _disposed;
    private volatile HashSet<uint>?  _knownAttachmentIds;

    [ObservableProperty] private string _avatarName  = string.Empty;
    [ObservableProperty] private string _statusText  = string.Empty;
    [ObservableProperty] private bool   _isLoading   = true;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private string _errorText   = string.Empty;

    /// <summary>
    /// When true the viewer is in T-Pose mode (default).
    /// Future: drives skeleton joint-transform selection for body mesh rendering.
    /// </summary>
    [ObservableProperty] private bool _isTpose = true;

    partial void OnIsTposeChanged(bool value) => ScheduleRetessellation();

    // ── Constructor ───────────────────────────────────────────────────────────────

    public AvatarViewerViewModel(RadegastInstanceAvalonia instance, UUID agentId, string agentName)
    {
        _instance  = instance;
        _agentId   = agentId;
        _agentName = agentName;
        AvatarName = agentName;
        _builder   = new PrimMeshBuilder(instance.Client);

        Client.Objects.ObjectUpdate += OnObjectUpdate;
        Client.Objects.KillObjects  += OnKillObjects;

        _cts = new CancellationTokenSource();
        _ = LoadAsync(_cts.Token, frontFrame: true);
    }

    // ── Viewport wiring ───────────────────────────────────────────────────────────

    public void SetViewport(GlViewportControl viewport) => _viewport = viewport;

    // ── Camera commands ───────────────────────────────────────────────────────────

    [RelayCommand] private void ResetCamera() => _viewport?.ResetCameraFront();
    [RelayCommand] private void OrbitLeft()   => _viewport?.OrbitStep(-15f,   0f);
    [RelayCommand] private void OrbitRight()  => _viewport?.OrbitStep( 15f,   0f);
    [RelayCommand] private void OrbitUp()     => _viewport?.OrbitStep(  0f, -10f);
    [RelayCommand] private void OrbitDown()   => _viewport?.OrbitStep(  0f,  10f);
    [RelayCommand] private void ZoomIn()      => _viewport?.ZoomStep( 1f);
    [RelayCommand] private void ZoomOut()     => _viewport?.ZoomStep(-1f);

    // ── Loading pipeline ──────────────────────────────────────────────────────────

    private async Task LoadAsync(CancellationToken ct, bool frontFrame = false)
    {
        IsLoading  = true;
        HasError   = false;
        ErrorText  = string.Empty;
        StatusText = "Finding avatar…";

        try
        {
            var sim = Client.Network.CurrentSim
                      ?? throw new InvalidOperationException("Not connected to a simulator.");

            uint avatarLocalId = ResolveAvatarLocalId(sim);
            if (avatarLocalId == 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = $"Avatar '{_agentName}' is no longer in range.";
                    IsLoading  = false;
                });
                return;
            }

            // Root attachments — prims directly parented to the avatar, excluding HUDs.
            var roots = sim.ObjectsPrimitives.Values
                .Where(p => p.ParentID == avatarLocalId && !IsHudPoint(p.PrimData.AttachmentPoint))
                .ToList();

            if (roots.Count == 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = "No attachments found.";
                    IsLoading  = false;
                });
                return;
            }

            var rootIds  = new HashSet<uint>(roots.Select(p => p.LocalID));
            var children = sim.ObjectsPrimitives.Values
                .Where(p => rootIds.Contains(p.ParentID))
                .ToList();
            var allPrims = roots.Concat(children).ToList();

            _knownAttachmentIds = new HashSet<uint>(allPrims.Select(p => p.LocalID));

            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => StatusText = msg));

            // Load the avatar skeleton lazily (bone hierarchy + attachment points).
            var skeleton = await Task.Run(() => _skeletonLazy.Value, ct).ConfigureAwait(false);

            var submission = await _builder.BuildAvatarAttachmentsAsync(
                allPrims, avatarLocalId, skeleton, _agentName, progress, ct).ConfigureAwait(false);

            var poseLabel = IsTpose ? "T-Pose" : "Current Pose";
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed)
                {
                    foreach (var f in submission.Faces) f.Texture?.Dispose();
                    return;
                }
                if (frontFrame)
                    _viewport?.SubmitFront(submission);
                else
                    _viewport?.Submit(submission);
                StatusText = $"{poseLabel} — {submission.Faces.Length} face(s), {allPrims.Count} attachment prim(s).";
                IsLoading  = false;
            });
        }
        catch (OperationCanceledException) { }
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

    // ── Live updates ──────────────────────────────────────────────────────────────

    private void OnObjectUpdate(object? sender, PrimEventArgs e)
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        uint avatarLocalId = ResolveAvatarLocalId(sim);
        if (avatarLocalId == 0) return;
        if (e.Prim.ParentID == avatarLocalId && !IsHudPoint(e.Prim.PrimData.AttachmentPoint))
            ScheduleRetessellation();
    }

    private void OnKillObjects(object? sender, KillObjectsEventArgs e)
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        if (ResolveAvatarLocalId(sim) == 0) return;  // avatar already left the sim

        var known = _knownAttachmentIds;
        if (known != null && e.ObjectLocalIDs.Any(id => known.Contains(id)))
            ScheduleRetessellation();
    }

    private uint ResolveAvatarLocalId(Simulator sim)
    {
        if (_agentId == Client.Self.AgentID)
            return Client.Self.LocalID;
        foreach (var av in sim.ObjectsAvatars.Values)
        {
            if (av?.ID == _agentId)
                return av.LocalID;
        }
        return 0;
    }

    private CancellationTokenSource? _retessCts;

    private void ScheduleRetessellation()
    {
        _retessCts?.Cancel();
        _retessCts?.Dispose();
        _retessCts = new CancellationTokenSource();
        var token = _retessCts.Token;
        _ = RetessellateAfterDelayAsync(token);
    }

    private async Task RetessellateAfterDelayAsync(CancellationToken ct)
    {
        try { await Task.Delay(350, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        await LoadAsync(_cts.Token, frontFrame: false).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static bool IsHudPoint(AttachmentPoint p) => (int)p is >= 31 and <= 38;

    // ── Disposal ──────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Client.Objects.ObjectUpdate -= OnObjectUpdate;
        Client.Objects.KillObjects  -= OnKillObjects;
        _retessCts?.Cancel();
        _retessCts?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
