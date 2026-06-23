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
using System.IO.Compression;
using System.Linq;
using System.Threading;
using LibreMetaverse;
using LibreMetaverse.StructuredData;

namespace Veles.Plugin.ImportExport;

/// <summary>
/// Serialises and deserialises linkset objects to/from a compressed archive.
///
/// Export format — a single ZIP archive with extension <c>.vobj</c>:
///   object.xml            – LLSD XML with all prim data
///   textures/&lt;uuid&gt;.jp2  – raw J2K texture assets (full-perm only)
/// </summary>
internal sealed class PrimSerializer
{
    private readonly GridClient _client;
    private readonly Action<string> _log;

    public PrimSerializer(GridClient client, Action<string> log)
    {
        _client = client;
        _log = log;
    }

    // ── Export ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Exports the linkset containing <paramref name="localID"/> to the
    /// compressed archive at <paramref name="archivePath"/> (<c>.vobj</c>).
    /// Textures that are full-perm (or creator-owned on non-SL grids) are
    /// bundled as JP2 entries inside the archive.
    /// </summary>
    /// <returns>True on success.</returns>
    public bool Export(uint localID, string archivePath)
    {
        // Find the prim and its simulator
        Primitive? prim = null;
        Simulator? sim = null;

        lock (_client.Network.Simulators)
        {
            foreach (var s in _client.Network.Simulators)
            {
                if (s?.ObjectsPrimitives.TryGetValue(localID, out prim) == true)
                {
                    sim = s;
                    break;
                }
            }
        }

        if (prim == null || sim == null)
        {
            _log($"[ImportExport] Object {localID} not found in any connected simulator.");
            return false;
        }

        // Resolve to root prim
        uint rootLocalID = prim.ParentID != 0 ? prim.ParentID : prim.LocalID;

        // If the root is on another sim, find it
        if (prim.ParentID != 0)
        {
            lock (_client.Network.Simulators)
            {
                foreach (var s in _client.Network.Simulators)
                {
                    if (s == null) continue;
                    if (s.ObjectsPrimitives.ContainsKey(prim.ParentID))
                    {
                        sim = s;
                        break;
                    }
                }
            }
        }

        // Verify export permission (must own and have created the object, per LL TOS)
        Primitive.ObjectProperties? props = null;
        var propsGate = new ManualResetEventSlim(false);
        EventHandler<ObjectPropertiesFamilyEventArgs> propsHandler = (_, e) =>
        {
            if (e?.Properties?.ObjectID != prim.ID) return;
            props = new Primitive.ObjectProperties();
            props.SetFamilyProperties(e.Properties);
            propsGate.Set();
        };
        _client.Objects.ObjectPropertiesFamily += propsHandler;
        _client.Objects.RequestObjectPropertiesFamily(sim, prim.ID);
        bool gotProps = propsGate.Wait(10_000);
        _client.Objects.ObjectPropertiesFamily -= propsHandler;

        if (!gotProps || props == null)
        {
            _log("[ImportExport] Couldn't fetch object permissions — try again.");
            return false;
        }

        bool isNonSL = _client.Network.CurrentSim?.SimVersion?.Contains("OpenSim", StringComparison.OrdinalIgnoreCase) == true
                       || _client.Network.CurrentSim?.SimVersion?.Contains("Halcyon", StringComparison.OrdinalIgnoreCase) == true;

        bool ownerCreator = props.CreatorID == _client.Self.AgentID && props.OwnerID == _client.Self.AgentID;
        bool nonSlFullPerm = isNonSL
            && props.OwnerID == _client.Self.AgentID
            && (props.Permissions.OwnerMask & (PermissionMask.Modify | PermissionMask.Copy | PermissionMask.Transfer))
               == (PermissionMask.Modify | PermissionMask.Copy | PermissionMask.Transfer);

        if (!ownerCreator && !nonSlFullPerm)
        {
            _log($"[ImportExport] No export permission. Owner: {props.OwnerID}, Creator: {props.CreatorID}, You: {_client.Self.AgentID}");
            return false;
        }

        // Collect prims in the linkset
        var prims = (from p in sim.ObjectsPrimitives
            where p.Value != null
            where p.Value.LocalID == rootLocalID || p.Value.ParentID == rootLocalID
            select p.Value).ToList();

        if (prims.Count == 0)
        {
            _log("[ImportExport] No prims found for the linkset.");
            return false;
        }

        // Fetch full ObjectProperties for each prim
        RequestObjectProperties(prims, 250, sim);

        string xml = OSDParser.SerializeLLSDXmlString(Helpers.PrimListToOSD(prims));

        // Download exportable textures into memory
        var textures = DownloadTextures(prims, isNonSL, props);

        // Write compressed archive
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? ".");
        using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            var xmlEntry = zip.CreateEntry("object.xml", CompressionLevel.Optimal);
            using (var w = new StreamWriter(xmlEntry.Open()))
                w.Write(xml);

            foreach (var (id, data) in textures)
            {
                var texEntry = zip.CreateEntry($"textures/{id}.jp2", CompressionLevel.NoCompression);
                using var ts = texEntry.Open();
                ts.Write(data, 0, data.Length);
            }
        }

        _log($"[ImportExport] Wrote {prims.Count} prim(s) and {textures.Count} texture(s) to {archivePath}");
        return true;
    }

    private void RequestObjectProperties(List<Primitive> prims, int msPerRequest, Simulator sim)
    {
        var waiting = new Dictionary<UUID, Primitive>();
        foreach (var p in prims)
            waiting[p.ID] = p;

        uint[] ids = prims.Select(p => p.LocalID).ToArray();

        EventHandler<ObjectPropertiesEventArgs> handler = null!;
        handler = (_, e) =>
        {
            lock (waiting)
                waiting.Remove(e.Properties.ObjectID);
        };

        _client.Objects.ObjectProperties += handler;
        _client.Objects.SelectObjects(sim, ids);

        int timeout = 2000 + msPerRequest * prims.Count;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            lock (waiting)
            {
                if (waiting.Count == 0) break;
            }
            Thread.Sleep(50);
        }

        _client.Objects.ObjectProperties -= handler;
    }

    private Dictionary<UUID, byte[]> DownloadTextures(List<Primitive> prims, bool isNonSL,
        Primitive.ObjectProperties rootProps)
    {
        var needed = new HashSet<UUID>();

        foreach (var p in prims)
        {
            if (p.Textures?.DefaultTexture?.TextureID is { } dt
                && dt != Primitive.TextureEntry.WHITE_TEXTURE)
                needed.Add(dt);

            if (p.Textures?.FaceTextures != null)
            {
                foreach (var ft in p.Textures.FaceTextures)
                {
                    if (ft != null && ft.TextureID != Primitive.TextureEntry.WHITE_TEXTURE)
                        needed.Add(ft.TextureID);
                }
            }

            if (p.Sculpt?.SculptTexture is { } st && st != UUID.Zero)
                needed.Add(st);
        }

        var exportable = needed.Where(id => CanExportTexture(id, isNonSL, rootProps)).ToList();
        var result = new Dictionary<UUID, byte[]>();

        if (exportable.Count == 0)
        {
            _log("[ImportExport] No exportable textures.");
            return result;
        }

        var tasks = exportable.Select(async id =>
        {
            var asset = await _client.Assets.RequestImageAsync(id, ImageType.Normal).ConfigureAwait(false);
            if (asset?.AssetData != null)
            {
                lock (result)
                    result[asset.AssetID] = asset.AssetData;
                _log($"[ImportExport] Downloaded texture {asset.AssetID}");
            }
            else
            {
                _log($"[ImportExport] Texture {id} download failed");
            }
        });

        var allDone = System.Threading.Tasks.Task.WhenAll(tasks);
        if (!allDone.Wait(TimeSpan.FromSeconds(60)))
            _log("[ImportExport] Texture download timed out — some textures may be missing.");
        else
            _log($"[ImportExport] Downloaded {result.Count}/{exportable.Count} texture(s).");

        return result;
    }

    private bool CanExportTexture(UUID id, bool isNonSL, Primitive.ObjectProperties rootProps)
    {
        // Built-in/default textures are always exportable
        if (IsBuiltIn(id)) return true;

        // On non-SL grids, allow full-perm textures
        if (isNonSL) return true;

        // Check inventory for full-perm
        if (_client.Inventory.Store != null && _client.Inventory.Store.Contains(id)
            && _client.Inventory.Store[id] is InventoryItem item)
        {
            return (item.Permissions.OwnerMask & (PermissionMask.Modify | PermissionMask.Copy | PermissionMask.Transfer))
                   == (PermissionMask.Modify | PermissionMask.Copy | PermissionMask.Transfer);
        }

        return false;
    }

    private static bool IsBuiltIn(UUID id)
    {
        return id == Primitive.TextureEntry.WHITE_TEXTURE
            || id == UUID.Zero
            || id == new UUID("5748decc-f629-461c-9a36-a35a221fe21f") // default plywood
            || id == new UUID("8dcd4a48-2d37-4909-9f78-f7a9eb4ef903"); // blank
    }

    // ── Import ─────────────────────────────────────────────────────────────

    public enum TextureMode
    {
        /// <summary>Use the original UUIDs from the export (works if the asset is still in the sim cache).</summary>
        Original,
        /// <summary>Re-upload textures from the export folder and use the new UUIDs.</summary>
        Reupload,
        /// <summary>Replace all textures with the default white texture.</summary>
        White,
    }

    /// <summary>
    /// Rezzes the linkset from the compressed archive at
    /// <paramref name="archivePath"/> (<c>.vobj</c>) at the agent's current
    /// position offset by <paramref name="offset"/>.
    /// </summary>
    public void Import(string archivePath, Vector3 offset, TextureMode textureMode, Action<string> log)
    {
        if (!File.Exists(archivePath))
        {
            log($"[ImportExport] Archive not found: {archivePath}");
            return;
        }

        string xml;
        var archivedTextures = new Dictionary<UUID, byte[]>();

        using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            var xmlEntry = zip.GetEntry("object.xml")
                ?? throw new InvalidDataException("Archive does not contain object.xml.");
            using (var sr = new StreamReader(xmlEntry.Open()))
                xml = sr.ReadToEnd();

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)
                    || !entry.FullName.EndsWith(".jp2", StringComparison.OrdinalIgnoreCase))
                    continue;

                string uuidStr = Path.GetFileNameWithoutExtension(entry.Name);
                if (!UUID.TryParse(uuidStr, out var texId)) continue;

                using var ms = new MemoryStream((int)entry.Length);
                entry.Open().CopyTo(ms);
                archivedTextures[texId] = ms.ToArray();
            }
        }

        var prims = Helpers.OSDToPrimList(OSDParser.DeserializeLLSDXml(xml));

        // Build texture re-upload map if needed
        var textureMap = new Dictionary<UUID, UUID>();
        if (textureMode == TextureMode.Reupload)
        {
            foreach (var (oldId, data) in archivedTextures)
            {
                UUID newId = UploadTexture(data, oldId.ToString(), log);
                if (newId != UUID.Zero)
                    textureMap[oldId] = newId;
            }
        }

        // Build linksets
        var linksets = new Dictionary<uint, (Primitive Root, List<Primitive> Children)>();
        foreach (var p in prims)
        {
            if (p.ParentID == 0)
            {
                if (!linksets.TryGetValue(p.LocalID, out var ls))
                    linksets[p.LocalID] = (p, new List<Primitive>());
                else
                    linksets[p.LocalID] = (p, ls.Children);
            }
            else
            {
                if (!linksets.TryGetValue(p.ParentID, out var ls))
                    linksets[p.ParentID] = (new Primitive(), new List<Primitive> { p });
                else
                    ls.Children.Add(p);
            }
        }

        var sim = _client.Network.CurrentSim!;
        var rezPos = _client.Self.SimPosition + offset;

        foreach (var (_, ls) in linksets)
        {
            if (ls.Root.LocalID == 0)
            {
                log("[ImportExport] Skipping linkset with missing root prim.");
                continue;
            }

            RezLinkset(sim, ls.Root, ls.Children, rezPos, textureMode, textureMap, log);
        }
    }

    private void RezLinkset(Simulator sim, Primitive root, List<Primitive> children,
        Vector3 rezPos, TextureMode textureMode, Dictionary<UUID, UUID> textureMap,
        Action<string> log)
    {
        var primsCreated = new List<Primitive>();
        uint rootLocalId = 0;
        var primDone = new AutoResetEvent(false);

        EventHandler<PrimEventArgs> onNewPrim = null!;
        onNewPrim = (_, e) =>
        {
            var p = e.Prim;
            if ((p.Flags & PrimFlags.CreateSelected) == 0) return;
            lock (primsCreated)
            {
                if (!primsCreated.Contains(p))
                    primsCreated.Add(p);
            }
            primDone.Set();
        };

        _client.Objects.ObjectUpdate += onNewPrim;

        try
        {
            // Rez root
            var savedRotation = root.Rotation;
            root.Position = rezPos;
            root.Rotation = Quaternion.Identity;

            _client.Objects.AddPrim(sim, root.PrimData, UUID.Zero, rezPos, root.Scale, root.Rotation);
            if (!primDone.WaitOne(10_000))
            {
                log("[ImportExport] Timed out waiting for root prim.");
                return;
            }

            lock (primsCreated)
                rootLocalId = primsCreated[^1].LocalID;

            _client.Objects.SetPosition(sim, rootLocalId, rezPos);
            ApplyPrimProperties(sim, rootLocalId, root, textureMode, textureMap);

            // Rez children
            var childIds = new List<uint> { rootLocalId };
            foreach (var child in children)
            {
                var childPos = child.Position + rezPos;
                _client.Objects.AddPrim(sim, child.PrimData, UUID.Zero, childPos, child.Scale, child.Rotation);
                if (!primDone.WaitOne(10_000))
                {
                    log($"[ImportExport] Timed out waiting for child prim.");
                    continue;
                }

                uint childLocalId;
                lock (primsCreated)
                    childLocalId = primsCreated[^1].LocalID;

                _client.Objects.SetPosition(sim, childLocalId, childPos);
                ApplyPrimProperties(sim, childLocalId, child, textureMode, textureMap);
                childIds.Add(childLocalId);
            }

            // Link if needed
            if (children.Count > 0)
            {
                _client.Objects.LinkPrims(sim, childIds);
                Thread.Sleep(1000);
            }

            // Restore root rotation
            _client.Objects.SetRotation(sim, rootLocalId, savedRotation);

            // Grant full perms to ourselves
            _client.Objects.SetPermissions(sim, childIds,
                PermissionWho.Everyone | PermissionWho.Group | PermissionWho.NextOwner,
                PermissionMask.All, true);

            log($"[ImportExport] Rezzed linkset with {1 + children.Count} prim(s).");
        }
        finally
        {
            _client.Objects.ObjectUpdate -= onNewPrim;
        }
    }

    private void ApplyPrimProperties(Simulator sim, uint localId, Primitive src,
        TextureMode textureMode, Dictionary<UUID, UUID> textureMap)
    {
        if (src.Textures != null)
        {
            var textures = RemapTextures(src.Textures, textureMode, textureMap);
            _client.Objects.SetTextures(sim, localId, textures);
        }

        if (src.Light != null && src.Light.Intensity > 0)
            _client.Objects.SetLight(sim, localId, src.Light);

        if (src.Flexible != null)
            _client.Objects.SetFlexible(sim, localId, src.Flexible);

        if (src.Sculpt?.SculptTexture is { } st && st != UUID.Zero)
        {
            var sculpt = new Primitive.SculptData
            {
                SculptTexture = RemapUUID(st, textureMode, textureMap),
                Type = src.Sculpt.Type
            };
            _client.Objects.SetSculpt(sim, localId, sculpt);
        }

        if (src.Properties?.Name is { Length: > 0 } name)
            _client.Objects.SetName(sim, localId, name);

        if (src.Properties?.Description is { Length: > 0 } desc)
            _client.Objects.SetDescription(sim, localId, desc);
    }

    private static Primitive.TextureEntry RemapTextures(Primitive.TextureEntry src,
        TextureMode mode, Dictionary<UUID, UUID> map)
    {
        if (mode == TextureMode.White)
        {
            var blank = new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE);
            return blank;
        }

        // Clone by round-tripping through bytes
        var copy = new Primitive.TextureEntry(src.GetBytes(), 0, src.GetBytes().Length);

        if (mode == TextureMode.Original) return copy;

        // Reupload mode — remap UUIDs
        if (copy.DefaultTexture != null)
            copy.DefaultTexture.TextureID = RemapUUID(copy.DefaultTexture.TextureID, mode, map);

        if (copy.FaceTextures != null)
        {
            for (int i = 0; i < copy.FaceTextures.Length; i++)
            {
                if (copy.FaceTextures[i] != null)
                    copy.FaceTextures[i].TextureID = RemapUUID(copy.FaceTextures[i].TextureID, mode, map);
            }
        }

        return copy;
    }

    private static UUID RemapUUID(UUID id, TextureMode mode, Dictionary<UUID, UUID> map)
    {
        if (mode == TextureMode.White) return Primitive.TextureEntry.WHITE_TEXTURE;
        if (mode == TextureMode.Reupload && map.TryGetValue(id, out var newId)) return newId;
        return id;
    }

    private UUID UploadTexture(byte[] data, string name, Action<string> log)
    {
        var folder = _client.Inventory.FindFolderForType(AssetType.Texture);
        var uploadTask = System.Threading.Tasks.Task.Run(() =>
            _client.Inventory.CreateItemFromAssetAsync(data, name, string.Empty,
                AssetType.Texture, InventoryType.Texture, folder,
                Permissions.FullPermissions));

        if (!uploadTask.Wait(TimeSpan.FromSeconds(60)))
        {
            log($"[ImportExport] Texture upload timed out: {name}");
            return UUID.Zero;
        }

        var r = uploadTask.Result;
        if (r.Success)
            log($"[ImportExport] Uploaded texture {name} → {r.AssetID}");
        else
            log($"[ImportExport] Failed to upload texture {name}: {r.Status}");

        return r.Success ? r.AssetID : UUID.Zero;
    }
}
