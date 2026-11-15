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

using NUnit.Framework;
using Radegast.Veles.Core;

namespace Radegast.Veles.Tests;

[TestFixture]
public class MapTileCacheTests
{
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(4, 8)]
    [TestCase(8, 128)]
    public void TileSpanDoublesPerLevel(int level, int expected)
    {
        Assert.That(MapTileCache.TileSpan(level), Is.EqualTo(expected));
    }

    [TestCase(512.0, 1)]
    [TestCase(256.0, 1)]
    [TestCase(129.0, 1)]
    [TestCase(128.0, 2)]
    [TestCase(64.0, 3)]
    [TestCase(25.6, 4)]
    [TestCase(8.0, 6)]
    [TestCase(0.5, 8)]
    [TestCase(0.0, 8)]
    public void LevelKeepsAtLeastOneImagePixelPerScreenPixel(double pixelsPerRegion, int expected)
    {
        Assert.That(MapTileCache.LevelFor(pixelsPerRegion), Is.EqualTo(expected));
    }

    [TestCase(1000u, 1, 1000u)]
    [TestCase(1001u, 2, 1000u)]
    [TestCase(1003u, 3, 1000u)]
    [TestCase(1007u, 4, 1000u)]
    [TestCase(1008u, 4, 1008u)]
    [TestCase(255u, 8, 128u)]
    public void TileOriginIsAlignedToTheTileSpan(uint grid, int level, uint expected)
    {
        Assert.That(MapTileCache.TileOrigin(grid, level), Is.EqualTo(expected));
    }

    [TestCase("https://map.example.org", "https://map.example.org/")]
    [TestCase("http://grid.example.org:8002/", "http://grid.example.org:8002/")]
    [TestCase("http://grid.example.org/maps", "http://grid.example.org/maps/")]
    public void MapServerGetsATrailingSlash(string input, string expected)
    {
        Assert.That(MapTileCache.NormalizeServer(input), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("file:///etc/passwd")]
    [TestCase("not a url")]
    public void MapServerMustBeAWebAddress(string? input)
    {
        Assert.That(MapTileCache.NormalizeServer(input), Is.Null);
    }
}
