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

using System;
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

        private int reflectionFBO = -1;
        private int refractionFBO = -1;
        private OpenMetaverse.Vector3 lastReflectionCameraPos;
        private float lastReflectionCameraYaw;
        private float lastReflectionCameraPitch;

        private void SetWaterPlanes()
        {
            AboveWaterPlane = Math3D.AbovePlane(Client.Network.CurrentSim.WaterHeight);
            BelowWaterPlane = Math3D.BelowPlane(Client.Network.CurrentSim.WaterHeight);
        }

        private void InitWater()
        {
            // Create empty textures for FBO rendering (256x256 for reflection/refraction)
            const int waterTextureSize = 256;
            
            // Reflection texture
            GL.GenTextures(1, out reflectionTexture);
            GL.BindTexture(TextureTarget.Texture2D, reflectionTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, waterTextureSize, waterTextureSize, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            
            // Refraction texture
            GL.GenTextures(1, out refractionTexture);
            GL.BindTexture(TextureTarget.Texture2D, refractionTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, waterTextureSize, waterTextureSize, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            
            // Depth texture
            GL.GenTextures(1, out depthTexture);
            GL.BindTexture(TextureTarget.Texture2D, depthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, waterTextureSize, waterTextureSize, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // Load normal map
            SKBitmap normal = SKBitmap.Decode(System.IO.Path.Combine("shader_data", "normalmap.png"));
            if (normal != null)
            {
                normalmap = RHelp.GLLoadImage(normal, false);
                normal.Dispose();
            }

            // Load DUDV map
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
            // Create FBO if needed
            if (reflectionFBO == -1)
            {
                GL.GenFramebuffers(1, out reflectionFBO);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, reflectionFBO);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, reflectionTexture, 0);
                
                // Create depth renderbuffer for proper depth testing
                int depthRB;
                GL.GenRenderbuffers(1, out depthRB);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRB);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, textureSize, textureSize);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthRB);
                
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
            
            // Bind FBO for rendering
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, reflectionFBO);
            GL.Viewport(0, 0, textureSize, textureSize);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();
            Camera.LookAt();

            if (Camera.RenderPosition.Z > waterHeight)
            {
                GL.Translate(0f, 0f, waterHeight * 2);
                GL.Scale(1f, 1f, -1f);
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
            
            // Unbind FBO - back to main framebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void CreateRefractionDepthTexture(float waterHeight, int textureSize)
        {
            // Create FBO if needed
            if (refractionFBO == -1)
            {
                GL.GenFramebuffers(1, out refractionFBO);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, refractionFBO);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, refractionTexture, 0);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTexture, 0);
                
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
            
            // Bind FBO for rendering
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, refractionFBO);
            GL.Viewport(0, 0, textureSize, textureSize);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();
            Camera.LookAt();

            if (Camera.RenderPosition.Z > waterHeight)
            {
                GL.Enable(EnableCap.ClipPlane0);
                GL.ClipPlane(ClipPlaneName.ClipPlane0, BelowWaterPlane);
                
                terrain.Render(RenderPass.Simple, 0, this, lastFrameTime);
                
                GL.Disable(EnableCap.ClipPlane0);
            }

            GL.PopMatrix();
            
            // Unbind FBO - back to main framebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void RenderWater()
        {
            float z = Client.Network.CurrentSim.WaterHeight;
            bool cameraUnderwater = Camera.RenderPosition.Z < z;

            bool useShader = RenderSettings.WaterReflections && RenderSettings.HasShaders && waterProgram.ID > 0;

            // Update reflection/refraction textures periodically if shaders are enabled
            // Only update when camera is above water (no reflections needed underwater)
            if (useShader && !cameraUnderwater)
            {
                timeSinceReflection += lastFrameTime;
                framesSinceReflection++;
                
                // Calculate camera movement since last reflection update
                OpenMetaverse.Vector3 currentPos = Camera.RenderPosition;
                float cameraMoved = OpenMetaverse.Vector3.Distance(currentPos, lastReflectionCameraPos);
                
                // Calculate camera rotation (approximate yaw/pitch from focal point)
                OpenMetaverse.Vector3 lookDir = Camera.RenderFocalPoint - Camera.RenderPosition;
                lookDir.Normalize();
                float currentYaw = (float)System.Math.Atan2(lookDir.Y, lookDir.X);
                float currentPitch = (float)System.Math.Asin(lookDir.Z);
                float yawDelta = System.Math.Abs(currentYaw - lastReflectionCameraYaw);
                float pitchDelta = System.Math.Abs(currentPitch - lastReflectionCameraPitch);
                
                // Adaptive update thresholds:
                // - Update every 3 frames minimum (smooth for fast camera movement)
                // - Or every 0.1 seconds (catch slow changes)
                // - Or if camera moved more than 2 meters
                // - Or if camera rotated more than 5 degrees (~0.087 radians)
                bool significantMovement = cameraMoved > 2.0f;
                bool significantRotation = yawDelta > 0.087f || pitchDelta > 0.087f;
                bool timeThreshold = framesSinceReflection >= 3 || timeSinceReflection >= 0.1f;
                
                if (timeThreshold || significantMovement || significantRotation)
                {
                    // Save current GL state
                    int[] savedViewport = new int[4];
                    GL.GetInteger(GetPName.Viewport, savedViewport);
                    
                    const int waterTextureSize = 256;
                    
                    // Update reflection texture (uses FBO internally)
                    CreateReflectionTexture(z, waterTextureSize);
                    
                    // Update refraction texture (uses FBO internally)
                    CreateRefractionDepthTexture(z, waterTextureSize);
                    
                    // Restore viewport (FBO unbinding resets to default framebuffer)
                    GL.Viewport(savedViewport[0], savedViewport[1], savedViewport[2], savedViewport[3]);
                    
                    // Ensure we're in the correct matrix mode
                    GL.MatrixMode(MatrixMode.Modelview);
                    
                    // Reset counters and save camera state
                    framesSinceReflection = 0;
                    timeSinceReflection = 0f;
                    lastReflectionCameraPos = currentPos;
                    lastReflectionCameraYaw = currentYaw;
                    lastReflectionCameraPitch = currentPitch;
                }
            }

            if (useShader && !cameraUnderwater)
            {
                // Above water: full shader with reflections
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
                    GL.Uniform4(lightPos, 0f, 0f, z + 100, 1f);
                int cameraPos = waterProgram.Uni("cameraPos");
                if (cameraPos != -1)
                    GL.Uniform4(cameraPos, Camera.RenderPosition.X, Camera.RenderPosition.Y, Camera.RenderPosition.Z, 1f);
                int waterColor = waterProgram.Uni("waterColor");
                if (waterColor != -1)
                    GL.Uniform4(waterColor, 0.09f, 0.28f, 0.63f, 0.84f);
            }
            else
            {
                // Underwater or fallback: simple translucent rendering
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
                
                // For underwater view, disable culling so water is visible from both sides
                if (cameraUnderwater)
                {
                    GL.DepthMask(false);
                    GL.Disable(EnableCap.CullFace);
                }
            }

            // advance CPU-side animation clock using configurable speed
            cpuWaveAnim += lastFrameTime * RenderSettings.FallbackWaterAnimationSpeed;

            // Make water truly expansive - render centered on camera position
            // Calculate camera-relative start position (snap to 256m grid)
            float camX = Camera.RenderPosition.X;
            float camY = Camera.RenderPosition.Y;
            float startX = (float)System.Math.Floor(camX / 256f) * 256f - 256f * 4; // Start 4 tiles back
            float startY = (float)System.Math.Floor(camY / 256f) * 256f - 256f * 4;
            float endX = startX + 256f * 9; // Render 9x9 grid of tiles (covers large area)
            float endY = startY + 256f * 9;

            GL.Begin(PrimitiveType.Quads);
            bool cpuAnimate = (!useShader || cameraUnderwater) && RenderSettings.FallbackWaterAnimationEnabled;
            
            // Adjust alpha for underwater view - more transparent to see through better
            float underwaterAlpha = cameraUnderwater ? 0.3f : 0.75f;
            
            for (float x = startX; x < endX; x += 256f)
            {
                for (float y = startY; y < endY; y += 256f)
                {
                    if (cameraUnderwater)
                    {
                        // Underwater: render water surface from below with adjusted color
                        DrawWaterQuadUnderwater(x, y, z, cpuAnimate, underwaterAlpha);
                    }
                    else
                    {
                        DrawWaterQuad(x, y, z, cpuAnimate);
                    }
                }
            }
            GL.End();

            if (useShader && !cameraUnderwater)
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
                
                if (cameraUnderwater)
                {
                    GL.DepthMask(true);
                    GL.Enable(EnableCap.CullFace);
                    GL.CullFace(CullFaceMode.Back);
                }
            }

            // Restore OpenGL state for subsequent rendering
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Color4(1f, 1f, 1f, 1f);
        }

        // Simplified water rendering for underwater view
        private void DrawWaterQuadUnderwater(float x, float y, float z, bool animate, float alpha)
        {
            // Simpler rendering from below - just a translucent surface
            // Use a slightly brighter color for visibility from below
            float r = 0.15f, g = 0.35f, b = 0.55f;
            
            if (animate && RenderSettings.FallbackWaterAnimationEnabled)
            {
                float baseAlpha = alpha;
                float amp = RenderSettings.FallbackWaterAnimationAmplitude * 0.5f; // Less animation underwater
                float waveScale = 0.02f;
                float phase = cpuWaveAnim;
                
                // Animate all four corners
                float a1 = baseAlpha + amp * (float)System.Math.Sin((x + y) * waveScale + phase);
                GL.Color4(r, g, b, a1);
                GL.Vertex3(x, y, z);
                
                float a2 = baseAlpha + amp * (float)System.Math.Sin((x + 256f + y) * waveScale + phase + 0.5f);
                GL.Color4(r, g, b, a2);
                GL.Vertex3(x + 256f, y, z);
                
                float a3 = baseAlpha + amp * (float)System.Math.Sin((x + 256f + y + 256f) * waveScale + phase + 1.0f);
                GL.Color4(r, g, b, a3);
                GL.Vertex3(x + 256f, y + 256f, z);
                
                float a4 = baseAlpha + amp * (float)System.Math.Sin((x + y + 256f) * waveScale + phase + 1.5f);
                GL.Color4(r, g, b, a4);
                GL.Vertex3(x, y + 256f, z);
            }
            else
            {
                GL.Color4(r, g, b, alpha);
                // Render quad with vertices in clockwise order for correct culling with CullFace.Front
                GL.Vertex3(x, y, z);
                GL.Vertex3(x + 256f, y, z);
                GL.Vertex3(x + 256f, y + 256f, z);
                GL.Vertex3(x, y + 256f, z);
            }
        }
    }
}