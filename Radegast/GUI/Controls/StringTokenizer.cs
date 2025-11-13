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

/********************************************************8
 *	Author: Andrew Deren
 *	Date: July, 2004
 *	http://www.adersoftware.com
 * 
 *	StringTokenizer class. You can use this class in any way you want
 * as long as this header remains in this file.
 * 
 **********************************************************/
using System;
using System.IO;
using System.Linq;

namespace Radegast
{
    public enum TokenKind
    {
        Unknown,
        Word,
        Number,
        QuotedString,
        WhiteSpace,
        Symbol,
        Comment,
        EOL,
        EOF
    }

    public class Token
    {
        private readonly string source;
        private readonly int start;
        private readonly int length;
        private string valueCache;

        // Existing constructor for callers that already have the string
        public Token(TokenKind kind, string value, int line, int column)
        {
            Kind = kind;
            valueCache = value;
            source = null;
            start = 0;
            length = 0;
            Line = line;
            Column = column;
        }

        // New constructor that lazily captures a slice of the source
        internal Token(TokenKind kind, string source, int start, int length, int line, int column)
        {
            Kind = kind;
            this.source = source;
            this.start = start;
            this.length = length;
            Line = line;
            Column = column;
            valueCache = null;
        }

        public int Column { get; }

        public TokenKind Kind { get; }

        public int Line { get; }

        public string Value
        {
            get
            {
                if (valueCache != null) return valueCache;
                if (length == 0) return valueCache = string.Empty;
                return valueCache = source.Substring(start, length);
            }
        }
    }

	/// <summary>
	/// StringTokenizer tokenized string (or stream) into tokens.
	/// </summary>
	public class StringTokenizer
	{
        private const char EOF = (char)0;

        private int line;
        private int column;
        private int pos;    // position within data

        private readonly string data;

        private int saveLine;
        private int saveCol;
        private int savePos;

		public StringTokenizer(TextReader reader)
		{
			if (reader == null)
				throw new ArgumentNullException(nameof(reader));

			data = reader.ReadToEnd();

			Reset();
		}

		public StringTokenizer(string data)
		{
            this.data = data ?? throw new ArgumentNullException(nameof(data));

			Reset();
		}

		/// <summary>
		/// gets or sets which characters are part of TokenKind.Symbol
		/// </summary>
		public char[] SymbolChars { get; set; }

	    /// <summary>
		/// if set to true, white space characters will be ignored,
		/// but EOL and whitespace inside of string will still be tokenized
		/// </summary>
		public bool IgnoreWhiteSpace { get; set; }

		private void Reset()
		{
			IgnoreWhiteSpace = false;
			SymbolChars = new char[]{'=', '+', '-', '/', ',', '.', '*', '~', '!', '@', '#', '$', '%', '^', '&', '(', ')', '{', '}', '[', ']', ':', ';', '<', '>', '?', '|', '\\'};

			line = 1;
			column = 1;
			pos = 0;
		}

		protected char LA(int count)
		{
			if (pos + count >= data.Length)
				return EOF;
			else
				return data[pos+count];
		}

		protected char Consume()
		{
			char ret = data[pos];
			pos++;
			column++;

			return ret;
		}

		protected Token CreateToken(TokenKind kind, string value)
		{
			return new Token(kind, value, line, column);
		}

		// Modified: create a Token that lazily refers to the source string slice
		protected Token CreateToken(TokenKind kind)
		{
			int length = pos - savePos;
			return new Token(kind, data, savePos, length, saveLine, saveCol);
		}

		public Token Next()
		{
			ReadToken:

			char ch = LA(0);
			switch (ch)
			{
				case EOF:
					return CreateToken(TokenKind.EOF, string.Empty);

				case ' ':
				case '\t':
				{
					if (IgnoreWhiteSpace)
					{
						Consume();
						goto ReadToken;
					}
					else
						return ReadWhitespace();
				}
				case '0':
				case '1':
				case '2':
				case '3':
				case '4':
				case '5':
				case '6':
				case '7':
				case '8':
				case '9':
					return ReadNumber();

				case '\r':
				{
					StartRead();
					Consume();
					if (LA(0) == '\n')
						Consume();    // on DOS/Windows we have \r\n for new line

					line++;
					column=1;

					return CreateToken(TokenKind.EOL);
				}
				case '\n':
				{
					StartRead();
					Consume();
					line++;
					column=1;
					
					return CreateToken(TokenKind.EOL);
				}

				case '"':
				{
					return ReadString();
				}

				case '/':
				{
					if (LA(1) == '/')
					{
						return ReadComment();
					}
					else if (LA(1) == '*')
					{
						return ReadStarComment();
					}
					else
					{
						StartRead();
						Consume();
						return CreateToken(TokenKind.Symbol);
					}
				}

				default:
				{
					if (char.IsLetter(ch) || ch == '_')
						return ReadWord();
					else if (IsSymbol(ch))
					{
						StartRead();
						Consume();
						return CreateToken(TokenKind.Symbol);
					}
					else
					{
						StartRead();
						Consume();
						return CreateToken(TokenKind.Unknown);                        
					}
				}

			}
		}

		/// <summary>
		/// save read point positions so that CreateToken can use those
		/// </summary>
		private void StartRead()
		{
			saveLine = line;
			saveCol = column;
			savePos = pos;
		}

		/// <summary>
		/// reads all whitespace characters (does not include newline)
		/// </summary>
		/// <returns></returns>
		protected Token ReadWhitespace()
		{
			StartRead();

			Consume(); // consume the looked-ahead whitespace char

			while (true)
			{
				char ch = LA(0);
				if (ch == '\t' || ch == ' ')
					Consume();
				else
					break;
			}

			return CreateToken(TokenKind.WhiteSpace);
			
		}

		/// <summary>
		/// reads number. Number is: DIGIT+ ("." DIGIT*)?
		/// </summary>
		/// <returns></returns>
		protected Token ReadNumber()
		{
			StartRead();

			bool hadDot = false;

			Consume(); // read first digit

			while (true)
			{
				char ch = LA(0);
				if (char.IsDigit(ch))
					Consume();
				else if (ch == '.' && !hadDot)
				{
					hadDot = true;
					Consume();
				}
				else
					break;
			}

			return CreateToken(TokenKind.Number);
		}

		/// <summary>
		/// reads word. Word contains any alpha character or _
		/// </summary>
		protected Token ReadWord()
		{
			StartRead();

			Consume(); // consume first character of the word

			while (true)
			{
				char ch = LA(0);
				if (char.IsLetter(ch) || ch == '_')
					Consume();
				else
					break;
			}

			return CreateToken(TokenKind.Word);
		}

        /// <summary>
        /// Reads he rest of line in // comment
        /// </summary>
        protected Token ReadComment()
        {
            StartRead();

            Consume(); // consume first character of the comment

            while (true)
            {
                char ch = LA(0);
                if (ch != EOF && ch != '\n' && ch != '\r')
                    Consume();
                else
                    break;
            }

            return CreateToken(TokenKind.Comment);
        }

        /// <summary>
        /// Read c-style comments /* */
        /// </summary>
        protected Token ReadStarComment()
        {
            StartRead();

            Consume(); // consume first character of the comment

            while (true)
            {
                char ch = LA(0);
                if (ch == EOF)
                {
                    break;
                }
                else if (ch == '*' && LA(1) == '/')
                {
                    Consume();
                    Consume();
                    break;
                }
                else
                {
                    Consume();
                }
            }

            return CreateToken(TokenKind.Comment);
        }

		/// <summary>
		/// reads all characters until next " is found.
		/// If "" (2 quotes) are found, then they are consumed as
		/// part of the string
		/// </summary>
		/// <returns></returns>
		protected Token ReadString()
		{
			StartRead();

			Consume(); // read "

			while (true)
			{
				char ch = LA(0);
				if (ch == EOF)
					break;
				else if (ch == '\r')    // handle CR in strings
				{
					Consume();
					if (LA(0) == '\n')    // for DOS & windows
						Consume();

					line++;
					column = 1;
				}
				else if (ch == '\n')    // new line in quoted string
				{
					Consume();

					line++;
					column = 1;
				}
				else if (ch == '"')
				{
					Consume();
					if (LA(0) != '"')
						break;    // done reading, and this quotes does not have escape character
					else
						Consume(); // consume second ", because first was just an escape
				}
				else
					Consume();
			}

			return CreateToken(TokenKind.QuotedString);
		}

		/// <summary>
		/// checks whether c is a symbol character.
		/// </summary>
		protected bool IsSymbol(char c)
        {
            return SymbolChars.Any(t => t == c);
        }
	}
}
