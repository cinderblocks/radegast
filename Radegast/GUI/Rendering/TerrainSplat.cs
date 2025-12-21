/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.Diagnostics;
using System.Threading;
using CoreJ2K;
using OpenMetaverse;
using SkiaSharp;

namespace Radegast.Rendering
{
    public static class TerrainSplat
    {
        #region Constants

        private static readonly UUID DIRT_DETAIL = new UUID("0bc58228-74a0-7e83-89bc-5c23464bcec5");
        private static readonly UUID GRASS_DETAIL = new UUID("63338ede-0037-c4fd-855b-015d77112fc8");
        private static readonly UUID MOUNTAIN_DETAIL = new UUID("303cd381-8560-7579-23f1-f0a880799740");
        private static readonly UUID ROCK_DETAIL = new UUID("53a2f406-4895-1d13-d541-d2e3b86bc19c");
        private static readonly UUID TERRAIN_CACHE_MAGIC = new UUID("2c0c7ef2-56be-4eb8-aacb-76712c535b4b");
        private static readonly int RegionSize = 256;

        private static readonly UUID[] DEFAULT_TERRAIN_DETAIL = new UUID[]
        {
            DIRT_DETAIL,
            GRASS_DETAIL,
            MOUNTAIN_DETAIL,
            ROCK_DETAIL
        };

        private static readonly SKColor[] DEFAULT_TERRAIN_COLOR = new SKColor[]
        {
            new SKColor(164, 136, 117, 255),
            new SKColor(65, 87, 47, 255),
            new SKColor(157, 145, 131, 255),
            new SKColor(125, 128, 130, 255)
        };

        #endregion Constants

        /// <summary>
        /// Builds a composited terrain texture given the region texture
        /// and heightmap settings
        /// </summary>
        /// <param name="instance">Radegast Instance</param>
        /// <param name="heightmap">Terrain heightmap</param>
        /// <param name="textureIDs"></param>
        /// <param name="startHeights"></param>
        /// <param name="heightRanges"></param>
        /// <returns>A composited 256x256 RGB texture ready for rendering</returns>
        /// <remarks>Based on the algorithm described at http://opensimulator.org/wiki/Terrain_Splatting
        /// </remarks>
        public static SKBitmap Splat(IRadegastInstance instance, float[,] heightmap, UUID[] textureIDs, float[] startHeights, float[] heightRanges)
        {
            Debug.Assert(textureIDs.Length == 4);
            Debug.Assert(startHeights.Length == 4);
            Debug.Assert(heightRanges.Length == 4);
            int outputSize = 2048;

            SKBitmap[] detailTexture = new SKBitmap[4];

            // Swap empty terrain textureIDs with default IDs
            for (int i = 0; i < textureIDs.Length; i++)
            {
                if (textureIDs[i] == UUID.Zero)
                    textureIDs[i] = DEFAULT_TERRAIN_DETAIL[i];
            }

            #region Texture Fetching
            for (int i = 0; i < 4; i++)
            {
                AutoResetEvent textureDone = new AutoResetEvent(false);
                UUID textureID = textureIDs[i];

                instance.Client.Assets.RequestImage(textureID, TextureDownloadCallback(detailTexture, i, textureDone));

                textureDone.WaitOne(60 * 1000, false);
            }

            #endregion Texture Fetching

            // Fill in any missing textures with a solid color and ensure all are in BGRA8888 format
            for (int i = 0; i < 4; i++)
            {
                if (detailTexture[i] == null)
                {
                    // Create a solid color texture for this layer in BGRA format
                    detailTexture[i] = new SKBitmap(outputSize, outputSize, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    using (var canvas = new SKCanvas(detailTexture[i]))
                    {
                        canvas.Clear(DEFAULT_TERRAIN_COLOR[i]);
                    }
                }
                else
                {
                    // Ensure texture is in BGRA8888 format and correct size
                    var needsConversion = detailTexture[i].ColorType != SKColorType.Bgra8888;
                    var needsResize = detailTexture[i].Width != 256 || detailTexture[i].Height != 256;

                    if (needsConversion || needsResize)
                    {
                        var targetWidth = needsResize ? 256 : detailTexture[i].Width;
                        var targetHeight = needsResize ? 256 : detailTexture[i].Height;
                        var converted = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
                        
                        using (var canvas = new SKCanvas(converted))
                        {
                            canvas.DrawBitmap(detailTexture[i], new SKRect(0, 0, targetWidth, targetHeight), new SKPaint
                            {
                                // use default sampling options
                            });
                        }
                        
                        detailTexture[i].Dispose();
                        detailTexture[i] = converted;
                    }
                }
            }

            #region Layer Map

            int diff = heightmap.GetLength(0) / RegionSize;
            float[] layermap = new float[RegionSize * RegionSize];

            for (int y = 0; y < heightmap.GetLength(0); y += diff)
            {
                for (int x = 0; x < heightmap.GetLength(1); x += diff)
                {
                    int newX = x / diff;
                    int newY = y / diff;
                    float height = heightmap[newX, newY];

                    float pctX = (float)newX / 255f;
                    float pctY = (float)newY / 255f;

                    // Use bilinear interpolation between the four corners of start height and
                    // height range to select the current values at this position
                    float startHeight = ImageUtils.Bilinear(
                        startHeights[0],
                        startHeights[2],
                        startHeights[1],
                        startHeights[3],
                        pctX, pctY);
                    startHeight = Utils.Clamp(startHeight, 0f, 255f);

                    float heightRange = ImageUtils.Bilinear(
                        heightRanges[0],
                        heightRanges[2],
                        heightRanges[1],
                        heightRanges[3],
                        pctX, pctY);
                    heightRange = Utils.Clamp(heightRange, 0f, 255f);

                    // Generate two frequencies of perlin noise based on our global position
                    // The magic values were taken from http://opensimulator.org/wiki/Terrain_Splatting
                    Vector3 vec = new Vector3
                    (
                        newX * 0.20319f,
                        newY * 0.20319f,
                        height * 0.25f
                    );

                    float lowFreq = Perlin.noise2(vec.X * 0.222222f, vec.Y * 0.222222f) * 6.5f;
                    float highFreq = Perlin.turbulence2(vec.X, vec.Y, 2f) * 2.25f;
                    float noise = (lowFreq + highFreq) * 2f;

                    // Combine the current height, generated noise, start height, and height range parameters, then scale all of it
                    float layer = ((height + noise - startHeight) / heightRange) * 4f;
                    if (float.IsNaN(layer))
                        layer = 0f;
                    layermap[newY * RegionSize + newX] = Utils.Clamp(layer, 0f, 3f);
                }
            }

            #endregion Layer Map

            #region Texture Compositing
            // Output in BGRA8888 to match input textures
            SKBitmap output = new SKBitmap(outputSize, outputSize, SKColorType.Bgra8888, SKAlphaType.Opaque);

            unsafe
            {
                // Get pixel pointers for all textures (all in BGRA8888 format now)
                IntPtr[] scans = new IntPtr[4];
                int[] rowBytes = new int[4];

                for (int i = 0; i < 4; i++)
                {
                    scans[i] = detailTexture[i].GetPixels();
                    rowBytes[i] = detailTexture[i].RowBytes;
                }

                IntPtr outputScan = output.GetPixels();
                int outputRowBytes = output.RowBytes;

                int ratio = outputSize / RegionSize;
                int bytesPerPixel = 4; // BGRA8888 is always 4 bytes per pixel

                for (int y = 0; y < outputSize; y++)
                {
                    for (int x = 0; x < outputSize; x++)
                    {
                        float layer = layermap[(y / ratio) * RegionSize + x / ratio];
                        float layerx = layermap[(y / ratio) * RegionSize + Math.Min(outputSize - 1, (x + 1)) / ratio];
                        float layerxx = layermap[(y / ratio) * RegionSize + Math.Max(0, (x - 1)) / ratio];
                        float layery = layermap[Math.Min(outputSize - 1, (y + 1)) / ratio * RegionSize + x / ratio];
                        float layeryy = layermap[(Math.Max(0, (y - 1)) / ratio) * RegionSize + x / ratio];

                        // Select two textures
                        int l0 = (int)Math.Floor(layer);
                        int l1 = Math.Min(l0 + 1, 3);

                        int texY = y % 256;
                        int texX = x % 256;

                        byte* ptrA = (byte*)scans[l0] + texY * rowBytes[l0] + texX * bytesPerPixel;
                        byte* ptrB = (byte*)scans[l1] + texY * rowBytes[l1] + texX * bytesPerPixel;
                        byte* ptrO = (byte*)outputScan + y * outputRowBytes + x * bytesPerPixel;

                        // BGRA format: B=0, G=1, R=2, A=3
                        float aB = *(ptrA + 0);
                        float aG = *(ptrA + 1);
                        float aR = *(ptrA + 2);

                        int lX = (int)Math.Floor(layerx);
                        byte* ptrX = (byte*)scans[lX] + texY * rowBytes[lX] + texX * bytesPerPixel;
                        int lXX = (int)Math.Floor(layerxx);
                        byte* ptrXX = (byte*)scans[lXX] + texY * rowBytes[lXX] + texX * bytesPerPixel;
                        int lY = (int)Math.Floor(layery);
                        byte* ptrY = (byte*)scans[lY] + texY * rowBytes[lY] + texX * bytesPerPixel;
                        int lYY = (int)Math.Floor(layeryy);
                        byte* ptrYY = (byte*)scans[lYY] + texY * rowBytes[lYY] + texX * bytesPerPixel;

                        float bB = *(ptrB + 0);
                        float bG = *(ptrB + 1);
                        float bR = *(ptrB + 2);

                        float layerDiff = layer - l0;
                        float xlayerDiff = layerx - layer;
                        float xxlayerDiff = layerxx - layer;
                        float ylayerDiff = layery - layer;
                        float yylayerDiff = layeryy - layer;

                        // Interpolate between the two selected textures (BGRA format)
                        *(ptrO + 0) = (byte)Math.Floor(aB + layerDiff * (bB - aB) + 
                            xlayerDiff * (*(ptrX + 0) - aB) + 
                            xxlayerDiff * (*(ptrXX + 0) - aB) + 
                            ylayerDiff * (*(ptrY + 0) - aB) + 
                            yylayerDiff * (*(ptrYY + 0) - aB));
                        *(ptrO + 1) = (byte)Math.Floor(aG + layerDiff * (bG - aG) + 
                            xlayerDiff * (*(ptrX + 1) - aG) +
                            xxlayerDiff * (*(ptrXX + 1) - aG) + 
                            ylayerDiff * (*(ptrY + 1) - aG) +
                            yylayerDiff * (*(ptrYY + 1) - aG));
                        *(ptrO + 2) = (byte)Math.Floor(aR + layerDiff * (bR - aR) +
                            xlayerDiff * (*(ptrX + 2) - aR) + 
                            xxlayerDiff * (*(ptrXX + 2) - aR) +
                            ylayerDiff * (*(ptrY + 2) - aR) + 
                            yylayerDiff * (*(ptrYY + 2) - aR));
                        *(ptrO + 3) = 255; // Alpha channel
                    }
                }

                for (int i = 0; i < 4; i++)
                {
                    detailTexture[i].Dispose();
                }
            }

            layermap = null;

            // Rotate the output
            var rotated = new SKBitmap(outputSize, outputSize, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(rotated))
            {
                canvas.Translate(outputSize / 2f, outputSize / 2f);
                canvas.RotateDegrees(270);
                canvas.Translate(-outputSize / 2f, -outputSize / 2f);
                canvas.DrawBitmap(output, 0, 0);
            }
            output.Dispose();

            #endregion Texture Compositing

            return rotated;
        }

        private static TextureDownloadCallback TextureDownloadCallback(SKBitmap[] detailTexture, int i, AutoResetEvent textureDone)
        {
            return (state, assetTexture) =>
            {
                if (state == TextureRequestState.Finished && assetTexture?.AssetData != null)
                {
                    detailTexture[i] = J2kImage.FromBytes(assetTexture.AssetData).As<SKBitmap>();
                }
                textureDone.Set();
            };
        }

        public static SKBitmap ResizeBitmap(SKBitmap b, int nWidth, int nHeight)
        {
            var result = new SKBitmap(nWidth, nHeight, b.ColorType, b.AlphaType);
            using (var canvas = new SKCanvas(result))
            {
                canvas.DrawBitmap(b, new SKRect(0, 0, nWidth, nHeight), new SKPaint
                {
                    // use default sampling options
                });
            }
            b.Dispose();
            return result;
        }

        public static SKBitmap TileBitmap(SKBitmap b, int tiles)
        {
            var result = new SKBitmap(b.Width * tiles, b.Width * tiles, b.ColorType, b.AlphaType);
            using (var canvas = new SKCanvas(result))
            {
                for (int x = 0; x < tiles; x++)
                {
                    for (int y = 0; y < tiles; y++)
                    {
                        canvas.DrawBitmap(b, x * 256, y * 256);
                    }
                }
            }
            b.Dispose();
            return result;
        }

        public static SKBitmap SplatSimple(float[,] heightmap)
        {
            const float BASE_HSV_H = 93f / 360f;
            const float BASE_HSV_S = 44f / 100f;
            const float BASE_HSV_V = 34f / 100f;

            var img = new SKBitmap(256, 256, SKColorType.Rgb888x, SKAlphaType.Opaque);

            unsafe
            {
                IntPtr pixels = img.GetPixels();
                int rowBytes = img.RowBytes;
                int bytesPerPixel = img.BytesPerPixel;

                for (int y = 255; y >= 0; y--)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        float normHeight = heightmap[x, y] / 255f;
                        normHeight = Utils.Clamp(normHeight, BASE_HSV_V, 1.0f);

                        Color4 color = Color4.FromHSV(BASE_HSV_H, BASE_HSV_S, normHeight);

                        byte* ptr = (byte*)pixels + y * rowBytes + x * bytesPerPixel;
                        *(ptr + 0) = (byte)(color.R * 255f);
                        *(ptr + 1) = (byte)(color.G * 255f);
                        *(ptr + 2) = (byte)(color.B * 255f);
                    }
                }
            }

            return img;
        }
    }
}
