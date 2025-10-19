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
using OpenMetaverse;

namespace Radegast
{
    public sealed class SitOnGroundAction : ContextAction
    {
        public SitOnGroundAction(RadegastInstance inst)
            : base(inst)
        {
            Label = "Sit on ground";
            ContextType = typeof(Vector3);
        }
        public override string LabelFor(object target)
        {
            if (Client.Self.Movement.SitOnGround)
            {
                return "Stand up";
            }
            return "Sit on ground";
        }
        public override bool IsEnabled(object target)
        {
            return true;
        }
        public override void OnInvoke(object sender, EventArgs e, object target)
        {
            if (Client.Self.Movement.SitOnGround)
            {
                instance.TabConsole.DisplayNotificationInChat("Standing up");
                Client.Self.Stand();
                return;
            }
            string pname = instance.Names.Get(ToUUID(target));
            if (pname == "(???) (???)") pname = "" + target;

            Simulator sim = null;
            Vector3 pos;

            if (TryFindPos(target, out sim, out pos))
            {
                instance.TabConsole.DisplayNotificationInChat(string.Format("Walking to {0}", pname));
                instance.State.MoveTo(sim, pos, false);
                //TODO wait until we get there

                double close = instance.State.WaitUntilPosition(StateManager.GlobalPosition(sim, pos), TimeSpan.FromSeconds(5), 1);
                if (close > 2)
                {
                    instance.TabConsole.DisplayNotificationInChat(
                        string.Format("Counldn't quite make it to {0}, now sitting", pname));
                }
                Client.Self.SitOnGround();
            }
            else
            {
                instance.TabConsole.DisplayNotificationInChat(string.Format("Could not locate {0}", target));
            }
        }
    }
}