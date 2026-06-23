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
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using LibreMetaverse.StructuredData;
using Radegast.Veles.Core;
using Radegast.Veles.Voice;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Manages the voice-synthesis (text-to-speech → WebRTC voice injection) feature.
/// Owned by <see cref="VoiceViewModel"/> and exposed as <c>Voice.VoiceSynth</c>.
/// </summary>
public sealed partial class VoiceSynthViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly VoiceSynthService _service;
    private readonly VoiceViewModel _voiceVm;

    /// <summary>The underlying VoiceSynthService, exposed for plugin API access and voice wiring.</summary>
    public VoiceSynthService Service => _service;

    // ── Observable state ──────────────────────────────────────────────────

    /// <summary>Master switch: voice-synth feature on/off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSpeak))]
    private bool _voiceSynthEnabled;

    /// <summary>Path to the directory that contains the Piper model files.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelStatusText))]
    private string _modelDirectory = string.Empty;

    /// <summary>True while a model is loaded and TTS is enabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSpeak))]
    [NotifyPropertyChangedFor(nameof(ModelStatusText))]
    private bool _modelLoaded;

    /// <summary>True while the model is being loaded from disk.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelStatusText))]
    private bool _modelLoading;

    /// <summary>Speaker ID for multi-speaker Piper models (0-based).</summary>
    [ObservableProperty]
    private int _speakerId;

    /// <summary>Speech speed multiplier (0.5 – 2.0; 1.0 = normal).</summary>
    [ObservableProperty]
    private double _speed = 1.0;

    /// <summary>Text typed in the manual TTS test box in Preferences.</summary>
    [ObservableProperty]
    private string _testText = "Hello! Text to speech is working.";

    /// <summary>True when outbound nearby chat messages should be synthesised into parcel voice.</summary>
    [ObservableProperty]
    private bool _speakOutboundChat;

    /// <summary>True when voice synth is ready and enabled.</summary>
    public bool CanSpeak => VoiceSynthEnabled && ModelLoaded;

    public string ModelStatusText => ModelLoading ? "Loading model…"
        : ModelLoaded  ? "Model ready"
        : string.IsNullOrWhiteSpace(ModelDirectory) ? "No model configured"
        : "Model not loaded";

    // ── Construction ──────────────────────────────────────────────────────

    /// <summary>
    /// Default directory for downloaded Piper voice models.
    /// Resolves to %AppData%\Radegast\Voices (or platform equivalent).
    /// </summary>
    public string VoicesDirectory => System.IO.Path.Combine(_instance.UserDir, "Voices");

    public VoiceSynthViewModel(RadegastInstanceAvalonia instance, VoiceViewModel voiceVm)
    {
        _instance = instance;
        _voiceVm  = voiceVm;

        var logFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });
        _service = new VoiceSynthService(logFactory.CreateLogger<VoiceSynthService>());
        _service.ReadyChanged += OnServiceReadyChanged;

        LoadSettings();
    }

    // ── Settings ──────────────────────────────────────────────────────────

    internal void LoadSettings()
    {
        var s = _instance.GlobalSettings;
        VoiceSynthEnabled             = s["voice_synth_enabled"].AsBoolean();
        ModelDirectory                = s["voice_synth_model_dir"].AsString();
        if (string.IsNullOrWhiteSpace(ModelDirectory))
            ModelDirectory            = VoicesDirectory;
        SpeakerId                     = s["voice_synth_speaker_id"].Type != OSDType.Unknown ? s["voice_synth_speaker_id"].AsInteger() : 0;
        Speed                         = s["voice_synth_speed"].Type != OSDType.Unknown ? s["voice_synth_speed"].AsReal() : 1.0;
        SpeakOutboundChat             = s["voice_synth_speak_outbound"].AsBoolean();

        if (VoiceSynthEnabled && !string.IsNullOrWhiteSpace(ModelDirectory))
            _ = LoadModel();
    }

    internal void SaveSettings()
    {
        var s = _instance.GlobalSettings;
        s["voice_synth_enabled"]               = OSD.FromBoolean(VoiceSynthEnabled);
        s["voice_synth_model_dir"]             = OSD.FromString(ModelDirectory);
        s["voice_synth_speaker_id"]            = OSD.FromInteger(SpeakerId);
        s["voice_synth_speed"]                 = OSD.FromReal(Speed);
        s["voice_synth_speak_outbound"] = OSD.FromBoolean(SpeakOutboundChat);

        _service.UpdateParameters(SpeakerId, (float)Speed);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadModel()
    {
        if (string.IsNullOrWhiteSpace(ModelDirectory) || ModelLoading) return;
        ModelLoading = true;
        ModelLoaded  = false;
        await _service.LoadModelAsync(ModelDirectory, SpeakerId, (float)Speed);
        // ModelLoaded / ModelLoading are updated via OnServiceReadyChanged
    }

    [RelayCommand]
    private void UnloadModel()
    {
        _service.UnloadModel();
        // ModelLoaded updated via OnServiceReadyChanged
    }

    [RelayCommand]
    private void SpeakTest()
    {
        if (!CanSpeak || string.IsNullOrWhiteSpace(TestText)) return;
        _service.Speak(TestText);
    }

    // ── Public surface used by VoiceViewModel / chat ─────────────────────

    /// <summary>Called by VoiceViewModel when a voice session connects.</summary>
    internal void OnVoiceConnected()
    {
        if (_voiceVm._voice != null)
            _service.AttachVoiceSession(_voiceVm._voice.AudioDevice);
    }

    /// <summary>Called by VoiceViewModel when a voice session closes.</summary>
    internal void OnVoiceDisconnected()
    {
        _service.DetachVoiceSession();
    }

    /// <summary>
    /// Synthesise <paramref name="message"/> into the active voice session.
    /// Used for outbound nearby chat (parcel channel) and outbound P2P/conference IMs.
    /// Does nothing if TTS is not ready or <see cref="SpeakOutboundChat"/> is false.
    /// </summary>
    public void SpeakOutbound(string message)
    {
        if (!CanSpeak || !SpeakOutboundChat || string.IsNullOrWhiteSpace(message)) return;
        _service.Speak(message);
    }

    /// <summary>Directly speak arbitrary text (used from commands etc.).</summary>
    public bool Speak(string text)
    {
        if (!CanSpeak) return false;
        return _service.Speak(text);
    }

    /// <summary>Set the model directory and reload the model if voice synth is enabled.</summary>
    public void SetModelDirectory(string dir)
    {
        ModelDirectory = dir;
        if (VoiceSynthEnabled && !string.IsNullOrWhiteSpace(dir))
            _ = LoadModel();
    }

    // ── Property change hooks ─────────────────────────────────────────────

    partial void OnVoiceSynthEnabledChanged(bool value)
    {
        if (!value)
            _service.DetachVoiceSession();
        else if (_voiceVm.IsConnected && _voiceVm._voice != null)
            _service.AttachVoiceSession(_voiceVm._voice.AudioDevice);
    }

    partial void OnSpeedChanged(double value)
        => _service.UpdateParameters(SpeakerId, (float)value);

    partial void OnSpeakerIdChanged(int value)
        => _service.UpdateParameters(value, (float)Speed);

    // ── Private helpers ───────────────────────────────────────────────────

    private void OnServiceReadyChanged(bool ready)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ModelLoaded  = ready;
            ModelLoading = false;
        });
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        _service.ReadyChanged -= OnServiceReadyChanged;
        _service.Dispose();
    }
}
