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
using System.Collections.Generic;
using System.Linq;
using OpenMetaverse;

namespace Radegast.Rendering
{
    /// <summary>
    /// Manages shader programs for rendering
    /// </summary>
    public class ShaderManager : IDisposable
    {
        private readonly Dictionary<string, ShaderProgram> programs = new Dictionary<string, ShaderProgram>();
        private string activeShader = null;
        private bool disposed = false;

        /// <summary>
        /// Initialize and load all available shaders
        /// </summary>
        public bool Initialize()
        {
            if (!RenderSettings.HasShaders)
            {
                Logger.Debug("Shaders not supported, skipping shader initialization");
                return false;
            }

            try
            {
                // Get the shader directory path
                string shaderDir = GetShaderDirectory();
                if (!System.IO.Directory.Exists(shaderDir))
                {
                    Logger.Warn($"Shader directory not found: {shaderDir}");
                    return false;
                }

                Logger.Debug($"Shader directory: {shaderDir}");

                // Discover and load shader programs by grouping files with the same base name
                var shaderFiles = System.IO.Directory.GetFiles(shaderDir)
                    .Where(f => f.EndsWith(".vert", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".frag", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".geom", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".tesc", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".tese", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var groups = shaderFiles
                    .GroupBy(f => System.IO.Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                    .ToList();

                foreach (var g in groups)
                {
                    var baseName = g.Key;
                    var files = g.ToArray();
                    // Use just the filenames relative to shaderDir when calling LoadShader
                    var relativeNames = files.Select(f => System.IO.Path.GetFileName(f)).ToArray();
                    if (!LoadShader(baseName, relativeNames))
                    {
                        Logger.Debug($"Failed to load shader program: {baseName}");
                    }
                }

                Logger.Info($"Shader manager initialized with {programs.Count} shader(s)");
                return programs.Count > 0;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to initialize shaders: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the shader directory path
        /// </summary>
        private static string GetShaderDirectory()
        {
            // Try application base directory first
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string shaderDir = System.IO.Path.Combine(baseDir, "shader_data");
            
            if (System.IO.Directory.Exists(shaderDir))
            {
                return shaderDir;
            }

            // Try current directory as fallback
            shaderDir = System.IO.Path.Combine(Environment.CurrentDirectory, "shader_data");
            if (System.IO.Directory.Exists(shaderDir))
            {
                return shaderDir;
            }

            // Try parent directory (for when running from bin\Debug or bin\Release)
            string parentDir = System.IO.Directory.GetParent(baseDir)?.FullName;
            if (parentDir != null)
            {
                shaderDir = System.IO.Path.Combine(parentDir, "shader_data");
                if (System.IO.Directory.Exists(shaderDir))
                {
                    return shaderDir;
                }

                // Try going up one more level
                string grandParentDir = System.IO.Directory.GetParent(parentDir)?.FullName;
                if (grandParentDir != null)
                {
                    shaderDir = System.IO.Path.Combine(grandParentDir, "shader_data");
                    if (System.IO.Directory.Exists(shaderDir))
                    {
                        return shaderDir;
                    }
                }
            }

            // Return the expected path even if it doesn't exist
            return System.IO.Path.Combine(baseDir, "shader_data");
        }

        /// <summary>
        /// Load a shader program
        /// </summary>
        private bool LoadShader(string name, params string[] shaderFiles)
        {
            if (!RenderSettings.HasShaders) return false;

            try
            {
                string shaderDir = GetShaderDirectory();
                
                // Check if all shader files exist
                foreach (var shaderFile in shaderFiles)
                {
                    string fullPath = System.IO.Path.Combine(shaderDir, shaderFile);
                    if (!System.IO.File.Exists(fullPath))
                    {
                        Logger.Debug($"Shader file not found: {fullPath}");
                        return false;
                    }
                }

                var program = new ShaderProgram();
                
                // Pass full paths to the shader program
                var fullPaths = shaderFiles.Select(f => System.IO.Path.Combine(shaderDir, f)).ToArray();
                
                if (program.Load(fullPaths))
                {
                    programs[name] = program;
                    Logger.Debug($"Loaded shader program: {name}");
                    return true;
                }
                else
                {
                    program.Dispose();
                    Logger.Debug($"Failed to load shader program: {name}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error loading shader {name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start using a specific shader program
        /// </summary>
        public bool StartShader(string name)
        {
            if (!RenderSettings.HasShaders || disposed) return false;

            if (programs.TryGetValue(name, out var program))
            {
                try
                {
                    program.Start();
                    activeShader = name;
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error starting shader {name}: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Stop using shaders and return to fixed-function pipeline
        /// </summary>
        public void StopShader()
        {
            if (!RenderSettings.HasShaders || disposed) return;

            try
            {
                ShaderProgram.Stop();
                activeShader = null;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error stopping shader: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a shader program by name
        /// </summary>
        public ShaderProgram GetProgram(string name)
        {
            return programs.TryGetValue(name, out var program) ? program : null;
        }

        /// <summary>
        /// Check if a shader is currently active
        /// </summary>
        public bool IsShaderActive => !string.IsNullOrEmpty(activeShader);

        /// <summary>
        /// Get the name of the currently active shader
        /// </summary>
        public string ActiveShaderName => activeShader;

        /// <summary>
        /// Check if shaders are available
        /// </summary>
        public bool HasShaders => RenderSettings.HasShaders && programs.Count > 0;

        /// <summary>
        /// Set a uniform value for the active shader
        /// </summary>
        public bool SetUniform(string name, float value)
        {
            if (!IsShaderActive || !programs.TryGetValue(activeShader, out var program))
                return false;

            try
            {
                program.SetUniform1(name, value);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error setting uniform {name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set a uniform value for the active shader
        /// </summary>
        public bool SetUniform(string name, int value)
        {
            if (!IsShaderActive || !programs.TryGetValue(activeShader, out var program))
                return false;

            try
            {
                program.SetUniform1(name, value);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error setting uniform {name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Dispose of all shader programs
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;

            disposed = true;

            StopShader();

            foreach (var program in programs.Values)
            {
                try
                {
                    program?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error disposing shader program: {ex.Message}");
                }
            }

            programs.Clear();
        }
    }
}
