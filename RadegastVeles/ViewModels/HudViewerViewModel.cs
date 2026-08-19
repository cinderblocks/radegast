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
using LibreMetaverse;
using Vector3 = System.Numerics.Vector3;
using Radegast.Veles.Core;
using Radegast.Veles.Rendering;

namespace Radegast.Veles.ViewModels;

/// <summary>One HUD attachment root prim shown in the list.</summary>
public sealed record HudEntry(uint LocalId, UUID Id, string Name, string AttachPoint);

/// <summary>
/// ViewModel for the HUD viewer: lists HUD attachments worn by the current avatar,
/// tessellates the selected one in an <see cref="ISingleObjectViewport"/>, and surfaces
/// live hover-text updates from the simulator.
/// </summary>
public partial class HudViewerViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly PrimMeshBuilder _builder;

    private ISingleObjectViewport?    _viewport;
    private CancellationTokenSource?  _cts;
    private bool                      _disposed;

    // Monotonic build counter + last-applied-generation-per-face-slot map, guarding against
    // out-of-order texture-patch delivery across successive LoadHudAsync calls. A live HUD
    // menu script fires ObjectUpdate/TerseObjectUpdate on every prim in the linkset for trivial
    // changes (highlight state, position nudges during expand/collapse), and HandlePrimChange
    // re-triggers a full retessellation -- debounced only 250ms -- on every one of them, so two
    // consecutive builds' texture downloads for the same face can both be in flight at once.
    // Network completion order isn't request order, so an older build's download for that
    // face's previous texture can finish and apply after a newer build's download for its
    // current (correct) texture already did, silently overwriting it -- the existing
    // ct.IsCancellationRequested check in LoadHudAsync's texturePatch callback only catches a
    // build cancelled before its own delivery, not this last-write-wins race between two builds
    // that were each still "current" when their own request went out. Keyed identically to
    // VkViewportControl's own _faceTextureSlots so the "current" generation for a given
    // face+slot only ever advances, never regresses.
    private long _hudBuildGeneration;
    private readonly System.Collections.Generic.Dictionary<(uint RootLocalId, int FaceIndex, TextureSlot Slot), long>
        _appliedTextureGeneration = new();

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
    /// <summary>Briefly true after a touch event is sent; drives a visual flash on the Touch button.</summary>
    [ObservableProperty] private bool      _isTouching;

    partial void OnSelectedHudChanged(HudEntry? value)
    {
        TouchCommand.NotifyCanExecuteChanged();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // A new HUD selection means the (LocalId, FaceIndex) key space starts over --
        // clear so a stale generation entry from a previous HUD can't suppress a
        // legitimate first patch for the newly selected one.
        lock (_appliedTextureGeneration) { _appliedTextureGeneration.Clear(); }

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
    /// Wire the viewport into this VM after the visual tree is ready. Called from the
    /// view's code-behind once DataContext is set.
    /// </summary>
    public void SetViewport(ISingleObjectViewport viewport)
    {
        _viewport = viewport;
        // A HUD is rendered isolated (no world context), same posture as PrimViewer/
        // AvatarViewer's own SetViewport -- Studio's flat, fixed lighting is what those two
        // already use. Sky's sun/ambient/atmosphere fields feed prim.frag's per-face lighting
        // unconditionally regardless of ShowSky, so Sky itself must also be set to Studio.
        _viewport.ShowSky = false;
        _viewport.Sky     = SkySettings.Studio;
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
        StatusText = $"Touching \"{SelectedHud.Name}\"…";
        _ = GrabFaceAsync(SelectedHud.LocalId, 0);
        _ = FlashTouchingAsync();
    }

    private bool CanTouch() => SelectedHud != null;

    private void OnFaceClicked(uint primLocalId, int faceIndex, FaceHitInfo hit)
    {
        if (SelectedHud == null) return;
        var sim = Client.Network.CurrentSim;
        string primLabel;
        if (sim != null && sim.ObjectsPrimitives.TryGetValue(primLocalId, out var clickedPrim))
        {
            var name = clickedPrim.Properties?.Name;
            var isRoot = primLocalId == SelectedHud.LocalId;
            var nameStr = string.IsNullOrWhiteSpace(name)
                ? (isRoot ? "root" : "child prim")
                : $"\"{name}\"";
            primLabel = nameStr;
        }
        else
        {
            primLabel = $"prim {primLocalId}";
        }
        StatusText = $"Touched face {faceIndex} of {primLabel}.";
        _ = GrabFaceAsync(primLocalId, faceIndex, hit);
        _ = FlashTouchingAsync();
    }

    /// <summary>
    /// Sets <see cref="IsTouching"/> to <see langword="true"/> for a short period so
    /// the Touch button can show a visual acknowledgement, then clears it.
    /// </summary>
    private async Task FlashTouchingAsync()
    {
        IsTouching = true;
        await Task.Delay(800).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() => IsTouching = false);
    }

    private async Task GrabFaceAsync(uint localId, int faceIndex, FaceHitInfo hit = default)
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

    [RelayCommand]
    private async Task DetachHud(HudEntry? entry)
    {
        if (entry == null) return;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        if (!sim.ObjectsPrimitives.TryGetValue(entry.LocalId, out var prim)) return;

        var itemId = CurrentOutfitFolder.GetAttachmentItemID(prim);
        if (itemId == UUID.Zero) return;

        var store = Client.Inventory.Store;
        if (store == null) return;
        if (!store.TryGetValue<InventoryItem>(itemId, out var item)) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _instance.COF.DetachAsync(item, cts.Token).ConfigureAwait(false);
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
        long myGeneration = ++_hudBuildGeneration;
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

            // Root prim + all linkset children. Root must come first so TessellateAsync picks
            // up the correct rootRot from prims[0]. Children are sorted by LocalID for a
            // deterministic tessellation order, since ObjectsPrimitives (a ConcurrentDictionary)
            // has undefined iteration order. For attachment linksets the SL server puts the
            // attachment point in Primitive.PrimData.State for ALL prims (root and children),
            // so State cannot be used to recover SL link numbers for attachments.
            var prims = sim.ObjectsPrimitives.Values
                .Where(p => p.LocalID == hud.LocalId || p.ParentID == hud.LocalId)
                .OrderBy(p => p.LocalID == hud.LocalId ? 0u : p.LocalID)
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

            // Without a texturePatch callback, PrimMeshBuilder.StreamTexturesAsync still
            // downloads and decodes every texture but just disposes each bitmap once decoded
            // instead of delivering it anywhere, leaving every HUD face untextured forever.
            // PrimViewerViewModel/AvatarViewerViewModel both already wire this.
            //
            // Drop patches that belong to a superseded build. HandlePrimChange re-triggers a
            // full retessellation on EVERY prim update in the linkset (debounced 250ms), so a
            // HUD with live menu/texture changes generates superseded builds far more often
            // than any other viewer panel -- without this guard, an in-flight download from an
            // old, already-cancelled build can still complete and get applied against a newer
            // submission's face list, landing the wrong (or partially-decoded) texture on a
            // face. Mirrors AvatarViewerViewModel's own identical guard exactly.
            var texturePatch = new Progress<SceneTexturePatch>(patch =>
            {
                if (ct.IsCancellationRequested)
                {
                    patch.Bitmap?.Dispose();
                    return;
                }

                // Reject a patch whose OWN build is still current but has been overtaken by a
                // later build's already-applied patch for this exact face+slot -- see the
                // _appliedTextureGeneration field comment. Without this, an older build's
                // download that finishes after a newer one's (network completion order isn't
                // request order) silently wins and sticks, since neither side's own ct is
                // cancelled at the time it's checked above.
                //
                // Progress<T>'s callback is expected to marshal back to the UI thread, but
                // that's Avalonia/BCL behavior this method doesn't control, so lock rather than
                // assume when touching this dictionary from a network-completion continuation.
                var key = (patch.RootLocalId, patch.FaceIndex, patch.Slot);
                lock (_appliedTextureGeneration)
                {
                    if (_appliedTextureGeneration.TryGetValue(key, out var lastApplied) && lastApplied > myGeneration)
                    {
                        patch.Bitmap?.Dispose();
                        return;
                    }
                    _appliedTextureGeneration[key] = myGeneration;
                }

                _viewport?.PatchSubmissionTexture(patch);
            });

            var submission = await _builder.BuildAsync(prims, hud.LocalId, hud.Name, progress, ct,
                                                        isHud: true, texturePatch: texturePatch)
                                           .ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                // Guard: if a newer build was started while this one was finishing, discard this
                // stale submission so it cannot replace fresh geometry with old -- mirrors
                // AvatarViewerViewModel's own identical guard.
                if (ct.IsCancellationRequested) return;
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
        if (prim.LocalID != SelectedHud.LocalId && prim.ParentID != SelectedHud.LocalId) return;

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
    /// Called from network threads, so all CTS mutation is marshalled to
    /// the UI thread to avoid races with OnSelectedHudChanged and Dispose.
    /// </summary>
    private void ScheduleRetessellation()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _retessCts?.Cancel();
            _retessCts?.Dispose();
            _retessCts = new CancellationTokenSource();
            var token = _retessCts.Token;
            _ = RetessellateAfterDelayAsync(token);
        });
    }

    private async Task RetessellateAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        if (ct.IsCancellationRequested) return;

        // Marshal back to the UI thread so that _cts is only ever mutated
        // from one thread, preventing ObjectDisposedException races where
        // OnSelectedHudChanged or another retessellation task disposes _cts
        // between our null-check and our Cancel()/Dispose() calls.
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var hud = SelectedHud;
            if (hud == null || ct.IsCancellationRequested) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            await LoadHudAsync(hud, _cts.Token).ConfigureAwait(false);
        });
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
        var suffix = raw[3..];
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
