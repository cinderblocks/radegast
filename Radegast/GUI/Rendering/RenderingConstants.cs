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

namespace Radegast.Rendering
{
    /// <summary>
    /// Shared constants used across rendering components
    /// </summary>
    public static class RenderingConstants
    {
        #region OpenGL Configuration
        /// <summary>Depth buffer bits</summary>
        public const int GL_DEPTH_BITS = 24;
        
        /// <summary>Stencil buffer bits</summary>
        public const int GL_STENCIL_BITS = 8;
        
        /// <summary>No anti-aliasing samples</summary>
        public const int GL_NO_SAMPLES = 0;
        
        /// <summary>Maximum anti-aliasing samples to try</summary>
        public const int GL_MAX_AA_SAMPLES = 4;
        
        /// <summary>Step size when testing AA sample counts</summary>
        public const int GL_AA_STEP = 2;
        #endregion

        #region Shininess Materials
        /// <summary>High shininess material value</summary>
        public const float SHININESS_HIGH = 94f;
        
        /// <summary>Medium shininess material value</summary>
        public const float SHININESS_MEDIUM = 64f;
        
        /// <summary>Low shininess material value</summary>
        public const float SHININESS_LOW = 24f;
        
        /// <summary>No shininess material value</summary>
        public const float SHININESS_NONE = 0f;
        #endregion

        #region Alpha Thresholds
        /// <summary>Alpha test threshold for masking</summary>
        public const float ALPHA_TEST_THRESHOLD = 0.5f;
        
        /// <summary>Threshold for fully opaque faces</summary>
        public const float ALPHA_PASS_THRESHOLD = 0.99f;
        
        /// <summary>Threshold for fully transparent faces</summary>
        public const float ALPHA_TRANSPARENT_THRESHOLD = 0.01f;
        #endregion

        #region Vertex Data Layout
        /// <summary>Number of components in a vertex position (X, Y, Z)</summary>
        public const int VERTEX_COMPONENTS = 3;
        
        /// <summary>Number of components in texture coordinates (U, V)</summary>
        public const int TEXCOORD_COMPONENTS = 2;
        
        /// <summary>Number of components in a normal vector (X, Y, Z)</summary>
        public const int NORMAL_COMPONENTS = 3;
        #endregion

        #region Picking
        /// <summary>Alpha channel value used for object picking</summary>
        public const byte PICKING_ALPHA_CHANNEL = 255;
        
        /// <summary>Picking background color component value</summary>
        public const float PICKING_BACKGROUND_COLOR = 1f;
        #endregion
    }
}
