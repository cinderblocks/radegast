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

namespace Radegast.Commands
{
    public class LoadPluginCommand : IRadegastCommand
    {
        private RadegastInstanceForms instance;
        public string Name => "loadplugin";

        public string Description => "Loads plugins from a path";

        public string Usage => "loadplugin c:\\\\myplugindir\\\\plugin.dll";

        public void StartCommand(IRadegastInstance inst)
        {
            instance = (RadegastInstanceForms)inst;
        }

        public void Dispose()
        {
            instance = null;
        }

        public void Execute(string n, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            string loadfilename = string.Join(" ", cmdArgs);
            try
            {
                instance.PluginManager.LoadPlugin(loadfilename);
            }
            catch (Exception ex)
            {
                WriteLine("ERROR in Radegast Plugin: {0} because {1} {2}", loadfilename, ex.Message, ex.StackTrace);
            }
        }
    }
}