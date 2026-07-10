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
using System.IO;
using CoreJ2K;
using LibreMetaverse.Imaging;
using SkiaSharp;

namespace Veles.Plugin.ImportExport;

/// <summary>
/// Saves SL texture asset bytes (J2K/J2C) to disk in the format implied by
/// the output path's extension.
/// </summary>
/// <remarks>
/// Extension → format mapping:
///   .j2k / .j2c / .jp2  — raw JPEG 2000 passthrough (no decode)
///   .png                 — PNG via SkiaSharp
///   .jpg / .jpeg         — JPEG via SkiaSharp (quality 90)
///   .webp                — WebP via SkiaSharp (quality 90)
///   .bmp                 — BMP via SkiaSharp
///   .tga                 — Truevision TGA via LibreMetaverse.Imaging.Targa
/// </remarks>
internal static class TextureConverter
{
    // Extensions that map to lossless SkiaSharp formats
    private static readonly string[] s_losslessExts = [".png", ".bmp"];

    /// <summary>
    /// Writes <paramref name="j2kBytes"/> to <paramref name="path"/> in the
    /// format determined by the file extension.
    /// </summary>
    /// <exception cref="InvalidDataException">J2K decode failed.</exception>
    public static void SaveAs(byte[] j2kBytes, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // Raw passthrough — no decode required
        if (ext is ".j2k" or ".j2c" or ".jp2")
        {
            File.WriteAllBytes(path, j2kBytes);
            return;
        }

        using var bitmap = J2kImage.DecodeToImage<SKBitmap>(j2kBytes)
            ?? throw new InvalidDataException("J2K decode returned null — asset data may be corrupt.");

        switch (ext)
        {
            case ".png":
                SkiaEncode(bitmap, path, SKEncodedImageFormat.Png, 100);
                break;

            case ".jpg":
            case ".jpeg":
                SkiaEncode(bitmap, path, SKEncodedImageFormat.Jpeg, 90);
                break;

            case ".webp":
                SkiaEncode(bitmap, path, SKEncodedImageFormat.Webp, 90);
                break;

            case ".bmp":
                SkiaEncode(bitmap, path, SKEncodedImageFormat.Bmp, 100);
                break;

            case ".tga":
                File.WriteAllBytes(path, Targa.Encode(LibreMetaverse.Imaging.Skia.SkiaTextureCodec.ToManagedImage(bitmap)));
                break;

            default:
                // Unknown extension: save as PNG and rename
                var pngPath = Path.ChangeExtension(path, ".png");
                SkiaEncode(bitmap, pngPath, SKEncodedImageFormat.Png, 100);
                // Rename to what the caller asked for (best-effort)
                if (pngPath != path)
                    File.Move(pngPath, path, overwrite: true);
                break;
        }
    }

    /// <summary>Returns the file picker display label for a given extension.</summary>
    public static string FormatLabel(string ext) => ext.ToLowerInvariant() switch
    {
        ".png"  => "PNG Image",
        ".jpg"  => "JPEG Image",
        ".jpeg" => "JPEG Image",
        ".webp" => "WebP Image",
        ".bmp"  => "BMP Image",
        ".tga"  => "Targa Image",
        ".j2k"  => "JPEG 2000 (raw)",
        ".j2c"  => "JPEG 2000 (raw)",
        ".jp2"  => "JPEG 2000 (raw)",
        _       => ext.TrimStart('.').ToUpperInvariant()
    };

    private static void SkiaEncode(SKBitmap bitmap, string path,
                                   SKEncodedImageFormat format, int quality)
    {
        using var encoded = bitmap.Encode(format, quality)
            ?? throw new InvalidOperationException(
                $"SkiaSharp failed to encode bitmap as {format}.");
        using var fs = File.Create(path);
        encoded.SaveTo(fs);
    }
}
