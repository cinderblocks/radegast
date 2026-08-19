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

// CPU-side mirror of the "SsaoParams" UBO declared in shader_data/vulkan/ssao.frag (set=0,
// binding=0) -- see plan Section 8c-2b. Same std140-by-hand discipline as VkPerFrameUbo.cs/
// VkSkyUbo.cs. Field order chosen to avoid ANY implicit padding gap (each offset is simply the
// previous field's offset + its own size, verified rather than assumed):
//   vec3 uKernel[64]   offset 0,    size 1024 (64 * 16-byte std140 array-element stride --
//                       array elements are ALWAYS 16-byte-strided in std140, even for a vec3
//                       whose own base alignment/size would otherwise be smaller; only
//                       uKernelSize of the 64 slots are read by the shader, matching GL's own
//                       "declare 64, use up to 64 via a runtime count" shape)
//   mat4 uProj         offset 1024, size 64  (16-byte aligned; 1024 already is)
//   vec2 uNoiseScale   offset 1088, size 8   (8-byte aligned; 1088 already is)
//   vec2 uScreenSize   offset 1096, size 8
//   float uRadius      offset 1104, size 4
//   float uBias        offset 1108, size 4
//   float uStrength    offset 1112, size 4
//   int   uKernelSize  offset 1116, size 4
//   total size 1120 (already a multiple of 16, no trailing round-up needed)
//
// A fixed float buffer (not Vector3[64]/Vector4[64]) holds the kernel: C# arrays are heap
// references, not inline value data, so they can't sit at a fixed byte offset inside a struct
// meant to be raw-memcpy'd into a UBO -- an unsafe fixed buffer is the inline, blittable shape
// this needs. Every kernel sample occupies 4 floats (xyz + one unused padding float, matching
// the 16-byte array-element stride above), so Kernel[i*4+0..2] is the i'th sample's xyz.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Radegast.Veles.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 1120)]
internal unsafe struct VkSsaoUbo
{
    [FieldOffset(0)] public fixed float Kernel[256];
    [FieldOffset(1024)] public Matrix4x4 Proj;
    [FieldOffset(1088)] public Vector2 NoiseScale;
    [FieldOffset(1096)] public Vector2 ScreenSize;
    [FieldOffset(1104)] public float Radius;
    [FieldOffset(1108)] public float Bias;
    [FieldOffset(1112)] public float Strength;
    [FieldOffset(1116)] public int KernelSize;

    /// <summary>Writes the i'th kernel sample's xyz (w stays 0, unused padding).</summary>
    public void SetKernelSample(int i, Vector3 v)
    {
        Kernel[i * 4 + 0] = v.X;
        Kernel[i * 4 + 1] = v.Y;
        Kernel[i * 4 + 2] = v.Z;
        Kernel[i * 4 + 3] = 0f;
    }
}
