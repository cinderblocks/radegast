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
using OpenMetaverse;
using Radegast;

namespace RadegastSpeech.GUI
{
    public sealed class NotecardReadAction : ContextAction
    {
        private readonly PluginControl control;
        public NotecardReadAction(RadegastInstanceForms inst, PluginControl pc)
            : base(inst)
        {
            control = pc;
            Label = "Read";
            ContextType = typeof(InventoryNotecard);
        }

        public override bool IsEnabled(object target)
        {
            return true;
        }

        /// <summary>
        /// Respond to a "Read Notecard" context menu action
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <param name="target"></param>
        public override void OnInvoke(object sender, EventArgs e, object target)
        {
            InventoryNotecard nc;

            // This action applies to notecards, which can come in
            // various forms.
            if (target is InventoryNotecard notecard)
            {
                nc = notecard;
            }
            else
            {
                control.talker.SayMore("Can not read this notecard.", Talk.BeepType.Bad);
                return;
            }

            // Create a conversation for reading notecards.
            control.converse.AddInterruption(new Conversation.InvNotecard(control, nc));
        }
    }
}