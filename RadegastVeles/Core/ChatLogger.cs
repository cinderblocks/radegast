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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Core;

/// <summary>
/// Writes chat and IM lines to per-avatar log files under %APPDATA%\RadegastVeles\{AvatarName}\.
/// Each session (nearby chat → chat.txt, or IM partner/group name → {name}.txt) has its own file.
/// Lines are written on a background thread so the UI is never blocked.
/// Format: [yyyy/MM/dd H:mm] DisplayText
/// </summary>
public sealed class ChatLogger : IDisposable
{
    private static readonly Regex LineRegex = new(
        @"^\[(\d{4}/\d{2}/\d{2} \d+:\d{2})\] (.+)$",
        RegexOptions.Compiled);

    private readonly BlockingCollection<(string path, string line)> _queue =
        new BlockingCollection<(string, string)>(new ConcurrentQueue<(string, string)>());

    private readonly Thread _worker;
    private volatile bool _disposed;

    // Message lines this instance has appended to each log file. They sit at the end of
    // the file and are already on screen as live messages, so history reads skip past them.
    private readonly ConcurrentDictionary<string, int> _linesWrittenThisSession = new();

    /// <summary>
    /// When set, overrides the default base directory (%APPDATA%\RadegastVeles).
    /// Changes take effect on the next call to <see cref="Log"/>.
    /// </summary>
    public string? BaseDirectory { get; set; }

    /// <summary>When false, <see cref="Log"/> silently discards all lines.</summary>
    public bool IsEnabled { get; set; } = true;

    private static readonly string DefaultBase = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RadegastVeles");

    public ChatLogger()
    {
        _worker = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "VelesChatLogger"
        };
        _worker.Start();
    }

    private string ResolveLogPath(string avatarName, string sessionName)
    {
        var root = string.IsNullOrWhiteSpace(BaseDirectory) ? DefaultBase : BaseDirectory;
        var dir  = Path.Combine(root, RadegastInstance.SafeFileName(avatarName));
        return Path.Combine(dir, RadegastInstance.SafeFileName(sessionName) + ".txt");
    }

    /// <summary>
    /// Queues a chat line for writing.
    /// </summary>
    /// <param name="avatarName">The logged-in avatar's full name (used as sub-directory).</param>
    /// <param name="sessionName">"chat" for nearby, or the partner/group name for IMs.</param>
    /// <param name="timestamp">When the message was received/sent.</param>
    /// <param name="displayText">
    /// Already-formatted text, e.g. "Igor Linden: howdy" or "* Igor Linden waves".
    /// </param>
    public void Log(string avatarName, string sessionName, DateTime timestamp, string displayText)
    {
        if (!IsEnabled || _disposed || string.IsNullOrWhiteSpace(avatarName)) return;

        var path = ResolveLogPath(avatarName, sessionName);
        var line = $"[{timestamp:yyyy/MM/dd H:mm}] {displayText}{Environment.NewLine}";

        try { _queue.Add((path, line)); }
        catch { /* disposed — discard */ }
    }

    /// <summary>
    /// Reads up to <paramref name="take"/> history lines from the log file, counting back
    /// from where the file ended when this session started and skipping the last
    /// <paramref name="skip"/> of those lines (already loaded).
    /// Returns them in chronological order (oldest first) as history <see cref="ChatLine"/> objects.
    /// </summary>
    public IReadOnlyList<ChatLine> ReadHistoryChunk(
        string avatarName, string sessionName, int skip, int take, out bool hasMore)
    {
        hasMore = false;
        var result = new List<ChatLine>();
        if (string.IsNullOrWhiteSpace(avatarName)) return result;

        var path = ResolveLogPath(avatarName, sessionName);
        if (!File.Exists(path)) return result;

        string[] allLines;
        try { allLines = File.ReadAllLines(path, System.Text.Encoding.UTF8); }
        catch { return result; }

        skip += _linesWrittenThisSession.GetValueOrDefault(path);

        // Work from the end: skip the last `skip` messages, then take `take`. A line that does
        // not start a message continues the message above it, so those met on the way up are
        // held until that message is reached. Only messages are counted, here and in the
        // caller's offset.
        var collected = new List<(DateTime ts, string text)>();
        var continuation = new List<string>();
        int skipped = 0;
        int lastConsumedIdx = -1;
        for (int i = allLines.Length - 1; i >= 0 && collected.Count < take; i--)
        {
            if (!TryParseLine(allLines[i], out var ts, out var text))
            {
                continuation.Add(allLines[i]);
                continue;
            }

            if (skipped < skip)
            {
                skipped++;
            }
            else
            {
                collected.Add((ts, AppendContinuation(text, continuation)));
                lastConsumedIdx = i;
            }
            continuation.Clear();
        }

        // Check whether older valid lines exist before what we consumed
        hasMore = false;
        if (lastConsumedIdx > 0)
        {
            for (int i = lastConsumedIdx - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(allLines[i]) && LineRegex.IsMatch(allLines[i]))
                {
                    hasMore = true;
                    break;
                }
            }
        }

        // Reverse to chronological order
        for (int i = collected.Count - 1; i >= 0; i--)
        {
            var (ts, text) = collected[i];
            result.Add(new ChatLine(ts, string.Empty, text, ChatLineType.History));
        }
        return result;
    }

    private void ProcessQueue()
    {
        foreach (var (path, line) in _queue.GetConsumingEnumerable())
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(path, line, System.Text.Encoding.UTF8);
                int written = CountMessageLines(line);
                _linesWrittenThisSession.AddOrUpdate(path, written, (_, n) => n + written);
            }
            catch { }
        }
    }

    private static bool TryParseLine(string raw, out DateTime timestamp, out string text)
    {
        timestamp = default;
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = LineRegex.Match(raw);
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy/MM/dd H:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
            return false;
        text = m.Groups[2].Value;
        return true;
    }

    /// <summary>
    /// Joins a message's first line with its later lines, given bottom-up as they were read.
    /// Blank lines at the end are dropped.
    /// </summary>
    private static string AppendContinuation(string firstLine, List<string> linesBottomUp)
    {
        int first = 0;
        while (first < linesBottomUp.Count && string.IsNullOrWhiteSpace(linesBottomUp[first])) first++;
        if (first == linesBottomUp.Count) return firstLine;

        var sb = new System.Text.StringBuilder(firstLine);
        for (int i = linesBottomUp.Count - 1; i >= first; i--)
            sb.Append('\n').Append(linesBottomUp[i]);
        return sb.ToString();
    }

    private static int CountMessageLines(string text)
    {
        int count = 0;
        foreach (var part in text.Split('\n'))
            if (TryParseLine(part.TrimEnd('\r'), out _, out _)) count++;
        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(5));
    }
}
