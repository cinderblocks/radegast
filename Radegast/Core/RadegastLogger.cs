/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Radegast
{
    /// <summary>
    /// Lightweight log entry used by Radegast's internal logger and UI consumers.
    /// </summary>
    public class LogEntry
    {
        public DateTime TimeStamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string Category { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }

        public override string ToString()
        {
            // Include category/scope in brackets followed by level
            if (Exception != null)
            {
                return $"{TimeStamp:o} [{Category}] [{Level}] {Message} - {Exception}";
            }
            return $"{TimeStamp:o} [{Category}] [{Level}] {Message}";
        }
    }

    public class LogEventArgs : EventArgs
    {
        public LogEntry Entry { get; }

        public LogEventArgs(LogEntry entry)
        {
            Entry = entry;
        }
    }

    /// <summary>
    /// Microsoft.Extensions.Logging-compatible provider/logger that writes to console, optional file and raises a UI event.
    /// Register with logging via: loggingBuilder.AddProvider(new RadegastAppender());
    /// </summary>
    public class RadegastAppender : ILoggerProvider, ILogger
    {
        #region Events
        private static EventHandler<LogEventArgs> m_Log;

        protected static void OnLog(object sender, LogEventArgs e)
        {
            EventHandler<LogEventArgs> handler = m_Log;
            if (handler == null) { return; }
            try { handler(sender, e); }
            catch { }
        }

        private static readonly object m_LogLock = new object();

        /// <summary>Raised when the main instance posts a log message</summary>
        public static event EventHandler<LogEventArgs> Log
        {
            add { lock (m_LogLock) { m_Log += value; } }
            remove { lock (m_LogLock) { m_Log -= value; } }
        }
        #endregion Events

        private readonly string _categoryName;
        private readonly Func<string, LogLevel, bool> _filter;

        public RadegastAppender(string categoryName = "Radegast", Func<string, LogLevel, bool> filter = null)
        {
            _categoryName = categoryName ?? "Radegast";
            _filter = filter ?? ((name, level) => true);
        }

        // ILoggerProvider
        public ILogger CreateLogger(string categoryName)
        {
            return new RadegastAppender(categoryName, _filter);
        }

        public void Dispose()
        {
            // no-op
        }

        // ILogger
        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return _filter(_categoryName, logLevel);
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!((ILogger)this).IsEnabled(logLevel)) return;

            string message = formatter != null ? formatter(state, exception) : (state?.ToString() ?? string.Empty);

            var entry = new LogEntry
            {
                TimeStamp = DateTime.Now,
                Level = logLevel,
                Category = _categoryName,
                Message = message,
                Exception = exception
            };

            ProcessLogEntry(entry);
        }

        private static void ProcessLogEntry(LogEntry entry)
        {
            try
            {
                if (m_Log != null)
                    OnLog(typeof(RadegastAppender), new LogEventArgs(entry));

                // Print time, then category in brackets, then level in brackets
                Console.Write("{0:HH:mm:ss} [", entry.TimeStamp);
                WriteColorText(ConsoleColor.Cyan, entry.Category ?? "");
                Console.Write("] [");
                WriteColorText(DeriveColor(entry.Level), entry.Level.ToString());
                Console.Write("]: - ");

                if (entry.Level == LogLevel.Error || entry.Level == LogLevel.Critical)
                {
                    WriteColorText(ConsoleColor.Red, entry.Message ?? string.Empty);
                }
                else if (entry.Level == LogLevel.Warning)
                {
                    WriteColorText(ConsoleColor.Yellow, entry.Message ?? string.Empty);
                }
                else
                {
                    Console.Write(entry.Message ?? string.Empty);
                }

                if (entry.Exception != null)
                {
                    Console.Write(" - ");
                    WriteColorText(ConsoleColor.Red, entry.Exception.ToString());
                }

                Console.WriteLine();

                if (RadegastInstanceForms.Initialized
                    && RadegastInstanceForms.Instance.GlobalLogFile != null
                    && (!RadegastInstanceForms.Instance.GlobalSettings.ContainsKey("log_to_file")
                        || RadegastInstanceForms.Instance.GlobalSettings["log_to_file"]))
                {
                    File.AppendAllText(RadegastInstanceForms.Instance.GlobalLogFile, entry + Environment.NewLine);
                }
            }
            catch (Exception) { }
        }

        private static void WriteColorText(ConsoleColor color, string sender)
        {
            try
            {
                lock (typeof(RadegastAppender))
                {
                    try
                    {
                        Console.ForegroundColor = color;
                        Console.Write(sender);
                        Console.ResetColor();
                    }
                    catch (ArgumentNullException)
                    {
                        Console.Write(sender);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static ConsoleColor DeriveColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    return ConsoleColor.DarkGray;
                case LogLevel.Information:
                    return ConsoleColor.Green;
                case LogLevel.Warning:
                    return ConsoleColor.Yellow;
                case LogLevel.Error:
                case LogLevel.Critical:
                    return ConsoleColor.Red;
                default:
                    return ConsoleColor.Gray;
            }
        }

        // Allow direct static logging from non-ILogger code
        public static void LogDirect(LogLevel level, string message, string category = "Radegast", Exception ex = null)
        {
            try
            {
                var entry = new LogEntry
                {
                    TimeStamp = DateTime.Now,
                    Level = level,
                    Category = category,
                    Message = message,
                    Exception = ex
                };

                ProcessLogEntry(entry);
            }
            catch { }
        }

        // Helper null scope implementation
        private class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
            private NullScope() { }
        }
    }
}

