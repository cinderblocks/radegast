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
using System.Threading.Tasks;

namespace RadegastSpeech.Talk
{
    internal class Synthesizer
    {
        protected PluginControl control;
        protected readonly string[] BeepNames;

        internal Synthesizer(PluginControl pc)
        {
            control = pc;

            // The following list must be in order of BeepType.
            BeepNames = new string[] {
                "ConfirmBeep.wav",
                "CommBeep.wav",
                "DialogBeep.wav",
                "BadBeep.wav",
                "GoodBeep.wav",
                "MoneyBeep.wav",
                "OpenBeep.wav",
                "CloseBeep.wav"};

            control.osLayer?.SpeechStart(
                control,
                BeepNames);
		}

        internal void Start()
        {
        }

        internal void Stop()
        {
            control.osLayer?.SpeechHalt();
        }

        internal Dictionary<string, AvailableVoice> GetVoices()
        {
            return control.osLayer?.GetVoices();
        }

        /// <summary>
        /// Asynchronous variant of Speak. Prefer calling the OS-layer async implementation
        /// if present; otherwise attempt to call a legacy synchronous Speak via reflection.
        /// </summary>
        internal Task SpeakAsync(QueuedSpeech q, string outputfile)
        {
            var t = control.osLayer?.Speak(q, outputfile);
            return t ?? Task.CompletedTask;
        }

        internal void Shutdown()
        {
            control.osLayer?.SpeechStop();
        }

    }
}
