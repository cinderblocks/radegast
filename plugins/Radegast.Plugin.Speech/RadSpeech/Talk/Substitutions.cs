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

using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenMetaverse.StructuredData;

namespace RadegastSpeech.Talk
{
    internal class Substitutions
    {
        private readonly List<Tuple> patterns;

        public Substitutions( PluginControl pc)
        {
            // TODO read this from the config file.
            patterns = new List<Tuple>();

            // Load standard substitutions
                Add("wb", "welcome back");
                Add(@"\^\^", "ha ha");
                Add(@"\^\s\^", "ha ha");
                Add(@"\^_\^", "ha ha");
                Add(@":\)", "ha ha");
                Add(@":-\)", "ha ha");
                Add("O.O", "wow");
                Add("afk", "away from keyboard");
                Add("lol", "ha ha");
                Add(@":\(", "frowns.");
                Add(@"\.\.+", ",");
                Add("lmao", "ha ha ha!");
                Add("w00t", "woot");
                Add("np", "no problem");
                Add("btw", "by the way");
                Add(":D", "ha ha");
                Add("brb", "Be right back.");
                Add("omg", "oh my god.");
                Add(@"http(s)*://[a-z0-9\./=%&?+-]+", ", a URL,");

            // Load custom substitutions
            if (pc.config["substitutions"] is OSDMap subs)
            {
                foreach (string key in subs.Keys)
                {
                    Add( key, subs[key].AsString() );
                }
            }

            // Strip out non-pronouncable characters for now.
            // TODO more language support.
            // For some reason \P{IsBasicLatin} matches the letter "i"
           Add(@"\p{IsHiragana}|p\{IsKatakana}");
           Add(@"[*<>-]");
        }

        public string FixExpressions(string input)
        {
            if (input == null) return null;
            foreach (Tuple t in patterns)
            {
                input = t.pattern.Replace(input, t.replace);
            }
            return input.Trim();
        }

        /// <summary>
        /// Add a rule to replace a symbol with an expression
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        internal void Add(string a, string b)
        {
            patterns.Add(new Tuple(@"\b" + a + @"\b", " " + b + " "));
        }
        /// <summary>
        /// Add a rule to remove a pattern
        /// </summary>
        /// <param name="a"></param>
        internal void Add(string a)
        {
            patterns.Add(new Tuple(a, ""));

        }

        private class Tuple
        {
            public readonly Regex pattern;
            public readonly string replace;
            /// <summary>
            /// Tuple to replace a pattern
            /// </summary>
            /// <param name="a"></param>
            /// <param name="b"></param>
            public Tuple(string a, string b)
            {
                // Compile substitution patterns to reduce runtime allocations
                pattern = new Regex(a, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                replace = b;
            }

        }
    }

}
