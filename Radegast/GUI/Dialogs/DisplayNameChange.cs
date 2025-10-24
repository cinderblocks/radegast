﻿/*
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
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenMetaverse;


namespace Radegast
{
    public partial class DisplayNameChange : RadegastForm
    {
        public DisplayNameChange()
        {
            InitializeComponent();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        public DisplayNameChange(RadegastInstanceForms inst)
            : base(inst)
        {
            InitializeComponent();
            Disposed += DisplayNameChange_Disposed;
            AutoSavePosition = true;

            Client.Self.SetDisplayNameReply += Self_SetDisplayNameReply;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void DisplayNameChange_Disposed(object sender, EventArgs e)
        {
            Client.Self.SetDisplayNameReply += Self_SetDisplayNameReply;
        }

        private void Self_SetDisplayNameReply(object sender, SetDisplayNameReplyEventArgs e)
        {
            UpdateStatus(e.Status == 200
                ? $"Name successfully changed to: {e.DisplayName.DisplayName}"
                : $"Update failed: {e.Reason}");
        }

        private void UpdateStatus(string msg)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !Instance.MonoRuntime)
                {
                    BeginInvoke(new MethodInvoker(() => UpdateStatus(msg)));
                }
                return;
            }

            lblStatus.Text = msg;
            Instance.TabConsole.DisplayNotificationInChat(msg, ChatBufferTextStyle.Invisible);

        }

        private void StartDisplayNameChange(string name)
        {
            Task.Run(async () =>
                {
                    await Client.Avatars.GetDisplayNames(new List<UUID>() { Client.Self.AgentID },
                        (success, names, badIDs) =>
                        {
                            if (!success || names.Length < 1)
                            {
                                UpdateStatus("Failed to get current name");
                            }
                            else
                            {
                                Client.Self.SetDisplayName(names[0].DisplayName, name);
                            }
                        }
                    );
                }
            );
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (txtDN1.Text != txtDN2.Text)
            {
                UpdateStatus("Name mismatch");
                return;
            }

            if (txtDN1.Text.Length == 0)
            {
                UpdateStatus("Cannot chose an empty display name");
                return;
            }

            UpdateStatus("Requested display name update...");
            StartDisplayNameChange(txtDN1.Text);

        }

        private void button3_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            UpdateStatus("Requested display name reset...");
            StartDisplayNameChange(string.Empty);
        }
    }
}
