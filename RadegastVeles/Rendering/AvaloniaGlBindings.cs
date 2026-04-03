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

using System;
using OpenTK;
using AvaloniaGlInterface = Avalonia.OpenGL.GlInterface;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Bridges Avalonia's <see cref="Avalonia.OpenGL.GlInterface"/> to OpenTK's
/// <see cref="IBindingsContext"/> so that <c>OpenTK.Graphics.OpenGL4.GL</c>
/// can resolve function pointers from Avalonia's managed GL context.
/// Call <c>GL.LoadBindings(new AvaloniaGlBindings(gl))</c> inside
/// <c>OnOpenGlInit</c>.
/// </summary>
internal sealed class AvaloniaGlBindings : IBindingsContext
{
    private readonly AvaloniaGlInterface _gl;

    public AvaloniaGlBindings(AvaloniaGlInterface gl) => _gl = gl;

    public IntPtr GetProcAddress(string procName) => _gl.GetProcAddress(procName);
}
