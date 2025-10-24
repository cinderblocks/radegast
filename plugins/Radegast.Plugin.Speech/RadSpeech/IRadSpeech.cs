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

namespace RadegastSpeech
{
    public delegate void SpeechEventHandler(string text);
    public interface IRadSpeech
    {
        // Speech Synthesis methods
        void SpeechStart( PluginControl pc, string[] beeps);
        void SpeechStop();
        void SpeechHalt();
        void Speak(Talk.QueuedSpeech utterance, string filename);
        Dictionary<string, Talk.AvailableVoice> GetVoices();

        // Speech Recognition methods
        void RecogStart();
        void RecogStop();
        void CreateGrammar(string name, string[] options);
        void ActivateGrammar(string name);
        void DeactivateGrammar(string name);
        event SpeechEventHandler OnRecognition;

    }
}
