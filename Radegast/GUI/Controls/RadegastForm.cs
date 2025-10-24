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
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Radegast
{
    [Browsable(false)]
    public class RadegastForm : Form
    {
        /// <summary>
        /// Indicates if position and size of the form should be saved
        /// </summary>
        [Browsable(true),
            Description("Indicates if position and size of the form should be saved"),
            Category("Behavior"),
            DefaultValue(false)
        ]
        public bool AutoSavePosition
        {
            get => autoSavePosition;

            set
            {
                if (!value)
                {
                    ClearSavedPosition();
                }
                autoSavePosition = value;
            }
        }
        private bool autoSavePosition = false;

        [Browsable(true),
            Description("Key used for saving this particular windows settings"),
            Category("Behavior"),
            DefaultValue(false)
        ]
        public virtual string SettingsKeyBase
        {
            get
            {
                if (settingsKeyBase == string.Empty)
                    return GetType().ToString();
                else
                    return settingsKeyBase;
            }

            set => settingsKeyBase = value;
        }
        private string settingsKeyBase = string.Empty;

        /// <summary>
        /// Instance of Radegast
        /// </summary>
        protected RadegastInstanceForms Instance { get; }

        /// <summary>
        /// Instance of OpenMetaverse's GridClient
        /// </summary>
        protected GridClient Client => Instance.Client;

        /// <summary>
        /// Instance of Netcom
        /// </summary>
        protected NetComForms NetCom => (NetComForms)Instance.NetCom;

        private System.Threading.Timer SettingsTimer;
        private const int SettingsTimerTimeout = 500;

        public RadegastForm()
        {
        }

        public RadegastForm(RadegastInstanceForms instance)
        {
            Instance = instance;
            instance.OnRadegastFormCreated(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (SettingsTimer != null)
                {
                    SettingsTimer.Dispose();
                    SettingsTimer = null;
                }
            }
            base.Dispose(disposing);
        }

        private void InitTimer()
        {
            SettingsTimer = new System.Threading.Timer(
                SettingsTimer_Tick,
                null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
        }

        protected void SavePosition()
        {
            if (Instance == null) return;

            Instance.GlobalSettings[GetSettingsKey("left")] = OSD.FromInteger(Left);
            Instance.GlobalSettings[GetSettingsKey("top")] = OSD.FromInteger(Top);
            Instance.GlobalSettings[GetSettingsKey("width")] = OSD.FromInteger(Width);
            Instance.GlobalSettings[GetSettingsKey("height")] = OSD.FromInteger(Height);
        }

        protected void ClearSavedPosition()
        {
            if (Instance == null) return;

            Instance.GlobalSettings.Remove(GetSettingsKey("left"));
            Instance.GlobalSettings.Remove(GetSettingsKey("top"));
            Instance.GlobalSettings.Remove(GetSettingsKey("width"));
            Instance.GlobalSettings.Remove(GetSettingsKey("height"));
        }

        protected void RestoreSavedPosition()
        {
            if (Instance == null) return;

            int left = Left, top = Top, width = Width, height = Height;

            if (Instance.GlobalSettings[GetSettingsKey("left")].Type != OSDType.Unknown)
                left = Instance.GlobalSettings[GetSettingsKey("left")].AsInteger();

            if (Instance.GlobalSettings[GetSettingsKey("top")].Type != OSDType.Unknown)
                top = Instance.GlobalSettings[GetSettingsKey("top")].AsInteger();

            if (Instance.GlobalSettings[GetSettingsKey("width")].Type != OSDType.Unknown)
                width = Instance.GlobalSettings[GetSettingsKey("width")].AsInteger();

            if (Instance.GlobalSettings[GetSettingsKey("height")].Type != OSDType.Unknown)
                height = Instance.GlobalSettings[GetSettingsKey("height")].AsInteger();

            Rectangle rec = Screen.GetWorkingArea(new Rectangle(left, top, width, height));

            if (left > rec.X + rec.Width - width) left = rec.X + rec.Width - width;
            if (top > rec.Y + rec.Height - height) top = rec.Y + rec.Height - height;
            if (left < rec.X) left = rec.X;
            if (top < rec.Y) top = rec.Y;
            if (width > rec.Width) width = rec.Width;
            if (height > rec.Height) height = rec.Height;

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        protected virtual string GetSettingsKey(string arg)
        {
            return SettingsKeyBase + "." + arg;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (AutoSavePosition)
                RestoreSavedPosition();
            InitTimer();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (AutoSavePosition && WindowState != FormWindowState.Minimized)
                TriggerSavePosition();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (AutoSavePosition && WindowState != FormWindowState.Minimized)
                TriggerSavePosition();
        }

        private void TriggerSavePosition()
        {
            SettingsTimer?.Change(
                SettingsTimerTimeout,
                System.Threading.Timeout.Infinite);
        }

        private void SettingsTimer_Tick(object e)
        {
            SavePosition();
        }
    }
}
