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
    public sealed class WalkToAction : ContextAction
    {
        public WalkToAction(RadegastInstanceForms inst)
            : base(inst)
        {
            Label = "Walk To";
            ContextType = typeof(Primitive);
        }
        public override bool Contributes(object o, Type type)
        {
            return type == typeof (Vector3) || type == typeof (Vector3d) || base.Contributes(o, type);
        }
        public override bool IsEnabled(object target)
        {
            return true;
        }
        public override void OnInvoke(object sender, EventArgs e, object target)
        {
            string pname = instance.Names.Get(ToUUID(target));
            if (pname == "(???) (???)") pname = "" + target;

            Simulator sim = null;
            Vector3 pos;

            if (TryFindPos(target, out sim, out pos))
            {
                instance.ShowNotificationInChat($"Walking to {pname}");
                instance.State.MoveTo(sim, pos, false);
            }
            else
            {
                instance.ShowNotificationInChat($"Could not locate {target}");
            }
        }
    }
}