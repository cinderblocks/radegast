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

using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Holds the process-wide Silk.NET <see cref="GL"/> instance for the single Avalonia GL
/// rendering context.  Unlike OpenTK's static <c>GL</c> class, Silk.NET's API is
/// instance-based; this accessor preserves the existing static call style during the
/// OpenTK→Silk.NET migration (render methods do <c>var gl = GlApi.Gl;</c> once, then call
/// <c>gl.Foo(...)</c>).  Initialised in <c>GlViewportControl.GlInit</c> and resolved from
/// Avalonia's managed GL context via <see cref="Initialize"/>.
/// </summary>
internal static class GlApi
{
    /// <summary>
    /// The active Silk.NET GL API. Valid only between <see cref="Initialize"/> (GlInit) and
    /// context teardown; accessing it off the GL render thread or before init is undefined.
    /// </summary>
    public static GL Gl = null!;

    /// <summary>
    /// Binds the Silk.NET GL API to Avalonia's managed GL context by resolving entry points
    /// through the supplied <paramref name="getProcAddress"/> (Avalonia's
    /// <c>GlInterface.GetProcAddress</c>). Call once per context creation on the GL thread.
    /// </summary>
    public static void Initialize(System.Func<string, nint> getProcAddress)
        => Gl = GL.GetApi(new LamdaNativeContext(getProcAddress));
}
