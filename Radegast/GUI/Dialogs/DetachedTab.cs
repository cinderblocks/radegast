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
using System.Drawing;
using System.Windows.Forms;

namespace Radegast
{
    public partial class frmDetachedTab : RadegastForm
    {
        public bool DockedToMain;

        private readonly RadegastInstanceForms instance;
        private readonly RadegastTab tab;

        //For reattachment

        private int mainTop;
        private int mainLeft;
        private Size mainSize;

        public frmDetachedTab(RadegastInstanceForms instance, RadegastTab tab)
            :base(instance)
        {
            InitializeComponent();
            Disposed += frmDetachedTab_Disposed;

            this.instance = instance;
            this.tab = tab;
            ClientSize = tab.Control.Size;
            Controls.Add(tab.Control);
            tab.Control.Visible = true;
            tab.Control.BringToFront();
            tab.Control.TextChanged += Control_TextChanged;
            SettingsKeyBase = "tab_window_" + tab.Control.GetType().Name;
            AutoSavePosition = true;
            instance.MainForm.Move += MainForm_ResizeEnd;
            SaveMainFormPos();
            if (tab.Floater)
            {
                Owner = instance.MainForm;
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void Control_TextChanged(object sender, EventArgs e)
        {
            Text = tab.Control.Text;
        }

        private void frmDetachedTab_Disposed(object sender, EventArgs e)
        {
            instance.MainForm.Move -= MainForm_ResizeEnd;
        }

        private void SaveMainFormPos()
        {
            mainTop = instance.MainForm.Top;
            mainLeft = instance.MainForm.Left;
            mainSize = instance.MainForm.Size;
        }

        private void MainForm_ResizeEnd(object sender, EventArgs e)
        {
            if (DockedToMain)
            {
                Left += (instance.MainForm.Left - mainLeft);
                Top += (instance.MainForm.Top - mainTop);
            }
            SaveMainFormPos();
        }

        private void frmDetachedTab_FormClosing(object sender, FormClosingEventArgs e)
        {
            tab.Control.TextChanged -= Control_TextChanged;
            if (tab.Detached)
            {
                if (tab.CloseOnDetachedClose)
                    tab.Close();
                else
                    tab.AttachTo(ReattachStrip, ReattachContainer);
            }
        }

        public ToolStrip ReattachStrip { get; set; }

        public Panel ReattachContainer { get; set; }

        private void UpdatePos()
        {
            Rectangle mainRect = new Rectangle(new Point(mainLeft, mainTop), mainSize);
            Rectangle myRect = new Rectangle(new Point(Left, Top), Size);
            
            bool oldDocked = DockedToMain; 

            if (tab.Floater && mainRect == Rectangle.Union(mainRect, myRect))
            {
                DockedToMain = true;
                ShowInTaskbar = false;
                Text = tab.Label;
            }
            else
            {
                DockedToMain = false;
                if (!ShowInTaskbar)
                    ShowInTaskbar = true;
                Text = tab.Label + " - " + Properties.Resources.ProgramName;
            }
        }

        private void frmDetachedTab_Shown(object sender, EventArgs e)
        {
            UpdatePos();
        }

        private void frmDetachedTab_ResizeEnd(object sender, EventArgs e)
        {
            UpdatePos();
        }

        private void frmDetachedTab_Deactivate(object sender, EventArgs e)
        {
            if (tab.Floater)
            {
                Opacity = 0.5;
            }
        }

        private void frmDetachedTab_Activated(object sender, EventArgs e)
        {
            if (tab.Floater)
            {
                Opacity = 1;
            }
        }
    }
}