/*
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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// Base class for all notifications (blue dialogs)
    /// </summary>
    public class Notification : UserControl, INotification
    {
        /// <summary>
        /// Notification type
        /// </summary>
        public NotificationType Type;

        private string accessibleDescription = string.Empty;
        private string accessibleName = string.Empty;

        /// <summary>
        /// Callback when blue dialog notification is displayed or closed
        /// </summary>
        public delegate void NotificationCallback(object sender, NotificationEventArgs e);

        /// <summary>
        /// Callback when blue dialog notification button is clicked
        /// </summary>
        public delegate void NotificationClickedCallback(object sender, EventArgs e, NotificationEventArgs notice);

        /// <summary>
        /// Fired when a notification is displayed
        /// </summary>
        public static event NotificationCallback OnNotificationDisplayed;

        public virtual bool SingleInstance => false;

        public Notification()
        {
            Type = NotificationType.Generic;
            InitializeAccessibility();
        }

        public Notification(NotificationType type)
        {
            Type = type;
            InitializeAccessibility();
        }

        private void InitializeAccessibility()
        {
            // Set default accessibility role for notifications
            AccessibleRole = AccessibleRole.Alert;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                // Recursively set accessible metadata for any buttons inside this notification
                Button firstButton = null;
                foreach (var btn in GetAllButtons(this))
                {
                    try
                    {
                        if (firstButton == null) firstButton = btn;

                        if (string.IsNullOrEmpty(btn.AccessibleName))
                            btn.AccessibleName = btn.Text ?? string.Empty;

                        if (string.IsNullOrEmpty(btn.AccessibleDescription))
                            btn.AccessibleDescription = $"Press Enter to activate {btn.Text}";

                        // Do not attach a MouseDown disabling handler — that can prevent Click handlers from firing
                        // Duplicate click protection is handled in NotificationEventArgs.Notification_Click.
                    }
                    catch { }
                }

                // Set focus to first button for keyboard users
                if (firstButton != null && !firstButton.IsDisposed && firstButton.CanFocus)
                {
                    try { firstButton.Focus(); } catch { }
                }
            }
            catch { }
        }

        protected static IEnumerable<Button> GetAllButtons(Control root)
        {
            if (root == null) yield break;
            foreach (Control c in root.Controls)
            {
                if (c is Button b)
                {
                    yield return b;
                }
                foreach (var child in GetAllButtons(c))
                    yield return child;
            }
        }

        protected void FireNotificationCallback(NotificationEventArgs e)
        {
            if (OnNotificationDisplayed == null) return;
            
            try
            {
                e.Type = Type;
                // Use Task.Run instead of ThreadPool to avoid blocking and get better exception handling
                _ = Task.Run(() => Notification_Displayed(this, e));
            }
            catch (Exception ex)
            {
                Logger.Warn("Error executing notification callback", ex);
            }
        }

        private void Notification_Displayed(Notification notification, NotificationEventArgs e)
        {
            try
            {
                e.HookNotification(this);
                OnNotificationDisplayed?.Invoke(notification, e);
            }
            catch (Exception ex)
            {
                Logger.Warn("Error executing notification displayed", ex);
            }
        }

        /// <summary>
        /// Accessible description of the notification
        /// </summary>
        public new string AccessibleDescription
        {
            get => accessibleDescription;
            set
            {
                if (accessibleDescription == value) return;
                accessibleDescription = value;
                UpdateAccessiblePropertiesAsync();
            }
        }

        /// <summary>
        /// Accessible name of the notification
        /// </summary>
        public new string AccessibleName
        {
            get => accessibleName;
            set
            {
                if (accessibleName == value) return;
                accessibleName = value;
                UpdateAccessiblePropertiesAsync();
            }
        }

        private void UpdateAccessiblePropertiesAsync()
        {
            // Use BeginInvoke for non-blocking UI updates
            if (!IsHandleCreated || IsDisposed) return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!IsDisposed)
                        {
                            base.AccessibleDescription = accessibleDescription;
                            base.AccessibleName = accessibleName;
                        }
                    }
                    catch (ObjectDisposedException) { }
                }));
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>
        /// Initialize accessible metadata for this notification.
        /// Sets AccessibleName and AccessibleDescription and updates the control.
        /// </summary>
        /// <param name="name">Short name for the notification (announced by screen readers)</param>
        /// <param name="description">Longer description / body text for assistive tech</param>
        public void InitializeAccessibleMetadata(string name, string description)
        {
            accessibleName = name ?? string.Empty;
            accessibleDescription = description ?? string.Empty;
            UpdateAccessiblePropertiesAsync();
        }
    }

    /// <summary>
    /// What kind of notification this is (blue dialog)
    /// </summary>
    public enum NotificationType
    {
        Generic,
        FriendshipOffer,
        GroupInvitation,
        GroupNotice,
        PermissionsRequest,
        ScriptDialog,
        Teleport,
        InventoryOffer,
        RequestLure,
        RegionRestart,
        SendLureRequest,
        SendLureOffer
    }

    /// <summary>
    /// Fired when blue dialog notification is displayed
    /// </summary>
    public class NotificationEventArgs : EventArgs, IDisposable
    {
        public event Notification.NotificationCallback OnNotificationClosed;
        public event Notification.NotificationClickedCallback OnNotificationClicked;

        /// <summary>
        /// The Notification form itself
        /// </summary>
        public Notification Notice;

        /// <summary>
        /// Type of notification
        /// </summary>
        public NotificationType Type;

        /// <summary>
        /// Instance of Radegast where the event occurred
        /// </summary>
        public RadegastInstanceForms Instance;

        /// <summary>
        /// Notification text
        /// </summary>
        public string Text = string.Empty;

        /// <summary>
        /// Buttons displayed on the notification window
        /// </summary>
        public List<Button> Buttons = new List<Button>();

        private bool canClose = false;
        private bool buttonSelected = false;
        private bool isDisposed = false;
        private readonly object syncLock = new object();

        /// <summary>
        /// Create new event args object
        /// </summary>
        /// <param name="instance">Instance of Radegast notification is coming from</param>
        public NotificationEventArgs(RadegastInstanceForms instance)
        {
            Instance = instance;
        }

        private void Notification_Closing(object sender, EventArgs e)
        {
            Notification_Close();
        }

        /// <summary>
        /// Triggers the OnNotificationClosing event.
        /// </summary>
        internal void Notification_Close()
        {
            lock (syncLock)
            {
                if (isDisposed || !buttonSelected) return;

                try
                {
                    OnNotificationClosed?.Invoke(this, this);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error executing OnNotificationClosed {Text}: ", ex);
                }

                canClose = true;
            }
        }

        /// <summary>
        /// Triggers the OnNotificationClicked event.
        /// </summary>
        internal void Notification_Click(object sender, EventArgs e)
        {
            lock (syncLock)
            {
                if (isDisposed) return;
                
                // Allow the same button to be clicked multiple times (removed the exception)
                if (buttonSelected && !canClose)
                {
                    return;
                }

                buttonSelected = true;
            }

            try
            {
                OnNotificationClicked?.Invoke(sender, e, this);
            }
            catch (Exception ex)
            {
                Logger.Warn("Error executing OnNotificationClicked", ex);
            }

            lock (syncLock)
            {
                if (canClose)
                {
                    Notification_Close();
                }
            }
        }

        public void HookNotification(Notification notification)
        {
            if (notification == null) return;

            lock (syncLock)
            {
                if (isDisposed) return;

                Notice = notification;
                
                try
                {
                    notification.HandleDestroyed += Notification_Closing;
                }
                catch (Exception ex)
                {
                    Logger.Error("Error hooking HandleDestroyed: ", ex);
                }

                int hooked = 0;
                if (Buttons != null)
                {
                    foreach (var button in Buttons)
                    {
                        try
                        {
                            button.Click += Notification_Click;
                            hooked++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Error hooking button click", ex);
                        }
                    }
                }

                if (hooked == 0)
                {
                    Logger.Debug($"No buttons found on Dialog {Text}");
                }
            }
        }

        public void Dispose()
        {
            lock (syncLock)
            {
                if (isDisposed) return;
                isDisposed = true;

                if (!canClose && buttonSelected)
                {
                    canClose = true;
                    Notification_Close();
                }

                if (Notice != null)
                {
                    try
                    {
                        Notice.HandleDestroyed -= Notification_Closing;
                    }
                    catch { }
                }

                if (Buttons != null)
                {
                    foreach (var button in Buttons)
                    {
                        try
                        {
                            button.Click -= Notification_Click;
                        }
                        catch { }
                    }
                }
            }
        }
    }
}
