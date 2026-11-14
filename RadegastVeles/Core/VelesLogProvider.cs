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
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Radegast.Veles.Core;

public class VelesLogEntry
{
    public DateTime TimeStamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}

public class VelesLogEventArgs : EventArgs
{
    public VelesLogEntry Entry { get; }
    public VelesLogEventArgs(VelesLogEntry entry) => Entry = entry;
}

public sealed class VelesLogProvider : ILoggerProvider
{
    private static readonly object s_lock = new();
    private static EventHandler<VelesLogEventArgs>? s_logReceived;

    // The log file is closed and renamed with this suffix once it reaches MaxLogFileBytes, so
    // at most two files of that size exist: the current one and the one before it.
    private const long MaxLogFileBytes = 8L * 1024 * 1024;
    private const string PreviousLogSuffix = ".old";

    // Entries waiting for the writer thread. When it falls this far behind, new entries are
    // dropped and counted instead of blocking the thread that logged them.
    private const int MaxQueuedEntries = 16384;

    private static StreamWriter? s_fileWriter;
    private static long s_fileBytes;
    private static readonly object s_fileLock = new();

    private static readonly BlockingCollection<VelesLogEntry> s_queue =
        new(new ConcurrentQueue<VelesLogEntry>(), MaxQueuedEntries);
    private static readonly ManualResetEventSlim s_idle = new(true);
    private static int s_dropped;
    private static int s_pending;
    private static Thread? s_writerThread;

    public static event EventHandler<VelesLogEventArgs> LogReceived
    {
        add { lock (s_lock) { s_logReceived += value; } }
        remove { lock (s_lock) { s_logReceived -= value; } }
    }

    private static string? s_filePath;

    public static string? LogFilePath
    {
        get { lock (s_fileLock) { return s_filePath; } }
    }

    public static void EnableFileLogging(string filePath)
    {
        lock (s_fileLock)
        {
            s_filePath = filePath;
            s_fileWriter?.Dispose();
            s_fileWriter = null;
            OpenLogFile(rotateIfLarge: true);
            WriteLineToFile(string.Empty);
            WriteLineToFile($"=== Veles Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            s_fileWriter?.Flush();

            if (s_writerThread == null)
            {
                s_writerThread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "VelesLogWriter"
                };
                s_writerThread.Start();
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
            }
        }
    }

    /// <summary>
    /// Blocks until every entry logged so far has been written to the file and flushed, or
    /// the timeout passes. For use before the process exits or reports a crash.
    /// </summary>
    public static void Flush(int timeoutMs = 2000)
    {
        if (Thread.CurrentThread != s_writerThread)
            s_idle.Wait(timeoutMs);
        lock (s_fileLock)
        {
            try { s_fileWriter?.Flush(); }
            catch { /* don't let file I/O failures break logging */ }
        }
    }

    internal static void RaiseLogReceived(VelesLogEntry entry)
    {
        var handler = s_logReceived;
        if (handler != null)
        {
            try { handler(typeof(VelesLogProvider), new VelesLogEventArgs(entry)); }
            catch { /* swallow exceptions from handlers */ }
        }

        if (s_writerThread == null || s_queue.IsAddingCompleted)
        {
            WriteToConsoleAndDebug(entry);
            WriteToFile(entry);
            return;
        }

        Interlocked.Increment(ref s_pending);
        s_idle.Reset();
        bool queued;
        try { queued = s_queue.TryAdd(entry); }
        catch (InvalidOperationException) { queued = false; }
        if (!queued)
        {
            Interlocked.Increment(ref s_dropped);
            if (Interlocked.Decrement(ref s_pending) == 0) s_idle.Set();
        }

        // A critical entry is usually the last thing logged before the process dies.
        if (entry.Level >= LogLevel.Critical) Flush();
    }

    private static void WriterLoop()
    {
        foreach (var entry in s_queue.GetConsumingEnumerable())
        {
            WriteToConsoleAndDebug(entry);
            WriteToFile(entry);

            int dropped = Interlocked.Exchange(ref s_dropped, 0);
            if (dropped > 0)
            {
                lock (s_fileLock)
                {
                    WriteLineToFile($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{LogLevel.Warning,-11}] [VelesLog] " +
                                    $"{dropped} log entries were dropped because the log writer fell behind.");
                }
            }

            if (Interlocked.Decrement(ref s_pending) == 0)
            {
                lock (s_fileLock)
                {
                    try { s_fileWriter?.Flush(); }
                    catch { /* don't let file I/O failures break logging */ }
                }
                if (Volatile.Read(ref s_pending) == 0) s_idle.Set();
            }
        }
    }

    // Caller holds s_fileLock.
    private static void OpenLogFile(bool rotateIfLarge)
    {
        if (s_filePath == null) return;
        try
        {
            if (rotateIfLarge)
            {
                var info = new FileInfo(s_filePath);
                if (info.Exists && info.Length >= MaxLogFileBytes)
                    File.Move(s_filePath, s_filePath + PreviousLogSuffix, overwrite: true);
            }

            s_fileWriter = new StreamWriter(s_filePath, append: true, new UTF8Encoding(false), 64 * 1024);
            s_fileBytes = s_fileWriter.BaseStream.Length;
        }
        catch
        {
            s_fileWriter = null;
        }
    }

    // Caller holds s_fileLock.
    private static void WriteLineToFile(string line)
    {
        if (s_fileWriter == null) return;
        try
        {
            s_fileWriter.WriteLine(line);
            s_fileBytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (s_fileBytes < MaxLogFileBytes) return;

            s_fileWriter.Dispose();
            s_fileWriter = null;
            OpenLogFile(rotateIfLarge: true);
            s_fileWriter?.WriteLine($"=== Veles Log Continued {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
        catch { /* don't let file I/O failures break logging */ }
    }

    private static void WriteToConsoleAndDebug(VelesLogEntry entry)
    {
        try
        {
            Console.Write("{0:HH:mm:ss} [", entry.TimeStamp);
            WriteColorText(ConsoleColor.Cyan, entry.Category);
            Console.Write("] [");
            WriteColorText(DeriveColor(entry.Level), entry.Level.ToString());
            Console.Write("]: - ");

            if (entry.Level == LogLevel.Error || entry.Level == LogLevel.Critical)
            {
                WriteColorText(ConsoleColor.Red, entry.Message);
            }
            else if (entry.Level == LogLevel.Warning)
            {
                WriteColorText(ConsoleColor.Yellow, entry.Message);
            }
            else
            {
                Console.Write(entry.Message);
            }

            if (entry.Exception != null)
            {
                Console.Write(" - ");
                WriteColorText(ConsoleColor.Red, entry.Exception.ToString());
            }

            Console.WriteLine();

            // Also write a structured line to Debug output so IDEs (Visual Studio) show logs in Debug pane
            string dbgMsg = $"{entry.TimeStamp:HH:mm:ss.fff} [{entry.Level}] {entry.Message}";
            if (entry.Exception != null)
            {
                dbgMsg += " - " + entry.Exception;
            }
            System.Diagnostics.Debug.WriteLine(dbgMsg);
        }
        catch { /* don't let console/debug output failures break logging */ }
    }

    private static void WriteColorText(ConsoleColor color, string text)
    {
        try
        {
            lock (s_lock)
            {
                try
                {
                    Console.ForegroundColor = color;
                    Console.Write(text);
                    Console.ResetColor();
                }
                catch (ArgumentNullException)
                {
                    Console.Write(text);
                }
            }
        }
        catch (ObjectDisposedException) { }
    }

    private static ConsoleColor DeriveColor(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => ConsoleColor.DarkGray,
        LogLevel.Information => ConsoleColor.Green,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
        _ => ConsoleColor.Gray
    };

    private static void WriteToFile(VelesLogEntry entry)
    {
        lock (s_fileLock)
        {
            if (s_fileWriter == null) return;
            WriteLineToFile($"{entry.TimeStamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level,-11}] [{entry.Category}] {entry.Message}");
            if (entry.Exception != null)
                WriteLineToFile($"  Exception: {entry.Exception}");
        }
    }

    public ILogger CreateLogger(string categoryName) => new VelesLogger(categoryName);

    public void Dispose()
    {
        s_queue.CompleteAdding();
        var writer = s_writerThread;
        if (writer != null && writer != Thread.CurrentThread)
            writer.Join(TimeSpan.FromSeconds(3));

        lock (s_fileLock)
        {
            s_fileWriter?.Dispose();
            s_fileWriter = null;
        }
    }

    private sealed class VelesLogger : ILogger
    {
        private readonly string _category;

        public VelesLogger(string category) => _category = category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var entry = new VelesLogEntry
            {
                TimeStamp = DateTime.Now,
                Level = logLevel,
                Category = _category,
                Message = formatter(state, exception),
                Exception = exception
            };

            RaiseLogReceived(entry);
        }
    }
}
