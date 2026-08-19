/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

// Vulkan replacement for GlShader.cs's compile/link/uniform-cache role -- see plan Section 7.
// Much thinner than GlShader by design: Vulkan shader modules don't compile GLSL text at
// runtime (that already happened at build time, see RadegastVeles.csproj's ShaderCompile
// items / plan Section 4), and there's no uniform-location caching to do -- data flows
// through descriptor sets and push constants instead of glUniform* calls, both bound via
// command-buffer calls in the render code, not through this class. This class's only job
// is turning a .spv file into a VkShaderModule.

using System;
using System.IO;
using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkShaderModule : IDisposable
{
    private readonly VkContext _vk;
    private ShaderModule _module;
    private bool _disposed;

    internal ShaderModule Handle => _module;

    private VkShaderModule(VkContext vk, ShaderModule module)
    {
        _vk = vk;
        _module = module;
    }

    /// <summary>
    /// Loads a compiled .spv file from <paramref name="path"/> (relative to
    /// <see cref="AppContext.BaseDirectory"/> -- ShaderCompile's <c>output_kind=content</c>
    /// copies compiled shaders to the output directory as loose files, not embedded
    /// AvaloniaResource assets like the GL originals; see plan Section 4's correction).
    /// </summary>
    public static VkShaderModule LoadFromFile(VkContext vk, string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        var bytes = File.ReadAllBytes(fullPath);
        return LoadFromBytes(vk, bytes);
    }

    public static VkShaderModule LoadFromBytes(VkContext vk, ReadOnlySpan<byte> spirv)
    {
        fixed (byte* ptr = spirv)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)ptr
            };
            vk.Api.CreateShaderModule(vk.Device, in createInfo, null, out var module).ThrowOnError();
            return new VkShaderModule(vk, module);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.Api.DestroyShaderModule(_vk.Device, _module, null);
    }
}
