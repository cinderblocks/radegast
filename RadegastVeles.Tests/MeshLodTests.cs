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

using System.Numerics;
using LibreMetaverse.Rendering;
using NUnit.Framework;
using Radegast.Veles.Rendering;

namespace Radegast.Veles.Tests;

[TestFixture]
public class MeshLodTests
{
    private static readonly Vector3 OneMetre = new(1f, 1f, 1f);

    [Test]
    public void NearbyObject_UsesTheHighestDetail()
    {
        Assert.That(PrimMeshBuilder.MeshLodFor(OneMetre, 2f), Is.EqualTo(DetailLevel.Highest));
    }

    [Test]
    public void SmallDistantObject_UsesTheLowestDetail()
    {
        Assert.That(PrimMeshBuilder.MeshLodFor(OneMetre, 200f), Is.EqualTo(DetailLevel.Low));
    }

    [Test]
    public void DetailNeverIncreasesWithDistance()
    {
        var previous = DetailLevel.Highest;
        for (float distance = 0.5f; distance <= 512f; distance *= 1.25f)
        {
            var lod = PrimMeshBuilder.MeshLodFor(OneMetre, distance);
            Assert.That((int)lod, Is.LessThanOrEqualTo((int)previous), $"at {distance} m");
            previous = lod;
        }
    }

    [Test]
    public void LargerObject_KeepsAtLeastAsMuchDetailAtTheSameDistance()
    {
        for (float distance = 1f; distance <= 512f; distance *= 1.5f)
        {
            var small = PrimMeshBuilder.MeshLodFor(OneMetre, distance);
            var large = PrimMeshBuilder.MeshLodFor(OneMetre * 10f, distance);
            Assert.That((int)large, Is.GreaterThanOrEqualTo((int)small), $"at {distance} m");
        }
    }

    [Test]
    public void DegenerateInput_FallsBackToTheHighestDetail()
    {
        Assert.That(PrimMeshBuilder.MeshLodFor(Vector3.Zero, 50f), Is.EqualTo(DetailLevel.Highest));
        Assert.That(PrimMeshBuilder.MeshLodFor(OneMetre, 0f), Is.EqualTo(DetailLevel.Highest));
    }
}
