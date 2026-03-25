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
    public class SlUriParserTests
    {
        #region TryParseMapLink — success cases

        [Test]
        public void TryParseMapLink_SlurlComWithAllCoords_ReturnsTrueAndPopulatesInfo()
        {
            bool result = SlUriParser.TryParseMapLink(
                "https://slurl.com/secondlife/Ahern/128/128/0", out var info);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(info.RegionName, Is.EqualTo("Ahern"));
                Assert.That(info.X, Is.EqualTo(128));
                Assert.That(info.Y, Is.EqualTo(128));
                Assert.That(info.Z, Is.EqualTo(0));
            });
        }

        [Test]
        public void TryParseMapLink_MapsSecondlifeComWithAllCoords_ReturnsTrueAndPopulatesInfo()
        {
            bool result = SlUriParser.TryParseMapLink(
                "https://maps.secondlife.com/secondlife/Sandbox/200/100/50", out var info);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(info.RegionName, Is.EqualTo("Sandbox"));
                Assert.That(info.X, Is.EqualTo(200));
                Assert.That(info.Y, Is.EqualTo(100));
                Assert.That(info.Z, Is.EqualTo(50));
            });
        }

        [Test]
        public void TryParseMapLink_UrlEncodedRegionName_DecodesRegionName()
        {
            bool result = SlUriParser.TryParseMapLink(
                "https://maps.secondlife.com/secondlife/Help%20Island/128/128/0", out var info);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(info.RegionName, Is.EqualTo("Help Island"));
            });
        }

        [Test]
        public void TryParseMapLink_RegionWithXOnly_YAndZAreNull()
        {
            bool result = SlUriParser.TryParseMapLink(
                "https://slurl.com/secondlife/Sandbox/50", out var info);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(info.X, Is.EqualTo(50));
                Assert.That(info.Y, Is.Null);
                Assert.That(info.Z, Is.Null);
            });
        }

        [Test]
        public void TryParseMapLink_RegionWithNoCoords_AllCoordsNull()
        {
            bool result = SlUriParser.TryParseMapLink(
                "https://slurl.com/secondlife/Welcome", out var info);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(info.RegionName, Is.EqualTo("Welcome"));
                Assert.That(info.X, Is.Null);
                Assert.That(info.Y, Is.Null);
                Assert.That(info.Z, Is.Null);
            });
        }

        #endregion

        #region TryParseMapLink — failure cases

        [Test]
        public void TryParseMapLink_UnrelatedUrl_ReturnsFalse()
        {
            Assert.That(SlUriParser.TryParseMapLink("https://example.com/page", out _), Is.False);
        }

        [Test]
        public void TryParseMapLink_EmptyString_ReturnsFalse()
        {
            Assert.That(SlUriParser.TryParseMapLink(string.Empty, out _), Is.False);
        }

        [Test]
        public void TryParseMapLink_SecondLifeUriScheme_ReturnsFalse()
        {
            // secondlife:// URIs are handled by a separate parser, not TryParseMapLink
            Assert.That(SlUriParser.TryParseMapLink("secondlife://Ahern/128/128/0", out _), Is.False);
        }

        #endregion

        #region MapLinkInfo.ToString

        [Test]
        public void MapLinkInfo_ToString_AllCoords_FormatsCorrectly()
        {
            var info = new SlUriParser.MapLinkInfo { RegionName = "Ahern", X = 128, Y = 64, Z = 32 };
            Assert.That(info.ToString(), Is.EqualTo("Ahern (128,64,32)"));
        }

        [Test]
        public void MapLinkInfo_ToString_XAndYOnly_FormatsCorrectly()
        {
            var info = new SlUriParser.MapLinkInfo { RegionName = "Ahern", X = 128, Y = 64 };
            Assert.That(info.ToString(), Is.EqualTo("Ahern (128,64)"));
        }

        [Test]
        public void MapLinkInfo_ToString_XOnly_FormatsCorrectly()
        {
            var info = new SlUriParser.MapLinkInfo { RegionName = "Ahern", X = 128 };
            Assert.That(info.ToString(), Is.EqualTo("Ahern (128)"));
        }

        [Test]
        public void MapLinkInfo_ToString_NoCoords_ReturnsRegionNameOnly()
        {
            var info = new SlUriParser.MapLinkInfo { RegionName = "Sandbox" };
            Assert.That(info.ToString(), Is.EqualTo("Sandbox"));
        }

        [Test]
        public void MapLinkInfo_ToString_ZSetWithoutXY_DefaultsXAndYToZero()
        {
            // Z branch: "({X ?? 0},{Y ?? 0},{Z})"
            var info = new SlUriParser.MapLinkInfo { RegionName = "Test", Z = 25 };
            Assert.That(info.ToString(), Is.EqualTo("Test (0,0,25)"));
        }

        [Test]
        public void MapLinkInfo_ToString_YSetWithoutX_DefaultsXToZero()
        {
            // Y branch: "({X ?? 0},{Y})"
            var info = new SlUriParser.MapLinkInfo { RegionName = "Test", Y = 100 };
            Assert.That(info.ToString(), Is.EqualTo("Test (0,100)"));
        }

        #endregion

        #region UrlRegex static member

        [Test]
        public void UrlRegex_MatchesHttpUrl()
        {
            Assert.That(SlUriParser.UrlRegex.IsMatch("https://example.com/path"), Is.True);
        }

        [Test]
        public void UrlRegex_MatchesHttpWithoutPath()
        {
            Assert.That(SlUriParser.UrlRegex.IsMatch("http://example.com"), Is.True);
        }

        [Test]
        public void UrlRegex_MatchesSecondLifeUri()
        {
            Assert.That(SlUriParser.UrlRegex.IsMatch("secondlife://Ahern/128/128/0"), Is.True);
        }

        [Test]
        public void UrlRegex_MatchesBracketedSecondLifeUri()
        {
            Assert.That(SlUriParser.UrlRegex.IsMatch("[secondlife://Ahern/128/128/0 Visit Ahern]"), Is.True);
        }

        [Test]
        public void UrlRegex_DoesNotMatchPlainText()
        {
            Assert.That(SlUriParser.UrlRegex.IsMatch("Hello World"), Is.False);
        }

        [Test]
        public void UrlRegex_DoesNotMatchEmptyString()
        {
            Assert.That(SlUriParser.UrlRegex.IsMatch(string.Empty), Is.False);
        }

        #endregion
    }
}
