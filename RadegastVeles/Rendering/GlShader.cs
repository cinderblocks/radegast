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
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

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

    // Cached uniform *values*, indexed by location. Uniform state is per-program and persists
    // until changed, so as long as every write goes through Set(...) we can skip the
    // glUniform* driver round-trip when the value is unchanged. The hot DrawFaces loop sets
    // ~23 uniforms per face every frame, most identical between adjacent faces (fullbright,
    // glow, PBR flags, UV transforms…), so this eliminates the large majority of those calls.
    // Matrices are intentionally not cached — they change essentially every face, and a
    // 16-float compare costs about as much as the upload.
    // Flat arrays (grown on demand) instead of Dictionary<int,T>: uniform locations are
    // small sequential ints, so this turns a hash lookup per Set(...) into an array index.
    private int[]     _cachedInt      = Array.Empty<int>();
    private bool[]    _cachedIntHas   = Array.Empty<bool>();
    private float[]   _cachedFloat    = Array.Empty<float>();
    private bool[]    _cachedFloatHas = Array.Empty<bool>();
    private Vector4[] _cachedVec      = Array.Empty<Vector4>();
    private bool[]    _cachedVecHas   = Array.Empty<bool>();

    // Locations above this are not value-cached (defensive against drivers handing out
    // sparse/large locations); the uniform is simply uploaded every time.
    private const int MaxCachedLocation = 1024;

    private static void EnsureCap<T>(ref T[] values, ref bool[] has, int loc)
    {
        if (loc < values.Length) return;
        int newLen = Math.Max(64, values.Length);
        while (newLen <= loc) newLen *= 2;
        Array.Resize(ref values, newLen);
        Array.Resize(ref has, newLen);
    }

    private GlShader(uint programId) => _programId = programId;

    /// <summary>Compile a compute shader and link into a program.</summary>
    /// <exception cref="InvalidOperationException">If compilation or linking fails.</exception>
    public static GlShader CompileCompute(string compSrc)
    {
        var gl = GlApi.Gl;
        uint comp = CompileStage(ShaderType.ComputeShader, compSrc);
        uint prog = gl.CreateProgram();
        gl.AttachShader(prog, comp);
        gl.LinkProgram(prog);
        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int ok);
        if (ok == 0)
        {
            string log = gl.GetProgramInfoLog(prog);
            gl.DeleteProgram(prog);
            throw new InvalidOperationException($"Compute shader link error: {log}");
        }
        gl.DetachShader(prog, comp);
        gl.DeleteShader(comp);
        return new GlShader(prog);
    }

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

    /// <summary>
    /// Resolves (and caches) the location of a uniform so hot draw loops can use the
    /// int-location <c>Set</c> overloads and skip the per-call string lookup.
    /// Returns -1 when the uniform does not exist (optimised out or wrong name);
    /// all int-location overloads treat -1 as a no-op.
    /// </summary>
    public int GetLocation(string name) => Loc(name);

    public void Set(string name, int v)     => Set(Loc(name), v);
    public void Set(string name, float v)   => Set(Loc(name), v);
    public void Set(string name, bool v)    => Set(Loc(name), v ? 1 : 0);
    public void Set(string name, Vector2 v) => Set(Loc(name), v);
    public void Set(string name, Vector3 v) => Set(Loc(name), v);
    public void Set(string name, Vector4 v) => Set(Loc(name), v);

    public void Set(int loc, int v)
    {
        if (loc < 0) return;
        if ((uint)loc < MaxCachedLocation)
        {
            EnsureCap(ref _cachedInt, ref _cachedIntHas, loc);
            if (_cachedIntHas[loc] && _cachedInt[loc] == v) return;
            _cachedInt[loc] = v;
            _cachedIntHas[loc] = true;
        }
        GlApi.Gl.Uniform1(loc, v);
    }

    public void Set(int loc, float v)
    {
        if (loc < 0) return;
        if ((uint)loc < MaxCachedLocation)
        {
            EnsureCap(ref _cachedFloat, ref _cachedFloatHas, loc);
            if (_cachedFloatHas[loc] && _cachedFloat[loc] == v) return;
            _cachedFloat[loc] = v;
            _cachedFloatHas[loc] = true;
        }
        GlApi.Gl.Uniform1(loc, v);
    }

    public void Set(int loc, bool v) => Set(loc, v ? 1 : 0);

    public void Set(int loc, Vector2 v)
    {
        if (loc < 0) return;
        var key = new Vector4(v.X, v.Y, 0f, 0f);
        if ((uint)loc < MaxCachedLocation)
        {
            EnsureCap(ref _cachedVec, ref _cachedVecHas, loc);
            if (_cachedVecHas[loc] && _cachedVec[loc] == key) return;
            _cachedVec[loc] = key;
            _cachedVecHas[loc] = true;
        }
        GlApi.Gl.Uniform2(loc, v.X, v.Y);
    }

    public void Set(int loc, Vector3 v)
    {
        if (loc < 0) return;
        var key = new Vector4(v.X, v.Y, v.Z, 0f);
        if ((uint)loc < MaxCachedLocation)
        {
            EnsureCap(ref _cachedVec, ref _cachedVecHas, loc);
            if (_cachedVecHas[loc] && _cachedVec[loc] == key) return;
            _cachedVec[loc] = key;
            _cachedVecHas[loc] = true;
        }
        GlApi.Gl.Uniform3(loc, v.X, v.Y, v.Z);
    }

    public void Set(int loc, Vector4 v)
    {
        if (loc < 0) return;
        if ((uint)loc < MaxCachedLocation)
        {
            EnsureCap(ref _cachedVec, ref _cachedVecHas, loc);
            if (_cachedVecHas[loc] && _cachedVec[loc] == v) return;
            _cachedVec[loc] = v;
            _cachedVecHas[loc] = true;
        }
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

    /// <summary>Upload a uniform float array (e.g. local-light radius/falloff).</summary>
    public void SetFloatArray(string name, float[] values)
    {
        int loc = Loc(name);
        if (loc < 0) return;
        var gl = GlApi.Gl;
        for (int i = 0; i < values.Length; i++)
            gl.Uniform1(loc + i, values[i]);
    }

    // System.Numerics Matrix4x4/Matrix3x3 are contiguous row-major float blocks.
    // Reinterpret them as a float span for Silk.NET's glUniformMatrix*fv (count = 1).
    public void Set(string name, ref Matrix4x4 m, bool transpose = false)
        => Set(Loc(name), ref m, transpose);

    public void Set(string name, ref Matrix3x3 m, bool transpose = false)
        => Set(Loc(name), ref m, transpose);

    public void Set(int loc, ref Matrix4x4 m, bool transpose = false)
    {
        if (loc < 0) return;
        ReadOnlySpan<float> span = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<Matrix4x4, float>(ref m), 16);
        GlApi.Gl.UniformMatrix4(loc, 1, transpose, span);
    }

    public void Set(int loc, ref Matrix3x3 m, bool transpose = false)
    {
        if (loc < 0) return;
        ReadOnlySpan<float> span = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<Matrix3x3, float>(ref m), 9);
        GlApi.Gl.UniformMatrix3(loc, 1, transpose, span);
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
