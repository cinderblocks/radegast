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
using OpenTK.Graphics.OpenGL4;

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
    private int  _vao, _vbo, _ebo;
    private int  _indexCount;
    private bool _disposed;
    // Kept in CPU memory so BuildLineEbo() doesn't need GL.GetBufferSubData (unavailable on ES).
    private readonly ushort[] _cpuIndices;
    private int  _lebo;      // line EBO (built lazily for ES wireframe)
    private int  _lineCount;

    private const int Stride = 32; // 8 floats × 4 bytes

    public GlMesh(float[] vertices, ushort[] indices)
        : this(vertices, vertices.Length, indices) { }

    /// <summary>
    /// Creates a mesh using only the first <paramref name="verticesLength"/> floats of
    /// <paramref name="vertices"/>, which may be an oversized ArrayPool-rented buffer.
    /// The caller is responsible for returning the rented buffer to the pool after this
    /// constructor returns.
    /// </summary>
    public GlMesh(float[] vertices, int verticesLength, ushort[] indices)
    {
        _indexCount = indices.Length;
        _cpuIndices = indices;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer,
            verticesLength * sizeof(float), vertices, BufferUsageHint.DynamicDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer,
            indices.Length * sizeof(ushort), indices, BufferUsageHint.StaticDraw);

        // Position (location 0)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride, 0);

        // Normal (location 1)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Stride, 12);

        // TexCoord (location 2)
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Stride, 24);

        GL.BindVertexArray(0);
    }

    public void Draw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount,
            DrawElementsType.UnsignedShort, 0);
        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Replace the vertex buffer contents in-place (animated LBS update).
    /// Must be called on the GL thread. <paramref name="verts"/> must have the
    /// same length as the original array passed to the constructor.
    /// </summary>
    public void UpdateVertices(float[] verts)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
            verts.Length * sizeof(float), verts);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    /// <summary>
    /// Replace the vertex buffer contents in-place using only the first
    /// <paramref name="vertsLength"/> floats of an oversized (e.g. ArrayPool-rented) buffer.
    /// Must be called on the GL thread.
    /// </summary>
    public void UpdateVertices(float[] verts, int vertsLength)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
            vertsLength * sizeof(float), verts);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    /// <summary>Draw edges as GL_LINES (ES wireframe fallback).</summary>
    public void DrawLines()
    {
        if (_lebo == 0) BuildLineEbo();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _lebo);
        GL.DrawElements(PrimitiveType.Lines, _lineCount,
            DrawElementsType.UnsignedShort, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo); // restore
        GL.BindVertexArray(0);
    }

    private void BuildLineEbo()
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
        _lebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _lebo);
        var arr = lineList.ToArray();
        GL.BufferData(BufferTarget.ElementArrayBuffer,
            arr.Length * sizeof(ushort), arr, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
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
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
        if (_lebo != 0) GL.DeleteBuffer(_lebo);
    }
}
