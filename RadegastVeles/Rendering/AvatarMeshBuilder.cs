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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using LibreMetaverse;
using LibreMetaverse.Rendering;
using Radegast.Veles.Core;
using SkiaSharp;
// Alias System.IO.Path to avoid clash with LibreMetaverse.Rendering.Path.
using SysPath    = System.IO.Path;
using Vector3    = System.Numerics.Vector3;
using Vector4    = System.Numerics.Vector4;
using Quaternion = System.Numerics.Quaternion;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Builds a <see cref="PrimRenderSubmission"/> containing the LOD-0 body meshes
/// for an avatar, with visual-parameter morphs and server-side baked textures applied.
/// No attachments.
/// </summary>
internal sealed class AvatarMeshBuilder(GridClient client)
{
    /// <summary>
    /// Intermediate per-face geometry before textures are downloaded.
    /// </summary>
    private sealed record BodyFaceData(
        float[]  Verts,
        ushort[] Indices,
        Vector3  Centroid,
        bool     IsTwoSided,
        float    AlphaCutoff,
        UUID     TexId,
        string?  BakeName,
        string[] Bone1,
        float[]  Weight1,
        string[] Bone2,
        float[]  Weight2);

    // IMG_DEFAULT_AVATAR: sentinel UUID meaning "no custom texture set".
    // Equivalent to LLVOAvatar's IMG_DEFAULT_AVATAR (llvoavatar.cpp:8871).
    // Source: AppearanceManager.DEFAULT_AVATAR_TEXTURE in secondlife/libremetaverse.
    private static readonly UUID DefaultAvatarTexture =
        new UUID("c228d1cf-4b5d-4ba8-84f4-899a0796aa97");

    // Magic UUIDs that attachment texture entries use to request a specific avatar bake layer
    // instead of a standalone texture.  Defined in indra/llcommon/indra_constants.cpp in the
    // SL viewer (IMG_USE_BAKED_*).  Values mapped to AvatarTextureIndex baked slot indices.
    // Static caches for data parsed from avatar_lad.xml / avatar_skeleton.xml.
    // These files never change at runtime, so parsing once and reusing is safe.
    private static LindenAvatarDefinition?                                      s_cachedAvatarDef;
    private static Dictionary<string, List<(int ParamId, string MorphName)>>?  s_cachedMorphParams;
    private static Dictionary<string, HashSet<string>>?                         s_cachedDynamicMorphNames;

    private static readonly Dictionary<UUID, int> s_imgUseBakedSlots = new()
    {
        { new UUID("5a9f4a74-30f2-821c-b88d-70499d3e7183"),  8 },  // HEAD   → HeadBaked
        { new UUID("ae2de45c-d252-50b8-5c6e-19f39ce79317"),  9 },  // UPPER  → UpperBaked
        { new UUID("24daea5f-0539-cfcf-047f-fbc40b2786ba"), 10 },  // LOWER  → LowerBaked
        { new UUID("52cc6bb6-2ee5-e632-d3ad-50197b1dcb8a"), 11 },  // EYES   → EyesBaked
        { new UUID("43529ce8-7faa-ad92-165a-bc4078371687"), 19 },  // SKIRT  → SkirtBaked
        { new UUID("09aac1fb-6bce-0bee-7d44-caac6dbb6c63"), 20 },  // HAIR   → HairBaked
        { new UUID("ff62763f-d60a-9855-890b-0c96f8f8cd98"), 41 },  // LEFTARM→ LeftArmBaked
        { new UUID("8e915e25-31d1-cc95-ae08-d58a47488251"), 42 },  // LEFTLEG→ LegLegBaked
        { new UUID("9742065b-19b5-297c-858a-29711d539043"), 43 },  // AUX1   → Aux1Baked
        { new UUID("03642e83-2bd1-4eb9-34b4-4c47ed586d2d"), 44 },  // AUX2   → Aux2Baked
        { new UUID("edd51b77-fc10-ce7a-4b3d-011dfc349e4f"), 45 },  // AUX3   → Aux3Baked
    };

    /// <summary>
    /// If <paramref name="texId"/> is one of the magic IMG_USE_BAKED_* sentinel UUIDs,
    /// returns the real baked-texture UUID from the avatar's TextureEntry for that bake slot.
    /// Otherwise returns <paramref name="texId"/> unchanged.
    /// </summary>
    private static UUID ResolveBakedTexId(Avatar? avatar, UUID texId)
    {
        if (avatar?.Textures == null) return texId;
        if (!s_imgUseBakedSlots.TryGetValue(texId, out int slot)) return texId;
        var resolved = avatar.Textures.GetFace((uint)slot).TextureID;
        if (resolved == UUID.Zero || resolved == DefaultAvatarTexture
            || resolved == Primitive.TextureEntry.WHITE_TEXTURE)
            return texId;  // slot not set; keep magic ID so the face is skipped/blank
        return resolved;
    }

    private readonly PrimMeshBuilder _primMesher = new(client);
    private Dictionary<int, AttachPoint>? _attachPoints;

    private sealed record AttachPoint(
        string     JointName,
        Vector3    Position,
        Quaternion Rotation);

    // ── Public API ────────────────────────────────────────────────────────────────

    public async Task<AvatarBuildResult> BuildAsync(
        uint                            avatarLocalId,
        IReadOnlyDictionary<int, float> visualParams,
        string                          label,
        IProgress<string>?              progress,
        CancellationToken               ct,
        int                             lodLevel        = 0,
        Action<PrimRenderSubmission>?   onGeometryReady = null,
        IProgress<SceneTexturePatch>?   texturePatch    = null)
    {
        // 1. Load avatar definition and build VP-morphed bone world matrices.
        LindenAvatarDefinition?            avatarDef         = null;
        Dictionary<string, BoneTransform>? boneTransforms    = null;
        Dictionary<string, Matrix4x4>?     boneWorldMatrices = null;
        try
        {
            progress?.Report("Loading avatar definition…");
            avatarDef         = s_cachedAvatarDef ??= LindenAvatarDefinition.Load();
            boneTransforms    = avatarDef.ComputeBoneTransforms(visualParams);
            // Build the DEFAULT (bind-pose) bone world matrices using the skeleton's
            // undeformed T-pose joint positions and scales — no VP bone deformations.
            // These are used as the invBind matrices for LBS and for eye pre-transforms.
            //
            // Body shape/proportion changes from visual parameters come ENTIRELY from
            // VP mesh morphs (coord deltas applied in ApplyBodyMeshMorphs).  The VP bone
            // transforms (boneTransforms above) are only used for attachment placement.
            //
            // Keeping invBind in the same default space as BindVerts means the LBS
            // formula reduces to a pure rotation:
            //   v_anim = v_bind × Invert(default_bone) × (default_bone + rotation_delta)
            //          ≈ v_bind × rotation_delta
            boneWorldMatrices = BuildBoneWorldMatrices(avatarDef.Skeleton,
                new Dictionary<string, BoneTransform>());
        }
        catch (Exception ex)
        {
            progress?.Report(
                $"Avatar definition error: {ex.Message} " +
                $"(RESOURCE_DIR={LibreMetaverse.Settings.ResourceDir})");
        }

        if (avatarDef == null)
        {
            var emptySub = new PrimRenderSubmission
            {
                Label     = label,
                Faces     = [],
                BoundsMin = new Vector3(-0.5f),
                BoundsMax = new Vector3( 0.5f),
            };
            return new AvatarBuildResult(emptySub, [], null, null, null, null,
                ImmutableArray<AvatarFaceMorphData>.Empty);
        }

        // 2. Find the avatar object in the sim for baked-texture lookup.
        var sim       = client.Network.CurrentSim;
        var avatarObj = sim?.ObjectsAvatars.Values
                           .FirstOrDefault(a => a?.LocalID == avatarLocalId);

        // For the self avatar the ObjectsAvatars entry may have no Textures set
        // (or may be missing entirely) early in the session, because the server
        // sends baked texture data via AgentAppearance rather than ObjectUpdate.
        // Clone the object and inject AppearanceManager.MyTextures so that
        // GetBakedTextureId() can resolve real bake UUIDs for every body mesh.
        if (avatarLocalId != 0 && avatarLocalId == client.Self.LocalID)
        {
            var myTex = client.Appearance.MyTextures;
            bool hasRealTextures = myTex.DefaultTexture != null
                || (myTex.FaceTextures?.Any(t => t != null && t.TextureID != UUID.Zero) ?? false);
            if (hasRealTextures)
            {
                var synthetic = avatarObj ?? new Avatar
                {
                    LocalID  = avatarLocalId,
                    ID       = client.Self.AgentID,
                    Position = client.Self.SimPosition,
                    Rotation = client.Self.SimRotation,
                };
                if (avatarObj == null || avatarObj.ID == UUID.Zero)
                    synthetic = new Avatar
                    {
                        LocalID  = synthetic.LocalID,
                        ID       = client.Self.AgentID,
                        Position = synthetic.Position,
                        Rotation = synthetic.Rotation,
                        Textures = myTex,
                    };
                else if (avatarObj.Textures == null
                    || !(avatarObj.Textures.FaceTextures?.Any(t => t != null && t.TextureID != UUID.Zero) ?? false))
                    synthetic = new Avatar
                    {
                        LocalID  = avatarObj.LocalID,
                        ID       = avatarObj.ID != UUID.Zero ? avatarObj.ID : client.Self.AgentID,
                        Position = avatarObj.Position,
                        Rotation = avatarObj.Rotation,
                        Textures = myTex,
                    };
                avatarObj = synthetic;
            }
        }

        // 3. Build body mesh faces with LOD selection, baked textures and VP morphs.
        // onGeometryReady is fired inside BuildBodyMeshFacesAsync after geometry is
        // ready but before texture download completes, so the caller can show a grey
        // placeholder while textures are still downloading.
        var (faces, skinData, faceMorphData, bMin, bMax) = await BuildBodyMeshFacesAsync(
            avatarDef, avatarObj, visualParams, boneWorldMatrices, progress, ct, lodLevel,
            onGeometryReady != null ? (greyFaces, gMin, gMax) =>
            {
                var greySub = new PrimRenderSubmission
                {
                    Label      = label,
                    Faces      = greyFaces,
                    BoundsMin  = gMin.X < float.MaxValue ? gMin : new Vector3(-0.5f),
                    BoundsMax  = gMax.X > float.MinValue ? gMax : new Vector3( 0.5f),
                    FlexiPrims = [],
                };
                onGeometryReady(greySub);
            } : null,
            avatarLocalId: avatarLocalId,
            texturePatch:  null)   // body bakes use the blocking path; patches would be lost when UploadSubmission frees the grey shell
            .ConfigureAwait(false);

        // VP-deformed bone world matrices: used only for attachment placement so that
        // attachment points follow the body-proportion-adjusted skeleton.  They are NOT
        // used for body-mesh LBS (that uses the default boneWorldMatrices above).
        Dictionary<string, Matrix4x4>? vpBoneWorldMatrices = null;
        if (boneTransforms != null)
            vpBoneWorldMatrices = BuildBoneWorldMatrices(avatarDef.Skeleton, boneTransforms);

        // 4. Build attachment faces (non-HUD, rigid only).
        // Use the default (non-VP) bone world matrices for attachment joint positions.
        // VP position deformations accumulate through the skeleton hierarchy into a different
        // coordinate space than the LLM mesh morphs, causing catastrophic misplacement.
        // StripScale() inside BuildAttachmentsAsync removes VP scale; default positions match
        // the LLM bind-pose space so attachments land on the correct mesh surface.
        var flexiInfos = new List<FlexiPrimInfo>();

        if (sim != null && boneWorldMatrices != null)
        {
            try
            {
                var (attFaces, attFaceBones, attRiggedSkins, attAttachInfos, aBMin, aBMax) = await BuildAttachmentsAsync(
                    avatarLocalId, avatarObj, sim, boneWorldMatrices, progress, ct, texturePatch)
                    .ConfigureAwait(false);
                int bodyFaceCount = faces.Count;

                // Flexi rigid attachment faces are driven by FlexiPrimAnimator, not skinData.
                // Key: prim LocalID  Value: (source Primitive, prim-to-avatar-local transform, attach-point metadata, ordered face-slot list)
                var pendingFlexi = new Dictionary<uint, (Primitive prim, Matrix4x4 attachTx, Matrix4x4 primLocalMatrix, string jointName, Vector3 jointOffset, Quaternion jointRot, List<(int faceIdx, float[] verts)> faceList)>();
                var emptyDeltas = System.Collections.Immutable.ImmutableDictionary<string, Quaternion>.Empty;
                var tposeAttachmentBones = ComputeAttachmentBoneWorldMatrices(avatarDef, boneTransforms!, emptyDeltas);

                for (int i = 0; i < attFaces.Count; i++)
                {
                    var origFace      = attFaces[i];
                    var faceTransform = origFace.Transform;
                    int nv            = (origFace.Vertices?.Length ?? 0) / 8;
                    var rigged        = attRiggedSkins[i];

                    // Attachment vertices are stored in prim-local space by PrimMeshBuilder;
                    // the shader always applies face.Transform as the model matrix.
                    // For LBS-animated faces we need Transform=Identity with vertices already
                    // in world space (matching the body mesh convention) so the AnimTick
                    // formula  v_anim = v_bind × invBind × animBone  is correct.
                    //
                    // Rigged faces arrive with Transform=Identity and bind-space vertices
                    // already — the TransformRow calls below become no-ops but we still run
                    // them to keep one code path.
                    var worldVerts = GC.AllocateUninitializedArray<float>(origFace.Vertices.Length);
                    for (int vi = 0; vi < nv; vi++)
                    {
                        int o  = vi * 8;
                        var wp = Vector4.Transform(
                            new Vector4(origFace.Vertices[o],     origFace.Vertices[o + 1],
                                        origFace.Vertices[o + 2], 1f), faceTransform);
                        var wn = Vector4.Transform(
                            new Vector4(origFace.Vertices[o + 3], origFace.Vertices[o + 4],
                                        origFace.Vertices[o + 5], 0f), faceTransform);
                        worldVerts[o]     = wp.X; worldVerts[o + 1] = wp.Y; worldVerts[o + 2] = wp.Z;
                        worldVerts[o + 3] = wn.X; worldVerts[o + 4] = wn.Y; worldVerts[o + 5] = wn.Z;
                        worldVerts[o + 6] = origFace.Vertices[o + 6];
                        worldVerts[o + 7] = origFace.Vertices[o + 7];
                    }

                    attFaces[i] = new PrimRenderFace
                    {
                        Vertices                 = worldVerts,
                        Indices                  = origFace.Indices,
                        Color                    = origFace.Color,
                        Fullbright               = origFace.Fullbright,
                        Glow                     = origFace.Glow,
                        HasAlpha                 = origFace.HasAlpha,
                        AlphaAuto                = origFace.AlphaAuto,
                        PrimLocalId              = origFace.PrimLocalId,
                        FaceIndex                = origFace.FaceIndex,
                        Texture                  = origFace.Texture,
                        Transform                = Matrix4x4.Identity,
                        Centroid                 = origFace.Centroid,
                        IsTwoSided               = origFace.IsTwoSided,
                        AlphaCutoff              = origFace.AlphaCutoff,
                        Shiny                    = origFace.Shiny,
                        HasBump                  = origFace.HasBump,
                        AlphaMode                = origFace.AlphaMode,
                        HasMaterial              = origFace.HasMaterial,
                        NormalMapTexture         = origFace.NormalMapTexture,
                        SpecularMapTexture       = origFace.SpecularMapTexture,
                        SpecularColor            = origFace.SpecularColor,
                        SpecularExponent         = origFace.SpecularExponent,
                        EnvironmentIntensity     = origFace.EnvironmentIntensity,
                        NormalUvXform            = origFace.NormalUvXform,
                        SpecularUvXform          = origFace.SpecularUvXform,
                        IsPBR                    = origFace.IsPBR,
                        MetallicRoughnessTexture = origFace.MetallicRoughnessTexture,
                        EmissiveTexture          = origFace.EmissiveTexture,
                        BaseColorFactor          = origFace.BaseColorFactor,
                        MetallicFactor           = origFace.MetallicFactor,
                        RoughnessFactor          = origFace.RoughnessFactor,
                        EmissiveFactor           = origFace.EmissiveFactor,
                        BaseColorUvXform         = origFace.BaseColorUvXform,
                        MetallicRoughnessUvXform = origFace.MetallicRoughnessUvXform,
                        EmissiveUvXform          = origFace.EmissiveUvXform,
                        PbrNormalUvXform         = origFace.PbrNormalUvXform,
                    };

                    if (rigged != null)
                    {
                        // Keep the per-attachment inverse bind matrices from this mesh asset's
                        // own skin block. Replacing them with body/skeleton-derived matrices
                        // puts fitted collision-volume weights in the wrong bind space.
                        RejectOutlierRiggedInfluences(rigged, worldVerts, nv, tposeAttachmentBones);

                        skinData.Add(new AvatarFaceSkinData
                        {
                            FaceIndex            = bodyFaceCount + i,
                            BindVerts            = worldVerts,
                            JointNames           = rigged.JointNames,
                            InvBindMatrices      = rigged.InvBindMatrices,
                            Joints  = rigged.Joints,
                            Weights = rigged.Weights,
                            UseVpBoneTransforms  = true,
                        });
                    }
                    else
                    {
                        // Check whether this rigid attachment prim is flexi.
                        var srcPrim  = sim.ObjectsPrimitives.TryGetValue(attFaces[i].PrimLocalId, out var sp) ? sp : null;
                        bool isFlexi = srcPrim?.Flexible != null
                                       && srcPrim.PrimData.PathCurve == PathCurve.Flexible;

                        if (isFlexi)
                        {
                            // Flexi faces are animated by FlexiPrimAnimator — do not bind to skinData.
                            // BaseVertices must be prim-local (origFace.Vertices, before the worldVerts
                            // transform is applied) so the animator can use Z ∈ [-0.5, 0.5] as the
                            // spine path parameter.  faceTransform is stored as AttachTransform and
                            // applied by the animator after each deformation tick to bring the result
                            // back into avatar-local space.
                            var ai = i < attAttachInfos.Count ? attAttachInfos[i] : default;
                            if (!pendingFlexi.TryGetValue(srcPrim!.LocalID, out var entry))
                            {
                                // Compute the prim's own SRT — Scale × Rotation × Translation.
                                // This is the prim-local part that prefixes the attachment joint
                                // matrix and must be combined with the live bone each tick.
                                var ps = new Vector3(srcPrim.Scale.X,    srcPrim.Scale.Y,    srcPrim.Scale.Z);
                                var pr = new Quaternion(srcPrim.Rotation.X, srcPrim.Rotation.Y, srcPrim.Rotation.Z, srcPrim.Rotation.W);
                                var pp = new Vector3(srcPrim.Position.X, srcPrim.Position.Y, srcPrim.Position.Z);
                                var primLocalMatrix = Matrix4x4.CreateScale(ps)
                                                   * Matrix4x4.CreateFromQuaternion(pr)
                                                   * Matrix4x4.CreateTranslation(pp);

                                // Child prims in an attachment linkset are positioned in the root
                                // prim's orientation space (see PrimMeshBuilder.TessellateAttachmentAsync:
                                // child transform = childScale × childRot × childTrans × rootRot ×
                                // rootTrans × attachJointMatrix).  The dynamic flexi recomputation in
                                // FlexiPrimAnimator only multiplies PrimLocalMatrix by the attachment
                                // joint matrix, so for a child we must bake the root's Rot × Trans
                                // into PrimLocalMatrix or the prim will jump off the avatar as soon
                                // as the live bone provider drives it.
                                if (srcPrim.LocalID != ai.PrimLocalId &&
                                    sim.ObjectsPrimitives.TryGetValue(ai.PrimLocalId, out var rootPrim) &&
                                    rootPrim != null)
                                {
                                    var rr = new Quaternion(rootPrim.Rotation.X, rootPrim.Rotation.Y, rootPrim.Rotation.Z, rootPrim.Rotation.W);
                                    var rp = new Vector3(rootPrim.Position.X, rootPrim.Position.Y, rootPrim.Position.Z);
                                    primLocalMatrix = primLocalMatrix
                                                    * Matrix4x4.CreateFromQuaternion(rr)
                                                    * Matrix4x4.CreateTranslation(rp);
                                }
                                entry = (srcPrim, faceTransform, primLocalMatrix,
                                         ai.JointName ?? string.Empty,
                                         ai.JointOffset, ai.JointRotation, new List<(int, float[])>());
                                pendingFlexi[srcPrim.LocalID] = entry;
                            }
                            entry.faceList.Add((bodyFaceCount + i, origFace.Vertices));
                        }
                        else
                        {
                            // Rigid attachment: bind every vertex to the attachment joint with weight 1.
                            var bone1   = new string[nv]; Array.Fill(bone1,   attFaceBones[i]);
                            var weight1 = new float[nv];  Array.Fill(weight1, 1.0f);
                            skinData.Add(new AvatarFaceSkinData
                            {
                                FaceIndex = bodyFaceCount + i,
                                BindVerts = worldVerts,
                                Bone1     = bone1,
                                Weight1   = weight1,
                                Bone2     = new string[nv],
                                Weight2   = new float[nv],
                            });
                        }
                    }
                }
                faces.AddRange(attFaces);

                // Flexi faces are fully driven by FlexiPrimAnimator which applies the
                // attachment transform after each deformation tick.  The static
                // face.Transform (T-pose attachment matrix) must be Identity so the GPU
                // does not double-apply it on top of the animator's output.
                foreach (var entry in pendingFlexi.Values)
                {
                    foreach (var (faceIdx, _) in entry.faceList)
                    {
                        faces[faceIdx].Transform = Matrix4x4.Identity;
                        // See PrimRenderFace.IsFlexi — keeps ApplySceneTransformOverrides
                        // from stomping this identity transform with the avatar's world matrix.
                        faces[faceIdx].IsFlexi   = true;
                    }
                }

                // Build FlexiPrimInfo entries for flexi rigid attachments.
                foreach (var (flexPrim, attachTx, primLocalMatrix, jointName, jointOffset, jointRot, faceList) in pendingFlexi.Values)
                {
                    if (faceList.Count == 0) continue;
                    int faceStart = faceList[0].faceIdx;
                    int faceCount = faceList.Count;
                    // BaseVertices are prim-local (origFace.Vertices, Z ∈ [-0.5, 0.5]).
                    // AttachTransform carries the full prim→avatar-local matrix so the
                    // animator can re-apply it after each deformation tick.
                    var baseVerts = faceList.Select(f => (float[])f.verts.Clone()).ToArray();
                    int segments  = FlexiPrimAnimator.ComputeSegmentCount(flexPrim.Flexible!.Softness);
                    flexiInfos.Add(new FlexiPrimInfo
                    {
                        Prim               = flexPrim,
                        FaceStart          = faceStart,
                        FaceCount          = faceCount,
                        BaseVertices       = baseVerts,
                        PathSegments       = segments,
                        ProfileVertexCount = baseVerts[0].Length / 8 / Math.Max(1, segments),
                        Scale              = flexPrim.Scale,
                        AttachTransform    = attachTx,
                        PrimLocalMatrix    = primLocalMatrix,
                        AttachJointName    = jointName.Length > 0 ? jointName : null,
                        AttachJointOffset  = jointOffset,
                        AttachJointRotation = jointRot,
                    });
                }
                if (aBMin.X < float.MaxValue)
                {
                    bMin = Vector3.Min(bMin, aBMin);
                    bMax = Vector3.Max(bMax, aBMax);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* attachment build failure is non-fatal */ }
        }

        if (faces.Count == 0 || bMin.X >= float.MaxValue)
        {
            bMin = new Vector3(-0.5f);
            bMax = new Vector3( 0.5f);
        }

        var skinArray = skinData.ToArray();
        var submission = new PrimRenderSubmission
        {
            Label      = label,
            Faces      = faces.ToArray(),
            BoundsMin  = bMin,
            BoundsMax  = bMax,
            FlexiPrims = flexiInfos.ToArray(),
            SkinData   = skinArray,
        };
        // Pass empty BoneTransforms so AnimTick uses the default skeleton + rotation-only LBS.
        // VP bone scale/position changes come from mesh morphs (BindVerts), not bone matrices.
        // FittedBoneTransforms carries the real VP-deformed values for rigged / fitted mesh
        // faces (see AvatarFaceSkinData.UseVpBoneTransforms).
        return new AvatarBuildResult(submission, skinArray, avatarDef,
            new Dictionary<string, BoneTransform>(), boneWorldMatrices,
            boneTransforms ?? new Dictionary<string, BoneTransform>(),
            faceMorphData);
    }

    // ── Bone world matrices ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a copy of <paramref name="m"/> with the scale removed from the upper-left 3×3,
    /// leaving only rotation and translation. Used for attachment placement so VP bone scale
    /// (which adjusts skeleton proportions) does not stretch rigid attachment geometry.
    /// </summary>
    private static Matrix4x4 StripScale(Matrix4x4 m)
    {
        var r0 = new Vector3(m.M11, m.M12, m.M13);
        if (r0.LengthSquared() > 1e-10f) r0 = Vector3.Normalize(r0);
        var r1 = new Vector3(m.M21, m.M22, m.M23);
        if (r1.LengthSquared() > 1e-10f) r1 = Vector3.Normalize(r1);
        var r2 = new Vector3(m.M31, m.M32, m.M33);
        if (r2.LengthSquared() > 1e-10f) r2 = Vector3.Normalize(r2);
        return new Matrix4x4(
            r0.X, r0.Y, r0.Z, 0f,
            r1.X, r1.Y, r1.Z, 0f,
            r2.X, r2.Y, r2.Z, 0f,
            m.M41, m.M42, m.M43, m.M44);
    }

    private static Dictionary<string, Matrix4x4> BuildBoneWorldMatrices(
        LindenSkeleton                    skeleton,
        Dictionary<string, BoneTransform> boneTransforms)
    {
        var result = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        BuildBoneMatricesRecursive(skeleton.bone, Matrix4x4.Identity, boneTransforms, result);
        return result;
    }

    // Caches the split alias arrays for each Joint instance so aliases.Split is never
    // called more than once per distinct Joint object across the lifetime of the process.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Joint, string[]>
        s_aliasCache = new();

    private static void BuildBoneMatricesRecursive(
        Joint                             joint,
        Matrix4x4                         parentWorld,
        Dictionary<string, BoneTransform> boneTransforms,
        Dictionary<string, Matrix4x4>     result,
        IReadOnlyDictionary<string, Quaternion>? rotDeltas = null,
        bool                              applyVpScale = true)
    {
        var world = BuildLocalMatrix(joint, boneTransforms, rotDeltas, applyVpScale) * parentWorld;

        if (!string.IsNullOrEmpty(joint.name))
            result[joint.name] = world;

        // Use cached split — Joint objects are loaded once at startup so this
        // eliminates the per-frame string[] allocation that was the #1 hotspot.
        if (!string.IsNullOrEmpty(joint.aliases))
        {
            var aliases = s_aliasCache.GetValue(joint,
                j => j.aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            foreach (var alias in aliases)
                if (!string.IsNullOrEmpty(alias))
                    result.TryAdd(alias, world);
        }

        foreach (var cv in joint.collision_volume ?? [])
        {
            if (cv == null) continue;
            // Collision volumes are fitted-mesh bones. Build them through the same
            // transform path as regular joints so visual-parameter position/scale
            // deformations drive attachments exactly as they drive the live avatar skeleton.
            var cvLocal = BuildCollisionVolumeLocalMatrix(cv, boneTransforms);
            var cvWorld = cvLocal * world;
            if (!string.IsNullOrEmpty(cv.name))
                result.TryAdd(cv.name, cvWorld);
        }

        foreach (var child in joint.bone ?? [])
        {
            if (child == null) continue;
            BuildBoneMatricesRecursive(child, world, boneTransforms, result, rotDeltas, applyVpScale);
        }
    }

    private static Matrix4x4 BuildLocalMatrix(
        JointBase                         joint,
        Dictionary<string, BoneTransform> boneTransforms,
        IReadOnlyDictionary<string, Quaternion>? rotDeltas = null,
        bool                              applyVpScale = true)
    {
        if (string.IsNullOrEmpty(joint.name))
            return Matrix4x4.Identity;

        Vector3 pos, scale;
        if (boneTransforms.TryGetValue(joint.name, out var bt))
        {
            pos   = new Vector3(bt.Position.X, bt.Position.Y, bt.Position.Z);
            // When applyVpScale is false (attachment LBS), use the XML default scale so that
            // VP scale deformations do not compound multiplicatively through the hierarchy.
            // VP position deltas are still applied to preserve proportional bone spacing.
            scale = applyVpScale
                ? new Vector3(bt.Scale.X, bt.Scale.Y, bt.Scale.Z)
                : (joint.scale?.Length >= 3
                    ? new Vector3(joint.scale[0], joint.scale[1], joint.scale[2])
                    : Vector3.One);
        }
        else
        {
            pos   = joint.pos?.Length   >= 3
                ? new Vector3(joint.pos[0],   joint.pos[1],   joint.pos[2])
                : Vector3.Zero;
            scale = joint.scale?.Length >= 3
                ? new Vector3(joint.scale[0], joint.scale[1], joint.scale[2])
                : Vector3.One;
        }

        float rx = joint.rot?.Length > 0 ? joint.rot[0] * (MathF.PI / 180f) : 0f;
        float ry = joint.rot?.Length > 1 ? joint.rot[1] * (MathF.PI / 180f) : 0f;
        float rz = joint.rot?.Length > 2 ? joint.rot[2] * (MathF.PI / 180f) : 0f;
        var rot = Quaternion.CreateFromYawPitchRoll(ry, rx, rz);

        // Apply animation delta on top of the T-pose rotation (mirrors deformbone in RenderAvatar).
        if (rotDeltas != null && rotDeltas.TryGetValue(joint.name, out var delta))
            rot = rot * delta;

        return Matrix4x4.CreateScale(scale)
             * Matrix4x4.CreateFromQuaternion(rot)
             * Matrix4x4.CreateTranslation(pos);
    }

    private static Matrix4x4 BuildCollisionVolumeLocalMatrix(
        CollisionVolume                   volume,
        Dictionary<string, BoneTransform> boneTransforms)
    {
        Vector3 pos, scale;
        if (!string.IsNullOrEmpty(volume.name) && boneTransforms.TryGetValue(volume.name, out var bt))
        {
            pos   = new Vector3(bt.Position.X, bt.Position.Y, bt.Position.Z);
            scale = new Vector3(bt.Scale.X,    bt.Scale.Y,    bt.Scale.Z);
        }
        else
        {
            pos = volume.pos?.Length >= 3
                ? new Vector3(volume.pos[0], volume.pos[1], volume.pos[2])
                : Vector3.Zero;
            scale = volume.scale?.Length >= 3
                ? new Vector3(volume.scale[0], volume.scale[1], volume.scale[2])
                : Vector3.One;
        }

        float rx = volume.rot?.Length > 0 ? volume.rot[0] * (MathF.PI / 180f) : 0f;
        float ry = volume.rot?.Length > 1 ? volume.rot[1] * (MathF.PI / 180f) : 0f;
        float rz = volume.rot?.Length > 2 ? volume.rot[2] * (MathF.PI / 180f) : 0f;
        var rot = Quaternion.CreateFromYawPitchRoll(ry, rx, rz);

        return Matrix4x4.CreateScale(scale)
             * Matrix4x4.CreateFromQuaternion(rot)
             * Matrix4x4.CreateTranslation(pos);
    }

    /// <summary>
    /// Compute animated bone world matrices by rebuilding the skeleton with the
    /// given per-joint rotation deltas applied on top of the T-pose rotations.
    /// Equivalent to calling <see cref="BuildBoneWorldMatrices"/> but with live
    /// animation values threaded through <see cref="BuildLocalMatrix"/>.
    /// </summary>
    internal static Dictionary<string, Matrix4x4> ComputeAnimatedBoneWorldMatrices(
        LindenAvatarDefinition                   avatarDef,
        Dictionary<string, BoneTransform>        boneTransforms,
        IReadOnlyDictionary<string, Quaternion>  rotDeltas)
    {
        var result = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        BuildBoneMatricesRecursive(avatarDef.Skeleton.bone, Matrix4x4.Identity,
            boneTransforms, result, rotDeltas);
        return result;
    }

    /// <summary>
    /// Reuse-overload: clears and refills <paramref name="result"/> in-place so the
    /// caller can keep a pre-allocated dictionary alive across frames.
    /// </summary>
    internal static void ComputeAnimatedBoneWorldMatrices(
        LindenAvatarDefinition                   avatarDef,
        Dictionary<string, BoneTransform>        boneTransforms,
        IReadOnlyDictionary<string, Quaternion>  rotDeltas,
        Dictionary<string, Matrix4x4>            result)
    {
        result.Clear();
        BuildBoneMatricesRecursive(avatarDef.Skeleton.bone, Matrix4x4.Identity,
            boneTransforms, result, rotDeltas);
    }

    /// <summary>
    /// Compute bone world matrices for rigged attachment LBS, applying VP position
    /// deltas but <em>not</em> VP scale deformations.
    /// compounding multiplicatively through the hierarchy (e.g. mChest accumulating
    /// ~4.8× Z scale), which would catastrophically inflate vertex positions.
    /// Matches the SL viewer: scale deformations affect only the avatar body mesh
    /// via vertex morphs, not the animBone matrices used for attachment skinning.
    /// </summary>
    internal static Dictionary<string, Matrix4x4> ComputeAttachmentBoneWorldMatrices(
        LindenAvatarDefinition                   avatarDef,
        Dictionary<string, BoneTransform>        boneTransforms,
        IReadOnlyDictionary<string, Quaternion>  rotDeltas)
    {
        var result = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        BuildBoneMatricesRecursive(avatarDef.Skeleton.bone, Matrix4x4.Identity,
            boneTransforms, result, rotDeltas, applyVpScale: false);
        return result;
    }

    /// <summary>
    /// Reuse-overload: clears and refills <paramref name="result"/> in-place.
    /// </summary>
    internal static void ComputeAttachmentBoneWorldMatrices(
        LindenAvatarDefinition                   avatarDef,
        Dictionary<string, BoneTransform>        boneTransforms,
        IReadOnlyDictionary<string, Quaternion>  rotDeltas,
        Dictionary<string, Matrix4x4>            result)
    {
        result.Clear();
        BuildBoneMatricesRecursive(avatarDef.Skeleton.bone, Matrix4x4.Identity,
            boneTransforms, result, rotDeltas, applyVpScale: false);
    }

    private static void RejectOutlierRiggedInfluences(
        PrimMeshBuilder.AttachmentRiggedSkin skin,
        float[]                              bindVerts,
        int                                  vertexCount,
        Dictionary<string, Matrix4x4>        tposeBones)
    {
        if (skin.InvBindMatrices.Length == 0 || skin.JointNames.Length == 0) return;

        const float maxDelta = 0.25f;
        float maxDeltaSq = maxDelta * maxDelta;

        for (int vi = 0; vi < vertexCount; vi++)
        {
            int o  = vi * 8;
            int si = vi * 4;
            var bp = new Vector4(bindVerts[o], bindVerts[o + 1], bindVerts[o + 2], 1f);

            RejectOutlierInfluence(skin.JointNames, skin.InvBindMatrices, tposeBones, bp, maxDeltaSq,
                skin.Joints[si],     ref skin.Weights[si]);
            RejectOutlierInfluence(skin.JointNames, skin.InvBindMatrices, tposeBones, bp, maxDeltaSq,
                skin.Joints[si + 1], ref skin.Weights[si + 1]);
            RejectOutlierInfluence(skin.JointNames, skin.InvBindMatrices, tposeBones, bp, maxDeltaSq,
                skin.Joints[si + 2], ref skin.Weights[si + 2]);
            RejectOutlierInfluence(skin.JointNames, skin.InvBindMatrices, tposeBones, bp, maxDeltaSq,
                skin.Joints[si + 3], ref skin.Weights[si + 3]);

            float sum = skin.Weights[si] + skin.Weights[si + 1] + skin.Weights[si + 2] + skin.Weights[si + 3];
            if (sum > 1e-6f)
            {
                float inv = 1f / sum;
                skin.Weights[si]     *= inv;
                skin.Weights[si + 1] *= inv;
                skin.Weights[si + 2] *= inv;
                skin.Weights[si + 3] *= inv;
            }
            else
            {
                skin.Joints[si] = FindNearestJoint(skin.JointNames, new Vector3(bp.X, bp.Y, bp.Z), tposeBones);
                skin.Joints[si + 1] = skin.Joints[si + 2] = skin.Joints[si + 3] = skin.Joints[si];
                skin.Weights[si]     = 1f;
                skin.Weights[si + 1] = skin.Weights[si + 2] = skin.Weights[si + 3] = 0f;
            }
        }
    }

    private static void RejectOutlierInfluence(
        string[]                      jointNames,
        Matrix4x4[]                   invBindMatrices,
        Dictionary<string, Matrix4x4> tposeBones,
        Vector4                       bindPos,
        float                         maxDeltaSq,
        int                           jointIndex,
        ref float                     weight)
    {
        if (weight <= 1e-4f) return;
        if ((uint)jointIndex >= (uint)jointNames.Length || (uint)jointIndex >= (uint)invBindMatrices.Length)
        {
            weight = 0f;
            return;
        }

        if (!tposeBones.TryGetValue(jointNames[jointIndex], out var tposeBone))
        {
            weight = 0f;
            return;
        }

        var bindCheck = Vector4.Transform(Vector4.Transform(bindPos, invBindMatrices[jointIndex]), tposeBone);
        var deltaXyz  = new Vector3(bindCheck.X - bindPos.X, bindCheck.Y - bindPos.Y, bindCheck.Z - bindPos.Z);
        if (deltaXyz.LengthSquared() > maxDeltaSq)
            weight = 0f;
    }

    private static int FindNearestJoint(
        string[]                      jointNames,
        Vector3                       pos,
        Dictionary<string, Matrix4x4> tposeBones)
    {
        int best = 0;
        float bestD2 = float.MaxValue;
        for (int i = 0; i < jointNames.Length; i++)
        {
            if (!tposeBones.TryGetValue(jointNames[i], out var m)) continue;
            var bonePos = new Vector3(m.M41, m.M42, m.M43);
            float d2 = (bonePos - pos).LengthSquared();
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Kinematic groups: bones that move independently of each other during animation.
    /// Kept separate so that cross-group weight contamination can be detected and corrected.
    /// Bones not listed in <see cref="s_boneGroups"/> (e.g. skull, eye, finger bones) are
    /// treated as "None" and are considered incorrect when found on clothing vertices.
    /// </summary>
    private enum BoneGroup
    {
        None        = 0,
        LeftArm     = 1,   // mCollarLeft … mWristLeft
        RightArm    = 2,   // mCollarRight … mWristRight
        LeftLeg     = 3,   // mHipLeft … mToeLeft
        RightLeg    = 4,   // mHipRight … mToeRight
        UpperSpine  = 5,   // mTorso, mChest, mNeck, mHead (stays upright during walk)
        Pelvis      = 6,   // mPelvis root (rocks with walk cycle; separate from UpperSpine)
    }

    private static readonly Dictionary<string, BoneGroup> s_boneGroups = new(StringComparer.Ordinal)
    {
        // ── Regular animation bones ───────────────────────────────────────────────
        // Left arm chain
        ["mCollarLeft"]   = BoneGroup.LeftArm,
        ["mShoulderLeft"] = BoneGroup.LeftArm,
        ["mElbowLeft"]    = BoneGroup.LeftArm,
        ["mWristLeft"]    = BoneGroup.LeftArm,
        // Left hand / finger bones (extended skeleton, children of mWristLeft)
        ["mHandMiddle1Left"] = BoneGroup.LeftArm,
        ["mHandMiddle2Left"] = BoneGroup.LeftArm,
        ["mHandMiddle3Left"] = BoneGroup.LeftArm,
        ["mHandIndex1Left"]  = BoneGroup.LeftArm,
        ["mHandIndex2Left"]  = BoneGroup.LeftArm,
        ["mHandIndex3Left"]  = BoneGroup.LeftArm,
        ["mHandRing1Left"]   = BoneGroup.LeftArm,
        ["mHandRing2Left"]   = BoneGroup.LeftArm,
        ["mHandRing3Left"]   = BoneGroup.LeftArm,
        ["mHandPinky1Left"]  = BoneGroup.LeftArm,
        ["mHandPinky2Left"]  = BoneGroup.LeftArm,
        ["mHandPinky3Left"]  = BoneGroup.LeftArm,
        ["mHandThumb1Left"]  = BoneGroup.LeftArm,
        ["mHandThumb2Left"]  = BoneGroup.LeftArm,
        ["mHandThumb3Left"]  = BoneGroup.LeftArm,
        // Right arm chain
        ["mCollarRight"]   = BoneGroup.RightArm,
        ["mShoulderRight"] = BoneGroup.RightArm,
        ["mElbowRight"]    = BoneGroup.RightArm,
        ["mWristRight"]    = BoneGroup.RightArm,
        // Right hand / finger bones (extended skeleton, children of mWristRight)
        ["mHandMiddle1Right"] = BoneGroup.RightArm,
        ["mHandMiddle2Right"] = BoneGroup.RightArm,
        ["mHandMiddle3Right"] = BoneGroup.RightArm,
        ["mHandIndex1Right"]  = BoneGroup.RightArm,
        ["mHandIndex2Right"]  = BoneGroup.RightArm,
        ["mHandIndex3Right"]  = BoneGroup.RightArm,
        ["mHandRing1Right"]   = BoneGroup.RightArm,
        ["mHandRing2Right"]   = BoneGroup.RightArm,
        ["mHandRing3Right"]   = BoneGroup.RightArm,
        ["mHandPinky1Right"]  = BoneGroup.RightArm,
        ["mHandPinky2Right"]  = BoneGroup.RightArm,
        ["mHandPinky3Right"]  = BoneGroup.RightArm,
        ["mHandThumb1Right"]  = BoneGroup.RightArm,
        ["mHandThumb2Right"]  = BoneGroup.RightArm,
        ["mHandThumb3Right"]  = BoneGroup.RightArm,
        // Left leg chain
        ["mHipLeft"]   = BoneGroup.LeftLeg,
        ["mKneeLeft"]  = BoneGroup.LeftLeg,
        ["mAnkleLeft"] = BoneGroup.LeftLeg,
        ["mFootLeft"]  = BoneGroup.LeftLeg,
        ["mToeLeft"]   = BoneGroup.LeftLeg,
        // Right leg chain
        ["mHipRight"]   = BoneGroup.RightLeg,
        ["mKneeRight"]  = BoneGroup.RightLeg,
        ["mAnkleRight"] = BoneGroup.RightLeg,
        ["mFootRight"]  = BoneGroup.RightLeg,
        ["mToeRight"]   = BoneGroup.RightLeg,
        // Upper spine (torso/chest — counter-rotates relative to pelvis during walk)
        ["mTorso"] = BoneGroup.UpperSpine,
        ["mChest"] = BoneGroup.UpperSpine,
        ["mNeck"]  = BoneGroup.UpperSpine,
        ["mHead"]  = BoneGroup.UpperSpine,
        // Pelvis root — kept separate so mPelvis ↔ mTorso splits are treated as cross-group
        ["mPelvis"] = BoneGroup.Pelvis,

        // ── Fitted-mesh collision volume bones ────────────────────────────────────
        // SL fitted mesh weights vertices to these volumes, not the regular anim bones.
        // Without entries here every collision-volume-weighted vertex is classified as
        // BoneGroup.None and incorrectly remapped to the nearest regular bone.
        // Parenting matches avatar_skeleton.xml (each volume child of the bone listed).
        // Left arm volumes (children of mCollarLeft / mShoulderLeft / mElbowLeft / mWristLeft)
        ["L_CLAVICLE"]  = BoneGroup.LeftArm,
        ["L_UPPER_ARM"] = BoneGroup.LeftArm,
        ["L_LOWER_ARM"] = BoneGroup.LeftArm,
        ["L_HAND"]      = BoneGroup.LeftArm,
        // Right arm volumes (children of mCollarRight / mShoulderRight / mElbowRight / mWristRight)
        ["R_CLAVICLE"]  = BoneGroup.RightArm,
        ["R_UPPER_ARM"] = BoneGroup.RightArm,
        ["R_LOWER_ARM"] = BoneGroup.RightArm,
        ["R_HAND"]      = BoneGroup.RightArm,
        // Left leg volumes (children of mHipLeft / mKneeLeft / mAnkleLeft)
        ["L_UPPER_LEG"] = BoneGroup.LeftLeg,
        ["L_LOWER_LEG"] = BoneGroup.LeftLeg,
        ["L_FOOT"]      = BoneGroup.LeftLeg,
        // Right leg volumes (children of mHipRight / mKneeRight / mAnkleRight)
        ["R_UPPER_LEG"] = BoneGroup.RightLeg,
        ["R_LOWER_LEG"] = BoneGroup.RightLeg,
        ["R_FOOT"]      = BoneGroup.RightLeg,
        // Torso/spine volumes (children of mTorso / mChest / mNeck / mHead)
        ["BELLY"]        = BoneGroup.UpperSpine,
        ["CHEST"]        = BoneGroup.UpperSpine,
        ["UPPER_BACK"]   = BoneGroup.UpperSpine,
        ["LOWER_BACK"]   = BoneGroup.UpperSpine,
        ["LEFT_HANDLE"]  = BoneGroup.UpperSpine,
        ["RIGHT_HANDLE"] = BoneGroup.UpperSpine,
        ["LEFT_PEC"]     = BoneGroup.UpperSpine,
        ["RIGHT_PEC"]    = BoneGroup.UpperSpine,
        ["NECK"]         = BoneGroup.UpperSpine,
        ["HEAD"]         = BoneGroup.UpperSpine,
        // Pelvis volumes (children of mPelvis)
        ["BUTT"]   = BoneGroup.Pelvis,
        ["PELVIS"] = BoneGroup.Pelvis,
    };

    /// <summary>
    /// Corrects skinning weight errors in rigged attachment meshes by snapping cross-group
    /// bone influences to the geometrically nearest bone in the correct kinematic group.
    /// <para>
    /// SL mesh exports commonly produce three classes of errors that this method fixes:
    /// (1) Cross-hemisphere arm errors (mShoulderLeft weight on a right-arm vertex).
    /// (2) Cross-group torso/pelvis splits (mTorso + mPelvis weights on a waist vertex).
    /// (3) Static-bone contamination (mChest weight on a sleeve vertex that should be arm).
    /// </para>
    /// <para>
    /// For each vertex, the geometrically nearest mobile bone (in T-pose world space) defines
    /// the "correct" kinematic group.  Any influence slot whose bone belongs to a different
    /// group is redirected to that nearest bone, eliminating inter-group tearing without
    /// disturbing within-group blending (e.g. elbow/wrist gradients are preserved).
    /// </para>
    /// </summary>
    private static void RemapCrossHemisphereWeights(
        string[]                      jointNames,
        float[]                       verts,
        int                           nv,
        int[]                         j0, int[] j1, int[] j2, int[] j3,
        Dictionary<string, Matrix4x4> tposeVpBones)
    {
        // Build T-pose world-space positions and group assignments for every joint.
        var bonePos      = new Vector3[jointNames.Length];
        var boneGroupArr = new BoneGroup[jointNames.Length];
        var activeIdx    = new List<int>(jointNames.Length);
        for (int j = 0; j < jointNames.Length; j++)
        {
            if (tposeVpBones.TryGetValue(jointNames[j], out var m))
                bonePos[j] = new Vector3(m.M41, m.M42, m.M43);
            else
                bonePos[j] = new Vector3(float.NaN, float.NaN, float.NaN);

            if (s_boneGroups.TryGetValue(jointNames[j], out var grp) && !float.IsNaN(bonePos[j].X))
            {
                boneGroupArr[j] = grp;
                activeIdx.Add(j);
            }
        }
        if (activeIdx.Count == 0) return;

        // Vertex positions in verts[] are in metres (post-BindShapeMatrix), and bonePos[]
        // positions from tposeVpBones are also in metres — compare directly.
        // 0.35 m comfortably covers the widest attachment vertices while staying clear of
        // bones belonging to a completely different body region.
        const float snapThresh = 0.35f;

        for (int vi = 0; vi < nv; vi++)
        {
            int dom = j0[vi];
            if ((uint)dom >= (uint)jointNames.Length) continue;

            var vPos = new Vector3(
                verts[vi * 8 + 0],
                verts[vi * 8 + 1],
                verts[vi * 8 + 2]);

            // Find the geometrically nearest mobile (non-static) bone in T-pose world space.
            // That bone's group defines the "correct" kinematic group for this vertex.
            int   bestBone = -1;
            float bestD2   = snapThresh * snapThresh;
            foreach (int ai in activeIdx)
            {
                float d2 = (bonePos[ai] - vPos).LengthSquared();
                if (d2 < bestD2) { bestD2 = d2; bestBone = ai; }
            }
            if (bestBone < 0) continue;   // vertex is far from all mobile bones — skip

            BoneGroup bestGroup = boneGroupArr[bestBone];
            BoneGroup domGroup  = boneGroupArr[dom];   // BoneGroup.None if dom is a static bone

            // Quick check: if every influence slot already belongs to the correct group,
            // there is nothing to fix.
            bool conflict = domGroup != bestGroup;
            if (!conflict)
            {
                if ((uint)j1[vi] < (uint)boneGroupArr.Length && boneGroupArr[j1[vi]] != bestGroup) conflict = true;
                else if ((uint)j2[vi] < (uint)boneGroupArr.Length && boneGroupArr[j2[vi]] != bestGroup) conflict = true;
                else if ((uint)j3[vi] < (uint)boneGroupArr.Length && boneGroupArr[j3[vi]] != bestGroup) conflict = true;
            }
            if (!conflict) continue;

            // Snap j0 if it is in the wrong group (domGroup ≠ bestGroup covers both the
            // static-bone case and the cross-hemisphere / cross-chain case).
            if (domGroup != bestGroup) j0[vi] = bestBone;

            // Snap every secondary slot that belongs to a different group than bestGroup.
            // Slots already in bestGroup are left alone to preserve within-chain blending.
            if ((uint)j1[vi] < (uint)boneGroupArr.Length && boneGroupArr[j1[vi]] != bestGroup) j1[vi] = bestBone;
            if ((uint)j2[vi] < (uint)boneGroupArr.Length && boneGroupArr[j2[vi]] != bestGroup) j2[vi] = bestBone;
            if ((uint)j3[vi] < (uint)boneGroupArr.Length && boneGroupArr[j3[vi]] != bestGroup) j3[vi] = bestBone;
        }
    }

    private static Matrix4x4[] ComputeFreshInvBindMatrices(
        string[]                      jointNames,
        Dictionary<string, Matrix4x4> tposeVpBones,
        Matrix4x4[]                   fallbackIBMs)
    {
        var result = new Matrix4x4[jointNames.Length];
        for (int j = 0; j < jointNames.Length; j++)
        {
            // v_bind and animBone world matrices are both in metres, so IBM = Invert(tposeVpBone).
            // No unit-conversion scale is needed here (the SL viewer's 39.37 factor belongs to
            // its internal inch-to-metre pipeline which does not apply to our coordinate space).
            result[j] = tposeVpBones.TryGetValue(jointNames[j], out var m)
                ? (Matrix4x4.Invert(m, out var inv) ? inv : Matrix4x4.Identity)
                : (fallbackIBMs != null && j < fallbackIBMs.Length ? fallbackIBMs[j] : Matrix4x4.Identity);
        }
        return result;
    }

    // ── Body mesh building ────────────────────────────────────────────────────────

    private async Task<(List<PrimRenderFace> faces, List<AvatarFaceSkinData> skinData,
                         ImmutableArray<AvatarFaceMorphData> faceMorphData,
                         Vector3 bMin, Vector3 bMax)>

        BuildBodyMeshFacesAsync(
            LindenAvatarDefinition          avatarDef,
            Avatar?                         avatarObj,
            IReadOnlyDictionary<int, float> visualParams,
            Dictionary<string, Matrix4x4>?  boneWorldMatrices,
            IProgress<string>?              progress,
            CancellationToken               ct,
            int                             lodLevel        = 0,
            Action<PrimRenderFace[], Vector3, Vector3>? onGeometryReady = null,
            uint                            avatarLocalId   = 0,
            IProgress<SceneTexturePatch>?   texturePatch    = null)
    {
        var faceData = new List<BodyFaceData>();
        var bMin     = new Vector3(float.MaxValue);
        var bMax     = new Vector3(float.MinValue);

        var morphParams      = s_cachedMorphParams      ??= TryLoadMeshMorphParams();
        var dynamicMorphSets = s_cachedDynamicMorphNames  ??= TryLoadDynamicMorphNames();

        // Select the best available LOD for each mesh type, falling back to coarser LODs
        // when the requested level is not present in the avatar definition.
        // Body-mesh LOD files follow the naming convention:
        //   lod=0 → avatar_head.llm
        //   lod=1 → avatar_head_1.llm
        //   lod=2 → avatar_head_2.llm   etc.
        // MinPixelWidth on each definition tells the SL viewer the minimum avatar on-screen
        // pixel height at which to switch to that LOD.
        var allDefs = avatarDef.MeshDefinitions;
        var lodByType = new Dictionary<string, AvatarMeshDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in allDefs)
        {
            if (string.IsNullOrEmpty(def.Type)) continue;

            // Body-mesh LOD files at level > 0 use the ReferenceMesh binary format
            // (face indices only, no vertex data), which LindenMesh.LoadMesh() cannot
            // parse.  Always select the LOD-0 full mesh for body meshes.
            if (def.LodLevel != 0) continue;

            if (!lodByType.ContainsKey(def.Type))
                lodByType[def.Type] = def;
        }

        var selectedDefs = lodByType.Values.ToList();

        // Phase 1: load and morph all body meshes concurrently, then extract face data
        // in definition order so face indices are stable.
        var faceMorphList = new List<AvatarFaceMorphData>();

        progress?.Report($"Loading {selectedDefs.Count} body mesh(es)…");

        // Kick off all mesh loads in parallel.
        var loadTasks = selectedDefs.Select((meshDef, idx) => Task.Run(() =>
        {
            var path = SysPath.Combine(
                LibreMetaverse.Settings.ResourceDir ?? string.Empty,
                "character",
                meshDef.FileName);

            if (!File.Exists(path))
                return (idx, meshDef, lm: (LindenMesh?)null);

            var m = new LindenMesh(meshDef.Type);
            m.LoadMesh(path);

            if (morphParams != null)
                ApplyBodyMeshMorphs(m, meshDef.Type, morphParams, visualParams);

            return (idx, meshDef, lm: (LindenMesh?)m);
        }, ct)).ToList();

        (int idx, AvatarMeshDefinition meshDef, LindenMesh? lm)[] loadResults;
        try
        {
            loadResults = await Task.WhenAll(loadTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Individual failures surface per-entry below.
            loadResults = loadTasks.Select(t => t.IsCompletedSuccessfully
                ? t.Result
                : default).ToArray();
        }

        ct.ThrowIfCancellationRequested();

        // Sequential extraction so ref bMin/bMax and faceData ordering are safe.
        foreach (var (_, meshDef, lm) in loadResults.OrderBy(r => r.idx))
        {
            if (lm == null) continue;

            try
            {
                var (slotIndex, bakeName) = GetBakeInfo(meshDef.Type);
                var texId = GetBakedTextureId(avatarObj, slotIndex);

                // Skirt is optional — skip if no SkirtBaked texture slot is set.
                if (meshDef.Type is "skirtMesh" && texId == UUID.Zero)
                    continue;

                var fd = ExtractBodyMeshFaceData(
                    lm, meshDef.Type, boneWorldMatrices, texId, bakeName,
                    ref bMin, ref bMax);
                if (fd != null)
                {
                    int faceIndex = faceData.Count;
                    faceData.Add(fd);

                    // Extract dynamic (animation-driven) morphs for this mesh face.
                    dynamicMorphSets.TryGetValue(meshDef.Type, out var dynNames);
                    if (dynNames != null && dynNames.Count > 0 && lm.Morphs.Length > 0)
                    {
                        var dynEntries = lm.Morphs
                            .Where(m => dynNames.Contains(m.Name))
                            .Select(m => new DynamicMorphEntry(m.Name,
                                m.Vertices.Select(mv => new FaceMorphVertex(
                                    mv.VertexIndex,
                                    new LibreMetaverse.Vector3(mv.Coord.X,  mv.Coord.Y,  mv.Coord.Z),
                                    new LibreMetaverse.Vector3(mv.Normal.X, mv.Normal.Y, mv.Normal.Z)))
                                .ToArray()))
                            .ToArray();

                        if (dynEntries.Length > 0)
                        {
                            var baseVerts = (float[])fd.Verts.Clone();
                            faceMorphList.Add(new AvatarFaceMorphData
                            {
                                FaceIndex = faceIndex,
                                BaseVerts = baseVerts,
                                WorkBuf   = new float[baseVerts.Length],
                                Morphs    = dynEntries,
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Mesh load error ({meshDef.Type}): {ex.Message}");
            }
        }

        if (faceData.Count == 0)
            return ([], [], ImmutableArray<AvatarFaceMorphData>.Empty, bMin, bMax);

        // Phase 1.5: fire geometry-ready callback with untextured (grey) faces so the
        // caller can display a placeholder while textures are still downloading.
        if (onGeometryReady != null && faceData.Count > 0)
        {
            var greyFaces = faceData.Select((fd, fi) => new PrimRenderFace
            {
                Vertices    = fd.Verts,
                Indices     = fd.Indices,
                Color       = new Vector4(0.45f, 0.45f, 0.45f, 1f),
                Transform   = Matrix4x4.Identity,
                Centroid    = fd.Centroid,
                IsTwoSided  = fd.IsTwoSided,
                AlphaCutoff = fd.AlphaCutoff,
                AlphaMode   = FaceAlphaMode.None,
                HasAlpha    = false,
                Texture     = null,
                PrimLocalId = avatarLocalId,
                FaceIndex   = fi,
            }).ToArray();
            onGeometryReady(greyFaces, bMin, bMax);
        }

        // Phase 2: download baked textures.
        // When a texturePatch callback is provided we return grey faces immediately
        // and stream each bake in the background, reporting a SceneTexturePatch as
        // each one arrives.  When no callback is provided we block until all are ready
        // so the returned faces already carry their textures (existing behaviour).
        var textures   = new Dictionary<UUID, SKBitmap?>();
        var avatarUuid = avatarObj?.ID ?? UUID.Zero;
        // For the self avatar, ObjectsAvatars may not yet have the UUID populated
        // (the entry is keyed by localID and ID can be Zero early in the session).
        // Fall back to Client.Self.AgentID which is always valid for self.
        if (avatarUuid == UUID.Zero && avatarLocalId != 0
            && avatarLocalId == client.Self.LocalID)
            avatarUuid = client.Self.AgentID;

        var unique = avatarUuid != UUID.Zero
            ? faceData
                .Where(fd => fd.TexId != UUID.Zero && fd.BakeName != null)
                .Select(fd => (TexId: fd.TexId, BakeName: fd.BakeName!))
                .DistinctBy(t => t.TexId)
                .ToList()
            : [];

        if (texturePatch != null)
        {
            // Streaming path: return grey faces now, download bakes in the background.
            // Build index maps so the background task can match texId → face indices.
            var faceTexIds = faceData.Select(fd => fd.TexId).ToArray();

            if (unique.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.WhenAll(unique.Select(async t =>
                    {
                        try
                        {
                            // Use an independent timeout — do NOT link to ct (the build
                            // token).  Avatar terse-updates cancel the build token mid-
                            // download, which drops every texture patch and leaves the
                            // avatar permanently white.  Textures belong to the *scene*,
                            // not to a single build pass; once downloaded they are valid
                            // until the avatar is removed entirely.
                            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                            // Preview callback: fire a low-res patch as soon as the first
                            // wavelet decode completes, before the full-res decode is done.
                            var previewProgress = new Progress<SKBitmap>(preview =>
                            {
                                for (int fi = 0; fi < faceTexIds.Length; fi++)
                                {
                                    if (faceTexIds[fi] == t.TexId)
                                        texturePatch.Report(new SceneTexturePatch(
                                            avatarLocalId, fi, TextureSlot.Albedo,
                                            preview.Copy(preview.ColorType)));
                                }
                            });

                            var bmp = await GridTextureHelper.DownloadServerBakedSkBitmapAsync(
                                client, avatarUuid, t.TexId, t.BakeName,
                                timeout.Token, previewProgress)
                                .ConfigureAwait(false);
                            if (bmp == null) return;

                            // Report final full-resolution patch for every face using this bake.
                            for (int fi = 0; fi < faceTexIds.Length; fi++)
                            {
                                if (faceTexIds[fi] == t.TexId)
                                    texturePatch.Report(new SceneTexturePatch(
                                        avatarLocalId, fi, TextureSlot.Albedo, bmp.Copy(bmp.ColorType)));
                            }
                        }
                        catch (OperationCanceledException) { /* timeout — face stays grey */ }
                        catch { /* network / decode errors: face stays grey */ }
                    })).ConfigureAwait(false);
                }, CancellationToken.None);
            }
        }
        else
        {
            // Blocking path (original): wait for all bakes before assembling faces.
            if (unique.Count > 0)
            {
                progress?.Report($"Downloading {unique.Count} baked texture(s)…");

                var tasks = unique.Select(t => Task.Run(async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linked  =
                        CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                    var bmp = await GridTextureHelper.DownloadServerBakedSkBitmapAsync(
                        client, avatarUuid, t.TexId, t.BakeName, linked.Token)
                        .ConfigureAwait(false);
                    // Preprocess here on the background thread (RGBA8888 convert + vertical
                    // flip) so the GL thread only issues the OpenGL upload call.
                    var processed = bmp != null ? GlTexture.Preprocess(bmp) : null;
                    lock (textures) textures[t.TexId] = processed;
                }, ct)).ToList();

                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch { /* individual failures leave a null entry in the dict */ }
            }
        }

        // Phase 3: assemble final PrimRenderFace list with downloaded textures.
        // Each face gets its own copy of the preprocessed bitmap because GlTexture's
        // constructor takes ownership and disposes after upload — sharing a single
        // instance across faces would leave the second face with a disposed/null pointer.
        var faces    = new List<PrimRenderFace>();
        var skinData = new List<AvatarFaceSkinData>();
        for (int fi = 0; fi < faceData.Count; fi++)
        {
            var fd = faceData[fi];
            SKBitmap? tex = null;
            if (fd.TexId != UUID.Zero)
            {
                SKBitmap? shared;
                lock (textures) textures.TryGetValue(fd.TexId, out shared);
                tex = shared?.Copy(shared.ColorType);
            }

            // Hair uses alpha-mask (hard discard at 0.2) to cut out the texture shape.
            // All other body mesh faces are fully opaque — baked skin textures encode
            // compositing layer data in alpha, not GL transparency.  Rendering them with
            // AlphaMode.Blend causes the alpha channel to be written into the scene FBO
            // and composited away when blitted to Avalonia's framebuffer.
            bool isHair     = fd.BakeName == "hair";
            var  alphaMode  = isHair ? FaceAlphaMode.Mask : FaceAlphaMode.None;
            bool hasAlpha   = false; // body faces always go into the opaque pass

            faces.Add(new PrimRenderFace
            {
                Vertices    = fd.Verts,
                Indices     = fd.Indices,
                Color       = Vector4.One,
                Transform   = Matrix4x4.Identity,
                Centroid    = fd.Centroid,
                IsTwoSided  = fd.IsTwoSided,
                AlphaCutoff = fd.AlphaCutoff,
                AlphaMode   = alphaMode,
                HasAlpha    = hasAlpha,
                Texture     = tex,
                PrimLocalId = avatarLocalId,
                FaceIndex   = fi,
            });

            skinData.Add(new AvatarFaceSkinData
            {
                FaceIndex = fi,
                BindVerts = fd.Verts,
                Bone1     = fd.Bone1,
                Weight1   = fd.Weight1,
                Bone2     = fd.Bone2,
                Weight2   = fd.Weight2,
            });
        }

        // Dispose the shared pre-processed bitmaps in the textures dict now that each
        // face has its own copy.
        lock (textures)
        {
            foreach (var bmp in textures.Values) bmp?.Dispose();
            textures.Clear();
        }

        return (faces, skinData, faceMorphList.ToImmutableArray(), bMin, bMax);
    }

    private static BodyFaceData? ExtractBodyMeshFaceData(
        LindenMesh                     mesh,
        string                         meshType,
        Dictionary<string, Matrix4x4>? boneWorldMatrices,
        UUID                           texId,
        string?                        bakeName,
        ref Vector3                    bMin,
        ref Vector3                    bMax)
    {
        if (mesh.NumVertices == 0 || mesh.NumFaces == 0) return null;

        // Eye ball meshes store vertices in bone-local space (a ~4 cm sphere near the
        // origin). Transform them into avatar world space using the T-pose eye bone
        // matrix so that LBS in AnimTick correctly reduces to v_local * anim_world.
        var boneName = meshType switch
        {
            "eyeBallLeftMesh"  => "mEyeLeft",
            "eyeBallRightMesh" => "mEyeRight",
            _ => null,
        };
        Matrix4x4? eyeBoneMat = null;
        if (boneName != null && boneWorldMatrices != null
                             && boneWorldMatrices.TryGetValue(boneName, out var bm))
            eyeBoneMat = bm;

        float[] verts = GC.AllocateUninitializedArray<float>(mesh.NumVertices * 8);
        for (int vi = 0; vi < mesh.NumVertices; vi++)
        {
            var v = mesh.Vertices[vi];
            int o = vi * 8;
            if (eyeBoneMat.HasValue)
            {
                var wp = Vector4.Transform(
                    new Vector4(v.Coord.X, v.Coord.Y, v.Coord.Z, 1f), eyeBoneMat.Value);
                var wn = Vector4.Transform(
                    new Vector4(v.Normal.X, v.Normal.Y, v.Normal.Z, 0f), eyeBoneMat.Value);
                verts[o + 0] = wp.X; verts[o + 1] = wp.Y; verts[o + 2] = wp.Z;
                verts[o + 3] = wn.X; verts[o + 4] = wn.Y; verts[o + 5] = wn.Z;
            }
            else
            {
                verts[o + 0] = v.Coord.X;  verts[o + 1] = v.Coord.Y;  verts[o + 2] = v.Coord.Z;
                verts[o + 3] = v.Normal.X; verts[o + 4] = v.Normal.Y; verts[o + 5] = v.Normal.Z;
            }
            verts[o + 6] = v.TexCoord.X;
            verts[o + 7] = v.TexCoord.Y;
        }

        ushort[] indices = GC.AllocateUninitializedArray<ushort>(mesh.NumFaces * 3);
        for (int fi = 0; fi < mesh.NumFaces; fi++)
        {
            indices[fi * 3 + 0] = (ushort)mesh.Faces[fi].Indices[0];
            indices[fi * 3 + 1] = (ushort)mesh.Faces[fi].Indices[1];
            indices[fi * 3 + 2] = (ushort)mesh.Faces[fi].Indices[2];
        }

        var centroidSum = Vector3.Zero;
        for (int vi = 0; vi < mesh.NumVertices; vi++)
        {
            int o  = vi * 8;
            var wp = new Vector3(verts[o], verts[o + 1], verts[o + 2]);
            bMin         = Vector3.Min(bMin, wp);
            bMax         = Vector3.Max(bMax, wp);
            centroidSum += wp;
        }
        var centroid = centroidSum * (1f / mesh.NumVertices);

        // Extract per-vertex skin weights (populated by LindenMesh.LoadMesh -> ExpandCompressedSkinWeights).
        string[] bone1, bone2;
        float[]  weight1, weight2;
        if (mesh.SkinWeights.Count == mesh.NumVertices)
        {
            bone1   = GC.AllocateUninitializedArray<string>(mesh.NumVertices);
            bone2   = GC.AllocateUninitializedArray<string>(mesh.NumVertices);
            weight1 = GC.AllocateUninitializedArray<float>(mesh.NumVertices);
            weight2 = GC.AllocateUninitializedArray<float>(mesh.NumVertices);
            for (int vi = 0; vi < mesh.NumVertices; vi++)
            {
                var sw    = mesh.SkinWeights[vi];
                bone1[vi]   = sw.Bone1;
                weight1[vi] = sw.Weight1;
                bone2[vi]   = sw.Bone2;
                weight2[vi] = sw.Weight2;
            }
        }
        else
        {
            bone1 = bone2 = [];
            weight1 = weight2 = [];
        }

        return new BodyFaceData(
            verts, indices, centroid,
            IsTwoSided:  true,
            AlphaCutoff: meshType is "hairMesh" ? 0.2f : 0.004f,
            TexId:       texId,
            BakeName:    bakeName,
            Bone1:       bone1,
            Weight1:     weight1,
            Bone2:       bone2,
            Weight2:     weight2);
    }

    // ── Attachment building ───────────────────────────────────────────────────────

    /// <summary>Per-attachment metadata used to track which bone a flexi prim follows.</summary>
    private readonly record struct AttachmentBuildInfo(
        string     JointName,
        Vector3    JointOffset,
        Quaternion JointRotation,
        uint       PrimLocalId);

    private async Task<(List<PrimRenderFace> faces, List<string> faceBonesNames,
                         List<PrimMeshBuilder.AttachmentRiggedSkin?> riggedSkins,
                         List<AttachmentBuildInfo> attachInfos,
                         Vector3 bMin, Vector3 bMax)>
        BuildAttachmentsAsync(
            uint                          avatarLocalId,
            Avatar?                       avatarObj,
            Simulator                     sim,
            Dictionary<string, Matrix4x4> boneWorldMatrices,
            IProgress<string>?            progress,
            CancellationToken             ct,
            IProgress<SceneTexturePatch>? texturePatch = null)
    {
        var allFaces       = new List<PrimRenderFace>();
        var allFaceBones   = new List<string>();
        var allRigged      = new List<PrimMeshBuilder.AttachmentRiggedSkin?>();
        var allAttachInfos = new List<AttachmentBuildInfo>();
        var bMin           = new Vector3(float.MaxValue);
        var bMax           = new Vector3(float.MinValue);

        var attachPoints = TryLoadAttachPoints();
        if (attachPoints == null) return (allFaces, allFaceBones, allRigged, allAttachInfos, bMin, bMax);

        // Root attachment prims: parent is this avatar, non-zero non-HUD attachment point.
        var rootPrims = sim.ObjectsPrimitives.Values
            .Where(p => p != null && p.ParentID == avatarLocalId)
            .Where(p =>
            {
                var ap = (int)p!.PrimData.AttachmentPoint;
                return ap > 0 && (ap < 31 || ap > 38);
            })
            .ToList();

        if (rootPrims.Count == 0) return (allFaces, allFaceBones, allRigged, allAttachInfos, bMin, bMax);

        // Build parent → children lookup for resolving linkset children.
        var primsByParent = sim.ObjectsPrimitives.Values
            .Where(p => p != null)
            .GroupBy(p => p!.ParentID)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Kick off all attachments concurrently.
        var attachTasks = rootPrims
            .Where(root =>
            {
                var apId2 = (int)root.PrimData.AttachmentPoint;
                return attachPoints.TryGetValue(apId2, out _)
                    && boneWorldMatrices.ContainsKey(attachPoints[apId2].JointName);
            })
            .Select(root => Task.Run(async () =>
            {
                var apId = (int)root.PrimData.AttachmentPoint;
                if (!attachPoints.TryGetValue(apId, out var apoint)) return default;
                if (!boneWorldMatrices.TryGetValue(apoint.JointName, out var boneMatrix)) return default;

                var attachJointMatrix = Matrix4x4.CreateFromQuaternion(apoint.Rotation)
                                      * Matrix4x4.CreateTranslation(apoint.Position)
                                      * StripScale(boneMatrix);

                var linkset = new List<Primitive> { root };
                if (primsByParent.TryGetValue(root.LocalID, out var children))
                    linkset.AddRange(children.OrderBy(p => p.LocalID));

                try
                {
                    Func<UUID, UUID> bakedResolver = id => ResolveBakedTexId(avatarObj, id);
                    var (attFaces, attRigged, aBMin, aBMax) = await _primMesher
                        .BuildAttachmentFacesAsync(linkset, attachJointMatrix, progress, ct,
                            bakedResolver,
                            rootLocalId:  root.LocalID,
                            texturePatch: texturePatch)
                        .ConfigureAwait(false);
                    return (apoint.JointName, apoint.Position, apoint.Rotation, root.LocalID,
                            attFaces, attRigged, aBMin, aBMax, ok: true);
                }
                catch (OperationCanceledException) { throw; }
                catch { return default; }
            }, ct)).ToList();

        progress?.Report($"Building {attachTasks.Count} attachment(s)…");

        (string JointName, Vector3 JointPos, Quaternion JointRot, uint RootLocalId,
         List<PrimRenderFace> attFaces, List<PrimMeshBuilder.AttachmentRiggedSkin?> attRigged,
         Vector3 aBMin, Vector3 aBMax, bool ok)[] attachResults;
        try
        {
            attachResults = await Task.WhenAll(attachTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            attachResults = attachTasks.Select(t => t.IsCompletedSuccessfully ? t.Result : default).ToArray();
        }

        foreach (var r in attachResults)
        {
            if (!r.ok) continue;
            for (int i = 0; i < r.attFaces.Count; i++)
            {
                allFaceBones.Add(r.JointName);
                allAttachInfos.Add(new AttachmentBuildInfo(r.JointName, r.JointPos, r.JointRot, r.RootLocalId));
            }
            allRigged.AddRange(r.attRigged);
            allFaces.AddRange(r.attFaces);
            if (r.aBMin.X < float.MaxValue)
            {
                bMin = Vector3.Min(bMin, r.aBMin);
                bMax = Vector3.Max(bMax, r.aBMax);
            }
        }

        return (allFaces, allFaceBones, allRigged, allAttachInfos, bMin, bMax);
    }

    // ── Bake info helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a body mesh type to the avatar TextureEntry slot index and SSB bake name.
    /// Slot indices match <see cref="AppearanceManager.AvatarTextureIndex"/>.
    /// Bake names match the values expected by <c>RequestServerBakedImage</c>.
    /// </summary>
    private static (int SlotIndex, string? BakeName) GetBakeInfo(string meshType) =>
        meshType switch
        {
            "headMesh"         => (8,  "head"),
            "eyelashMesh"      => (8,  "head"),
            "upperBodyMesh"    => (9,  "upper"),
            "lowerBodyMesh"    => (10, "lower"),
            "eyeBallLeftMesh"  => (11, "eyes"),
            "eyeBallRightMesh" => (11, "eyes"),
            "hairMesh"         => (20, "hair"),
            "skirtMesh"        => (19, "skirt"),
            _                  => (-1, null),
        };

    private static UUID GetBakedTextureId(Avatar? avatar, int slotIndex)
    {
        if (avatar?.Textures == null || slotIndex < 0) return UUID.Zero;
        // GetFace() mirrors LLVOAvatar::getTEImage(te): returns FaceTextures[slot]
        // if explicitly set, otherwise falls back to DefaultTexture.
        var texId = avatar.Textures.GetFace((uint)slotIndex).TextureID;
        // Port of LLVOAvatar::isTextureDefined (llvoavatar.cpp:11741):
        // reject IMG_DEFAULT_AVATAR and IMG_DEFAULT — both mean "no real texture".
        if (texId == UUID.Zero)                             return UUID.Zero;
        if (texId == DefaultAvatarTexture)                  return UUID.Zero;
        if (texId == Primitive.TextureEntry.WHITE_TEXTURE)  return UUID.Zero;
        return texId;
    }

    // ── Attachment point helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Parses <c>avatar_lad.xml</c> to build a mapping from attachment-point ID to
    /// joint name, default position, and default rotation.
    /// The result is cached after the first successful load.
    /// </summary>
    private Dictionary<int, AttachPoint>? TryLoadAttachPoints()
    {
        if (_attachPoints != null) return _attachPoints;
        try
        {
            var ladPath = SysPath.Combine(
                LibreMetaverse.Settings.ResourceDir ?? string.Empty,
                "character",
                "avatar_lad.xml");
            var doc    = XDocument.Load(ladPath);
            var result = new Dictionary<int, AttachPoint>();
            foreach (var el in doc.Descendants("attachment_point"))
            {
                if (!int.TryParse(el.Attribute("id")?.Value, out var id)) continue;
                var jointName = el.Attribute("joint")?.Value;
                if (string.IsNullOrEmpty(jointName)) continue;
                var pos    = ParseXmlVector3(el.Attribute("position")?.Value ?? "0 0 0");
                var rotDeg = ParseXmlVector3(el.Attribute("rotation")?.Value ?? "0 0 0");
                var rot    = Quaternion.CreateFromYawPitchRoll(
                    rotDeg.Y * (MathF.PI / 180f),
                    rotDeg.X * (MathF.PI / 180f),
                    rotDeg.Z * (MathF.PI / 180f));
                result[id] = new AttachPoint(jointName, pos, rot);
            }
            return _attachPoints = result;
        }
        catch { return null; }
    }

    private static Vector3 ParseXmlVector3(string s)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float x = parts.Length > 0 && float.TryParse(parts[0],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px) ? px : 0f;
        float y = parts.Length > 1 && float.TryParse(parts[1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var py) ? py : 0f;
        float z = parts.Length > 2 && float.TryParse(parts[2],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pz) ? pz : 0f;
        return new Vector3(x, y, z);
    }

    // ── VP morph application ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses avatar_lad.xml to build a mapping from mesh type to
    /// (paramId, morphName) pairs for VP mesh morph application.
    /// Only includes params that have a wearable= attribute (shape-driven morphs).
    /// Returns null if the file cannot be read.
    /// </summary>
    private static Dictionary<string, List<(int ParamId, string MorphName)>>?
        TryLoadMeshMorphParams()
    {
        try
        {
            var ladPath = SysPath.Combine(
                LibreMetaverse.Settings.ResourceDir ?? string.Empty,
                "character",
                "avatar_lad.xml");
            var doc    = XDocument.Load(ladPath);
            var result = new Dictionary<string, List<(int, string)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var meshEl in doc.Descendants("mesh"))
            {
                var meshType = meshEl.Attribute("type")?.Value;
                if (string.IsNullOrEmpty(meshType)) continue;
                if (!int.TryParse(meshEl.Attribute("lod")?.Value, out var lod) || lod != 0) continue;

                if (!result.TryGetValue(meshType!, out var list))
                    result[meshType!] = list = [];

                foreach (var paramEl in meshEl.Elements("param"))
                {
                    if (!int.TryParse(paramEl.Attribute("id")?.Value, out var paramId)) continue;
                    if (!paramEl.Elements("param_morph").Any()) continue;
                    // Only shape-wearable-driven params here; animation-driven params
                    // (no wearable= attribute) are handled in TryLoadDynamicMorphNames.
                    var wearable = paramEl.Attribute("wearable")?.Value;
                    if (string.IsNullOrEmpty(wearable)) continue;
                    var name = paramEl.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(name))
                        list.Add((paramId, name!));
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.DebugLog($"[VP Morph] TryLoadMeshMorphParams failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses avatar_lad.xml to find animation-driven morph param names per mesh type.
    /// These are group=1 params with no wearable= attribute — driven by BVH position keys
    /// rather than wearable visual parameter values.
    /// Returns an empty dict on parse failure.
    /// </summary>
    private static Dictionary<string, HashSet<string>> TryLoadDynamicMorphNames()
    {
        try
        {
            var ladPath = SysPath.Combine(
                LibreMetaverse.Settings.ResourceDir ?? string.Empty,
                "character",
                "avatar_lad.xml");
            var doc    = XDocument.Load(ladPath);
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var meshEl in doc.Descendants("mesh"))
            {
                var meshType = meshEl.Attribute("type")?.Value;
                if (string.IsNullOrEmpty(meshType)) continue;
                if (!int.TryParse(meshEl.Attribute("lod")?.Value, out var lod) || lod != 0) continue;

                if (!result.TryGetValue(meshType!, out var set))
                    result[meshType!] = set = new HashSet<string>(StringComparer.Ordinal);

                foreach (var paramEl in meshEl.Elements("param"))
                {
                    // Only animation-driven params (wearable attribute absent or empty).
                    var wearable = paramEl.Attribute("wearable")?.Value;
                    if (!string.IsNullOrEmpty(wearable)) continue;
                    if (!paramEl.Elements("param_morph").Any()) continue;
                    var name = paramEl.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(name))
                        set.Add(name!);
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.DebugLog($"[Dynamic Morph] TryLoadDynamicMorphNames failed: {ex.Message}");
            return new Dictionary<string, HashSet<string>>();
        }
    }

    private static void ApplyBodyMeshMorphs(
        LindenMesh                                                 mesh,
        string                                                     meshType,
        Dictionary<string, List<(int ParamId, string MorphName)>> morphParams,
        IReadOnlyDictionary<int, float>                            visualParams)
    {
        if (!morphParams.TryGetValue(meshType, out var entries)) return;

        // Normalization is applied only to hairMesh.
        // The hair .llm mesh baseline is at the minimum-morph (contracted) position, so
        // bidirectional params (e.g. Hair_Big_Front, min=-1, max=1) must be normalized via
        // (val-min)/(max-min) so their neutral raw value (0) maps to weight=0.5 (midpoint)
        // rather than 0 (which would keep the hair stuck at its minimum/flat shape).
        // Head and body .llm baselines are already at the neutral/rest pose, so applying
        // bidirectional-neutral params at 0.5 would move them away from rest — use raw values.
        // Eye meshes accumulate multiple params onto one morph causing weight overflow at 1.4+.
        bool normalizeWeights = meshType is "hairMesh";

        var weights = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (paramId, morphName) in entries)
        {
            if (!visualParams.TryGetValue(paramId, out var val))
            {
                if (!VisualParams.Params.TryGetValue(paramId, out var vpFallback)) continue;
                val = vpFallback.DefaultValue;
            }

            if (normalizeWeights && VisualParams.Params.TryGetValue(paramId, out var vpDef))
            {
                // Normalize the raw VP value to a [0, 1] morph weight, mirroring
                // LLVisualParam::setWeight in the SL C++ viewer (llviewervisualparam.cpp).
                // Without this, bidirectional params (e.g. Hair_Big_Front, min=-1 max=1)
                // have their neutral value (0) produce weight=0 instead of 0.5, making the
                // hair mesh appear collapsed to its minimum-morph shape.
                float range = vpDef.MaxValue - vpDef.MinValue;
                val = range > 1e-6f ? (val - vpDef.MinValue) / range : 0f;
                val = Math.Clamp(val, 0f, 1f);
            }

            weights[morphName] = weights.TryGetValue(morphName, out var prev) ? prev + val : val;
        }

        foreach (var morph in mesh.Morphs)
        {
            if (!weights.TryGetValue(morph.Name, out var w) || w == 0f) continue;

            foreach (var mv in morph.Vertices)
            {
                var vi = (int)mv.VertexIndex;
                if ((uint)vi >= (uint)mesh.Vertices.Length) continue;
                mesh.Vertices[vi].Coord    += mv.Coord    * w;
                mesh.Vertices[vi].Normal   += mv.Normal   * w;
                mesh.Vertices[vi].TexCoord += mv.TexCoord * w;
            }
        }
    }
}
