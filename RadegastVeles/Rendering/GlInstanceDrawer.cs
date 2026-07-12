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
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Manages the reusable VAO + streaming VBO used for instanced draws.
/// <para>
/// Per-instance data layout (44 floats / 176 bytes per instance, column-major matrices):
/// <list type="bullet">
///   <item>[0..15]  mat4  MVP</item>
///   <item>[16..31] mat4  ModelView</item>
///   <item>[32..35] vec4  color (RGBA)</item>
///   <item>[36..39] vec4  misc  (fullbright01, glow, shiny, alphaCutoff)</item>
///   <item>[40]     float alphaMode (cast from int)</item>
///   <item>[41..43] padding</item>
/// </list>
/// </para>
/// Must be created and disposed on the GL thread.
/// </summary>
internal sealed class GlInstanceDrawer : IDisposable
{
    public const int InstanceFloats = 44;
    public const int InstanceStride = InstanceFloats * sizeof(float); // 176 bytes

    // Attribute locations for per-instance vertex attributes.
    // Each mat4 occupies 4 consecutive locations (one per column).
    private const uint LocMvp0      = 3;   // mat4 aInstMvp  → 3,4,5,6
    private const uint LocMv0       = 7;   // mat4 aInstMv   → 7,8,9,10
    private const uint LocColor     = 11;  // vec4 aInstColor
    private const uint LocMisc      = 12;  // vec4 aInstMisc
    private const uint LocAlphaMode = 13;  // float aInstAlphaMode

    private uint _vao;
    private uint _vbo;
    private int  _vboFloatCap;
    private bool _instanceAttribsSetup;
    private bool _disposed;

    public GlInstanceDrawer()
    {
        var gl = GlApi.Gl;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
    }

    /// <summary>
    /// Uploads per-instance data and issues a single <c>DrawElementsInstanced</c> call.
    /// Must be called on the GL thread.
    /// </summary>
    public unsafe void DrawInstanced(GlMesh mesh, float[] instanceData, int instanceCount)
    {
        if (instanceCount <= 0) return;

        var gl = GlApi.Gl;

        // Upload per-instance data to the streaming VBO.
        int floatCount = instanceCount * InstanceFloats;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = instanceData)
        {
            if (floatCount > _vboFloatCap)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(floatCount * sizeof(float)), p, BufferUsageARB.StreamDraw);
                _vboFloatCap = floatCount;
            }
            else
            {
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                    (nuint)(floatCount * sizeof(float)), p);
            }
        }

        gl.BindVertexArray(_vao);

        // Set up per-instance attribs once (they always point to _vbo which never changes handle).
        if (!_instanceAttribsSetup)
        {
            SetupInstanceAttribs();
            _instanceAttribsSetup = true;
        }

        // Re-attach the geometry buffers unconditionally on every batch. Caching this by
        // buffer handle was actively dangerous (GL recycles deleted names, so a rebuilt
        // object's new mesh can inherit its predecessor's vbo id and the skipped setup
        // leaves the VAO reading the orphaned old buffer), and even identity caching
        // assumes no other code ever perturbs this VAO's attachments. A few redundant
        // pointer/bind calls per batch are noise next to the instanced draw itself.
        SetupGeomAttribs(mesh);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, mesh.Ebo);

        gl.DrawElementsInstanced(PrimitiveType.Triangles,
            (uint)mesh.IndexCount, DrawElementsType.UnsignedShort, (void*)0, (uint)instanceCount);

        gl.BindVertexArray(0);
    }

    private unsafe void SetupGeomAttribs(GlMesh mesh)
    {
        var gl = GlApi.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mesh.Vbo);
        const uint stride = GlMesh.VertexStride;

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.VertexAttribDivisor(0, 0);

        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
        gl.VertexAttribDivisor(1, 0);

        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)24);
        gl.VertexAttribDivisor(2, 0);

        // Tangent (location 14) — per-vertex from mesh VBO, divisor 0
        gl.EnableVertexAttribArray(14);
        gl.VertexAttribPointer(14, 4, VertexAttribPointerType.Float, false, stride, (void*)32);
        gl.VertexAttribDivisor(14, 0);
    }

    private unsafe void SetupInstanceAttribs()
    {
        var gl = GlApi.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // mat4 aInstMvp → locations 3,4,5,6 (each column = vec4)
        for (uint col = 0; col < 4; col++)
        {
            uint loc = LocMvp0 + col;
            gl.EnableVertexAttribArray(loc);
            gl.VertexAttribPointer(loc, 4, VertexAttribPointerType.Float, false,
                InstanceStride, (void*)((col * 4) * sizeof(float)));
            gl.VertexAttribDivisor(loc, 1);
        }

        // mat4 aInstMv → locations 7,8,9,10
        for (uint col = 0; col < 4; col++)
        {
            uint loc = LocMv0 + col;
            gl.EnableVertexAttribArray(loc);
            gl.VertexAttribPointer(loc, 4, VertexAttribPointerType.Float, false,
                InstanceStride, (void*)((16 + col * 4) * sizeof(float)));
            gl.VertexAttribDivisor(loc, 1);
        }

        // vec4 aInstColor → location 11
        gl.EnableVertexAttribArray(LocColor);
        gl.VertexAttribPointer(LocColor, 4, VertexAttribPointerType.Float, false,
            InstanceStride, (void*)(32 * sizeof(float)));
        gl.VertexAttribDivisor(LocColor, 1);

        // vec4 aInstMisc → location 12 (fullbright, glow, shiny, alphaCutoff)
        gl.EnableVertexAttribArray(LocMisc);
        gl.VertexAttribPointer(LocMisc, 4, VertexAttribPointerType.Float, false,
            InstanceStride, (void*)(36 * sizeof(float)));
        gl.VertexAttribDivisor(LocMisc, 1);

        // float aInstAlphaMode → location 13
        gl.EnableVertexAttribArray(LocAlphaMode);
        gl.VertexAttribPointer(LocAlphaMode, 1, VertexAttribPointerType.Float, false,
            InstanceStride, (void*)(40 * sizeof(float)));
        gl.VertexAttribDivisor(LocAlphaMode, 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var gl = GlApi.Gl;
        gl.DeleteVertexArray(_vao);
        gl.DeleteBuffer(_vbo);
    }
}
