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

// CPU-side mirror of prim.frag's "Material" UBO (set=2, binding=5) -- see VkPerFrameUbo.cs's
// much longer note for the full std140 rules rationale (vec3's 16-byte alignment, vec4's
// 16-byte alignment forcing tail padding after odd runs of scalars, etc.). Field list read
// directly from shader_data/vulkan/prim.frag (28 fields) -- the live shader source is
// authoritative. Offsets hand-computed against std140 then verified empirically via
// Marshal.SizeOf/Marshal.OffsetOf, the same process used for VkPerFrameUbo.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Radegast.Veles.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 304)]
internal struct VkMaterialUbo
{
    [FieldOffset(0)]  public int HasTexture;
    [FieldOffset(4)]  public int HasBump;
    [FieldOffset(8)]  public int IsTerrain;

    [FieldOffset(12)] public int HasMaterial;
    [FieldOffset(16)] public int HasNormalMap;
    [FieldOffset(32)] public Vector4 NormalUvST;
    [FieldOffset(48)] public float NormalUvRot;

    [FieldOffset(52)] public int HasSpecularMap;
    [FieldOffset(64)] public Vector4 SpecUvST;
    [FieldOffset(80)] public float SpecUvRot;
    [FieldOffset(96)] public Vector4 SpecColor;
    [FieldOffset(112)] public float SpecExp;
    [FieldOffset(116)] public float EnvIntensity;

    [FieldOffset(120)] public int IsPBR;
    [FieldOffset(124)] public int HasMRMap;
    [FieldOffset(128)] public Vector4 MRUvST;
    [FieldOffset(144)] public float MRUvRot;

    [FieldOffset(148)] public int HasEmissiveMap;
    [FieldOffset(160)] public Vector4 EmissiveUvST;
    [FieldOffset(176)] public float EmissiveUvRot;

    [FieldOffset(192)] public Vector4 BaseColorFactor;
    [FieldOffset(208)] public float MetallicFactor;
    [FieldOffset(212)] public float RoughnessFactor;
    [FieldOffset(224)] public Vector3 EmissiveFactor;

    [FieldOffset(240)] public Vector4 PbrNormalUvST;
    [FieldOffset(256)] public float PbrNormalUvRot;
    [FieldOffset(272)] public Vector4 BaseColorUvST;
    [FieldOffset(288)] public float BaseColorUvRot;
}
