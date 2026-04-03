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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse.Appearance;
using OpenMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.Rendering;

namespace Radegast.Veles.ViewModels;

/// <summary>One HUD attachment root prim shown in the list.</summary>
public sealed record HudEntry(uint LocalId, UUID Id, string Name, string AttachPoint);

/// <summary>
/// ViewModel for the HUD viewer: lists HUD attachments worn by the current
/// avatar, tessellates the selected one in a <see cref="GlViewportControl"/>,
/// and surfaces live hover-text updates from the simulator.
/// </summary>
public partial class HudViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly PrimMeshBuilder _builder;

    private GlViewportControl?        _viewport;
    private CancellationTokenSource?  _cts;
    private bool                      _disposed;

    // ── Observable state ─────────────────────────────────────────────────────────

    public ObservableCollection<HudEntry> HudAttachments { get; } = new();

    [ObservableProperty] private HudEntry? _selectedHud;
    [ObservableProperty] private string    _hoverText    = string.Empty;
    [ObservableProperty] private bool      _showHoverText;
    [ObservableProperty] private string    _statusText   = string.Empty;
    [ObservableProperty] private bool      _isLoading;
    [ObservableProperty] private bool      _hasError;
    [ObservableProperty] private string    _errorText    = string.Empty;
    [ObservableProperty] private bool      _isHudListVisible = true;

    partial void OnSelectedHudChanged(HudEntry? value)
    {
        TouchCommand.NotifyCanExecuteChanged();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        HasError      = false;
        ErrorText     = string.Empty;
        HoverText     = string.Empty;
        ShowHoverText = false;

        if (value == null) return;

        _cts = new CancellationTokenSource();
        _ = LoadHudAsync(value, _cts.Token, frontFrame: true);
    }

    // ── Constructor ───────────────────────────────────────────────────────────────

    public HudViewerViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        _builder  = new PrimMeshBuilder(instance.Client);

        Client.Objects.ObjectUpdate += OnObjectUpdate;
        Client.Objects.TerseObjectUpdate += OnTerseObjectUpdate;
        Client.Objects.ObjectDataBlockUpdate += OnObjectDataBlockUpdate;
        Client.Objects.KillObjects += OnKillObjects;
        Refresh();
    }

    // ── Viewport wiring ───────────────────────────────────────────────────────────

    /// <summary>
    /// Wire the GL viewport into this VM after the visual tree is ready.
    /// Called from the view's code-behind once DataContext is set.
    /// </summary>
    public void SetViewport(GlViewportControl viewport)
    {
        _viewport = viewport;
        _viewport.FaceClicked += OnFaceClicked;
    }

    // ── Commands ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Refresh()
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        HudAttachments.Clear();

        uint selfId = Client.Self.LocalID;
        foreach (var prim in sim.ObjectsPrimitives.Values)
        {
            if (prim.ParentID != selfId) continue;
            if (!IsHudPoint(prim.PrimData.AttachmentPoint)) continue;

            var name  = prim.Properties?.Name ?? $"HUD {prim.LocalID}";
            var point = FormatAttachPoint(prim.PrimData.AttachmentPoint);
            HudAttachments.Add(new HudEntry(prim.LocalID, prim.ID, name, point));
        }

        StatusText = HudAttachments.Count == 0
            ? "No HUD attachments found."
            : $"{HudAttachments.Count} HUD attachment(s).";
    }

    [RelayCommand(CanExecute = nameof(CanTouch))]
    private void Touch()
    {
        if (SelectedHud == null) return;
        _ = GrabFaceAsync(SelectedHud.LocalId, 0);
    }

    private bool CanTouch() => SelectedHud != null;

    private void OnFaceClicked(uint primLocalId, int faceIndex)
    {
        if (SelectedHud == null) return;
        _ = GrabFaceAsync(primLocalId, faceIndex);
        StatusText = $"Touched face {faceIndex} of prim {primLocalId}.";
    }

    private async Task GrabFaceAsync(uint localId, int faceIndex)
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Client.Objects.ClickObjectAsync(
                sim, localId,
                Vector3.Zero, Vector3.Zero, faceIndex,
                Vector3.Zero, Vector3.Zero, Vector3.Zero,
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private async Task DetachHud(HudEntry? entry)
    {
        if (entry == null) return;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        if (!sim.ObjectsPrimitives.TryGetValue(entry.LocalId, out var prim)) return;

        var itemId = CurrentOutfitFolder.GetAttachmentItemID(prim);
        if (itemId == UUID.Zero) return;

        if (!Client.Inventory.Store.TryGetValue<InventoryItem>(itemId, out var item)) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _instance.COF.Detach(item, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"Detach failed: {ex.Message}";
            });
        }
    }

    [RelayCommand] private void ResetCamera() => _viewport?.ResetCameraFront();
    [RelayCommand] private void OrbitLeft()   => _viewport?.OrbitStep(-15f,   0f);
    [RelayCommand] private void OrbitRight()  => _viewport?.OrbitStep( 15f,   0f);
    [RelayCommand] private void OrbitUp()     => _viewport?.OrbitStep(  0f, -10f);
    [RelayCommand] private void OrbitDown()   => _viewport?.OrbitStep(  0f,  10f);
    [RelayCommand] private void ZoomIn()      => _viewport?.ZoomStep( 1f);
    [RelayCommand] private void ZoomOut()     => _viewport?.ZoomStep(-1f);

    // ── Loading pipeline ──────────────────────────────────────────────────────────

    private async Task LoadHudAsync(HudEntry hud, CancellationToken ct, bool frontFrame = false)
    {
        IsLoading  = true;
        HasError   = false;
        ErrorText  = string.Empty;
        StatusText = $"Loading {hud.Name}…";

        try
        {
            var sim = Client.Network.CurrentSim
                      ?? throw new InvalidOperationException("Not connected to a simulator.");

            if (!sim.ObjectsPrimitives.TryGetValue(hud.LocalId, out var root))
                throw new InvalidOperationException($"HUD prim {hud.LocalId} not found in simulator.");

            // Root prim + all linkset children.
            var prims = sim.ObjectsPrimitives.Values
                .Where(p => p.LocalID == hud.LocalId || p.ParentID == hud.LocalId)
                .ToList();

            // Snapshot hover text from the root prim immediately.
            var ht = root.Text?.Trim() ?? string.Empty;
            Dispatcher.UIThread.Post(() =>
            {
                HoverText     = ht;
                ShowHoverText = ht.Length > 0;
            });

            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => StatusText = msg));

            var submission = await _builder.BuildAsync(prims, hud.LocalId, hud.Name, progress, ct)
                                           .ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                if (frontFrame)
                    _viewport?.SubmitFront(submission);
                else
                    _viewport?.Submit(submission);
                StatusText = $"Ready — {submission.Faces.Length} face(s), {prims.Count} prim(s).";
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
        HandlePrimChange(e.Prim);
        HandleNewAttachment(e.Prim);
    }

    private void OnTerseObjectUpdate(object? sender, TerseObjectUpdateEventArgs e)
    {
        HandlePrimChange(e.Prim);
    }

    private void OnObjectDataBlockUpdate(object? sender, ObjectDataBlockUpdateEventArgs e)
    {
        HandlePrimChange(e.Prim);
    }

    private void HandleNewAttachment(Primitive prim)
    {
        // Only consider root HUD prims attached directly to self.
        if (prim.ParentID != Client.Self.LocalID) return;
        if (!IsHudPoint(prim.PrimData.AttachmentPoint)) return;
        if (HudAttachments.Any(h => h.LocalId == prim.LocalID)) return;

        var name  = prim.Properties?.Name ?? $"HUD {prim.LocalID}";
        var point = FormatAttachPoint(prim.PrimData.AttachmentPoint);
        var entry = new HudEntry(prim.LocalID, prim.ID, name, point);
        Dispatcher.UIThread.Post(() =>
        {
            if (HudAttachments.Any(h => h.LocalId == entry.LocalId)) return;
            HudAttachments.Add(entry);
            StatusText = $"{HudAttachments.Count} HUD attachment(s).";
        });
    }

    private void OnKillObjects(object? sender, KillObjectsEventArgs e)
    {
        var killed = new System.Collections.Generic.HashSet<uint>(e.ObjectLocalIDs);
        Dispatcher.UIThread.Post(() =>
        {
            var toRemove = HudAttachments.Where(h => killed.Contains(h.LocalId)).ToList();
            foreach (var h in toRemove)
            {
                if (SelectedHud?.LocalId == h.LocalId)
                    SelectedHud = null;
                HudAttachments.Remove(h);
            }
            if (toRemove.Count > 0)
                StatusText = $"{HudAttachments.Count} HUD attachment(s).";
        });
    }

    private void HandlePrimChange(Primitive prim)
    {
        if (SelectedHud == null) return;
        // Only react to prims that belong to the currently displayed HUD linkset.
        if (prim.LocalID != SelectedHud.LocalId && prim.ParentID != SelectedHud.LocalId) return;

        // Update hover text from the root prim.
        if (prim.LocalID == SelectedHud.LocalId)
        {
            var ht = prim.Text?.Trim() ?? string.Empty;
            Dispatcher.UIThread.Post(() =>
            {
                HoverText     = ht;
                ShowHoverText = ht.Length > 0;
            });
        }

        // Re-tessellate the entire linkset to reflect geometry/texture changes.
        ScheduleRetessellation();
    }

    private CancellationTokenSource? _retessCts;

    /// <summary>
    /// Debounce rapid prim updates — waits 250 ms of silence before
    /// re-tessellating so we don't spam the builder on every terse update.
    /// </summary>
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
        try
        {
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        var hud = SelectedHud;
        if (hud == null || ct.IsCancellationRequested) return;

        // Cancel any existing full load and start a fresh build.
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        await LoadHudAsync(hud, _cts.Token).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>HUD attachment points span values 31–38 in the SL protocol.</summary>
    private static bool IsHudPoint(AttachmentPoint p) =>
        (int)p is >= 31 and <= 38;

    private static string FormatAttachPoint(AttachmentPoint p)
    {
        // "HUDTopRight" → "HUD Top Right" for readability.
        var raw = p.ToString();
        if (!raw.StartsWith("HUD", StringComparison.Ordinal)) return raw;
        var suffix = raw[3..]; // strip "HUD"
        // Insert spaces before each uppercase letter after the first.
        var spaced = System.Text.RegularExpressions.Regex.Replace(suffix, "(?<!^)([A-Z])", " $1");
        return "HUD " + spaced.TrimStart();
    }

    // ── Disposal ──────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Client.Objects.ObjectUpdate -= OnObjectUpdate;
        Client.Objects.TerseObjectUpdate -= OnTerseObjectUpdate;
        Client.Objects.ObjectDataBlockUpdate -= OnObjectDataBlockUpdate;
        Client.Objects.KillObjects -= OnKillObjects;
        if (_viewport != null)
            _viewport.FaceClicked -= OnFaceClicked;
        _retessCts?.Cancel();
        _retessCts?.Dispose();
        _retessCts = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
