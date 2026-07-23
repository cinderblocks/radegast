/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.StructuredData;
using Radegast.Media;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class MediaViewModel : InstanceViewModelBase, IDisposable
{
    private MediaManager Manager => _instance.MediaManager;
    private Settings Settings => _instance.GlobalSettings;

    private Stream? _parcelStream;
    private readonly object _parcelMusicLock = new();
    private bool _playing;
    private bool _userStopped;
    private string _currentURL = string.Empty;
    private string _lastArtistTag = string.Empty;

    private Timer? _configTimer;
    private const int SaveConfigTimeout = 1000;

    // --- Parcel streaming ---

    [ObservableProperty]
    private string _streamUrl = string.Empty;

    [ObservableProperty]
    private string _stationName = string.Empty;

    [ObservableProperty]
    private string _songTitle = string.Empty;

    [ObservableProperty]
    private bool _autoPlay;

    [ObservableProperty]
    private bool _keepUrl;

    [ObservableProperty]
    private bool _isPlaying;

    // --- Volumes (0–100 integer for slider binding) ---

    [ObservableProperty]
    private int _streamVolume = 20;

    [ObservableProperty]
    private int _objectVolume = 50;

    [ObservableProperty]
    private int _uiVolume = 50;

    [ObservableProperty]
    private int _masterVolume = 100;

    // --- Toggles ---

    [ObservableProperty]
    private bool _objectSoundsEnabled = true;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _mediaMetadataNotificationsEnabled;

    // --- Sound system status ---

    [ObservableProperty]
    private string _soundSystemStatus = "Initializing…";

    [ObservableProperty]
    private bool _soundSystemAvailable;

    // --- Audio devices ---

    public ObservableCollection<AudioDriverInfo> AudioDevices { get; } = [];

    [ObservableProperty]
    private AudioDriverInfo? _selectedAudioDevice;

    // --- Audio profiles ---

    public ObservableCollection<AudioProfile> AudioProfiles { get; } = [];

    [ObservableProperty]
    private AudioProfile? _selectedProfile;

    public MediaViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        RestoreSettings();

        _configTimer = new Timer(SaveConfig, null, Timeout.Infinite, Timeout.Infinite);

        // Initialize FMOD
        Manager.Initialize();

        SoundSystemAvailable = Manager.SoundSystemAvailable;
        UpdateSoundSystemStatus();

        Manager.SoundSystemAvailableChanged += OnSoundSystemAvailableChanged;
        Manager.AudioDevicesChanged += OnAudioDevicesChanged;

        Client.Parcels.ParcelProperties += OnParcelProperties;

        PopulateAudioDevices();
        PopulateAudioProfiles();
    }

    private void RestoreSettings()
    {
        var s = Settings;

        if (s["parcel_audio_url"].Type != OSDType.Unknown)
            StreamUrl = s["parcel_audio_url"].AsString();
        if (s["parcel_audio_vol"].Type != OSDType.Unknown)
            StreamVolume = (int)(s["parcel_audio_vol"].AsReal() * 100);
        if (s["parcel_audio_play"].Type != OSDType.Unknown)
            AutoPlay = s["parcel_audio_play"].AsBoolean();
        if (s["parcel_audio_keep_url"].Type != OSDType.Unknown)
            KeepUrl = s["parcel_audio_keep_url"].AsBoolean();
        if (s["object_audio_enable"].Type != OSDType.Unknown)
            ObjectSoundsEnabled = s["object_audio_enable"].AsBoolean();
        if (s["object_audio_vol"].Type != OSDType.Unknown)
        {
            var vol = (float)s["object_audio_vol"].AsReal();
            Manager.ObjectVolume = vol;
            ObjectVolume = (int)(vol * 100);
        }
        if (s["ui_audio_vol"].Type != OSDType.Unknown)
        {
            var vol = (float)s["ui_audio_vol"].AsReal();
            Manager.UIVolume = vol;
            UiVolume = (int)(vol * 100);
        }
        if (s["master_volume"].Type != OSDType.Unknown)
        {
            var vol = (float)s["master_volume"].AsReal();
            Manager.MasterVolume = vol;
            MasterVolume = (int)(vol * 100);
        }
        if (s["audio_driver_index"].Type != OSDType.Unknown)
        {
            Manager.PreferredDriver = s["audio_driver_index"].AsInteger();
        }
        if (s["media_metadata_notifications"].Type != OSDType.Unknown)
            MediaMetadataNotificationsEnabled = s["media_metadata_notifications"].AsBoolean();

        Manager.ObjectEnable = ObjectSoundsEnabled;
    }

    private void ScheduleConfigSave()
    {
        _configTimer?.Change(SaveConfigTimeout, Timeout.Infinite);
    }

    private void SaveConfig(object? state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var s = Settings;
            s["parcel_audio_url"] = OSD.FromString(StreamUrl);
            s["parcel_audio_vol"] = OSD.FromReal(StreamVolume / 100.0);
            s["parcel_audio_play"] = OSD.FromBoolean(AutoPlay);
            s["parcel_audio_keep_url"] = OSD.FromBoolean(KeepUrl);
            s["object_audio_vol"] = OSD.FromReal(ObjectVolume / 100.0);
            s["object_audio_enable"] = OSD.FromBoolean(ObjectSoundsEnabled);
            s["ui_audio_vol"] = OSD.FromReal(UiVolume / 100.0);
            s["master_volume"] = OSD.FromReal(MasterVolume / 100.0);

            s["media_metadata_notifications"] = OSD.FromBoolean(MediaMetadataNotificationsEnabled);

            if (SelectedAudioDevice != null && SoundSystemAvailable)
            {
                s["audio_driver_index"] = OSD.FromInteger(SelectedAudioDevice.Index);
            }
        });
    }

    // --- Property change handlers ---

    partial void OnStreamVolumeChanged(int value)
    {
        lock (_parcelMusicLock)
        {
            _parcelStream?.Volume = value / 100f;
        }
        ScheduleConfigSave();
    }

    partial void OnObjectVolumeChanged(int value)
    {
        Manager.ObjectVolume = value / 100f;
        ScheduleConfigSave();
    }

    partial void OnUiVolumeChanged(int value)
    {
        Manager.UIVolume = value / 100f;
        ScheduleConfigSave();
    }

    partial void OnMasterVolumeChanged(int value)
    {
        Manager.MasterVolume = value / 100f;
        ScheduleConfigSave();
    }

    partial void OnObjectSoundsEnabledChanged(bool value)
    {
        Manager.ObjectEnable = value;
        ScheduleConfigSave();
    }

    partial void OnIsMutedChanged(bool value)
    {
        Manager.MuteAll = value;
    }

    partial void OnAutoPlayChanged(bool value)
    {
        ScheduleConfigSave();
    }

    partial void OnKeepUrlChanged(bool value)
    {
        ScheduleConfigSave();
    }

    partial void OnMediaMetadataNotificationsEnabledChanged(bool value)
    {
        ScheduleConfigSave();
    }

    partial void OnStreamUrlChanged(string value)
    {
        ScheduleConfigSave();
    }

    partial void OnSelectedAudioDeviceChanged(AudioDriverInfo? value)
    {
        ScheduleConfigSave();
    }

    // --- Commands ---

    [RelayCommand]
    private void PlayStream()
    {
        lock (_parcelMusicLock)
        {
            if (_playing) return;
            _currentURL = StreamUrl;
            Play();
        }
    }

    [RelayCommand]
    private void StopStream()
    {
        lock (_parcelMusicLock)
        {
            if (!_playing) return;
            _currentURL = string.Empty;
            Stop();
            _userStopped = true;
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (SelectedProfile == null) return;

        try
        {
            SelectedProfile.ApplyTo(Manager);

            MasterVolume = (int)(SelectedProfile.MasterVolume * 100);
            ObjectVolume = (int)(SelectedProfile.ObjectVolume * 100);
            UiVolume = (int)(SelectedProfile.UIVolume * 100);
            StreamVolume = (int)(SelectedProfile.MusicVolume * 100);
            ObjectSoundsEnabled = SelectedProfile.ObjectSoundsEnabled;

            ScheduleConfigSave();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error loading profile: {SelectedProfile.Name}", ex);
        }
    }

    [RelayCommand]
    private void RetryInitialize()
    {
        Manager.Initialize();
        SoundSystemAvailable = Manager.SoundSystemAvailable;
        UpdateSoundSystemStatus();
        PopulateAudioDevices();
    }

    // --- Streaming audio playback ---

    /// <summary>Stops any current stream and immediately starts playing <paramref name="url"/>.
    /// Called externally, e.g. from the Land Profile panel.</summary>
    public void PlayUrl(string url)
    {
        lock (_parcelMusicLock)
        {
            StreamUrl = url;
            _currentURL = url;
            Play();
        }
    }

    private void Play()
    {
        Stop();
        _playing = true;
        _userStopped = false;
        IsPlaying = true;
        _parcelStream = new Stream { Volume = StreamVolume / 100f };
        _parcelStream.OnStreamInfo += OnStreamInfo;
        _parcelStream.PlayStream(_currentURL);
    }

    private void Stop()
    {
        _playing = false;
        IsPlaying = false;
        _parcelStream?.Dispose();
        _parcelStream = null;
        StationName = string.Empty;
        SongTitle = string.Empty;
        _lastArtistTag = string.Empty;
    }

    private void OnStreamInfo(object? sender, StreamInfoArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.Key)
            {
                case "artist":
                    _lastArtistTag = e.Value;
                    break;
                case "title":
                    // Replace (never append) so each new track fully overwrites the last —
                    // otherwise every song change concatenates onto the previous title forever.
                    SongTitle = string.IsNullOrEmpty(_lastArtistTag)
                        ? e.Value
                        : $"{_lastArtistTag} - {e.Value}";
                    if (MediaMetadataNotificationsEnabled && !string.IsNullOrEmpty(SongTitle))
                        VelesNotificationService.Show("Now Playing", SongTitle);
                    break;
                case "icy-name":
                    StationName = e.Value;
                    break;
            }
        });
    }

    // --- Parcel properties handler ---

    private void OnParcelProperties(object? sender, ParcelPropertiesEventArgs e)
    {
        if (KeepUrl || e.Result != ParcelResult.Single) return;

        Dispatcher.UIThread.Post(() =>
        {
            lock (_parcelMusicLock)
            {
                StreamUrl = e.Parcel.MusicURL;
                if (_playing)
                {
                    if (_currentURL != StreamUrl)
                    {
                        _currentURL = StreamUrl;
                        Play();
                    }
                }
                else if (AutoPlay && !_userStopped)
                {
                    _currentURL = StreamUrl;
                    Play();
                }
            }
        });
    }

    // --- Sound system events ---

    private void OnSoundSystemAvailableChanged(object? sender, SoundSystemAvailableEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SoundSystemAvailable = e.IsAvailable;
            UpdateSoundSystemStatus();
            PopulateAudioDevices();
        });
    }

    private void OnAudioDevicesChanged(object? sender, AudioDevicesChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PopulateAudioDevices();
        });
    }

    private void UpdateSoundSystemStatus()
    {
        if (SoundSystemAvailable)
            SoundSystemStatus = $"Sound system ready ({Manager.DriverCount} device(s))";
        else
            SoundSystemStatus = "Sound system not available";
    }

    private void PopulateAudioDevices()
    {
        AudioDevices.Clear();
        if (!SoundSystemAvailable) return;

        try
        {
            var drivers = Manager.GetAudioDrivers();
            foreach (var driver in drivers)
            {
                AudioDevices.Add(driver);
            }

            int current = Manager.SelectedDriver;
            if (current >= 0 && current < AudioDevices.Count)
                SelectedAudioDevice = AudioDevices[current];
            else if (AudioDevices.Count > 0)
                SelectedAudioDevice = AudioDevices[0];
        }
        catch (Exception ex)
        {
            Logger.Warn("Failed to populate audio devices", ex);
        }
    }

    private void PopulateAudioProfiles()
    {
        AudioProfiles.Clear();
        foreach (var profile in MediaManager.GetPredefinedProfiles())
        {
            AudioProfiles.Add(profile);
        }

        if (Settings["audio_profiles"].Type == OSDType.Array)
        {
            var arr = (OSDArray)Settings["audio_profiles"];
            foreach (var osd in arr)
            {
                var profile = AudioProfile.FromOSD(osd);
                if (profile != null)
                    AudioProfiles.Add(profile);
            }
        }

        if (AudioProfiles.Count > 0)
            SelectedProfile = AudioProfiles[0];
    }

    public void Dispose()
    {
        Manager.SoundSystemAvailableChanged -= OnSoundSystemAvailableChanged;
        Manager.AudioDevicesChanged -= OnAudioDevicesChanged;
        Client.Parcels.ParcelProperties -= OnParcelProperties;

        lock (_parcelMusicLock)
        {
            Stop();
        }

        if (_configTimer != null)
        {
            _configTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _configTimer.Dispose();
            _configTimer = null;
        }
    }
}
