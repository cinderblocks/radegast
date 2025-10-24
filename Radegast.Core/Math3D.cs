/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using OpenMetaverse;

namespace Radegast.Rendering
{
    public static class Math3D
    {
        // Column-major:
        // |  0  4  8 12 |
        // |  1  5  9 13 |
        // |  2  6 10 14 |
        // |  3  7 11 15 |

        public static float[] CreateTranslationMatrix(Vector3 v)
        {
            float[] mat = new float[16];

            mat[12] = v.X;
            mat[13] = v.Y;
            mat[14] = v.Z;
            mat[0] = mat[5] = mat[10] = mat[15] = 1;

            return mat;
        }

        public static float[] CreateRotationMatrix(Quaternion q)
        {
            float[] mat = new float[16];

            // Transpose the quaternion (don't ask me why)
            q.X *= -1f;
            q.Y *= -1f;
            q.Z *= -1f;

            float x2 = q.X + q.X;
            float y2 = q.Y + q.Y;
            float z2 = q.Z + q.Z;
            float xx = q.X * x2;
            float xy = q.X * y2;
            float xz = q.X * z2;
            float yy = q.Y * y2;
            float yz = q.Y * z2;
            float zz = q.Z * z2;
            float wx = q.W * x2;
            float wy = q.W * y2;
            float wz = q.W * z2;

            mat[0] = 1.0f - (yy + zz);
            mat[1] = xy - wz;
            mat[2] = xz + wy;
            mat[3] = 0.0f;

            mat[4] = xy + wz;
            mat[5] = 1.0f - (xx + zz);
            mat[6] = yz - wx;
            mat[7] = 0.0f;

            mat[8] = xz - wy;
            mat[9] = yz + wx;
            mat[10] = 1.0f - (xx + yy);
            mat[11] = 0.0f;

            mat[12] = 0.0f;
            mat[13] = 0.0f;
            mat[14] = 0.0f;
            mat[15] = 1.0f;

            return mat;
        }

        public static float[] CreateSRTMatrix(Vector3 scale, Quaternion q, Vector3 pos)
        {
            float[] mat = new float[16];

            // Transpose the quaternion (don't ask me why)
            q.X *= -1f;
            q.Y *= -1f;
            q.Z *= -1f;

            float x2 = q.X + q.X;
            float y2 = q.Y + q.Y;
            float z2 = q.Z + q.Z;
            float xx = q.X * x2;
            float xy = q.X * y2;
            float xz = q.X * z2;
            float yy = q.Y * y2;
            float yz = q.Y * z2;
            float zz = q.Z * z2;
            float wx = q.W * x2;
            float wy = q.W * y2;
            float wz = q.W * z2;

            mat[0] = (1.0f - (yy + zz)) * scale.X;
            mat[1] = (xy - wz) * scale.X;
            mat[2] = (xz + wy) * scale.X;
            mat[3] = 0.0f;

            mat[4] = (xy + wz) * scale.Y;
            mat[5] = (1.0f - (xx + zz)) * scale.Y;
            mat[6] = (yz - wx) * scale.Y;
            mat[7] = 0.0f;

            mat[8] = (xz - wy) * scale.Z;
            mat[9] = (yz + wx) * scale.Z;
            mat[10] = (1.0f - (xx + yy)) * scale.Z;
            mat[11] = 0.0f;

            //Positional parts
            mat[12] = pos.X;
            mat[13] = pos.Y;
            mat[14] = pos.Z;
            mat[15] = 1.0f;

            return mat;
        }


        public static float[] CreateScaleMatrix(Vector3 v)
        {
            float[] mat = new float[16];

            mat[0] = v.X;
            mat[5] = v.Y;
            mat[10] = v.Z;
            mat[15] = 1;

            return mat;
        }

        public static float[] Lerp(float[] matrix1, float[] matrix2, float amount)
        {

            float[] lerp = new float[16];
            //Probably not doing this as a loop is cheaper(unrolling)
            //also for performance we probably should not create new objects
            // but meh.
            for (int x = 0; x < 16; x++)
            {
                lerp[x] = matrix1[x] + ((matrix2[x] - matrix1[x]) * amount);
            }

            return lerp;
        }

        public static double[] AbovePlane(double height)
        {
            return new double[] { 0, 0, 1, -height };
        }

        public static double[] BelowPlane(double height)
        {
            return new double[] { 0, 0, -1, height };
        }
    }
}
