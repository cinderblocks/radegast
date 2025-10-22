/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
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
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Meebey.SmartIrc4net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using SkiaSharp;

namespace Radegast.Plugin.IRC
{
    public partial class RelayConsole : RadegastTabControl
    {
        private enum RelaySourceType
        {
            Unknown,
            Group,
            Conference,
            Chat,
            IM
        }

        private class RelaySource
        {
            public RelaySource(string name, RelaySourceType sourcetype, UUID sessionId)
            {
                Name = name;
                SourceType = sourcetype;
                SessionId = sessionId;
            }

            public override string ToString()
            {
                return Name;
            }

            public static bool operator ==(RelaySource left, RelaySource right)
            {
                if (ReferenceEquals(left, right))
                    return true;

                if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
                    return false;

                return left.Name == right.Name && left.SessionId == right.SessionId && left.SourceType == right.SourceType;
            }

            public static bool operator !=(RelaySource left, RelaySource right)
            {
                return !(left == right);
            }

            protected bool Equals(RelaySource other)
            {
                return string.Equals(Name, other.Name) && SourceType == other.SourceType && SessionId.Equals(other.SessionId);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != GetType()) return false;
                return Equals((RelaySource)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Name?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ (int)SourceType;
                    hashCode = (hashCode * 397) ^ SessionId.GetHashCode();
                    return hashCode;
                }
            }

            public string Name;
            public RelaySourceType SourceType;
            public UUID SessionId;
        }

        public IrcClient irc;

        TabsConsole TC => instance.TabConsole;
        RichTextBoxPrinter textPrinter;
        private List<string> chatHistory = new List<string>();
        private int chatPointer;
        volatile bool connecting;
        public OSDMap config;

        RelaySource currentSource;

        public RelayConsole(RadegastInstance instance)
            : base(instance)
        {
            InitializeComponent();
            Disposed += RelayConsole_Disposed;
            
            textPrinter = new RichTextBoxPrinter(rtbChatText);

            // Get configuration settings, and initialize if not found.
            config = instance.GlobalSettings["plugin.irc"] as OSDMap;

            if (config == null)
            {
                config = new OSDMap
                {
                    ["server"] = new OSDString("irc.freenode.net"),
                    ["port"] = new OSDInteger(6667),
                    ["nick"] = new OSDString(string.Empty),
                    ["channel"] = new OSDString("#"),
                    ["send_delay"] = new OSDInteger(200),
                    ["auto_reconnect"] = new OSDBoolean(true),
                    ["ctcp_version"] = new OSDString("Radegast IRC")
                };
                instance.GlobalSettings["plugin.irc"] = config;
            }

            if (!config.ContainsKey("server"))
                config["server"] = new OSDString("irc.freenode.net");
            if (!config.ContainsKey("port"))
                config["port"] = new OSDInteger(6667);
            if (!config.ContainsKey("nick"))
                config["nick"] = new OSDString(instance.Client.Self.Name);
            if (!config.ContainsKey("channel"))
                config["channel"] = new OSDString("#");
            if (!config.ContainsKey("send_delay"))
                config["send_delay"] = new OSDInteger(200);
            if (!config.ContainsKey("auto_reconnect"))
                config["auto_reconnect"] = new OSDBoolean(true);
            if (!config.ContainsKey("ctcp_version"))
                config["ctcp_version"] = new OSDString("Radegast IRC");

            txtChan.Text = config["channel"].AsString();
            txtNick.Text = config["nick"].AsString();
            txtPort.Text = config["port"].AsString();
            txtServer.Text = config["server"].AsString();

            irc = new IrcClient
            {
                SendDelay = config["send_delay"].AsInteger(),
                AutoReconnect = config["auto_reconnect"].AsBoolean(),
                CtcpVersion = config["ctcp_version"].AsString(),
                Encoding = Encoding.UTF8
            };

            TC.OnTabAdded += TC_OnTabAdded;
            TC.OnTabRemoved += TC_OnTabRemoved;
            irc.OnError += irc_OnError;
            irc.OnRawMessage += irc_OnRawMessage;
            irc.OnChannelMessage += irc_OnChannelMessage;
            irc.OnConnected += irc_OnConnected;
            irc.OnDisconnected += irc_OnDisconnected;

            client.Self.IM += Self_IM;
            client.Self.ChatFromSimulator += Self_ChatFromSimulator;

            UpdateGui();

            RefreshGroups();
        }

        void RelayConsole_Disposed(object sender, EventArgs e)
        {
            client.Self.ChatFromSimulator -= Self_ChatFromSimulator;
            client.Self.IM -= Self_IM;

            TC.OnTabAdded -= TC_OnTabAdded;
            TC.OnTabRemoved -= TC_OnTabRemoved;

            irc.OnError -= irc_OnError;
            irc.OnRawMessage -= irc_OnRawMessage;
            irc.OnChannelMessage -= irc_OnChannelMessage;
            irc.OnConnected -= irc_OnConnected;
            irc.OnDisconnected -= irc_OnDisconnected;

            instance.GlobalSettings.Save();

            if (irc.IsConnected)
            {
                irc.AutoReconnect = false;
                irc.Disconnect();
            }
        }

        void TC_OnTabRemoved(object sender, TabEventArgs e)
        {
            RefreshGroups();
        }

        void TC_OnTabAdded(object sender, TabEventArgs e)
        {
            if (e.Tab.Control is GroupIMTabWindow ||
                e.Tab.Control is ConferenceIMTabWindow ||
                e.Tab.Control is IMTabWindow ||
                e.Tab.Control is ChatConsole)
            {
                RefreshGroups();
            }
        }

        void RefreshGroups()
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => RefreshGroups()));
                return;
            }

            cbSource.Items.Clear();

            bool foundActive = false;

            foreach (var tab in TC.Tabs)
            {
                RelaySourceType sourcetype = RelaySourceType.Unknown;
                RelaySource newSource;

                if (tab.Value.Control is GroupIMTabWindow)
                    sourcetype = RelaySourceType.Group;
                else if (tab.Value.Control is ConferenceIMTabWindow)
                    sourcetype = RelaySourceType.Conference;
                else if (tab.Value.Control is IMTabWindow)
                    sourcetype = RelaySourceType.IM;
                else if (tab.Value.Control is ChatConsole)
                    sourcetype = RelaySourceType.Chat;
                else
                    continue;

                UUID sessionId = UUID.Zero;
                UUID.TryParse(tab.Key, out sessionId);

                if (sessionId == UUID.Zero && sourcetype != RelaySourceType.Chat)
                    continue;

                newSource = new RelaySource(sourcetype + ": " + tab.Value.Label, sourcetype, sessionId);

                if (sourcetype == RelaySourceType.IM)
                    newSource.SessionId = ((IMTabWindow)tab.Value.Control).TargetId;

                if (newSource == currentSource)
                    foundActive = true;

                cbSource.Items.Add(newSource);
            }

            if (!foundActive)
            {
                currentSource = null;
                cbSource.Text = "None";
            }
        }

        private void PrintMsg(string fromName, string message)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => PrintMsg(fromName, message)));

                return;
            }

            DateTime timestamp = DateTime.Now;

            if (message == null)
                message = string.Empty;

            if (true)
            {
                textPrinter.ForeColor = SKColors.Gray;
                textPrinter.PrintText(timestamp.ToString("[HH:mm] "));
            }

            textPrinter.ForeColor = SKColors.Black;

            StringBuilder sb = new StringBuilder();

            if (message.StartsWith("/me "))
            {
                sb.Append(fromName);
                sb.Append(message.Substring(3));
            }
            else
            {
                sb.Append(fromName);
                sb.Append(": ");
                sb.Append(message);
            }

            textPrinter.PrintTextLine(sb.ToString());
        }

        private void UpdateGui()
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    Invoke(new MethodInvoker(UpdateGui));
                return;
            }

            if (IsDisposed)
                return;

            bool isConnectedOrConnecting = connecting || irc.IsConnected;

            btnSend.Enabled = isConnectedOrConnecting;
            btnDisconnect.Enabled = isConnectedOrConnecting;
            btnDisconnect.Visible = isConnectedOrConnecting;
            btnConnect.Enabled = !isConnectedOrConnecting;
            btnConnect.Visible = !isConnectedOrConnecting;

            txtChan.ReadOnly = isConnectedOrConnecting;
            txtNick.ReadOnly = isConnectedOrConnecting;
            txtPort.ReadOnly = isConnectedOrConnecting;
            txtServer.ReadOnly = isConnectedOrConnecting;
            rtbChatText.BackColor = isConnectedOrConnecting ? SystemColors.Window : SystemColors.Control;
        }

        private void IrcThread(object param)
        {
            object[] args = (object[])param;
            string server = (string)args[0];
            int port = (int)args[1];
            string nick = (string)args[2];
            string chan = (string)args[3];
            try
            {
                irc.Connect(server, port);
                PrintMsg("System", "Logging in...");
                irc.Login(nick, "Radegast SL Relay", 0, nick);

                connecting = false;
                UpdateGui();

                PrintMsg("System", "Joining channel...");
                irc.RfcJoin(chan);

                PrintMsg("System", "Ready!");
                irc.Listen();

                if (irc.IsConnected)
                {
                    // todo: why disable autoreconnect here?
                    PrintMsg("System", irc.IsConnected.ToString());
                    irc.AutoReconnect = false;
                    irc.Disconnect();
                }
            }
            catch (Exception ex)
            {
                connecting = false;
                PrintMsg("System", "An error has occured: " + ex.Message);
            }

            if (irc.IsConnected)
                irc.Disconnect();

            UpdateGui();
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (connecting)
            {
                PrintMsg("System", "Already connecting");
                return;
            }

            if (irc.IsConnected)
            {
                PrintMsg("System", "Already connected");
                return;
            }

            PrintMsg("System", "Connecting...");
            connecting = true;
            UpdateGui();

            try
            {
                Thread IRCConnection = new Thread(IrcThread)
                {
                    Name = "IRC Thread",
                    IsBackground = true
                };
                int port = 6667;
                int.TryParse(txtPort.Text, out port);
                IRCConnection.Start(new object[] { txtServer.Text, port, txtNick.Text, txtChan.Text });
            }
            catch (Exception ex)
            {
                if (irc.IsConnected)
                    irc.Disconnect();

                PrintMsg("System", "Failed: " + ex.Message);

                connecting = false;
                UpdateGui();
            }
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            if (irc.IsConnected)
            {
                irc.AutoReconnect = false;
                irc.Disconnect();
            }
        }

        void irc_OnRawMessage(object sender, IrcEventArgs e)
        {
            if (e.Data.Type == ReceiveType.Unknown || e.Data.Type == ReceiveType.ChannelMessage) 
                return;

            PrintMsg(e.Data.Nick, e.Data.Type + ": " + e.Data.Message);
        }

        void irc_OnError(object sender, ErrorEventArgs e)
        {
            PrintMsg("Error", e.ErrorMessage);
        }

        void irc_OnDisconnected(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    Invoke(new MethodInvoker(() => irc_OnDisconnected(sender, e)));
                return;
            }

            UpdateGui();
        }

        void irc_OnConnected(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    Invoke(new MethodInvoker(() => irc_OnConnected(sender, e)));
                return;
            }

            UpdateGui();
        }

        void irc_OnChannelMessage(object sender, IrcEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
                {
                    PrintMsg(e.Data.Nick, e.Data.Message);
                    if (currentSource != null)
                    {
                        string message = string.Format("(irc:{2}) {0}: {1}", e.Data.Nick, e.Data.Message, e.Data.Channel);

                        switch (currentSource.SourceType)
                        {
                            case RelaySourceType.Group:
                            case RelaySourceType.Conference:
                                client.Self.InstantMessageGroup(currentSource.SessionId, message);
                                break;
                            case RelaySourceType.Chat:
                                client.Self.Chat(message, 0, ChatType.Normal);
                                break;
                            case RelaySourceType.IM:
                                client.Self.InstantMessage(currentSource.SessionId, message);
                                break;
                        }
                    }
                }
            );
        }

        private void ProcessMessage(string message, string from)
        {
            string[] lines = Regex.Split(message, "\n+");

            foreach (var line in lines)
            {
                string[] words = line.Split(' ');
                string outstr = string.Empty;

                foreach (var word in words)
                {
                    outstr += word + " ";
                    if (outstr.Length > 380)
                    {
                        PrintMsg(irc.Nickname, $"{@from}: {outstr.Remove(outstr.Length - 1)}");
                        irc.SendMessage(SendType.Message, txtChan.Text, $"{@from}: {outstr.Remove(outstr.Length - 1)}");
                        outstr = string.Empty;
                    }
                }
                PrintMsg(irc.Nickname, $"{@from}: {outstr.Remove(outstr.Length - 1)}");
                irc.SendMessage(SendType.Message, txtChan.Text, $"{@from}: {outstr.Remove(outstr.Length - 1)}");
            }
        }

        void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            if (currentSource == null || currentSource.SourceType != RelaySourceType.Chat)
                return;

            if (e.SourceID == client.Self.AgentID)
            {
                if (e.Message.StartsWith("(irc:"))
                    return;
            }

            if (e.Type == ChatType.Normal || e.Type == ChatType.Shout || e.Type == ChatType.Whisper)
                ProcessMessage(e.Message, e.FromName);
        }

        void Self_IM(object sender, InstantMessageEventArgs e)
        {
            if (currentSource == null)
                return;

            if (!irc.IsConnected)
                return;

            if (e.IM.FromAgentID == client.Self.AgentID)
                return;

            if (e.IM.Dialog == InstantMessageDialog.MessageFromAgent || e.IM.Dialog == InstantMessageDialog.MessageFromObject)
            {
                if (e.IM.FromAgentID != currentSource.SessionId)
                    return;
            }
            else if (e.IM.Dialog == InstantMessageDialog.SessionSend)
            {
                if (e.IM.IMSessionID != currentSource.SessionId)
                    return;
            }
            else
            {
                return;
            }

            ProcessMessage(e.IM.Message, e.IM.FromAgentName);
        }

        void SendMsg()
        {
            string msg = cbxInput.Text;
            if (msg == string.Empty)
                return;

            chatHistory.Add(cbxInput.Text);
            chatPointer = chatHistory.Count;

            cbxInput.Text = string.Empty;
            if (irc.IsConnected)
            {
                PrintMsg(irc.Nickname, msg);
                irc.SendMessage(SendType.Message, txtChan.Text, msg);
            }
        }

        void ChatHistoryPrev()
        {
            if (chatPointer == 0)
                return;

            chatPointer--;
            if (chatHistory.Count > chatPointer)
            {
                cbxInput.Text = chatHistory[chatPointer];
                cbxInput.SelectionStart = cbxInput.Text.Length;
                cbxInput.SelectionLength = 0;
            }
        }

        void ChatHistoryNext()
        {
            if (chatPointer == chatHistory.Count) 
                return;

            chatPointer++;
            if (chatPointer == chatHistory.Count)
            {
                cbxInput.Text = string.Empty;
                return;
            }
            cbxInput.Text = chatHistory[chatPointer];
            cbxInput.SelectionStart = cbxInput.Text.Length;
            cbxInput.SelectionLength = 0;
        }

        private void cbxInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                ChatHistoryPrev();
                return;
            }

            if (e.KeyCode == Keys.Down && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                ChatHistoryNext();
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                SendMsg();
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            SendMsg();
        }

        private void txtServer_Validated(object sender, EventArgs e)
        {
            config["server"] = new OSDString(txtServer.Text);
        }

        private void txtPort_Validated(object sender, EventArgs e)
        {
            int port = 6667;
            int.TryParse(txtPort.Text, out port);

            config["port"] = new OSDInteger(port);
        }

        private void txtChan_Validated(object sender, EventArgs e)
        {
            config["channel"] = new OSDString(txtChan.Text);
        }

        private void txtNick_Validated(object sender, EventArgs e)
        {
            config["nick"] = new OSDString(txtNick.Text);
        }

        private void txtPort_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = !char.IsNumber(e.KeyChar);
        }

        private void cbSource_SelectionChangeCommitted(object sender, EventArgs e)
        {
            currentSource = cbSource.SelectedItem as RelaySource;
        }
    }
}
