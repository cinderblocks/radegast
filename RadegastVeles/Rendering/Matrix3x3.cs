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

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Radegast.Veles.Rendering;

/// <summary>
/// A 3×3 row-major float matrix used for normal-matrix uploads to the GPU.
/// Replaces OpenTK.Mathematics.Matrix3 which is not available in System.Numerics.
/// Memory layout: 9 floats, row-major (Row0.X, Row0.Y, Row0.Z, Row1.X, ...).
/// </summary>
public struct Matrix3x3
{
    public float M11, M12, M13;
    public float M21, M22, M23;
    public float M31, M32, M33;

    public static readonly Matrix3x3 Identity = new Matrix3x3(
        1f, 0f, 0f,
        0f, 1f, 0f,
        0f, 0f, 1f);

    public Matrix3x3(
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
    {
        M11 = m11; M12 = m12; M13 = m13;
        M21 = m21; M22 = m22; M23 = m23;
        M31 = m31; M32 = m32; M33 = m33;
    }

    /// <summary>Construct from three row vectors (Vector3 rows).</summary>
    public Matrix3x3(Vector3 row0, Vector3 row1, Vector3 row2)
    {
        M11 = row0.X; M12 = row0.Y; M13 = row0.Z;
        M21 = row1.X; M22 = row1.Y; M23 = row1.Z;
        M31 = row2.X; M32 = row2.Y; M33 = row2.Z;
    }

    /// <summary>Matrix multiplication: result[i,j] = sum_k this[i,k]*rhs[k,j].</summary>
    public static Matrix3x3 operator *(Matrix3x3 a, Matrix3x3 b) => new Matrix3x3(
        a.M11*b.M11 + a.M12*b.M21 + a.M13*b.M31,
        a.M11*b.M12 + a.M12*b.M22 + a.M13*b.M32,
        a.M11*b.M13 + a.M12*b.M23 + a.M13*b.M33,

        a.M21*b.M11 + a.M22*b.M21 + a.M23*b.M31,
        a.M21*b.M12 + a.M22*b.M22 + a.M23*b.M32,
        a.M21*b.M13 + a.M22*b.M23 + a.M23*b.M33,

        a.M31*b.M11 + a.M32*b.M21 + a.M33*b.M31,
        a.M31*b.M12 + a.M32*b.M22 + a.M33*b.M32,
        a.M31*b.M13 + a.M32*b.M23 + a.M33*b.M33);
}
