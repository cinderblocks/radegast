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

using System.IO;
using NUnit.Framework;

namespace Radegast.Tests
{
    [TestFixture]
    public class UtilitiesTests
    {
        #region SafeFileName

        [Test]
        public void SafeFileName_NullInput_ReturnsEmpty()
        {
            Assert.That(Utilities.SafeFileName(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void SafeFileName_EmptyString_ReturnsEmpty()
        {
            Assert.That(Utilities.SafeFileName(string.Empty), Is.EqualTo(string.Empty));
        }

        [Test]
        public void SafeFileName_ValidName_ReturnsUnchanged()
        {
            Assert.That(Utilities.SafeFileName("ValidFileName.txt"), Is.EqualTo("ValidFileName.txt"));
        }

        [Test]
        public void SafeFileName_SingleInvalidChar_ReplacedWithUnderscore()
        {
            char invalid = Path.GetInvalidFileNameChars()[0];
            string input = "file" + invalid + "name";
            Assert.That(Utilities.SafeFileName(input), Is.EqualTo("file_name"));
        }

        [Test]
        public void SafeFileName_MultipleInvalidChars_AllReplacedWithUnderscore()
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string input = invalidChars[0] + "name" + invalidChars[1];
            Assert.That(Utilities.SafeFileName(input), Is.EqualTo("_name_"));
        }

        [Test]
        public void SafeFileName_AllValidChars_ReturnsUnchanged()
        {
            const string input = "file_name-2025.txt";
            Assert.That(Utilities.SafeFileName(input), Is.EqualTo(input));
        }

        #endregion

        #region SafeDirName

        [Test]
        public void SafeDirName_NullInput_ReturnsEmpty()
        {
            Assert.That(Utilities.SafeDirName(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void SafeDirName_EmptyString_ReturnsEmpty()
        {
            Assert.That(Utilities.SafeDirName(string.Empty), Is.EqualTo(string.Empty));
        }

        [Test]
        public void SafeDirName_ValidName_ReturnsUnchanged()
        {
            Assert.That(Utilities.SafeDirName("valid_dir"), Is.EqualTo("valid_dir"));
        }

        [Test]
        public void SafeDirName_InvalidChar_ReplacedWithUnderscore()
        {
            char invalid = Path.GetInvalidPathChars()[0];
            string input = "dir" + invalid + "name";
            Assert.That(Utilities.SafeDirName(input), Is.EqualTo("dir_name"));
        }

        #endregion

        #region TryParseTwoNames

        [Test]
        public void TryParseTwoNames_NullInput_ReturnsFalse()
        {
            Assert.That(Utilities.TryParseTwoNames(null, out _, out _), Is.False);
        }

        [Test]
        public void TryParseTwoNames_EmptyString_ReturnsFalse()
        {
            Assert.That(Utilities.TryParseTwoNames(string.Empty, out _, out _), Is.False);
        }

        [Test]
        public void TryParseTwoNames_SingleWord_ReturnsFalse()
        {
            Assert.That(Utilities.TryParseTwoNames("OnlyOne", out _, out _), Is.False);
        }

        [Test]
        public void TryParseTwoNames_ThreeWords_ReturnsFalse()
        {
            Assert.That(Utilities.TryParseTwoNames("First Middle Last", out _, out _), Is.False);
        }

        [Test]
        public void TryParseTwoNames_TwoWords_ReturnsTrueAndSetsNames()
        {
            bool result = Utilities.TryParseTwoNames("John Doe", out string first, out string last);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(first, Is.EqualTo("John"));
                Assert.That(last, Is.EqualTo("Doe"));
            });
        }

        [Test]
        public void TryParseTwoNames_LeadingAndTrailingSpaces_ReturnsTrueAndSetsNames()
        {
            bool result = Utilities.TryParseTwoNames("  Alice  Smith  ", out string first, out string last);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(first, Is.EqualTo("Alice"));
                Assert.That(last, Is.EqualTo("Smith"));
            });
        }

        [Test]
        public void TryParseTwoNames_WhitespaceOnly_ReturnsFalse()
        {
            Assert.That(Utilities.TryParseTwoNames("   ", out _, out _), Is.False);
        }

        #endregion

        #region FormatNullableInt

        [Test]
        public void FormatNullableInt_NullValue_ReturnsNull()
        {
            Assert.That(Utilities.FormatNullableInt(null), Is.Null);
        }

        [TestCase(0, "0")]
        [TestCase(42, "42")]
        [TestCase(-1, "-1")]
        [TestCase(int.MaxValue, "2147483647")]
        public void FormatNullableInt_WithValue_ReturnsFormattedString(int value, string expected)
        {
            Assert.That(Utilities.FormatNullableInt(value), Is.EqualTo(expected));
        }

        #endregion

        #region FormatCoordinates

        [Test]
        public void FormatCoordinates_AllNull_ReturnsEmpty()
        {
            Assert.That(Utilities.FormatCoordinates(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatCoordinates_AllValues_ReturnsFormattedString()
        {
            Assert.That(Utilities.FormatCoordinates(10, 20, 30), Is.EqualTo(" (10,20,30)"));
        }

        [Test]
        public void FormatCoordinates_OnlyX_ReturnsFormattedString()
        {
            Assert.That(Utilities.FormatCoordinates(x: 5), Is.EqualTo(" (5)"));
        }

        [Test]
        public void FormatCoordinates_OnlyY_ReturnsFormattedString()
        {
            Assert.That(Utilities.FormatCoordinates(y: 128), Is.EqualTo(" (128)"));
        }

        [Test]
        public void FormatCoordinates_XAndZ_ReturnsFormattedString()
        {
            Assert.That(Utilities.FormatCoordinates(x: 1, z: 3), Is.EqualTo(" (1,3)"));
        }

        #endregion
    }
}
