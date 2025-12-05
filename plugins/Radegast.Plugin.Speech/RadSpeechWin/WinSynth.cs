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
using System.Linq;
using System.IO;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using RadegastSpeech.Talk;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace RadegastSpeech
{
    internal class WinSynth
    {
        private SpeechSynthesizer syn;

        private SpeechAudioFormatInfo format;
        private PromptBuilder pb;
        private PromptStyle mainStyle;
        private FileStream mstream;
        private readonly string[] BeepNames;
        private readonly OSDMap voiceProperties;

        private PromptRate voiceRate
        {
            get
            {
                string voiceSpeed = voiceProperties?["voice_speed"];
                if (!string.IsNullOrEmpty(voiceSpeed))
                {
                    switch (voiceSpeed)
                    {
                        case "fast": return PromptRate.Fast;
                        case "slow": return PromptRate.Slow;
                    }
                }
                return PromptRate.Medium;
            }
        }

        internal WinSynth(PluginControl pc, string[] beeps)
        {
            BeepNames = beeps;
            voiceProperties = pc.config["properties"] as OSDMap;
        }

        internal void SpeechStart()
        {
            syn = new SpeechSynthesizer();
        }

        internal void SpeechStop()
        {
            syn.Dispose();
            syn = null;
        }

        /// <summary>
        /// Interrupt synthesis immediately.
        /// </summary>
        internal void Halt()
        {
        }

        internal void Speak(QueuedSpeech utterance, string outputfile)
        {
            if (syn == null) return;

            // Got something to say.  Initialize temp file.
            StartSpeech(utterance.voice, outputfile);

            // Play the beep, if there is one.
            if (utterance.beep != BeepType.None)
            {
                pb.StartSentence();
                pb.AppendAudio(BeepNames[(int)utterance.beep]);
                pb.EndSentence();
            }

            // Say who is talking, if we know.
            if (utterance.speaker != null)
            {
                if (utterance.isAction)
                {
                    SayPrompt(utterance.speaker);
                }
                else
                {
                    // Speak the name of the person speaking.  This is done in a neutral,
                    // softer voice, speaking slightly rapidly.
                    SayPrompt(utterance.speaker + ".");
                }
            }

            // Then synthesize the main part of the message.
            SaySegment(utterance);

            // Close the temporary WAV file.
            FinishSpeech();
        }

        private void StartSpeech(AssignedVoice vb, string outputfile)
        {
            WinAvailableVoice wv = (WinAvailableVoice)vb.root;

            // Find the best audio format to use for this voice.
            System.Collections.ObjectModel.ReadOnlyCollection<SpeechAudioFormatInfo> formats =
                wv.winVoice.VoiceInfo.SupportedAudioFormats;

            format = formats.FirstOrDefault() ?? new SpeechAudioFormatInfo(
                         16000,      // Samples per second
                         AudioBitsPerSample.Sixteen,
                         AudioChannel.Mono);

            // First set up to synthesize the message into a WAV file.
            mstream = new FileStream(outputfile, FileMode.Create, FileAccess.Write);

            syn.SetOutputToWaveStream(mstream);

            pb = new PromptBuilder();
            mainStyle = new PromptStyle();
            //            mainStyle.Volume = promptVol;
            syn.SelectVoice(wv.winVoice.VoiceInfo.Name);
            pb.StartStyle(mainStyle);
        }

        /// <summary>
        /// Say the main part of the message.
        /// </summary>
        /// <param name="utterance"></param>
        private void SaySegment(QueuedSpeech utterance)
        {
            PromptStyle body = new PromptStyle();
            /* User preference now
            switch (utterance.voice.rateModification)
            {
                case 00: body.Rate = PromptRate.Fast; break;
                case +1: body.Rate = PromptRate.Fast; break;
                case -1: body.Rate = PromptRate.Medium; break;
            }
            */

            body.Rate = voiceRate;

            switch (utterance.voice.pitchModification)
            {
                case 00: body.Emphasis = PromptEmphasis.Moderate; break;
                case +1: body.Emphasis = PromptEmphasis.Strong; break;
                case -1: body.Emphasis = PromptEmphasis.Reduced; break;
            }

            pb.StartStyle(body);
            pb.StartSentence();
            pb.AppendText(utterance.message);
            pb.EndSentence();
            pb.EndStyle();
        }

        /// <summary>
        /// Say the introductory tag.
        /// </summary>
        /// <param name="text"></param>
        private void SayPrompt(string text)
        {
            PromptStyle intro = new PromptStyle
            {
                Rate = voiceRate,
                //Volume = promptVol - 1;
                Emphasis = PromptEmphasis.Moderate
            };
            pb.StartStyle(intro);
            pb.StartSentence();
            pb.AppendText(text + ":");
            pb.EndSentence();
            pb.EndStyle();
        }

        /// <summary>
        /// Wrap up recording of the speech.
        /// </summary>
        private void FinishSpeech()
        {
            pb.EndStyle();

            // Actually generate the WAV file now
            try
            {
                syn.Speak(pb);
            }
            catch (Exception e)
            {
                Logger.Error("Synthesizer error: " + e.Message);
            }
            finally
            {
                // VERY IMPORTANT we set the synthesizer output to null here.
                // Otherwise, it does not fully close its output stream.
                syn.SetOutputToNull();
                mstream.Flush();
                mstream.Close();
                mstream.Dispose();
            }
        }

        /// <summary>
        /// Get the list of available installed voices.
        /// </summary>
        /// <returns></returns>
        internal Dictionary<string, AvailableVoice> GetVoices()
        {
            Dictionary<string, AvailableVoice> names = new Dictionary<string, AvailableVoice>();
            bool AnnaPresent = false;
            bool SamPresent = false;

            // Query the synthesizer for the voices installed.
            System.Collections.ObjectModel.ReadOnlyCollection<InstalledVoice> installed
                = syn.GetInstalledVoices();

            // Copy that information into a Dictionary we can update.
            foreach (InstalledVoice v in installed)
            {
                if (v.Enabled)
                {
                    bool skip = false;

                    // Check for additional information about this voice
                    string propString = voiceProperties?[v.VoiceInfo.Name].AsString();
                    if (propString != null)
                    {
                        // Properties are a series of blank-separated keywords
                        string[] props = propString.Split(' ');

                        foreach (string key in props)
                        {
                            switch (key)
                            {
                                case "ignore":
                                    skip = true;
                                    break;
                            }
                        }
                    }

                    // If this voice is not blocked add it to the list.
                    if (!skip)
                    {
                        WinAvailableVoice wav = new WinAvailableVoice(v);
                        names[v.VoiceInfo.Name] = (AvailableVoice)wav;
                    }
                }
                // Notice certain Microsoft voices.
                if (v.VoiceInfo.Name.Equals("Microsoft Anna")) AnnaPresent = true;
                else if (v.VoiceInfo.Name.StartsWith("Microsoft ")) SamPresent = true;
            }

            // We have all the voices.  Remove the old Microsoft voices
            // if this is Vista or later.  This is because they do not work.
            if (AnnaPresent && SamPresent)
            {
                RemoveIf(names, "Microsoft Sam");
                RemoveIf(names, "Microsoft Mike");
                RemoveIf(names, "Microsoft Mary");
            }
            //says "Blah to Blah Blah the Blah Blah"
            RemoveIf(names, "SampleTTSVoice");

            return names;
        }

        private void RemoveIf(Dictionary<string, AvailableVoice> d, string key)
        {
            if (d.ContainsKey(key))
                d.Remove(key);
        }

        private class WinAvailableVoice : AvailableVoice
        {
            internal readonly InstalledVoice winVoice;
            internal WinAvailableVoice(InstalledVoice i)
            {
                winVoice = i;
                Name = i.VoiceInfo.Name;
                Male = (winVoice.VoiceInfo.Gender == VoiceGender.Male);
            }
        }
    }
}