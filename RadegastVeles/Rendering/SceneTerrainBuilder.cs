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
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using SkiaSharp;
using TkVector3 = OpenTK.Mathematics.Vector3;
using TkVector4 = OpenTK.Mathematics.Vector4;
using TkMatrix4 = OpenTK.Mathematics.Matrix4;

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
        TkVector3  regionOffset = default,
        CancellationToken ct = default)
    {
        sim ??= _client.Network.CurrentSim;
        if (sim?.Terrain == null) return null;

        // ── Sample heightmap on a background thread ───────────────────────────
        var heightmap = await Task.Run(() => SampleHeightmap(sim), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // ── Composite terrain texture ─────────────────────────────────────────
        SKBitmap splatBmp;
        try
        {
            splatBmp = await TerrainSplat.SplatAsync(
                _client,
                heightmap,
                [sim.TerrainDetail0, sim.TerrainDetail1, sim.TerrainDetail2, sim.TerrainDetail3],
                [sim.TerrainStartHeight00, sim.TerrainStartHeight01, sim.TerrainStartHeight10, sim.TerrainStartHeight11],
                [sim.TerrainHeightRange00, sim.TerrainHeightRange01, sim.TerrainHeightRange10, sim.TerrainHeightRange11],
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            splatBmp = TerrainSplat.SplatSimple(heightmap);
        }

        ct.ThrowIfCancellationRequested();

        // ── Build terrain mesh ────────────────────────────────────────────────
        var (terrainFace, bMin, bMax) = await Task.Run(() => BuildTerrainMesh(heightmap), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // ── Assemble terrain face ─────────────────────────────────────────────
        // Water is rendered analytically by the full-screen water shader; no water
        // mesh is needed here.
        var offsetMat = regionOffset == TkVector3.Zero
            ? TkMatrix4.Identity
            : TkMatrix4.CreateTranslation(regionOffset);

        var terrainCentroid = (bMin + bMax) * 0.5f + regionOffset;
        var terrainFaceOut  = new PrimRenderFace
        {
            Vertices    = PackVertices(terrainFace),
            Indices     = terrainFace.Indices.ToArray(),
            Color       = TkVector4.One,
            Transform   = offsetMat,
            Texture     = splatBmp,
            Fullbright  = false,
            Glow        = 0f,
            HasAlpha    = false,
            AlphaMode   = FaceAlphaMode.None,
            PrimLocalId = 0,
            FaceIndex   = 0,
            Centroid    = terrainCentroid,
        };

        bMin += regionOffset;
        bMax += regionOffset;

        return new PrimRenderSubmission
        {
            Label     = "terrain",
            Faces     = [terrainFaceOut],
            BoundsMin = bMin,
            BoundsMax = bMax,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static float[,] SampleHeightmap(Simulator sim)
    {
        var hm = new float[256, 256];
        for (int x = 0; x < 256; x++)
        {
            for (int y = 0; y < 256; y++)
            {
                int patchNr = (x / 16) * 16 + y / 16;
                var patch   = sim.Terrain[patchNr];
                if (patch?.Data != null)
                    hm[x, y] = patch.Data[(x % 16) * 16 + y % 16];
            }
        }
        return hm;
    }

    private (Face face, TkVector3 bMin, TkVector3 bMax) BuildTerrainMesh(float[,] heightmap)
    {
        var face = _mesher.TerrainMesh(heightmap, 0f, 255f, 0f, 255f);

        var bMin = new TkVector3(float.MaxValue);
        var bMax = new TkVector3(float.MinValue);
        foreach (var v in face.Vertices)
        {
            var p = new TkVector3(v.Position.X, v.Position.Y, v.Position.Z);
            bMin = TkVector3.ComponentMin(bMin, p);
            bMax = TkVector3.ComponentMax(bMax, p);
        }
        if (face.Vertices.Count == 0) { bMin = TkVector3.Zero; bMax = new TkVector3(255f, 255f, 0f); }

        return (face, bMin, bMax);
    }

    private static float[] PackVertices(Face face)
    {
        var verts = new float[face.Vertices.Count * 8];
        for (int i = 0; i < face.Vertices.Count; i++)
        {
            var v = face.Vertices[i];
            int o = i * 8;
            verts[o + 0] = v.Position.X;
            verts[o + 1] = v.Position.Y;
            verts[o + 2] = v.Position.Z;
            verts[o + 3] = v.Normal.X;
            verts[o + 4] = v.Normal.Y;
            verts[o + 5] = v.Normal.Z;
            verts[o + 6] = v.TexCoord.X;
            verts[o + 7] = v.TexCoord.Y;
        }
        return verts;
    }

}
