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
using System.Drawing;
using System.Windows.Forms;

namespace Radegast
{
    public class Floater : RadegastForm
    {
        public Control Control, TrackedControl;
        public bool DockedToMain;

        int mainTop;
        int mainLeft;
        Size mainSize;
        Form parentForm;

        public Floater(RadegastInstance instance, Control control, Control trackedControl)
            : base(instance)
        {
            Control = control;
            TrackedControl = trackedControl;
            SettingsKeyBase = "tab_window_" + control.GetType().Name;
            AutoSavePosition = true;

            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            Text = Control.Text;
            
            TrackedControl.ParentChanged += Control_ParentChanged;
            Control.TextChanged += Control_TextChanged;
            Activated += Floater_Activated;
            Deactivate += Floater_Deactivate;
            ResizeEnd += Floater_ResizeEnd;
            Shown += Floater_Shown;
            Load += Floater_Load;

        }

        void Floater_Load(object sender, EventArgs e)
        {
            Control.Dock = DockStyle.Fill;
            ClientSize = Control.Size;
            Controls.Add(Control);
            SaveMainFormPos();
            parentForm.Move += parentForm_Move;
        }

        void Floater_Shown(object sender, EventArgs e)
        {
            UpdatePos();
        }

        void Floater_ResizeEnd(object sender, EventArgs e)
        {
            UpdatePos();
        }

        void Floater_Activated(object sender, EventArgs e)
        {
            Opacity = 1;
        }

        void Floater_Deactivate(object sender, EventArgs e)
        {
            if (DockedToMain)
            {
                Opacity = 0.75;
            }
        }

        void Control_TextChanged(object sender, EventArgs e)
        {
            Text = Control.Text;
        }

        void parentForm_Move(object sender, EventArgs e)
        {
            if (DockedToMain)
            {
                Left += (parentForm.Left - mainLeft);
                Top += (parentForm.Top - mainTop);
            }
            SaveMainFormPos();
            UpdatePos();
        }

        void Control_ParentChanged(object sender, EventArgs e)
        {
            if (parentForm != null)
            {
                parentForm.Move -= parentForm_Move;

            }
            SaveMainFormPos();
            if (parentForm != null)
            {
                Owner = parentForm;
                parentForm.Move += parentForm_Move;
                UpdatePos();
            }
        }

        void SaveMainFormPos()
        {
            parentForm = TrackedControl.FindForm();
            if (parentForm != null)
            {
                mainTop = parentForm.Top;
                mainLeft = parentForm.Left;
                mainSize = parentForm.Size;
            }
        }

        private void UpdatePos()
        {
            Rectangle mainRect = new Rectangle(new Point(mainLeft, mainTop), mainSize);
            Rectangle myRect = new Rectangle(new Point(Left, Top), Size);

            DockedToMain = mainRect == Rectangle.Union(mainRect, myRect);
        }
    }
}
