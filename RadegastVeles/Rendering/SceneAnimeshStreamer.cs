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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using LibreMetaverse.Rendering;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Drives LBS animation for all standalone rigged mesh (animesh) objects visible
/// in the scene viewer.
/// <para>
/// Subscribes to <see cref="SceneObjectStreamer.ObjectBuilt"/> to capture per-face
/// skin data, then runs a 30 Hz loop that calls
/// <see cref="LibreMetaverse.Animesh.AnimeshManager.Update"/> and ticks every
/// active <see cref="SceneAnimeshAnimator"/>.  An animator is created the first
/// time a built skin submission is matched with an
/// <see cref="LibreMetaverse.Animesh.AnimeshPlayer"/> from the manager.
/// </para>
/// </summary>
internal sealed class SceneAnimeshStreamer : IDisposable
{
    private readonly GridClient           _client;
    private readonly GlViewportControl    _viewport;
    private readonly SceneObjectStreamer  _objectStreamer;
    private readonly LindenSkeleton?      _skeleton;

    // rootLocalId → per-face skin data extracted at build time.
    private readonly ConcurrentDictionary<uint, AnimeshFaceSkinData[]> _builtSkins = new();

    // rootLocalId → active animator (created once player + skins are both available).
    private readonly ConcurrentDictionary<uint, SceneAnimeshAnimator> _animators = new();

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public SceneAnimeshStreamer(
        GridClient client,
        GlViewportControl viewport,
        SceneObjectStreamer objectStreamer)
    {
        _client        = client;
        _viewport      = viewport;
        _objectStreamer = objectStreamer;

        _skeleton = TryLoadSkeleton();

        _objectStreamer.ObjectBuilt += OnObjectBuilt;

        if (_skeleton != null)
            _ = RunAsync(_cts.Token);
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>Called by the VM's KillObject handler.</summary>
    public void OnKillObject(Simulator sim, uint localId)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;

        _builtSkins.TryRemove(localId, out _);
        _animators.TryRemove(localId, out _);
    }

    /// <summary>Stop all animators and clear state (sim change / viewer close).</summary>
    public void Clear()
    {
        _builtSkins.Clear();
        _animators.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _objectStreamer.ObjectBuilt -= OnObjectBuilt;
        _cts.Cancel();
        _cts.Dispose();
        Clear();
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private void OnObjectBuilt(uint rootLocalId, PrimRenderSubmission submission)
    {
        if (_disposed || _skeleton == null) return;
        if (submission.AnimeshSkinData.Length == 0) return;

        _builtSkins[rootLocalId] = submission.AnimeshSkinData;

        // Remove any stale animator from a previous build of the same object.
        _animators.TryRemove(rootLocalId, out _);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 30.0));
        var sw    = Stopwatch.StartNew();
        float prev = 0f;
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                float now = (float)sw.Elapsed.TotalSeconds;
                float dt  = Math.Min(now - prev, 0.1f);
                prev = now;
                Tick(dt);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Tick(float dt)
    {
        if (_disposed) return;

        // Advance all animesh players in lockstep.
        _client.Animesh.Update(dt);

        // For each player the manager knows about, ensure we have an animator.
        var sim = _client.Network.CurrentSim;
        foreach (var player in _client.Animesh.AllPlayers)
        {
            var objectId = player.ObjectID;

            if (_animators.ContainsKey(ResolveLocalId(sim, objectId)))
                continue;

            uint localId = ResolveLocalId(sim, objectId);
            if (localId == 0) continue;

            if (!_builtSkins.TryGetValue(localId, out var skins)) continue;

            var animator = new SceneAnimeshAnimator(localId, _viewport, player, _skeleton!, skins);
            _animators[localId] = animator;
        }

        // Tick all active animators.
        foreach (var kv in _animators)
            kv.Value.AnimTick();
    }

    /// <summary>
    /// Finds the local prim ID for <paramref name="objectId"/> by scanning the
    /// current sim's prim table.  Returns 0 when not found.
    /// </summary>
    private static uint ResolveLocalId(Simulator? sim, UUID objectId)
    {
        if (sim == null) return 0;
        foreach (var kv in sim.ObjectsPrimitives)
        {
            if (kv.Value.ID == objectId) return kv.Key;
        }
        return 0;
    }

    /// <summary>
    /// Loads the avatar skeleton once.  Returns null on failure so animesh rendering
    /// degrades gracefully rather than throwing on startup.
    /// </summary>
    private static LindenSkeleton? TryLoadSkeleton()
    {
        try
        {
            return LindenAvatarDefinition.Load().Skeleton;
        }
        catch
        {
            return null;
        }
    }
}
