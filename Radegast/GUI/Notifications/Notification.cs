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
using System.Threading;
using OpenMetaverse;

using System.Windows.Forms;

namespace Radegast
{

    /// <summary>
    /// Base class for all notifications (blue dialogs)
    /// </summary>
    public class Notification : UserControl
    {
        /// <summary>
        /// Notification type
        /// </summary>
        public NotificationType Type;

        /// <summary>
        /// Callback when blue dialog notification is displayed or closed
        /// </summary>
        /// <param name="sender">Notification dialog</param>
        /// <param name="e">Notification parameters</param>
        public delegate void NotificationCallback(object sender, NotificationEventArgs e);

        /// <summary>
        /// Callback when blue dialog notification button is clicked
        /// </summary>
        /// <param name="sender">Notification dialog</param>
        /// <param name="e">Notification parameters</param>
        public delegate void NotificationClickedCallback(object sender, EventArgs e, NotificationEventArgs notice);

        /// <summary>
        /// Fired when a notification is displayed
        /// </summary>
        public static event NotificationCallback OnNotificationDisplayed;

        public virtual bool SingleInstance => false;

        public Notification()
        {
            Type = NotificationType.Generic;
        }

        public Notification(NotificationType type)
        {
            Type = type;
        }

        protected void FireNotificationCallback(NotificationEventArgs e)
        {
            if (OnNotificationDisplayed == null) return;
            try
            {
                e.Type = Type;
                ThreadPool.QueueUserWorkItem(o => Notification_Displayed(this, e));
            }
            catch (Exception ex)
            {
                Console.WriteLine("" + ex);
                Logger.Log("Error executing notification callback", Helpers.LogLevel.Warning, ex);
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
                Console.WriteLine("" + ex);
                Logger.Log("Error executing notification displayed", Helpers.LogLevel.Warning, ex);
            }
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
        /// Instance of Radegast where the event occured
        /// </summary>
        public RadegastInstance Instance;

        /// <summary>
        /// Notification text
        /// </summary>
        public string Text = string.Empty;

        /// <summary>
        /// Buttons displayed on the notification window
        /// </summary>
        public List<Button> Buttons = new List<Button>();

        /// <summary>
        /// When set true the Dialog Can send the Close Event
        /// </summary>
        public bool CanClose = false;

        /// <summary>
        /// The button has been pushed once
        /// </summary>        
        public bool ButtonSelected = false;

        /// <summary>
        /// Create new event args object
        /// </summary>
        /// <param name="instance">Instance of Radegast notification is coming from</param>
        public NotificationEventArgs(RadegastInstance instance)
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
            if (ButtonSelected)
            {
                try
                {
                    OnNotificationClosed?.Invoke(this, this);
                }
                catch (Exception ex)
                {
                    Logger.Log("Error executing OnNotificationClosed " + Text,
                                             Helpers.LogLevel.Warning, ex);
                }
                if (!CanClose) Dispose();
            }
            CanClose = true;
        }

        /// <summary>
        /// Triggers the OnNotificationClicked event.
        /// </summary>
        internal void Notification_Click(object sender, EventArgs e)
        {
            if (ButtonSelected) throw new InvalidOperationException($"Clicked twice {Text} item: {sender}");
            ButtonSelected = true;
            try
            {
                OnNotificationClicked?.Invoke(sender, e, this);
            }
            catch (Exception ex)
            {
                Logger.Log("Error executing OnNotificationClicked", Helpers.LogLevel.Warning, ex);
            }
            if (CanClose)
            {
                Notification_Close();
            }
        }

        public void HookNotification(Notification notification)
        {
            Notice = notification;
            int hooked = 0;
            lock (notification)
            {
                notification.HandleDestroyed += Notification_Closing;
                lock (Buttons)
                    foreach (var button in Buttons)
                    {
                        button.Click += Notification_Click;
                        hooked++;
                    }
            }
            if (hooked == 0)
                throw new InvalidOperationException("No buttons found on Dialog " + Text);

        }

        public void Dispose()
        {
            if (!CanClose)
            {
                CanClose = true;
                ButtonSelected = true;
                Notification_Close();
            }
            lock (Notice)
            {
                Notice.HandleDestroyed -= Notification_Closing;
                lock (Buttons)
                    foreach (var button in Buttons)
                        button.Click -= Notification_Click;
            }
        }
    }
}
