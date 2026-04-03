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
using SkiaSharp;
using OpenTK.Graphics.OpenGL4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Uploads a <see cref="SKBitmap"/> as a 2D OpenGL texture with mipmaps.
/// Must be created and disposed on the GL render thread.
/// </summary>
public sealed class GlTexture : IDisposable
{
    public int  TextureId { get; private set; }
    private bool _disposed;

    public GlTexture(SKBitmap bitmap)
    {
        bool ownBitmap = false;
        if (bitmap.ColorType != SKColorType.Rgba8888)
        {
            bitmap    = bitmap.Copy(SKColorType.Rgba8888)
                     ?? throw new ArgumentException("Cannot convert bitmap to RGBA8888.");
            ownBitmap = true;
        }

        // OpenGL places (0,0) at the bottom-left of the texture, but bitmap row 0
        // is the top row.  Flip vertically so the image appears the right way up —
        // the same fix the legacy renderer applies before glTexImage2D.
        var flipped = new SKBitmap(bitmap.Width, bitmap.Height,
                                   bitmap.ColorType, bitmap.AlphaType);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Scale(1f, -1f, 0f, bitmap.Height / 2f);
            canvas.DrawBitmap(bitmap, 0f, 0f);
        }
        if (ownBitmap) bitmap.Dispose();
        bitmap    = flipped;
        ownBitmap = true;

        TextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, TextureId);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            bitmap.Width, bitmap.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, bitmap.GetPixels());

        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        if (ownBitmap) bitmap.Dispose();
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, TextureId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteTexture(TextureId);
    }
}
