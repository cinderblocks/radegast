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
using System.Collections.Generic;
using System.Linq;

namespace Radegast
{
    public static class Utilities
    {
        private static readonly char[] InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = System.IO.Path.GetInvalidPathChars();

        public static string SafeFileName(string original)
        {
            if (string.IsNullOrEmpty(original)) return string.Empty;

            return string.Concat(original.Select(c => 
                InvalidFileNameChars.Contains(c) ? '_' : c));
        }

        public static string SafeDirName(string original)
        {
            if (string.IsNullOrEmpty(original)) return string.Empty;

            return string.Concat(original.Select(c => 
                InvalidPathChars.Contains(c) ? '_' : c));
        }

        /// <summary>
        /// Sanitize a string by replacing invalid characters with a replacement character
        /// </summary>
        public static string Sanitize(string input, char[] invalidChars, char replacement = '_')
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            return string.Concat(input.Select(c => 
                Array.IndexOf(invalidChars, c) >= 0 ? replacement : c));
        }

        /// <summary>
        /// Try parse two names from a string (like "First Last")
        /// </summary>
        public static bool TryParseTwoNames(string input, out string first, out string last)
        {
            first = null;
            last = null;
            if (string.IsNullOrEmpty(input)) return false;

            int len = input.Length;
            int i = 0;

            // skip leading whitespace
            while (i < len && char.IsWhiteSpace(input[i])) i++;
            if (i >= len) return false;

            int j = i;
            // find end of first token
            while (j < len && !char.IsWhiteSpace(input[j])) j++;
            if (j == i) return false;

            // skip spaces between first and second
            int k = j;
            while (k < len && char.IsWhiteSpace(input[k])) k++;
            if (k >= len) return false;

            int l = k;
            // find end of second token
            while (l < len && !char.IsWhiteSpace(input[l])) l++;
            if (l == k) return false;

            // ensure no non-space content after second token
            int m = l;
            while (m < len && char.IsWhiteSpace(input[m])) m++;
            if (m != len) return false;

            first = input.Substring(i, j - i);
            last = input.Substring(k, l - k);
            return true;
        }

        /// <summary>
        /// Format a nullable integer as a string, or return null
        /// </summary>
        public static string FormatNullableInt(int? value)
        {
            return value?.ToString();
        }

        /// <summary>
        /// Create a formatted coordinate string from optional X, Y, Z values
        /// </summary>
        public static string FormatCoordinates(int? x = null, int? y = null, int? z = null)
        {
            if (x == null && y == null && z == null) return string.Empty;

            var coords = new List<string>();
            if (x.HasValue) coords.Add(x.Value.ToString());
            if (y.HasValue) coords.Add(y.Value.ToString());
            if (z.HasValue) coords.Add(z.Value.ToString());

            return coords.Count > 0 ? $" ({string.Join(",", coords)})" : string.Empty;
        }
    }
}