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


using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Radegast.Veles.Core;

namespace Radegast.Veles.Tests;

[TestFixture]
public class AppCommandTests
{
    [TestCase("--new-login", nameof(AppCommandKind.NewLogin))]
    [TestCase("--check-updates", nameof(AppCommandKind.CheckForUpdates))]
    [TestCase("--exit", nameof(AppCommandKind.Exit))]
    public void TryParse_ReadsSimpleVerbs(string verb, string expected)
    {
        Assert.That(AppCommand.TryParse(new[] { "ignored", verb }, out var command), Is.True);
        Assert.That(command.Kind, Is.EqualTo(Enum.Parse<AppCommandKind>(expected)));
    }

    [Test]
    public void TryParse_ReadsSessionVerbs()
    {
        var id = Guid.NewGuid();
        Assert.That(AppCommand.TryParse(new[] { "--show", id.ToString() }, out var show), Is.True);
        Assert.That(show, Is.EqualTo(new AppCommand(AppCommandKind.ShowSession, id)));
        Assert.That(AppCommand.TryParse(new[] { "--logout", id.ToString() }, out var logout), Is.True);
        Assert.That(logout, Is.EqualTo(new AppCommand(AppCommandKind.LogoutSession, id)));
    }

    [TestCase()]
    [TestCase("--unknown")]
    [TestCase("--show")]
    [TestCase("--show", "not-a-guid")]
    [TestCase("new-login")]
    public void TryParse_RejectsEverythingElse(params string[] args)
    {
        Assert.That(AppCommand.TryParse(args, out _), Is.False);
    }

    [Test]
    public void ToArguments_RoundTrips()
    {
        foreach (var command in new[]
                 {
                     new AppCommand(AppCommandKind.NewLogin),
                     new AppCommand(AppCommandKind.CheckForUpdates),
                     new AppCommand(AppCommandKind.Exit),
                     new AppCommand(AppCommandKind.ShowSession, Guid.NewGuid()),
                     new AppCommand(AppCommandKind.LogoutSession, Guid.NewGuid()),
                 })
        {
            Assert.That(AppCommand.TryParseLine(command.ToArguments(), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(command));
        }
    }
}

[TestFixture]
public class AppCommandChannelTests
{
    private string _endpoint = null!;
    private string? _directory;

    [SetUp]
    public void SetUp()
    {
        if (OperatingSystem.IsWindows())
        {
            _endpoint = "RadegastVeles.tests." + Guid.NewGuid().ToString("N");
        }
        else
        {
            // Unix socket paths are limited to about a hundred bytes, so the names stay short.
            _directory = Path.Combine(Path.GetTempPath(), "veles-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_directory);
            _endpoint = Path.Combine(_directory, "t.sock");
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void TrySend_WithNothingListening_ReturnsFalse()
    {
        Assert.That(AppCommandChannel.TrySend(_endpoint, new AppCommand(AppCommandKind.Exit)), Is.False);
    }

    [Test]
    public void Listener_ReceivesEachCommandSent()
    {
        using var received = new SemaphoreSlim(0);
        AppCommand? last = null;
        using var channel = AppCommandChannel.TryListen(_endpoint, command => { last = command; received.Release(); });
        Assert.That(channel, Is.Not.Null);

        var show = new AppCommand(AppCommandKind.ShowSession, Guid.NewGuid());
        Assert.That(AppCommandChannel.TrySend(_endpoint, show), Is.True);
        Assert.That(received.Wait(5000), Is.True);
        Assert.That(last, Is.EqualTo(show));

        Assert.That(AppCommandChannel.TrySend(_endpoint, new AppCommand(AppCommandKind.NewLogin)), Is.True);
        Assert.That(received.Wait(5000), Is.True);
        Assert.That(last, Is.EqualTo(new AppCommand(AppCommandKind.NewLogin)));
    }

    [Test]
    public void TryListen_WhileAnotherListens_ReturnsNull()
    {
        using var first = AppCommandChannel.TryListen(_endpoint, _ => { });
        Assert.That(first, Is.Not.Null);
        using var second = AppCommandChannel.TryListen(_endpoint, _ => { });
        Assert.That(second, Is.Null);
    }

    [Test]
    public void TryListen_ReplacesASocketLeftByADeadProcess()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Named pipes leave nothing behind.");

        AppCommandChannel.TryListen(_endpoint, _ => { })!.Dispose();
        File.WriteAllText(_endpoint, string.Empty);

        using var channel = AppCommandChannel.TryListen(_endpoint, _ => { });
        Assert.That(channel, Is.Not.Null);
        Assert.That(AppCommandChannel.TrySend(_endpoint, new AppCommand(AppCommandKind.Exit)), Is.True);
    }

    [Test]
    public void TrySend_AfterTheListenerStops_ReturnsFalse()
    {
        AppCommandChannel.TryListen(_endpoint, _ => { })!.Dispose();
        Assert.That(AppCommandChannel.TrySend(_endpoint, new AppCommand(AppCommandKind.Exit)), Is.False);
    }
}
