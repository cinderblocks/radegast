/**
 * Radegast Metaverse Client
 * Copyright(c) 2026, Sjofn, LLC
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
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using SkiaSharp;

namespace Radegast.Rendering
{
    /// <summary>
    /// Terrain renderer for adjacent simulators
    /// </summary>
    public class RenderAdjacentTerrain : SceneObject
    {
        private readonly RadegastInstanceForms Instance;
        private GridClient Client => Instance.Client;
        private readonly Simulator targetSim;

        public bool Modified = true;
        private readonly float[,] heightTable = new float[256, 256];
        private Face terrainFace;
        private uint[] terrainIndices;
        private ColorVertex[] terrainVertices;
        private int terrainTexture = -1;
        private bool fetchingTerrainTexture = false;
        private SKBitmap terrainImage = null;
        private int terrainVBO = -1;
        private int terrainIndexVBO = -1;
        private bool terrainVBOFailed = false;
        private bool terrainInProgress = false;
        private bool terrainTextureNeedsUpdate = false;
        private float terrainTimeSinceUpdate = RenderSettings.MinimumTimeBetweenTerrainUpdated + 1f;
        private readonly MeshmerizerR renderer;

        public RenderAdjacentTerrain(RadegastInstanceForms instance, Simulator simulator)
        {
            Instance = instance;
            targetSim = simulator;
            renderer = new MeshmerizerR();
        }

        public void ResetTerrain()
        {
            ResetTerrain(true);
        }

        public void ResetTerrain(bool removeImage)
        {
            if (terrainImage != null)
            {
                terrainImage.Dispose();
                terrainImage = null;
            }

            if (terrainVBO != -1)
            {
                Compat.DeleteBuffer(terrainVBO);
                terrainVBO = -1;
            }

            if (terrainIndexVBO != -1)
            {
                Compat.DeleteBuffer(terrainIndexVBO);
                terrainIndexVBO = -1;
            }

            if (removeImage)
            {
                if (terrainTexture != -1)
                {
                    GL.DeleteTexture(terrainTexture);
                    terrainTexture = -1;
                }
            }

            fetchingTerrainTexture = false;
            Modified = true;
        }

        private void UpdateTerrain()
        {
            if (targetSim == null || targetSim.Terrain == null) return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                int step = 1;

                for (int x = 0; x < 256; x += step)
                {
                    for (int y = 0; y < 256; y += step)
                    {
                        float z = 0;
                        int patchNr = ((int)x / 16) * 16 + (int)y / 16;
                        if (targetSim.Terrain[patchNr] != null
                            && targetSim.Terrain[patchNr].Data != null)
                        {
                            float[] data = targetSim.Terrain[patchNr].Data;
                            z = data[(int)x % 16 * 16 + (int)y % 16];
                        }
                        heightTable[x, y] = z;
                    }
                }

                terrainFace = renderer.TerrainMesh(heightTable, 0f, 255f, 0f, 255f);
                terrainVertices = new ColorVertex[terrainFace.Vertices.Count];
                for (int i = 0; i < terrainFace.Vertices.Count; i++)
                {
                    byte[] part = Utils.IntToBytes(i);
                    terrainVertices[i] = new ColorVertex()
                    {
                        Vertex = terrainFace.Vertices[i],
                        Color = new Color4b()
                        {
                            R = part[0],
                            G = part[1],
                            B = part[2],
                            A = 253 // terrain picking
                        }
                    };
                }
                terrainIndices = new uint[terrainFace.Indices.Count];
                for (int i = 0; i < terrainIndices.Length; i++)
                {
                    terrainIndices[i] = terrainFace.Indices[i];
                }
                terrainInProgress = false;
                Modified = false;
                terrainTextureNeedsUpdate = true;
                terrainTimeSinceUpdate = 0f;
            });
        }

        private void UpdateTerrainTexture()
        {
            if (!fetchingTerrainTexture && targetSim != null)
            {
                fetchingTerrainTexture = true;
                ThreadPool.QueueUserWorkItem(sync =>
                {
                    terrainImage = TerrainSplat.Splat(Instance, heightTable,
                        new UUID[] { targetSim.TerrainDetail0, targetSim.TerrainDetail1, targetSim.TerrainDetail2, targetSim.TerrainDetail3 },
                        new float[] { targetSim.TerrainStartHeight00, targetSim.TerrainStartHeight01, targetSim.TerrainStartHeight10, targetSim.TerrainStartHeight11 },
                        new float[] { targetSim.TerrainHeightRange00, targetSim.TerrainHeightRange01, targetSim.TerrainHeightRange10, targetSim.TerrainHeightRange11 });

                    fetchingTerrainTexture = false;
                    terrainTextureNeedsUpdate = false;
                });
            }
        }

        public override void Render(RenderPass pass, int pickingID, SceneWindow scene, float time)
        {
            terrainTimeSinceUpdate += time;

            if (Modified && terrainTimeSinceUpdate > RenderSettings.MinimumTimeBetweenTerrainUpdated)
            {
                if (!terrainInProgress)
                {
                    terrainInProgress = true;
                    ResetTerrain(false);
                    UpdateTerrain();
                }
            }

            if (terrainTextureNeedsUpdate)
            {
                UpdateTerrainTexture();
            }

            if (terrainIndices == null || terrainVertices == null) return;

            // Save current GL state that we'll modify
            bool wasTextureEnabled = GL.IsEnabled(EnableCap.Texture2D);
            int previousTexture = 0;
            if (wasTextureEnabled)
            {
                GL.GetInteger(GetPName.TextureBinding2D, out previousTexture);
            }

            GL.Color3(1f, 1f, 1f);
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.TextureCoordArray);
            GL.EnableClientState(ArrayCap.NormalArray);
            if (pass == RenderPass.Picking)
            {
                GL.EnableClientState(ArrayCap.ColorArray);
                GL.ShadeModel(ShadingModel.Flat);
            }

            if (terrainImage != null)
            {
                if (terrainTexture != -1)
                {
                    GL.DeleteTexture(terrainTexture);
                }

                terrainTexture = RHelp.GLLoadImage(terrainImage, false);
                terrainImage.Dispose();
                terrainImage = null;
            }

            if (pass != RenderPass.Picking && terrainTexture != -1)
            {
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, terrainTexture);
            }

            if (!RenderSettings.UseVBO || terrainVBOFailed)
            {
                unsafe
                {
                    fixed (float* normalPtr = &terrainVertices[0].Vertex.Normal.X)
                    fixed (float* texPtr = &terrainVertices[0].Vertex.TexCoord.X)
                    fixed (byte* colorPtr = &terrainVertices[0].Color.R)
                    {
                        GL.NormalPointer(NormalPointerType.Float, ColorVertex.Size, (IntPtr)normalPtr);
                        GL.TexCoordPointer(2, TexCoordPointerType.Float, ColorVertex.Size, (IntPtr)texPtr);
                        GL.VertexPointer(3, VertexPointerType.Float, ColorVertex.Size, terrainVertices);
                        if (pass == RenderPass.Picking)
                        {
                            GL.ColorPointer(4, ColorPointerType.UnsignedByte, ColorVertex.Size, (IntPtr)colorPtr);
                        }
                        GL.DrawElements(PrimitiveType.Triangles, terrainIndices.Length, DrawElementsType.UnsignedInt, terrainIndices);
                    }
                }
            }
            else
            {
                if (terrainVBO == -1)
                {
                    Compat.GenBuffers(out terrainVBO);
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, terrainVBO);
                    Compat.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(terrainVertices.Length * ColorVertex.Size), terrainVertices, BufferUsageHint.StaticDraw);
                    if (Compat.BufferSize(BufferTarget.ArrayBuffer) != terrainVertices.Length * ColorVertex.Size)
                    {
                        terrainVBOFailed = true;
                        Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                        terrainVBO = -1;
                    }
                }
                else
                {
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, terrainVBO);
                }

                if (terrainIndexVBO == -1)
                {
                    Compat.GenBuffers(out terrainIndexVBO);
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, terrainIndexVBO);
                    Compat.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(terrainIndices.Length * sizeof(uint)), terrainIndices, BufferUsageHint.StaticDraw);
                    if (Compat.BufferSize(BufferTarget.ElementArrayBuffer) != terrainIndices.Length * sizeof(uint))
                    {
                        terrainVBOFailed = true;
                        Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                        terrainIndexVBO = -1;
                    }
                }
                else
                {
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, terrainIndexVBO);
                }

                if (!terrainVBOFailed)
                {
                    GL.NormalPointer(NormalPointerType.Float, ColorVertex.Size, (IntPtr)12);
                    GL.TexCoordPointer(2, TexCoordPointerType.Float, ColorVertex.Size, (IntPtr)(24));
                    if (pass == RenderPass.Picking)
                    {
                        GL.ColorPointer(4, ColorPointerType.UnsignedByte, ColorVertex.Size, (IntPtr)32);
                    }
                    GL.VertexPointer(3, VertexPointerType.Float, ColorVertex.Size, (IntPtr)(0));

                    GL.DrawElements(PrimitiveType.Triangles, terrainIndices.Length, DrawElementsType.UnsignedInt, IntPtr.Zero);
                }

                Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            }

            // Clean up state before returning
            if (pass == RenderPass.Picking)
            {
                GL.DisableClientState(ArrayCap.ColorArray);
                GL.ShadeModel(ShadingModel.Smooth);
            }
            
            // CRITICAL: Properly restore texture state
            GL.BindTexture(TextureTarget.Texture2D, 0);
            if (!wasTextureEnabled)
            {
                GL.Disable(EnableCap.Texture2D);
            }
            else if (previousTexture != 0)
            {
                GL.BindTexture(TextureTarget.Texture2D, previousTexture);
            }
            
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.TextureCoordArray);
            GL.DisableClientState(ArrayCap.NormalArray);
            
            // Ensure color is reset to white
            GL.Color4(1f, 1f, 1f, 1f);
        }
    }
}
