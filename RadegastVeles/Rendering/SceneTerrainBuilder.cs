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
/// and an alpha-blended water plane for the currently-connected simulator region.
/// </summary>
internal sealed class SceneTerrainBuilder
{
    private readonly GridClient _client;
    private readonly MeshFoundry _mesher = new();

    public SceneTerrainBuilder(GridClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Samples the current simulator heightmap, composites the terrain splat
    /// texture, adds a water plane, and packs everything into a single
    /// <see cref="PrimRenderSubmission"/> whose AABB covers terrain + water.
    /// Returns <c>null</c> when no simulator is connected or patch data is absent.
    /// </summary>
    public async Task<PrimRenderSubmission?> RebuildAsync(CancellationToken ct = default)
    {
        var sim = _client.Network.CurrentSim;
        if (sim?.Terrain == null) return null;

        float waterZ = sim.WaterHeight;

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

        // ── Assemble faces list ───────────────────────────────────────────────
        var faces = new List<PrimRenderFace>(2);

        // Terrain face — opaque, textured
        var terrainCentroid = (bMin + bMax) * 0.5f;
        faces.Add(new PrimRenderFace
        {
            Vertices    = PackVertices(terrainFace),
            Indices     = terrainFace.Indices.ToArray(),
            Color       = TkVector4.One,
            Transform   = TkMatrix4.Identity,
            Texture     = splatBmp,
            Fullbright  = false,
            Glow        = 0f,
            HasAlpha    = false,
            AlphaMode   = FaceAlphaMode.None,
            PrimLocalId = 0,
            FaceIndex   = 0,
            Centroid    = terrainCentroid,
        });

        // Water plane face — alpha-blended, no texture
        var (waterVerts, waterIdx) = BuildWaterPlane(waterZ);
        var waterCentroid = new TkVector3(127.5f, 127.5f, waterZ);
        faces.Add(new PrimRenderFace
        {
            Vertices    = waterVerts,
            Indices     = waterIdx,
            Color       = new TkVector4(0.15f, 0.45f, 0.65f, 0.62f),
            Transform   = TkMatrix4.Identity,
            Texture     = null,
            Fullbright  = false,
            Glow        = 0f,
            HasAlpha    = true,
            AlphaMode   = FaceAlphaMode.Blend,
            IsTwoSided  = true,
            PrimLocalId = 0,
            FaceIndex   = 1,
            Centroid    = waterCentroid,
        });

        // Expand AABB to include the water surface
        var waterMin = new TkVector3(0f,   0f,   waterZ);
        var waterMax = new TkVector3(255f, 255f, waterZ);
        bMin = TkVector3.ComponentMin(bMin, waterMin);
        bMax = TkVector3.ComponentMax(bMax, waterMax);

        return new PrimRenderSubmission
        {
            Label     = "terrain",
            Faces     = [.. faces],
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

    /// <summary>
    /// Builds a flat 256×256 m quad centred at Z=<paramref name="z"/>.
    /// Returns interleaved vertex data (Position+Normal+UV, 8 floats each)
    /// and a triangle index array.
    /// </summary>
    private static (float[] verts, ushort[] indices) BuildWaterPlane(float z)
    {
        // Four corners: (0,0), (255,0), (255,255), (0,255) — normal points up (+Z)
        var verts = new float[]
        {
            //  X      Y      Z     nX   nY   nZ    U     V
              0f,    0f,    z,    0f,  0f,  1f,  0f,  0f,
            255f,    0f,    z,    0f,  0f,  1f,  1f,  0f,
            255f,  255f,    z,    0f,  0f,  1f,  1f,  1f,
              0f,  255f,    z,    0f,  0f,  1f,  0f,  1f,
        };

        var indices = new ushort[] { 0, 1, 2,  0, 2, 3 };

        return (verts, indices);
    }
}
