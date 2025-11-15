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
using System.Collections.Frozen;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using LibreMetaverse;
using LibreMetaverse.LslTools;

namespace Radegast
{

    public class RRichTextBox : ExtendedRichTextBox
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

        private bool suppressOnTextChanged = false;
        private bool syntaxHighLightEnabled = false;
        private readonly bool monoRuntime = false;
        private readonly string rtfHeader;
        private FrozenDictionary<string, LslSyntax.LslKeyword> Keywords;

        //  Tool tip related private members
        private System.Threading.Timer ttTimer;
        private ToolTip ttKeyWords;

        private static readonly Color StateColor = Color.FromArgb(127, 26, 77);
        private static readonly Color DataTypeColor = Color.FromArgb(125, 75, 255);
        private static readonly Color EventColor = Color.FromArgb(0, 77, 128);
        private static readonly Color ConstantColor = Color.FromArgb(26, 26, 127);
        private static readonly Color FunctionColor = Color.FromArgb(128, 0, 38);
        private static readonly Color FlowColor = Color.FromArgb(0, 0, 204);
        private static readonly Color CommentColor = Color.FromArgb(203, 76, 37);
        private static readonly Color LiteralColor = Color.FromArgb(0, 51, 0);
        private static readonly Color[] UsedColors = new Color[] {
            StateColor, DataTypeColor, EventColor, ConstantColor, FunctionColor, FlowColor, CommentColor, LiteralColor
        };


        #region Public properties
        [Browsable(true), Category("Behavior"), DefaultValue(false)]
        public bool SyntaxHighlightEnabled
        {
            get => syntaxHighLightEnabled;

            set
            {
                if (value != syntaxHighLightEnabled)
                {
                    syntaxHighLightEnabled = value;
                    BeginUpdate();
                    SaveState(true);
                    string oldText = Text;
                    Clear();
                    Text = oldText;

                    RestoreState(true);
                    EndUpdate();
                }
            }
        }
        #endregion

        public RRichTextBox()
        {
            InitializeComponent();

            // Are we running mono?
            if (Type.GetType("Mono.Runtime") != null)
            {
                monoRuntime = true;
            }

            // Extract a minimal RTF header fragment and ensure it contains
            // an ANSI code page and the unicode fallback (\uc1) so that
            // \uN? sequences are interpreted correctly by the control.
            try
            {
                rtfHeader = Rtf.Substring(0, Rtf.IndexOf('{', 2)) + " ";
                if (!rtfHeader.Contains("\\ansicpg"))
                {
                    rtfHeader = rtfHeader.Replace("{\\rtf1\\ansi", "{\\rtf1\\ansi\\ansicpg1252");
                }
                if (!rtfHeader.Contains("\\uc"))
                {
                    rtfHeader = rtfHeader + "\\uc1 ";
                }
            }
            catch
            {
                // Fallback to a safe default header
                rtfHeader = "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033 \\uc1 ";
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void InitializeComponent()
        {
            Keywords = LslSyntax.Keywords;
            components = new Container();
            ttKeyWords = new ToolTip(components);
            ttTimer = new System.Threading.Timer(ttTimerElapsed, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components.Dispose();
                ttTimer.Dispose();
            }
            base.Dispose(disposing);
        }


        #region Update supression
        private int savedSelStart;
        private int savedSelLen;
        private Win32.POINT savedScrollPos;

        public void SaveState(bool saveScrollBars)
        {
            savedSelStart = SelectionStart;
            savedSelLen = SelectionLength;
            if (saveScrollBars)
                savedScrollPos = GetScrollPos();
        }

        public void RestoreState(bool saveScrollBars)
        {
            SelectionStart = savedSelStart;
            SelectionLength = savedSelLen;
            if (saveScrollBars)
                SetScrollPos(savedScrollPos);
        }

        #endregion

        #region Copy/Paste
        public new void Paste(DataFormats.Format format)
        {
            Paste();
        }

        public new void Copy()
        {
            if (SelectionLength > 0)
            {
                Clipboard.SetText(SelectedText);
            }
        }

        public new void Cut()
        {
            if (SelectionLength > 0)
            {
                Clipboard.SetText(SelectedText);
                SelectedText = string.Empty;
            }
        }

        public new void Paste()
        {
            string toPaste = Clipboard.GetText();
            if (syntaxHighLightEnabled && toPaste.Contains("\n") && !monoRuntime)
            {
                SelectedRtf = ReHighlight(toPaste);
            }
            else
            {
                SelectedText = toPaste;
            }
        }
        #endregion

        #region Syntax highligting
        private string rtfEscaped(string s)
        {
            return RtfUnicode(s.Replace(@"\", @"\\").Replace("{", @"\{").Replace("}", @"\}").Replace("\n", "\\par\n"));
        }

        private string colorTag(LslSyntax.LslCategory c, string s)
        {
            switch (c)
            {
                case LslSyntax.LslCategory.Function:
                    return colorTag(FunctionColor, s);
                case LslSyntax.LslCategory.Control:
                    return colorTag(StateColor, s);
                case LslSyntax.LslCategory.Event:
                    return colorTag(EventColor, s);
                case LslSyntax.LslCategory.Datatype:
                    return colorTag(DataTypeColor, s);
                case LslSyntax.LslCategory.Constant:
                    return colorTag(ConstantColor, s);
                case LslSyntax.LslCategory.Flow:
                    return colorTag(FlowColor, s);
                default: 
                    return s;
            }
        }

        private string colorTag(Color color, string s)
        {
            var idx = Array.IndexOf(UsedColors, color);
            return $"\\cf{idx+1} {rtfEscaped(s)}\\cf1 ";
        }

        private string ReHighlight(string text)
        {
            StringTokenizer tokenizer = new StringTokenizer(text);
            Token token;
            StringBuilder body = new StringBuilder();

            do
            {
                token = tokenizer.Next();

                switch (token.Kind)
                {
                    case TokenKind.Word:
                        if (Keywords.TryGetValue(token.Value, out var keyword))
                        {
                            body.Append(colorTag(keyword.Category, token.Value));
                        }
                        else
                        {
                            goto default;
                        }
                        break;

                    case TokenKind.QuotedString:
                        body.Append(colorTag(LiteralColor, token.Value));
                        break;

                    case TokenKind.Comment:
                        body.Append(colorTag(CommentColor, token.Value));
                        break;

                    case TokenKind.EOL:
                        body.Append("\\par\n\\cf1 ");
                        break;

                    default:
                        body.Append(rtfEscaped(token.Value));
                        break;
                }

            } while (token.Kind != TokenKind.EOF);

            StringBuilder colorTable = new StringBuilder();
            colorTable.Append(@"{\colortbl;");

            foreach (Color color in UsedColors)
            {
                colorTable.AppendFormat("\\red{0}\\green{1}\\blue{2};", color.R, color.G, color.B);
            }

            colorTable.Append("}");

            // Construct final rtf. Include the ANSI code page and unicode fallback (\uc1)
            // so that \uN? sequences are handled correctly.
            StringBuilder rtf = new StringBuilder();
            rtf.AppendLine("{\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 " + rtfEscaped(Font.Name) + ";}}\\viewkind4\\uc1");
            rtf.AppendLine(colorTable.ToString());
            rtf.Append("\\pard\\f0\\fs" + (int)(Font.SizeInPoints * 2) + " ");
            rtf.Append(body);
            rtf.AppendLine("}");

             return rtf.ToString();
        }

        public override string Text
        {
            get => base.Text;
            set
            {
                if (syntaxHighLightEnabled && value != null)
                {
                    BeginUpdate();
                    Rtf = ReHighlight(value);
                    EndUpdate();
                }
                else
                {
                    base.Text = value;
                }
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (suppressOnTextChanged || Updating) return;

            if (!syntaxHighLightEnabled || monoRuntime || Lines.Length == 0)
            {
                base.OnTextChanged(e);
                return;
            }

            suppressOnTextChanged = true;
            BeginUpdate();

            // Save selection
            int selectionStart = SelectionStart;
            int selectionLength = SelectionLength;

            // Re-highlight line
            int currentLineNr = GetLineFromCharIndex(selectionStart);
            string currentLine = Lines[currentLineNr];
            int firstCharIndex = GetFirstCharIndexOfCurrentLine();

            SelectionStart = firstCharIndex;
            SelectionLength = currentLine.Length;
            SelectedRtf = ReHighlight(currentLine);

            // Restore selection
            SelectionStart = selectionStart;
            SelectionLength = selectionLength;

            base.OnTextChanged(e);

            EndUpdate();
            suppressOnTextChanged = false;
        }

        #endregion

        public struct CursorLocation
        {
            public CursorLocation(int Line, int Column)
            {
                this.Line = Line;
                this.Column = Column;
            }

            public int Line;
            public int Column;

            public override string ToString()
            {
                return $"Ln {Line + 1}  Col {Column + 1}";
            }
        }

        [Browsable(false)]
        public CursorLocation CursorPosition
        {
            get
            {
                int currentLine = GetLineFromCharIndex(SelectionStart);
                int currentCol = 0;
                int offset = 0;
                int i = 0;

                foreach (string line in Lines)
                {
                    if (i < currentLine)
                    {
                        offset += line.Length + 1;
                    }
                    else
                    {
                        break;
                    }
                    i++;
                }

                currentCol = SelectionStart - offset;
                if (currentCol < 0) currentCol = 0;
                return new CursorLocation(currentLine, currentCol);
            }

            set
            {
                int Offset = 0;
                int i = 0;

                foreach (string L in Lines)
                {
                    if (i < value.Line)
                    {
                        Offset += L.Length + 1;
                    }
                    else
                    {
                        break;
                    }

                    i++;
                }

                Select(Offset + value.Column, 0);
            }
        }

        public override void InsertImage(Image _image)
        {
            suppressOnTextChanged = true;
            if (!monoRuntime)
                base.InsertImage(_image);
            suppressOnTextChanged = false;
        }

        #region ToolTips
        private bool validWordChar(char c)
        {
            return
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '_';
        }

        private void ttTimerElapsed(object sender)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { ttTimerElapsed(sender); }));
                return;
            }

            char trackedChar = GetCharFromPosition(trackedMousePos);

            if (!validWordChar(trackedChar))
            {
                return;
            }

            string trackedString = Text;
            int trackedPos = GetCharIndexFromPosition(trackedMousePos);
            int starPos;
            int endPos;

			// Yes we want empty statement here
			#pragma warning disable 642
            for (starPos = trackedPos; starPos >= 0 && validWordChar(trackedString[starPos]); starPos--) { }
            for (endPos = trackedPos; endPos < trackedString.Length && validWordChar(trackedString[endPos]); endPos++) { }
            string word = trackedString.Substring(starPos + 1, endPos - starPos - 1);

            if (!Keywords.ContainsKey(word) || Keywords[word].Tooltip == string.Empty)
            {
                return;
            }

            ttKeyWords.Show(Keywords[word].Tooltip, this, new Point(trackedMousePos.X, trackedMousePos.Y + 15), 120 * 1000);
        }

        private Point trackedMousePos = new Point(0, 0);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point currentMousePos = new Point(e.X, e.Y);

            if (currentMousePos != trackedMousePos)
            {
                trackedMousePos = currentMousePos;
                ttTimer.Change(500, System.Threading.Timeout.Infinite);
                ttKeyWords.Hide(this);
            }
            base.OnMouseMove(e);
        }
        #endregion

        #region Links
        /// <summary>
        /// Insert a given text as a link into the RichTextBox at the current insert position.
        /// </summary>
        /// <param name="text">Text to be inserted</param>
        public void InsertLink(string text)
        {
            InsertLink(text, SelectionStart);
        }

        /// <summary>
        /// Insert a given text at a given position as a link. 
        /// </summary>
        /// <param name="text">Text to be inserted</param>
        /// <param name="position">Insert position</param>
        public void InsertLink(string text, int position)
        {
            if (position < 0 || position > Text.Length)
                throw new ArgumentOutOfRangeException(nameof(position));

            SelectionStart = position;
            SelectedText = text;
            Select(position, text.Length);
            SetSelectionLink(true);
            Select(position + text.Length, 0);
        }

        /// <summary>
        /// Insert a given text at the current input position as a link.
        /// The link text is followed by a hash (#) and the given hyperlink text, both of
        /// them invisible.
        /// When clicked on, the whole link text and hyperlink string are given in the
        /// LinkClickedEventArgs.
        /// </summary>
        /// <param name="text">Text to be inserted</param>
        /// <param name="hyperlink">Invisible hyperlink string to be inserted</param>
        public void InsertLink(string text, string hyperlink)
        {
            InsertLink(text, hyperlink, SelectionStart);
        }

        //public const char LinkSeparator = (char)0x1970;
        public const char LinkSeparator = (char)0x8D;

        private string RtfUnicode(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c > (char)255)
                {
                    sb.Append($"\\u{(short) c}?");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Insert a given text at a given position as a link. The link text is followed by
        /// a hash (#) and the given hyperlink text, both of them invisible.
        /// When clicked on, the whole link text and hyperlink string are given in the
        /// LinkClickedEventArgs.
        /// </summary>
        /// <param name="text">Text to be inserted</param>
        /// <param name="hyperlink">Invisible hyperlink string to be inserted</param>
        /// <param name="position">Insert position</param>
        public void InsertLink(string text, string hyperlink, int position)
        {
            if (position < 0 /* Commented out for now until we can find out why this is happening || position > Text.Length*/)
                throw new ArgumentOutOfRangeException(nameof(position));

            SelectionStart = position;

            if (monoRuntime)
            {
                SelectedText = text;
                SelectionStart = position + text.Length;
            }
            else
            {
                SelectedRtf = rtfHeader + RtfUnicode(text) + @"\v " + LinkSeparator + hyperlink + @"\v0}";
                Select(position, text.Length + hyperlink.Length + 1);
                SetSelectionLink(true);
                Select(position + text.Length + hyperlink.Length + 1, 0);
            }
        }

        /// <summary>
        /// Set the current selection's link style
        /// </summary>
        /// <param name="link">true: set link style, false: clear link style</param>
        public void SetSelectionLink(bool link)
        {
            SetSelectionStyle(Win32.CFM_LINK, link ? Win32.CFE_LINK : 0);
        }
        /// <summary>
        /// Get the link style for the current selection
        /// </summary>
        /// <returns>0: link style not set, 1: link style set, -1: mixed</returns>
        public int GetSelectionLink()
        {
            return GetSelectionStyle(Win32.CFM_LINK, Win32.CFE_LINK);
        }

        private void SetSelectionStyle(uint mask, uint effect)
        {
            Win32.CHARFORMAT2_STRUCT cf = new Win32.CHARFORMAT2_STRUCT();
            cf.cbSize = (uint)Marshal.SizeOf(cf);
            cf.dwMask = mask;
            cf.dwEffects = effect;

            IntPtr wpar = new IntPtr(Win32.SCF_SELECTION);
            IntPtr lpar = Marshal.AllocCoTaskMem(Marshal.SizeOf(cf));
            Marshal.StructureToPtr(cf, lpar, false);

            IntPtr res = Win32.SendMessage(Handle, Win32.EM_SETCHARFORMAT, wpar, lpar);

            Marshal.FreeCoTaskMem(lpar);
        }

        private int GetSelectionStyle(uint mask, uint effect)
        {
            Win32.CHARFORMAT2_STRUCT cf = new Win32.CHARFORMAT2_STRUCT();
            cf.cbSize = (uint)Marshal.SizeOf(cf);
            cf.szFaceName = new char[32];

            IntPtr wpar = new IntPtr(Win32.SCF_SELECTION);
            IntPtr lpar = Marshal.AllocCoTaskMem(Marshal.SizeOf(cf));
            Marshal.StructureToPtr(cf, lpar, false);

            IntPtr res = Win32.SendMessage(Handle, Win32.EM_GETCHARFORMAT, wpar, lpar);

            cf = (Win32.CHARFORMAT2_STRUCT)Marshal.PtrToStructure(lpar, typeof(Win32.CHARFORMAT2_STRUCT));

            int state;
            // dwMask holds the information which properties are consistent throughout the selection:
            if ((cf.dwMask & mask) == mask)
            {
                state = (cf.dwEffects & effect) == effect ? 1 : 0;
            }
            else
            {
                state = -1;
            }

            Marshal.FreeCoTaskMem(lpar);
            return state;
        }
        #endregion

        #region Scrollbar positions functions
        /// <summary>
        /// Sends a win32 message to get the scrollbar's position.
        /// </summary>
        /// <returns>a POINT structure containing horizontal
        ///       and vertical scrollbar position.</returns>
        private unsafe Win32.POINT GetScrollPos()
        {
            Win32.POINT res = new Win32.POINT();
            IntPtr ptr = new IntPtr(&res);
            Win32.SendMessage(Handle, Win32.EM_GETSCROLLPOS, 0, ptr);
            return res;

        }

        /// <summary>
        /// Sends a win32 message to set scrollbars position.
        /// </summary>
        /// <param name="point">a POINT
        ///        containing H/Vscrollbar scrollpos.</param>
        private unsafe void SetScrollPos(Win32.POINT point)
        {
            IntPtr ptr = new IntPtr(&point);
            Win32.SendMessage(Handle, Win32.EM_SETSCROLLPOS, 0, ptr);

        }

        /// <summary>
        /// Summary description for Win32.
        /// </summary>
        private class Win32
        {

            #region Consts
            public const int WM_USER = 0x400;
            public const int WM_PAINT = 0xF;
            public const int WM_KEYDOWN = 0x100;
            public const int WM_KEYUP = 0x101;
            public const int WM_CHAR = 0x102;

            public const int EM_GETSCROLLPOS = (WM_USER + 221);
            public const int EM_SETSCROLLPOS = (WM_USER + 222);

            public const int VK_CONTROL = 0x11;
            public const int VK_UP = 0x26;
            public const int VK_DOWN = 0x28;
            public const int VK_NUMLOCK = 0x90;

            public const short KS_ON = 0x01;
            public const short KS_KEYDOWN = 0x80;

            public const int EM_GETCHARFORMAT = WM_USER + 58;
            public const int EM_SETCHARFORMAT = WM_USER + 68;

            public const int SCF_SELECTION = 0x0001;
            public const int SCF_WORD = 0x0002;
            public const int SCF_ALL = 0x0004;
            #endregion

            #region Structs
            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int x;
                public int y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct CHARFORMAT2_STRUCT
            {
                public UInt32 cbSize;
                public UInt32 dwMask;
                public UInt32 dwEffects;
                public Int32 yHeight;
                public Int32 yOffset;
                public Int32 crTextColor;
                public byte bCharSet;
                public byte bPitchAndFamily;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
                public char[] szFaceName;
                public UInt16 wWeight;
                public UInt16 sSpacing;
                public int crBackColor; // Color.ToArgb() -> int
                public int lcid;
                public int dwReserved;
                public Int16 sStyle;
                public Int16 wKerning;
                public byte bUnderlineType;
                public byte bAnimation;
                public byte bRevAuthor;
                public byte bReserved1;
            }
            #endregion

            #region CHARFORMAT2 Flags
            public const uint CFE_BOLD = 0x0001;
            public const uint CFE_ITALIC = 0x0002;
            public const uint CFE_UNDERLINE = 0x0004;
            public const uint CFE_STRIKEOUT = 0x0008;
            public const uint CFE_PROTECTED = 0x0010;
            public const uint CFE_LINK = 0x0020;
            public const uint CFE_AUTOCOLOR = 0x40000000;
            public const uint CFE_SUBSCRIPT = 0x00010000;		/* Superscript and subscript are */
            public const uint CFE_SUPERSCRIPT = 0x00020000;		/*  mutually exclusive			 */

            public const int CFM_SMALLCAPS = 0x0040;			/* (*)	*/
            public const int CFM_ALLCAPS = 0x0080;			/* Displayed by 3.0	*/
            public const int CFM_HIDDEN = 0x0100;			/* Hidden by 3.0 */
            public const int CFM_OUTLINE = 0x0200;			/* (*)	*/
            public const int CFM_SHADOW = 0x0400;			/* (*)	*/
            public const int CFM_EMBOSS = 0x0800;			/* (*)	*/
            public const int CFM_IMPRINT = 0x1000;			/* (*)	*/
            public const int CFM_DISABLED = 0x2000;
            public const int CFM_REVISED = 0x4000;

            public const int CFM_BACKCOLOR = 0x04000000;
            public const int CFM_LCID = 0x02000000;
            public const int CFM_UNDERLINETYPE = 0x00800000;		/* Many displayed by 3.0 */
            public const int CFM_WEIGHT = 0x00400000;
            public const int CFM_SPACING = 0x00200000;		/* Displayed by 3.0	*/
            public const int CFM_KERNING = 0x00100000;		/* (*)	*/
            public const int CFM_STYLE = 0x00080000;		/* (*)	*/
            public const int CFM_ANIMATION = 0x00040000;		/* (*)	*/
            public const int CFM_REVAUTHOR = 0x00008000;


            public const uint CFM_BOLD = 0x00000001;
            public const uint CFM_ITALIC = 0x00000002;
            public const uint CFM_UNDERLINE = 0x00000004;
            public const uint CFM_STRIKEOUT = 0x00000008;
            public const uint CFM_PROTECTED = 0x00000010;
            public const uint CFM_LINK = 0x00000020;
            public const uint CFM_SIZE = 0x80000000;
            public const uint CFM_COLOR = 0x40000000;
            public const uint CFM_FACE = 0x20000000;
            public const uint CFM_OFFSET = 0x10000000;
            public const uint CFM_CHARSET = 0x08000000;
            public const uint CFM_SUBSCRIPT = CFE_SUBSCRIPT | CFE_SUPERSCRIPT;
            public const uint CFM_SUPERSCRIPT = CFM_SUBSCRIPT;

            public const byte CFU_UNDERLINENONE = 0x00000000;
            public const byte CFU_UNDERLINE = 0x00000001;
            public const byte CFU_UNDERLINEWORD = 0x00000002; /* (*) displayed as ordinary underline	*/
            public const byte CFU_UNDERLINEDOUBLE = 0x00000003; /* (*) displayed as ordinary underline	*/
            public const byte CFU_UNDERLINEDOTTED = 0x00000004;
            public const byte CFU_UNDERLINEDASH = 0x00000005;
            public const byte CFU_UNDERLINEDASHDOT = 0x00000006;
            public const byte CFU_UNDERLINEDASHDOTDOT = 0x00000007;
            public const byte CFU_UNDERLINEWAVE = 0x00000008;
            public const byte CFU_UNDERLINETHICK = 0x00000009;
            public const byte CFU_UNDERLINEHAIRLINE = 0x0000000A; /* (*) displayed as ordinary underline	*/
            #endregion

            #region Imported functions
            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr SendMessage(IntPtr IntPtr, int msg, IntPtr wParam, IntPtr lParam);
            [DllImport("user32")]
            public static extern int SendMessage(IntPtr IntPtr, int wMsg, int wParam, IntPtr lParam);
            [DllImport("user32")]
            public static extern int PostMessage(IntPtr IntPtr, int wMsg, int wParam, int lParam);
            [DllImport("user32")]
            public static extern short GetKeyState(int nVirtKey);
            [DllImport("user32")]
            public static extern int LockWindowUpdate(IntPtr IntPtr);
            #endregion
        }
        #endregion
    }

    internal class LSLErrorHandler : ErrorHandler
    {
        public LSLErrorHandler()
            : base(false)
        {
        }
        public override void Error(CSToolsException e)
        {
            Report(e);
        }
    }
}