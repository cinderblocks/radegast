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
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using OpenMetaverse;

namespace Radegast.Commands
{
    public sealed class FaceCommand : RadegastCommand
    {
        private TabsConsole TC => Instance.TabConsole;
        private ObjectsConsole Objects;
        private ChatConsole Chat;
        private Vector3 targetPos = Vector3.Zero;
        private const RegexOptions regexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase;
        private ConsoleWriteLine wl;

        public FaceCommand(RadegastInstance instance)
            : base(instance)
        {
            Name = "face";
            Description = "Changes the direction";
            Usage = "face (direction|heading|object|person|help) [additional args] (type \"" + CommandsManager.CmdPrefix + "face help\" for full usage)";

            Chat = (ChatConsole)TC.Tabs["chat"].Control;
        }

        public override void Dispose()
        {
            Objects = null;
            Chat = null;
            base.Dispose();
        }

        private void PrintUsage()
        {
            wl("Wrong arguments for \"face\" command. For detailed description type: " + CommandsManager.CmdPrefix + "face help");
        }

        private void PrintFullUsage()
        {
            wl(@"Usage:
{0}face (direction|heading|object|person|help) [additional args]

Direction (in degrees) mode:
Specifies heading in degrees which we should be oriented in
Examples:
{0}face 0 -- face east
{0}face 180 -- face west

Heading mode:
Specifies compass heading we should be facing
Examples:
{0}face n -- face east
{0}face sw -- face southwest

Object mode:
Turns toward a named object
Example:
{0}face object desk chair -- turn toward object whose names begins with ""desk chair""

Person mode:
Turns toward a person
Examples:
{0}face person Latif -- turn toward the closest person whose name begins with Latif", CommandsManager.CmdPrefix);
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

            var cmd = string.Join(" ", cmdArgs);
            var args = new List<string>(Regex.Split(cmd, @"\s", regexOptions));

            if (args.Count == 0) { PrintUsage(); return; }

            var subcmd = args[0];
            args.RemoveAt(0);
            var subarg = string.Empty;

            // Face certain direction
            if (int.TryParse(subcmd, out var heading))
            {
                var rad = 0.0174532925d * heading;
                Client.Self.Movement.UpdateFromHeading(rad, true);
                WriteLine("Facing {0} degrees", heading % 360);
                return;
            }

            if (subcmd == "help")
            {
                PrintFullUsage();
                return;
            }

            KnownHeading kh = null;
            kh = StateManager.KnownHeadings.Find(h => h.ID == subcmd.ToUpper());
            if (kh != null)
            {
                Client.Self.Movement.BodyRotation = Client.Self.Movement.HeadRotation = kh.Heading;
                WriteLine("Facing {0}", kh.Name);
                return;
            }

            if (args.Count == 0) { PrintUsage(); return; }
            subarg = string.Join(" ", args.ToArray());

            // Move towards
            switch (subcmd)
            {
                case "person":
                    var people = Chat.GetAvatarList();
                    var person = people.Find(id => Instance.Names.Get(id).ToLower().StartsWith(subarg.ToLower()));
                    if (person == UUID.Zero)
                    {
                        WriteLine("Could not find {0}", subarg);
                        return;
                    }
                    var pname = Instance.Names.Get(person);

                    if (!Instance.State.TryFindAvatar(person, out targetPos))
                    {
                        WriteLine("Could not locate {0}", pname);
                        return;
                    }

                    WriteLine("Facing {0}", pname);
                    Client.Self.Movement.TurnToward(targetPos);

                    return;

                case "object":

                    if (!TC.TabExists("objects"))
                    {
                        var tab = TC.AddTab("objects", "Objects", new ObjectsConsole(Instance));
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
                        WriteLine("Could not find '{0}' nearby", subarg);
                        return;
                    }

                    targetPos = target.Position;

                    WriteLine("Facing object '{0}'", target.Properties.Name);
                    Client.Self.Movement.TurnToward(targetPos);
                    return;

                default:
                    WriteLine("Unrecognized face command {0}", subcmd);
                    return;
            }
        }
    }
}
