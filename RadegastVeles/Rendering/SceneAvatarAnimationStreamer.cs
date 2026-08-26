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
        if (flexi == null)
        {
            // Detach/clear is NOT part of the AvatarBuilt race handled below -- RemoveAnimator
            // calls this whenever a flexi's own linkset goes away (e.g. an attachment being
            // detached) independent of any avatar rebuild, so it's safe (and necessary) to apply
            // directly to whatever animator is currently live, plus drop any not-yet-consumed
            // pending entry so a build that's still in flight doesn't attach a since-removed
            // flexi animator when it completes.
            if (_animators.TryGetValue(localId, out var anim))
                anim.SetFlexiAnimator(null);
            _pendingFlexi.TryRemove(localId, out _);
            return;
        }

        // Attach: this method's only non-null caller (SceneFlexiStreamer.OnAvatarBuilt) always
        // fires as a same-event race against THIS class's own OnAvatarBuilt handler for the
        // identical AvatarBuilt event (SceneFlexiStreamer subscribes first, so its handler runs
        // first -- see that class's own comment). On the very first build no _animators entry
        // exists yet, so attaching directly here used to be harmless. But on every SUBSEQUENT
        // rebuild, _animators[localId] still holds the OLD animator at the moment this runs --
        // OnAvatarBuilt below hasn't replaced it yet for THIS build cycle. Attaching directly to
        // it meant the freshly-built flexi animator got attached to an instance that was disposed
        // moments later (OnAvatarBuilt's RemoveAnimator call), and the brand-new animator
        // replacing it never received a flexi animator at all -- confirmed via
        // [FlexiAvatarBuilt]/[FlexiAnimatorAttach] logging: FlexiPrims.Length stayed non-zero on
        // every rebuild while hadPending went false starting with the second one. Always
        // stashing here removes the race entirely: OnAvatarBuilt unconditionally consumes
        // _pendingFlexi itself, after the new animator already exists.
        _pendingFlexi[localId] = flexi;
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
            // Self's animator is keyed by SceneAvatarStreamer.SelfSceneId, not the raw protocol
            // LocalID -- OnAvatarBuilt's localId parameter (used to populate _animators) already
            // carries that translation, since it comes straight from
            // SceneAvatarStreamer.AvatarBuilt's own invocation. Using the raw LocalID here means
            // this lookup always misses for self: UpdateAnimations is never called, so self's
            // animator never learns which animation is active and stays in bind pose permanently,
            // regardless of region crossings or attachments.
            localId = SceneAvatarStreamer.SelfSceneId;
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
