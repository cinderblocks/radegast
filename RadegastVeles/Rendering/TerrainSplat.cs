/*
 * Radegast Metaverse Client
 * Copyright (c) 2009-2014, Radegast Development Team
 * Copyright (c) 2016-2026, Sjofn, LLC
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
using System.Threading;
using System.Threading.Tasks;
using CoreJ2K;
using LibreMetaverse;
using SkiaSharp;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Fetches the four terrain detail textures for a region and builds the height/noise-based
/// layer map that selects between them, following the OpenSimulator terrain-splatting
/// algorithm's layer computation.
/// <para>
/// Unlike the original CPU-side splatting approach (which pre-composited the four detail
/// textures into one texture using the terrain mesh's simple top-down planar UVs), the
/// blending itself now happens in <c>prim.frag</c> at draw time via triplanar projection —
/// see <see cref="GlViewportControl"/>'s terrain path. That avoids the severe stretching a
/// pre-baked, planar-UV-mapped texture suffers on steep terrain (a single top-down UV
/// coordinate is shared by every vertex at that (x,y), regardless of how tall the slope
/// is there), and removes a real per-rebuild CPU cost (a 2048×2048 parallel pixel blend).
/// </para>
/// </summary>
/// <remarks>Layer-map math ported from Radegast.Rendering.TerrainSplat — see
/// http://opensimulator.org/wiki/Terrain_Splatting </remarks>
internal static class TerrainSplat
{
    private static readonly UUID DIRT_DETAIL     = new("0bc58228-74a0-7e83-89bc-5c23464bcec5");
    private static readonly UUID GRASS_DETAIL    = new("63338ede-0037-c4fd-855b-015d77112fc8");
    private static readonly UUID MOUNTAIN_DETAIL = new("303cd381-8560-7579-23f1-f0a880799740");
    private static readonly UUID ROCK_DETAIL     = new("53a2f406-4895-1d13-d541-d2e3b86bc19c");

    private static readonly UUID[] DefaultDetailIds =
    [
        DIRT_DETAIL, GRASS_DETAIL, MOUNTAIN_DETAIL, ROCK_DETAIL
    ];

    private static readonly SKColor[] DefaultColors =
    [
        new SKColor(164, 136, 117),
        new SKColor( 65,  87,  47),
        new SKColor(157, 145, 131),
        new SKColor(125, 128, 130),
    ];

    private const int RegionSize     = 256;
    private const int DetailTileSize = 256;

    /// <summary>
    /// The four raw detail-texture bitmaps (index order matches the region's
    /// TerrainDetail0-3) and the baked layer-select map (grayscale, value/255*3 gives the
    /// [0,3] layer value <c>prim.frag</c>'s terrain path expects), both owned by the caller.
    /// </summary>
    public readonly record struct TerrainLayers(SKBitmap[] Detail, SKBitmap LayerMap);

    /// <summary>
    /// Fetches the four detail textures and builds the layer-select map asynchronously.
    /// </summary>
    public static async Task<TerrainLayers> BuildLayersAsync(
        GridClient       client,
        float[,]         heightmap,
        UUID[]           textureIds,
        float[]          startHeights,
        float[]          heightRanges,
        CancellationToken ct = default)
    {
        // Replace zero UUIDs with defaults
        var ids = (UUID[])textureIds.Clone();
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] == UUID.Zero) ids[i] = DefaultDetailIds[i];

        // ── Fetch the four detail textures in parallel ────────────────────────
        var tasks = new Task<SKBitmap?>[4];
        for (int i = 0; i < 4; i++)
            tasks[i] = FetchDetailTextureAsync(client, ids[i], ct);

        await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var detail = new SKBitmap[4];
        for (int i = 0; i < 4; i++)
        {
            detail[i] = tasks[i].Result != null
                ? EnsureBgraSize(tasks[i].Result!, DetailTileSize)
                : SolidColor(DetailTileSize, DefaultColors[i]);
        }

        // ── Build layer map ───────────────────────────────────────────────────
        int hmW = heightmap.GetLength(0);
        int hmH = heightmap.GetLength(1);
        int diff = hmW / RegionSize;
        var layerValues = new float[RegionSize * RegionSize];

        for (int y = 0; y < hmH; y += diff)
        {
            for (int x = 0; x < hmW; x += diff)
            {
                int nx     = x / diff;
                int ny     = y / diff;
                float height = heightmap[nx, ny];
                float pctX   = nx / 255f;
                float pctY   = ny / 255f;

                float startH = Bilinear(startHeights[0], startHeights[2],
                                        startHeights[1], startHeights[3], pctX, pctY);
                startH = Utils.Clamp(startH, 0f, 255f);

                float rangeH = Bilinear(heightRanges[0], heightRanges[2],
                                        heightRanges[1], heightRanges[3], pctX, pctY);
                rangeH = Utils.Clamp(rangeH, 0f, 255f);

                float lowFreq  = Perlin.noise2(nx * 0.20319f * 0.222222f, ny * 0.20319f * 0.222222f) * 6.5f;
                float highFreq = Perlin.turbulence2(nx * 0.20319f, ny * 0.20319f, 2f) * 2.25f;
                float noise    = (lowFreq + highFreq) * 2f;

                float layer = ((height + noise - startH) / rangeH) * 4f;
                if (float.IsNaN(layer)) layer = 0f;
                layerValues[ny * RegionSize + nx] = Utils.Clamp(layer, 0f, 3f);
            }
        }

        ct.ThrowIfCancellationRequested();

        // Bake the layer map into a texture, encoded so a plain [0,1] R-channel sample
        // (bilinear-filtered by the GPU on upload, giving the same cross-cell smoothing
        // the old CPU composite did by hand) recovers layer/3. Written into all of B/G/R
        // so the shader isn't sensitive to which channel it happens to read.
        var layerBmp = new SKBitmap(RegionSize, RegionSize, SKColorType.Bgra8888, SKAlphaType.Opaque);
        unsafe
        {
            IntPtr pixels   = layerBmp.GetPixels();
            int    rowBytes = layerBmp.RowBytes;
            for (int y = 0; y < RegionSize; y++)
            {
                byte* row = (byte*)pixels + y * rowBytes;
                for (int x = 0; x < RegionSize; x++)
                {
                    byte v = (byte)(Utils.Clamp(layerValues[y * RegionSize + x] / 3f, 0f, 1f) * 255f);
                    int o = x * 4;
                    row[o + 0] = v; row[o + 1] = v; row[o + 2] = v; row[o + 3] = 255;
                }
            }
        }

        // Rotate 270° to match the terrain mesh's existing (unchanged) UV orientation —
        // the same transform the old pre-composited splat texture used, so the layer map
        // lines up with SculptMesh's planar top-down UVs exactly as before.
        var layerRotated = new SKBitmap(RegionSize, RegionSize, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(layerRotated))
        {
            canvas.Translate(RegionSize / 2f, RegionSize / 2f);
            canvas.RotateDegrees(270);
            canvas.Translate(-RegionSize / 2f, -RegionSize / 2f);
            canvas.DrawBitmap(layerBmp, 0f, 0f, new SKSamplingOptions());
        }
        layerBmp.Dispose();

        return new TerrainLayers(detail, layerRotated);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<SKBitmap?> FetchDetailTextureAsync(
        GridClient client, UUID id, CancellationToken ct)
    {
        var assetTexture = await client.Assets.RequestImageAsync(id, ImageType.Normal, ct).ConfigureAwait(false);
        if (assetTexture?.AssetData == null) return null;
        try { return await Task.Run(() => J2kImage.DecodeToImage<SKBitmap>(assetTexture.AssetData), ct).ConfigureAwait(false); }
        catch { return null; }
    }

    private static SKBitmap EnsureBgraSize(SKBitmap src, int size)
    {
        if (src.ColorType == SKColorType.Bgra8888 && src.Width == size && src.Height == size)
            return src;

        var dst = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(src, new SKRect(0, 0, size, size), new SKSamplingOptions());
        src.Dispose();
        return dst;
    }

    private static SKBitmap SolidColor(int size, SKColor color)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }

    private static float Bilinear(float v00, float v01, float v10, float v11, float xPct, float yPct)
        => Utils.Lerp(Utils.Lerp(v00, v01, xPct), Utils.Lerp(v10, v11, xPct), yPct);

    /// <summary>
    /// Fallback: builds a simple green–brown heightmap texture when detail
    /// texture download fails.  Returns a 256×256 RGB bitmap.
    /// </summary>
    internal static SKBitmap SplatSimple(float[,] heightmap)
    {
        const float BASE_H = 93f  / 360f;
        const float BASE_S = 44f  / 100f;
        const float BASE_V = 34f  / 100f;

        var bmp = new SKBitmap(256, 256, SKColorType.Rgb888x, SKAlphaType.Opaque);
        unsafe
        {
            IntPtr pixels   = bmp.GetPixels();
            int    rowBytes = bmp.RowBytes;
            int    bpp      = bmp.BytesPerPixel;

            for (int y = 255; y >= 0; y--)
            {
                for (int x = 0; x < 256; x++)
                {
                    float norm = Utils.Clamp(heightmap[x, y] / 255f, BASE_V, 1f);
                    HsvToRgb(BASE_H * 360f, BASE_S, norm, out byte r, out byte g, out byte b);
                    byte* ptr = (byte*)pixels + y * rowBytes + x * bpp;
                    ptr[0] = r; ptr[1] = g; ptr[2] = b;
                }
            }
        }
        return bmp;
    }

    private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
    {
        float c  = v * s;
        float x  = c * (1f - Math.Abs(h / 60f % 2f - 1f));
        float m  = v - c;
        float r1, g1, b1;
        if      (h < 60f)  { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180f) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0; b1 = c; }
        else               { r1 = c; g1 = 0; b1 = x; }
        r = (byte)Math.Round((r1 + m) * 255f);
        g = (byte)Math.Round((g1 + m) * 255f);
        b = (byte)Math.Round((b1 + m) * 255f);
    }
}
