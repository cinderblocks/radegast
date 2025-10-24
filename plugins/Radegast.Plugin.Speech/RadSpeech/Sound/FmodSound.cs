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

// Define the following symbol to use the shared Radegast FMOD instance.
#define SHAREDFMOD

using System;
using System.IO;
using System.Threading;
using Radegast.Media;

namespace RadegastSpeech.Sound
{
    internal class FmodSound : Control
    {
        private Speech speechPlayer;
        private readonly AutoResetEvent playing;

        internal FmodSound(PluginControl pc)
            : base(pc)
        {
            playing = new AutoResetEvent(false);
            speechPlayer = new Speech();
            speechPlayer.OnSpeechDone += SpeechDoneHandler;
        }

        internal override void Stop()
        {
            speechPlayer?.Stop();
        }

        /// <summary>
        /// Play a prerecorded sound
        /// </summary>
        /// <param name="filename">Name of the file to play</param>
        /// <param name="sps">Samples per second</param>
        /// <param name="worldPos">Position of the sound</param>
        /// <param name="deleteAfter">True if we should delete the file when done</param>
        /// <param name="global">True if position is in world coordinates
        /// instead of hed-relative</param>
        internal override void Play(string filename,
            int sps,
            OpenMetaverse.Vector3 worldPos,
            bool deleteAfter,
            bool global)
        {
            if (speechPlayer != null)
            {
                // Play this file at the designated position.  When it finishes, the
                // SpeechDoneHandler will be called.
                uint len = speechPlayer.Play(filename, global, worldPos);

                // Wait for it to finish. Max 2sec longer tha it is supposed to
                playing.WaitOne((int)len + 2000, false);
            }

            // Delete the WAV file if requested.
            if (deleteAfter)
            {
                File.Delete(filename);
            }
        }

        // Handler for event when speech is done playing.
        private void SpeechDoneHandler(object sender, EventArgs e)
        {
            // Poke the semaphore
            playing.Set();
        }

        // TODO do we need this?
        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (speechPlayer != null)
                {
                    speechPlayer.Stop();
                    speechPlayer = null;
                }
            }

        }

    }
}
