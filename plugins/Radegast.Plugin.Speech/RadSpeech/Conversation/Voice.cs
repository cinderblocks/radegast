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
using Radegast;

namespace RadegastSpeech.Conversation
{
    internal class Voice : Mode
    {
        private readonly VoiceConsole vTab;
        private System.Windows.Forms.ListView participants;
        private bool connected;

        #region State Change
        internal Voice(PluginControl pc)
            : base(pc)
        {
            Title = "voice";

            vTab = (VoiceConsole)control.instance.TabConsole.Tabs["voice"].Control;
            participants = vTab.participants;
        }

        internal override void Start()
        {
            vTab.VoiceConnected += Vtab_VoiceConnected;
            vTab.VoiceDisconnected += Vtab_VoiceDisconnected;
            vTab.ParticipantJoined += Vtab_ParticipantJoined;
            vTab.chkVoiceEnable.CheckStateChanged += chkVoiceEnable_CheckStateChanged;
            SayEnabled();
        }

        internal override void Stop()
        {
            vTab.VoiceConnected -= Vtab_VoiceConnected;
            vTab.VoiceDisconnected -= Vtab_VoiceDisconnected;
            vTab.ParticipantJoined -= Vtab_ParticipantJoined;
            vTab.chkVoiceEnable.CheckStateChanged -= chkVoiceEnable_CheckStateChanged;
        }
        #endregion

        #region Sessions

        private void Vtab_VoiceConnected()
        {
            connected = true;
            control.talker.Say("Voice connected.");
        }

        private void Vtab_VoiceDisconnected()
        {
            connected = false;
            control.talker.Say("Voice session closed.");
        }

        private void Vtab_ParticipantJoined(string name)
        {
            control.talker.SayMore(name + " is in voice range.");
        }

        #endregion

        private void chkVoiceEnable_CheckStateChanged(object sender, EventArgs e)
        {
            SayEnabled();
        }

        private void SayEnabled()
        {
            string msg = "Voice is ";
            if (vTab.chkVoiceEnable.Checked)
            {
                msg += connected ? "enabled and connected" : "enabled";
                Talker.SayMore(msg);
            }
            else
                Talker.SayMore("Voice is disabled.");
        }
    }
}
