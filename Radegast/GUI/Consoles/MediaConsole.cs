/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2026, Sjofn, LLC
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
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Radegast.Media;

namespace Radegast
{
    public partial class MediaConsole : DetachableControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private readonly Settings s;
        private float m_audioVolume;

        private float audioVolume
        {
            get => m_audioVolume;
            set
            {
                if (value >= 0f && value < 1f)
                {
                    m_audioVolume = value;
                    parcelStream.Volume = m_audioVolume;
                }
            }
        }
        private System.Threading.Timer configTimer;
        private const int saveConfigTimeout = 1000;
        private bool playing;
        private string currentURL;
        private Stream parcelStream;
        private readonly object parcelMusicLock = new object();


        public MediaConsole(RadegastInstanceForms instance)
        {
            InitializeComponent();
            DisposeOnDetachedClose = false;
            Text = "Media";

            Disposed += MediaConsole_Disposed;

            this.instance = instance;
            parcelStream = new Stream();

            s = instance.GlobalSettings;

            // Set some defaults in case we don't have them in config
            audioVolume = 0.2f;
            objVolume.Value = 50;
            instance.MediaManager.ObjectVolume = 1f;

            // Restore settings
            if (s["parcel_audio_url"].Type != OSDType.Unknown)
                txtAudioURL.Text = s["parcel_audio_url"].AsString();
            if (s["parcel_audio_vol"].Type != OSDType.Unknown)
                audioVolume = (float)s["parcel_audio_vol"].AsReal();
            if (s["parcel_audio_play"].Type != OSDType.Unknown)
                cbPlayAudioStream.Checked = s["parcel_audio_play"].AsBoolean();
            if (s["parcel_audio_keep_url"].Type != OSDType.Unknown)
                cbKeep.Checked = s["parcel_audio_keep_url"].AsBoolean();
            if (s["object_audio_enable"].Type != OSDType.Unknown)
                cbObjSoundEnable.Checked = s["object_audio_enable"].AsBoolean();
            if (s["object_audio_vol"].Type != OSDType.Unknown)
            {
                instance.MediaManager.ObjectVolume = (float)s["object_audio_vol"].AsReal();
                objVolume.Value = (int)(50f * instance.MediaManager.ObjectVolume);
            }
            if (s["ui_audio_vol"].Type != OSDType.Unknown)
            {
                instance.MediaManager.UIVolume = (float)s["ui_audio_vol"].AsReal();
                UIVolume.Value = (int)(50f * instance.MediaManager.UIVolume);
            }
            if (s["master_volume"].Type != OSDType.Unknown)
            {
                instance.MediaManager.MasterVolume = (float)s["master_volume"].AsReal();
                masterVolume.Value = (int)(100f * instance.MediaManager.MasterVolume);
            }

            volAudioStream.Value = (int)(audioVolume * 50);
            instance.MediaManager.ObjectEnable = cbObjSoundEnable.Checked;

            // Set preferred audio driver before initializing (if saved)
            if (s["audio_driver_index"].Type != OSDType.Unknown)
            {
                instance.MediaManager.PreferredDriver = s["audio_driver_index"].AsInteger();
            }

            configTimer = new System.Threading.Timer(SaveConfig, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            // Initialize audio device controls
            InitializeAudioDeviceControls();

            // Load audio profiles
            PopulateAudioProfiles();

            // Subscribe to sound system availability changes
            instance.MediaManager.SoundSystemAvailableChanged += MediaManager_SoundSystemAvailableChanged;

            // Subscribe to audio device changes
            instance.MediaManager.AudioDevicesChanged += MediaManager_AudioDevicesChanged;

            if (!instance.MediaManager.SoundSystemAvailable)
            {
                foreach (Control c in pnlParcelAudio.Controls)
                    c.Enabled = false;
            }

            // GUI Events
            volAudioStream.Scroll += volAudioStream_Scroll;
            txtAudioURL.TextChanged += txtAudioURL_TextChanged;
            cbKeep.CheckedChanged += cbKeep_CheckedChanged;
            cbPlayAudioStream.CheckedChanged += cbPlayAudioStream_CheckedChanged;
            lblStation.Tag = lblStation.Text = string.Empty;
            lblStation.Click += lblStation_Click;

            objVolume.Scroll += volObject_Scroll;
            cbObjSoundEnable.CheckedChanged += cbObjEnableChanged;

            UIVolume.Scroll += UIVolume_Scroll;
            masterVolume.Scroll += masterVolume_Scroll;

            // Network callbacks
            client.Parcels.ParcelProperties += Parcels_ParcelProperties;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void PopulateAudioProfiles()
        {
            cmbAudioProfile.Items.Clear();

            // Add predefined profiles
            foreach (var profile in Media.MediaManager.GetPredefinedProfiles())
            {
                cmbAudioProfile.Items.Add(profile);
            }

            // Add custom profiles from settings
            if (s["audio_profiles"].Type == OSDType.Array)
            {
                var profilesArray = (OSDArray)s["audio_profiles"];
                foreach (var profileOSD in profilesArray)
                {
                    var profile = Media.AudioProfile.FromOSD(profileOSD);
                    if (profile != null)
                    {
                        cmbAudioProfile.Items.Add(profile);
                    }
                }
            }

            if (cmbAudioProfile.Items.Count > 0)
            {
                cmbAudioProfile.SelectedIndex = 0;
            }
        }

        private void MediaManager_SoundSystemAvailableChanged(object sender, Media.SoundSystemAvailableEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => MediaManager_SoundSystemAvailableChanged(sender, e)));
                return;
            }

            // Refresh audio device controls when sound system becomes available or unavailable
            InitializeAudioDeviceControls();

            // Enable or disable parcel audio controls based on availability
            foreach (Control c in pnlParcelAudio.Controls)
                c.Enabled = e.IsAvailable;

            if (e.IsAvailable)
            {
                Logger.Info("Sound system became available - UI updated");
            }
        }

        private void MediaManager_AudioDevicesChanged(object sender, Media.AudioDevicesChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => MediaManager_AudioDevicesChanged(sender, e)));
                return;
            }

            Logger.Info($"Audio devices changed: {e.DeviceCount} device(s) detected");
            
            // Refresh the device list
            PopulateAudioDevices();
            UpdateAudioDeviceStatus();
            
            // Optionally notify user
            if (instance.GlobalSettings["audio_notify_device_changes"].AsBoolean())
            {
                instance.ShowNotificationInChat(
                    $"Audio devices changed: {e.DeviceCount} device(s) detected. Check Media tab if sound stopped working.",
                    ChatBufferTextStyle.Alert);
            }
        }

        private void InitializeAudioDeviceControls()
        {
            UpdateAudioDeviceStatus();
            PopulateAudioDevices();

            // Restore saved audio driver selection
            if (s["audio_driver_index"].Type != OSDType.Unknown)
            {
                int savedDriverIndex = s["audio_driver_index"].AsInteger();
                if (savedDriverIndex >= 0 && savedDriverIndex < cmbAudioDevice.Items.Count)
                {
                    cmbAudioDevice.SelectedIndex = savedDriverIndex;
                }
            }
            else if (cmbAudioDevice.Items.Count > 0)
            {
                cmbAudioDevice.SelectedIndex = 0;
            }
        }

        private void PopulateAudioDevices()
        {
            cmbAudioDevice.Items.Clear();

            if (!instance.MediaManager.SoundSystemAvailable)
            {
                cmbAudioDevice.Items.Add("Sound system not available");
                cmbAudioDevice.SelectedIndex = 0;
                cmbAudioDevice.Enabled = false;
                btnRefreshDevices.Enabled = false;
                return;
            }

            try
            {
                var drivers = instance.MediaManager.GetAudioDrivers();
                
                if (drivers.Count == 0)
                {
                    cmbAudioDevice.Items.Add("No audio devices found");
                    cmbAudioDevice.SelectedIndex = 0;
                    cmbAudioDevice.Enabled = false;
                }
                else
                {
                    foreach (var driver in drivers)
                    {
                        cmbAudioDevice.Items.Add(driver);
                    }
                    
                    // Select the current driver
                    int currentDriver = instance.MediaManager.SelectedDriver;
                    if (currentDriver >= 0 && currentDriver < cmbAudioDevice.Items.Count)
                    {
                        cmbAudioDevice.SelectedIndex = currentDriver;
                    }
                    else if (cmbAudioDevice.Items.Count > 0)
                    {
                        cmbAudioDevice.SelectedIndex = 0;
                    }
                    
                    cmbAudioDevice.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to populate audio devices", ex);
                cmbAudioDevice.Items.Add("Error loading devices");
                cmbAudioDevice.SelectedIndex = 0;
                cmbAudioDevice.Enabled = false;
            }
        }

        private void UpdateAudioDeviceStatus()
        {
            if (instance.MediaManager.SoundSystemAvailable)
            {
                lblAudioDeviceStatus.Text = $"Sound system ready ({instance.MediaManager.DriverCount} device(s))";
                lblAudioDeviceStatus.ForeColor = System.Drawing.Color.Green;
                btnRetryInit.Enabled = false;
            }
            else
            {
                lblAudioDeviceStatus.Text = "Sound system not available";
                lblAudioDeviceStatus.ForeColor = System.Drawing.Color.Red;
                btnRetryInit.Enabled = true;
            }
        }

        private void MediaConsole_Disposed(object sender, EventArgs e)
        {
            Stop();

            // Unsubscribe from events
            instance.MediaManager.SoundSystemAvailableChanged -= MediaManager_SoundSystemAvailableChanged;
            instance.MediaManager.AudioDevicesChanged -= MediaManager_AudioDevicesChanged;
            client.Parcels.ParcelProperties -= Parcels_ParcelProperties;

            if (configTimer != null)
            {
                configTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                configTimer.Dispose();
                configTimer = null;
            }
        }

        private void Parcels_ParcelProperties(object sender, ParcelPropertiesEventArgs e)
        {
            if (cbKeep.Checked || e.Result != ParcelResult.Single) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Parcels_ParcelProperties(sender, e)));
                return;
            }
            lock (parcelMusicLock)
            {
                txtAudioURL.Text = e.Parcel.MusicURL;
                if (playing)
                {
                    if (currentURL != txtAudioURL.Text)
                    {
                        currentURL = txtAudioURL.Text;
                        Play();
                    }
                }
                else if (cbPlayAudioStream.Checked)
                {
                    currentURL = txtAudioURL.Text;
                    Play();
                }
            }
        }

        private void Stop()
        {
            lock (parcelMusicLock)
            {
                playing = false;
                parcelStream?.Dispose();
                parcelStream = null;
                lblStation.Tag = lblStation.Text = string.Empty;
                txtSongTitle.Text = string.Empty;
            }
        }

        private void Play()
        {
            lock (parcelMusicLock)
            {
                Stop();
                playing = true;
                parcelStream = new Stream {Volume = audioVolume};
                parcelStream.PlayStream(currentURL);
                parcelStream.OnStreamInfo += ParcelMusic_OnStreamInfo;
            }
        }

        private void ParcelMusic_OnStreamInfo(object sender, StreamInfoArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => ParcelMusic_OnStreamInfo(sender, e)));
                return;
            }

            switch (e.Key)
            {
                case "artist":
                    txtSongTitle.Text = e.Value;
                    break;

                case "title":
                    txtSongTitle.Text += " - " + e.Value;
                    break;

                case "icy-name":
                    lblStation.Text = e.Value;
                    break;

                case "icy-url":
                    lblStation.Tag = e.Value;
                    break;
            }
        }

        private void SaveConfig(object state)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => SaveConfig(state)));
                return;
            }

            s["parcel_audio_url"] = OSD.FromString(txtAudioURL.Text);
            s["parcel_audio_vol"] = OSD.FromReal(audioVolume);
            s["parcel_audio_play"] = OSD.FromBoolean(cbPlayAudioStream.Checked);
            s["parcel_audio_keep_url"] = OSD.FromBoolean(cbKeep.Checked);
            s["object_audio_vol"] = OSD.FromReal(instance.MediaManager.ObjectVolume);
            s["object_audio_enable"] = OSD.FromBoolean(cbObjSoundEnable.Checked);
            s["ui_audio_vol"] = OSD.FromReal(instance.MediaManager.UIVolume);
            s["master_volume"] = OSD.FromReal(instance.MediaManager.MasterVolume);
            
            // Save selected audio driver
            if (cmbAudioDevice.SelectedIndex >= 0 && instance.MediaManager.SoundSystemAvailable)
            {
                s["audio_driver_index"] = OSD.FromInteger(cmbAudioDevice.SelectedIndex);
            }
        }

        #region GUI event handlers

        private void lblStation_Click(object sender, EventArgs e)
        {
            if (lblStation.ToString() != string.Empty)
            {
                instance.MainForm.ProcessLink(lblStation.Tag.ToString());
            }
        }

        private void volAudioStream_Scroll(object sender, EventArgs e)
        {
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
            lock (parcelMusicLock)
                if (parcelStream != null)
                    audioVolume = volAudioStream.Value / 50f;
        }

        private void volObject_Scroll(object sender, EventArgs e)
        {
            instance.MediaManager.ObjectVolume = objVolume.Value / 50f;
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
        }

        private void cbObjEnableChanged(object sender, EventArgs e)
        {
            instance.MediaManager.ObjectEnable = cbObjSoundEnable.Checked;
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
        }

        private void txtAudioURL_TextChanged(object sender, EventArgs e)
        {
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
        }

        private void cbPlayAudioStream_CheckedChanged(object sender, EventArgs e)
        {
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
        }

        private void cbKeep_CheckedChanged(object sender, EventArgs e)
        {
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
        }

        private void btnPlay_Click(object sender, EventArgs e)
        {
            lock (parcelMusicLock) if (!playing)
            {
                currentURL = txtAudioURL.Text;
                Play();
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            lock (parcelMusicLock) if (playing)
            {
                currentURL = string.Empty;
                Stop();
            }
        }

        private void UIVolume_Scroll(object sender, EventArgs e)
        {
            instance.MediaManager.UIVolume = UIVolume.Value / 50f;
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
        }

        private void masterVolume_Scroll(object sender, EventArgs e)
        {
            instance.MediaManager.MasterVolume = masterVolume.Value / 100f;
            UpdateMuteButtonText();
            configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
        }

        private void btnMuteAll_Click(object sender, EventArgs e)
        {
            instance.MediaManager.MuteAll = !instance.MediaManager.MuteAll;
            UpdateMuteButtonText();
        }

        private void UpdateMuteButtonText()
        {
            if (instance.MediaManager.MuteAll)
            {
                btnMuteAll.Text = "Unmute All";
                masterVolume.Enabled = false;
            }
            else
            {
                btnMuteAll.Text = "Mute All";
                masterVolume.Enabled = true;
            }
        }

        private void btnLoadProfile_Click(object sender, EventArgs e)
        {
            if (cmbAudioProfile.SelectedItem is Media.AudioProfile profile)
            {
                try
                {
                    profile.ApplyTo(instance.MediaManager);

                    // Update UI to reflect new values
                    masterVolume.Value = (int)(profile.MasterVolume * 100);
                    objVolume.Value = (int)(profile.ObjectVolume * 50);
                    UIVolume.Value = (int)(profile.UIVolume * 50);
                    volAudioStream.Value = (int)(profile.MusicVolume * 50);
                    cbObjSoundEnable.Checked = profile.ObjectSoundsEnabled;

                    Logger.Info($"Loaded audio profile: {profile.Name}");
                    instance.ShowNotificationInChat($"Loaded audio profile: {profile.Name}", ChatBufferTextStyle.Normal);

                    configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error loading profile: {profile.Name}", ex);
                    MessageBox.Show(
                        $"Error loading profile: {ex.Message}",
                        "Profile Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void btnSaveProfile_Click(object sender, EventArgs e)
        {
            using (var inputForm = new Form())
            {
                inputForm.Text = "Save Audio Profile";
                inputForm.Width = 300;
                inputForm.Height = 120;
                inputForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                inputForm.StartPosition = FormStartPosition.CenterParent;
                inputForm.MaximizeBox = false;
                inputForm.MinimizeBox = false;

                var label = new Label { Left = 10, Top = 10, Text = "Profile Name:", Width = 270 };
                var textBox = new TextBox { Left = 10, Top = 30, Width = 260, Text = "My Profile" };
                var okButton = new Button { Text = "OK", Left = 110, Width = 70, Top = 60, DialogResult = DialogResult.OK };
                var cancelButton = new Button { Text = "Cancel", Left = 190, Width = 70, Top = 60, DialogResult = DialogResult.Cancel };

                inputForm.Controls.Add(label);
                inputForm.Controls.Add(textBox);
                inputForm.Controls.Add(okButton);
                inputForm.Controls.Add(cancelButton);
                inputForm.AcceptButton = okButton;
                inputForm.CancelButton = cancelButton;

                if (inputForm.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(textBox.Text))
                    return;

                string profileName = textBox.Text;

                try
                {
                    var profile = Media.AudioProfile.FromMediaManager(instance.MediaManager, profileName);
                    profile.MusicVolume = volAudioStream.Value / 50f;

                    // Add to combo box
                    cmbAudioProfile.Items.Add(profile);
                    cmbAudioProfile.SelectedItem = profile;

                    // Save to settings
                    var profilesArray = new OSDArray();
                    if (s["audio_profiles"].Type == OSDType.Array)
                    {
                        profilesArray = (OSDArray)s["audio_profiles"];
                    }

                    profilesArray.Add(profile.ToOSD());
                    s["audio_profiles"] = profilesArray;

                    Logger.Info($"Saved audio profile: {profileName}");
                    instance.ShowNotificationInChat($"Saved audio profile: {profileName}", ChatBufferTextStyle.Normal);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error saving profile: {profileName}", ex);
                    MessageBox.Show(
                        $"Error saving profile: {ex.Message}",
                        "Profile Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void cmbAudioDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbAudioDevice.SelectedIndex < 0 || !instance.MediaManager.SoundSystemAvailable)
                return;

            try
            {
                var selectedDriver = cmbAudioDevice.SelectedItem as Media.AudioDriverInfo;
                if (selectedDriver != null && selectedDriver.Index != instance.MediaManager.SelectedDriver)
                {
                    if (instance.MediaManager.SetAudioDriver(selectedDriver.Index))
                    {
                        Logger.Info($"Successfully switched to audio driver: {selectedDriver.Name}");
                        configTimer.Change(saveConfigTimeout, System.Threading.Timeout.Infinite);
                    }
                    else
                    {
                        // Revert selection if failed
                        Logger.Warn($"Failed to switch to audio driver: {selectedDriver.Name}");
                        cmbAudioDevice.SelectedIndex = instance.MediaManager.SelectedDriver;
                        MessageBox.Show(
                            $"Failed to switch to audio driver: {selectedDriver.Name}\nCheck the log for details.",
                            "Audio Device Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Error changing audio device", ex);
            }
        }

        private void btnRefreshDevices_Click(object sender, EventArgs e)
        {
            PopulateAudioDevices();
            UpdateAudioDeviceStatus();
        }

        private void btnRetryInit_Click(object sender, EventArgs e)
        {
            btnRetryInit.Enabled = false;
            btnRetryInit.Text = "Retrying...";

            try
            {
                if (instance.MediaManager.RetryInitialization())
                {
                    // Success! UI will be updated by the event handler
                    // MediaManager_SoundSystemAvailableChanged will handle the UI refresh
                    
                    MessageBox.Show(
                        "Sound system successfully initialized!",
                        "Sound System",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Failed to initialize sound system. Check the log for details.",
                        "Sound System Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Error retrying sound initialization", ex);
                MessageBox.Show(
                    $"Error retrying sound initialization: {ex.Message}",
                    "Sound System Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnRetryInit.Text = "Retry Init";
                UpdateAudioDeviceStatus();
            }
        }

        private void btnShowStats_Click(object sender, EventArgs e)
        {
            try
            {
                var perfInfo = instance.MediaManager.GetPerformanceInfo();
                
                string statsMessage = "=== Audio Performance Statistics ===\n\n" +
                    $"Sound System: {(perfInfo.SoundSystemAvailable ? "Available" : "Not Available")}\n" +
                    $"Audio Drivers: {perfInfo.DriverCount} detected\n" +
                    $"Selected Driver: {perfInfo.SelectedDriver}\n\n" +
                    $"Active Channels: {perfInfo.ChannelsPlaying} / {perfInfo.RealChannels}\n\n" +
                    $"CPU Usage:\n" +
                    $"  DSP: {perfInfo.DSPUsage:F2}%\n" +
                    $"  Streaming: {perfInfo.StreamUsage:F2}%\n" +
                    $"  Geometry: {perfInfo.GeometryUsage:F2}%\n" +
                    $"  Update: {perfInfo.UpdateUsage:F2}%\n" +
                    $"  Total: {perfInfo.TotalUsage:F2}%\n\n" +
                    $"Queue Statistics:\n" +
                    $"  {perfInfo.Stats}";

                MessageBox.Show(
                    statsMessage,
                    "Audio Performance Statistics",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Warn("Error displaying audio stats", ex);
                MessageBox.Show(
                    $"Error retrieving audio statistics: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        #endregion GUI event handlers
    }
}
