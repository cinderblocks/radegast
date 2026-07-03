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
using System.Linq;
using System.Text;
using System.Threading;
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

        _ctx.RegisterCommand("exportshape", "Export avatar shape wearable to a .llw file",
            "exportshape                    — export currently worn shape\n" +
            "exportshape <path>             — export current shape to specified path\n" +
            "exportshape <assetUUID> [path] — export shape asset by UUID",
            OnExportShapeCommand);
        _ctx.RegisterCommand("importshape", "Upload a .llw shape wearable and wear it",
            "importshape <path> [name]  — uploads the shape to inventory and wears it.",
            OnImportShapeCommand);
        _ctx.RegisterCommand("exportphysics", "Export avatar physics wearable to a .llw file",
            "exportphysics                    — export currently worn physics\n" +
            "exportphysics <path>             — export current physics to specified path\n" +
            "exportphysics <assetUUID> [path] — export physics asset by UUID",
            OnExportPhysicsCommand);
        _ctx.RegisterCommand("importphysics", "Upload a .llw physics wearable and wear it",
            "importphysics <path> [name]  — uploads the physics to inventory and wears it.",
            OnImportPhysicsCommand);
        _ctx.RegisterCommand("exportwearable", "Export any currently worn wearable to a .llw file",
            "exportwearable <type> [uuid] [path]\n" +
            "  type: shape skin hair eyes shirt pants shoes socks jacket gloves\n" +
            "        undershirt underpants skirt alpha tattoo physics universal",
            OnExportWearableCommand);
        _ctx.RegisterCommand("importwearable", "Upload a .llw wearable and wear it (type auto-detected)",
            "importwearable <path> [name]  — wearable type is read from the file content.",
            OnImportWearableCommand);

        foreach (var (wt, at) in s_wearableTable)
        {
            WearableType wearType  = wt;
            AssetType    assetType = at;
            string       id        = wt.ToString().ToLowerInvariant();
            string       label     = wt.ToString();
            _ctx.AddMenuItem(new PluginMenuItemInfo($"importexport_wear_export_{id}",
                $"Import/Export: Export {label}…",
                () => OnExportWearableMenuClick(wearType, assetType)));
            _ctx.AddMenuItem(new PluginMenuItemInfo($"importexport_wear_import_{id}",
                $"Import/Export: Import {label}…",
                () => OnImportWearableMenuClick(wearType)));
        }

        _ctx.RegisterCommand("exporttex", "Export a texture asset to an image file",
            "exporttex <assetUUID> [path]  — downloads the texture and writes it to disk.\n" +
            "  Extension sets format: .png .jpg .webp .bmp .tga (decoded)  |  .j2k .j2c .jp2 (raw).\n" +
            "  Default: <uuid>.png in ~/VelesExports/",
            OnExportTexCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_exporttex",
            "Import/Export: Export Texture…", OnExportTexMenuClick));

        _ctx.RegisterCommand("exportsound", "Export a sound asset to an OGG or WAV file",
            "exportsound <assetUUID> [path]  — downloads the sound and writes it to disk.\n" +
            "  Extension determines format: .ogg (default, lossless copy) or .wav (decoded).",
            OnExportSoundCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("importexport_exportsound",
            "Import/Export: Export Sound…", OnExportSoundMenuClick));
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
        foreach (var (wt, _) in s_wearableTable)
        {
            string id = wt.ToString().ToLowerInvariant();
            _ctx.RemoveMenuItem($"importexport_wear_export_{id}");
            _ctx.RemoveMenuItem($"importexport_wear_import_{id}");
        }
        _ctx.RemoveMenuItem("importexport_exporttex");
        _ctx.RemoveMenuItem("importexport_exportanim");
        _ctx.RemoveMenuItem("importexport_exportsound");
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

    // ── Texture export ────────────────────────────────────────────────────

    private static readonly FilePickerFileType[] s_texPickerTypes =
    [
        new FilePickerFileType("PNG Image")       { Patterns = ["*.png"]  },
        new FilePickerFileType("JPEG Image")      { Patterns = ["*.jpg"]  },
        new FilePickerFileType("WebP Image")      { Patterns = ["*.webp"] },
        new FilePickerFileType("Targa Image")     { Patterns = ["*.tga"]  },
        new FilePickerFileType("BMP Image")       { Patterns = ["*.bmp"]  },
        new FilePickerFileType("JPEG 2000 (raw)") { Patterns = ["*.j2k", "*.j2c", "*.jp2"] },
    ];

    private void OnExportTexCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1 || !UUID.TryParse(args[0], out UUID assetUUID))
        {
            write("Usage: exporttex <assetUUID> [path]");
            write("  Supported extensions: .png .jpg .webp .bmp .tga .j2k .j2c .jp2");
            return;
        }
        string path = args.Length >= 2 ? args[1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "VelesExports", $"{assetUUID}.png");
        write($"[Import/Export] Downloading texture {assetUUID} …");
        _ = ExportTextureAsync(assetUUID, path, write);
    }

    private void OnExportTexMenuClick()
    {
        _ = Task.Run(async () =>
        {
            IStorageFile? file = null;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Texture — paste asset UUID as filename",
                    SuggestedFileName = "texture.png",
                    DefaultExtension = ".png",
                    FileTypeChoices = s_texPickerTypes,
                });
            });
            if (file == null) return;

            string path      = file.Path.LocalPath;
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            if (!UUID.TryParse(nameNoExt, out UUID assetUUID))
            {
                _ctx.LogToChat("[Import/Export] Export Texture cancelled: filename must be the " +
                               "asset UUID (e.g. 89556747-24cb-43ed-920b-47caed15465f.png).");
                return;
            }
            _ctx.LogToChat($"[Import/Export] Downloading texture {assetUUID} …");
            await ExportTextureAsync(assetUUID, path, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private async Task ExportTextureAsync(UUID assetUUID, string path, Action<string> write)
    {
        try
        {
            var asset = await _ctx.Client.Assets
                .RequestImageAsync(assetUUID, ImageType.Normal)
                .ConfigureAwait(false);

            if (asset?.AssetData == null)
            {
                write($"[Import/Export] Texture {assetUUID} not found or download failed.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            TextureConverter.SaveAs(asset.AssetData, path);

            var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            write($"[Import/Export] Texture written: {path}  " +
                  $"({asset.AssetData.Length / 1024.0:F1} KB J2K → {ext})");
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Export texture error: {ex.Message}");
        }
    }

    // ── Sound export ──────────────────────────────────────────────────────

    private void OnExportSoundCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1 || !UUID.TryParse(args[0], out UUID assetUUID))
        {
            write("Usage: exportsound <assetUUID> [path]");
            write("  path extension determines format: .ogg (default) or .wav");
            return;
        }
        string path = args.Length >= 2 ? args[1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "VelesExports", $"{assetUUID}.ogg");
        write($"[Import/Export] Downloading sound {assetUUID} …");
        _ = ExportSoundAsync(assetUUID, path, write);
    }

    private void OnExportSoundMenuClick()
    {
        _ = Task.Run(async () =>
        {
            IStorageFile? file = null;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Sound — paste asset UUID as filename",
                    SuggestedFileName = "sound.ogg",
                    DefaultExtension = ".ogg",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("OGG Vorbis") { Patterns = ["*.ogg"] },
                        new FilePickerFileType("WAV Audio")  { Patterns = ["*.wav"] },
                    ]
                });
            });
            if (file == null) return;

            string path      = file.Path.LocalPath;
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            if (!UUID.TryParse(nameNoExt, out UUID assetUUID))
            {
                _ctx.LogToChat("[Import/Export] Export Sound cancelled: filename must be the asset UUID " +
                               "(e.g. 12345678-1234-1234-1234-123456789abc.ogg).");
                return;
            }
            _ctx.LogToChat($"[Import/Export] Downloading sound {assetUUID} …");
            await ExportSoundAsync(assetUUID, path, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private async Task ExportSoundAsync(UUID assetUUID, string path, Action<string> write)
    {
        try
        {
            var asset = await _ctx.Client.Assets
                .RequestAssetAsync(assetUUID, AssetType.Sound, priority: true)
                .ConfigureAwait(false);
            if (asset?.AssetData == null)
            {
                write($"[Import/Export] Sound {assetUUID} not found or download failed.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SoundConverter.SaveAs(asset.AssetData, path);
            write($"[Import/Export] Sound written: {path}  ({asset.AssetData.Length} bytes)");
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Export sound error: {ex.Message}");
        }
    }

    // ── Wearable type table ────────────────────────────────────────────────

    private static readonly (WearableType Type, AssetType Asset)[] s_wearableTable =
    [
        (WearableType.Shape,      AssetType.Bodypart),
        (WearableType.Skin,       AssetType.Bodypart),
        (WearableType.Hair,       AssetType.Bodypart),
        (WearableType.Eyes,       AssetType.Bodypart),
        (WearableType.Shirt,      AssetType.Clothing),
        (WearableType.Pants,      AssetType.Clothing),
        (WearableType.Shoes,      AssetType.Clothing),
        (WearableType.Socks,      AssetType.Clothing),
        (WearableType.Jacket,     AssetType.Clothing),
        (WearableType.Gloves,     AssetType.Clothing),
        (WearableType.Undershirt, AssetType.Clothing),
        (WearableType.Underpants, AssetType.Clothing),
        (WearableType.Skirt,      AssetType.Clothing),
        (WearableType.Alpha,      AssetType.Clothing),
        (WearableType.Tattoo,     AssetType.Clothing),
        (WearableType.Physics,    AssetType.Clothing),
        (WearableType.Universal,  AssetType.Clothing),
    ];

    private static AssetType GetAssetTypeForWearable(WearableType wt)
    {
        foreach (var (type, asset) in s_wearableTable)
            if (type == wt) return asset;
        return AssetType.Clothing;
    }

    // ── Generic wearable commands ──────────────────────────────────────────

    private void OnExportWearableCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1)
        {
            write("Usage: exportwearable <type> [uuid] [path]");
            write("  Types: shape skin hair eyes shirt pants shoes socks jacket gloves");
            write("         undershirt underpants skirt alpha tattoo physics universal");
            return;
        }
        if (!Enum.TryParse<WearableType>(args[0], ignoreCase: true, out var wearType)
            || wearType == WearableType.Invalid)
        {
            write($"[Import/Export] Unknown wearable type: {args[0]}");
            return;
        }
        int idx = 1;
        UUID explicitUUID = UUID.Zero;
        if (args.Length > idx && UUID.TryParse(args[idx], out var u)) { explicitUUID = u; idx++; }
        string path = args.Length > idx
            ? args[idx]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "VelesExports", $"{wearType.ToString().ToLowerInvariant()}.llw");
        if (!path.EndsWith(".llw", StringComparison.OrdinalIgnoreCase)) path += ".llw";
        write($"[Import/Export] Exporting {wearType}…");
        _ = ExportWearableAsync(wearType, GetAssetTypeForWearable(wearType), explicitUUID, path, write);
    }

    private void OnImportWearableCommand(string[] args, Action<string> write)
    {
        if (args.Length < 1) { write("Usage: importwearable <path> [name]"); return; }
        string path = args[0];
        if (!File.Exists(path)) { write($"[Import/Export] File not found: {path}"); return; }
        string name = args.Length >= 2
            ? string.Join(" ", args[1..])
            : Path.GetFileNameWithoutExtension(path);
        write($"[Import/Export] Uploading wearable \"{name}\" …");
        _ = ImportWearableAutoAsync(path, name, write);
    }

    private void OnExportWearableMenuClick(WearableType wearType, AssetType assetType)
    {
        _ = Task.Run(async () =>
        {
            IStorageFile? file = null;
            string label = wearType.ToString();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                file = await _ctx.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Export {label}",
                    SuggestedFileName = $"{label.ToLowerInvariant()}.llw",
                    DefaultExtension = ".llw",
                    FileTypeChoices = [new FilePickerFileType("Linden Lab Wearable") { Patterns = ["*.llw"] }]
                });
            });
            if (file == null) return;
            _ctx.LogToChat($"[Import/Export] Exporting current {label}…");
            await ExportWearableAsync(wearType, assetType, UUID.Zero,
                file.Path.LocalPath, _ctx.LogToChat).ConfigureAwait(false);
        });
    }

    private void OnImportWearableMenuClick(WearableType wearType)
    {
        _ = Task.Run(async () =>
        {
            IReadOnlyList<IStorageFile> files = [];
            string label = wearType.ToString();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                files = await _ctx.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Import {label}",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Linden Lab Wearable") { Patterns = ["*.llw"] },
                        new FilePickerFileType("All Files") { Patterns = ["*.*"] },
                    ]
                });
            });
            if (files is not [var file]) return;
            string name = Path.GetFileNameWithoutExtension(file.Path.LocalPath);
            _ctx.LogToChat($"[Import/Export] Uploading {label} \"{name}\" …");
            await ImportWearableAutoAsync(file.Path.LocalPath, name,
                _ctx.LogToChat, enforceType: wearType).ConfigureAwait(false);
        });
    }

    // ── Named wearable command aliases ────────────────────────────────────

    private void OnExportShapeCommand(string[] args, Action<string> write)
        => DispatchWearableExport(WearableType.Shape, "shape.llw", args, write);

    private void OnImportShapeCommand(string[] args, Action<string> write)
        => DispatchWearableImport(WearableType.Shape, "importshape", args, write);

    private void OnExportPhysicsCommand(string[] args, Action<string> write)
        => DispatchWearableExport(WearableType.Physics, "physics.llw", args, write);

    private void OnImportPhysicsCommand(string[] args, Action<string> write)
        => DispatchWearableImport(WearableType.Physics, "importphysics", args, write);

    private void DispatchWearableExport(WearableType wearType, string defaultFile,
        string[] args, Action<string> write)
    {
        UUID explicitUUID = UUID.Zero;
        int pathIdx = 0;
        if (args.Length >= 1 && UUID.TryParse(args[0], out UUID u)) { explicitUUID = u; pathIdx = 1; }
        string path = args.Length > pathIdx
            ? args[pathIdx]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "VelesExports", defaultFile);
        if (!path.EndsWith(".llw", StringComparison.OrdinalIgnoreCase)) path += ".llw";
        write($"[Import/Export] Exporting {wearType}…");
        _ = ExportWearableAsync(wearType, GetAssetTypeForWearable(wearType), explicitUUID, path, write);
    }

    private void DispatchWearableImport(WearableType wearType, string usage,
        string[] args, Action<string> write)
    {
        if (args.Length < 1) { write($"Usage: {usage} <path> [name]"); return; }
        string path = args[0];
        if (!File.Exists(path)) { write($"[Import/Export] File not found: {path}"); return; }
        string name = args.Length >= 2
            ? string.Join(" ", args[1..])
            : Path.GetFileNameWithoutExtension(path);
        write($"[Import/Export] Uploading {wearType} \"{name}\" …");
        _ = ImportWearableAutoAsync(path, name, write, enforceType: wearType);
    }

    // ── Shared wearable async helpers ─────────────────────────────────────

    private async Task ExportWearableAsync(WearableType wearType, AssetType assetType,
        UUID explicitUUID, string path, Action<string> write)
    {
        try
        {
            UUID assetUUID = explicitUUID;
            if (assetUUID == UUID.Zero)
            {
                assetUUID = _ctx.Client.Appearance
                    .GetWearableAssets(wearType)
                    .FirstOrDefault();
                if (assetUUID == UUID.Zero)
                {
                    write($"[Import/Export] No {wearType} is currently worn.");
                    return;
                }
            }

            var asset = await _ctx.Client.Assets
                .RequestAssetAsync(assetUUID, assetType, priority: true)
                .ConfigureAwait(false);

            if (asset?.AssetData == null)
            {
                write($"[Import/Export] {wearType} asset {assetUUID} not found or download failed.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, asset.AssetData).ConfigureAwait(false);
            write($"[Import/Export] {wearType} written: {path}  ({asset.AssetData.Length} bytes)");
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Export {wearType} error: {ex.Message}");
        }
    }

    // AssetBodypart.Decode() reads the same LLWearable text format as AssetClothing.Decode();
    // the embedded "type" field determines the actual WearableType.
    private async Task ImportWearableAutoAsync(string filePath, string name, Action<string> write,
        WearableType? enforceType = null)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);

            var probe = new AssetBodypart(UUID.Zero, bytes);
            if (!probe.Decode())
                throw new InvalidDataException("File is not a valid LLWearable (decode failed).");

            if (enforceType.HasValue && probe.WearableType != enforceType.Value)
                throw new InvalidDataException(
                    $"File contains a {probe.WearableType} wearable, expected {enforceType.Value}.");

            var wearType  = probe.WearableType;
            var assetType = GetAssetTypeForWearable(wearType);
            var folder    = _ctx.Client.Inventory.FindFolderForType(assetType);

            var (ok, status, itemID, _) = await _ctx.Client.Inventory
                .RequestCreateItemFromAssetAsync(
                    bytes, name, "Imported with Veles",
                    assetType, InventoryType.Wearable,
                    folder, Permissions.FullPermissions)
                .ConfigureAwait(false);

            if (!ok) { write($"[Import/Export] {wearType} upload failed: {status}"); return; }

            write($"[Import/Export] {wearType} \"{name}\" created ({itemID}). Wearing…");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var item = await _ctx.Client.Inventory
                .FetchItemAsync(itemID, _ctx.Client.Self.AgentID, cts.Token)
                .ConfigureAwait(false);

            if (item != null)
            {
                _ctx.Client.Appearance.AddToOutfit(item, replace: true);
                write($"[Import/Export] {wearType} applied.");
            }
            else
            {
                write($"[Import/Export] {wearType} created but item fetch timed out — wear from inventory.");
            }
        }
        catch (Exception ex)
        {
            write($"[Import/Export] Import wearable error: {ex.Message}");
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
