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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Radegast.Veles.Plugins;

/// <summary>Item displayed in the plugin manager list.</summary>
public partial class PluginListItem : ObservableObject
{
    private readonly LoadedPlugin _plugin;

    public string Id => _plugin.Id;
    public string Name => _plugin.Metadata.Name;
    public string Version => _plugin.Metadata.Version;
    public string Author => _plugin.Metadata.Author;
    public string Description => _plugin.Metadata.Description;
    public string FilePath => _plugin.FilePath;
    public string FileName => Path.GetFileName(_plugin.FilePath);
    public DateTime LoadedAt => _plugin.LoadedAt;

    [ObservableProperty]
    private string _state;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canStart;

    [ObservableProperty]
    private bool _canStop;

    public PluginListItem(LoadedPlugin plugin)
    {
        _plugin = plugin;
        _state = plugin.State.ToString();
        _errorMessage = plugin.ErrorMessage;
        UpdateButtons();
    }

    internal void Refresh()
    {
        State = _plugin.State.ToString();
        ErrorMessage = _plugin.ErrorMessage;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        CanStart = _plugin.State is PluginState.Loaded or PluginState.Stopped or PluginState.Error;
        CanStop = _plugin.State == PluginState.Running;
    }
}

/// <summary>
/// ViewModel for the Plugin Manager window. Displays loaded plugins
/// and provides commands for start/stop/reload/unload operations.
/// </summary>
public partial class PluginManagerViewModel : ObservableObject, IDisposable
{
    private readonly PluginManager _manager;

    public ObservableCollection<PluginListItem> Plugins { get; } = new();

    [ObservableProperty]
    private PluginListItem? _selectedPlugin;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public string PluginDirectory => _manager.PluginDirectory;

    public PluginManagerViewModel(PluginManager manager)
    {
        _manager = manager;
        _manager.PluginLoaded += OnPluginLoaded;
        _manager.PluginUnloaded += OnPluginUnloaded;
        _manager.PluginStateChanged += OnPluginStateChanged;
        RefreshList();
    }

    private void RefreshList()
    {
        Plugins.Clear();
        foreach (var p in _manager.Plugins)
            Plugins.Add(new PluginListItem(p));
        StatusText = $"{_manager.Plugins.Count} plugin(s) loaded";
    }

    [RelayCommand]
    private void Start()
    {
        if (SelectedPlugin == null) return;
        _manager.StartPlugin(SelectedPlugin.Id);
    }

    [RelayCommand]
    private void Stop()
    {
        if (SelectedPlugin == null) return;
        _manager.StopPlugin(SelectedPlugin.Id);
    }

    [RelayCommand]
    private void Reload()
    {
        if (SelectedPlugin == null) return;
        _manager.ReloadPlugin(SelectedPlugin.Id);
    }

    [RelayCommand]
    private void Unload()
    {
        if (SelectedPlugin == null) return;
        _manager.UnloadPlugin(SelectedPlugin.Id);
    }

    [RelayCommand]
    private void StartAll() => _manager.StartAll();

    [RelayCommand]
    private void StopAll() => _manager.StopAll();

    [RelayCommand]
    private void Rescan()
    {
        _manager.LoadPluginsFromDirectory();
        RefreshList();
        StatusText = $"Rescanned. {_manager.Plugins.Count} plugin(s) loaded.";
    }

    [RelayCommand]
    private void OpenPluginFolder()
    {
        try
        {
            if (!Directory.Exists(_manager.PluginDirectory))
                Directory.CreateDirectory(_manager.PluginDirectory);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _manager.PluginDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open folder: {ex.Message}";
        }
    }

    private void OnPluginLoaded(object? sender, LoadedPlugin e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Plugins.All(p => p.Id != e.Id))
                Plugins.Add(new PluginListItem(e));
            StatusText = $"Loaded: {e.Metadata.Name}";
        });
    }

    private void OnPluginUnloaded(object? sender, LoadedPlugin e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var item = Plugins.FirstOrDefault(p => p.Id == e.Id);
            if (item != null) Plugins.Remove(item);
            StatusText = $"Unloaded: {e.Metadata.Name}";
        });
    }

    private void OnPluginStateChanged(object? sender, LoadedPlugin e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var item = Plugins.FirstOrDefault(p => p.Id == e.Id);
            item?.Refresh();
        });
    }

    public void Dispose()
    {
        _manager.PluginLoaded -= OnPluginLoaded;
        _manager.PluginUnloaded -= OnPluginUnloaded;
        _manager.PluginStateChanged -= OnPluginStateChanged;
    }
}
