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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using OpenTK.Mathematics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Compiles, links, and wraps a GLSL shader program.
/// All methods must be called on the GL render thread.
/// </summary>
public sealed class GlShader : IDisposable
{
    private uint _programId;
    private bool _disposed;

    // Cached uniform locations: populated on first Loc(name) call, reused on all subsequent.
    // Eliminates GL.GetUniformLocation driver round-trips on every Set(...) call.
    private readonly Dictionary<string, int> _uniformLocations = new(StringComparer.Ordinal);

    // Cached uniform *values*, keyed by location. Uniform state is per-program and persists
    // until changed, so as long as every write goes through Set(...) we can skip the
    // glUniform* driver round-trip when the value is unchanged. The hot DrawFaces loop sets
    // ~23 uniforms per face every frame, most identical between adjacent faces (fullbright,
    // glow, PBR flags, UV transforms…), so this eliminates the large majority of those calls.
    // Matrices are intentionally not cached — they change essentially every face, and a
    // 16-float compare costs about as much as the upload.
    private readonly Dictionary<int, int>     _cachedInt   = new();
    private readonly Dictionary<int, float>   _cachedFloat = new();
    private readonly Dictionary<int, Vector4> _cachedVec   = new();

    private GlShader(uint programId) => _programId = programId;

    /// <summary>Compile vertex and fragment stages and link into a program.</summary>
    /// <exception cref="InvalidOperationException">If compilation or linking fails.</exception>
    public static GlShader Compile(string vertSrc, string fragSrc)
    {
        var gl = GlApi.Gl;
        uint vert = CompileStage(ShaderType.VertexShader,   vertSrc);
        uint frag = CompileStage(ShaderType.FragmentShader, fragSrc);

        uint prog = gl.CreateProgram();
        gl.AttachShader(prog, vert);
        gl.AttachShader(prog, frag);
        gl.LinkProgram(prog);

        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int ok);
        if (ok == 0)
        {
            string log = gl.GetProgramInfoLog(prog);
            gl.DeleteProgram(prog);
            throw new InvalidOperationException($"Shader link error: {log}");
        }

        gl.DetachShader(prog, vert);
        gl.DetachShader(prog, frag);
        gl.DeleteShader(vert);
        gl.DeleteShader(frag);

        return new GlShader(prog);
    }

    public void Use()   => GlApi.Gl.UseProgram(_programId);
    public void Unuse() => GlApi.Gl.UseProgram(0);

    private int Loc(string name)
    {
        if (!_uniformLocations.TryGetValue(name, out var loc))
        {
            loc = GlApi.Gl.GetUniformLocation(_programId, name);
            _uniformLocations[name] = loc;
        }
        return loc;
    }

    public void Set(string name, int v)
    {
        int loc = Loc(name);
        if (loc < 0) return;
        if (_cachedInt.TryGetValue(loc, out var prev) && prev == v) return;
        _cachedInt[loc] = v;
        GlApi.Gl.Uniform1(loc, v);
    }

    public void Set(string name, float v)
    {
        int loc = Loc(name);
        if (loc < 0) return;
        if (_cachedFloat.TryGetValue(loc, out var prev) && prev == v) return;
        _cachedFloat[loc] = v;
        GlApi.Gl.Uniform1(loc, v);
    }

    public void Set(string name, bool v) => Set(name, v ? 1 : 0);

    public void Set(string name, Vector2 v)
    {
        int loc = Loc(name);
        if (loc < 0) return;
        var key = new Vector4(v.X, v.Y, 0f, 0f);
        if (_cachedVec.TryGetValue(loc, out var prev) && prev == key) return;
        _cachedVec[loc] = key;
        GlApi.Gl.Uniform2(loc, v.X, v.Y);
    }

    public void Set(string name, Vector3 v)
    {
        int loc = Loc(name);
        if (loc < 0) return;
        var key = new Vector4(v.X, v.Y, v.Z, 0f);
        if (_cachedVec.TryGetValue(loc, out var prev) && prev == key) return;
        _cachedVec[loc] = key;
        GlApi.Gl.Uniform3(loc, v.X, v.Y, v.Z);
    }

    public void Set(string name, Vector4 v)
    {
        int loc = Loc(name);
        if (loc < 0) return;
        if (_cachedVec.TryGetValue(loc, out var prev) && prev == v) return;
        _cachedVec[loc] = v;
        GlApi.Gl.Uniform4(loc, v.X, v.Y, v.Z, v.W);
    }

    /// <summary>Upload a uniform vec3 array (e.g. SSAO sample kernel).</summary>
    public void SetVec3Array(string name, Vector3[] values)
    {
        int loc = Loc(name);
        if (loc < 0) return;
        var gl = GlApi.Gl;
        for (int i = 0; i < values.Length; i++)
            gl.Uniform3(loc + i, values[i].X, values[i].Y, values[i].Z);
    }

    // OpenTK's Matrix4/Matrix3 are contiguous row-major float blocks (4×Vector4 / 3×Vector3).
    // Reinterpret them as a float span for Silk.NET's glUniformMatrix*fv (count = 1). The
    // 'transpose' flag carries through unchanged, so layout handling matches the old OpenTK call.
    public void Set(string name, ref Matrix4 m, bool transpose = false)
    {
        ReadOnlySpan<float> span = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<Matrix4, float>(ref m), 16);
        GlApi.Gl.UniformMatrix4(Loc(name), 1, transpose, span);
    }

    public void Set(string name, ref Matrix3 m, bool transpose = false)
    {
        ReadOnlySpan<float> span = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<Matrix3, float>(ref m), 9);
        GlApi.Gl.UniformMatrix3(Loc(name), 1, transpose, span);
    }

    private static uint CompileStage(ShaderType type, string src)
    {
        var gl = GlApi.Gl;
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, src);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"Shader compile error ({type}): {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GlApi.Gl.DeleteProgram(_programId);
    }
}
