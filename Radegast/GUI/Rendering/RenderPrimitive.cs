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
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using SkiaSharp;

namespace Radegast.Rendering
{
    /// <summary>
    /// Contains per primitive face data
    /// </summary>
    public class FaceData : IDisposable
    {
        public float[] Vertices;
        public ushort[] Indices;
        public float[] TexCoords;
        public float[] Normals;
        public int PickingID = -1;
        public int VertexVBO = -1;
        public int IndexVBO = -1;
        public int Vao = -1;
        public TextureInfo TextureInfo = new TextureInfo();
        public BoundingVolume BoundingVolume = new BoundingVolume();
        public static int VertexSize = 32; // sizeof (vertex), 2  x vector3 + 1 x vector2 = 8 floats x 4 bytes = 32 bytes 
        public TextureAnimationInfo AnimInfo;
        public int QueryID = 0;
        public bool VBOFailed = false;

        /// <summary>
        /// Dispose VBOs if we have them in graphics card memory
        /// </summary>
        public void Dispose()
        {
            if (VertexVBO != -1) { try { Compat.DeleteBuffer(VertexVBO); } catch { } }
            if (IndexVBO != -1) { try { Compat.DeleteBuffer(IndexVBO); } catch { } }
            if (Vao != -1) { try { Compat.DeleteVertexArray(Vao); } catch { } }
            VertexVBO = -1;
            IndexVBO = -1;
            Vao = -1;
        }

        /// <summary>
        /// Checks if VBOs are created, if they are, bind them, if not create new
        /// </summary>
        /// <param name="face">Which face's mesh is uploaded in this VBO</param>
        /// <returns>True, if face data was successfully uploaded to the graphics card memory</returns>
        public bool CheckVBO(Face face)
        {
            if (VertexVBO == -1)
            {
                Vertex[] vArray = face.Vertices.ToArray();
                Compat.GenBuffers(out VertexVBO);
                Compat.BindBuffer(BufferTarget.ArrayBuffer, VertexVBO);
                Compat.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vArray.Length * VertexSize), vArray, BufferUsageHint.StaticDraw);
                if (Compat.BufferSize(BufferTarget.ArrayBuffer) != vArray.Length * VertexSize)
                {
                    VBOFailed = true;
                    Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                    Compat.DeleteBuffer(VertexVBO);
                    VertexVBO = -1;
                    return false;
                }
                Compat.BindBuffer(BufferTarget.ArrayBuffer, 0); // Unbind after creation
            }

            if (IndexVBO == -1)
            {
                ushort[] iArray = face.Indices.ToArray();
                Compat.GenBuffers(out IndexVBO);
                Compat.BindBuffer(BufferTarget.ElementArrayBuffer, IndexVBO);
                Compat.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(iArray.Length * sizeof(ushort)), iArray, BufferUsageHint.StaticDraw);
                if (Compat.BufferSize(BufferTarget.ElementArrayBuffer) != iArray.Length * sizeof(ushort))
                {
                    VBOFailed = true;
                    Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                    Compat.DeleteBuffer(IndexVBO);
                    IndexVBO = -1;
                    return false;
                }
                Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0); // Unbind after creation
            }

            return true;
        }

        // Helper helpers for binding/unbinding VAO and buffers for draw time
        public bool UsesVao => Vao != -1;

        public void BindVao()
        {
            if (Vao != -1) Compat.BindVertexArray(Vao);
        }

        public void UnbindVao()
        {
            if (Vao != -1) Compat.BindVertexArray(0);
        }

        public void BindBuffers()
        {
            Compat.BindBuffer(BufferTarget.ArrayBuffer, VertexVBO);
            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, IndexVBO);
        }

        public void UnbindBuffers()
        {
            Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        }
    }

    /// <summary>
    /// Class handling texture animations
    /// </summary>
    public class TextureAnimationInfo
    {
        public Primitive.TextureAnimation PrimAnimInfo;
        public float CurrentFrame;
        public float CurrentTime;
        public bool PingPong;
        private float LastTime = 0f;
        private float TotalTime = 0f;

        /// <summary>
        /// Perform texture manipulation to implement texture animations
        /// </summary>
        /// <param name="lastFrameTime">Time passed since the last run (in seconds)</param>
        public void Step(float lastFrameTime)
        {
            float numFrames = 1f;
            float fullLength = 1f;

            numFrames = PrimAnimInfo.Length > 0 ? PrimAnimInfo.Length : Math.Max(1f, PrimAnimInfo.SizeX * PrimAnimInfo.SizeY);

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.PING_PONG) != 0)
            {
                if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SMOOTH) != 0)
                {
                    fullLength = 2f * numFrames;
                }
                else if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.LOOP) != 0)
                {
                    fullLength = 2f * numFrames - 2f;
                    fullLength = Math.Max(1f, fullLength);
                }
                else
                {
                    fullLength = 2f * numFrames - 1f;
                    fullLength = Math.Max(1f, fullLength);
                }
            }
            else
            {
                fullLength = numFrames;
            }

            float frameCounter;
            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SMOOTH) != 0)
            {
                frameCounter = lastFrameTime * PrimAnimInfo.Rate + LastTime;
            }
            else
            {
                TotalTime += lastFrameTime;
                frameCounter = TotalTime * PrimAnimInfo.Rate;
            }
            LastTime = frameCounter;

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.LOOP) != 0)
            {
                frameCounter %= fullLength;
            }
            else
            {
                frameCounter = Math.Min(fullLength - 1f, frameCounter);
            }

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SMOOTH) == 0)
            {
                frameCounter = (float)Math.Floor(frameCounter + 0.01f);
            }

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.PING_PONG) != 0)
            {
                if (frameCounter > numFrames)
                {
                    if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SMOOTH) != 0)
                    {
                        frameCounter = numFrames - (frameCounter - numFrames);
                    }
                    else
                    {
                        frameCounter = (numFrames - 1.99f) - (frameCounter - numFrames);
                    }
                }
            }

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.REVERSE) != 0)
            {
                if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SMOOTH) != 0)
                {
                    frameCounter = numFrames - frameCounter;
                }
                else
                {
                    frameCounter = (numFrames - 0.99f) - frameCounter;
                }
            }

            frameCounter += PrimAnimInfo.Start;

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SMOOTH) == 0)
            {
                frameCounter = (float)Math.Round(frameCounter);
            }


            GL.MatrixMode(MatrixMode.Texture);
            GL.LoadIdentity();

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.ROTATE) != 0)
            {
                GL.Translate(0.5f, 0.5f, 0f);
                GL.Rotate(Utils.RAD_TO_DEG * frameCounter, OpenTK.Vector3d.UnitZ);
                GL.Translate(-0.5f, -0.5f, 0f);
            }
            else if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SCALE) != 0)
            {
                GL.Scale(frameCounter, frameCounter, 0);
            }
            else // Translate
            {
                float sizeX = Math.Max(1f, (float)PrimAnimInfo.SizeX);
                float sizeY = Math.Max(1f, (float)PrimAnimInfo.SizeY);

                GL.Scale(1f / sizeX, 1f / sizeY, 0);
                GL.Translate(frameCounter % sizeX, Math.Floor(frameCounter / sizeY), 0);
            }

            GL.MatrixMode(MatrixMode.Modelview);
        }

        [Obsolete("Use Step() instead")]
        public void ExperimentalStep(float time)
        {
            int reverseFactor = 1;
            float rate = PrimAnimInfo.Rate;

            if (rate < 0)
            {
                rate = -rate;
                reverseFactor = -reverseFactor;
            }

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.REVERSE) != 0)
            {
                reverseFactor = -reverseFactor;
            }

            CurrentTime += time;
            float totalTime = 1 / rate;

            uint x = Math.Max(1, PrimAnimInfo.SizeX);
            uint y = Math.Max(1, PrimAnimInfo.SizeY);
            uint nrFrames = x * y;

            if (PrimAnimInfo.Length > 0 && PrimAnimInfo.Length < nrFrames)
            {
                nrFrames = (uint)PrimAnimInfo.Length;
            }

            GL.MatrixMode(MatrixMode.Texture);
            GL.LoadIdentity();

            if (CurrentTime >= totalTime)
            {
                CurrentTime = 0;
                CurrentFrame++;
                if (CurrentFrame > nrFrames) CurrentFrame = (uint)PrimAnimInfo.Start;
                if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.PING_PONG) != 0)
                {
                    PingPong = !PingPong;
                }
            }

            float smoothOffset = 0f;

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.SMOOTH) != 0)
            {
                smoothOffset = (CurrentTime / totalTime) * reverseFactor;
            }

            float f = CurrentFrame;
            if (reverseFactor < 0)
            {
                f = nrFrames - CurrentFrame;
            }

            if ((PrimAnimInfo.Flags & Primitive.TextureAnimMode.ROTATE) == 0) // not rotating
            {
                GL.Scale(1f / x, 1f / y, 0f);
                GL.Translate((f % x) + smoothOffset, f / y, 0);
            }
            else
            {
                smoothOffset = (CurrentTime * PrimAnimInfo.Rate);
                float startAngle = PrimAnimInfo.Start;
                float endAngle = PrimAnimInfo.Length;
                float angle = startAngle + (endAngle - startAngle) * smoothOffset;
                GL.Translate(0.5f, 0.5f, 0f);
                GL.Rotate(Utils.RAD_TO_DEG * angle, OpenTK.Vector3d.UnitZ);
                GL.Translate(-0.5f, -0.5f, 0f);
            }

            GL.MatrixMode(MatrixMode.Modelview);
        }

    }

    /// <summary>
    /// Class that handle rendering of objects: simple primitives, sculpties, and meshes
    /// </summary>
    public class RenderPrimitive : SceneObject
    {
        #region Public fields
        /// <summary>Base simulator object</summary>
        public Primitive Prim;
        public List<Face> Faces;
        /// <summary>Is this object attached to an avatar</summary>
        public bool Attached;
        /// <summary>Do we know if object is attached</summary>
        public bool AttachedStateKnown;
        /// <summary>Are meshes constructed and ready for this prim</summary>
        public bool Meshed;
        /// <summary>Process of creating a mesh is underway</summary>
        public bool Meshing;
        #endregion Public fields

        #region Private fields

        private int prevTEHash;
        private int prevSculptHash;
        private int prevShapeHash;
        #endregion Private fields

        /// <summary>
        /// Default constructor
        /// </summary>
        public RenderPrimitive()
        {
            Type = SceneObjectType.Primitive;
        }

        /// <summary>
        /// Remove any GL resource we may still have in use
        /// </summary>
        public override void Dispose()
        {
            if (Faces != null)
            {
                foreach (Face f in Faces)
                {
                    if (f.UserData is FaceData data)
                    {
                        data.Dispose();
                        data = null;
                    }
                }
                Faces = null;
            }
            base.Dispose();
        }

        /// <summary>
        /// Simulator object that is basis for this SceneObject
        /// </summary>
        public override Primitive BasePrim
        {
            get => Prim;
            set
            {
                Prim = value;

                int TEHash = Prim.Textures?.GetHashCode() ?? 0;

                if (Meshed)
                {
                    if (Prim.Type == PrimType.Sculpt || Prim.Type == PrimType.Mesh)
                    {
                        var sculptHash = Prim.Sculpt.GetHashCode();
                        if (Prim.Sculpt.GetHashCode() != prevSculptHash || TEHash != prevTEHash)
                        {
                            Meshed = false;
                        }
                        prevSculptHash = sculptHash;
                    }
                    else
                    {
                        var shapeHash = Prim.PrimData.GetHashCode();
                        if (shapeHash != prevShapeHash)
                        {
                            Meshed = false;
                        }
                        else if (TEHash != prevTEHash)
                        {
                            Meshed = false;
                        }
                        prevShapeHash = shapeHash;
                    }
                }
                prevTEHash = TEHash;
            }
        }

        /// <summary>
        /// Set initial state of the object
        /// </summary>
        public override void Initialize()
        {
            AttachedStateKnown = false;
            base.Initialize();
        }

        /// <summary>
        /// Render Primitive
        /// </summary>
        /// <param name="pass">Which pass are we currently in</param>
        /// <param name="pickingID">ID used to identify which object was picked</param>
        /// <param name="scene">Main scene renderer</param>
        /// <param name="time">Time it took to render the last frame</param>
        public override void Render(RenderPass pass, int pickingID, SceneWindow scene, float time)
        {
            if (!RenderSettings.AvatarRenderingEnabled && Attached) return;

            // Don't render if not yet meshed
            if (!Meshed || Faces == null || Faces.Count == 0) return;

            // Individual prim matrix
            GL.PushMatrix();

            // Prim rotation and position and scale
            GL.MultMatrix(Math3D.CreateSRTMatrix(Prim.Scale, RenderRotation, RenderPosition));

            // If scene supports shader uniform updates, call it
            try
            {
                if (scene is SceneWindow sw)
                {
                    sw.UpdateShaderMatrices();
                }
            }
            catch { }

            // Do we have animated texture on this face
            bool animatedTexture = false;

            // Track whether we're currently in shader mode to minimize state changes
            bool inShaderMode = false;

            // Initialise flags tracking what type of faces this prim has
            if (pass == RenderPass.Simple)
            {
                HasSimpleFaces = false;
            }
            else if (pass == RenderPass.Alpha)
            {
                HasAlphaFaces = false;
            }
            else if (pass == RenderPass.Invisible)
            {
                HasInvisibleFaces = false;
            }

            // Draw the prim faces
            for (int j = 0; j < Faces.Count; j++)
            {
                Primitive.TextureEntryFace teFace = Prim.Textures.GetFace((uint)j);
                Face face = Faces[j];
                FaceData data = (FaceData)face.UserData;

                if (data == null)
                    continue;

                if (teFace == null)
                    continue;

                // Don't render transparent faces
                Color4 RGBA = teFace.RGBA;

                if (data.TextureInfo.FullAlpha || RGBA.A <= 0.01f) continue;

                bool switchedLightsOff = false;
                float shiny = 0f;  // Declare here so it's available throughout the face rendering

                if (pass == RenderPass.Picking)
                {
                    data.PickingID = pickingID;
                    var primNrBytes = Utils.UInt16ToBytes((ushort)pickingID);
                    var faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)j, 255 };
                    GL.Color4(faceColor);
                }
                else if (pass == RenderPass.Invisible)
                {
                    if (!data.TextureInfo.IsInvisible) continue;
                    HasInvisibleFaces = true;
                }
                else
                {
                    if (data.TextureInfo.IsInvisible) continue;
                    bool belongToAlphaPass = (RGBA.A < 0.99f) || (data.TextureInfo.HasAlpha && !data.TextureInfo.IsMask);

                    if (belongToAlphaPass && pass != RenderPass.Alpha) continue;
                    if (!belongToAlphaPass && pass == RenderPass.Alpha) continue;

                    if (pass == RenderPass.Simple)
                    {
                        HasSimpleFaces = true;
                    }
                    else if (pass == RenderPass.Alpha)
                    {
                        HasAlphaFaces = true;
                    }

                    if (teFace.Fullbright)
                    {
                        GL.Disable(EnableCap.Lighting);
                        switchedLightsOff = true;
                    }

                    switch (teFace.Shiny)
                    {
                        case Shininess.High:
                            shiny = 0.96f;
                            break;

                        case Shininess.Medium:
                            shiny = 0.64f;
                            break;

                        case Shininess.Low:
                            shiny = 0.24f;
                            break;
                    }

                    // Use shaders for shiny materials (both world prims and attachments)
                    if (shiny > 0f)
                    {
                        scene.StartShiny();
                    }
                    else
                    {
                        // No shader start for non-shiny faces. Don't stop shader here —
                        // stopping mid-prim can cause flicker between shaded and
                        // fixed-function rendering for adjacent faces.
                    }
                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shiny);
                    var faceColor = new float[] { RGBA.R, RGBA.G, RGBA.B, RGBA.A };
                    GL.Color4(faceColor);

                    GL.Material(MaterialFace.Front, MaterialParameter.Specular, new float[] { 0.5f, 0.5f, 0.5f, 1f });

                    if (data.TextureInfo.TexturePointer == 0)
                    {
                        TextureInfo teInfo;
                        if (scene.TryGetTextureInfo(teFace.TextureID, out teInfo))
                        {
                            data.TextureInfo = teInfo;
                        }
                    }

                    if (data.TextureInfo.TexturePointer == 0)
                    {
                        GL.Disable(EnableCap.Texture2D);
                        if (!data.TextureInfo.FetchFailed)
                        {
                            scene.DownloadTexture(new TextureLoadItem()
                            {
                                Prim = Prim,
                                TeFace = teFace,
                                Data = data
                            }, false);
                        }
                    }
                    else
                    {
                        GL.Enable(EnableCap.Texture2D);
                        // Ensure texture unit 0 is active for shader sampling
                        GL.ActiveTexture(TextureUnit.Texture0);
                        GL.BindTexture(TextureTarget.Texture2D, data.TextureInfo.TexturePointer);
                        
                        // Check if this face uses texture animation
                        if ((Prim.TextureAnim.Flags & Primitive.TextureAnimMode.ANIM_ON) != 0
                            && (Prim.TextureAnim.Face == j || Prim.TextureAnim.Face == 255))
                        {
                            if (data.AnimInfo == null)
                            {
                                data.AnimInfo = new TextureAnimationInfo();
                            }
                            data.AnimInfo.PrimAnimInfo = Prim.TextureAnim;
                            animatedTexture = true;
                        }
                        else
                        {
                            data.AnimInfo = null;
                            animatedTexture = false;
                        }
                    }
                }

                if (!RenderSettings.UseVBO || data.VBOFailed)
                {
                    Vertex[] verts = face.Vertices.ToArray();
                    ushort[] indices = face.Indices.ToArray();

                    // Guard against empty geometry which would cause verts[0] access to throw
                    if (verts == null || verts.Length == 0 || indices == null || indices.Length == 0)
                        continue;

                    // Apply texture animation for non-VBO path (fixed-function only)
                    if (animatedTexture && data.AnimInfo != null)
                    {
                        GL.MatrixMode(MatrixMode.Texture);
                        GL.PushMatrix();
                        data.AnimInfo.Step(time);
                    }

                    unsafe
                    {
                        fixed (float* normalPtr = &verts[0].Normal.X)
                        fixed (float* texPtr = &verts[0].TexCoord.X)
                        {
                            GL.NormalPointer(NormalPointerType.Float, FaceData.VertexSize, (IntPtr)normalPtr);
                            GL.TexCoordPointer(2, TexCoordPointerType.Float, FaceData.VertexSize, (IntPtr)texPtr);
                            GL.VertexPointer(3, VertexPointerType.Float, FaceData.VertexSize, verts);
                            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedShort, indices);
                        }
                    }

                    // Restore texture matrix if animation was applied
                    if (animatedTexture && data.AnimInfo != null)
                    {
                        GL.MatrixMode(MatrixMode.Texture);
                        GL.PopMatrix();
                        GL.MatrixMode(MatrixMode.Modelview);
                    }
                }
                else
                {
                    // Skip faces with no geometry to avoid VBO creation/draw errors
                    if (face.Vertices == null || face.Vertices.Count == 0 || face.Indices == null || face.Indices.Count == 0)
                        continue;

                    if (data.CheckVBO(face))
                    {
                        // Determine if we should use shader attribute path
                        bool wantShaderPath = pass != RenderPass.Picking && shiny > 0f && RenderSettings.HasShaders && RenderSettings.EnableShiny;
                        
                        if (wantShaderPath)
                        {
                            var sw = scene as SceneWindow;
                            if (sw != null)
                            {
                                try
                                {
                                    var posLoc = sw.GetShaderAttr("aPosition");
                                    var normLoc = sw.GetShaderAttr("aNormal");
                                    var texLoc = sw.GetShaderAttr("aTexCoord");
                                    
                                    // If any of the required attributes are missing, fall back to fixed-function
                                    if (posLoc == -1 || normLoc == -1 || texLoc == -1)
                                    {
                                        // Stop shader and fall back to fixed-function path
                                        try { sw?.StopShiny(); } catch { }
                                        wantShaderPath = false;
                                    }
                                    else
                                    {
                                        // Proceed only if all attribute locations are present
                                        if (posLoc != -1 && normLoc != -1 && texLoc != -1)
                                        {
                                            // Verify shader program is active
                                            GL.GetInteger(GetPName.CurrentProgram, out int activeProgram);
                                            if (activeProgram != 0)
                                            {
                                                // --- Shader Path ---
                                                // Switch to shader mode if not already in it
                                                if (!inShaderMode)
                                                {
                                                    GL.DisableClientState(ArrayCap.VertexArray);
                                                    GL.DisableClientState(ArrayCap.NormalArray);
                                                    GL.DisableClientState(ArrayCap.TextureCoordArray);
                                                    inShaderMode = true;
                                                }
                                                
                                                // Pass face material color to shader
                                                sw.SetShaderMaterialColor(RGBA);
                                                sw.SetShaderHasTexture(data.TextureInfo.TexturePointer != 0);

                                                // If the TE has material properties, set the material layer uniforms
                                                try
                                                {
                                                    // Use the known TextureEntryFace fields instead of reflection
                                                    // - teFace.material (byte) encodes bump/shiny/fullbright
                                                    // - teFace.MaterialID (UUID) references a material texture/asset when present
                                                    bool hasMat = teFace.MaterialID != UUID.Zero;
                                                    var matSpec = new OpenTK.Vector3(0.5f, 0.5f, 0.5f);
                                                    float matSh = 24.0f;
                                                    float matStr = 1.0f;

                                                    // Map the Shiny enum to a shader shininess value
                                                    switch (teFace.Shiny)
                                                    {
                                                        case Shininess.High:
                                                            matSh = 94.0f;
                                                            break;
                                                        case Shininess.Medium:
                                                            matSh = 64.0f;
                                                            break;
                                                        case Shininess.Low:
                                                            matSh = 24.0f;
                                                            break;
                                                        default:
                                                            matSh = 24.0f;
                                                            break;
                                                    }

                                                    if (hasMat)
                                                    {
                                                        // Try to use the material asset as a texture: if already downloaded, sample it to derive specular info
                                                        try
                                                        {
                                                            TextureInfo matTexInfo;
                                                            if (sw.TryGetTextureInfo(teFace.MaterialID, out matTexInfo) && matTexInfo != null && matTexInfo.TexturePointer != 0 && matTexInfo.Texture != null)
                                                            {
                                                                SKBitmap bmp = matTexInfo.Texture;
                                                                if (bmp.Width > 0 && bmp.Height > 0)
                                                                {
                                                                    // Sample center pixel as a cheap approximation of specular color
                                                                    var px = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
                                                                    float r = px.Red / 255f;
                                                                    float g = px.Green / 255f;
                                                                    float b = px.Blue / 255f;
                                                                    matSpec = new OpenTK.Vector3(r, g, b);
                                                                    // Strength = average brightness
                                                                    matStr = (r + g + b) / 3f;
                                                                }
                                                            }
                                                            else
                                                            {
                                                                // Not available yet: request download of the material texture by creating a temporary TE with TextureID = MaterialID
                                                                try
                                                                {
                                                                    var tempFace = (Primitive.TextureEntryFace)teFace.Clone();
                                                                    tempFace.TextureID = teFace.MaterialID;
                                                                    sw.DownloadTexture(new TextureLoadItem()
                                                                    {
                                                                        Prim = Prim,
                                                                        TeFace = tempFace,
                                                                        Data = data
                                                                    }, false);
                                                                }
                                                                catch { }
                                                            }
                                                        }
                                                        catch { }

                                                        sw.SetShaderMaterialLayer(true, matSpec, matSh, matStr);
                                                    }
                                                    else
                                                    {
                                                        sw.SetShaderMaterialLayer(false, matSpec, matSh, matStr);
                                                    }
                                                 }
                                                 catch { }

                                                 // Set per-face glow if available
                                                 try
                                                 {
                                                    float faceGlow = 0f;
                                                    try { faceGlow = teFace.Glow; } catch { faceGlow = 0f; }
                                                    sw.SetShaderGlow(faceGlow);
                                                 }
                                                 catch { }

                                                // Bind the vertex and index buffers
                                                Compat.BindBuffer(BufferTarget.ArrayBuffer, data.VertexVBO);
                                                Compat.BindBuffer(BufferTarget.ElementArrayBuffer, data.IndexVBO);

                                                // Set up vertex attribute pointers
                                                GL.EnableVertexAttribArray(posLoc);
                                                GL.EnableVertexAttribArray(normLoc);
                                                GL.EnableVertexAttribArray(texLoc);

                                                GL.VertexAttribPointer(posLoc, 3, VertexAttribPointerType.Float, false, FaceData.VertexSize, 0);
                                                GL.VertexAttribPointer(normLoc, 3, VertexAttribPointerType.Float, false, FaceData.VertexSize, 12);
                                                GL.VertexAttribPointer(texLoc, 2, VertexAttribPointerType.Float, false, FaceData.VertexSize, 24);

                                                // Draw with the index buffer still bound
                                                GL.DrawElements(PrimitiveType.Triangles, face.Indices.Count, DrawElementsType.UnsignedShort, IntPtr.Zero);

                                                // Disable vertex attributes
                                                GL.DisableVertexAttribArray(posLoc);
                                                GL.DisableVertexAttribArray(normLoc);
                                                GL.DisableVertexAttribArray(texLoc);

                                                // Unbind buffers after drawing
                                                Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                                                Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                                                

                                                continue; // Skip fixed-function path
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Shader rendering failed for prim {Prim.LocalID} face {j}: {ex.Message}");
                                }
                            }
                        }

                        // --- Fixed-Function Path ---
                        // Switch back to fixed-function mode if we were in shader mode
                        if (inShaderMode)
                        {
                            GL.EnableClientState(ArrayCap.VertexArray);
                            GL.EnableClientState(ArrayCap.NormalArray);
                            GL.EnableClientState(ArrayCap.TextureCoordArray);
                            inShaderMode = false;
                        }
                        
                        // Do not call StopShiny() here; shader stop is handled once
                        // after the prim rendering to avoid toggling the program
                        // between faces which produces visual flicker.
                        
                        // Apply texture animation for fixed-function VBO path
                        if (animatedTexture && data.AnimInfo != null)
                        {
                            GL.MatrixMode(MatrixMode.Texture);
                            GL.PushMatrix();
                            data.AnimInfo.Step(time);
                        }
                        
                        // Use VAO if available (non-shader path)
                        if (data.UsesVao)
                        {
                            data.BindVao();
                            GL.DrawElements(PrimitiveType.Triangles, face.Indices.Count, DrawElementsType.UnsignedShort, IntPtr.Zero);
                            data.UnbindVao();
                        }
                        else
                        {
                            Compat.BindBuffer(BufferTarget.ArrayBuffer, data.VertexVBO);
                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, data.IndexVBO);
                            
                            GL.NormalPointer(NormalPointerType.Float, FaceData.VertexSize, (IntPtr)12);
                            GL.TexCoordPointer(2, TexCoordPointerType.Float, FaceData.VertexSize, (IntPtr)24);
                            GL.VertexPointer(3, VertexPointerType.Float, FaceData.VertexSize, (IntPtr)0);

                            GL.DrawElements(PrimitiveType.Triangles, face.Indices.Count, DrawElementsType.UnsignedShort, IntPtr.Zero);

                            Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                        }
                        
                        // Restore texture matrix if animation was applied
                        if (animatedTexture && data.AnimInfo != null)
                        {
                            GL.MatrixMode(MatrixMode.Texture);
                            GL.PopMatrix();
                            GL.MatrixMode(MatrixMode.Modelview);
                        }
                    }
                }

                if (switchedLightsOff)
                {
                    GL.Enable(EnableCap.Lighting);
                    switchedLightsOff = false;
                }

            }

            // Restore fixed-function state if we ended in shader mode
            if (inShaderMode)
            {
                GL.EnableClientState(ArrayCap.VertexArray);
                GL.EnableClientState(ArrayCap.NormalArray);
                GL.EnableClientState(ArrayCap.TextureCoordArray);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
            RHelp.ResetMaterial();

            // Ensure shader is stopped at the end of rendering this prim
            if (pass != RenderPass.Picking && RenderSettings.HasShaders && RenderSettings.EnableShiny)
            {
                scene.StopShiny();
            }

            // Pop the prim matrix
            GL.PopMatrix();

            base.Render(pass, pickingID, scene, time);
        }

        /// <summary>
        /// String representation of the object
        /// </summary>
        /// <returns>String containing local ID of the object and it's distance from the camera</returns>
        public override string ToString()
        {
            uint id = Prim == null ? 0 : Prim.LocalID;
            float distance = (float)Math.Sqrt(DistanceSquared);
            return $"LocalID: {id}, distance {distance:0.00}";
        }
    }
}
