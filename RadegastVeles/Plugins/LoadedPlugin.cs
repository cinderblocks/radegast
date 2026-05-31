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
using System.Runtime.Loader;
using Radegast.Veles.PluginApi;

namespace Radegast.Veles.Plugins;

/// <summary>The runtime state of a plugin.</summary>
public enum PluginState
{
    Loaded,
    Running,
    Stopped,
    Error
}

/// <summary>
/// Tracks a single loaded plugin instance together with its metadata,
/// load context, and runtime state.
/// </summary>
public sealed class LoadedPlugin
{
    /// <summary>Stable identifier derived from assembly path + type name.</summary>
    public string Id { get; }

    /// <summary>Full path to the assembly file that was loaded.</summary>
    public string FilePath { get; }

    /// <summary>The plugin instance.</summary>
    public IVelesPlugin Instance { get; }

    /// <summary>Metadata from the <see cref="VelesPluginAttribute"/>.</summary>
    public VelesPluginAttribute Metadata { get; }

    /// <summary>The <see cref="PluginContext"/> given to this plugin.</summary>
    internal PluginContext Context { get; }

    /// <summary>The isolated assembly load context (collectible for unload).</summary>
    internal AssemblyLoadContext? LoadContext { get; }

    /// <summary>Current lifecycle state.</summary>
    public PluginState State { get; internal set; }

    /// <summary>When the assembly was loaded.</summary>
    public DateTime LoadedAt { get; } = DateTime.UtcNow;

    /// <summary>Last error message, if <see cref="State"/> is <see cref="PluginState.Error"/>.</summary>
    public string? ErrorMessage { get; internal set; }

    internal LoadedPlugin(
        string id,
        string filePath,
        IVelesPlugin instance,
        VelesPluginAttribute metadata,
        PluginContext context,
        AssemblyLoadContext? loadContext)
    {
        Id = id;
        FilePath = filePath;
        Instance = instance;
        Metadata = metadata;
        Context = context;
        LoadContext = loadContext;
        State = PluginState.Loaded;
    }
}
