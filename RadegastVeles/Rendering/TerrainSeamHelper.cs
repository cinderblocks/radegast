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

using LibreMetaverse;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Small helpers shared by <see cref="SceneTerrainBuilder"/>'s edge-blend pass: locating the
/// simulator adjacent to a given region, and sampling a single heightmap cell with the same
/// indexing convention <see cref="SceneTerrainBuilder"/> itself uses.
/// </summary>
internal static class TerrainSeamHelper
{
    /// <summary>
    /// Resolves the simulator across a given edge/corner. <paramref name="dx"/>/<paramref name="dy"/>
    /// are region-grid steps in metres (SL regions are 256m square, and <see cref="Simulator.Handle"/>
    /// encodes grid position in metres) -- e.g. (0, 256) for the north neighbor, (256, -256) for the
    /// south-east diagonal neighbor. Returns null when that neighbor isn't currently connected/tracked.
    /// </summary>
    public static Simulator? FindNeighborSim(GridClient client, Simulator sim, int dx, int dy)
    {
        Utils.LongToUInts(sim.Handle, out uint sx, out uint sy);
        long nx = (long)sx + dx;
        long ny = (long)sy + dy;
        if (nx < 0 || ny < 0 || nx > uint.MaxValue || ny > uint.MaxValue) return null;
        ulong neighborHandle = Utils.UIntsToLong((uint)nx, (uint)ny);

        lock (client.Network.Simulators)
        {
            foreach (var s in client.Network.Simulators)
                if (s.Handle == neighborHandle) return s;
        }
        return null;
    }

    /// <summary>
    /// Bounds-checked single-cell heightmap sample using the same patch/cell indexing as
    /// <see cref="SceneTerrainBuilder.SampleHeightmap"/>. Returns false (rather than a default
    /// height) when the owning patch hasn't arrived yet, so callers can distinguish "known zero
    /// height" from "unknown" and fall back appropriately.
    /// </summary>
    public static bool TryGetHeight(Simulator sim, int x, int y, out float height)
    {
        height = 0f;
        if (x < 0 || x > 255 || y < 0 || y > 255) return false;
        var terrain = sim.Terrain;
        if (terrain == null) return false;
        int patchNr = (x / 16) * 16 + y / 16;
        if (patchNr < 0 || patchNr >= terrain.Length) return false;
        var patch = terrain[patchNr];
        if (patch?.Data == null) return false;
        height = patch.Data[(x % 16) * 16 + y % 16];
        return true;
    }
}
