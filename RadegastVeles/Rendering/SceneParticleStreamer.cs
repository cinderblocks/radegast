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
using System.Collections.Generic;
using System.Threading;
using LibreMetaverse;
using Vector3 = System.Numerics.Vector3;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Manages <see cref="ParticleViewerDriver"/> instances for every root prim in
/// the scene that carries a particle system, updating them as objects arrive,
/// change, or disappear from the simulator.
/// <para>
/// One driver per root linkset is kept alive for as long as the root is within
/// the stream radius and has an active particle emitter.  The viewport's single
/// particle render pass is shared across all drivers via
/// <see cref="GlViewportControl.SubmitParticles"/>; each driver overwrites the
/// previous submission on its tick, which is acceptable for a scene viewer
/// where multiple distant emitters blend together in the same pass.
/// </para>
/// </summary>
internal sealed class SceneParticleStreamer : IDisposable
{
    private readonly GridClient        _client;
    private readonly GlViewportControl _viewport;

    // rootLocalId → active driver
    private readonly ConcurrentDictionary<uint, ParticleViewerDriver> _drivers = new();

    // rootLocalIds that have been dirtied and need driver rebuild
    private readonly ConcurrentDictionary<uint, long> _dirty = new();

    private const int   DebounceMs      = 600;
    private const float MaxStreamRadius = 96f;

    private readonly Timer _debounceTimer;
    private bool           _disposed;

    public SceneParticleStreamer(GridClient client, GlViewportControl viewport)
    {
        _client   = client;
        _viewport = viewport;

        _debounceTimer = new Timer(_ => ProcessDirty(), null,
            Timeout.Infinite, Timeout.Infinite);
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>Called by the VM's ObjectUpdate handler (non-attachment prims only).</summary>
    public void OnObjectUpdate(Simulator sim, Primitive prim, bool isAttachment)
    {
        if (_disposed || isAttachment) return;
        if (sim != _client.Network.CurrentSim) return;

        var avatarPos = _client.Self.SimPosition;
        var rootId    = prim.ParentID == 0 ? prim.LocalID : prim.ParentID;

        if (!IsWithinRadius(prim.Position, avatarPos))
        {
            RemoveDriver(rootId);
            return;
        }

        // Only track linksets that actually have an emitter somewhere.
        if (!LinksetHasEmitter(rootId)) return;

        // Fast-path: if driver already running, just update its world position.
        if (prim.ParentID == 0 && _drivers.TryGetValue(rootId, out var existing))
        {
            existing.UpdateWorldPos(new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z));
            return;
        }

        EnqueueDirty(rootId);
    }

    /// <summary>Called by the VM's terse update handler for position-only changes.</summary>
    public void OnTerseObjectUpdate(Simulator sim, Primitive prim)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;
        var rootId = prim.ParentID == 0 ? prim.LocalID : prim.ParentID;
        if (prim.ParentID == 0 && _drivers.TryGetValue(rootId, out var driver))
            driver.UpdateWorldPos(new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z));
    }

    /// <summary>Seed the streamer with all currently known prims in the sim (called on scene open).</summary>
    public void SeedFromCurrentSim()
    {
        if (_disposed) return;
        var sim = _client.Network.CurrentSim;
        if (sim == null) return;
        var avatarPos = _client.Self.SimPosition;
        foreach (var kv in sim.ObjectsPrimitives)
        {
            var prim = kv.Value;
            if (prim.ParentID != 0) continue; // root prims only
            if (!IsWithinRadius(prim.Position, avatarPos)) continue;
            if (!LinksetHasEmitter(prim.LocalID)) continue;
            EnqueueDirty(prim.LocalID);
        }
    }

    /// <summary>Called by the VM's KillObject handler.</summary>
    public void OnKillObject(Simulator sim, uint localId)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;
        RemoveDriver(localId);
    }

    /// <summary>Stop all drivers and clear state (sim change / viewer close).</summary>
    public void Clear()
    {
        if (_disposed) return;
        _dirty.Clear();
        foreach (var kv in _drivers)
            kv.Value.Dispose();
        _drivers.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Dispose();
        Clear();
    }

    // ── Internal ──────────────────────────────────────────────────────────────────

    private void EnqueueDirty(uint rootId)
    {
        _dirty.TryAdd(rootId, Environment.TickCount64);
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void RemoveDriver(uint rootId)
    {
        _dirty.TryRemove(rootId, out _);
        if (_drivers.TryRemove(rootId, out var driver))
            driver.Dispose();
    }

    private void ProcessDirty()
    {
        if (_disposed) return;
        long now = Environment.TickCount64;
        foreach (var kv in _dirty)
        {
            if (now - kv.Value < DebounceMs) continue;
            _dirty.TryRemove(kv.Key, out _);
            RebuildDriver(kv.Key);
        }

        if (!_dirty.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void RebuildDriver(uint rootId)
    {
        if (_disposed) return;
        var sim = _client.Network.CurrentSim;
        if (sim == null) return;

        var prims = CollectLinkset(sim, rootId);
        if (prims.Count == 0 || !LinksetHasEmitter(prims))
        {
            RemoveDriver(rootId);
            return;
        }

        // Dispose existing driver if present.
        if (_drivers.TryRemove(rootId, out var old))
            old.Dispose();

        var root = prims[0];
        var worldPos = new Vector3(root.Position.X, root.Position.Y, root.Position.Z);

        var driver = new ParticleViewerDriver(_client, prims, (ulong)rootId, worldPos);
        driver.SetViewport(_viewport);
        driver.Start();
        _drivers[rootId] = driver;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static List<Primitive> CollectLinkset(Simulator sim, uint rootId)
    {
        var result = new List<Primitive>();
        if (!sim.ObjectsPrimitives.TryGetValue(rootId, out var root)) return result;
        result.Add(root);
        foreach (var kv in sim.ObjectsPrimitives)
        {
            if (kv.Value.ParentID == rootId)
                result.Add(kv.Value);
        }
        return result;
    }

    private bool LinksetHasEmitter(uint rootId)
    {
        var sim = _client.Network.CurrentSim;
        if (sim == null) return false;
        var prims = CollectLinkset(sim, rootId);
        return LinksetHasEmitter(prims);
    }

    private static bool LinksetHasEmitter(List<Primitive> prims)
    {
        foreach (var p in prims)
        {
            if (p.ParticleSys.Pattern != Primitive.ParticleSystem.SourcePattern.None ||
                p.ParticleSys.BurstPartCount > 0)
                return true;
        }
        return false;
    }

    private static bool IsWithinRadius(LibreMetaverse.Vector3 primPos, LibreMetaverse.Vector3 avatarPos)
    {
        float dx = primPos.X - avatarPos.X;
        float dy = primPos.Y - avatarPos.Y;
        float dz = primPos.Z - avatarPos.Z;
        return (dx * dx + dy * dy + dz * dz) <= MaxStreamRadius * MaxStreamRadius;
    }
}
