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
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Prepares an <see cref="SKBitmap"/> for GL upload by converting it to
    /// <see cref="SKColorType.Rgba8888"/> and flipping it vertically.
    /// Safe to call from any background thread — does no GL work.
    /// The returned bitmap is a new allocation owned by the caller.
    /// <paramref name="source"/> is disposed if a conversion copy was required.
    /// </summary>
    public static SKBitmap Preprocess(SKBitmap source)
    {
        // Step 1: ensure RGBA8888 (required format for GL upload).
        SKBitmap rgba;
        bool ownRgba;
        if (source.ColorType == SKColorType.Rgba8888)
        {
            rgba    = source;
            ownRgba = false;
        }
        else
        {
            rgba    = source.Copy(SKColorType.Rgba8888)
                   ?? throw new ArgumentException("Cannot convert bitmap to RGBA8888.");
            source.Dispose();
            ownRgba = true;
        }

        // Step 2: vertical flip — GL (0,0) is bottom-left; bitmap row 0 is top.
        // Copy rows from source in reverse order directly into the destination
        // pixel buffer. This bypasses the Skia rasterizer entirely (no canvas,
        // no paint, no blend) which is safe here because RGBA8888 pixels are
        // copied verbatim — alpha channels are preserved exactly.
        var flipped = new SKBitmap(rgba.Width, rgba.Height, rgba.ColorType, rgba.AlphaType);
        int rowBytes = rgba.RowBytes;
        int height   = rgba.Height;
        var src = MemoryMarshal.Cast<byte, byte>(rgba.GetPixelSpan());
        var dst = MemoryMarshal.Cast<byte, byte>(flipped.GetPixelSpan());
        for (int y = 0; y < height; y++)
        {
            src.Slice((height - 1 - y) * rowBytes, rowBytes)
               .CopyTo(dst.Slice(y * rowBytes, rowBytes));
        }
        if (ownRgba) rgba.Dispose();
        return flipped;
    }

    /// <summary>
    /// Creates a GL texture from a <em>pre-processed</em> bitmap (RGBA8888, vertically
    /// flipped) produced by <see cref="Preprocess"/>.  Only GL calls are made here —
    /// no Skia work — so this is safe to call frequently on the GL render thread.
    /// Ownership of <paramref name="bitmap"/> transfers to this constructor; it is
    /// disposed before the constructor returns.
    /// </summary>
    public GlTexture(SKBitmap bitmap)
    {
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

        bitmap.Dispose();
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
