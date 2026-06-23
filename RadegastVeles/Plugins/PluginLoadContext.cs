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
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Radegast.Veles.Plugins;

/// <summary>
/// A collectible <see cref="AssemblyLoadContext"/> that isolates a plugin
/// assembly and allows it to be unloaded at runtime.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // If the default (host) context already has this assembly loaded,
        // defer to it so that type identities are shared between the host
        // and all plugin contexts. This prevents "Method not found" errors
        // on interface members (e.g. IPluginContext.add_ChatReceived).
        // The check is fully dynamic — no hardcoded names or prefixes needed.
        string name = assemblyName.Name ?? string.Empty;
        bool loadedByHost = Default.Assemblies.Any(
            a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));

        if (loadedByHost)
            return null;

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
            return LoadFromAssemblyPath(assemblyPath);

        // Fall back to the default context for anything else not found locally.
        return null;
    }
}
