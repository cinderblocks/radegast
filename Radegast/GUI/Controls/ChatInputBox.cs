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

namespace Radegast
{
    public class ChatInputBox : TextBox
    {
        public static readonly string NewlineMarker = new string('\u00b6', 1);

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x302: //WM_PASTE
                    Paste();
                    break;

                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        public new void Paste()
        {
            Paste(Clipboard.GetText());
        }

        public new void Paste(string text)
        {
            base.Paste(text.Replace(Environment.NewLine, NewlineMarker));
        }


    }
}
