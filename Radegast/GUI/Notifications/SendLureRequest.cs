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
    public partial class ntfSendLureRequest : Notification
    {
        private readonly RadegastInstanceForms instance;
        private readonly UUID agentID;
        private readonly string agentName;

        public ntfSendLureRequest(RadegastInstanceForms instance, UUID agentID)
            : base(NotificationType.SendLureRequest)
        {
            InitializeComponent();
            this.instance = instance;
            this.agentID = agentID;

            txtHead.BackColor = instance.MainForm.NotificationBackground;

            agentName = instance.Names.GetAsync(agentID).GetAwaiter().GetResult();
            txtHead.Text = $"Request a teleport to {agentName}'s location with the following message:";
            txtMessage.BackColor = instance.MainForm.NotificationBackground;
            btnRequest.Focus();

            // Fire off event
            NotificationEventArgs args = new NotificationEventArgs(instance)
            {
                Text = txtHead.Text + Environment.NewLine + txtMessage.Text
            };
            args.Buttons.Add(btnRequest);
            args.Buttons.Add(btnCancel);
            FireNotificationCallback(args);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void btnTeleport_Click(object sender, EventArgs e)
        {
            if (!instance.Client.Network.Connected) return;

            instance.Client.Self.InstantMessage(instance.Client.Self.Name, agentID, txtMessage.Text,
                instance.Client.Self.AgentID ^ agentID, InstantMessageDialog.RequestLure, InstantMessageOnline.Offline,
                instance.Client.Self.SimPosition, instance.Client.Network.CurrentSim.ID, null);
            instance.RemoveNotification(this);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            instance.RemoveNotification(this);
        }
    }
}
