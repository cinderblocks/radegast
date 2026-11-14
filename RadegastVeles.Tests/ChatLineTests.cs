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
using System.Collections.ObjectModel;
using System.Linq;
using NUnit.Framework;
using Radegast.Veles.Controls;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Tests;

[TestFixture]
public class ChatLineTests
{
    private static ChatLine Line(int n, DateTime? at = null) =>
        new(at ?? new DateTime(2026, 1, 1, 12, 0, 0), "Someone", $": line {n}", ChatLineType.Normal);

    [Test]
    public void TrimOldest_LeavesShortConversationsAlone()
    {
        var lines = new ObservableCollection<ChatLine>(Enumerable.Range(0, 100).Select(i => Line(i)));

        ChatLine.TrimOldest(lines);

        Assert.That(lines, Has.Count.EqualTo(100));
    }

    [Test]
    public void TrimOldest_MakesRoomForOneMoreLineAtTheLimit()
    {
        var lines = new ObservableCollection<ChatLine>(
            Enumerable.Range(0, ChatLine.MaxLinesInMemory + 25).Select(i => Line(i)));

        ChatLine.TrimOldest(lines);
        lines.Add(Line(-1));

        Assert.That(lines, Has.Count.EqualTo(ChatLine.MaxLinesInMemory));
        Assert.That(lines[0].Text, Is.EqualTo(": line 26"));
        Assert.That(lines[^1].Text, Is.EqualTo(": line -1"));
    }

    [Test]
    public void WithDateSeparators_InsertsOneBetweenDifferentDays()
    {
        var day1 = new DateTime(2026, 1, 1, 23, 0, 0);
        var day2 = new DateTime(2026, 1, 2, 9, 0, 0);

        var result = ChatLine.WithDateSeparators([Line(1, day1), Line(2, day1), Line(3, day2)]);

        Assert.That(result.Select(l => l.Type), Is.EqualTo(new[]
        {
            ChatLineType.Normal, ChatLineType.Normal, ChatLineType.DateSeparator, ChatLineType.Normal
        }));
        Assert.That(result[2].Timestamp.Date, Is.EqualTo(day2.Date));
    }

    [Test]
    public void WithDateSeparators_AddsNothingWithinOneDay()
    {
        var result = ChatLine.WithDateSeparators([Line(1), Line(2), Line(3)]);

        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseSegments_SplitsTextAroundLinks()
    {
        var segments = ChatLinkHelper.ParseSegments("see https://example.com/a now", null);

        Assert.That(segments, Has.Count.EqualTo(3));
        Assert.That(segments[0].IsLink, Is.False);
        Assert.That(segments[0].DisplayText, Is.EqualTo("see "));
        Assert.That(segments[1].IsLink, Is.True);
        Assert.That(segments[1].Url, Is.EqualTo("https://example.com/a"));
        Assert.That(segments[2].DisplayText, Is.EqualTo(" now"));
    }

    [Test]
    public void ParseSegments_DoesNotTreatOtherSchemesAsLinks()
    {
        var segments = ChatLinkHelper.ParseSegments("open file:///etc/hosts please", null);

        Assert.That(segments.Any(s => s.IsLink), Is.False);
    }

    [Test]
    public void ParseSegments_HandlesEmptyText()
    {
        Assert.That(ChatLinkHelper.ParseSegments(null, null), Is.Empty);
        Assert.That(ChatLinkHelper.ParseSegments(string.Empty, null), Is.Empty);
    }
}
