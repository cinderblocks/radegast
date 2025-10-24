/**
 * Radegast Metaverse Client
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
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

using System;
using OpenMetaverse;

namespace Radegast.Rendering
{
    public static class Perlin
    {
        // We use a hardcoded seed to keep the noise generation consistent between runs
        private const int SEED = 42;

        private const int SAMPLE_SIZE = 1024;
        private const int B = SAMPLE_SIZE;
        private const int BM = SAMPLE_SIZE - 1;
        private const int N = 0x1000;

        private static readonly int[] p = new int[SAMPLE_SIZE + SAMPLE_SIZE + 2];
        private static readonly float[,] g3 = new float[SAMPLE_SIZE + SAMPLE_SIZE + 2, 3];
        private static readonly float[,] g2 = new float[SAMPLE_SIZE + SAMPLE_SIZE + 2, 2];
        private static readonly float[] g1 = new float[SAMPLE_SIZE + SAMPLE_SIZE + 2];

        static Perlin()
        {
            Random rng = new Random(SEED);
            int i, j, k;

            for (i = 0; i < B; i++)
            {
                p[i] = i;
                g1[i] = (float)((rng.Next() % (B + B)) - B) / B;

                for (j = 0; j < 2; j++)
                    g2[i, j] = (float)((rng.Next() % (B + B)) - B) / B;
                normalize2(g2, i);

                for (j = 0; j < 3; j++)
                    g3[i, j] = (float)((rng.Next() % (B + B)) - B) / B;
                normalize3(g3, i);
            }

            while (--i > 0)
            {
                k = p[i];
                p[i] = p[j = rng.Next() % B];
                p[j] = k;
            }

            for (i = 0; i < B + 2; i++)
            {
                p[B + i] = p[i];
                g1[B + i] = g1[i];
                for (j = 0; j < 2; j++)
                    g2[B + i, j] = g2[i, j];
                for (j = 0; j < 3; j++)
                    g3[B + i, j] = g3[i, j];
            }
        }

        public static float noise1(float arg)
        {
            int bx0, bx1;
            float rx0, rx1, sx, t, u, v;

            t = arg + N;
            bx0 = ((int)t) & BM;
            bx1 = (bx0 + 1) & BM;
            rx0 = t - (int)t;
            rx1 = rx0 - 1f;

            sx = s_curve(rx0);

            u = rx0 * g1[p[bx0]];
            v = rx1 * g1[p[bx1]];

            return Utils.Lerp(u, v, sx);
        }

        public static float noise2(float x, float y)
        {
            int bx0, bx1, by0, by1, b00, b10, b01, b11;
            float rx0, rx1, ry0, ry1, sx, sy, a, b, t, u, v;
            int i, j;

            t = x + N;
            bx0 = ((int)t) & BM;
            bx1 = (bx0 + 1) & BM;
            rx0 = t - (int)t;
            rx1 = rx0 - 1f;

            t = y + N;
            by0 = ((int)t) & BM;
            by1 = (by0 + 1) & BM;
            ry0 = t - (int)t;
            ry1 = ry0 - 1f;

            i = p[bx0];
            j = p[bx1];

            b00 = p[i + by0];
            b10 = p[j + by0];
            b01 = p[i + by1];
            b11 = p[j + by1];

            sx = s_curve(rx0);
            sy = s_curve(ry0);

            u = rx0 * g2[b00, 0] + ry0 * g2[b00, 1];
            v = rx1 * g2[b10, 0] + ry0 * g2[b10, 1];
            a = Utils.Lerp(u, v, sx);

            u = rx0 * g2[b01, 0] + ry1 * g2[b01, 1];
            v = rx1 * g2[b11, 0] + ry1 * g2[b11, 1];
            b = Utils.Lerp(u, v, sx);

            return Utils.Lerp(a, b, sy);
        }

        public static float noise3(float x, float y, float z)
        {
            int bx0, bx1, by0, by1, bz0, bz1, b00, b10, b01, b11;
            float rx0, rx1, ry0, ry1, rz0, rz1, sy, sz, a, b, c, d, t, u, v;
            int i, j;

            t = x + N;
            bx0 = ((int)t) & BM;
            bx1 = (bx0 + 1) & BM;
            rx0 = t - (int)t;
            rx1 = rx0 - 1f;

            t = y + N;
            by0 = ((int)t) & BM;
            by1 = (by0 + 1) & BM;
            ry0 = t - (int)t;
            ry1 = ry0 - 1f;

            t = z + N;
            bz0 = ((int)t) & BM;
            bz1 = (bz0 + 1) & BM;
            rz0 = t - (int)t;
            rz1 = rz0 - 1f;

            i = p[bx0];
            j = p[bx1];

            b00 = p[i + by0];
            b10 = p[j + by0];
            b01 = p[i + by1];
            b11 = p[j + by1];

            t = s_curve(rx0);
            sy = s_curve(ry0);
            sz = s_curve(rz0);

            u = rx0 * g3[b00 + bz0, 0] + ry0 * g3[b00 + bz0, 1] + rz0 * g3[b00 + bz0, 2];
            v = rx1 * g3[b10 + bz0, 0] + ry0 * g3[b10 + bz0, 1] + rz0 * g3[b10 + bz0, 2];
            a = Utils.Lerp(u, v, t);

            u = rx0 * g3[b01 + bz0, 0] + ry1 * g3[b01 + bz0, 1] + rz0 * g3[b01 + bz0, 2];
            v = rx1 * g3[b11 + bz0, 0] + ry1 * g3[b11 + bz0, 1] + rz0 * g3[b11 + bz0, 2];
            b = Utils.Lerp(u, v, t);

            c = Utils.Lerp(a, b, sy);

            u = rx0 * g3[b00 + bz1, 0] + ry0 * g3[b00 + bz1, 1] + rz1 * g3[b00 + bz1, 2];
            v = rx1 * g3[b10 + bz1, 0] + ry0 * g3[b10 + bz1, 1] + rz1 * g3[b10 + bz1, 2];
            a = Utils.Lerp(u, v, t);

            u = rx0 * g3[b01 + bz1, 0] + ry1 * g3[b01 + bz1, 1] + rz1 * g3[b01 + bz1, 2];
            v = rx1 * g3[b11 + bz1, 0] + ry1 * g3[b11 + bz1, 1] + rz1 * g3[b11 + bz1, 2];
            b = Utils.Lerp(u, v, t);

            d = Utils.Lerp(a, b, sy);
            return Utils.Lerp(c, d, sz);
        }

        public static float turbulence1(float x, float freq)
        {
            float t;
            float v;

            for (t = 0f; freq >= 1f; freq *= 0.5f)
            {
                v = freq * x;
                t += noise1(v) / freq;
            }
            return t;
        }

        public static float turbulence2(float x, float y, float freq)
        {
            float t;
            Vector2 vec;

            for (t = 0f; freq >= 1f; freq *= 0.5f)
            {
                vec.X = freq * x;
                vec.Y = freq * y;
                t += noise2(vec.X, vec.Y) / freq;
            }
            return t;
        }

        public static float turbulence3(float x, float y, float z, float freq)
        {
            float t;
            Vector3 vec;

            for (t = 0f; freq >= 1f; freq *= 0.5f)
            {
                vec.X = freq * x;
                vec.Y = freq * y;
                vec.Z = freq * z;
                t += noise3(vec.X, vec.Y, vec.Z) / freq;
            }
            return t;
        }

        private static void normalize2(float[,] v, int i)
        {
            float s;

            s = (float)Math.Sqrt(v[i, 0] * v[i, 0] + v[i, 1] * v[i, 1]);
            s = 1.0f / s;
            v[i, 0] = v[i, 0] * s;
            v[i, 1] = v[i, 1] * s;
        }

        private static void normalize3(float[,] v, int i)
        {
            float s;

            s = (float)Math.Sqrt(v[i, 0] * v[i, 0] + v[i, 1] * v[i, 1] + v[i, 2] * v[i, 2]);
            s = 1.0f / s;

            v[i, 0] = v[i, 0] * s;
            v[i, 1] = v[i, 1] * s;
            v[i, 2] = v[i, 2] * s;
        }

        private static float s_curve(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
