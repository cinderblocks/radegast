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
using System.IO;
using OpenMetaverse;
using OpenTK.Graphics.OpenGL;

namespace Radegast.Rendering
{
    public class Shader : IDisposable
    {
        public int ID = -1;

        public bool Load(string fileName)
        {
            if (!RenderSettings.HasShaders) return false;

            try
            {
                if (!File.Exists(fileName))
                {
                    Logger.DebugLog($"Shader file not found: {fileName}");
                    return false;
                }

                ShaderType type;

                string code = File.ReadAllText(fileName);
                if (fileName.EndsWith(".vert"))
                {
                    type = ShaderType.VertexShader;
                }
                else if (fileName.EndsWith(".frag"))
                {
                    type = ShaderType.FragmentShader;
                }
                else
                {
                    Logger.DebugLog($"Unknown shader type for file: {fileName}");
                    return false;
                }

                ID = GL.CreateShader(type);
                GL.ShaderSource(ID, code);
                GL.CompileShader(ID);
                string info = GL.GetShaderInfoLog(ID);
                int res;
                GL.GetShader(ID, ShaderParameter.CompileStatus, out res);
                if (res != 1)
                {
                    Logger.DebugLog($"Shader compilation failed for {fileName}: {info}");
                    ID = -1;
                    return false;
                }
                Logger.DebugLog($"{type} {Path.GetFileName(fileName)} compiled successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.DebugLog($"Error loading shader {fileName}: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (!RenderSettings.HasShaders) return;

            if (ID == -1) return;
            GL.DeleteShader(ID);
        }
    }

    public class ShaderProgram : IDisposable
    {
        public static int CurrentProgram = 0;
        public int ID = -1;
        private Shader[] shaders;

        public bool Load(params string[] shaderNames)
        {
            if (!RenderSettings.HasShaders) return false;

            shaders = new Shader[shaderNames.Length];
            for (int i = 0; i < shaderNames.Length; i++)
            {
                Shader s = new Shader();
                
                // Check if the path is already absolute or contains directory separators
                string shaderPath = shaderNames[i];
                if (!Path.IsPathRooted(shaderPath) && !shaderPath.Contains(Path.DirectorySeparatorChar.ToString()))
                {
                    // Only add shader_data prefix if it's a simple filename
                    shaderPath = Path.Combine("shader_data", shaderNames[i]);
                }
                
                if (!s.Load(shaderPath))
                    return false;
                shaders[i] = s;
            }

            ID = GL.CreateProgram();
            foreach (var shader in shaders)
            {
                GL.AttachShader(ID, shader.ID);
            }
            GL.LinkProgram(ID);
            int res;
            GL.GetProgram(ID, GetProgramParameterName.LinkStatus, out res);
            if (res != 1)
            {
                Logger.DebugLog("Linking shader program failed!");
                return false;
            }

            Logger.DebugLog($"Linking shader program consisting of {shaders.Length} shaders successful");
            return true;
        }

        public void Start()
        {
            if (!RenderSettings.HasShaders) return;

            if (ID != -1)
            {
                GL.UseProgram(ID);
                CurrentProgram = ID;
            }
        }

        public static void Stop()
        {
            if (!RenderSettings.HasShaders) return;
            
            if (CurrentProgram != 0)
            {
                GL.UseProgram(0);
            }
        }

        public int Uni(string var)
        {
            // Avoid querying uniforms on an invalid program ID which can cause GL errors
            if (!RenderSettings.HasShaders || ID < 1) return -1;

            return GL.GetUniformLocation(ID, var);
        }

        public int Attr(string name)
        {
            if (!RenderSettings.HasShaders) return -1;
            if (ID < 1) return -1;
            return GL.GetAttribLocation(ID, name);
        }

        public void SetUniform1(string var, int value)
        {
            if (!RenderSettings.HasShaders || ID < 1) return;
            int varID = Uni(var);
            if (varID == -1)
            {
                return;
            }
            GL.Uniform1(varID, value);
        }

        public void SetUniform1(string var, float value)
        {
            if (!RenderSettings.HasShaders || ID < 1) return;
            int varID = Uni(var);
            if (varID == -1)
            {
                return;
            }
            GL.Uniform1(varID, value);
        }

        public void Dispose()
        {
            if (!RenderSettings.HasShaders) return;

            if (ID != -1)
            {
                if (CurrentProgram == ID)
                {
                    GL.UseProgram(0);
                }
                GL.DeleteProgram(ID);
                ID = -1;
            }

            if (shaders != null)
            {
                foreach (var shader in shaders)
                {
                    shader.Dispose();
                }
            }
            shaders = null;
        }
    }
}
