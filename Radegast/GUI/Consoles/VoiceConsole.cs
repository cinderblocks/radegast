/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2022, Sjofn, LLC
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
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using LibreMetaverse.Voice.Vivox;

namespace Radegast
{
    public partial class VoiceConsole : UserControl
    {
        // These enumerated values must match the sequence of icons in TalkStates.
        private enum TalkState
        {
            Idle = 0,
            Talking,
            Muted
        };

        private RadegastInstance instance;
        private Radegast.Netcom netcom => instance.Netcom;
        private GridClient client => instance.Client;
        private TabsConsole tabConsole;
        private OSDMap config;
        public VoiceGateway gateway = null;
        public VoiceSession session;
        private VoiceParticipant selected;

        public VoiceConsole(RadegastInstance instance)
        {
            InitializeComponent();
            Disposed += VoiceConsole_Disposed;

            this.instance = instance;

            // Callbacks
            netcom.ClientLoginStatus += netcom_ClientLoginStatus;

            this.instance.MainForm.Load += MainForm_Load;

            config = instance.GlobalSettings["voice"] as OSDMap;
            if (config == null)
            {
                config = new OSDMap {["enabled"] = new OSDBoolean(false)};
                instance.GlobalSettings["voice"] = config;
                instance.GlobalSettings.Save();
            }

            chkVoiceEnable.Checked = config["enabled"].AsBoolean();
            if (chkVoiceEnable.Checked)
                Start();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void Start()
        {
            if (gateway == null)
            {
                gateway = new VoiceGateway(instance.Client);
            }

            // Initialize progress bar
            progressBar1.Maximum = (int)VoiceGateway.ConnectionState.SessionRunning;
            SetProgress(0);
            RegisterClientEvents();

            gateway.Start();
        }

        private void Stop()
        {
            participants.Clear();
            gateway.Stop();
            UnregisterClientEvents();
            session = null;
            gateway = null;
            SetProgress(VoiceGateway.ConnectionState.None);
            GC.Collect();
        }

        /// <summary>
        /// Open context menu for voice items
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void RadegastContextMenuStrip_OnContentMenuOpened(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
        {
            lock (e.Menu)
            {
                // Figure out what this menu applies to.
                ListViewItem item = e.Menu.Selection as ListViewItem;
                if (item?.Tag is VoiceParticipant tag)
                {
                    selected = tag;
                    ToolStripButton muteButton;
                    muteButton = selected.IsMuted 
                        ? new ToolStripButton("Unmute", null, OnUnMuteClick)
                        : new ToolStripButton("Mute", null, OnMuteClick);
                    e.Menu.Items.Add(muteButton);
                }
            }
        }

        void OnMuteClick(object sender, EventArgs e)
        {
            selected.IsMuted = true;
        }

        void OnUnMuteClick(object sender, EventArgs e)
        {
            selected.IsMuted = false;
        }

        private void RegisterClientEvents()
        {
            instance.Names.NameUpdated += Names_NameUpdated;

            // Voice hooks
            gateway.OnSessionCreate +=
                gateway_OnSessionCreate;
            gateway.OnSessionRemove +=
                gateway_OnSessionRemove;
            gateway.OnVoiceConnectionChange +=
                gateway_OnVoiceConnectionChange;
            gateway.OnAuxGetCaptureDevicesResponse +=
                gateway_OnAuxGetCaptureDevicesResponse;
            gateway.OnAuxGetRenderDevicesResponse +=
                gateway_OnAuxGetRenderDevicesResponse;
        }

        private void UnregisterClientEvents()
        {
            instance.Names.NameUpdated -= Names_NameUpdated;

            gateway.OnSessionCreate -=
               gateway_OnSessionCreate;
            gateway.OnSessionRemove -=
                gateway_OnSessionRemove;
            gateway.OnVoiceConnectionChange -=
                gateway_OnVoiceConnectionChange;
            gateway.OnAuxGetCaptureDevicesResponse -=
                gateway_OnAuxGetCaptureDevicesResponse;
            gateway.OnAuxGetRenderDevicesResponse -=
                gateway_OnAuxGetRenderDevicesResponse;
        }

        void Names_NameUpdated(object sender, UUIDNameReplyEventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke((MethodInvoker)(() => Names_NameUpdated(sender, e)));
                return;
            }

            lock (participants)
            {
                foreach (var name in e.Names)
                {
                    if (participants.Items.ContainsKey(name.Key.ToString()))
                    {
                        participants.Items[name.Key.ToString()].Text = name.Value;
                        ((VoiceParticipant)participants.Items[name.Key.ToString()].Tag).Name = name.Value;
                    }
                }
            }
        }

        #region Connection Status
        void gateway_OnAuxGetRenderDevicesResponse(object sender, VoiceGateway.VoiceDevicesEventArgs e)
        {
            BeginInvoke(new MethodInvoker(() => LoadSpkrDevices(e.Devices, e.CurrentDevice)));
        }

        void gateway_OnAuxGetCaptureDevicesResponse(object sender, VoiceGateway.VoiceDevicesEventArgs e)
        {
            BeginInvoke(new MethodInvoker(() => LoadMicDevices(e.Devices, e.CurrentDevice)));
        }

        void gateway_OnVoiceConnectionChange(VoiceGateway.ConnectionState state)
        {
            BeginInvoke(new MethodInvoker(() => SetProgress(state)));
        }

        void SetProgress(VoiceGateway.ConnectionState s)
        {
            int value = (int)s;
            if (value == progressBar1.Maximum)
            {
                progressBar1.ForeColor = Color.Green;
                LoadConfigVolume();
            }
            else if (value > (progressBar1.Maximum / 2))
                progressBar1.ForeColor = Color.Yellow;
            else
                progressBar1.ForeColor = Color.Red;

            progressBar1.Value = value;
        }
        #endregion

        #region Sessions
        bool queriedDevices = false;

        void gateway_OnSessionCreate(object sender, EventArgs e)
        {
            if (!queriedDevices)
            {
                queriedDevices = true;
                Logger.Log("Voice session started, asking for device info", Helpers.LogLevel.Debug);
                gateway.AuxGetCaptureDevices();
                gateway.AuxGetRenderDevices();
            }

            VoiceSession s = sender as VoiceSession;

            BeginInvoke(new MethodInvoker(delegate()
             {
                 // There could theoretically be more than one session active
                 // at a time, but the current implementation in SL seems to be limited to one.
                 session = s;
                 session.OnParticipantAdded += session_OnParticipantAdded;
                 session.OnParticipantRemoved += session_OnParticipantRemoved;
                 session.OnParticipantUpdate += session_OnParticipantUpdate;
                 participants.Clear();

                 // Default Mic off and Spkr on
                 gateway.MicMute = true;
                 gateway.SpkrMute = false;
                 gateway.SpkrLevel = 64;
                 gateway.MicLevel = 64;

                 SetProgress(VoiceGateway.ConnectionState.SessionRunning);
             }));
        }

        void gateway_OnSessionRemove(object sender, EventArgs e)
        {
            VoiceSession s = sender as VoiceSession;
            if (s == null) return;

            s.OnParticipantAdded -= session_OnParticipantAdded;
            s.OnParticipantRemoved -= session_OnParticipantRemoved;
            s.OnParticipantUpdate -= session_OnParticipantUpdate;

            if (session != s) return;

            BeginInvoke(new MethodInvoker(delegate()
             {
                 participants.Clear();

                 if (gateway != null)
                 {
                     gateway.MicMute = true;
                 }
                 micMute.Checked = true;

                 session = null;
             }));
        }


        #endregion
        #region Participants
        void session_OnParticipantAdded(object sender, EventArgs e)
        {
            VoiceParticipant p = sender as VoiceParticipant;
            BeginInvoke(new MethodInvoker(delegate()
            {
                // Supply the name based on the UUID.
                if (p == null) return;
                p.Name = instance.Names.Get(p.ID);

                if (participants.Items.ContainsKey(p.ID.ToString()))
                    return;

                ListViewItem item = new ListViewItem(p.Name)
                {
                    Name = p.ID.ToString(), Tag = p, StateImageIndex = (int)TalkState.Idle
                };

                lock (participants)
                    participants.Items.Add(item);
            }));
        }

        void session_OnParticipantRemoved(object sender, EventArgs e)
        {
            VoiceParticipant p = sender as VoiceParticipant;
            if (p?.Name == null) return;
            BeginInvoke(new MethodInvoker(delegate()
            {
                lock (participants)
                {
                    ListViewItem item = FindParticipantItem(p);
                    if (item != null)
                        participants.Items.Remove(item);
                }
            }));
        }

        void session_OnParticipantUpdate(object sender, EventArgs e)
        {
            VoiceParticipant p = sender as VoiceParticipant;
            if (p?.Name == null) return;
            BeginInvoke(new MethodInvoker(delegate()
            {
                lock (participants)
                {
                    ListViewItem item = FindParticipantItem(p);
                    if (item != null)
                    {
                        if (p.IsMuted)
                            item.StateImageIndex = (int)TalkState.Muted;
                        else if (p.IsSpeaking)
                            item.StateImageIndex = (int)TalkState.Talking;
                        else
                            item.StateImageIndex = (int)TalkState.Idle;
                    }
                }
            }));
        }

        /// <summary>
        /// Right-clicks on participants beings up Mute, etc menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void participants_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            RadegastContextMenuStrip cms = new RadegastContextMenuStrip();
            instance.ContextActionManager.AddContributions(cms, instance.Client);
            cms.Show((Control)sender, new Point(e.X, e.Y));
        }

        /// <summary>
        /// UnMute all participants
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnUnmuteAll_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem i in participants.Items)
            {
                if (i.Tag is VoiceParticipant p && p.ID != instance.Client.Self.AgentID)
                {
                    p.IsMuted = false;
                    i.StateImageIndex = (int)TalkState.Idle;
                }
            }
        }

        /// <summary>
        /// Mute all participants
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnMuteAll_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem i in participants.Items)
            {
                if (i.Tag is VoiceParticipant p && p.ID != instance.Client.Self.AgentID)
                {
                    p.IsMuted = true;
                    i.StateImageIndex = (int)TalkState.Muted;
                }
            }
        }

        ListViewItem FindParticipantItem(VoiceParticipant p)
        {
            foreach (ListViewItem item in participants.Items)
            {
                if (item.Tag == p)
                    return item;
            }
            return null;
        }

        #endregion
        #region Console control
        void VoiceConsole_Disposed(object sender, EventArgs e)
        {
            try
            {
                netcom.ClientLoginStatus -= netcom_ClientLoginStatus;
                if (gateway != null)
                {
                    UnregisterClientEvents();
                    gateway.Stop();
                }
            }
            catch { }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            tabConsole = instance.TabConsole;
        }

        private void netcom_ClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status != LoginStatus.Success) return;

            BeginInvoke(new MethodInvoker(delegate()
            {
                if (chkVoiceEnable.Checked)
                    gateway.Start();
            }));

        }
        #endregion

        #region Talk control
        void OnMouseUp(object sender, MouseEventArgs e)
        {
            BeginInvoke(new MethodInvoker(delegate()
            {
                if (e.Button == MouseButtons.Left)
                {
                    micMute.Checked = true;
                    gateway.MicMute = true;
                }
            }));
        }

        void OnMouseDown(object sender, MouseEventArgs e)
        {
            BeginInvoke(new MethodInvoker(delegate()
            {

                if (e.Button == MouseButtons.Left)
                {
                    micMute.Checked = false;
                    gateway.MicMute = false;
                }
            }));
        }
        #endregion

        #region Audio Settings

        void LoadMicDevices(List<string> available, string current)
        {
            if (available == null) return;

            micDevice.Items.Clear();
            foreach (string name in available)
                micDevice.Items.Add(name);
            micDevice.SelectedItem = current;
        }

        void LoadSpkrDevices(List<string> available, string current)
        {
            if (available == null) return;

            spkrDevice.Items.Clear();
            foreach (string name in available)
                spkrDevice.Items.Add(name);
            spkrDevice.SelectedItem = current;
        }

        private void spkrDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            gateway.PlaybackDevice = (string)spkrDevice.SelectedItem;
            config["playback.device"] = new OSDString((string)spkrDevice.SelectedItem);
            instance.GlobalSettings.Save();
        }

        private void micDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            gateway.CurrentCaptureDevice = (string)micDevice.SelectedItem;
            config["capture.device"] = new OSDString((string)micDevice.SelectedItem);
            instance.GlobalSettings.Save();
        }

        private void micLevel_ValueChanged(object sender, EventArgs e)
        {
            if (gateway != null)
            {
                gateway.MicLevel = micLevel.Value;
            }
            instance.GlobalSettings["voice_mic_level"] = micLevel.Value;
        }

        private void spkrLevel_ValueChanged(object sender, EventArgs e)
        {
            if (gateway != null)
            {
                gateway.SpkrLevel = spkrLevel.Value;
            }
            instance.GlobalSettings["voice_speaker_level"] = spkrLevel.Value;
        }
        #endregion

        /// <summary>
        /// Start and stop the voice functions.
        /// </summary>
        private void chkVoiceEnable_Click(object sender, EventArgs e)
        {
            BeginInvoke(new MethodInvoker(delegate()
            {
                config["enabled"] = new OSDBoolean(chkVoiceEnable.Checked);
                instance.GlobalSettings.Save();

                if (chkVoiceEnable.Checked)
                {
                    Start();
                }
                else
                {
                    Stop();
                }
            }));
        }

        private void micMute_CheckedChanged(object sender, EventArgs e)
        {
            if (gateway != null)
            {
                gateway.MicMute = micMute.Checked;
            }
        }

        void LoadConfigVolume()
        {
            if (instance.GlobalSettings.ContainsKey("voice_mic_level"))
            {
                micLevel.Value = instance.GlobalSettings["voice_mic_level"];
            }

            if (instance.GlobalSettings.ContainsKey("voice_speaker_level"))
            {
                spkrLevel.Value = instance.GlobalSettings["voice_speaker_level"];
            }
        }

        private void VoiceConsole_Load(object sender, EventArgs e)
        {
            LoadConfigVolume();
        }
    }
}

