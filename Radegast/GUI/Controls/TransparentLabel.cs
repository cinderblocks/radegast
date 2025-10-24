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

using System.Drawing;
using System.Windows.Forms;

namespace Radegast
{
    internal class TransparentLabel : Label
    {
        public TransparentLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20;
                return cp;
            }
        }

        //private void PaintParentBackground(Graphics g)
        //{
        //    if (Parent != null)
        //    {
        //        Rectangle rect = new Rectangle(Left, Top, Width, Height);

        //        g.TranslateTransform(-rect.X, -rect.Y);

        //        try
        //        {
        //            using (PaintEventArgs pea =
        //                        new PaintEventArgs(g, rect))
        //            {
        //                pea.Graphics.SetClip(rect);
        //                InvokePaintBackground(Parent, pea);
        //                InvokePaint(Parent, pea);
        //            }
        //        }
        //        finally
        //        {
        //           g.TranslateTransform(rect.X, rect.Y);
        //        }
        //    }
        //    else
        //    {
        //        g.FillRectangle(SystemBrushes.Control, ClientRectangle);
        //    }
        //}

        //protected override void OnPaint(PaintEventArgs e)
        //{
        //    PaintParentBackground(e.Graphics);
        //    e.Graphics.DrawString("foo", Font, new SolidBrush(ForeColor), 0, 0);
        //}

        //protected override void OnPaintBackground(PaintEventArgs pevent)
        //{
        //    PaintParentBackground(pevent.Graphics);
        //}

    }
}
