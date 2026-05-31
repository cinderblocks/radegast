/*
 * Radegast Metaverse Client
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
 * Copyright (c) 2009-2014, Radegast Development Team
 * Copyright (c) 2016-2026, Sjofn, LLC
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
using OpenMetaverse;

namespace Radegast.Veles.Rendering;

internal static class Perlin
{
    private const int SEED        = 42;
    private const int SAMPLE_SIZE = 1024;
    private const int B           = SAMPLE_SIZE;
    private const int BM          = SAMPLE_SIZE - 1;
    private const int N           = 0x1000;

    private static readonly int[]   p  = new int  [SAMPLE_SIZE + SAMPLE_SIZE + 2];
    private static readonly float[,] g3 = new float[SAMPLE_SIZE + SAMPLE_SIZE + 2, 3];
    private static readonly float[,] g2 = new float[SAMPLE_SIZE + SAMPLE_SIZE + 2, 2];
    private static readonly float[]  g1 = new float[SAMPLE_SIZE + SAMPLE_SIZE + 2];

    static Perlin()
    {
        var rng = new Random(SEED);
        int i, j, k;

        for (i = 0; i < B; i++)
        {
            p[i]  = i;
            g1[i] = (float)((rng.Next() % (B + B)) - B) / B;
            for (j = 0; j < 2; j++)
                g2[i, j] = (float)((rng.Next() % (B + B)) - B) / B;
            Normalize2(g2, i);
            for (j = 0; j < 3; j++)
                g3[i, j] = (float)((rng.Next() % (B + B)) - B) / B;
            Normalize3(g3, i);
        }

        while (--i > 0)
        {
            k    = p[i];
            p[i] = p[j = rng.Next() % B];
            p[j] = k;
        }

        for (i = 0; i < B + 2; i++)
        {
            p[B + i]  = p[i];
            g1[B + i] = g1[i];
            for (j = 0; j < 2; j++) g2[B + i, j] = g2[i, j];
            for (j = 0; j < 3; j++) g3[B + i, j] = g3[i, j];
        }
    }

    public static float noise2(float x, float y)
    {
        int bx0, bx1, by0, by1, b00, b10, b01, b11;
        float rx0, rx1, ry0, ry1, sx, sy, a, b, t, u, v;
        int i, j;

        t   = x + N;
        bx0 = ((int)t) & BM; bx1 = (bx0 + 1) & BM; rx0 = t - (int)t; rx1 = rx0 - 1f;
        t   = y + N;
        by0 = ((int)t) & BM; by1 = (by0 + 1) & BM; ry0 = t - (int)t; ry1 = ry0 - 1f;

        i   = p[bx0]; j = p[bx1];
        b00 = p[i + by0]; b10 = p[j + by0]; b01 = p[i + by1]; b11 = p[j + by1];
        sx  = SCurve(rx0); sy = SCurve(ry0);

        u = rx0 * g2[b00, 0] + ry0 * g2[b00, 1];
        v = rx1 * g2[b10, 0] + ry0 * g2[b10, 1];
        a = Utils.Lerp(u, v, sx);

        u = rx0 * g2[b01, 0] + ry1 * g2[b01, 1];
        v = rx1 * g2[b11, 0] + ry1 * g2[b11, 1];
        b = Utils.Lerp(u, v, sx);

        return Utils.Lerp(a, b, sy);
    }

    public static float turbulence2(float x, float y, float freq)
    {
        float t = 0f;
        for (; freq >= 1f; freq *= 0.5f)
            t += noise2(freq * x, freq * y) / freq;
        return t;
    }

    private static void Normalize2(float[,] v, int i)
    {
        float s = (float)Math.Sqrt(v[i, 0] * v[i, 0] + v[i, 1] * v[i, 1]);
        s = 1.0f / s;
        v[i, 0] *= s; v[i, 1] *= s;
    }

    private static void Normalize3(float[,] v, int i)
    {
        float s = (float)Math.Sqrt(v[i, 0] * v[i, 0] + v[i, 1] * v[i, 1] + v[i, 2] * v[i, 2]);
        s = 1.0f / s;
        v[i, 0] *= s; v[i, 1] *= s; v[i, 2] *= s;
    }

    private static float SCurve(float t) => t * t * (3f - 2f * t);
}
