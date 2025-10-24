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
using log4net.Appender;
using log4net.Core;

namespace Radegast
{
    /// <summary>
    /// Writes log information out onto the console
    /// </summary>
    public class RadegastAppender : AnsiColorTerminalAppender
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

        /// <summary>Raised when the GridClient object in the main Radegast instance is changed</summary>
        public static event EventHandler<LogEventArgs> Log
        {
            add { lock (m_LogLock) { m_Log += value; } }
            remove { lock (m_LogLock) { m_Log -= value; } }
        }
        #endregion Events

        protected override void Append(LoggingEvent le)
        {
            try
            {
                if (m_Log != null)
                    OnLog(this, new LogEventArgs(le));
                
                Console.Write("{0:HH:mm:ss} [", le.TimeStamp);
                WriteColorText(DeriveColor(le.Level.Name), le.Level.Name);
                Console.Write("]: - ");

                if (le.Level == Level.Error)
                {
                    WriteColorText(ConsoleColor.Red, le.MessageObject.ToString());
                }
                else if (le.Level == Level.Warn)
                {
                    WriteColorText(ConsoleColor.Yellow, le.MessageObject.ToString());
                }
                else
                {
                    Console.Write(le.MessageObject.ToString());
                }
                Console.WriteLine();

                if (RadegastInstanceForms.Initialized
                    && RadegastInstanceForms.Instance.GlobalLogFile != null
                    && (!RadegastInstanceForms.Instance.GlobalSettings.ContainsKey("log_to_file") 
                        || RadegastInstanceForms.Instance.GlobalSettings["log_to_file"]))
                    File.AppendAllText(RadegastInstanceForms.Instance.GlobalLogFile, RenderLoggingEvent(le) + Environment.NewLine);
            }
            catch (Exception) { }
        }

        private void WriteColorText(ConsoleColor color, string sender)
        {
            try
            {
                lock (this)
                {
                    try
                    {
                        Console.ForegroundColor = color;
                        Console.Write(sender);
                        Console.ResetColor();
                    }
                    catch (ArgumentNullException)
                    {
                        // Some older systems dont support coloured text.
                        Console.WriteLine(sender);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static ConsoleColor DeriveColor(string input)
        {
            int colIdx = (input.ToUpper().GetHashCode() % 6) + 9;
            return (ConsoleColor)colIdx;
        }
    }

    public class LogEventArgs : EventArgs
    {
        public LoggingEvent LogEntry;

        public LogEventArgs(LoggingEvent e)
        {
            LogEntry = e;
        }
    }
}

