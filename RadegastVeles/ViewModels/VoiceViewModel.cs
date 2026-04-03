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
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse.Voice.WebRTC;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Manages WebRTC voice for the current session.
/// Created once per login in <see cref="MainViewModel"/> and exposed through
/// <see cref="NearbyViewModel.Voice"/> so <see cref="Views.ChatPanel"/> can bind to it.
/// </summary>
public partial class VoiceViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private VoiceManager? _voice;

    /// <summary>True when the SDL3 audio backend initialised successfully.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectLabel))]
    [NotifyPropertyChangedFor(nameof(IsVisibleInUI))]
    private bool _isAvailable;

    /// <summary>True while a voice session is active.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectLabel))]
    [NotifyPropertyChangedFor(nameof(PushToTalkButtonVisible))]
    [NotifyPropertyChangedFor(nameof(MicMuteButtonVisible))]
    private bool _isConnected;

    /// <summary>True when the local microphone is muted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicIcon))]
    private bool _isMicMuted;

    /// <summary>Human-readable status shown in the voice toolbar.</summary>
    [ObservableProperty]
    private string _statusText = "Not connected";

    /// <summary>User-level on/off switch for voice. When false the UI is hidden and voice is disconnected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleInUI))]
    private bool _voiceEnabled = true;

    /// <summary>When true, the mic is always muted and the PTT button must be held to speak.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PushToTalkButtonVisible))]
    [NotifyPropertyChangedFor(nameof(MicMuteButtonVisible))]
    private bool _pushToTalkEnabled = true;

    /// <summary>True while the PTT button is held and mic is live.</summary>
    [ObservableProperty]
    private bool _isPushToTalking;

    /// <summary>Live microphone input level, 0–1 (RMS). Updates ~20fps while recording.</summary>
    [ObservableProperty]
    private float _micLevel;

    /// <summary>True while the mic-test recording is running in the Preferences panel.</summary>
    [ObservableProperty]
    private bool _isMicTestActive;

    public string ConnectLabel => IsConnected ? "Leave Voice" : "Join Voice";
    public string MicIcon      => IsMicMuted  ? "🔇"          : "🎤";

    /// <summary>True when voice hardware is available AND the user has voice enabled.</summary>
    public bool IsVisibleInUI          => IsAvailable && VoiceEnabled;
    /// <summary>PTT button shown when connected and PTT mode is on.</summary>
    public bool PushToTalkButtonVisible => IsConnected && PushToTalkEnabled;
    /// <summary>Mute toggle shown when connected and PTT mode is off.</summary>
    public bool MicMuteButtonVisible    => IsConnected && !PushToTalkEnabled;

    // ── Persistent settings ───────────────────────────────────────────────

    /// <summary>Automatically connect to parcel voice on region change.</summary>
    [ObservableProperty]
    private bool _autoConnect;

    /// <summary>Speaker/playback volume (0–100). Mapped to AudioDevice.SpeakerLevel.</summary>
    [ObservableProperty]
    private int _outputVolume = 80;

    /// <summary>Selected microphone device name. Empty string = default device.</summary>
    [ObservableProperty]
    private string _selectedInputDevice = string.Empty;

    /// <summary>Available microphone input devices on this machine.</summary>
    public ObservableCollection<string> InputDevices { get; } = [];

    /// <summary>Avatars currently in the same voice channel.</summary>
    public ObservableCollection<VoiceParticipant> Participants { get; } = [];

    private float _micLevelSmooth;
    private long _lastMicLevelTick;
    private bool _micTestStartedRecording;

    public VoiceViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        try
        {
            _voice = new VoiceManager(instance.Client);
            IsAvailable = _voice.AudioDevice.IsAvailable;
            if (IsAvailable)
            {
                WireEvents();
                LoadSettings();
                RefreshInputDevices();
            }
            else StatusText = "Voice: audio device unavailable";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            StatusText   = $"Voice unavailable: {ex.Message}";
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────

    internal void LoadSettings()
    {
        var s = _instance.GlobalSettings;
        VoiceEnabled     = s["voice_enabled"].Type == OSDType.Unknown || s["voice_enabled"].AsBoolean();
        PushToTalkEnabled = s["voice_push_to_talk"].Type == OSDType.Unknown || s["voice_push_to_talk"].AsBoolean();
        AutoConnect      = s["voice_auto_connect"].Type != OSDType.Unknown && s["voice_auto_connect"].AsBoolean();
        OutputVolume = s["voice_output_volume"].Type != OSDType.Unknown ? s["voice_output_volume"].AsInteger() : 80;
        var savedDevice = s["voice_input_device"].AsString();
        SelectedInputDevice = string.IsNullOrEmpty(savedDevice) ? "(Default)" : savedDevice;

        if (_voice != null)
        {
            _voice.AudioDevice.SpeakerLevel = OutputVolume / 100.0f;
            if (SelectedInputDevice != "(Default)")
                _voice.AudioDevice.SetRecordingDevice(SelectedInputDevice);
        }
    }

    internal void SaveSettings()
    {
        var s = _instance.GlobalSettings;
        s["voice_enabled"]       = OSD.FromBoolean(VoiceEnabled);
        s["voice_push_to_talk"]  = OSD.FromBoolean(PushToTalkEnabled);
        s["voice_auto_connect"]  = OSD.FromBoolean(AutoConnect);
        s["voice_output_volume"] = OSD.FromInteger(OutputVolume);
        var deviceToSave = SelectedInputDevice == "(Default)" ? string.Empty : SelectedInputDevice;
        s["voice_input_device"]  = OSD.FromString(deviceToSave);

        if (_voice != null)
        {
            _voice.AudioDevice.SpeakerLevel = OutputVolume / 100.0f;
            var device = SelectedInputDevice == "(Default)" ? null : SelectedInputDevice;
            _voice.AudioDevice.SetRecordingDevice(device);
        }
    }

    internal void RefreshInputDevices()
    {
        InputDevices.Clear();
        InputDevices.Add("(Default)");
        if (_voice == null) return;
        foreach (var kv in _voice.AudioDevice.GetRecordingDevices())
            InputDevices.Add(kv.Value);
    }

    // ── Public API used by other ViewModels ───────────────────────────────

    internal async Task JoinGroupVoiceAsync(UUID groupId)
    {
        if (_voice == null) return;
        try { await _voice.JoinGroupVoice(groupId); }
        catch (Exception ex) { StatusText = $"Group voice failed: {ex.Message}"; }
    }

    internal async Task LeaveGroupVoiceAsync(UUID groupId)
    {
        if (_voice == null) return;
        try { await _voice.LeaveGroupVoice(groupId); }
        catch { }
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleConnect()
    {
        if (IsConnected) Disconnect();
        else await Connect();
    }

    [RelayCommand]
    private void ToggleMic()
    {
        if (_voice == null || !IsConnected) return;
        IsMicMuted = !IsMicMuted;
        _voice.AudioDevice.MicMute = IsMicMuted;
    }

    /// <summary>Starts or stops a temporary microphone recording used only for level testing in Preferences.</summary>
    [RelayCommand]
    private void ToggleMicTest()
    {
        if (_voice == null || !IsAvailable) return;
        if (IsMicTestActive)
        {
            IsMicTestActive = false;
            if (_micTestStartedRecording)
            {
                _voice.AudioDevice.StopRecording();
                _micTestStartedRecording = false;
            }
            _micLevelSmooth = 0f;
            MicLevel = 0f;
        }
        else
        {
            IsMicTestActive = true;
            if (!_voice.AudioDevice.RecordingActive)
            {
                _voice.AudioDevice.StartRecording();
                _micTestStartedRecording = true;
            }
        }
    }

    /// <summary>Called when the user flips the master voice on/off toggle.</summary>
    partial void OnVoiceEnabledChanged(bool value)
    {
        if (!value)
        {
            if (IsMicTestActive) ToggleMicTest();
            if (IsConnected) Disconnect();
        }
    }

    /// <summary>Called when PTT mode is toggled. Adjusts mic state immediately if connected.</summary>
    partial void OnPushToTalkEnabledChanged(bool value)
    {
        if (!IsConnected || _voice == null) return;
        if (value)
        {
            // PTT on: mute mic until button is held
            IsPushToTalking = false;
            IsMicMuted = true;
            _voice.AudioDevice.MicMute = true;
        }
        else
        {
            // PTT off: go back to always-on mic (unmute)
            IsPushToTalking = false;
            IsMicMuted = false;
            _voice.AudioDevice.MicMute = false;
        }
    }

    /// <summary>Called by the UI's PointerPressed event on the PTT button. Unmutes the mic.</summary>
    public void StartPushToTalk()
    {
        if (_voice == null || !IsConnected || !PushToTalkEnabled || IsPushToTalking) return;
        IsPushToTalking = true;
        IsMicMuted = false;
        _voice.AudioDevice.MicMute = false;
    }

    /// <summary>Called by the UI's PointerReleased event on the PTT button. Re-mutes the mic.</summary>
    public void StopPushToTalk()
    {
        if (!IsPushToTalking) return;
        IsPushToTalking = false;
        IsMicMuted = true;
        if (_voice != null) _voice.AudioDevice.MicMute = true;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task Connect()
    {
        if (_voice == null || !IsAvailable) return;
        StatusText = "Connecting...";
        try
        {
            bool ok = await _voice.ConnectPrimaryRegion();
            if (!ok)
            {
                StatusText = "Failed to connect";
                IsConnected = false;
            }
        }
        catch (Exception ex)
        {
            StatusText  = $"Error: {ex.Message}";
            IsConnected = false;
        }
    }

    private void Disconnect()
    {
        _voice?.Disconnect();
        IsConnected    = false;
        IsMicMuted     = false;
        IsPushToTalking = false;
        Participants.Clear();
        StatusText = "Disconnected";
    }

    private void WireEvents()
    {
        if (_voice == null) return;
        _voice.PeerConnectionReady          += OnConnectionReady;
        _voice.PeerConnectionClosed         += OnConnectionClosed;
        _voice.OnRegionTransitionCompleted  += OnRegionTransitionCompleted;
        _voice.OnRegionTransitionFailed     += OnRegionTransitionFailed;
        _voice.PeerJoined                   += OnPeerJoined;
        _voice.PeerLeft                     += OnPeerLeft;
        _voice.PeerAudioUpdated             += OnPeerAudioUpdated;
        _voice.OnP2PCallIncoming            += OnP2PCallIncoming;
        _voice.OnGroupVoiceJoined           += OnGroupVoiceJoined;
        _voice.AudioDevice.OnAudioSourceEncodedSample += OnEncodedSampleReceived;
    }

    private void UnwireEvents()
    {
        if (_voice == null) return;
        _voice.PeerConnectionReady          -= OnConnectionReady;
        _voice.PeerConnectionClosed         -= OnConnectionClosed;
        _voice.OnRegionTransitionCompleted  -= OnRegionTransitionCompleted;
        _voice.OnRegionTransitionFailed     -= OnRegionTransitionFailed;
        _voice.PeerJoined                   -= OnPeerJoined;
        _voice.PeerLeft                     -= OnPeerLeft;
        _voice.PeerAudioUpdated             -= OnPeerAudioUpdated;
        _voice.OnP2PCallIncoming            -= OnP2PCallIncoming;
        _voice.OnGroupVoiceJoined           -= OnGroupVoiceJoined;
        _voice.AudioDevice.OnAudioSourceEncodedSample -= OnEncodedSampleReceived;
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void OnConnectionReady()
        => Dispatcher.UIThread.Post(() =>
        {
            IsConnected = true;
            StatusText  = "Connected";
            // In PTT mode, start with mic muted
            if (PushToTalkEnabled && _voice != null)
            {
                IsMicMuted = true;
                _voice.AudioDevice.MicMute = true;
            }
        });

    private void OnConnectionClosed()
        => Dispatcher.UIThread.Post(() =>
        {
            IsConnected    = false;
            IsMicMuted     = false;
            IsPushToTalking = false;
            Participants.Clear();
            StatusText = "Disconnected";
        });

    private void OnRegionTransitionCompleted()
        => Dispatcher.UIThread.Post(async () =>
        {
            // Participants from the old region are stale
            Participants.Clear();
            IsConnected = false;
            StatusText  = "Reconnecting...";
            if (AutoConnect)
                await Connect();
            // PeerConnectionReady will set IsConnected = true when the new session is ready
        });

    private void OnRegionTransitionFailed(Exception ex)
        => Dispatcher.UIThread.Post(() =>
        {
            IsConnected = false;
            StatusText  = $"Voice failed: {ex.Message}";
        });

    private void OnPeerJoined(UUID peerId)
    {
        var name = _instance.Names.Get(peerId);
        Dispatcher.UIThread.Post(() =>
        {
            if (!TryGetParticipant(peerId, out _))
                Participants.Add(new VoiceParticipant(peerId, name));
        });
    }

    private void OnPeerLeft(UUID peerId)
        => Dispatcher.UIThread.Post(() =>
        {
            for (int i = Participants.Count - 1; i >= 0; i--)
            {
                if (Participants[i].Id == peerId)
                {
                    Participants.RemoveAt(i);
                    break;
                }
            }
        });

    private void OnPeerAudioUpdated(UUID peerId, VoiceSession.PeerAudioState state)
    {
        bool speaking = state.VoiceActive ?? false;
        Dispatcher.UIThread.Post(() =>
        {
            if (TryGetParticipant(peerId, out var p))
                p!.IsSpeaking = speaking;
        });
    }

    private void OnP2PCallIncoming(UUID callerId)
    {
        var name = _instance.Names.Get(callerId);
        var vm = NotificationViewModel.ForGenericMessage(
            "Incoming Voice Call",
            $"{name} is calling you.");
        Dispatcher.UIThread.Post(() =>
            _instance.RaiseNotification(vm));
    }

    private void OnGroupVoiceJoined(UUID groupId)
        => Dispatcher.UIThread.Post(() => IsConnected = true);

    private void OnEncodedSampleReceived(uint durationRtpUnits, byte[] sample)
    {
        // Opus VBR: encoded frame size loosely correlates with audio amplitude.
        // Typical speech frame at default bitrate: ~20–80 bytes. Silent/DTX: ~3–5 bytes.
        const float maxExpected = 80f;
        float level = Math.Min(1.0f, sample.Length / maxExpected);
        // Smooth toward new value
        _micLevelSmooth = _micLevelSmooth * 0.6f + level * 0.4f;
        // Throttle UI posts to ~20fps
        var now = Environment.TickCount64;
        if (now - _lastMicLevelTick < 50) return;
        _lastMicLevelTick = now;
        var l = _micLevelSmooth;
        Dispatcher.UIThread.Post(() => MicLevel = l);
    }

    private bool TryGetParticipant(UUID id, out VoiceParticipant? participant)
    {
        foreach (var p in Participants)
        {
            if (p.Id == id)
            {
                participant = p;
                return true;
            }
        }
        participant = null;
        return false;
    }

    public void Dispose()
    {
        if (IsMicTestActive) ToggleMicTest();
        UnwireEvents();
        try { _voice?.Disconnect(); } catch { }
        _voice = null;
    }
}

/// <summary>A single avatar participating in voice.</summary>
public partial class VoiceParticipant : ObservableObject
{
    public UUID Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeakingIcon))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeakingIcon))]
    private bool _isSpeaking;

    [ObservableProperty]
    private bool _isMuted;

    public string SpeakingIcon => IsSpeaking ? "🔊" : "   ";

    public VoiceParticipant(UUID id, string name)
    {
        Id    = id;
        _name = name;
    }
}
