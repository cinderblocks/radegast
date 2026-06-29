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
using System.Linq;
using System.Text;
using LibreMetaverse;

namespace Veles.Plugin.ImportExport;

/// <summary>
/// Converts a parsed SL binary animation to a standard BVH text file.
/// </summary>
/// <remarks>
/// Coordinate system: SL uses X-forward, Y-left, Z-up.
/// The BVH output preserves these axes.  When importing into Blender choose
/// Forward: Y Forward, Up: Z Up.
/// Rotation order: intrinsic XYZ  (channel order Xrotation Yrotation Zrotation).
/// Positions are in metres.
/// </remarks>
internal static class BvhExporter
{
    // ── Skeleton definition ─────────────────────────────────────────────────

    private sealed class BoneNode
    {
        public readonly string Name;
        public readonly string? Parent;
        // Local position offset from parent (from avatar_skeleton.xml "pos" attribute).
        // Intermediate extended spine bones (mSpine1-4) are folded into their nearest
        // base ancestors:
        //   mPelvis → mTorso:  cumulative = (0, 0, 0.084)
        //   mTorso  → mChest:  cumulative = (-0.015, 0, 0.205)
        public readonly float Ox, Oy, Oz;
        // End-site offset (from "end" attribute) used only when this node is a leaf.
        public readonly float Ex, Ey, Ez;
        public readonly List<BoneNode> Children = [];

        public BoneNode(string name, string? parent,
                        float ox, float oy, float oz,
                        float ex, float ey, float ez)
        {
            Name = name; Parent = parent;
            Ox = ox; Oy = oy; Oz = oz;
            Ex = ex; Ey = ey; Ez = ez;
        }
    }

    // Full avatar skeleton from avatar_skeleton.xml (base + common extended).
    private static readonly BoneNode[] s_allBones =
    [
        //                        name              parent               ox       oy       oz       ex       ey       ez
        new("mPelvis",            null,              0f,      0f,      0f,      0f,      0f,      0.084f),
        new("mTorso",             "mPelvis",         0f,      0f,      0.084f, -0.015f,  0f,      0.205f),
        new("mChest",             "mTorso",         -0.015f,  0f,      0.205f, -0.010f,  0f,      0.251f),
        new("mNeck",              "mChest",         -0.010f,  0f,      0.251f,  0f,      0f,      0.076f),
        new("mHead",              "mNeck",           0f,      0f,      0.076f,  0f,      0f,      0.079f),
        // Skull / eyes (rarely animated but base bones)
        new("mSkull",             "mHead",           0f,      0f,      0.079f,  0f,      0f,      0.033f),
        new("mEyeLeft",           "mHead",           0.098f,  0.036f,  0.079f,  0.025f,  0f,      0f),
        new("mEyeRight",          "mHead",           0.098f, -0.036f,  0.079f,  0.025f,  0f,      0f),
        // Left arm
        new("mCollarLeft",        "mChest",         -0.021f,  0.085f,  0.165f,  0f,      0.079f,  0f),
        new("mShoulderLeft",      "mCollarLeft",     0f,      0.079f,  0f,      0f,      0.248f,  0f),
        new("mElbowLeft",         "mShoulderLeft",   0f,      0.248f,  0f,      0f,      0.205f,  0f),
        new("mWristLeft",         "mElbowLeft",      0f,      0.205f,  0f,      0f,      0.060f,  0f),
        // Left hand (Bento)
        new("mHandThumb1Left",    "mWristLeft",      0.031f,  0.026f,  0.004f,  0.028f,  0.032f,  0f),
        new("mHandThumb2Left",    "mHandThumb1Left", 0.028f,  0.032f, -0.001f,  0.023f,  0.031f,  0f),
        new("mHandThumb3Left",    "mHandThumb2Left", 0.023f,  0.031f, -0.001f,  0.015f,  0.025f,  0f),
        new("mHandIndex1Left",    "mWristLeft",      0.038f,  0.097f,  0.015f,  0.017f,  0.036f, -0.006f),
        new("mHandIndex2Left",    "mHandIndex1Left", 0.017f,  0.036f, -0.006f,  0.014f,  0.032f, -0.006f),
        new("mHandIndex3Left",    "mHandIndex2Left", 0.014f,  0.032f, -0.006f,  0.011f,  0.025f, -0.004f),
        new("mHandMiddle1Left",   "mWristLeft",      0.013f,  0.101f,  0.015f, -0.001f,  0.040f, -0.006f),
        new("mHandMiddle2Left",   "mHandMiddle1Left",-0.001f, 0.040f, -0.006f, -0.001f,  0.049f, -0.008f),
        new("mHandMiddle3Left",   "mHandMiddle2Left",-0.001f, 0.049f, -0.008f, -0.002f,  0.033f, -0.006f),
        new("mHandRing1Left",     "mWristLeft",     -0.010f,  0.099f,  0.009f, -0.013f,  0.038f, -0.008f),
        new("mHandRing2Left",     "mHandRing1Left", -0.013f,  0.038f, -0.008f, -0.013f,  0.040f, -0.009f),
        new("mHandRing3Left",     "mHandRing2Left", -0.013f,  0.040f, -0.009f, -0.010f,  0.028f, -0.006f),
        new("mHandPinky1Left",    "mWristLeft",     -0.031f,  0.095f,  0.003f, -0.024f,  0.025f, -0.006f),
        new("mHandPinky2Left",    "mHandPinky1Left",-0.024f,  0.025f, -0.006f, -0.015f,  0.018f, -0.004f),
        new("mHandPinky3Left",    "mHandPinky2Left",-0.015f,  0.018f, -0.004f, -0.013f,  0.016f, -0.004f),
        // Right arm
        new("mCollarRight",       "mChest",         -0.021f, -0.085f,  0.165f,  0f,     -0.079f,  0f),
        new("mShoulderRight",     "mCollarRight",    0f,     -0.079f,  0f,      0f,     -0.248f,  0f),
        new("mElbowRight",        "mShoulderRight",  0f,     -0.248f,  0f,      0f,     -0.205f,  0f),
        new("mWristRight",        "mElbowRight",     0f,     -0.205f,  0f,      0f,     -0.060f,  0f),
        // Right hand (Bento) – mirror of left
        new("mHandThumb1Right",   "mWristRight",     0.031f, -0.026f,  0.004f,  0.028f, -0.032f,  0f),
        new("mHandThumb2Right",   "mHandThumb1Right",0.028f, -0.032f, -0.001f,  0.023f, -0.031f,  0f),
        new("mHandThumb3Right",   "mHandThumb2Right",0.023f, -0.031f, -0.001f,  0.015f, -0.025f,  0f),
        new("mHandIndex1Right",   "mWristRight",     0.038f, -0.097f,  0.015f,  0.017f, -0.036f, -0.006f),
        new("mHandIndex2Right",   "mHandIndex1Right",0.017f, -0.036f, -0.006f,  0.014f, -0.032f, -0.006f),
        new("mHandIndex3Right",   "mHandIndex2Right",0.014f, -0.032f, -0.006f,  0.011f, -0.025f, -0.004f),
        new("mHandMiddle1Right",  "mWristRight",     0.013f, -0.101f,  0.015f, -0.001f, -0.040f, -0.006f),
        new("mHandMiddle2Right",  "mHandMiddle1Right",-0.001f,-0.040f,-0.006f, -0.001f, -0.049f, -0.008f),
        new("mHandMiddle3Right",  "mHandMiddle2Right",-0.001f,-0.049f,-0.008f, -0.002f, -0.033f, -0.006f),
        new("mHandRing1Right",    "mWristRight",    -0.010f, -0.099f,  0.009f, -0.013f, -0.038f, -0.008f),
        new("mHandRing2Right",    "mHandRing1Right",-0.013f, -0.038f, -0.008f, -0.013f, -0.040f, -0.009f),
        new("mHandRing3Right",    "mHandRing2Right",-0.013f, -0.040f, -0.009f, -0.010f, -0.028f, -0.006f),
        new("mHandPinky1Right",   "mWristRight",    -0.031f, -0.095f,  0.003f, -0.024f, -0.025f, -0.006f),
        new("mHandPinky2Right",   "mHandPinky1Right",-0.024f,-0.025f, -0.006f, -0.015f, -0.018f, -0.004f),
        new("mHandPinky3Right",   "mHandPinky2Right",-0.015f,-0.018f, -0.004f, -0.013f, -0.016f, -0.004f),
        // Left leg
        new("mHipLeft",           "mPelvis",         0.034f,  0.127f, -0.041f, -0.001f, -0.046f, -0.491f),
        new("mKneeLeft",          "mHipLeft",       -0.001f, -0.046f, -0.491f, -0.029f,  0.001f, -0.468f),
        new("mAnkleLeft",         "mKneeLeft",      -0.029f,  0.001f, -0.468f,  0.112f,  0f,     -0.061f),
        new("mFootLeft",          "mAnkleLeft",      0.112f,  0f,     -0.061f,  0.109f,  0f,      0f),
        new("mToeLeft",           "mFootLeft",       0.109f,  0f,      0f,      0.020f,  0f,      0f),
        // Right leg
        new("mHipRight",          "mPelvis",         0.034f, -0.129f, -0.041f, -0.001f,  0.049f, -0.491f),
        new("mKneeRight",         "mHipRight",      -0.001f,  0.049f, -0.491f, -0.029f,  0f,     -0.468f),
        new("mAnkleRight",        "mKneeRight",     -0.029f,  0f,     -0.468f,  0.112f,  0f,     -0.061f),
        new("mFootRight",         "mAnkleRight",     0.112f,  0f,     -0.061f,  0.109f,  0f,      0f),
        new("mToeRight",          "mFootRight",      0.109f,  0f,      0f,      0.020f,  0f,      0f),
    ];

    private static readonly Dictionary<string, BoneNode> s_boneMap =
        s_allBones.ToDictionary(b => b.Name, StringComparer.Ordinal);

    // Bones always included in the exported hierarchy (standard body).
    private static readonly HashSet<string> s_coreJoints = new(StringComparer.Ordinal)
    {
        "mPelvis","mTorso","mChest","mNeck","mHead",
        "mCollarLeft","mShoulderLeft","mElbowLeft","mWristLeft",
        "mCollarRight","mShoulderRight","mElbowRight","mWristRight",
        "mHipLeft","mKneeLeft","mAnkleLeft","mFootLeft","mToeLeft",
        "mHipRight","mKneeRight","mAnkleRight","mFootRight","mToeRight",
    };

    // ── Public API ──────────────────────────────────────────────────────────

    public static void Export(BinBVHAnimationReader anim, TextWriter writer)
    {
        const int fps = 30;
        float frameTime = 1f / fps;
        float span = anim.OutPoint - anim.InPoint;
        int frameCount = Math.Max(1, (int)MathF.Ceiling(span / frameTime) + 1);

        // Collect joints to include: core body + anything the animation uses
        var needed = new HashSet<string>(s_coreJoints, StringComparer.Ordinal);
        foreach (var j in anim.joints)
        {
            needed.Add(j.Name);
            // Add ancestors so the tree is connected
            var cur = j.Name;
            while (s_boneMap.TryGetValue(cur, out var info) && info.Parent != null)
            {
                if (!needed.Add(info.Parent)) break;
                cur = info.Parent;
            }
        }

        // Build tree
        var nodeMap = new Dictionary<string, BoneNode>(StringComparer.Ordinal);
        foreach (var name in needed)
        {
            if (s_boneMap.TryGetValue(name, out var proto))
                nodeMap[name] = new BoneNode(proto.Name, proto.Parent,
                                             proto.Ox, proto.Oy, proto.Oz,
                                             proto.Ex, proto.Ey, proto.Ez);
            else
                nodeMap[name] = new BoneNode(name, "mPelvis", 0f, 0f, 0f, 0f, 0f, 0.05f);
        }
        BoneNode root = nodeMap["mPelvis"];
        foreach (var node in nodeMap.Values)
        {
            if (node.Parent == null) continue;
            if (nodeMap.TryGetValue(node.Parent, out var parent))
                parent.Children.Add(node);
            else
                root.Children.Add(node); // orphan → attach to pelvis
        }

        // Depth-first visit list (used for both HIERARCHY and MOTION in same order)
        var visitOrder = new List<BoneNode>();
        CollectDF(root, visitOrder);

        // Animation lookup
        var animByName = anim.joints.ToDictionary(j => j.Name, StringComparer.Ordinal);

        // ── HIERARCHY ────────────────────────────────────────────────────
        writer.WriteLine("HIERARCHY");
        WriteJoint(writer, root, depth: 0, isRoot: true);
        writer.WriteLine();

        // ── MOTION ───────────────────────────────────────────────────────
        writer.WriteLine("MOTION");
        writer.WriteLine($"Frames: {frameCount}");
        writer.WriteLine(FormattableString.Invariant($"Frame Time: {frameTime:F6}"));

        var sb = new StringBuilder(visitOrder.Count * 24);
        for (int f = 0; f < frameCount; f++)
        {
            float t = anim.InPoint + f * frameTime;
            sb.Clear();
            bool first = true;

            foreach (var bone in visitOrder)
            {
                if (!first) sb.Append(' ');
                first = false;

                if (bone.Name == "mPelvis")
                {
                    var pos = EvalPosition(animByName, bone.Name, t);
                    var (rx, ry, rz) = EvalRotation(animByName, bone.Name, t);
                    AppendF(sb, pos.X); sb.Append(' ');
                    AppendF(sb, pos.Y); sb.Append(' ');
                    AppendF(sb, pos.Z); sb.Append(' ');
                    AppendF(sb, rx);   sb.Append(' ');
                    AppendF(sb, ry);   sb.Append(' ');
                    AppendF(sb, rz);
                }
                else
                {
                    var (rx, ry, rz) = EvalRotation(animByName, bone.Name, t);
                    AppendF(sb, rx); sb.Append(' ');
                    AppendF(sb, ry); sb.Append(' ');
                    AppendF(sb, rz);
                }
            }

            writer.WriteLine(sb.ToString());
        }
    }

    // ── Tree helpers ────────────────────────────────────────────────────────

    private static void CollectDF(BoneNode node, List<BoneNode> result)
    {
        result.Add(node);
        foreach (var c in node.Children) CollectDF(c, result);
    }

    // ── Recursive BVH HIERARCHY writer ─────────────────────────────────────

    private static void WriteJoint(TextWriter w, BoneNode node, int depth, bool isRoot)
    {
        string pad  = new string('\t', depth);
        string pad1 = pad + '\t';
        string pad2 = pad1 + '\t';

        if (isRoot)
            w.WriteLine($"ROOT {node.Name}");
        else
            w.WriteLine($"{pad}JOINT {node.Name}");

        w.WriteLine($"{pad}{{");
        w.WriteLine(FormattableString.Invariant($"{pad1}OFFSET {node.Ox:F6} {node.Oy:F6} {node.Oz:F6}"));

        if (isRoot)
            w.WriteLine($"{pad1}CHANNELS 6 Xposition Yposition Zposition Xrotation Yrotation Zrotation");
        else
            w.WriteLine($"{pad1}CHANNELS 3 Xrotation Yrotation Zrotation");

        if (node.Children.Count == 0)
        {
            w.WriteLine($"{pad1}End Site");
            w.WriteLine($"{pad1}{{");
            w.WriteLine(FormattableString.Invariant(
                $"{pad2}OFFSET {node.Ex:F6} {node.Ey:F6} {node.Ez:F6}"));
            w.WriteLine($"{pad1}}}");
        }
        else
        {
            foreach (var child in node.Children)
                WriteJoint(w, child, depth + 1, isRoot: false);
        }

        w.WriteLine($"{pad}}}");
    }

    // ── Keyframe evaluation ─────────────────────────────────────────────────

    private static Vector3 EvalPosition(Dictionary<string, binBVHJoint> byName,
                                        string bone, float t)
    {
        if (!byName.TryGetValue(bone, out var j) || j.positionkeys.Length == 0)
            return Vector3.Zero;
        var k = j.positionkeys;
        if (k.Length == 1) return k[0].key_element;
        int hi = Bracket(k, t);
        if (hi == 0)         return k[0].key_element;
        if (hi >= k.Length)  return k[^1].key_element;
        float f = Frac(k[hi-1].time, k[hi].time, t);
        return Vector3.Lerp(k[hi-1].key_element, k[hi].key_element, f);
    }

    private static (float x, float y, float z) EvalRotation(
        Dictionary<string, binBVHJoint> byName, string bone, float t)
    {
        if (!byName.TryGetValue(bone, out var j) || j.rotationkeys.Length == 0)
            return (0f, 0f, 0f);
        var k = j.rotationkeys;
        Quaternion q;
        if (k.Length == 1)
        {
            q = Decode(k[0].key_element);
        }
        else
        {
            int hi = Bracket(k, t);
            if (hi == 0)        q = Decode(k[0].key_element);
            else if (hi >= k.Length) q = Decode(k[^1].key_element);
            else
            {
                float f = Frac(k[hi-1].time, k[hi].time, t);
                q = Quaternion.Slerp(Decode(k[hi-1].key_element), Decode(k[hi].key_element), f);
            }
        }
        return ToXYZDeg(q);
    }

    private static int Bracket(binBVHJointKey[] keys, float t)
    {
        int lo = 0, hi = keys.Length;
        while (lo < hi) { int m = (lo+hi)>>1; if (keys[m].time < t) lo=m+1; else hi=m; }
        return lo;
    }

    private static float Frac(float t0, float t1, float t) =>
        t1 > t0 ? (t - t0) / (t1 - t0) : 0f;

    private static Quaternion Decode(Vector3 v)
    {
        float wSq = 1f - v.X*v.X - v.Y*v.Y - v.Z*v.Z;
        float w   = wSq > 0f ? MathF.Sqrt(wSq) : 0f;
        return Quaternion.Normalize(new Quaternion(v.X, v.Y, v.Z, w));
    }

    /// <summary>
    /// Quaternion → intrinsic XYZ Euler angles in degrees.
    /// R = Rx(α)*Ry(β)*Rz(γ), matching channel order "Xrotation Yrotation Zrotation".
    /// </summary>
    private static (float x, float y, float z) ToXYZDeg(Quaternion q)
    {
        float qx=q.X, qy=q.Y, qz=q.Z, qw=q.W;
        // sin(β) = R[0][2] = 2*(qx·qz + qy·qw)
        float sinB = Math.Clamp(2f*(qx*qz + qy*qw), -1f, 1f);
        float b = MathF.Asin(sinB);
        // α = atan2(-R[1][2], R[2][2])
        float a = MathF.Atan2(-(2f*(qy*qz - qx*qw)), 1f - 2f*(qx*qx + qy*qy));
        // γ = atan2(-R[0][1], R[0][0])
        float g = MathF.Atan2(-(2f*(qx*qy - qz*qw)), 1f - 2f*(qy*qy + qz*qz));
        const float R2D = 180f / MathF.PI;
        return (a*R2D, b*R2D, g*R2D);
    }

    private static void AppendF(StringBuilder sb, float v) =>
        sb.Append(v.ToString("F6", CultureInfo.InvariantCulture));
}
