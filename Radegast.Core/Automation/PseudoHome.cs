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
using System.Timers;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Radegast.Automation
{
    public class PseudoHomePreferences
    {
        public bool Enabled { get; set; }
        public string Region { get; set; }
        public Vector3 Position { get; set; }
        public uint Tolerance { get; set; } = 256;

        public static explicit operator PseudoHomePreferences(OSD osd)
        {
            PseudoHomePreferences prefs = new PseudoHomePreferences();

            if (osd != null && osd.Type == OSDType.Map)
            {
                OSDMap map = (OSDMap)osd;
                prefs.Enabled = map.ContainsKey("Enabled") && map["Enabled"].AsBoolean();
                prefs.Region = map.ContainsKey("Region") ? map["Region"].AsString().Trim() : "";
                prefs.Position = map.ContainsKey("Position") ? map["Position"].AsVector3() : new Vector3();
                prefs.Tolerance = map.ContainsKey("Tolerance") ? Math.Min(256, Math.Max(1, map["Tolerance"].AsUInteger())) : 256;
            }

            return prefs;
        }

        public static implicit operator OSD(PseudoHomePreferences prefs)
        {
            return (OSDMap)prefs;
        }

        public static explicit operator OSDMap(PseudoHomePreferences prefs)
        {
            OSDMap map = new OSDMap(4)
            {
                ["Enabled"] = prefs != null && prefs.Enabled,
                ["Region"] = prefs?.Region != null ? prefs.Region.Trim() : string.Empty,
                ["Position"] = prefs?.Position ?? Vector3.Zero,
                ["Tolerance"] = prefs?.Tolerance ?? 256
            };


            return map;
        }

        public static explicit operator PseudoHomePreferences(Settings s)
        {
            return (s != null && s.TryGetValue("PseudoHome", out var value)) ? (PseudoHomePreferences)value : new PseudoHomePreferences
            {
                Enabled = false,
                Region = "",
                Position = Vector3.Zero,
                Tolerance = 256
            };
        }
    }

    public class PseudoHome
    {
        private readonly RadegastInstance m_instance;
        private readonly Timer m_Timer;

        public PseudoHome(RadegastInstance instance)
        {
            m_instance = instance;
            m_Timer = new Timer(5000);
            m_Timer.Elapsed += (sender, args) => {
                ETGoHome();
            };
            m_Timer.Enabled = false;
        }

        public PseudoHomePreferences Preferences
        {
            get => !m_instance.Client.Network.Connected ? null : (PseudoHomePreferences)m_instance.ClientSettings;

            set => m_instance.ClientSettings["PseudoHome"] = value;
        }

        public void ETGoHome()
        {
            if (Preferences != null && m_instance.Client.Network.Connected && Preferences.Region.Trim() != string.Empty)
            {
                if (Preferences.Enabled 
                    && (m_instance.Client.Network.CurrentSim.Name != Preferences.Region 
                        || Vector3.Distance(m_instance.Client.Self.SimPosition, Preferences.Position) > Preferences.Tolerance))
                {
                    m_instance.Client.Self.Teleport(Preferences.Region, Preferences.Position);
                    m_Timer.Enabled = true;
                }
                else
                {
                    m_Timer.Enabled = false;
                }
            }
            else
            {
                m_Timer.Enabled = false;
            }
        }
    }
}
