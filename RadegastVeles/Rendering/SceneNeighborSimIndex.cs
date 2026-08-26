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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using LibreMetaverse;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Shared registry mapping simulators to a stable per-session index, assigned once the first time
/// a simulator is seen and never reassigned afterwards -- regardless of whether that simulator is
/// later "current" or a neighbor. Scene keys built from this index encode the simulator in their
/// upper 32 bits and a LocalID in the lower 32 bits, so objects/avatars/terrain with the same
/// LocalID in different regions never collide.
/// <para>
/// Indices are deliberately <em>not</em> tied to "0 = current sim": an earlier version of this
/// class resolved index 0 dynamically against whatever <see cref="Network.CurrentSim"/> was at
/// call time, which made a region crossing change every already-tracked object's key out from
/// under it the moment <c>CurrentSim</c> flipped (the object stays keyed under its old index
/// forever; the code that would look it up computes a different new key and never finds it --
/// a permanent leak, not the transient one it was meant to fix). Permanent indices mean a
/// simulator's scene keys never change for as long as it stays tracked (until <see cref="Clear"/>),
/// so a region crossing needs to correct only each already-built object's baked world-space
/// transform (see <c>SceneObjectStreamer.RebaseAllForRegionPromotion</c>) -- never its key.
/// "Which sim is current" is tracked separately, in the region-offset math
/// (<c>SceneObjectStreamer.RegionOffset</c>/<c>ApplyRegionOffset</c>), which is unrelated to this
/// index and is recomputed fresh against live <see cref="Network.CurrentSim"/> on every call.
/// </para>
/// </summary>
internal sealed class SceneNeighborSimIndex
{
    private int _nextIndex;
    private readonly ConcurrentDictionary<ulong, uint> _indexByHandle = new(); // handle -> index
    private readonly ConcurrentDictionary<uint, Simulator> _simByIndex = new(); // index -> sim

    /// <summary>Returns this simulator's permanent index, assigning one on first use.</summary>
    public uint GetSimIndex(Simulator sim)
    {
        var handle = sim.Handle;
        if (_indexByHandle.TryGetValue(handle, out uint existing))
        {
            _simByIndex[existing] = sim; // refresh in case a new Simulator instance replaced this handle
            return existing;
        }
        uint newIdx = (uint)Interlocked.Increment(ref _nextIndex) - 1;
        uint idx    = _indexByHandle.GetOrAdd(handle, newIdx);
        _simByIndex[idx] = sim;
        return idx;
    }

    public ulong MakeSceneKey(Simulator sim, uint localId)
        => ((ulong)GetSimIndex(sim) << 32) | localId;

    public Simulator? SimForSceneKey(ulong key)
    {
        uint simIndex = (uint)(key >> 32);
        return _simByIndex.TryGetValue(simIndex, out var s) ? s : null;
    }

    public static uint LocalIdForSceneKey(ulong key) => (uint)(key & 0xFFFF_FFFF);

    /// <summary>True if <paramref name="handle"/> has already been assigned an index.</summary>
    public bool IsTracked(ulong handle) => _indexByHandle.ContainsKey(handle);

    /// <summary>Every simulator currently tracked, for bulk iteration (cleanup, rebase).</summary>
    public IReadOnlyDictionary<uint, Simulator> TrackedSims => _simByIndex;

    /// <summary>
    /// Resets the registry. Call only on a full clear (teleport / crossing into an untracked
    /// region) -- never on a lightweight neighbor-promotion crossing, which depends on existing
    /// index assignments surviving.
    /// </summary>
    public void Clear()
    {
        _indexByHandle.Clear();
        _simByIndex.Clear();
        Interlocked.Exchange(ref _nextIndex, 0);
    }

    /// <summary>Drops the index assignment for a single simulator (e.g. on <see cref="Network.SimDisconnected"/>).</summary>
    public void Forget(ulong handle)
    {
        if (_indexByHandle.TryRemove(handle, out uint idx))
            _simByIndex.TryRemove(idx, out _);
    }
}
