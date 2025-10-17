/*
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
using System.Linq;
using System.Windows.Forms;

namespace Radegast.Commands
{
    public class SitCommand : RadegastCommand
    {
        TabsConsole TC => Instance.TabConsole;
        ObjectsConsole Objects;
        ChatConsole Chat;
        ConsoleWriteLine wl;

        public SitCommand(RadegastInstance instance)
            : base(instance)
        {
            Name = "sit";
            Description = "Sits on an object or on the ground";
            Usage = "sit (object name|ground)";

            Chat = (ChatConsole)TC.Tabs["chat"].Control;
        }

        public override void Dispose()
        {
            Objects = null;
            Chat = null;
            base.Dispose();
        }

        void PrintUsage()
        {
            wl("Wrong arguments for \"sit\" command. Use {0}{1}", CommandsManager.CmdPrefix, Usage);
        }


        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            if (Chat.InvokeRequired)
            {
                if (!Instance.MonoRuntime || Chat.IsHandleCreated)
                    Chat.Invoke(new MethodInvoker(() => Execute(name, cmdArgs, WriteLine)));
                return;
            }
            wl = WriteLine;

            if (cmdArgs.Length == 0) { PrintUsage(); return; }

            string cmd = string.Join(" ", cmdArgs);

            if (cmd == "ground")
            {
                Client.Self.SitOnGround();
                wl("Sat on the ground");
                return;
            }

            if (!TC.TabExists("objects"))
            {
                RadegastTab tab = TC.AddTab("objects", "Objects", new ObjectsConsole(Instance));
                tab.AllowClose = true;
                tab.AllowDetach = true;
                tab.Visible = true;
                tab.AllowHide = false;
                ((ObjectsConsole)tab.Control).RefreshObjectList();
                TC.Tabs["chat"].Select();

                WriteLine("Objects list was not active. Started getting object names, please try again in a minute.");
                return;
            }

            Objects = (ObjectsConsole)TC.Tabs["objects"].Control;
            var target = Objects.GetObjects().FirstOrDefault(
                prim => prim.Properties != null 
                        && prim.Properties.Name.IndexOf(cmd, StringComparison.OrdinalIgnoreCase) >= 0);
            if (target == null)
            {
                WriteLine("Could not find '{0}' nearby", cmd);
                return;
            }

            WriteLine("Requesting to sit on object '{0}'", target.Properties.Name);
            Instance.State.SetSitting(true, target.ID);
        }
    }
}
