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
using System.IO;
using System.Reflection;
using Radegast;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace RadegastSpeech
{
    [Plugin(Name = "Speech", Description = "Adds TTS and STT accessibility capabilities to Radegast", Version = "0.3")]
    public class PluginControl : IRadegastPlugin
    {
        private const string VERSION = "0.3";
        public RadegastInstanceForms instance;
        internal Talk.Control talker;
        internal Listen.Control listener;
        internal Conversation.Control converse;
        internal Environment.Control env;
        internal Sound.Control sound;
        private ToolStripMenuItem SpeechButton;
        internal IRadSpeech osLayer;
        public OSDMap config;
        private bool started;

        /// <summary>
        /// Plugin start-up entry.
        /// </summary>
        /// <param name="inst"></param>
        /// <remarks>Called by Radegast at start-up</remarks>
        public void StartPlugin(RadegastInstanceForms inst)
        {
            instance = inst;

            // Get configuration settings, and initialize if not found.
            config = instance.GlobalSettings["plugin.speech"] as OSDMap;

            if (config == null)
            {
                config = new OSDMap
                {
                    ["enabled"] = new OSDBoolean(false),
                    ["voices"] = new OSDMap(),
                    ["properties"] = new OSDMap(),
                    ["substitutions"] = new OSDMap()
                };
                instance.GlobalSettings["plugin.speech"] = config;
            }

            if (!config.ContainsKey("enabled_for_inventory"))
            {
                config["enabled_for_inventory"] = true;
            }

            if (!config.ContainsKey("enabled_for_objects"))
            {
                config["enabled_for_objects"] = true;
            }

            if (!config.ContainsKey("enabled_for_friends"))
            {
                config["enabled_for_friends"] = true;
            }

            OSDMap props = (OSDMap)config["properties"];
            if (props["voice_speed"] == "")
            {
                props["voice_speed"] = "medium";
            }

            #region Buttons on the plugin menu
            // SpeechButton = new ToolStripMenuItem("Speech", null, OnSpeechMenuButtonClicked);
            SpeechButton = new ToolStripMenuItem("Speech");
            instance.MainForm.PluginsMenu.DropDownItems.Add(SpeechButton);

            SpeechButton.Checked = config["enabled"].AsBoolean();

            // Enabled sub menu
            {
                ToolStripMenuItem button = new ToolStripMenuItem("Enabled", null, (sender, e) =>
                {
                    OnSpeechMenuButtonClicked(SpeechButton, EventArgs.Empty);
                    ((ToolStripMenuItem)sender).Checked = SpeechButton.Checked;
                });

                button.Checked = SpeechButton.Checked;

                SpeechButton.DropDownItems.Add(button);
            }

            SpeechButton.DropDownItems.Add(new ToolStripSeparator());

            // Voice rate
            {
                ToolStripMenuItem slowButton = new ToolStripMenuItem("Slow");
                slowButton.Name = "slow";
                if (props["voice_speed"] == "slow") slowButton.Checked = true;

                ToolStripMenuItem mediumButton = new ToolStripMenuItem("Medium");
                if (props["voice_speed"] == "medium") mediumButton.Checked = true;
                mediumButton.Name = "medium";

                ToolStripMenuItem fastButton = new ToolStripMenuItem("Fast");
                if (props["voice_speed"] == "fast") fastButton.Checked = true;
                fastButton.Name = "fast";

                slowButton.Click += (sender, e) =>
                {
                    slowButton.Checked = !slowButton.Checked;
                    if (slowButton.Checked)
                    {
                        props["voice_speed"] = "slow";
                        mediumButton.Checked = false;
                        fastButton.Checked = false;
                    }
                };

                mediumButton.Click += (sender, e) =>
                {
                    mediumButton.Checked = !mediumButton.Checked;
                    if (mediumButton.Checked)
                    {
                        props["voice_speed"] = "medium";
                        slowButton.Checked = false;
                        fastButton.Checked = false;
                    }
                };

                fastButton.Click += (sender, e) =>
                {
                    fastButton.Checked = !fastButton.Checked;
                    if (fastButton.Checked)
                    {
                        props["voice_speed"] = "fast";
                        slowButton.Checked = false;
                        mediumButton.Checked = false;
                    }
                };

                SpeechButton.DropDownItems.Add(slowButton);
                SpeechButton.DropDownItems.Add(mediumButton);
                SpeechButton.DropDownItems.Add(fastButton);
            }

            SpeechButton.DropDownItems.Add(new ToolStripSeparator());

            // Enable / disable for inventory tab
            {
                ToolStripMenuItem button = new ToolStripMenuItem("Inventory", null, (sender, e) =>
                {
                    var me = (ToolStripMenuItem)sender;
                    me.Checked = !me.Checked;
                    config["enabled_for_inventory"] = me.Checked;
                });
                button.Name = "speech_for_inventory";
                button.AccessibleName = "Speech for inventory";
                button.Checked = config["enabled_for_inventory"].AsBoolean();

                SpeechButton.DropDownItems.Add(button);
            }

            // Enable / disable for objects tab
            {
                ToolStripMenuItem button = new ToolStripMenuItem("Objects", null, (sender, e) =>
                {
                    var me = (ToolStripMenuItem)sender;
                    me.Checked = !me.Checked;
                    config["enabled_for_objects"] = me.Checked;
                });
                button.Name = "speech_for_objects";
                button.AccessibleName = "Speech for objects";
                button.Checked = config["enabled_for_objects"].AsBoolean();

                SpeechButton.DropDownItems.Add(button);
            }

            // Enable / disable for friends tab
            {
                ToolStripMenuItem button = new ToolStripMenuItem("Friends", null, (sender, e) =>
                {
                    var me = (ToolStripMenuItem)sender;
                    me.Checked = !me.Checked;
                    config["enabled_for_friends"] = me.Checked;
                });
                button.Name = "speech_for_friends";
                button.AccessibleName = "Speech for friends";
                button.Checked = config["enabled_for_friends"].AsBoolean();

                SpeechButton.DropDownItems.Add(button);
            }

            SpeechButton.DropDownItems.Add(new ToolStripSeparator());

            // 3D Sound sub menu
            {
                ToolStripMenuItem button = new ToolStripMenuItem("3D Sound", null, (sender, e) =>
                {
                    var me = (ToolStripMenuItem)sender;
                    me.Checked = !me.Checked;
                    config["3d_sound"] = me.Checked;
                    instance.GlobalSettings.Save();
                    Radegast.Media.Speech.Surround = me.Checked;
                });

                button.Checked = config["3d_sound"].AsBoolean();
                Radegast.Media.Speech.Surround = button.Checked;

                SpeechButton.DropDownItems.Add(button);
            }
            #endregion Buttons on the plugin menu

            if (SpeechButton.Checked)
            {
                Initialize();
            }

            instance.GlobalSettings.Save();

            instance.MainForm.KeyDown += MainForm_KeyDown;
        }

        /// <summary>
        /// Plugin shut-down entry
        /// </summary>
        /// <param name="inst"></param>
        /// <remarks>Called by Radegast at shut-down, or when Speech is switched off.
        /// We use this to release system resources.</remarks>
        public void StopPlugin(RadegastInstanceForms inst)
        {
            instance.MainForm.KeyDown -= MainForm_KeyDown;
            SpeechButton.Dispose();
            Shutdown();
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl-Shift-R toggle speech rate
            if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.R)
            {
                e.Handled = e.SuppressKeyPress = true;
                OSDMap props = (OSDMap)config["properties"];
                if (props["voice_speed"] == "slow")
                {
                    props["voice_speed"] = "medium";
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("slow", false)[0]).Checked = false;
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("medium", false)[0]).Checked = true;
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("fast", false)[0]).Checked = false;
                }
                else if (props["voice_speed"] == "medium")
                {
                    props["voice_speed"] = "fast";
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("slow", false)[0]).Checked = false;
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("medium", false)[0]).Checked = false;
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("fast", false)[0]).Checked = true;
                }
                else
                {
                    props["voice_speed"] = "slow";
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("slow", false)[0]).Checked = true;
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("medium", false)[0]).Checked = false;
                    ((ToolStripMenuItem)SpeechButton.DropDownItems.Find("fast", false)[0]).Checked = false;
                }

                instance.ShowNotificationInChat("Voice rate set to " + props["voice_speed"]);
            }
        }

        /// <summary>
        /// Startup code (executed only when needed)
        /// </summary>
        private void Initialize()
        {
            // Never initialize twice
            if (started) return;

            // Do the one-time only initializations.
            try
            {
                LoadOSLayer();
                env = new Environment.Control(this);
                talker = new Talk.Control(this);
                listener = new Listen.Control(this);
                converse = new Conversation.Control(this);
                sound = new Sound.FmodSound(this);
                StartControls();
                if (!instance.NetCom.IsLoggedIn)
                {
                    talker.SayMore("Press enter to connect.");
                }
                else
                {
                    // Create conversations and pick active one if we are activating
                    // the speech plugin mid-session
                    foreach (RadegastTab tab in instance.TabConsole.Tabs.Values)
                    {
                        converse.CreateConversationFromTab(tab, false);
                    }
                    converse.ActivateConversationFromTab(instance.TabConsole.SelectedTab);

                }
                started = true;
            }
            catch (Exception e)
            {
                SpeechButton.Checked = false;
                config["enabled"] = OSD.FromBoolean(false);
                SaveSpeechSettings();
                MessageBox.Show("Speech failed initialization: " + e.Message);
            }
        }

        /// <summary>
        /// Shutdown speech module
        /// </summary>
        private void Shutdown()
        {
            if (!started) return;
            try
            {
                converse.Shutdown();
                listener.Shutdown();
                talker.Shutdown();
                env.Shutdown();
                sound.Shutdown();
            }
            catch (Exception e)
            {
                Logger.Log("Failed to shutdown speech modules: ", Helpers.LogLevel.Warning, e);
            }
            finally
            {
                started = false;
            }
        }

        /// <summary>
        /// Start all speech subsystems 
        /// </summary>
        private void StartControls()
        {
            // Start up each of the area controllers.  The order
            // here is important.
            try
            {
                sound.Start();      // Sound output
                talker.Start();     // Synthesis
                listener.Start();   // Recognition
                converse.Start();   // Topic-specific conversations
                env.Start();        // Environmental awareness
            }
            catch (Exception e)
            {
                MessageBox.Show("Speech can not start.  See log.");
                Logger.Log("Speech can not start.", Helpers.LogLevel.Error, e);

                Console.WriteLine(e.StackTrace);
                MarkDisabled();
                return;
            }

            // Register the speech-related actions for context menus.
            // Editing voice assignments to avatars.
            instance.ContextActionManager.RegisterContextAction(
                new GUI.AvatarSpeechAction(instance, this));
            // Reading the contents of notecards.
            instance.ContextActionManager.RegisterContextAction(
                new GUI.NotecardReadAction(instance, this));

            talker.Say("Rahdegast is ready.");
        }

        private void MarkDisabled()
        {
            SpeechButton.Checked = false;
            osLayer = null;
        }

        /// <summary>
        /// Handle toggling of our enable flag
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnSpeechMenuButtonClicked(object sender, EventArgs e)
        {
            SpeechButton.Checked = !SpeechButton.Checked;

            if (SpeechButton.Checked)
            {
                Initialize();
            }
            else
            {
                Shutdown();
            }

            // Save this into the INI file.
            config["enabled"] = OSD.FromBoolean(SpeechButton.Checked);
            SaveSpeechSettings();
        }

        public void SaveSpeechSettings()
        {
            instance.GlobalSettings.Save();
        }

        /// <summary>
        /// Find the system-specific DLL for this platform
        /// </summary>
        private void LoadOSLayer()
        {
            string dirName = Application.StartupPath;

            if (!Directory.Exists(dirName))
                throw new Exception("No startup directory found " + dirName);

            // The filename depends on the platform.
            Version version = System.Environment.OSVersion.Version;
            string loadfilename = null;
            switch (System.Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32Windows:
                    // XP and later have Speech Synthesis.
                    // XP=5, Vista=6, W7=7
                    if (version.Major >= 5)
                        loadfilename = "RadSpeechWin";
                    break;
                case PlatformID.Unix:
                    loadfilename = "RadSpeechLin";
                    break;
                case PlatformID.MacOSX:
                    loadfilename = "RadSpeechMac";
                    break;
            }

            // If the name was not set, we do not support this platform
            if (loadfilename == null)
                throw new Exception("Platform not supported for Speech");

            loadfilename = Path.Combine(dirName, loadfilename + ".dll");
            try
            {
                Assembly assembly = Assembly.LoadFile(loadfilename);

                // Examine the types exposed by this DLL, looking for ours.
                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(IRadSpeech).IsAssignableFrom(type))
                    {
                        foreach (var ci in type.GetConstructors())
                        {
                            if (ci.GetParameters().Length > 0) continue;
                            try
                            {
                                // This is the one.  Instantiate it.
                                osLayer = (IRadSpeech)ci.Invoke(Array.Empty<object>());
                                return;
                            }
                            catch (Exception ex)
                            {
                                throw new Exception("ERROR in Speech OS Layer: " + ex.Message);
                            }
                        }
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // non .NET .dlls
                throw new Exception("Speech OS Layer bad format " + loadfilename);
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Out of date or dlls missing sub dependencies
                throw new Exception("ERROR loading Speech OS Layer " + loadfilename + ", " + ex.Message);
            }
            catch (Exception ex)
            {
                throw new Exception("Exception loading Speech OS Layer " + loadfilename + ", " + ex.Message);
            }
        }

    }
}
