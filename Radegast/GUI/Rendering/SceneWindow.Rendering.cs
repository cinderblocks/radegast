// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// Copyright (c) 2019-2026, Sjofn LLC
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
using SkiaSharp;

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

                    // Calculate tag position in world space
                    // Use avatar height if available, otherwise use default
                    float avatarHeight = av.Height > 0 ? av.Height : 2.0f;
                    
                    // Position tag well above the head - add extra offset
                    // Avatar height is measured from feet, so we need to go above that
                    var tagWorldPos = avPos + new Vector3(0, 0, avatarHeight * 0.65f + 0.3f); // 65% of height plus 30cm above
                    
                    var tagPos = RHelp.TKVector3(tagWorldPos);
                    
                    if (!GLU.Project(tagPos, ModelMatrix, ProjectionMatrix, Viewport, 
                            out var screenPos))
                    {
                        continue;
                    }

                    // Get avatar name - try instance.Names first, fallback to avatar.Name
                    var tagText = instance.Names.Get(av.avatar.ID, av.avatar.Name);
                    if (string.IsNullOrEmpty(tagText) || tagText.Trim().Length == 0)
                    {
                        // Fallback to the avatar's name property
                        tagText = av.avatar.Name ?? "Loading...";
                    }
                    
                    // Add group name above if present
                    if (!string.IsNullOrEmpty(av.avatar.GroupName))
                        tagText = av.avatar.GroupName + "\n" + tagText;

                    // Add status indicators on a separate line below the name
                    string statusIcons = GetAvatarStatusIcons(av.avatar);
                    if (!string.IsNullOrEmpty(statusIcons))
                    {
                        tagText = tagText + "\n" + statusIcons;
                    }

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
                        // Calculate distance-based alpha fade for name tags
                        float distanceFactor = 1.0f;
                        if (av.DistanceSquared > 100f) // Start fading beyond 10 meters
                        {
                            float distance = (float)Math.Sqrt(av.DistanceSquared);
                            distanceFactor = Math.Max(0.3f, 1.0f - ((distance - 10f) / 10f)); // Fade from 10m to 20m
                        }
                        
                        // Render tag background with subtle gradient
                        float halfWidth = tSize.Width / 2 + 12;
                        float halfHeight = tSize.Height / 2 + 5;
                        
                        // Ensure blending is enabled for semi-transparent background
                        GL.Enable(EnableCap.Blend);
                        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                        
                        // Choose background color based on avatar status
                        var bgColor = GetAvatarTagBackgroundColor(av.avatar);
                        
                        // Apply distance fade to background
                        byte fadedAlpha = (byte)(bgColor.Alpha * distanceFactor);
                        GL.Color4(bgColor.Red / 255f, bgColor.Green / 255f, bgColor.Blue / 255f, fadedAlpha / 255f);
                        
                        RHelp.DrawRounded2DBox(quadPos.X - halfWidth, quadPos.Y - halfHeight, halfWidth * 2, halfHeight * 2, 8f, screenPos.Z);

                        if (pass == RenderPass.Simple)
                        {
                            textRendering.Begin();
                            
                            // Choose text color based on avatar status
                            var textColor = GetAvatarTagTextColor(av.avatar);
                            
                            // Apply distance fade to text color
                            textColor = new SKColor(textColor.Red, textColor.Green, textColor.Blue, (byte)(255 * distanceFactor));
                            
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
                                    textRendering.Print(text, HoverTextFont, new SKColor(faceColor[0], faceColor[1], faceColor[2], faceColor[3]), new Rectangle((int)screenPos.X, (int)screenPos.Y, size.Width + 2, size.Height + 2), flags);
                                    break;
                                }
                                faceID++;
                            }
                        }
                        else
                        {
                            // Shadow
                            if (color != Color.Black)
                                textRendering.Print(text, HoverTextFont, new SKColor(0, 0, 0, 128), new Rectangle((int)screenPos.X + 1, (int)screenPos.Y + 1, size.Width + 2, size.Height + 2), flags);

                            // Text
                            textRendering.Print(text, HoverTextFont, new SKColor(color.R, color.G, color.B, color.A), new Rectangle((int)screenPos.X, (int)screenPos.Y, size.Width + 2, size.Height + 2), flags);
                        }
                    }

                    textRendering.End();
                }
            }
        }

        /// <summary>
        /// Get status icon string for an avatar based on their current state
        /// </summary>
        private string GetAvatarStatusIcons(Avatar avatar)
        {
            var icons = new System.Collections.Generic.List<string>();
            
            // Check if this is a friend (show first as most important)
            if (Client.Friends.FriendList.ContainsKey(avatar.ID))
            {
                icons.Add("[Friend]");
            }
            
            // Check if avatar is sitting
            if (avatar.ParentID != 0)
            {
                icons.Add("[Sitting]");
            }
            
            // Check if avatar is flying
            if (avatar.Animations != null)
            {
                var flyAnim = new UUID("aec4610c-757f-bc4e-c092-c6e9caf18daf");
                if (avatar.Animations.Any(a => a.AnimationID == flyAnim))
                {
                    icons.Add("[Flying]");
                }
                
                // Check for AFK animation
                var afkAnim = new UUID("fd037134-85d4-f241-72c6-4f42164fedee");
                if (avatar.Animations.Any(a => a.AnimationID == afkAnim))
                {
                    icons.Add("[Away]");
                }
                
                // Check for busy animation (typing on a keyboard, etc.)
                var busyAnim = new UUID("efcf670c-2d18-8128-973a-034ebc806b67");
                if (avatar.Animations.Any(a => a.AnimationID == busyAnim))
                {
                    icons.Add("[Busy]");
                }
            }
            
            return icons.Count > 0 ? string.Join(" ", icons) : "";
        }

        /// <summary>
        /// Get background color for avatar name tag based on status
        /// </summary>
        private SKColor GetAvatarTagBackgroundColor(Avatar avatar)
        {
            // Default: semi-transparent black
            byte alpha = 180;
            
            // Highlight friends with a slightly different tint
            if (Client.Friends.FriendList.ContainsKey(avatar.ID))
            {
                return new SKColor(0, 40, 80, alpha); // Dark blue tint for friends
            }
            
            // Highlight yourself
            if (avatar.ID == Client.Self.AgentID)
            {
                return new SKColor(0, 80, 0, alpha); // Dark green for self
            }
            
            return new SKColor(0, 0, 0, alpha); // Standard black
        }

        /// <summary>
        /// Get text color for avatar name tag based on status
        /// </summary>
        private SKColor GetAvatarTagTextColor(Avatar avatar)
        {
            // Friends - warm yellow
            if (Client.Friends.FriendList.ContainsKey(avatar.ID))
            {
                return new SKColor(255, 220, 100); // Warm yellow
            }
            
            // Yourself - bright green
            if (avatar.ID == Client.Self.AgentID)
            {
                return new SKColor(100, 255, 100); // Bright green
            }
            
            // Default - standard yellow
            return SKColors.Yellow;
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
                    
                    // Set avatar-specific shader parameters once at the start
                    var prog = shaderManager?.GetProgram("avatar");
                    if (prog != null)
                    {
                        // Set default avatar rendering parameters
                        var uGlowStr = prog.Uni("glowStrength");
                        if (uGlowStr != -1) GL.Uniform1(uGlowStr, 1.0f);
                        
                        var uEmissive = prog.Uni("emissiveStrength");
                        if (uEmissive != -1) GL.Uniform1(uEmissive, 1.0f);
                        
                        // Avatars use softer specular highlighting
                        var uShininess = prog.Uni("shininessExp");
                        if (uShininess != -1) GL.Uniform1(uShininess, 16.0f); // Softer than prims
                        
                        var uSpecStr = prog.Uni("specularStrength");
                        if (uSpecStr != -1) GL.Uniform1(uSpecStr, 0.5f); // Less shiny than prims
                        
                        // No glow by default for avatars
                        var uGlow = prog.Uni("glow");
                        if (uGlow != -1) GL.Uniform1(uGlow, 0.0f);
                    }
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
                        UpdateAvatarShaderMatrices();
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
                                // Get the baked texture for this mesh part
                                bool hasTexture = false;
                                int texturePointer = 0;
                                
                                // Try to get the appropriate baked texture
                                if (mesh.teFaceID >= 0 && mesh.teFaceID < av.data.Length && av.data[mesh.teFaceID] != null)
                                {
                                    texturePointer = av.data[mesh.teFaceID].TextureInfo.TexturePointer;
                                    hasTexture = texturePointer > 0 && !av.data[mesh.teFaceID].TextureInfo.IsInvisible;
                                }
                                
                                // Bind texture for both shader and fixed-function
                                if (hasTexture)
                                {
                                    GL.Enable(EnableCap.Texture2D);
                                    GL.ActiveTexture(TextureUnit.Texture0);
                                    GL.BindTexture(TextureTarget.Texture2D, texturePointer);
                                    
                                    // Set texture parameters for better quality
                                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                                }
                                else
                                {
                                    // No texture - render with a default gray color
                                    GL.Disable(EnableCap.Texture2D);
                                    GL.Color3(0.7f, 0.7f, 0.7f);
                                }
                                
                                // Set hasTexture uniform for shader
                                if (useShader)
                                {
                                    SetShaderHasTexture(hasTexture);
                                    
                                    // Set material color to white so texture shows through properly
                                    var prog = shaderManager?.GetProgram("avatar");
                                    if (prog != null)
                                    {
                                        var uMatColor = prog.Uni("materialColor");
                                        if (uMatColor != -1)
                                        {
                                            GL.Uniform4(uMatColor, 1.0f, 1.0f, 1.0f, 1.0f);
                                        }
                                        
                                        // Ensure colorMap sampler points to texture unit 0
                                        var uColorMap = prog.Uni("colorMap");
                                        if (uColorMap != -1)
                                        {
                                            GL.Uniform1(uColorMap, 0);
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
                                    // Prepare VBO once if not already done
                                    if (mesh.VertexVBO == -1 && mesh.IndexVBO == -1)
                                    {
                                        mesh.PrepareVBO();
                                    }

                                    if (mesh.VertexVBO != -1 && mesh.IndexVBO != -1 && !mesh.VBOFailed)
                                    {
                                        // Update VBO with animated vertex data each frame
                                        // Only update if skeleton has changed (optimization)
                                        if (av.glavatar.skel.mNeedsMeshRebuild)
                                        {
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

                                        if (!mesh.VBOFailed)
                                        {
                                            // Bind VBOs once
                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, mesh.VertexVBO);
                                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.IndexVBO);

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
                                                GL.VertexPointer(3, VertexPointerType.Float, 8 * sizeof(float), IntPtr.Zero);
                                                GL.NormalPointer(NormalPointerType.Float, 8 * sizeof(float), (IntPtr)(3 * sizeof(float)));
                                                GL.TexCoordPointer(2, TexCoordPointerType.Float, 8 * sizeof(float), (IntPtr)(6 * sizeof(float)));

                                                GL.DrawElements(PrimitiveType.Triangles, mesh.RenderData.Indices.Length, DrawElementsType.UnsignedShort, IntPtr.Zero);

                                                usedShaderPath = true; // Mark as handled
                                            }

                                            // Unbind VBOs once
                                            Compat.BindBuffer(BufferTarget.ArrayBuffer, 0);
                                            Compat.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
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
