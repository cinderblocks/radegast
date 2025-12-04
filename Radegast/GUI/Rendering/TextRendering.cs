/**
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
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;

namespace Radegast.Rendering
{
    public class TextRendering : IDisposable
    {
        private class CachedInfo
        {
            public int TextureID;
            public int LastUsed;
            public int Width;
            public int Height;
        }

        private class TextItem
        {
            public readonly string Text;
            public readonly Font Font;
            public readonly SKColor Color;
            public Rectangle Box;
            public readonly TextFormatFlags Flags;

            public int ImgWidth;
            public int ImgHeight;

            public int TextureID = -1;

            public TextItem(string text, Font font, SKColor color, Rectangle box, TextFormatFlags flags)
            {
                Text = text;
                Font = font;
                Color = color;
                Box = box;
                Flags = flags | TextFormatFlags.NoPrefix;
            }
        }

        public static Size MaxSize = new Size(8192, 8192);

        private IRadegastInstance Instance;
        private readonly List<TextItem> textItems;
        private readonly int[] Viewport = new int[4];
        private int ScreenWidth { get; set; }
        private int ScreenHeight { get; set; }
        private readonly Dictionary<int, CachedInfo> Cache = new Dictionary<int, CachedInfo>();

        public TextRendering(IRadegastInstance instance)
        {
            Instance = instance;
            textItems = new List<TextItem>();
        }

        public void Dispose()
        {
        }

        public void Print(string text, Font font, SKColor color, Rectangle box, TextFormatFlags flags)
        {
            textItems.Add(new TextItem(text, font, color, box, flags));
        }

        public static Size Measure(string text, Font font, TextFormatFlags flags)
        {
            return MeasureSkia(text, font);
        }

        public void Begin()
        {
        }

        public void End()
        {
            GL.GetInteger(GetPName.Viewport, Viewport);
            ScreenWidth = Viewport[2];
            ScreenHeight = Viewport[3];
            int stamp = Environment.TickCount;

            GL.Enable(EnableCap.Texture2D);
            GLHUDBegin();
            {
                foreach (TextItem item in textItems)
                {
                    int hash = GetItemHash(item);
                    CachedInfo tex = new CachedInfo() { TextureID = -1 };
                    if (Cache.TryGetValue(hash, out var value))
                    {
                        tex = value;
                        tex.LastUsed = stamp;
                    }
                    else
                    {
                        PrepareText(item);
                        if (item.TextureID != -1)
                        {
                            Cache[hash] = tex = new CachedInfo()
                            {
                                TextureID = item.TextureID,
                                Width = item.ImgWidth,
                                Height = item.ImgHeight,
                                LastUsed = stamp
                            };
                        }
                    }
                    if (tex.TextureID == -1) continue;
                    GL.Color4(item.Color.Red / 255f, item.Color.Green / 255f, item.Color.Blue / 255f, item.Color.Alpha / 255f);
                    GL.BindTexture(TextureTarget.Texture2D, tex.TextureID);
                    RHelp.Draw2DBox(item.Box.X, ScreenHeight - item.Box.Y - tex.Height, tex.Width, tex.Height, 0f);
                }

                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.Disable(EnableCap.Texture2D);
                GL.Color4(1f, 1f, 1f, 1f);
            }
            GLHUDEnd();

            textItems.Clear();
        }

        private int GetItemHash(TextItem item)
        {
            int ret = 17;
            ret = ret * 31 + item.Text.GetHashCode();
            ret = ret * 31 + item.Font.GetHashCode();
            ret = ret * 31 + (int)item.Flags;
            return ret;
        }


        private void PrepareText(TextItem item)
        {
            // If we're modified and have texture already delete it from graphics card
            if (item.TextureID > 0)
            {
                //GL.DeleteTexture(item.TextureID);
                item.TextureID = -1;
            }

            Size s;

            try
            {
                s = MeasureSkia(item.Text, item.Font);
            }
            catch
            {
                return;
            }

            item.ImgWidth = s.Width;
            item.ImgHeight = s.Height;

            if (!RenderSettings.TextureNonPowerOfTwoSupported)
            {
                item.ImgWidth = RHelp.NextPow2(s.Width);
                item.ImgHeight = RHelp.NextPow2(s.Height);
            }

            using (var skBitmap = new SKBitmap(item.ImgWidth, item.ImgHeight, SKColorType.Bgra8888, SKAlphaType.Premul))
            using (var canvas = new SKCanvas(skBitmap))
            using (var paint = CreatePaint(item.Font))
            {
                canvas.Clear(SKColors.Transparent);

                paint.Color = item.Color;

                // Compute baseline and line height
                paint.GetFontMetrics(out var metrics);
                float baseline = -metrics.Ascent;
                float lineHeight = (metrics.Descent - metrics.Ascent) + metrics.Leading;
                if (lineHeight <= 0f) lineHeight = baseline + metrics.Descent;

                // Prepare lines and total height
                var lines = item.Text.Split(new[] { "\n" }, StringSplitOptions.None);
                float totalHeight = lineHeight * lines.Length;
                // Start Y such that text block is vertically centered
                float y = ((float)item.ImgHeight - totalHeight) * 0.5f + baseline;
                foreach (var line in lines)
                {
                    // Measure line width to center horizontally
                    float lineWidth = paint.MeasureText(line ?? string.Empty);
                    float x = ((float)item.ImgWidth - lineWidth) * 0.5f;
                    canvas.DrawText(line, x, y, paint);
                    y += lineHeight;
                }

                item.TextureID = RHelp.GLLoadImage(skBitmap, true, false);
            }
        }

        private static Size MeasureSkia(string text, Font font)
        {
            using (var paint = CreatePaint(font))
            {
                // Height via metrics
                paint.GetFontMetrics(out var metrics);
                float lineHeight = (metrics.Descent - metrics.Ascent) + metrics.Leading;
                if (lineHeight <= 0f) lineHeight = (-metrics.Ascent) + metrics.Descent;

                // Measure each line
                var lines = text?.Split(new[] { "\n" }, StringSplitOptions.None) ?? Array.Empty<string>();
                if (lines.Length == 0) lines = new[] { string.Empty };

                float maxWidth = 0f;
                foreach (var line in lines)
                {
                    float w = paint.MeasureText(line ?? string.Empty);
                    if (w > maxWidth) maxWidth = w;
                }
                float totalHeight = lineHeight * lines.Length;

                return new Size((int)Math.Ceiling(maxWidth), (int)Math.Ceiling(totalHeight));
            }
        }

        private static SKPaint CreatePaint(Font font)
        {
            // Create typeface from font family name
            var typeface = SKTypeface.FromFamilyName(font.Name);
            var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = font.Size,
                IsAntialias = true,
                SubpixelText = true,
                IsStroke = false,
                FilterQuality = SKFilterQuality.High
            };
            return paint;
        }

        private bool depthTestEnabled;
        private bool lightningEnabled;

        // Switch to ortho display mode for drawing hud
        private void GLHUDBegin()
        {
            depthTestEnabled = GL.IsEnabled(EnableCap.DepthTest);
            lightningEnabled = GL.IsEnabled(EnableCap.Lighting);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0, ScreenWidth, 0, ScreenHeight, 1, -1);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
        }

        // Switch back to frustrum display mode
        private void GLHUDEnd()
        {
            if (depthTestEnabled)
            {
                GL.Enable(EnableCap.DepthTest);
            }
            if (lightningEnabled)
            {
                GL.Enable(EnableCap.Lighting);
            }
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
        }

    }
}
