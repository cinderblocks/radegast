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
using Radegast;
using OpenMetaverse;

namespace Radegast
{
    public partial class IMTabWindow : UserControl
    {
        private RadegastInstance instance;
        private Radegast.Netcom netcom => instance.Netcom;
        private UUID target;
        private bool typing = false;
        private List<string> chatHistory = new List<string>();
        private int chatPointer;

        public IMTabWindow(RadegastInstance instance, UUID target, UUID session, string toName)
        {
            InitializeComponent();
            Disposed += IMTabWindow_Disposed;
            
            this.instance = instance;

            this.target = target;
            SessionId = session;
            TargetName = toName;

            TextManager = new IMTextManager(this.instance, new RichTextBoxPrinter(rtbIMText, autoScrollCB), IMTextManagerType.Agent, SessionId, toName);

            AddNetcomEvents();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void IMTabWindow_Disposed(object sender, EventArgs e)
        {
            CleanUp();
        }

        private void AddNetcomEvents()
        {
            netcom.ClientLoginStatus += netcom_ClientLoginStatus;
            netcom.ClientDisconnected += netcom_ClientDisconnected;
            instance.GlobalSettings.OnSettingChanged += GlobalSettings_OnSettingChanged;
        }

        private void RemoveNetcomEvents()
        {
            netcom.ClientLoginStatus -= netcom_ClientLoginStatus;
            netcom.ClientDisconnected -= netcom_ClientDisconnected;
            instance.GlobalSettings.OnSettingChanged -= GlobalSettings_OnSettingChanged;
        }

        void GlobalSettings_OnSettingChanged(object sender, SettingsEventArgs e)
        {
        }

        private void netcom_ClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status != LoginStatus.Success) return;

            RefreshControls();
        }

        private void netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            RefreshControls();
        }

        public void CleanUp()
        {
            instance.TabConsole.RemoveTab(SessionId.ToString());
            TextManager.CleanUp();
            TextManager = null;
            RemoveNetcomEvents();
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            ProcessInput();
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

                if (!typing)
                {
                    netcom.SendIMStartTyping(target, SessionId);
                    typing = true;
                }
            }
            else
            {
                btnSend.Enabled = false;
                netcom.SendIMStopTyping(target, SessionId);
                typing = false;
            }
        }

        private void ProcessInput()
        {
            if (cbxInput.Text.Length == 0) return;

            chatHistory.Add(cbxInput.Text);
            chatPointer = chatHistory.Count;

            string msg = cbxInput.Text.Replace(ChatInputBox.NewlineMarker, "\n");

            if (instance.GlobalSettings["mu_emotes"].AsBoolean() && msg.StartsWith(":"))
            {
                msg = "/me " + msg.Substring(1);
            }

            if (instance.RLV.RestictionActive("sendim", target.ToString()))
                msg = "*** IM blocked by sender's viewer";

            netcom.SendInstantMessage(msg, target, SessionId);
            ClearIMInput();
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
            ProcessInput();
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

        private void tbtnProfile_Click(object sender, EventArgs e)
        {
            instance.MainForm.ShowAgentProfile(TargetName, target);
        }

        private void btnOfferTeleport_Click(object sender, EventArgs e)
        {
            instance.MainForm.AddNotification(new ntfSendLureOffer(instance, target));
        }

        private void btnPay_Click(object sender, EventArgs e)
        {
            (new frmPay(instance, target, TargetName, false)).ShowDialog();
        }

        public UUID TargetId
        {
            get => target;
            set => target = value;
        }

        public string TargetName { get; set; }

        public UUID SessionId { get; set; }

        public IMTextManager TextManager { get; set; }

        private void cbxInput_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible) cbxInput.Focus();
        }

        private void cbxInput_SizeChanged(object sender, EventArgs e)
        {
            pnlChatInput.Height = cbxInput.Height + 9;
        }

        private void cbAlwaysDing_CheckedChanged(object sender, EventArgs e)
        {
            TextManager.DingOnAllIncoming = ((CheckBox)sender).Checked;
        }
    }
}
