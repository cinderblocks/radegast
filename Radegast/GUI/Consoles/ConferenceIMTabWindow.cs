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
using System.Windows.Forms;
using System.Threading;
using Radegast;
using OpenMetaverse;

namespace Radegast
{
    public partial class ConferenceIMTabWindow : UserControl
    {
        private RadegastInstance instance;
        private Radegast.Netcom netcom;
        private GridClient client;
        private List<UUID> participants = new List<UUID>();
        ManualResetEvent WaitForSessionStart = new ManualResetEvent(false);
        private List<string> chatHistory = new List<string>();
        private int chatPointer;

        public string SessionName { get; set; }

        public ConferenceIMTabWindow(RadegastInstance instance, UUID session, string sessionName)
        {
            InitializeComponent();
            Disposed += IMTabWindow_Disposed;

            this.instance = instance;
            client = instance.Client;
            SessionName = sessionName;
            netcom = this.instance.Netcom;

            SessionId = session;

            TextManager = new IMTextManager(this.instance, new RichTextBoxPrinter(rtbIMText), IMTextManagerType.Conference, SessionId, sessionName);
            
            // Callbacks
            netcom.ClientLoginStatus += netcom_ClientLoginStatus;
            netcom.ClientDisconnected += netcom_ClientDisconnected;
            instance.GlobalSettings.OnSettingChanged += GlobalSettings_OnSettingChanged;

            if (!client.Self.GroupChatSessions.ContainsKey(session))
            {
                _ = client.Self.ChatterBoxAcceptInvite(session);
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void IMTabWindow_Disposed(object sender, EventArgs e)
        {
            netcom.ClientLoginStatus -= netcom_ClientLoginStatus;
            netcom.ClientDisconnected -= netcom_ClientDisconnected;
            instance.GlobalSettings.OnSettingChanged -= GlobalSettings_OnSettingChanged;
            CleanUp();
        }

        private void netcom_ClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status != LoginStatus.Success) return;

            RefreshControls();
        }

        void GlobalSettings_OnSettingChanged(object sender, SettingsEventArgs e)
        {
        }

        private void netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            RefreshControls();
        }

        public void CleanUp()
        {
            TextManager.CleanUp();
            TextManager = null;
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (cbxInput.Text.Length == 0) return;

            SendMessage(cbxInput.Text);
            ClearIMInput();
        }

        private void cbxInput_TextChanged(object sender, EventArgs e)
        {
            RefreshControls();
        }

        private void RefreshControls()
        {
            if (!netcom.IsLoggedIn)
            {
                cbxInput.Enabled = false;
                btnSend.Enabled = false;
                return;
            }

            cbxInput.Enabled = true;

            if (cbxInput.Text.Length > 0)
            {
                btnSend.Enabled = true;
            }
            else
            {
                btnSend.Enabled = false;
            }
        }

        void ChatHistoryPrev()
        {
            if (chatPointer == 0) return;
            chatPointer--;
            if (chatHistory.Count > chatPointer)
            {
                cbxInput.Text = chatHistory[chatPointer];
                cbxInput.SelectionStart = cbxInput.Text.Length;
                cbxInput.SelectionLength = 0;
            }
        }

        void ChatHistoryNext()
        {
            if (chatPointer == chatHistory.Count) return;
            chatPointer++;
            if (chatPointer == chatHistory.Count)
            {
                cbxInput.Text = string.Empty;
                return;
            }
            cbxInput.Text = chatHistory[chatPointer];
            cbxInput.SelectionStart = cbxInput.Text.Length;
            cbxInput.SelectionLength = 0;
        }

        private void cbxInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                ChatHistoryPrev();
                return;
            }

            if (e.KeyCode == Keys.Down && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                ChatHistoryNext();
                return;
            }

            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            if (cbxInput.Text.Length == 0) return;

            string message = cbxInput.Text;

            SendMessage(message);

            ClearIMInput();
        }

        private void SendMessage(string msg)
        {
            if (msg == string.Empty) return;

            chatHistory.Add(msg);
            chatPointer = chatHistory.Count;

            if (instance.RLV.RestictionActive("sendim")) return;

            string message = msg.Replace(ChatInputBox.NewlineMarker, "\n");

            if (instance.GlobalSettings["mu_emotes"].AsBoolean() && message.StartsWith(":"))
            {
                message = "/me " + message.Substring(1);
            }

            if (message.Length > 1023)
            {
                message = message.Remove(1023);
            }
            if (message.Length > 0)
            {
                client.Self.InstantMessageGroup(SessionId, message);
            }
        }

        private void ClearIMInput()
        {
            cbxInput.Text = string.Empty;
        }

        public void SelectIMInput()
        {
            cbxInput.Select();
        }

        private void rtbIMText_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            instance.MainForm.ProcessLink(e.LinkText);
        }

        public UUID SessionId { get; set; }

        public IMTextManager TextManager { get; set; }

        private void cbxInput_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible) cbxInput.Focus();
        }

        private void cbxInput_SizeChanged(object sender, EventArgs e)
        {
            pnlChatInput.Height = cbxInput.Height + 7;
        }
    }
}
