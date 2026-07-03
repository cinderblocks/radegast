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
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibreMetaverse;
using Radegast.Veles.PluginApi;
using Veles.Plugin.Macros.UI;

namespace Veles.Plugin.Macros;

[VelesPlugin("Macros",
    Description = "Define and run named sequences of in-world actions (say, emote, IM, gesture, wait, …).",
    Author      = "Sjofn LLC",
    Version     = "1.0.0",
    Url         = "https://radegast.life/")]
public sealed class MacroPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;
    private List<Macro> _macros = [];
    private string _filePath = string.Empty;
    private readonly MacroRunner _runner = new();
    private bool _disposed;

    // ── Lifecycle ──────────────────────────────────────────────────

    public void Attach(IPluginContext context)
    {
        _ctx = context;

        _filePath = MacroPersistence.DefaultPath();
        if (_ctx.Client.Network.Connected && _ctx.Client.Self.AgentID != UUID.Zero)
            _filePath = MacroPersistence.AvatarPath(_ctx.Client.Self.AgentID.ToString());

        _macros = MacroPersistence.Load(_filePath);

        _ctx.RegisterCommand("macro", "Run, list, or stop macros",
            "macro list | run <name> | stop",
            OnMacroCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("macros_open", "Macros…", OpenMacrosWindow));

        _ctx.AddPreferenceTab(new PluginPreferenceTab(
            "macros", "Macros",
            BuildPanel));

        _ctx.Connected    += OnConnected;
        _ctx.Disconnected += OnDisconnected;

        _ctx.LogToChat($"[Macros] Plugin attached — {_macros.Count} macro(s) loaded.");
    }

    public void Detach()
    {
        _runner.Stop();
        _ctx.Connected    -= OnConnected;
        _ctx.Disconnected -= OnDisconnected;
        _ctx.RemoveMenuItem("macros_open");
        _ctx.RemovePreferenceTab("macros");
        MacroPersistence.Save(_filePath, _macros);
        _ctx.LogToChat("[Macros] Plugin detached.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runner.Dispose();
    }

    // ── Session events ─────────────────────────────────────────────

    private void OnConnected(object? sender, EventArgs e)
    {
        var avatarPath = MacroPersistence.AvatarPath(_ctx.Client.Self.AgentID.ToString());
        if (avatarPath == _filePath) return;
        _filePath = avatarPath;
        var loaded = MacroPersistence.Load(_filePath);
        _macros.Clear();
        _macros.AddRange(loaded);
    }

    private void OnDisconnected(object? sender, EventArgs e) => _runner.Stop();

    // ── //macro command ────────────────────────────────────────────

    private void OnMacroCommand(string[] args, Action<string> w)
    {
        if (args.Length == 0) { PrintHelp(w); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                if (_macros.Count == 0) { w("[Macros] No macros defined."); return; }
                foreach (var m in _macros)
                    w($"  [{m.Id}] {(m.Enabled ? "ON " : "off")}  \"{m.Name}\"  ({m.Steps.Count} steps)");
                break;

            case "run":
            {
                if (args.Length < 2) { w("Usage: macro run <name-or-id>"); return; }
                var query = string.Join(" ", args[1..]).Trim();
                var macro = FindMacro(query);
                if (macro == null)          { w($"[Macros] No enabled macro matching \"{query}\"."); return; }
                w($"[Macros] Running \"{macro.Name}\"…");
                _ = _runner.RunAsync(macro, _ctx);
                break;
            }

            case "stop":
                _runner.Stop();
                w("[Macros] Stopped.");
                break;

            default:
                PrintHelp(w);
                break;
        }
    }

    private static void PrintHelp(Action<string> w)
    {
        w("Usage: macro list | run <name-or-id> | stop");
        w("  list         — show all macros");
        w("  run <name>   — run a macro by name or ID prefix (case-insensitive)");
        w("  stop         — cancel the currently running macro");
    }

    private Macro? FindMacro(string query)
    {
        // Exact name match (case-insensitive) first, then ID prefix, then partial name
        return _macros.FirstOrDefault(m => m.Enabled &&
               m.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? _macros.FirstOrDefault(m => m.Enabled &&
               m.Id.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            ?? _macros.FirstOrDefault(m => m.Enabled &&
               m.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    // ── Window ────────────────────────────────────────────────────

    private void OpenMacrosWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var panel = BuildPanel();
            var win = new Window
            {
                Title  = "Macros",
                Width  = 560,
                Height = 420,
                Content = panel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            win.Show();
        });
    }

    // ── Preference tab builder ─────────────────────────────────────

    private Avalonia.Controls.Control BuildPanel()
    {
        var panel = new MacrosPanel(
            _macros,
            _runner,
            save:   Save,
            import: ImportAsync,
            export: ExportAsync,
            editorFactory: m => new MacroEditorWindow(m));

        panel.MacroRunRequested += macro =>
            _ = _runner.RunAsync(macro, _ctx);

        return panel;
    }

    private void Save() => MacroPersistence.Save(_filePath, _macros);

    // ── Import / Export ────────────────────────────────────────────

    private async void ImportAsync()
    {
        var files = await _ctx.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Macros",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON Macros") { Patterns = ["*.json"] },
                new FilePickerFileType("All Files")   { Patterns = ["*.*"] },
            ]
        });

        if (files is not [var file]) return;
        var imported = MacroPersistence.Import(file.Path.LocalPath);
        if (imported.Count == 0)
        {
            _ctx.LogToChat("[Macros] Import: no macros found or file could not be parsed.");
            return;
        }
        _macros.Clear();
        _macros.AddRange(imported);
        MacroPersistence.Save(_filePath, _macros);
        _ctx.LogToChat($"[Macros] Imported {imported.Count} macro(s) from {file.Name}.");
    }

    private async void ExportAsync()
    {
        var file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title            = "Export Macros",
            SuggestedFileName = "macros.json",
            DefaultExtension = "json",
            FileTypeChoices  =
            [
                new FilePickerFileType("JSON Macros") { Patterns = ["*.json"] },
            ]
        });

        if (file == null) return;
        MacroPersistence.Export(file.Path.LocalPath, _macros);
        _ctx.LogToChat($"[Macros] Exported {_macros.Count} macro(s) to {file.Name}.");
    }
}
