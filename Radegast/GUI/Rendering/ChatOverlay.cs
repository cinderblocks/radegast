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
    public class ChatOverlay : IDisposable
    {
        public static Font ChatFont = new Font(FontFamily.GenericSansSerif, 11f, FontStyle.Regular, GraphicsUnit.Point);
        public static float ChatLineTimeOnScreen = 20f; // time in seconds chat line appears on the screen
        public static float ChatLineFade = 1.5f; // number of seconds before expiry to start fading chat off
        public static OpenTK.Graphics.Color4 ChatBackground = new OpenTK.Graphics.Color4(0f, 0f, 0f, 0.75f);

        private readonly RadegastInstanceForms Instance;
        private readonly SceneWindow Window;
        private float runningTime;

        private Queue<ChatLine> chatLines;
        private int screenWidth, screenHeight;

        public ChatOverlay(RadegastInstanceForms instance, SceneWindow window)
        {
            Instance = instance;
            Window = window;
            Instance.TabConsole.MainChatManger.ChatLineAdded += MainChatManger_ChatLineAdded;
            chatLines = new Queue<ChatLine>();
        }

        public void Dispose()
        {
            Instance.TabConsole.MainChatManger.ChatLineAdded -= MainChatManger_ChatLineAdded;

            if (chatLines != null)
            {
                lock (chatLines)
                {
                    foreach (var line in chatLines)
                    {
                        line.Dispose();
                    }
                }
                chatLines = null;
            }
        }

        private void MainChatManger_ChatLineAdded(object sender, ChatLineAddedArgs e)
        {
            lock (chatLines)
            {
                chatLines.Enqueue(new ChatLine(e.Item, runningTime));
            }
        }

        public static OpenTK.Graphics.Color4 GetColorForStyle(ChatBufferTextStyle style)
        {
            switch (style)
            {
                case ChatBufferTextStyle.StatusBlue:
                    return new OpenTK.Graphics.Color4(0.2f, 0.2f, 1f, 1f);
                case ChatBufferTextStyle.StatusDarkBlue:
                    return new OpenTK.Graphics.Color4(0f, 0f, 1f, 1f);
                case ChatBufferTextStyle.ObjectChat:
                    return new OpenTK.Graphics.Color4(0.7f, 0.9f, 0.7f, 1f);
                case ChatBufferTextStyle.OwnerSay:
                    return new OpenTK.Graphics.Color4(0.99f, 0.99f, 0.69f, 1f);
                case ChatBufferTextStyle.Alert:
                    return new OpenTK.Graphics.Color4(0.8f, 1f, 1f, 1f);
                case ChatBufferTextStyle.Error:
                    return new OpenTK.Graphics.Color4(0.82f, 0.27f, 0.27f, 1f);

                default:
                    return new OpenTK.Graphics.Color4(1f, 1f, 1f, 1f);

            }
        }


        public void RenderChat(float time, RenderPass pass)
        {
            runningTime += time;
            if (chatLines.Count == 0) return;

            int c = 0;
            int expired = 0;
            screenWidth = Window.Viewport[2];
            screenHeight = Window.Viewport[3];
            ChatLine[] lines;

            lock (chatLines)
            {
                lines = chatLines.ToArray();
                c = lines.Length;
                for (int i = 0; i < c; i++)
                {
                    if ((runningTime - lines[i].TimeAdded) > ChatLineTimeOnScreen)
                    {
                        expired++;
                        ChatLine goner = chatLines.Dequeue();
                        goner.Dispose();
                    }
                    else
                    {
                        break;
                    }
                }
                if (expired > 0)
                {
                    lines = chatLines.ToArray();
                    c = lines.Length;
                }
            }

            if (c == 0)
            {
                runningTime = 0f;
                return;
            }

            int maxWidth = (int)((float)screenWidth * 0.7f);

            int actualMaxWidth = 0;
            int height = 0;
            for (int i = 0; i < c; i++)
            {
                lines[i].PrepareText(maxWidth);
                if (lines[i].TextWidth > actualMaxWidth) actualMaxWidth = lines[i].TextWidth;
                height += lines[i].TextHeight;
            }

            int x = 5;
            int y = 5;
            GL.Enable(EnableCap.Texture2D);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Color4(ChatBackground);
            RHelp.DrawRounded2DBox(x, y, actualMaxWidth + 6, height + 10, 6f, 1f);
            for (int i = c - 1; i >= 0; i--)
            {
                ChatLine line = lines[i];
                OpenTK.Graphics.Color4 color = GetColorForStyle(line.Style);
                float remain = ChatLineTimeOnScreen - (runningTime - line.TimeAdded);
                if (remain < ChatLineFade)
                {
                    color.A = remain / ChatLineFade;
                }
                GL.Color4(color);
                line.Render(x + 3, y + 5);
                y += line.TextHeight;
            }
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Disable(EnableCap.Texture2D);
            GL.Color4(1f, 1f, 1f, 1f);
        }
    }

    public class ChatLine : IDisposable
    {
        public float TimeAdded;
        
        public int TextWidth;
        public int TextHeight;
        public int ImgWidth;
        public int ImgHeight;

        public ChatBufferTextStyle Style => item.Style;

        private int textureID = -1;
        private int widthForTextureGenerated = -1;
        private readonly ChatBufferItem item;
       
        public ChatLine(ChatBufferItem item, float timeAdded)
        {
            this.item = item;
            TimeAdded = timeAdded;
        }

        public void Dispose()
        {
            if (textureID > 0)
            {
                GL.DeleteTexture(textureID);
                textureID = -1;
            }
        }

        public void PrepareText(int maxWidth)
        {
            if (maxWidth != widthForTextureGenerated)
            {
                string txt = item.From + item.Text;

                // If we're modified and have texture already delete it from graphics card
                if (textureID > 0)
                {
                    GL.DeleteTexture(textureID);
                    textureID = -1;
                }

                // Create paint for measuring and drawing
                using (var paint = CreatePaint(ChatOverlay.ChatFont))
                {
                    // Word-wrap into lines not exceeding maxWidth
                    var lines = WrapText(txt, paint, maxWidth);

                    // Measure height via font metrics
                    paint.GetFontMetrics(out var metrics);
                    float lineHeight = (metrics.Descent - metrics.Ascent) + metrics.Leading;
                    if (lineHeight <= 0f) lineHeight = (-metrics.Ascent) + metrics.Descent;

                    // Measure width as max line width
                    float maxLineWidth = 0f;
                    foreach (var line in lines)
                    {
                        float w = paint.MeasureText(line);
                        if (w > maxLineWidth) maxLineWidth = w;
                    }

                    TextWidth = (int)Math.Ceiling(maxLineWidth);
                    TextHeight = (int)Math.Ceiling(lineHeight * lines.Count);

                    ImgWidth = TextWidth;
                    ImgHeight = TextHeight;

                    if (!RenderSettings.TextureNonPowerOfTwoSupported)
                    {
                        ImgWidth = RHelp.NextPow2(TextWidth);
                        ImgHeight = RHelp.NextPow2(TextHeight);
                    }

                    using (var skBitmap = new SKBitmap(ImgWidth, ImgHeight, SKColorType.Bgra8888, SKAlphaType.Premul))
                    using (var canvas = new SKCanvas(skBitmap))
                    {
                        canvas.Clear(SKColors.Transparent);

                        // Draw lines centered horizontally within ImgWidth
                        float baseline = -metrics.Ascent;
                        float y = baseline;
                        foreach (var line in lines)
                        {
                            float lw = paint.MeasureText(line);
                            float x = ((float)ImgWidth - lw) * 0.0f; // Left align within box; HUD positions will handle centering
                            canvas.DrawText(line, x, y, paint);
                            y += lineHeight;
                        }

                        widthForTextureGenerated = maxWidth;
                        textureID = RHelp.GLLoadImage(skBitmap, true, false);
                    }
                }
            }
        }

        public void Render(int x, int y)
        {
            if (textureID == -1) return;
            GL.BindTexture(TextureTarget.Texture2D, textureID);
            RHelp.Draw2DBox(x, y, ImgWidth, ImgHeight, 0f);
        }

        private static SKPaint CreatePaint(Font font)
        {
            var typeface = SKTypeface.FromFamilyName(font.Name);
            return new SKPaint
            {
                Typeface = typeface,
                TextSize = font.Size,
                IsAntialias = true,
                SubpixelText = true,
                IsStroke = false,
                FilterQuality = SKFilterQuality.High,
                Color = new SKColor(255, 255, 255, 255)
            };
        }

        private static List<string> WrapText(string text, SKPaint paint, int maxWidth)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) { result.Add(string.Empty); return result; }

            var words = text.Split(new[] { ' ' }, StringSplitOptions.None);
            var line = string.Empty;

            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
                float width = paint.MeasureText(candidate);
                if (width <= maxWidth)
                {
                    line = candidate;
                }
                else
                {
                    if (!string.IsNullOrEmpty(line)) result.Add(line);
                    // If single word exceeds maxWidth, force break
                    if (paint.MeasureText(word) > maxWidth)
                    {
                        result.Add(word);
                        line = string.Empty;
                    }
                    else
                    {
                        line = word;
                    }
                }
            }

            if (!string.IsNullOrEmpty(line)) result.Add(line);
            return result;
        }
    }
}
