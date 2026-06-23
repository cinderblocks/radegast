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
using System.Numerics;
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// GPU-side dynamic buffer for CPU-billboarded particle quads.
/// <para>
/// Each particle is expanded on the CPU into two triangles (6 vertices) that face the camera.
/// The billboard expansion is done every frame on the CPU using the camera right/up vectors,
/// matching the SL viewer approach (lldrawpoolalpha / LLVOPartGroup) rather than requiring a
/// geometry shader (not available on GL ES 3.0 / ANGLE).
/// </para>
/// <para>
/// Vertex layout per vertex (stride = 36 bytes):
/// <list type="table">
///   <item><term>0</term><description>Position  – vec3 (12 bytes)</description></item>
///   <item><term>12</term><description>TexCoord  – vec2 (8 bytes)</description></item>
///   <item><term>20</term><description>Color     – vec4 (16 bytes)</description></item>
/// </list>
/// </para>
/// Must be created, updated, drawn, and disposed on the GL render thread.
/// </summary>
internal sealed class GlParticleBuffer : IDisposable
{
    private uint _vao, _vbo;
    private int  _uploadedCount;
    private bool _disposed;

    // 6 vertices per particle, 9 floats per vertex.
    private const int FloatsPerVertex = 9;
    private const int VerticesPerParticle = 6;
    private const int Stride = FloatsPerVertex * sizeof(float); // 36 bytes

    public unsafe GlParticleBuffer()
    {
        var gl = GlApi.Gl;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // Allocate an empty buffer; we'll grow via BufferData each tick.
        gl.BufferData(BufferTargetARB.ArrayBuffer, 0, null, BufferUsageARB.StreamDraw);

        // Position (location 0) – vec3 @ offset 0
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride, (void*)0);

        // TexCoord (location 1) – vec2 @ offset 12
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Stride, (void*)12);

        // Color (location 2) – vec4 @ offset 20
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, Stride, (void*)20);

        gl.BindVertexArray(0);
    }

    /// <summary>
    /// Expand particle data into camera-facing billboard quads and upload to the GPU.
    /// </summary>
    /// <param name="particles">Particle snapshots from the simulator.</param>
    /// <param name="cameraRight">Camera right vector in world space.</param>
    /// <param name="cameraUp">Camera up vector in world space.</param>
    public unsafe void Upload(ReadOnlySpan<ParticleVertex> particles, Vector3 cameraRight, Vector3 cameraUp)
    {
        _uploadedCount = particles.Length;
        if (_uploadedCount == 0) return;

        float[] verts = new float[_uploadedCount * VerticesPerParticle * FloatsPerVertex];
        int idx = 0;

        // Pre-compute UV corners.
        ReadOnlySpan<Vector2> uvs = stackalloc Vector2[]
        {
            new(0f, 1f), // bottom-left
            new(1f, 1f), // bottom-right
            new(1f, 0f), // top-right
            new(0f, 1f), // bottom-left  (second tri)
            new(1f, 0f), // top-right
            new(0f, 0f), // top-left
        };

        foreach (var p in particles)
        {
            var r = cameraRight * p.HalfW;
            var u = cameraUp    * p.HalfH;

            // Four corners of the billboard quad.
            var c0 = p.Position - r - u; // BL
            var c1 = p.Position + r - u; // BR
            var c2 = p.Position + r + u; // TR
            var c3 = p.Position - r + u; // TL

            // Triangle 1: BL, BR, TR
            verts[idx++] = c0.X; verts[idx++] = c0.Y; verts[idx++] = c0.Z;
            verts[idx++] = uvs[0].X; verts[idx++] = uvs[0].Y;
            verts[idx++] = p.Color.X; verts[idx++] = p.Color.Y; verts[idx++] = p.Color.Z; verts[idx++] = p.Color.W;

            verts[idx++] = c1.X; verts[idx++] = c1.Y; verts[idx++] = c1.Z;
            verts[idx++] = uvs[1].X; verts[idx++] = uvs[1].Y;
            verts[idx++] = p.Color.X; verts[idx++] = p.Color.Y; verts[idx++] = p.Color.Z; verts[idx++] = p.Color.W;

            verts[idx++] = c2.X; verts[idx++] = c2.Y; verts[idx++] = c2.Z;
            verts[idx++] = uvs[2].X; verts[idx++] = uvs[2].Y;
            verts[idx++] = p.Color.X; verts[idx++] = p.Color.Y; verts[idx++] = p.Color.Z; verts[idx++] = p.Color.W;

            // Triangle 2: BL, TR, TL
            verts[idx++] = c0.X; verts[idx++] = c0.Y; verts[idx++] = c0.Z;
            verts[idx++] = uvs[3].X; verts[idx++] = uvs[3].Y;
            verts[idx++] = p.Color.X; verts[idx++] = p.Color.Y; verts[idx++] = p.Color.Z; verts[idx++] = p.Color.W;

            verts[idx++] = c2.X; verts[idx++] = c2.Y; verts[idx++] = c2.Z;
            verts[idx++] = uvs[4].X; verts[idx++] = uvs[4].Y;
            verts[idx++] = p.Color.X; verts[idx++] = p.Color.Y; verts[idx++] = p.Color.Z; verts[idx++] = p.Color.W;

            verts[idx++] = c3.X; verts[idx++] = c3.Y; verts[idx++] = c3.Z;
            verts[idx++] = uvs[5].X; verts[idx++] = uvs[5].Y;
            verts[idx++] = p.Color.X; verts[idx++] = p.Color.Y; verts[idx++] = p.Color.Z; verts[idx++] = p.Color.W;
        }

        var gl = GlApi.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = verts)
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(verts.Length * sizeof(float)), p, BufferUsageARB.StreamDraw);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>Draw all uploaded particle quads.</summary>
    public void Draw()
    {
        if (_uploadedCount == 0) return;
        var gl = GlApi.Gl;
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(_uploadedCount * VerticesPerParticle));
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var gl = GlApi.Gl;
        if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
        if (_vbo != 0) { gl.DeleteBuffer(_vbo);      _vbo = 0; }
    }
}
