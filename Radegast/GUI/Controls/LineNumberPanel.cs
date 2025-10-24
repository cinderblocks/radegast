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
using System.ComponentModel;

namespace Radegast
{
    /// <summary>
    /// Used in conjuction with ritch text box to display lone numbers
    /// </summary>
    [Browsable(true),
        Category("Containers"),
        Description("Used in conjuction with ritch text box to display lone numbers")]
    public class LineNumberPanel : Control
    {
        private RRichTextBox rtb;
        private readonly SolidBrush brush;

        /// <summary>
        /// Associated ritch text box control
        /// </summary>
        [Category("Behavior")]
        public RRichTextBox RTB
        {
            get
            {
                if (rtb == null)
                {
                    return new RRichTextBox();
                }
                else
                {
                    return rtb;
                }
            }

            set
            {
                if (rtb != null)
                {
                    rtb.VScroll -= rtb_InvalidateNumbers;
                    rtb.TextChanged -= rtb_InvalidateNumbers;
                    rtb.Resize -= rtb_InvalidateNumbers;
                }
                rtb = value;
                rtb.VScroll += rtb_InvalidateNumbers;
                rtb.TextChanged += rtb_InvalidateNumbers;
                rtb.Resize += rtb_InvalidateNumbers;
            }
        }

        private void rtb_InvalidateNumbers(object sender, EventArgs e)
        {
            Invalidate();
        }

        public LineNumberPanel()
        {
            SetStyle(
                ControlStyles.DoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint,
                true
            );

            UpdateStyles();
            brush = new SolidBrush(ForeColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (rtb == null)
            {
                return;
            }

            float font_height;
            Graphics g = e.Graphics;
            brush.Color = ForeColor;
            if (rtb.Lines.Length < 3)
            {
                font_height = rtb.Font.Height + 1.5f;
            }
            else
            {
                font_height = rtb.GetPositionFromCharIndex(rtb.GetFirstCharIndexFromLine(2)).Y - rtb.GetPositionFromCharIndex(rtb.GetFirstCharIndexFromLine(1)).Y;
            }

            Point pos = new Point(0, (int)(g.VisibleClipBounds.Y + font_height / 3));
            var first_index = rtb.GetCharIndexFromPosition(pos);
            var first_line = rtb.GetLineFromCharIndex(first_index);
            var first_line_y = 1 + rtb.GetPositionFromCharIndex(first_index).Y;

            int i = first_line;
            float y = 0;
            int total_lines = rtb.GetLineFromCharIndex(int.MaxValue) + 1;
            StringFormat align = new StringFormat(StringFormatFlags.NoWrap)
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center
            };
            int maxWidth = 0;

            while (y < g.VisibleClipBounds.Y + g.VisibleClipBounds.Height)
            {
                SizeF tSize = g.MeasureString(i.ToString(), rtb.Font);

                if (maxWidth < (int)tSize.Width)
                {
                    maxWidth = (int)tSize.Width;
                }

                y = first_line_y - 1 + font_height * (i - first_line - 1);
                var x = Width - (int)tSize.Width - 5;

                if (i <= total_lines)
                {
                    g.DrawString((i).ToString(), rtb.Font, brush, x, y);
                }
                i++;
            }

            maxWidth++;

            if (Width != (maxWidth + 13))
            {
                Width = maxWidth + 13;
                Invalidate();
            }
   
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            pevent.Graphics.Clear(BackColor);
        }
    }
}
