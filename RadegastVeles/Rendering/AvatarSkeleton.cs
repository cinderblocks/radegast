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
using System.Globalization;
using System.IO;
using System.Xml;
using OpenMetaverse;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Lightweight read-only skeleton loaded from the Linden avatar definition files
/// (<c>avatar_skeleton.xml</c> and <c>avatar_lad.xml</c>).  Provides bone offsets
/// and attachment-point transforms needed to position worn attachments correctly
/// relative to the avatar origin.
/// </summary>
internal sealed class AvatarSkeleton
{
    // ── Bone hierarchy ────────────────────────────────────────────────────────────

    private sealed class Bone
    {
        public required string     Name   { get; init; }
        public required Vector3    Pos    { get; init; }
        public required Quaternion Rot    { get; init; }
        public required Vector3    Scale  { get; init; }
        public Bone?               Parent { get; set; }

        // Lazily computed cumulative transforms.
        private Vector3?    _totalOffset;
        private Quaternion? _totalRotation;

        /// <summary>
        /// Cumulative offset from the avatar root, accounting for all ancestor
        /// positions scaled by their parent's scale and rotated into world space.
        /// Mirrors legacy <c>Bone.getTotalOffset()</c>.
        /// </summary>
        public Vector3 GetTotalOffset()
        {
            if (_totalOffset.HasValue) return _totalOffset.Value;

            if (Parent != null)
            {
                var parentRot = Parent.GetTotalRotation();
                var parentOff = Parent.GetTotalOffset();
                _totalOffset = parentOff + Pos * Parent.Scale * parentRot;
            }
            else
            {
                _totalOffset = Pos;
            }

            return _totalOffset.Value;
        }

        /// <summary>
        /// Cumulative rotation from the avatar root, chaining all ancestor rotations.
        /// Mirrors legacy <c>Bone.getTotalRotation()</c>.
        /// </summary>
        public Quaternion GetTotalRotation()
        {
            if (_totalRotation.HasValue) return _totalRotation.Value;

            _totalRotation = Parent != null
                ? Parent.GetTotalRotation() * Rot
                : Rot;

            return _totalRotation.Value;
        }
    }

    // ── Attachment point ──────────────────────────────────────────────────────────

    private readonly record struct AttachmentPoint(
        int        Id,
        string     Joint,
        Vector3    Position,
        Quaternion Rotation);

    // ── Instance state ────────────────────────────────────────────────────────────

    private readonly Dictionary<string, Bone> _bones = new(StringComparer.Ordinal);
    private readonly Dictionary<int, AttachmentPoint> _attachmentPoints = new();

    // ── Factory ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the skeleton and attachment-point definitions from the standard Linden
    /// character directory (typically <c>linden/character/</c> under the output path,
    /// populated by the LibreMetaverse NuGet content files).
    /// </summary>
    public static AvatarSkeleton Load()
    {
        string basedir = Path.Combine(OpenMetaverse.Settings.RESOURCE_DIR, "character");
        return Load(basedir);
    }

    /// <summary>
    /// Loads from an explicit character-data directory.
    /// </summary>
    public static AvatarSkeleton Load(string characterDir)
    {
        var skel = new AvatarSkeleton();

        // 1. Load bone hierarchy from avatar_skeleton.xml.
        var skelDoc = new XmlDocument();
        skelDoc.Load(Path.Combine(characterDir, "avatar_skeleton.xml"));
        var root = skelDoc.GetElementsByTagName("linden_skeleton")[0];
        if (root != null)
        {
            foreach (XmlNode child in root.ChildNodes)
                skel.LoadBone(child, null);
        }

        // 2. Load attachment points from avatar_lad.xml.
        var ladDoc = new XmlDocument();
        ladDoc.Load(Path.Combine(characterDir, "avatar_lad.xml"));
        var skeletonNodes = ladDoc.GetElementsByTagName("skeleton");
        if (skeletonNodes.Count > 0)
        {
            foreach (XmlNode node in skeletonNodes[0]!.ChildNodes)
            {
                if (node.Name == "attachment_point")
                    skel.LoadAttachmentPoint(node);
            }
        }

        return skel;
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the avatar-local offset and rotation for a given attachment-point
    /// index.  The returned values account for the bone's accumulated transform
    /// plus the attachment point's own offset/rotation.  The caller should then
    /// further compose with <c>prim.Position</c> and <c>prim.Rotation</c>.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the attachment point is known; <c>false</c> otherwise
    /// (in which case <paramref name="offset"/> and <paramref name="rotation"/>
    /// are set to identity values).
    /// </returns>
    public bool TryGetAttachmentTransform(
        int            attachmentIndex,
        out Vector3    offset,
        out Quaternion rotation)
    {
        if (!_attachmentPoints.TryGetValue(attachmentIndex, out var apoint)
            || !_bones.TryGetValue(apoint.Joint, out var bone))
        {
            offset   = Vector3.Zero;
            rotation = Quaternion.Identity;
            return false;
        }

        // Bone cumulative transform.
        var boneOff = bone.GetTotalOffset();
        var boneRot = bone.GetTotalRotation();

        // Start from bone position, then add attachment-point local offset
        // rotated into bone space.
        offset   = boneOff + apoint.Position * boneRot;
        rotation = boneRot * apoint.Rotation;
        return true;
    }

    /// <summary>
    /// Returns the cumulative avatar-local offset for the named bone.
    /// </summary>
    public bool TryGetBoneOffset(string boneName, out Vector3 offset)
    {
        if (_bones.TryGetValue(boneName, out var bone))
        {
            offset = bone.GetTotalOffset();
            return true;
        }
        offset = Vector3.Zero;
        return false;
    }

    // ── Private loading helpers ───────────────────────────────────────────────────

    private void LoadBone(XmlNode node, Bone? parent)
    {
        if (node.Name != "bone") return;

        var attrs = node.Attributes;
        if (attrs == null) return;

        string name = attrs.GetNamedItem("name")!.Value!;
        var pos   = ParseVector3(attrs.GetNamedItem("pos")!.Value!);
        var rot   = ParseRotationDegrees(attrs.GetNamedItem("rot")!.Value!);
        var scale = ParseVector3(attrs.GetNamedItem("scale")!.Value!);

        var bone = new Bone
        {
            Name   = name,
            Pos    = pos,
            Rot    = rot,
            Scale  = scale,
            Parent = parent
        };

        _bones[name] = bone;

        foreach (XmlNode child in node.ChildNodes)
            LoadBone(child, bone);
    }

    private void LoadAttachmentPoint(XmlNode node)
    {
        var attrs = node.Attributes;
        if (attrs == null) return;

        int    id       = int.Parse(attrs.GetNamedItem("id")!.Value!, CultureInfo.InvariantCulture);
        string joint    = attrs.GetNamedItem("joint")!.Value!;
        var    position = ParseVector3(attrs.GetNamedItem("position")!.Value!);
        var    rotation = ParseRotationDegrees(attrs.GetNamedItem("rotation")!.Value!);

        _attachmentPoints[id] = new AttachmentPoint(id, joint, position, rotation);
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────────

    private static Vector3 ParseVector3(string s)
    {
        var parts = s.Split(s_splitChars, StringSplitOptions.RemoveEmptyEntries);
        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private static Quaternion ParseRotationDegrees(string s)
    {
        var parts = s.Split(s_splitChars, StringSplitOptions.RemoveEmptyEntries);
        float xDeg = float.Parse(parts[0], CultureInfo.InvariantCulture);
        float yDeg = float.Parse(parts[1], CultureInfo.InvariantCulture);
        float zDeg = float.Parse(parts[2], CultureInfo.InvariantCulture);
        return Quaternion.CreateFromEulers(
            xDeg * MathF.PI / 180f,
            yDeg * MathF.PI / 180f,
            zDeg * MathF.PI / 180f);
    }

    private static readonly char[] s_splitChars = [' ', '\t', ','];
}
