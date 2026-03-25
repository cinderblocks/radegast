/**
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn, LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using NUnit.Framework;
using OpenMetaverse;
using Radegast.Rendering;

namespace Radegast.Tests
{
    [TestFixture]
    public class Math3DTests
    {
        private const float Tolerance = 1e-6f;

        #region CreateTranslationMatrix

        [Test]
        public void CreateTranslationMatrix_ReturnsArrayOf16Elements()
        {
            float[] mat = Math3D.CreateTranslationMatrix(new Vector3(0f, 0f, 0f));
            Assert.That(mat, Has.Length.EqualTo(16));
        }

        [Test]
        public void CreateTranslationMatrix_SetsTranslationComponents()
        {
            float[] mat = Math3D.CreateTranslationMatrix(new Vector3(1f, 2f, 3f));
            Assert.Multiple(() =>
            {
                Assert.That(mat[12], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[13], Is.EqualTo(2f).Within(Tolerance));
                Assert.That(mat[14], Is.EqualTo(3f).Within(Tolerance));
            });
        }

        [Test]
        public void CreateTranslationMatrix_HasIdentityDiagonal()
        {
            float[] mat = Math3D.CreateTranslationMatrix(new Vector3(5f, 6f, 7f));
            Assert.Multiple(() =>
            {
                Assert.That(mat[0], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[5], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[10], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[15], Is.EqualTo(1f).Within(Tolerance));
            });
        }

        [Test]
        public void CreateTranslationMatrix_ZeroVector_ReturnsIdentityMatrix()
        {
            float[] mat = Math3D.CreateTranslationMatrix(new Vector3(0f, 0f, 0f));
            Assert.Multiple(() =>
            {
                Assert.That(mat[0], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[5], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[10], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[15], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[12], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[13], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[14], Is.EqualTo(0f).Within(Tolerance));
            });
        }

        #endregion

        #region CreateRotationMatrix

        [Test]
        public void CreateRotationMatrix_ReturnsArrayOf16Elements()
        {
            float[] mat = Math3D.CreateRotationMatrix(Quaternion.Identity);
            Assert.That(mat, Has.Length.EqualTo(16));
        }

        [Test]
        public void CreateRotationMatrix_IdentityQuaternion_ReturnsIdentityMatrix()
        {
            float[] mat = Math3D.CreateRotationMatrix(Quaternion.Identity);
            Assert.Multiple(() =>
            {
                // Diagonal must be 1
                Assert.That(mat[0], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[5], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[10], Is.EqualTo(1f).Within(Tolerance));
                Assert.That(mat[15], Is.EqualTo(1f).Within(Tolerance));

                // Off-diagonal rotation elements must be 0
                Assert.That(mat[1], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[2], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[4], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[6], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[8], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[9], Is.EqualTo(0f).Within(Tolerance));

                // Translation column must be 0
                Assert.That(mat[12], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[13], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[14], Is.EqualTo(0f).Within(Tolerance));
            });
        }

        [Test]
        public void CreateRotationMatrix_LastRowIsHomogeneous()
        {
            float[] mat = Math3D.CreateRotationMatrix(Quaternion.Identity);
            Assert.Multiple(() =>
            {
                Assert.That(mat[3], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[7], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[11], Is.EqualTo(0f).Within(Tolerance));
                Assert.That(mat[15], Is.EqualTo(1f).Within(Tolerance));
            });
        }

        #endregion
    }
}
