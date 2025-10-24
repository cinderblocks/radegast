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
using OpenMetaverse;

namespace RadegastSpeech.Conversation
{
    /// <summary>
    /// Represents a single IM conversation
    /// </summary>
    /// <remarks>This can be with an individual or a group</remarks>
    internal abstract class IMSession : Mode
    {
        protected UUID SessionID;
        protected UUID AgentID;
        private static int positionSlot = 0;
        private const int MAXSLOTS = 4;
        private const double SLOTRADIUS = 3.0;
        private static bool firstTime = true;
        private const string MUTE_IM = "mute this";
        private const string UNMUTE_IM = "unmute this";
        internal IMSession(PluginControl pc, UUID sess) : base(pc)
        {
            SessionID = sess;
            Title = "IM";

            // Each conversation a predictable different location.
            AssignSlot();

            if (firstTime)
            {
                firstTime = false;
                // Make a recognition grammar to improve accuracy.
                Listener.CreateGrammar("im",
                    new string[] {
                    MUTE_IM,
                    UNMUTE_IM });
            }
        }

        /// <summary>
        /// Assign IM conversations to different spatial locations.
        /// </summary>
        private void AssignSlot()
        {
            // We pick locations in a circle.
            int slot = positionSlot++;
            if (positionSlot >= MAXSLOTS) positionSlot = 0;

            // Compute the angle of the slot
            double angle = Math.PI * 2.0 * (double)slot / (double)MAXSLOTS;

            SessionPosition = new Vector3(
                (float)(SLOTRADIUS * Math.Sin(angle)),
                (float)(SLOTRADIUS * Math.Cos(angle)),
                0.0f);
        }
        internal override void Start()
        {
            base.Start();
            Listener.ActivateGrammar("im");
        }

        internal override void Stop()
        {
            base.Stop();
            Listener.DeactivateGrammar("im");
        }

        internal override bool Hear(string message)
        {
            switch (message.ToLower())
            {
            case MUTE_IM:
                isMuted = true;
                Talker.SayMore(Title + " is muted.");
                return true;
            case UNMUTE_IM:
                isMuted = false;
                Talker.SayMore(Title + " is un-muted.");
                return true;
            }
            return false;
        }

        internal abstract void OnMessage(UUID agent, string agentName, string message);
    }
}
