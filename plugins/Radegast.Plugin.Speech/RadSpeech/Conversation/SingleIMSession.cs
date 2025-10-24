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

using OpenMetaverse;

namespace RadegastSpeech.Conversation
{
    /// <summary>
    /// Instant Message session with an individual
    /// </summary>
    internal class SingleIMSession : IMSession
    {
        internal SingleIMSession(PluginControl pc, string label, UUID agent, UUID session) : 
            base(pc, session)
        {
            AgentID = agent;
            Title = label;

            Talker.Say("New I.M. from "+Title, Talk.BeepType.Comm );
        }

        internal override void Start()
        {
            base.Start();
            Talker.SayMore("I.M. from "+Title );
        }

        internal override void OnMessage(UUID agentID, string agentName, string message)
        {
            if (isMuted) return;

            string who = FriendlyName(agentName, agentID);
            if (!amCurrent())
                who = "I.M. from " + who;

            Talker.SayIMPerson(
                who,
                message,
                SessionPosition,
                Talker.voices.VoiceFor(agentID));
        }

        internal override bool Hear(string message)
        {
            if (base.Hear(message)) return true;

            if (message.ToLower() == "disconnect")
            {
                Radegast.RadegastTab tab = control.instance.TabConsole.Tabs[Title];
                tab.Close();
                return true;
            }

            Talker.Say(control.converse.LoginName, message);
            control.instance.NetCom.SendInstantMessage(
                        message,
                        AgentID,
                        SessionID);
            return true;
        }


    }
}
