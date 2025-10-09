/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2023, Sjofn, LLC
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

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    public class ChatTextManager : TextManagerBase, IDisposable
    {
        public event EventHandler<ChatLineAddedArgs> ChatLineAdded;

        private bool showTimestamps;
        private List<ChatBufferItem> textBuffer;

        public ChatTextManager(RadegastInstance instance, ITextPrinter textPrinter)
            : base(instance, textPrinter)
        {
            textBuffer = new List<ChatBufferItem>();

            InitializeConfig();

            // Callbacks
            instance.Netcom.ChatReceived += netcom_ChatReceived;
            instance.Netcom.ChatSent += netcom_ChatSent;
            instance.Netcom.AlertMessageReceived += netcom_AlertMessageReceived;
        }

        public override void Dispose()
        {
            instance.Netcom.ChatReceived -= netcom_ChatReceived;
            instance.Netcom.ChatSent -= netcom_ChatSent;
            instance.Netcom.AlertMessageReceived -= netcom_AlertMessageReceived;

            base.Dispose();
        }

        private void InitializeConfig()
        {
            if (instance.GlobalSettings["chat_timestamps"].Type == OSDType.Unknown)
            {
                instance.GlobalSettings["chat_timestamps"] = OSD.FromBoolean(true);
            }

            showTimestamps = instance.GlobalSettings["chat_timestamps"].AsBoolean();
        }

        protected override void OnSettingChanged(object sender, SettingsEventArgs e)
        {
            if (e.Key == "chat_timestamps" && e.Value != null)
            {
                showTimestamps = e.Value.AsBoolean();
                ReprintAllText();
            }

            base.OnSettingChanged(sender, e);
        }

        private void netcom_ChatSent(object sender, ChatSentEventArgs e)
        {
            if (e.Channel == 0) return;

            ProcessOutgoingChat(e);
        }

        private void netcom_AlertMessageReceived(object sender, AlertMessageEventArgs e)
        {
            if (e.NotificationId == "AutopilotCanceled") { return; } // workaround the stupid autopilot alerts

            ChatBufferItem item = new ChatBufferItem(
                DateTime.Now, "Alert message", UUID.Zero, ": " + e.Message, ChatBufferTextStyle.Alert);

            ProcessBufferItem(item, true);
        }

        private void netcom_ChatReceived(object sender, ChatEventArgs e)
        {
            ProcessIncomingChat(e);
        }

        public void PrintStartupMessage()
        {
            ChatBufferItem title = new ChatBufferItem(
                DateTime.Now, "",
                UUID.Zero,
                Properties.Resources.RadegastTitle + " " + Assembly.GetExecutingAssembly().GetName().Version,
                ChatBufferTextStyle.StartupTitle);

            ChatBufferItem ready = new ChatBufferItem(
                DateTime.Now, "", UUID.Zero, "Ready.", ChatBufferTextStyle.StatusBlue);

            ProcessBufferItem(title, true);
            ProcessBufferItem(ready, true);
        }

        private Object SyncChat = new Object();

        public void ProcessBufferItem(ChatBufferItem item, bool isNewMessage)
        {
            ChatLineAdded?.Invoke(this, new ChatLineAddedArgs(item));

            lock (SyncChat)
            {
                instance.LogClientMessage("chat.txt", item.From + item.Text);
                if (isNewMessage) textBuffer.Add(item);

                if (showTimestamps)
                {
                    if (FontSettings.ContainsKey("Timestamp"))
                    {
                        var fontSetting = FontSettings["Timestamp"];
                        TextPrinter.ForeColor = fontSetting.ForeColor;
                        TextPrinter.BackColor = fontSetting.BackColor;
                        TextPrinter.Font = fontSetting.Font;
                        TextPrinter.PrintText(item.Timestamp.ToString("[HH:mm] "));
                    }
                    else
                    {
                        TextPrinter.ForeColor = SystemColors.GrayText;
                        TextPrinter.BackColor = Color.Transparent;
                        TextPrinter.Font = Settings.FontSetting.DefaultFont;
                        TextPrinter.PrintText(item.Timestamp.ToString("[HH:mm] "));
                    }
                }

                if (FontSettings.ContainsKey("Name"))
                {
                    var fontSetting = FontSettings["Name"];
                    TextPrinter.ForeColor = fontSetting.ForeColor;
                    TextPrinter.BackColor = fontSetting.BackColor;
                    TextPrinter.Font = fontSetting.Font;
                }
                else
                {
                    TextPrinter.ForeColor = SystemColors.WindowText;
                    TextPrinter.BackColor = Color.Transparent;
                    TextPrinter.Font = Settings.FontSetting.DefaultFont;
                }

                if (item.Style == ChatBufferTextStyle.Normal && item.ID != UUID.Zero && instance.GlobalSettings["av_name_link"])
                {
                    TextPrinter.InsertLink(item.From, $"secondlife:///app/agent/{item.ID}/about");
                }
                else
                {
                    TextPrinter.PrintText(item.From);
                }

                if (FontSettings.ContainsKey(item.Style.ToString()))
                {
                    var fontSetting = FontSettings[item.Style.ToString()];
                    TextPrinter.ForeColor = fontSetting.ForeColor;
                    TextPrinter.BackColor = fontSetting.BackColor;
                    TextPrinter.Font = fontSetting.Font;
                }
                else
                {
                    TextPrinter.ForeColor = SystemColors.WindowText;
                    TextPrinter.BackColor = Color.Transparent;
                    TextPrinter.Font = Settings.FontSetting.DefaultFont;
                }

                ProcessAndPrintText(item.Text, isNewMessage, true);
            }
        }

        //Used only for non-public chat
        private void ProcessOutgoingChat(ChatSentEventArgs e)
        {
            StringBuilder sb = new StringBuilder();

            switch (e.Type)
            {
                case ChatType.Normal:
                    sb.Append(": ");
                    break;

                case ChatType.Whisper:
                    sb.Append(" whisper: ");
                    break;

                case ChatType.Shout:
                    sb.Append(" shout: ");
                    break;
            }

            sb.Append(e.Message);

            ChatBufferItem item = new ChatBufferItem(
                DateTime.Now, $"(channel {e.Channel}) {instance.Client.Self.Name}", instance.Client.Self.AgentID, sb.ToString(), ChatBufferTextStyle.StatusDarkBlue);

            ProcessBufferItem(item, true);

            sb = null;
        }

        private void ProcessIncomingChat(ChatEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Message)) { return; }

            // Check if the sender agent is muted
            if (e.SourceType == ChatSourceType.Agent
                && instance.Client.Self.MuteList.Find(me => me.Type == MuteType.Resident
                                                   && me.ID == e.SourceID) != null)
            {
                return;
            }

            // Check if it's script debug
            if (e.Type == ChatType.Debug && !instance.GlobalSettings["show_script_errors"])
            {
                return;
            }

            // Check if sender object is muted
            if (e.SourceType == ChatSourceType.Object &&
                null != instance.Client.Self.MuteList.Find(me =>
                        (me.Type == MuteType.Resident && me.ID == e.OwnerID) // Owner muted
                        || (me.Type == MuteType.Object && me.ID == e.SourceID) // Object muted by ID
                        || (me.Type == MuteType.ByName && me.Name == e.FromName) // Object muted by name
                ))
            {
                return;
            }

            if (instance.RLV.Enabled && e.Type == ChatType.OwnerSay && e.Message.StartsWith("@"))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                        {
                            await instance.RLV.ProcessCMD(e, cts.Token);
                        }
                    }
                    catch (TaskCanceledException ex)
                    {
                        Logger.LogInstance.Error($"Timed out while processing RLV command '{e.Message}' from object '{e.FromName}' [{e.SourceID}]", ex);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogInstance.Error($"Timed out while processing RLV command '{e.Message}' from object '{e.FromName}' [{e.SourceID}]", ex);
                    }
                });

                if (!instance.RLV.EnabledDebugCommands)
                {
                    return;
                }
            }

            ChatBufferItem item = new ChatBufferItem { ID = e.SourceID, RawMessage = e };
            StringBuilder sb = new StringBuilder();

            item.From = e.SourceType == ChatSourceType.Agent
                ? instance.Names.Get(e.SourceID, e.FromName)
                : e.FromName;

            bool isEmote = e.Message.StartsWith("/me ", StringComparison.OrdinalIgnoreCase);

            if (!isEmote)
            {
                switch (e.Type)
                {

                    case ChatType.Whisper:
                        sb.Append(" whispers");
                        break;

                    case ChatType.Shout:
                        sb.Append(" shouts");
                        break;
                }
            }

            if (isEmote)
            {
                if (instance.RLV.Enabled && !instance.RLV.Permissions.CanReceiveChat(e.Message, e.SourceID.Guid))
                {
                    sb.Append(" ...");
                }
                else
                {
                    sb.Append(e.Message.Substring(3));
                }
            }
            else
            {
                if (instance.RLV.Enabled && !instance.RLV.Permissions.CanReceiveChat(e.Message, e.SourceID.Guid))
                {
                    sb.Append(": ...");
                }
                else
                {
                    sb.Append(": " + e.Message);
                }
            }

            item.Timestamp = DateTime.Now;
            item.Text = sb.ToString();

            switch (e.SourceType)
            {
                case ChatSourceType.Agent:
                    if (e.FromName.EndsWith("Linden"))
                    {
                        item.Style = ChatBufferTextStyle.LindenChat;
                    }
                    else if (isEmote)
                    {
                        item.Style = ChatBufferTextStyle.Emote;
                    }
                    else if (e.SourceID == instance.Client.Self.AgentID)
                    {
                        item.Style = ChatBufferTextStyle.Self;
                    }
                    else
                    {
                        item.Style = ChatBufferTextStyle.Normal;
                    }
                    break;
                case ChatSourceType.Object:
                    switch (e.Type)
                    {
                        case ChatType.OwnerSay when isEmote:
                            item.Style = ChatBufferTextStyle.Emote;
                            break;
                        case ChatType.OwnerSay:
                            item.Style = ChatBufferTextStyle.OwnerSay;
                            break;
                        case ChatType.Debug:
                            item.Style = ChatBufferTextStyle.Error;
                            break;
                        default:
                            item.Style = ChatBufferTextStyle.ObjectChat;
                            break;
                    }
                    break;
            }

            ProcessBufferItem(item, true);
            instance.TabConsole.Tabs["chat"].Highlight();

            sb = null;
        }

        public override void ReprintAllText()
        {
            TextPrinter.ClearText();

            foreach (ChatBufferItem item in textBuffer)
            {
                ProcessBufferItem(item, false);
            }
        }
    }

    public class ChatLineAddedArgs : EventArgs
    {
        public ChatBufferItem Item { get; }

        public ChatLineAddedArgs(ChatBufferItem item)
        {
            Item = item;
        }
    }
}
