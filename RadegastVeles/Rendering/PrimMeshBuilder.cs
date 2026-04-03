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
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using Radegast.Veles.Core;
using SkiaSharp;
// Alias OpenTK math types to avoid conflicts with identically-named OpenMetaverse types.
using TkMatrix4    = OpenTK.Mathematics.Matrix4;
using TkVector3    = OpenTK.Mathematics.Vector3;
using TkVector4    = OpenTK.Mathematics.Vector4;
using TkQuaternion = OpenTK.Mathematics.Quaternion;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Shared tessellation + texture pipeline used by prim and HUD viewers.
/// Converts a linkset of <see cref="Primitive"/> objects into a
/// <see cref="PrimRenderSubmission"/> ready for <see cref="GlViewportControl.Submit"/>.
/// </summary>
internal sealed class PrimMeshBuilder(GridClient client)
{
    private readonly MeshmerizerR _mesher = new();

    private record RawFace(
        float[]   Vertices,
        ushort[]  Indices,
        TkVector4 Color,
        bool      Fullbright,
        float     Glow,
        bool      HasAlpha,
        UUID      TextureId,
        TkMatrix4 Transform,
        uint      PrimLocalId,
        int       FaceIndex);

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="PrimRenderSubmission"/> from a linkset.
    /// Progress messages are reported via <paramref name="progress"/> on the UI thread.
    /// </summary>
    public async Task<PrimRenderSubmission> BuildAsync(
        IReadOnlyList<Primitive> prims,
        uint                    rootLocalId,
        string                  label,
        IProgress<string>?      progress,
        CancellationToken       ct)
    {
        var (rawFaces, bMin, bMax) = await TessellateAsync(prims, rootLocalId, progress, ct)
                                           .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        int texCount = CountUniqueTextures(rawFaces);
        progress?.Report($"Loading textures (0 / {texCount})…");

        var faces = await FetchTexturesAsync(rawFaces, texCount, progress, ct)
                          .ConfigureAwait(false);

        return new PrimRenderSubmission
        {
            Label     = label,
            Faces     = faces.ToArray(),
            BoundsMin = bMin,
            BoundsMax = bMax,
        };
    }

    // ── Tessellation ──────────────────────────────────────────────────────────────

    private async Task<(List<RawFace> faces, TkVector3 bMin, TkVector3 bMax)> TessellateAsync(
        IReadOnlyList<Primitive> prims,
        uint                    rootLocalId,
        IProgress<string>?      progress,
        CancellationToken       ct)
    {
        var faces = new List<RawFace>();
        var bMin  = new TkVector3(float.MaxValue);
        var bMax  = new TkVector3(float.MinValue);

        var rootRot = ToTkQuaternion(prims[0].Rotation);

        for (int pi = 0; pi < prims.Count; pi++)
        {
            ct.ThrowIfCancellationRequested();
            var prim    = prims[pi];
            int primNum = pi + 1;
            progress?.Report($"Building mesh ({primNum} / {prims.Count})…");

            // ── Sculpt / Mesh / Parametric ────────────────────────────────
            var mesh = await GetPrimMeshAsync(prim, ct).ConfigureAwait(false);
            if (mesh == null) continue;

            // ── Build per-prim transform ──────────────────────────────────
            var scale = new TkVector3(prim.Scale.X, prim.Scale.Y, prim.Scale.Z);
            TkMatrix4 transform;
            if (prim.LocalID == rootLocalId)
            {
                transform = TkMatrix4.CreateScale(scale)
                          * TkMatrix4.CreateFromQuaternion(rootRot);
            }
            else
            {
                var pos = new TkVector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
                var rot = ToTkQuaternion(prim.Rotation);
                transform = TkMatrix4.CreateScale(scale)
                          * TkMatrix4.CreateFromQuaternion(rot)
                          * TkMatrix4.CreateTranslation(pos)
                          * TkMatrix4.CreateFromQuaternion(rootRot);
            }

            AppendFaces(mesh, prim, transform, faces, ref bMin, ref bMax);
        }

        if (faces.Count == 0 || bMin.X == float.MaxValue)
        {
            bMin = new TkVector3(-0.5f);
            bMax = new TkVector3( 0.5f);
        }

        return (faces, bMin, bMax);
    }

    private async Task<FacetedMesh?> DownloadMeshAsync(Primitive prim, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<FacetedMesh?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetResult(null));

        client.Assets.RequestMesh(prim.Sculpt.SculptTexture, (success, meshAsset) =>
        {
            if (success && FacetedMesh.TryDecodeFromAsset(prim, meshAsset, DetailLevel.High, out var result))
                tcs.TrySetResult(result);
            else
                tcs.TrySetResult(null);
        });

        return await tcs.Task;
    }

    // ── Texture fetching ──────────────────────────────────────────────────────────

    private static int CountUniqueTextures(List<RawFace> faces) =>
        faces.Select(f => f.TextureId).Where(id => id != UUID.Zero).Distinct().Count();

    private async Task<List<PrimRenderFace>> FetchTexturesAsync(
        List<RawFace>      rawFaces,
        int                total,
        IProgress<string>? progress,
        CancellationToken  ct,
        Dictionary<UUID, (UUID AvatarId, string BakeName)>? serverBakedMeta = null)
    {
        var uniqueIds = rawFaces
            .Select(f => f.TextureId)
            .Where(id => id != UUID.Zero)
            .Distinct()
            .ToList();

        int loaded   = 0;
        var textures = new Dictionary<UUID, SKBitmap?>();

        var tasks = uniqueIds.Select(id => Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            SKBitmap? bmp;
            if (serverBakedMeta != null && serverBakedMeta.TryGetValue(id, out var bakeMeta))
                bmp = await GridTextureHelper.DownloadServerBakedSkBitmapAsync(
                                client, bakeMeta.AvatarId, id, bakeMeta.BakeName, linked.Token)
                                             .ConfigureAwait(false);
            else
                bmp = await GridTextureHelper.DownloadSkBitmapAsync(client, id, linked.Token)
                                             .ConfigureAwait(false);

            lock (textures) textures[id] = bmp;

            int n = Interlocked.Increment(ref loaded);
            progress?.Report($"Loading textures ({n} / {total})…");
        }, ct)).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return rawFaces.Select(rf =>
        {
            SKBitmap? tex = null;
            if (rf.TextureId != UUID.Zero)
                lock (textures) textures.TryGetValue(rf.TextureId, out tex);

            bool hasAlpha = rf.HasAlpha
                         || (tex != null && tex.AlphaType != SKAlphaType.Opaque);

            return new PrimRenderFace
            {
                Vertices    = rf.Vertices,
                Indices     = rf.Indices,
                Color       = rf.Color,
                Fullbright  = rf.Fullbright,
                Glow        = rf.Glow,
                HasAlpha    = hasAlpha,
                Transform   = rf.Transform,
                Texture     = tex,
                PrimLocalId = rf.PrimLocalId,
                FaceIndex   = rf.FaceIndex,
            };
        }).ToList();
    }

    // ── Shared prim helpers ───────────────────────────────────────────────────────

    /// <summary>Tessellates a single prim into a <see cref="FacetedMesh"/>.</summary>
    private async Task<FacetedMesh?> GetPrimMeshAsync(Primitive prim, CancellationToken ct)
    {
        if (prim.Sculpt != null && prim.Sculpt.SculptTexture != UUID.Zero)
        {
            if (prim.Sculpt.Type != SculptType.Mesh)
            {
                var sculptBmp = await GridTextureHelper.DownloadSkBitmapAsync(
                    client, prim.Sculpt.SculptTexture, ct).ConfigureAwait(false);
                if (sculptBmp != null)
                {
                    using (sculptBmp)
                        return _mesher.GenerateFacetedSculptMesh(prim, sculptBmp, DetailLevel.High);
                }
                // Fallback: parametric if sculpt texture unavailable.
                return _mesher.GenerateFacetedMesh(prim, DetailLevel.High);
            }
            else
            {
                return await DownloadMeshAsync(prim, ct).ConfigureAwait(false);
            }
        }
        return _mesher.GenerateFacetedMesh(prim, DetailLevel.High);
    }

    /// <summary>
    /// Packs all faces of <paramref name="mesh"/> into <paramref name="faces"/> using
    /// the supplied per-prim <paramref name="transform"/>, and expands the AABB.
    /// </summary>
    private void AppendFaces(
        FacetedMesh   mesh,
        Primitive     prim,
        TkMatrix4     transform,
        List<RawFace> faces,
        ref TkVector3 bMin,
        ref TkVector3 bMax)
    {
        for (int fi = 0; fi < mesh.Faces.Count; fi++)
        {
            var face = mesh.Faces[fi];
            if (face.Vertices.Count == 0) continue;

            var texFace = prim.Textures?.GetFace((uint)fi);
            if (texFace != null)
                _mesher.TransformTexCoords(face.Vertices, face.Center, texFace, prim.Scale);

            // Pack into interleaved float array: position(3) + normal(3) + uv(2) = 8 floats.
            float[] verts = new float[face.Vertices.Count * 8];
            for (int vi = 0; vi < face.Vertices.Count; vi++)
            {
                var v = face.Vertices[vi];
                int o = vi * 8;
                verts[o + 0] = v.Position.X;
                verts[o + 1] = v.Position.Y;
                verts[o + 2] = v.Position.Z;
                verts[o + 3] = v.Normal.X;
                verts[o + 4] = v.Normal.Y;
                verts[o + 5] = v.Normal.Z;
                verts[o + 6] = v.TexCoord.X;
                verts[o + 7] = v.TexCoord.Y;

                // Accumulate world-space AABB for camera framing.
                var wp = TkVector3.TransformPosition(
                    new TkVector3(v.Position.X, v.Position.Y, v.Position.Z), transform);
                bMin = TkVector3.ComponentMin(bMin, wp);
                bMax = TkVector3.ComponentMax(bMax, wp);
            }

            ushort[] indices = face.Indices.ToArray();

            float r = 1f, g = 1f, b = 1f, a = 1f;
            bool  fullbright = false;
            float glow       = 0f;
            bool  hasAlpha   = false;
            UUID  texId      = UUID.Zero;

            if (texFace != null)
            {
                r          = texFace.RGBA.R;
                g          = texFace.RGBA.G;
                b          = texFace.RGBA.B;
                a          = texFace.RGBA.A;
                fullbright = texFace.Fullbright;
                glow       = texFace.Glow;
                hasAlpha   = a < 0.99f;
                texId      = texFace.TextureID;
            }

            faces.Add(new RawFace(verts, indices,
                new TkVector4(r, g, b, a), fullbright, glow, hasAlpha, texId, transform,
                prim.LocalID, fi));
        }
    }

    // ── Avatar attachment rendering ───────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="PrimRenderSubmission"/> from all non-HUD attachment prims
    /// worn by an avatar.  Each attachment root is positioned using the skeleton's
    /// bone hierarchy and the attachment-point offsets from <c>avatar_lad.xml</c>,
    /// matching the transform chain used by the legacy SceneWindow renderer.
    /// </summary>
    public async Task<PrimRenderSubmission> BuildAvatarAttachmentsAsync(
        IReadOnlyList<Primitive> prims,
        uint                    avatarLocalId,
        AvatarSkeleton          skeleton,
        string                  label,
        IProgress<string>?      progress,
        CancellationToken       ct)
    {
        var (rawFaces, bMin, bMax, serverBakedMeta) = await TessellateAvatarAsync(prims, avatarLocalId, skeleton, progress, ct)
                                                           .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        int texCount = CountUniqueTextures(rawFaces);
        progress?.Report($"Loading textures (0 / {texCount})…");

        var faces = await FetchTexturesAsync(rawFaces, texCount, progress, ct, serverBakedMeta)
                          .ConfigureAwait(false);

        return new PrimRenderSubmission
        {
            Label     = label,
            Faces     = faces.ToArray(),
            BoundsMin = bMin,
            BoundsMax = bMax,
        };
    }

    private async Task<(List<RawFace> faces, TkVector3 bMin, TkVector3 bMax, Dictionary<UUID, (UUID AvatarId, string BakeName)> serverBakedMeta)> TessellateAvatarAsync(
        IReadOnlyList<Primitive> prims,
        uint                    avatarLocalId,
        AvatarSkeleton          skeleton,
        IProgress<string>?      progress,
        CancellationToken       ct)
    {
        var faces = new List<RawFace>();
        var bMin  = new TkVector3(float.MaxValue);
        var bMax  = new TkVector3(float.MinValue);

        // Render the avatar body meshes first (T-pose — vertices are in avatar-local space).
        var characterDir  = System.IO.Path.Combine(OpenMetaverse.Settings.RESOURCE_DIR, "character");
        var (avatarAgentId, bakeTextures) = ResolveBakeTextures(avatarLocalId);

        // Build server-baked metadata: textureUUID → (avatarAgentId, bakeName).
        // These UUIDs require RequestServerBakedImage — RequestImage silently hangs for them.
        var serverBakedMeta = new Dictionary<UUID, (UUID AvatarId, string BakeName)>();
        foreach (var (_, _, _, _, bakeIndex, bakeName) in BodyParts)
        {
            if (bakeTextures != null && bakeIndex < bakeTextures.Length)
            {
                var texId = bakeTextures[bakeIndex];
                if (texId != UUID.Zero && !serverBakedMeta.ContainsKey(texId))
                    serverBakedMeta[texId] = (avatarAgentId, bakeName);
            }
        }

        await Task.Run(() => AppendBodyMeshes(characterDir, skeleton, bakeTextures, faces, ref bMin, ref bMax),
            ct).ConfigureAwait(false);

        // Process attachment roots first so their transforms are available for children.
        var roots    = prims.Where(p => p.ParentID == avatarLocalId).ToList();
        var children = prims.Where(p => p.ParentID != avatarLocalId).ToList();
        var ordered  = roots.Concat(children).ToList();

        // Maps attachment-root LocalID → world-space (rotation, position) for child lookups.
        var rootXforms = new Dictionary<uint, (TkQuaternion rot, TkVector3 pos)>();

        for (int pi = 0; pi < ordered.Count; pi++)
        {
            ct.ThrowIfCancellationRequested();
            var prim    = ordered[pi];
            int primNum = pi + 1;
            progress?.Report($"Building mesh ({primNum} / {ordered.Count})…");

            var mesh = await GetPrimMeshAsync(prim, ct).ConfigureAwait(false);
            if (mesh == null) continue;

            var scale = new TkVector3(prim.Scale.X, prim.Scale.Y, prim.Scale.Z);
            var primPos = new TkVector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
            var primRot = ToTkQuaternion(prim.Rotation);

            TkMatrix4 transform;
            if (prim.ParentID == avatarLocalId)
            {
                // Attachment root: use skeleton bone + attachment-point transform.
                // This mirrors the legacy SceneWindow.PrimPosAndRot attachment chain:
                //   pos  = bone.getTotalOffset()
                //   rot  = bone.getTotalRotation()
                //   pos += apoint.position * rot
                //   rot *= apoint.rotation
                //   pos += prim.Position * rot
                //   rot *= prim.Rotation
                int apIndex = (int)prim.PrimData.AttachmentPoint;
                if (skeleton.TryGetAttachmentTransform(apIndex,
                        out var apOffset, out var apRotation))
                {
                    var apOff = new TkVector3(apOffset.X, apOffset.Y, apOffset.Z);
                    var apRot = ToTkQuaternion(apRotation);

                    // Compose: prim.Position is relative to the attachment point.
                    var finalPos = apOff
                                 + TkVector3.Transform(primPos, apRot);
                    var finalRot = apRot * primRot;

                    transform = TkMatrix4.CreateScale(scale)
                              * TkMatrix4.CreateFromQuaternion(finalRot)
                              * TkMatrix4.CreateTranslation(finalPos);
                    rootXforms[prim.LocalID] = (finalRot, finalPos);
                }
                else
                {
                    // Unknown attachment point — fall back to raw position.
                    transform = TkMatrix4.CreateScale(scale)
                              * TkMatrix4.CreateFromQuaternion(primRot)
                              * TkMatrix4.CreateTranslation(primPos);
                    rootXforms[prim.LocalID] = (primRot, primPos);
                }
            }
            else if (rootXforms.TryGetValue(prim.ParentID, out var parent))
            {
                // Child of attachment root: position is relative to the root.
                var worldPos = parent.pos
                             + TkVector3.Transform(primPos, parent.rot);
                var worldRot = parent.rot * primRot;

                transform = TkMatrix4.CreateScale(scale)
                          * TkMatrix4.CreateFromQuaternion(worldRot)
                          * TkMatrix4.CreateTranslation(worldPos);
            }
            else
            {
                continue;
            }

            AppendFaces(mesh, prim, transform, faces, ref bMin, ref bMax);
        }

        if (faces.Count == 0 || bMin.X == float.MaxValue)
        {
            bMin = new TkVector3(-0.5f);
            bMax = new TkVector3( 0.5f);
        }

        return (faces, bMin, bMax, serverBakedMeta);
    }

    // ── Body mesh rendering ───────────────────────────────────────────────────────

    // (meshTypeName, filename, fallback color RGBA, bone name for offset — null means avatar origin, AvatarTextureIndex face slot for bake, SSB bake layer name)
    private static readonly (string TypeName, string File, TkVector4 Color, string? BoneName, int BakeIndex, string BakeName)[] BodyParts =
    [
        ("lowerBodyMesh",    "avatar_lower_body.llm",  new TkVector4(0.80f, 0.67f, 0.53f, 1f), null,         10, "lower"), // LowerBaked
        ("upperBodyMesh",    "avatar_upper_body.llm",  new TkVector4(0.80f, 0.67f, 0.53f, 1f), null,          9, "upper"), // UpperBaked
        ("headMesh",         "avatar_head.llm",        new TkVector4(0.80f, 0.67f, 0.53f, 1f), null,          8, "head"),  // HeadBaked
        ("hairMesh",         "avatar_hair.llm",        new TkVector4(0.20f, 0.15f, 0.10f, 1f), null,         20, "hair"),  // HairBaked
        ("eyeBallRightMesh", "avatar_eye.llm",         new TkVector4(0.40f, 0.65f, 0.85f, 1f), "mEyeRight",  11, "eyes"), // EyesBaked
        ("eyeBallLeftMesh",  "avatar_eye.llm",         new TkVector4(0.40f, 0.65f, 0.85f, 1f), "mEyeLeft",   11, "eyes"), // EyesBaked
        ("eyelashMesh",      "avatar_eyelashes.llm",   new TkVector4(0.10f, 0.08f, 0.06f, 1f), "mHead",       8, "head"), // HeadBaked
    ];

    private static void AppendBodyMeshes(
        string          characterDir,
        AvatarSkeleton  skeleton,
        UUID[]?         bakeTextures,
        List<RawFace>   faces,
        ref TkVector3   bMin,
        ref TkVector3   bMax)
    {
        for (int partIndex = 0; partIndex < BodyParts.Length; partIndex++)
        {
            var (typeName, file, color, boneName, bakeIndex, _) = BodyParts[partIndex];
            var path = System.IO.Path.Combine(characterDir, file);
            if (!System.IO.File.Exists(path)) continue;

            var mesh = new LindenMesh(typeName);
            try { mesh.LoadMesh(path); }
            catch { continue; }

            if (mesh.NumVertices == 0 || mesh.NumFaces == 0) continue;

            // Bone offset — eye and eyelash meshes are in bone-local space.
            var boneOff = TkVector3.Zero;
            if (boneName != null && skeleton.TryGetBoneOffset(boneName, out var off))
                boneOff = new TkVector3(off.X, off.Y, off.Z);

            float[] verts = new float[mesh.NumVertices * 8];
            for (int vi = 0; vi < mesh.NumVertices; vi++)
            {
                var v = mesh.Vertices[vi];
                int o = vi * 8;
                var pos = new TkVector3(v.Coord.X, v.Coord.Y, v.Coord.Z) + boneOff;
                verts[o + 0] = pos.X;
                verts[o + 1] = pos.Y;
                verts[o + 2] = pos.Z;
                verts[o + 3] = v.Normal.X;
                verts[o + 4] = v.Normal.Y;
                verts[o + 5] = v.Normal.Z;
                verts[o + 6] = v.TexCoord.X;
                verts[o + 7] = v.TexCoord.Y;

                bMin = TkVector3.ComponentMin(bMin, pos);
                bMax = TkVector3.ComponentMax(bMax, pos);
            }

            ushort[] indices = new ushort[mesh.NumFaces * 3];
            for (int fi = 0; fi < mesh.NumFaces; fi++)
            {
                indices[fi * 3 + 0] = (ushort)mesh.Faces[fi].Indices[0];
                indices[fi * 3 + 1] = (ushort)mesh.Faces[fi].Indices[1];
                indices[fi * 3 + 2] = (ushort)mesh.Faces[fi].Indices[2];
            }

            // Use the avatar's baked texture when available; fall back to a flat skin/hair colour.
            var texId      = bakeTextures != null && bakeIndex < bakeTextures.Length
                             ? bakeTextures[bakeIndex]
                             : UUID.Zero;
            var faceColor  = texId != UUID.Zero ? TkVector4.One : color;

            faces.Add(new RawFace(verts, indices, faceColor,
                false, 0f, false, texId, TkMatrix4.Identity, 0, partIndex));
        }
    }

    private (UUID AvatarAgentId, UUID[]?) ResolveBakeTextures(uint avatarLocalId)
    {
        var sim = client.Network.CurrentSim;
        if (sim == null) return (UUID.Zero, null);

        Avatar? av = null;
        foreach (var a in sim.ObjectsAvatars.Values)
        {
            if (a?.LocalID == avatarLocalId) { av = a; break; }
        }

        if (av?.Textures?.FaceTextures == null) return (UUID.Zero, null);

        var result = new UUID[av.Textures.FaceTextures.Length];
        for (int i = 0; i < av.Textures.FaceTextures.Length; i++)
        {
            var texFace = av.Textures.FaceTextures[i];
            if (texFace != null
                && texFace.TextureID != UUID.Zero
                && texFace.TextureID != AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                result[i] = texFace.TextureID;
        }
        return (av.ID, result);
    }

    // ── Utility ───────────────────────────────────────────────────────────────────

    private static TkQuaternion ToTkQuaternion(OpenMetaverse.Quaternion q) =>
        new(q.X, q.Y, q.Z, q.W);
}
