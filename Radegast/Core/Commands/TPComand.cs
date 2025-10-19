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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using OpenMetaverse;

namespace Radegast.Commands
{
    public sealed class TPCommand : RadegastCommand
    {
        TabsConsole TC => Instance.TabConsole;
        public static string FolderName = "Radegast Landmarks";
        Inventory Inv => Client.Inventory.Store;
        ConsoleWriteLine wl;

        public TPCommand(RadegastInstance instance)
            : base(instance)
        {
            Name = "tp";
			Description = "Teleport to a named landmark from the \"" + FolderName + "\" inventory folder";
            Usage = "tp (list|landmark name|landmark number|help) (type \"" + CommandsManager.CmdPrefix + "tp help\" for full usage)";
            
        }

        void PrintUsage()
        {
            wl("Wrong arguments for \"tp\" command. For detailed description type: " + CommandsManager.CmdPrefix + "tp help");
        }

        void PrintFullUsage()
        {
            wl(@"Usage:
{0}tp (list|landmark name|landmark number|help)

List mode:
Lists landmarks in ""{1}"" folder
Example:
{0}tp list

Landmark name mode:
Specifies name of the landmark to teleport to
Example:
{0}tp Latif's place -- teleports to a landmark whose name begins with ""Latif's place""

Landmark number mode:
Teleport to landmark number specified
Example:
{0}tp 3 -- teleports to landmark number 3 as printed in {0}tp list output", CommandsManager.CmdPrefix, FolderName);
        }

        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            wl = WriteLine;
            if (cmdArgs.Length == 0) { PrintUsage(); return; }

            if (cmdArgs[0] == "help")
            {
                PrintFullUsage();
                return;
            }

            InventoryFolder folder = (InventoryFolder)Inv.GetContents(Inv.RootFolder).Find(b => b.Name == FolderName && b is InventoryFolder);
            if (folder == null)
            {
                Client.Inventory.CreateFolder(Inv.RootFolder.UUID, FolderName);
                WriteLine(@"Could not find ""{0}"" folder in the inventory, creating one.", FolderName);
                return;
            }

            List<InventoryLandmark> landmarks = new List<InventoryLandmark>();
            Inv.GetContents(folder)
                .FindAll(b => b is InventoryLandmark)
                .ForEach(l => { landmarks.Add((InventoryLandmark)l); });

            if (landmarks.Count == 0)
            {
                WriteLine(@"""{0}"" folder is empty, nothing to do.", FolderName);
                return;
            }

            string cmd = string.Join(" ", cmdArgs);

            landmarks.Sort(new LMSorter());

            if (cmd == "list")
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Landmarks:");
                for (int i = 0; i < landmarks.Count; i++)
                {
                    sb.AppendLine(string.Format("{0}. {1}", i + 1, landmarks[i].Name));
                }
                WriteLine(sb.ToString());
                return;
            }

            int lmnr;
            if (int.TryParse(cmd, out lmnr))
            {
                if (lmnr >= 1 && lmnr <= landmarks.Count)
                {
                    WriteLine("Teleporting to {0}", landmarks[lmnr - 1].Name);
                    Client.Self.RequestTeleport(landmarks[lmnr - 1].AssetUUID);
                }
                else
                {
                    WriteLine("Valid landmark number is between 1 and {0}", landmarks.Count);
                }
                return;
            }

            InventoryLandmark lm = landmarks.Find(l => l.Name.ToLower().StartsWith(cmd.ToLower()));
            if (lm == null)
            {
                WriteLine("Could not find landmark {0}, try {1}tp list", cmd, CommandsManager.CmdPrefix);
            }
            else
            {
                WriteLine("Teleporting to {0}", lm.Name);
                Client.Self.RequestTeleport(lm.AssetUUID);
            }
        }

        class LMSorter : IComparer<InventoryLandmark>
        {
            public int Compare(InventoryLandmark lm1, InventoryLandmark lm2)
            {
                Debug.Assert(lm1 != null, nameof(lm1) + " != null");
                Debug.Assert(lm2 != null, nameof(lm2) + " != null");
                return DateTime.Compare(lm1.CreationDate, lm2.CreationDate);
            }
        }
    }
}
