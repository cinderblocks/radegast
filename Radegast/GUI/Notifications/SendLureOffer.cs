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
    public partial class ntfSendLureOffer : Notification
    {
        private readonly RadegastInstanceForms instance;
        private readonly UUID agentID;
        private readonly string agentName;

        public ntfSendLureOffer(RadegastInstanceForms instance, UUID agentID)
            : base(NotificationType.SendLureOffer)
        {
            InitializeComponent();
            this.instance = instance;
            this.agentID = agentID;

            txtHead.BackColor = instance.MainForm.NotificationBackground;

            agentName = instance.Names.GetAsync(agentID).GetAwaiter().GetResult();
            txtHead.Text = $"Offer a teleport to {agentName} with the following message: ";
            txtMessage.Text = $"Join me in {instance.Client.Network.CurrentSim.Name}!";
            txtMessage.BackColor = instance.MainForm.NotificationBackground;
            btnOffer.Focus();

            // Accessible metadata
            InitializeAccessibleMetadata($"Offer teleport to {agentName}", txtHead.Text + " " + txtMessage.Text);

            // Fire off event
            NotificationEventArgs args = new NotificationEventArgs(instance)
            {
                Text = txtHead.Text + Environment.NewLine + txtMessage.Text
            };
            args.Buttons.Add(btnOffer);
            args.Buttons.Add(btnCancel);
            FireNotificationCallback(args);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void btnOffer_Click(object sender, EventArgs e)
        {
            if (!instance.Client.Network.Connected) return;

            instance.Client.Self.SendTeleportLure(agentID, txtMessage.Text);

            instance.RemoveNotification(this);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            instance.RemoveNotification(this);
        }
    }
}
