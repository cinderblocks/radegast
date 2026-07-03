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
using LibreMetaverse;
using LibreMetaverse.StructuredData;

namespace Radegast.Veles.Rendering;

/// <summary>Pathfinding classification for a scene object.</summary>
public enum NavMeshWalkabilityType
{
    /// <summary>No pathfinding role (e.g. phantom, or type not set).</summary>
    None,
    /// <summary>Ground that characters can walk on.</summary>
    Walkable,
    /// <summary>Solid object that blocks movement; characters path around it.</summary>
    StaticObstacle,
    /// <summary>Moving blocker (physics objects, vehicles).</summary>
    DynamicObstacle,
    /// <summary>Area characters cannot enter (exclusion zones 1/2/3).</summary>
    ExclusionZone,
}

/// <summary>
/// Downloads and caches per-object navmesh walkability data for one simulator.
/// Call <see cref="RefreshAsync"/> whenever the sim's navmesh version changes
/// (subscribe to <see cref="AgentManager.NavMeshStatusUpdate"/>).
/// After a refresh, <see cref="LocalIdTypes"/> is updated and <see cref="Changed"/>
/// fires so callers can push the new data to the viewport.
/// </summary>
internal sealed class NavMeshManager
{
    private volatile Dictionary<uint, NavMeshWalkabilityType> _types = new();

    /// <summary>Current localId → walkability type map.  Updated atomically after each refresh.</summary>
    public IReadOnlyDictionary<uint, NavMeshWalkabilityType> LocalIdTypes => _types;

    /// <summary>Last known navmesh generation status string from the server.</summary>
    public string Status { get; private set; } = "unknown";

    /// <summary>Fires on the calling thread after <see cref="RefreshAsync"/> completes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Fetches the current navmesh status and per-object walkability properties for
    /// <paramref name="sim"/>, then fires <see cref="Changed"/>.
    /// Safe to call from any thread; uses the <paramref name="client"/> HTTP caps client.
    /// </summary>
    public async Task RefreshAsync(
        GridClient client,
        Simulator  sim,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Step 1: navmesh generation status.
        var statusMsg = await client.Self
            .RequestNavMeshGenerationStatusAsync(ct)
            .ConfigureAwait(false);
        if (statusMsg != null)
            Status = statusMsg.Status;

        // Step 2: per-object properties.
        var types = new Dictionary<uint, NavMeshWalkabilityType>();
        var cap   = sim.Caps?.CapabilityURI("ObjectNavMeshProperties");

        if (cap != null)
        {
            try
            {
                var (response, data) = await client.HttpCapsClient
                    .GetAsync(cap, ct)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode && data is { Length: > 0 })
                {
                    var osd = OSDParser.Deserialize(data);
                    var uuidToType = ParseObjectProperties(osd);

                    // Correlate UUID → localId using the sim's prim cache.
                    foreach (var kvp in sim.ObjectsPrimitives)
                    {
                        if (uuidToType.TryGetValue(kvp.Value.ID, out var t))
                            types[kvp.Key] = t;
                    }
                }
                else if (!response.IsSuccessStatusCode)
                {
                    Logger.Warn(
                        $"NavMesh: ObjectNavMeshProperties returned {response.StatusCode}",
                        client);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"NavMesh: ObjectNavMeshProperties request failed: {ex.Message}",
                    client);
            }
        }

        _types = types;
        Changed?.Invoke();
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    private static Dictionary<UUID, NavMeshWalkabilityType> ParseObjectProperties(OSD osd)
    {
        var result = new Dictionary<UUID, NavMeshWalkabilityType>();

        // The response may wrap the array under "links" or "objects".
        OSDArray? arr = osd switch
        {
            OSDArray a                                              => a,
            OSDMap m when m.TryGetValue("links",   out var v) && v is OSDArray la => la,
            OSDMap m when m.TryGetValue("objects", out var v) && v is OSDArray oa => oa,
            _ => null,
        };

        if (arr == null) return result;

        foreach (var item in arr)
        {
            if (item is not OSDMap entry) continue;

            // Object UUID.
            if (!entry.TryGetValue("id", out var idOsd)) continue;
            var id = idOsd.AsUUID();
            if (id == UUID.Zero) continue;

            // Pathfinding type string — try common field names.
            string typeStr = "";
            if      (entry.TryGetValue("type",             out var t)) typeStr = t.AsString();
            else if (entry.TryGetValue("pathfinding_type", out t))     typeStr = t.AsString();

            result[id] = typeStr switch
            {
                "walkable"         => NavMeshWalkabilityType.Walkable,
                "static_obstacle"  => NavMeshWalkabilityType.StaticObstacle,
                "dynamic_obstacle" => NavMeshWalkabilityType.DynamicObstacle,
                "exclusion_1"      => NavMeshWalkabilityType.ExclusionZone,
                "exclusion_2"      => NavMeshWalkabilityType.ExclusionZone,
                "exclusion_3"      => NavMeshWalkabilityType.ExclusionZone,
                _                  => NavMeshWalkabilityType.None,
            };
        }

        return result;
    }
}
