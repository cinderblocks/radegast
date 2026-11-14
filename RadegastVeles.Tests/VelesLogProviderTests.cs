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
using System.Linq;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Radegast.Veles.Core;

namespace Radegast.Veles.Tests;

[TestFixture]
[NonParallelizable]
public class VelesLogProviderTests
{
    private string _dir = string.Empty;
    private string _logPath = string.Empty;
    private TextWriter _originalOut = TextWriter.Null;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "veles-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "Veles.log");
        _originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        VelesLogProvider.EnableFileLogging(_logPath);
    }

    [TearDown]
    public void TearDown()
    {
        VelesLogProvider.Flush(5000);
        Console.SetOut(_originalOut);
        VelesLogProvider.EnableFileLogging(Path.Combine(Path.GetTempPath(), "veles-log-tests-idle.log"));
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static ILogger CreateLogger() => new VelesLogProvider().CreateLogger("Test");

    [Test]
    public void LoggedLines_AreInTheFileAfterFlush()
    {
        var log = CreateLogger();
        log.LogInformation("first entry");
        log.LogWarning(new InvalidOperationException("the cause"), "second entry");

        VelesLogProvider.Flush(5000);

        var text = File.ReadAllText(_logPath);
        Assert.That(text, Does.Contain("=== Veles Log Started"));
        Assert.That(text, Does.Contain("[Test] first entry"));
        Assert.That(text, Does.Contain("[Test] second entry"));
        Assert.That(text, Does.Contain("InvalidOperationException: the cause"));
        Assert.That(text.IndexOf("first entry", StringComparison.Ordinal),
            Is.LessThan(text.IndexOf("second entry", StringComparison.Ordinal)));
    }

    [Test]
    public void CriticalEntry_IsWrittenWithoutAnExplicitFlush()
    {
        CreateLogger().LogCritical("about to stop");

        Assert.That(File.ReadAllText(_logPath), Does.Contain("about to stop"));
    }

    [Test]
    public void Subscribers_ReceiveEachEntry()
    {
        string? seen = null;
        void Handler(object? sender, VelesLogEventArgs e) => seen = e.Entry.Message;
        VelesLogProvider.LogReceived += Handler;
        try
        {
            CreateLogger().LogInformation("for the viewer");
        }
        finally
        {
            VelesLogProvider.LogReceived -= Handler;
        }

        Assert.That(seen, Is.EqualTo("for the viewer"));
    }

    [Test]
    public void FullFile_IsKeptAsThePreviousLogAndANewOneStarted()
    {
        var log = CreateLogger();
        var padding = new string('x', 1000);
        const int lineCount = 10_000;
        for (int i = 0; i < lineCount; i++)
        {
            log.LogDebug("line {Index} {Padding}", i, padding);
            if (i % 2000 == 0) VelesLogProvider.Flush(5000);
        }
        VelesLogProvider.Flush(5000);

        var previous = _logPath + ".old";
        Assert.That(File.Exists(previous), Is.True);
        Assert.That(new FileInfo(previous).Length, Is.GreaterThanOrEqualTo(8L * 1024 * 1024));
        Assert.That(new FileInfo(_logPath).Length, Is.LessThan(8L * 1024 * 1024));

        int written = File.ReadLines(previous).Count(l => l.Contains("] line "))
                      + File.ReadLines(_logPath).Count(l => l.Contains("] line "));
        Assert.That(written, Is.EqualTo(lineCount));
        Assert.That(File.ReadAllText(_logPath), Does.Contain("=== Veles Log Continued"));
    }
}
