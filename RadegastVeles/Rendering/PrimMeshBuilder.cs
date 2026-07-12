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
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse.Materials;
using LibreMetaverse;
using LibreMetaverse.Assets;
using LibreMetaverse.Rendering;
using Radegast.Veles.Core;
using SkiaSharp;
using Quaternion   = System.Numerics.Quaternion;
using Vector3      = System.Numerics.Vector3;
using Vector4      = System.Numerics.Vector4;

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
        Vector4       Color,
        bool          Fullbright,
        float         Glow,
        bool          HasAlpha,
        UUID          TextureId,
        Matrix4x4     Transform,
        uint          PrimLocalId,
        int           FaceIndex,
        Vector3       Centroid,
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
        bool                    isHud                  = false,
        DetailLevel             detailLevel            = DetailLevel.High,
        IProgress<SceneTexturePatch>? texturePatch     = null,
        int                     textureResolutionLevel = -1)
    {
        var (rawFaces, bMin, bMax, flexiPrims, animeshSkins) = await TessellateAsync(prims, rootLocalId, progress, ct, isHud, detailLevel)
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
            Label          = label,
            Faces          = faces.ToArray(),
            BoundsMin      = bMin,
            BoundsMax      = bMax,
            FlexiPrims     = flexiPrims.ToArray(),
            AnimeshSkinData = animeshSkins.Count > 0 ? animeshSkins.ToArray() : [],
        };

        // Stream textures asynchronously in the background — report each patch as it
        // arrives so the caller can forward it to GlViewportControl.PatchSceneObjectTexture.
        int texCount = CountUniqueTextures(rawFaces, materials, pbrMaterials);
        if (texCount > 0)
        {
            progress?.Report($"Loading textures (0 / {texCount})…");
            _ = StreamTexturesAsync(rawFaces, faces, rootLocalId, texCount, progress,
                                    texturePatch, materials, pbrMaterials, ct,
                                    textureResolutionLevel: textureResolutionLevel);
        }

        return submission;
    }

    // ── Tessellation ──────────────────────────────────────────────────────────────

    private async Task<(List<RawFace> faces, Vector3 bMin, Vector3 bMax, List<FlexiPrimInfo> flexiPrims, List<AnimeshFaceSkinData> animeshSkins)> TessellateAsync(
        IReadOnlyList<Primitive> prims,
        uint                    rootLocalId,
        IProgress<string>?      progress,
        CancellationToken       ct,
        bool                    isHud       = false,
        DetailLevel             detailLevel = DetailLevel.High)
    {
        var faces        = new List<RawFace>();
        var flexiPrims   = new List<FlexiPrimInfo>();
        var animeshSkins = new List<AnimeshFaceSkinData>();
        var bMin         = new Vector3(float.MaxValue);
        var bMax         = new Vector3(float.MinValue);

        var rootPrim = prims.FirstOrDefault(p => p.LocalID == rootLocalId) ?? prims[0];
        var rootRot = new Quaternion(rootPrim.Rotation.X, rootPrim.Rotation.Y, rootPrim.Rotation.Z, rootPrim.Rotation.W);

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
            var scale = new Vector3(prim.Scale.X, prim.Scale.Y, prim.Scale.Z);
            Matrix4x4 transform;
            if (prim.LocalID == rootLocalId)
            {
                transform = Matrix4x4.CreateScale(scale)
                          * Matrix4x4.CreateFromQuaternion(rootRot);
            }
            else
            {
                var pos = new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
                var rot = new Quaternion(prim.Rotation.X, prim.Rotation.Y, prim.Rotation.Z, prim.Rotation.W);
                transform = Matrix4x4.CreateScale(scale)
                          * Matrix4x4.CreateFromQuaternion(rot)
                          * Matrix4x4.CreateTranslation(pos)
                          * Matrix4x4.CreateFromQuaternion(rootRot);
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
                        var raw = new float[mf.Vertices.Count * 12];
                        for (int vi = 0; vi < mf.Vertices.Count; vi++)
                        {
                            var v = mf.Vertices[vi];
                            int o = vi * 12;
                            raw[o]     = v.Position.X; raw[o + 1] = v.Position.Y; raw[o + 2] = v.Position.Z;
                            raw[o + 3] = v.Normal.X;   raw[o + 4] = v.Normal.Y;   raw[o + 5] = v.Normal.Z;
                            raw[o + 6] = v.TexCoord.X; raw[o + 7] = v.TexCoord.Y;
                            // tangents 8-11 left as zero; flexi prims rarely use normal maps
                        }
                        flexiBaseVerts[bfi++] = raw;
                    }
                }
            }

            int faceStart = faces.Count;
            // Flexi faces are positioned entirely by the animator via FlexiPrimInfo.AttachTransform;
            // the GPU must NOT apply the rest-pose prim transform on top of the deformed verts.
            AppendFaces(mesh, prim, transform, faces, ref bMin, ref bMax,
                        faceTransformOverride: flexiBaseVerts != null ? Matrix4x4.Identity : (Matrix4x4?)null);

            // Extract bind-space skinning data for standalone rigged mesh prims so that
            // SceneAnimeshStreamer can drive LBS animation each tick.
            if (mesh.SkinData?.JointNames?.Length > 0 && flexiBaseVerts == null)
            {
                var bindShape = FloatsToMatrix(mesh.SkinData.BindShapeMatrix);
                for (int gi = faceStart; gi < faces.Count; gi++)
                {
                    int fi = faces[gi].FaceIndex;
                    var mf = mesh.Faces[fi];
                    if (mf.Weights == null || mf.Weights.Count == 0) continue;

                    int nv         = mf.Vertices.Count;
                    var bindVerts  = new float[nv * 12];
                    var skinJoints = new int  [nv * 4];
                    var skinWts    = new float[nv * 4];

                    for (int vi = 0; vi < nv; vi++)
                    {
                        var v  = mf.Vertices[vi];
                        var bp = Vector4.Transform(
                            new Vector4(v.Position.X, v.Position.Y, v.Position.Z, 1f), bindShape);
                        var bn = Vector4.Transform(
                            new Vector4(v.Normal.X,   v.Normal.Y,   v.Normal.Z,   0f), bindShape);
                        int o = vi * 12;
                        bindVerts[o    ] = bp.X; bindVerts[o + 1] = bp.Y; bindVerts[o + 2] = bp.Z;
                        bindVerts[o + 3] = bn.X; bindVerts[o + 4] = bn.Y; bindVerts[o + 5] = bn.Z;
                        bindVerts[o + 6] = v.TexCoord.X;
                        bindVerts[o + 7] = v.TexCoord.Y;
                        // tangents 8-11 computed after this loop

                        var vw = vi < mf.Weights.Count ? mf.Weights[vi] : default;
                        int si = vi * 4;
                        skinJoints[si]     = vw.Joint0; skinWts[si]     = vw.Weight0;
                        skinJoints[si + 1] = vw.Joint1; skinWts[si + 1] = vw.Weight1;
                        skinJoints[si + 2] = vw.Joint2; skinWts[si + 2] = vw.Weight2;
                        skinJoints[si + 3] = vw.Joint3; skinWts[si + 3] = vw.Weight3;

                        NormalizeSkinWeights(mesh.SkinData.JointNames.Length,
                            ref skinJoints[si],     ref skinWts[si],
                            ref skinJoints[si + 1], ref skinWts[si + 1],
                            ref skinJoints[si + 2], ref skinWts[si + 2],
                            ref skinJoints[si + 3], ref skinWts[si + 3]);
                    }

                    var faceIndices = mf.Indices.Select(idx => (ushort)idx).ToArray();
                    ComputeTangents(bindVerts, nv, faceIndices);

                    animeshSkins.Add(new AnimeshFaceSkinData
                    {
                        FaceIndex = gi,
                        BindVerts = bindVerts,
                        SkinData  = mesh.SkinData,
                        Joints    = skinJoints,
                        Weights   = skinWts,
                    });
                }
            }

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
                        Scale              = prim.Scale,
                        AttachTransform    = transform,
                    });
                }
            }
        }

        if (faces.Count == 0 || bMin.X == float.MaxValue)
        {
            bMin = new Vector3(-0.5f);
            bMax = new Vector3( 0.5f);
        }

        return (faces, bMin, bMax, flexiPrims, animeshSkins);
    }

    private async Task<FacetedMesh?> DownloadMeshAsync(Primitive prim, CancellationToken ct)
    {
        var meshAsset = await client.Assets.RequestMeshAsync(prim.Sculpt!.SculptTexture, ct).ConfigureAwait(false);
        if (meshAsset == null) return null;
        return _mesher.GenerateFacetedMeshMesh(prim, meshAsset.AssetData);
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
                var baseColorFactor = new Vector4(
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
                    EmissiveFactor           = new Vector3(
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
                // True only when the alpha mode came from the face colour alone (no explicit
                // material/force-opaque override). Such faces are eligible to be upgraded to
                // the alpha pass once their albedo texture is decoded and found to be transparent.
                bool legacyAlphaAuto = false;
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
                    legacyAlphaAuto = true;
                }

                float legacyAlphaCutoff = rf.AlphaCutoff;
                if (mat != null && legacyAlphaMode == FaceAlphaMode.Mask)
                    legacyAlphaCutoff = mat.AlphaMaskCutoff / 255f;

                var normalUv  = UvTransform.Default;
                var specUv    = UvTransform.Default;
                var specColor = Vector4.One;
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
                    specColor = new Vector4(
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
                    AlphaAuto            = legacyAlphaAuto,
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
        float                             texturePriority        = 101300f,
        int                               textureResolutionLevel = -1)
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

                // Progressive decode callback: report a fast low-res preview before the full
                // bitmap arrives.  Only sent for full-quality builds (textureResolutionLevel == -1)
                // because the final result for LOD builds is already low-res — a preview-of-a-preview
                // adds no visible benefit and doubles the patch traffic.
                IProgress<SKBitmap>? progressiveDecode = (texturePatch != null && slots.Count > 0
                                                          && textureResolutionLevel == -1)
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
                              priority: texturePriority, resolutionLevel: textureResolutionLevel)
                                                  .ConfigureAwait(false);

                int n = Interlocked.Increment(ref loaded);
                progress?.Report($"Loading textures ({n} / {total})…");

                if (bmp == null) return;

                // Decide once, here on the background thread, whether this texture is actually
                // transparent — but only if some face uses it as its albedo. The viewport uses
                // this to move opaque-tinted legacy faces with a transparent texture into the
                // alpha pass (without it they render fully opaque). Scanning here keeps the
                // per-pixel work off the GL render thread.
                bool texHasAlpha = false;
                for (int si = 0; si < slots.Count; si++)
                    if (slots[si].Slot == TextureSlot.Albedo) { texHasAlpha = BitmapHasTransparency(bmp); break; }

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
                                texturePatch.Report(new SceneTexturePatch(primLocalId, faceIndex, slot, delivery)
                                {
                                    TextureHasAlpha    = texHasAlpha,
                                    ResolutionLevel    = textureResolutionLevel,
                                });
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

    /// <summary>
    /// Returns true if <paramref name="bmp"/> has a meaningfully non-opaque alpha channel.
    /// Alpha occupies byte 3 of every pixel in both RGBA8888 and BGRA8888 — the only 32-bit
    /// layouts CoreJ2K emits — so a single strided scan works for either. A threshold below
    /// 255 ignores the stray near-opaque pixels that JPEG2000 ringing can leave on otherwise
    /// solid textures, which would otherwise push opaque faces into the alpha pass needlessly.
    /// </summary>
    private static bool BitmapHasTransparency(SKBitmap bmp)
    {
        // 2-component J2K → SKColorType.Rg88: R = luminance, G = alpha.
        // Skia forces AlphaType = Opaque for no-alpha color types, so the normal
        // early-out below fires even though the G channel carries real alpha data.
        if (bmp.ColorType == SKColorType.Rg88)
        {
            var px = bmp.GetPixelSpan();
            for (int i = 1; i < px.Length; i += 2) // G byte = alpha
                if (px[i] < 250) return true;
            return false;
        }

        // An opaque alpha type cannot carry transparency — cheap early-out, no scan.
        if (bmp.AlphaType == SKAlphaType.Opaque) return false;
        if (bmp.BytesPerPixel != 4) return false; // unexpected layout: be conservative (treat as opaque)

        var pixels = bmp.GetPixelSpan();
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] < 250) return true;
        return false;
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
            var fetched = await client.Objects.RequestMaterialsAsync(sim, needed, linked.Token)
                                              .ConfigureAwait(false);

            var byId = new Dictionary<UUID, LegacyMaterial>();
            foreach (var mat in fetched)
                byId[mat.ID] = mat;

            lock (_materialCacheLock)
            {
                // Same pattern as PBR cache: never overwrite a successful fetch with null.
                // Two concurrent builds fetching the same material UUID could otherwise
                // have the failed fetch poison the cache entry written by the successful one.
                foreach (var id in needed)
                {
                    if (byId.TryGetValue(id, out var m))
                        _materialCache[id] = m;
                    else
                        _materialCache.TryAdd(id, null);
                }
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

                var result = await client.Assets.RequestAssetAsync(id, AssetType.Material, true, linked.Token)
                    .ConfigureAwait(false) as AssetMaterial;
                result?.Decode();
                lock (_pbrCacheLock)
                {
                    // Never overwrite a successful fetch with a failure: two concurrent
                    // builds for different objects may both need the same material UUID.
                    // If one fetch times out after the other succeeds, the null result
                    // must not poison the cache entry and block future PBR texture loads.
                    if (result != null)
                        _pbrCache[id] = result;
                    else
                        _pbrCache.TryAdd(id, null);
                }
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
                        return _mesher.GenerateFacetedSculptMesh(prim, LibreMetaverse.Imaging.Skia.SkiaTextureCodec.ToManagedImage(sculptBmp), detailLevel);
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
        Primitive   prim,
        Matrix4x4   transform,
        List<RawFace> faces,
        ref Vector3 bMin,
        ref Vector3 bMax,
        Matrix4x4?  faceTransformOverride = null)
    {
        var faceTransform = faceTransformOverride ?? transform;
        for (int fi = 0; fi < mesh.Faces.Count; fi++)
        {
            var face = mesh.Faces[fi];
            if (face.Vertices.Count == 0) continue;

            var texFace = prim.Textures?.GetFace((uint)fi);
            if (texFace != null)
                _mesher.TransformTexCoords(face.Vertices, face.Center, texFace, prim.Scale);

            // Pack into interleaved float array: position(3) + normal(3) + uv(2) + tangent(4) = 12 floats.
            // Rent from the shared pool to avoid LOH pressure; return after faces.Add.
            int needed = face.Vertices.Count * 12;
            float[] verts = ArrayPool<float>.Shared.Rent(needed);
            var centroidSum = Vector3.Zero;
            for (int vi = 0; vi < face.Vertices.Count; vi++)
            {
                var v = face.Vertices[vi];
                int o = vi * 12;
                verts[o + 0]  = v.Position.X;
                verts[o + 1]  = v.Position.Y;
                verts[o + 2]  = v.Position.Z;
                verts[o + 3]  = v.Normal.X;
                verts[o + 4]  = v.Normal.Y;
                verts[o + 5]  = v.Normal.Z;
                verts[o + 6]  = v.TexCoord.X;
                verts[o + 7]  = v.TexCoord.Y;
                verts[o + 8]  = 0f; // tangent — filled in by ComputeTangents below
                verts[o + 9]  = 0f;
                verts[o + 10] = 0f;
                verts[o + 11] = 0f;

                // Accumulate world-space AABB for camera framing.
                var wp = Vector3.Transform(
                    new Vector3(v.Position.X, v.Position.Y, v.Position.Z), transform);
                bMin = Vector3.Min(bMin, wp);
                bMax = Vector3.Max(bMax, wp);
                centroidSum += wp;
            }
            var centroid = face.Vertices.Count > 0
                ? centroidSum * (1f / face.Vertices.Count)
                : Vector3.Zero;

            ushort[] indices = face.Indices.ToArray();
            ComputeTangents(verts, face.Vertices.Count, indices);

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
                new Vector4(r, g, b, a), fullbright, glow, hasAlpha, texId, faceTransform,
                prim.LocalID, fi, centroid,
                Shiny: shiny, HasBump: hasBump, AlphaMode: alphaMode,
                MaterialId: materialId, RenderMaterialId: renderMaterialId));
        }
    }

    /// <summary>
    /// Computes per-vertex tangents (vec4: xyz = tangent, w = handedness ±1) and writes
    /// them into slots 8-11 of the interleaved vertex buffer (stride 12 floats per vertex).
    /// Uses the UV-based Lengyel method so tangents are consistent with the surface's UV mapping,
    /// including mirrored UV islands where screen-space derivatives give the wrong handedness.
    /// </summary>
    internal static void ComputeTangents(float[] verts, int vertCount, ushort[] indices)
    {
        var tanAccum = new Vector3[vertCount];
        var bitAccum = new Vector3[vertCount];

        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            int o0 = i0 * 12, o1 = i1 * 12, o2 = i2 * 12;

            var p0 = new Vector3(verts[o0], verts[o0 + 1], verts[o0 + 2]);
            var p1 = new Vector3(verts[o1], verts[o1 + 1], verts[o1 + 2]);
            var p2 = new Vector3(verts[o2], verts[o2 + 1], verts[o2 + 2]);

            float du1 = verts[o1 + 6] - verts[o0 + 6];
            float dv1 = verts[o1 + 7] - verts[o0 + 7];
            float du2 = verts[o2 + 6] - verts[o0 + 6];
            float dv2 = verts[o2 + 7] - verts[o0 + 7];

            float r = du1 * dv2 - du2 * dv1;
            if (MathF.Abs(r) < 1e-8f) continue;
            float inv = 1f / r;

            var e1  = p1 - p0;
            var e2  = p2 - p0;
            var tan = (e1 * dv2 - e2 * dv1) * inv;
            var bit = (e2 * du1 - e1 * du2) * inv;

            tanAccum[i0] += tan; tanAccum[i1] += tan; tanAccum[i2] += tan;
            bitAccum[i0] += bit; bitAccum[i1] += bit; bitAccum[i2] += bit;
        }

        for (int vi = 0; vi < vertCount; vi++)
        {
            int o = vi * 12;
            var n = new Vector3(verts[o + 3], verts[o + 4], verts[o + 5]);
            var t = tanAccum[vi];
            var b = bitAccum[vi];

            var tOrth = t - n * Vector3.Dot(n, t);
            float len  = tOrth.Length();
            if (len < 1e-6f) continue; // degenerate — leave as zero (fallback to screen-space)
            tOrth /= len;

            float w = MathF.Sign(Vector3.Dot(Vector3.Cross(n, tOrth), b));
            if (w == 0f) w = 1f;

            verts[o + 8]  = tOrth.X;
            verts[o + 9]  = tOrth.Y;
            verts[o + 10] = tOrth.Z;
            verts[o + 11] = w;
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
        public Matrix4x4[] InvBindMatrices = [];
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
    internal async Task<(List<PrimRenderFace> faces, List<AttachmentRiggedSkin?> riggedSkins, Vector3 bMin, Vector3 bMax)>
        BuildAttachmentFacesAsync(
            IReadOnlyList<Primitive> prims,
            Matrix4x4                attachJointMatrix,
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
    private async Task<(List<RawFace> rawFaces, List<AttachmentRiggedSkin?> riggedSkins, Vector3 bMin, Vector3 bMax)>
        TessellateAttachmentAsync(
            IReadOnlyList<Primitive> prims,
            Matrix4x4                attachJointMatrix,
            IProgress<string>?       progress,
            CancellationToken        ct,
            Func<UUID, UUID>?        bakedTexResolver = null)
    {
        var rawFaces    = new List<RawFace>();
        var riggedSkins = new List<AttachmentRiggedSkin?>();
        var bMin        = new Vector3(float.MaxValue);
        var bMax        = new Vector3(float.MinValue);

        if (prims.Count == 0)
            return (rawFaces, riggedSkins, bMin, bMax);

        var root      = prims[0];
        var rootScale = new Vector3(root.Scale.X,    root.Scale.Y,    root.Scale.Z);
        var rootRot   = new Quaternion(root.Rotation.X, root.Rotation.Y, root.Rotation.Z, root.Rotation.W);
        var rootPos   = new Vector3(root.Position.X, root.Position.Y, root.Position.Z);

        // Root prim: scale → rotate → translate by user offset → attachment joint transform.
        var rootWorldMatrix = Matrix4x4.CreateScale(rootScale)
                            * Matrix4x4.CreateFromQuaternion(rootRot)
                            * Matrix4x4.CreateTranslation(rootPos)
                            * attachJointMatrix;

        // Child prims are positioned in root-orientation space; root scale is NOT inherited
        // (SL linkset semantics: child positions are in metres relative to root centre).
        var linkSpaceToWorld = Matrix4x4.CreateFromQuaternion(rootRot)
                             * Matrix4x4.CreateTranslation(rootPos)
                             * attachJointMatrix;

        for (int pi = 0; pi < prims.Count; pi++)
        {
            ct.ThrowIfCancellationRequested();
            var prim = prims[pi];

            var mesh = await GetPrimMeshAsync(prim, ct, DetailLevel.High).ConfigureAwait(false);
            if (mesh == null) continue;

            Matrix4x4 transform;
            if (pi == 0)
            {
                transform = rootWorldMatrix;
            }
            else
            {
                var childScale = new Vector3(prim.Scale.X,    prim.Scale.Y,    prim.Scale.Z);
                var childRot   = new Quaternion(prim.Rotation.X, prim.Rotation.Y, prim.Rotation.Z, prim.Rotation.W);
                var childPos   = new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
                transform = Matrix4x4.CreateScale(childScale)
                          * Matrix4x4.CreateFromQuaternion(childRot)
                          * Matrix4x4.CreateTranslation(childPos)
                          * linkSpaceToWorld;
            }

            AppendAttachmentFaces(mesh, prim, transform, rawFaces, riggedSkins,
                                  ref bMin, ref bMax, bakedTexResolver);
        }

        if (rawFaces.Count == 0 || bMin.X >= float.MaxValue)
        {
            bMin = new Vector3(-0.5f);
            bMax = new Vector3( 0.5f);
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
        Matrix4x4                      transform,
        List<RawFace>                  faces,
        List<AttachmentRiggedSkin?>    riggedSkins,
        ref Vector3                    bMin,
        ref Vector3                    bMax,
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
            int      nv8     = nv * 12;
            float[]  verts   = ArrayPool<float>.Shared.Rent(nv8);
            var      centSum = Vector3.Zero;
            int      nv4     = nv * 4;
            int[]    joints  = new int  [nv4];
            float[]  weights = new float[nv4];

            for (int vi = 0; vi < nv; vi++)
            {
                var v = face.Vertices[vi];
                var bp = Vector4.Transform(
                    new Vector4(v.Position.X, v.Position.Y, v.Position.Z, 1f), bindShape);
                var bn = Vector4.Transform(
                    new Vector4(v.Normal.X, v.Normal.Y, v.Normal.Z, 0f), bindShape);

                int o = vi * 12;
                verts[o    ] = bp.X; verts[o + 1] = bp.Y; verts[o + 2] = bp.Z;
                verts[o + 3] = bn.X; verts[o + 4] = bn.Y; verts[o + 5] = bn.Z;
                verts[o + 6] = v.TexCoord.X;
                verts[o + 7] = v.TexCoord.Y;
                // tangents 8-11 computed after this loop via ComputeTangents

                // Bind-space AABB (rigged faces don't use a prim transform, so the bind-space
                // position IS the avatar-local resting position — good enough for framing).
                var wp = new Vector3(bp.X, bp.Y, bp.Z);
                bMin = Vector3.Min(bMin, wp);
                bMax = Vector3.Max(bMax, wp);
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
            ComputeTangents(verts, nv, indices);

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
                new Vector4(r, g, b, a), fullbright, glow, hasAlpha, texId,
                Matrix4x4.Identity,          // rigged faces: drawn in avatar-local bind space
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
        Matrix4x4     transform,
        List<RawFace> faces,
        ref Vector3   bMin,
        ref Vector3   bMax)
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

    /// <summary>Converts a 16-element row-major float array to a Matrix4x4.</summary>
    private static Matrix4x4 FloatsToMatrix(float[] f)
    {
        if (f == null || f.Length < 16) return Matrix4x4.Identity;
        return new Matrix4x4(
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
    private static Matrix4x4[] BuildInvBindMatrices(MeshSkinData skin)
    {
        int n = skin.JointNames.Length;
        var result = new Matrix4x4[n];

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
                result[i] = new Matrix4x4(
                    raw[b    ], raw[b + 1], raw[b + 2], raw[b + 3],
                    raw[b + 4], raw[b + 5], raw[b + 6], raw[b + 7],
                    raw[b + 8], raw[b + 9], raw[b +10], raw[b +11],
                    raw[b +12], raw[b +13], raw[b +14], raw[b +15]);
            }
            else
            {
                result[i] = Matrix4x4.Identity;
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

}
