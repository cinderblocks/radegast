/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2026, Sjofn, LLC
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

#region Usings

using OpenTK.Graphics.OpenGL;
using SkiaSharp;

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
            SKBitmap normal = SKBitmap.Decode(System.IO.Path.Combine("shader_data", "normalmap.png"));
            if (normal != null)
            {
                reflectionTexture = RHelp.GLLoadImage(normal, false);
                refractionTexture = RHelp.GLLoadImage(normal, false);
                normalmap = RHelp.GLLoadImage(normal, false);
                depthTexture = RHelp.GLLoadImage(normal, false);
                normal.Dispose();
            }

            SKBitmap dudv = SKBitmap.Decode(System.IO.Path.Combine("shader_data", "dudvmap.png"));
            if (dudv != null)
            {
                dudvmap = RHelp.GLLoadImage(dudv, false);
                dudv.Dispose();
            }

            waterProgram.Load("water.vert", "water.frag");
        }

        private readonly float waterUV = 35f;
        private readonly float waterFlow = 0.0025f;
        private float normalMove = 0f;
        private float move = 0f;
        private const float kNormalMapScale = 0.25f;
        private float normalUV;
        // CPU-side animation time accumulator for fallback water animation
        private float cpuWaveAnim = 0f;

        // cpuAnimate: when true use CPU-side per-vertex color modulation for a simple animated effect
        private void DrawWaterQuad(float x, float y, float z, bool cpuAnimate)
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
             if (cpuAnimate)
             {
                 // compute subtle alpha modulation per corner based on position and animation time
                 // values chosen to be small so the effect is subtle
                 float baseAlpha = RenderSettings.FallbackWaterBaseAlpha;
                 float amp = RenderSettings.FallbackWaterAnimationAmplitude;
                 // scale position into a wave coordinate
                 float waveScale = 0.02f;
                 float phase = cpuWaveAnim;

                 float a1 = baseAlpha + amp * (float)System.Math.Sin((x + y) * waveScale + phase);
                 GL.Color4(0.05f, 0.20f, 0.45f, a1);
                 GL.Vertex3(x, y, z);
             }
             else
             {
                 GL.Color4(0.05f, 0.20f, 0.45f, 0.75f);
                 GL.Vertex3(x, y, z);
             }

             if (RenderSettings.HasMultiTexturing)
             {
                 GL.MultiTexCoord2(TextureUnit.Texture0, waterUV, waterUV);
                 GL.MultiTexCoord2(TextureUnit.Texture1, waterUV, waterUV - move);
                 GL.MultiTexCoord2(TextureUnit.Texture2, normalUV, normalUV + normalMove);
                 GL.MultiTexCoord2(TextureUnit.Texture3, 0f, 0f);
                 GL.MultiTexCoord2(TextureUnit.Texture4, 0f, 0f);
             }
             if (cpuAnimate)
             {
                 float baseAlpha = RenderSettings.FallbackWaterBaseAlpha;
                 float amp = RenderSettings.FallbackWaterAnimationAmplitude;
                 float waveScale = 0.02f;
                 float phase = cpuWaveAnim + 0.5f;
                 float a2 = baseAlpha + amp * (float)System.Math.Sin((x + 256f + y) * waveScale + phase);
                 GL.Color4(0.05f, 0.20f, 0.45f, a2);
                 GL.Vertex3(x + 256f, y, z);
             }
             else
             {
                 GL.Color4(0.05f, 0.20f, 0.45f, 0.75f);
                 GL.Vertex3(x + 256f, y, z);
             }

             if (RenderSettings.HasMultiTexturing)
             {
                 GL.MultiTexCoord2(TextureUnit.Texture0, waterUV, 0f);
                 GL.MultiTexCoord2(TextureUnit.Texture1, waterUV, 0f - move);
                 GL.MultiTexCoord2(TextureUnit.Texture2, normalUV, 0f + normalMove);
                 GL.MultiTexCoord2(TextureUnit.Texture3, 0f, 0f);
                 GL.MultiTexCoord2(TextureUnit.Texture4, 0f, 0f);
             }
             if (cpuAnimate)
             {
                 float baseAlpha = RenderSettings.FallbackWaterBaseAlpha;
                 float amp = RenderSettings.FallbackWaterAnimationAmplitude;
                 float waveScale = 0.02f;
                 float phase = cpuWaveAnim + 1.0f;
                 float a3 = baseAlpha + amp * (float)System.Math.Sin((x + 256f + y + 256f) * waveScale + phase);
                 GL.Color4(0.05f, 0.20f, 0.45f, a3);
                 GL.Vertex3(x + 256f, y + 256f, z);
             }
             else
             {
                 GL.Color4(0.05f, 0.20f, 0.45f, 0.75f);
                 GL.Vertex3(x + 256f, y + 256f, z);
             }

             if (RenderSettings.HasMultiTexturing)
             {
                 GL.MultiTexCoord2(TextureUnit.Texture0, 0f, 0f);
                 GL.MultiTexCoord2(TextureUnit.Texture1, 0f, 0f - move);
                 GL.MultiTexCoord2(TextureUnit.Texture2, 0f, 0f + normalMove);
                 GL.MultiTexCoord2(TextureUnit.Texture3, 0f, 0f);
                 GL.MultiTexCoord2(TextureUnit.Texture4, 0f, 0f);
             }
             if (cpuAnimate)
             {
                 float baseAlpha = RenderSettings.FallbackWaterBaseAlpha;
                 float amp = RenderSettings.FallbackWaterAnimationAmplitude;
                 float waveScale = 0.02f;
                 float phase = cpuWaveAnim + 1.5f;
                 float a4 = baseAlpha + amp * (float)System.Math.Sin((x + y + 256f) * waveScale + phase);
                 GL.Color4(0.05f, 0.20f, 0.45f, a4);
                 GL.Vertex3(x, y + 256f, z);
             }
             else
             {
                 GL.Color4(0.05f, 0.20f, 0.45f, 0.75f);
                 GL.Vertex3(x, y + 256f, z);
             }

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

            bool useShader = RenderSettings.WaterReflections && RenderSettings.HasShaders && waterProgram.ID > 0;

            if (useShader)
            {
                GL.Color4(0.09f, 0.28f, 0.63f, 0.84f);
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


                int lightPos = waterProgram.Uni("lightPos");
                if (lightPos != -1)
                    GL.Uniform4(lightPos, 0f, 0f, z + 100, 1f); // For now sun reflection in the water comes from the south west sim corner
                int cameraPos = waterProgram.Uni("cameraPos");
                if (cameraPos != -1)
                    GL.Uniform4(cameraPos, Camera.RenderPosition.X, Camera.RenderPosition.Y, Camera.RenderPosition.Z, 1f);
                int waterColor = waterProgram.Uni("waterColor");
                if (waterColor != -1)
                    GL.Uniform4(waterColor, 0.09f, 0.28f, 0.63f, 0.84f);
            }
            else
            {
                // Fallback flat-colored water rendering (fixed-function pipeline)
                // Ensure no textures are bound and blending is enabled for transparency
                for (TextureUnit tu = TextureUnit.Texture0; tu <= TextureUnit.Texture4; tu++)
                {
                    GL.ActiveTexture(tu);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    GL.Disable(EnableCap.Texture2D);
                }

                // Set up proper blending for water transparency
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                
                // Disable lighting to use vertex colors directly
                GL.Disable(EnableCap.Lighting);
            }

            // advance CPU-side animation clock using configurable speed
            cpuWaveAnim += lastFrameTime * RenderSettings.FallbackWaterAnimationSpeed; // speed multiplier for visible motion

            // Make water truly expansive - render centered on camera position
            // Calculate camera-relative start position (snap to 256m grid)
            float camX = Camera.RenderPosition.X;
            float camY = Camera.RenderPosition.Y;
            float startX = (float)System.Math.Floor(camX / 256f) * 256f - 256f * 4; // Start 4 tiles back
            float startY = (float)System.Math.Floor(camY / 256f) * 256f - 256f * 4;
            float endX = startX + 256f * 9; // Render 9x9 grid of tiles (covers large area)
            float endY = startY + 256f * 9;

            GL.Begin(PrimitiveType.Quads);
            bool cpuAnimate = !useShader && RenderSettings.FallbackWaterAnimationEnabled;
            for (float x = startX; x < endX; x += 256f)
                for (float y = startY; y < endY; y += 256f)
                    DrawWaterQuad(x, y, z, cpuAnimate);
            GL.End();

            if (useShader)
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
            else
            {
                // Restore fixed-function state
                GL.Disable(EnableCap.Blend);
                GL.Enable(EnableCap.Lighting);
            }

            // Restore OpenGL state for subsequent rendering
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Color4(1f, 1f, 1f, 1f);
        }

    }
}