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

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using OpenMetaverse;
using OpenMetaverse.Messages.Linden;

namespace Radegast.Commands
{
    public sealed class ScriptInfoCommand : RadegastCommand
    {
        public ScriptInfoCommand(RadegastInstance instance)
            : base(instance)
        {
            Name = "scriptinfo";
            Description = "Prints out available information about the current script usage";
            Usage = "scriptinfo (avatar|parcel) - display script resource usage details about your avatar or parcel you are on. If not specified avatar is assumed";
        }

        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            if (cmdArgs.Length == 0)
            {
                _ = AttachmentInfo(WriteLine);
            }
            else switch (cmdArgs[0])
            {
                case "avatar":
                    _ = AttachmentInfo(WriteLine);
                    break;
                case "parcel":
                    _ = ParcelInfo(WriteLine);
                    break;
            }

        }

        public async Task ParcelInfo(ConsoleWriteLine WriteLine)
        {
            WriteLine("Requesting script resources information...");
            UUID currentParcel = Client.Parcels.RequestRemoteParcelID(Client.Self.SimPosition, Client.Network.CurrentSim.Handle, Client.Network.CurrentSim.ID);
            await Client.Parcels.GetParcelResources(currentParcel, true, (success, info) =>
            {
                if (!success || info == null) return;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Summary:");
                sb.AppendFormat("Memory used {0} KB out of {1} KB available.", info.SummaryUsed["memory"] / 1024, info.SummaryAvailable["memory"] / 1024);
                sb.AppendLine();
                sb.AppendFormat("URLs used {0} out of {1} available.", info.SummaryUsed["urls"], info.SummaryAvailable["urls"]);
                sb.AppendLine();

                if (info.Parcels != null)
                {
                    foreach (ParcelResourcesDetail resource in info.Parcels)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"Detailed usage for parcel {resource.Name}");
                        foreach (ObjectResourcesDetail ord in resource.Objects)
                        {
                            sb.AppendFormat("{0} KB - {1}", ord.Resources["memory"] / 1024, ord.Name);
                            sb.AppendLine();
                        }
                    }
                }
                WriteLine(sb.ToString());
            });
        }

        public async Task AttachmentInfo(ConsoleWriteLine WriteLine)
        {
            await Client.Self.GetAttachmentResources((success, info) =>
            {
                if (!success || info == null)
                {
                    WriteLine("Failed to get the script info.");
                    return;
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Summary:");
                sb.AppendFormat("Memory used {0} KB out of {1} KB available.", info.SummaryUsed["memory"] / 1024, info.SummaryAvailable["memory"] / 1024);
                sb.AppendLine();
                sb.AppendFormat("URLs used {0} out of {1} available.", info.SummaryUsed["urls"], info.SummaryAvailable["urls"]);
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("Details:");

                foreach (var kvp in info.Attachments)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Attached to {Utils.EnumToText(kvp.Key)}:");
                    foreach (var obj in kvp.Value)
                    {
                        sb.AppendLine($"{obj.Name} using {obj.Resources["memory"] / 1024}KB");
                    }
                }

                WriteLine(sb.ToString());
            }
            );
        }
    }
}
