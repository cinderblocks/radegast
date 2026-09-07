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

// CPU-side mirror of the "WaterPass" UBO declared in shader_data/vulkan/water.frag (set=1,
// binding=0). Same std140-by-hand discipline as VkSkyUbo.cs/
// VkPerFrameUbo.cs. Field order below matches water.frag's own declaration order (no reordering
// needed to pack tightly this time -- it already falls out that way: three mat4s, then a
// vec3+float pair that packs into the vec3's tail padding exactly like VkSkyUbo's
// CameraPos/Time, then a second float, then a 16-aligned vec4, then a trailing int).
//
// Layout, hand-computed then verified via Marshal.OffsetOf/SizeOf in a scratch console project
// (same verification VkPerFrameUbo.cs/VkSkyUbo.cs used, scratch project deleted afterward):
//   mat4  uViewProj       offset 0,   size 64
//   mat4  uReflViewProj   offset 64,  size 64
//   mat4  uInvViewProj    offset 128, size 64
//   vec3  uEyePos         offset 192, size 12 (+4 pad, consumed by uWaterHeight below)
//   float uWaterHeight    offset 204, size 4  (packs into uEyePos's tail padding)
//   float uTime           offset 208, size 4
//   vec4  uWaterColor     offset 224, size 16 (vec4 is 16-aligned; 208+4=212 rounds up to 224)
//   int   uHasReflection  offset 240, size 4
//   int   uHasRefraction  offset 244, size 4 (packs right after uHasReflection, still within
//                                              the existing 256-byte total -- no size change)
//   total size 256

using System.Numerics;
using System.Runtime.InteropServices;

namespace Radegast.Veles.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 256)]
internal struct VkWaterUbo
{
    [FieldOffset(0)]   public Matrix4x4 ViewProj;
    [FieldOffset(64)]  public Matrix4x4 ReflViewProj;
    [FieldOffset(128)] public Matrix4x4 InvViewProj;
    [FieldOffset(192)] public Vector3 EyePos;
    [FieldOffset(204)] public float WaterHeight;
    [FieldOffset(208)] public float Time;
    [FieldOffset(224)] public Vector4 WaterColor;
    [FieldOffset(240)] public int HasReflection;
    [FieldOffset(244)] public int HasRefraction;
}
