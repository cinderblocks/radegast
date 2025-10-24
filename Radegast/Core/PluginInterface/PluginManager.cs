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
using System.IO;
using System.Reflection;
using System.CodeDom.Compiler;
using System.Linq;
using System.Text;
using OpenMetaverse;
using Microsoft.CSharp;

namespace Radegast
{
    /// <summary>
    /// Handles loading Radegast plugins
    /// </summary>
    public class PluginManager : IDisposable
    {
        /// <summary>List of files that should not be scanned for plugins</summary>
        private static readonly List<string> PluginBlackList = new List<string>(new string[]
        {
            "AIMLbot.dll",
            "Binaron.Serializer.dll",
            "BouncyCastle.Crypto.dll",
            "C5.dll",
            "CommandLine.dll",
            "CoreJ2K.dll",
            "CoreJ2K.Skia.dll",
            "DbgHelp.dll",
            "fmod.dll",
            "IKVM.dll",
            "libSkiaSharp.dll",
            "log4net.dll",
            "Meebey.SmartIrc4net.dll",
            "Monobjc.Cocoa.dll",
            "Monobjc.dll",
            "NetSparkleUpdater.UI.WinForms.dll",
            "Newtonsoft.Json.dll",
            "NSec.Cryptography.dll",
            "nvdacontrollerClient32.dll",
            "nvdacontrollerClient64.dll",
            "OpenCyc.dll",
            "openjpeg-dotnet.dll",
            "OpenJpegDotNetNative.dll",
            "OpenTK",
            "OpenTK.GLControl.dll",
            "ortp.dll",
            "Pfim.dll",
            "PrimMesher.dll",
            "protobuf-net.dll",
            "Radegast.Core.dll",
            "RadSpeechLin.dll",
            "RadSpeechMac.dll",
            "RadSpeechWin.dll",
            "SmartThreadPool",
            "SLVoice.exe",
            "Tao.OpenGl.dll",
            "Tao.Platform.Windows.dll",
            "UniversalSpeech.dll",
            "vivoxsdk.dll",
            "XmlRpcCore.dll",
            "zlib.net.dll",
            "zlib1.dll",
            // Blacklist all of these
            "OpenMetaverse.",
            "LibreMetaverse.",
            "Microsoft.",
            "SkiaSharp.",
            "System.",
        });

        /// <summary>Collection of assemblies that are referenced when compiling scripts.</summary>
        private static readonly string[] ReferencedAssemblies = new string[]
        {
            "LibreMetaverse.StructuredData.dll",
            "LibreMetaverse.Types.dll",
            "LibreMetaverse.dll",
            "Radegast.exe",
            "System.dll",
            "System.ComponentModel.Composition.dll",
            "System.Core.dll",
            "System.Xml.dll",
            "System.Drawing.dll",
            "System.Drawing.Common.dll",
            "System.Threading.Channels.dll",
            "System.Windows.Forms.dll",
        };

        /// <summary>List of file extensions that could potentially hold plugins</summary>
        private static readonly List<string> AllowedPluginExtensions = new List<string>(new string[]
        {
            ".cs",
            ".dll",
            ".exe"
        });

        private RadegastInstanceForms Instance { get; }

        /// <summary>Collection of all loaded plugins</summary>
        public List<PluginInfo> Plugins { get; } = new List<PluginInfo>();

        /// <summary>
        /// Creates new PluginManager
        /// </summary>
        /// <param name="instance">Radegast instance PluginManager is associated with</param>
        public PluginManager(RadegastInstanceForms instance)
        {
            Instance = instance;
        }

        /// <summary>
        /// Unloads all plugins
        /// </summary>
        public void Dispose()
        {
            lock (Plugins)
            {
                // Make a copy so we can have something to iterate over while removing items from the original list.
                var pluginsToRemove = Plugins.ToList();
                foreach (var plugin in pluginsToRemove)
                {
                    try
                    {
                        UnloadPlugin(plugin);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ERROR unloading plugin: {plugin} because {ex}", Helpers.LogLevel.Warning, ex);
                    }
                }
            }
        }

        /// <summary>
        /// Unloads a plugin
        /// </summary>
        /// <param name="plugin">Plugin to unload</param>
        public void UnloadPlugin(PluginInfo plugin)
        {
            lock (Plugins)
            {
                var pluginsToUnload = Plugins.FindAll(loadedPlugin => plugin.FileName == loadedPlugin.FileName);
                foreach (var pluginToUnload in pluginsToUnload)
                {
                    try
                    {
                        pluginToUnload.Stop(Instance);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ERROR stopping plugin: {pluginToUnload} because {ex}", Helpers.LogLevel.Warning, ex);
                    }
                    Plugins.Remove(pluginToUnload);
                }
            }
        }

        /// <summary>
        /// Loads and starts all plugins in the specified directory.
        /// </summary>
        /// <exception cref="Exception">On failure</exception>
        public void LoadPluginsInDirectory(string pluginDirectory)
        {
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                return;
            }

            foreach (var pluginPath in Directory.GetFiles(pluginDirectory))
            {
                LoadPlugin(pluginPath);
            }
        }

        /// <summary>
        /// Loads and starts the specified plugin.
        /// </summary>
        /// <exception cref="Exception">On failure</exception>
        public void LoadPlugin(string pluginPath)
        {
            var newPlugins = new List<PluginInfo>();

            try
            {
                var foundPlugins = LoadPluginFile(pluginPath);
                newPlugins.AddRange(foundPlugins);
            }
            catch (Exception ex)
            {
                Logger.Log($"ERROR unable to load plugin: {pluginPath} because {ex}", Helpers.LogLevel.Warning);
            }

            StartPlugins(newPlugins);
        }

        /// <summary>
        /// Starts the specified plugins.
        /// </summary>
        /// <param name="pluginsToStart">Plugins to start.</param>
        /// <exception cref="Exception">On failure</exception>
        private void StartPlugins(IEnumerable<PluginInfo> pluginsToStart)
        {
            lock (Plugins)
            {
                foreach (var newPlugin in pluginsToStart)
                {
                    Logger.DebugLog($"Starting {newPlugin}");
                    try
                    {
                        newPlugin.Start(Instance);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ERROR in Starting Radegast Plugin: {newPlugin} because {ex}", Helpers.LogLevel.Warning);
                    }
                    Plugins.Add(newPlugin);
                }
            }
        }

        /// <summary>
        /// Loads plugins from the specified assembly or script.
        /// </summary>
        /// <param name="path">Path to an assembly or script file containing plugins.</param>
        /// <returns>List of plugins that were successfully loaded.</returns>
        /// <exception cref="Exception">On failure</exception>
        private List<PluginInfo> LoadPluginFile(string path)
        {
            if (IsBlacklisted(path))
            {
                return new List<PluginInfo>();
            }

            if (IsPluginLoaded(path))
            {
                Instance.ShowNotificationInChat($"Plugin already loaded, skipping: {path}");
                return new List<PluginInfo>();
            }

            var extension = Path.GetExtension(path)?.ToLower();
            switch (extension)
            {
                case ".cs":
                    return LoadScript(path);
                case ".dll":
                case ".exe":
                    return LoadAssembly(path);
            }

            return new List<PluginInfo>();
        }

        /// <summary>
        /// Determines if the specified file path is blacklisted and should not be loaded.
        /// </summary>
        /// <param name="path">File path to check.</param>
        /// <returns>True if the file path is blacklisted and should not be loaded.</returns>
        private static bool IsBlacklisted(string path)
        {
            var extension = Path.GetExtension(path)?.ToLower();
            if (string.IsNullOrEmpty(extension)) { return true; }
            if (!AllowedPluginExtensions.Contains(extension)) { return true; }

            var filename = Path.GetFileName(path).ToLower();
            if (string.IsNullOrEmpty(filename)) { return true; }
            if (filename.StartsWith("System.")) { return true; }

            return PluginBlackList.Any(blackListItem => filename.StartsWith(blackListItem.ToLower()));
        }

        /// <summary>
        /// Determines if the plugin at the specified path has already been loaded.
        /// </summary>
        /// <param name="pluginPath">Path to the plugin to check.</param>
        /// <returns>True if the plugin has already been loaded.</returns>
        private bool IsPluginLoaded(string pluginPath)
        {
            lock (Plugins)
            {
                return Plugins.Find(plugin => plugin.FileName == pluginPath) != null;
            }
        }

        /// <summary>
        /// Compiles and loads plugins from a script
        /// </summary>
        /// <param name="scriptPath">Path to the script to compile and load plugins from.</param>
        /// <returns>List of plugins that were successfully loaded.</returns>
        /// <exception cref="Exception">On failure</exception>
        private List<PluginInfo> LoadScript(string scriptPath)
        {
            // *** Generate dynamic compiler
            var compilerOptions = new Dictionary<string, string>
            {
                {"CompilerVersion", "v4.0"}
            };
            var compiler = new CSharpCodeProvider(compilerOptions);
            var compilerParameters = new CompilerParameters();

            // *** Start by adding any referenced assemblies
            compilerParameters.ReferencedAssemblies.AddRange(ReferencedAssemblies);

            // *** Load the resulting assembly into memory
            compilerParameters.GenerateInMemory = true;
            compilerParameters.GenerateExecutable = false;

            // *** Now compile the whole thing
            var source = File.ReadAllText(scriptPath);
            var compilerResults = compiler.CompileAssemblyFromSource(compilerParameters, source);

            // *** Check for compilation errors
            if (compilerResults.Errors.HasErrors)
            {
                var errorMessage = new StringBuilder();

                errorMessage.AppendLine($"Compilation failed: {compilerResults.Errors.Count} errors:");
                for (var i = 0; i < compilerResults.Errors.Count; i++)
                {
                    errorMessage.AppendLine($"Line: {compilerResults.Errors[i].Line} - {compilerResults.Errors[i].ErrorText}");
                }

                Instance.ShowNotificationInChat(errorMessage.ToString(), ChatBufferTextStyle.Alert);
                return new List<PluginInfo>();
            }

            Instance.ShowNotificationInChat("Compilation successful.");
            return LoadPluginsFromAssembly(scriptPath, compilerResults.CompiledAssembly);
        }

        /// <summary>
        /// Loads all plugins from the specified assembly path.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly to load plugins from.</param>
        /// <returns>List of plugins that were successfully loaded.</returns>
        /// <exception cref="Exception">On failure</exception>
        private List<PluginInfo> LoadAssembly(string assemblyPath)
        {
            Assembly pluginAssembly = null;
            // We may try to load plugins from the currently running process. The assembly is already loaded.
            var currentProcessPath = Assembly.GetEntryAssembly()?.Location;
            pluginAssembly = Path.GetFileName(currentProcessPath) == Path.GetFileName(assemblyPath) 
                ? Assembly.GetEntryAssembly() : Assembly.LoadFile(assemblyPath);

            return LoadPluginsFromAssembly(assemblyPath, pluginAssembly);
        }

        /// <summary>
        /// Loads all plugins from the specified assembly.
        /// </summary>
        /// <param name="assemblyPath">Path the assembly was loaded from.</param>
        /// <param name="pluginAssembly">Assembly to load plugins from.</param>
        /// <returns>List of plugins that were successfully loaded.</returns>
        /// <exception cref="Exception">On failure.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="pluginAssembly"/> is <see langword="null"/></exception>
        private List<PluginInfo> LoadPluginsFromAssembly(string assemblyPath, Assembly pluginAssembly)
        {
            if (pluginAssembly == null)
            {
                throw new ArgumentNullException(nameof(pluginAssembly));
            }

            var loadedPlugins = new List<PluginInfo>();

            foreach (var type in pluginAssembly.GetTypes())
            {
                if (typeof(IRadegastPlugin).IsAssignableFrom(type))
                {
                    if (type.IsInterface)
                    {
                        continue;
                    }

                    try
                    {
                        var newPlugin = LoadPluginFromType(assemblyPath, type);
                        loadedPlugins.Add(newPlugin);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Cannot load {type.Name} from {pluginAssembly.FullName} because: {ex}", Helpers.LogLevel.Warning);
                    }
                }
                else
                {
                    Instance.CommandsManager.LoadType(type);
                    Instance.ContextActionManager.LoadType(type);
                }
            }

            return loadedPlugins;
        }

        /// <summary>
        /// Instantiates a new plugin from the specified type.
        /// </summary>
        /// <param name="pluginPath">Path to the plugin or script.</param>
        /// <param name="pluginType">Type to instantiate a new plugin from.</param>
        /// <returns>Plugin that was loaded from the specified type.</returns>
        /// <exception cref="Exception">On failure.</exception>
        private PluginInfo LoadPluginFromType(string pluginPath, Type pluginType)
        {
            IRadegastPlugin pluginInstance;

            // Does the assembly have a constructor that has a RadegastInstance parameter?
            var constructorInfo = pluginType.GetConstructor(new Type[] { typeof(RadegastInstance) });
            if (constructorInfo != null)
            {
                pluginInstance = (IRadegastPlugin) constructorInfo.Invoke(new object[] {Instance});
            }
            else
            {
                // Does the assembly have a constructor?
                constructorInfo = pluginType.GetConstructor(Type.EmptyTypes);
                if (constructorInfo != null)
                {
                    pluginInstance = (IRadegastPlugin) constructorInfo.Invoke(null);
                }
                else
                {
                    throw new Exception($"No suitable constructor found in {pluginType.Name}.");
                }
            }

            return new PluginInfo(pluginPath, pluginInstance);
        }
    }
}
