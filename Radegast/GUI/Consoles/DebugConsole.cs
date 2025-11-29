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
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Radegast
{
    public partial class DebugConsole : RadegastTabControl
    {
        public DebugConsole()
            : this(null)
        {
        }

        public DebugConsole(RadegastInstanceForms instance)
            :base(instance)
        {
            InitializeComponent();
            Disposed += DebugConsole_Disposed;
            RadegastAppender.Log += RadegastAppender_Log;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void DebugConsole_Disposed(object sender, EventArgs e)
        {
            RadegastAppender.Log -= RadegastAppender_Log;
        }

        private void RadegastAppender_Log(object sender, LogEventArgs e)
        {
            if (!IsHandleCreated) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => RadegastAppender_Log(sender, e)));
                return;
            }

            var entry = e.Entry;

            rtbLog.SelectionColor = Color.FromKnownColor(KnownColor.WindowText);
            rtbLog.AppendText(string.Format("{0:HH:mm:ss} [", entry.TimeStamp));

            // Choose color based on Microsoft.Extensions.Logging.LogLevel
            if (entry.Level == LogLevel.Error || entry.Level == LogLevel.Critical)
            {
                rtbLog.SelectionColor = Color.Red;
            }
            else if (entry.Level == LogLevel.Warning)
            {
                rtbLog.SelectionColor = Color.Yellow;
            }
            else if (entry.Level == LogLevel.Information)
            {
                rtbLog.SelectionColor = Color.Green;
            }
            else
            {
                rtbLog.SelectionColor = Color.Gray;
            }

            rtbLog.AppendText(entry.Level.ToString());
            rtbLog.SelectionColor = Color.FromKnownColor(KnownColor.WindowText);
            rtbLog.AppendText($"]: - {entry.Message}{Environment.NewLine}");
        }

        private void rtbLog_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            instance.MainForm.ProcessLink(e.LinkText);
        }

    }
}
