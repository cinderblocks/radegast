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
using System.Windows.Forms;
using OpenMetaverse;
using Radegast;

namespace RadegastSpeech.Conversation
{
    /// <summary>
    /// Talking version of a context menu.
    /// </summary>
    internal class TalkingContextMenu
    {
        private RadegastContextMenuStrip menuStrip;
        private readonly PluginControl control;
        private string menuFor;
        private OpenMetaverse.UUID id;

        internal TalkingContextMenu(PluginControl pc)
        {
            control = pc;
            RadegastContextMenuStrip.OnContentMenuOpened += OnContentMenuOpened;
            RadegastContextMenuStrip.OnContentMenuItemSelected += OnContentMenuItemSelected;
            RadegastContextMenuStrip.OnContentMenuItemClicked += OnContentMenuItemClicked;
            RadegastContextMenuStrip.OnContentMenuClosing += OnContentMenuClosing;
        }

        internal void Start()
        {
            menuStrip = null;
        }

        internal void Stop()
        {
            menuStrip = null;
        }

        /// <summary>
        /// Select context menu by speaking it
        /// </summary>
        /// <param name="txt"></param>
        /// <returns></returns>
        internal bool Hear(string txt)
        {
            if (menuStrip==null) return false;

            if (txt == "cancel")
            {
                menuStrip.Close();
                return true;
            }

            foreach (var item in menuStrip.AllChoices())
            {
                if (item.Text == txt)
                {
                    if (item.Enabled)
                    {
                        control.talker.SayMore("Done.", Talk.BeepType.Good);
                        item.PerformClick();
                    }
                    else
                        control.talker.SayMore(txt + " is disabled.", Talk.BeepType.Bad);

                    return true;
                }
            }
            return false;
        }

        internal void SetObjectName(string name)
        {
        }

        private ToolStripMenuItem MakeSpeechAction()
        {
            return new ToolStripMenuItem("Speech...", null, OnSpeechMenu);
        }

        /// <summary>
        /// Announce the opening of a context menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnContentMenuOpened(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
        {
            lock (e.Menu)
            {
                menuStrip = e.Menu;
                menuFor = string.Empty;
                id = OpenMetaverse.UUID.Zero;

                // Figure out what this menu applies to.
                if (menuStrip.Selection is string selection)
                {
                    menuFor = selection;
                }
                else if (menuStrip.Selection is ListViewItem lv)
                {
                    menuFor = lv.Text;
                }
                else if (menuStrip.Selection is InventoryItem item)
                {
                    // Something in Inventory.
                    menuFor = ((OpenMetaverse.InventoryItem)item).Name;
                }
                else if (menuStrip.Selection is FriendInfo f)
                {
                    // A Friend.
                    menuFor = f.Name;
                }
                else if (menuStrip.Selection is Primitive p)
                {
                    // Something in the Objects list.
                    menuFor = p.Properties != null ? p.Properties.Name : "Loading object";
                }

                // Remove parenthesized distance, etc
                int lParen = menuFor.IndexOf('(');
                if (lParen > 0)
                    menuFor = menuFor.Substring(0, lParen);

                // Stop reading old choices if moving fast.
                control.talker.Flush();
                control.talker.SayMore(menuFor + " menu.", Talk.BeepType.Open);
            }
        }

        /// <summary>
        /// Put up the voice assignment dialog box.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnSpeechMenu( object sender, EventArgs e )
        {
            Form va =
                new GUI.VoiceAssignment(control, menuFor, id);
            va.Show();
        }

        /// <summary>
        /// Announce closing of a context menu.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnContentMenuClosing(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
        {
            lock (e.Menu)
            {
                control.talker.SayMore("Menu closed.", Talk.BeepType.Close);
                menuStrip = null;
            }
        }
        
        private void OnContentMenuItemSelected(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
        {
            lock (e.Menu)
            {
                if (e.MenuItem == null) return;

                if (e.MenuItem.Enabled)
                {
                    control.talker.SayMore(e.MenuItem.Text); 
                }
                else
                {
                    control.talker.SayMore("Disabled " + e.MenuItem.Text);
                }
            }
        }

        private void OnContentMenuItemClicked(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
        {
            if (!e.MenuItem.Enabled)
            {
                control.talker.SayMore(e.MenuItem.Text + " is disabled.", Talk.BeepType.Bad);
                return;
            }

            control.talker.Flush();
            control.talker.SayMore("Doing " + e.MenuItem.Text, Talk.BeepType.Good );
        }
    }
}
