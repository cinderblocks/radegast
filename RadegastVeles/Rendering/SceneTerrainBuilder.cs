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
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using LibreMetaverse.Rendering;
using SkiaSharp;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Builds a <see cref="PrimRenderSubmission"/> that combines the terrain mesh
/// and an alpha-blended water plane for a given simulator region.
/// </summary>
internal sealed class SceneTerrainBuilder
{
    private readonly GridClient  _client;
    private readonly MeshFoundry _mesher = new();

    public SceneTerrainBuilder(GridClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Samples the heightmap for <paramref name="sim"/>, composites the terrain splat
    /// texture, adds a water plane, and packs everything into a single
    /// <see cref="PrimRenderSubmission"/> whose AABB covers terrain + water.
    /// <para>
    /// Pass <paramref name="regionOffset"/> to shift all face positions into world-space
    /// when building terrain for a neighboring region.  The offset is baked into each
    /// face's <see cref="PrimRenderFace.Transform"/> translation so the renderer does
    /// not need to know about region boundaries.
    /// </para>
    /// Returns <c>null</c> when patch data is absent.
    /// </summary>
    public async Task<PrimRenderSubmission?> RebuildAsync(
        Simulator? sim = null,
        Vector3    regionOffset = default,
        CancellationToken ct = default)
    {
        sim ??= _client.Network.CurrentSim;
        if (sim?.Terrain == null) return null;

        // ── Sample heightmap on a background thread ───────────────────────────
        var heightmap = await Task.Run(() => SampleHeightmap(sim), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // ── Fetch terrain detail textures + layer map ─────────────────────────
        // Success: real triplanar terrain (5 texture slots, see face assembly below).
        // Failure: fall back to a single flat height-gradient texture, rendered as an
        // ordinary (non-terrain) textured face — matches the pre-triplanar behaviour.
        SKBitmap[]? detail = null;
        SKBitmap?   layerMap = null;
        SKBitmap?   fallbackBmp = null;
        try
        {
            var layers = await TerrainSplat.BuildLayersAsync(
                _client,
                heightmap,
                [sim.TerrainDetail0, sim.TerrainDetail1, sim.TerrainDetail2, sim.TerrainDetail3],
                [sim.TerrainStartHeight00, sim.TerrainStartHeight01, sim.TerrainStartHeight10, sim.TerrainStartHeight11],
                [sim.TerrainHeightRange00, sim.TerrainHeightRange01, sim.TerrainHeightRange10, sim.TerrainHeightRange11],
                ct).ConfigureAwait(false);
            detail   = layers.Detail;
            layerMap = layers.LayerMap;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LibreMetaverse.Logger.Debug("SceneTerrainBuilder: TerrainSplat.BuildLayersAsync failed, falling back to SplatSimple.", ex);
            fallbackBmp = TerrainSplat.SplatSimple(heightmap);
        }

        ct.ThrowIfCancellationRequested();

        // ── Build terrain mesh ────────────────────────────────────────────────
        var (terrainFace, bMin, bMax) = await Task.Run(() => BuildTerrainMesh(heightmap), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // ── Assemble terrain face ─────────────────────────────────────────────
        // Water is rendered analytically by the full-screen water shader; no water
        // mesh is needed here.
        var offsetMat = regionOffset == Vector3.Zero
            ? Matrix4x4.Identity
            : Matrix4x4.CreateTranslation(regionOffset);

        var terrainCentroid = (bMin + bMax) * 0.5f + regionOffset;
        var terrainFaceOut  = new PrimRenderFace
        {
            Vertices    = PackVertices(terrainFace),
            Indices     = terrainFace.Indices.ToArray(),
            Color       = Vector4.One,
            Transform   = offsetMat,
            Fullbright  = false,
            Glow        = 0f,
            HasAlpha    = false,
            AlphaMode   = FaceAlphaMode.None,
            PrimLocalId = 0,
            FaceIndex   = 0,
            Centroid    = terrainCentroid,
            // Real triplanar terrain: reuse the PBR material texture slots to carry the
            // four raw detail textures + baked layer map (not PBR data) — see prim.frag's
            // terrain path. HasMaterial=false and IsPBR=false (default) keep the
            // legacy-material specular/normal-map lighting code from misinterpreting these
            // slots; prim.frag's uIsTerrain gate is the authoritative guard.
            IsTerrain                = detail != null,
            HasMaterial              = false,
            Texture                  = detail?[0] ?? fallbackBmp,
            NormalMapTexture         = detail?[1],
            SpecularMapTexture       = detail?[2],
            MetallicRoughnessTexture = detail?[3],
            EmissiveTexture          = layerMap,
        };

        bMin += regionOffset;
        bMax += regionOffset;

        // Diagnostic: reports this region's final world-space terrain footprint. Two adjacent
        // regions' touching edges should read the identical coordinate (e.g. one region's
        // bMax.X should equal its east neighbor's bMin.X) if the boundary-snap in
        // BuildTerrainMesh is taking effect and regionOffset is what's expected.
        LibreMetaverse.Logger.DebugLog(
            $"[TerrainBounds] sim={sim.Name} handle={sim.Handle} regionOffset={regionOffset} " +
            $"worldBounds=({bMin.X:F3},{bMin.Y:F3})-({bMax.X:F3},{bMax.Y:F3})");

        return new PrimRenderSubmission
        {
            Label     = "terrain",
            Faces     = [terrainFaceOut],
            BoundsMin = bMin,
            BoundsMax = bMax,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private float[,] SampleHeightmap(Simulator sim)
    {
        // hm[x, y] = height at region coordinate (x, y) -- kept as the array's storage
        // convention because TerrainSplat.BuildLayersAsync/SplatSimple both read heightmap[x, y]
        // directly and its splat-texture rotation is already tuned to match. SculptMesh itself
        // reads zMap[y, x] for the vertex it places at local (x, y) (PrimMesher/SculptMesh.cs:138)
        // -- i.e. it consumes this same array transposed -- so BlendEdges below has to target its
        // writes at the transposed slot a given edge actually lands on after that read, not the
        // (x, y) slot that matches the region-coordinate arguments being sampled.
        var hm = new float[256, 256];
        for (int x = 0; x < 256; x++)
        {
            for (int y = 0; y < 256; y++)
            {
                if (TerrainSeamHelper.TryGetHeight(sim, x, y, out float h))
                    hm[x, y] = h;
            }
        }

        BlendEdges(sim, hm);
        return hm;
    }

    /// <summary>
    /// Sews this region's boundary rows/columns to whichever neighbors are currently connected
    /// and have received their own edge patch data, so adjacent regions' terrain meshes agree at
    /// the shared edge instead of showing a visible crack (mirrors the edge-stitching real SL
    /// viewers do). The grid is a uniform 1m/vertex mesh with no LOD, so a boundary cell here and
    /// the corresponding cell on the neighbor's near edge are the same world-space point --
    /// replacing both with their symmetric average, <c>(own + neighbor) / 2</c>, is what makes
    /// this self-consistent without an ownership/arbitration scheme: once both sides have each
    /// other's edge data and both apply the same averaging, both edges independently converge to
    /// the identical height.
    /// <para>
    /// Neighbor data is not guaranteed to have arrived yet -- both sides stream in patches
    /// asynchronously and independently. A cell with no available neighbor sample is left at its
    /// own height; the seam closes on a later rebuild once that data streams in
    /// (<c>RefreshNeighborTerrainAsync</c> already re-triggers per patch, and
    /// <c>OnLandPatchReceived</c>'s cross-edge trigger further ensures a boundary patch's arrival
    /// also rebuilds the neighbor across that edge).
    /// </para>
    /// <para>
    /// Corner cells get touched by both of their adjacent edge loops in sequence rather than a
    /// true 3/4-way average against every touching region -- for the single shared vertex this
    /// affects, that's an acceptable v1 simplification; it still pulls corners toward agreement
    /// with both cardinal neighbors, just not with perfectly symmetric weighting when a diagonal
    /// neighbor's data differs from both.
    /// </para>
    /// </summary>
    private void BlendEdges(Simulator sim, float[,] hm)
    {
        // hm is stored as hm[x, y] = height at region (x, y) (see SampleHeightmap), but
        // SculptMesh reads it transposed -- the vertex it places at mesh-local (x, y) gets
        // zMap[y, x] = hm[y, x]. So the mesh's actual north edge (all x, mesh-local y=255) is
        // *read* from hm[255, x], not hm[x, 255] -- every write below targets the transposed slot
        // a given edge lands on after that read, not the (x, y) slot matching the region
        // coordinates being sampled/found. Getting this backwards doesn't throw or produce an
        // obviously wrong shape; it silently blends each mesh edge against the WRONG cardinal
        // neighbor (north against east's data, east against north's, south against west's, west
        // against south's), which still "succeeds" (no gap in the vertex grid, since
        // BuildTerrainMesh's boundary snap is unconditional) while leaving the real seam's
        // heights mismatched -- exactly what let the vertex-snap fix close the gap geometrically
        // while a visible height-mismatch crack (water/sky showing through) remained.

        // TerrainSeamHelper.TryGetHeight(sim, a, b) itself samples transposed relative to its
        // own (x, y)-looking parameter names: LibreMetaverse.TerrainManager.DecompressLand stores
        // each patch at Terrain[header.Y * 16 + header.X] with intra-patch data laid out
        // local_y * 16 + local_x, but TryGetHeight computes patchNr = (x/16)*16 + y/16 and indexes
        // Data[(x%16)*16 + y%16] -- both halves swapped, which compose into a single clean
        // transpose: TryGetHeight(sim, a, b) actually returns the height DecompressLand associated
        // with region coordinate (b, a), not (a, b). SampleHeightmap's self-fill loop and
        // BuildTerrainMesh's zMap[y, x] read happen to compose with this into a second transpose
        // that cancels it out -- confirmed empirically (no one has ever seen mirrored terrain) --
        // so that path is left alone. But each neighbor call below is a *bare* TryGetHeight call
        // with no such second transpose to cancel it, so it needs its own (a, b) arguments
        // pre-swapped to land on the neighbor's true matching edge. Confirmed via
        // [TerrainSeamSample]: Citrago's own north-edge height (76.826) closely matched Diloba's
        // own south-edge height (75.636, from Diloba's own [TerrainSeamSample] line) -- i.e. the
        // two sides' RAW heights already agree at the true shared point -- but Citrago's
        // (unswapped) TryGetHeight(north, x, 0) call was reading an unrelated point (34.221), not
        // that value.

        // North edge (region y=255) <-> neighbor's south edge (region y=0).
        var north = TerrainSeamHelper.FindNeighborSim(_client, sim, 0, 256);
        if (north != null)
        {
            for (int x = 0; x < 256; x++)
                if (TerrainSeamHelper.TryGetHeight(north, 0, x, out float nh))
                {
                    if (x == 128)
                        LibreMetaverse.Logger.DebugLog(
                            $"[TerrainSeamSample] sim={sim.Name} edge=North own={hm[255, x]:F3} neighbor={north.Name}:{nh:F3}");
                    hm[255, x] = (hm[255, x] + nh) * 0.5f;
                }
        }

        // South edge (region y=0) <-> neighbor's north edge (region y=255).
        var south = TerrainSeamHelper.FindNeighborSim(_client, sim, 0, -256);
        if (south != null)
        {
            for (int x = 0; x < 256; x++)
                if (TerrainSeamHelper.TryGetHeight(south, 255, x, out float sh))
                {
                    if (x == 128)
                        LibreMetaverse.Logger.DebugLog(
                            $"[TerrainSeamSample] sim={sim.Name} edge=South own={hm[0, x]:F3} neighbor={south.Name}:{sh:F3}");
                    hm[0, x] = (hm[0, x] + sh) * 0.5f;
                }
        }

        // East edge (region x=255) <-> neighbor's west edge (region x=0).
        var east = TerrainSeamHelper.FindNeighborSim(_client, sim, 256, 0);
        if (east != null)
        {
            for (int y = 0; y < 256; y++)
                if (TerrainSeamHelper.TryGetHeight(east, y, 0, out float eh))
                {
                    if (y == 128)
                        LibreMetaverse.Logger.DebugLog(
                            $"[TerrainSeamSample] sim={sim.Name} edge=East own={hm[y, 255]:F3} neighbor={east.Name}:{eh:F3}");
                    hm[y, 255] = (hm[y, 255] + eh) * 0.5f;
                }
        }

        // West edge (region x=0) <-> neighbor's east edge (region x=255).
        var west = TerrainSeamHelper.FindNeighborSim(_client, sim, -256, 0);
        if (west != null)
        {
            for (int y = 0; y < 256; y++)
                if (TerrainSeamHelper.TryGetHeight(west, y, 255, out float wh))
                {
                    if (y == 128)
                        LibreMetaverse.Logger.DebugLog(
                            $"[TerrainSeamSample] sim={sim.Name} edge=West own={hm[y, 0]:F3} neighbor={west.Name}:{wh:F3}");
                    hm[y, 0] = (hm[y, 0] + wh) * 0.5f;
                }
        }
    }

    private (Face face, Vector3 bMin, Vector3 bMax) BuildTerrainMesh(float[,] heightmap)
    {
        // MeshFoundry.TerrainMesh's underlying SculptMesh spaces vertices by
        // xStep = (xEnd-xBegin)/(numXElements-1); with a 0..255 span that's exactly 1m/vertex,
        // so the last (index-255) vertex lands at local X/Y = 255.0. A neighboring region is
        // placed via a flat +256m regionOffset translation (its own true world width), so that
        // neighbor's own first vertex lands at world 256.0 -- a full 1m gap between every pair of
        // adjacent regions' terrain meshes, everywhere, independent of whether their height data
        // agrees. BlendEdges (below) only equalizes height VALUES at the boundary; it can't close
        // a gap between two vertices that are never placed at the same point.
        //
        // Snapping just the outermost row/column to 256.0 below (instead of stretching xEnd/yEnd
        // to 256 for the whole mesh) keeps interior spacing at exactly 1m/vertex -- prims and
        // avatars are positioned by their true region coordinates, so stretching every vertex
        // would leave the terrain under them off by up to ~0.5m at region center, growing to 1m
        // at the far edge (max at 256/255 ratio). Confining the stretch to the single outermost
        // 1m strip, where BlendEdges has already made both sides agree on height, tiles with zero
        // gap and zero interior misalignment.
        var face = _mesher.TerrainMesh(heightmap, 0f, 255f, 0f, 255f);

        int n = heightmap.GetLength(0); // 256; SculptMesh emits vertices row-major, index = y*n + x
        for (int i = 0; i < face.Vertices.Count; i++)
        {
            var v = face.Vertices[i];
            int x = i % n, y = i / n;
            float px = x == n - 1 ? 256f : v.Position.X;
            float py = y == n - 1 ? 256f : v.Position.Y;
            v.Position = new LibreMetaverse.Vector3(px, py, v.Position.Z);
            face.Vertices[i] = v;
        }

        // Diagnostic: bounds alone (logged below in RebuildAsync) only report the two extreme
        // vertices and would read post-snap regardless of whether interior/near-boundary
        // vertices are actually where expected -- this checks the snap reached the packed
        // buffer at the one place that would show it (the second-to-last vertex should stay at
        // local 254.0; only the last should have moved to 256.0).
        var atMidY = face.Vertices[128 * n + (n - 2)].Position;   // x=254, y=128
        var atMidYLast = face.Vertices[128 * n + (n - 1)].Position; // x=255, y=128
        LibreMetaverse.Logger.DebugLog(
            $"[TerrainSnapCheck] x=254 pos=({atMidY.X:F3},{atMidY.Y:F3}) " +
            $"x=255 pos=({atMidYLast.X:F3},{atMidYLast.Y:F3})");

        var bMin = new Vector3(float.MaxValue);
        var bMax = new Vector3(float.MinValue);
        foreach (var v in face.Vertices)
        {
            var p = new Vector3(v.Position.X, v.Position.Y, v.Position.Z);
            bMin = Vector3.Min(bMin, p);
            bMax = Vector3.Max(bMax, p);
        }
        if (face.Vertices.Count == 0) { bMin = Vector3.Zero; bMax = new Vector3(256f, 256f, 0f); }

        return (face, bMin, bMax);
    }

    private static float[] PackVertices(Face face)
    {
        var verts = new float[face.Vertices.Count * 12];
        for (int i = 0; i < face.Vertices.Count; i++)
        {
            var v = face.Vertices[i];
            int o = i * 12;
            verts[o + 0] = v.Position.X;
            verts[o + 1] = v.Position.Y;
            verts[o + 2] = v.Position.Z;
            verts[o + 3] = v.Normal.X;
            verts[o + 4] = v.Normal.Y;
            verts[o + 5] = v.Normal.Z;
            verts[o + 6] = v.TexCoord.X;
            verts[o + 7] = v.TexCoord.Y;
            // Tangent left as zero — terrain uses no normal maps.
        }
        return verts;
    }

}
