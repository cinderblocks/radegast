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
using LibreMetaverse;
using Vector3 = System.Numerics.Vector3;

namespace Radegast.Veles.Rendering;

/// <summary>
/// One local point light collected from an in-world prim's "Light" extra params
/// (<see cref="Primitive.LightData"/>). <see cref="Color"/> is pre-multiplied by
/// <see cref="Intensity"/> so the renderer can use it directly as a light colour.
/// </summary>
public readonly record struct LocalLight(
    Vector3 WorldPosition,
    Vector3 Color,
    float   Intensity,
    float   Radius,
    float   Falloff);

/// <summary>
/// Tracks root prims carrying SL "Light" extra params (lamps, torches, glow
/// props, ...) within stream radius of the avatar, feeding the local-light
/// forward-lighting and shadow-casting passes in <see cref="GlViewportControl"/>.
/// <para>
/// Modeled on <see cref="SceneParticleStreamer"/>: current-sim-only, event-driven
/// off the same ObjectUpdate/TerseObjectUpdate/KillObject hooks. Unlike particle
/// systems, SL light params can technically be set on any prim in a linkset, but
/// a child prim's world position requires composing the full linkset transform
/// (position *and* rotation of every ancestor). Root-only keeps this streamer
/// simple and covers the overwhelming common case — free-standing lamps/torches/
/// glow props are almost always the linkset root themselves.
/// </para>
/// </summary>
public sealed class SceneLightStreamer : IDisposable
{
    private readonly GridClient _client;

    // rootLocalId -> light state. Replaced wholesale on update (cheap value-type
    // swap; no lock needed since ConcurrentDictionary indexer writes are atomic).
    private readonly ConcurrentDictionary<uint, LocalLight> _lights = new();

    private const float MaxStreamRadius = 32f;

    private bool _disposed;

    public SceneLightStreamer(GridClient client) => _client = client;

    /// <summary>Snapshot of all currently tracked local lights.</summary>
    public ICollection<LocalLight> Lights => _lights.Values;

    /// <summary>Called by the VM's ObjectUpdate handler (non-attachment prims only).</summary>
    public void OnObjectUpdate(Simulator sim, Primitive prim, bool isAttachment)
    {
        if (_disposed || isAttachment) return;
        if (sim != _client.Network.CurrentSim) return;
        if (prim.ParentID != 0) return; // root prims only, see class remarks

        if (prim.Light == null || !IsWithinRadius(prim.Position, _client.Self.SimPosition))
        {
            _lights.TryRemove(prim.LocalID, out _);
            return;
        }

        _lights[prim.LocalID] = ToLocalLight(prim);
    }

    /// <summary>Called by the VM's terse update handler for position-only changes.</summary>
    public void OnTerseObjectUpdate(Simulator sim, Primitive prim)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;
        if (prim.ParentID != 0) return;
        if (!_lights.TryGetValue(prim.LocalID, out var existing)) return;

        if (!IsWithinRadius(prim.Position, _client.Self.SimPosition))
        {
            _lights.TryRemove(prim.LocalID, out _);
            return;
        }

        _lights[prim.LocalID] = existing with
        {
            WorldPosition = new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z)
        };
    }

    /// <summary>Called by the VM's KillObject handler.</summary>
    public void OnKillObject(Simulator sim, uint localId)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;
        _lights.TryRemove(localId, out _);
    }

    /// <summary>Seed the streamer with all currently known root prims in the sim (called on scene open).</summary>
    public void SeedFromCurrentSim()
    {
        if (_disposed) return;
        var sim = _client.Network.CurrentSim;
        if (sim == null) return;
        var avatarPos = _client.Self.SimPosition;
        foreach (var prim in sim.ObjectsPrimitives.Values)
        {
            if (prim.ParentID != 0) continue;
            if (prim.Light == null) continue;
            if (!IsWithinRadius(prim.Position, avatarPos)) continue;
            _lights[prim.LocalID] = ToLocalLight(prim);
        }
    }

    /// <summary>Stop tracking and clear state (sim change / viewer close).</summary>
    public void Clear()
    {
        if (_disposed) return;
        _lights.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }

    private static LocalLight ToLocalLight(Primitive prim)
    {
        var l = prim.Light!;
        return new LocalLight(
            WorldPosition: new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z),
            Color:         new Vector3(l.Color.R, l.Color.G, l.Color.B) * l.Intensity,
            Intensity:     l.Intensity,
            // Floored well above GlViewportControl.PointShadowNear (0.1m) so a light's
            // radius can never collapse the point-shadow perspective's near/far range.
            Radius:        MathF.Max(1.0f, l.Radius),
            Falloff:       l.Falloff);
    }

    private static bool IsWithinRadius(LibreMetaverse.Vector3 primPos, LibreMetaverse.Vector3 avatarPos)
    {
        float dx = primPos.X - avatarPos.X;
        float dy = primPos.Y - avatarPos.Y;
        float dz = primPos.Z - avatarPos.Z;
        return (dx * dx + dy * dy + dz * dz) <= MaxStreamRadius * MaxStreamRadius;
    }
}
