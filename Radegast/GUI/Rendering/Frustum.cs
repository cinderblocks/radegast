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

/* 
 * tutoriaali:
 * http://www.crownandcutlass.com/features/technicaldetails/frustum.html
 *
 */

using System;
using OpenMetaverse;
//using OpenTK.Graphics.OpenGL;
//using OpenTK;

namespace Radegast.Rendering
{
    public class Frustum
    {
        //static Matrix4 ProjMatrix, ModelMatrix;
        private static readonly float[] ClipMatrix = new float[16];
        private static readonly float[,] frustum = new float[6, 4];
        private const int RIGHT = 0, LEFT = 1, BOTTOM = 2, TOP = 3, BACK = 4, FRONT = 5;

        private static void NormalizePlane(float[,] frustum, int side)
        {
            float magnitude = (float)Math.Sqrt((frustum[side, 0] * frustum[side, 0]) + (frustum[side, 1] * frustum[side, 1])
                                                + (frustum[side, 2] * frustum[side, 2]));

            frustum[side, 0] /= magnitude;
            frustum[side, 1] /= magnitude;
            frustum[side, 2] /= magnitude;
            frustum[side, 3] /= magnitude;
        }

        public static void CalculateFrustum(OpenTK.Matrix4 ProjMatrix, OpenTK.Matrix4 ModelMatrix)
        {
            ClipMatrix[0] = (ModelMatrix.M11 * ProjMatrix.M11) + (ModelMatrix.M12 * ProjMatrix.M21) + (ModelMatrix.M13 * ProjMatrix.M31) + (ModelMatrix.M14 * ProjMatrix.M41);
            ClipMatrix[1] = (ModelMatrix.M11 * ProjMatrix.M12) + (ModelMatrix.M12 * ProjMatrix.M22) + (ModelMatrix.M13 * ProjMatrix.M32) + (ModelMatrix.M14 * ProjMatrix.M42);
            ClipMatrix[2] = (ModelMatrix.M11 * ProjMatrix.M13) + (ModelMatrix.M12 * ProjMatrix.M23) + (ModelMatrix.M13 * ProjMatrix.M33) + (ModelMatrix.M14 * ProjMatrix.M43);
            ClipMatrix[3] = (ModelMatrix.M11 * ProjMatrix.M14) + (ModelMatrix.M12 * ProjMatrix.M24) + (ModelMatrix.M13 * ProjMatrix.M34) + (ModelMatrix.M14 * ProjMatrix.M44);

            ClipMatrix[4] = (ModelMatrix.M21 * ProjMatrix.M11) + (ModelMatrix.M22 * ProjMatrix.M21) + (ModelMatrix.M23 * ProjMatrix.M31) + (ModelMatrix.M24 * ProjMatrix.M41);
            ClipMatrix[5] = (ModelMatrix.M21 * ProjMatrix.M12) + (ModelMatrix.M22 * ProjMatrix.M22) + (ModelMatrix.M23 * ProjMatrix.M32) + (ModelMatrix.M24 * ProjMatrix.M42);
            ClipMatrix[6] = (ModelMatrix.M21 * ProjMatrix.M13) + (ModelMatrix.M22 * ProjMatrix.M23) + (ModelMatrix.M23 * ProjMatrix.M33) + (ModelMatrix.M24 * ProjMatrix.M43);
            ClipMatrix[7] = (ModelMatrix.M21 * ProjMatrix.M14) + (ModelMatrix.M22 * ProjMatrix.M24) + (ModelMatrix.M23 * ProjMatrix.M34) + (ModelMatrix.M24 * ProjMatrix.M44);

            ClipMatrix[8] = (ModelMatrix.M31 * ProjMatrix.M11) + (ModelMatrix.M32 * ProjMatrix.M21) + (ModelMatrix.M33 * ProjMatrix.M31) + (ModelMatrix.M34 * ProjMatrix.M41);
            ClipMatrix[9] = (ModelMatrix.M31 * ProjMatrix.M12) + (ModelMatrix.M32 * ProjMatrix.M22) + (ModelMatrix.M33 * ProjMatrix.M32) + (ModelMatrix.M34 * ProjMatrix.M42);
            ClipMatrix[10] = (ModelMatrix.M31 * ProjMatrix.M13) + (ModelMatrix.M32 * ProjMatrix.M23) + (ModelMatrix.M33 * ProjMatrix.M33) + (ModelMatrix.M34 * ProjMatrix.M43);
            ClipMatrix[11] = (ModelMatrix.M31 * ProjMatrix.M14) + (ModelMatrix.M32 * ProjMatrix.M24) + (ModelMatrix.M33 * ProjMatrix.M34) + (ModelMatrix.M34 * ProjMatrix.M44);

            ClipMatrix[12] = (ModelMatrix.M41 * ProjMatrix.M11) + (ModelMatrix.M42 * ProjMatrix.M21) + (ModelMatrix.M43 * ProjMatrix.M31) + (ModelMatrix.M44 * ProjMatrix.M41);
            ClipMatrix[13] = (ModelMatrix.M41 * ProjMatrix.M12) + (ModelMatrix.M42 * ProjMatrix.M22) + (ModelMatrix.M43 * ProjMatrix.M32) + (ModelMatrix.M44 * ProjMatrix.M42);
            ClipMatrix[14] = (ModelMatrix.M41 * ProjMatrix.M13) + (ModelMatrix.M42 * ProjMatrix.M23) + (ModelMatrix.M43 * ProjMatrix.M33) + (ModelMatrix.M44 * ProjMatrix.M43);
            ClipMatrix[15] = (ModelMatrix.M41 * ProjMatrix.M14) + (ModelMatrix.M42 * ProjMatrix.M24) + (ModelMatrix.M43 * ProjMatrix.M34) + (ModelMatrix.M44 * ProjMatrix.M44);

            // laske frustumin tasot ja normalisoi ne
            frustum[RIGHT, 0] = ClipMatrix[3] - ClipMatrix[0];
            frustum[RIGHT, 1] = ClipMatrix[7] - ClipMatrix[4];
            frustum[RIGHT, 2] = ClipMatrix[11] - ClipMatrix[8];
            frustum[RIGHT, 3] = ClipMatrix[15] - ClipMatrix[12];
            NormalizePlane(frustum, RIGHT);

            frustum[LEFT, 0] = ClipMatrix[3] + ClipMatrix[0];
            frustum[LEFT, 1] = ClipMatrix[7] + ClipMatrix[4];
            frustum[LEFT, 2] = ClipMatrix[11] + ClipMatrix[8];
            frustum[LEFT, 3] = ClipMatrix[15] + ClipMatrix[12];
            NormalizePlane(frustum, LEFT);

            frustum[BOTTOM, 0] = ClipMatrix[3] + ClipMatrix[1];
            frustum[BOTTOM, 1] = ClipMatrix[7] + ClipMatrix[5];
            frustum[BOTTOM, 2] = ClipMatrix[11] + ClipMatrix[9];
            frustum[BOTTOM, 3] = ClipMatrix[15] + ClipMatrix[13];
            NormalizePlane(frustum, BOTTOM);

            frustum[TOP, 0] = ClipMatrix[3] - ClipMatrix[1];
            frustum[TOP, 1] = ClipMatrix[7] - ClipMatrix[5];
            frustum[TOP, 2] = ClipMatrix[11] - ClipMatrix[9];
            frustum[TOP, 3] = ClipMatrix[15] - ClipMatrix[13];
            NormalizePlane(frustum, TOP);

            frustum[BACK, 0] = ClipMatrix[3] - ClipMatrix[2];
            frustum[BACK, 1] = ClipMatrix[7] - ClipMatrix[6];
            frustum[BACK, 2] = ClipMatrix[11] - ClipMatrix[10];
            frustum[BACK, 3] = ClipMatrix[15] - ClipMatrix[14];
            NormalizePlane(frustum, BACK);

            frustum[FRONT, 0] = ClipMatrix[3] + ClipMatrix[2];
            frustum[FRONT, 1] = ClipMatrix[7] + ClipMatrix[6];
            frustum[FRONT, 2] = ClipMatrix[11] + ClipMatrix[10];
            frustum[FRONT, 3] = ClipMatrix[15] + ClipMatrix[14];
            NormalizePlane(frustum, FRONT);
        }

        /// <summary>
        /// tasojen normaalit osoittaa sisäänpäin joten jos testattava vertex on
        /// kaikkien tasojen "edessä", se on ruudulla ja rendataan
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public static bool PointInFrustum(float x, float y, float z)
        {
            // tasoyhtälö: A*x + B*y + C*z + D = 0
            // ABC on normaalin X, Y ja Z
            // D on tason etäisyys origosta
            // =0 vertex on tasolla
            // <0 tason takana
            // >0 tason edessä
            for (int a = 0; a < 6; a++)
            {
                // jos vertex jonkun tason takana, niin palauta false (ei rendata)
                if (((frustum[a, 0] * x) + (frustum[a, 1] * y) + (frustum[a, 2] * z) + frustum[a, 3]) <= 0)
                {
                    return false;
                }
            }

            // ruudulla
            return true;
        }

        /// <summary>
        /// palauttaa etäisyyden kameraan jos pallo frustumissa, muuten 0.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <param name="radius"></param>
        /// <returns></returns>
        public static float SphereInFrustum(float x, float y, float z, float radius)
        {
            float d = 0;
            for (int p = 0; p < 6; p++)
            {
                d = frustum[p, 0] * x + frustum[p, 1] * y + frustum[p, 2] * z + frustum[p, 3];
                if (d <= -radius) // jos pallo ei ole ruudulla
                {
                    return 0;
                }
            }
            // kaikkien tasojen edessä eli näkyvissä.
            return d + radius; // palauta matka kameraan
        }

        public static bool ObjectInFrustum(Vector3 position, BoundingVolume bound)
        {
            return ObjectInFrustum(position.X, position.Y, position.Z, bound);
        }

        public static bool ObjectInFrustum(float x, float y, float z, BoundingVolume bound)
        {
            if (bound == null) return true;
            if (SphereInFrustum(x, y, z, bound.ScaledR) == 0) return false;
            return true;
        }
    }

    public class BoundingVolume
    {
        private Vector3 Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private Vector3 Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        public Vector3 ScaledMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        public Vector3 ScaledMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        public float ScaledR = 0f;

        public void CalcScaled(Vector3 scale)
        {
            ScaledMin = Min * scale;
            ScaledMax = Max * scale;
            Vector3 dist = ScaledMax - ScaledMin;
            ScaledR = dist.Length();
        }

        public void CreateBoundingVolume(OpenMetaverse.Rendering.Face mesh, Vector3 scale)
        {
            for (int q = 0; q < mesh.Vertices.Count; q++)
            {
                if (mesh.Vertices[q].Position.X < Min.X) Min.X = mesh.Vertices[q].Position.X;
                if (mesh.Vertices[q].Position.Y < Min.Y) Min.Y = mesh.Vertices[q].Position.Y;
                if (mesh.Vertices[q].Position.Z < Min.Z) Min.Z = mesh.Vertices[q].Position.Z;

                if (mesh.Vertices[q].Position.X > Max.X) Max.X = mesh.Vertices[q].Position.X;
                if (mesh.Vertices[q].Position.Y > Max.Y) Max.Y = mesh.Vertices[q].Position.Y;
                if (mesh.Vertices[q].Position.Z > Max.Z) Max.Z = mesh.Vertices[q].Position.Z;
            }

            Vector3 dist = Max - Min;
            mesh.Center = Min + (dist / 2);
            CalcScaled(scale);
        }

        public void FromScale(Vector3 scale)
        {
            ScaledMax = scale / 2f;
            ScaledMin = -ScaledMax;
            Vector3 dist = ScaledMax - ScaledMin;
            ScaledR = dist.Length();
        }

        public void AddVolume(BoundingVolume vol, Vector3 scale)
        {
            if (vol.Min.X < Min.X) Min.X = vol.Min.X;
            if (vol.Min.Y < Min.Y) Min.Y = vol.Min.Y;
            if (vol.Min.Z < Min.Z) Min.Z = vol.Min.Z;

            if (vol.Max.X > Max.X) Max.X = vol.Max.X;
            if (vol.Max.Y > Max.Y) Max.Y = vol.Max.Y;
            if (vol.Max.Z > Max.Z) Max.Z = vol.Max.Z;
            Vector3 dist = Max - Min;
            CalcScaled(scale);
        }

        //public void CreateBoundingVolume(Model mesh, Vector3 min, Vector3 max)
        //{
        //    Min = min;
        //    Max = max;
        //    Vector3 dist = Max - Min;
        //    R = dist.Length;
        //    mesh.ObjCenter = Min + (dist / 2); // objektin keskikohta
        //}
    }
}