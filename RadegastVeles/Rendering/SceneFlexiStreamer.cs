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
using System.Collections.Concurrent;
using OpenMetaverse;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Manages <see cref="FlexiPrimAnimator"/> instances for every root prim linkset
/// in the scene that contains flexible prims.
/// <para>
/// Subscribes to <see cref="SceneObjectStreamer.ObjectBuilt"/> so that whenever
/// a linkset is (re)built a fresh animator is started for it if the submission
/// carries any <see cref="PrimRenderSubmission.FlexiPrims"/>.
/// </para>
/// </summary>
internal sealed class SceneFlexiStreamer : IDisposable
{
    private readonly GridClient        _client;
    private readonly GlViewportControl _viewport;
    private readonly SceneObjectStreamer  _objectStreamer;
    private          SceneAvatarStreamer?           _avatarStreamer;
    private          SceneAvatarAnimationStreamer?  _animationStreamer;

    // sceneKey → active animator (prim root localId for objects, AvatarKeyOffset+localId for avatars)
    // avatarLocalId → sceneKey (reverse map so SetAnimationStreamer can look up the right flexi animator)
    private readonly ConcurrentDictionary<uint, FlexiPrimAnimator> _animators  = new();
    private readonly ConcurrentDictionary<uint, uint>              _localToKey = new();

    private bool _disposed;

    public SceneFlexiStreamer(GridClient client, GlViewportControl viewport,
        SceneObjectStreamer objectStreamer)
    {
        _client        = client;
        _viewport      = viewport;
        _objectStreamer = objectStreamer;

        _objectStreamer.ObjectBuilt += OnObjectBuilt;
    }

    /// <summary>
    /// Subscribe to an avatar streamer so that flexi-prim attachments on avatars
    /// are also animated in the scene viewer.
    /// </summary>
    public void SetAvatarStreamer(SceneAvatarStreamer avatarStreamer)
    {
        if (_avatarStreamer != null)
            _avatarStreamer.AvatarBuilt -= OnAvatarBuilt;
        _avatarStreamer = avatarStreamer;
        _avatarStreamer.AvatarBuilt += OnAvatarBuilt;
    }

    /// <summary>
    /// Wire the animation streamer so that when an avatar flexi animator is created
    /// its bone provider is hooked up to the avatar's live skeleton immediately.
    /// </summary>
    public void SetAnimationStreamer(SceneAvatarAnimationStreamer animationStreamer)
        => _animationStreamer = animationStreamer;

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>Called by the VM's KillObject handler.</summary>
    public void OnKillObject(Simulator sim, uint localId)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;
        RemoveAnimator(localId);
    }

    /// <summary>Stop all animators and clear state (sim change / viewer close).</summary>
    public void Clear()
    {
        foreach (var kv in _animators)
            kv.Value.Dispose();
        _animators.Clear();
        _localToKey.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _objectStreamer.ObjectBuilt -= OnObjectBuilt;
        if (_avatarStreamer != null)
            _avatarStreamer.AvatarBuilt -= OnAvatarBuilt;
        Clear();
    }

    // ── Internal ──────────────────────────────────────────────────────────────────

    private void OnObjectBuilt(uint rootId, PrimRenderSubmission submission)
    {
        if (_disposed) return;
        StartAnimator(rootId, submission, sceneKey: rootId);
    }

    private void OnAvatarBuilt(uint sceneKey, uint localId, AvatarBuildResult result)
    {
        if (_disposed) return;
        StartAnimator(sceneKey, result.Submission, sceneKey: sceneKey, avatarLocalId: localId);

        // Seed the initial world matrix so the flexi attachment appears at the correct
        // world position from the very first tick, rather than snapping from origin
        // until the next terse avatar update arrives.
        if (_avatarStreamer != null && _animationStreamer != null)
        {
            var worldMatrix = _avatarStreamer.GetCurrentWorldMatrix(localId);
            _animationStreamer.OnFlexiWorldUpdate(localId, worldMatrix);
        }
    }

    private void StartAnimator(uint key, PrimRenderSubmission submission, uint sceneKey,
        uint avatarLocalId = 0)
    {
        if (submission.FlexiPrims.Length == 0)
        {
            RemoveAnimator(key);
            return;
        }

        RemoveAnimator(key);

        // Capture viewport reference once; the lambda keeps it alive for the animator's lifetime.
        var vp = _viewport;
        // FlexiPrimAnimator allocates exact-size buffers with new[] (not pool-rented) because
        // glBufferSubData requires the byte count to match exactly; isPoolRented is false.
        Action<int, float[]> schedule = sceneKey != 0
            ? (faceIndex, verts) => vp.ScheduleSceneVertexUpdate(sceneKey, faceIndex, verts, verts.Length, isPoolRented: false)
            : vp.ScheduleVertexUpdate;

        var animator = new FlexiPrimAnimator(submission, schedule);
        animator.Start();
        _animators[key] = animator;

        // For avatar attachments: register the reverse mapping and push the flexi
        // animator into the avatar's SceneAvatarAnimator so bone matrices flow
        // into the flexi physics each tick.
        if (avatarLocalId != 0)
        {
            _localToKey[avatarLocalId] = key;
            _animationStreamer?.SetFlexiAnimator(avatarLocalId, animator);
        }
    }

    private void RemoveAnimator(uint rootId)
    {
        if (_animators.TryRemove(rootId, out var anim))
            anim.Dispose();
        // If this was an avatar key, remove the reverse mapping.
        foreach (var kv in _localToKey)
        {
            if (kv.Value == rootId)
            {
                _localToKey.TryRemove(kv.Key, out _);
                // Clear the flexi reference from the avatar animator.
                _animationStreamer?.SetFlexiAnimator(kv.Key, null);
                break;
            }
        }
    }
}
