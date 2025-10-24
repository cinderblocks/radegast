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
using System.ComponentModel;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class AvatarPicker : UserControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private UUID searchID;
        public ListView currentList;

        [Browsable(true)]
        public event EventHandler SelectionChanged;

        [Browsable(false)]
        public Dictionary<UUID, string> SelectedAvatars
        {
            get
            {
                Dictionary<UUID, string> res = new Dictionary<UUID,string>();
                if (currentList != null)
                {
                    foreach (ListViewItem item in currentList.SelectedItems)
                    {
                        res.Add((UUID)item.Tag, item.Text);
                    }
                }
                return res;
            }
        }

        public AvatarPicker(RadegastInstanceForms instance)
        {
            InitializeComponent();
            Disposed += AvatarPicker_Disposed;
            
            this.instance = instance;

            // events
            client.Avatars.AvatarPickerReply += Avatars_AvatarPickerReply;

            List<NearbyAvatar> nearAvatars = instance.TabConsole.NearbyAvatars;
            foreach (var avatar in nearAvatars)
            {
                string name = instance.Names.Get(avatar.ID, avatar.Name);
                lvwNear.Items.Add(new ListViewItem() { Text = avatar.Name, Tag = avatar.ID });
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void AvatarPicker_Disposed(object sender, EventArgs e)
        {
            client.Avatars.AvatarPickerReply -= Avatars_AvatarPickerReply;
        }

        private void Avatars_AvatarPickerReply(object sender, AvatarPickerReplyEventArgs e)
        {
            if (searchID == e.QueryID && e.Avatars.Count > 0)
            {
                BeginInvoke(new MethodInvoker(() =>
                    {
                        foreach (KeyValuePair<UUID, string> kvp in e.Avatars)
                        {
                            string name = instance.Names.Get(kvp.Key, kvp.Value);
                            lvwSearch.Items.Add(new ListViewItem(name) { Text = kvp.Value, Tag = kvp.Key });
                        }
                    }));
            }
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            if (txtSearch.Text.Length > 2)
            {
                searchID = UUID.Random();
                lvwSearch.Items.Clear();
                client.Avatars.RequestAvatarNameSearch(txtSearch.Text, searchID);
            }
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            btnSearch.Enabled = txtSearch.Text.Length > 2;
        }

        private void lvwNear_SelectedIndexChanged(object sender, EventArgs e)
        {
            currentList = lvwNear;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void lvwSearch_SelectedIndexChanged(object sender, EventArgs e)
        {
            currentList = lvwSearch;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void lvwSearch_SizeChanged(object sender, EventArgs e)
        {
            ListView view = (ListView)sender;
            view.Columns[0].Width = view.Width - 30;
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = e.Handled = true;
                if (btnSearch.Enabled)
                    btnSearch.PerformClick();
            }
        }

    }
}
