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
using OpenMetaverse;

namespace Radegast
{
    public partial class PluginsTab : RadegastTabControl
    {
        public PluginsTab(RadegastInstanceForms instance)
            : base(instance)
        {
            InitializeComponent();
            ListPlugins();
            PluginsTab_SizeChanged(this, EventArgs.Empty);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void ListPlugins()
        {
            List<PluginInfo> plugins = instance.PluginManager.Plugins;
            lvwPlugins.Items.Clear();

            foreach (PluginInfo plugin in plugins)
            {
                ListViewItem item = new ListViewItem {Text = plugin.Attribures.Name, Tag = plugin};
                item.SubItems.Add(plugin.Attribures.Description);
                item.SubItems.Add(plugin.Attribures.Version);
                lvwPlugins.Items.Add(item);
            }

        }

        private void PluginsTab_SizeChanged(object sender, EventArgs e)
        {
            float w = (float)(lvwPlugins.Width - 30);
            lvwPlugins.Columns[0].Width = (int)(w * 0.3);
            lvwPlugins.Columns[1].Width = (int)(w * 0.6);
            lvwPlugins.Columns[2].Width = (int)(w * 0.1);
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            ListPlugins();
        }

        private void btnUnload_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in lvwPlugins.SelectedItems)
            {
                var plugin = item.Tag as PluginInfo;
                if (plugin == null)
                {
                    Logger.Warn($"ERROR Attempting to unload a null plugin: {item}");
                    continue;
                }

                try
                {
                    instance.PluginManager.UnloadPlugin(plugin);
                }
                catch (Exception ex)
                {
                    instance.ShowNotificationInChat($"ERROR unable to unload plugin: {plugin} because {ex}", ChatBufferTextStyle.Error);
                }
            }
            ListPlugins();
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Load plugin file (source or compiled)";
                dlg.Filter = "Plugin source (*.cs)|*.cs|Plugin binary (*.dll)|*.dll|All files (*.*)|*.*";
                dlg.Multiselect = true;
                DialogResult res = dlg.ShowDialog();

                if (res == DialogResult.OK)
                {
                    foreach (var name in dlg.FileNames)
                    {
                        try
                        {
                            instance.PluginManager.LoadPlugin(name);
                        }
                        catch (Exception ex)
                        {
                            instance.ShowNotificationInChat($"ERROR unable to load plugin: {name} because {ex}", ChatBufferTextStyle.Error);
                        }
                    }

                    ListPlugins();
                }
            }
        }

        private void btnReload_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in lvwPlugins.SelectedItems)
            {
                var plugin = item.Tag as PluginInfo;
                if (plugin == null)
                {
                    Logger.Warn($"ERROR Attempting to reload a null plugin: {item}");
                    continue;
                }

                try
                {
                    instance.PluginManager.UnloadPlugin(plugin);
                    instance.PluginManager.LoadPlugin(plugin.FileName);
                }
                catch (Exception ex)
                {
                    instance.ShowNotificationInChat($"ERROR unable to reload plugin: {item} because {ex}", ChatBufferTextStyle.Error);
                }
                ListPlugins();
            }
        }
    }
}
