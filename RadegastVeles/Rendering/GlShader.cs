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
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Compiles, links, and wraps a GLSL shader program.
/// All methods must be called on the GL render thread.
/// </summary>
public sealed class GlShader : IDisposable
{
    private int  _programId;
    private bool _disposed;

    private GlShader(int programId) => _programId = programId;

    /// <summary>Compile vertex and fragment stages and link into a program.</summary>
    /// <exception cref="InvalidOperationException">If compilation or linking fails.</exception>
    public static GlShader Compile(string vertSrc, string fragSrc)
    {
        int vert = CompileStage(ShaderType.VertexShader,   vertSrc);
        int frag = CompileStage(ShaderType.FragmentShader, fragSrc);

        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vert);
        GL.AttachShader(prog, frag);
        GL.LinkProgram(prog);

        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetProgramInfoLog(prog);
            GL.DeleteProgram(prog);
            throw new InvalidOperationException($"Shader link error: {log}");
        }

        GL.DetachShader(prog, vert);
        GL.DetachShader(prog, frag);
        GL.DeleteShader(vert);
        GL.DeleteShader(frag);

        return new GlShader(prog);
    }

    public void Use()   => GL.UseProgram(_programId);
    public void Unuse() => GL.UseProgram(0);

    private int Loc(string name) => GL.GetUniformLocation(_programId, name);

    public void Set(string name, int    v) => GL.Uniform1(Loc(name), v);
    public void Set(string name, float  v) => GL.Uniform1(Loc(name), v);
    public void Set(string name, bool   v) => GL.Uniform1(Loc(name), v ? 1 : 0);
    public void Set(string name, Vector4 v) => GL.Uniform4(Loc(name), v);

    public void Set(string name, ref Matrix4 m, bool transpose = false) =>
        GL.UniformMatrix4(Loc(name), transpose, ref m);

    public void Set(string name, ref Matrix3 m, bool transpose = false) =>
        GL.UniformMatrix3(Loc(name), transpose, ref m);

    private static int CompileStage(ShaderType type, string src)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, src);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"Shader compile error ({type}): {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteProgram(_programId);
    }
}
