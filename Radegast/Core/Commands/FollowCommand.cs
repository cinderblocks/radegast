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

using System.Collections.Generic;
using System.Windows.Forms;

using OpenMetaverse;

namespace Radegast.Commands
{
    public sealed class FollowCommand : RadegastCommand
    {
        TabsConsole TC => Instance.TabConsole;
        ChatConsole Chat;

        public FollowCommand(RadegastInstance instance)
            : base(instance)
        {
            Name = "follow";
            Description = "Follows a person";
            Usage = "follow (person name|stop)";

            Chat = (ChatConsole)TC.Tabs["chat"].Control;
        }

        public override void Dispose()
        {
            Chat = null;
            base.Dispose();
        }

        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            if (Chat.InvokeRequired)
            {
                if (!Instance.MonoRuntime || Chat.IsHandleCreated)
                    Chat.Invoke(new MethodInvoker(() => Execute(name, cmdArgs, WriteLine)));
                return;
            }

            if (cmdArgs.Length == 0)
            {
                if (Instance.State.FollowName == string.Empty)
                    WriteLine("Follow inactive, type {0}follow person name, to start following them", CommandsManager.CmdPrefix);
                else
                    WriteLine("Following {0}", Instance.State.FollowName);
                return;
            }

            string cmd = string.Join(" ", cmdArgs);

            if (cmd == "stop")
            {
                Instance.State.StopFollowing();
                WriteLine("Following stopped");
                return;
            }

            List<UUID> people = Chat.GetAvatarList();
            UUID person = people.Find(id => Instance.Names.Get(id).ToLower().StartsWith(cmd.ToLower()));
            if (person == UUID.Zero)
            {
                WriteLine("Could not find {0}", cmd);
                return;
            }

            Instance.State.Follow(Instance.Names.Get(person), person);
            WriteLine("Following {0}", Instance.State.FollowName);
        }
    }
}
