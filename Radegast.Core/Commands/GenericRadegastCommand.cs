/**
 * Radegast Metaverse Client
 * Copyright(c) 2025, Sjofn, LLC
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

using Radegast.Commands;

namespace Radegast.Core.Commands
{
    public class GenericRadegastCommand : RadegastCommand
    {
        public GenericRadegastCommand(IRadegastInstance instance, CommandExecuteDelegate execute) 
            : base(instance, execute) { }

        public GenericRadegastCommand(IRadegastInstance inst) : base(inst) { }

        public override void Dispose()
        {
        }
    }
}
