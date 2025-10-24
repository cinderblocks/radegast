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
    public sealed class PointAtAction : ContextAction
    {
        public PointAtAction(RadegastInstanceForms inst)
            : base(inst)
        {
            Label = "Point at";
            ContextType = typeof(Primitive);
        }
        public override string LabelFor(object target)
        {
            if (instance.State.IsPointing)
            {
                return "Unpoint";
            }
            return base.LabelFor(target);
        }

        public override bool IsEnabled(object target)
        {
            return true;
        }
        public override void OnInvoke(object sender, EventArgs e, object target)
        {
            if (instance.State.IsPointing)
            {
                instance.ShowNotificationInChat("stopping pointing");
                instance.State.UnSetPointing();
                return;
            }
            string pname = instance.Names.Get(ToUUID(target));
            if (pname == "(???) (???)") pname = "" + target;
            Primitive prim = ToPrimitive(target);

            if (prim != null)
            {
                instance.ShowNotificationInChat($"Pointing at {pname}");
                instance.State.SetPointing(prim, 5);
            }
            else
            {
                instance.ShowNotificationInChat($"Could not locate {target}");
            }
        }
    }
}