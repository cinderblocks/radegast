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
    public partial class ntfPermissions : Notification
    {
        private readonly UUID taskID;
        private readonly UUID itemID;
        private readonly string objectName;
        private string objectOwner;
        private readonly ScriptPermission questions;
        private readonly RadegastInstanceForms instance;
        private readonly Simulator simulator;


        public ntfPermissions(RadegastInstanceForms instance, Simulator simulator, UUID taskID, UUID itemID, string objectName, string objectOwner, ScriptPermission questions)
            : base(NotificationType.PermissionsRequest)
        {
            InitializeComponent();

            this.instance = instance;
            this.simulator = simulator;
            this.taskID = taskID;
            this.itemID = itemID;
            this.objectName = objectName;
            this.objectOwner = objectOwner;
            this.questions = questions;

            txtMessage.BackColor = instance.MainForm.NotificationBackground;
            txtMessage.Text = $"Object {objectName} owned by {objectOwner} is asking permission to {questions}. Do you accept?";

            // Accessible metadata
            InitializeAccessibleMetadata("Permissions Request", txtMessage.Text);

            // Fire off event
            NotificationEventArgs args = new NotificationEventArgs(instance) {Text = txtMessage.Text};
            args.Buttons.Add(btnYes);
            args.Buttons.Add(btnNo);
            args.Buttons.Add(btnMute);
            FireNotificationCallback(args);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                foreach (var b in new[] { btnYes, btnNo, btnMute })
                {
                    if (b == null) continue;
                    if (string.IsNullOrEmpty(b.AccessibleName)) b.AccessibleName = b.Text;
                    if (string.IsNullOrEmpty(b.AccessibleDescription)) b.AccessibleDescription = $"Press Enter to activate {b.Text}";
                }

                try { btnYes.Focus(); } catch { }
            }
            catch { }
        }

        private void btnYes_Click(object sender, EventArgs e)
        {
            instance.Client.Self.ScriptQuestionReply(simulator, itemID, taskID, questions);
            instance.RemoveNotification(this);
        }

        private void btnNo_Click(object sender, EventArgs e)
        {
            instance.Client.Self.ScriptQuestionReply(simulator, itemID, taskID, 0);
            instance.RemoveNotification(this);
        }

        private void btnMute_Click(object sender, EventArgs e)
        {
            instance.Client.Self.UpdateMuteListEntry(MuteType.Object, taskID, objectName);
            instance.RemoveNotification(this);
        }
    }
}
