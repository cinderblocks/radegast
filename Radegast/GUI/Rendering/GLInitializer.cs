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

using OpenTK.Graphics.OpenGL;

namespace Radegast.Rendering
{
    /// <summary>
    /// Handles common OpenGL initialization tasks
    /// </summary>
    public static class GLInitializer
    {
        /// <summary>
        /// Initialize basic OpenGL rendering state
        /// </summary>
        public static void InitializeBasicGL()
        {
            GL.ShadeModel(ShadingModel.Smooth);
            GL.ClearColor(0f, 0f, 0f, 0f);

            GL.ClearDepth(1.0d);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ColorMaterial);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.ColorMaterial(MaterialFace.Front, ColorMaterialParameter.AmbientAndDiffuse);
            GL.ColorMaterial(MaterialFace.Front, ColorMaterialParameter.Specular);

            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            GL.MatrixMode(MatrixMode.Projection);
        }

        /// <summary>
        /// Setup lighting with Light0
        /// </summary>
        /// <param name="ambient">Ambient light component</param>
        /// <param name="diffuse">Diffuse light component</param>
        /// <param name="specular">Specular light component</param>
        /// <param name="alpha">Alpha component</param>
        /// <param name="position">Light position (x, y, z, w)</param>
        public static void SetupLighting(float ambient, float diffuse, float specular, float alpha, float[] position)
        {
            GL.Enable(EnableCap.Lighting);
            GL.Enable(EnableCap.Light0);
            
            GL.Light(LightName.Light0, LightParameter.Ambient, new float[] { ambient, ambient, ambient, alpha });
            GL.Light(LightName.Light0, LightParameter.Diffuse, new float[] { diffuse, diffuse, diffuse, alpha });
            GL.Light(LightName.Light0, LightParameter.Specular, new float[] { specular, specular, specular, alpha });
            
            if (position != null && position.Length >= 4)
            {
                GL.Light(LightName.Light0, LightParameter.Position, position);
            }
        }

        /// <summary>
        /// Setup alpha blending and testing
        /// </summary>
        public static void SetupBlending()
        {
            GL.Enable(EnableCap.Blend);
            GL.AlphaFunc(AlphaFunction.Greater, RenderingConstants.ALPHA_TEST_THRESHOLD);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        /// <summary>
        /// Complete OpenGL initialization with lighting
        /// </summary>
        /// <param name="ambient">Ambient light component</param>
        /// <param name="diffuse">Diffuse light component</param>
        /// <param name="specular">Specular light component</param>
        /// <param name="alpha">Alpha component</param>
        /// <param name="lightPosition">Light position (x, y, z, w)</param>
        public static void InitializeGL(float ambient, float diffuse, float specular, float alpha, float[] lightPosition)
        {
            InitializeBasicGL();
            SetupLighting(ambient, diffuse, specular, alpha, lightPosition);
            SetupBlending();
        }
    }
}
