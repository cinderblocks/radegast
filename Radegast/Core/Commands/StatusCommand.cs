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
using System.Text;
using System.Threading;
using OpenMetaverse;

namespace Radegast.Commands
{
    public sealed class StatusCommand : RadegastCommand
    {
        private readonly List<string> args;

        public StatusCommand(IRadegastInstance instance)
            : base(instance)
        {
            Name = "status";
            Description = "Prints various status infromation";
            Usage = "status (doing|region|parcel|money|location|time)";

            args = new List<string>();
        }

        private bool arg(string a)
        {
            return args.Count == 0 || args.Contains(a);
        }

        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            args.Clear();
            args.AddRange(cmdArgs);
            StringBuilder sb = new StringBuilder();

            if (arg("doing"))
            {
                if (Client.Self.SittingOn != 0)
                {
                    sb.Append("Sitting");
                    Primitive seat;
                    if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(Client.Self.SittingOn, out seat))
                    {
                        if (seat.Properties != null)
                        {
                            sb.AppendFormat(" on object {0}", seat.Properties.Name);
                        }
                        else
                        {
                            ManualResetEvent gotName = new ManualResetEvent(false);
                            EventHandler<ObjectPropertiesFamilyEventArgs> handler = (sender, e) =>
                            {
                                if (e.Properties.ObjectID == seat.ID)
                                    gotName.Set();
                            };

                            Client.Objects.ObjectPropertiesFamily += handler;
                            Client.Objects.RequestObjectPropertiesFamily(Client.Network.CurrentSim, seat.ID);
                            if (gotName.WaitOne(3000, false))
                            {
                                sb.Append(sb.AppendFormat(" on object {0}", seat.Properties != null ? seat.Properties.Name : ""));
                            }

                            Client.Objects.ObjectPropertiesFamily -= handler;
                        }
                    }
                }
                else
                {
                    if (Client.Self.Movement.SitOnGround)
                        sb.Append("Sitting on the ground");
                    else if (Client.Self.Movement.Fly)
                        sb.Append("Flying");
                    else
                        sb.Append("Standing");
                    if (Client.Self.Movement.AlwaysRun)
                        sb.Append(", always running when moving");
                }
                
                if (Instance.State.FollowName != string.Empty)
                    sb.AppendFormat(", following {0}", Instance.State.FollowName);

                sb.AppendLine();
            }

            if (arg("region"))
            {
                sb.AppendLine(string.Format("Region: {0} ({1})",
                    Client.Network.CurrentSim.Name,
                    Utils.EnumToText(Client.Network.CurrentSim.Access)));
            }

            if (arg("parcel") && Instance.State.Parcel != null)
            {
                Parcel parcel = Instance.State.Parcel;
                sb.AppendFormat(@"Parcel: {0}", parcel.Name);
                List<string> mods = new List<string>();

                if ((parcel.Flags & ParcelFlags.AllowFly) != ParcelFlags.AllowFly)
                    mods.Add("no fly");
                if ((parcel.Flags & ParcelFlags.CreateObjects) != ParcelFlags.CreateObjects)
                    mods.Add("no build");
                if ((parcel.Flags & ParcelFlags.AllowOtherScripts) != ParcelFlags.AllowOtherScripts)
                    mods.Add("no scripts");
                if ((parcel.Flags & ParcelFlags.RestrictPushObject) == ParcelFlags.RestrictPushObject)
                    mods.Add("no push");
                if ((parcel.Flags & ParcelFlags.AllowDamage) == ParcelFlags.AllowDamage)
                    mods.Add("damage allowed");
                if ((parcel.Flags & ParcelFlags.AllowVoiceChat) != ParcelFlags.AllowVoiceChat)
                    mods.Add("no voice");

                if (mods.Count > 0)
                    sb.AppendFormat(" ({0})", string.Join(", ", mods.ToArray()));
                sb.AppendLine();
            }

            if (arg("location"))
            {
                Primitive seat = null;
                if (Client.Self.SittingOn != 0)
                    Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(Client.Self.SittingOn, out seat);

                Vector3 pos = Client.Self.RelativePosition;
                if (seat != null) pos = seat.Position + pos;

                sb.AppendFormat("Position <{0:0}, {1:0}, {2:0}>", pos.X, pos.Y, pos.Z);

                Quaternion rot;
                if (seat == null)
                    rot = Client.Self.Movement.BodyRotation;
                else
                    rot = seat.Rotation + Client.Self.RelativeRotation;

                Vector3 heading = StateManager.RotToEuler(rot);
                int facing = (int)(57.2957795d * heading.Z);
                if (facing < 0) facing = 360 + facing;
                sb.AppendFormat(" heading {0:0} degrees, ", facing);
                sb.AppendFormat(" facing {0}", StateManager.ClosestKnownHeading(facing).Name);
                sb.AppendLine();
            }

            if (arg("money"))
            {
                sb.AppendFormat("Account balance: ${0}", Client.Self.Balance);
                sb.AppendLine();
            }

            if (arg("time"))
            {
                try
                {
                    TimeZoneInfo SLTime = null;
                    foreach (TimeZoneInfo tz in TimeZoneInfo.GetSystemTimeZones())
                    {
                        if (tz.Id == "Pacific Standard Time" || tz.Id == "America/Los_Angeles")
                        {
                            SLTime = tz;
                            break;
                        }
                    }

                    DateTime now = SLTime != null ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SLTime) : DateTime.UtcNow.AddHours(-7);

                    sb.AppendLine(string.Format("Current time is " + now.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch { }
            }

            WriteLine(sb.ToString());
        }
    }
}
