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
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using OpenMetaverse;

namespace Radegast.Rendering
{
    /// <summary>
    /// Renders a vibrant sky dome with atmosphere effects
    /// </summary>
    public class RenderSky : IDisposable
    {
        #region Sky Color Constants
        // Zenith (top of sky) - Rich deep blue
        private static readonly OpenTK.Graphics.Color4 ZENITH_COLOR = new OpenTK.Graphics.Color4(0.2f, 0.4f, 0.8f, 1.0f);
        
        // Horizon - Lighter blue transitioning to atmosphere
        private static readonly OpenTK.Graphics.Color4 HORIZON_COLOR = new OpenTK.Graphics.Color4(0.5f, 0.7f, 0.95f, 1.0f);
        
        // Sun halo color - Warm golden
        private static readonly OpenTK.Graphics.Color4 SUN_HALO_COLOR = new OpenTK.Graphics.Color4(1.0f, 0.9f, 0.7f, 1.0f);
        
        // Sun core color - Bright white-yellow
        private static readonly OpenTK.Graphics.Color4 SUN_CORE_COLOR = new OpenTK.Graphics.Color4(1.0f, 1.0f, 0.95f, 1.0f);

        // Sky dome radius and resolution
        private const float SKY_RADIUS = 500.0f;
        private const int LATITUDE_BANDS = 16;
        private const int LONGITUDE_BANDS = 32;
        #endregion

        #region Fields
        private int skyVBO = -1;
        private int skyIndexVBO = -1;
        private int skyVAO = -1;
        private bool vboFailed = false;
        private bool initialized = false;
        
        private float[] skyVertices;
        private float[] skyColors;
        private ushort[] skyIndices;
        private int vertexCount;
        private int indexCount;
        
        private readonly SceneWindow scene;
        private Vector3 sunDirection;

        // Cloud layer fields
        private int[] cloudTextures = null;
        private int cloudLayerCount = 3;
        private float[] cloudRotation;
        private float[] cloudSpeed;
        private float[] cloudScaleFactors;
        private float[] cloudHeights;
        private int cloudTextureSize = 512;
        private int lastCloudUpdateMs = Environment.TickCount;
        private readonly Random cloudRand = new Random();
        #endregion

        #region Construction and Disposal
        public RenderSky(SceneWindow sceneWindow)
        {
            scene = sceneWindow;
            sunDirection = new Vector3(0.5f, 0.5f, 1.0f);
            sunDirection.Normalize();
        }

        public void Dispose()
        {
            if (skyVBO != -1)
            {
                try { Compat.DeleteBuffer(skyVBO); } catch { }
                skyVBO = -1;
            }
            if (skyIndexVBO != -1)
            {
                try { Compat.DeleteBuffer(skyIndexVBO); } catch { }
                skyIndexVBO = -1;
            }
            if (skyVAO != -1)
            {
                try { Compat.DeleteVertexArray(skyVAO); } catch { }
                skyVAO = -1;
            }
            
            skyVertices = null;
            skyColors = null;
            skyIndices = null;
            initialized = false;

            // Dispose cloud textures
            if (cloudTextures != null)
            {
                foreach (var t in cloudTextures)
                {
                    if (t > 0)
                    {
                        try { GL.DeleteTexture(t); } catch { }
                    }
                }
                cloudTextures = null;
            }
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initialize the sky dome geometry
        /// </summary>
        public void Initialize()
        {
            if (initialized) return;

            GenerateSkyDome();
            
            if (RenderSettings.UseVBO)
            {
                CreateVBOs();
            }
            
            initialized = true;

            // Prepare cloud layers
            GenerateCloudLayers();
        }

        /// <summary>
        /// Generate sky dome vertices, colors, and indices
        /// </summary>
        private void GenerateSkyDome()
        {
            vertexCount = (LATITUDE_BANDS + 1) * (LONGITUDE_BANDS + 1);
            
            // Interleaved: position (3) + color (4) = 7 floats per vertex
            skyVertices = new float[vertexCount * 3];
            skyColors = new float[vertexCount * 4];
            
            int vertIdx = 0;
            int colorIdx = 0;

            for (int lat = 0; lat <= LATITUDE_BANDS; lat++)
            {
                float theta = lat * (float)Math.PI / (2.0f * LATITUDE_BANDS); // 0 to PI/2 (hemisphere)
                float sinTheta = (float)Math.Sin(theta);
                float cosTheta = (float)Math.Cos(theta);

                for (int lon = 0; lon <= LONGITUDE_BANDS; lon++)
                {
                    float phi = lon * 2.0f * (float)Math.PI / LONGITUDE_BANDS;
                    float sinPhi = (float)Math.Sin(phi);
                    float cosPhi = (float)Math.Cos(phi);

                    // Position
                    float x = cosPhi * sinTheta;
                    float y = sinPhi * sinTheta;
                    float z = cosTheta;

                    skyVertices[vertIdx++] = x * SKY_RADIUS;
                    skyVertices[vertIdx++] = y * SKY_RADIUS;
                    skyVertices[vertIdx++] = z * SKY_RADIUS;

                    // Calculate color based on position
                    Vector3 vertPos = new Vector3(x, y, z);
                    vertPos.Normalize();
                    
                    // Interpolate between zenith and horizon based on Z (altitude)
                    float altitude = z; // 0 at horizon, 1 at zenith
                    
                    // Sun influence
                    float sunInfluence = Math.Max(0.0f, Vector3.Dot(vertPos, sunDirection));
                    sunInfluence = (float)Math.Pow(sunInfluence, 4.0); // Sharp falloff
                    
                    // Base sky color
                    OpenTK.Graphics.Color4 skyColor = InterpolateColor(HORIZON_COLOR, ZENITH_COLOR, altitude);
                    
                    // Add sun halo
                    if (sunInfluence > 0.01f)
                    {
                        float haloStrength = sunInfluence * 0.7f;
                        skyColor = InterpolateColor(skyColor, SUN_HALO_COLOR, haloStrength);
                    }
                    
                    // Add sun core for vertices very close to sun direction
                    if (sunInfluence > 0.95f)
                    {
                        float coreStrength = (sunInfluence - 0.95f) / 0.05f;
                        skyColor = InterpolateColor(skyColor, SUN_CORE_COLOR, coreStrength);
                    }

                    skyColors[colorIdx++] = skyColor.R;
                    skyColors[colorIdx++] = skyColor.G;
                    skyColors[colorIdx++] = skyColor.B;
                    skyColors[colorIdx++] = skyColor.A;
                }
            }

            // Generate indices
            indexCount = LATITUDE_BANDS * LONGITUDE_BANDS * 6;
            skyIndices = new ushort[indexCount];
            int idx = 0;

            for (int lat = 0; lat < LATITUDE_BANDS; lat++)
            {
                for (int lon = 0; lon < LONGITUDE_BANDS; lon++)
                {
                    int first = lat * (LONGITUDE_BANDS + 1) + lon;
                    int second = first + LONGITUDE_BANDS + 1;

                    skyIndices[idx++] = (ushort)first;
                    skyIndices[idx++] = (ushort)second;
                    skyIndices[idx++] = (ushort)(first + 1);

                    skyIndices[idx++] = (ushort)second;
                    skyIndices[idx++] = (ushort)(second + 1);
                    skyIndices[idx++] = (ushort)(first + 1);
                }
            }
        }

        /// <summary>
        /// Create VBOs for the sky dome
        /// </summary>
        private void CreateVBOs()
        {
            try
            {
                // Create interleaved VBO: position (3) + color (4)
                int stride = 7 * sizeof(float);
                var interleaved = new float[vertexCount * 7];
                
                for (int i = 0; i < vertexCount; i++)
                {
                    int interleavedIdx = i * 7;
                    int vertIdx = i * 3;
                    int colorIdx = i * 4;
                    
                    // Position
                    interleaved[interleavedIdx + 0] = skyVertices[vertIdx + 0];
                    interleaved[interleavedIdx + 1] = skyVertices[vertIdx + 1];
                    interleaved[interleavedIdx + 2] = skyVertices[vertIdx + 2];
                    
                    // Color
                    interleaved[interleavedIdx + 3] = skyColors[colorIdx + 0];
                    interleaved[interleavedIdx + 4] = skyColors[colorIdx + 1];
                    interleaved[interleavedIdx + 5] = skyColors[colorIdx + 2];
                    interleaved[interleavedIdx + 6] = skyColors[colorIdx + 3];
                }

                // Create vertex buffer
                Compat.GenBuffers(out skyVBO);
                Compat.BindBuffer(BufferTarget.ArrayBuffer, skyVBO);
                Compat.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(interleaved.Length * sizeof(float)), 
                    interleaved, BufferUsageHint.StaticDraw);
                
                if (Compat.BufferSize(BufferTarget.ArrayBuffer) != interleaved.Length * sizeof(float))
                {
                    vboFailed = true;
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                    Compat.DeleteBuffer(skyVBO);
                    skyVBO = -1;
                    return;
                }

                // Create index buffer
                Compat.GenBuffers(out skyIndexVBO);
                Compat.BindBuffer(BufferTarget.ElementArrayBuffer, skyIndexVBO);
                Compat.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(skyIndices.Length * sizeof(ushort)), 
                    skyIndices, BufferUsageHint.StaticDraw);
                
                if (Compat.BufferSize(BufferTarget.ElementArrayBuffer) != skyIndices.Length * sizeof(ushort))
                {
                    vboFailed = true;
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                    Compat.DeleteBuffer(skyIndexVBO);
                    skyIndexVBO = -1;
                    if (skyVBO != -1) { Compat.DeleteBuffer(skyVBO); skyVBO = -1; }
                    return;
                }

                // Create VAO if supported
                try
                {
                    Compat.GenVertexArrays(out skyVAO);
                    Compat.BindVertexArray(skyVAO);
                    
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, skyVBO);
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, skyIndexVBO);
                    
                    // Position: location 0, 3 floats, stride 7 floats, offset 0
                    GL.EnableVertexAttribArray(0);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
                    
                    // Color: location 1, 4 floats, stride 7 floats, offset 3 floats
                    GL.EnableVertexAttribArray(1);
                    GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
                    
                    Compat.BindVertexArray(0);
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                }
                catch
                {
                    // VAO not supported, fall back to VBO only
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                    skyVAO = -1;
                }
                
                vboFailed = false;
            }
            catch
            {
                vboFailed = true;
                if (skyVBO != -1) { try { Compat.DeleteBuffer(skyVBO); } catch { } skyVBO = -1; }
                if (skyIndexVBO != -1) { try { Compat.DeleteBuffer(skyIndexVBO); } catch { } skyIndexVBO = -1; }
                if (skyVAO != -1) { try { Compat.DeleteVertexArray(skyVAO); } catch { } skyVAO = -1; }
            }
        }

        /// <summary>
        /// Generate cloud layers textures and parameters
        /// </summary>
        private void GenerateCloudLayers()
        {
            // Initialize arrays
            cloudTextures = new int[cloudLayerCount];
            cloudRotation = new float[cloudLayerCount];
            cloudSpeed = new float[cloudLayerCount];
            cloudScaleFactors = new float[cloudLayerCount];
            cloudHeights = new float[cloudLayerCount];

            for (int i = 0; i < cloudLayerCount; i++)
            {
                // Different speeds and scales per layer for parallax
                cloudRotation[i] = (float)(cloudRand.NextDouble() * 360.0);
                // small speeds, some clockwise, some counter
                cloudSpeed[i] = (float)((cloudRand.NextDouble() * 10.0 + 2.0) * (cloudRand.Next(0,2) == 0 ? 1.0 : -1.0));
                cloudScaleFactors[i] = 1.0f + (float)i * 0.8f; // higher layers larger
                cloudHeights[i] = 60.0f + i * 30.0f; // offsets above camera

                // Generate bitmap
                using (var bmp = new SkiaSharp.SKBitmap(cloudTextureSize, cloudTextureSize, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                using (var canvas = new SkiaSharp.SKCanvas(bmp))
                {
                    canvas.Clear(SkiaSharp.SKColors.Transparent);

                    // Paint many soft circles to approximate cloud shapes
                    int circles = 120 + i * 40;
                    for (int c = 0; c < circles; c++)
                    {
                        float rx = (float)(cloudRand.NextDouble() * cloudTextureSize);
                        float ry = (float)(cloudRand.NextDouble() * cloudTextureSize);
                        float radius = (float)(cloudRand.NextDouble() * (cloudTextureSize * (0.05 + i * 0.05)) + cloudTextureSize * 0.02);
                        float alpha = (float)(0.03 + cloudRand.NextDouble() * 0.07);

                        using (var paint = new SkiaSharp.SKPaint())
                        {
                            paint.IsAntialias = true;
                            paint.Color = new SkiaSharp.SKColor(255, 255, 255, (byte)(alpha * 255));
                            // Use a radial gradient to make softer edges
                            var shader = SkiaSharp.SKShader.CreateRadialGradient(new SkiaSharp.SKPoint(rx, ry), radius,
                                new SkiaSharp.SKColor[] { new SkiaSharp.SKColor(255,255,255,(byte)(alpha*255)), SkiaSharp.SKColors.Transparent },
                                new float[] { 0, 1 }, SkiaSharp.SKShaderTileMode.Clamp);
                            paint.Shader = shader;
                            canvas.DrawCircle(rx, ry, radius, paint);
                            paint.Shader?.Dispose();
                        }
                    }

                    // Slight global fade and variation
                    using (var paint = new SkiaSharp.SKPaint())
                    {
                        paint.Color = new SkiaSharp.SKColor(255, 255, 255, 25);
                        canvas.DrawRect(new SkiaSharp.SKRect(0, 0, cloudTextureSize, cloudTextureSize), paint);
                    }

                    // Upload to GL and store texture
                    try
                    {
                        int tex = RHelp.GLLoadImage(bmp, true);
                        cloudTextures[i] = tex;
                    }
                    catch
                    {
                        cloudTextures[i] = -1;
                    }
                }
            }
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Update sun direction (call when lighting changes)
        /// </summary>
        public void UpdateSunDirection(Vector3 direction)
        {
            sunDirection = direction;
            sunDirection.Normalize();
            
            // Regenerate sky colors with new sun position
            if (initialized)
            {
                initialized = false;
                Dispose();
                Initialize();
            }
        }

        /// <summary>
        /// Render the sky dome
        /// </summary>
        public void Render(RenderPass pass, Vector3 cameraPosition)
        {
            if (!initialized) Initialize();
            if (pass == RenderPass.Picking) return; // Don't render sky in picking pass

            // Update cloud rotations
            int nowMs = Environment.TickCount;
            float dt = Math.Max(0f, (nowMs - lastCloudUpdateMs) / 1000f);
            lastCloudUpdateMs = nowMs;

            if (cloudTextures != null)
            {
                for (int i = 0; i < cloudLayerCount; i++)
                {
                    cloudRotation[i] += cloudSpeed[i] * dt;
                }
            }

            // Save state
            GL.PushAttrib(AttribMask.EnableBit | AttribMask.DepthBufferBit | AttribMask.LightingBit);
            
            // Disable depth writes but keep depth test so sky is behind everything
            GL.DepthMask(false);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Texture2D);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            
            GL.PushMatrix();
            
            // Center sky dome on camera so it always surrounds the viewer
            GL.Translate(cameraPosition.X, cameraPosition.Y, cameraPosition.Z);
            
            bool usedShader = false;
            
            // Try shader path if available
            if (RenderSettings.HasShaders && RenderSettings.EnableSkyShader && scene != null)
            {
                try
                {
                    var shaderManager = scene.GetType().GetField("shaderManager", 
                        BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(scene);
                    
                    if (shaderManager != null)
                    {
                        var startMethod = shaderManager.GetType().GetMethod("StartShader");
                        var getMethod = shaderManager.GetType().GetMethod("GetProgram");
                        
                        if (startMethod != null && getMethod != null)
                        {
                            bool started = (bool)startMethod.Invoke(shaderManager, new object[] { "sky" });
                            if (started)
                            {
                                var prog = getMethod.Invoke(shaderManager, new object[] { "sky" });
                                if (prog != null)
                                {
                                    usedShader = RenderWithShader(prog);
                                    
                                    // Stop shader
                                    var stopMethod = shaderManager.GetType().GetMethod("StopShader");
                                    stopMethod?.Invoke(shaderManager, null);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    usedShader = false;
                }
            }
            
            if (!usedShader)
            {
                // Fixed-function rendering path
                if (RenderSettings.UseVBO && !vboFailed && skyVBO != -1 && skyIndexVBO != -1)
                {
                    RenderWithVBO();
                }
                else
                {
                    RenderWithClientArrays();
                }

                // Render cloud layers as large textured quads centered on camera (fixed-function fallback)
                GL.Enable(EnableCap.Texture2D);
                for (int i = 0; i < cloudLayerCount; i++)
                {
                    int tex = cloudTextures[i];
                    if (tex <= 0) continue;

                    GL.PushMatrix();
                    // place layer at camera altitude + layer height
                    GL.Translate(cameraPosition.X, cameraPosition.Y, cameraPosition.Z + cloudHeights[i]);
                    GL.Rotate(cloudRotation[i], 0f, 0f, 1f);
                    float size = SKY_RADIUS * 2.5f * cloudScaleFactors[i];

                    GL.Color4(1f, 1f, 1f, 0.6f - i * 0.12f);
                    GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, tex);

                    GL.Begin(PrimitiveType.Quads);
                    GL.TexCoord2(0f, 0f); GL.Vertex3(-size, -size, 0f);
                    GL.TexCoord2(1f, 0f); GL.Vertex3(size, -size, 0f);
                    GL.TexCoord2(1f, 1f); GL.Vertex3(size, size, 0f);
                    GL.TexCoord2(0f, 1f); GL.Vertex3(-size, size, 0f);
                    GL.End();

                    GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 0);
                    GL.PopMatrix();
                }
                GL.Disable(EnableCap.Texture2D);
                GL.Color4(1f, 1f, 1f, 1f);
            }
            else
            {
                // Shader path handled cloud composition inside the fragment shader; nothing else to do here.
            }
            
            GL.PopMatrix();
            
            // Restore state
            GL.DepthMask(true);
            GL.PopAttrib();
        }

        /// <summary>
        /// Render using shader program
        /// </summary>
        private bool RenderWithShader(object prog)
        {
            try
            {
                var progType = prog.GetType();
                var uniMethod = progType.GetMethod("Uni");
                var attrMethod = progType.GetMethod("Attr");

                if (uniMethod == null || attrMethod == null) return false;

                // Get uniform locations
                int uMVP = (int)uniMethod.Invoke(prog, new object[] { "uMVP" });
                int uAtmosphere = (int)uniMethod.Invoke(prog, new object[] { "atmosphereStrength" });
                int uSunDir = (int)uniMethod.Invoke(prog, new object[] { "sunDirection" });
                int uSunColor = (int)uniMethod.Invoke(prog, new object[] { "sunColor" });
                int uSunInfluence = (int)uniMethod.Invoke(prog, new object[] { "sunInfluence" });
                int uRayleigh = (int)uniMethod.Invoke(prog, new object[] { "rayleighScale" });
                int uMie = (int)uniMethod.Invoke(prog, new object[] { "mieScale" });
                int uMieG = (int)uniMethod.Invoke(prog, new object[] { "mieG" });
                int uSunI = (int)uniMethod.Invoke(prog, new object[] { "sunIntensity" });
                int uExposure = (int)uniMethod.Invoke(prog, new object[] { "exposure" });

                // Get attribute locations
                int aPosition = (int)attrMethod.Invoke(prog, new object[] { "aPosition" });
                int aColor = (int)attrMethod.Invoke(prog, new object[] { "aColor" });

                if (aPosition == -1 || aColor == -1) return false;

                // Set uniforms
                if (uMVP != -1)
                {
                    GL.GetFloat(GetPName.ModelviewMatrix, out OpenTK.Matrix4 mv);
                    GL.GetFloat(GetPName.ProjectionMatrix, out OpenTK.Matrix4 proj);
                    var mvp = mv * proj;
                    GL.UniformMatrix4(uMVP, false, ref mvp);
                }

                if (uAtmosphere != -1)
                {
                    GL.Uniform1(uAtmosphere, 0.3f); // Subtle atmospheric effect
                }

                if (uSunDir != -1)
                {
                    GL.Uniform3(uSunDir, sunDirection.X, sunDirection.Y, sunDirection.Z);
                }

                if (uSunColor != -1)
                {
                    GL.Uniform4(uSunColor, SUN_CORE_COLOR.R, SUN_CORE_COLOR.G, SUN_CORE_COLOR.B, SUN_CORE_COLOR.A);
                }

                if (uSunInfluence != -1)
                {
                    GL.Uniform1(uSunInfluence, 0.5f);
                }

                // Additional scattering uniforms with defaults
                if (uRayleigh != -1) GL.Uniform1(uRayleigh, 0.0025f);
                if (uMie != -1) GL.Uniform1(uMie, 0.0010f);
                if (uMieG != -1) GL.Uniform1(uMieG, 0.76f);
                if (uSunI != -1) GL.Uniform1(uSunI, 20.0f);
                if (uExposure != -1) GL.Uniform1(uExposure, 1.0f);

                // Gamma uniform via reflection
                int ugu = (int)uniMethod.Invoke(prog, new object[] { "gamma" });
                if (ugu != -1) GL.Uniform1(ugu, RenderSettings.Gamma);

                // Bind cloud textures and set cloud uniforms if present
                try
                {
                    int baseUnit = 4;

                    int uCloud0 = (int)uniMethod.Invoke(prog, new object[] { "cloud0" });
                    int uCloud1 = (int)uniMethod.Invoke(prog, new object[] { "cloud1" });
                    int uCloud2 = (int)uniMethod.Invoke(prog, new object[] { "cloud2" });

                    int uCloud0Scale = (int)uniMethod.Invoke(prog, new object[] { "cloud0Scale" });
                    int uCloud1Scale = (int)uniMethod.Invoke(prog, new object[] { "cloud1Scale" });
                    int uCloud2Scale = (int)uniMethod.Invoke(prog, new object[] { "cloud2Scale" });

                    int uCloud0Alpha = (int)uniMethod.Invoke(prog, new object[] { "cloud0Alpha" });
                    int uCloud1Alpha = (int)uniMethod.Invoke(prog, new object[] { "cloud1Alpha" });
                    int uCloud2Alpha = (int)uniMethod.Invoke(prog, new object[] { "cloud2Alpha" });

                    int uCloud0Offset = (int)uniMethod.Invoke(prog, new object[] { "cloud0Offset" });
                    int uCloud1Offset = (int)uniMethod.Invoke(prog, new object[] { "cloud1Offset" });
                    int uCloud2Offset = (int)uniMethod.Invoke(prog, new object[] { "cloud2Offset" });

                    if (cloudTextures != null)
                    {
                        for (int i = 0; i < cloudLayerCount && i < cloudTextures.Length; i++)
                        {
                            int tex = cloudTextures[i];
                            if (tex <= 0) continue;

                            GL.ActiveTexture(TextureUnit.Texture0 + (baseUnit + i));
                            GL.Enable(EnableCap.Texture2D);
                            GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, tex);

                            if (i == 0 && uCloud0 != -1) GL.Uniform1(uCloud0, baseUnit + i);
                            if (i == 1 && uCloud1 != -1) GL.Uniform1(uCloud1, baseUnit + i);
                            if (i == 2 && uCloud2 != -1) GL.Uniform1(uCloud2, baseUnit + i);

                            if (i == 0)
                            {
                                if (uCloud0Scale != -1) GL.Uniform1(uCloud0Scale, cloudScaleFactors[i]);
                                if (uCloud0Alpha != -1) GL.Uniform1(uCloud0Alpha, 0.6f - i * 0.12f);
                                if (uCloud0Offset != -1) GL.Uniform1(uCloud0Offset, cloudRotation[i] / 360.0f);
                            }
                            else if (i == 1)
                            {
                                if (uCloud1Scale != -1) GL.Uniform1(uCloud1Scale, cloudScaleFactors[i]);
                                if (uCloud1Alpha != -1) GL.Uniform1(uCloud1Alpha, 0.6f - i * 0.12f);
                                if (uCloud1Offset != -1) GL.Uniform1(uCloud1Offset, cloudRotation[i] / 360.0f);
                            }
                            else if (i == 2)
                            {
                                if (uCloud2Scale != -1) GL.Uniform1(uCloud2Scale, cloudScaleFactors[i]);
                                if (uCloud2Alpha != -1) GL.Uniform1(uCloud2Alpha, 0.6f - i * 0.12f);
                                if (uCloud2Offset != -1) GL.Uniform1(uCloud2Offset, cloudRotation[i] / 360.0f);
                            }
                        }

                        GL.ActiveTexture(TextureUnit.Texture0);
                    }
                }
                catch { }

                // Render with VBO and shader attributes
                if (RenderSettings.UseVBO && !vboFailed && skyVBO != -1 && skyIndexVBO != -1)
                {
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, skyVBO);
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, skyIndexVBO);

                    int stride = 7 * sizeof(float);
                    GL.EnableVertexAttribArray(aPosition);
                    GL.EnableVertexAttribArray(aColor);

                    GL.VertexAttribPointer(aPosition, 3, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
                    GL.VertexAttribPointer(aColor, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)(3 * sizeof(float)));

                    GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, IntPtr.Zero);

                    GL.DisableVertexAttribArray(aPosition);
                    GL.DisableVertexAttribArray(aColor);

                    Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                }
                else
                {
                    // Shader with client arrays (fallback)
                    GL.EnableVertexAttribArray(aPosition);
                    GL.EnableVertexAttribArray(aColor);

                    unsafe
                    {
                        fixed (float* vPtr = skyVertices)
                        fixed (float* cPtr = skyColors)
                        fixed (ushort* iPtr = skyIndices)
                        {
                            GL.VertexAttribPointer(aPosition, 3, VertexAttribPointerType.Float, false, 0, (IntPtr)vPtr);
                            GL.VertexAttribPointer(aColor, 4, VertexAttribPointerType.Float, false, 0, (IntPtr)cPtr);
                            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, (IntPtr)iPtr);
                        }
                    }

                    GL.DisableVertexAttribArray(aPosition);
                    GL.DisableVertexAttribArray(aColor);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Render using VBO
        /// </summary>
        private void RenderWithVBO()
        {
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.ColorArray);
            
            Compat.BindBuffer(BufferTarget.ArrayBuffer, skyVBO);
            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, skyIndexVBO);
            
            int stride = 7 * sizeof(float);
            GL.VertexPointer(3, VertexPointerType.Float, stride, IntPtr.Zero);
            GL.ColorPointer(4, ColorPointerType.Float, stride, (IntPtr)(3 * sizeof(float)));
            
            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, IntPtr.Zero);
            
            Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.ColorArray);
        }

        /// <summary>
        /// Render using client-side arrays (fallback)
        /// </summary>
        private void RenderWithClientArrays()
        {
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.ColorArray);
            
            GL.VertexPointer(3, VertexPointerType.Float, 0, skyVertices);
            GL.ColorPointer(4, ColorPointerType.Float, 0, skyColors);
            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, skyIndices);
            
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.ColorArray);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Interpolate between two colors
        /// </summary>
        private OpenTK.Graphics.Color4 InterpolateColor(OpenTK.Graphics.Color4 a, OpenTK.Graphics.Color4 b, float t)
        {
            t = Math.Max(0.0f, Math.Min(1.0f, t));
            return new OpenTK.Graphics.Color4(
                a.R + (b.R - a.R) * t,
                a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t,
                a.A + (b.A - a.A) * t
            );
        }
        #endregion
    }
}
