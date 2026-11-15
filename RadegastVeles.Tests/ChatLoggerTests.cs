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
using System.Threading;
using NUnit.Framework;
using Radegast.Veles.Core;

namespace Radegast.Veles.Tests;

[TestFixture]
public class ChatLoggerTests
{
    private const string Avatar = "Test Resident";
    private const string Session = "chat";

    private string _dir = string.Empty;
    private ChatLogger _logger = null!;

    private string LogPath => Path.Combine(_dir, Avatar, Session + ".txt");

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "veles-chat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, Avatar));
        _logger = new ChatLogger { BaseDirectory = _dir };
    }

    [TearDown]
    public void TearDown()
    {
        _logger.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteEarlierSession(int count)
    {
        var lines = Enumerable.Range(1, count).Select(i => $"[2026/01/01 9:{i % 60:00}] Someone: old {i}");
        File.WriteAllLines(LogPath, lines);
    }

    private void LogLive(params string[] texts)
    {
        int before = File.Exists(LogPath) ? File.ReadAllLines(LogPath).Length : 0;
        int expected = before + texts.Sum(t => t.Split('\n').Length);
        foreach (var text in texts)
            _logger.Log(Avatar, Session, new DateTime(2026, 1, 2, 10, 0, 0), text);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(LogPath) && File.ReadAllLines(LogPath).Length >= expected) return;
            Thread.Sleep(10);
        }
        Assert.Fail("The chat logger did not write the lines in time.");
    }

    private string[] Read(int skip, int take, out bool hasMore) =>
        _logger.ReadHistoryChunk(Avatar, Session, skip, take, out hasMore).Select(l => l.Text).ToArray();

    [Test]
    public void History_IsReadNewestChunkFirstInChronologicalOrder()
    {
        WriteEarlierSession(10);

        var newest = Read(0, 4, out var hasMore);
        var older = Read(4, 4, out _);

        Assert.That(newest, Is.EqualTo(new[] { "Someone: old 7", "Someone: old 8", "Someone: old 9", "Someone: old 10" }));
        Assert.That(older, Is.EqualTo(new[] { "Someone: old 3", "Someone: old 4", "Someone: old 5", "Someone: old 6" }));
        Assert.That(hasMore, Is.True);
    }

    [Test]
    public void LinesLoggedThisSession_AreNotReturnedAsHistory()
    {
        WriteEarlierSession(10);
        LogLive("Me: live 1", "Me: live 2", "Me: live 3");

        var newest = Read(0, 4, out _);

        Assert.That(newest, Is.EqualTo(new[] { "Someone: old 7", "Someone: old 8", "Someone: old 9", "Someone: old 10" }));
    }

    [Test]
    public void LoadingEarlierHistory_DoesNotRepeatLinesAfterChatting()
    {
        WriteEarlierSession(10);
        var first = Read(0, 4, out _);

        LogLive("Me: live 1", "Me: live 2", "Me: live 3");
        var second = Read(first.Length, 4, out _);

        Assert.That(second, Is.EqualTo(new[] { "Someone: old 3", "Someone: old 4", "Someone: old 5", "Someone: old 6" }));
        Assert.That(first.Intersect(second), Is.Empty);
    }

    [Test]
    public void MultiLineMessages_DoNotShiftLaterChunks()
    {
        File.WriteAllLines(LogPath,
        [
            "[2026/01/01 9:01] Someone: old 1",
            "[2026/01/01 9:02] Someone: old 2",
            "[2026/01/01 9:03] Someone: old 3 first line",
            "second line of old 3",
            "[2026/01/01 9:04] Someone: old 4",
        ]);
        LogLive("Me: live first line\nlive second line");

        var first = Read(0, 2, out _);
        var second = Read(first.Length, 2, out var hasMore);

        Assert.That(first, Is.EqualTo(new[] { "Someone: old 3 first line\nsecond line of old 3", "Someone: old 4" }));
        Assert.That(second, Is.EqualTo(new[] { "Someone: old 1", "Someone: old 2" }));
        Assert.That(hasMore, Is.False);
    }

    [Test]
    public void MultiLineMessage_IsReturnedWhole()
    {
        File.WriteAllLines(LogPath,
        [
            "[2026/01/01 9:01] Someone: line one",
            "line two",
            "",
            "line four",
            "",
            "[2026/01/01 9:02] Someone: after",
        ]);

        var lines = Read(0, 10, out _);

        Assert.That(lines, Is.EqualTo(new[] { "Someone: line one\nline two\n\nline four", "Someone: after" }));
    }

    [Test]
    public void MultiLineMessage_SplitAcrossChunks_KeepsItsLines()
    {
        File.WriteAllLines(LogPath,
        [
            "[2026/01/01 9:01] Someone: first",
            "more of first",
            "[2026/01/01 9:02] Someone: second",
            "more of second",
        ]);

        var newest = Read(0, 1, out var hasMore);
        var older = Read(1, 1, out _);

        Assert.That(newest, Is.EqualTo(new[] { "Someone: second\nmore of second" }));
        Assert.That(hasMore, Is.True);
        Assert.That(older, Is.EqualTo(new[] { "Someone: first\nmore of first" }));
    }

    [Test]
    public void MissingLog_ReturnsNothing()
    {
        Assert.That(Read(0, 50, out var hasMore), Is.Empty);
        Assert.That(hasMore, Is.False);
    }
}
