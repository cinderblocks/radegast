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
using System.ComponentModel;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// A Radegast based ContextMenuStrip
    ///   the Opened/Closing/Selected/Clicked events may be subscribed to by plugins
    /// This class is our Drop In replacement that add accessibility features to context menus
    /// 
    /// To use:  
    ///   1) In the 'Forms designer', initially drag in or use a ContextMenuStrip
    ///   2) The replace the declared type and constructor with this class in the MyForm.Designer.cs
    ///   3) Pretend it is a typical ContextMenuStrip
    /// 
    /// To hook:
    /// 
    /// <pre>
    ///public class TestContextMenuOpen
    ///{
    ///    static TestContextMenuOpen testContextMenu = new TestContextMenuOpen();
    ///    private RadegastContextMenuStrip TheirInterest;
    ///    TestContextMenuOpen()
    ///    {
    ///        RadegastContextMenuStrip.OnContentMenuOpened += Test_OnContentMenuOpened;
    ///        RadegastContextMenuStrip.OnContentMenuItemSelected += Test_OnContentMenuItemSelected;
    ///        RadegastContextMenuStrip.OnContentMenuItemClicked += Test_OnContentMenuItemClicked;
    ///        RadegastContextMenuStrip.OnContentMenuClosing += Test_OnContentMenuClosing;
    ///    }
    ///
    ///    private void Test_OnContentMenuItemClicked(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
    ///    {
    ///        Console.WriteLine("I hope you meant to " + e.MenuItem.Text + "  " + e.Selection + "!");
    ///        if (!e.MenuItem.Enabled)
    ///        {
    ///            Console.WriteLine("If not do not worry it was not enabled ");
    ///        }
    ///    }
    ///
    ///    private void Test_OnContentMenuItemSelected(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
    ///    {
    ///        lock (testContextMenu)
    ///        {
    ///            if (e.MenuItem == null)
    ///            {
    ///                Console.WriteLine("The last menu selection is not hightlighted by the mouse anymore so do not click");
    ///            }
    ///            else if (!e.MenuItem.Enabled)
    ///            {
    ///                Console.WriteLine("You cannot " + e.MenuItem.Text + " at this time to " + e.Selection);
    ///            }
    ///            else
    ///            {
    ///                Console.WriteLine("You can " + e.MenuItem.Text + " " + e.Selection + " if you press enter or click");
    ///            }
    ///        }
    ///    }
    ///
    ///    private void Test_OnContentMenuClosing(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
    ///    {
    ///        lock (testContextMenu)
    ///        {
    ///            Console.WriteLine("You can no longer see the Menu: " + TheirInterest);
    ///            TheirInterest = null;
    ///        }
    ///    }
    ///
    ///    private void Test_OnContentMenuOpened(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
    ///    {
    ///        lock (testContextMenu)
    ///        {
    ///            TheirInterest = e.Menu;
    ///            Console.WriteLine("You are looking at Menu: " + TheirInterest);
    ///            Console.WriteLine("The Item you are going to do something to is: " + e.Selection);
    ///            foreach (var item in e.Menu.AllChoices())
    ///                if (item.Enabled)
    ///                    Console.WriteLine(" You can: " + item.Text);
    ///                else
    ///                    Console.WriteLine(" cannot: " + item.Text);
    ///        }
    ///    }
    ///}     
    /// </pre>  
    /// </summary>
    public class RadegastContextMenuStrip : ContextMenuStrip
    {
        /// <summary>
        /// MenuEventArgs e.Menu contains the reference to the RadegastContextMenuStrip 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public delegate void ContextMenuCallback(object sender, ContextMenuEventArgs e);

        /// <summary>
        /// Fires whenever a context menu is "Opening" (not yet opened) anywhere from Radegast
        ///   Accessibility should be more interested in OnContentMenuOpened
        ///   This is for times context menus are busy deciding what to enable/disable
        /// </summary>
        public static event ContextMenuCallback OnContentMenuOpening;

        /// <summary>
        /// Fires whenever a context menu is "Opened" anywhere from Radegast
        /// </summary>
        public static event ContextMenuCallback OnContentMenuOpened;

        /// <summary>
        /// Fires whenever a context menu is "Closing" anywhere from Radegast
        /// </summary>
        public static event ContextMenuCallback OnContentMenuClosing;

        /// <summary>
        /// Fires whenever a context menu item is "Selected" (Hightlighted) anywhere from Radegast
        /// </summary>
        public static event ContextMenuCallback OnContentMenuItemSelected;

        /// <summary>
        /// Fires whenever a context menu item is "Clicked" anywhere from Radegast
        /// </summary>
        public static event ContextMenuCallback OnContentMenuItemClicked;

        /// <summary>
        /// Arguments for tab events
        /// </summary>
        public class ContextMenuEventArgs : EventArgs
        {
            /// <summary>
            /// Menu that was manipulated in the event
            /// </summary>
            public RadegastContextMenuStrip Menu;

            /// <summary>
            /// The Menu Item that was clicked or selected on the System.Windows.Forms.ToolStrip
            /// </summary>
            public ToolStripDropDownItem MenuItem;

            /// <summary>
            /// The object (like AvatarListItem) that the context menu was/is targeting
            /// </summary>
            public object Selection;

            public ContextMenuEventArgs(RadegastContextMenuStrip tab, ToolStripDropDownItem item, object target)
            {
                Menu = tab;
                MenuItem = item;
                Selection = item;
            }
        }

        /// <summary>
        /// If the context menu has a valid target
        /// </summary>
        public bool HasSelection;
        /// <summary>
        /// The target of the ContextMenu
        /// </summary>
        public object Selection;

        /// <summary>
        /// The MenuItem Currently Highlighted/Selected
        /// </summary>
        public ToolStripDropDownItem MenuItem;

        /// <summary>
        /// Locking arround changing the MenuItem
        /// </summary>
        private readonly object _selectionLock = new object();

        /// <summary>
        /// Children we have added our hooks into
        /// </summary>
        public readonly HashSet<ToolStripDropDownItem> KnownItems = new HashSet<ToolStripDropDownItem>();

        // for Control Character we'd use when the Keys.Apps is not available 
        public const Keys ContexMenuKeyCode = Keys.Enter;

        /// <summary>
        /// Initializes a new instance of the System.Windows.Forms.ContextMenuStrip class and associates it with the specified container.
        /// </summary>
        /// <param name="components">A component that implements System.ComponentModel.IContainer that is the container of the System.Windows.Forms.ContextMenuStrip</param>
        public RadegastContextMenuStrip(IContainer components)
            : base(components)
        {
            RegisterEvents();
        }

        /// <summary>
        /// Initializes a new instance of the System.Windows.Forms.ContextMenuStrip class.
        /// </summary>
        public RadegastContextMenuStrip()
        {
            RegisterEvents();
        }

        /// <summary>
        /// Events we need on the ContextMenuStrip
        /// </summary>
        private void RegisterEvents()
        {
            Opening += Rad_Menu_Opening;
            Opened += Rad_Menu_Opened;
            Closing += Rad_Menu_Closing;
            KeyUp += Rad_Menu_KeyUp;
            ItemClicked += Rad_Menu_ItemClicked;
            PreviewKeyDown += Rad_Menu_PreviewKeyDown;
            ItemAdded += Rad_OnItemAdded;
            ItemRemoved += Rad_OnItemRemoved;
            ScanAndHookItems();
        }

        private void Rad_OnItemRemoved(object sender, ToolStripItemEventArgs e)
        {
            if (!(e.Item is ToolStripDropDownItem Item)) return;

            lock (KnownItems)
            {
                DeregisterItemEvents(Item);
                WriteDebug("removed {0}", e.Item);
            }
        }


        private void Rad_OnItemAdded(object sender, ToolStripItemEventArgs e)
        {
            RegisterItemEvents(e.Item as ToolStripDropDownItem);
        }

        private void WriteDebug(string s, params object[] args)
        {
            // maybe for debugging why a context menu isnt working correctly
            //Console.WriteLine(s + " " + ToString(),args);
        }

        public override string ToString()
        {
            return $"RadMenu {Name} MenuItem='{MenuItem}' selection='{Selection}'";
        }


        private void ScanForSelected()
        {
            lock (KnownItems)
                foreach (ToolStripDropDownItem item in KnownItems)
                {
                    if (item.Selected)
                    {
                        SetMenuItemSelected(item);
                        return;
                    }
                }
        }

        private void Rad_Menu_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            ScanForSelected();
        }

        private void Rad_Menu_KeyUp(object sender, KeyEventArgs e)
        {
            ScanForSelected();
        }


        private void Rad_Menu_Opened(object sender, EventArgs e)
        {
            WriteDebug("Menu_Opened: {0} {1}", sender, e);
            if (OnContentMenuOpened == null) return;

            try
            {
                OnContentMenuOpened(sender, new ContextMenuEventArgs(this, MenuItem, Selection));
            }
            catch (Exception ex)
            {
                Logger.DebugLog("ERROR " + ex + " in " + this);
            }
        }

        private void Rad_Menu_Opening(object sender, CancelEventArgs e)
        {
            WriteDebug("Menu_Opening: {0} {1}", sender, e.Cancel);

            if (RadegastInstanceForms.Instance.GlobalSettings["theme_compatibility_mode"])
            {
                RenderMode = ToolStripRenderMode.System;
            }
            e.Cancel = false;
            OnContentMenuOpening?.Invoke(sender, new ContextMenuEventArgs(this, MenuItem, Selection));
        }

        private void Rad_Menu_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            WriteDebug("Menu_Closing: {0} {1}", sender, e.CloseReason);
            if (OnContentMenuClosing == null) return;

            try
            {
                OnContentMenuClosing(sender, new ContextMenuEventArgs(this, MenuItem, Selection));
            }
            catch (Exception ex)
            {
                Logger.DebugLog("ERROR " + ex + " in " + this);
            }
        }

        // This might be both the best Keyboard and Mouse event
        private void Rad_Menu_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            SetMenuItemSelected(e.ClickedItem);
            WriteDebug("Menu_ItemClicked: {0} {1}", sender, e.ClickedItem);
            if (OnContentMenuItemClicked == null) return;
            try
            {
                OnContentMenuItemClicked(sender, new ContextMenuEventArgs(this, MenuItem, Selection));
            }
            catch (Exception ex)
            {
                Logger.DebugLog("ERROR " + ex + " in " + this);
            }
        }

        /// <summary>
        /// This main site that all clues hit for selection changing events
        /// </summary>
        /// <param name="sender"></param>
        private void SetMenuItemSelected(object sender)
        {
            if (!(sender is ToolStripDropDownItem stripDropDownItem)) return;

            // dereference a chain of dropdown items
            if (stripDropDownItem.HasDropDownItems)
            {
                foreach (var s in stripDropDownItem.DropDownItems)
                {
                    if (!(s is ToolStripDropDownItem sub)) continue;

                    if (sub.Selected)
                    {
                        SetMenuItemSelected(sub);
                        return;
                    }
                }
            }
            // otherwise use it
            lock (_selectionLock)
            {
                if (MenuItem == stripDropDownItem) return;
                MenuItem = stripDropDownItem;
                if (string.IsNullOrEmpty(MenuItem.ToolTipText) || MenuItem.ToolTipText.StartsWith(" "))
                {
                    MenuItem.ToolTipText = " " + MenuItem.Text + " " + Selection;
                }
                OnItemSelected(MenuItem);
            }
        }

        private void OnItemSelected(ToolStripDropDownItem item)
        {
            lock (_selectionLock)
            {
                WriteDebug("OnMenuItemChanged {0}", this);
                if (OnContentMenuItemSelected == null) return;
                try
                {
                    OnContentMenuItemSelected(this, new ContextMenuEventArgs(this, MenuItem, Selection));
                }
                catch (Exception ex)
                {
                    Logger.DebugLog("ERROR " + ex + " in " + this);
                }
            }
        }

        private void ScanAndHookItems()
        {
            lock (KnownItems)
            {
                foreach (var i in Items)
                {
                    ToolStripDropDownItem item = i as ToolStripDropDownItem;
                    RegisterItemEvents(item);
                }
            }
        }

        /// <summary>
        /// Events we need on the ToolStripDropDownItem
        /// </summary>
        private void RegisterItemEvents(ToolStripDropDownItem item)
        {
            // Separators == null
            if (item == null) return;
            lock (KnownItems)
            {
                if (KnownItems.Add(item))
                {
                    // Vital events
                    item.Click += Rad_Item_Click;
                    item.DropDownItemClicked += Rad_Item_Clicked;
                    item.MouseHover += Rad_Item_Enter;
                    item.MouseEnter += Rad_Item_Enter;
                    item.MouseLeave += Rad_Item_Leave;
                    item.DropDownOpening += Rad_Item_Opening;

                    // Not so vital events that may lead to discovering some accessiblity features
                    item.GiveFeedback += Rad_Item_GiveFeedback;

                    // Register childs
                    if (item.HasDropDownItems)
                    {
                        foreach (var collection in item.DropDownItems)
                        {
                            RegisterItemEvents(collection as ToolStripDropDownItem);
                        }
                    }
                }
            }
        }

        private void DeregisterItemEvents(ToolStripDropDownItem item)
        {
            // Separators == null
            if (item == null) return;
            lock (KnownItems)
            {
                if (KnownItems.Remove(item))
                {
                    // Vital events
                    item.Click -= Rad_Item_Click;
                    item.DropDownItemClicked -= Rad_Item_Clicked;
                    item.MouseHover -= Rad_Item_Enter;
                    item.MouseEnter -= Rad_Item_Enter;
                    item.MouseLeave -= Rad_Item_Leave;
                    item.DropDownOpening -= Rad_Item_Opening;

                    // Not so vital events that may lead to discovering some accessiblity features
                    item.GiveFeedback -= Rad_Item_GiveFeedback;

                    // Deregister children
                    if (item.HasDropDownItems)
                    {
                        foreach (var collection in item.DropDownItems)
                        {
                            DeregisterItemEvents(collection as ToolStripDropDownItem);
                        }
                    }
                }
            }
        }


        private void Rad_Item_Opening(object sender, EventArgs e)
        {
            if (RadegastInstanceForms.Instance.GlobalSettings["theme_compatibility_mode"])
            {
                RenderMode = ToolStripRenderMode.System;
            }

            ToolStripDropDownItem stripDropDownItem = sender as ToolStripDropDownItem;
            if (stripDropDownItem == null) return;
            SetMenuItemSelected(stripDropDownItem);
            if (stripDropDownItem.HasDropDownItems)
            {
                WriteDebug("Item_DropDownOpening: {0} {1}", sender, e);
            }
        }

        private void Rad_Item_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            SetMenuItemSelected(sender);
            WriteDebug("Item_GiveFeedback: {0} {1}", sender, e.Effect);
        }

        private void Rad_Item_Click(object sender, EventArgs e)
        {
            SetMenuItemSelected(sender);
        }

        private void Rad_Item_Enter(object sender, EventArgs e)
        {
            SetMenuItemSelected(sender);
        }

        private void Rad_Item_Leave(object sender, EventArgs e)
        {
            if (!(sender is ToolStripDropDownItem stripDropDownItem)) return;

            lock (_selectionLock)
            {
                if (MenuItem == stripDropDownItem)
                {
                    WriteDebug("Item_Leave: {0} {1}", sender, e);
                    MenuItem = null;
                    OnItemSelected(MenuItem);
                }
            }
        }

        private void Rad_Item_Clicked(object sender, ToolStripItemClickedEventArgs e)
        {
            SetMenuItemSelected(sender);
            WriteDebug("Item_Clicked: {0} {1}", sender, e.ClickedItem);
        }

        public IEnumerable<ToolStripItem> Choices()
        {
            List<ToolStripItem> lst = new List<ToolStripItem>();
            foreach (ToolStripItem item in Items)
            {
                lst.Add(item);
            }
            return lst;
        }
        public IEnumerable<ToolStripItem> AllChoices()
        {
            List<ToolStripItem> lst = new List<ToolStripItem>();
            lock (KnownItems)
                foreach (ToolStripDropDownItem item in KnownItems)
                {
                    lst.Add(item);
                }
            return lst;
        }
    }

    public interface IContextMenuProvider
    {
        RadegastContextMenuStrip GetContextMenu();
    }
}