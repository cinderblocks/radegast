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

using OpenMetaverse;

namespace Radegast
{
    public class RLVRule
    {
        public string Behaviour { set; get; }
        public string Option { set; get; }
        public string Param { set; get; }
        public RLVNotification? Notification { get; set; }
        public UUID Sender { set; get; }
        public string SenderName { set; get; }

        public override string ToString()
        {
            return string.Format("{0}: {1}:{2}{4}={3} [{4}]", SenderName, Behaviour, Option, Param, Sender, Notification != null ? $";${Notification}" : null);
        }

        public string ToNotifyString()
        {
            if (string.IsNullOrEmpty(Behaviour))
            {
                return $"/{Notification}";
            }

            string message = "/" + Behaviour;

            if (!string.IsNullOrEmpty(Option))
            {
                message += ":" + Option;
            }
            if (!string.IsNullOrEmpty(Param))
            {
                message += "=" + Param;
            }

            return message;
        }
    }
}

