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
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class GroupInvite : RadegastForm
    {
        private readonly AvatarPicker Picker;

        private readonly Group group;
        private Dictionary<UUID, GroupRole> roles;

        public GroupInvite(RadegastInstanceForms instance, Group group, Dictionary<UUID, GroupRole> roles)
            :base(instance)
        {
            InitializeComponent();
            Disposed += GroupInvite_Disposed;
            AutoSavePosition = true;

            this.roles = roles;
            this.group = group;

            Picker = new AvatarPicker(instance) { Dock = DockStyle.Fill };
            Controls.Add(Picker);
            Picker.SelectionChanged += PickerSelectionChanged;
            Picker.BringToFront();
            
            NetCom.ClientDisconnected += NetComClientDisconnected;

            cmbRoles.Items.Add(roles[UUID.Zero]);
            cmbRoles.SelectedIndex = 0;

            foreach (var role in roles.Where(role => role.Key != UUID.Zero))
                cmbRoles.Items.Add(role.Value);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void PickerSelectionChanged(object sender, EventArgs e)
        {
            btnIvite.Enabled = Picker.SelectedAvatars.Count > 0;
        }

        private void GroupInvite_Disposed(object sender, EventArgs e)
        {
            NetCom.ClientDisconnected -= NetComClientDisconnected;
            Picker.Dispose();
            Logger.DebugLog("Group picker disposed");
        }

        private void NetComClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            ((NetCom)sender).ClientDisconnected -= NetComClientDisconnected;

            if (!Instance.MonoRuntime || IsHandleCreated)
                BeginInvoke(new MethodInvoker(Close));
        }


        private void GroupInvite_Load(object sender, EventArgs e)
        {
            Picker.txtSearch.Focus();
        }

        private void btnInvite_Click(object sender, EventArgs e)
        {
            List<UUID> roleID = new List<UUID> {((GroupRole) cmbRoles.SelectedItem).ID};

            foreach (UUID key in Picker.SelectedAvatars.Keys)
            {
                Instance.Client.Groups.Invite(group.ID, roleID, key);
            }
            Close();
        }
    }
}
