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

// CPU-side mirror of the "PerFrame" UBO declared identically in shader_data/vulkan/prim.vert
// and prim.frag (set=0, binding=0). Every FieldOffset below was computed
// by hand against GLSL's std140 layout rules (the default block layout, no explicit
// "layout(std140)" needed since it's std140 unless std430 is requested) directly from the
// GLSL declaration order, NOT [StructLayout(LayoutKind.Sequential)] -- Sequential would pack
// this wrong in several places (see the vec3/array notes below) and produce a struct that
// compiles fine in C# but reads garbage in the shader, silently. A mistake here is a "lighting
// looks subtly wrong" bug, not a crash, and the validation layer (opt-in via
// VELES_VK_VALIDATION=1, see VkContext.cs) is the main thing that would ever catch a
// size/offset mismatch here.
//
// std140 rules actually in play here (the ones that bite, not an exhaustive list):
//   - vec3 has a 16-byte base alignment (NOT 12) -- a bare vec3 leaves 4 bytes of tail
//     padding unless a scalar (e.g. a float) immediately follows it in the source, in which
//     case that scalar packs into the padding. This struct's field order/offsets mirror
//     exactly which GLSL fields do/don't get that packing -- do not reorder fields relative
//     to the GLSL source without recomputing every following offset.
//   - float[N] and vec3[N] arrays: EVERY element is padded to a 16-byte stride, even float[N]
//     (a float array is NOT 4 bytes/element in std140 -- it's 16). This is why
//     uPointLightRadius[4]/uPointLightFalloff[4]/uPointShadowFar[2] below are NOT
//     `fixed float[N]` -- that would assume 4-byte packing and desync every offset after
//     them. Each array element is instead its own explicitly-offset field.
//   - mat4 is 4 columns of vec4 (64 bytes, 16-byte aligned), matching System.Numerics
//     Matrix4x4's 64-byte size exactly.
//
// Matrix upload convention -- verified against the existing GL path, not assumed: GlShader.cs
// (:240-253) documents "System.Numerics Matrix4x4 ... are contiguous row-major float blocks"
// and every call site in the GL rendering code passes transpose=false (the default) to
// glUniformMatrix4fv. Row-major-memory + row-vector convention (System.Numerics' convention,
// v' = v * M) is the bit-for-bit transpose of column-major-memory + column-vector convention
// (GLSL's convention, v' = M * v) for the same visual transform -- so raw-copying a
// System.Numerics.Matrix4x4's bytes into a std140 mat4 field with no transpose, then doing
// `gl_Position = M * v` in the shader (which is exactly what prim.vert does), is already
// correct. This is the same "just blit it" identity the existing GL path already relies on
// implicitly; Vulkan's UBO upload (a raw memcpy, see VkBufferHelper) has no transpose
// parameter at all, so this identity isn't optional here -- it's the only option, and it
// happens to already be the right one.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Radegast.Veles.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 784)]
internal struct VkPerFrameUbo
{
    [FieldOffset(0)]   public Matrix4x4 View;
    [FieldOffset(64)]  public Matrix4x4 Proj;
    [FieldOffset(128)] public Matrix4x4 ViewInv;

    [FieldOffset(192)] public Vector3 SunDir;
    [FieldOffset(208)] public Vector3 SunColor;
    [FieldOffset(224)] public Vector3 AmbientColor;
    [FieldOffset(236)] public float FogDensity;

    [FieldOffset(240)] public Vector3 BlueHorizon;
    [FieldOffset(256)] public Vector3 BlueDensity;
    [FieldOffset(268)] public float HazeHorizon;
    [FieldOffset(272)] public float HazeDensity;
    [FieldOffset(288)] public Vector3 SunlightColor;
    [FieldOffset(304)] public Vector3 Ambient;
    [FieldOffset(320)] public Vector3 SunDirection;
    [FieldOffset(332)] public float SunGlowFocus;
    [FieldOffset(336)] public float SunGlowSize;

    [FieldOffset(340)] public int PointLightCount;

    // uPointLightPos[4]: vec3[4], 16-byte element stride (base offset 352, elements at
    // +0/+16/+32/+48).
    [FieldOffset(352)] public Vector3 PointLightPos0;
    [FieldOffset(368)] public Vector3 PointLightPos1;
    [FieldOffset(384)] public Vector3 PointLightPos2;
    [FieldOffset(400)] public Vector3 PointLightPos3;

    // uPointLightColor[4]: vec3[4], base offset 416.
    [FieldOffset(416)] public Vector3 PointLightColor0;
    [FieldOffset(432)] public Vector3 PointLightColor1;
    [FieldOffset(448)] public Vector3 PointLightColor2;
    [FieldOffset(464)] public Vector3 PointLightColor3;

    // uPointLightRadius[4]: float[4], 16-byte element stride (NOT 4) -- base offset 480.
    [FieldOffset(480)] public float PointLightRadius0;
    [FieldOffset(496)] public float PointLightRadius1;
    [FieldOffset(512)] public float PointLightRadius2;
    [FieldOffset(528)] public float PointLightRadius3;

    // uPointLightFalloff[4]: float[4], base offset 544.
    [FieldOffset(544)] public float PointLightFalloff0;
    [FieldOffset(560)] public float PointLightFalloff1;
    [FieldOffset(576)] public float PointLightFalloff2;
    [FieldOffset(592)] public float PointLightFalloff3;

    [FieldOffset(608)] public int ShadowsOn;
    [FieldOffset(624)] public Matrix4x4 LightVp;
    [FieldOffset(688)] public int PointShadowCount;

    // uPointShadowPos[2]: vec3[2], base offset 704.
    [FieldOffset(704)] public Vector3 PointShadowPos0;
    [FieldOffset(720)] public Vector3 PointShadowPos1;

    // uPointShadowFar[2]: float[2], 16-byte element stride -- base offset 736.
    [FieldOffset(736)] public float PointShadowFar0;
    [FieldOffset(752)] public float PointShadowFar1;

    [FieldOffset(768)] public int HasSsao;
    [FieldOffset(776)] public Vector2 ScreenSize;
}
