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

// CPU-side mirror of the "SkyPass" UBO declared in shader_data/vulkan/sky.frag (set=1,
// binding=0) -- see plan Section 8c-2. Same std140-by-hand discipline as VkPerFrameUbo.cs
// (vec3's 16-byte alignment/tail-packing is the only rule in play here; no arrays). Field
// order below is chosen to pack tightly, NOT to match sky.frag's own declaration order --
// std140 only requires the OFFSETS to agree between the two sides, not textual order, and
// GLSL is free to declare its members in whatever order reads best; verified against sky.frag
// by matching each field's type, not its position in the file.
//
// Layout, hand-computed then verified via Marshal.OffsetOf/SizeOf in a scratch console project
// (same verification VkPerFrameUbo.cs used, scratch project deleted afterward):
//   mat4  uInvViewProj       offset 0,   size 64
//   vec3  uCloudColor        offset 64,  size 12 (+4 pad, consumed by uCloudScale below)
//   float uCloudScale        offset 76,  size 4  (packs into uCloudColor's tail padding)
//   vec4  uCloudPosDensity1  offset 80,  size 16
//   vec4  uCloudPosDensity2  offset 96,  size 16
//   vec2  uCloudScrollRate   offset 112, size 8  (vec2 has 8-byte alignment; 112 is 8-aligned)
//   float uCloudShadow       offset 120, size 4
//   float uCloudVariance     offset 124, size 4
//   vec3  uCameraPos         offset 128, size 12 (+4 pad, consumed by uTime below)
//   float uTime              offset 140, size 4  (packs into uCameraPos's tail padding)
//   total size 144 (already a multiple of 16, so no trailing round-up needed)

using System.Numerics;
using System.Runtime.InteropServices;

namespace Radegast.Veles.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 144)]
internal struct VkSkyUbo
{
    [FieldOffset(0)]  public Matrix4x4 InvViewProj;
    [FieldOffset(64)] public Vector3 CloudColor;
    [FieldOffset(76)] public float CloudScale;
    [FieldOffset(80)] public Vector4 CloudPosDensity1;
    [FieldOffset(96)] public Vector4 CloudPosDensity2;
    [FieldOffset(112)] public Vector2 CloudScrollRate;
    [FieldOffset(120)] public float CloudShadow;
    [FieldOffset(124)] public float CloudVariance;
    [FieldOffset(128)] public Vector3 CameraPos;
    [FieldOffset(140)] public float Time;
}
