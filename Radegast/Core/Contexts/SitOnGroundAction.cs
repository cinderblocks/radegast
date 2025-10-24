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

namespace Radegast
{
    public sealed class SitOnGroundAction : ContextAction
    {
        public SitOnGroundAction(RadegastInstanceForms inst)
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
                instance.ShowNotificationInChat("Standing up");
                Client.Self.Stand();
                return;
            }
            string pname = instance.Names.Get(ToUUID(target));
            if (pname == "(???) (???)") pname = "" + target;

            if (TryFindPos(target, out var sim, out var pos))
            {
                instance.ShowNotificationInChat($"Walking to {pname}");
                instance.State.MoveTo(sim, pos, false);
                //TODO wait until we get there

                double close = instance.State.WaitUntilPosition(StateManager.GlobalPosition(sim, pos), TimeSpan.FromSeconds(5), 1);
                if (close > 2)
                {
                    instance.ShowNotificationInChat(
                        $"Couldn't quite make it to {pname}, now sitting");
                }
                Client.Self.SitOnGround();
            }
            else
            {
                instance.ShowNotificationInChat($"Could not locate {target}");
            }
        }
    }
}