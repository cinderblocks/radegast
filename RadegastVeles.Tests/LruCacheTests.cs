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

using System.Collections.Generic;
using NUnit.Framework;
using Radegast.Veles.Core;

namespace Radegast.Veles.Tests;

[TestFixture]
public class LruCacheTests
{
    [Test]
    public void AddingBeyondCapacity_EvictsTheLeastRecentlyUsedEntry()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, int>(2, (key, _) => evicted.Add(key));

        cache.AddOrUpdate("a", 1);
        cache.AddOrUpdate("b", 2);
        Assert.That(cache.TryGetValue("a", out _), Is.True);
        cache.AddOrUpdate("c", 3);

        Assert.That(evicted, Is.EqualTo(new[] { "b" }));
        Assert.That(cache.ContainsKey("a"), Is.True);
        Assert.That(cache.ContainsKey("c"), Is.True);
        Assert.That(cache.Count, Is.EqualTo(2));
    }

    [Test]
    public void LoweringCapacity_EvictsDownToTheNewSize()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, int>(4, (key, _) => evicted.Add(key));
        foreach (var key in new[] { "a", "b", "c", "d" })
            cache.AddOrUpdate(key, 0);

        cache.Capacity = 2;

        Assert.That(cache.Count, Is.EqualTo(2));
        Assert.That(evicted, Is.EquivalentTo(new[] { "a", "b" }));
    }

    [Test]
    public void TryRemove_ReturnsTheValueAndDropsTheEntry()
    {
        var cache = new LruCache<string, int>(2);
        cache.AddOrUpdate("a", 7);

        Assert.That(cache.TryRemove("a", out var value), Is.True);
        Assert.That(value, Is.EqualTo(7));
        Assert.That(cache.TryGetValue("a", out _), Is.False);
    }
}
