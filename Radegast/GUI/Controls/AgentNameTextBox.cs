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
using System.ComponentModel;
using OpenMetaverse;

namespace Radegast
{
    public class AgentNameTextBox : TextBox
    {
        private UUID agentID;
        private static RadegastInstance Instance => RadegastInstanceForms.Instance;

        [Browsable(false)]
        public UUID AgentID
        {
            get => agentID;

            set
            {
                if (agentID == value) return;

                agentID = value;

                if (agentID == UUID.Zero)
                {
                    SetName(string.Empty);
                }
                else
                {
                    SetupHandlers();
                    string name = Instance.Names.Get(agentID);
                    SetName(name);
                }
            }
        }

        public AgentNameTextBox()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            Disposed += CleanupHandlers;
        }

        private void SetupHandlers()
        {
            if (Instance?.Names == null) return;
            Instance.Names.NameUpdated += Names_NameUpdated;
        }

        private void CleanupHandlers(object sender, EventArgs e)
        {
            if (Instance?.Names != null)
            {
                Instance.Names.NameUpdated -= Names_NameUpdated;
            }
        }

        private void Names_NameUpdated(object sender, UUIDNameReplyEventArgs e)
        {
            if (e.Names.ContainsKey(agentID))
            {
                SetName(e.Names[agentID]);
            }
        }

        private void SetName(string name)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { SetName(name); }));
                return;
            }

            Text = name;
        }
    }
}
