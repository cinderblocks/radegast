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
using SkiaSharp;
using OpenTK.Mathematics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// UV transform parameters matching the SL LLMaterial UV layout:
/// scale around (0.5, 0.5), rotate, then translate.
/// </summary>
public readonly struct UvTransform
{
    public static readonly UvTransform Default = new(0f, 0f, 1f, 1f, 0f);

    public float OffsetX  { get; }
    public float OffsetY  { get; }
    public float ScaleX   { get; }
    public float ScaleY   { get; }
    public float Rotation { get; }

    public UvTransform(float offsetX, float offsetY, float scaleX, float scaleY, float rotation)
    {
        OffsetX  = offsetX;
        OffsetY  = offsetY;
        ScaleX   = scaleX;
        ScaleY   = scaleY;
        Rotation = rotation;
    }
}

/// <summary>
/// CPU-side data for one rendered face of a prim, ready to be uploaded to the GPU.
/// Transferred from the VM thread to the GL thread via
/// <see cref="GlViewportControl.Submit"/>.
/// </summary>
public sealed class PrimRenderFace
{
    /// <summary>
    /// Interleaved floats: Position(3) + Normal(3) + TexCoord(2) per vertex.
    /// Owned by the builder, consumed by <see cref="GlMesh"/> during upload. After upload
    /// the viewport may null this out and populate <see cref="PickerVertices"/> instead
    /// to release the LOH array (5/8 of which is dead weight post-upload). Code that reads
    /// this field after upload must fall back to <see cref="PickerVertices"/>.
    /// When the buffer was rented from <see cref="System.Buffers.ArrayPool{T}"/>,
    /// <see cref="VerticesLength"/> holds the valid element count (the rented array may
    /// be larger); if zero, <see cref="Vertices"/> length should be used.
    /// </summary>
    public required float[]? Vertices   { get; set; }

    /// <summary>
    /// Number of valid floats in <see cref="Vertices"/> when it is an ArrayPool-rented
    /// buffer (which may be oversized). Zero means the full array length is valid.
    /// </summary>
    public          int      VerticesLength { get; set; }

    /// <summary>
    /// Positions-only buffer (3 floats per vertex) populated by the viewport after the
    /// interleaved <see cref="Vertices"/> buffer has been uploaded to the GPU. Used by
    /// the CPU-side picker (<c>ComputeHitInfo</c>) for ray–triangle intersection.
    /// Null until the face has been uploaded.
    /// </summary>
    public          float[]? PickerVertices { get; set; }

    /// <summary>
    /// Compact normal+UV buffer (5 floats per vertex: nx, ny, nz, u, v) extracted from
    /// the interleaved <see cref="Vertices"/> buffer at GPU upload time. Used by
    /// <c>ComputeHitInfo</c> for normal and UV interpolation on the hit triangle.
    /// Populated at the same time as <see cref="PickerVertices"/>; null until uploaded.
    /// </summary>
    public          float[]? NormalUvVertices { get; set; }

    /// <summary>Triangle indices into <see cref="Vertices"/> / <see cref="PickerVertices"/>.</summary>
    public required ushort[] Indices    { get; init; }

    /// <summary>RGBA tint color (default: white / fully opaque).</summary>
    public          Vector4  Color      { get; init; } = Vector4.One;

    public          bool     Fullbright { get; init; }
    public          float    Glow       { get; init; }

    /// <summary>If true this face is rendered in the alpha pass.</summary>
    public          bool     HasAlpha   { get; init; }

    /// <summary>LocalID of the prim this face belongs to (for picking / touch).</summary>
    public          uint     PrimLocalId { get; init; }

    /// <summary>Zero-based face index within the prim (for picking / touch).</summary>
    public          int      FaceIndex   { get; init; }

    /// <summary>
    /// Optional texture bitmap. Consumed and disposed after the GL upload;
    /// callers must not use it afterward.
    /// </summary>
    public          SKBitmap? Texture   { get; init; }

    private         Matrix4   _transform = Matrix4.Identity;
    private         Matrix3   _modelInv3;
    private         bool      _modelInv3Ready;

    public          Matrix4   Transform
    {
        get => _transform;
        set { _transform = value; _modelInv3Ready = false; }
    }

    /// <summary>
    /// Upper-left 3×3 of <c>Transform⁻¹</c>, lazily computed and cached until
    /// <see cref="Transform"/> next changes. The full normal matrix <c>(MV⁻¹)ᵀ</c> factors
    /// as <c>(V⁻¹)₃ × (M⁻¹)₃</c>; the view part is constant per frame, so caching the model
    /// part here lets the draw loop skip the per-face 4×4 invert for static geometry —
    /// only a single 3×3 multiply remains per face. Accessed on the GL thread only.
    /// </summary>
    internal Matrix3 ModelInverse3
    {
        get
        {
            if (!_modelInv3Ready)
            {
                _modelInv3 = InvertUpper3x3(in _transform);
                _modelInv3Ready = true;
            }
            return _modelInv3;
        }
    }

    /// <summary>
    /// Inverse of the upper-left 3×3 of an affine transform. For an affine matrix this
    /// equals <c>upper3x3(Matrix4.Invert(t))</c>, but is computed directly so that a
    /// degenerate prim (a flattened face with a zero-scale axis) falls back to identity
    /// instead of throwing. <see cref="Matrix4.Invert"/> raises on a singular matrix, and
    /// catching that per face every frame (degenerate faces are re-inverted whenever a
    /// terse update changes their Transform) caused load-time hitching. Computing only the
    /// 3×3 we actually need is also cheaper than a full 4×4 inverse.
    /// </summary>
    private static Matrix3 InvertUpper3x3(in Matrix4 t)
    {
        float a = t.M11, b = t.M12, c = t.M13;
        float d = t.M21, e = t.M22, f = t.M23;
        float g = t.M31, h = t.M32, i = t.M33;

        float A = e * i - f * h;
        float B = f * g - d * i;
        float C = d * h - e * g;
        float det = a * A + b * B + c * C;
        if (System.Math.Abs(det) < 1e-12f)
            return Matrix3.Identity;

        float id = 1f / det;
        return new Matrix3(
            A * id,                 (c * h - b * i) * id,   (b * f - c * e) * id,
            B * id,                 (a * i - c * g) * id,   (c * d - a * f) * id,
            C * id,                 (b * g - a * h) * id,   (a * e - b * d) * id);
    }

    /// <summary>
    /// True for faces belonging to a flexi prim.  The vertex buffer for such a face is
    /// produced each tick by <see cref="FlexiPrimAnimator"/> already in world (or
    /// avatar-local) space, so <see cref="Transform"/> must remain <see cref="Matrix4.Identity"/>.
    /// Scene-level transform overrides (root-prim terse updates / avatar world matrices)
    /// must NOT be applied to flexi faces — they would double-transform the verts and
    /// push the attachment off-screen.
    /// </summary>
    public          bool      IsFlexi    { get; set; }

    /// <summary>
    /// World-space centroid of this face, used for alpha depth sorting.
    /// For skinned faces this is the average of the post-skinning vertex positions;
    /// for rigid faces it is the average of the world-transformed vertex positions.
    /// </summary>
    public          Vector3   Centroid   { get; init; }

    /// <summary>
    /// When true, the face is rendered without back-face culling.
    /// Set for avatar body mesh faces whose winding is not guaranteed consistent
    /// (mirrors SL viewer behaviour: glDisable(GL_CULL_FACE) for the avatar body).
    /// </summary>
    public          bool      IsTwoSided { get; init; }

    /// <summary>
    /// Alpha discard threshold. Fragments with alpha below this value are discarded.
    /// Defaults to 0.004 (near-zero); hair/eyelash body meshes use 0.2 to match
    /// the SL C++ viewer's <c>glAlphaFunc(GL_GREATER, 0.2f)</c> for avatar hair.
    /// </summary>
    public          float     AlphaCutoff { get; init; } = 0.004f;

    /// <summary>
    /// Legacy TE shiny factor mapped to [0, 1]:
    /// None → 0.0, Low → 0.24, Medium → 0.64, High → 0.96.
    /// Scales both the specular exponent and the specular strength in the shader.
    /// </summary>
    public          float     Shiny       { get; init; }

    /// <summary>
    /// True when the TE face has a non-None legacy bump code (Bumpiness 1-20).
    /// Routes the face through the bump shader path which perturbs the surface
    /// normal using a screen-space TBN reconstruction of the diffuse texture.
    /// </summary>
    public          bool      HasBump     { get; init; }

    /// <summary>How the diffuse alpha channel is interpreted for this face.</summary>
    public          FaceAlphaMode AlphaMode { get; init; } = FaceAlphaMode.Blend;

    /// <summary>True when this face has an LLMaterial applied.</summary>
    public          bool        HasMaterial          { get; init; }

    /// <summary>Normal map bitmap for material faces. Consumed and disposed after GL upload.</summary>
    public          SKBitmap?   NormalMapTexture      { get; init; }

    /// <summary>Specular map bitmap for material faces. Consumed and disposed after GL upload.</summary>
    public          SKBitmap?   SpecularMapTexture    { get; init; }

    /// <summary>Specular colour tint (linear). Defaults to white.</summary>
    public          Vector4     SpecularColor         { get; init; } = Vector4.One;

    /// <summary>
    /// Specular exponent in [0, 1]; maps to shininess = max(1, exp × 255) in the shader.
    /// Derived from <c>LLMaterial.SpecularExponent / 255f</c>.
    /// </summary>
    public          float       SpecularExponent      { get; init; }

    /// <summary>
    /// Environment / Fresnel reflection intensity in [0, 1].
    /// Derived from <c>LLMaterial.EnvironmentIntensity / 255f</c>.
    /// </summary>
    public          float       EnvironmentIntensity  { get; init; }

    /// <summary>UV transform for the normal map texture.</summary>
    public          UvTransform NormalUvXform         { get; init; } = UvTransform.Default;

    /// <summary>UV transform for the specular map texture.</summary>
    public          UvTransform SpecularUvXform       { get; init; } = UvTransform.Default;

    // ── PBR (GLTF metallic-roughness) fields ─────────────────────────────────────

    /// <summary>True when this face has a GLTF PBR render material applied.</summary>
    public          bool        IsPBR                      { get; init; }

    /// <summary>Metallic-roughness ORM texture bitmap. Consumed after GL upload.</summary>
    public          SKBitmap?   MetallicRoughnessTexture   { get; init; }

    /// <summary>Emissive texture bitmap. Consumed after GL upload.</summary>
    public          SKBitmap?   EmissiveTexture            { get; init; }

    /// <summary>PBR base colour factor (linear RGBA, pre-multiplied with TE colour).</summary>
    public          Vector4     BaseColorFactor            { get; init; } = Vector4.One;

    /// <summary>Metallic factor [0, 1]. Default 1.0 per GLTF spec.</summary>
    public          float       MetallicFactor             { get; init; } = 1f;

    /// <summary>Roughness factor [0, 1]. Default 1.0 per GLTF spec.</summary>
    public          float       RoughnessFactor            { get; init; } = 1f;

    /// <summary>Emissive factor (linear RGB).</summary>
    public          Vector3     EmissiveFactor             { get; init; } = Vector3.Zero;

    /// <summary>UV transform for the PBR base colour texture.</summary>
    public          UvTransform BaseColorUvXform           { get; init; } = UvTransform.Default;

    /// <summary>UV transform for the PBR metallic-roughness texture.</summary>
    public          UvTransform MetallicRoughnessUvXform   { get; init; } = UvTransform.Default;

    /// <summary>UV transform for the PBR emissive texture.</summary>
    public          UvTransform EmissiveUvXform            { get; init; } = UvTransform.Default;

    /// <summary>UV transform for the PBR normal map texture.</summary>
    public          UvTransform PbrNormalUvXform           { get; init; } = UvTransform.Default;

    // ── Lazy local-space AABB (for frustum culling) ──────────────────────────────
    private Vector3 _localMin;
    private Vector3 _localMax;
    private int     _localAabbReady; // 0 = not computed, 1 = computed.

    /// <summary>
    /// Computes the world-space AABB by transforming the eight corners of the cached
    /// local-space AABB through <see cref="Transform"/>. The local AABB is computed
    /// on first call from <see cref="Vertices"/> and cached for the lifetime of this
    /// face. Animated faces (whose Vertices buffer is replaced via
    /// <c>GlMesh.UpdateVertices</c>) keep the original rest-pose AABB; this is the
    /// same approximation used by the SL viewer's renderable bounding volume so
    /// occasional near-pose deformation never causes false culling.
    /// </summary>
    public void GetWorldAabb(out Vector3 min, out Vector3 max)
    {
        EnsureLocalAabb();
        var t = Transform;
        // Transform 8 corners and reduce.
        Vector3 wMin = new(float.PositiveInfinity);
        Vector3 wMax = new(float.NegativeInfinity);
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3(
                (i & 1) == 0 ? _localMin.X : _localMax.X,
                (i & 2) == 0 ? _localMin.Y : _localMax.Y,
                (i & 4) == 0 ? _localMin.Z : _localMax.Z);
            var w = Vector3.TransformPosition(c, t);
            if (w.X < wMin.X) wMin.X = w.X; if (w.X > wMax.X) wMax.X = w.X;
            if (w.Y < wMin.Y) wMin.Y = w.Y; if (w.Y > wMax.Y) wMax.Y = w.Y;
            if (w.Z < wMin.Z) wMin.Z = w.Z; if (w.Z > wMax.Z) wMax.Z = w.Z;
        }
        min = wMin;
        max = wMax;
    }

    private void EnsureLocalAabb()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _localAabbReady, 1, 0) == 1)
            return;
        // Prefer PickerVertices (stride 3) if the viewport has already slimmed this face;
        // fall back to the original interleaved Vertices (stride 8) otherwise.
        var pv = PickerVertices;
        if (pv != null && pv.Length >= 3)
        {
            float pminX = float.PositiveInfinity, pminY = pminX, pminZ = pminX;
            float pmaxX = float.NegativeInfinity, pmaxY = pmaxX, pmaxZ = pmaxX;
            for (int i = 0; i + 2 < pv.Length; i += 3)
            {
                float x = pv[i], y = pv[i + 1], z = pv[i + 2];
                if (x < pminX) pminX = x; if (x > pmaxX) pmaxX = x;
                if (y < pminY) pminY = y; if (y > pmaxY) pmaxY = y;
                if (z < pminZ) pminZ = z; if (z > pmaxZ) pmaxZ = z;
            }
            _localMin = new Vector3(pminX, pminY, pminZ);
            _localMax = new Vector3(pmaxX, pmaxY, pmaxZ);
            return;
        }
        var v = Vertices;
        if (v == null || v.Length < 3)
        {
            _localMin = Vector3.Zero;
            _localMax = Vector3.Zero;
            return;
        }
        float minX = float.PositiveInfinity, minY = minX, minZ = minX;
        float maxX = float.NegativeInfinity, maxY = maxX, maxZ = maxX;
        // Stride is 8 floats (pos3 + nrm3 + uv2); positions at offset 0.
        for (int i = 0; i + 2 < v.Length; i += 8)
        {
            float x = v[i], y = v[i + 1], z = v[i + 2];
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }
        _localMin = new Vector3(minX, minY, minZ);
        _localMax = new Vector3(maxX, maxY, maxZ);
    }

    /// <summary>
    /// Returns a copy of this face with <paramref name="worldTranslation"/> applied on top of
    /// the existing <see cref="Transform"/> (i.e. the translation is the outermost operation
    /// so the object moves to its world-space position while retaining its local rotation/scale).
    /// Bitmaps are <em>not</em> cloned — the caller is responsible for ensuring the original
    /// face is no longer used after this call.
    /// </summary>
    internal PrimRenderFace WithWorldTranslation(Vector3 worldTranslation)
    {
        var newTransform = Transform * Matrix4.CreateTranslation(worldTranslation);
        var newCentroid  = Centroid + worldTranslation;
        return WithWorldTransform(newTransform, newCentroid);
    }

    /// <summary>
    /// Returns a copy of this face with the supplied pre-built world matrix and centroid.
    /// All other properties (textures, material, etc.) are shared by reference.
    /// Bitmaps are <em>not</em> cloned.
    /// </summary>
    internal PrimRenderFace WithWorldTransform(Matrix4 worldTransform, Vector3 worldCentroid)
    {
        return new PrimRenderFace
        {
            Vertices                   = Vertices,
            PickerVertices             = PickerVertices,
            Indices                    = Indices,
            Color                      = Color,
            Fullbright                 = Fullbright,
            Glow                       = Glow,
            HasAlpha                   = HasAlpha,
            PrimLocalId                = PrimLocalId,
            FaceIndex                  = FaceIndex,
            Texture                    = Texture,
            Transform                  = worldTransform,
            IsFlexi                    = IsFlexi,
            Centroid                   = worldCentroid,
            IsTwoSided                 = IsTwoSided,
            AlphaCutoff                = AlphaCutoff,
            Shiny                      = Shiny,
            HasBump                    = HasBump,
            AlphaMode                  = AlphaMode,
            HasMaterial                = HasMaterial,
            NormalMapTexture           = NormalMapTexture,
            SpecularMapTexture         = SpecularMapTexture,
            SpecularColor              = SpecularColor,
            SpecularExponent           = SpecularExponent,
            EnvironmentIntensity       = EnvironmentIntensity,
            NormalUvXform              = NormalUvXform,
            SpecularUvXform            = SpecularUvXform,
            IsPBR                      = IsPBR,
            MetallicRoughnessTexture   = MetallicRoughnessTexture,
            EmissiveTexture            = EmissiveTexture,
            BaseColorFactor            = BaseColorFactor,
            MetallicFactor             = MetallicFactor,
            RoughnessFactor            = RoughnessFactor,
            EmissiveFactor             = EmissiveFactor,
            BaseColorUvXform           = BaseColorUvXform,
            MetallicRoughnessUvXform   = MetallicRoughnessUvXform,
            EmissiveUvXform            = EmissiveUvXform,
            PbrNormalUvXform           = PbrNormalUvXform,
        };
    }
}

/// <summary>
/// Alpha rendering mode for a prim face.
/// Mirrors the SL LLMaterial diffuse-alpha-mode values; for legacy TE faces
/// without LLMaterial the mode is inferred from the TE colour alpha.
/// </summary>
public enum FaceAlphaMode : byte
{
    /// <summary>Alpha channel is ignored; face is always fully opaque.</summary>
    None     = 0,
    /// <summary>Standard alpha blending (src_alpha, one_minus_src_alpha).</summary>
    Blend    = 1,
    /// <summary>Alpha masking: fragments below <see cref="PrimRenderFace.AlphaCutoff"/> are discarded.</summary>
    Mask     = 2,
    /// <summary>Alpha channel drives additive emission rather than transparency.</summary>
    Emissive = 3,
}

/// <summary>
/// Describes the base (undeformed) vertex data for a single flexi prim in a linkset submission,
/// along with the range of global face indices in <see cref="PrimRenderSubmission.Faces"/> that
/// belong to it. Used by <c>FlexiPrimAnimator</c> to update vertices each simulation tick.
/// </summary>
public sealed class FlexiPrimInfo
{
    /// <summary>The prim whose <see cref="OpenMetaverse.Primitive.Flexible"/> data drives simulation.</summary>
    public required OpenMetaverse.Primitive Prim { get; init; }

    /// <summary>
    /// Index of the first face in <see cref="PrimRenderSubmission.Faces"/> that belongs to this prim.
    /// </summary>
    public required int FaceStart { get; init; }

    /// <summary>Number of faces belonging to this prim.</summary>
    public required int FaceCount { get; init; }

    /// <summary>
    /// Base (rest-pose / undeformed) vertex arrays, one per face, in the same layout as
    /// <see cref="PrimRenderFace.Vertices"/>: Position(3) + Normal(3) + UV(2) per vertex.
    /// These are copied at build time and must not be mutated.
    /// </summary>
    public required float[][] BaseVertices { get; init; }

    /// <summary>
    /// Number of path segments along the prim's Z axis — derived from the mesh path node count.
    /// </summary>
    public required int PathSegments { get; init; }

    /// <summary>
    /// Number of profile vertices per path step (the "ring" cross-section vertex count).
    /// Used to stride through vertices when applying segment transforms.
    /// </summary>
    public required int ProfileVertexCount { get; init; }

    /// <summary>
    /// The prim's scale baked into the base vertices — needed to rebuild per-segment transforms.
    /// </summary>
    public required OpenMetaverse.Vector3 Scale { get; init; }

    /// <summary>
    /// The prim's own Scale × Rotation × Translation matrix, independent of the skeleton.
    /// For stand-alone scene prims this equals <see cref="AttachTransform"/>.
    /// For attachment prims this is the prim-local part that must prefix the live
    /// attachment joint matrix each tick:
    /// <c>live_attachTx = PrimLocalMatrix × SlotRotation × SlotOffset × StripScale(liveBone)</c>
    /// </summary>
    public OpenTK.Mathematics.Matrix4 PrimLocalMatrix { get; init; } = OpenTK.Mathematics.Matrix4.Identity;

    /// <summary>
    /// Transform applied to the deformed spine positions to bring them from prim-local space
    /// into the coordinate space expected by the renderer (world / avatar-local).
    /// <para>
    /// For stand-alone scene prims this bakes the prim's scale+rotation and is fixed.
    /// For attachment prims this is the <em>static</em> T-pose fallback only — at runtime
    /// the animator recomputes the matrix each tick via <see cref="AttachJointName"/> and
    /// <see cref="AttachBoneProvider"/> so the flexi prim follows the animated skeleton.
    /// </para>
    /// </summary>
    public OpenTK.Mathematics.Matrix4 AttachTransform { get; set; } = OpenTK.Mathematics.Matrix4.Identity;

    /// <summary>
    /// Additional transform applied <em>after</em> <see cref="AttachTransform"/> (or its
    /// dynamic per-tick recomputation when a live bone provider is present).
    /// <para>
    /// Used to push the prim from its local/avatar coordinate space into the renderer's
    /// world space without being clobbered when the dynamic-attachment branch rebuilds
    /// <c>attachTx</c> from <see cref="PrimLocalMatrix"/> and the live bone matrix:
    /// </para>
    /// <list type="bullet">
    ///   <item>Stand-alone scene prims (SceneViewer): world translation of the linkset root.</item>
    ///   <item>Avatar attachments (SceneViewer): avatar world matrix.</item>
    ///   <item>Avatar attachments (PrimViewer / AvatarViewer): Identity.</item>
    /// </list>
    /// Default is Identity (no extra transform).
    /// </summary>
    public OpenTK.Mathematics.Matrix4 ExternalTransform { get; set; } = OpenTK.Mathematics.Matrix4.Identity;

    // ── Live attachment tracking (avatar attachments only) ────────────────────

    /// <summary>
    /// Name of the avatar bone (joint) this attachment is parented to.
    /// Null for stand-alone scene prims.
    /// </summary>
    public string? AttachJointName { get; init; }

    /// <summary>
    /// Default rotation of the attachment point slot (from avatar_lad.xml).
    /// Combined with <see cref="AttachJointOffset"/> and the live bone matrix to
    /// produce the per-tick transform.
    /// </summary>
    public OpenTK.Mathematics.Quaternion AttachJointRotation { get; init; }

    /// <summary>
    /// Default positional offset of the attachment point slot (from avatar_lad.xml).
    /// </summary>
    public OpenTK.Mathematics.Vector3 AttachJointOffset { get; init; }

    /// <summary>
    /// Live bone-matrix provider: given a joint name returns the current animated
    /// world matrix for that bone.  Set by the avatar viewer each tick so the
    /// flexi animator can keep the attachment planted on the moving skeleton.
    /// Null for stand-alone scene prims (static <see cref="AttachTransform"/> used).
    /// </summary>
    public Func<string, OpenTK.Mathematics.Matrix4>? AttachBoneProvider { get; set; }
}

/// <summary>
/// A complete set of faces for one object (prim or linkset), keyed by a caller-supplied label.
/// Submitting a new <see cref="PrimRenderSubmission"/> replaces any existing geometry.
/// </summary>
public sealed class PrimRenderSubmission
{
    /// <summary>Unique label — used for window title and future cache keying.</summary>
    public required string           Label     { get; init; }

    public required PrimRenderFace[] Faces     { get; init; }

    /// <summary>AABB minimum corner in object space (for auto-framing).</summary>
    public          Vector3 BoundsMin { get; init; } = Vector3.Zero;

    /// <summary>AABB maximum corner in object space (for auto-framing).</summary>
    public          Vector3 BoundsMax { get; init; } = Vector3.One;

    /// <summary>
    /// Flexi prim simulation descriptors — one entry per flexi prim in the linkset.
    /// Empty when the linkset contains no flexi prims.
    /// </summary>
    public          FlexiPrimInfo[]  FlexiPrims { get; init; } = [];
}

/// <summary>
/// Identifies which texture slot of a <see cref="PrimRenderFace"/> a streamed
/// bitmap should be patched into.
/// </summary>
public enum TextureSlot : byte
{
    /// <summary>Diffuse / PBR base-colour texture.</summary>
    Albedo             = 0,
    /// <summary>Legacy material normal map / PBR normal map.</summary>
    Normal             = 1,
    /// <summary>Legacy material specular map.</summary>
    Specular           = 2,
    /// <summary>PBR metallic-roughness (ORM) map.</summary>
    MetallicRoughness  = 3,
    /// <summary>PBR emissive texture.</summary>
    Emissive           = 4,
}

/// <summary>
/// Carries a single decoded bitmap that should be uploaded and stitched into an
/// already-live scene-object face.  Produced by <see cref="PrimMeshBuilder"/> as
/// each texture download completes and consumed by
/// <see cref="GlViewportControl.PatchSceneObjectTexture"/>.
/// </summary>
/// <param name="RootLocalId">
///   The root prim LocalID passed to <see cref="GlViewportControl.SubmitSceneObject"/>.
/// </param>
/// <param name="FaceIndex">
///   Zero-based index into <see cref="PrimRenderSubmission.Faces"/> for the target face.
/// </param>
/// <param name="Slot">Which texture slot to fill.</param>
/// <param name="Bitmap">
///   The decoded bitmap.  Ownership transfers to the viewport; it will be disposed
///   after the GPU upload.
/// </param>
public sealed record SceneTexturePatch(
    uint        RootLocalId,
    int         FaceIndex,
    TextureSlot Slot,
    SKBitmap?   Bitmap);
