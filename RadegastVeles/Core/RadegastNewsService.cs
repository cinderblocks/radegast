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
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Radegast.Veles.Core;

public sealed record NewsItem(string Title, string Link, DateTimeOffset PublishedAt);

/// <summary>
/// Reads the Radegast.life RSS feed for the login screen's "Latest News" panel.
/// Never throws: any failure degrades to the last good cache (or empty on first run).
/// </summary>
public static class RadegastNewsService
{
    private const string FeedUrl = "https://radegast.life/feed/";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    static RadegastNewsService()
    {
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadegastVeles", AppVersionInfo.CurrentVersionString.TrimStart('v')));
    }

    private static List<NewsItem>? _cache;
    private static DateTimeOffset _cacheTime;

    public static async Task<IReadOnlyList<NewsItem>> GetLatestAsync(int count = 3)
    {
        if (_cache != null && DateTimeOffset.UtcNow - _cacheTime < CacheTtl)
            return _cache.Take(count).ToList();

        try
        {
            var xml = await Http.GetStringAsync(FeedUrl);
            var doc = XDocument.Parse(xml);

            var items = doc.Descendants("item")
                .Select(item => new NewsItem(
                    Title: item.Element("title")?.Value.Trim() ?? string.Empty,
                    Link: item.Element("link")?.Value.Trim() ?? string.Empty,
                    PublishedAt: DateTimeOffset.TryParse(item.Element("pubDate")?.Value, out var d)
                        ? d
                        : DateTimeOffset.MinValue))
                .Where(i => !string.IsNullOrEmpty(i.Title) && !string.IsNullOrEmpty(i.Link))
                .OrderByDescending(i => i.PublishedAt)
                .ToList();

            _cache = items;
            _cacheTime = DateTimeOffset.UtcNow;
            return items.Take(count).ToList();
        }
        catch
        {
            return _cache?.Take(count).ToList() ?? [];
        }
    }
}
