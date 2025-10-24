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
using System.Text;
using OpenMetaverse;

namespace Radegast.Commands
{
    public sealed class SimInfoCommand : RadegastCommand
    {
        public SimInfoCommand(IRadegastInstance instance)
            : base(instance)
        {
            Name = "siminfo";
            Description = "Prints out available information about the current region";
            Usage = Name;
        }

        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            StringBuilder sb = new StringBuilder();
            Simulator sim = Client.Network.CurrentSim;
            
            sb.AppendLine();
            sb.AppendFormat("Information on region {0}:{1}", sim.Name, Environment.NewLine);
            sb.AppendFormat("Product name: {0}{1}", sim.ProductName, Environment.NewLine);
            sb.AppendFormat("Product SKU: {0}{1}", sim.ProductSku, Environment.NewLine);
            sb.AppendFormat("Regions per CPU: {0}{1}", sim.CPURatio, Environment.NewLine);
            sb.AppendFormat("CPU class: {0}{1}", sim.CPUClass, Environment.NewLine);
            sb.AppendFormat("Datacenter: {0}{1}", sim.ColoLocation, Environment.NewLine);
            sb.AppendFormat("Agents: {0}{1}", sim.Stats.Agents, Environment.NewLine);
            sb.AppendFormat("Active scripts: {0}{1}", sim.Stats.ActiveScripts, Environment.NewLine);
            sb.AppendFormat("Time dilation: {0}{1}", sim.Stats.Dilation, Environment.NewLine);
            
            WriteLine(sb.ToString());
        }
    }
}
