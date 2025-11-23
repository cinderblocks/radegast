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
using System.Windows.Forms;

namespace Radegast
{
    public partial class ntfGeneric : Notification
    {
        private readonly RadegastInstanceForms Instance;

        public ntfGeneric(RadegastInstanceForms instance, string msg)
            : base(NotificationType.Generic)
        {
            InitializeComponent();

            Instance = instance;
            txtMessage.BackColor = instance.MainForm.NotificationBackground;
            txtMessage.Text = msg.Replace("\n", "\r\n");
            if (msg.Length < 100)
            {
                txtMessage.ScrollBars = ScrollBars.None;
            }
            btnOk.Focus();

            // Set accessible metadata for assistive technologies
            InitializeAccessibleMetadata("Notification", txtMessage.Text);

            // Fire off event
            NotificationEventArgs args = new NotificationEventArgs(instance) {Text = txtMessage.Text};
            args.Buttons.Add(btnOk);
            FireNotificationCallback(args);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            Instance.MainForm.RemoveNotification(this);
        }
    }
}
