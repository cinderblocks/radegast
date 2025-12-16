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
using System.Threading.Tasks;
using Radegast.Media;

namespace RadegastSpeech.Sound
{
    internal class FmodSound : Control
    {
        private Speech speechPlayer;

        internal FmodSound(PluginControl pc)
            : base(pc)
        {
            speechPlayer = new Speech();
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
                try
                {
                    // Start async playback without blocking this caller.
                    _ = PlayAndWaitAsync(filename, global, worldPos);
                }
                catch (Exception)
                {
                    // Swallow exceptions to preserve original behavior
                }
            }

            // If the async path does not delete the file for some reason, attempt best-effort cleanup
            if (deleteAfter)
            {
                // Deletion is handled after playback in PlayAndWaitAsync; not needed here.
            }
        }

        /// <summary>
        /// Async variant that waits for playback to finish and deletes the file afterwards.
        /// </summary>
        internal override async Task PlayAndWaitAsync(string filename, bool global, OpenMetaverse.Vector3 pos)
        {
            if (speechPlayer != null)
            {
                try
                {
                    await speechPlayer.PlayAndWaitAsync(filename, global, pos).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Ignore playback errors
                }
            }

            // Delete the WAV file if present. Best-effort cleanup.
            try { File.Delete(filename); } catch { }
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
