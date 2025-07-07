/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
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
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class BanGroupMember : RadegastForm
    {
        AvatarPicker picker;
        RadegastInstance instance;
        Radegast.Netcom netcom;
        GroupDetails parent;

        Group group;

        public BanGroupMember(RadegastInstance instance, Group group, GroupDetails parent)
            :base(instance)
        {
            InitializeComponent();
            Disposed += GroupInvite_Disposed;
            AutoSavePosition = true;

            this.instance = instance;
            this.group = group;
            netcom = instance.Netcom;
            this.parent = parent;

            picker = new AvatarPicker(instance) { Dock = DockStyle.Fill };
            Controls.Add(picker);
            picker.SelectionChaged += picker_SelectionChaged;
            picker.BringToFront();
            
            netcom.ClientDisconnected += Netcom_ClientDisconnected;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        void picker_SelectionChaged(object sender, EventArgs e)
        {
            btnBan.Enabled = picker.SelectedAvatars.Count > 0;
        }

        void GroupInvite_Disposed(object sender, EventArgs e)
        {
            netcom.ClientDisconnected -= Netcom_ClientDisconnected;
            netcom = null;
            instance = null;
            picker.Dispose();
            Logger.DebugLog("Group picker disposed");
        }

        void Netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            ((Radegast.Netcom)sender).ClientDisconnected -= Netcom_ClientDisconnected;

            if (!instance.MonoRuntime || IsHandleCreated)
                BeginInvoke(new MethodInvoker(Close));
        }


        private void GroupInvite_Load(object sender, EventArgs e)
        {
            picker.txtSearch.Focus();
        }

        private void btnBan_Click(object sender, EventArgs e)
        {
            var toBan = picker.SelectedAvatars.Keys.ToList();
            if (toBan.Count > 0)
            {
                _ = Client.Groups.RequestBanAction(group.ID, GroupBanAction.Ban, toBan.ToArray(), (xs, xe) =>
                {
                    parent.RefreshBans();
                });
            }
            Close();
        }
    }
}
