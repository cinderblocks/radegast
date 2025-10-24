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

namespace Radegast
{
    public class LoginOptions
    {
        public static bool IsPasswordMD5(string pass)
        {
            return pass.Length == 35 && pass.StartsWith("$1$");
        }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string FullName
        {
            get
            {
                if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(LastName))
                    return string.Empty;
                else
                    return FirstName + " " + LastName;
            }
        }

        public string Password { get; set; }

        public string MfaToken { get; set; }

        public string MfaHash { get; set; }

        public StartLocationType StartLocation { get; set; } = StartLocationType.Home;

        public string StartLocationCustom { get; set; } = string.Empty;

        public string Channel { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public Grid Grid { get; set; }

        public string GridCustomLoginUri { get; set; } = string.Empty;

        public LastExecStatus LastExecEvent { get; set; } = LastExecStatus.Normal;
    }
}
