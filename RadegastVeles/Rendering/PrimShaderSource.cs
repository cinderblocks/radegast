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
using Avalonia.Platform;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Loads GLSL shader source files from the embedded Avalonia resource store
/// at <c>avares://RadegastVeles/Rendering/shader_data/</c>.
/// </summary>
internal static class ShaderLoader
{
    private const string Base = "avares://RadegastVeles/Rendering/shader_data/";

    /// <summary>Load a shader source file by name (e.g. "prim.vert").</summary>
    public static string Load(string filename) => Load(filename, 0);

    private static string Load(string filename, int depth)
    {
        // GLSL has no native #include; a shallow recursion cap catches accidental cycles.
        if (depth > 4)
            throw new InvalidOperationException(
                $"Shader include depth exceeded while loading '{filename}' (include cycle?).");

        var uri = new Uri(Base + filename);
        try
        {
            using var stream = AssetLoader.Open(uri);
            // Use UTF-8 with BOM detection; TrimStart removes any residual BOM or
            // leading whitespace that would push #version off line 1.
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var raw = reader.ReadToEnd()
                            .TrimStart()
                            .Replace("\r\n", "\n")  // normalize CRLF -> LF
                            .Replace("\r",   "\n"); // normalize lone CR -> LF

            // GLSL ES 3.00 §3.1 permits only 7-bit ASCII source characters.
            // Strip anything outside that range (e.g. Unicode box-drawing chars
            // used as decorative comment rulers) so strict drivers don't error.
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
                if (c < 128) sb.Append(c);

            return ExpandIncludes(sb.ToString(), depth);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to load shader '{uri}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Replaces lines of the form <c>#include "file.glsl"</c> with the named file's
    /// contents (recursively). Included files must not contain a <c>#version</c>
    /// directive of their own.
    /// </summary>
    private static string ExpandIncludes(string source, int depth)
    {
        if (!source.Contains("#include")) return source;

        var outSb = new System.Text.StringBuilder(source.Length);
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#include", StringComparison.Ordinal))
            {
                int q1 = trimmed.IndexOf('"');
                int q2 = q1 >= 0 ? trimmed.IndexOf('"', q1 + 1) : -1;
                if (q2 < 0)
                    throw new InvalidOperationException($"Malformed shader #include line: '{line}'");
                outSb.Append(Load(trimmed.Substring(q1 + 1, q2 - q1 - 1), depth + 1)).Append('\n');
            }
            else
            {
                outSb.Append(line).Append('\n');
            }
        }
        return outSb.ToString();
    }
}
