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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Radegast.Commands;

namespace Radegast.Veles.ViewModels;

public partial class KeyboardShortcutsViewModel : ObservableObject
{
    public ObservableCollection<ShortcutEntry> Shortcuts { get; } = new();

    public KeyboardShortcutsViewModel(CommandsManager commandsManager)
    {
        // ── Built-in keyboard shortcuts ──────────────────────────────────
        AddBuiltIn("Enter",           "Send nearby chat (normal)");
        AddBuiltIn("Shift+Enter",     "Whisper nearby chat");
        AddBuiltIn("Ctrl+Enter",      "Shout nearby chat");
        AddBuiltIn("Ctrl+↑",          "Recall previous chat entry");
        AddBuiltIn("Ctrl+↓",          "Recall next chat entry");
        AddBuiltIn("Ctrl+Tab",        "Switch to next tab");
        AddBuiltIn("Ctrl+Shift+Tab",  "Switch to previous tab");
        AddBuiltIn("Ctrl+Shift+H",    "Teleport Home");
        AddBuiltIn("Ctrl+Alt+D",      "Open Log Viewer / Debug Console");
        AddBuiltIn("LeftAlt (hold)",  "Push-to-Talk (default PTT key)");

        // ── Slash commands ───────────────────────────────────────────────
        lock (commandsManager.CommandsLoaded)
        {
            foreach (var cmd in commandsManager.CommandsLoaded)
            {
                Shortcuts.Add(new ShortcutEntry(
                    CommandsManager.CmdPrefix + cmd.Name,
                    cmd.Description,
                    CommandsManager.CmdPrefix + cmd.Usage));
            }
        }
    }

    private void AddBuiltIn(string keys, string description)
        => Shortcuts.Add(new ShortcutEntry(keys, description, string.Empty));
}

public record ShortcutEntry(string Command, string Description, string Usage);
