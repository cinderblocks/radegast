/**
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn, LLC
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
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Radegast
{
    public partial class LogViewerConsole : RadegastTabControl
    {
        private static readonly Color ColorTrace    = Color.FromArgb(150, 150, 150);
        private static readonly Color ColorDebug    = Color.FromArgb(100, 100, 100);
        private static readonly Color ColorInfo     = Color.FromArgb(0, 160, 0);
        private static readonly Color ColorWarning  = Color.FromArgb(200, 120, 0);
        private static readonly Color ColorError    = Color.Red;
        private static readonly Color ColorCritical = Color.DarkRed;
        private static readonly Color ColorCategory = Color.CornflowerBlue;

        public LogViewerConsole() : this(null) { }

        public LogViewerConsole(RadegastInstanceForms instance) : base(instance)
        {
            InitializeComponent();
            Disposed += LogViewerConsole_Disposed;

            cmbLevel.Items.AddRange(new object[]
                { "All", "Trace", "Debug", "Information", "Warning", "Error", "Critical" });
            cmbLevel.SelectedIndex = 0;
            cmbLevel.SelectedIndexChanged += Filter_Changed;
            txtCategory.TextChanged += Filter_Changed;

            RadegastAppender.Log += RadegastAppender_Log;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void LogViewerConsole_Disposed(object sender, EventArgs e)
        {
            RadegastAppender.Log -= RadegastAppender_Log;
        }

        private LogLevel GetMinLevel()
        {
            switch (cmbLevel.SelectedIndex)
            {
                case 1: return LogLevel.Trace;
                case 2: return LogLevel.Debug;
                case 3: return LogLevel.Information;
                case 4: return LogLevel.Warning;
                case 5: return LogLevel.Error;
                case 6: return LogLevel.Critical;
                default: return LogLevel.Trace;
            }
        }

        private bool MatchesFilter(LogEntry entry)
        {
            if (entry.Level < GetMinLevel())
                return false;

            var cat = txtCategory.Text.Trim();
            return string.IsNullOrEmpty(cat) ||
                   (entry.Category != null &&
                    entry.Category.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RadegastAppender_Log(object sender, LogEventArgs e)
        {
            if (!IsHandleCreated) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => RadegastAppender_Log(sender, e)));
                return;
            }

            if (!MatchesFilter(e.Entry))
                return;

            AppendEntry(e.Entry);
        }

        private void AppendEntry(LogEntry entry)
        {
            var levelColor = GetLevelColor(entry.Level);

            rtbLog.SelectionColor = SystemColors.WindowText;
            rtbLog.AppendText($"{entry.TimeStamp:HH:mm:ss} [");

            rtbLog.SelectionColor = ColorCategory;
            rtbLog.AppendText(entry.Category ?? string.Empty);

            rtbLog.SelectionColor = SystemColors.WindowText;
            rtbLog.AppendText("] [");

            rtbLog.SelectionColor = levelColor;
            rtbLog.AppendText(entry.Level.ToString());

            rtbLog.SelectionColor = SystemColors.WindowText;
            rtbLog.AppendText("]: ");

            rtbLog.SelectionColor = levelColor;
            rtbLog.AppendText(entry.Message ?? string.Empty);

            if (entry.Exception != null)
            {
                rtbLog.SelectionColor = ColorError;
                rtbLog.AppendText($" \u2014 {entry.Exception.Message}");
            }

            rtbLog.SelectionColor = SystemColors.WindowText;
            rtbLog.AppendText(Environment.NewLine);

            if (chkAutoScroll.Checked)
            {
                rtbLog.SelectionStart = rtbLog.Text.Length;
                rtbLog.ScrollToCaret();
            }
        }

        private static Color GetLevelColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:       return ColorTrace;
                case LogLevel.Debug:       return ColorDebug;
                case LogLevel.Information: return ColorInfo;
                case LogLevel.Warning:     return ColorWarning;
                case LogLevel.Error:       return ColorError;
                case LogLevel.Critical:    return ColorCritical;
                default:                   return SystemColors.WindowText;
            }
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            // Filtering is applied to new entries only; existing entries remain as-is.
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            rtbLog.Clear();
        }

        private void rtbLog_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            instance?.MainForm.ProcessLink(e.LinkText);
        }
    }
}
