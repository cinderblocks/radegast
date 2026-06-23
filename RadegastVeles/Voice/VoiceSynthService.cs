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
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using Microsoft.Extensions.Logging;
using Radegast.Veles.PluginApi;
using SherpaOnnx;

namespace Radegast.Veles.Voice;

/// <summary>
/// Offline voice-synthesis service backed by sherpa-onnx / Piper VITS models.
/// Synthesizes text to Opus-encoded frames and raises <see cref="OnEncodedSample"/>
/// which can be wired to any WebRTC peer connection's <c>SendAudio</c> handler,
/// or consumed by plugins via <see cref="IVoiceSynthService"/>.
/// </summary>
public sealed class VoiceSynthService : IVoiceSynthService, IDisposable
{
    private readonly ILogger<VoiceSynthService> _log;

    // ── Model / engine ───────────────────────────────────────────────────
    private OfflineTts? _tts;
    private readonly object _ttsLock = new();

    // ── Speak queue ──────────────────────────────────────────────────────
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 32);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;

    // ── Audio routing ────────────────────────────────────────────────────

    // The attached AudioDevice. When set, synthesized PCM is fed via
    // FeedPcmSamples which handles Opus encoding and fires OnAudioSourceEncodedSample
    // that VoiceSession wires to pc.SendAudio — transmitting TTS over WebRTC.
    private AudioDevice? _attachedAudio;

    /// <inheritdoc/>
    public string ModelDirectory { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public int SpeakerId { get; private set; }

    /// <inheritdoc/>
    public float Speed { get; private set; } = 1.0f;

    /// <inheritdoc/>
    public bool IsReady { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>Raised on the thread pool when the model load state changes.</summary>
    public event Action<bool>? ReadyChanged;

    // ─────────────────────────────────────────────────────────────────────

    public VoiceSynthService(ILogger<VoiceSynthService> log)
    {
        _log = log;
        _workerTask = Task.Run(WorkerLoop);
    }

    // ── Voice session attachment ──────────────────────────────────────────

    /// <summary>
    /// Wires TTS audio output to the given <see cref="Sdl3Audio"/> device.
    /// Synthesized PCM is fed via <see cref="Sdl3Audio.FeedPcmSamples"/> which
    /// handles Opus encoding and fires <c>OnAudioSourceEncodedSample</c> that
    /// VoiceSession wires to <c>pc.SendAudio</c> — transmitting TTS over WebRTC.
    /// </summary>
    public void AttachVoiceSession(AudioDevice audioDevice)
    {
        _attachedAudio = audioDevice;
    }

    /// <summary>Unwire TTS from the current voice audio device.</summary>
    public void DetachVoiceSession()
    {
        _attachedAudio = null;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task LoadModelAsync(string modelDir, int speakerId = 0, float speed = 1.0f)
    {
        ModelDirectory = modelDir;
        SpeakerId = speakerId;
        Speed = speed;
        return Task.Run(LoadModel);
    }

    /// <inheritdoc/>
    public void UpdateParameters(int speakerId, float speed)
    {
        SpeakerId = speakerId;
        Speed = speed;
    }

    /// <inheritdoc/>
    public void UnloadModel()
    {
        lock (_ttsLock)
        {
            _tts?.Dispose();
            _tts = null;
            IsReady = false;
        }
        ReadyChanged?.Invoke(false);
    }

    /// <inheritdoc/>
    public bool Speak(string text)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(text)) return false;
        return _queue.TryAdd(text.Trim());
    }

    // ── Model loading ─────────────────────────────────────────────────────

    private void LoadModel()
    {
        lock (_ttsLock)
        {
            try
            {
                _tts?.Dispose();
                _tts = null;
                IsReady = false;

                if (string.IsNullOrWhiteSpace(ModelDirectory) || !Directory.Exists(ModelDirectory))
                {
                    _log.LogWarning("VoiceSynth: model directory not found: {Dir}", ModelDirectory);
                    ReadyChanged?.Invoke(false);
                    return;
                }

                var onnxFile = FindFile(ModelDirectory, "*.onnx");
                if (onnxFile == null)
                {
                    _log.LogWarning("VoiceSynth: no .onnx file found in {Dir}", ModelDirectory);
                    ReadyChanged?.Invoke(false);
                    return;
                }

                var tokensFile = FindFile(ModelDirectory, "tokens.txt");

                var eSpeakDataDir = string.Empty;
                var eSpeakCandidate = Path.Combine(ModelDirectory, "espeak-ng-data");
                if (Directory.Exists(eSpeakCandidate))
                    eSpeakDataDir = eSpeakCandidate;

                // Piper/VITS models that use espeak-ng phonemization require the
                // espeak-ng-data directory.  The sherpa-onnx native library will
                // access-violate if DataDir is empty and the model needs it.
                // Detect this early and give the user an actionable message.
                if (string.IsNullOrEmpty(eSpeakDataDir))
                {
                    // Look for espeak-ng-data next to the executable as a fallback
                    // (some deployment layouts copy it there).
                    var appDirCandidate = Path.Combine(
                        AppContext.BaseDirectory, "espeak-ng-data");
                    if (Directory.Exists(appDirCandidate))
                        eSpeakDataDir = appDirCandidate;
                }

                if (string.IsNullOrEmpty(eSpeakDataDir))
                {
                    _log.LogError(
                        "VoiceSynth: espeak-ng-data not found in model directory '{Dir}' or " +
                        "application directory. Piper voices require this folder. " +
                        "Re-download the voice model – the downloader will include it automatically.",
                        ModelDirectory);
                    ReadyChanged?.Invoke(false);
                    return;
                }

                var config = new OfflineTtsConfig
                {
                    Model =
                    {
                        Vits =
                        {
                            Model        = onnxFile,
                            Tokens       = tokensFile ?? string.Empty,
                            DataDir      = eSpeakDataDir,
                            LengthScale  = 1.0f / Math.Max(0.1f, Speed),
                            NoiseScale   = 0.667f,
                            NoiseScaleW  = 0.8f,
                        },
                        NumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                        Debug      = 0,
                        Provider   = "cpu",
                    },
                    MaxNumSentences = 1,
                };

                _tts = new OfflineTts(config);
                IsReady = true;
                _log.LogInformation("VoiceSynth: model loaded from {Dir} (sampleRate={SR})",
                    ModelDirectory, _tts.SampleRate);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "VoiceSynth: failed to load model from {Dir}", ModelDirectory);
                _tts?.Dispose();
                _tts = null;
                IsReady = false;
            }
        }

        ReadyChanged?.Invoke(IsReady);
    }

    // ── Worker loop ───────────────────────────────────────────────────────

    private void WorkerLoop()
    {
        try
        {
            foreach (var text in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try { SynthesizeAndEmitAsync(text).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _log.LogWarning(ex, "VoiceSynth: synthesis failed for: {Text}", text); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SynthesizeAndEmitAsync(string text)
    {
        OfflineTts? engine;
        lock (_ttsLock)
            engine = _tts;
        if (engine == null) return;

        var audio = engine.Generate(text, Speed, SpeakerId);
        if (audio.NumSamples <= 0) return;

        var audio2 = _attachedAudio;
        if (audio2 == null) return;

        // Convert float32 samples [-1,+1] to int16 PCM
        var floatSamples = audio.Samples;
        var pcm = new short[floatSamples.Length];
        for (int i = 0; i < floatSamples.Length; i++)
        {
            var c = Math.Max(-1.0f, Math.Min(1.0f, floatSamples[i]));
            pcm[i] = (short)(c * short.MaxValue);
        }

        // Resample to 48 kHz mono if needed, then pace one Opus frame (20 ms)
        // per real-time interval so WebRTC peers receive frames at the correct rate
        // rather than as a burst (which causes buffering/garble on the remote end).
        await audio2.FeedPcmSamplesPacedAsync(pcm, channels: 1, sampleRate: audio.SampleRate,
            _cts.Token).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string? FindFile(string dir, string pattern)
    {
        var files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        return files.Length > 0 ? files[0] : null;
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        DetachVoiceSession();
        _cts.Cancel();
        _queue.CompleteAdding();
        try { _workerTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        lock (_ttsLock) { _tts?.Dispose(); _tts = null; }
        _cts.Dispose();
        _queue.Dispose();
    }
}
