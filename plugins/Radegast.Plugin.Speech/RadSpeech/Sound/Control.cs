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
using System.Threading.Tasks;

namespace RadegastSpeech.Sound
{
    internal abstract class Control : AreaControl
    {
        internal Control(PluginControl pc)
            : base(pc)
        {
        }

        /// <summary>
        /// Play a sound once at a specific location.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="sps"></param>
        /// <param name="pos"></param>
        /// <param name="deleteAfter"></param>
        /// <param name="spatialized"></param>
        internal abstract void Play(
            string filename,
            int sps,
            OpenMetaverse.Vector3 pos,
            bool deleteAfter,
            bool spatialized);

        /// <summary>
        /// Async variant of Play. Default implementation offloads to a thread-pool thread
        /// to preserve compatibility while allowing callers to await playback without
        /// blocking the calling thread.
        /// </summary>
        internal virtual Task PlayAsync(
            string filename,
            int sps,
            OpenMetaverse.Vector3 pos,
            bool deleteAfter,
            bool spatialized)
        {
            // Default behavior: run the blocking Play on threadpool
            return Task.Run(() => Play(filename, sps, pos, deleteAfter, spatialized));
        }

        /// <summary>
        /// Async variant that waits for playback to finish. Default implementation
        /// delegates to PlayAsync and does not guarantee completion of playback.
        /// Override in implementations that can await playback completion.
        /// </summary>
        internal virtual Task PlayAndWaitAsync(
            string filename,
            bool global,
            OpenMetaverse.Vector3 pos)
        {
            // Default: call PlayAsync and return its Task
            return PlayAsync(filename, 16000, pos, true, true);
        }

        internal abstract void Stop();
        internal override void Start()
        {
 
        }
        internal override void Shutdown()
        {
            Stop();
        }

    }
}
