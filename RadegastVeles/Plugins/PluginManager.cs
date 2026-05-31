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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.PluginApi;

namespace Radegast.Veles.Plugins;

/// <summary>
/// Manages the lifecycle of Veles plugins: discovery, loading, starting,
/// stopping, unloading, reloading, and blacklisting.
/// </summary>
public sealed class PluginManager : IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly List<LoadedPlugin> _plugins = new();
    private readonly HashSet<string> _loadedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blacklistedPatterns;
    private readonly string _pluginDirectory;

    private static readonly string[] DefaultBlacklistExtensions =
        [".pdb", ".xml", ".json", ".config", ".deps.json"];

    private static readonly string[] DefaultBlacklistFiles =
    [
        "RadegastVeles.dll",
        "Radegast.Core.dll",
        "Radegast.Veles.PluginApi.dll",
        "LibreMetaverse.dll",
        "LibreMetaverse.Types.dll",
        "Avalonia.dll"
    ];

    /// <summary>All currently loaded plugins.</summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>Menu items contributed by plugins. Observed by the UI.</summary>
    public ObservableCollection<PluginMenuItemInfo> MenuItems { get; } = new();

    /// <summary>Preference tabs contributed by plugins.</summary>
    public ObservableCollection<PluginPreferenceTab> PreferenceTabs { get; } = new();

    /// <summary>Raised after a plugin is successfully loaded.</summary>
    public event EventHandler<LoadedPlugin>? PluginLoaded;

    /// <summary>Raised after a plugin is unloaded.</summary>
    public event EventHandler<LoadedPlugin>? PluginUnloaded;

    /// <summary>Raised when a plugin's state changes.</summary>
    public event EventHandler<LoadedPlugin>? PluginStateChanged;

    public PluginManager(RadegastInstanceAvalonia instance, string? pluginDirectory = null)
    {
        _instance = instance;
        _pluginDirectory = pluginDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "plugins");

        _blacklistedPatterns = new HashSet<string>(
            DefaultBlacklistFiles, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scan the plugin directory and load all valid plugin assemblies found.
    /// </summary>
    public void LoadPluginsFromDirectory(string? directory = null)
    {
        string dir = directory ?? _pluginDirectory;
        if (!Directory.Exists(dir))
        {
            Logger.Log($"Plugin directory does not exist, creating: {dir}", LogLevel.Information);
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                Logger.Log($"Could not create plugin directory: {ex.Message}", LogLevel.Warning);
            }
            return;
        }

        Logger.Log($"Scanning plugin directory: {dir}", LogLevel.Information);

        foreach (string file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
        {
            if (IsBlacklisted(file))
            {
                Logger.Log($"Skipping blacklisted file: {Path.GetFileName(file)}", LogLevel.Debug);
                continue;
            }

            try
            {
                LoadPlugin(file);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load plugin from {file}: {ex.Message}", LogLevel.Warning);
            }
        }
    }

    /// <summary>
    /// Load a single plugin assembly from disk.
    /// </summary>
    /// <returns>The loaded plugin, or null if the assembly contains no valid plugin.</returns>
    public LoadedPlugin? LoadPlugin(string assemblyPath)
    {
        string fullPath = Path.GetFullPath(assemblyPath);

        if (_loadedFiles.Contains(fullPath))
        {
            Logger.Log($"Plugin already loaded from: {fullPath}", LogLevel.Warning);
            return null;
        }

        if (IsBlacklisted(fullPath))
        {
            Logger.Log($"Assembly is blacklisted: {Path.GetFileName(fullPath)}", LogLevel.Warning);
            return null;
        }

        var loadContext = new PluginLoadContext(fullPath);
        Assembly assembly;
        try
        {
            assembly = loadContext.LoadFromAssemblyPath(fullPath);
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not load assembly {fullPath}: {ex.Message}", LogLevel.Warning);
            loadContext.Unload();
            return null;
        }

        // Find the plugin class: must have [VelesPlugin] and implement IVelesPlugin
        Type? pluginType = null;
        VelesPluginAttribute? attribute = null;

        foreach (var type in assembly.GetExportedTypes())
        {
            var attr = type.GetCustomAttribute<VelesPluginAttribute>();
            if (attr != null && typeof(IVelesPlugin).IsAssignableFrom(type))
            {
                pluginType = type;
                attribute = attr;
                break;
            }
        }

        if (pluginType == null || attribute == null)
        {
            Logger.Log($"No plugin type found in {Path.GetFileName(fullPath)}", LogLevel.Debug);
            loadContext.Unload();
            return null;
        }

        IVelesPlugin pluginInstance;
        try
        {
            pluginInstance = (IVelesPlugin)(Activator.CreateInstance(pluginType)
                ?? throw new InvalidOperationException($"Failed to create instance of {pluginType.FullName}"));
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not instantiate plugin {pluginType.FullName}: {ex.Message}", LogLevel.Warning);
            loadContext.Unload();
            return null;
        }

        string pluginId = $"{attribute.Name}_{Path.GetFileNameWithoutExtension(fullPath)}";
        var context = new PluginContext(_instance, this, pluginId) { PluginName = attribute.Name };
        var loaded = new LoadedPlugin(pluginId, fullPath, pluginInstance, attribute, context, loadContext);

        _plugins.Add(loaded);
        _loadedFiles.Add(fullPath);

        Logger.Log($"Loaded plugin: {attribute.Name} v{attribute.Version} by {attribute.Author} from {Path.GetFileName(fullPath)}", LogLevel.Information);

        PluginLoaded?.Invoke(this, loaded);
        return loaded;
    }

    /// <summary>Start a loaded plugin (calls <see cref="IVelesPlugin.Attach"/>).</summary>
    public bool StartPlugin(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return false;

        if (plugin.State == PluginState.Running) return true;

        try
        {
            plugin.Instance.Attach(plugin.Context);
            plugin.State = PluginState.Running;
            plugin.ErrorMessage = null;
            Logger.Log($"Started plugin: {plugin.Metadata.Name}", LogLevel.Information);
            PluginStateChanged?.Invoke(this, plugin);
            return true;
        }
        catch (Exception ex)
        {
            plugin.State = PluginState.Error;
            plugin.ErrorMessage = ex.Message;
            Logger.Log($"Error starting plugin {plugin.Metadata.Name}: {ex.Message}", LogLevel.Warning);
            PluginStateChanged?.Invoke(this, plugin);
            return false;
        }
    }

    /// <summary>Stop a running plugin (calls <see cref="IVelesPlugin.Detach"/>).</summary>
    public bool StopPlugin(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return false;

        if (plugin.State != PluginState.Running) return true;

        try
        {
            plugin.Instance.Detach();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during plugin detach {plugin.Metadata.Name}: {ex.Message}", LogLevel.Warning);
        }

        // Always clean up registrations even if Detach threw
        plugin.Context.CleanUp();
        plugin.State = PluginState.Stopped;
        Logger.Log($"Stopped plugin: {plugin.Metadata.Name}", LogLevel.Information);
        PluginStateChanged?.Invoke(this, plugin);
        return true;
    }

    /// <summary>Unload a plugin completely, freeing its assembly.</summary>
    public bool UnloadPlugin(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return false;

        // Stop first if running
        if (plugin.State == PluginState.Running)
            StopPlugin(pluginId);

        try
        {
            plugin.Instance.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error disposing plugin {plugin.Metadata.Name}: {ex.Message}", LogLevel.Warning);
        }

        _plugins.Remove(plugin);
        _loadedFiles.Remove(plugin.FilePath);

        // Unload the assembly context
        plugin.LoadContext?.Unload();

        Logger.Log($"Unloaded plugin: {plugin.Metadata.Name}", LogLevel.Information);
        PluginUnloaded?.Invoke(this, plugin);
        return true;
    }

    /// <summary>Reload a plugin by unloading and re-loading from the same file.</summary>
    public LoadedPlugin? ReloadPlugin(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return null;

        string filePath = plugin.FilePath;
        bool wasRunning = plugin.State == PluginState.Running;

        UnloadPlugin(pluginId);

        var reloaded = LoadPlugin(filePath);
        if (reloaded != null && wasRunning)
        {
            StartPlugin(reloaded.Id);
        }
        return reloaded;
    }

    /// <summary>Start all loaded plugins that are not yet running.</summary>
    public void StartAll()
    {
        foreach (var plugin in _plugins.ToArray())
        {
            if (plugin.State == PluginState.Loaded || plugin.State == PluginState.Stopped)
                StartPlugin(plugin.Id);
        }
    }

    /// <summary>Stop all running plugins.</summary>
    public void StopAll()
    {
        foreach (var plugin in _plugins.ToArray())
        {
            if (plugin.State == PluginState.Running)
                StopPlugin(plugin.Id);
        }
    }

    // ── Blacklist ──────────────────────────────────────────────────

    public void AddBlacklistPattern(string pattern) =>
        _blacklistedPatterns.Add(pattern);

    public void RemoveBlacklistPattern(string pattern) =>
        _blacklistedPatterns.Remove(pattern);

    public bool IsBlacklisted(string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        // Check extension blacklist
        foreach (var ext in DefaultBlacklistExtensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check filename blacklist
        return _blacklistedPatterns.Contains(fileName);
    }

    // ── Plugin UI contributions ────────────────────────────────────

    internal void AddPluginMenuItem(PluginMenuItemInfo item)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => MenuItems.Add(item));
    }

    internal void RemovePluginMenuItem(string id)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var item = MenuItems.FirstOrDefault(m => m.Id == id);
            if (item != null) MenuItems.Remove(item);
        });
    }

    internal void AddPluginPreferenceTab(PluginPreferenceTab tab)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PreferenceTabs.Add(tab));
    }

    internal void RemovePluginPreferenceTab(string id)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var tab = PreferenceTabs.FirstOrDefault(t => t.Id == id);
            if (tab != null) PreferenceTabs.Remove(tab);
        });
    }

    /// <summary>
    /// Returns all <see cref="PluginPreferenceTab"/>s that were registered by
    /// the plugin with the given <paramref name="pluginId"/>.
    /// </summary>
    public IReadOnlyList<PluginPreferenceTab> GetPreferenceTabsForPlugin(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return [];

        var tabIds = plugin.Context.RegisteredPreferenceTabs;
        return PreferenceTabs.Where(t => tabIds.Contains(t.Id)).ToList();
    }

    /// <summary>The directory where plugins are loaded from.</summary>
    public string PluginDirectory => _pluginDirectory;

    public void Dispose()
    {
        StopAll();
        foreach (var plugin in _plugins.ToArray())
        {
            try
            {
                plugin.Instance.Dispose();
                plugin.LoadContext?.Unload();
            }
            catch { /* best effort during shutdown */ }
        }
        _plugins.Clear();
        _loadedFiles.Clear();
        MenuItems.Clear();
        PreferenceTabs.Clear();
    }
}
