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
        /// <param name="?"></param>
        internal abstract void Play(
            string filename,
            int sps,
            OpenMetaverse.Vector3 pos,
            bool deleteAfter,
            bool spatialized);

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
