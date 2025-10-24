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

using OpenMetaverse;

namespace RadegastSpeech.Conversation
{
    internal class InvLandmark : Mode
    {
        private readonly InventoryLandmark asset;
        internal InvLandmark(PluginControl pc, InventoryLandmark a)
            : base(pc)
        {
            asset = a;
        }

        internal override void Start()
        {
            base.Start();
            Talker.SayMore("Do you want to go to " + NiceName(asset.Name));
        }

        internal override bool Hear(string cmd)
        {
            // Commands "go" "describe"
            switch (cmd)
            {
                case "no":
                    FinishInterruption();
                    return true;
                case "describe":
                    Describe();
                    return true;
                case "yes":
                case "go":
                    Talker.SayMore("Here we go.");
                    Client.Self.Teleport(asset.AssetUUID);
                    // TODO Should force conversation to Chat here.
                    FinishInterruption();
                    return true;
                default:
                    return false;
            }
        }

        private void Describe()
        {
            Talker.SayMore(asset.Description);
        }

     }
}
