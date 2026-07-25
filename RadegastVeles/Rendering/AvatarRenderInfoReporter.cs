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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LibreMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Reports Veles's locally-estimated avatar complexity to the current region via the
/// <c>AvatarRenderInfo</c> capability, and fetches the region's aggregate back to warn
/// the user if nearby viewers are having trouble rendering them.
/// <para>
/// Mirrors the reference SL viewer's actual behavior
/// (<c>LLAvatarRenderInfoAccountant</c>/<c>LLAvatarRenderNotifier</c>, verified against
/// the <c>secondlife/viewer</c> source): every avatar this viewer currently renders gets
/// reported — its Veles-estimated weight, and whether Veles has judged it too complex to
/// render fully (<see cref="SceneAvatarStreamer.SnapshotReportableAvatars"/>) — not just
/// the local agent. This is a crowd-sourced signal: every viewer present reports its own
/// opinion of everyone it can see, and the region aggregates it.
/// </para>
/// </summary>
internal sealed class AvatarRenderInfoReporter : IDisposable
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FetchInterval  = TimeSpan.FromSeconds(30);

    private readonly GridClient _client;
    private readonly SceneAvatarStreamer _avatarStreamer;
    private readonly RadegastInstanceAvalonia _instance;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    // Reset on sim change so the warning can fire again in a new region — not a
    // once-per-session cap, but also not re-shown every 30s while it's true.
    private bool _warnedThisSim;

    /// <summary>
    /// When false, both loops skip all work for their tick — cheap to toggle live from
    /// Preferences, no relog needed (unlike some other graphics-adjacent toggles here).
    /// </summary>
    public bool Enabled { get; set; } = true;

    public AvatarRenderInfoReporter(GridClient client, SceneAvatarStreamer avatarStreamer, RadegastInstanceAvalonia instance)
    {
        _client         = client;
        _avatarStreamer = avatarStreamer;
        _instance       = instance;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ReportLoopAsync(_cts.Token);
        _ = FetchLoopAsync(_cts.Token);
    }

    /// <summary>Call on region change so the self-facing warning can fire again in the new region.</summary>
    public void OnSimChanged() => _warnedThisSim = false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ReportLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ReportInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (!Enabled) continue;
                await ReportOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task FetchLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(FetchInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (!Enabled) continue;
                await FetchOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReportOnceAsync(CancellationToken ct)
    {
        var sim = _client.Network.CurrentSim;
        if (sim?.Caps?.CapabilityURI("AvatarRenderInfo") == null) return;

        var avatars = _avatarStreamer.SnapshotReportableAvatars();
        if (avatars.Count == 0) return;

        var agents = new Dictionary<UUID, (int Weight, bool TooComplex)>(avatars.Count);
        foreach (var (agentId, weight, tooComplex) in avatars)
            agents[agentId] = (weight, tooComplex);

        await _client.Self.PostAvatarRenderInfoAsync(agents, ct).ConfigureAwait(false);
    }

    private async Task FetchOnceAsync(CancellationToken ct)
    {
        var sim = _client.Network.CurrentSim;
        if (sim?.Caps?.CapabilityURI("AvatarRenderInfo") == null) return;

        var info = await _client.Self.GetAvatarRenderInfoAsync(ct).ConfigureAwait(false);
        if (info == null || _warnedThisSim || info.OverLimit <= 0) return;

        _warnedThisSim = true;
        var vm = NotificationViewModel.ForGenericMessage(
            "Avatar Complexity",
            $"{info.OverLimit} nearby {(info.OverLimit == 1 ? "person is" : "people are")} " +
            $"having trouble rendering you fully (region limit: {info.ReportingLimit}). " +
            "Consider simplifying your outfit if that matters to you here.");
        Dispatcher.UIThread.Post(() => _instance.RaiseNotification(vm));
    }
}
