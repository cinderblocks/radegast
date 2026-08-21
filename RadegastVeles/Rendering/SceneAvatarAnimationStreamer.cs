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
using System.Linq;
using System.Numerics;
using LibreMetaverse;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Manages <see cref="SceneAvatarAnimator"/> instances for every nearby avatar
/// rendered by <see cref="SceneAvatarStreamer"/>.
/// Subscribes to <see cref="SceneAvatarStreamer.AvatarBuilt"/> and
/// <see cref="GridClient.Avatars.AvatarAnimation"/> to keep each animator
/// seeded with the correct active animation set.
/// </summary>
internal sealed class SceneAvatarAnimationStreamer : IDisposable
{
    private readonly GridClient          _client;
    private readonly ISceneViewport      _viewport;
    private readonly SceneAvatarStreamer _avatarStreamer;

    // avatarLocalId → active animator
    private readonly ConcurrentDictionary<uint, SceneAvatarAnimator> _animators = new();

    // Flexi animators that arrived BEFORE the matching SceneAvatarAnimator was
    // created (SceneFlexiStreamer subscribes to AvatarBuilt ahead of us, so its
    // handler fires first).  These get applied as soon as the animator exists.
    private readonly ConcurrentDictionary<uint, FlexiPrimAnimator> _pendingFlexi = new();

    // Ticks every animator in _animators from one shared timer/task instead of each one
    // owning its own -- a busy region can have dozens of avatars, and also applies
    // distance-based tick throttling (see AvatarSceneScheduler), mirroring SceneFlexiStreamer's
    // own _scheduler field exactly.
    private readonly AvatarSceneScheduler _scheduler;

    private bool _disposed;

    public SceneAvatarAnimationStreamer(GridClient client, ISceneViewport viewport,
        SceneAvatarStreamer avatarStreamer)
    {
        _client         = client;
        _viewport       = viewport;
        _avatarStreamer = avatarStreamer;
        _scheduler      = new AvatarSceneScheduler(client);
        _scheduler.Start();

        _avatarStreamer.AvatarBuilt          += OnAvatarBuilt;
        _client.Avatars.AvatarAnimation      += OnAvatarAnimation;
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Forwards an updated avatar world matrix to the flexi attachment animator
    /// for <paramref name="localId"/> so flexi prims track the avatar's position.
    /// Called by <see cref="SceneAvatarStreamer"/> on each terse avatar update.
    /// </summary>
    public void OnFlexiWorldUpdate(uint localId, Matrix4x4 world)
    {
        if (_disposed) return;
        if (_animators.TryGetValue(localId, out var anim))
            anim.UpdateAvatarWorldMatrix(world);
    }

    /// <summary>Called by the VM's KillObject handler.</summary>
    public void OnKillAvatar(Simulator sim, uint localId)
    {
        if (_disposed) return;
        if (sim != _client.Network.CurrentSim) return;
        RemoveAnimator(localId);
    }

    /// <summary>
    /// Forwards a flexi animator to the scene avatar animator for <paramref name="localId"/>
    /// so that flexi attachment prims follow the skeleton each tick.
    /// Called by <see cref="SceneFlexiStreamer"/> when an avatar's flexi submission is ready.
    /// </summary>
    public void SetFlexiAnimator(uint localId, FlexiPrimAnimator? flexi)
    {
        if (_animators.TryGetValue(localId, out var anim))
        {
            anim.SetFlexiAnimator(flexi);
            _pendingFlexi.TryRemove(localId, out _);
            return;
        }

        // Animator not built yet (event-order race with SceneFlexiStreamer).
        // Stash so OnAvatarBuilt can attach it immediately on creation.
        if (flexi != null)
        {
            _pendingFlexi[localId] = flexi;
        }
        else
            _pendingFlexi.TryRemove(localId, out _);
    }

    /// <summary>Stop all animators and clear state (sim change / viewer close).</summary>
    public void Clear()
    {
        foreach (var kv in _animators)
        {
            _scheduler.Unregister(kv.Value);
            kv.Value.Dispose();
        }
        _animators.Clear();
        _pendingFlexi.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _avatarStreamer.AvatarBuilt     -= OnAvatarBuilt;
        _client.Avatars.AvatarAnimation -= OnAvatarAnimation;
        Clear();
        _scheduler.Dispose();
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private void OnAvatarBuilt(ulong sceneKey, uint localId, AvatarBuildResult result, Matrix4x4 worldMatrix)
    {
        if (_disposed) return;

        bool hadPending = _pendingFlexi.TryRemove(localId, out var pendingFlexi);

        RemoveAnimator(localId);

        // SceneAvatarAnimator uses uint for ScheduleSceneVertexUpdate; avatar keys always
        // fit in uint (AvatarKeyOffset = 0x8000_0000, localId < 0x8000_0000).
        var animator = new SceneAvatarAnimator(_client, localId, (uint)sceneKey, _viewport, result);
        _animators[localId] = animator;

        // If a flexi animator arrived ahead of us, hook it up before registering with the
        // scheduler so the very first Tick already pushes live bone matrices into it.
        if (hadPending)
            animator.SetFlexiAnimator(pendingFlexi);

        // Seed the world matrix before registering with the scheduler, so the very first
        // tick has a correct placement for rigid-single-bone attachment faces to compose
        // against instead of Identity.
        animator.UpdateAvatarWorldMatrix(worldMatrix);

        _scheduler.Register(animator);
    }

    private void OnAvatarAnimation(object? sender, AvatarAnimationEventArgs e)
    {
        if (_disposed) return;

        // Resolve local ID from UUID.
        var sim = _client.Network.CurrentSim;
        if (sim == null) return;

        uint localId = 0;
        if (e.AvatarID == _client.Self.AgentID)
        {
            localId = _client.Self.LocalID;
        }
        else
        {
            foreach (var kv in sim.ObjectsAvatars)
            {
                if (kv.Value.ID == e.AvatarID) { localId = kv.Key; break; }
            }
        }

        if (localId == 0) return;

        if (_animators.TryGetValue(localId, out var anim))
            anim.UpdateAnimations(e.Animations.Select(a => a.AnimationID));
    }

    private void RemoveAnimator(uint localId)
    {
        if (_animators.TryRemove(localId, out var anim))
        {
            _scheduler.Unregister(anim);
            anim.Dispose();
        }
        _pendingFlexi.TryRemove(localId, out _);
    }
}
