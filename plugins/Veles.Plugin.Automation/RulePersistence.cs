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
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Veles.Plugin.Automation;

/// <summary>
/// Loads and persists <see cref="AutomationRule"/> lists to a JSON file on disk.
/// One file per plugin-settings directory, keyed by plugin ID to support multi-session use.
/// </summary>
internal static class RulePersistence
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Load rules from <paramref name="filePath"/>.
    /// Returns an empty list if the file does not exist or cannot be parsed.
    /// </summary>
    public static List<AutomationRule> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return [];
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<AutomationRule>>(json, s_options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Persist <paramref name="rules"/> to <paramref name="filePath"/>.
    /// Silently swallows I/O errors (non-fatal).
    /// </summary>
    public static void Save(string filePath, List<AutomationRule> rules)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(rules, s_options));
        }
        catch { }
    }

    /// <summary>
    /// Return the default (non-avatar-specific) path for the rules file.
    /// Used as a fallback before the first connection.
    /// </summary>
    public static string DefaultPath(string pluginId)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RadegastVeles", "plugins", pluginId);
        return Path.Combine(dir, "rules.json");
    }

    /// <summary>
    /// Return the per-avatar rules path keyed by <paramref name="agentId"/>.
    /// Each avatar logged in at the same time gets its own rules file.
    /// </summary>
    public static string AvatarPath(string agentId)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RadegastVeles", "plugins", "automation", "agents", agentId);
        return Path.Combine(dir, "rules.json");
    }

    /// <summary>
    /// Export <paramref name="rules"/> to an arbitrary user-chosen <paramref name="filePath"/>.
    /// The file format is identical to the internal save format.
    /// </summary>
    public static void Export(string filePath, List<AutomationRule> rules)
        => Save(filePath, rules);

    /// <summary>
    /// Import rules from an arbitrary user-chosen <paramref name="filePath"/>.
    /// Returns an empty list on any error.
    /// </summary>
    public static List<AutomationRule> Import(string filePath)
        => Load(filePath);
}
