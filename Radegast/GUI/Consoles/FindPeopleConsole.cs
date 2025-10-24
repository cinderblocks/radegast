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
    public partial class FindPeopleConsole : UserControl
    {
        private INetCom NetCom;
        private readonly GridClient Client;

        public event EventHandler SelectedIndexChanged;

        public FindPeopleConsole(IRadegastInstance instance, UUID queryID)
        {
            InitializeComponent();
            Disposed += FindPeopleConsole_Disposed;

            LLUUIDs = new Dictionary<string, UUID>();
            QueryID = queryID;

            NetCom = instance.NetCom;
            Client = instance.Client;

            // Callbacks
            Client.Directory.DirPeopleReply += Directory_DirPeopleReply;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void FindPeopleConsole_Disposed(object sender, EventArgs e)
        {
            Client.Directory.DirPeopleReply -= Directory_DirPeopleReply;
        }

        private void Directory_DirPeopleReply(object sender, DirPeopleReplyEventArgs e)
        {
            if (e.QueryID != QueryID) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Directory_DirPeopleReply(sender, e)));
                return;
            }

            lvwFindPeople.BeginUpdate();

            foreach (DirectoryManager.AgentSearchData person in e.MatchedPeople)
            {
                string fullName = person.FirstName + " " + person.LastName;
                LLUUIDs.Add(fullName, person.AgentID);

                ListViewItem item = lvwFindPeople.Items.Add(fullName);
                item.SubItems.Add(person.Online ? "Yes" : "No");
            }

            lvwFindPeople.Sort();
            lvwFindPeople.EndUpdate();
        }

        public void ClearResults()
        {
            LLUUIDs.Clear();
            lvwFindPeople.Items.Clear();
        }

        private void lvwFindPeople_SelectedIndexChanged(object sender, EventArgs e)
        {
            OnSelectedIndexChanged(e);
        }

        protected virtual void OnSelectedIndexChanged(EventArgs e)
        {
            SelectedIndexChanged?.Invoke(this, e);
        }

        public Dictionary<string, UUID> LLUUIDs { get; }

        public UUID QueryID { get; set; }

        public int SelectedIndex
        {
            get
            {
                if (lvwFindPeople.SelectedItems.Count == 0) return -1;

                return lvwFindPeople.SelectedIndices[0];
            }
        }

        public string SelectedName => lvwFindPeople.SelectedItems.Count == 0 ? string.Empty : lvwFindPeople.SelectedItems[0].Text;

        public bool SelectedOnlineStatus
        {
            get
            {
                if (lvwFindPeople.SelectedItems.Count == 0) return false;

                string yesNo = lvwFindPeople.SelectedItems[0].SubItems[0].Text;

                switch (yesNo)
                {
                    case "Yes":
                        return true;
                    case "No":
                    default:
                        return false;
                }
            }
        }

        public UUID SelectedAgentUUID
        {
            get
            {
                if (lvwFindPeople.SelectedItems.Count == 0) return UUID.Zero;

                string name = lvwFindPeople.SelectedItems[0].Text;
                return LLUUIDs[name];
            }
        }
    }
}
