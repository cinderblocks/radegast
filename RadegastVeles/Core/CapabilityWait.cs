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
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;

namespace Radegast.Veles.Core;

/// <summary>
/// A region hands out its capability addresses a moment after the agent arrives (login,
/// teleport, region crossing). A request made before then fails as if the region did not
/// offer the capability at all, so callers wait here first.
/// </summary>
public static class CapabilityWait
{
    private const int PollIntervalMs = 100;

    /// <summary>
    /// Waits until the current region offers <paramref name="capabilityName"/>. Returns false
    /// if it is still missing after <paramref name="timeoutMs"/>.
    /// </summary>
    public static async Task<bool> WaitForAsync(
        GridClient client, string capabilityName, int timeoutMs, CancellationToken cancellationToken = default)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (client.Network.CurrentSim?.Caps?.CapabilityURI(capabilityName) != null) return true;
            if (Environment.TickCount64 >= deadline) return false;
            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
