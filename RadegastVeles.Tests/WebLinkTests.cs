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
public class WebLinkTests
{
    [TestCase("https://example.com/page?x=1")]
    [TestCase("http://example.com")]
    [TestCase("HTTPS://Example.com/Path")]
    [TestCase("  https://example.com  ")]
    public void TryNormalize_AcceptsWebAddresses(string url)
    {
        Assert.That(WebLink.TryNormalize(url, out var normalized), Is.True);
        Assert.That(normalized, Does.StartWith("http"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("example.com")]
    [TestCase("www.example.com/page")]
    [TestCase("file:///Applications/Calculator.app")]
    [TestCase("/Applications/Calculator.app")]
    [TestCase("C:\\Windows\\System32\\calc.exe")]
    [TestCase("\\\\server\\share\\run.exe")]
    [TestCase("calc.exe")]
    [TestCase("javascript:alert(1)")]
    [TestCase("ftp://example.com/file")]
    [TestCase("secondlife://Region/1/2/3")]
    [TestCase("mailto:someone@example.com")]
    [TestCase("httpx://example.com")]
    [TestCase("http://")]
    public void TryNormalize_RefusesEverythingElse(string? url)
    {
        Assert.That(WebLink.TryNormalize(url, out var normalized), Is.False);
        Assert.That(normalized, Is.Empty);
    }

    [Test]
    public void TryOpen_RefusesNonWebAddressWithoutLaunching()
    {
        Assert.That(WebLink.TryOpen("file:///etc/hosts"), Is.False);
    }
}
