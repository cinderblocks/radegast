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
using SkiaSharp;

namespace Radegast
{
    public interface ITextPrinter
    {
        void InsertLink(string text);
        void InsertLink(string text, string hyperlink);
        void PrintText(string text);
        void PrintTextLine(string text);
        void PrintTextLine(string text, SKColor color);
        void ClearText();

        string Content { get; set; }
        SKColor ForeColor { get; set; }
        SKColor BackColor { get; set; }
        Font Font { get; set; }
    }
}
