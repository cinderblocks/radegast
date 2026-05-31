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
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse.Materials;
using OpenMetaverse;
using OpenMetaverse.Assets;
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
    private readonly MeshFoundry _mesher = new();

    private readonly Dictionary<UUID, LegacyMaterial?> _materialCache     = new();
    private readonly object                             _materialCacheLock = new();

    private readonly Dictionary<UUID, AssetMaterial?>   _pbrCache     = new();
    private readonly object                             _pbrCacheLock = new();

    private record RawFace(
        float[]       Vertices,
        int           VerticesLength,
        ushort[]      Indices,
        TkVector4     Color,
        bool          Fullbright,
        float         Glow,
        bool          HasAlpha,
        UUID          TextureId,
        TkMatrix4     Transform,
        uint          PrimLocalId,
        int           FaceIndex,
        TkVector3     Centroid,
        bool          IsTwoSided  = false,
        bool          ForceOpaque = false,
        float         AlphaCutoff = 0.004f,
        float         Shiny       = 0f,
        bool          HasBump     = false,
        FaceAlphaMode AlphaMode   = FaceAlphaMode.None,
        UUID          MaterialId  = default,
        UUID          RenderMaterialId = default);

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
        CancellationToken       ct,
        bool                    isHud               = false,
        DetailLevel             detailLevel         = DetailLevel.High,
        IProgress<SceneTexturePatch>? texturePatch  = null)
    {
        var (rawFaces, bMin, bMax, flexiPrims) = await TessellateAsync(prims, rootLocalId, progress, ct, isHud, detailLevel)
                                                       .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        var materials = await FetchMaterialsAsync(rawFaces, ct).ConfigureAwait(false);

        var pbrMaterials = await FetchPBRMaterialsAsync(rawFaces, ct).ConfigureAwait(false);

        // Build texture-free faces immediately so the caller can submit geometry
        // and make the object visible without waiting for any downloads.
        var faces = BuildFacesWithoutTextures(rawFaces, materials, pbrMaterials);

        // Mark flexi faces so GlViewportControl.ApplySceneTransformOverrides does NOT
        // stomp their identity Transform with the per-frame root world matrix — their
        // vertex buffers are already produced in world space by FlexiPrimAnimator.
        foreach (var fp in flexiPrims)
        {
            int end = Math.Min(fp.FaceStart + fp.FaceCount, faces.Count);
            for (int fi = fp.FaceStart; fi < end; fi++)
                faces[fi].IsFlexi = true;
        }

        var submission = new PrimRenderSubmission
        {
            Label      = label,
            Faces      = faces.ToArray(),
            BoundsMin  = bMin,
            BoundsMax  = bMax,
            FlexiPrims = flexiPrims.ToArray(),
        };

        // Stream textures asynchronously in the background — report each patch as it
        // arrives so the caller can forward it to GlViewportControl.PatchSceneObjectTexture.
        int texCount = CountUniqueTextures(rawFaces, materials, pbrMaterials);
        if (texCount > 0)
        {
            progress?.Report($"Loading textures (0 / {texCount})…");
            _ = StreamTexturesAsync(rawFaces, faces, rootLocalId, texCount, progress,
                                    texturePatch, materials, pbrMaterials, ct);
        }

        return submission;
    }

    // ── Tessellation ──────────────────────────────────────────────────────────────

    private async Task<(List<RawFace> faces, TkVector3 bMin, TkVector3 bMax, List<FlexiPrimInfo> flexiPrims)> TessellateAsync(
        IReadOnlyList<Primitive> prims,
        uint                    rootLocalId,
        IProgress<string>?      progress,
        CancellationToken       ct,
        bool                    isHud       = false,
        DetailLevel             detailLevel = DetailLevel.High)
    {
        var faces      = new List<RawFace>();
        var flexiPrims = new List<FlexiPrimInfo>();
        var bMin       = new TkVector3(float.MaxValue);
        var bMax       = new TkVector3(float.MinValue);

        var rootPrim = prims.FirstOrDefault(p => p.LocalID == rootLocalId) ?? prims[0];
        var rootRot = ToTkQuaternion(rootPrim.Rotation);

        for (int pi = 0; pi < prims.Count; pi++)
        {
            ct.ThrowIfCancellationRequested();
            var prim    = prims[pi];
            int primNum = pi + 1;
            progress?.Report($"Building mesh ({primNum} / {prims.Count})…");

            // ── Sculpt / Mesh / Parametric ────────────────────────────────
            var mesh = await GetPrimMeshAsync(prim, ct, detailLevel).ConfigureAwait(false);
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

            // ── Flexi prim bookkeeping ────────────────────────────────────
            // For flexi prims we capture raw (prim-local, pre-transform) vertex data
            // BEFORE calling AppendFaces so that Z stays in [-0.5, 0.5] — the path
            // parameter the animator needs.  The prim transform is stored in
            // FlexiPrimInfo.AttachTransform and applied by the animator after each
            // deformation tick.
            float[][]? flexiBaseVerts = null;
            if (prim.Flexible != null && prim.PrimData.PathCurve == PathCurve.Flexible)
            {
                int n2 = 0;
                for (int fi = 0; fi < mesh.Faces.Count; fi++)
                    if (mesh.Faces[fi].Vertices.Count > 0) n2++;
                if (n2 > 0)
                {
                    flexiBaseVerts = new float[n2][];
                    int bfi = 0;
                    for (int fi = 0; fi < mesh.Faces.Count; fi++)
                    {
                        var mf = mesh.Faces[fi];
                        if (mf.Vertices.Count == 0) continue;
                        var raw = new float[mf.Vertices.Count * 8];
                        for (int vi = 0; vi < mf.Vertices.Count; vi++)
                        {
                            var v = mf.Vertices[vi];
                            int o = vi * 8;
                            raw[o]     = v.Position.X; raw[o + 1] = v.Position.Y; raw[o + 2] = v.Position.Z;
                            raw[o + 3] = v.Normal.X;   raw[o + 4] = v.Normal.Y;   raw[o + 5] = v.Normal.Z;
                            raw[o + 6] = v.TexCoord.X; raw[o + 7] = v.TexCoord.Y;
                        }
                        flexiBaseVerts[bfi++] = raw;
                    }
                }
            }

            int faceStart = faces.Count;
            // Flexi faces are positioned entirely by the animator via FlexiPrimInfo.AttachTransform;
            // the GPU must NOT apply the rest-pose prim transform on top of the deformed verts.
            AppendFaces(mesh, prim, transform, faces, ref bMin, ref bMax,
                        faceTransformOverride: flexiBaseVerts != null ? TkMatrix4.Identity : (TkMatrix4?)null);

            if (flexiBaseVerts != null)
            {
                int faceCount = faces.Count - faceStart;
                if (faceCount > 0 && faceCount == flexiBaseVerts.Length)
                {
                    int segments = FlexiPrimAnimator.ComputeSegmentCount(prim.Flexible!.Softness);
                    int profileVerts = mesh.Faces.Count > 0 && mesh.Faces[0].Vertices.Count > 0
                        ? mesh.Faces[0].Vertices.Count / Math.Max(1, segments)
                        : 4;

                    flexiPrims.Add(new FlexiPrimInfo
                    {
                        Prim               = prim,
                        FaceStart          = faceStart,
                        FaceCount          = faceCount,
                        BaseVertices       = flexiBaseVerts,
                        PathSegments       = segments,
                        ProfileVertexCount = profileVerts,
                        Scale              = new Vector3(prim.Scale.X, prim.Scale.Y, prim.Scale.Z),
                        AttachTransform    = transform,
                    });
                }
            }
        }

        if (faces.Count == 0 || bMin.X == float.MaxValue)
        {
            bMin = new TkVector3(-0.5f);
            bMax = new TkVector3( 0.5f);
        }

        return (faces, bMin, bMax, flexiPrims);
    }

    private async Task<FacetedMesh?> DownloadMeshAsync(Primitive prim, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<FacetedMesh?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetResult(null));

        client.Assets.RequestMesh(prim.Sculpt!.SculptTexture, (success, meshAsset) =>
        {
            if (success && meshAsset != null)
                tcs.TrySetResult(_mesher.GenerateFacetedMeshMesh(prim, meshAsset.AssetData));
            else
                tcs.TrySetResult(null);
        });

        return await tcs.Task;
    }

    // ── Texture fetching ──────────────────────────────────────────────────────────

    private static int CountUniqueTextures(
        List<RawFace> faces,
        Dictionary<UUID, LegacyMaterial>? materials = null,
        Dictionary<UUID, AssetMaterial>? pbrMaterials = null)
    {
        var ids = new HashSet<UUID>();
        foreach (var f in faces)
        {
            if (f.TextureId != UUID.Zero) ids.Add(f.TextureId);

            // PBR textures take priority over legacy when present.
            if (pbrMaterials != null && f.RenderMaterialId != UUID.Zero
                && pbrMaterials.TryGetValue(f.RenderMaterialId, out var pbr))
            {
                for (int i = 0; i < AssetMaterial.TEXTURE_COUNT; i++)
                {
                    if (pbr.TextureIds[i] != UUID.Zero)
                        ids.Add(pbr.TextureIds[i]);
                }
            }
            else if (materials != null && f.MaterialId != UUID.Zero
                && materials.TryGetValue(f.MaterialId, out var mat))
            {
                if (mat.NormalMap != UUID.Zero)   ids.Add(mat.NormalMap);
                if (mat.SpecularMap != UUID.Zero)  ids.Add(mat.SpecularMap);
            }
        }
        return ids.Count;
    }

    /// <summary>
    /// Constructs a <see cref="PrimRenderFace"/> list with all material / alpha state
    /// resolved but <em>no</em> texture bitmaps attached (all texture fields are null).
    /// Texture delivery is deferred to <see cref="StreamTexturesAsync"/>.
    /// </summary>
    private static List<PrimRenderFace> BuildFacesWithoutTextures(
        List<RawFace>                     rawFaces,
        Dictionary<UUID, LegacyMaterial>? materials,
        Dictionary<UUID, AssetMaterial>?  pbrMaterials)
    {
        var result = new List<PrimRenderFace>(rawFaces.Count);
        foreach (var rf in rawFaces)
        {
            // Resolve PBR material if present.
            AssetMaterial? pbr  = null;
            bool isPBR = pbrMaterials != null && rf.RenderMaterialId != UUID.Zero
                      && pbrMaterials.TryGetValue(rf.RenderMaterialId, out pbr);

            LegacyMaterial? mat  = null;
            bool hasMaterial = !isPBR && materials != null && rf.MaterialId != UUID.Zero
                             && materials.TryGetValue(rf.MaterialId, out mat);

            if (isPBR && pbr != null)
            {
                // PBR alpha mode
                FaceAlphaMode alphaMode;
                bool hasAlpha;
                if (rf.ForceOpaque)
                {
                    hasAlpha  = false;
                    alphaMode = FaceAlphaMode.None;
                }
                else
                {
                    alphaMode = pbr.AlphaMode switch
                    {
                        GltfAlphaMode.Blend => FaceAlphaMode.Blend,
                        GltfAlphaMode.Mask  => FaceAlphaMode.Mask,
                        _                   => FaceAlphaMode.None,
                    };
                    hasAlpha = alphaMode == FaceAlphaMode.Blend;
                }

                float alphaCutoff = alphaMode == FaceAlphaMode.Mask ? pbr.AlphaCutoff : 0.004f;

                var bcf = pbr.BaseColorFactor;
                var baseColorFactor = new TkVector4(
                    bcf.R * rf.Color.X, bcf.G * rf.Color.Y,
                    bcf.B * rf.Color.Z, bcf.A * rf.Color.W);

                static UvTransform ToUvXform(GltfTextureTransform t) =>
                    new(t.Offset.X, t.Offset.Y, t.Scale.X, t.Scale.Y, t.Rotation);

                result.Add(new PrimRenderFace
                {
                    Vertices                 = rf.Vertices,
                    VerticesLength           = rf.VerticesLength,
                    Indices                  = rf.Indices,
                    Color                    = rf.Color,
                    Fullbright               = rf.Fullbright,
                    Glow                     = rf.Glow,
                    HasAlpha                 = hasAlpha,
                    AlphaMode                = alphaMode,
                    AlphaCutoff              = alphaCutoff,
                    Transform                = rf.Transform,
                    PrimLocalId              = rf.PrimLocalId,
                    FaceIndex                = rf.FaceIndex,
                    Centroid                 = rf.Centroid,
                    IsTwoSided               = pbr.DoubleSided || rf.IsTwoSided,
                    IsPBR                    = true,
                    BaseColorFactor          = baseColorFactor,
                    MetallicFactor           = pbr.MetallicFactor,
                    RoughnessFactor          = pbr.RoughnessFactor,
                    EmissiveFactor           = new TkVector3(
                        pbr.EmissiveFactor.X, pbr.EmissiveFactor.Y, pbr.EmissiveFactor.Z),
                    BaseColorUvXform         = ToUvXform(pbr.TextureTransforms[AssetMaterial.TEXTURE_BASE_COLOR]),
                    PbrNormalUvXform         = ToUvXform(pbr.TextureTransforms[AssetMaterial.TEXTURE_NORMAL]),
                    MetallicRoughnessUvXform = ToUvXform(pbr.TextureTransforms[AssetMaterial.TEXTURE_METALLIC_ROUGHNESS]),
                    EmissiveUvXform          = ToUvXform(pbr.TextureTransforms[AssetMaterial.TEXTURE_EMISSIVE]),
                });
            }
            else
            {
                // Legacy path — alpha mode without knowledge of texture alpha yet.
                FaceAlphaMode legacyAlphaMode;
                bool legacyHasAlpha;
                if (rf.ForceOpaque)
                {
                    legacyHasAlpha  = false;
                    legacyAlphaMode = FaceAlphaMode.None;
                }
                else if (mat != null && mat.DiffuseAlphaMode != LegacyMaterialAlphaMode.Default)
                {
                    legacyAlphaMode = (FaceAlphaMode)(byte)mat.DiffuseAlphaMode;
                    legacyHasAlpha  = legacyAlphaMode == FaceAlphaMode.Blend;
                }
                else
                {
                    legacyAlphaMode = rf.AlphaMode;
                    legacyHasAlpha  = legacyAlphaMode == FaceAlphaMode.Blend;
                }

                float legacyAlphaCutoff = rf.AlphaCutoff;
                if (mat != null && legacyAlphaMode == FaceAlphaMode.Mask)
                    legacyAlphaCutoff = mat.AlphaMaskCutoff / 255f;

                var normalUv  = UvTransform.Default;
                var specUv    = UvTransform.Default;
                var specColor = TkVector4.One;
                float specExp = 0f;
                float envInt  = 0f;
                if (mat != null)
                {
                    normalUv = new UvTransform(
                        (float)mat.NormalMapOffsetX, (float)mat.NormalMapOffsetY,
                        (float)mat.NormalMapRepeatX, (float)mat.NormalMapRepeatY,
                        (float)mat.NormalMapRotation);
                    specUv = new UvTransform(
                        (float)mat.SpecularMapOffsetX, (float)mat.SpecularMapOffsetY,
                        (float)mat.SpecularMapRepeatX, (float)mat.SpecularMapRepeatY,
                        (float)mat.SpecularMapRotation);
                    specColor = new TkVector4(
                        mat.SpecularColor.R, mat.SpecularColor.G,
                        mat.SpecularColor.B, mat.SpecularColor.A);
                    specExp = mat.SpecularExponent / 255f;
                    envInt  = mat.EnvironmentIntensity / 255f;
                }

                result.Add(new PrimRenderFace
                {
                    Vertices             = rf.Vertices,
                    VerticesLength       = rf.VerticesLength,
                    Indices              = rf.Indices,
                    Color                = rf.Color,
                    Fullbright           = rf.Fullbright,
                    Glow                 = rf.Glow,
                    HasAlpha             = legacyHasAlpha,
                    AlphaMode            = legacyAlphaMode,
                    Shiny                = rf.Shiny,
                    HasBump              = rf.HasBump,
                    Transform            = rf.Transform,
                    PrimLocalId          = rf.PrimLocalId,
                    FaceIndex            = rf.FaceIndex,
                    Centroid             = rf.Centroid,
                    IsTwoSided           = rf.IsTwoSided,
                    AlphaCutoff          = legacyAlphaCutoff,
                    HasMaterial          = hasMaterial && mat != null,
                    SpecularColor        = specColor,
                    SpecularExponent     = specExp,
                    EnvironmentIntensity = envInt,
                    NormalUvXform        = normalUv,
                    SpecularUvXform      = specUv,
                });
            }
        }
        return result;
    }

    /// <summary>
    /// Downloads each unique texture for the linkset and reports a
    /// <see cref="SceneTexturePatch"/> per face/slot as each bitmap arrives.
    /// This runs in the background after the geometry submission has already been
    /// forwarded to the viewport; the viewport applies each patch on the next frame.
    /// </summary>
    // Higher asset-pipeline priority for attachment textures so they arrive before
    // distant scene-object textures.  Matches the SL viewer's boost for worn content.
    private const float AttachmentTexturePriority = 200000f;

    // Global cap on concurrent texture downloads across ALL PrimMeshBuilder instances.
    // Each gate slot spans: network I/O → J2K decode → Preprocess (flip/convert) →
    // spin-wait inside PatchSceneObjectTexture until the GL queue drains below 2 000.
    // 8 slots keeps the network pipeline full while bounding peak in-flight bitmap RAM
    // to ~8 × 4 MB (worst-case 4K RGBA) = ~32 MB, vs. the multi-GB seen without any gate.
    private static readonly SemaphoreSlim GlobalTextureGate = new(8, 8);

    private async Task StreamTexturesAsync(
        List<RawFace>                     rawFaces,
        List<PrimRenderFace>              faces,
        uint                              rootLocalId,
        int                               total,
        IProgress<string>?                progress,
        IProgress<SceneTexturePatch>?     texturePatch,
        Dictionary<UUID, LegacyMaterial>? materials,
        Dictionary<UUID, AssetMaterial>?  pbrMaterials,
        CancellationToken                 ct,
        float                             texturePriority = 101300f)
    {
        // Build a map from UUID → list of (primLocalId, faceIndex, slot) so a single
        // download satisfies every face that shares the same texture.
        // Each entry carries the prim's own LocalID because child prims in a linkset
        // have PrimLocalId != rootLocalId; the viewport matches by PrimLocalId + FaceIndex.
        var slotMap = new Dictionary<UUID, List<(uint PrimLocalId, int FaceIndex, TextureSlot Slot)>>();

        for (int fi = 0; fi < rawFaces.Count; fi++)
        {
            var rf = rawFaces[fi];

            void Register(UUID id, TextureSlot slot)
            {
                if (id == UUID.Zero) return;
                if (!slotMap.TryGetValue(id, out var list))
                    slotMap[id] = list = new List<(uint, int, TextureSlot)>();
                list.Add((rf.PrimLocalId, rf.FaceIndex, slot));
            }

            if (pbrMaterials != null && rf.RenderMaterialId != UUID.Zero
                && pbrMaterials.TryGetValue(rf.RenderMaterialId, out var pbr))
            {
                Register(pbr.TextureIds[AssetMaterial.TEXTURE_BASE_COLOR], TextureSlot.Albedo);
                Register(pbr.TextureIds[AssetMaterial.TEXTURE_NORMAL],     TextureSlot.Normal);
                Register(pbr.TextureIds[AssetMaterial.TEXTURE_METALLIC_ROUGHNESS], TextureSlot.MetallicRoughness);
                Register(pbr.TextureIds[AssetMaterial.TEXTURE_EMISSIVE],   TextureSlot.Emissive);
                // Albedo fallback to diffuse TE texture when PBR base-color slot is empty.
                if (pbr.TextureIds[AssetMaterial.TEXTURE_BASE_COLOR] == UUID.Zero)
                    Register(rf.TextureId, TextureSlot.Albedo);
            }
            else
            {
                Register(rf.TextureId, TextureSlot.Albedo);
                if (materials != null && rf.MaterialId != UUID.Zero
                    && materials.TryGetValue(rf.MaterialId, out var mat))
                {
                    Register(mat.NormalMap,   TextureSlot.Normal);
                    Register(mat.SpecularMap, TextureSlot.Specular);
                }
            }
        }

        int loaded = 0;

        var tasks = slotMap.Select(kvp => Task.Run(async () =>
        {
            // Acquire the global gate — this bounds total in-flight bitmaps across
            // all linksets so we never hold thousands of decoded SKBitmaps in RAM
            // while the GL thread drains the upload queue at 5 objects/frame.
            await GlobalTextureGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var id    = kvp.Key;
                var slots = kvp.Value;

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                // Progressive decode callback: report partial bitmaps as they arrive.
                // Only send the partial preview to the *first* slot that uses this texture.
                // Fanning out a full SKBitmap copy to every slot on every progress tick
                // (potentially N×4 MB every 250 ms) creates enormous GC pressure when
                // many objects load simultaneously.  The first slot is enough to show
                // progressive feedback; the final delivery below still patches all slots.
                IProgress<SKBitmap>? progressiveDecode = (texturePatch != null && slots.Count > 0)
                    ? new Progress<SKBitmap>(partial =>
                    {
                        var (primLocalId, faceIndex, slot) = slots[0];
                        var copy = partial.Copy(partial.ColorType);
                        partial.Dispose(); // Copy() is synchronous; dispose source to prevent native memory leak
                        if (copy != null)
                            texturePatch.Report(new SceneTexturePatch(primLocalId, faceIndex, slot, copy));
                    })
                    : null;

                var bmp = await GridTextureHelper.DownloadSkBitmapAsync(
                              client, id, progress: progressiveDecode, ct: linked.Token,
                              priority: texturePriority)
                                                  .ConfigureAwait(false);

                int n = Interlocked.Increment(ref loaded);
                progress?.Report($"Loading textures ({n} / {total})…");

                if (bmp == null) return;

                // Final (full-quality) bitmap: report one patch per face/slot that uses it.
                // Ownership of `bmp` transfers to the last Report call.  If Report throws
                // (e.g. OCE from PatchSceneObjectTexture) before we reach the last slot,
                // `bmp` would leak; wrapping in try/finally ensures disposal on any exit.
                if (texturePatch != null)
                {
                    bool bmpOwned = true; // we still own bmp until the last Report succeeds
                    try
                    {
                        for (int si = 0; si < slots.Count; si++)
                        {
                            var (primLocalId, faceIndex, slot) = slots[si];
                            // Last slot takes the original bitmap; earlier ones get a copy.
                            var delivery = (si < slots.Count - 1) ? bmp.Copy(bmp.ColorType) : bmp;
                            if (delivery != null)
                            {
                                if (si == slots.Count - 1) bmpOwned = false; // transfer ownership
                                texturePatch.Report(new SceneTexturePatch(primLocalId, faceIndex, slot, delivery));
                            }
                        }
                    }
                    finally
                    {
                        if (bmpOwned) bmp.Dispose();
                    }
                }
                else
                {
                    bmp.Dispose();
                }
            }
            finally
            {
                GlobalTextureGate.Release();
            }
        }, ct)).ToList();

        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    // ── Material fetching ────────────────────────────────────────────────────────

    // ── Material fetching ────────────────────────────────────────────────────────

    private async Task<Dictionary<UUID, LegacyMaterial>?> FetchMaterialsAsync(
        List<RawFace>     rawFaces,
        CancellationToken ct)
    {
        var needed = new HashSet<UUID>();
        foreach (var rf in rawFaces)
        {
            if (rf.MaterialId == UUID.Zero) continue;
            lock (_materialCacheLock)
            {
                if (_materialCache.ContainsKey(rf.MaterialId)) continue;
            }
            needed.Add(rf.MaterialId);
        }

        if (needed.Count == 0)
        {
            // Return cached results if any faces reference materials already cached.
            lock (_materialCacheLock)
            {
                if (_materialCache.Count == 0) return null;
                var cached = new Dictionary<UUID, LegacyMaterial>();
                foreach (var rf in rawFaces)
                {
                    if (rf.MaterialId != UUID.Zero
                        && _materialCache.TryGetValue(rf.MaterialId, out var m) && m != null
                        && !cached.ContainsKey(rf.MaterialId))
                        cached[rf.MaterialId] = m;
                }
                return cached.Count > 0 ? cached : null;
            }
        }

        var sim = client.Network.CurrentSim;
        if (sim == null) return null;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var fetched = await client.Objects.RequestMaterials(sim, needed, linked.Token)
                                              .ConfigureAwait(false);

            var byId = new Dictionary<UUID, LegacyMaterial>();
            foreach (var mat in fetched)
                byId[mat.ID] = mat;

            lock (_materialCacheLock)
            {
                foreach (var id in needed)
                    _materialCache[id] = byId.TryGetValue(id, out var m) ? m : null;
            }
        }
        catch
        {
            lock (_materialCacheLock)
            {
                foreach (var id in needed)
                    _materialCache.TryAdd(id, null);
            }
        }

        // Build the final result dict from cache for all faces in this build.
        lock (_materialCacheLock)
        {
            var result = new Dictionary<UUID, LegacyMaterial>();
            foreach (var rf in rawFaces)
            {
                if (rf.MaterialId != UUID.Zero
                    && _materialCache.TryGetValue(rf.MaterialId, out var m) && m != null
                    && !result.ContainsKey(rf.MaterialId))
                    result[rf.MaterialId] = m;
            }
            return result.Count > 0 ? result : null;
        }
    }

    // ── Shared prim helpers ───────────────────────────────────────────────────────

    /// <summary>Fetches GLTF PBR render materials for faces that have a RenderMaterialID.</summary>
    private async Task<Dictionary<UUID, AssetMaterial>?> FetchPBRMaterialsAsync(
        List<RawFace>     rawFaces,
        CancellationToken ct)
    {
        var needed = new HashSet<UUID>();
        foreach (var rf in rawFaces)
        {
            if (rf.RenderMaterialId == UUID.Zero) continue;
            lock (_pbrCacheLock)
            {
                if (_pbrCache.ContainsKey(rf.RenderMaterialId)) continue;
            }
            needed.Add(rf.RenderMaterialId);
        }

        if (needed.Count > 0)
        {
            var tasks = needed.Select(id => Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                var tcs = new TaskCompletionSource<AssetMaterial?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using var reg = linked.Token.Register(() => tcs.TrySetResult(null));

                client.Assets.RequestAsset(id, AssetType.Material, true, (transfer, asset) =>
                {
                    if (transfer.Success && asset is AssetMaterial am)
                    {
                        am.Decode();
                        tcs.TrySetResult(am);
                    }
                    else
                        tcs.TrySetResult(null);
                });

                var result = await tcs.Task;
                lock (_pbrCacheLock) _pbrCache[id] = result;
            }, ct)).ToList();

            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch
            {
                lock (_pbrCacheLock)
                {
                    foreach (var id in needed) _pbrCache.TryAdd(id, null);
                }
            }
        }

        // Build result dict from cache.
        lock (_pbrCacheLock)
        {
            var result = new Dictionary<UUID, AssetMaterial>();
            foreach (var rf in rawFaces)
            {
                if (rf.RenderMaterialId != UUID.Zero
                    && _pbrCache.TryGetValue(rf.RenderMaterialId, out var m) && m != null
                    && !result.ContainsKey(rf.RenderMaterialId))
                    result[rf.RenderMaterialId] = m;
            }
            return result.Count > 0 ? result : null;
        }
    }

    // ── Shared prim helpers (continued) ──────────────────────────────────────────

    /// <summary>Tessellates a single prim into a <see cref="FacetedMesh"/>.</summary>
    private async Task<FacetedMesh?> GetPrimMeshAsync(Primitive prim, CancellationToken ct,
        DetailLevel detailLevel = DetailLevel.High)
    {
        if (prim.Sculpt != null && prim.Sculpt.SculptTexture != UUID.Zero)
        {
            if (prim.Sculpt.Type != SculptType.Mesh)
            {
                var sculptBmp = await GridTextureHelper.DownloadSkBitmapAsync(
                    client, prim.Sculpt.SculptTexture, ct: ct).ConfigureAwait(false);
                if (sculptBmp != null)
                {
                    using (sculptBmp)
                        return _mesher.GenerateFacetedSculptMesh(prim, sculptBmp, detailLevel);
                }
                // Fallback: parametric if sculpt texture unavailable.
                return _mesher.GenerateFacetedMesh(prim, detailLevel);
            }
            else
            {
                return await DownloadMeshAsync(prim, ct).ConfigureAwait(false);
            }
        }
        return _mesher.GenerateFacetedMesh(prim, detailLevel);
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
        ref TkVector3 bMax,
        TkMatrix4?    faceTransformOverride = null)
    {
        var faceTransform = faceTransformOverride ?? transform;
        for (int fi = 0; fi < mesh.Faces.Count; fi++)
        {
            var face = mesh.Faces[fi];
            if (face.Vertices.Count == 0) continue;

            var texFace = prim.Textures?.GetFace((uint)fi);
            if (texFace != null)
                _mesher.TransformTexCoords(face.Vertices, face.Center, texFace, prim.Scale);

            // Pack into interleaved float array: position(3) + normal(3) + uv(2) = 8 floats.
            // Rent from the shared pool to avoid LOH pressure; return after faces.Add.
            int needed = face.Vertices.Count * 8;
            float[] verts = ArrayPool<float>.Shared.Rent(needed);
            var centroidSum = TkVector3.Zero;
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
                centroidSum += wp;
            }
            var centroid = face.Vertices.Count > 0
                ? centroidSum * (1f / face.Vertices.Count)
                : TkVector3.Zero;

            ushort[] indices = face.Indices.ToArray();

            float         r          = 1f, g = 1f, b = 1f, a = 1f;
            bool          fullbright = false;
            float         glow       = 0f;
            bool          hasAlpha   = false;
            UUID          texId      = UUID.Zero;
            float         shiny      = 0f;
            bool          hasBump    = false;
            FaceAlphaMode alphaMode  = FaceAlphaMode.None;
            UUID          materialId = UUID.Zero;
            UUID          renderMaterialId = UUID.Zero;

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
                materialId = texFace.MaterialID;
                renderMaterialId = texFace.RenderMaterialID;

                shiny = texFace.Shiny switch
                {
                    Shininess.Low    => 0.24f,
                    Shininess.Medium => 0.64f,
                    Shininess.High   => 0.96f,
                    _                => 0f,
                };
                hasBump   = texFace.Bump != Bumpiness.None;
                alphaMode = hasAlpha ? FaceAlphaMode.Blend : FaceAlphaMode.None;
            }

            // Skip fully-invisible faces — alpha at or below this threshold means
            // the face will never contribute visible pixels (mirrors SL viewer logic).
            if (a <= 0.01f) continue;

            faces.Add(new RawFace(verts, needed, indices,
                new TkVector4(r, g, b, a), fullbright, glow, hasAlpha, texId, faceTransform,
                prim.LocalID, fi, centroid,
                Shiny: shiny, HasBump: hasBump, AlphaMode: alphaMode,
                MaterialId: materialId, RenderMaterialId: renderMaterialId));
        }
    }

    // ── Attachment tessellation ───────────────────────────────────────────────────

    /// <summary>
    /// Rigged / fitted mesh skinning data for a single attachment face.
    /// When non-null, the caller must render the face in avatar-local space
    /// (its <see cref="PrimRenderFace.Transform"/> is already Identity and its
    /// vertices are in mesh bind-space) and drive animation through the supplied
    /// joint names + inverse bind matrices + per-vertex weights.
    /// </summary>
    internal sealed class AttachmentRiggedSkin
    {
        public string[]    JointNames      = [];
        public TkMatrix4[] InvBindMatrices = [];
        /// <summary>
        /// Interleaved per-vertex joint indices: <c>Joints[vi * 4 + k]</c> is influence k of vertex vi.
        /// </summary>
        public int[]       Joints  = [];
        /// <summary>
        /// Interleaved per-vertex weights: <c>Weights[vi * 4 + k]</c> is weight k of vertex vi.
        /// </summary>
        public float[]     Weights = [];
    }

    /// <summary>
    /// Tessellates an attachment linkset and downloads its textures.
    /// <paramref name="attachJointMatrix"/> is the precomputed world matrix for the
    /// attachment joint (bone world matrix combined with the attachment-point default
    /// offset and rotation from <c>avatar_lad.xml</c>).
    /// </summary>
    /// <returns>
    /// Tuple with the built faces, a parallel list of per-face rigged skin data
    /// (null entries for non-rigged rigid faces), and the world-space AABB.
    /// </returns>
    internal async Task<(List<PrimRenderFace> faces, List<AttachmentRiggedSkin?> riggedSkins, TkVector3 bMin, TkVector3 bMax)>
        BuildAttachmentFacesAsync(
            IReadOnlyList<Primitive> prims,
            TkMatrix4                attachJointMatrix,
            IProgress<string>?       progress,
            CancellationToken        ct,
            Func<UUID, UUID>?        bakedTexResolver    = null,
            uint                     rootLocalId         = 0,
            IProgress<SceneTexturePatch>? texturePatch   = null)
    {
        var (rawFaces, riggedSkins, bMin, bMax) = await TessellateAttachmentAsync(
            prims, attachJointMatrix, progress, ct, bakedTexResolver).ConfigureAwait(false);

        if (rawFaces.Count == 0)
            return ([], [], bMin, bMax);

        var materials    = await FetchMaterialsAsync(rawFaces, ct).ConfigureAwait(false);
        var pbrMaterials = await FetchPBRMaterialsAsync(rawFaces, ct).ConfigureAwait(false);
        var faces        = BuildFacesWithoutTextures(rawFaces, materials, pbrMaterials);

        int texCount = CountUniqueTextures(rawFaces, materials, pbrMaterials);
        if (texCount > 0 && texturePatch != null)
        {
            progress?.Report($"Loading attachment textures (0 / {texCount})…");
            _ = StreamTexturesAsync(rawFaces, faces, rootLocalId, texCount, progress,
                                    texturePatch, materials, pbrMaterials, ct,
                                    texturePriority: AttachmentTexturePriority);
        }

        return (faces, riggedSkins, bMin, bMax);
    }

    /// <summary>
    /// Tessellates each prim in <paramref name="prims"/> into <see cref="RawFace"/>
    /// entries positioned in avatar-local space via <paramref name="attachJointMatrix"/>.
    /// <paramref name="prims"/>[0] must be the root prim of the attachment linkset.
    /// </summary>
    /// <remarks>
    /// Rigged/fitted mesh faces (those with per-vertex skin weights in the mesh asset)
    /// are emitted with <see cref="RawFace.Transform"/> = identity and vertices already
    /// multiplied by the mesh's BindShapeMatrix.  Their prim transform and the
    /// attachment joint matrix are ignored — placement is driven entirely by the
    /// avatar skeleton via the returned <see cref="AttachmentRiggedSkin"/>.
    /// </remarks>
    private async Task<(List<RawFace> rawFaces, List<AttachmentRiggedSkin?> riggedSkins, TkVector3 bMin, TkVector3 bMax)>
        TessellateAttachmentAsync(
            IReadOnlyList<Primitive> prims,
            TkMatrix4                attachJointMatrix,
            IProgress<string>?       progress,
            CancellationToken        ct,
            Func<UUID, UUID>?        bakedTexResolver = null)
    {
        var rawFaces    = new List<RawFace>();
        var riggedSkins = new List<AttachmentRiggedSkin?>();
        var bMin        = new TkVector3(float.MaxValue);
        var bMax        = new TkVector3(float.MinValue);

        if (prims.Count == 0)
            return (rawFaces, riggedSkins, bMin, bMax);

        var root      = prims[0];
        var rootScale = new TkVector3(root.Scale.X,    root.Scale.Y,    root.Scale.Z);
        var rootRot   = ToTkQuaternion(root.Rotation);
        var rootPos   = new TkVector3(root.Position.X, root.Position.Y, root.Position.Z);

        // Root prim: scale → rotate → translate by user offset → attachment joint transform.
        var rootWorldMatrix = TkMatrix4.CreateScale(rootScale)
                            * TkMatrix4.CreateFromQuaternion(rootRot)
                            * TkMatrix4.CreateTranslation(rootPos)
                            * attachJointMatrix;

        // Child prims are positioned in root-orientation space; root scale is NOT inherited
        // (SL linkset semantics: child positions are in metres relative to root centre).
        var linkSpaceToWorld = TkMatrix4.CreateFromQuaternion(rootRot)
                             * TkMatrix4.CreateTranslation(rootPos)
                             * attachJointMatrix;

        for (int pi = 0; pi < prims.Count; pi++)
        {
            ct.ThrowIfCancellationRequested();
            var prim = prims[pi];

            var mesh = await GetPrimMeshAsync(prim, ct, DetailLevel.High).ConfigureAwait(false);
            if (mesh == null) continue;

            TkMatrix4 transform;
            if (pi == 0)
            {
                transform = rootWorldMatrix;
            }
            else
            {
                var childScale = new TkVector3(prim.Scale.X,    prim.Scale.Y,    prim.Scale.Z);
                var childRot   = ToTkQuaternion(prim.Rotation);
                var childPos   = new TkVector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
                transform = TkMatrix4.CreateScale(childScale)
                          * TkMatrix4.CreateFromQuaternion(childRot)
                          * TkMatrix4.CreateTranslation(childPos)
                          * linkSpaceToWorld;
            }

            AppendAttachmentFaces(mesh, prim, transform, rawFaces, riggedSkins,
                                  ref bMin, ref bMax, bakedTexResolver);
        }

        if (rawFaces.Count == 0 || bMin.X >= float.MaxValue)
        {
            bMin = new TkVector3(-0.5f);
            bMax = new TkVector3( 0.5f);
        }

        return (rawFaces, riggedSkins, bMin, bMax);
    }

    /// <summary>
    /// Attachment-specific face packing.  Delegates to <see cref="AppendFaces"/> for
    /// rigid faces; for rigged faces (those carrying per-vertex skin weights) it
    /// bypasses the prim transform, applies only the mesh's <c>BindShapeMatrix</c>,
    /// and emits a parallel <see cref="AttachmentRiggedSkin"/> entry.
    /// </summary>
    private void AppendAttachmentFaces(
        FacetedMesh                    mesh,
        Primitive                      prim,
        TkMatrix4                      transform,
        List<RawFace>                  faces,
        List<AttachmentRiggedSkin?>    riggedSkins,
        ref TkVector3                  bMin,
        ref TkVector3                  bMax,
        Func<UUID, UUID>?              bakedTexResolver = null)
    {
        // Non-rigged meshes: reuse the shared rigid path.
        if (mesh.SkinData == null || mesh.SkinData.JointNames.Length == 0)
        {
            int before = faces.Count;
            AppendFaces(mesh, prim, transform, faces, ref bMin, ref bMax);
            for (int i = before; i < faces.Count; i++) riggedSkins.Add(null);
            return;
        }

        // Pre-build per-joint InvBindMatrices once per submission — shared across all
        // faces of this mesh.  The skinning formula we use is
        //   v_anim = v_bind × invBind × animBone
        // where v_bind = BindShapeMatrix × raw_vertex (so BindShapeMatrix is baked into
        // the vertex, not into invBind).
        var jointInvBind = BuildInvBindMatrices(mesh.SkinData);

        // Use the stored BindShapeMatrix from the mesh asset.  The IBM was baked by the
        // exporter as Invert(bone_world × Scale(s)), so IBM.Row0.Length = 1/s = 39.37.
        // The stored BSM encodes the mesh's PositionDomain-to-world-space mapping:
        // its scale converts from the normalised [-0.5, 0.5] decoded vertex range to the
        // per-axis prim dimensions, and its translation carries the mesh centre offset so
        // that v_bind × IBM × bone_world places vertices at the correct avatar-space Z.
        // Using a uniform Scale(0.0254) instead would discard the translation and give
        // v_anim ≈ v_raw (dress rendered at origin, not on the avatar).
        var bindShape = FloatsToMatrix(mesh.SkinData.BindShapeMatrix);

        for (int fi = 0; fi < mesh.Faces.Count; fi++)
        {
            var face = mesh.Faces[fi];
            if (face.Vertices.Count == 0) continue;

            var texFace = prim.Textures?.GetFace((uint)fi);
            if (texFace != null)
                _mesher.TransformTexCoords(face.Vertices, face.Center, texFace, prim.Scale);

            // Face has no per-vertex weights: treat as rigid (uses prim transform).
            if (face.Weights == null || face.Weights.Count == 0)
            {
                int before = faces.Count;
                AppendSingleFace(mesh, fi, prim, transform, faces, ref bMin, ref bMax);
                for (int i = before; i < faces.Count; i++) riggedSkins.Add(null);
                continue;
            }

            // Rigged face: bake BindShapeMatrix into the vertex so v_bind is in the
            // mesh's bind-space (matches the SL viewer's applyBindShape step).
            int      nv      = face.Vertices.Count;
            int      nv8     = nv * 8;
            float[]  verts   = ArrayPool<float>.Shared.Rent(nv8);
            var      centSum = TkVector3.Zero;
            int      nv4     = nv * 4;
            int[]    joints  = new int  [nv4];
            float[]  weights = new float[nv4];

            for (int vi = 0; vi < nv; vi++)
            {
                var v = face.Vertices[vi];
                var bp = TkVector4.TransformRow(
                    new TkVector4(v.Position.X, v.Position.Y, v.Position.Z, 1f), bindShape);
                var bn = TkVector4.TransformRow(
                    new TkVector4(v.Normal.X, v.Normal.Y, v.Normal.Z, 0f), bindShape);

                int o = vi * 8;
                verts[o    ] = bp.X; verts[o + 1] = bp.Y; verts[o + 2] = bp.Z;
                verts[o + 3] = bn.X; verts[o + 4] = bn.Y; verts[o + 5] = bn.Z;
                verts[o + 6] = v.TexCoord.X;
                verts[o + 7] = v.TexCoord.Y;

                // Bind-space AABB (rigged faces don't use a prim transform, so the bind-space
                // position IS the avatar-local resting position — good enough for framing).
                var wp = new TkVector3(bp.X, bp.Y, bp.Z);
                bMin = TkVector3.ComponentMin(bMin, wp);
                bMax = TkVector3.ComponentMax(bMax, wp);
                centSum += wp;

                var vw = vi < face.Weights.Count ? face.Weights[vi] : default;
                int si = vi * 4;
                joints[si]     = vw.Joint0;  weights[si]     = vw.Weight0;
                joints[si + 1] = vw.Joint1;  weights[si + 1] = vw.Weight1;
                joints[si + 2] = vw.Joint2;  weights[si + 2] = vw.Weight2;
                joints[si + 3] = vw.Joint3;  weights[si + 3] = vw.Weight3;

                NormalizeSkinWeights(mesh.SkinData.JointNames.Length,
                    ref joints[si],     ref weights[si],
                    ref joints[si + 1], ref weights[si + 1],
                    ref joints[si + 2], ref weights[si + 2],
                    ref joints[si + 3], ref weights[si + 3]);
            }
            var centroid = centSum * (1f / nv);
            ushort[] indices = face.Indices.ToArray();

            // Extract TE colour / material / alpha — same logic as AppendFaces.
            float r = 1f, g = 1f, b = 1f, a = 1f;
            bool  fullbright = false;
            float glow       = 0f;
            bool  hasAlpha   = false;
            UUID  texId      = UUID.Zero;
            float shiny      = 0f;
            bool  hasBump    = false;
            FaceAlphaMode alphaMode = FaceAlphaMode.None;
            UUID  materialId = UUID.Zero;
            UUID  renderMaterialId = UUID.Zero;
            if (texFace != null)
            {
                r = texFace.RGBA.R; g = texFace.RGBA.G; b = texFace.RGBA.B; a = texFace.RGBA.A;
                fullbright = texFace.Fullbright;
                glow       = texFace.Glow;
                hasAlpha   = a < 0.99f;
                texId      = texFace.TextureID;
                if (bakedTexResolver != null) texId = bakedTexResolver(texId);
                materialId = texFace.MaterialID;
                renderMaterialId = texFace.RenderMaterialID;
                shiny = texFace.Shiny switch
                {
                    Shininess.Low    => 0.24f,
                    Shininess.Medium => 0.64f,
                    Shininess.High   => 0.96f,
                    _                => 0f,
                };
                hasBump   = texFace.Bump != Bumpiness.None;
                alphaMode = hasAlpha ? FaceAlphaMode.Blend : FaceAlphaMode.None;
            }
            if (a <= 0.01f) continue;

            faces.Add(new RawFace(verts, nv8, indices,
                new TkVector4(r, g, b, a), fullbright, glow, hasAlpha, texId,
                TkMatrix4.Identity,          // rigged faces: drawn in avatar-local bind space
                prim.LocalID, fi, centroid,
                Shiny: shiny, HasBump: hasBump, AlphaMode: alphaMode,
                MaterialId: materialId, RenderMaterialId: renderMaterialId));
            riggedSkins.Add(new AttachmentRiggedSkin
            {
                JointNames      = mesh.SkinData.JointNames,
                InvBindMatrices = jointInvBind,
                Joints          = joints,
                Weights         = weights,
            });
        }
    }

    /// <summary>
    /// Packs a single face (by index) through the same pipeline as <see cref="AppendFaces"/>.
    /// Used when a rigged mesh contains mixed rigid + rigged faces.
    /// </summary>
    private void AppendSingleFace(
        FacetedMesh   mesh,
        int           faceIndex,
        Primitive     prim,
        TkMatrix4     transform,
        List<RawFace> faces,
        ref TkVector3 bMin,
        ref TkVector3 bMax)
    {
        // Build a temporary single-face FacetedMesh view and forward.  This keeps all of
        // the TE/material extraction in AppendFaces authoritative without duplicating it.
        var singleFaceMesh = new FacetedMesh
        {
            Prim    = mesh.Prim,
            Path    = mesh.Path,
            Profile = mesh.Profile,
            Faces   = new List<Face> { mesh.Faces[faceIndex] },
        };
        int before = faces.Count;
        AppendFaces(singleFaceMesh, prim, transform, faces, ref bMin, ref bMax);
        // Patch the face index so downstream renders refer to the correct face on the prim.
        for (int i = before; i < faces.Count; i++)
        {
            faces[i] = faces[i] with { FaceIndex = faceIndex };
        }
    }

    /// <summary>Converts a 16-element row-major float array to an OpenTK Matrix4.</summary>
    private static TkMatrix4 FloatsToMatrix(float[] f)
    {
        if (f == null || f.Length < 16) return TkMatrix4.Identity;
        return new TkMatrix4(
            f[ 0], f[ 1], f[ 2], f[ 3],
            f[ 4], f[ 5], f[ 6], f[ 7],
            f[ 8], f[ 9], f[10], f[11],
            f[12], f[13], f[14], f[15]);
    }

    /// <summary>
    /// Builds the per-joint inverse bind matrix array used for rigged skinning.
    /// The SL mesh format supplies one 4×4 (16 floats, row-major, row-vector) per joint.
    /// When the asset contains <see cref="MeshSkinData.AltInverseBindMatrices"/> (i.e. the
    /// mesh was uploaded with joint position overrides), those are preferred over the regular
    /// <see cref="MeshSkinData.InverseBindMatrices"/>.  This matches the SL viewer branch:
    /// <c>use_alt_ibm = skin.mJointOverrides.size() &gt; 0</c>.
    /// </summary>
    private static TkMatrix4[] BuildInvBindMatrices(MeshSkinData skin)
    {
        int n = skin.JointNames.Length;
        var result = new TkMatrix4[n];

        // Prefer alt IBMs when present — they account for custom joint positions baked
        // into the mesh by the uploader (e.g. a dress whose skeleton was exported at a
        // non-standard joint position).  Using the regular IBMs for such a mesh will
        // produce explosive distortion because the IBM is in the wrong bind-pose space.
        bool useAlt = skin.AltInverseBindMatrices.Length >= n * 16;
        var  raw    = useAlt ? skin.AltInverseBindMatrices : skin.InverseBindMatrices;
        int  have   = raw.Length / 16;

        for (int i = 0; i < n; i++)
        {
            if (i < have)
            {
                int b = i * 16;
                result[i] = new TkMatrix4(
                    raw[b    ], raw[b + 1], raw[b + 2], raw[b + 3],
                    raw[b + 4], raw[b + 5], raw[b + 6], raw[b + 7],
                    raw[b + 8], raw[b + 9], raw[b +10], raw[b +11],
                    raw[b +12], raw[b +13], raw[b +14], raw[b +15]);
            }
            else
            {
                result[i] = TkMatrix4.Identity;
            }
        }

        return result;
    }

    private static void NormalizeSkinWeights(
        int jointCount,
        ref int j0, ref float w0,
        ref int j1, ref float w1,
        ref int j2, ref float w2,
        ref int j3, ref float w3)
    {
        if ((uint)j0 >= (uint)jointCount) w0 = 0f;
        if ((uint)j1 >= (uint)jointCount) w1 = 0f;
        if ((uint)j2 >= (uint)jointCount) w2 = 0f;
        if ((uint)j3 >= (uint)jointCount) w3 = 0f;

        w0 = Math.Clamp(w0, 0f, 1f);
        w1 = Math.Clamp(w1, 0f, 1f);
        w2 = Math.Clamp(w2, 0f, 1f);
        w3 = Math.Clamp(w3, 0f, 1f);

        float sum = w0 + w1 + w2 + w3;
        if (sum > 1e-6f)
        {
            float inv = 1f / sum;
            w0 *= inv; w1 *= inv; w2 *= inv; w3 *= inv;
            return;
        }

        j0 = 0; j1 = 0; j2 = 0; j3 = 0;
        w0 = jointCount > 0 ? 1f : 0f;
        w1 = 0f; w2 = 0f; w3 = 0f;
    }

    // ── Utility ───────────────────────────────────────────────────────────────────

    private static TkQuaternion ToTkQuaternion(Quaternion q) =>
        new(q.X, q.Y, q.Z, q.W);
}
