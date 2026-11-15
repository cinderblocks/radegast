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
public class LaunchOptionsTests
{
    [Test]
    public void Parse_WithNoArguments_HasNothingSet()
    {
        var options = LaunchOptions.Parse([]);
        Assert.That(options.HasLoginOptions, Is.False);
        Assert.That(options.Errors, Is.Empty);
        Assert.That(options.NoSound, Is.False);
    }

    [Test]
    public void Parse_ReadsLongOptions()
    {
        var options = LaunchOptions.Parse(
            ["--username", "Test Resident", "--grid", "agni", "--location", "Ahern/128/128/30", "--auto-login", "--no-sound"]);
        Assert.That(options.Errors, Is.Empty);
        Assert.That(options.Username, Is.EqualTo("Test Resident"));
        Assert.That(options.Grid, Is.EqualTo("agni"));
        Assert.That(options.Location, Is.EqualTo("Ahern/128/128/30"));
        Assert.That(options.AutoLogin, Is.True);
        Assert.That(options.NoSound, Is.True);
        Assert.That(options.HasLoginOptions, Is.True);
    }

    [Test]
    public void Parse_ReadsShortOptionsAndLegacySpellings()
    {
        var options = LaunchOptions.Parse(["-u", "a", "-p", "b", "-g", "c", "-l", "home", "-a"]);
        Assert.That(options.Errors, Is.Empty);
        Assert.That((options.Username, options.Password, options.Grid, options.Location, options.AutoLogin),
            Is.EqualTo(("a", "b", "c", "home", true)));

        var legacy = LaunchOptions.Parse(["--loginuri", "https://example.com/login", "--autologin"]);
        Assert.That(legacy.Errors, Is.Empty);
        Assert.That(legacy.LoginUri, Is.EqualTo("https://example.com/login"));
        Assert.That(legacy.AutoLogin, Is.True);
    }

    [Test]
    public void Parse_AcceptsEqualsForm()
    {
        var options = LaunchOptions.Parse(["--username=Test Resident", "--login-uri=https://example.com/a?b=c"]);
        Assert.That(options.Errors, Is.Empty);
        Assert.That(options.Username, Is.EqualTo("Test Resident"));
        Assert.That(options.LoginUri, Is.EqualTo("https://example.com/a?b=c"));
    }

    [Test]
    public void Parse_ReportsProblems()
    {
        Assert.That(LaunchOptions.Parse(["--usernme", "x"]).Errors, Has.Count.EqualTo(2));
        Assert.That(LaunchOptions.Parse(["--username"]).Errors, Has.Count.EqualTo(1));
        Assert.That(LaunchOptions.Parse(["--grid", "a", "--login-uri", "b"]).Errors, Has.Count.EqualTo(1));
        Assert.That(LaunchOptions.Parse(["--password", "a", "--password-stdin"]).Errors, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_LeavesRunningCopyVerbsAlone()
    {
        var options = LaunchOptions.Parse(["--show", "0f8fad5b-d9cb-469f-a165-70867728950e", "--exit", "--new-login"]);
        Assert.That(options.Errors, Is.Empty);
        Assert.That(options.HasLoginOptions, Is.False);
    }

    [Test]
    public void SupplyPassword_IgnoresAnEmptyValue()
    {
        var options = LaunchOptions.Parse(["--password-stdin"]);
        options.SupplyPassword(null);
        Assert.That(options.Password, Is.Null);
        options.SupplyPassword("secret");
        Assert.That(options.Password, Is.EqualTo("secret"));
    }

    [TestCase("Test Resident", "test.resident", true)]
    [TestCase("Test", "Test Resident", true)]
    [TestCase("  First   Last ", "first.last", true)]
    [TestCase("First Last", "First", false)]
    [TestCase("First Last", "First Other", false)]
    public void SameAccountName_IgnoresCaseSeparatorAndResident(string a, string b, bool expected)
    {
        Assert.That(LaunchOptions.SameAccountName(a, b), Is.EqualTo(expected));
    }
}
