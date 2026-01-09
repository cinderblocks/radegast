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
using System.Reflection;
using System.Threading;
using CoreJ2K;
using CoreJ2K.Util;
using OpenTK.Graphics;
using OpenTK.Platform;
using OpenMetaverse;
using OpenMetaverse.Assets;
using OpenMetaverse.Imaging;
using SkiaSharp;

namespace Radegast.Rendering
{
    /// <summary>
    /// Texture management functionality for SceneWindow
    /// </summary>
    public partial class SceneWindow
    {
        #region Texture constants
        public static readonly UUID invisi1 = new UUID("38b86f85-2575-52a9-a531-23108d8da837");
        public static readonly UUID invisi2 = new UUID("e97cf410-8e61-7005-ec06-629eba4cd1fb");
        #endregion

        #region Texture thread

        private bool TextureThreadRunning = true;

        private void TextureThread()
        {
            // Signal that the texture thread started
            TextureThreadContextReady.Set();

            Logger.DebugLog("Started Texture Thread");

            try
            {
                // Try to obtain the GLControl's internal IWindowInfo via reflection and create a shared GraphicsContext on this thread
                object winInfoObj = null;
                var t = glControl.GetType();
                var f = t.GetField("windowInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? t.GetField("m_windowInfo", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null)
                {
                    winInfoObj = f.GetValue(glControl);
                }

                if (winInfoObj == null)
                {
                    foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                    {
                        if (typeof(IWindowInfo).IsAssignableFrom(fi.FieldType))
                        {
                            winInfoObj = fi.GetValue(glControl);
                            if (winInfoObj != null) break;
                        }
                    }
                }

                sharedWindowInfo = winInfoObj as IWindowInfo;

                if (sharedWindowInfo != null)
                {
                    // Use reflection to find a compatible GraphicsContext constructor and create the worker context that shares with the GLControl
                    var gcType = typeof(GraphicsContext);
                    var ctors = gcType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    object created = null;
                    foreach (var ctor in ctors)
                    {
                        var pars = ctor.GetParameters();
                        var args = new object[pars.Length];
                        var ok = true;
                        var intPadValue = 0;
                        for (var i = 0; i < pars.Length; i++)
                        {
                            var pType = pars[i].ParameterType;
                            if (pType == typeof(GraphicsMode) || pType.FullName.Contains("GraphicsMode"))
                            {
                                args[i] = GLMode ?? new GraphicsMode();
                            }
                            else if (typeof(IWindowInfo).IsAssignableFrom(pType))
                            {
                                args[i] = sharedWindowInfo;
                            }
                            else if (pType == typeof(int))
                            {
                                // supply major/minor or placeholder
                                if (intPadValue == 0) { args[i] = 3; intPadValue++; }
                                else { args[i] = 0; }
                            }
                            else if (pType.FullName.Contains("GraphicsContextFlags"))
                            {
                                args[i] = GraphicsContextFlags.Default;
                            }
                            else if (typeof(IGraphicsContext).IsAssignableFrom(pType) || pType.FullName.Contains("IGraphicsContext"))
                            {
                                args[i] = glControl.Context;
                            }
                            else
                            {
                                ok = false;
                                break;
                            }
                        }

                        if (!ok) continue;

                        try
                        {
                            created = ctor.Invoke(args);
                            if (created != null) break;
                        }
                        catch
                        {
                            created = null;
                        }
                    }

                    textureContext = created as IGraphicsContext;
                    if (textureContext == null)
                    {
                        Logger.Debug("No compatible GraphicsContext constructor found or creation failed on texture thread");
                    }
                    else
                    {
                        try
                        {
                            textureContext.MakeCurrent(sharedWindowInfo);
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug("Failed to make worker context current: " + ex.Message, ex);
                            try { textureContext.MakeCurrent(null); } catch { }
                            textureContext = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("Unexpected exception creating worker context: " + ex.Message, ex);
                textureContext = null;
                sharedWindowInfo = null;
            }

            while (TextureThreadRunning)
            {
                try
                {
                    PendingTexturesAvailable.Wait(cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!TextureThreadRunning) { break; }

                if (!PendingTextures.TryDequeue(out var item)) { continue; }

                // Already have this one loaded
                if (item.Data.TextureInfo.TexturePointer != 0) { continue; }

                ProcessTextureItem(item);
            }

            // Cleanup worker context
            try { if (textureContext != null) { try { textureContext.MakeCurrent(null); } catch { } try { textureContext.Dispose(); } catch { } textureContext = null; } } catch { }

            TextureThreadContextReady.Set();
            Logger.DebugLog("Texture thread exited");
        }

        private void ProcessTextureItem(TextureLoadItem item)
        {
            byte[] imageBytes = null;
            SKBitmap skBitmap = null;
            InterleavedImage j2kImage = null;

            if (item.TextureData != null || item.LoadAssetFromCache)
            {
                if (item.LoadAssetFromCache)
                {
                    item.TextureData = Client.Assets.Cache.GetCachedAssetBytes(item.Data.TextureInfo.TextureID);
                }
                if (item.TextureData == null) { return; }

                try
                {
                    j2kImage = J2kImage.FromBytes(item.TextureData);
                }
                catch
                {
                    j2kImage = null;
                }

                if (j2kImage != null)
                {
                    var mi = new ManagedImage(j2kImage);

                    var hasAlpha = false;
                    var fullAlpha = false;
                    var isMask = false;
                    if ((mi.Channels & ManagedImage.ImageChannels.Alpha) != 0)
                    {
                        fullAlpha = true;
                        isMask = true;

                        foreach (var b in mi.Alpha)
                        {
                            if (b < 255) hasAlpha = true;
                            if (b != 0) fullAlpha = false;
                            if (b != 0 && b != 255) isMask = false;
                        }
                    }

                    item.Data.TextureInfo.HasAlpha = hasAlpha;
                    item.Data.TextureInfo.FullAlpha = fullAlpha;
                    item.Data.TextureInfo.IsMask = isMask;

                    imageBytes = item.TextureData;

                    try { skBitmap = j2kImage.As<SKBitmap>(); } catch { skBitmap = null; }

                    if (CacheDecodedTextures)
                    {
                        RHelp.SaveCachedImage(imageBytes, item.TeFace.TextureID, hasAlpha, fullAlpha, isMask);
                    }
                }
            }

            if (imageBytes != null)
            {
                if (skBitmap == null)
                {
                    try { skBitmap = j2kImage.As<SKBitmap>(); } catch { skBitmap = null; }
                }

                if (skBitmap != null)
                {
                    // Flip vertically for OpenGL
                    var flipped = new SKBitmap(skBitmap.Width, skBitmap.Height, skBitmap.ColorType, skBitmap.AlphaType);
                    using (var canvas = new SKCanvas(flipped))
                    {
                        canvas.Scale(1, -1, 0, skBitmap.Height / 2f);
                        canvas.DrawBitmap(skBitmap, 0, 0);
                    }

                    try { skBitmap.Dispose(); } catch { }

                    UploadTexture(item, flipped);
                }
            }

            item.TextureData = null;
            imageBytes = null;
        }

        private void UploadTexture(TextureLoadItem item, SKBitmap bitmap)
        {
            if (textureContext != null)
            {
                try
                {
                    textureContext.MakeCurrent(sharedWindowInfo);
                    item.Data.TextureInfo.TexturePointer = RHelp.GLLoadImage(bitmap, item.Data.TextureInfo.HasAlpha);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Texture thread GL upload failed, falling back to UI thread: {ex.Message}", ex, Client);
                    if (instance.MainForm.IsHandleCreated)
                    {
                        ThreadingHelper.SafeInvoke(instance.MainForm, new Action(() =>
                        {
                            item.Data.TextureInfo.TexturePointer = RHelp.GLLoadImage(bitmap, item.Data.TextureInfo.HasAlpha);
                            try { bitmap.Dispose(); } catch { }
                        }), instance.MonoRuntime);
                    }
                    else
                    {
                        try { bitmap.Dispose(); } catch { }
                    }
                }
                finally
                {
                    try { bitmap.Dispose(); } catch { }
                }
            }
            else
            {
                if (instance.MainForm.IsHandleCreated)
                {
                    ThreadingHelper.SafeInvoke(instance.MainForm, new Action(() =>
                    {
                        item.Data.TextureInfo.TexturePointer = RHelp.GLLoadImage(bitmap, item.Data.TextureInfo.HasAlpha);
                        try { bitmap.Dispose(); } catch { }
                    }), instance.MonoRuntime);
                }
                else
                {
                    try { bitmap.Dispose(); } catch { }
                }
            }
        }

        #endregion Texture thread

        #region Texture management

        public bool TryGetTextureInfo(UUID textureID, out TextureInfo info)
        {
            info = null;

            if (TexturesPtrMap.TryGetValue(textureID, out var textureinfo))
            {
                info = textureinfo;
                return true;
            }

            return false;
        }

        public void DownloadTexture(TextureLoadItem item, bool force)
        {
            if (force || texturesRequestedThisFrame < RenderSettings.TexturesToDownloadPerFrame)
            {
                lock (TexturesPtrMap)
                {
                    if (TexturesPtrMap.TryGetValue(item.TeFace.TextureID, out var textureInfo))
                    {
                        item.Data.TextureInfo = textureInfo;
                    }
                    else if (item.TeFace.TextureID == invisi1 || item.TeFace.TextureID == invisi2)
                    {
                        TexturesPtrMap[item.TeFace.TextureID] = item.Data.TextureInfo;
                        TexturesPtrMap[item.TeFace.TextureID].HasAlpha = false;
                        TexturesPtrMap[item.TeFace.TextureID].IsInvisible = true;
                    }
                    else
                    {
                        TexturesPtrMap[item.TeFace.TextureID] = item.Data.TextureInfo;

                        if (item.TextureData == null)
                        {
                            if (CacheDecodedTextures && RHelp.LoadCachedImage(item.TeFace.TextureID, out item.TextureData,
                                    out item.Data.TextureInfo.HasAlpha, out item.Data.TextureInfo.FullAlpha, out item.Data.TextureInfo.IsMask))
                            {
                                PendingTextures.Enqueue(item);
                                PendingTexturesAvailable.Release();
                            }
                            else if (Client.Assets.Cache.HasAsset(item.Data.TextureInfo.TextureID))
                            {
                                item.LoadAssetFromCache = true;
                                PendingTextures.Enqueue(item);
                                PendingTexturesAvailable.Release();
                            }
                            else if (!item.Data.TextureInfo.FetchFailed)
                            {
                                void handler(TextureRequestState state, AssetTexture asset)
                                {
                                    switch (state)
                                    {
                                        case TextureRequestState.Finished:
                                            item.TextureData = asset.AssetData;
                                            PendingTextures.Enqueue(item);
                                            PendingTexturesAvailable.Release();
                                            break;

                                        case TextureRequestState.Aborted:
                                        case TextureRequestState.NotFound:
                                        case TextureRequestState.Timeout:
                                            item.Data.TextureInfo.FetchFailed = true;
                                            break;
                                    }
                                }

                                if (item.ImageType == ImageType.ServerBaked && !string.IsNullOrEmpty(item.BakeName))
                                { // Server side bake
                                    Client.Assets.RequestServerBakedImage(item.AvatarID, item.TeFace.TextureID, item.BakeName, handler);
                                }
                                else
                                { // Regular texture 
                                    Client.Assets.RequestImage(item.TeFace.TextureID, item.ImageType, handler);
                                }

                                texturesRequestedThisFrame++;
                            }
                        }
                        else
                        {
                            PendingTextures.Enqueue(item);
                            PendingTexturesAvailable.Release();
                        }
                    }
                }
            }
        }

        private bool LoadTexture(UUID textureID, ref SKBitmap texture)
        {
            var gotImage = new ManualResetEvent(false);
            SKBitmap img = null;

            try
            {
                gotImage.Reset();
                if (RHelp.LoadCachedImage(textureID, out var textureBytes,
                        out var hasAlpha, out var fullAlpha, out var isMask))
                {
                    img = J2kImage.FromBytes(textureBytes).As<SKBitmap>();
                }
                else
                {
                    instance.Client.Assets.RequestImage(textureID, (state, assetTexture) =>
                        {
                            if (state == TextureRequestState.Finished)
                            {
                                RHelp.SaveCachedImage(assetTexture.AssetData, textureID,
                                    true, false, false);
                            }

                            gotImage.Set();
                        }
                    );
                    gotImage.WaitOne(TimeSpan.FromMinutes(1), false);
                }

                if (img == null) { return false; }
                texture = img;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message, ex, instance.Client);
                return false;
            }
        }

        #endregion Texture management
    }
}
