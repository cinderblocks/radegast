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
using System.Diagnostics;
using System.Threading.Tasks;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.Controls;

/// <summary>
/// A plain text span or a clickable link within a chat message.
/// </summary>
public sealed record ChatTextSegment(string DisplayText, string? Url = null)
{
    public bool IsLink => Url != null;
}

/// <summary>
/// Helpers for parsing chat text into segments and executing SL/HTTP links.
/// </summary>
public static class ChatLinkHelper
{
    /// <summary>
    /// Splits <paramref name="text"/> into alternating plain-text and link segments
    /// using the standard SL URL regex.
    /// </summary>
    public static IReadOnlyList<ChatTextSegment> ParseSegments(
        string? text, RadegastInstanceAvalonia? instance)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ChatTextSegment>();

        var result = new List<ChatTextSegment>();
        var matches = SlUriParser.UrlRegex.Matches(text);
        int pos = 0;

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (m.Index > pos)
                result.Add(new ChatTextSegment(text[pos..m.Index]));

            result.Add(new ChatTextSegment(GetDisplayLabel(m.Value, instance), m.Value));
            pos = m.Index + m.Length;
        }

        if (pos < text.Length)
            result.Add(new ChatTextSegment(text[pos..]));

        return result;
    }

    /// <summary>
    /// Returns a human-readable label for a URL or SLURL/SLAPP URI.
    /// </summary>
    public static string GetDisplayLabel(string url, RadegastInstanceAvalonia? instance)
    {
        if (string.IsNullOrEmpty(url)) return url;

        // Bracketed custom label: [secondlife://... Display Name]
        if (url.StartsWith('['))
        {
            var bracketClose = url.LastIndexOf(']');
            var spaceAfterUri = url.IndexOf(' ');
            if (spaceAfterUri > 0 && bracketClose > spaceAfterUri)
                return url[(spaceAfterUri + 1)..bracketClose];
            // Bracketed with no label — strip brackets and fall through
            url = url.TrimStart('[').TrimEnd(']');
        }

        // HTTP/HTTPS — check for known map links first
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (SlUriParser.TryParseMapLink(url, out var mapInfo) && mapInfo != null)
                return mapInfo.ToString();
            // Truncate long raw URLs
            return url.Length > 60 ? url[..57] + "…" : url;
        }

        // secondlife:// — use LibreMetaverse SlurlParser for structured info
        if (url.StartsWith("secondlife://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var parser = new SlurlParser(url);

                if (parser.UriType == ViewerUriType.Location)
                {
                    var sim = Uri.UnescapeDataString(parser.Sim);
                    return $"{sim} ({parser.X},{parser.Y},{parser.Z})";
                }

                if (parser.UriType == ViewerUriType.Application)
                {
                    var parts = parser.CommandPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    switch (parser.Command)
                    {
                        case SlappCommand.Agent when parts.Length >= 2 && UUID.TryParse(parts[1], out var agentId):
                            var agentAction = parts.Length >= 3 ? parts[2] : "about";
                            var agentName = instance?.Names.Get(agentId) ?? "Agent";
                            return agentAction switch
                            {
                                "im" => $"IM {agentName}",
                                "pay" => $"Pay {agentName}",
                                "requestfriend" => $"Add {agentName} as friend",
                                "offerteleport" => $"Offer teleport to {agentName}",
                                _ => agentName
                            };

                        case SlappCommand.Group when parts.Length >= 2 && UUID.TryParse(parts[1], out var groupId):
                            if (instance != null &&
                                instance.TryGetCachedGroupName(groupId, out var cachedGroupName))
                                return cachedGroupName;
                            return "Group Profile";

                        case SlappCommand.Teleport:
                            var tpSim = Uri.UnescapeDataString(parser.Sim);
                            return $"Teleport to {tpSim} ({parser.X},{parser.Y},{parser.Z})";

                        case SlappCommand.WorldMap:
                            var mapSim = Uri.UnescapeDataString(parser.Sim);
                            return $"Map: {mapSim} ({parser.X},{parser.Y},{parser.Z})";

                        default:
                            break;
                    }
                }
            }
            catch { /* fall through to raw display */ }
        }

        return url.Length > 60 ? url[..57] + "…" : url;
    }

    /// <summary>
    /// Executes a URL or SLURL/SLAPP URI, dispatching to the appropriate in-world action.
    /// </summary>
    public static void ExecuteLink(string url, RadegastInstanceAvalonia? instance)
    {
        if (string.IsNullOrEmpty(url)) return;

        // Strip brackets if present
        if (url.StartsWith('['))
        {
            var spaceAfterUri = url.IndexOf(' ');
            url = spaceAfterUri > 0
                ? url[1..spaceAfterUri]
                : url.TrimStart('[').TrimEnd(']');
        }

        // HTTP/HTTPS
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            OpenUrl(url);
            return;
        }

        if (!url.StartsWith("secondlife://", StringComparison.OrdinalIgnoreCase)) return;
        if (instance == null) return;

        try
        {
            var parser = new SlurlParser(url);

            if (parser.UriType == ViewerUriType.Location)
            {
                var sim = parser.Sim;
                var pos = new Vector3(parser.X, parser.Y, parser.Z);
                _ = instance.Client.Self.TeleportAsync(sim, pos);
                return;
            }

            if (parser.UriType == ViewerUriType.Application)
            {
                var parts = parser.CommandPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                switch (parser.Command)
                {
                    case SlappCommand.Agent when parts.Length >= 2 && UUID.TryParse(parts[1], out var agentId):
                        var agentAction = parts.Length >= 3 ? parts[2] : "about";
                        var agentName = instance.Names.Get(agentId);
                        switch (agentAction)
                        {
                            case "im":
                                instance.RequestIM(agentId, agentName);
                                break;
                            case "pay":
                                instance.OpenPayWindow(agentId, agentName);
                                break;
                            default:
                                instance.ShowAgentProfile(agentName, agentId);
                                break;
                        }
                        break;

                    case SlappCommand.Group when parts.Length >= 2 && UUID.TryParse(parts[1], out var groupId):
                        instance.ShowGroupProfile(groupId);
                        break;

                    case SlappCommand.Teleport:
                        var tpSim = parser.Sim;
                        var tpPos = new Vector3(parser.X, parser.Y, parser.Z);
                        _ = instance.Client.Self.TeleportAsync(tpSim, tpPos);
                        break;

                    case SlappCommand.WorldMap:
                        // Open the SL maps URL in the system browser as fallback
                        OpenUrl($"https://maps.secondlife.com/secondlife/{Uri.EscapeDataString(parser.Sim)}/{parser.X}/{parser.Y}/{parser.Z}");
                        break;
                }
            }
        }
        catch { /* ignore malformed URIs */ }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}
