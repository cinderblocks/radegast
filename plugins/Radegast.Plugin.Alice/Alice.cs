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
using System.Collections;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using AIMLbot;

namespace Radegast.Plugin.Alice
{
    [Plugin(Name = "ALICE Chatbot", Description = "A.L.I.C.E. based AI chat bot", Version = "1.1")]
    public class AliceAI : IRadegastPlugin
    {
        private RadegastInstanceForms Instance;
        private GridClient Client => Instance.Client;

        private bool Enabled = false;
        private Avatar.AvatarProperties MyProfile;
        private Bot Alice;
        private readonly Hashtable AliceUsers = new Hashtable();
        private ToolStripMenuItem MenuButton, EnabledButton;
        private TalkToAvatar talkToAvatar;
        private bool respondWithoutName = false;
        private int respondRange = -1;
        private bool shout2shout = false;
        private bool whisper2whisper = false;
        private ToolStripMenuItem respondWithoutNameButton, distance_5m, distance_10m, distance_15m, distance_20m, btn_shout2shout, btn_whisper2whisper, btn_enableDelay;
        private bool DisableOnStart = false;
        private ToolStripMenuItem btn_DisableOnStart;
        private bool EnableRandomDelay = false;
        private bool AimlLoaded = false;

        public void StartPlugin(RadegastInstanceForms inst)
        {
            Instance = inst;
            Instance.ClientChanged += Instance_ClientChanged;

            if (!Instance.GlobalSettings.Keys.Contains("plugin.alice.enabled"))
            {
                Instance.GlobalSettings["plugin.alice.enabled"] = OSD.FromBoolean(Enabled);
            }
            else
            {
                Enabled = Instance.GlobalSettings["plugin.alice.enabled"].AsBoolean();
            }

            if (!Instance.GlobalSettings.Keys.Contains("plugin.alice.disableOnStart"))
            {
                Instance.GlobalSettings["plugin.alice.disableOnStart"] = OSD.FromBoolean(DisableOnStart);
            }
            else
            {
                DisableOnStart = Instance.GlobalSettings["plugin.alice.disableOnStart"].AsBoolean();
            }
            if (DisableOnStart)
            {
                Enabled = false;
            }
            btn_DisableOnStart = new ToolStripMenuItem("Disable on start", null, (sender, e) =>
            {
                DisableOnStart = btn_DisableOnStart.Checked = !DisableOnStart;
                Instance.GlobalSettings["plugin.alice.disableOnStart"] = OSD.FromBoolean(DisableOnStart);
            });
            btn_DisableOnStart.Checked = DisableOnStart;

            EnabledButton = new ToolStripMenuItem("Enabled", null, (sender, e) =>
            {
                Enabled = SetEnabled(!Enabled);
                EnabledButton.Checked = MenuButton.Checked = Enabled;
                Instance.GlobalSettings["plugin.alice.enabled"] = OSD.FromBoolean(Enabled);
            });

            if (!Instance.GlobalSettings.Keys.Contains("plugin.alice.respondWithoutName"))
            {
                Instance.GlobalSettings["plugin.alice.respondWithoutName"] = OSD.FromBoolean(respondWithoutName);
            }
            else
            {
                respondWithoutName = Instance.GlobalSettings["plugin.alice.respondWithoutName"].AsBoolean();
            }

            respondWithoutNameButton = new ToolStripMenuItem("Respond without name", null, (sender, e) =>
            {
                respondWithoutName = respondWithoutNameButton.Checked = !respondWithoutName;
                Instance.GlobalSettings["plugin.alice.respondWithoutName"] = OSD.FromBoolean(respondWithoutName);
            });

            if (!Instance.GlobalSettings.Keys.Contains("plugin.alice.respondRange"))
            {
                Instance.GlobalSettings["plugin.alice.respondRange"] = respondRange;
            }
            else
            {
                respondRange = Instance.GlobalSettings["plugin.alice.respondRange"];
            }

            distance_5m = new ToolStripMenuItem("5m range", null, (sender, e) =>
            {
                distance_5m.Checked = !distance_5m.Checked;
                if (distance_5m.Checked)
                {
                    respondRange = 5;
                    distance_10m.Checked = false;
                    distance_15m.Checked = false;
                    distance_20m.Checked = false;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
                else if (!distance_10m.Checked && !distance_15m.Checked && !distance_20m.Checked)
                {
                    respondRange = -1;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
            });

            distance_10m = new ToolStripMenuItem("10m range", null, (sender, e) =>
            {
                distance_10m.Checked = !distance_10m.Checked;
                if (distance_10m.Checked)
                {
                    respondRange = 10;
                    distance_5m.Checked = false;
                    distance_15m.Checked = false;
                    distance_20m.Checked = false;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
                else if (!distance_5m.Checked && !distance_15m.Checked && !distance_20m.Checked)
                {
                    respondRange = -1;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
            });

            distance_15m = new ToolStripMenuItem("15m range", null, (sender, e) =>
            {
                distance_15m.Checked = !distance_15m.Checked;
                if (distance_15m.Checked)
                {
                    respondRange = 15;
                    distance_5m.Checked = false;
                    distance_10m.Checked = false;
                    distance_20m.Checked = false;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
                else if (!distance_5m.Checked && !distance_10m.Checked && !distance_20m.Checked)
                {
                    respondRange = -1;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
            });

            distance_20m = new ToolStripMenuItem("20m range", null, (sender, e) =>
            {
                distance_20m.Checked = !distance_20m.Checked;
                if (distance_20m.Checked)
                {
                    respondRange = 20;
                    distance_5m.Checked = false;
                    distance_10m.Checked = false;
                    distance_15m.Checked = false;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
                else if (!distance_5m.Checked && !distance_10m.Checked && !distance_15m.Checked)
                {
                    respondRange = -1;
                    Instance.GlobalSettings["plugin.alice.respondRange"] = OSD.FromReal(respondRange);
                }
            });

            if (!Instance.GlobalSettings.ContainsKey("plugin.alice.shout2shout"))
            {
                Instance.GlobalSettings["plugin.alice.shout2shout"] = OSD.FromBoolean(shout2shout);
            }
            else
            {
                shout2shout = Instance.GlobalSettings["plugin.alice.shout2shout"].AsBoolean();
            }

            btn_shout2shout = new ToolStripMenuItem("Shout response to Shout", null, (sender, e) =>
            {
                shout2shout = btn_shout2shout.Checked = !shout2shout;
                Instance.GlobalSettings["plugin.alice.shout2shout"] = OSD.FromBoolean(shout2shout);
            });

            if (!Instance.GlobalSettings.ContainsKey("plugin.alice.whisper2whisper"))
            {
                Instance.GlobalSettings["plugin.alice.whisper2whisper"] = OSD.FromBoolean(whisper2whisper);
            }
            else
            {
                whisper2whisper = Instance.GlobalSettings["plugin.alice.whisper2whisper"].AsBoolean();
            }

            btn_whisper2whisper = new ToolStripMenuItem("Whisper response to Whisper", null, (sender, e) =>
            {
                whisper2whisper = btn_whisper2whisper.Checked = !whisper2whisper;
                Instance.GlobalSettings["plugin.alice.whisper2whisper"] = OSD.FromBoolean(whisper2whisper);
            });

            MenuButton = new ToolStripMenuItem("ALICE chatbot", null, (sender, e) =>
            {
                Enabled = SetEnabled(!Enabled);
                EnabledButton.Checked = MenuButton.Checked = Enabled;
                Instance.GlobalSettings["plugin.alice.enabled"] = OSD.FromBoolean(Enabled);
            });

            btn_enableDelay = new ToolStripMenuItem("Enable random delay", null, (sender, e) =>
            {
                btn_enableDelay.Checked = !btn_enableDelay.Checked;
                Instance.GlobalSettings["plugin.alice.enable_delay"] = EnableRandomDelay = btn_enableDelay.Checked;
            });
            btn_enableDelay.Checked = EnableRandomDelay = Instance.GlobalSettings["plugin.alice.enable_delay"];

            Instance.MainForm.PluginsMenu.DropDownItems.Add(MenuButton);
            Instance.MainForm.PluginsMenu.Visible = true;
            MenuButton.DropDownItems.Add(EnabledButton);
            MenuButton.Checked = EnabledButton.Checked = Enabled;

            MenuButton.DropDownItems.Add(respondWithoutNameButton);
            MenuButton.DropDownItems.Add(distance_5m);
            MenuButton.DropDownItems.Add(distance_10m);
            MenuButton.DropDownItems.Add(distance_15m);
            MenuButton.DropDownItems.Add(distance_20m);
            MenuButton.DropDownItems.Add(btn_shout2shout);
            MenuButton.DropDownItems.Add(btn_whisper2whisper);

            respondWithoutNameButton.Checked = respondWithoutName;
            if (respondRange == 5)
            {
                distance_5m.Checked = true;
            }
            else if (respondRange == 10)
            {
                distance_10m.Checked = true;
            }
            else if (respondRange == 15)
            {
                distance_15m.Checked = true;
            }
            else if (respondRange == 20)
            {
                distance_20m.Checked = true;
            }
            btn_shout2shout.Checked = shout2shout;
            btn_whisper2whisper.Checked = whisper2whisper;

            MenuButton.DropDownItems.Add(btn_enableDelay);
            MenuButton.DropDownItems.Add(btn_DisableOnStart);
            MenuButton.DropDownItems.Add("Reload AIML", null, (sender, e) =>
            {
                Alice = null;
                GC.Collect();
                LoadALICE();
            });

            SetEnabled(Enabled);

            // Events
            RegisterClientEvents(Client);
        }

        private bool SetEnabled(bool e)
        {
            if (!e || AimlLoaded) return e;
            if (!LoadALICE()) return false;
            if (Client.Network.Connected)
            {
                Alice.GlobalSettings.updateSetting("name", FirstName(Client.Self.Name));
            }
            AimlLoaded = true;
            talkToAvatar = new TalkToAvatar(Instance, Alice);
            Instance.ContextActionManager.RegisterContextAction(talkToAvatar);
            return true;
        }

        private bool LoadALICE()
        {
            try
            {
                Alice = new Bot
                {
                    isAcceptingUserInput = false
                };
                string configFile = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "aiml_config", "Settings.xml");
                Alice.loadSettings(configFile);
                var loader = new AIMLbot.Utils.AIMLLoader(Alice);
                Alice.isAcceptingUserInput = false;
                loader.loadAIML(Alice.PathToAIML);
                Alice.isAcceptingUserInput = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed loading ALICE: " + ex.Message);
                return false;
            }
        }


        public void StopPlugin(RadegastInstanceForms instance)
        {
            // Remove the menu buttons
            EnabledButton.Dispose();
            MenuButton.Dispose();

            if (talkToAvatar != null)
            {
                instance.ContextActionManager.DeregisterContextAction(talkToAvatar);
            }
            // Unregister events
            UnregisterClientEvents(Client);
        }

        private void RegisterClientEvents(GridClient client)
        {
            Client.Self.ChatFromSimulator += Self_ChatFromSimulator;
            Client.Self.IM += Self_IM;
            Client.Avatars.AvatarPropertiesReply += Avatars_AvatarPropertiesReply;
            Instance.NetCom.ClientConnected += Netcom_ClientConnected;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            Client.Self.ChatFromSimulator -= Self_ChatFromSimulator;
            Client.Self.IM -= Self_IM;
            Client.Avatars.AvatarPropertiesReply -= Avatars_AvatarPropertiesReply;
            Instance.NetCom.ClientConnected -= Netcom_ClientConnected;
        }

        private void Instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(Client);
        }

        private void Netcom_ClientConnected(object sender, EventArgs e)
        {
            if (AimlLoaded)
            {
                Alice.GlobalSettings.updateSetting("name", FirstName(Client.Self.Name));
            }
        }

        private void Avatars_AvatarPropertiesReply(object sender, AvatarPropertiesReplyEventArgs e)
        {
            if (e.AvatarID == Client.Self.AgentID && AimlLoaded)
            {
                MyProfile = e.Properties;
                Alice.GlobalSettings.updateSetting("birthday", MyProfile.BornOn);
                DateTime bd;
                if (DateTime.TryParse(MyProfile.BornOn, Utils.EnUsCulture, System.Globalization.DateTimeStyles.None, out bd))
                {
                    DateTime now = DateTime.Today;
                    int age = now.Year - bd.Year;
                    if (now.Month < bd.Month || (now.Month == bd.Month && now.Day < bd.Day))
                    {
                        --age;
                    }
                    Alice.GlobalSettings.updateSetting("age", age.ToString());
                    Alice.GlobalSettings.updateSetting("birthday", bd.ToLongDateString());

                }
            }
        }

        private readonly object syncChat = new object();
        private readonly Random rand = new Random();

        private void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            // We ignore everything except normal chat from other avatars
            if (!Enabled 
                || e.SourceType != ChatSourceType.Agent 
                || e.FromName == Client.Self.Name 
                || e.Message.Trim().Length == 0) 
            { 
                return; 
            }

            // Ignore muted agents
            if (Client.Self.MuteList.Find(me => me.Type == MuteType.Resident && me.ID == e.SourceID) != null)
            {
                return;
            }

            bool parseForResponse = Alice != null && Alice.isAcceptingUserInput && Enabled;
            if (parseForResponse && respondRange >= 0)
            {
                parseForResponse = Vector3.Distance(Client.Self.SimPosition, e.Position) <= respondRange;
            }
            if (parseForResponse)
            {
                parseForResponse = respondWithoutName || e.Message.ToLower().Contains(FirstName(Client.Self.Name).ToLower());
            }


            if (parseForResponse)
            {
                ThreadPool.QueueUserWorkItem(sync =>
                {
                    lock (syncChat)
                    {
                        Alice.GlobalSettings.updateSetting("location", "region " + Client.Network.CurrentSim.Name);
                        string msg = e.Message.ToLower();
                        msg = msg.Replace(FirstName(Client.Self.Name).ToLower(), "");
                        User user;
                        if (AliceUsers.ContainsKey(e.FromName))
                        {
                            user = (User)AliceUsers[e.FromName];
                        }
                        else
                        {
                            user = new User(e.FromName, Alice);
                            user.Predicates.removeSetting("name");
                            user.Predicates.addSetting("name", FirstName(e.FromName));
                            AliceUsers[e.FromName] = user;
                        }

                        var typingAnimationIsEnabled = !Instance.GlobalSettings["no_typing_anim"].AsBoolean();

                        Client.Self.Movement.TurnToward(e.Position);
                        if (EnableRandomDelay) Thread.Sleep(1000 + 1000 * rand.Next(2));
                        if (!Instance.State.IsTyping && typingAnimationIsEnabled)
                        {
                            Instance.State.SetTyping(true);
                        }
                        if (EnableRandomDelay)
                        {
                            Thread.Sleep(2000 + 1000 * rand.Next(5));
                        }
                        else
                        {
                            Thread.Sleep(1000);
                        }
                        if (typingAnimationIsEnabled)
                        {
                            Instance.State.SetTyping(false);
                        }
                        Request req = new Request(msg, user, Alice);
                        Result res = Alice.Chat(req);
                        string outp = res.Output;
                        if (outp.Length > 1000)
                        {
                            outp = outp.Substring(0, 1000);
                        }

                        ChatType useChatType = ChatType.Normal;
                        if (shout2shout && e.Type == ChatType.Shout)
                        {
                            useChatType = ChatType.Shout;
                        }
                        else if (whisper2whisper && e.Type == ChatType.Whisper)
                        {
                            useChatType = ChatType.Whisper;
                        }
                        Client.Self.Chat(outp, 0, useChatType);
                    }
                });
            }
        }

        private void Self_IM(object sender, InstantMessageEventArgs e)
        {
            if (!Enabled) return;
            // Every event coming from a different thread (almost all of them, most certainly those
            // from libomv) needs to be executed on the GUI thread. This code can be basically
            // copy-pasted on the beginning of each libomv event handler that results in update 
            // of any GUI element
            //
            // In this case the IM we sent back as a reply is also displayed in the corresponding IM tab
            if (Instance.MainForm.InvokeRequired)
            {
                Instance.MainForm.BeginInvoke(
                    new MethodInvoker(
                        delegate ()
                        {
                            Self_IM(sender, e);
                        }
                        ));
                return;
            }

            // We need to filter out all sorts of things that come in as an instant message
            if (!(e.IM.Dialog == InstantMessageDialog.MessageFromAgent // Message is not notice, inv. offer, etc etc
                && !Instance.Groups.ContainsKey(e.IM.IMSessionID)  // Message is not group IM (sessionID == groupID)
                && e.IM.BinaryBucket.Length < 2                    // Session is not ad-hoc friends conference
                && e.IM.FromAgentName != "Second Life"             // Not a system message
                && Alice.isAcceptingUserInput                    // Alice bot loaded successfully
                ))
            {
                return;
            }

            // Ignore muted agents
            if (Client.Self.MuteList.Find(muteEntry => muteEntry.Type == MuteType.Resident
                && muteEntry.ID == e.IM.FromAgentID) != null)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(sync =>
            {
                lock (syncChat)
                {
                    Alice.GlobalSettings.updateSetting("location", "region " + Client.Network.CurrentSim.Name);
                    User user;
                    if (AliceUsers.ContainsKey(e.IM.FromAgentName))
                    {
                        user = (User)AliceUsers[e.IM.FromAgentName];
                    }
                    else
                    {
                        user = new User(e.IM.FromAgentName, Alice);
                        user.Predicates.removeSetting("name");
                        user.Predicates.addSetting("name", FirstName(e.IM.FromAgentName));
                        AliceUsers[e.IM.FromAgentName] = user;
                    }
                    Request req = new Request(e.IM.Message, user, Alice);
                    Result res = Alice.Chat(req);
                    string msg = res.Output;
                    if (msg.Length > 1000)
                    {
                        msg = msg.Substring(0, 1000);
                    }
                    if (EnableRandomDelay) Thread.Sleep(2000 + 1000 * rand.Next(3));
                    Instance.NetCom.SendIMStartTyping(e.IM.FromAgentID, e.IM.IMSessionID);
                    if (EnableRandomDelay)
                    {
                        Thread.Sleep(2000 + 1000 * rand.Next(5));
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                    Instance.NetCom.SendIMStopTyping(e.IM.FromAgentID, e.IM.IMSessionID);
                    if (Instance.MainForm.InvokeRequired)
                    {
                        Instance.MainForm.BeginInvoke(new MethodInvoker(() => Instance.NetCom.SendInstantMessage(msg, e.IM.FromAgentID, e.IM.IMSessionID)));
                    }
                    else
                    {
                        Instance.NetCom.SendInstantMessage(msg, e.IM.FromAgentID, e.IM.IMSessionID);
                    }
                }
            });
        }

        private string FirstName(string name)
        {
            return name.Split(' ')[0];
        }
    }
}
