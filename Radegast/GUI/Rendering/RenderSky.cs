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
                // Different speeds and scales per layer for parallax effect
                cloudRotation[i] = (float)(cloudRand.NextDouble() * 360.0);
                cloudSpeed[i] = (float)((cloudRand.NextDouble() * 2.0 + 0.5) * (cloudRand.Next(0, 2) == 0 ? 1.0 : -1.0));
                cloudScaleFactors[i] = 1.0f + (float)i * 0.3f;
                cloudHeights[i] = 100.0f + i * 40.0f;

                // Create cloud texture directly using GL calls for reliability
                try
                {
                    cloudTextures[i] = CreateCloudTexture(i);
                }
                catch
                {
                    cloudTextures[i] = 0;
                }
            }
        }

        /// <summary>
        /// Create a single cloud texture layer
        /// </summary>
        private int CreateCloudTexture(int layerIndex)
        {
            int size = cloudTextureSize;
            byte[] pixels = new byte[size * size * 4]; // RGBA

            // Initialize to fully transparent
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;     // R - white
                pixels[i + 1] = 255; // G - white
                pixels[i + 2] = 255; // B - white
                pixels[i + 3] = 0;   // A - transparent
            }

            // Draw cloud puffs - simple circles with soft edges
            int numPuffs = 60 + layerIndex * 20;
            for (int p = 0; p < numPuffs; p++)
            {
                int cx = cloudRand.Next(size);
                int cy = cloudRand.Next(size);
                int radius = cloudRand.Next(30, 80 + layerIndex * 20);
                float maxAlpha = 0.3f + (float)cloudRand.NextDouble() * 0.5f; // 0.3 to 0.8

                // Draw soft circle
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx;
                        int py = cy + dy;

                        // Wrap coordinates for tiling
                        px = ((px % size) + size) % size;
                        py = ((py % size) + size) % size;

                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist < radius)
                        {
                            // Soft falloff from center
                            float falloff = 1.0f - (dist / radius);
                            falloff = falloff * falloff; // Quadratic for softer edges
                            float alpha = maxAlpha * falloff;

                            int idx = (py * size + px) * 4;
                            
                            // Alpha blend with existing
                            float existingAlpha = pixels[idx + 3] / 255.0f;
                            float newAlpha = alpha + existingAlpha * (1.0f - alpha);
                            
                            // Clamp and store
                            pixels[idx + 3] = (byte)Math.Min(255, (int)(newAlpha * 255));
                        }
                    }
                }
            }

            // Upload to OpenGL
            int texId;
            GL.GenTextures(1, out texId);
            GL.BindTexture(TextureTarget.Texture2D, texId);

            // Set texture parameters for proper alpha blending
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            // Upload pixel data
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                size, size, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            return texId;
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

        /// <summary>
        /// Render using shader (currently not implemented - returns false to use fixed-function)
        /// </summary>
        private bool RenderWithShader(object prog)
        {
            // Sky shader not implemented yet, return false to use fixed-function fallback
            return false;
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

            // Save current GL state that we will modify
            bool wasDepthWriteEnabled = GL.GetBoolean(GetPName.DepthWritemask);
            bool wasBlendEnabled = GL.IsEnabled(EnableCap.Blend);
            bool wasTextureEnabled = GL.IsEnabled(EnableCap.Texture2D);
            bool wasColorMaterialEnabled = GL.IsEnabled(EnableCap.ColorMaterial);
            bool wasCullFaceEnabled = GL.IsEnabled(EnableCap.CullFace);
            bool wasDepthTestEnabled = GL.IsEnabled(EnableCap.DepthTest);
            
            // ABSOLUTELY CRITICAL: Configure GL state for sky rendering with vertex colors
            GL.DepthMask(false); // Don't write to depth buffer (sky is always behind everything)
            GL.DepthFunc(DepthFunction.Lequal);

            // Disable depth test to avoid far-plane clipping of the sky dome
            GL.Disable(EnableCap.DepthTest);
            
            // Disable face culling so we see the sky from inside the dome
            GL.Disable(EnableCap.CullFace);
            
            // IMPORTANT: Sky should always render solid, never wireframe
            // Save and override polygon mode to ensure sky is always solid
            int[] polygonMode = new int[2];
            GL.GetInteger(GetPName.PolygonMode, polygonMode);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            
            // Disable everything that could interfere with vertex colors
            GL.Disable(EnableCap.Lighting);      // No lighting calculations
            GL.Disable(EnableCap.Texture2D);     // No textures on sky dome itself
            GL.UseProgram(0);                     // No shaders - use fixed function
            
            // Enable alpha blending for smooth colors
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            
            // CRITICAL: Enable color material so vertex colors are actually used
            GL.Enable(EnableCap.ColorMaterial);
            GL.ColorMaterial(MaterialFace.FrontAndBack, ColorMaterialParameter.AmbientAndDiffuse);
            
            // Set material to white so it doesn't tint vertex colors
            float[] white = { 1.0f, 1.0f, 1.0f, 1.0f };
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Ambient, white);
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Diffuse, white);
            GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
            
            // Use smooth shading for gradient
            GL.ShadeModel(ShadingModel.Smooth);
            
            GL.PushMatrix();
            
            // Center sky dome on camera
            GL.Translate(cameraPosition.X, cameraPosition.Y, cameraPosition.Z);

            // Scale dome to just inside camera draw distance to avoid far-plane clipping
            try
            {
                float effectiveFar = 1000f;
                try { effectiveFar = Math.Max(1000f, (float)scene.DrawDistance); } catch { }
                float effectiveRadius = Math.Max(1000f, effectiveFar * 0.95f);
                float scale = effectiveRadius / SKY_RADIUS;
                if (scale > 0f && Math.Abs(scale - 1.0f) > 1e-6f)
                {
                    GL.Scale(scale, scale, scale);
                }
            }
            catch { }
            
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

                // Render cloud layers
                if (cloudTextures != null && cloudHeights != null)
                {
                    GL.Enable(EnableCap.Texture2D);
                    GL.EnableClientState(ArrayCap.TextureCoordArray);
                    
                    for (int i = 0; i < cloudLayerCount; i++)
                    {
                        int tex = i < cloudTextures.Length ? cloudTextures[i] : 0;
                        if (tex <= 0) continue;

                        GL.PushMatrix();
                        GL.Translate(0f, 0f, cloudHeights[i]);
                        GL.Rotate(cloudRotation[i], 0f, 0f, 1f);
                        
                        float size = SKY_RADIUS * 1.5f * cloudScaleFactors[i];
                        float cloudAlpha = 0.8f - i * 0.15f;
                        
                        GL.BindTexture(TextureTarget.Texture2D, tex);
                        GL.Color4(1f, 1f, 1f, cloudAlpha);

                        GL.Begin(PrimitiveType.Quads);
                        GL.TexCoord2(0f, 0f); GL.Vertex3(-size, -size, 0f);
                        GL.TexCoord2(2f, 0f); GL.Vertex3(size, -size, 0f);
                        GL.TexCoord2(2f, 2f); GL.Vertex3(size, size, 0f);
                        GL.TexCoord2(0f, 2f); GL.Vertex3(-size, size, 0f);
                        GL.End();

                        GL.BindTexture(TextureTarget.Texture2D, 0);
                        GL.PopMatrix();
                    }
                    
                    GL.DisableClientState(ArrayCap.TextureCoordArray);
                    GL.Disable(EnableCap.Texture2D);
                    GL.Color4(1f, 1f, 1f, 1f);
                }
            }
            
            GL.PopMatrix();
            
            // Restore GL state
            GL.DepthMask(wasDepthWriteEnabled);
            GL.DepthFunc(DepthFunction.Less);

            if (wasDepthTestEnabled)
                GL.Enable(EnableCap.DepthTest);
            else
                GL.Disable(EnableCap.DepthTest);
            
            // Restore polygon mode to what it was before (might be wireframe)
            GL.PolygonMode(MaterialFace.FrontAndBack, (PolygonMode)polygonMode[0]);
            
            if (wasCullFaceEnabled)
                GL.Enable(EnableCap.CullFace);
            else
                GL.Disable(EnableCap.CullFace);
            
            if (!wasBlendEnabled)
                GL.Disable(EnableCap.Blend);
            
            if (wasTextureEnabled)
                GL.Enable(EnableCap.Texture2D);
            else
                GL.Disable(EnableCap.Texture2D);
                
            if (!wasColorMaterialEnabled)
                GL.Disable(EnableCap.ColorMaterial);
                
            GL.Color4(1f, 1f, 1f, 1f);
        }
        #endregion Rendering

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
