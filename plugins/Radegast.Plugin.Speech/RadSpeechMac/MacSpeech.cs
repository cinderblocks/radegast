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

using System.Collections.Generic;
using RadegastSpeech.Talk;

namespace RadegastSpeech
{
    public class MacSpeech : IRadSpeech
    {
        private MacSynth synth;

        #pragma warning disable 67
        public event SpeechEventHandler OnRecognition;

        public void SpeechStart( PluginControl pc, string[] beeps)
        {
            synth = new MacSynth( pc, beeps);
            if (OnRecognition != null)
            {
            }
        }

        public void SpeechStop()
        {
            synth.Stop();
        }

        public void SpeechHalt()
        {
            synth.Halt();
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

        public void RecogStart()
        {
            if (OnRecognition != null) // Supress compiler wanring until we have something for this
            {
            }
        }

        public void RecogStop()
        {
        }

        public void CreateGrammar(string name, string[] alternatives)
        {
        }

        public void ActivateGrammar(string name)
        {
        }

        public void DeactivateGrammar(string name)
        {
        }

    }
}
