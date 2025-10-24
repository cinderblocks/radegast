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
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class MuteList : RadegastTabControl
    {
        public MuteList()
        {
            InitializeComponent();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        public MuteList(RadegastInstanceForms instance)
            : base(instance)
        {
            InitializeComponent();
            Disposed += MuteList_Disposed;
            RegisterClientEvents(client);
            instance.ClientChanged += instance_ClientChanged;
            RefreshMuteList();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(e.Client);
        }

        private void MuteList_Disposed(object sender, EventArgs e)
        {
            UnregisterClientEvents(client);
        }

        private void RegisterClientEvents(GridClient client)
        {
            if (null == client) return;
            client.Self.MuteListUpdated += Self_MuteListUpdated;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            if (null == client) return;
            client.Self.MuteListUpdated -= Self_MuteListUpdated;
        }

        private void Self_MuteListUpdated(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => Self_MuteListUpdated(sender, e)));
                }
                return;
            }

            RefreshMuteList();
        }

        public void RefreshMuteList()
        {
            try
            {
                lvMuteList.BeginUpdate();
                lvMuteList.Items.Clear();

                client.Self.MuteList.ForEach(me =>
                    {
                        string type = "";
                        switch (me.Type)
                        {
                            case MuteType.ByName: type = "Object by name"; break;
                            case MuteType.Object: type = "Object"; break;
                            case MuteType.Resident: type = "Resident"; break;
                            case MuteType.Group: type = "Group"; break;
                        }

                        var item = new ListViewItem(type) {Tag = me};
                        item.SubItems.Add(new ListViewItem.ListViewSubItem(item, me.Name));
                        lvMuteList.Items.Add(item);
                    }
                );
            }
            finally
            {
                lvMuteList.EndUpdate();
            }
        }

        private void lvMuteList_SizeChanged(object sender, EventArgs e)
        {
            chName.Width = Width - chType.Width - 130;
        }

        private void btnMuteByName_Click(object sender, EventArgs e)
        {
            txtMuteByName.Text = string.Empty;
            pnlMuteObjectByName.Visible = true;
            txtMuteByName.Select();
        }

        private void btnMuteObjectByName_Click(object sender, EventArgs e)
        {
            pnlMuteObjectByName.Visible = false;
            client.Self.UpdateMuteListEntry(MuteType.ByName, UUID.Zero, txtMuteByName.Text);
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            lvMuteList.Items.Clear();
            btnUnmute.Enabled = false;
            client.Self.RequestMuteList();
        }

        private void lvMuteList_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnUnmute.Enabled = lvMuteList.SelectedItems.Count != 0;
        }

        private void txtMuteByName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = e.Handled = true;
                btnMuteObjectByName.PerformClick();
            }
        }

        private void btnUnmute_Click(object sender, EventArgs e)
        {
            List<MuteEntry> toRemove = new List<MuteEntry>();
            foreach (ListViewItem item in lvMuteList.SelectedItems)
            {
                if (item.Tag is MuteEntry)
                {
                    toRemove.Add((MuteEntry)item.Tag);
                }
            }

            foreach (var item in toRemove)
            {
                client.Self.RemoveMuteListEntry(item.ID, item.Name);
            }
        }

        private void btnMuteResident_Click(object sender, EventArgs e)
        {
            new MuteResidentForm(instance).ShowDialog();
        }
    }
}
