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
using System.Collections.Generic;
using System.IO;
using NVorbis;

namespace Veles.Plugin.ImportExport;

/// <summary>
/// Audio format conversion helpers used by the Import/Export plugin.
/// OGG decoding uses NVorbis (deployed by the host app).
/// WAV reading/writing uses the standard RIFF format in pure .NET.
/// </summary>
internal static class SoundConverter
{
    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Saves an OGG Vorbis byte array to disk in the format implied by
    /// <paramref name="path"/>'s extension (.ogg or .wav).
    /// </summary>
    public static void SaveAs(byte[] oggBytes, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".ogg":
                File.WriteAllBytes(path, oggBytes);
                break;
            case ".wav":
                var (samples, rate, channels) = DecodeOgg(oggBytes);
                WriteWav(path, samples, rate, channels);
                break;
            default:
                // Unknown extension: write raw OGG and let the OS sort it out
                File.WriteAllBytes(path, oggBytes);
                break;
        }
    }

    /// <summary>
    /// Detects whether <paramref name="data"/> is an OGG or WAV file and
    /// returns the audio as raw interleaved 16-bit signed PCM bytes along
    /// with the sample rate and channel count, ready for
    /// <see cref="LibreMetaverse.Assets.AssetSound.PcmToOgg"/>.
    /// Returns null if the format is unrecognised.
    /// </summary>
    public static (byte[] pcm, int sampleRate, int channels)? ToPcm(byte[] data)
    {
        if (IsOgg(data))
        {
            var (floats, rate, ch) = DecodeOgg(data);
            return (FloatsToPcm16(floats), rate, ch);
        }
        if (IsWav(data))
        {
            return ParseWav(data);
        }
        return null;
    }

    // ── Format detection ────────────────────────────────────────────────────

    public static bool IsOgg(byte[] data) =>
        data.Length >= 4 &&
        data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53; // "OggS"

    public static bool IsWav(byte[] data) =>
        data.Length >= 12 &&
        data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && // "RIFF"
        data[8] == 0x57 && data[9] == 0x41 && data[10] == 0x56 && data[11] == 0x45; // "WAVE"

    // ── OGG → PCM (via NVorbis) ─────────────────────────────────────────────

    /// <returns>Interleaved float samples in [-1,1], sample rate, channel count.</returns>
    private static (float[] samples, int sampleRate, int channels) DecodeOgg(byte[] oggBytes)
    {
        using var ms      = new MemoryStream(oggBytes);
        using var vorbis  = new VorbisReader(ms);
        int channels   = vorbis.Channels;
        int sampleRate = vorbis.SampleRate;

        var result  = new List<float>(vorbis.TotalSamples > 0
            ? (int)(vorbis.TotalSamples * channels) : 44100 * channels * 30);
        var buffer  = new float[4096];
        int read;
        while ((read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                result.Add(buffer[i]);
        }
        return (result.ToArray(), sampleRate, channels);
    }

    // ── WAV parse ───────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal RIFF/WAV parser. Returns raw interleaved PCM bytes (same layout
    /// as the data chunk), the sample rate, and the number of channels.
    /// Only PCM (audioFormat=1) is supported.
    /// </summary>
    public static (byte[] pcm, int sampleRate, int channels) ParseWav(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        // RIFF header
        if (br.ReadUInt32() != 0x46464952u) // "RIFF" LE
            throw new InvalidDataException("Not a RIFF file.");
        br.ReadUInt32(); // file size - 8 (ignored)
        if (br.ReadUInt32() != 0x45564157u) // "WAVE" LE
            throw new InvalidDataException("Not a WAVE file.");

        int sampleRate   = 0;
        int channels     = 0;
        int bitsPerSample = 16;
        byte[]? pcm      = null;

        // Scan chunks until we have fmt + data
        while (ms.Position < ms.Length - 8)
        {
            uint chunkId   = br.ReadUInt32();
            int  chunkSize = (int)br.ReadUInt32();
            long nextChunk = ms.Position + chunkSize;

            if (chunkId == 0x20746D66u) // "fmt " LE
            {
                int audioFormat = br.ReadUInt16();
                if (audioFormat != 1)
                    throw new InvalidDataException(
                        $"WAV audioFormat {audioFormat} is not PCM (1). Only uncompressed PCM is supported.");
                channels      = br.ReadUInt16();
                sampleRate    = (int)br.ReadUInt32();
                br.ReadUInt32(); // byteRate
                br.ReadUInt16(); // blockAlign
                bitsPerSample = br.ReadUInt16();
            }
            else if (chunkId == 0x61746164u) // "data" LE
            {
                pcm = br.ReadBytes(chunkSize);
            }

            // Skip any padding to align to next chunk
            ms.Position = Math.Min(nextChunk + (chunkSize & 1), ms.Length);
            if (pcm != null && sampleRate > 0) break;
        }

        if (pcm == null)   throw new InvalidDataException("WAV file has no data chunk.");
        if (sampleRate == 0) throw new InvalidDataException("WAV file has no fmt chunk.");

        // Normalise to 16-bit if necessary
        if (bitsPerSample == 8)
            pcm = Convert8to16(pcm);
        else if (bitsPerSample != 16)
            throw new InvalidDataException($"WAV bit depth {bitsPerSample} is not supported (8 or 16 only).");

        return (pcm, sampleRate, channels);
    }

    // ── WAV write ───────────────────────────────────────────────────────────

    private static void WriteWav(string path, float[] samples, int sampleRate, int channels)
    {
        var pcm = FloatsToPcm16(samples);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        int dataSize   = pcm.Length;
        int byteRate   = sampleRate * channels * 2; // 16-bit = 2 bytes/sample
        int blockAlign = channels * 2;

        // RIFF header
        bw.Write(0x46464952u); // "RIFF"
        bw.Write(36 + dataSize);
        bw.Write(0x45564157u); // "WAVE"
        // fmt chunk
        bw.Write(0x20746D66u); // "fmt "
        bw.Write(16);
        bw.Write((short)1);       // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)16);      // bitsPerSample
        // data chunk
        bw.Write(0x61746164u); // "data"
        bw.Write(dataSize);
        bw.Write(pcm);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static byte[] FloatsToPcm16(float[] samples)
    {
        var result = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp((int)(samples[i] * 32768f), -32768, 32767);
            result[i * 2]     = (byte)(s & 0xFF);
            result[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return result;
    }

    private static byte[] Convert8to16(byte[] data)
    {
        // 8-bit WAV is unsigned (0–255, midpoint 128) → 16-bit signed
        var result = new byte[data.Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            short s = (short)((data[i] - 128) << 8);
            result[i * 2]     = (byte)(s & 0xFF);
            result[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return result;
    }
}
