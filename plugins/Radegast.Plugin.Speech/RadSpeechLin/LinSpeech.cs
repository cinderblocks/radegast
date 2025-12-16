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
using System.Threading.Tasks;
using RadegastSpeech.Talk;

namespace RadegastSpeech
{
    public class LinSpeech : IRadSpeech
    {
        private LinSynth synth;

        public event SpeechEventHandler OnRecognition;

        #region Recognition
        // Speech recognition is not yet available on Linux
        public void RecogStart()
        {
            if (OnRecognition != null) // Suppress compiler warning until we have something for this
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
        #endregion
        #region Speech
        public Task SpeechStart( PluginControl pc, string[] beeps)
        {
            synth = new LinSynth( pc, beeps);
            return Task.CompletedTask;
        }
        public Task SpeechStop()
        {
            synth.Stop();
            return Task.CompletedTask;
        }

        public Task SpeechHalt()
        {
            synth.Halt();
            return Task.CompletedTask;
        }

        public Dictionary<string, AvailableVoice> GetVoices()
        {
            return synth.GetVoices();
        }

        public async Task Speak(QueuedSpeech utterance, string filename)
        {
            await Task.Run(() => synth.Speak(utterance, filename)).ConfigureAwait(false);
        }

        #endregion

    }
}
