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

namespace Radegast.Tests
{
    [TestFixture]
    public class LoginOptionsTests
    {
        #region IsPasswordMD5

        // A valid MD5-hashed password is exactly 35 chars: "$1$" prefix + 32 hex digits
        [TestCase("$1$00000000000000000000000000000000", true)]
        [TestCase("$1$abcdef1234567890abcdef1234567890", true)]
        [TestCase("plainpassword", false)]
        [TestCase("$1$tooshort", false)]
        [TestCase("$2$00000000000000000000000000000000", false)]
        [TestCase("", false)]
        public void IsPasswordMD5_VariousPasswords_ReturnsExpected(string pass, bool expected)
        {
            Assert.That(LoginOptions.IsPasswordMD5(pass), Is.EqualTo(expected));
        }

        [Test]
        public void IsPasswordMD5_Exactly35CharsButWrongPrefix_ReturnsFalse()
        {
            // 35 chars, does not start with "$1$"
            Assert.That(LoginOptions.IsPasswordMD5("XX000000000000000000000000000000000"), Is.False);
        }

        #endregion

        #region FullName

        [Test]
        public void FullName_BothNamesSet_ReturnsCombined()
        {
            var opts = new LoginOptions { FirstName = "Jane", LastName = "Doe" };
            Assert.That(opts.FullName, Is.EqualTo("Jane Doe"));
        }

        [Test]
        public void FullName_FirstNameEmpty_ReturnsEmpty()
        {
            var opts = new LoginOptions { FirstName = string.Empty, LastName = "Doe" };
            Assert.That(opts.FullName, Is.EqualTo(string.Empty));
        }

        [Test]
        public void FullName_LastNameEmpty_ReturnsEmpty()
        {
            var opts = new LoginOptions { FirstName = "Jane", LastName = string.Empty };
            Assert.That(opts.FullName, Is.EqualTo(string.Empty));
        }

        [Test]
        public void FullName_BothNull_ReturnsEmpty()
        {
            var opts = new LoginOptions();
            Assert.That(opts.FullName, Is.EqualTo(string.Empty));
        }

        #endregion

        #region Defaults

        [Test]
        public void StartLocation_Default_IsHome()
        {
            var opts = new LoginOptions();
            Assert.That(opts.StartLocation, Is.EqualTo(StartLocationType.Home));
        }

        [Test]
        public void StartLocationCustom_Default_IsEmpty()
        {
            var opts = new LoginOptions();
            Assert.That(opts.StartLocationCustom, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Channel_Default_IsEmpty()
        {
            var opts = new LoginOptions();
            Assert.That(opts.Channel, Is.EqualTo(string.Empty));
        }

        #endregion
    }
}
