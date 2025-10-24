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
using System.Windows.Forms;

namespace Radegast
{
    public interface IContextAction : IDisposable
    {
        void IContextAction(RadegastInstanceForms instance);

        /// <summary>
        /// Generate a list of ToolStripMenuItems that might be embedded into a ContextMenuStrip host
        /// </summary>
        /// <param name="target">the context-sensitive item</param>
        /// <param name="type">the dereferenced type</param>
        /// <returns>List of ToolStripMenuItem that will be appended to the current menu</returns>
        IEnumerable<ToolStripMenuItem> GetToolItems(object target, Type type);

        /// <summary>
        /// If the menu item is Enabled
        /// </summary>
        /// <param name="target">the context-sensitive item</param>
        /// <returns>false when the menu Item should be greyed</returns>
        bool IsEnabled(object target);

        /// <summary>
        /// The menu Item's text based on target 
        /// </summary>
        /// <param name="target">the context-sensitive item</param>
        /// <returns>true if the Action is available - for instance some Avatar might not even be logged in so "follow" would not even show up</returns>
        string LabelFor(object target);

        /// <summary>
        /// If the context menu is usable to the target
        /// </summary>
        /// <param name="target">the context-sensitive item</param>
        /// <param name="type">the dereferenced type</param>
        /// <returns>The name that is displayed in a menu of options</returns>
        bool Contributes(object target, Type type);

        /// <summary>
        /// The Action code goes here
        /// </summary>
        /// <param name="sender">the Control that originates the event</param>
        /// <param name="e">The EventArgs proprietary to the Controls event like MouseEventArgs or KeyEventArgs etc</param>
        /// <param name="target">The Context Item that is really targeted</param>
        void OnInvoke(object sender, EventArgs e, object target);
    }
}