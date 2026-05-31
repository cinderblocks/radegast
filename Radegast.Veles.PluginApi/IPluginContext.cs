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
using Avalonia.Platform.Storage;
using OpenMetaverse;

namespace Radegast.Veles.PluginApi;

/// <summary>
/// Provides the bridge between a plugin and the Veles application.
/// Passed to <see cref="IVelesPlugin.Attach"/> and used to register
/// commands, menu items, event handlers, and UI extensions.
/// </summary>
public interface IPluginContext
{
    // ── Client Access ──────────────────────────────────────────────

    /// <summary>The LibreMetaverse grid client for the current session.</summary>
    GridClient Client { get; }

    /// <summary>
    /// True when a P2P voice call is currently active with at least one other avatar.
    /// Use this to decide whether TTS replies should be spoken over voice.
    /// </summary>
    bool IsInP2PCall { get; }

    /// <summary>
    /// True when the user has disabled the in-world typing animation in Preferences.
    /// Plugins should check this before calling <see cref="IRadegastInstance"/>.State.SetTyping.
    /// </summary>
    bool NoTypingAnim { get; }

    /// <summary>Network communication helper.</summary>
    INetCom NetCom { get; }

    /// <summary>The core Radegast instance.</summary>
    IRadegastInstance Instance { get; }

    /// <summary>
    /// Built-in voice-synthesis service. Allows plugins to synthesize text and transmit
    /// it over the active WebRTC voice channel. Null when voice is unavailable.
    /// </summary>
    IVoiceSynthService? VoiceSynth { get; }

    // ── Commands ────────────────────────────────────────────────────

    /// <summary>Register a chat command.</summary>
    /// <param name="name">Command name (no prefix).</param>
    /// <param name="description">Short description.</param>
    /// <param name="usage">Usage string.</param>
    /// <param name="execute">
    /// Callback receiving the argument array and a write-line delegate for output.
    /// </param>
    void RegisterCommand(string name, string description, string usage,
        Action<string[], Action<string>> execute);

    /// <summary>Remove a previously registered command.</summary>
    void UnregisterCommand(string name);

    // ── Menu Items ─────────────────────────────────────────────────

    /// <summary>Add an item to the Plugins menu in the main window.</summary>
    void AddMenuItem(PluginMenuItemInfo item);

    /// <summary>Remove a previously added menu item by its id.</summary>
    void RemoveMenuItem(string id);

    // ── Preference Tabs ────────────────────────────────────────────

    /// <summary>Register a tab that appears in the Preferences window.</summary>
    void AddPreferenceTab(PluginPreferenceTab tab);

    /// <summary>Remove a previously registered preference tab.</summary>
    void RemovePreferenceTab(string id);

    // ── File Pickers ────────────────────────────────────────────────

    /// <summary>
    /// Show the platform file-open dialog. Must be called from the UI thread.
    /// Returns the selected files, or an empty list if cancelled.
    /// </summary>
    Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options);

    /// <summary>
    /// Show the platform file-save dialog. Must be called from the UI thread.
    /// Returns the chosen file, or null if cancelled.
    /// </summary>
    Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options);

    // ── Notifications ──────────────────────────────────────────────

    /// <summary>Show a toast/desktop notification.</summary>
    void ShowNotification(string title, string message);

    /// <summary>Log a message to the nearby chat panel.</summary>
    void LogToChat(string message);

    // ── Plugin Settings ────────────────────────────────────────────

    /// <summary>Persist a key/value setting for this plugin.</summary>
    void SetSetting(string key, string value);

    /// <summary>Retrieve a previously stored setting, or null.</summary>
    string? GetSetting(string key);

    // ── Events ─────────────────────────────────────────────────────

    /// <summary>Fired when a nearby chat message is received.</summary>
    event EventHandler<ChatEventArgs> ChatReceived;

    /// <summary>Fired when an instant message is received.</summary>
    event EventHandler<InstantMessageEventArgs> IMReceived;

    /// <summary>Fired when the client connects to a simulator.</summary>
    event EventHandler<EventArgs> Connected;

    /// <summary>Fired when the client disconnects.</summary>
    event EventHandler<EventArgs> Disconnected;

    /// <summary>Fired when an object or avatar update is received from the simulator.</summary>
    event EventHandler<PrimEventArgs> ObjectUpdated;

    /// <summary>Fired at each step of a teleport sequence.</summary>
    event EventHandler<TeleportEventArgs> TeleportProgress;

    /// <summary>Fired when a friend comes online.</summary>
    event EventHandler<FriendInfoEventArgs> FriendOnline;

    /// <summary>Fired when a friend goes offline.</summary>
    event EventHandler<FriendInfoEventArgs> FriendOffline;

    /// <summary>Fired when a group chat session is joined or updated.</summary>
    event EventHandler<GroupChatJoinedEventArgs> GroupChatJoined;
}
