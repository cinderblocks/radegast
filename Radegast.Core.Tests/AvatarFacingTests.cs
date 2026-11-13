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

using LibreMetaverse;
using NUnit.Framework;

namespace Radegast.Tests
{
    [TestFixture]
    public class AvatarFacingTests
    {
        private const float Tolerance = 1e-4f;

        [TestCase(1f, 0f, 0f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(-1f, 0f, 0f)]
        [TestCase(0f, -1f, 0f)]
        [TestCase(0.6f, -0.8f, 0.3f)]
        public void TryGetBodyRotation_FacesTheHorizontalLookAt(float x, float y, float z)
        {
            Assert.That(AvatarFacing.TryGetBodyRotation(new Vector3(x, y, z), out Quaternion rotation), Is.True);

            Vector3 forward = Vector3.UnitX * rotation;
            Vector3 flat = Vector3.Normalize(new Vector3(x, y, 0f));
            Assert.Multiple(() =>
            {
                Assert.That(forward.X, Is.EqualTo(flat.X).Within(Tolerance));
                Assert.That(forward.Y, Is.EqualTo(flat.Y).Within(Tolerance));
                Assert.That(forward.Z, Is.EqualTo(0f).Within(Tolerance));
            });
        }

        [Test]
        public void TryGetBodyRotation_East_IsIdentity()
        {
            Assert.That(AvatarFacing.TryGetBodyRotation(new Vector3(1f, 0f, 0f), out Quaternion rotation), Is.True);
            Assert.That(rotation, Is.EqualTo(Quaternion.Identity));
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(0f, 0f, 1f)]
        [TestCase(0f, 0f, -1f)]
        public void TryGetBodyRotation_RejectsLookAtWithNoHorizontalDirection(float x, float y, float z)
        {
            Assert.That(AvatarFacing.TryGetBodyRotation(new Vector3(x, y, z), out _), Is.False);
        }
    }
}
