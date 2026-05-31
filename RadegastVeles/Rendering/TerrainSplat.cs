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
using OpenMetaverse;
using SkiaSharp;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Builds a composited terrain splat texture from a region heightmap and four
/// detail textures, following the OpenSimulator terrain-splatting algorithm.
/// </summary>
/// <remarks>Ported from Radegast.Rendering.TerrainSplat — see
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

    private const int RegionSize  = 256;
    private const int OutputSize  = 2048;
    private const int DetailTileSize = 256;

    /// <summary>
    /// Builds a composited terrain texture asynchronously.
    /// Returns a 2048×2048 BGRA8888 <see cref="SKBitmap"/> owned by the caller.
    /// </summary>
    public static async Task<SKBitmap> SplatAsync(
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
        var layermap = new float[RegionSize * RegionSize];

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
                layermap[ny * RegionSize + nx] = Utils.Clamp(layer, 0f, 3f);
            }
        }

        // ── Composite ─────────────────────────────────────────────────────────
        var output = new SKBitmap(OutputSize, OutputSize, SKColorType.Bgra8888, SKAlphaType.Opaque);
        CompositeDetail(output, detail, layermap);
        foreach (var bmp in detail) bmp.Dispose();

        // Rotate 270° to match SL orientation
        var rotated = new SKBitmap(OutputSize, OutputSize, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Translate(OutputSize / 2f, OutputSize / 2f);
            canvas.RotateDegrees(270);
            canvas.Translate(-OutputSize / 2f, -OutputSize / 2f);
            canvas.DrawBitmap(output, 0, 0);
        }
        output.Dispose();
        return rotated;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Composites four detail bitmaps into <paramref name="output"/> using the
    /// terrain layer map. Extracted for benchmarking and to isolate the hot loop.
    /// </summary>
    internal static unsafe void CompositeDetail(SKBitmap output, SKBitmap[] detail, float[] layermap)
    {
        const int ratio = OutputSize / RegionSize;

        var scans    = new IntPtr[4];
        var rowBytes = new int[4];
        for (int i = 0; i < 4; i++)
        {
            scans[i]    = detail[i].GetPixels();
            rowBytes[i] = detail[i].RowBytes;
        }

        IntPtr outPtr      = output.GetPixels();
        int    outRowBytes = output.RowBytes;

        // Each row is independent: reads are from shared read-only arrays (scans, layermap)
        // and each row writes to a distinct slice of outPtr — no data races.
        Parallel.For(0, OutputSize, y =>
        {
            int lmY  = y / ratio;
            int lmY1 = lmY < RegionSize - 1 ? lmY + 1 : RegionSize - 1;
            int lmYM = lmY > 0              ? lmY - 1 : 0;

            unsafe
            {
                byte* pOutRow = (byte*)outPtr + y * outRowBytes;

                for (int x = 0; x < OutputSize; x++)
                {
                    int lmX  = x / ratio;
                    int lmX1 = lmX < RegionSize - 1 ? lmX + 1 : RegionSize - 1;
                    int lmXM = lmX > 0              ? lmX - 1 : 0;

                    float layer   = layermap[lmY  * RegionSize + lmX];
                    float layerx  = layermap[lmY  * RegionSize + lmX1];
                    float layerxx = layermap[lmY  * RegionSize + lmXM];
                    float layery  = layermap[lmY1 * RegionSize + lmX];
                    float layeryy = layermap[lmYM * RegionSize + lmX];

                    int l0 = (int)layer;
                    int l1 = l0 < 3 ? l0 + 1 : 3;
                    int tx = x % DetailTileSize;
                    int ty = y % DetailTileSize;

                    int rbA = rowBytes[l0];
                    int rbB = rowBytes[l1];
                    byte* pA = (byte*)scans[l0] + ty * rbA + tx * 4;
                    byte* pB = (byte*)scans[l1] + ty * rbB + tx * 4;
                    byte* pO = pOutRow + x * 4;

                    float aB = pA[0], aG = pA[1], aR = pA[2];
                    float bB = pB[0], bG = pB[1], bR = pB[2];

                    int lX  = (int)layerx;
                    int lXX = (int)layerxx;
                    int lY  = (int)layery;
                    int lYY = (int)layeryy;
                    byte* pX  = (byte*)scans[lX]  + ty * rowBytes[lX]  + tx * 4;
                    byte* pXX = (byte*)scans[lXX] + ty * rowBytes[lXX] + tx * 4;
                    byte* pY  = (byte*)scans[lY]  + ty * rowBytes[lY]  + tx * 4;
                    byte* pYY = (byte*)scans[lYY] + ty * rowBytes[lYY] + tx * 4;

                    float d   = layer   - l0;
                    float dx  = layerx  - layer;
                    float dxx = layerxx - layer;
                    float dy  = layery  - layer;
                    float dyy = layeryy - layer;

                    float fB = aB + d * (bB - aB) + dx * (pX[0] - aB) + dxx * (pXX[0] - aB) + dy * (pY[0] - aB) + dyy * (pYY[0] - aB);
                    float fG = aG + d * (bG - aG) + dx * (pX[1] - aG) + dxx * (pXX[1] - aG) + dy * (pY[1] - aG) + dyy * (pYY[1] - aG);
                    float fR = aR + d * (bR - aR) + dx * (pX[2] - aR) + dxx * (pXX[2] - aR) + dy * (pY[2] - aR) + dyy * (pYY[2] - aR);
                    pO[0] = fB < 0f ? (byte)0 : fB > 255f ? (byte)255 : (byte)fB;
                    pO[1] = fG < 0f ? (byte)0 : fG > 255f ? (byte)255 : (byte)fG;
                    pO[2] = fR < 0f ? (byte)0 : fR > 255f ? (byte)255 : (byte)fR;
                    pO[3] = 255;
                }
            }
        });
    }



    private static async Task<SKBitmap?> FetchDetailTextureAsync(
        GridClient client, UUID id, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<SKBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetResult(null));

        client.Assets.RequestImage(id, ImageType.Normal, (state, asset) =>
        {
            if (state is TextureRequestState.Pending or TextureRequestState.Started
                      or TextureRequestState.Progress)
                return;

            if (state == TextureRequestState.Finished && asset?.AssetData != null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var bmp = J2kImage.FromBytes(asset.AssetData).As<SKBitmap>();
                        tcs.TrySetResult(bmp);
                    }
                    catch { tcs.TrySetResult(null); }
                }, ct);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private static SKBitmap EnsureBgraSize(SKBitmap src, int size)
    {
        if (src.ColorType == SKColorType.Bgra8888 && src.Width == size && src.Height == size)
            return src;

        var dst = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(src, new SKRect(0, 0, size, size));
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

    private static byte ClampByte(float v) => (byte)Math.Floor(Math.Max(0, Math.Min(255, v)));

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
