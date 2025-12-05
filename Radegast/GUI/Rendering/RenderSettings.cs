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

using OpenMetaverse.Rendering;

namespace Radegast.Rendering
{
    public static class RenderSettings
    {
        /// <summary>
        /// Gamma correction value applied in shaders (1.0 = no correction)
        /// </summary>
        public static float Gamma = 0.5f;

        #region VBO support
        public static bool UseVBO = true; // VBOs are always used when supported
        public static bool CoreVBOPresent;
        public static bool ARBVBOPresent;
        #endregion VBO support

        #region Occlusion queries
        /// <summary>Should we try to optimize by not drawing objects occluded behind other objects</summary>
        public static bool OcclusionCullingEnabled;
        public static bool CoreQuerySupported;
        public static bool ARBQuerySupported;
        #endregion Occlusion queries

        public static bool HasMultiTexturing;
        public static bool HasMipmap;
        public static bool HasShaders;
        public static DetailLevel PrimRenderDetail = DetailLevel.High;
        public static DetailLevel SculptRenderDetail = DetailLevel.High;
        public static DetailLevel MeshRenderDetail = DetailLevel.Highest;
        public static bool AllowQuickAndDirtyMeshing = true;
        public static int MeshesPerFrame = 16;
        public static int TexturesToDownloadPerFrame = 2;
        /// <summary>Should we try to make sure that large prims that are > our draw distance are in view when we are standing on them</summary>
        public static bool HeavierDistanceChecking = true;
        /// <summary>Minimum time between rebuilding terrain mesh and texture</summary>
        public static float MinimumTimeBetweenTerrainUpdated = 15f;
        /// <summary>Are textures that don't have dimensions that are powers of two supported</summary>
        public static bool TextureNonPowerOfTwoSupported;

        /// <summary>
        /// Render avatars
        /// </summary>
        public static bool AvatarRenderingEnabled = true;

        /// <summary>
        /// Render prims
        /// </summary>
        public static bool PrimitiveRenderingEnabled = true;

        /// <summary>
        /// Show avatar skeleton
        /// </summary>
        public static bool RenderAvatarSkeleton = false;

        /// <summary>
        /// Enable shader for shiny
        /// </summary>
        public static bool EnableShiny = false;

        /// <summary>
        /// Enable glow effect in shaders
        /// </summary>
        public static bool EnableGlow = true;

        /// <summary>
        /// Enable material layer usage in shaders (specular color/strength/shininess)
        /// </summary>
        public static bool EnableMaterials = true;

        /// <summary>
        /// Enable use of the sky shader (if available)
        /// </summary>
        public static bool EnableSkyShader = true;

        /// <summary>
        /// Enable simple shadowing in shaders
        /// </summary>
        public static bool EnableShadows = false;

        /// <summary>
        /// Shadow intensity multiplier used by shaders (0.0 - 1.0)
        /// </summary>
        public static float ShadowIntensity = 1.0f;

        #region Lighting
        /// <summary>
        /// Ambient light level (0.0 to 1.0)
        /// </summary>
        public static float AmbientLight = 0.40f;

        /// <summary>
        /// Diffuse light level (0.0 to 1.0)
        /// </summary>
        public static float DiffuseLight = 0.75f;

        /// <summary>
        /// Specular light level (0.0 to 1.0)
        /// </summary>
        public static float SpecularLight = 0.50f;

        /// <summary>
        /// Global emissive strength multiplier used by shaders for glowing faces
        /// </summary>
        public static float EmissiveStrength = 1.0f;
        #endregion Lighting

        #region Water
        public static bool WaterReflections = true;
        // Fallback CPU-side water animation when shaders are unavailable
        public static bool FallbackWaterAnimationEnabled = true;
        // Speed multiplier for the CPU fallback animation (higher -> faster)
        public static float FallbackWaterAnimationSpeed = 1.5f;
        // Amplitude of the alpha modulation used by the CPU fallback (0 = no modulation)
        public static float FallbackWaterAnimationAmplitude = 0.12f;
        // Base alpha used by the CPU fallback
        public static float FallbackWaterBaseAlpha = 0.84f;
        #endregion Water
    }
}
