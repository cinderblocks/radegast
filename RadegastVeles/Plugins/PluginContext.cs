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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using LibreMetaverse;
using Microsoft.Extensions.Logging;
using Radegast.Veles.Core;
using Radegast.Veles.PluginApi;

namespace Radegast.Veles.Plugins;

/// <summary>
/// Implements <see cref="IPluginContext"/> for a specific plugin instance,
/// bridging the plugin API to the Veles application. Tracks all registrations
/// so they can be cleaned up automatically on detach.
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly PluginManager _manager;
    private readonly string _pluginId;
    private readonly List<string> _registeredCommands = new();
    private readonly List<string> _registeredMenuItems = new();
    private readonly List<string> _registeredPreferenceTabs = new();

    /// <summary>Display name of the owning plugin, set after construction.</summary>
    internal string PluginName { get; set; } = string.Empty;

    /// <summary>IDs of preference tabs registered by this plugin.</summary>
    internal IReadOnlyList<string> RegisteredPreferenceTabs => _registeredPreferenceTabs;

    // ── Bridged events ─────────────────────────────────────────────
    private EventHandler<ChatEventArgs>? _chatReceived;
    private EventHandler<InstantMessageEventArgs>? _imReceived;
    private EventHandler<EventArgs>? _connected;
    private EventHandler<EventArgs>? _disconnected;
    private EventHandler<PrimEventArgs>? _objectUpdated;
    private EventHandler<TeleportEventArgs>? _teleportProgress;
    private EventHandler<FriendInfoEventArgs>? _friendOnline;
    private EventHandler<FriendInfoEventArgs>? _friendOffline;
    private EventHandler<GroupChatJoinedEventArgs>? _groupChatJoined;

    public GridClient Client => _instance.Client;
    public INetCom NetCom => _instance.NetCom;
    public IRadegastInstance Instance => _instance;
    public IVoiceSynthService? VoiceSynth => _instance.Voice?.VoiceSynth.Service;

    public bool IsInP2PCall =>
        _instance.Voice?._voice?.GetActiveP2PCalls() is { Count: > 0 };

    public bool NoTypingAnim =>
        _instance.GlobalSettings["no_typing_anim"].AsBoolean();

    internal PluginContext(RadegastInstanceAvalonia instance, PluginManager manager, string pluginId)
    {
        _instance = instance;
        _manager = manager;
        _pluginId = pluginId;
    }

    // ── Commands ────────────────────────────────────────────────────

    public void RegisterCommand(string name, string description, string usage,
        Action<string[], Action<string>> execute)
    {
        _instance.CommandsManager.AddCmd(name, description, usage,
            (n, args, writeLine) => execute(args, msg => writeLine(msg)));
        _registeredCommands.Add(name);
    }

    public void UnregisterCommand(string name)
    {
        _instance.CommandsManager.RemoveCommand(name);
        _registeredCommands.Remove(name);
    }

    // ── Menu Items ─────────────────────────────────────────────────

    public void AddMenuItem(PluginMenuItemInfo item)
    {
        item.PluginName = PluginName;
        item.PluginId   = _pluginId;
        _manager.AddPluginMenuItem(item);
        _registeredMenuItems.Add(item.Id);
    }

    public void RemoveMenuItem(string id)
    {
        _manager.RemovePluginMenuItem(id);
        _registeredMenuItems.Remove(id);
    }

    // ── Preference Tabs ────────────────────────────────────────────

    public void AddPreferenceTab(PluginPreferenceTab tab)
    {
        _manager.AddPluginPreferenceTab(tab);
        _registeredPreferenceTabs.Add(tab.Id);
    }

    public void RemovePreferenceTab(string id)
    {
        _manager.RemovePluginPreferenceTab(id);
        _registeredPreferenceTabs.Remove(id);
    }

    // ── File Pickers ────────────────────────────────────────────────

    public Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
    {
        var topLevel = GetMainTopLevel();
        if (topLevel == null)
            return Task.FromResult<IReadOnlyList<IStorageFile>>(Array.Empty<IStorageFile>());
        return topLevel.StorageProvider.OpenFilePickerAsync(options);
    }

    public Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options)
    {
        var topLevel = GetMainTopLevel();
        if (topLevel == null)
            return Task.FromResult<IStorageFile?>(null);
        return topLevel.StorageProvider.SaveFilePickerAsync(options);
    }

    private static TopLevel? GetMainTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return TopLevel.GetTopLevel(desktop.MainWindow);
        return null;
    }

    // ── Notifications ──────────────────────────────────────────────

    public void ShowNotification(string title, string message)
    {
        VelesNotificationService.Show(title, message);
    }

    public void LogToChat(string message)
    {
        _instance.ShowNotificationInChat(message);
    }

    // ── Settings ───────────────────────────────────────────────────

    public void SetSetting(string key, string value)
    {
        string settingsKey = $"Plugin_{_pluginId}_{key}";
        _instance.GlobalSettings[settingsKey] = LibreMetaverse.StructuredData.OSD.FromString(value);
    }

    public string? GetSetting(string key)
    {
        string settingsKey = $"Plugin_{_pluginId}_{key}";
        var osd = _instance.GlobalSettings[settingsKey];
        if (osd.Type == LibreMetaverse.StructuredData.OSDType.Unknown)
            return null;
        return osd.AsString();
    }

    // ── Events ─────────────────────────────────────────────────────

    public event EventHandler<ChatEventArgs> ChatReceived
    {
        add
        {
            if (_chatReceived == null)
                _instance.Client.Self.ChatFromSimulator += OnChat;
            _chatReceived += value;
        }
        remove
        {
            _chatReceived -= value;
            if (_chatReceived == null)
                _instance.Client.Self.ChatFromSimulator -= OnChat;
        }
    }

    public event EventHandler<InstantMessageEventArgs> IMReceived
    {
        add
        {
            if (_imReceived == null)
                _instance.Client.Self.IM += OnIM;
            _imReceived += value;
        }
        remove
        {
            _imReceived -= value;
            if (_imReceived == null)
                _instance.Client.Self.IM -= OnIM;
        }
    }

    public event EventHandler<EventArgs> Connected
    {
        add
        {
            if (_connected == null)
                _instance.Client.Network.SimConnected += OnSimConnected;
            _connected += value;
        }
        remove
        {
            _connected -= value;
            if (_connected == null)
                _instance.Client.Network.SimConnected -= OnSimConnected;
        }
    }

    public event EventHandler<EventArgs> Disconnected
    {
        add
        {
            if (_disconnected == null)
                _instance.Client.Network.Disconnected += OnDisconnected;
            _disconnected += value;
        }
        remove
        {
            _disconnected -= value;
            if (_disconnected == null)
                _instance.Client.Network.Disconnected -= OnDisconnected;
        }
    }

    public event EventHandler<PrimEventArgs> ObjectUpdated
    {
        add
        {
            if (_objectUpdated == null)
                _instance.Client.Objects.ObjectUpdate += OnObjectUpdated;
            _objectUpdated += value;
        }
        remove
        {
            _objectUpdated -= value;
            if (_objectUpdated == null)
                _instance.Client.Objects.ObjectUpdate -= OnObjectUpdated;
        }
    }

    public event EventHandler<TeleportEventArgs> TeleportProgress
    {
        add
        {
            if (_teleportProgress == null)
                _instance.Client.Self.TeleportProgress += OnTeleportProgress;
            _teleportProgress += value;
        }
        remove
        {
            _teleportProgress -= value;
            if (_teleportProgress == null)
                _instance.Client.Self.TeleportProgress -= OnTeleportProgress;
        }
    }

    public event EventHandler<FriendInfoEventArgs> FriendOnline
    {
        add
        {
            if (_friendOnline == null)
                _instance.Client.Friends.FriendOnline += OnFriendOnline;
            _friendOnline += value;
        }
        remove
        {
            _friendOnline -= value;
            if (_friendOnline == null)
                _instance.Client.Friends.FriendOnline -= OnFriendOnline;
        }
    }

    public event EventHandler<FriendInfoEventArgs> FriendOffline
    {
        add
        {
            if (_friendOffline == null)
                _instance.Client.Friends.FriendOffline += OnFriendOffline;
            _friendOffline += value;
        }
        remove
        {
            _friendOffline -= value;
            if (_friendOffline == null)
                _instance.Client.Friends.FriendOffline -= OnFriendOffline;
        }
    }

    public event EventHandler<GroupChatJoinedEventArgs> GroupChatJoined
    {
        add
        {
            if (_groupChatJoined == null)
                _instance.Client.Self.GroupChatJoined += OnGroupChatJoined;
            _groupChatJoined += value;
        }
        remove
        {
            _groupChatJoined -= value;
            if (_groupChatJoined == null)
                _instance.Client.Self.GroupChatJoined -= OnGroupChatJoined;
        }
    }

    // Each of these is a direct subscriber on a shared GridClient multicast event, so an
    // unhandled exception here would propagate back into that event's invocation and stop
    // any other subscriber (other plugins, or Radegast's own internal handlers) from running.
    private void OnChat(object? sender, ChatEventArgs e)
    {
        try { _chatReceived?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' chat handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnIM(object? sender, InstantMessageEventArgs e)
    {
        try { _imReceived?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' IM handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnSimConnected(object? sender, SimConnectedEventArgs e)
    {
        try { _connected?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' sim-connected handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnDisconnected(object? sender, DisconnectedEventArgs e)
    {
        try { _disconnected?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' disconnected handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnObjectUpdated(object? sender, PrimEventArgs e)
    {
        try { _objectUpdated?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' object-updated handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnTeleportProgress(object? sender, TeleportEventArgs e)
    {
        try { _teleportProgress?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' teleport-progress handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnFriendOnline(object? sender, FriendInfoEventArgs e)
    {
        try { _friendOnline?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' friend-online handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnFriendOffline(object? sender, FriendInfoEventArgs e)
    {
        try { _friendOffline?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' friend-offline handler threw: {ex.Message}", LogLevel.Warning); }
    }

    private void OnGroupChatJoined(object? sender, GroupChatJoinedEventArgs e)
    {
        try { _groupChatJoined?.Invoke(this, e); }
        catch (Exception ex) { Logger.Log($"Plugin '{PluginName}' group-chat-joined handler threw: {ex.Message}", LogLevel.Warning); }
    }

    /// <summary>
    /// Remove all registrations made by the plugin. Called automatically
    /// during plugin detach to ensure clean teardown.
    /// </summary>
    internal void CleanUp()
    {
        // Unsubscribe bridged events
        if (_chatReceived != null)
            _instance.Client.Self.ChatFromSimulator -= OnChat;
        if (_imReceived != null)
            _instance.Client.Self.IM -= OnIM;
        if (_connected != null)
            _instance.Client.Network.SimConnected -= OnSimConnected;
        if (_disconnected != null)
            _instance.Client.Network.Disconnected -= OnDisconnected;
        if (_objectUpdated != null)
            _instance.Client.Objects.ObjectUpdate -= OnObjectUpdated;
        if (_teleportProgress != null)
            _instance.Client.Self.TeleportProgress -= OnTeleportProgress;
        if (_friendOnline != null)
            _instance.Client.Friends.FriendOnline -= OnFriendOnline;
        if (_friendOffline != null)
            _instance.Client.Friends.FriendOffline -= OnFriendOffline;
        if (_groupChatJoined != null)
            _instance.Client.Self.GroupChatJoined -= OnGroupChatJoined;

        _chatReceived = null;
        _imReceived = null;
        _connected = null;
        _disconnected = null;
        _objectUpdated = null;
        _teleportProgress = null;
        _friendOnline = null;
        _friendOffline = null;
        _groupChatJoined = null;

        // Unregister commands
        foreach (var cmd in _registeredCommands)
        {
            try { _instance.CommandsManager.RemoveCommand(cmd); }
            catch { /* best effort */ }
        }
        _registeredCommands.Clear();

        // Unregister menu items
        foreach (var mi in _registeredMenuItems)
        {
            try { _manager.RemovePluginMenuItem(mi); }
            catch { /* best effort */ }
        }
        _registeredMenuItems.Clear();

        // Unregister preference tabs
        foreach (var pt in _registeredPreferenceTabs)
        {
            try { _manager.RemovePluginPreferenceTab(pt); }
            catch { /* best effort */ }
        }
        _registeredPreferenceTabs.Clear();
    }
}
