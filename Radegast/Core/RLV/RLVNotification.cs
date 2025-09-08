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

namespace Radegast
{
    public struct RLVNotification
    {
        public static RLVNotification Default = new RLVNotification(string.Empty);
        public RLVNotification(string action, string type = null, Legality? legallity = null, string param = null)
        {
            Action = action;
            Type = type;
            Legallity = legallity;
            Param = param;
        }

        public string Action { get; }
        public string Type { get; }
        public Legality? Legallity { get; }
        public string Param { get; }

        public static RLVNotification Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return Default;
            }

            input = input.Trim();

            int firstSpace = input.IndexOf(' ');
            if (firstSpace == -1)
            {
                return new RLVNotification(input);
            }

            string action = input.Substring(0, firstSpace);
            string rest = input.Substring(firstSpace + 1).Trim();

            string type = null;
            Legality? legality = null;
            string param = null;

            int legalityIndex = rest.IndexOf(" legally ");
            if (legalityIndex < 0)
            {
                legalityIndex = rest.IndexOf(" illegally ");
            }

            if (legalityIndex >= 0)
            {
                // Found legality keyword in middle
                type = rest.Substring(0, legalityIndex).TrimEnd();
                string legalityWord = rest.Substring(legalityIndex + 1, 7); // "legally" or "illegally"
                legality = legalityWord == "legally" ? Legality.Legally : Legality.Illegally;

                int paramStart = legalityIndex + 1 + legalityWord.Length;
                if (paramStart < rest.Length)
                    param = rest.Substring(paramStart).Trim();
            }
            else
            {
                // No legality word
                int space = rest.IndexOf(' ');
                if (space == -1)
                {
                    type = rest;
                }
                else
                {
                    type = rest.Substring(0, space);
                    param = rest.Substring(space + 1);
                }
            }

            return new RLVNotification(action, type, legality, param);
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Type) && string.IsNullOrEmpty(Param) && Legallity == null)
            {
                return Action;
            }

            if (string.IsNullOrEmpty(Action))
            {
                return string.Empty;
            }

            string result = Action;
            if (!string.IsNullOrEmpty(Type))
            {
                result += " " + Type;
            }

            if (Legallity != null)
            {
                result += " " + Legallity.ToString().ToLower();
            }

            if (!string.IsNullOrEmpty(Param))
            {
                result += " " + Param;
            }

            return result;
        }
    }

    public enum Legality
    {
        Legally,
        Illegally
    }
}