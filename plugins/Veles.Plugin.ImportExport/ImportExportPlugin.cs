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
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibreMetaverse;
using LibreMetaverse.Assets;
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

        _ctx.RegisterCommand("exportanim", "Export a SL animation asset to a BVH file",
            "exportanim <assetUUID> [path]  — downloads the animation and writes a BVH file.\n" +
            "  assetUUID : UUID of the animation asset in inventory or the asset server\n" +
            "  path      : optional output path (default: ~/VelesExports/<uuid>.bvh)\n" +
            "Coordinate system: X-forward Y-left Z-up (SL native). In Blender: Forward=Y, Up=Z.",
            OnExportAnimCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_export",
            "Import/Export: Export Object…", OnExportMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_import",
            "Import/Export: Import Object…", OnImportMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_importtex",
            "Import/Export: Import Object (Re-upload Textures)…", OnImportTexMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_exportanim",
            "Import/Export: Export Animation (BVH)…", OnExportAnimMenuClick));

        _ctx.RegisterCommand("exportscript", "Export a script asset to a .lsl file",
            "exportscript <assetUUID> [path]  — downloads the LSL script and writes it to disk.",
            OnExportScriptCommand);
        _ctx.RegisterCommand("importscript", "Upload a .lsl file as a new script in inventory",
            "importscript <path> [name]  — creates a new Script item in your Scripts folder.\n" +
            "  name defaults to the filename without extension.",
            OnImportScriptCommand);
        _ctx.RegisterCommand("exportnote", "Export a notecard asset body to a .txt file",
            "exportnote <assetUUID> [path]  — downloads the notecard and writes the plain text body.",
            OnExportNoteCommand);
        _ctx.RegisterCommand("importnote", "Upload a .txt file as a new notecard in inventory",
            "importnote <path> [name]  — creates a new Notecard item in your Notecards folder.\n" +
            "  name defaults to the filename without extension.",
            OnImportNoteCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_exportscript",
            "Import/Export: Export Script…", OnExportScriptMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_importscript",
            "Import/Export: Import Script…", OnImportScriptMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_exportnote",
            "Import/Export: Export Notecard…", OnExportNoteMenuClick));
        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_importnote",
            "Import/Export: Import Notecard…", OnImportNoteMenuClick));

        _ctx.LogToChat("[Import/Export] Plugin attached. Commands: export, import, importtex, exportanim, exportscript, importscript, exportnote, importnote");
    }

    public void Detach()
    {
        _ctx.RemoveMenuItem("importexport_exportanim");
        _ctx.RemoveMenuItem("importexport_exportscript");
        _ctx.RemoveMenuItem("importexport_importscript");
        _ctx.RemoveMenuItem("importexport_exportnote");
        _ctx.RemoveMenuItem("importexport_importnote");
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

    // ── Animation BVH export ───────────────────────────────────────────────

    private void OnExportAnimCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1 || !UUID.TryParse(args[0], out UUID assetUUID))
        {
            write("Usage: exportanim <assetUUID> [path]");
            return;
        }

        string bvhPath = args.Length >= 2
            ? args[1]
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "VelesExports",
                $"{assetUUID}.bvh");

        if (!bvhPath.EndsWith(".bvh", StringComparison.OrdinalIgnoreCase))
            bvhPath += ".bvh";

        write($"[Import/Export] Downloading animation {assetUUID} …");
        _ = ExportAnimAsync(assetUUID, bvhPath, write);
    }

    private void OnExportAnimMenuClick()
    {
        _ = Task.Run(async () =>
        {
            IStorageFile? file = null;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Animation — paste asset UUID as filename",
                    SuggestedFileName = "animation.bvh",
                    DefaultExtension = ".bvh",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("BVH Animation") { Patterns = ["*.bvh"] }
                    ]
                });
            });

            if (file == null) return;

            string bvhPath    = file.Path.LocalPath;
            string nameNoExt  = Path.GetFileNameWithoutExtension(bvhPath);

            if (!UUID.TryParse(nameNoExt, out UUID assetUUID))
            {
                _ctx.LogToChat("[Import/Export] Export Animation cancelled: filename must be the asset UUID " +
                               "(e.g. 550f97d3-7b31-4d85-a3c3-07b14cf43c74.bvh).");
                return;
            }

            _ctx.LogToChat($"[Import/Export] Downloading animation {assetUUID} …");
            await ExportAnimAsync(assetUUID, bvhPath, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private async Task ExportAnimAsync(UUID assetUUID, string bvhPath, Action<string> write)
    {
        try
        {
            var asset = await _ctx.Client.Assets
                .RequestAssetAsync(assetUUID, AssetType.Animation, priority: true)
                .ConfigureAwait(false);

            if (asset?.AssetData == null)
            {
                write($"[Import/Export] Animation {assetUUID} not found or download failed.");
                return;
            }

            var anim = new BinBVHAnimationReader(asset.AssetData);
            Directory.CreateDirectory(Path.GetDirectoryName(bvhPath)!);

            using var sw = new StreamWriter(bvhPath, append: false, Encoding.ASCII);
            BvhExporter.Export(anim, sw);

            write($"[Import/Export] BVH written: {bvhPath}  " +
                  $"({anim.joints.Length} joints, {anim.OutPoint - anim.InPoint:F2}s)");
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Export animation error: {ex.Message}");
        }
    }

    // ── Script export / import ─────────────────────────────────────────────

    private void OnExportScriptCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1 || !UUID.TryParse(args[0], out UUID assetUUID))
        {
            write("Usage: exportscript <assetUUID> [path]");
            return;
        }
        string path = args.Length >= 2 ? args[1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "VelesExports", $"{assetUUID}.lsl");
        if (!path.EndsWith(".lsl", StringComparison.OrdinalIgnoreCase)) path += ".lsl";
        write($"[Import/Export] Downloading script {assetUUID} …");
        _ = ExportScriptAsync(assetUUID, path, write);
    }

    private void OnImportScriptCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1) { write("Usage: importscript <path> [name]"); return; }
        string path = args[0];
        if (!File.Exists(path)) { write($"[Import/Export] File not found: {path}"); return; }
        string name = args.Length >= 2
            ? string.Join(" ", args[1..])
            : Path.GetFileNameWithoutExtension(path);
        write($"[Import/Export] Uploading script \"{name}\" …");
        _ = ImportScriptAsync(path, name, write);
    }

    private void OnExportScriptMenuClick()
    {
        _ = Task.Run(async () =>
        {
            IStorageFile? file = null;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Script — paste asset UUID as filename",
                    SuggestedFileName = "script.lsl",
                    DefaultExtension = ".lsl",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("LSL Script") { Patterns = ["*.lsl"] },
                        new FilePickerFileType("Text File") { Patterns = ["*.txt"] },
                    ]
                });
            });
            if (file == null) return;

            string path     = file.Path.LocalPath;
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            if (!UUID.TryParse(nameNoExt, out UUID assetUUID))
            {
                _ctx.LogToChat("[Import/Export] Export Script cancelled: filename must be the asset UUID " +
                               "(e.g. 550f97d3-7b31-4d85-a3c3-07b14cf43c74.lsl).");
                return;
            }
            _ctx.LogToChat($"[Import/Export] Downloading script {assetUUID} …");
            await ExportScriptAsync(assetUUID, path, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private void OnImportScriptMenuClick()
    {
        _ = Task.Run(async () =>
        {
            IReadOnlyList<IStorageFile> files = [];
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                files = await _ctx.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Script",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("LSL Script") { Patterns = ["*.lsl"] },
                        new FilePickerFileType("Text File") { Patterns = ["*.txt"] },
                        new FilePickerFileType("All Files") { Patterns = ["*.*"] },
                    ]
                });
            });
            if (files is not [var file]) return;
            string name = Path.GetFileNameWithoutExtension(file.Path.LocalPath);
            _ctx.LogToChat($"[Import/Export] Uploading script \"{name}\" …");
            await ImportScriptAsync(file.Path.LocalPath, name, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private async Task ExportScriptAsync(UUID assetUUID, string path, Action<string> write)
    {
        try
        {
            var asset = await _ctx.Client.Assets
                .RequestAssetAsync(assetUUID, AssetType.LSLText, priority: true)
                .ConfigureAwait(false);
            if (asset?.AssetData == null)
            {
                write($"[Import/Export] Script {assetUUID} not found or download failed.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, asset.AssetData).ConfigureAwait(false);
            write($"[Import/Export] Script written: {path}  ({asset.AssetData.Length} bytes)");
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Export script error: {ex.Message}");
        }
    }

    private async Task ImportScriptAsync(string path, string name, Action<string> write)
    {
        try
        {
            var text   = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
            var script = new AssetScriptText { Source = text };
            script.Encode();

            var folderID = _ctx.Client.Inventory.FindFolderForType(AssetType.LSLText);
            var (ok, status, itemID, _) = await _ctx.Client.Inventory
                .RequestCreateItemFromAssetAsync(
                    script.AssetData!, name, string.Empty,
                    AssetType.LSLText, InventoryType.LSL,
                    folderID, Permissions.FullPermissions)
                .ConfigureAwait(false);

            write(ok
                ? $"[Import/Export] Script \"{name}\" created in inventory ({itemID})."
                : $"[Import/Export] Script upload failed: {status}");
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Import script error: {ex.Message}");
        }
    }

    // ── Notecard export / import ───────────────────────────────────────────

    private void OnExportNoteCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1 || !UUID.TryParse(args[0], out UUID assetUUID))
        {
            write("Usage: exportnote <assetUUID> [path]");
            return;
        }
        string path = args.Length >= 2 ? args[1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "VelesExports", $"{assetUUID}.txt");
        if (!path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) path += ".txt";
        write($"[Import/Export] Downloading notecard {assetUUID} …");
        _ = ExportNotecardAsync(assetUUID, path, write);
    }

    private void OnImportNoteCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1) { write("Usage: importnote <path> [name]"); return; }
        string path = args[0];
        if (!File.Exists(path)) { write($"[Import/Export] File not found: {path}"); return; }
        string name = args.Length >= 2
            ? string.Join(" ", args[1..])
            : Path.GetFileNameWithoutExtension(path);
        write($"[Import/Export] Uploading notecard \"{name}\" …");
        _ = ImportNotecardAsync(path, name, write);
    }

    private void OnExportNoteMenuClick()
    {
        _ = Task.Run(async () =>
        {
            IStorageFile? file = null;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Notecard — paste asset UUID as filename",
                    SuggestedFileName = "notecard.txt",
                    DefaultExtension = ".txt",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Text File") { Patterns = ["*.txt"] },
                    ]
                });
            });
            if (file == null) return;

            string path      = file.Path.LocalPath;
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            if (!UUID.TryParse(nameNoExt, out UUID assetUUID))
            {
                _ctx.LogToChat("[Import/Export] Export Notecard cancelled: filename must be the asset UUID " +
                               "(e.g. 550f97d3-7b31-4d85-a3c3-07b14cf43c74.txt).");
                return;
            }
            _ctx.LogToChat($"[Import/Export] Downloading notecard {assetUUID} …");
            await ExportNotecardAsync(assetUUID, path, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private void OnImportNoteMenuClick()
    {
        _ = Task.Run(async () =>
        {
            IReadOnlyList<IStorageFile> files = [];
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                files = await _ctx.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Notecard",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Text File") { Patterns = ["*.txt"] },
                        new FilePickerFileType("All Files") { Patterns = ["*.*"] },
                    ]
                });
            });
            if (files is not [var file]) return;
            string name = Path.GetFileNameWithoutExtension(file.Path.LocalPath);
            _ctx.LogToChat($"[Import/Export] Uploading notecard \"{name}\" …");
            await ImportNotecardAsync(file.Path.LocalPath, name, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private async Task ExportNotecardAsync(UUID assetUUID, string path, Action<string> write)
    {
        try
        {
            var asset = await _ctx.Client.Assets
                .RequestAssetAsync(assetUUID, AssetType.Notecard, priority: true)
                .ConfigureAwait(false);
            if (asset?.AssetData == null)
            {
                write($"[Import/Export] Notecard {assetUUID} not found or download failed.");
                return;
            }
            var notecard = new AssetNotecard(assetUUID, asset.AssetData);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (notecard.Decode())
            {
                await File.WriteAllTextAsync(path, notecard.BodyText, Encoding.UTF8).ConfigureAwait(false);
                string embedNote = notecard.EmbeddedItems.Count > 0
                    ? $"  ({notecard.EmbeddedItems.Count} embedded item(s) not exported)"
                    : string.Empty;
                write($"[Import/Export] Notecard written: {path}{embedNote}");
            }
            else
            {
                // Fall back to raw bytes if Linden format parse fails
                await File.WriteAllBytesAsync(path, asset.AssetData).ConfigureAwait(false);
                write($"[Import/Export] Notecard written (raw): {path}");
            }
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Export notecard error: {ex.Message}");
        }
    }

    private async Task ImportNotecardAsync(string path, string name, Action<string> write)
    {
        try
        {
            var text      = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
            var notecard  = new AssetNotecard { BodyText = text };
            notecard.Encode();

            var folderID = _ctx.Client.Inventory.FindFolderForType(AssetType.Notecard);
            var (ok, status, itemID, _) = await _ctx.Client.Inventory
                .RequestCreateItemFromAssetAsync(
                    notecard.AssetData!, name, string.Empty,
                    AssetType.Notecard, InventoryType.Notecard,
                    folderID, Permissions.FullPermissions)
                .ConfigureAwait(false);

            write(ok
                ? $"[Import/Export] Notecard \"{name}\" created in inventory ({itemID})."
                : $"[Import/Export] Notecard upload failed: {status}");
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Import notecard error: {ex.Message}");
        }
    }
}
