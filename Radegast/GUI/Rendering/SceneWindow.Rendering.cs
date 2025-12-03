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
//       this software without specific prior permission.
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

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using OpenMetaverse;

namespace Radegast.Rendering
{
    /// <summary>
    /// Avatar and text rendering functionality for SceneWindow
    /// </summary>
    public partial class SceneWindow
    {
        #region Rendering helpers

        private void SetPerspective()
        {
            var dAspRat = (float)glControl.Width / (float)glControl.Height;
            GluPerspective(50.0f * Camera.Zoom, dAspRat, 0.1f, 1000f);
        }

        private void GluPerspective(float fovy, float aspect, float zNear, float zFar)
        {
            var fH = (float)Math.Tan(fovy / 360 * (float)Math.PI) * zNear;
            var fW = fH * aspect;
            GL.Frustum(-fW, fW, -fH, fH, zNear, zFar);
        }

        private void RenderStats()
        {
            // This is a FIR filter known as a MMA or Modified Mean Average, using a 20 point sampling width
            advTimerTick = ((19 * advTimerTick) + lastFrameTime) / 20;
            // Stats in window title for now
            Text =
                $"Scene Viewer: FPS {1d/advTimerTick:000.00} Texture decode queue: {PendingTextures.Count}, Sculpt queue: {PendingTasks.Count}";

#if TURNS_OUT_PRINTER_IS_EXPENISVE
            int posX = glControl.Width - 100;
            int posY = 0;

            Printer.Begin();
            Printer.Print(String.Format("FPS {0:000.00}", 1d / advTimerTick), AvatarTagFont, Color.Orange,
                new RectangleF(posX, posY, 100, 50),
                OpenTK.Graphics.TextPrinterOptions.Default, OpenTK.Graphics.TextAlignment.Center);
            Printer.End();
#endif
        }

        private void RenderText(RenderPass pass)
        {
            lock (VisibleAvatars)
            {
                foreach (var av in VisibleAvatars)
                {
                    var avPos = av.RenderPosition;
                    if (av.DistanceSquared > 400f) continue;

                    byte[] faceColor = null;

                    var tagPos = RHelp.TKVector3(avPos);
                    tagPos.Z += 1.2f;
                    if (!GLU.Project(tagPos, ModelMatrix, ProjectionMatrix, Viewport, 
                            out var screenPos))
                    {
                        continue;
                    }

                    var tagText = instance.Names.Get(av.avatar.ID, av.avatar.Name);
                    if (!string.IsNullOrEmpty(av.avatar.GroupName))
                        tagText = av.avatar.GroupName + "\n" + tagText;

                    var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;
                    var tSize = TextRendering.Measure(tagText, AvatarTagFont, flags);

                    if (pass == RenderPass.Picking)
                    {
                        // Send avatar anyway, we're attached to it
                        var faceID = 0;
                        foreach (var f in av.data)
                        {
                            if (f != null)
                            {
                                var primNrBytes = Utils.Int16ToBytes((short)f.PickingID);
                                faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)faceID, 254 };
                                GL.Color4(faceColor);
                                break;
                            }
                            faceID++;
                        }
                    }

                    var quadPos = screenPos;
                    screenPos.Y = glControl.Height - screenPos.Y;
                    screenPos.X -= tSize.Width / 2;
                    screenPos.Y -= tSize.Height / 2 + 2;

                    if (screenPos.Y > 0)
                    {
                        // Render tag background
                        float halfWidth = tSize.Width / 2 + 12;
                        float halfHeight = tSize.Height / 2 + 5;
                        GL.Color4(0f, 0f, 0f, 0.4f);
                        RHelp.Draw2DBox(quadPos.X - halfWidth, quadPos.Y - halfHeight, halfWidth * 2, halfHeight * 2, screenPos.Z);

                        if (pass == RenderPass.Simple)
                        {
                            textRendering.Begin();
                            var textColor = pass == RenderPass.Simple ?
                                Color.Orange :
                                Color.FromArgb(faceColor[3], faceColor[0], faceColor[1], faceColor[2]);
                            textRendering.Print(tagText, AvatarTagFont, textColor,
                                new Rectangle((int)screenPos.X, (int)screenPos.Y, tSize.Width + 2, tSize.Height + 2),
                                flags);
                            textRendering.End();
                        }
                    }
                }
            }

            lock (SortedObjects)
            {
                var primNr = 0;
                foreach (var prim in SortedObjects.OfType<RenderPrimitive>())
                {
                    primNr++;

                    if (string.IsNullOrEmpty(prim.BasePrim.Text)) { continue; }
                    var text = System.Text.RegularExpressions.Regex.Replace(prim.BasePrim.Text, "(\r?\n)+", "\n");
                    var primPos = RHelp.TKVector3(prim.RenderPosition);

                    // Display hovertext only on objects that are withing 12m of the camera
                    if (prim.DistanceSquared > (12 * 12)) { continue; }

                    primPos.Z += prim.BasePrim.Scale.Z * 0.8f;

                    // Convert objects world position to 2D screen position in pixels
                    if (!GLU.Project(primPos, ModelMatrix, ProjectionMatrix, Viewport,
                            out var screenPos))
                    {
                        continue;
                    }
                    screenPos.Y = glControl.Height - screenPos.Y;

                    textRendering.Begin();

                    var color = Color.FromArgb((int)(prim.BasePrim.TextColor.A * 255), (int)(prim.BasePrim.TextColor.R * 255), (int)(prim.BasePrim.TextColor.G * 255), (int)(prim.BasePrim.TextColor.B * 255));

                    var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;
                    var size = TextRendering.Measure(text, HoverTextFont, flags);
                    screenPos.X -= size.Width / 2;
                    screenPos.Y -= size.Height;

                    if (screenPos.Y + size.Height > 0)
                    {
                        if (pass == RenderPass.Picking)
                        {
                            //Send the prim anyway, we're attached to it
                            var faceID = 0;
                            foreach (var f in prim.Faces)
                            {
                                if (f.UserData != null)
                                {
                                    var primNrBytes = Utils.Int16ToBytes((short)((FaceData)f.UserData).PickingID);
                                    var faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)faceID, 255 };
                                    textRendering.Print(text, HoverTextFont, Color.FromArgb(faceColor[3], faceColor[0], faceColor[1], faceColor[2]), new Rectangle((int)screenPos.X, (int)screenPos.Y, size.Width + 2, size.Height + 2), flags);
                                    break;
                                }
                                faceID++;
                            }
                        }
                        else
                        {
                            // Shadow
                            if (color != Color.Black)
                                textRendering.Print(text, HoverTextFont, Color.Black, new Rectangle((int)screenPos.X + 1, (int)screenPos.Y + 1, size.Width + 2, size.Height + 2), flags);

                            // Text
                            textRendering.Print(text, HoverTextFont, color, new Rectangle((int)screenPos.X, (int)screenPos.Y, size.Width + 2, size.Height + 2), flags);
                        }
                    }

                    textRendering.End();
                }
            }
        }

        #endregion

        #region Avatar management

        private void AddAvatarToScene(Avatar av)
        {
            lock (Avatars)
            {
                if (Avatars.ContainsKey(av.LocalID))
                {
                    // flag we got an update??
                    UpdateAVtes(Avatars[av.LocalID]);
                    Avatars[av.LocalID].glavatar.morph(av);
                    UpdateAvatarAnimations(Avatars[av.LocalID]);
                }
                else
                {
                    var ga = new GLAvatar();

                    //ga.morph(av);
                    var ra = new RenderAvatar {avatar = av, glavatar = ga};
                    UpdateAVtes(ra);
                    Avatars.Add(av.LocalID, ra);
                    ra.glavatar.morph(av);

                    if (av.LocalID == Client.Self.LocalID)
                    {
                        myself = ra;
                    }

                    UpdateAvatarAnimations(ra);
                }
            }
        }

        private void UpdateAvatarAnimations(RenderAvatar av)
        {
            if (av.avatar.Animations == null) return;

            av.glavatar.skel.flushanimations();
            foreach (var anim in av.avatar.Animations)
            {
                //Console.WriteLine(string.Format("AvatarAnimationChanged {0} {1}", anim.AnimationID, anim.AnimationSequence));

                // Don't play internal turn 180 animations
                if (anim.AnimationID == new UUID("038fcec9-5ebd-8a8e-0e2e-6e71a0a1ac53"))
                    continue;

                if (anim.AnimationID == new UUID("6883a61a-b27b-5914-a61e-dda118a9ee2c"))
                    continue;

                av.glavatar.skel.processAnimation(anim.AnimationID);

                if (AssetFetchFailCount.TryGetValue(anim.AnimationID, out var nofails))
                {
                    if (nofails >= 5)
                        continue; // asset fetch has failed 5 times, give up.
                }

                var tid = UUID.Random();
                skeleton.mAnimationTransactions.Add(tid, av);

                if (skeleton.mAnimationCache.TryGetValue(anim.AnimationID, out var bvh))
                {
                    try
                    {
                        skeleton.addanimation(null, tid, bvh, anim.AnimationID);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failure in skel.addanimation: {ex.Message}", ex);
                    }
                    continue;
                }

                Logger.Trace($"Requesting new animation asset {anim.AnimationID}");

                Client.Assets.RequestAsset(anim.AnimationID, AssetType.Animation, false, SourceType.Asset, tid, AnimReceivedCallback);
            }

            av.glavatar.skel.flushanimationsfinal();
            skeleton.recalcpriorities(av);

        }

        private void UpdateAVtes(RenderAvatar ra)
        {
            if (ra.avatar.Textures == null)
                return;


            foreach (var fi in RenderAvatar.BakedTextures.Keys)
            {
                var TEF = ra.avatar.Textures.FaceTextures[fi];
                if (TEF == null)
                    continue;

                if (ra.data[fi] == null || ra.data[fi].TextureInfo.TextureID != TEF.TextureID || ra.data[fi].TextureInfo.TexturePointer < 1)
                {
                    var data = new FaceData();
                    ra.data[fi] = data;
                    data.TextureInfo.TextureID = TEF.TextureID;

                    var type = ImageType.Baked;
                    if (ra.avatar.COFVersion > 0) // This avatar was server baked
                    {
                        type = ImageType.ServerBaked;
                    }

                    DownloadTexture(new TextureLoadItem()
                    {
                        Data = data,
                        Prim = ra.avatar,
                        TeFace = ra.avatar.Textures.FaceTextures[fi],
                        ImageType = type,
                        BakeName = RenderAvatar.BakedTextures[fi],
                        AvatarID = ra.avatar.ID
                    }, true);
                }
            }
        }

        #endregion

        #region Avatar rendering

        private void RenderAvatarsSkeleton(RenderPass pass)
        {
            if (!RenderSettings.RenderAvatarSkeleton) return;

            lock (Avatars)
            {
                foreach (var av in Avatars.Values)
                {
                    // Individual prim matrix
                    GL.PushMatrix();

                    // Prim rotation and position
                    //Vector3 pos = av.avatar.Position;

                    var avataroffset = av.glavatar.skel.getOffset("mPelvis");
                    avataroffset.X += 1.0f;

                    GL.MultMatrix(Math3D.CreateSRTMatrix(Vector3.One, av.RenderRotation, av.RenderPosition - avataroffset * av.RenderRotation));

                    GL.Begin(PrimitiveType.Lines);

                    GL.Color3(1.0, 0.0, 0.0);

                    foreach (var b in av.glavatar.skel.mBones.Values)
                    {
                        var newpos = b.getTotalOffset();

                        if (b.parent != null)
                        {
                            var parentpos = b.parent.getTotalOffset();
                            GL.Vertex3(parentpos.X, parentpos.Y, parentpos.Z);
                        }
                        else
                        {
                            GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        }

                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        //Mark the joints


                        newpos.X += 0.01f;
                        newpos.Y += 0.01f;
                        newpos.Z += 0.01f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.X -= 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.Y -= 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.X += 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.Y += 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.Z -= 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.Y -= 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.X -= 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.Y += 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.X += 0.02f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                        newpos.Y -= 0.01f;
                        newpos.Z += 0.01f;
                        newpos.X -= 0.01f;
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);



                    }

                    GL.Color3(0.0, 1.0, 0.0);

                    GL.End();

                    GL.PopMatrix();
                }
            }
        }

        private void RenderAvatars(RenderPass pass)
        {
            if (!RenderSettings.AvatarRenderingEnabled) return;

            lock (Avatars)
            {
                GL.EnableClientState(ArrayCap.VertexArray);
                GL.EnableClientState(ArrayCap.TextureCoordArray);
                GL.EnableClientState(ArrayCap.NormalArray);

                // Enable avatar shader if available and not in picking mode
                bool useShader = false;
                if (pass != RenderPass.Picking && RenderSettings.HasShaders && RenderSettings.AvatarRenderingEnabled)
                {
                    useShader = true;
                    StartAvatarShader();
                }

                var avatarNr = 0;
                foreach (var av in VisibleAvatars)
                {
                    avatarNr++;

                    // Whole avatar position
                    GL.PushMatrix();

                    // Prim rotation and position
                    av.UpdateSize();
                    GL.MultMatrix(Math3D.CreateSRTMatrix(Vector3.One, av.RenderRotation, av.AdjustedPosition(av.RenderPosition)));

                    // Update shader matrices after setting modelview for this avatar
                    if (useShader)
                    {
                        UpdateShaderMatrices();
                    }

                    if (av.glavatar._meshes.Count > 0)
                    {
                        var faceNr = 0;
                        foreach (var mesh in av.glavatar._meshes.Values)
                        {
                            if (av.glavatar.skel.mNeedsMeshRebuild)
                            {
                                mesh.applyjointweights();
                            }

                            faceNr++;
                            if (!av.glavatar._showSkirt && mesh.Name == "skirtMesh")
                                continue;


                            // If we don't have a hair bake OR the hair bake is invisible don't render it
                            if (mesh.Name == "hairMesh" &&
                                (av.data[(int) AvatarTextureIndex.HairBaked] == null
                                 || av.data[(int) AvatarTextureIndex.HairBaked].TextureInfo.IsInvisible))
                            {
                                continue;
                            }

                            GL.Color3(1f, 1f, 1f);

                            if (pass == RenderPass.Picking)
                            {
                                GL.Disable(EnableCap.Texture2D);

                                foreach (var d in av.data)
                                {
                                    if (d != null)
                                    {
                                        d.PickingID = avatarNr;
                                    }
                                }
                                var primNrBytes = Utils.Int16ToBytes((short)avatarNr);
                                var faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)faceNr, 254 };
                                GL.Color4(faceColor);
                            }
                            else
                            {
                                if (av.data[mesh.teFaceID] == null)
                                {
                                    GL.Disable(EnableCap.Texture2D);
                                    if (useShader)
                                    {
                                        SetShaderHasTexture(false);
                                    }
                                }
                                else
                                {
                                    if (mesh.teFaceID != 0)
                                    {
                                        GL.Enable(EnableCap.Texture2D);
                                        GL.ActiveTexture(TextureUnit.Texture0);
                                        GL.BindTexture(TextureTarget.Texture2D, av.data[mesh.teFaceID].TextureInfo.TexturePointer);
                                        if (useShader)
                                        {
                                            SetShaderHasTexture(true);
                                        }
                                    }
                                    else
                                    {
                                        GL.Disable(EnableCap.Texture2D);
                                        if (useShader)
                                        {
                                            SetShaderHasTexture(false);
                                        }
                                    }
                                }
                            }

                            // Render the mesh
                            bool usedShaderPath = false;
                            if (RenderSettings.UseVBO)
                            {
                                try
                                {
                                    if (mesh.VertexVBO == -1 && mesh.IndexVBO == -1)
                                    {
                                        mesh.PrepareVBO();
                                    }

                                    if (mesh.VertexVBO != -1 && mesh.IndexVBO != -1 && !mesh.VBOFailed)
                                    {
                                        // Update VBO with animated vertex data each frame
                                        try
                                        {
                                            var numVerts = mesh.RenderData.Vertices.Length / 3;
                                            var interleaved = new float[numVerts * 8];
                                            for (int i = 0, vi = 0; i < numVerts; i++)
                                            {
                                                // Animated position from RenderData.Vertices (updated by applyjointweights)
                                                interleaved[vi++] = mesh.RenderData.Vertices[i * 3];
                                                interleaved[vi++] = mesh.RenderData.Vertices[i * 3 + 1];
                                                interleaved[vi++] = mesh.RenderData.Vertices[i * 3 + 2];
                                                // Animated normal from MorphRenderData.Normals
                                                interleaved[vi++] = mesh.MorphRenderData.Normals[i * 3];
                                                interleaved[vi++] = mesh.MorphRenderData.Normals[i * 3 + 1];
                                                interleaved[vi++] = mesh.MorphRenderData.Normals[i * 3 + 2];
                                                // Texture coords (static)
                                                interleaved[vi++] = mesh.RenderData.TexCoords[i * 2];
                                                interleaved[vi++] = mesh.RenderData.TexCoords[i * 2 + 1];
                                            }

                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, mesh.VertexVBO);
                                            Compat.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(interleaved.Length * sizeof(float)), interleaved, BufferUsageHint.StreamDraw);
                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                                        }
                                        catch
                                        {
                                            // If VBO update fails, fall back to non-VBO rendering
                                            mesh.VBOFailed = true;
                                        }
                                    }

                                    if (mesh.VertexVBO != -1 && mesh.IndexVBO != -1 && !mesh.VBOFailed)
                                    {
                                        // Try shader attribute path for avatars
                                        if (useShader && pass != RenderPass.Picking)
                                        {
                                            try
                                            {
                                                var prog = shaderManager?.GetProgram("avatar");
                                                if (prog != null)
                                                {
                                                    var posLoc = prog.Attr("aPosition");
                                                    var normLoc = prog.Attr("aNormal");
                                                    var texLoc = prog.Attr("aTexCoord");

                                                    if (posLoc != -1 && normLoc != -1 && texLoc != -1)
                                                    {
                                                        // Verify shader program is active
                                                        GL.GetInteger(GetPName.CurrentProgram, out int activeProgram);
                                                        if (activeProgram != 0)
                                                        {
                                                            // --- Shader Path for Avatars ---
                                                            
                                                            // Disable fixed-function client states
                                                            GL.DisableClientState(ArrayCap.VertexArray);
                                                            GL.DisableClientState(ArrayCap.NormalArray);
                                                            GL.DisableClientState(ArrayCap.TextureCoordArray);

                                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, mesh.VertexVBO);
                                                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.IndexVBO);

                                                            GL.EnableVertexAttribArray(posLoc);
                                                            GL.EnableVertexAttribArray(normLoc);
                                                            GL.EnableVertexAttribArray(texLoc);

                                                            GL.VertexAttribPointer(posLoc, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 0);
                                                            GL.VertexAttribPointer(normLoc, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
                                                            GL.VertexAttribPointer(texLoc, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), 6 * sizeof(float));

                                                            GL.DrawElements(PrimitiveType.Triangles, mesh.RenderData.Indices.Length, DrawElementsType.UnsignedShort, IntPtr.Zero);

                                                            GL.DisableVertexAttribArray(posLoc);
                                                            GL.DisableVertexAttribArray(normLoc);
                                                            GL.DisableVertexAttribArray(texLoc);

                                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                                                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

                                                            // Re-enable fixed-function states for subsequent rendering
                                                            GL.EnableClientState(ArrayCap.VertexArray);
                                                            GL.EnableClientState(ArrayCap.NormalArray);
                                                            GL.EnableClientState(ArrayCap.TextureCoordArray);

                                                            var err = GL.GetError();
                                                            usedShaderPath = (err == ErrorCode.NoError);
                                                            if (!usedShaderPath)
                                                            {
                                                                System.Diagnostics.Debug.WriteLine($"GL error after avatar shader draw: {err}");
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"Avatar shader rendering failed: {ex.Message}");
                                            }
                                        }

                                        if (!usedShaderPath)
                                        {
                                            // Fixed-function VBO path with interleaved data
                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, mesh.VertexVBO);
                                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.IndexVBO);

                                            GL.VertexPointer(3, VertexPointerType.Float, 8 * sizeof(float), IntPtr.Zero);
                                            GL.NormalPointer(NormalPointerType.Float, 8 * sizeof(float), (IntPtr)(3 * sizeof(float)));
                                            GL.TexCoordPointer(2, TexCoordPointerType.Float, 8 * sizeof(float), (IntPtr)(6 * sizeof(float)));

                                            GL.DrawElements(PrimitiveType.Triangles, mesh.RenderData.Indices.Length, DrawElementsType.UnsignedShort, IntPtr.Zero);

                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

                                            usedShaderPath = true; // Mark as handled
                                        }
                                    }
                                }
                                catch { usedShaderPath = false; }
                            }

                            if (!usedShaderPath)
                            {
                                // Fallback to client-side arrays
                                GL.TexCoordPointer(2, TexCoordPointerType.Float, 0, mesh.RenderData.TexCoords);
                                GL.VertexPointer(3, VertexPointerType.Float, 0, mesh.RenderData.Vertices);
                                GL.NormalPointer(NormalPointerType.Float, 0, mesh.MorphRenderData.Normals);

                                GL.DrawElements(PrimitiveType.Triangles, mesh.RenderData.Indices.Length, DrawElementsType.UnsignedShort, mesh.RenderData.Indices);
                            }
                        }

                        av.glavatar.skel.mNeedsMeshRebuild = false;
                    }

                    // Whole avatar position
                    GL.PopMatrix();
                }

                // Stop avatar shader if it was enabled
                if (useShader)
                {
                    StopAvatarShader();
                }

                GL.Disable(EnableCap.Texture2D);
                GL.DisableClientState(ArrayCap.NormalArray);
                GL.DisableClientState(ArrayCap.VertexArray);
                GL.DisableClientState(ArrayCap.TextureCoordArray);

            }
        }

        #endregion
    }
}
