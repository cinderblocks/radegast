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

namespace Radegast
{
    public static class Utilities
    {
        public static string SafeFileName(string original)
        {
            var fileName = string.Empty;
            var invalid = new List<char>(System.IO.Path.GetInvalidFileNameChars());

            foreach (var c in original)
            {
                if (invalid.Contains(c))
                {
                    fileName += "_";
                }
                else
                {
                    fileName += c;
                }
            }

            return fileName;
        }

        public static string SafeDirName(string original)
        {
            var fileName = string.Empty;
            var invalid = new List<char>(System.IO.Path.GetInvalidPathChars());

            foreach (var c in original)
            {
                if (invalid.Contains(c))
                {
                    fileName += "_";
                }
                else
                {
                    fileName += c;
                }
            }
            return fileName;
        }
    }
}