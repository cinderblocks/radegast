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
using System.Speech.Recognition;
using OpenMetaverse;

namespace RadegastSpeech
{
    internal class WinRecog
    {
        private SpeechRecognizer rec;
        private readonly DictationGrammar dGrammar;
        private readonly Dictionary<string, Grammar> cGrammars;
        internal event SpeechEventHandler OnWinRecognition;

        internal WinRecog( )
        {
            try
            {
                rec = new SpeechRecognizer();
                dGrammar = new DictationGrammar();
                rec.LoadGrammar(dGrammar);
                cGrammars = new Dictionary<string, Grammar>();
            }
            catch (Exception ex)
            {
                rec = null;
                Logger.Warn("Speech recognition disabled", ex);
            }
        }

        internal void Start()
        {
            if (rec == null) return;
            rec.Enabled = true;
            rec.SpeechRecognized += onSpeech;
        }

        internal void Stop()
        {
            if (rec == null) return;
            rec.Enabled = false;
            rec.SpeechRecognized -= onSpeech;
            rec.Dispose();
            rec = null;
        }

        /// <summary>
        /// Add a grammar to the active list
        /// </summary>
        /// <param name="name"></param>
        internal void ActivateGrammar(string name)
        {
            if (rec == null) return;

            if (!cGrammars.ContainsKey(name) || rec.Grammars.Contains(cGrammars[name]))
                return;
            rec.LoadGrammar(cGrammars[name]);
        }

        /// <summary>
        /// Remove a grammar from active list
        /// </summary>
        /// <param name="name"></param>
        internal void DeactivateGrammar(string name)
        {
            if (rec == null) return;

            // Avoid exceptions from deactivating what is not active
            if (!cGrammars.ContainsKey(name))
                return;
            Grammar v = cGrammars[name];
            if (!rec.Grammars.Contains(v))
                return;
            rec.UnloadGrammar(v);
        }

        private void onSpeech(object sender, SpeechRecognizedEventArgs e)
        {
            if (rec == null) return;

            if (OnWinRecognition == null) return;

            RecognitionResult r = e.Result;
            OnWinRecognition(r.Text);
        }

        internal void CreateGrammar(string name, string[] options)
        {
            if (rec == null) return;

            GrammarBuilder gb = new GrammarBuilder();
            gb.Append(new Choices(options));
            cGrammars[name] = new Grammar(gb);
        }

    }
}
