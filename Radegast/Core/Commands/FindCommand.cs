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
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using LibreMetaverse;

namespace Radegast.Commands
{
    public sealed class FindCommand : RadegastCommand
    {
        private TabsConsole TC => ((RadegastInstanceForms)Instance).TabConsole;
        private ObjectsConsole Objects;
        private ChatConsole Chat;
        private ConsoleWriteLine wl;

        public FindCommand(IRadegastInstance instance)
            : base(instance)
        {
            Name = "find";
            Description = "Finds nearby person or object";
            Usage = "find (object|person) name";

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
            wl("Wrong arguments for \"find\" command. Use {0}{1}", CommandsManager.CmdPrefix, Usage);
        }

        /// <summary>Compass heading from <paramref name="from"/> toward <paramref name="to"/>.
        /// Normalizing a zero-length vector yields NaN, so returns "right here" instead
        /// when the two positions coincide.</summary>
        private static string DescribeHeading(Vector3 from, Vector3 to)
        {
            if (Vector3.Distance(from, to) < 0.01f) return "right here";

            Vector3 heading = StateManager.RotToEuler(Vector3.RotationBetween(Vector3.UnitX, Vector3.Normalize(to - from)));
            int facing = (int)(57.2957795d * heading.Z);
            if (facing < 0) facing = 360 + facing;
            return StateManager.ClosestKnownHeading(facing).ToString();
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
            List<string> args = new List<string>(Regex.Split(cmd, @"\s"));

            string subcmd = args[0];
            args.RemoveAt(0);
            if (args.Count == 0) { PrintUsage(); return; }
            string subarg = string.Join(" ", args.ToArray());

            Primitive seat = null;
            if (Client.Self.SittingOn != 0)
                Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(Client.Self.SittingOn, out seat);

            Vector3 mypos = Client.Self.RelativePosition;
            if (seat != null) mypos = seat.Position + mypos;
            StringBuilder sb = new StringBuilder();

            if (subcmd == "object")
            {
                if (!ObjectCommandHelper.TryGetReadyObjectsConsole(TC, Instance, WriteLine, out Objects))
                    return;
                foreach (var target in Objects.GetObjects().Where(
                             prim => prim.Properties != null
                                     && prim.Properties.Name.IndexOf(subarg, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    sb.AppendFormat("{0} is {1:0} meters away to the {2}",
                        target.Properties.Name,
                        Vector3.Distance(mypos, target.Position),
                        DescribeHeading(mypos, target.Position)
                        );

                    float elev = target.Position.Z - mypos.Z;
                    if (Math.Abs(elev) < 2f)
                        sb.Append(" at our level");
                    else if (elev > 0)
                        sb.AppendFormat(", {0:0} meters above our level", elev);
                    else
                        sb.AppendFormat(", {0:0} meters below our level", -elev);

                    sb.AppendLine();
                }

                if (sb.Length == 0)
                {
                    WriteLine("Could not find '{0}' nearby", subarg);
                    return;
                }

                wl(sb.ToString());

                return;
            }

            if (subcmd == "person")
            {
                List<UUID> people = Chat.GetAvatarList();
                people = people.FindAll(id => id != Client.Self.AgentID && Instance.Names.Get(id).StartsWith(subarg, StringComparison.OrdinalIgnoreCase));
                if (people.Count == 0)
                {
                    WriteLine("Could not find {0}", subarg);
                    return;
                }

                foreach (UUID person in people)
                {
                    string pname = Instance.Names.Get(person);

                    Vector3 targetPos = Vector3.Zero;
                    bool located = false;

                    // try to find where they are
                    var kvp = Client.Network.CurrentSim.ObjectsAvatars.FirstOrDefault(
                        av => av.Value.ID == person);

                    if (kvp.Value != null)
                    {
                        var avi = kvp.Value;
                        if (avi.ParentID == 0)
                        {
                            targetPos = avi.Position;
                            located = true;
                        }
                        else
                        {
                            if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(avi.ParentID, out var seatObj))
                            {
                                targetPos = seatObj.Position + avi.Position;
                                located = true;
                            }
                        }
                    }
                    else
                    {
                        // a coarse altitude of 0 means unknown (above 1020 m), as in StateManager.AvatarPosition
                        if (Instance.State.TryGetCoarsePosition(Client.Network.CurrentSim, person, out var pos)
                            && pos.Z > 0.01f)
                        {
                            targetPos = pos;
                            located = true;
                        }
                    }

                    if (!located)
                    {
                        WriteLine("Could not locate {0}", pname);
                        continue;
                    }

                    sb.AppendFormat("{0} is {1:0} meters away to the {2}",
                        pname,
                        Vector3.Distance(mypos, targetPos),
                        DescribeHeading(mypos, targetPos)
                        );

                    float elev = targetPos.Z - mypos.Z;
                    if (Math.Abs(elev) < 2f)
                        sb.Append(" at our level");
                    else if (elev > 0)
                        sb.AppendFormat(", {0:0} meters above our level", elev);
                    else
                        sb.AppendFormat(", {0:0} meters below our level", -elev);

                    sb.AppendLine();
                }

                if (sb.Length > 0)
                    wl(sb.ToString());
            }
        }
    }
}
