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
using OpenMetaverse;

namespace Radegast
{
    public partial class ntfFriendshipOffer : Notification
    {
        private readonly RadegastInstanceForms instance;
        private readonly InstantMessage msg;

        public ntfFriendshipOffer(RadegastInstanceForms instance, InstantMessage msg)
            : base(NotificationType.FriendshipOffer)
        {
            InitializeComponent();
            this.instance = instance;
            this.msg = msg;

            txtHead.BackColor = instance.MainForm.NotificationBackground;
            txtHead.Text = $"{msg.FromAgentName} has offered you friendship.";
            txtMessage.BackColor = instance.MainForm.NotificationBackground;
            txtMessage.Text = msg.Message;
            btnYes.Focus();

            // Accessible metadata for assistive tech
            InitializeAccessibleMetadata(msg.FromAgentName + " friendship offer", txtHead.Text + " " + txtMessage.Text);

            // Fire off event
            NotificationEventArgs args = new NotificationEventArgs(instance) {Text = txtHead.Text};
            args.Buttons.Add(btnYes);
            args.Buttons.Add(btnNo);
            args.Buttons.Add(btnIgnore);
            FireNotificationCallback(args);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void btnYes_Click(object sender, EventArgs e)
        {
            instance.Client.Friends.AcceptFriendship(msg.FromAgentID, msg.IMSessionID);
            instance.MainForm.RemoveNotification(this);
        }

        private void btnNo_Click(object sender, EventArgs e)
        {
            instance.Client.Friends.DeclineFriendship(msg.FromAgentID, msg.IMSessionID);
            instance.MainForm.RemoveNotification(this);
        }

        private void btnIgnore_Click(object sender, EventArgs e)
        {
            instance.MainForm.RemoveNotification(this);
        }
    }
}
