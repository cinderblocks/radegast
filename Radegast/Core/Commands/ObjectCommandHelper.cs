/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2026, Sjofn, LLC
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

namespace Radegast.Commands
{
    internal static class ObjectCommandHelper
    {
        /// <summary>Opens the Objects tab if needed and returns its console, or false
        /// (after printing a status message) if it just now started loading.</summary>
        internal static bool TryGetReadyObjectsConsole(TabsConsole tc, IRadegastInstance instance,
            ConsoleWriteLine writeLine, out ObjectsConsole console)
        {
            if (!tc.TabExists("objects"))
            {
                RadegastTab tab = tc.AddTab("objects", "Objects", new ObjectsConsole((RadegastInstanceForms)instance));
                tab.AllowClose = true;
                tab.AllowDetach = true;
                tab.Visible = true;
                tab.AllowHide = false;
                ((ObjectsConsole)tab.Control).RefreshObjectList();
                tc.Tabs["chat"].Select();

                writeLine("Objects list was not active. Started getting object names, please try again in a minute.");
                console = null;
                return false;
            }

            console = (ObjectsConsole)tc.Tabs["objects"].Control;
            return true;
        }
    }
}
