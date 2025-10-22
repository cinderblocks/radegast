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
using System.Drawing;
using System.Text;
using System.IO;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Radegast
{
    public class IMTextManager : TextManagerBase
    {
        public bool DingOnAllIncoming = false;

        IMTextManagerType Type;
        string sessionName;
        bool AutoResponseSent = false;
        ArrayList textBuffer;

        private bool showTimestamps;

        public IMTextManager(RadegastInstance instance, ITextPrinter textPrinter, IMTextManagerType type, UUID sessionID, string sessionName)
            : base(instance, textPrinter)
        {
            SessionID = sessionID;
            this.sessionName = sessionName;
            textBuffer = new ArrayList();
            Type = type;

            PrintLastLog();
            AddNetcomEvents();
            InitializeConfig();
        }

        private void InitializeConfig()
        {
            Settings s = instance.GlobalSettings;

            if (s["im_timestamps"].Type == OSDType.Unknown)
            {
                s["im_timestamps"] = OSD.FromBoolean(true);
            }

            showTimestamps = s["im_timestamps"].AsBoolean();
        }

        protected override void OnSettingChanged(object sender, SettingsEventArgs e)
        {
            if (e.Key == "im_timestamps" && e.Value != null)
            {
                showTimestamps = e.Value.AsBoolean();
                ReprintAllText();
            }

            base.OnSettingChanged(sender, e);
        }

        private void AddNetcomEvents()
        {
            instance.Netcom.InstantMessageReceived += netcom_InstantMessageReceived;
            instance.Netcom.InstantMessageSent += netcom_InstantMessageSent;
        }

        private void RemoveNetcomEvents()
        {
            instance.Netcom.InstantMessageReceived -= netcom_InstantMessageReceived;
            instance.Netcom.InstantMessageSent -= netcom_InstantMessageSent;
        }

        private void netcom_InstantMessageSent(object sender, InstantMessageSentEventArgs e)
        {
            if (e.SessionID != SessionID) return;

            textBuffer.Add(e);
            ProcessIM(e, true);
        }

        private void netcom_InstantMessageReceived(object sender, InstantMessageEventArgs e)
        {
            if (e.IM.IMSessionID != SessionID) return;
            if (e.IM.Dialog == InstantMessageDialog.StartTyping ||
                e.IM.Dialog == InstantMessageDialog.StopTyping)
                return;

            if (instance.Client.Self.MuteList.Find(me => me.Type == MuteType.Resident && me.ID == e.IM.FromAgentID) != null)
            {
                return;
            }

            textBuffer.Add(e);
            ProcessIM(e, true);
        }

        public void ProcessIM(object e, bool isNewMessage)
        {
            if (e is InstantMessageEventArgs)
                ProcessIncomingIM((InstantMessageEventArgs)e, isNewMessage);
            else if (e is InstantMessageSentEventArgs)
                ProcessOutgoingIM((InstantMessageSentEventArgs)e, isNewMessage);
        }

        private void ProcessOutgoingIM(InstantMessageSentEventArgs e, bool isNewMessage)
        {
            PrintIM(e.Timestamp, instance.Netcom.LoginOptions.FullName, instance.Client.Self.AgentID, e.Message, isNewMessage);
        }

        private void ProcessIncomingIM(InstantMessageEventArgs e, bool isNewMessage)
        {
            string msg = e.IM.Message;

            if (instance.RLV.Enabled && !instance.RLV.Permissions.CanReceiveIM(msg, e.IM.FromAgentID.Guid))
            {
                msg = "*** IM blocked by your viewer";

                if (Type == IMTextManagerType.Agent)
                {
                    instance.Client.Self.InstantMessage(instance.Client.Self.Name,
                            e.IM.FromAgentID,
                            "***  The Resident you messaged is prevented from reading your instant messages at the moment, please try again later.",
                            e.IM.IMSessionID,
                            InstantMessageDialog.BusyAutoResponse,
                            InstantMessageOnline.Offline,
                            instance.Client.Self.RelativePosition,
                            instance.Client.Network.CurrentSim.ID,
                            null);
                }
            }

            if (DingOnAllIncoming)
            {
                instance.MediaManager.PlayUISound(UISounds.IM);
            }
            PrintIM(DateTime.Now, instance.Names.Get(e.IM.FromAgentID, e.IM.FromAgentName), e.IM.FromAgentID, msg, isNewMessage);

            if (!AutoResponseSent && Type == IMTextManagerType.Agent && e.IM.FromAgentID != UUID.Zero && e.IM.FromAgentName != "Second Life")
            {
                bool autoRespond = false;
                AutoResponseType art = (AutoResponseType)instance.GlobalSettings["auto_response_type"].AsInteger();

                switch (art)
                {
                    case AutoResponseType.WhenBusy: autoRespond = instance.State.IsBusy; break;
                    case AutoResponseType.WhenFromNonFriend: autoRespond = !instance.Client.Friends.FriendList.ContainsKey(e.IM.FromAgentID); break;
                    case AutoResponseType.Always: autoRespond = true; break;
                }

                if (autoRespond)
                {
                    AutoResponseSent = true;
                    instance.Client.Self.InstantMessage(instance.Client.Self.Name,
                        e.IM.FromAgentID,
                        instance.GlobalSettings["auto_response_text"].AsString(),
                        e.IM.IMSessionID,
                        InstantMessageDialog.BusyAutoResponse,
                        InstantMessageOnline.Online,
                        instance.Client.Self.RelativePosition,
                        instance.Client.Network.CurrentSim.ID,
                        null);

                    PrintIM(DateTime.Now, instance.Client.Self.Name, instance.Client.Self.AgentID, instance.GlobalSettings["auto_response_text"].AsString(), isNewMessage);
                }
            }
        }

        public void DisplayNotification(string message)
        {
            if (instance.MainForm.InvokeRequired)
            {
                instance.MainForm.Invoke(new System.Windows.Forms.MethodInvoker(() => DisplayNotification(message)));
                return;
            }

            if (showTimestamps)
            {
                if(FontSettings.ContainsKey("Timestamp"))
                {
                    var fontSetting = FontSettings["Timestamp"];
                    TextPrinter.ForeColor = fontSetting.ForeColor;
                    TextPrinter.BackColor = fontSetting.BackColor;
                    TextPrinter.Font = fontSetting.Font;
                    TextPrinter.PrintText(DateTime.Now.ToString("[HH:mm] "));
                }
                else
                {
                    TextPrinter.ForeColor = SystemColors.GrayText.ToSKColor();
                    TextPrinter.BackColor = SKColors.Transparent;
                    TextPrinter.Font = Settings.FontSetting.DefaultFont;
                    TextPrinter.PrintText(DateTime.Now.ToString("[HH:mm] "));
                }
            }

            if(FontSettings.ContainsKey("Notification"))
            {
                var fontSetting = FontSettings["Notification"];
                TextPrinter.ForeColor = fontSetting.ForeColor;
                TextPrinter.BackColor = fontSetting.BackColor;
                TextPrinter.Font = fontSetting.Font;
            }
            else
            {
                TextPrinter.ForeColor = SKColors.DarkCyan;
                TextPrinter.BackColor = SKColors.Transparent;
                TextPrinter.Font = Settings.FontSetting.DefaultFont;
            }

            instance.LogClientMessage(sessionName + ".txt", message);
            TextPrinter.PrintTextLine(message);
        }

        private void PrintIM(DateTime timestamp, string fromName, UUID fromID, string message, bool isNewMessage)
        {
            if (showTimestamps)
            {
                if(FontSettings.ContainsKey("Timestamp"))
                {
                    var fontSetting = FontSettings["Timestamp"];
                    TextPrinter.ForeColor = fontSetting.ForeColor;
                    TextPrinter.BackColor = fontSetting.BackColor;
                    TextPrinter.Font = fontSetting.Font;
                    TextPrinter.PrintText(DateTime.Now.ToString("[HH:mm] "));
                }
                else
                {
                    TextPrinter.ForeColor = SystemColors.GrayText.ToSKColor();
                    TextPrinter.BackColor = SKColors.Transparent;
                    TextPrinter.Font = Settings.FontSetting.DefaultFont;
                    TextPrinter.PrintText(DateTime.Now.ToString("[HH:mm] "));
                }
            }

            if(FontSettings.ContainsKey("Name"))
            {
                var fontSetting = FontSettings["Name"];
                TextPrinter.ForeColor = fontSetting.ForeColor;
                TextPrinter.BackColor = fontSetting.BackColor;
                TextPrinter.Font = fontSetting.Font;
            }
            else
            {
                TextPrinter.ForeColor = SystemColors.WindowText.ToSKColor();
                TextPrinter.BackColor = SKColors.Transparent;
                TextPrinter.Font = Settings.FontSetting.DefaultFont;
            }

            if (instance.GlobalSettings["av_name_link"])
            {
                TextPrinter.InsertLink(fromName, $"secondlife:///app/agent/{fromID}/about");
            }
            else
            {
                TextPrinter.PrintText(fromName);
            }

            StringBuilder sb = new StringBuilder();

            if (message.StartsWith("/me "))
            {
                if(FontSettings.ContainsKey("Emote"))
                {
                    var fontSetting = FontSettings["Emote"];
                    TextPrinter.ForeColor = fontSetting.ForeColor;
                    TextPrinter.BackColor = fontSetting.BackColor;
                    TextPrinter.Font = fontSetting.Font;
                }
                else
                {
                    TextPrinter.ForeColor = SystemColors.WindowText.ToSKColor();
                    TextPrinter.BackColor = SKColors.Transparent;
                    TextPrinter.Font = Settings.FontSetting.DefaultFont;
                }

                sb.Append(message.Substring(3));
            }
            else
            {
                if(fromID == instance.Client.Self.AgentID)
                {
                    if(FontSettings.ContainsKey("OutgoingIM"))
                    {
                        var fontSetting = FontSettings["OutgoingIM"];
                        TextPrinter.ForeColor = fontSetting.ForeColor;
                        TextPrinter.BackColor = fontSetting.BackColor;
                        TextPrinter.Font = fontSetting.Font;
                    }
                    else
                    {
                        TextPrinter.ForeColor = SystemColors.WindowText.ToSKColor();
                        TextPrinter.BackColor = SKColors.Transparent;
                        TextPrinter.Font = Settings.FontSetting.DefaultFont;
                    }
                }
                else
                {
                    if(FontSettings.ContainsKey("IncomingIM"))
                    {
                        var fontSetting = FontSettings["IncomingIM"];
                        TextPrinter.ForeColor = fontSetting.ForeColor;
                        TextPrinter.BackColor = fontSetting.BackColor;
                        TextPrinter.Font = fontSetting.Font;
                    }
                    else
                    {
                        TextPrinter.ForeColor = SystemColors.WindowText.ToSKColor();
                        TextPrinter.BackColor = SKColors.Transparent;
                        TextPrinter.Font = Settings.FontSetting.DefaultFont;
                    }
                }

                sb.Append(": ");
                sb.Append(message);
            }

            if(isNewMessage)
            {
                instance.LogClientMessage(sessionName + ".txt", fromName + sb);
            }

            ProcessAndPrintText(sb.ToString(), isNewMessage, true);
        }

        public static string ReadEndTokens(string path, Int64 numberOfTokens, Encoding encoding, string tokenSeparator)
        {

            int sizeOfChar = encoding.GetByteCount("\n");
            byte[] buffer = encoding.GetBytes(tokenSeparator);


            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                Int64 tokenCount = 0;
                Int64 endPosition = fs.Length / sizeOfChar;

                for (Int64 position = sizeOfChar; position < endPosition; position += sizeOfChar)
                {
                    fs.Seek(-position, SeekOrigin.End);
                    fs.Read(buffer, 0, buffer.Length);

                    if (encoding.GetString(buffer) == tokenSeparator)
                    {
                        tokenCount++;
                        if (tokenCount == numberOfTokens)
                        {
                            byte[] returnBuffer = new byte[fs.Length - fs.Position];
                            fs.Read(returnBuffer, 0, returnBuffer.Length);
                            return encoding.GetString(returnBuffer);
                        }
                    }
                }

                // handle case where number of tokens in file is less than numberOfTokens
                fs.Seek(0, SeekOrigin.Begin);
                buffer = new byte[fs.Length];
                fs.Read(buffer, 0, buffer.Length);
                return encoding.GetString(buffer);
            }
        }

        private void PrintLastLog()
        {
            string last = string.Empty;
            try
            {
                last = ReadEndTokens(instance.ChatFileName(sessionName + ".txt"), 20, Encoding.UTF8, Environment.NewLine);
            }
            catch { }

            if (string.IsNullOrEmpty(last))
            {
                return;
            }

            string[] lines = last.Split(Environment.NewLine.ToCharArray());
            foreach (var line in lines)
            {
                string msg = line.Trim();
                if (!string.IsNullOrEmpty(msg))
                {
                    if(FontSettings.ContainsKey("History"))
                    {
                        var fontSetting = FontSettings["History"];
                        TextPrinter.ForeColor = fontSetting.ForeColor;
                        TextPrinter.BackColor = fontSetting.BackColor;
                        TextPrinter.Font = fontSetting.Font;
                    }
                    else
                    {
                        TextPrinter.ForeColor = SystemColors.GrayText.ToSKColor();
                        TextPrinter.BackColor = SKColors.Transparent;
                        TextPrinter.Font = Settings.FontSetting.DefaultFont;
                    }

                    ProcessAndPrintText(msg, false, true);
                }
            }

            if(FontSettings.ContainsKey("History"))
            {
                var fontSetting = FontSettings["History"];
                TextPrinter.ForeColor = fontSetting.ForeColor;
                TextPrinter.BackColor = fontSetting.BackColor;
                TextPrinter.Font = fontSetting.Font;
            }
            else
            {
                TextPrinter.ForeColor = SystemColors.GrayText.ToSKColor();
                TextPrinter.BackColor = SKColors.Transparent;
                TextPrinter.Font = Settings.FontSetting.DefaultFont;
            }
            TextPrinter.PrintTextLine("====");
        }

        public override void ReprintAllText()
        {
            TextPrinter.ClearText();
            PrintLastLog();
            foreach (object obj in textBuffer)
                ProcessIM(obj, false);
        }

        public void CleanUp()
        {
            RemoveNetcomEvents();

            textBuffer.Clear();
            textBuffer = null;
        }

        public UUID SessionID { get; set; }
    }

    public enum IMTextManagerType
    {
        Agent,
        Group,
        Conference
    }
}
