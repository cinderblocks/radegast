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

namespace Radegast.Veles.PluginApi;

/// <summary>
/// Public voice-synthesis API exposed to Veles plugins via <see cref="IPluginContext.VoiceSynth"/>.
/// Allows plugins (e.g. an OllamaChat plugin) to synthesize and transmit speech
/// over the active WebRTC voice session without depending on the internal VoiceSynthService.
/// </summary>
public interface IVoiceSynthService
{
    // ── State ─────────────────────────────────────────────────────────────

    /// <summary>True when a model is loaded and ready to speak.</summary>
    bool IsReady { get; }

    /// <summary>Directory of the currently loaded Piper voice model, or empty.</summary>
    string ModelDirectory { get; }

    /// <summary>Speaker ID (0-based) used for multi-speaker models.</summary>
    int SpeakerId { get; }

    /// <summary>Speech speed multiplier (1.0 = normal, &gt;1.0 = faster, &lt;1.0 = slower).</summary>
    float Speed { get; }

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>Raised when the model ready state changes (true = loaded, false = unloaded).</summary>
    event Action<bool> ReadyChanged;

    // ── Model management ──────────────────────────────────────────────────

    /// <summary>
    /// Load (or reload) the Piper ONNX model from <paramref name="modelDir"/>.
    /// Expected layout: <c>*.onnx</c> + <c>tokens.txt</c> + optional <c>espeak-ng-data/</c>.
    /// Runs on a background thread; <see cref="ReadyChanged"/> fires when complete.
    /// </summary>
    Task LoadModelAsync(string modelDir, int speakerId = 0, float speed = 1.0f);

    /// <summary>Unload the current model and release native resources.</summary>
    void UnloadModel();

    /// <summary>Update speaker ID and speed without reloading the model.</summary>
    void UpdateParameters(int speakerId, float speed);

    // ── Speech ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueue <paramref name="text"/> for synthesis and transmission.
    /// Returns <c>false</c> if no model is loaded, the queue is full, or text is empty.
    /// The call returns immediately; audio is transmitted asynchronously.
    /// </summary>
    bool Speak(string text);
}
