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
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Uploads and wraps a VAO + interleaved VBO + EBO on the GPU.
/// <para>
/// Vertex layout (32 bytes per vertex):
/// <list type="table">
///   <item><term>Attribute 0</term><description>Position  – vec3 @ offset  0</description></item>
///   <item><term>Attribute 1</term><description>Normal    – vec3 @ offset 12</description></item>
///   <item><term>Attribute 2</term><description>TexCoord  – vec2 @ offset 24</description></item>
/// </list>
/// </para>
/// Must be created and disposed on the GL render thread.
/// </summary>
public sealed class GlMesh : IDisposable
{
    private uint _vao, _vbo, _ebo;
    private int  _indexCount;
    private bool _disposed;
    // Kept in CPU memory so BuildLineEbo() doesn't need GL.GetBufferSubData (unavailable on ES).
    private readonly ushort[] _cpuIndices;
    private uint _lebo;      // line EBO (built lazily for ES wireframe)
    private int  _lineCount;

    // Exposed so GlInstanceDrawer can set up a shared instance VAO pointing to this mesh's buffers.
    internal uint Vbo        => _vbo;
    internal uint Ebo        => _ebo;
    internal int  IndexCount => _indexCount;
    internal const int VertexStride = 32; // 8 floats × 4 bytes

    public GlMesh(float[] vertices, ushort[] indices)
        : this(vertices, vertices.Length, indices) { }

    /// <summary>
    /// Creates a mesh using only the first <paramref name="verticesLength"/> floats of
    /// <paramref name="vertices"/>, which may be an oversized ArrayPool-rented buffer.
    /// The caller is responsible for returning the rented buffer to the pool after this
    /// constructor returns.
    /// </summary>
    public unsafe GlMesh(float[] vertices, int verticesLength, ushort[] indices)
    {
        _indexCount = indices.Length;
        _cpuIndices = indices;

        var gl = GlApi.Gl;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        _ebo = gl.GenBuffer();

        gl.BindVertexArray(_vao);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = vertices)
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(verticesLength * sizeof(float)), p, BufferUsageARB.DynamicDraw);

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (ushort* p = indices)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                (nuint)(indices.Length * sizeof(ushort)), p, BufferUsageARB.StaticDraw);

        // Position (location 0)
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VertexStride, (void*)0);

        // Normal (location 1)
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, VertexStride, (void*)12);

        // TexCoord (location 2)
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, VertexStride, (void*)24);

        gl.BindVertexArray(0);
    }

    public unsafe void Draw()
    {
        var gl = GlApi.Gl;
        gl.BindVertexArray(_vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount,
            DrawElementsType.UnsignedShort, (void*)0);
        gl.BindVertexArray(0);
    }

    /// <summary>
    /// Replace the vertex buffer contents in-place (animated LBS update).
    /// Must be called on the GL thread. <paramref name="verts"/> must have the
    /// same length as the original array passed to the constructor.
    /// </summary>
    public unsafe void UpdateVertices(float[] verts)
    {
        var gl = GlApi.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = verts)
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                (nuint)(verts.Length * sizeof(float)), p);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>
    /// Replace the vertex buffer contents in-place using only the first
    /// <paramref name="vertsLength"/> floats of an oversized (e.g. ArrayPool-rented) buffer.
    /// Must be called on the GL thread.
    /// </summary>
    public unsafe void UpdateVertices(float[] verts, int vertsLength)
    {
        var gl = GlApi.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = verts)
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                (nuint)(vertsLength * sizeof(float)), p);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>Draw edges as GL_LINES (ES wireframe fallback).</summary>
    public unsafe void DrawLines()
    {
        if (_lebo == 0) BuildLineEbo();
        var gl = GlApi.Gl;
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _lebo);
        gl.DrawElements(PrimitiveType.Lines, (uint)_lineCount,
            DrawElementsType.UnsignedShort, (void*)0);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo); // restore
        gl.BindVertexArray(0);
    }

    private unsafe void BuildLineEbo()
    {
        var edges    = new HashSet<(ushort, ushort)>();
        var lineList = new List<ushort>();

        for (int i = 0; i < _cpuIndices.Length; i += 3)
        {
            ushort a = _cpuIndices[i], b = _cpuIndices[i + 1], c = _cpuIndices[i + 2];
            AddEdge(edges, lineList, a, b);
            AddEdge(edges, lineList, b, c);
            AddEdge(edges, lineList, c, a);
        }

        _lineCount = lineList.Count;
        var gl = GlApi.Gl;
        _lebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _lebo);
        var arr = lineList.ToArray();
        fixed (ushort* p = arr)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                (nuint)(arr.Length * sizeof(ushort)), p, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
    }

    private static void AddEdge(
        HashSet<(ushort, ushort)> edges,
        List<ushort> list,
        ushort a, ushort b)
    {
        var key = a < b ? (a, b) : (b, a);
        if (edges.Add(key)) { list.Add(a); list.Add(b); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var gl = GlApi.Gl;
        gl.DeleteVertexArray(_vao);
        gl.DeleteBuffer(_vbo);
        gl.DeleteBuffer(_ebo);
        if (_lebo != 0) gl.DeleteBuffer(_lebo);
    }
}
