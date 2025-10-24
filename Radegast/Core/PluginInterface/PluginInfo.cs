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
using System.Linq;

namespace Radegast
{
    /// <summary>
    /// Information about loaded plugin
    /// </summary>
    public class PluginInfo
    {
        /// <summary>File name from which the plugin was loaded, cannot load plugin twice from the same file</summary>
        public string FileName { get; }

        /// <summary>Display name</summary>
        public string DisplayName { get; set; }

        /// <summary>Is plugin started</summary>
        public bool IsStarted { get; private set; }

        /// <summary>Plugin attributes</summary>
        public PluginAttribute Attribures { get; }

        /// <summary>Plugin instance</summary>
        private IRadegastPlugin PluginInstance { get; }

        public override string ToString() => DisplayName;

        /// <summary>
        /// Constructs a new PluginInfo
        /// </summary>
        /// <param name="filename">Path to the plugin.</param>
        /// <param name="plugin">Valid plugin instance. Must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="plugin"/> is <see langword="null"/></exception>
        public PluginInfo(string filename, IRadegastPlugin plugin)
        {
            PluginInstance = plugin ?? throw new ArgumentNullException(nameof(plugin));
            FileName = filename;
            DisplayName = PluginInstance.GetType().Name;

            PluginAttribute pluginAttributes = null;
            try
            {
                var customAttributes = Attribute.GetCustomAttributes(PluginInstance.GetType());
                pluginAttributes = customAttributes.First(attribute => attribute is PluginAttribute) as PluginAttribute;
            }
            catch
            {
                // Suppress
            }

            Attribures = pluginAttributes ?? new PluginAttribute {Name = PluginInstance.GetType().FullName};
        }

        /// <summary>
        /// Starts the plugin. Attempts to start a plugin that has already been started will result in an exception.
        /// </summary>
        /// <param name="instance">Radegast instance</param>
        /// <exception cref="Exception">On failure</exception>
        public void Start(RadegastInstanceForms instance)
        {
            if (!IsStarted)
            {
                PluginInstance.StartPlugin(instance);
                IsStarted = true;
            }
            else
            {
                throw new Exception("Attempting to start a plugin that is already in the running state.");
            }
        }

        /// <summary>
        /// Stops the plugin. Attempts to stop a plugin that has already been stopped will result in an exception.
        /// </summary>
        /// <param name="instance">Radegast instance</param>
        /// <exception cref="Exception">On failure</exception>
        public void Stop(RadegastInstanceForms instance)
        {
            if (IsStarted)
            {
                PluginInstance.StopPlugin(instance);
                IsStarted = false;
            }
            else
            {
                throw new Exception("Attempting to stop a plugin that is already in the stopped state.");
            }
        }
    }
}
