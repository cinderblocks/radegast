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
    public sealed class OfferTeleportAction : ContextAction
    {
        public OfferTeleportAction(RadegastInstance inst)
            : base(inst)
        {
            Label = "Offer Teleport";
            ContextType = typeof(Avatar);
        }
        public override bool IsEnabled(object target)
        {
            return true;
        }

        public override void OnInvoke(object sender, EventArgs e, object target)
        {
            UUID id = ToUUID(target);
            instance.MainForm.AddNotification(new ntfSendLureOffer(instance, id));
            instance.TabConsole.DisplayNotificationInChat(string.Format("Sent Lure to {0}", id));
        }
    }
}