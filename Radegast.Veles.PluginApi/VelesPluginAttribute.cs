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

namespace Radegast.Veles.PluginApi;

/// <summary>
/// Marks a class as a Veles plugin. The class must also implement <see cref="IVelesPlugin"/>.
/// Only one plugin class per assembly is supported.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class VelesPluginAttribute : Attribute
{
    /// <summary>Human-readable plugin name.</summary>
    public string Name { get; }

    /// <summary>Short description of what the plugin does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Plugin author or organization.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Plugin version string (e.g. "1.0.0").</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Optional URL for the plugin's homepage or repository.</summary>
    public string Url { get; set; } = string.Empty;

    public VelesPluginAttribute(string name)
    {
        Name = name;
    }
}
