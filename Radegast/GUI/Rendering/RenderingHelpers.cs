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

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.IO.Compression;
using OpenTK.Graphics.OpenGL;
using System.Runtime.InteropServices;
using System.Drawing;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Radegast.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Color4b
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ColorVertex
    {
        [FieldOffset(0)]
        public Vertex Vertex;
        [FieldOffset(32)]
        public Color4b Color;
        public static int Size = 36;
    }

    public class TextureInfo
    {
        public SKBitmap Texture;
        public int TexturePointer;
        public bool HasAlpha;
        public bool FullAlpha;
        public bool IsMask;
        public bool IsInvisible;
        public UUID TextureID;
        public bool FetchFailed;
    }

    public class TextureLoadItem
    {
        public FaceData Data;
        public Primitive Prim;
        public Primitive.TextureEntryFace TeFace;
        public byte[] TextureData = null;
        public bool LoadAssetFromCache = false;
        public ImageType ImageType = ImageType.Normal;
        public string BakeName = string.Empty;
        public UUID AvatarID = UUID.Zero;
    }

    public enum RenderPass
    {
        Picking,
        Simple,
        Alpha,
        Invisible
    }

    public enum SceneObjectType
    {
        None,
        Primitive,
        Avatar,
    }

    /// <summary>
    /// Base class for all scene objects
    /// </summary>
    public abstract class SceneObject : IComparable, IDisposable
    {
        #region Public fields
        /// <summary>Interpolated local position of the object</summary>
        public Vector3 InterpolatedPosition;
        /// <summary>Interpolated local rotation of the object</summary>
        public Quaternion InterpolatedRotation;
        /// <summary>Rendered position of the object in the region</summary>
        public Vector3 RenderPosition;
        /// <summary>Rendered rotationm of the object in the region</summary>
        public Quaternion RenderRotation;
        /// <summary>Per frame calculated square of the distance from camera</summary>
        public float DistanceSquared;
        /// <summary>Bounding volume of the object</summary>
        public BoundingVolume BoundingVolume;
        /// <summary>Was the sim position and distance from camera calculated during this frame</summary>
        public bool PositionCalculated;
        /// <summary>Scene object type</summary>
        public SceneObjectType Type = SceneObjectType.None;
        /// <summary>Libomv primitive</summary>
        public virtual Primitive BasePrim { get; set; }
        /// <summary>Were initial initialization tasks done</summary>
        public bool Initialized;
        /// <summary>Is this object disposed</summary>
        public bool IsDisposed = false;
        public int AlphaQueryID = -1;
        public int SimpleQueryID = -1;
        public bool HasAlphaFaces;
        public bool HasSimpleFaces;
        public bool HasInvisibleFaces;

        #endregion Public fields

        private uint previousParent = uint.MaxValue;

        /// <summary>
        /// Cleanup resources used
        /// </summary>
        public virtual void Dispose()
        {
            IsDisposed = true;
        }

        /// <summary>
        /// Task performed the fist time object is set for rendering
        /// </summary>
        public virtual void Initialize()
        {
            RenderPosition = InterpolatedPosition = BasePrim.Position;
            RenderRotation = InterpolatedRotation = BasePrim.Rotation;
            Initialized = true;
        }

        /// <summary>
        /// Perform per frame tasks
        /// </summary>
        /// <param name="time">Time since the last call (last frame time in seconds)</param>
        public virtual void Step(float time)
        {
            if (BasePrim == null) return;

            // Don't interpolate when parent changes (sit/stand link/unlink)
            if (previousParent != BasePrim.ParentID)
            {
                previousParent = BasePrim.ParentID;
                InterpolatedPosition = BasePrim.Position;
                InterpolatedRotation = BasePrim.Rotation;
                return;
            }

            // Linear velocity and acceleration
            if (BasePrim.Velocity != Vector3.Zero)
            {
                BasePrim.Position = InterpolatedPosition = BasePrim.Position + BasePrim.Velocity * time
                    * 0.98f * RadegastInstanceForms.Instance.Client.Network.CurrentSim.Stats.Dilation;
                BasePrim.Velocity += BasePrim.Acceleration * time;
            }
            else if (InterpolatedPosition != BasePrim.Position)
            {
                InterpolatedPosition = RHelp.Smoothed1stOrder(InterpolatedPosition, BasePrim.Position, time);
            }

            // Angular velocity (target omega)
            if (BasePrim.AngularVelocity != Vector3.Zero)
            {
                Vector3 angVel = BasePrim.AngularVelocity;
                float angle = time * angVel.Length();
                Quaternion dQ = Quaternion.CreateFromAxisAngle(angVel, angle);
                InterpolatedRotation = dQ * InterpolatedRotation;
            }
            else if (InterpolatedRotation != BasePrim.Rotation && !(this is RenderAvatar))
            {
                InterpolatedRotation = Quaternion.Slerp(InterpolatedRotation, BasePrim.Rotation, time * 10f);
                if (1f - Math.Abs(Quaternion.Dot(InterpolatedRotation, BasePrim.Rotation)) < 0.0001)
                    InterpolatedRotation = BasePrim.Rotation;
            }
            else
            {
                InterpolatedRotation = BasePrim.Rotation;
            }
        }

        /// <summary>
        /// Render scene object
        /// </summary>
        /// <param name="pass">Which pass are we currently in</param>
        /// <param name="pickingID">ID used to identify which object was picked</param>
        /// <param name="scene">Main scene renderer</param>
        /// <param name="time">Time it took to render the last frame</param>
        public virtual void Render(RenderPass pass, int pickingID, SceneWindow scene, float time)
        {
        }

        /// <summary>
        /// Implementation of the IComparable interface
        /// used for sorting by distance
        /// </summary>
        /// <param name="other">Object we are comparing to</param>
        /// <returns>Result of the comparison</returns>
        public virtual int CompareTo(object other)
        {
            SceneObject o = (SceneObject)other;
            if (DistanceSquared < o.DistanceSquared)
                return -1;
            if (DistanceSquared > o.DistanceSquared)
                return 1;
            return 0;
        }

        #region Occlusion queries
        public void StartQuery(RenderPass pass)
        {
            if (!RenderSettings.OcclusionCullingEnabled) return;

            if (pass == RenderPass.Simple)
            {
                StartSimpleQuery();
            }
            else if (pass == RenderPass.Alpha)
            {
                StartAlphaQuery();
            }
        }

        public void EndQuery(RenderPass pass)
        {
            if (!RenderSettings.OcclusionCullingEnabled) return;

            if (pass == RenderPass.Simple)
            {
                EndSimpleQuery();
            }
            else if (pass == RenderPass.Alpha)
            {
                EndAlphaQuery();
            }
        }

        public void StartAlphaQuery()
        {
            if (!RenderSettings.OcclusionCullingEnabled) return;

            if (AlphaQueryID == -1)
            {
                Compat.GenQueries(out AlphaQueryID);
            }
            if (AlphaQueryID > 0)
            {
                Compat.BeginQuery(QueryTarget.SamplesPassed, AlphaQueryID);
            }
        }

        public void EndAlphaQuery()
        {
            if (!RenderSettings.OcclusionCullingEnabled) return;

            if (AlphaQueryID > 0)
            {
                Compat.EndQuery(QueryTarget.SamplesPassed);
            }
        }

        public void StartSimpleQuery()
        {
            if (!RenderSettings.OcclusionCullingEnabled) return;

            if (SimpleQueryID == -1)
            {
                Compat.GenQueries(out SimpleQueryID);
            }
            if (SimpleQueryID > 0)
            {
                Compat.BeginQuery(QueryTarget.SamplesPassed, SimpleQueryID);
            }
        }

        public void EndSimpleQuery()
        {
            if (!RenderSettings.OcclusionCullingEnabled) return;

            if (SimpleQueryID > 0)
            {
                Compat.EndQuery(QueryTarget.SamplesPassed);
            }
        }

        public bool Occluded()
        {
            if (!RenderSettings.OcclusionCullingEnabled) return false;

            // Never occlude invisible faces - they need to be rendered in invisible pass
            if (HasInvisibleFaces) return false;

            // Don't occlude if we haven't run any queries yet (both are -1 means uninitialized)
            // This prevents treating new objects as occluded before they've been tested
            if (SimpleQueryID == -1 && AlphaQueryID == -1)
            {
                return false; // Not occluded - haven't tested yet
            }

            // If the object has no renderable faces, don't treat it as occluded
            // (This prevents edge cases where face flags aren't set yet)
            if (!HasAlphaFaces && !HasSimpleFaces)
            {
                return false; // Can't be occluded if we don't know what faces we have yet
            }

            // Check simple pass query results
            if (HasSimpleFaces && SimpleQueryID > 0)
            {
                try
                {
                    Compat.GetQueryObject(SimpleQueryID, GetQueryObjectParam.QueryResult, out int samples);
                    if (samples > 0)
                    {
                        return false; // Visible in simple pass
                    }
                }
                catch
                {
                    // If query fails, assume not occluded to be safe
                    return false;
                }
            }

            // Check alpha pass query results
            if (HasAlphaFaces && AlphaQueryID > 0)
            {
                try
                {
                    Compat.GetQueryObject(AlphaQueryID, GetQueryObjectParam.QueryResult, out int samples);
                    if (samples > 0)
                    {
                        return false; // Visible in alpha pass
                    }
                }
                catch
                {
                    // If query fails, assume not occluded to be safe
                    return false;
                }
            }

            // Only return true (occluded) if we actually ran queries and got zero samples
            return true;
        }
        #endregion Occlusion queries
    }

    public static class GLU
    {
        public static bool Project(OpenTK.Vector3 objPos, OpenTK.Matrix4 modelMatrix, 
            OpenTK.Matrix4 projMatrix, int[] viewport, out OpenTK.Vector3 screenPos)
        {
            OpenTK.Vector4 _in;

            _in.X = objPos.X;
            _in.Y = objPos.Y;
            _in.Z = objPos.Z;
            _in.W = 1.0f;

            var _out = OpenTK.Vector4.Transform(_in, modelMatrix);
            _in = OpenTK.Vector4.Transform(_out, projMatrix);

            if (_in.W <= 0.0)
            {
                screenPos = OpenTK.Vector3.Zero;
                return false;
            }

            _in.X /= _in.W;
            _in.Y /= _in.W;
            _in.Z /= _in.W;
            /* Map x, y and z to range 0-1 */
            _in.X = _in.X * 0.5f + 0.5f;
            _in.Y = _in.Y * 0.5f + 0.5f;
            _in.Z = _in.Z * 0.5f + 0.5f;

            /* Map x,y to viewport */
            _in.X = _in.X * viewport[2] + viewport[0];
            _in.Y = _in.Y * viewport[3] + viewport[1];

            screenPos.X = _in.X;
            screenPos.Y = _in.Y;
            screenPos.Z = _in.Z;

            return true;
        }

        public static bool UnProject(float winx, float winy, float winz, OpenTK.Matrix4 modelMatrix, 
            OpenTK.Matrix4 projMatrix, int[] viewport, out OpenTK.Vector3 pos)
        {
            OpenTK.Vector4 _in;

            var finalMatrix = OpenTK.Matrix4.Mult(modelMatrix, projMatrix);

            try
            {
                finalMatrix.Invert();
            }
            catch (InvalidOperationException)
            {
                pos = OpenTK.Vector3.Zero;
                return false;
            }

            _in.X = winx;
            _in.Y = winy;
            _in.Z = winz;
            _in.W = 1.0f;

            /* Map x and y from window coordinates */
            _in.X = (_in.X - viewport[0]) / viewport[2];
            _in.Y = (_in.Y - viewport[1]) / viewport[3];

            pos = OpenTK.Vector3.Zero;

            /* Map to range -1 to 1 */
            _in.X = _in.X * 2 - 1;
            _in.Y = _in.Y * 2 - 1;
            _in.Z = _in.Z * 2 - 1;

            //__gluMultMatrixVecd(finalMatrix, _in, _out);
            // check if this works:
            var _out = OpenTK.Vector4.Transform(_in, finalMatrix);

            if (_out.W == 0.0f)
                return false;
            _out.X /= _out.W;
            _out.Y /= _out.W;
            _out.Z /= _out.W;
            pos.X = _out.X;
            pos.Y = _out.Y;
            pos.Z = _out.Z;
            return true;
        }
    }

    public static class RHelp
    {
        public static readonly Vector3 InvalidPosition = new Vector3(99999f, 99999f, 99999f);
        private static readonly float t1 = 0.075f;
        private static readonly float t2 = t1 / 5.7f;

        public static Vector3 Smoothed1stOrder(Vector3 curPos, Vector3 targetPos, float lastFrameTime)
        {
            int numIterations = (int)(lastFrameTime * 100);
            do
            {
                curPos += (targetPos - curPos) * t1;
                numIterations--;
            }
            while (numIterations > 0);
            if (Vector3.DistanceSquared(curPos, targetPos) < 0.00001f)
            {
                curPos = targetPos;
            }
            return curPos;
        }

        public static Vector3 Smoothed2ndOrder(Vector3 curPos, Vector3 targetPos, ref Vector3 accel, float lastFrameTime)
        {
            int numIterations = (int)(lastFrameTime * 100);
            do
            {
                accel += (targetPos - accel - curPos) * t1;
                curPos += accel * t2;
                numIterations--;
            }
            while (numIterations > 0);
            if (Vector3.DistanceSquared(curPos, targetPos) < 0.00001f)
            {
                curPos = targetPos;
            }
            return curPos;
        }

        public static OpenTK.Vector2 TKVector3(Vector2 v)
        {
            return new OpenTK.Vector2(v.X, v.Y);
        }

        public static OpenTK.Vector3 TKVector3(Vector3 v)
        {
            return new OpenTK.Vector3(v.X, v.Y, v.Z);
        }

        public static OpenTK.Vector4 TKVector3(Vector4 v)
        {
            return new OpenTK.Vector4(v.X, v.Y, v.Z, v.W);
        }

        public static Vector2 OMVVector2(OpenTK.Vector2 v)
        {
            return new Vector2(v.X, v.Y);
        }

        public static Vector3 OMVVector3(OpenTK.Vector3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        public static Vector4 OMVVector4(OpenTK.Vector4 v)
        {
            return new Vector4(v.X, v.Y, v.Z, v.W);
        }

        public static Color WinColor(OpenTK.Graphics.Color4 color)
        {
            return Color.FromArgb((int)(color.A * 255), (int)(color.R * 255), (int)(color.G * 255), (int)(color.B * 255));
        }

        public static Color WinColor(Color4 color)
        {
            return Color.FromArgb((int)(color.A * 255), (int)(color.R * 255), (int)(color.G * 255), (int)(color.B * 255));
        }

        public static int NextPow2(int start)
        {
            int pow = 1;
            while (pow < start) pow *= 2;
            return pow;
        }

        #region Cached image save and load
        private static readonly string RAD_IMG_MAGIC = "rj";
        private const int CACHE_IMG_HEADER_SIZE = 24;

        [Flags]
        public enum CacheImageFlags : byte
        {
            None = 0x0,
            HasAlpha = 0x1,
            FullAlpha = 0x2,
            IsMask = 0x4,
        }

        public static bool LoadCachedImage(UUID textureID, out byte[] textureBytes, out bool hasAlpha, out bool fullAlpha, out bool isMask)
        {
            textureBytes = null;
            hasAlpha = fullAlpha = isMask = false;

            try
            {
                var fname = RadegastInstance.ComputeCacheName(RadegastInstanceForms.Instance.Client.Settings.ASSET_CACHE_DIR, textureID) + ".rzi";

                using (var f = File.Open(fname, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var header = new byte[CACHE_IMG_HEADER_SIZE];
                    var i = 0;
                    if (f.Read(header, 0, header.Length) != header.Length) { return false; }

                    // check if the file is starting with magic string
                    if (RAD_IMG_MAGIC != Utils.BytesToString(header, 0, RAD_IMG_MAGIC.Length))
                    {
                        return false;
                    }
                    i += RAD_IMG_MAGIC.Length;

                    if (header[i++] != 2) { return false; }// check version

                    var flags = (CacheImageFlags)header[i++];
                    hasAlpha = flags.HasFlag(CacheImageFlags.HasAlpha);
                    fullAlpha = flags.HasFlag(CacheImageFlags.FullAlpha);
                    isMask = flags.HasFlag(CacheImageFlags.IsMask);

                    var uncompressedSize = Utils.BytesToInt(header, i);
                    i += 4;

                    textureID = new UUID(header, i);
                    i += 16;

                    textureBytes = new byte[uncompressedSize];
                    using (var compressed = new DeflateStream(f, CompressionMode.Decompress))
                    {
                        var read = 0;
                        while ((read = compressed.Read(textureBytes, read, uncompressedSize - read)) > 0) { }
                    }
                }

                return true;
            }
            catch (FileNotFoundException) { }
            catch (Exception ex)
            {
                Logger.DebugLog($"Failed to load radegast cache file {textureID}: {ex.Message}");
            }
            return false;
        }

        public static bool SaveCachedImage(byte[] textureBytes, UUID textureID, bool hasAlpha, bool fullAlpha, bool isMask)
        {
            try
            {
                string fname = RadegastInstance.ComputeCacheName(RadegastInstanceForms.Instance.Client.Settings.ASSET_CACHE_DIR, textureID) + ".rzi";

                using (var f = File.Open(fname, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int i = 0;
                    // magic header
                    f.Write(Utils.StringToBytes(RAD_IMG_MAGIC), 0, RAD_IMG_MAGIC.Length);
                    i += RAD_IMG_MAGIC.Length;

                    // version
                    f.WriteByte(2);
                    i++;

                    // texture info
                    CacheImageFlags flags = CacheImageFlags.None;
                    if (hasAlpha) { flags |= CacheImageFlags.HasAlpha; }
                    if (fullAlpha) { flags |= CacheImageFlags.FullAlpha; }
                    if (isMask) { flags |= CacheImageFlags.IsMask; }
                    f.WriteByte((byte)flags);
                    i++;

                    // texture size
                    byte[] uncompressedSize = Utils.IntToBytes(textureBytes.Length);
                    f.Write(uncompressedSize, 0, uncompressedSize.Length);
                    i += uncompressedSize.Length;

                    // texture id
                    byte[] id = new byte[16];
                    textureID.ToBytes(id, 0);
                    f.Write(id, 0, 16);
                    i += 16;

                    // compressed texture data
                    using (var compressed = new DeflateStream(f, CompressionMode.Compress))
                    {
                        compressed.Write(textureBytes, 0, textureBytes.Length);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.DebugLog($"Failed to save radegast cache file {textureID}: {ex.Message}");
                return false;
            }
        }
        #endregion Cached image save and load

        #region Static vertices and indices for a cube (used for bounding box drawing)
        /**********************************************
          5 --- 4
         /|    /|
        1 --- 0 |
        | 6 --| 7
        |/    |/
        2 --- 3
        ***********************************************/
        public static readonly float[] CubeVertices = new float[]
        {
             0.5f,  0.5f,  0.5f, // 0
	        -0.5f,  0.5f,  0.5f, // 1
	        -0.5f, -0.5f,  0.5f, // 2
	         0.5f, -0.5f,  0.5f, // 3
	         0.5f,  0.5f, -0.5f, // 4
	        -0.5f,  0.5f, -0.5f, // 5
	        -0.5f, -0.5f, -0.5f, // 6
	         0.5f, -0.5f, -0.5f  // 7
        };

        public static readonly ushort[] CubeIndices = new ushort[]
        {
            0, 1, 2, 3,     // Front Face
	        4, 5, 6, 7,     // Back Face
	        1, 2, 6, 5,     // Left Face
	        0, 3, 7, 4,     // Right Face
	        0, 1, 5, 4,     // Top Face
	        2, 3, 7, 6      // Bottom Face
        };
        #endregion Static vertices and indices for a cube (used for bounding box drawing)

        public static int GLLoadImage(SKBitmap bitmap, bool hasAlpha = true, bool useMipmap = true)
        {
            useMipmap = useMipmap && RenderSettings.HasMipmap;
            int ret;
            GL.GenTextures(1, out ret);
            GL.BindTexture(TextureTarget.Texture2D, ret);

            IntPtr pixels = bitmap.GetPixels();
            int width = bitmap.Width;
            int height = bitmap.Height;

            OpenTK.Graphics.OpenGL.PixelFormat pixelFormat;
            PixelInternalFormat internalFormat;

            // Determine format based on the actual SKBitmap color type
            if (bitmap.ColorType == SKColorType.Bgra8888)
            {
                // BGRA8888 format - always 4 bytes per pixel
                pixelFormat = OpenTK.Graphics.OpenGL.PixelFormat.Bgra;
                internalFormat = hasAlpha ? PixelInternalFormat.Rgba : PixelInternalFormat.Rgb8;
            }
            else if (bitmap.ColorType == SKColorType.Rgba8888)
            {
                // RGBA8888 format - always 4 bytes per pixel
                pixelFormat = OpenTK.Graphics.OpenGL.PixelFormat.Rgba;
                internalFormat = hasAlpha ? PixelInternalFormat.Rgba : PixelInternalFormat.Rgb8;
            }
            else if (bitmap.ColorType == SKColorType.Rgb888x)
            {
                // RGB888x format - 4 bytes per pixel with unused alpha
                pixelFormat = OpenTK.Graphics.OpenGL.PixelFormat.Rgba;
                internalFormat = PixelInternalFormat.Rgb8;
            }
            else
            {
                // Fallback: convert to BGRA8888
                var converted = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(converted))
                {
                    canvas.DrawBitmap(bitmap, 0, 0);
                }
                bitmap = converted;
                pixels = bitmap.GetPixels();
                pixelFormat = OpenTK.Graphics.OpenGL.PixelFormat.Bgra;
                internalFormat = hasAlpha ? PixelInternalFormat.Rgba : PixelInternalFormat.Rgb8;
            }

            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                internalFormat,
                width,
                height,
                0,
                pixelFormat,
                PixelType.UnsignedByte,
                pixels);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            if (useMipmap)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.GenerateMipmap, 1);
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }

            return ret;
        }

        public static void Draw2DBox(float x, float y, float width, float height, float depth)
        {
            GL.Begin(PrimitiveType.Quads);
            {
                GL.TexCoord2(0, 1);
                GL.Vertex3(x, y, depth);
                GL.TexCoord2(1, 1);
                GL.Vertex3(x + width, y, depth);
                GL.TexCoord2(1, 0);
                GL.Vertex3(x + width, y + height, depth);
                GL.TexCoord2(0, 0);
                GL.Vertex3(x, y + height, depth);
            }
            GL.End();
        }

        public static void ResetMaterial()
        {
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Ambient, new float[] { 0.2f, 0.2f, 0.2f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Diffuse, new float[] { 0.8f, 0.8f, 0.8f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Specular, new float[] { 0f, 0f, 0f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Emission, new float[] { 0f, 0f, 0f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Shininess, 0f);
            ShaderProgram.Stop();
        }

        public static void DrawRounded2DBox(float x, float y, float width, float height, float radius, float depth)
        {
            // Clamp radius
            radius = Math.Max(0f, Math.Min(radius, Math.Min(width, height) * 0.5f));

            // Precompute corner centers
            float left = x;
            float right = x + width;
            float top = y + height;
            float bottom = y;

            float cxLeft = left + radius;
            float cxRight = right - radius;
            float cyTop = top - radius;
            float cyBottom = bottom + radius;

            // Draw center rectangle
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex3(cxLeft, cyBottom, depth);
            GL.Vertex3(cxRight, cyBottom, depth);
            GL.Vertex3(cxRight, cyTop, depth);
            GL.Vertex3(cxLeft, cyTop, depth);
            GL.End();

            // Draw side rectangles
            GL.Begin(PrimitiveType.Quads);
            // Left side
            GL.Vertex3(left, cyBottom, depth);
            GL.Vertex3(cxLeft, cyBottom, depth);
            GL.Vertex3(cxLeft, cyTop, depth);
            GL.Vertex3(left, cyTop, depth);

            // Right side
            GL.Vertex3(cxRight, cyBottom, depth);
            GL.Vertex3(right, cyBottom, depth);
            GL.Vertex3(right, cyTop, depth);
            GL.Vertex3(cxRight, cyTop, depth);

            // Bottom side
            GL.Vertex3(cxLeft, bottom, depth);
            GL.Vertex3(cxRight, bottom, depth);
            GL.Vertex3(cxRight, cyBottom, depth);
            GL.Vertex3(cxLeft, cyBottom, depth);

            // Top side
            GL.Vertex3(cxLeft, cyTop, depth);
            GL.Vertex3(cxRight, cyTop, depth);
            GL.Vertex3(cxRight, top, depth);
            GL.Vertex3(cxLeft, top, depth);
            GL.End();

            // Draw rounded corners with triangle fans (approximate quarter-circles)
            const int segments = 12;
            double step = Math.PI / 2.0 / segments;

            // Bottom-left corner (center at cxLeft, cyBottom), angles 180..270
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex3(cxLeft, cyBottom, depth);
            for (int i = 0; i <= segments; i++)
            {
                double ang = Math.PI + i * step;
                float vx = cxLeft + (float)(Math.Cos(ang) * radius);
                float vy = cyBottom + (float)(Math.Sin(ang) * radius);
                GL.Vertex3(vx, vy, depth);
            }
            GL.End();

            // Bottom-right corner (center at cxRight, cyBottom), angles 270..360
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex3(cxRight, cyBottom, depth);
            for (int i = 0; i <= segments; i++)
            {
                double ang = 1.5 * Math.PI + i * step;
                float vx = cxRight + (float)(Math.Cos(ang) * radius);
                float vy = cyBottom + (float)(Math.Sin(ang) * radius);
                GL.Vertex3(vx, vy, depth);
            }
            GL.End();

            // Top-right corner (center at cxRight, cyTop), angles 0..90
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex3(cxRight, cyTop, depth);
            for (int i = 0; i <= segments; i++)
            {
                double ang = 0 + i * step;
                float vx = cxRight + (float)(Math.Cos(ang) * radius);
                float vy = cyTop + (float)(Math.Sin(ang) * radius);
                GL.Vertex3(vx, vy, depth);
            }
            GL.End();

            // Top-left corner (center at cxLeft, cyTop), angles 90..180
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex3(cxLeft, cyTop, depth);
            for (int i = 0; i <= segments; i++)
            {
                double ang = 0.5 * Math.PI + i * step;
                float vx = cxLeft + (float)(Math.Cos(ang) * radius);
                float vy = cyTop + (float)(Math.Sin(ang) * radius);
                GL.Vertex3(vx, vy, depth);
            }
            GL.End();
        }
    }

    /// <summary>
    /// Represents camera object
    /// </summary>
    public class Camera
    {
        /// <summary>
        /// Indicates that there was manual camera movement, stop tracking objects
        /// </summary>
        public bool Manual;

        private Vector3 mPosition;
        private Vector3 mFocalPoint;

        /// <summary>Camera position</summary>
        public Vector3 Position
        {
            get => mPosition;

            set
            {
                if (mPosition != value)
                {
                    mPosition = value;
                    Modify();
                }
            }
        }

        /// <summary>Camera target</summary>
        public Vector3 FocalPoint
        {
            get => mFocalPoint;

            set
            {
                if (mFocalPoint != value)
                {
                    mFocalPoint = value;
                    Modify();
                }
            }
        }

        /// <summary>Zoom level</summary>
        public float Zoom;
        /// <summary>Draw distance</summary>
        public float Far;
        /// <summary>Has camera been modified</summary>
        public bool Modified { get; set; }

        public float TimeToTarget = 0f;

        public Vector3 RenderPosition;
        public Vector3 RenderFocalPoint;

        private void Modify()
        {
            Modified = true;
        }

        public void Step(float time)
        {
            if (RenderPosition != Position)
            {
                RenderPosition = RHelp.Smoothed1stOrder(RenderPosition, Position, time);
                Modified = true;
            }
            if (RenderFocalPoint != FocalPoint)
            {
                RenderFocalPoint = RHelp.Smoothed1stOrder(RenderFocalPoint, FocalPoint, time);
                Modified = true;
            }
        }

#if OBSOLETE_CODE
        [Obsolete("Use Step(), left in here for reference")]
        public void Step2(float time)
        {
            TimeToTarget -= time;
            if (TimeToTarget <= time)
            {
                EndMove();
                return;
            }

            mModified = true;

            float pctElapsed = time / TimeToTarget;

            if (RenderPosition != Position)
            {
                float distance = Vector3.Distance(RenderPosition, Position);
                RenderPosition = Vector3.Lerp(RenderPosition, Position, distance * pctElapsed);
            }

            if (RenderFocalPoint != FocalPoint)
            {
                RenderFocalPoint = Interpolate(RenderFocalPoint, FocalPoint, pctElapsed);
            }
        }

        Vector3 Interpolate(Vector3 start, Vector3 end, float fraction)
        {
            float distance = Vector3.Distance(start, end);
            Vector3 direction = end - start;
            return start + direction * fraction;
        }

        public void EndMove()
        {
            mModified = true;
            TimeToTarget = 0;
            RenderPosition = Position;
            RenderFocalPoint = FocalPoint;
        }
#endif

        public void Pan(float deltaX, float deltaY)
        {
            Manual = true;
            Vector3 direction = Position - FocalPoint;
            direction.Normalize();
            Vector3 vy = direction % Vector3.UnitZ;
            Vector3 vx = vy % direction;
            Vector3 vxy = vx * deltaY + vy * deltaX;
            Position += vxy;
            FocalPoint += vxy;
        }

        public void Rotate(float delta, bool horizontal)
        {
            Manual = true;
            Vector3 direction = Position - FocalPoint;
            if (horizontal)
            {
                Position = FocalPoint + direction * new Quaternion(0f, 0f, (float)Math.Sin(delta), (float)Math.Cos(delta));
            }
            else
            {
                Position = FocalPoint + direction * Quaternion.CreateFromAxisAngle(direction % Vector3.UnitZ, delta);
            }
        }

        public void MoveToTarget(float delta)
        {
            Manual = true;
            Position += (Position - FocalPoint) * delta;
        }

        /// <summary>
        /// Sets the world in perspective of the camera
        /// </summary>
        public void LookAt()
        {
            OpenTK.Matrix4 lookAt = OpenTK.Matrix4.LookAt(
                RenderPosition.X, RenderPosition.Y, RenderPosition.Z,
                RenderFocalPoint.X, RenderFocalPoint.Y, RenderFocalPoint.Z,
                0f, 0f, 1f);
            GL.MultMatrix(ref lookAt);
        }
    }

    public static class MeshToOBJ
    {
        public static bool MeshesToOBJ(Dictionary<uint, FacetedMesh> meshes, string filename)
        {
            StringBuilder obj = new StringBuilder();
            StringBuilder mtl = new StringBuilder();

            FileInfo objFileInfo = new FileInfo(filename);

            string mtlFilename = objFileInfo.FullName.Substring(objFileInfo.DirectoryName.Length + 1,
                objFileInfo.FullName.Length - (objFileInfo.DirectoryName.Length + 1) - 4) + ".mtl";

            obj.AppendLine("# Created by libprimrender");
            obj.AppendLine("mtllib ./" + mtlFilename);
            obj.AppendLine();

            mtl.AppendLine("# Created by libprimrender");
            mtl.AppendLine();

            int primNr = 0;
            foreach (FacetedMesh mesh in meshes.Values)
            {
                foreach (var face in mesh.Faces)
                {
                    if (face.Vertices.Count > 2)
                    {
                        string mtlName = $"material{primNr}-{face.ID}";
                        Primitive.TextureEntryFace tex = face.TextureFace;
                        string texName = tex.TextureID + ".tga";

                        // FIXME: Convert the source to TGA (if needed) and copy to the destination

                        float shiny = 0.00f;
                        switch (tex.Shiny)
                        {
                            case Shininess.High:
                                shiny = 1.00f;
                                break;
                            case Shininess.Medium:
                                shiny = 0.66f;
                                break;
                            case Shininess.Low:
                                shiny = 0.33f;
                                break;
                        }

                        obj.AppendFormat("g face{0}-{1}{2}", primNr, face.ID, Environment.NewLine);

                        mtl.AppendLine("newmtl " + mtlName);
                        mtl.AppendFormat("Ka {0} {1} {2}{3}", tex.RGBA.R, tex.RGBA.G, tex.RGBA.B, Environment.NewLine);
                        mtl.AppendFormat("Kd {0} {1} {2}{3}", tex.RGBA.R, tex.RGBA.G, tex.RGBA.B, Environment.NewLine);
                        //mtl.AppendFormat("Ks {0} {1} {2}{3}");
                        mtl.AppendLine("Tr " + tex.RGBA.A);
                        mtl.AppendLine("Ns " + shiny);
                        mtl.AppendLine("illum 1");
                        if (tex.TextureID != UUID.Zero && tex.TextureID != Primitive.TextureEntry.WHITE_TEXTURE)
                            mtl.AppendLine("map_Kd ./" + texName);
                        mtl.AppendLine();

                        // Write the vertices, texture coordinates, and vertex normals for this side
                        foreach (var vertex in face.Vertices)
                        {
                            #region Vertex

                            Vector3 pos = vertex.Position;

                            // Apply scaling
                            pos *= mesh.Prim.Scale;

                            // Apply rotation
                            pos *= mesh.Prim.Rotation;

                            // The root prim position is sim-relative, while child prim positions are
                            // parent-relative. We want to apply parent-relative translations but not
                            // sim-relative ones
                            if (mesh.Prim.ParentID != 0)
                                pos += mesh.Prim.Position;

                            obj.AppendFormat("v {0} {1} {2}{3}", pos.X, pos.Y, pos.Z, Environment.NewLine);

                            #endregion Vertex

                            #region Texture Coord

                            obj.AppendFormat("vt {0} {1}{2}", vertex.TexCoord.X, vertex.TexCoord.Y,
                                Environment.NewLine);

                            #endregion Texture Coord

                            #region Vertex Normal

                            // HACK: Sometimes normals are getting set to <NaN,NaN,NaN>
                            if (!float.IsNaN(vertex.Normal.X) && 
                                !float.IsNaN(vertex.Normal.Y) &&
                                !float.IsNaN(vertex.Normal.Z))
                            {
                                obj.AppendFormat("vn {0} {1} {2}{3}", vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z,
                                    Environment.NewLine);
                            }
                            else
                            {
                                obj.AppendLine("vn 0.0 1.0 0.0");
                            }
                            #endregion Vertex Normal
                        }

                        obj.AppendFormat("# {0} vertices{1}", face.Vertices.Count, Environment.NewLine);
                        obj.AppendLine();
                        obj.AppendLine("usemtl " + mtlName);

                        #region Elements

                        // Write all faces (triangles) for this side
                        for (int k = 0; k < face.Indices.Count / 3; k++)
                        {
                            obj.AppendFormat("f -{0}/-{0}/-{0} -{1}/-{1}/-{1} -{2}/-{2}/-{2}{3}",
                                face.Vertices.Count - face.Indices[k * 3 + 0],
                                face.Vertices.Count - face.Indices[k * 3 + 1],
                                face.Vertices.Count - face.Indices[k * 3 + 2],
                                Environment.NewLine);
                        }

                        obj.AppendFormat("# {0} elements{1}", face.Indices.Count / 3, Environment.NewLine);
                        obj.AppendLine();

                        #endregion Elements
                    }
                }

                primNr++;
            }

            try
            {
                File.WriteAllText(filename, obj.ToString());
                File.WriteAllText(mtlFilename, mtl.ToString());
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }

    /*
     *  Helper classs for reading the static VFS file, call 
     *  staticVFS.readVFSheaders() with the path to the static_data.db2 and static_index.db2 files
     *  and it will pass and dump in to openmetaverse_data for you
     *  This should only be needed to be used if LL update the static VFS in order to refresh our data
     */

    internal class VFSblock
    {
        public int mLocation;
        public int mLength;
        public int mAccessTime;
        public UUID mFileID;
        public int mSize;
        public AssetType mAssetType;

        public int Readblock(byte[] blockdata, int offset)
        {

            BitPack input = new BitPack(blockdata, offset);
            mLocation = input.UnpackInt();
            mLength = input.UnpackInt();
            mAccessTime = input.UnpackInt();
            mFileID = input.UnpackUUID();
            int filetype = input.UnpackShort();
            mAssetType = (AssetType)filetype;
            mSize = input.UnpackInt();
            offset += 34;

            Logger.Info($"Found header for {mFileID} type {mAssetType} length {mSize} at {mLocation}");

            return offset;
        }

    }

    public class StaticVFS
    {
        public static void ReadVFSheaders(string datafile, string indexfile)
        {
            FileStream datastream;
            FileStream indexstream;

            datastream = File.Open(datafile, FileMode.Open);
            indexstream = File.Open(indexfile, FileMode.Open);

            int offset = 0;

            byte[] blockdata = new byte[indexstream.Length];
            indexstream.Read(blockdata, 0, (int)indexstream.Length);

            while (offset < indexstream.Length)
            {
                VFSblock block = new VFSblock();
                offset = block.Readblock(blockdata, offset);

                FileStream writer = File.Open(System.IO.Path.Combine(OpenMetaverse.Settings.RESOURCE_DIR, block.mFileID.ToString()), FileMode.Create);
                byte[] data = new byte[block.mSize];
                datastream.Seek(block.mLocation, SeekOrigin.Begin);
                datastream.Read(data, 0, block.mSize);
                writer.Write(data, 0, block.mSize);
                writer.Close();
            }

        }
    }
}