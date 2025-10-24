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
using Radegast;
using System.Windows.Forms;

namespace RadegastSpeech.Conversation
{
    internal class Closet : Mode
    {
        private readonly Inventory inv;
        private InventoryBase selected;
        private InventoryConsole invTab;
        private TreeView tree;
        private const string INVNAME = "inventory";

        internal Closet(PluginControl pc)
            : base(pc)
        {
            Title = "inventory";
            inv = control.instance.Client.Inventory.Store;
            selected = inv.RootNode.Data;

            control.listener.CreateGrammar(
                INVNAME,
                new string[] {
                    "read it",
                    "go there",
                    "close the closet",
                    "describe it" });
        }

        internal override void Start()
        {
            base.Start();
            if (!control.instance.TabConsole.Tabs.ContainsKey(INVNAME))
                return;

            control.listener.ActivateGrammar(INVNAME);
            
            invTab = (InventoryConsole)control.instance.TabConsole.Tabs[INVNAME].Control;
            tree = invTab.invTree;
 
            tree.AfterSelect += OnItemChange;
            tree.AfterExpand += tree_AfterExpand;
            tree.AfterCollapse += tree_AfterCollapse;

            Talker.SayMore("Inventory");
            SayWhere();
        }

        internal override void Stop()
        {
            base.Stop();
            Listener.DeactivateGrammar(INVNAME);

            if (tree == null) return;
            tree.AfterSelect -= OnItemChange;
            tree.AfterExpand -= tree_AfterExpand;
            tree.AfterCollapse -= tree_AfterCollapse;
        }

        private void tree_AfterCollapse(object sender, TreeViewEventArgs e)
        {
            Talker.SayMore(DescriptiveName( selected ) + " collapsed.");
        }

        private void tree_AfterExpand(object sender, TreeViewEventArgs e)
        {
            Talker.SayMore(DescriptiveName(selected) + " expanded.");
        }

        private void OnItemChange(object sender, TreeViewEventArgs e)
        {
            selected = (InventoryBase)e.Node.Tag;
            SayWhere();
        }

        internal override bool Hear(string cmd)
        {
            if (base.Hear(cmd)) return true;

            if (selected is InventoryNotecard notecard && cmd == "read it")
            {
                control.converse.AddInterruption(
                    new InvNotecard(control, notecard));
                return true;
            }

            if (selected is InventoryLandmark landmark && cmd == "go there")
            {
                control.converse.AddInterruption(
                    new InvLandmark( control, landmark));
                return true;
            }

            switch (cmd)
            {
                case "describe it":
                {
                    ListNode();
                    break;
                }
                case "close my inventory":
                case "close the closet":
                    Talker.SayMore("The closet is closed.");
                    control.listener.DeactivateGrammar(INVNAME);
                    control.converse.SelectConversation("chat");
                    return true;

                default:
                    return false;
            }
            return true;
        }

        private void SayWhere()
        {
            Talker.SayMore("Looking at " + DescriptiveName( selected ));
        }

        private void ListNode()
        {
            SayWhere();

            if (selected is InventoryItem)
            {
                if (!(selected is InventoryFolder))
                {
                    Talker.SayMore("Going farther is not yet implemented.");
                }
            }
        }

        private string DescriptiveName(InventoryBase item)
         {
             string name = NiceName(item.Name);

             if (item is InventoryFolder)
                 return name + " folder";

             if (item is InventoryNotecard)
                 return name + ", a notecard";

             if (item is InventoryWearable wearable)
                 return name + ", a " + WearableType(wearable);

             if (item is InventoryLandmark)
                 return name + ", a landmark";

             // TODO other types

             return name;
         }

        private string ItemType(InventoryItem item)
        {
            switch (item.AssetType)
            {
                case AssetType.Notecard: return "note card";
                case AssetType.Folder: return "folder";
                case AssetType.Clothing: return "piece of clothing";
                case AssetType.CallingCard: return "calling card";
                case AssetType.Landmark: return "landmark";
                case AssetType.Bodypart: return "body part";
                default: return "thing";
            }

        }

        /// <summary>
        /// Get a pronouncable form of each wearable type.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private string WearableType(InventoryWearable item)
        {
            switch (item.WearableType)
            {
                case OpenMetaverse.WearableType.Shirt: return "shirt";
                case OpenMetaverse.WearableType.Pants: return "pants";
                case OpenMetaverse.WearableType.Skirt: return "skirt";
                case OpenMetaverse.WearableType.Shoes: return "shoes";
                case OpenMetaverse.WearableType.Jacket: return "jacket";
                case OpenMetaverse.WearableType.Socks: return "socks";
                case OpenMetaverse.WearableType.Undershirt: return "undershirt";
                case OpenMetaverse.WearableType.Underpants: return "underpants";
                case OpenMetaverse.WearableType.Skin: return "skin";
                case OpenMetaverse.WearableType.Eyes: return "eyes";
                case OpenMetaverse.WearableType.Gloves: return "gloves";
                case OpenMetaverse.WearableType.Hair: return "hair";
                case OpenMetaverse.WearableType.Shape: return "body shape";
                //case OpenMetaverse.WearableType.Universal: 
                default: return "clothes";
            }
        }

        private void Wearing()
        {

        }
    }
}
