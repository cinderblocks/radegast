// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
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
//       this software without specific prior CreateReflectionTexture permission.
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
// $Id$
//

#region Usings

using System.Drawing;
using OpenTK.Graphics.OpenGL;

#endregion Usings

namespace Radegast.Rendering
{

    public partial class SceneWindow
    {
        private double[] AboveWaterPlane;
        private double[] BelowWaterPlane;

        private readonly ShaderProgram waterProgram = new ShaderProgram();

        private int reflectionTexture;
        private int refractionTexture;
        private int dudvmap;
        private int normalmap;
        private int depthTexture;

        private void SetWaterPlanes()
        {
            AboveWaterPlane = Math3D.AbovePlane(Client.Network.CurrentSim.WaterHeight);
            BelowWaterPlane = Math3D.BelowPlane(Client.Network.CurrentSim.WaterHeight);
        }

        private void InitWater()
        {
            Bitmap normal = (Bitmap)Image.FromFile(System.IO.Path.Combine("shader_data", "normalmap.png"));
            reflectionTexture = RHelp.GLLoadImage(normal, false);
            refractionTexture = RHelp.GLLoadImage(normal, false);
            normalmap = RHelp.GLLoadImage(normal, false);
            depthTexture = RHelp.GLLoadImage(normal, false);
            dudvmap = RHelp.GLLoadImage((Bitmap)Image.FromFile(System.IO.Path.Combine("shader_data", "dudvmap.png")), false);
            waterProgram.Load("water.vert", "water.frag");
        }

        private readonly float waterUV = 35f;
        private readonly float waterFlow = 0.0025f;
        private float normalMove = 0f;
        private float move = 0f;
        private const float kNormalMapScale = 0.25f;
        private float normalUV;

        private void DrawWaterQuad(float x, float y, float z)
        {
            normalUV = waterUV * kNormalMapScale;
            normalMove += waterFlow * kNormalMapScale * lastFrameTime;
            move += waterFlow * lastFrameTime;

            if (RenderSettings.HasMultiTexturing)
            {
                GL.MultiTexCoord2(TextureUnit.Texture0, 0f, waterUV);
                GL.MultiTexCoord2(TextureUnit.Texture1, 0f, waterUV - move);
                GL.MultiTexCoord2(TextureUnit.Texture2, 0f, normalUV + normalMove);
                GL.MultiTexCoord2(TextureUnit.Texture3, 0f, 0f);
                GL.MultiTexCoord2(TextureUnit.Texture4, 0f, 0f);
            }
            GL.Vertex3(x, y, z);

            if (RenderSettings.HasMultiTexturing)
            {
                GL.MultiTexCoord2(TextureUnit.Texture0, waterUV, waterUV);
                GL.MultiTexCoord2(TextureUnit.Texture1, waterUV, waterUV - move);
                GL.MultiTexCoord2(TextureUnit.Texture2, normalUV, normalUV + normalMove);
                GL.MultiTexCoord2(TextureUnit.Texture3, 0f, 0f);
                GL.MultiTexCoord2(TextureUnit.Texture4, 0f, 0f);
            }
            GL.Vertex3(x + 256f, y, z);

            if (RenderSettings.HasMultiTexturing)
            {
                GL.MultiTexCoord2(TextureUnit.Texture0, waterUV, 0f);
                GL.MultiTexCoord2(TextureUnit.Texture1, waterUV, 0f - move);
                GL.MultiTexCoord2(TextureUnit.Texture2, normalUV, 0f + normalMove);
                GL.MultiTexCoord2(TextureUnit.Texture3, 0f, 0f);
                GL.MultiTexCoord2(TextureUnit.Texture4, 0f, 0f);
            }
            GL.Vertex3(x + 256f, y + 256f, z);

            if (RenderSettings.HasMultiTexturing)
            {
                GL.MultiTexCoord2(TextureUnit.Texture0, 0f, 0f);
                GL.MultiTexCoord2(TextureUnit.Texture1, 0f, 0f - move);
                GL.MultiTexCoord2(TextureUnit.Texture2, 0f, 0f + normalMove);
                GL.MultiTexCoord2(TextureUnit.Texture3, 0f, 0f);
                GL.MultiTexCoord2(TextureUnit.Texture4, 0f, 0f);
            }
            GL.Vertex3(x, y + 256f, z);

        }

        public void CreateReflectionTexture(float waterHeight, int textureSize)
        {
            GL.Viewport(0, 0, textureSize, textureSize);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.LoadIdentity();
            Camera.LookAt();
            GL.PushMatrix();

            if (Camera.RenderPosition.Z > waterHeight)
            {
                GL.Translate(0f, 0f, waterHeight * 2);
                GL.Scale(1f, 1f, -1f); // upside down;
                GL.Enable(EnableCap.ClipPlane0);
                GL.ClipPlane(ClipPlaneName.ClipPlane0, AboveWaterPlane);
                GL.CullFace(CullFaceMode.Front);
                terrain.Render(RenderPass.Simple, 0, this, lastFrameTime);
                RenderObjects(RenderPass.Simple);
                RenderAvatars(RenderPass.Simple);
                RenderObjects(RenderPass.Alpha);
                GL.Disable(EnableCap.ClipPlane0);
                GL.CullFace(CullFaceMode.Back);
            }

            GL.PopMatrix();
            GL.BindTexture(TextureTarget.Texture2D, reflectionTexture);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, textureSize, textureSize);
        }

        public void CreateRefractionDepthTexture(float waterHeight, int textureSize)
        {
            GL.Viewport(0, 0, textureSize, textureSize);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.LoadIdentity();
            Camera.LookAt();
            GL.PushMatrix();

            if (Camera.RenderPosition.Z > waterHeight)
            {
                GL.Enable(EnableCap.ClipPlane0);
                GL.ClipPlane(ClipPlaneName.ClipPlane0, BelowWaterPlane);
                terrain.Render(RenderPass.Simple, 0, this, lastFrameTime);
                GL.Disable(EnableCap.ClipPlane0);
            }

            GL.PopMatrix();
            GL.BindTexture(TextureTarget.Texture2D, refractionTexture);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, textureSize, textureSize);

            GL.BindTexture(TextureTarget.Texture2D, depthTexture);
            GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent, 0, 0, textureSize, textureSize, 0);
        }

        public void RenderWater()
        {
            float z = Client.Network.CurrentSim.WaterHeight;
            GL.Color4(0.09f, 0.28f, 0.63f, 0.84f);

            if (RenderSettings.WaterReflections)
            {
                waterProgram.Start();

                // Reflection texture unit
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, reflectionTexture);
                GL.Uniform1(waterProgram.Uni("reflection"), 0);

                // Refraction texture unit
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, refractionTexture);
                GL.Uniform1(waterProgram.Uni("refraction"), 1);

                // Normal map
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, normalmap);
                GL.Uniform1(waterProgram.Uni("normalMap"), 2);

                //// DUDV map
                GL.ActiveTexture(TextureUnit.Texture3);
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, dudvmap);
                GL.Uniform1(waterProgram.Uni("dudvMap"), 3);


                //// Depth map
                GL.ActiveTexture(TextureUnit.Texture4);
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, depthTexture);
                GL.Uniform1(waterProgram.Uni("depthMap"), 4);


                int lightPos = GL.GetUniformLocation(waterProgram.ID, "lightPos");
                GL.Uniform4(lightPos, 0f, 0f, z + 100, 1f); // For now sun reflection in the water comes from the south west sim corner
                int cameraPos = GL.GetUniformLocation(waterProgram.ID, "cameraPos");
                GL.Uniform4(cameraPos, Camera.RenderPosition.X, Camera.RenderPosition.Y, Camera.RenderPosition.Z, 1f);
                int waterColor = GL.GetUniformLocation(waterProgram.ID, "waterColor");
                GL.Uniform4(waterColor, 0.09f, 0.28f, 0.63f, 0.84f);
            }

            GL.Begin(PrimitiveType.Quads);
            for (float x = -256f * 2; x <= 256 * 2; x += 256f)
                for (float y = -256f * 2; y <= 256 * 2; y += 256f)
                    DrawWaterQuad(x, y, z);
            GL.End();

            if (RenderSettings.WaterReflections)
            {
                GL.ActiveTexture(TextureUnit.Texture4);
                GL.Disable(EnableCap.Texture2D);

                GL.ActiveTexture(TextureUnit.Texture3);
                GL.Disable(EnableCap.Texture2D);

                GL.ActiveTexture(TextureUnit.Texture2);
                GL.Disable(EnableCap.Texture2D);

                GL.ActiveTexture(TextureUnit.Texture1);
                GL.Disable(EnableCap.Texture2D);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.Disable(EnableCap.Texture2D);

                ShaderProgram.Stop();
            }
        }

    }
}