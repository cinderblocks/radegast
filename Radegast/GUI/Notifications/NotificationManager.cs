/*
 * Radegast Metaverse Client
 * Copyright(c) 2026, Sjofn, LLC
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
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Radegast
{
    /// <summary>
    /// Manages toast-style stacked notifications in a corner of a host form.
    /// Each toast is hosted in a borderless owned Form so controls receive input reliably.
    /// </summary>
    public class NotificationManager : IDisposable
    {
        private readonly Form owner;
        private readonly RadegastInstanceForms instance;
        private readonly object sync = new object();
        private readonly List<ToastEntry> toasts = new List<ToastEntry>();
        private readonly int maxToasts;
        private readonly int margin = 8;

        private class ToastEntry
        {
            public Notification Control;
            public Timer AutoDismiss;
            public Form HostForm;
        }

        public NotificationManager(Form owner, RadegastInstanceForms instance, int maxToasts = 5)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
            this.maxToasts = Math.Max(1, maxToasts);
        }

        /// <summary>
        /// Show a notification as a toast. If timeoutSeconds > 0, it will auto-dismiss after the interval.
        /// timeoutSeconds == 0 means persistent until user dismisses.
        /// </summary>
        public void ShowToast(Notification notification, int timeoutSeconds = 0)
        {
            if (notification == null) return;
            if (owner.IsDisposed || owner.Disposing) return;

            if (owner.InvokeRequired)
            {
                owner.BeginInvoke(new Action(() => ShowToast(notification, timeoutSeconds)));
                return;
            }

            lock (sync)
            {
                // limit number of stacked toasts
                if (toasts.Count >= maxToasts)
                {
                    var oldest = toasts.FirstOrDefault();
                    if (oldest != null)
                    {
                        RemoveToastInternal(oldest);
                    }
                }

                // Host each toast in its own borderless form to avoid input issues
                var host = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    BackColor = Color.Transparent,
                    Opacity = 1.0,
                    TopMost = false
                };

                // Prepare notification control
                notification.Visible = true;
                notification.Dock = DockStyle.Fill;
                notification.Margin = new Padding(0);

                // Put notification into host
                host.Controls.Add(notification);

                // Calculate size after adding control
                notification.PerformLayout();
                host.ClientSize = notification.PreferredSize;

                // Position host relative to owner, stacking upwards
                var screenBottomRight = owner.PointToScreen(new Point(owner.ClientSize.Width, owner.ClientSize.Height));

                int stackOffset = 0;
                foreach (var t in toasts)
                {
                    stackOffset += t.HostForm.Height + margin;
                }

                var hostX = screenBottomRight.X - host.Width - margin;
                var hostY = screenBottomRight.Y - host.Height - margin - stackOffset;

                host.Location = new Point(hostX, hostY);

                // Show owned window so it stays above owner but doesn't steal taskbar
                try
                {
                    host.Show(owner);
                }
                catch
                {
                    // Fallback to Show() if Show(owner) fails
                    try { host.Show(); } catch { }
                }

                var entry = new ToastEntry { Control = notification, HostForm = host };
                toasts.Add(entry);

                // Hook up closing when host is closed
                host.FormClosed += (s, e) =>
                {
                    // Ensure we remove entry if host closed externally
                    lock (sync)
                    {
                        var en = toasts.FirstOrDefault(x => x.HostForm == host);
                        if (en != null)
                        {
                            RemoveToastInternal(en);
                        }
                    }
                };

                if (timeoutSeconds > 0)
                {
                    var t = new Timer { Interval = timeoutSeconds * 1000 };
                    t.Tick += (s, e) =>
                    {
                        try { t.Stop(); t.Dispose(); } catch { }
                        if (!host.IsDisposed && host.IsHandleCreated)
                        {
                            try { host.BeginInvoke(new Action(() => { if (!host.IsDisposed) host.Close(); })); } catch { }
                        }
                    };
                    entry.AutoDismiss = t;
                    t.Start();
                }

                // Play sound for toast
                try { instance.MediaManager.PlayUISound(UISounds.WindowOpen); } catch { }
            }
        }

        /// <summary>
        /// Remove a specific toast notification.
        /// </summary>
        public void RemoveToast(Notification notification)
        {
            if (notification == null) return;
            if (owner.InvokeRequired)
            {
                owner.BeginInvoke(new Action(() => RemoveToast(notification)));
                return;
            }

            lock (sync)
            {
                var entry = toasts.FirstOrDefault(t => t.Control == notification);
                if (entry != null)
                {
                    RemoveToastInternal(entry);
                }
            }
        }

        private void RemoveToastInternal(ToastEntry entry)
        {
            try
            {
                if (entry == null) return;

                if (entry.AutoDismiss != null)
                {
                    try { entry.AutoDismiss.Stop(); entry.AutoDismiss.Dispose(); } catch { }
                    entry.AutoDismiss = null;
                }

                if (entry.HostForm != null && !entry.HostForm.IsDisposed)
                {
                    try { entry.HostForm.FormClosed -= null; } catch { }
                    try { entry.HostForm.Close(); } catch { }
                    try { entry.HostForm.Dispose(); } catch { }
                }

                if (entry.Control != null)
                {
                    try { entry.Control.Dispose(); } catch { }
                }

                toasts.Remove(entry);

                // Reposition remaining toasts
                RepositionToasts();
            }
            catch { }
        }

        private void RepositionToasts()
        {
            try
            {
                var screenBottomRight = owner.PointToScreen(new Point(owner.ClientSize.Width, owner.ClientSize.Height));
                int stackOffset = 0;
                foreach (var t in toasts)
                {
                    var host = t.HostForm;
                    if (host == null || host.IsDisposed) continue;
                    var hostX = screenBottomRight.X - host.Width - margin;
                    var hostY = screenBottomRight.Y - host.Height - margin - stackOffset;
                    try { host.Location = new Point(hostX, hostY); } catch { }
                    stackOffset += host.Height + margin;
                }
            }
            catch { }
        }

        /// <summary>
        /// Clear all toasts immediately.
        /// </summary>
        public void ClearAll()
        {
            if (owner.InvokeRequired)
            {
                owner.BeginInvoke(new Action(ClearAll));
                return;
            }

            lock (sync)
            {
                foreach (var e in toasts.ToArray())
                {
                    RemoveToastInternal(e);
                }
            }
        }

        public void Dispose()
        {
            ClearAll();
        }
    }
}
