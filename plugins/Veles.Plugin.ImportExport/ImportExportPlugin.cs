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
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibreMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.ImportExport;

[VelesPlugin("Import/Export",
    Description = "Export linksets (with textures) to disk and re-import them in-world.",
    Author = "Sjofn LLC",
    Version = "1.0.0",
    Url = "https://radegast.life/")]
public sealed class ImportExportPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;

    // ── IVelesPlugin ───────────────────────────────────────────────────────

    public void Attach(IPluginContext context)
    {
        _ctx = context;

        _ctx.RegisterCommand("export", "Export an in-world object to a .vobj archive",
            "export <localID> [path]  — exports the linkset containing localID.\n" +
            "  localID : local object ID (shown in object inspector or nearby objects list)\n" +
            "  path    : optional output path (default: ~/VelesExports/object_<localID>.vobj)",
            OnExportCommand);

        _ctx.RegisterCommand("import", "Import a previously exported .vobj archive",
            "import <path> [x y z]  — rezzes the object from a .vobj archive at current position + optional offset.\n" +
            "  path  : .vobj file written by 'export'\n" +
            "  x y z : optional XYZ offset from current position (default 0 2 0)\n" +
            "Textures are applied using their original UUIDs (fastest, requires asset server cache).",
            OnImportCommand);

        _ctx.RegisterCommand("importtex", "Import a .vobj archive and re-upload its textures",
            "importtex <path> [x y z]  — like 'import' but re-uploads the bundled JP2 textures to inventory first.\n" +
            "Useful when the original asset UUIDs are no longer in the asset server.",
            OnImportTexCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_export",
            "Import/Export: Export Object…", OnExportMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_import",
            "Import/Export: Import Object…", OnImportMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_importtex",
            "Import/Export: Import Object (Re-upload Textures)…", OnImportTexMenuClick));

        _ctx.LogToChat("[Import/Export] Plugin attached. Commands: export, import, importtex");
    }

    public void Detach()
    {
        _ctx.LogToChat("[Import/Export] Plugin detached.");
    }

    public void Dispose() { }

    // ── Menu item handlers ─────────────────────────────────────────────────

    private void OnExportMenuClick()
    {
        _ = Task.Run(async () =>
        {
            string localIdStr = string.Empty;

            // Ask for the localID via SaveFilePicker with a suggested name pattern.
            IStorageFile? file = null;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export object — enter localID as filename",
                    SuggestedFileName = "object_0.vobj",
                    DefaultExtension = ".vobj",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Veles Object Archive") { Patterns = ["*.vobj"] }
                    ]
                });
            });

            if (file == null) return;

            string archivePath = file.Path.LocalPath;
            string nameNoExt = Path.GetFileNameWithoutExtension(archivePath);

            // Try to parse localID from the filename ("object_12345" or just "12345").
            string[] parts = nameNoExt.Split('_');
            string candidate = parts[^1];
            if (!uint.TryParse(candidate, out uint localId))
            {
                _ctx.LogToChat("[Import/Export] Export cancelled: could not determine localID from filename. " +
                               "Use the filename pattern 'object_<localID>.vobj'.");
                return;
            }

            if (!archivePath.EndsWith(".vobj", StringComparison.OrdinalIgnoreCase))
                archivePath += ".vobj";

            _ctx.LogToChat($"[Import/Export] Exporting localID {localId} to {archivePath} …");
            try
            {
                var serializer = new PrimSerializer(_ctx.Client, _ctx.LogToChat);
                bool ok = serializer.Export(localId, archivePath);
                _ctx.LogToChat(ok
                    ? $"[Import/Export] Export complete: {archivePath}"
                    : "[Import/Export] Export failed — see messages above.");
            }
            catch (Exception ex)
            {
                _ctx.LogToChat($"[Import/Export] Export error: {ex.Message}");
            }
        });
    }

    private void OnImportMenuClick() => RunImportFromPicker(PrimSerializer.TextureMode.Original);

    private void OnImportTexMenuClick() => RunImportFromPicker(PrimSerializer.TextureMode.Reupload);

    private void RunImportFromPicker(PrimSerializer.TextureMode textureMode)
    {
        _ = Task.Run(async () =>
        {
            IReadOnlyList<IStorageFile> files = [];
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                files = await _ctx.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select .vobj archive to import",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Veles Object Archive") { Patterns = ["*.vobj"] },
                        new FilePickerFileType("All files") { Patterns = ["*.*"] }
                    ]
                });
            });

            if (files is not [var file]) return;

            string archivePath = file.Path.LocalPath;
            _ctx.LogToChat($"[Import/Export] Importing from {archivePath} with texture mode {textureMode} …");
            try
            {
                var serializer = new PrimSerializer(_ctx.Client, _ctx.LogToChat);
                serializer.Import(archivePath, new Vector3(0f, 2f, 0f), textureMode, _ctx.LogToChat);
                _ctx.LogToChat("[Import/Export] Import complete.");
            }
            catch (Exception ex)
            {
                _ctx.LogToChat($"[Import/Export] Import error: {ex.Message}");
            }
        });
    }

    // ── Command handlers ───────────────────────────────────────────────────

    private void OnExportCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1 || !uint.TryParse(args[0], out uint localId))
        {
            write("Usage: export <localID> [path]");
            return;
        }

        string archivePath = args.Length >= 2
            ? args[1]
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "VelesExports",
                $"object_{localId}.vobj");

        if (!archivePath.EndsWith(".vobj", StringComparison.OrdinalIgnoreCase))
            archivePath += ".vobj";

        write($"[Import/Export] Exporting localID {localId} to {archivePath} …");

        Task.Run(() =>
        {
            try
            {
                var serializer = new PrimSerializer(_ctx.Client, write);
                bool ok = serializer.Export(localId, archivePath);
                write(ok
                    ? $"[Import/Export] Export complete: {archivePath}"
                    : "[Import/Export] Export failed — see messages above.");
            }
            catch (Exception ex)
            {
                write($"[Import/Export] Export error: {ex.Message}");
            }
        });
    }

    private void OnImportCommand(string[] args, Action<string> write)
        => RunImport(args, write, PrimSerializer.TextureMode.Original);

    private void OnImportTexCommand(string[] args, Action<string> write)
        => RunImport(args, write, PrimSerializer.TextureMode.Reupload);

    private void RunImport(string[] args, Action<string> write, PrimSerializer.TextureMode textureMode)
    {
        if (args.Length < 1)
        {
            write("Usage: import <path.vobj> [x y z]");
            return;
        }

        string archivePath = args[0];
        if (!File.Exists(archivePath))
        {
            write($"[Import/Export] File not found: {archivePath}");
            return;
        }

        var offset = Vector3.Zero;
        if (args.Length >= 4
            && float.TryParse(args[1], out float ox)
            && float.TryParse(args[2], out float oy)
            && float.TryParse(args[3], out float oz))
        {
            offset = new Vector3(ox, oy, oz);
        }
        else
        {
            // Default: 2m ahead of agent
            offset = new Vector3(0f, 2f, 0f);
        }

        write($"[Import/Export] Importing from {archivePath} with texture mode {textureMode} …");

        Task.Run(() =>
        {
            try
            {
                var serializer = new PrimSerializer(_ctx.Client, write);
                serializer.Import(archivePath, offset, textureMode, write);
                write("[Import/Export] Import complete.");
            }
            catch (Exception ex)
            {
                write($"[Import/Export] Import error: {ex.Message}");
            }
        });
    }
}
