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

namespace RadegastSpeech.Listen
{
    internal class Control : AreaControl
    {
        private Recognizer recog = null;
        internal Control(PluginControl pc)
            : base(pc)
        {
            recog = new Recognizer(control);
        }

        internal void CreateGrammar(string name, string[] options)
        {
            recog?.CreateGrammar(name, options);
        }
        internal override void Start()
        {
            // If we have a recognizer, start it.
            recog?.Start();
        }
        internal override void Shutdown()
        {
            if (recog == null) return;
            recog.Stop();
            recog = null;
        }
        internal void ActivateGrammar(string name)
        {
            recog?.ActivateGrammar(name);
        }
        internal void DeactivateGrammar(string name)
        {
            recog?.DeactivateGrammar(name);
        }

    }
}
