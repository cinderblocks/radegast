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
using System.Diagnostics;
using System.Threading;
using Avalonia.Threading;

namespace Radegast.Veles.Core;

/// <summary>
/// Measures how long work posted to the UI thread waits before it runs. A background thread
/// posts two probes at a fixed interval, one at the highest dispatcher priority and one at
/// render priority. A late high-priority probe means a single job held the thread; a late
/// render-priority probe alone means higher-priority jobs kept arriving ahead of it.
/// Runs only while at least one caller holds it open.
/// </summary>
internal static class UiThreadWatch
{
    private const int    ProbeIntervalMs   = 50;
    private const double RecordThresholdMs = 30.0;
    private const int    HistorySize       = 32;

    private sealed class Probe(DispatcherPriority priority)
    {
        private readonly (long Posted, long Ran)[] _history = new (long, long)[HistorySize];
        private readonly object _lock = new();
        private int  _next;
        private long _pendingSince;

        public void Post()
        {
            long now = Stopwatch.GetTimestamp();
            if (Interlocked.CompareExchange(ref _pendingSince, now, 0) != 0) return;
            Dispatcher.UIThread.Post(Ran, priority);
        }

        private void Ran()
        {
            long now = Stopwatch.GetTimestamp();
            long posted = Interlocked.Exchange(ref _pendingSince, 0);
            if (posted == 0 || Ms(now - posted) < RecordThresholdMs) return;
            lock (_lock)
            {
                _history[_next] = (posted, now);
                _next = (_next + 1) % HistorySize;
            }
        }

        public double LongestWaitMs(long from, long to)
        {
            long longest = 0;
            lock (_lock)
            {
                foreach (var (posted, ran) in _history)
                    if (ran > from && posted < to) longest = Math.Max(longest, ran - posted);
            }
            long pending = Interlocked.Read(ref _pendingSince);
            if (pending != 0 && pending < to) longest = Math.Max(longest, to - pending);
            return Ms(longest);
        }
    }

    private static readonly Probe s_blocked = new(DispatcherPriority.Send);
    private static readonly Probe s_render  = new(DispatcherPriority.Render);
    private static readonly object s_lifetimeLock = new();
    private static int s_holders;
    private static CancellationTokenSource? s_cts;

    public static void Acquire()
    {
        lock (s_lifetimeLock)
        {
            if (s_holders++ > 0) return;
            var cts = s_cts = new CancellationTokenSource();
            new Thread(() => Run(cts.Token)) { IsBackground = true, Name = "UiThreadWatch" }.Start();
        }
    }

    public static void Release()
    {
        lock (s_lifetimeLock)
        {
            if (s_holders == 0 || --s_holders > 0) return;
            s_cts?.Cancel();
            s_cts = null;
        }
    }

    /// <summary>
    /// Longest waits seen between two <see cref="Stopwatch"/> timestamps: for a job at the
    /// highest priority, and for one at render priority.
    /// </summary>
    public static (double BlockedMs, double RenderPriorityMs) LongestWaits(long from, long to)
        => (s_blocked.LongestWaitMs(from, to), s_render.LongestWaitMs(from, to));

    private static void Run(CancellationToken ct)
    {
        while (!ct.WaitHandle.WaitOne(ProbeIntervalMs))
        {
            s_blocked.Post();
            s_render.Post();
        }
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
