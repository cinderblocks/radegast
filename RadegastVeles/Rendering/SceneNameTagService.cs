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
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using OmVector3 = LibreMetaverse.Vector3;
using Vector3   = System.Numerics.Vector3;
using Vector4   = System.Numerics.Vector4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// A single name-tag entry pushed to the overlay canvas.
/// <see cref="X"/> and <see cref="Y"/> are viewport pixel coordinates of the
/// tag's anchor point (bottom-centre of the label).
/// </summary>
public sealed class NameTagItem
{
    public string Name  { get; init; } = string.Empty;
    public double X     { get; init; }
    public double Y     { get; init; }
    /// <summary>True when this tag belongs to the local agent.</summary>
    public bool   IsSelf { get; init; }
}

/// <summary>
/// A single prim hover-text entry pushed to the overlay canvas.
/// Mirrors <see cref="NameTagItem"/> but carries colour and multiline text.
/// </summary>
public sealed class HoverTextItem
{
    public string Text    { get; init; } = string.Empty;
    public double X       { get; init; }
    public double Y       { get; init; }
    /// <summary>ARGB colour specified by the prim's <c>TextColor</c> field.</summary>
    public uint   Color   { get; init; } = 0xFFFFFFFF;
}

/// <summary>
/// Runs a lightweight background loop that samples nearby avatar positions,
/// projects them into screen space using the viewport camera, and publishes
/// the result as a list of <see cref="NameTagItem"/> values.
/// The overlay canvas in <c>SceneViewerPanel.axaml</c> data-binds to
/// <see cref="SceneViewerViewModel.NameTags"/> which this service drives.
/// </summary>
internal sealed class SceneNameTagService : IDisposable
{
    private readonly GridClient        _client;
    private readonly GlViewportControl _viewport;

    // Height above avatar root position where the name tag is anchored (metres).
    private const float TagHeightOffset = 2.2f;
    // Hover text is only shown within this distance of the camera (matches legacy Radegast).
    private const float HoverTextMaxDist = 12f;

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>
    /// Raised on the thread-pool whenever the avatar name-tag list should be refreshed.
    /// The consumer (VM) must marshal to the UI thread before applying to a collection.
    /// </summary>
    public event Action<IReadOnlyList<NameTagItem>>?    TagsUpdated;

    /// <summary>
    /// Raised on the thread-pool whenever the prim hover-text list should be refreshed.
    /// </summary>
    public event Action<IReadOnlyList<HoverTextItem>>?  HoverTagsUpdated;

    public SceneNameTagService(GridClient client, GlViewportControl viewport)
    {
        _client   = client;
        _viewport = viewport;
    }

    public void Start() => _ = RunAsync(_cts.Token);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        // Refresh at ~10 Hz — smooth enough for walking avatars, cheap enough to ignore.
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                Tick();
        }
        catch (OperationCanceledException) { }
    }

    private void Tick()
    {
        if (_disposed) return;

        var sim = _client.Network.CurrentSim;
        if (sim == null) return;

        var vpBounds = _viewport.Bounds;
        double vpW = vpBounds.Width;
        double vpH = vpBounds.Height;
        if (vpW <= 0 || vpH <= 0) return;

        // Snapshot camera matrices — Camera3D is not thread-safe but these are reads
        // of value-type fields so any torn read is at worst one stale frame.
        float aspect = (float)(vpW / vpH);
        var view = _viewport.Camera.GetViewMatrix();
        var proj = _viewport.Camera.GetProjectionMatrix(aspect);
        var vp   = view * proj;

        var tags = new List<NameTagItem>();
        var selfId = _client.Self.AgentID;

        foreach (var kvp in sim.ObjectsAvatars)
        {
            var localId = kvp.Key;
            var av      = kvp.Value;
            if (av == null) continue;

            // World position: sim-local coords + tag height offset (Z-up).
            var wp = av.Position + new OmVector3(0f, 0f, TagHeightOffset);

            // Clip-space transform.
            var clip = Vector4.Transform(
                new Vector4(wp.X, wp.Y, wp.Z, 1f), vp);

            // Behind the camera — skip.
            if (clip.W <= 0f) continue;

            float ndcX =  clip.X / clip.W;
            float ndcY = -clip.Y / clip.W; // NDC Y is up, screen Y is down

            // Outside frustum — skip (with small margin for partially-visible labels).
            if (ndcX < -1.1f || ndcX > 1.1f || ndcY < -1.1f || ndcY > 1.1f) continue;

            double sx = (ndcX + 1.0) * 0.5 * vpW;
            double sy = (ndcY + 1.0) * 0.5 * vpH;

            string name = av.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = localId.ToString();

            tags.Add(new NameTagItem
            {
                Name   = name,
                X      = sx,
                Y      = sy,
                IsSelf = av.ID == selfId,
            });
        }

        TagsUpdated?.Invoke(tags);

        // ── Prim hover text ───────────────────────────────────────────────────────
        var camPos = _viewport.Camera.EyePosition;
        var camPosOmv = new Vector3(camPos.X, camPos.Y, camPos.Z);
        var hoverTags = new List<HoverTextItem>();

        foreach (var kvp in sim.ObjectsPrimitives)
        {
            var prim = kvp.Value;
            if (prim == null || string.IsNullOrEmpty(prim.Text)) continue;

            // Only show within 12 m of the camera eye position.
            var dist = Vector3.Distance(new Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z), camPosOmv);
            if (dist > HoverTextMaxDist) continue;

            // Anchor point: just above the top of the prim.
            var wp = prim.Position + new OmVector3(0f, 0f, prim.Scale.Z * 0.8f);

            var clip = Vector4.Transform(
                new Vector4(wp.X, wp.Y, wp.Z, 1f), vp);

            if (clip.W <= 0f) continue;

            float ndcX =  clip.X / clip.W;
            float ndcY = -clip.Y / clip.W;

            if (ndcX < -1.1f || ndcX > 1.1f || ndcY < -1.1f || ndcY > 1.1f) continue;

            double sx = (ndcX + 1.0) * 0.5 * vpW;
            double sy = (ndcY + 1.0) * 0.5 * vpH;

            // Normalise the text (collapse consecutive blank lines like legacy).
            var text = System.Text.RegularExpressions.Regex.Replace(
                prim.Text, @"(\r?\n){2,}", "\n");

            // Convert TextColor (0–1 floats) to ARGB uint.
            var tc = prim.TextColor;
            uint argb = ((uint)(tc.A * 255) << 24)
                      | ((uint)(tc.R * 255) << 16)
                      | ((uint)(tc.G * 255) << 8)
                      |  (uint)(tc.B * 255);

            hoverTags.Add(new HoverTextItem { Text = text, X = sx, Y = sy, Color = argb });
        }

        HoverTagsUpdated?.Invoke(hoverTags);
    }
}
