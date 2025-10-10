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

#region Using directives

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

#endregion

namespace Radegast
{
    #region Control enums
    /// <summary>
    /// Specifies the style of underline that should be
    /// applied to the text.
    /// </summary>
    public enum UnderlineStyle
    {
        /// <summary>
        /// No underlining.
        /// </summary>
        None = 0,

        /// <summary>
        /// Standard underlining across all words.
        /// </summary>
        Normal = 1,

        /// <summary>
        /// Standard underlining broken between words.
        /// </summary>
        Word = 2,

        /// <summary>
        /// Double line underlining.
        /// </summary>
        Double = 3,

        /// <summary>
        /// Dotted underlining.
        /// </summary>
        Dotted = 4,

        /// <summary>
        /// Dashed underlining.
        /// </summary>
        Dash = 5,

        /// <summary>
        /// Dash-dot ("-.-.") underlining.
        /// </summary>
        DashDot = 6,

        /// <summary>
        /// Dash-dot-dot ("-..-..") underlining.
        /// </summary>
        DashDotDot = 7,

        /// <summary>
        /// Wave underlining (like spelling mistakes in MS Word).
        /// </summary>
        Wave = 8,

        /// <summary>
        /// Extra thick standard underlining.
        /// </summary>
        Thick = 9,

        /// <summary>
        /// Extra thin standard underlining.
        /// </summary>
        HairLine = 10,

        /// <summary>
        /// Double thickness wave underlining.
        /// </summary>
        DoubleWave = 11,

        /// <summary>
        /// Thick wave underlining.
        /// </summary>
        HeavyWave = 12,

        /// <summary>
        /// Extra long dash underlining.
        /// </summary>
        LongDash = 13
    }

    /// <summary>
    /// Specifies the color of underline that should be
    /// applied to the text.
    /// </summary>
    /// <remarks>
    /// I named these colors by their appearance, so some
    /// of them might not be what you expect. Please email
    /// me if you feel one should be changed.
    /// </remarks>
    public enum UnderlineColor
    {
        /// <summary>Black.</summary>
        Black = 0x00,

        /// <summary>None.</summary>
        None = 0x00,

        /// <summary>Blue.</summary>
        Blue = 0x10,

        /// <summary>Cyan.</summary>
        Cyan = 0x20,

        /// <summary>Lime green.</summary>
        LimeGreen = 0x30,

        /// <summary>Magenta.</summary>
        Magenta = 0x40,

        /// <summary>Red.</summary>
        Red = 0x50,

        /// <summary>Yellow.</summary>
        Yellow = 0x60,

        /// <summary>White.</summary>
        White = 0x70,

        /// <summary>DarkBlue.</summary>
        DarkBlue = 0x80,

        /// <summary>DarkCyan.</summary>
        DarkCyan = 0x90,

        /// <summary>Green.</summary>
        Green = 0xA0,

        /// <summary>Dark magenta.</summary>
        DarkMagenta = 0xB0,

        /// <summary>Brown.</summary>
        Brown = 0xC0,

        /// <summary>Olive green.</summary>
        OliveGreen = 0xD0,

        /// <summary>Dark gray.</summary>
        DarkGray = 0xE0,

        /// <summary>Gray.</summary>
        Gray = 0xF0
    }

    // Enum for possible RTF colors
    public enum RtfColor
    {
        Black, Maroon, Green, Olive, Navy, Purple, Teal, Gray, Silver,
        Red, Lime, Yellow, Blue, Fuchsia, Aqua, White
    }
    #endregion

    public class ExtendedRichTextBox : RichTextBox
    {
        #region Private fields and constructors
        private int _Updating = 0;
        private int _OldEventMask = 0;
        private ToolTip myToolTip;

        public ExtendedRichTextBox()
        {

            myToolTip = new ToolTip();
            ContextMenuStrip = new ContextMenuStrip();

            // Initialize default text and background colors
            textColor = RtfColor.Black;
            highlightColor = RtfColor.White;

            // Initialize the dictionary mapping color codes to definitions
            rtfColor = new Dictionary<RtfColor, string>
            {
                [RtfColor.Aqua] = RtfColorDef.Aqua,
                [RtfColor.Black] = RtfColorDef.Black,
                [RtfColor.Blue] = RtfColorDef.Blue,
                [RtfColor.Fuchsia] = RtfColorDef.Fuchsia,
                [RtfColor.Gray] = RtfColorDef.Gray,
                [RtfColor.Green] = RtfColorDef.Green,
                [RtfColor.Lime] = RtfColorDef.Lime,
                [RtfColor.Maroon] = RtfColorDef.Maroon,
                [RtfColor.Navy] = RtfColorDef.Navy,
                [RtfColor.Olive] = RtfColorDef.Olive,
                [RtfColor.Purple] = RtfColorDef.Purple,
                [RtfColor.Red] = RtfColorDef.Red,
                [RtfColor.Silver] = RtfColorDef.Silver,
                [RtfColor.Teal] = RtfColorDef.Teal,
                [RtfColor.White] = RtfColorDef.White,
                [RtfColor.Yellow] = RtfColorDef.Yellow
            };

            // Initialize the dictionary mapping default Framework font families to
            // RTF font families
            rtfFontFamily = new Dictionary<string, string>
            {
                [FontFamily.GenericMonospace.Name] = RtfFontFamilyDef.Modern,
                [FontFamily.GenericSansSerif.Name] = RtfFontFamilyDef.Swiss,
                [FontFamily.GenericSerif.Name] = RtfFontFamilyDef.Roman,
                [FF_UNKNOWN] = RtfFontFamilyDef.Unknown
            };

            // Get the horizontal and vertical resolutions at which the object is
            // being displayed
            using (Graphics _graphics = CreateGraphics())
            {
                xDpi = _graphics.DpiX;
                yDpi = _graphics.DpiY;
            }
        }
        #endregion

        #region Elements required to create an RTF document

        /* RTF HEADER
         * ----------
         *
         * \rtf[N]        - For text to be considered to be RTF, it must be enclosed in this tag.
         *                 rtf1 is used because the RichTextBox conforms to RTF Specification
         *                 version 1.
         * \ansi        - The character set.
         * \ansicpg[N]    - Specifies that unicode characters might be embedded. ansicpg1252
         *                 is the default used by Windows.
         * \deff[N]        - The default font. \deff0 means the default font is the first font
         *                 found.
         * \deflang[N]    - The default language. \deflang1033 specifies US English.
         * */
        private const string RTF_HEADER = @"{\rtf1\ansi\ansicpg1252\deff0\deflang1033";

        /* RTF DOCUMENT AREA
         * -----------------
         *
         * \viewkind[N]    - The type of view or zoom level. \viewkind4 specifies normal view.
         * \uc[N]        - The number of bytes corresponding to a Unicode character.
         * \pard        - Resets to default paragraph properties
         * \cf[N]        - Foreground color. \cf1 refers to the color at index 1 in
         *                 the color table
         * \f[N]        - Font number. \f0 refers to the font at index 0 in the font
         *                 table.
         * \fs[N]        - Font size in half-points.
         * */
        private const string RTF_DOCUMENT_PRE = @"\viewkind4\uc1\pard\cf1\f0\fs20";
        private const string RTF_DOCUMENT_POST = @"\cf0\fs17}";
        private string RTF_IMAGE_POST = @"}";

        // Represents an unknown font family
        private const string FF_UNKNOWN = "UNKNOWN";

        // Specifies the flags/options for the unmanaged call to the GDI+ method
        // Metafile.EmfToWmfBits().
        private enum EmfToWmfBitsFlags
        {

            // Use the default conversion
            EmfToWmfBitsFlagsDefault = 0x00000000,

            // Embedded the source of the EMF metafiel within the resulting WMF
            // metafile
            EmfToWmfBitsFlagsEmbedEmf = 0x00000001,

            // Place a 22-byte header in the resulting WMF file. The header is
            // required for the metafile to be considered placeable.
            EmfToWmfBitsFlagsIncludePlaceable = 0x00000002,

            // Don't simulate clipping by using the XOR operator.
            EmfToWmfBitsFlagsNoXORClip = 0x00000004
        };

        #endregion
        #region Windows API
        private const int WM_VSCROLL = 0x115;
        private const int WM_HSCROLL = 0x114;
        private const int SB_LINEUP = 0;
        private const int SB_LINEDOWN = 1;
        private const int SB_PAGEUP = 2;
        private const int SB_PAGEDOWN = 3;
        private const int SB_THUMBPOSITION = 4;
        private const int SB_THUMBTRACK = 5;
        private const int SB_TOP = 6;
        private const int SB_BOTTOM = 7;
        private const int SB_ENDSCROLL = 8;

        private const int WM_SETREDRAW = 0x0B;
        private const int EM_SETEVENTMASK = 0x0431;
        private const int EM_SETCHARFORMAT = 0x0444;
        private const int EM_GETCHARFORMAT = 0x043A;
        private const int EM_GETPARAFORMAT = 0x043D;
        private const int EM_SETPARAFORMAT = 0x0447;
        private const int EM_SETTYPOGRAPHYOPTIONS = 0x04CA;
        private const int CFM_UNDERLINETYPE = 0x800000;
        private const int CFM_BACKCOLOR = 0x4000000;
        private const int CFE_AUTOBACKCOLOR = 0x4000000;
        private const int SCF_SELECTION = 0x01;
        private const int PFM_ALIGNMENT = 0x08;
        private const int TO_ADVANCEDTYPOGRAPHY = 0x01;

        // These are the scroll bar constants.
        private const int SBS_HORIZ = 0;
        private const int SBS_VERT = 1;
        // Get which bits.
        private const int SIF_RANGE = 0x0001;
        private const int SIF_PAGE = 0x0002;
        private const int SIF_POS = 0x0004;
        private const int SIF_DISABLENOSCROLL = 0x0008;
        private const int SIF_TRACKPOS = 0x0010;
        private const int SIF_ALL = (SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS);

        [DllImport("user32", CharSet = CharSet.Auto)]
        private static extern int SendMessage(HandleRef hWnd, int msg, int wParam, int lParam);

        [DllImport("user32", CharSet = CharSet.Auto)]
        private static extern int SendMessage(HandleRef hWnd, int msg, int wParam, ref CHARFORMAT2 lParam);

        [DllImport("user32", CharSet = CharSet.Auto)]
        private static extern int SendMessage(HandleRef hWnd, int msg, int wParam, ref PARAFORMAT2 lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            HandleRef hWnd,
            [MarshalAs(UnmanagedType.LPWStr)]
            string pszSubAppName,
            [MarshalAs(UnmanagedType.LPWStr)]
            string pszSubIdList);

        /// <summary>
        /// The HideCaret function removes the caret from the screen.
        /// </summary>
        [DllImport("user32.dll")]
        protected static extern bool HideCaret(IntPtr hWnd);

        /// <summary>
        /// This will find the scroll position of the specified window.
        /// </summary>
        /// <param name="hWnd">the window to send the message to</param>
        /// <param name="nBar">the number of the sroll bar to look at</param>
        /// <param name="info"></param>
        /// <returns></returns>
        [DllImport("user32", CharSet = CharSet.Auto)]
        private static extern int GetScrollInfo(HandleRef hWnd, int nBar, ref SCROLLINFO info);

        /// <summary>
        /// Contains information about character formatting in a rich edit control.
        /// </summary>
        /// <remarks><see cref="CHARFORMAT"/> works with all Rich Edit versions.</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct CHARFORMAT
        {
            public int cbSize;
            public uint dwMask;
            public uint dwEffects;
            public int yHeight;
            public int yOffset;
            public int crTextColor;
            public byte bCharSet;
            public byte bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public char[] szFaceName;
        }

        /// <summary>
        /// Contains information about character formatting in a rich edit control.
        /// </summary>
        /// <remarks><see cref="CHARFORMAT2"/> requires Rich Edit 2.0.</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct CHARFORMAT2
        {
            public int cbSize;
            public uint dwMask;
            public uint dwEffects;
            public int yHeight;
            public int yOffset;
            public int crTextColor;
            public byte bCharSet;
            public byte bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public char[] szFaceName;
            public short wWeight;
            public short sSpacing;
            public int crBackColor;
            public int LCID;
            public uint dwReserved;
            public short sStyle;
            public short wKerning;
            public byte bUnderlineType;
            public byte bAnimation;
            public byte bRevAuthor;
        }

        /// <summary>
        /// Contains information about paragraph formatting in a rich edit control.
        /// </summary>
        /// <remarks><see cref="PARAFORMAT"/> works with all Rich Edit versions.</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct PARAFORMAT
        {
            public int cbSize;
            public uint dwMask;
            public short wNumbering;
            public short wReserved;
            public int dxStartIndent;
            public int dxRightIndent;
            public int dxOffset;
            public short wAlignment;
            public short cTabCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] rgxTabs;
        }

        /// <summary>
        /// Contains information about paragraph formatting in a rich edit control.
        /// </summary>
        /// <remarks><see cref="PARAFORMAT2"/> requires Rich Edit 2.0.</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct PARAFORMAT2
        {
            public int cbSize;
            public uint dwMask;
            public short wNumbering;
            public short wReserved;
            public int dxStartIndent;
            public int dxRightIndent;
            public int dxOffset;
            public short wAlignment;
            public short cTabCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] rgxTabs;
            public int dySpaceBefore;
            public int dySpaceAfter;
            public int dyLineSpacing;
            public short sStyle;
            public byte bLineSpacingRule;
            public byte bOutlineLevel;
            public short wShadingWeight;
            public short wShadingStyle;
            public short wNumberingStart;
            public short wNumberingStyle;
            public short wNumberingTab;
            public short wBorderSpace;
            public short wBorderWidth;
            public short wBorders;
        }

        /// <summary>
        /// Contains information the scroll bar positions.
        /// </summary>
        /// <remarks><see cref="PARAFORMAT"/> works with all Rich Edit versions.</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public int cbSize;
            public int fMask;
            public int nMin;
            public int nMax;
            public int nPage;
            public int nPos;
            public int nTrackPos;
        }

        // Definitions for colors in an RTF document
        private struct RtfColorDef
        {
            public const string Black = @"\red0\green0\blue0";
            public const string Maroon = @"\red128\green0\blue0";
            public const string Green = @"\red0\green128\blue0";
            public const string Olive = @"\red128\green128\blue0";
            public const string Navy = @"\red0\green0\blue128";
            public const string Purple = @"\red128\green0\blue128";
            public const string Teal = @"\red0\green128\blue128";
            public const string Gray = @"\red128\green128\blue128";
            public const string Silver = @"\red192\green192\blue192";
            public const string Red = @"\red255\green0\blue0";
            public const string Lime = @"\red0\green255\blue0";
            public const string Yellow = @"\red255\green255\blue0";
            public const string Blue = @"\red0\green0\blue255";
            public const string Fuchsia = @"\red255\green0\blue255";
            public const string Aqua = @"\red0\green255\blue255";
            public const string White = @"\red255\green255\blue255";
        }

        // Control words for RTF font families
        private struct RtfFontFamilyDef
        {
            public const string Unknown = @"\fnil";
            public const string Roman = @"\froman";
            public const string Swiss = @"\fswiss";
            public const string Modern = @"\fmodern";
            public const string Script = @"\fscript";
            public const string Decor = @"\fdecor";
            public const string Technical = @"\ftech";
            public const string BiDirect = @"\fbidi";
        }
        #endregion
        #region Property: SelectionUnderlineStyle
        /// <summary>
        /// Gets or sets the underline style to apply to the current selection or insertion point.
        /// </summary>
        /// <value>A <see cref="UnderlineStyle"/> that represents the underline style to
        /// apply to the current text selection or to text entered after the insertion point.</value>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public UnderlineStyle SelectionUnderlineStyle
        {
            get
            {
                CHARFORMAT2 fmt = new CHARFORMAT2();
                fmt.cbSize = Marshal.SizeOf(fmt);

                // Get the underline style
                SendMessage(new HandleRef(this, Handle), EM_GETCHARFORMAT, SCF_SELECTION, ref fmt);
                if ((fmt.dwMask & CFM_UNDERLINETYPE) == 0)
                {
                    return UnderlineStyle.None;
                }
                else
                {
                    byte style = (byte)(fmt.bUnderlineType & 0x0F);
                    return (UnderlineStyle)style;
                }
            }
            set
            {
                // Ensure we don't alter the color
                UnderlineColor color = SelectionUnderlineColor;

                // Ensure we don't show it if it shouldn't be shown
                if (value == UnderlineStyle.None)
                    color = UnderlineColor.Black;

                // Set the underline type
                CHARFORMAT2 fmt = new CHARFORMAT2();
                fmt.cbSize = Marshal.SizeOf(fmt);
                fmt.dwMask = CFM_UNDERLINETYPE;
                fmt.bUnderlineType = (byte)((byte)value | (byte)color);
                SendMessage(new HandleRef(this, Handle), EM_SETCHARFORMAT, SCF_SELECTION, ref fmt);
            }
        }

        #endregion
        #region Property: SelectionUnderlineColor
        /// <summary>
        /// Gets or sets the underline color to apply to the current selection or insertion point.
        /// </summary>    
        /// <value>A <see cref="UnderlineColor"/> that represents the underline color to
        /// apply to the current text selection or to text entered after the insertion point.</value>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public UnderlineColor SelectionUnderlineColor
        {
            get
            {
                CHARFORMAT2 fmt = new CHARFORMAT2();
                fmt.cbSize = Marshal.SizeOf(fmt);

                // Get the underline color
                SendMessage(new HandleRef(this, Handle), EM_GETCHARFORMAT, SCF_SELECTION, ref fmt);
                if ((fmt.dwMask & CFM_UNDERLINETYPE) == 0)
                {
                    return UnderlineColor.None;
                }
                else
                {
                    byte style = (byte)(fmt.bUnderlineType & 0xF0);
                    return (UnderlineColor)style;
                }
            }
            set
            {
                // If the an underline color of "None" is specified, remove underline effect
                if (value == UnderlineColor.None)
                {
                    SelectionUnderlineStyle = UnderlineStyle.None;
                }
                else
                {
                    // Ensure we don't alter the style
                    UnderlineStyle style = SelectionUnderlineStyle;

                    // Ensure we don't show it if it shouldn't be shown
                    if (style == UnderlineStyle.None)
                        value = UnderlineColor.Black;

                    // Set the underline color
                    CHARFORMAT2 fmt = new CHARFORMAT2();
                    fmt.cbSize = Marshal.SizeOf(fmt);
                    fmt.dwMask = CFM_UNDERLINETYPE;
                    fmt.bUnderlineType = (byte)((byte)style | (byte)value);
                    SendMessage(new HandleRef(this, Handle), EM_SETCHARFORMAT, SCF_SELECTION, ref fmt);
                }
            }
        }
        #endregion
        #region Method: BeginUpdate / EndUpdate
        /// <summary>
        /// Maintains performance while updating.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It is recommended to call this method before doing
        /// any major updates that you do not wish the user to
        /// see. Remember to call EndUpdate when you are finished
        /// with the update. Nested calls are supported.
        /// </para>
        /// <para>
        /// Calling this method will prevent redrawing. It will
        /// also setup the event mask of the underlying richedit
        /// control so that no events are sent.
        /// </para>
        /// </remarks>
        public void BeginUpdate()
        {
            // Deal with nested calls
            _Updating++;
            if (_Updating > 1)
                return;

            // Prevent the control from raising any events
            _OldEventMask = SendMessage(new HandleRef(this, Handle), EM_SETEVENTMASK, 0, 0);

            // Prevent the control from redrawing itself
            SendMessage(new HandleRef(this, Handle), WM_SETREDRAW, 0, 0);
        }

        /// <summary>
        /// Resumes drawing and event handling.
        /// </summary>
        /// <remarks>    
        /// This method should be called every time a call is made
        /// made to BeginUpdate. It resets the event mask to it's
        /// original value and enables redrawing of the control.
        /// </remarks>
        public void EndUpdate()
        {
            // Deal with nested calls
            _Updating--;
            if (_Updating > 0)
                return;

            // Allow the control to redraw itself
            SendMessage(new HandleRef(this, Handle), WM_SETREDRAW, 1, 0);

            // Allow the control to raise event messages
            SendMessage(new HandleRef(this, Handle), EM_SETEVENTMASK, 0, _OldEventMask);
            Invalidate();
        }

        /// <summary>
        /// Returns if updates to the controls are under way with BeginUpdate
        /// </summary>
        public bool Updating => _Updating > 0;

        #endregion
        #region ScrollBarDetails
        /// <summary>
        /// This scrolls the scroll bar down to the bottom of the window.
        /// </summary>
        public void ScrollToBottom()
        {
            SendMessage(new HandleRef(this, Handle), WM_VSCROLL, SB_BOTTOM, 0);
        }

        /// <summary>
        /// Scrolls the data up one page.
        /// </summary>
        public void ScrollPageUp()
        {
            SendMessage(new HandleRef(this, Handle), WM_VSCROLL, SB_PAGEUP, 0);
        }

        /// <summary>
        /// Scrolls the data down one page.
        /// </summary>
        public void ScrollPageDown()
        {
            SendMessage(new HandleRef(this, Handle), WM_VSCROLL, SB_PAGEDOWN, 0);
        }

        /// <summary>
        /// Scrolls the data up one page.
        /// </summary>
        public void ScrollLineUp(int num)
        {
            for (int i = 0; i < num; i++)
            {
                SendMessage(new HandleRef(this, Handle), WM_VSCROLL, SB_LINEUP, 0);
            }
        }

        /// <summary>
        /// Scrolls the data down one page.
        /// </summary>
        public void ScrollLineDown(int num)
        {
            for (int i = 0; i < num; i++)
            {
                SendMessage(new HandleRef(this, Handle), WM_VSCROLL, SB_LINEDOWN, 0);
            }
        }

        /// <summary>
        /// This is the information associated with the scroll bar, showing it's position
        /// and other details.
        /// </summary>
        /// <value>the scroll bar information</value>
        public ScrollBarInformation VerticalScrollInformation
        {
            get
            {
                SCROLLINFO info = new SCROLLINFO();
                info.cbSize = Marshal.SizeOf(info);
                info.fMask = SIF_ALL;
                int ret = GetScrollInfo(new HandleRef(this, Handle), SBS_VERT, ref info);
                return new ScrollBarInformation(info.nMin, info.nMax, info.nPage, info.nPos, info.nTrackPos);
            }
        }

        #endregion
        #region Rtf Privates

        // Not used in this application. Descriptions can be found with documentation
        // of Windows GDI function SetMapMode
        private const int MM_TEXT = 1;
        private const int MM_LOMETRIC = 2;
        private const int MM_HIMETRIC = 3;
        private const int MM_LOENGLISH = 4;
        private const int MM_HIENGLISH = 5;
        private const int MM_TWIPS = 6;

        // Ensures that the metafile maintains a 1:1 aspect ratio
        private const int MM_ISOTROPIC = 7;

        // Allows the x-coordinates and y-coordinates of the metafile to be adjusted
        // independently
        private const int MM_ANISOTROPIC = 8;

        // The number of hundredths of millimeters (0.01 mm) in an inch
        // For more information, see GetImagePrefix() method.
        private const int HMM_PER_INCH = 2540;

        // The number of twips in an inch
        // For more information, see GetImagePrefix() method.
        private const int TWIPS_PER_INCH = 1440;

        // The default text color
        private RtfColor textColor;

        // The default text background color
        private RtfColor highlightColor;

        // Dictionary that maps color enums to RTF color codes
        private Dictionary<RtfColor, string> rtfColor;

        // Dictionary that mapas Framework font families to RTF font families
        private Dictionary<string, string> rtfFontFamily;

        // The horizontal resolution at which the control is being displayed
        private float xDpi;

        // The vertical resolution at which the control is being displayed
        private float yDpi;

        #endregion
        #region Append RTF or Text to RichTextBox Contents

        /// <summary>
        /// Assumes the string passed as a paramter is valid RTF text and attempts
        /// to append it as RTF to the content of the control.
        /// </summary>
        /// <param name="_rtf"></param>
        public void AppendRtf(string _rtf)
        {

            // Move caret to the end of the text
            Select(TextLength, 0);

            // Since SelectedRtf is null, this will append the string to the
            // end of the existing RTF
            SelectedRtf = _rtf;
        }

        /// <summary>
        /// Assumes that the string passed as a parameter is valid RTF text and
        /// attempts to insert it as RTF into the content of the control.
        /// </summary>
        /// <remarks>
        /// NOTE: The text is inserted wherever the caret is at the time of the call,
        /// and if any text is selected, that text is replaced.
        /// </remarks>
        /// <param name="_rtf"></param>
        public void InsertRtf(string _rtf)
        {
            SelectedRtf = _rtf;
        }

        /// <summary>
        /// Appends the text using the current font, text, and highlight colors.
        /// </summary>
        /// <param name="_text"></param>
        public void AppendTextAsRtf(string _text)
        {
            AppendTextAsRtf(_text, Font);
        }

        /// <summary>
        /// Appends the text using the given font, and current text and highlight
        /// colors.
        /// </summary>
        /// <param name="_text"></param>
        /// <param name="_font"></param>
        public void AppendTextAsRtf(string _text, Font _font)
        {
            AppendTextAsRtf(_text, _font, textColor);
        }

        /// <summary>
        /// Appends the text using the given font and text color, and the current
        /// highlight color.
        /// </summary>
        /// <param name="_text"></param>
        /// <param name="_font"></param>
        /// <param name="_textColor"></param>
        public void AppendTextAsRtf(string _text, Font _font, RtfColor _textColor)
        {
            AppendTextAsRtf(_text, _font, _textColor, highlightColor);
        }

        /// <summary>
        /// Appends the text using the given font, text, and highlight colors. Simply
        /// moves the caret to the end of the RichTextBox's text and makes a call to
        /// insert.
        /// </summary>
        /// <param name="_text"></param>
        /// <param name="_font"></param>
        /// <param name="_textColor"></param>
        /// <param name="_backColor"></param>
        public void AppendTextAsRtf(string _text, Font _font, RtfColor _textColor, RtfColor _backColor)
        {
            // Move carret to the end of the text
            Select(TextLength, 0);

            InsertTextAsRtf(_text, _font, _textColor, _backColor);
        }

        #endregion
        #region RTF Insert Plain Text

        /// <summary>
        /// Inserts the text using the current font, text, and highlight colors.
        /// </summary>
        /// <param name="_text"></param>
        public void InsertTextAsRtf(string _text)
        {
            InsertTextAsRtf(_text, Font);
        }


        /// <summary>
        /// Inserts the text using the given font, and current text and highlight
        /// colors.
        /// </summary>
        /// <param name="_text"></param>
        /// <param name="_font"></param>
        public void InsertTextAsRtf(string _text, Font _font)
        {
            InsertTextAsRtf(_text, _font, textColor);
        }

        /// <summary>
        /// Inserts the text using the given font and text color, and the current
        /// highlight color.
        /// </summary>
        /// <param name="_text"></param>
        /// <param name="_font"></param>
        /// <param name="_textColor"></param>
        public void InsertTextAsRtf(string _text, Font _font, RtfColor _textColor)
        {
            InsertTextAsRtf(_text, _font, _textColor, highlightColor);
        }

        /// <summary>
        /// Inserts the text using the given font, text, and highlight colors. The
        /// text is wrapped in RTF codes so that the specified formatting is kept.
        /// You can only assign valid RTF to the RichTextBox.Rtf property, else
        /// an exception is thrown. The RTF string should follow this format ...
        ///
        /// {\rtf1\ansi\ansicpg1252\deff0\deflang1033{\fonttbl{[FONTS]}{\colortbl ;[COLORS]}}
        /// \viewkind4\uc1\pard\cf1\f0\fs20 [DOCUMENT AREA] }
        ///
        /// </summary>
        /// <remarks>
        /// NOTE: The text is inserted wherever the caret is at the time of the call,
        /// and if any text is selected, that text is replaced.
        /// </remarks>
        /// <param name="_text"></param>
        /// <param name="_font"></param>
        /// <param name="_textColor"></param>
        /// <param name="_backColor"></param>
        public void InsertTextAsRtf(string _text, Font _font, RtfColor _textColor, RtfColor _backColor)
        {

            StringBuilder _rtf = new StringBuilder();

            // Append the RTF header
            _rtf.Append(RTF_HEADER);

            // Create the font table from the font passed in and append it to the
            // RTF string
            _rtf.Append(GetFontTable(_font));

            // Create the color table from the colors passed in and append it to the
            // RTF string
            _rtf.Append(GetColorTable(_textColor, _backColor));

            // Create the document area from the text to be added as RTF and append
            // it to the RTF string.
            _rtf.Append(GetDocumentArea(_text, _font));

            SelectedRtf = _rtf.ToString();
        }

        /// <summary>
        /// Creates the Document Area of the RTF being inserted. The document area
        /// (in this case) consists of the text being added as RTF and all the
        /// formatting specified in the Font object passed in. This should have the
        /// form ...
        ///
        /// \viewkind4\uc1\pard\cf1\f0\fs20 [DOCUMENT AREA] }
        ///
        /// </summary>
        /// <param name="_text"></param>
        /// <param name="_font"></param>
        /// <returns>
        /// The document area as a string.
        /// </returns>
        private string GetDocumentArea(string _text, Font _font)
        {

            StringBuilder _doc = new StringBuilder();

            // Append the standard RTF document area control string
            _doc.Append(RTF_DOCUMENT_PRE);

            // Set the highlight color (the color behind the text) to the
            // third color in the color table. See GetColorTable for more details.
            _doc.Append(@"\highlight2");

            // If the font is bold, attach corresponding tag
            if (_font.Bold)
                _doc.Append(@"\b");

            // If the font is italic, attach corresponding tag
            if (_font.Italic)
                _doc.Append(@"\i");

            // If the font is strikeout, attach corresponding tag
            if (_font.Strikeout)
                _doc.Append(@"\strike");

            // If the font is underlined, attach corresponding tag
            if (_font.Underline)
                _doc.Append(@"\ul");

            // Set the font to the first font in the font table.
            // See GetFontTable for more details.
            _doc.Append(@"\f0");

            // Set the size of the font. In RTF, font size is measured in
            // half-points, so the font size is twice the value obtained from
            // Font.SizeInPoints
            _doc.Append(@"\fs");
            _doc.Append((int)Math.Round((2 * _font.SizeInPoints)));

            // Apppend a space before starting actual text (for clarity)
            _doc.Append(@" ");

            // Append actual text, however, replace newlines with RTF \par.
            // Any other special text should be handled here (e.g.) tabs, etc.
            _doc.Append(_text.Replace("\n", @"\par "));

            // RTF isn't strict when it comes to closing control words, but what the
            // heck ...

            // Remove the highlight
            _doc.Append(@"\highlight0");

            // If font is bold, close tag
            if (_font.Bold)
                _doc.Append(@"\b0");

            // If font is italic, close tag
            if (_font.Italic)
                _doc.Append(@"\i0");

            // If font is strikeout, close tag
            if (_font.Strikeout)
                _doc.Append(@"\strike0");

            // If font is underlined, cloes tag
            if (_font.Underline)
                _doc.Append(@"\ulnone");

            // Revert back to default font and size
            _doc.Append(@"\f0");
            _doc.Append(@"\fs20");

            // Close the document area control string
            _doc.Append(RTF_DOCUMENT_POST);

            return _doc.ToString();
        }
        #endregion
        #region RTF Insert Image

        /// <summary>
        /// Inserts an image into the RichTextBox. The image is wrapped in a Windows
        /// Format Metafile, because although Microsoft discourages the use of a WMF,
        /// the RichTextBox (and even MS Word), wraps an image in a WMF before inserting
        /// the image into a document. The WMF is attached in HEX format (a string of
        /// HEX numbers).
        ///
        /// The RTF Specification v1.6 says that you should be able to insert bitmaps,
        /// .jpegs, .gifs, .pngs, and Enhanced Metafiles (.emf) directly into an RTF
        /// document without the WMF wrapper. This works fine with MS Word,
        /// however, when you don't wrap images in a WMF, WordPad and
        /// RichTextBoxes simply ignore them. Both use the riched20.dll or msfted.dll.
        /// </summary>
        /// <remarks>
        /// NOTE: The image is inserted wherever the caret is at the time of the call,
        /// and if any text is selected, that text is replaced.
        /// </remarks>
        /// <param name="_image"></param>
        public virtual void InsertImage(Image _image)
        {

            StringBuilder _rtf = new StringBuilder();

            // Append the RTF header
            _rtf.Append(RTF_HEADER);

            // Create the font table using the RichTextBox's current font and append
            // it to the RTF string
            _rtf.Append(GetFontTable(Font));

            // Create the image control string and append it to the RTF string
            _rtf.Append(GetImagePrefix(_image));

            // Create the Windows Metafile and append its bytes in HEX format
            _rtf.Append(GetRtfImage(_image));

            // Close the RTF image control string
            _rtf.Append(RTF_IMAGE_POST);

            SelectedRtf = _rtf.ToString();
        }

        /// <summary>
        /// Creates the RTF control string that describes the image being inserted.
        /// This description (in this case) specifies that the image is an
        /// MM_ANISOTROPIC metafile, meaning that both X and Y axes can be scaled
        /// independently. The control string also gives the images current dimensions,
        /// and its target dimensions, so if you want to control the size of the
        /// image being inserted, this would be the place to do it. The prefix should
        /// have the form ...
        ///
        /// {\pict\wmetafile8\picw[A]\pich[B]\picwgoal[C]\pichgoal[D]
        ///
        /// where ...
        ///
        /// A    = current width of the metafile in hundredths of millimeters (0.01mm)
        ///        = Image Width in Inches * Number of (0.01mm) per inch
        ///        = (Image Width in Pixels / Graphics Context's Horizontal Resolution) * 2540
        ///        = (Image Width in Pixels / Graphics.DpiX) * 2540
        ///
        /// B    = current height of the metafile in hundredths of millimeters (0.01mm)
        ///        = Image Height in Inches * Number of (0.01mm) per inch
        ///        = (Image Height in Pixels / Graphics Context's Vertical Resolution) * 2540
        ///        = (Image Height in Pixels / Graphics.DpiX) * 2540
        ///
        /// C    = target width of the metafile in twips
        ///        = Image Width in Inches * Number of twips per inch
        ///        = (Image Width in Pixels / Graphics Context's Horizontal Resolution) * 1440
        ///        = (Image Width in Pixels / Graphics.DpiX) * 1440
        ///
        /// D    = target height of the metafile in twips
        ///        = Image Height in Inches * Number of twips per inch
        ///        = (Image Height in Pixels / Graphics Context's Horizontal Resolution) * 1440
        ///        = (Image Height in Pixels / Graphics.DpiX) * 1440
        ///    
        /// </summary>
        /// <remarks>
        /// The Graphics Context's resolution is simply the current resolution at which
        /// windows is being displayed. Normally it's 96 dpi, but instead of assuming
        /// I just added the code.
        ///
        /// According to Ken Howe at pbdr.com, "Twips are screen-independent units
        /// used to ensure that the placement and proportion of screen elements in
        /// your screen application are the same on all display systems."
        ///
        /// Units Used
        /// ----------
        /// 1 Twip = 1/20 Point
        /// 1 Point = 1/72 Inch
        /// 1 Twip = 1/1440 Inch
        ///
        /// 1 Inch = 2.54 cm
        /// 1 Inch = 25.4 mm
        /// 1 Inch = 2540 (0.01)mm
        /// </remarks>
        /// <param name="_image"></param>
        /// <returns></returns>
        private string GetImagePrefix(Image _image)
        {

            StringBuilder _rtf = new StringBuilder();

            // Calculate the current width of the image in (0.01)mm
            int picw = (int)Math.Round((_image.Width / xDpi) * HMM_PER_INCH);

            // Calculate the current height of the image in (0.01)mm
            int pich = (int)Math.Round((_image.Height / yDpi) * HMM_PER_INCH);

            // Calculate the target width of the image in twips
            int picwgoal = (int)Math.Round((_image.Width / xDpi) * TWIPS_PER_INCH);

            // Calculate the target height of the image in twips
            int pichgoal = (int)Math.Round((_image.Height / yDpi) * TWIPS_PER_INCH);

            // Append values to RTF string
            _rtf.Append(@"{\pict\wmetafile8");
            _rtf.Append(@"\picw");
            _rtf.Append(picw);
            _rtf.Append(@"\pich");
            _rtf.Append(pich);
            _rtf.Append(@"\picwgoal");
            _rtf.Append(picwgoal);
            _rtf.Append(@"\pichgoal");
            _rtf.Append(pichgoal);
            _rtf.Append(" ");

            return _rtf.ToString();
        }

        /// <summary>
        /// Use the EmfToWmfBits function in the GDI+ specification to convert a
        /// Enhanced Metafile to a Windows Metafile
        /// </summary>
        /// <param name="_hEmf">
        /// A handle to the Enhanced Metafile to be converted
        /// </param>
        /// <param name="_bufferSize">
        /// The size of the buffer used to store the Windows Metafile bits returned
        /// </param>
        /// <param name="_buffer">
        /// An array of bytes used to hold the Windows Metafile bits returned
        /// </param>
        /// <param name="_mappingMode">
        /// The mapping mode of the image. This control uses MM_ANISOTROPIC.
        /// </param>
        /// <param name="_flags">
        /// Flags used to specify the format of the Windows Metafile returned
        /// </param>
        [DllImportAttribute("gdiplus.dll")]
        private static extern uint GdipEmfToWmfBits(IntPtr _hEmf, uint _bufferSize,
            byte[] _buffer, int _mappingMode, EmfToWmfBitsFlags _flags);


        /// <summary>
        /// Wraps the image in an Enhanced Metafile by drawing the image onto the
        /// graphics context, then converts the Enhanced Metafile to a Windows
        /// Metafile, and finally appends the bits of the Windows Metafile in HEX
        /// to a string and returns the string.
        /// </summary>
        /// <param name="_image"></param>
        /// <returns>
        /// A string containing the bits of a Windows Metafile in HEX
        /// </returns>
        private string GetRtfImage(Image _image)
        {

            StringBuilder _rtf = null;

            // Used to store the enhanced metafile
            MemoryStream _stream = null;

            // Used to create the metafile and draw the image
            Graphics _graphics = null;

            // The enhanced metafile
            Metafile _metaFile = null;

            // Handle to the device context used to create the metafile
            IntPtr _hdc;

            try
            {
                _rtf = new StringBuilder();
                _stream = new MemoryStream();

                // Get a graphics context from the RichTextBox
                using (_graphics = CreateGraphics())
                {

                    // Get the device context from the graphics context
                    _hdc = _graphics.GetHdc();

                    // Create a new Enhanced Metafile from the device context
                    _metaFile = new Metafile(_stream, _hdc);

                    // Release the device context
                    _graphics.ReleaseHdc(_hdc);
                }
                
                // Get a graphics context from the Enhanced Metafile
                using (_graphics = Graphics.FromImage(_metaFile))
                {

                    // Draw the image on the Enhanced Metafile
                    _graphics.DrawImage(_image, new Rectangle(0, 0, _image.Width, _image.Height));

                }

                // Get the handle of the Enhanced Metafile
                IntPtr _hEmf = _metaFile.GetHenhmetafile();

                // A call to EmfToWmfBits with a null buffer return the size of the
                // buffer need to store the WMF bits. Use this to get the buffer
                // size.
                uint _bufferSize = GdipEmfToWmfBits(_hEmf, 0, null, MM_ANISOTROPIC,
                    EmfToWmfBitsFlags.EmfToWmfBitsFlagsDefault);

                // Create an array to hold the bits
                byte[] _buffer = new byte[_bufferSize];

                // A call to EmfToWmfBits with a valid buffer copies the bits into the
                // buffer an returns the number of bits in the WMF.
                uint _convertedSize = GdipEmfToWmfBits(_hEmf, _bufferSize, _buffer, MM_ANISOTROPIC,
                    EmfToWmfBitsFlags.EmfToWmfBitsFlagsDefault);

                // Append the bits to the RTF string
                foreach (var t in _buffer)
                {
                    _rtf.Append(String.Format("{0:X2}", t));
                }

                return _rtf.ToString();
            }
            finally
            {
                _graphics?.Dispose();
                _metaFile?.Dispose();
                _stream?.Close();
            }
        }

        #endregion
        #region RTF Helpers

        /// <summary>
        /// Creates a font table from a font object. When an Insert or Append
        /// operation is performed a font is either specified or the default font
        /// is used. In any case, on any Insert or Append, only one font is used,
        /// thus the font table will always contain a single font. The font table
        /// should have the form ...
        ///
        /// {\fonttbl{\f0\[FAMILY]\fcharset0 [FONT_NAME];}
        /// </summary>
        /// <param name="_font"></param>
        /// <returns></returns>
        private string GetFontTable(Font _font)
        {

            StringBuilder _fontTable = new StringBuilder();

            // Append table control string
            _fontTable.Append("{\\fonttbl{\\f0");
            _fontTable.Append("\\");

            // If the font's family corresponds to an RTF family, append the
            // RTF family name, else, append the RTF for unknown font family.
            _fontTable.Append(rtfFontFamily.TryGetValue(_font.FontFamily.Name, out var name)
                ? name : rtfFontFamily[FF_UNKNOWN]);

            // \fcharset specifies the character set of a font in the font table.
            // 0 is for ANSI.
            _fontTable.Append("\\fcharset0 ");

            // Append the name of the font
            _fontTable.Append(_font.Name);

            // Close control string
            _fontTable.Append(";}}");

            return _fontTable.ToString();
        }

        /// <summary>
        /// Creates a font table from the RtfColor structure. When an Insert or Append
        /// operation is performed, _textColor and _backColor are either specified
        /// or the default is used. In any case, on any Insert or Append, only three
        /// colors are used. The default color of the RichTextBox (signified by a
        /// semicolon (;) without a definition), is always the first color (index 0) in
        /// the color table. The second color is always the text color, and the third
        /// is always the highlight color (color behind the text). The color table
        /// should have the form ...
        ///
        /// {\colortbl ;[TEXT_COLOR];[HIGHLIGHT_COLOR];}
        ///
        /// </summary>
        /// <param name="_textColor"></param>
        /// <param name="_backColor"></param>
        /// <returns></returns>
        private string GetColorTable(RtfColor _textColor, RtfColor _backColor)
        {

            StringBuilder _colorTable = new StringBuilder();

            // Append color table control string and default font (;)
            _colorTable.Append(@"{\colortbl ;");

            // Append the text color
            _colorTable.Append(rtfColor[_textColor]);
            _colorTable.Append(@";");

            // Append the highlight color
            _colorTable.Append(rtfColor[_backColor]);
            _colorTable.Append(@";}\n");

            return _colorTable.ToString();
        }

        /// <summary>
        /// Called by overrided RichTextBox.Rtf accessor.
        /// Removes the null character from the RTF. This is residue from developing
        /// the control for a specific instant messaging protocol and can be ommitted.
        /// </summary>
        /// <param name="_originalRtf"></param>
        /// <returns>RTF without null character</returns>
        private string RemoveBadChars(string _originalRtf)
        {
            return _originalRtf.Replace("\0", "");
        }

        #endregion
        #region Printing
        //Convert the unit used by the .NET framework (1/100 inch)
        //and the unit used by Win32 API calls (twips 1/1440 inch)
        private const double anInch = 14.4;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CHARRANGE
        {
            public int cpMin;         //First character of range (0 for start of doc)
            public int cpMax;         //Last character of range (-1 for end of doc)
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FORMATRANGE
        {
            public IntPtr hdc;             //Actual DC to draw on
            public IntPtr hdcTarget;     //Target DC for determining text formatting
            public RECT rc;                //Region of the DC to draw to (in twips)
            public RECT rcPage;            //Region of the whole DC (page size) (in twips)
            public CHARRANGE chrg;         //Range of text to draw (see earlier declaration)
        }

        private const int WM_USER = 0x0400;
        private const int EM_FORMATRANGE = WM_USER + 57;

        [DllImport("USER32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        // Render the contents of the RichTextBox for printing
        //    Return the last character printed + 1 (printing start from this point for next page)
        public int Print(int charFrom, int charTo, PrintPageEventArgs e)
        {
            //Calculate the area to render and print
            RECT rectToPrint;
            rectToPrint.Top = (int)(e.MarginBounds.Top * anInch);
            rectToPrint.Bottom = (int)(e.MarginBounds.Bottom * anInch);
            rectToPrint.Left = (int)(e.MarginBounds.Left * anInch);
            rectToPrint.Right = (int)(e.MarginBounds.Right * anInch);

            //Calculate the size of the page
            RECT rectPage;
            rectPage.Top = (int)(e.PageBounds.Top * anInch);
            rectPage.Bottom = (int)(e.PageBounds.Bottom * anInch);
            rectPage.Left = (int)(e.PageBounds.Left * anInch);
            rectPage.Right = (int)(e.PageBounds.Right * anInch);

            IntPtr hdc = e.Graphics.GetHdc();

            FORMATRANGE fmtRange;
            fmtRange.chrg.cpMax = charTo;                //Indicate character from to character to
            fmtRange.chrg.cpMin = charFrom;
            fmtRange.hdc = hdc;                    //Use the same DC for measuring and rendering
            fmtRange.hdcTarget = hdc;             //Point at printer hDC
            fmtRange.rc = rectToPrint;             //Indicate the area on page to print
            fmtRange.rcPage = rectPage;            //Indicate size of page

            IntPtr res = IntPtr.Zero;

            IntPtr wparam = IntPtr.Zero;
            wparam = new IntPtr(1);

            //Get the pointer to the FORMATRANGE structure in memory
            IntPtr lparam = IntPtr.Zero;
            lparam = Marshal.AllocCoTaskMem(Marshal.SizeOf(fmtRange));
            Marshal.StructureToPtr(fmtRange, lparam, false);

            //Send the rendered data for printing
            res = SendMessage(Handle, EM_FORMATRANGE, wparam, lparam);

            //Free the block of memory allocated
            Marshal.FreeCoTaskMem(lparam);

            //Release the device context handle obtained by a previous call
            e.Graphics.ReleaseHdc(hdc);

            //Return last + 1 character printer
            return res.ToInt32();
        }
        #endregion
        #region WndProc
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_VSCROLL:
                    base.WndProc(ref m);
                    if ((m.WParam.ToInt32() & 0xffff) == SB_THUMBTRACK)
                    {
                        OnVScroll(EventArgs.Empty);
                    }
                    if ((m.WParam.ToInt32() & 0xffff) == SB_THUMBPOSITION)
                    {
                        OnVScroll(EventArgs.Empty);
                    }
                    break;

                case WM_HSCROLL:
                    base.WndProc(ref m);
                    if ((m.WParam.ToInt32() & 0xffff) == SB_THUMBTRACK)
                    {
                        OnHScroll(EventArgs.Empty);
                    }
                    if ((m.WParam.ToInt32() & 0xffff) == SB_THUMBPOSITION)
                    {
                        OnHScroll(EventArgs.Empty);
                    }
                    break;
                default:
                    base.WndProc(ref m);
                    break;
            }
        }
        #endregion
    }

    #region Scrollbar Information
    /// <summary>
    /// This class contains all the scroll bar information.
    /// </summary>
    public class ScrollBarInformation
    {
        /// <summary>
        /// Sets up an empty scroll bar information class.
        /// </summary>
        public ScrollBarInformation()
        {

        }

        /// <summary>
        /// This sets up the scroll bar information with all the basic details.
        /// </summary>
        /// <param name="min">the minimum size</param>
        /// <param name="max">the maximum size</param>
        /// <param name="page">the size of the page</param>
        /// <param name="pos">the position of the thingy</param>
        /// <param name="trackpos">this is updated while the scroll bar is wiggling up and down.</param>
        public ScrollBarInformation(int min, int max, int page, int pos, int trackpos)
        {
            Minimum = min;
            Maximum = max;
            Page = page;
            Position = pos;
            TrackPosition = trackpos;
        }

        /// <summary>
        /// Specifies the minimum scrolling position.
        /// </summary>
        /// <value>the minimum scrolling position</value>
        public int Minimum { get; set; } = 0;

        /// <summary>
        /// Specifies the maximum scrolling position.
        /// </summary>
        /// <value>the maximum scrolling position</value>
        public int Maximum { get; set; } = 0;

        /// <summary>
        /// Specifies the page size. A scroll bar uses this value to determine the
        /// appropriate size of the proportional scroll box.
        /// </summary>
        /// <value></value>
        public int Page { get; set; } = 0;

        /// <summary>
        /// The position of the thumb inside the scroll bar.
        /// </summary>
        /// <value></value>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Specifies the immediate position of a scroll box that the user is dragging.
        /// An application can retrieve this value while processing the SB_THUMBTRACK
        /// request code. An application cannot set the immediate scroll position; the
        /// SetScrollInfo function ignores this member.
        /// </summary>
        /// <value>the immediated position of the scroll box</value>
        public int TrackPosition { get; set; } = 0;
    }
    #endregion
}