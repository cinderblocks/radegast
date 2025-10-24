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
using RadegastSpeech.Talk;

namespace RadegastSpeech
{
    /// <summary>
    /// Main interface to the Speech OS Layer for Microsoft Windows.
    /// </summary>
    public class WinSpeech : IRadSpeech
    {
        private WinSynth synth;
        private WinRecog recog;

        public event SpeechEventHandler OnRecognition;

        #region Synthesis
        public void SpeechStart( PluginControl pc, string[] beeps)
        {
            synth = new WinSynth( pc, beeps);
            synth.SpeechStart();
        }

        public void SpeechStop()
        {
            if (synth != null)
            {
                synth.SpeechStop();
                synth = null;
            }

            if (recog != null)
            {
                recog.Stop();
                recog = null;
            }
        }

        public void SpeechHalt()
        {
            synth?.Halt();
        }

        public Dictionary<string, AvailableVoice> GetVoices()
        {
            return synth.GetVoices();
        }

        public void Speak(
            QueuedSpeech utterance,
            string filename)
        {
            synth.Speak(utterance, filename);
        }

        #endregion
        #region Recognition
        public void RecogStart()
        {
            try
            {
                recog = new WinRecog();
                recog.Start();

                // Hook up event relay
                recog.OnWinRecognition +=
                    recog_OnWinRecognition;
            }
            catch (Exception)
            {
                // No recognition on this platform
                recog = null;
            }
        }

        private void recog_OnWinRecognition(string text)
        {
            OnRecognition?.Invoke(text);
        }

        public void RecogStop()
        {
            recog?.Stop();
        }

        public void CreateGrammar( string name, string[] alternatives )
        {
            recog?.CreateGrammar(name, alternatives);
        }

        public void ActivateGrammar(string name)
        {
            recog?.ActivateGrammar(name);
        }

        public void DeactivateGrammar(string name)
        {
            recog?.DeactivateGrammar(name);
        }
        #endregion
    }
}
