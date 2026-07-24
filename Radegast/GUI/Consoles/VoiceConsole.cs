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
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreMetaverse;
using LibreMetaverse.StructuredData;
using LibreMetaverse.Voice.WebRTC;

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

        // Ordinal values for progressBar1 (Maximum=2 in the Designer).
        private enum VoiceStatus
        {
            Disconnected = 0,
            Connecting = 1,
            Connected = 2
        };

        // Wraps a Keys value with a friendly display name for the PTT key combo box.
        private sealed class PttKeyChoice
        {
            public readonly Keys Key;
            private readonly string _label;
            public PttKeyChoice(Keys key, string label) { Key = key; _label = label; }
            public override string ToString() => _label;
        }

        // The Keyboard message filter only sees generic virtual-key codes (WM_KEYDOWN/WM_SYSKEYDOWN
        // report VK_MENU/VK_CONTROL/VK_SHIFT, not the left/right variants), so PTT key choices are
        // restricted to keys that are unambiguous without inspecting the extended-key bit.
        private static readonly PttKeyChoice[] PttKeyOptions =
        {
            new PttKeyChoice(Keys.Menu, "Alt"),
            new PttKeyChoice(Keys.ControlKey, "Ctrl"),
            new PttKeyChoice(Keys.ShiftKey, "Shift"),
            new PttKeyChoice(Keys.F1, "F1"),
            new PttKeyChoice(Keys.F2, "F2"),
            new PttKeyChoice(Keys.F3, "F3"),
            new PttKeyChoice(Keys.F4, "F4"),
            new PttKeyChoice(Keys.F5, "F5"),
            new PttKeyChoice(Keys.F6, "F6"),
            new PttKeyChoice(Keys.F7, "F7"),
            new PttKeyChoice(Keys.F8, "F8"),
            new PttKeyChoice(Keys.F9, "F9"),
            new PttKeyChoice(Keys.F10, "F10"),
            new PttKeyChoice(Keys.CapsLock, "Caps Lock"),
            new PttKeyChoice(Keys.Tab, "Tab"),
            new PttKeyChoice(Keys.Insert, "Insert"),
            new PttKeyChoice(Keys.Home, "Home"),
            new PttKeyChoice(Keys.End, "End"),
            new PttKeyChoice(Keys.PageUp, "Page Up"),
            new PttKeyChoice(Keys.PageDown, "Page Down"),
            new PttKeyChoice(Keys.Oemtilde, "` (tilde)"),
            new PttKeyChoice(Keys.Back, "Backspace"),
        };

        private readonly RadegastInstanceForms Instance;
        private INetCom NetCom => Instance.NetCom;
        private GridClient Client => Instance.Client;

        internal VoiceManager voice;
        private readonly Dictionary<UUID, ListViewItem> participantItems = new Dictionary<UUID, ListViewItem>();
        private readonly System.Windows.Forms.Timer pttTimer;
        private readonly ToolTip reconnectToolTip = new ToolTip();
        private bool pttKeyHeld;
        private bool isConnected;
        private string selectedInputDevice = string.Empty;
        private Keys pttKey = Keys.Menu;
        private float micLevelSmooth;
        private int lastMicLevelTick;

        /// <summary>Raised when the primary voice session becomes connected. Used by RadSpeech.</summary>
        public event Action VoiceConnected;
        /// <summary>Raised when the primary voice session disconnects. Used by RadSpeech.</summary>
        public event Action VoiceDisconnected;
        /// <summary>Raised (with the resolved display name) when a participant joins voice range. Used by RadSpeech.</summary>
        public event Action<string> ParticipantJoined;

        public VoiceConsole(RadegastInstanceForms instance)
        {
            InitializeComponent();
            Disposed += VoiceConsole_Disposed;

            Instance = instance;

            NetCom.ClientLoginStatus += NetComClientLoginStatus;

            foreach (var choice in PttKeyOptions)
                pttKeyCombo.Items.Add(choice);

            pttTimer = new System.Windows.Forms.Timer { Interval = 40 };
            pttTimer.Tick += PttTimer_Tick;

            LoadSettings();

            if (chkVoiceEnable.Checked)
                Start();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        #region Lifecycle

        private void Start()
        {
            if (voice == null)
            {
                var candidate = new VoiceManager(Instance.Client);
                if (!candidate.AudioDevice.IsAvailable)
                {
                    // VoiceManager's constructor unconditionally subscribes to
                    // Client.Network.SimChanged/Self.TeleportProgress and registers CAPS event
                    // callbacks regardless of audio availability — the only thing that ever
                    // unhooks them is Disconnect(). Every retry here (Reconnect button, toggling
                    // chkVoiceEnable, a fresh login while hardware is still unavailable) used to
                    // leave `candidate` simply dropped, permanently leaking those subscriptions
                    // against the live GridClient for the rest of the session.
                    candidate.Disconnect();

                    // Leave voice null so the next Start() (e.g. next login, or the
                    // user re-toggling chkVoiceEnable) retries from scratch instead of
                    // being stuck thinking voice is already (half-)initialized.
                    SetStatus(VoiceStatus.Disconnected);
                    return;
                }
                voice = candidate;
                RegisterVoiceEvents();
                RefreshDevices();
            }

            SetStatus(VoiceStatus.Disconnected);

            if (chkAutoConnect.Checked)
                _ = ConnectAsync();
        }

        private void Stop()
        {
            pttTimer.Stop();
            pttKeyHeld = false;
            ClearParticipants();

            if (voice != null)
            {
                UnregisterVoiceEvents();
                voice.Disconnect();
            }
            voice = null;
            isConnected = false;
            SetStatus(VoiceStatus.Disconnected);
        }

        private async Task ConnectAsync()
        {
            if (voice == null) return;
            if (!VoiceManager.IsVoiceAllowedAt(Client))
            {
                // Don't even attempt it — the server rejects the provisioning offer outright
                // (HTTP 472) for a parcel/estate with voice disabled, so trying just wastes a
                // round trip for a failure that isn't really "disconnected," it's "not permitted
                // here." UpdateVoiceAvailability() (driven by voice.OnVoicePermissionChanged)
                // keeps btnReconnect disabled for this same reason, but this guards the
                // auto-connect-on-login path too, which doesn't go through the button.
                UpdateVoiceAvailability(false);
                return;
            }
            SetStatus(VoiceStatus.Connecting);
            try
            {
                bool ok = await voice.ConnectPrimaryRegionAsync();
                if (!ok) SetStatus(VoiceStatus.Disconnected);
            }
            catch
            {
                // The failure reason itself is now logged inside VoiceSession.PostCapsWithRetries
                // (and other library-level failure points) via IVoiceLogger — this catch only
                // needs to update the UI, not duplicate that logging here.
                SetStatus(VoiceStatus.Disconnected);
            }
        }

        // Manual recovery path for when reprovision backoff has genuinely exhausted (see
        // Voice_OnReprovisionFailed) and nothing is going to reconnect on its own. Toggling
        // chkVoiceEnable off/on already does this, but that checkbox reads as "do I want voice
        // at all," not "reconnect now," so it isn't an obvious recovery step for a stuck session.
        private void btnReconnect_Click(object sender, EventArgs e)
        {
            if (voice == null)
            {
                // Never initialized (e.g. audio device unavailable at startup) — retry from scratch.
                if (chkVoiceEnable.Checked) Start();
                return;
            }
            _ = ConnectAsync();
        }

        /// <summary>
        /// Reflects whether voice is permitted at the current parcel/estate in the UI: disables
        /// the Reconnect button (so there's nothing to click into a guaranteed rejection) and
        /// shows why via tooltip, rather than letting the user hit a generic connect failure for
        /// what is really a permissions issue.
        /// </summary>
        private void UpdateVoiceAvailability(bool allowed)
        {
            if (allowed)
            {
                btnReconnect.Enabled = true;
                reconnectToolTip.SetToolTip(btnReconnect, string.Empty);
                reconnectToolTip.SetToolTip(progressBar1, string.Empty);
                return;
            }

            isConnected = false;
            btnReconnect.Enabled = false;
            const string reason = "Voice is disabled on this parcel or estate";
            reconnectToolTip.SetToolTip(btnReconnect, reason);
            reconnectToolTip.SetToolTip(progressBar1, reason);
            progressBar1.Value = 0;
            progressBar1.ForeColor = Color.Gray;
        }

        private void RegisterVoiceEvents()
        {
            Instance.Names.NameUpdated += Names_NameUpdated;
            voice.PeerConnectionReady += Voice_PeerConnectionReady;
            voice.PeerConnectionClosed += Voice_PeerConnectionClosed;
            voice.OnRegionTransitionCompleted += Voice_OnRegionTransitionCompleted;
            voice.OnRegionTransitionFailed += Voice_OnRegionTransitionFailed;
            voice.OnVoicePermissionChanged += Voice_OnVoicePermissionChanged;
            voice.OnReprovisionSucceeded += Voice_OnReprovisionSucceeded;
            voice.OnReprovisionFailed += Voice_OnReprovisionFailed;
            voice.PeerJoined += Voice_PeerJoined;
            voice.PeerLeft += Voice_PeerLeft;
            voice.PeerAudioUpdated += Voice_PeerAudioUpdated;
            voice.AudioDevice.OnAudioSourceEncodedSample += Voice_OnAudioSourceEncodedSample;
        }

        private void UnregisterVoiceEvents()
        {
            Instance.Names.NameUpdated -= Names_NameUpdated;
            voice.PeerConnectionReady -= Voice_PeerConnectionReady;
            voice.PeerConnectionClosed -= Voice_PeerConnectionClosed;
            voice.OnRegionTransitionCompleted -= Voice_OnRegionTransitionCompleted;
            voice.OnRegionTransitionFailed -= Voice_OnRegionTransitionFailed;
            voice.OnVoicePermissionChanged -= Voice_OnVoicePermissionChanged;
            voice.OnReprovisionSucceeded -= Voice_OnReprovisionSucceeded;
            voice.OnReprovisionFailed -= Voice_OnReprovisionFailed;
            voice.PeerJoined -= Voice_PeerJoined;
            voice.PeerLeft -= Voice_PeerLeft;
            voice.PeerAudioUpdated -= Voice_PeerAudioUpdated;
            voice.AudioDevice.OnAudioSourceEncodedSample -= Voice_OnAudioSourceEncodedSample;
        }

        #endregion

        #region Settings

        private void LoadSettings()
        {
            var s = Instance.GlobalSettings;
            chkVoiceEnable.Checked = s["voice_enabled"].Type == OSDType.Unknown || s["voice_enabled"].AsBoolean();
            // Default true-if-unset: preserves the historical Legacy behavior where enabling
            // voice always reconnected automatically on every login.
            chkAutoConnect.Checked = s["voice_auto_connect"].Type == OSDType.Unknown || s["voice_auto_connect"].AsBoolean();
            chkPushToTalk.Checked = s["voice_push_to_talk"].Type == OSDType.Unknown || s["voice_push_to_talk"].AsBoolean();

            int savedVolume = s["voice_output_volume"].Type != OSDType.Unknown ? s["voice_output_volume"].AsInteger() : 80;
            spkrLevel.Value = Math.Max(spkrLevel.Minimum, Math.Min(spkrLevel.Maximum, savedVolume));

            selectedInputDevice = s["voice_input_device"].AsString();

            if (s["voice_ptt_key"].Type != OSDType.Unknown &&
                Enum.TryParse(s["voice_ptt_key"].AsString(), out Keys savedKey))
            {
                pttKey = savedKey;
            }
            pttKeyCombo.SelectedItem = Array.Find(PttKeyOptions, c => c.Key == pttKey) ?? PttKeyOptions[0];
            pttKeyCombo.Enabled = chkPushToTalk.Checked;
        }

        private void SaveSettings()
        {
            var s = Instance.GlobalSettings;
            s["voice_enabled"] = OSD.FromBoolean(chkVoiceEnable.Checked);
            s["voice_auto_connect"] = OSD.FromBoolean(chkAutoConnect.Checked);
            s["voice_push_to_talk"] = OSD.FromBoolean(chkPushToTalk.Checked);
            s["voice_output_volume"] = OSD.FromInteger(spkrLevel.Value);
            s["voice_input_device"] = OSD.FromString(selectedInputDevice ?? string.Empty);
            s["voice_ptt_key"] = OSD.FromString(pttKey.ToString());
        }

        #endregion

        #region Devices

        private void RefreshDevices()
        {
            if (voice == null) return;

            micDevice.Items.Clear();
            micDevice.Items.Add("(Default)");
            foreach (var kv in voice.AudioDevice.GetRecordingDevices())
                micDevice.Items.Add(kv.Value);
            micDevice.SelectedItem = string.IsNullOrEmpty(selectedInputDevice) ? "(Default)" : selectedInputDevice;
            if (!string.IsNullOrEmpty(selectedInputDevice))
                voice.AudioDevice.SetRecordingDevice(selectedInputDevice);

            spkrDevice.Items.Clear();
            spkrDevice.Items.Add("(Default)");
            foreach (var kv in voice.AudioDevice.GetPlaybackDevices())
                spkrDevice.Items.Add(kv.Value);
            spkrDevice.SelectedItem = "(Default)";

            voice.AudioDevice.SpeakerLevel = spkrLevel.Value / 100f;
        }

        private void micDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            var sel = micDevice.SelectedItem as string;
            selectedInputDevice = (sel == null || sel == "(Default)") ? string.Empty : sel;
            voice?.AudioDevice.SetRecordingDevice(string.IsNullOrEmpty(selectedInputDevice) ? null : selectedInputDevice);
            SaveSettings();
        }

        private void spkrDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            var sel = spkrDevice.SelectedItem as string;
            voice?.AudioDevice.SetPlaybackDevice((sel == null || sel == "(Default)") ? null : sel);
        }

        private void spkrLevel_ValueChanged(object sender, EventArgs e)
        {
            if (voice != null)
                voice.AudioDevice.SpeakerLevel = spkrLevel.Value / 100f;
            SaveSettings();
        }

        private async void spkrMute_CheckedChanged(object sender, EventArgs e)
        {
            if (voice == null) return;
            try
            {
                if (spkrMute.Checked)
                    await voice.AudioDevice.StopPlaybackAsync();
                else
                    await voice.AudioDevice.StartPlaybackAsync();
            }
            catch { }
        }

        #endregion

        #region Mute / Talk control

        private void micMute_CheckedChanged(object sender, EventArgs e)
        {
            if (voice == null) return;
            voice.AudioDevice.MicMute = micMute.Checked;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) StartPushToTalk();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) StopPushToTalk();
        }

        private void StartPushToTalk()
        {
            if (voice == null || !isConnected) return;
            voice.AudioDevice.MicMute = false;
            micMute.Checked = false;
        }

        private void StopPushToTalk()
        {
            if (voice == null) return;
            voice.AudioDevice.MicMute = true;
            micMute.Checked = true;
        }

        private void chkPushToTalk_CheckedChanged(object sender, EventArgs e)
        {
            pttKeyCombo.Enabled = chkPushToTalk.Checked;
            SaveSettings();
            if (voice != null && isConnected)
            {
                // Mirrors the "join in whichever mode is selected" behavior on (re)connect:
                // PTT on -> muted until held; PTT off -> open mic.
                voice.AudioDevice.MicMute = chkPushToTalk.Checked;
                micMute.Checked = chkPushToTalk.Checked;
            }
        }

        private void pttKeyCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (pttKeyCombo.SelectedItem is PttKeyChoice choice)
            {
                pttKey = choice.Key;
                SaveSettings();
            }
        }

        private void PttTimer_Tick(object sender, EventArgs e)
        {
            if (voice == null || !isConnected || !chkPushToTalk.Checked)
            {
                if (pttKeyHeld)
                {
                    pttKeyHeld = false;
                    StopPushToTalk();
                }
                return;
            }

            bool down = Instance.Keyboard.IsKeyDown(pttKey) && !IsTypingFocused();

            if (down && !pttKeyHeld)
            {
                pttKeyHeld = true;
                StartPushToTalk();
            }
            else if (!down && pttKeyHeld)
            {
                pttKeyHeld = false;
                StopPushToTalk();
            }
        }

        private bool IsTypingFocused()
        {
            Control c = Instance.MainForm.ActiveControl;
            while (c is ContainerControl cc && cc.ActiveControl != null)
                c = cc.ActiveControl;
            return c is TextBoxBase;
        }

        #endregion

        #region Voice events

        private void Voice_PeerConnectionReady()
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                isConnected = true;
                SetStatus(VoiceStatus.Connected);
                pttTimer.Start();

                bool muted = chkPushToTalk.Checked;
                voice.AudioDevice.MicMute = muted;
                micMute.Checked = muted;
                spkrMute.Checked = false;
                voice.AudioDevice.SpeakerLevel = spkrLevel.Value / 100f;

                VoiceConnected?.Invoke();
            }));
        }

        private void Voice_PeerConnectionClosed()
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                isConnected = false;
                pttTimer.Stop();
                pttKeyHeld = false;
                ClearParticipants();
                micMute.Checked = true;
                SetStatus(VoiceStatus.Disconnected);
                VoiceDisconnected?.Invoke();
            }));
        }

        private void Voice_OnRegionTransitionCompleted()
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                ClearParticipants();
                isConnected = false;
                SetStatus(VoiceStatus.Connecting);
            }));
        }

        private void Voice_OnRegionTransitionFailed(Exception ex)
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                isConnected = false;
                SetStatus(VoiceStatus.Disconnected);
            }));
        }

        private void Voice_OnVoicePermissionChanged(bool allowed)
        {
            BeginInvoke(new MethodInvoker(() => UpdateVoiceAvailability(allowed)));
        }

        // Fired by the dead-channel watchdog (stuck ICE, failed peer connection, etc.) when it
        // rebuilds the session in place — distinct from a region crossing. The rebuilt session's
        // PeerConnectionReady event (already wired) is what actually flips the status back to
        // Connected once it comes up; this handler exists so a failed attempt (see below) doesn't
        // leave the console permanently stuck showing "Connecting" with nothing to correct it.
        private void Voice_OnReprovisionSucceeded()
        {
        }

        private void Voice_OnReprovisionFailed(Exception ex)
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                isConnected = false;
                pttTimer.Stop();
                pttKeyHeld = false;
                SetStatus(VoiceStatus.Disconnected);
            }));
        }

        private void Voice_OnAudioSourceEncodedSample(uint durationRtpUnits, byte[] sample)
        {
            // Opus VBR: encoded frame size loosely correlates with audio amplitude.
            const float maxExpected = 80f;
            float level = Math.Min(1f, sample.Length / maxExpected);
            micLevelSmooth = micLevelSmooth * 0.6f + level * 0.4f;
            var now = Environment.TickCount;
            if (unchecked(now - lastMicLevelTick) < 50) return;
            lastMicLevelTick = now;
            int value = Math.Max(micLevel.Minimum, Math.Min(micLevel.Maximum, (int)(micLevelSmooth * 100)));
            BeginInvoke(new MethodInvoker(() => micLevel.Value = value));
        }

        #endregion

        #region Participants

        private void Voice_PeerJoined(UUID id)
        {
            string name = Instance.Names.Get(id);
            BeginInvoke(new MethodInvoker(() =>
            {
                if (participantItems.ContainsKey(id)) return;

                ListViewItem item = new ListViewItem(name)
                {
                    Name = id.ToString(),
                    Tag = id,
                    StateImageIndex = (int)TalkState.Idle
                };

                participantItems[id] = item;
                lock (participants)
                    participants.Items.Add(item);

                ParticipantJoined?.Invoke(name);
            }));
        }

        private void Voice_PeerLeft(UUID id)
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                if (!participantItems.TryGetValue(id, out ListViewItem item)) return;
                lock (participants)
                    participants.Items.Remove(item);
                participantItems.Remove(id);
            }));
        }

        private void Voice_PeerAudioUpdated(UUID id, VoiceSession.PeerAudioState state)
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                if (!participantItems.TryGetValue(id, out ListViewItem item)) return;

                if (state.ModeratorMuted == true)
                    item.StateImageIndex = (int)TalkState.Muted;
                else if (state.VoiceActive == true)
                    item.StateImageIndex = (int)TalkState.Talking;
                else
                    item.StateImageIndex = (int)TalkState.Idle;
            }));
        }

        private void Names_NameUpdated(object sender, UUIDNameReplyEventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !Instance.MonoRuntime)
                    BeginInvoke((MethodInvoker)(() => Names_NameUpdated(sender, e)));
                return;
            }

            lock (participants)
            {
                foreach (var name in e.Names)
                {
                    if (participantItems.TryGetValue(name.Key, out ListViewItem item))
                        item.Text = name.Value;
                }
            }
        }

        private void ClearParticipants()
        {
            lock (participants)
                participants.Items.Clear();
            participantItems.Clear();
        }

        /// <summary>
        /// Open context menu for voice items
        /// </summary>
        private void RadegastContextMenuStrip_OnContentMenuOpened(object sender, RadegastContextMenuStrip.ContextMenuEventArgs e)
        {
            lock (e.Menu)
            {
                ListViewItem item = e.Menu.Selection as ListViewItem;
                if (item?.Tag is UUID peerId)
                {
                    bool currentlyMuted = item.StateImageIndex == (int)TalkState.Muted;
                    ToolStripButton muteButton = currentlyMuted
                        ? new ToolStripButton("Unmute", null, (s, ev) => SetPeerMuted(peerId, false))
                        : new ToolStripButton("Mute", null, (s, ev) => SetPeerMuted(peerId, true));
                    e.Menu.Items.Add(muteButton);
                }
            }
        }

        private void SetPeerMuted(UUID peerId, bool mute)
        {
            voice?.SetPeerMute(peerId, mute);
            if (participantItems.TryGetValue(peerId, out ListViewItem item))
                item.StateImageIndex = (int)(mute ? TalkState.Muted : TalkState.Idle);
        }

        /// <summary>
        /// Right-clicks on participants beings up Mute, etc menu
        /// </summary>
        private void participants_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            RadegastContextMenuStrip cms = new RadegastContextMenuStrip();
            Instance.ContextActionManager.AddContributions(cms, Instance.Client);
            cms.Show((Control)sender, new Point(e.X, e.Y));
        }

        private void btnUnmuteAll_Click(object sender, EventArgs e)
        {
            foreach (var kvp in participantItems)
            {
                if (kvp.Key == Instance.Client.Self.AgentID) continue;
                SetPeerMuted(kvp.Key, false);
            }
        }

        private void btnMuteAll_Click(object sender, EventArgs e)
        {
            foreach (var kvp in participantItems)
            {
                if (kvp.Key == Instance.Client.Self.AgentID) continue;
                SetPeerMuted(kvp.Key, true);
            }
        }

        #endregion

        #region Console control

        private void SetStatus(VoiceStatus status)
        {
            progressBar1.Value = (int)status;
            progressBar1.ForeColor = status switch
            {
                VoiceStatus.Connected => Color.Green,
                VoiceStatus.Connecting => Color.Yellow,
                _ => Color.Red
            };
        }

        private void VoiceConsole_Disposed(object sender, EventArgs e)
        {
            try
            {
                NetCom.ClientLoginStatus -= NetComClientLoginStatus;
                pttTimer?.Stop();
                pttTimer?.Dispose();
                if (voice != null)
                {
                    UnregisterVoiceEvents();
                    voice.Disconnect();
                }
            }
            catch { }
        }

        private void NetComClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status != LoginStatus.Success) return;

            BeginInvoke(new MethodInvoker(delegate ()
            {
                if (chkVoiceEnable.Checked)
                    Start();
            }));
        }

        private void chkVoiceEnable_Click(object sender, EventArgs e)
        {
            SaveSettings();
            if (chkVoiceEnable.Checked)
                Start();
            else
                Stop();
        }

        private void chkAutoConnect_CheckedChanged(object sender, EventArgs e)
        {
            SaveSettings();
        }

        private void VoiceConsole_Load(object sender, EventArgs e)
        {
            if (voice != null)
                RefreshDevices();
        }

        #endregion
    }
}
