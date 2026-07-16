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

using System.Collections.Generic;
using System.Numerics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Coarse uniform spatial hash grid over scene-object world AABBs, used as a fast
/// pre-filter in front of the exact per-face frustum test in <see cref="GlViewportControl"/>.
/// <para>
/// World space is unbounded/continuous across neighbour regions (see
/// <c>SceneObjectStreamer.RegionOffset</c>), so cells are a sparse hash keyed by integer
/// cell coordinates rather than a fixed-size array.
/// </para>
/// <para>
/// GL-thread-only, like the <c>_sceneObjects</c> dictionary it parallels in
/// <see cref="GlViewportControl"/> — no locking. Every mutating call (<see cref="Upsert"/>,
/// <see cref="Remove"/>, <see cref="Clear"/>) and <see cref="QueryVisible"/> must happen on
/// the render thread.
/// </para>
/// <para>
/// This class is purely an optimization: <see cref="QueryVisible"/> is conservative
/// (never omits a scene key whose bounds actually overlap the frustum), so callers must
/// still run the exact per-face AABB test on survivors — the grid can only skip work for
/// objects it is certain are outside the frustum, never change the final visible set.
/// </para>
/// </summary>
internal sealed class SceneSpatialGrid
{
    private const float CellSize = 16f;

    // Objects whose AABB spans more than this on any axis (megaprim builds, region-spanning
    // skyboxes) skip per-cell bucketing entirely and go into _oversized instead, so one huge
    // object can't register itself into thousands of cells. Tested individually every query —
    // there are very few such objects in any real scene, so this stays cheap.
    private const float MaxSpanForGridding = 8f * CellSize;

    private readonly struct GridEntry
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;
        public readonly List<(int x, int y, int z)>? Cells; // null when Oversized
        public readonly bool Oversized;

        public GridEntry(Vector3 min, Vector3 max, List<(int, int, int)>? cells, bool oversized)
        {
            Min = min; Max = max; Cells = cells; Oversized = oversized;
        }
    }

    private readonly Dictionary<(int x, int y, int z), List<ulong>> _cells = new();
    private readonly Dictionary<ulong, GridEntry> _objects = new();
    private readonly List<ulong> _oversized = new();

    /// <summary>
    /// Inserts or updates the grid entry for <paramref name="sceneKey"/> with the given
    /// world-space AABB. A no-op if the object is already registered with an identical
    /// cell range (cheap bounds compare), which avoids redundant rebucketing for objects
    /// that moved but stayed within the same cell(s).
    /// </summary>
    public void Upsert(ulong sceneKey, Vector3 min, Vector3 max)
    {
        if (_objects.TryGetValue(sceneKey, out var existing) && existing.Min == min && existing.Max == max)
            return;

        Remove(sceneKey);

        var span = max - min;
        bool oversized = span.X > MaxSpanForGridding || span.Y > MaxSpanForGridding || span.Z > MaxSpanForGridding;
        if (oversized)
        {
            _oversized.Add(sceneKey);
            _objects[sceneKey] = new GridEntry(min, max, null, oversized: true);
            return;
        }

        var cMin = CellOf(min);
        var cMax = CellOf(max);
        var cells = new List<(int, int, int)>();
        for (int x = cMin.x; x <= cMax.x; x++)
        for (int y = cMin.y; y <= cMax.y; y++)
        for (int z = cMin.z; z <= cMax.z; z++)
        {
            var key = (x, y, z);
            if (!_cells.TryGetValue(key, out var list))
                _cells[key] = list = new List<ulong>();
            list.Add(sceneKey);
            cells.Add(key);
        }
        _objects[sceneKey] = new GridEntry(min, max, cells, oversized: false);
    }

    /// <summary>Removes <paramref name="sceneKey"/> from the grid, if present.</summary>
    public void Remove(ulong sceneKey)
    {
        if (!_objects.Remove(sceneKey, out var entry)) return;

        if (entry.Oversized)
        {
            _oversized.Remove(sceneKey);
            return;
        }

        foreach (var cell in entry.Cells!)
        {
            if (_cells.TryGetValue(cell, out var list))
            {
                list.Remove(sceneKey);
                if (list.Count == 0) _cells.Remove(cell);
            }
        }
    }

    /// <summary>Removes every entry from the grid.</summary>
    public void Clear()
    {
        _cells.Clear();
        _objects.Clear();
        _oversized.Clear();
    }

    /// <summary>
    /// Populates <paramref name="results"/> (cleared first) with the set of scene keys
    /// whose grid cell(s) — or, for oversized objects, whose own AABB — intersect
    /// <paramref name="frustum"/>. Coarse and conservative: callers must still run the
    /// exact per-face AABB test on survivors.
    /// <para>
    /// Takes a caller-owned set rather than returning an internally-reused one so that
    /// multiple queries against different frustums in the same frame (e.g. the main
    /// camera and a mirrored water-reflection camera) can't alias and clobber each other.
    /// Callers should keep a persistent <see cref="HashSet{T}"/> per concurrently-needed
    /// query rather than allocating one each frame.
    /// </para>
    /// </summary>
    public void QueryVisible(in Frustum frustum, HashSet<ulong> results)
    {
        results.Clear();

        foreach (var (cell, keys) in _cells)
        {
            var min = new Vector3(cell.x * CellSize, cell.y * CellSize, cell.z * CellSize);
            var max = min + new Vector3(CellSize, CellSize, CellSize);
            if (!FrustumCuller.IntersectsAabb(frustum, min, max)) continue;
            foreach (var key in keys) results.Add(key);
        }

        foreach (var key in _oversized)
        {
            var entry = _objects[key];
            if (FrustumCuller.IntersectsAabb(frustum, entry.Min, entry.Max))
                results.Add(key);
        }
    }

    private static (int x, int y, int z) CellOf(Vector3 p) => (
        (int)System.MathF.Floor(p.X / CellSize),
        (int)System.MathF.Floor(p.Y / CellSize),
        (int)System.MathF.Floor(p.Z / CellSize));
}
