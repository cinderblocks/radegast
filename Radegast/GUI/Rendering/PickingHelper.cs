// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// Copyright (c) 2019-2025, Sjofn LLC
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Radegast", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//

using System;
using OpenTK.Graphics.OpenGL;

namespace Radegast.Rendering
{
    /// <summary>
    /// Helper for object picking via color-coded rendering
    /// </summary>
    public static class PickingHelper
    {
        /// <summary>
        /// Setup OpenGL state for picking render pass
        /// </summary>
        public static void BeginPicking()
        {
            // Save old attributes
            GL.PushAttrib(AttribMask.AllAttribBits);

            // Disable features to make objects flat/solid color
            GL.Disable(EnableCap.Fog);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Dither);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.LineStipple);
            GL.Disable(EnableCap.PolygonStipple);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.AlphaTest);
        }

        /// <summary>
        /// Restore OpenGL state after picking render pass
        /// </summary>
        public static void EndPicking()
        {
            GL.PopAttrib();
        }

        /// <summary>
        /// Read the color at the specified pixel coordinate
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate (will be flipped)</param>
        /// <param name="viewportHeight">Height of viewport for Y-axis flip</param>
        /// <returns>4-byte RGBA color at the pixel</returns>
        public static byte[] ReadPixelColor(int x, int y, int viewportHeight)
        {
            byte[] color = new byte[4];
            GL.ReadPixels(x, viewportHeight - y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, color);
            return color;
        }

        /// <summary>
        /// Read depth value at the specified pixel coordinate
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate (will be flipped)</param>
        /// <param name="viewportHeight">Height of viewport for Y-axis flip</param>
        /// <returns>Depth value at the pixel</returns>
        public static float ReadPixelDepth(int x, int y, int viewportHeight)
        {
            float depth = 0f;
            GL.ReadPixels(x, viewportHeight - y, 1, 1, PixelFormat.DepthComponent, PixelType.Float, ref depth);
            return depth;
        }

        /// <summary>
        /// Execute a complete picking operation
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="viewportHeight">Height of viewport</param>
        /// <param name="renderAction">Action that renders the scene in picking mode</param>
        /// <returns>4-byte RGBA color at the pixel</returns>
        public static byte[] ExecutePicking(int x, int y, int viewportHeight, Action renderAction)
        {
            BeginPicking();
            
            try
            {
                renderAction?.Invoke();
                return ReadPixelColor(x, y, viewportHeight);
            }
            finally
            {
                EndPicking();
            }
        }
    }
}
