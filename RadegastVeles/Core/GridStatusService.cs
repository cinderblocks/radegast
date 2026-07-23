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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Radegast.Veles.Core;

public enum GridStatusIndicator { None, Minor, Major, Critical, Maintenance, Unknown }

public sealed class IncidentSummary
{
    public string Name { get; init; } = string.Empty;
    public GridStatusIndicator Impact { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public string LatestUpdateBody { get; init; } = string.Empty;
    public bool IsUnresolved => ResolvedAt == null;
}

public sealed class GridStatusSnapshot
{
    public GridStatusIndicator Indicator { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<IncidentSummary> Recent { get; init; } = [];
    public IncidentSummary? ActiveIncident => Recent.FirstOrDefault(i => i.IsUnresolved);
}

/// <summary>
/// Reads the public Statuspage.io API for status.secondlifegrid.net. Never throws:
/// any failure degrades to the last good cache (or null on first run).
/// </summary>
public static class GridStatusService
{
    private const string SummaryUrl = "https://status.secondlifegrid.net/api/v2/summary.json";
    private const string IncidentsUrl = "https://status.secondlifegrid.net/api/v2/incidents.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(3);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    static GridStatusService()
    {
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadegastVeles", AppVersionInfo.CurrentVersionString.TrimStart('v')));
    }

    private static GridStatusSnapshot? _cache;
    private static DateTimeOffset _cacheTime;

    public static async Task<GridStatusSnapshot?> GetStatusAsync()
    {
        if (_cache != null && DateTimeOffset.UtcNow - _cacheTime < CacheTtl)
            return _cache;

        try
        {
            var summaryJson = await Http.GetStringAsync(SummaryUrl);
            var incidentsJson = await Http.GetStringAsync(IncidentsUrl);

            var summary = JsonSerializer.Deserialize<StatuspageSummary>(summaryJson);
            var incidents = JsonSerializer.Deserialize<StatuspageIncidents>(incidentsJson);

            if (summary == null) return _cache;

            var cutoff = DateTimeOffset.UtcNow - RecentWindow;
            var recent = (incidents?.Incidents ?? [])
                .Select(ToSummary)
                .Where(i => i.IsUnresolved || i.CreatedAt >= cutoff)
                .OrderByDescending(i => i.CreatedAt)
                .Take(5)
                .ToList();

            var snapshot = new GridStatusSnapshot
            {
                Indicator = ParseIndicator(summary.Status?.Indicator),
                Description = summary.Status?.Description ?? "Unknown",
                Recent = recent
            };

            _cache = snapshot;
            _cacheTime = DateTimeOffset.UtcNow;
            return snapshot;
        }
        catch
        {
            return _cache;
        }
    }

    private static IncidentSummary ToSummary(StatuspageIncident incident)
    {
        var latestBody = incident.IncidentUpdates?.OrderByDescending(u => u.CreatedAt).FirstOrDefault()?.Body
                          ?? string.Empty;
        return new IncidentSummary
        {
            Name = incident.Name ?? "Incident",
            Impact = ParseIndicator(incident.Impact),
            CreatedAt = incident.CreatedAt,
            ResolvedAt = incident.ResolvedAt,
            LatestUpdateBody = latestBody
        };
    }

    private static GridStatusIndicator ParseIndicator(string? value) => value?.ToLowerInvariant() switch
    {
        "none" => GridStatusIndicator.None,
        "minor" => GridStatusIndicator.Minor,
        "major" => GridStatusIndicator.Major,
        "critical" => GridStatusIndicator.Critical,
        "maintenance" => GridStatusIndicator.Maintenance,
        _ => GridStatusIndicator.Unknown
    };

    // Minimal subsets of the Statuspage.io v2 API schema - only the fields we use.
    private sealed class StatuspageSummary
    {
        [JsonPropertyName("status")] public StatuspageStatus? Status { get; set; }
    }

    private sealed class StatuspageStatus
    {
        [JsonPropertyName("indicator")] public string? Indicator { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class StatuspageIncidents
    {
        [JsonPropertyName("incidents")] public List<StatuspageIncident>? Incidents { get; set; }
    }

    private sealed class StatuspageIncident
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("impact")] public string? Impact { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("resolved_at")] public DateTimeOffset? ResolvedAt { get; set; }
        [JsonPropertyName("incident_updates")] public List<StatuspageIncidentUpdate>? IncidentUpdates { get; set; }
    }

    private sealed class StatuspageIncidentUpdate
    {
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    }
}
