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
    public class AutoSitPreferences
    {
        public UUID Primitive { get; set; }
        public string PrimitiveName { get; set; }
        public bool Enabled { get; set; }

        public static explicit operator AutoSitPreferences(OSD osd){
            AutoSitPreferences prefs = new AutoSitPreferences
            {
                Primitive = UUID.Zero,
                PrimitiveName = ""
            };

            if (osd == null || osd.Type != OSDType.Map) return prefs;

            OSDMap map = (OSDMap)osd;
            prefs.Primitive = map.ContainsKey("Primitive") ? map["Primitive"].AsUUID() : UUID.Zero;
            prefs.PrimitiveName = prefs.Primitive != UUID.Zero && map.ContainsKey("PrimitiveName") ? map["PrimitiveName"].AsString() : "";
            prefs.Enabled = map.ContainsKey("Enabled") && map["Enabled"].AsBoolean();

            return prefs;
        }

        public static implicit operator OSD(AutoSitPreferences prefs){
            return (OSDMap)prefs;
        }

        public static explicit operator OSDMap(AutoSitPreferences prefs)
        {
            OSDMap map = new OSDMap(3)
            {
                ["Primitive"] = prefs.Primitive,
                ["PrimitiveName"] = prefs.PrimitiveName,
                ["Enabled"] = prefs.Enabled
            };


            return map;
        }

        public static explicit operator AutoSitPreferences(Settings s){
            return (s != null && s.TryGetValue("AutoSit", out var value)) ? (AutoSitPreferences)value : new AutoSitPreferences();
        }
    }

    public class AutoSit : IDisposable
    {
        private const string c_label = "Use as Auto-Sit target";

        private readonly RadegastInstance m_instance;
        private Timer m_Timer;

        public AutoSit(RadegastInstance instance)
        {
            m_instance = instance;
            m_Timer = new Timer(10 * 1000);
            m_Timer.Elapsed += (sender, args) => {
                TrySit();
            };
            m_Timer.Enabled = false;
        }

        public void Dispose()
        {
            if (m_Timer != null)
            {
                m_Timer.Enabled = false;
                m_Timer.Dispose();
                m_Timer = null;
            }
        }

        public AutoSitPreferences Preferences
        {
            get => !m_instance.Client.Network.Connected ? null : (AutoSitPreferences)m_instance.ClientSettings;

            set {
                m_instance.ClientSettings["AutoSit"] = value;
                if (Preferences.Enabled)
                {
                    m_instance.RegisterContextAction(typeof(Primitive), c_label, PrimitiveContextAction);
                }
                else
                {
                    m_instance.DeregisterContextAction(typeof(Primitive), c_label);
                }
            }
        }

        public void PrimitiveContextAction(object sender, EventArgs e)
        {
            Primitive prim = (Primitive)sender;
            Preferences = new AutoSitPreferences
            {
                Primitive = prim.ID,
                PrimitiveName = prim.Properties != null ? prim.Properties.Name : "",
                Enabled = Preferences.Enabled
            };
            if (prim.Properties == null)
            {
                m_instance.Client.Objects.ObjectProperties += Objects_ObjectProperties;
                m_instance.Client.Objects.ObjectPropertiesUpdated += Objects_ObjectProperties;
            }
        }

        public void Objects_ObjectProperties(object sender, ObjectPropertiesEventArgs e)
        {
            if (e.Properties.ObjectID != Preferences.Primitive) return;

            Preferences = new AutoSitPreferences
            {
                Primitive = Preferences.Primitive,
                PrimitiveName = e.Properties.Name,
                Enabled = Preferences.Enabled
            };

            m_instance.Client.Objects.ObjectProperties -= Objects_ObjectProperties;
        }

        public void TrySit()
        {
            if (Preferences != null && m_instance.Client.Network.Connected)
            {
                if (Preferences.Enabled && Preferences.Primitive != UUID.Zero)
                {
                    if (!m_instance.State.IsSitting)
                    {
                        m_instance.State.SetSitting(true, Preferences.Primitive);
                        m_Timer.Enabled = true;
                    }
                    else
                    {
                        if (!m_instance.Client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(m_instance.Client.Self.SittingOn))
                        {
                            m_instance.State.SetSitting(false, UUID.Zero);
                        }
                    }
                }
                else
                {
                    m_Timer.Enabled = false;
                }
            }
            else
            {
                m_Timer.Enabled = false; // being lazy here, just letting timer elapse rather than disabling on client disconnect
            }
        }
    }
}
