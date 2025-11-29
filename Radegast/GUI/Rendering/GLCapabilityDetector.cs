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
using OpenTK.Graphics;
using OpenMetaverse;

namespace Radegast.Rendering
{
    /// <summary>
    /// Detects OpenGL capabilities and features
    /// </summary>
    public static class GLCapabilityDetector
    {
        /// <summary>
        /// Detect all OpenGL capabilities and populate RenderSettings
        /// </summary>
        /// <param name="context">OpenGL graphics context</param>
        /// <param name="glExtensions">String of GL extensions</param>
        /// <param name="globalSettings">Global settings dictionary for user preferences</param>
        public static void DetectCapabilities(
            IGraphicsContextInternal context, 
            string glExtensions,
            Settings globalSettings)
        {
            if (context == null || string.IsNullOrEmpty(glExtensions))
            {
                Logger.Debug("GLCapabilityDetector: Invalid context or extensions");
                return;
            }

            try
            {
                // VBO (Vertex Buffer Objects)
                RenderSettings.ARBVBOPresent = context.GetAddress("glGenBuffersARB") != IntPtr.Zero;
                RenderSettings.CoreVBOPresent = context.GetAddress("glGenBuffers") != IntPtr.Zero;
                RenderSettings.UseVBO = (RenderSettings.ARBVBOPresent || RenderSettings.CoreVBOPresent)
                    && globalSettings["rendering_use_vbo"];

                // Occlusion Query
                RenderSettings.ARBQuerySupported = context.GetAddress("glGetQueryObjectivARB") != IntPtr.Zero;
                RenderSettings.CoreQuerySupported = context.GetAddress("glGetQueryObjectiv") != IntPtr.Zero;
                RenderSettings.OcclusionCullingEnabled = (RenderSettings.CoreQuerySupported || RenderSettings.ARBQuerySupported)
                    && globalSettings["rendering_occlusion_culling_enabled2"];

                // Mipmap generation
                RenderSettings.HasMipmap = context.GetAddress("glGenerateMipmap") != IntPtr.Zero;

                // Shader support
                RenderSettings.HasShaders = glExtensions.Contains("vertex_shader") 
                    && glExtensions.Contains("fragment_shader");

                // Multi-texturing
                RenderSettings.HasMultiTexturing = context.GetAddress("glMultiTexCoord2f") != IntPtr.Zero;
                
                // Water reflections require both multi-texturing and shaders
                RenderSettings.WaterReflections = globalSettings["water_reflections"];
                if (!RenderSettings.HasMultiTexturing || !RenderSettings.HasShaders)
                {
                    RenderSettings.WaterReflections = false;
                }

                // Non-power-of-two textures
                RenderSettings.TextureNonPowerOfTwoSupported = glExtensions.Contains("texture_non_power_of_two");

                // Shiny materials
                RenderSettings.EnableShiny = globalSettings["scene_viewer_shiny"];

                // Log detected capabilities
                Logger.Debug($"GL Capabilities: VBO={RenderSettings.UseVBO}, " +
                    $"Shaders={RenderSettings.HasShaders}, " +
                    $"Mipmap={RenderSettings.HasMipmap}, " +
                    $"MultiTex={RenderSettings.HasMultiTexturing}, " +
                    $"OcclusionCulling={RenderSettings.OcclusionCullingEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error detecting GL capabilities: {ex.Message}");
            }
        }
    }
}
