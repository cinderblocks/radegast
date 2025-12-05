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
    public partial class MuteResidentForm : RadegastForm
    {
        private readonly AvatarPicker picker;
        private RadegastInstanceForms instance;
        private NetCom _netCom;

        public MuteResidentForm(RadegastInstanceForms instance)
            :base(instance)
        {
            InitializeComponent();
            Disposed += MuteResidentForm_Disposed;
            AutoSavePosition = true;

            this.instance = instance;
            _netCom = (NetCom)instance.NetCom;

            picker = new AvatarPicker(instance) { Dock = DockStyle.Fill };
            Controls.Add(picker);
            picker.SelectionChanged += picker_SelectionChanged;
            picker.BringToFront();
            
            _netCom.ClientDisconnected += NetComClientDisconnected;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void picker_SelectionChanged(object sender, EventArgs e)
        {
            btnMute.Enabled = picker.SelectedAvatars.Count > 0;
        }

        private void MuteResidentForm_Disposed(object sender, EventArgs e)
        {
            _netCom.ClientDisconnected -= NetComClientDisconnected;
            _netCom = null;
            instance = null;
            picker.Dispose();
            Logger.DebugLog("Group picker disposed");
        }

        private void NetComClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            ((NetCom)sender).ClientDisconnected -= NetComClientDisconnected;

            if (!instance.MonoRuntime || IsHandleCreated)
            {
                ThreadingHelper.SafeInvoke(this, Close, instance.MonoRuntime);
                return;
            }

            Close();
        }


        private void MuteResidentForm_Load(object sender, EventArgs e)
        {
            picker.txtSearch.Focus();
        }

        private void btnMute_Click(object sender, EventArgs e)
        {
            foreach (var kvp in picker.SelectedAvatars)
            {
                Client.Self.UpdateMuteListEntry(MuteType.Resident, kvp.Key, instance.Names.Get(kvp.Key, kvp.Value));
            }
            Close();
        }
    }
}
