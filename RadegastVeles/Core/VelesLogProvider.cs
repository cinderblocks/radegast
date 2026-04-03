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

    private static StreamWriter? s_fileWriter;
    private static readonly object s_fileLock = new();

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
            s_fileWriter = new StreamWriter(filePath, append: true)
            {
                AutoFlush = true
            };
            s_fileWriter.WriteLine();
            s_fileWriter.WriteLine($"=== Veles Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
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

        WriteToFile(entry);
    }

    private static void WriteToFile(VelesLogEntry entry)
    {
        lock (s_fileLock)
        {
            if (s_fileWriter == null) return;
            try
            {
                var line = $"{entry.TimeStamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level,-11}] [{entry.Category}] {entry.Message}";
                s_fileWriter.WriteLine(line);
                if (entry.Exception != null)
                {
                    s_fileWriter.WriteLine($"  Exception: {entry.Exception}");
                }
            }
            catch { /* don't let file I/O failures break logging */ }
        }
    }

    public ILogger CreateLogger(string categoryName) => new VelesLogger(categoryName);

    public void Dispose()
    {
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
