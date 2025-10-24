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
using System.Linq;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public class ContextActionsManager : IDisposable
    {
        private readonly List<IContextAction> contextEventHandlers = new List<IContextAction>();
        private readonly RadegastInstanceForms instance;
        public ContextActionsManager(RadegastInstanceForms instance)
        {
            this.instance = instance;
        }

        /// <summary>
        /// Register a Context Action 
        ///  <code>           RegisterContextAction(typeof(Object), "Clueless Object!",
        ///                   (obj, hand)
        ///                   => Console.WriteLine("I am an Object!: {0} {1} {2}", obj, hand, obj.GetType()));
        /// </code>
        /// </summary>
        /// <param name="libomvType"></param>
        /// <param name="label"></param>
        /// <param name="handler">Action&lt;object,EventArgs&gt;</param>
        public void RegisterContextAction(Type libomvType, string label, EventHandler handler)
        {
            RegisterContextAction(new ContextAction(instance)
                               {
                                   Label = label,
                                   Handler = handler,
                                   ContextType = libomvType,
                                   ExactContextType = true
                               });
        }

        /// <summary>
        /// Deregister a Context Action 
        ///  <code>DeregisterContextAction(typeof(Object), "Clueless Object!")</code>
        /// </summary>
        /// <param name="libomvType"></param>
        /// <param name="label"></param>
        public void DeregisterContextAction(Type libomvType, string label)
        {
            lock (contextEventHandlers)
            {
                contextEventHandlers.RemoveAll(e => e is ContextAction ca 
                                                    && ca.Label == label 
                                                    && libomvType == ca.ContextType);
            }
        }

        public void RegisterContextAction(IContextAction handler)
        {
            lock (contextEventHandlers)
            {
                handler.IContextAction(instance);
                contextEventHandlers.Add(handler);
            }
        }

        /// <summary>
        /// Register a Context Action 
        /// </summary>
        /// <param name="handler"></param>
        public void DeregisterContextAction(IContextAction handler)
        {
            lock (contextEventHandlers)
            {
                contextEventHandlers.Remove(handler);
            }
        }

        /// <summary>
        /// Used by UI forms to add new ContextMenu Items to a Menu they are about to Display based on the Object
        /// </summary>
        /// <param name="strip">The form's menu</param>
        /// <param name="o">The target object</param>
        public void AddContributions(ToolStripDropDown strip, object o)
        {
            SetCurrentItem(strip, o);
            AddContributions(strip, o?.GetType(), o);
        }

        /// <summary>
        /// Used by AddContributions to add new ContextMenu Items to a Menu they are about to Display
        /// </summary>
        /// <param name="strip">The form's menu</param>
        /// <param name="itemsIn">New Items to Add</param>
        public static void AddContributions_Helper(ToolStripDropDown strip, List<ToolStripMenuItem> itemsIn)
        {
            if (itemsIn == null || itemsIn.Count == 0) return;
            List<ToolStripItem> items = new List<ToolStripItem>();
            itemsIn.ForEach(o =>
                                {
                                    string txt = (o.Text ?? "").ToLower().Trim();
                                    if (strip.Items.Cast<ToolStripItem>().Any(item => txt == (item.Text ?? "").ToLower().Trim()))
                                    {
                                        txt = null;
                                    }
                                    if (items.Any(item => txt == (item.Text ?? "").ToLower().Trim()))
                                    {
                                        txt = null;
                                    }
                                    if (txt != null) items.Add(o);
                                });
            if (items.Count == 0) return;
            if (strip.Items.Count > 0)
                items.Insert(0, new ToolStripSeparator());
            strip.Items.AddRange(items.ToArray());
            strip.Closing += ((sender, args) => items.ForEach((o) => strip.Items.Remove(o)));
        }

        /// <summary>
        /// Used by UI forms to add new ContextMenu Items to a Menu they are about to Display
        /// </summary>
        /// <param name="strip">The form's menu</param>        
        /// <param name="type">The type it will target</param>
        /// <param name="obj">the Target ofbject</param>
        /// <param name="controls">Control to search for extra contributions (like buttons)</param>
        public void AddContributions(ToolStripDropDown strip, Type type, object obj, params Control[] controls)
        {
            SetCurrentItem(strip, obj);
            List<ToolStripMenuItem> items = new List<ToolStripMenuItem>();
            GleanContributions(strip, type, obj, controls);
            if (strip.Parent != null) GleanContributions(strip, type, obj, strip.Parent);
            foreach (IContextAction i in contextEventHandlers)
            {
                if (!i.Contributes(obj, type)) continue;
                var v = i.GetToolItems(obj, type);
                if (v != null)
                {
                    items.AddRange(from item in v let alreadyPresent = strip.Items.OfType<ToolStripMenuItem>()
                        .Any(mi => string.Equals((mi.Text ?? ""), (item.Text ?? ""), StringComparison.CurrentCultureIgnoreCase))
                                   where !alreadyPresent select item);
                }
            }
            items.Sort(CompareItems);
            AddContributions_Helper(strip, items);
            
#if DEBUG_CONTEXT
            List<ToolStripMenuItem> item1 = new List<ToolStripMenuItem>();
            string newVariable = obj.GetType() == type
                                     ? type.Name
                                     : string.Format("{0} -> {1}", obj.GetType().Name, type.Name);
            item1.Add(new ToolStripMenuItem(newVariable, null,
                                            (sender, e) =>
                                            instance.DisplayNotificationInChat(
                                                string.Format(" sender={0}\ntarget={1}", ToString(sender), ToString(obj)))

                          )
            {
                Enabled = true,
                ToolTipText = "" + obj
            });

            AddContributions_Helper(strip, item1);
#endif
        }

        /// <summary>
        /// Used by UI forms to set the Context target (saved in the top-level strip if it's a RadegastContextMenuStrip)
        /// </summary>
        /// <param name="strip"></param>
        /// <param name="o"></param>
        public void SetCurrentItem(ToolStripDropDown strip, object o)
        {
            if (strip is RadegastContextMenuStrip rmenu) rmenu.Selection = o;
        }

        private string ToString(object sender)
        {
            string t = sender.GetType().Name + ":";
            if (sender is Control)
            {
                Control control = (Control)sender;
                return $"{t}{control.Text} {control.Name} {ToString(control.Tag)}";
            }
            if (sender is ListViewItem)
            {
                ListViewItem control = (ListViewItem)sender;
                return $"{t}{control.Text} {control.Name} {ToString(control.Tag)}";
            }
            return t + sender;
        }

        /// <summary>
        /// Used by UI forms to add new ContextMenu Items gleaned from Controls
        /// </summary>
        /// <param name="strip">The form's menu</param>        
        /// <param name="type">The type it will target</param>
        /// <param name="obj">the Target object</param>
        /// <param name="controls">Control to search for extra contributions (like buttons)</param>
        public void GleanContributions(ToolStripDropDown strip, Type type, object obj, params Control[] controls)
        {
            SetCurrentItem(strip, obj);
            List<ToolStripMenuItem> items = new List<ToolStripMenuItem>();
            foreach (Control control in controls) GleanContributions_Helper(items, type, control, obj);
            if (obj is Control)
            {
                Control control1 = (Control)obj;
                GleanContributions_Helper(items, type, control1.Parent, obj);
            }
            if (items.Count == 0) return;
            items.Sort(CompareItems);
            AddContributions_Helper(strip, items);
        }

        /// <summary>
        /// Used by GleanContributions to add new ContextMenu Items gleaned from Parent control that has buttons on it
        /// </summary>
        /// <param name="items">Collection of Items to be added to</param>        
        /// <param name="type">The type it will target</param>
        /// <param name="control">Parent control that has buttons on it</param>
        /// <param name="obj">Will becvome the button's target when </param>
        public static void GleanContributions_Helper(ICollection<ToolStripMenuItem> items, Type type, Control control, object obj)
        {
            if (control == null) return;
            if (control is Button)
            {
                Button button = (Button)control;
                string newVariable = obj.GetType() == type
                                         ? type.Name
                                         : $"{obj.GetType().Name} -> {type.Name}";
                items.Add(new ToolStripMenuItem(button.Text, null,
                                                (sender, e) =>
                                                {
                                                    button.PerformClick();
                                                    Logger.DebugLog($"Button: {newVariable} {e} {sender} {obj}");
                                                })
                              {
                                  Enabled = button.Enabled,
                                  Visible = button.Visible,
                                  ToolTipText = "" + obj
                              });
                return;
            }
            foreach (object v in control.Controls)
                GleanContributions_Helper(items, type, (Control)v, obj);
        }

        private static int CompareItems(ToolStripItem i1, ToolStripItem i2)
        {
            if (i1 == i2) return 0;
            if (i1 is ToolStripSeparator)
                return (i2 is ToolStripSeparator) ? 0 : -1;
            int i = string.CompareOrdinal(i1.Text, i2.Text);
            return i == 0 ? i1.GetHashCode().CompareTo(i2.GetHashCode()) : i;
        }

        public void Dispose()
        {
            lock (contextEventHandlers)
            {
                foreach (IContextAction handler in contextEventHandlers)
                {
                    try
                    {
                        handler.Dispose();
                    }
                    catch (Exception) { }
                }
                contextEventHandlers.Clear();
            }

        }
        /// <summary>
        /// Loads context actions
        /// </summary>
        /// <param name="type">Type to try to load</param>
        /// <returns>True if useful types were found and loaded</returns>
        public bool LoadType(Type type)
        {
            if (typeof(IContextAction).IsAssignableFrom(type) && type != typeof(ContextAction))
            {
                try
                {
                    var c = type.GetConstructor(new Type[] { typeof(RadegastInstance) });
                    if (c != null)
                    {
                        IContextAction plug = (IContextAction)c.Invoke(new object[] { instance });
                        RegisterContextAction(plug);
                        return true;
                    }
                    c = type.GetConstructor(Type.EmptyTypes);
                    if (c != null)
                    {
                        IContextAction plug = (IContextAction)c.Invoke(Array.Empty<object>());
                        RegisterContextAction(plug);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ERROR in Radegast Command: " + type + " because " + ex.Message + " " + ex.StackTrace,
                               Helpers.LogLevel.Debug);
                    throw;
                }
            }
            return false;
        }
    }
}