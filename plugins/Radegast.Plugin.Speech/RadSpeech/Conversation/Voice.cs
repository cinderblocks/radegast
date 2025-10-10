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
using Radegast;
using LibreMetaverse.Voice.Vivox;

namespace RadegastSpeech.Conversation
{
    class Voice : Mode
    {
        private VoiceConsole vTab;
        private System.Windows.Forms.ListView participants;
        private VoiceSession session;
 
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
            vTab.gateway.OnSessionCreate +=OnSessionCreate;
            vTab.gateway.OnSessionRemove += gateway_OnSessionRemove;
            vTab.chkVoiceEnable.CheckStateChanged += chkVoiceEnable_CheckStateChanged;
            SayEnabled();
        }

        internal override void Stop()
        {
            if (vTab.gateway != null)
            {
                vTab.gateway.OnSessionCreate -= OnSessionCreate;
                vTab.gateway.OnSessionRemove -= gateway_OnSessionRemove;
            }
            vTab.chkVoiceEnable.CheckStateChanged -= chkVoiceEnable_CheckStateChanged;
        }
#endregion

#region Sessions
        void OnSessionCreate(object sender, EventArgs e)
        {
            session = sender as VoiceSession;
            if (session != null) { control.talker.Say("Voice started in " + session.RegionName); }
            //            session.OnParticipantAdded += new EventHandler(session_OnParticipantAdded);
        }

        void gateway_OnSessionRemove(object sender, EventArgs e)
        {
            VoiceSession session = sender as VoiceSession;
            control.talker.Say("Voice session closed.");
//            session.OnParticipantAdded -= new EventHandler(session_OnParticipantAdded);
        }

        void session_OnParticipantAdded(object sender, EventArgs e)
        {
            if (sender is VoiceParticipant p)
            {
                string pName = p.Name ?? control.instance.Names.Get(p.ID);
                control.talker.SayMore(pName + " is in voice range.");
            }
        }

        #endregion

        void chkVoiceEnable_CheckStateChanged(object sender, EventArgs e)
        {
            SayEnabled();
        }

        void SayEnabled()
        {
            string msg = "Voice is ";
            if (vTab.chkVoiceEnable.Checked)
            {
                msg += "enabled";
                if (session != null)
                    msg += " in " + session.RegionName;
                Talker.SayMore(msg);
            }
            else
                Talker.SayMore("Voice is disabled.");
        }
    }
}
