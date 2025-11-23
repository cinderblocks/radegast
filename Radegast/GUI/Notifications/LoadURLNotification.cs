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
using OpenMetaverse;

namespace Radegast
{
    public partial class ntfLoadURL : Notification
    {
        private readonly LoadUrlEventArgs ev;
        private readonly RadegastInstanceForms instance;

        public ntfLoadURL(RadegastInstanceForms instance, LoadUrlEventArgs e)
        {
            InitializeComponent();
            Disposed += ntfLoadURL_Disposed;
            
            ev = e;
            this.instance = instance;

            instance.Names.NameUpdated += Avatars_UUIDNameReply;

            SetText();

            // Fire off event
            NotificationEventArgs args = new NotificationEventArgs(instance) {Text = rtbText.Text};
            args.Buttons.Add(btnGoTo);
            args.Buttons.Add(btnCancel);
            FireNotificationCallback(args);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void ntfLoadURL_Disposed(object sender, EventArgs e)
        {
            instance.Names.NameUpdated -= Avatars_UUIDNameReply;
        }

        private void Avatars_UUIDNameReply(object sender, UUIDNameReplyEventArgs e)
        {
            if (!e.Names.ContainsKey(ev.OwnerID)) return;

            instance.Names.NameUpdated -= Avatars_UUIDNameReply;

            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => Avatars_UUIDNameReply(sender, e)));
                return;
            }

            SetText();
        }


        public void SetText()
        {
            rtbText.Text = string.Format("Do you want to load web page?{0}{1}From object: {2}, owner: {3}",
                Environment.NewLine + ev.URL +Environment.NewLine + Environment.NewLine,
                ev.Message + Environment.NewLine + Environment.NewLine,
                ev.ObjectName,
                instance.Names.Get(ev.OwnerID)
            );

            // Accessible metadata
            InitializeAccessibleMetadata("Load URL", rtbText.Text);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            instance.MainForm.RemoveNotification(this);
        }

        private void btnGoTo_Click(object sender, EventArgs e)
        {
            instance.MainForm.ProcessLink(ev.URL);
            instance.MainForm.RemoveNotification(this);
        }

        private void rtbText_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            instance.MainForm.ProcessLink(e.LinkText);
        }

        private void ntfLoadURL_ParentChanged(object sender, EventArgs e)
        {
            if (Parent != null)
            {
                rtbText.BackColor = Parent.BackColor;
            }
        }

        private void btnMute_Click(object sender, EventArgs e)
        {
            instance.Client.Self.UpdateMuteListEntry(MuteType.Object, ev.ObjectID, ev.ObjectName);
            instance.MainForm.RemoveNotification(this);
        }
    }
}
