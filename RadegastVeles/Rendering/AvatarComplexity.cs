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
using LibreMetaverse;
using Vector4 = System.Numerics.Vector4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// How an avatar should be rendered given its estimated local render cost
/// relative to <see cref="SceneAvatarStreamer.ComplexityThreshold"/>.
/// </summary>
internal enum AvatarRenderTier
{
    /// <summary>Full body mesh + attachments + baked textures.</summary>
    Full,
    /// <summary>Body mesh only, flat identity color, no attachments/textures — still skinned/animated.</summary>
    Silhouette,
    /// <summary>No body mesh at all — a colored particle cloud, same as the loading-time placeholder.</summary>
    Cloud
}

/// <summary>
/// Estimates a Veles-local "render cost" for an avatar from <see cref="Primitive"/>
/// metadata already resident in <see cref="Simulator.ObjectsPrimitives"/> — deliberately
/// never triggers an asset fetch or mesh decode, since the whole point of tiering is to
/// avoid paying the cost of building a bloated avatar, not to measure it after the fact.
/// <para>
/// This is Veles's own weighting, not a port of Second Life's Avatar Rendering Cost:
/// SL's ARC has no notion of Veles-specific costs like real-time shadow cubemaps per
/// point light or CPU flexi simulation, so those are weighted here explicitly (see
/// <c>project_veles_shadows</c>/<c>project_veles_flexi_review</c> history) — a plain
/// Second Life ARC number would systematically undercount what actually costs Veles.
/// </para>
/// </summary>
internal static class AvatarComplexityEstimator
{
    // Named coefficients in Veles Complexity Points (VCP) — starting values pending
    // in-world tuning against real profiling data, not validated against measured
    // frame cost yet. Adjust here if a specific feature turns out to be mis-weighted.
    public const float AvatarBaseCost  = 5.0f;  // every avatar: body mesh + bake composite
    public const float PrimBaseCost    = 1.0f;  // per attachment prim: submission/draw-call overhead
    public const float TextureCost     = 0.5f;  // per distinct TextureID referenced by a prim's faces
    public const float SculptCost      = 3.0f;  // sculpted (non-mesh) prim: bounded generated triangle count
    public const float MeshUnknownCost = 8.0f;  // mesh prim, real triangle count unknown pre-decode
    public const float MeshVertexNormalizer = 500f; // VCP per 500 real vertices, used only when already cached
    public const float FlexiCost       = 6.0f;  // Veles-specific: CPU simulation cost (FlexiSceneScheduler)
    public const float LightCost       = 10.0f; // Veles-specific: contends for the 2 shadow-cubemap slots
    public const float ParticleCost    = 4.0f;  // particle emitter GPU buffer cost

    /// <summary>
    /// Estimates the render cost of the avatar at <paramref name="avatarLocalId"/> from
    /// its currently-known attachments. Cheap: one pass over already-resident
    /// <see cref="Primitive"/> objects, no network I/O.
    /// </summary>
    public static float EstimateCost(Simulator sim, uint avatarLocalId)
    {
        float cost = AvatarBaseCost;

        // Root attachment prims: parent is this avatar, non-zero non-HUD attachment
        // point. Mirrors AvatarMeshBuilder.BuildAttachmentsAsync's exact filter so the
        // estimate covers precisely what a full build would actually render.
        var rootPrims = new List<Primitive>();
        foreach (var p in sim.ObjectsPrimitives.Values)
        {
            if (p == null || p.ParentID != avatarLocalId) continue;
            var ap = (int)p.PrimData.AttachmentPoint;
            if (ap > 0 && (ap < 31 || ap > 38)) rootPrims.Add(p);
        }
        if (rootPrims.Count == 0) return cost;

        // Bucket only linkset children of THIS avatar's attachment roots (which also
        // render, and may themselves be flexible/lit/particle-emitting) — deliberately
        // NOT a sim-wide GroupBy of every prim by ParentID. AvatarMeshBuilder.
        // BuildAttachmentsAsync does use a sim-wide GroupBy for the equivalent lookup,
        // but that only runs once per actual build; this runs once per tier check
        // (called from SceneAvatarStreamer.EnqueueBuild, a much hotter path — every
        // avatar dirty-settle, not just real appearance changes), so allocating a
        // dictionary entry for every distinct parent in the whole sim here would scale
        // with total sim prim count instead of just this avatar's attachment count.
        var rootIds = new HashSet<uint>(rootPrims.Count);
        foreach (var root in rootPrims) rootIds.Add(root.LocalID);

        var childrenByRoot = new Dictionary<uint, List<Primitive>>(rootPrims.Count);
        foreach (var p in sim.ObjectsPrimitives.Values)
        {
            if (p == null || !rootIds.Contains(p.ParentID)) continue;
            if (!childrenByRoot.TryGetValue(p.ParentID, out var list))
                childrenByRoot[p.ParentID] = list = new List<Primitive>();
            list.Add(p);
        }

        foreach (var root in rootPrims)
        {
            cost += CostOfPrim(root);
            if (childrenByRoot.TryGetValue(root.LocalID, out var children))
                foreach (var child in children)
                    cost += CostOfPrim(child);
        }
        return cost;
    }

    private static float CostOfPrim(Primitive p)
    {
        float c = PrimBaseCost;

        // Distinct texture IDs across faces — cheap metadata read (TextureEntry), no decode.
        var distinct = new HashSet<UUID>();
        if (p.Textures != null)
        {
            if (p.Textures.DefaultTexture != null)
                distinct.Add(p.Textures.DefaultTexture.TextureID);
            if (p.Textures.FaceTextures != null)
                foreach (var f in p.Textures.FaceTextures)
                    if (f != null) distinct.Add(f.TextureID);
        }
        c += distinct.Count * TextureCost;

        if (p.Sculpt != null && p.Sculpt.Type == SculptType.Mesh)
        {
            c += PrimMeshBuilder.TryPeekCachedMeshVertexCount(p.Sculpt.SculptTexture, out int vcount)
                ? vcount / MeshVertexNormalizer
                : MeshUnknownCost;
        }
        else if (p.Sculpt != null && p.Sculpt.Type != SculptType.None)
        {
            c += SculptCost;
        }

        // Matches the exact "is this prim flexible" convention used elsewhere in this
        // codebase (AvatarMeshBuilder.cs, PrimMeshBuilder.cs) — Flexible alone isn't a
        // reliable null-check, PathCurve is the actual discriminator.
        if (p.Flexible != null && p.PrimData.PathCurve == PathCurve.Flexible)
            c += FlexiCost;

        if (p.Light != null)
            c += LightCost;

        if (p.ParticleSys.Pattern != Primitive.ParticleSystem.SourcePattern.None
            || p.ParticleSys.BurstPartCount > 0)
            c += ParticleCost;

        return c;
    }
}

/// <summary>
/// Deterministic per-avatar identity color for silhouette/cloud impostors — lets
/// separate anonymized avatars be told apart in a crowd (mirrors SL jellydoll behavior)
/// without revealing anything about what the wearer actually looks like.
/// </summary>
internal static class AvatarIdentityColor
{
    public static Vector4 FromUuid(UUID id)
    {
        // FNV-1a 32-bit — same construction as SceneAvatarStreamer.ComputeVisualParamHash.
        uint hash = 2166136261u;
        foreach (byte b in id.GetBytes())
            hash = (hash ^ b) * 16777619u;

        float hue = (hash % 360u) / 360f;
        var (r, g, b2) = HsvToRgb(hue, 0.65f, 0.85f);
        return new Vector4(r, g, b2, 1f);
    }

    private static (float R, float G, float B) HsvToRgb(float h, float s, float v)
    {
        float i = MathF.Floor(h * 6f);
        float f = h * 6f - i;
        float p = v * (1f - s);
        float q = v * (1f - f * s);
        float t = v * (1f - (1f - f) * s);
        return ((int)i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }
}
