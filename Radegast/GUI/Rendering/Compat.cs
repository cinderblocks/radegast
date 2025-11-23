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

using System;
using OpenTK.Graphics.OpenGL;

namespace Radegast.Rendering
{
    /// <summary>
    /// Compatibility functions for some OpenGL variants
    /// </summary>
    public class Compat
    {
        #region VBO functions
        public static void GenBuffers(out int buffer)
        {
            if (RenderSettings.CoreVBOPresent)
            {
                GL.GenBuffers(1, out buffer);
            }
            else
            {
                GL.Arb.GenBuffers(1, out buffer);
            }
        }

        public static void BindBuffer(BufferTarget target, int id)
        {
            if (RenderSettings.CoreVBOPresent)
            {
                GL.BindBuffer(target, id);
            }
            else
            {
                GL.Arb.BindBuffer((BufferTargetArb)(int)target, id);
            }
        }

        public static void BufferData<T2>(BufferTarget target, IntPtr size, T2[] data, BufferUsageHint usage) where T2 : struct
        {
            if (RenderSettings.CoreVBOPresent)
            {
                GL.BufferData<T2>(target, size, data, usage);
            }
            else
            {
                GL.Arb.BufferData<T2>((BufferTargetArb)(int)target, size, data, (BufferUsageArb)(int)usage);
            }
        }

        public static int BufferSize(BufferTarget target)
        {
            int ret = 0;
            if (RenderSettings.CoreVBOPresent)
            {
                GL.GetBufferParameter(target, BufferParameterName.BufferSize, out ret);
            }
            else
            {
                GL.Arb.GetBufferParameter((BufferTargetArb)(int)target, BufferParameterNameArb.BufferSize, out ret);
            }
            return ret;
        }

        public static void DeleteBuffer(int id)
        {
            if (RenderSettings.CoreVBOPresent)
            {
                GL.DeleteBuffers(1, ref id);
            }
            else
            {
                GL.Arb.DeleteBuffers(1, ref id);
            }
        }

        #endregion VBO functions

        #region Occlusion query functions
        public static void GenQueries(out int id)
        {
            if (RenderSettings.CoreQuerySupported)
            {
                GL.GenQueries(1, out id);
            }
            else
            {
                GL.Arb.GenQueries(1, out id);
            }
        }

        public static void BeginQuery(QueryTarget target, int id)
        {
            if (RenderSettings.CoreQuerySupported)
            {
                GL.BeginQuery(target, id);
            }
            else
            {
                GL.Arb.BeginQuery((ArbOcclusionQuery)(int)target, id);
            }
        }

        public static void EndQuery(QueryTarget target)
        {
            if (RenderSettings.CoreQuerySupported)
            {
                GL.EndQuery(target);
            }
            else
            {
                GL.Arb.EndQuery(target);
            }
        }

        public static void GetQueryObject(int id, GetQueryObjectParam param, out int res)
        {
            if (RenderSettings.CoreQuerySupported)
            {
                GL.GetQueryObject(id, param, out res);
            }
            else
            {
                GL.Arb.GetQueryObject(id, (QueryObjectParameterName)param, out res);
            }
        }
        #endregion Occlusion query functions

        #region VAO functions
        public static void GenVertexArrays(out int array)
        {
            try
            {
                GL.GenVertexArrays(1, out array);
            }
            catch
            {
                // failed to create VAO - signal failure with -1
                array = -1;
            }
        }

        public static void BindVertexArray(int id)
        {
            try
            {
                GL.BindVertexArray(id);
            }
            catch
            {
                // no-op if VAO not supported
            }
        }

        public static void DeleteVertexArray(int id)
        {
            try
            {
                GL.DeleteVertexArray(id);
            }
            catch
            {
                // ignore if not supported
            }
        }

        #endregion VAO functions
    }
}
