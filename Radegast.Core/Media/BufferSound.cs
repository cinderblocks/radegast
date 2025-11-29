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

// Uncomment this to get lots more logging
//#define TRACE_SOUND
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using FMOD;
using OpenMetaverse;
using OpenMetaverse.Assets;
using System.Threading;

namespace Radegast.Media
{

    public class BufferSound : MediaObject
    {
        private UUID Id;
        private UUID ContainerId;
        private readonly bool prefetchOnly = false;
        private MODE mode;
        public Sound Sound => sound;
        private bool loopSound = false;
        /// <summary>
        /// The individual volume setting for THIS object
        /// </summary>
        private float volumeSetting = 0.5f;

        /// <summary>
        /// Creates a new sound object
        /// </summary>
        public BufferSound(UUID objectId, UUID soundId, bool loop, bool global, Vector3 worldpos, float vol)
        {
            InitBuffer(objectId, soundId, loop, global, worldpos, vol);
        }

        public BufferSound(UUID objectId, UUID soundId, bool loop, bool global, Vector3d worldpos, float vol)
        {
            InitBuffer(objectId, soundId, loop, global, new Vector3(worldpos), vol);
        }

        private void InitBuffer(UUID objectId, UUID soundId, bool loop, bool global, Vector3 worldpos, float vol)
        {
            if (manager == null || !manager.SoundSystemAvailable) return;

            // Do not let this get garbage-collected.
            lock (allBuffers)
                allBuffers[objectId] = this;

            ContainerId = objectId;
            Id = soundId;
            position = FromOMVSpace(worldpos);
            volumeSetting = vol;
            loopSound = loop;

            Logger.Log($"Playing sound at <{position.x:0.0},{position.y:0.0},{position.z:0.0}> ID {Id}",
                Helpers.LogLevel.Debug);

            // Set flags to determine how it will be played.
            mode = MODE.DEFAULT |
                MODE._3D |         // Need 3D effects for placement
                MODE.OPENMEMORY;   // Use sound data in memory

            // Set coordinate space interpretation.
            if (global)
                mode |= MODE._3D_WORLDRELATIVE;
            else
                mode |= MODE._3D_HEADRELATIVE;

            if (loopSound)
                mode |= MODE.LOOP_NORMAL;

            // Fetch the sound data.
            manager.Instance.Client.Assets.RequestAsset(
                Id,
                AssetType.Sound,
                false,
                Assets_OnSoundReceived);
        }

        public static void Kill(UUID id)
        {
            if (allBuffers.ContainsKey(id))
            {
                BufferSound bs = allBuffers[id];
                bs.StopSound(true);
            }
        }

        /// <summary>
        /// Stop all playing sounds in the environment
        /// </summary>
        public static void KillAll()
        {
            // Make a list from the dictionary so we do not get a deadlock
            // on it when removing entries.
            var list = new List<BufferSound>(allBuffers.Values);

            foreach (var s in list)
            {
                s.StopSound();
            }

            var objs = new List<MediaObject>(allChannels.Values);
            foreach (var obj in objs)
            {
                (obj as BufferSound)?.StopSound();
            }
        }

        /// <summary>
        /// Adjust volumes of all playing sounds to observe the new global sound volume
        /// </summary>
        public static void AdjustVolumes()
        {
            // Make a list from the dictionary so we do not get a deadlock
            var list = new List<BufferSound>(allBuffers.Values);

            foreach (var s in list)
            {
                s.AdjustVolume();
            }
        }

        /// <summary>
        /// Adjust the volume of THIS sound when all are being adjusted.
        /// </summary>
        private void AdjustVolume()
        {
            Volume = volumeSetting * AllObjectVolume;
        }

        // A simpler constructor used by PreFetchSound.
        public BufferSound(UUID soundId)
        {
            prefetchOnly = true;
            ContainerId = UUID.Zero;
            Id = soundId;

            manager.Instance.Client.Assets.RequestAsset(
                Id,
                AssetType.Sound,
                false,
                Assets_OnSoundReceived);
        }

        /**
         * Handle arrival of a sound resource.
         */
        private void Assets_OnSoundReceived(AssetDownload transfer, Asset asset)
        {
            if (transfer.Success)
            {
                // If this was a Prefetch, just stop here.
                if (prefetchOnly)
                {
                    return;
                }

                //                Logger.Log("Opening sound " + Id.ToString(), Helpers.LogLevel.Debug);

                // Decode the Ogg Vorbis buffer.
                if (!(asset is AssetSound s)) return;

                s.Decode();
                var data = s.AssetData;

                // Describe the data to FMOD
                extraInfo.length = (uint)data.Length;
                extraInfo.cbsize = Marshal.SizeOf(extraInfo);

                invoke(delegate
                {
                    try
                    {
                        // Create an FMOD sound of this Ogg data.
                        FMODExec(system.createSound(
                            data,
                            mode,
                            ref extraInfo,
                            out sound));

                        // Register for callbacks.
                        RegisterSound(sound);


                        // If looping is requested, loop the entire thing.
                        if (loopSound)
                        {
                            uint soundlen = 0;
                            FMODExec(sound.getLength(out soundlen, TIMEUNIT.PCM));
                            FMODExec(sound.setLoopPoints(0, TIMEUNIT.PCM, soundlen - 1, TIMEUNIT.PCM));
                            FMODExec(sound.setLoopCount(-1));
                        }

                        // Allocate a channel and set initial volume.  Initially paused.
                        ChannelGroup masterChannelGroup;
                        system.getMasterChannelGroup(out masterChannelGroup);
                        FMODExec(system.playSound(sound, masterChannelGroup, true, out channel));
#if TRACE_SOUND
                    Logger.Log(
                        String.Format("Channel {0} for {1} assigned to {2}",
                             channel.getRaw().ToString("X"),
                             sound.getRaw().ToString("X"),
                             Id),
                        Helpers.LogLevel.Debug);
#endif
                        RegisterChannel(channel);

                        FMODExec(channel.setVolume(volumeSetting * AllObjectVolume));

                        // Take note of when the sound is finished playing.
                        FMODExec(channel.setCallback(endCallback));

                        // Set attenuation limits.
                        FMODExec(sound.set3DMinMaxDistance(
                            1.2f, // Any closer than this gets no louder
                            100.0f)); // Further than this gets no softer.

                        // Set the sound point of origin.  This is in SIM coordinates.
                        FMODExec(channel.set3DAttributes(ref position, ref ZeroVector));

                        // Turn off pause mode.  The sound will start playing now.
                        FMODExec(channel.setPaused(false));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Error playing sound: ", Helpers.LogLevel.Error, ex);
                    }
                });
            }
            else
            {
                Logger.Log("Failed to download sound: " + transfer.Status,
                                        Helpers.LogLevel.Error);
            }
        }

        /// <summary>
        /// Handles stop sound even from FMOD
        /// </summary>
        /// <returns>RESULT.OK</returns>
        protected override RESULT EndCallbackHandler()
        {
            // Stop asynchronously from FMOD callback thread to avoid blocking
            _ = StopSoundAsync(false);
            return RESULT.OK;
        }

        protected void StopSound()
        {
            // Fire-and-forget non-blocking stop
            _ = StopSoundAsync(false);
        }

        protected void StopSound(bool blocking)
        {
            // Preserve synchronous blocking behavior by waiting on the async task
            try
            {
                StopSoundAsync(blocking).GetAwaiter().GetResult();
            }
            catch { }
        }

        /// <summary>
        /// Asynchronous implementation of stopping the sound. When 'blocking' is true the returned Task
        /// will complete after FMOD resource cleanup has executed on the FMOD/UI thread invoked by invoke(...).
        /// </summary>
        /// <param name="blocking">If true, caller will wait for cleanup to finish</param>
        /// <returns></returns>
        protected async System.Threading.Tasks.Task StopSoundAsync(bool blocking)
        {
            System.Threading.Tasks.TaskCompletionSource<bool> tcs = null;
            if (blocking) tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

            finished = true;

            invoke(delegate
            {
                string chanStr = "none";
                string soundStr = "none";

                // Release the buffer to avoid a big memory leak.
                try
                {
                    if (channel.hasHandle())
                    {
                        lock (allChannels)
                            allChannels.Remove(channel.handle);
                        chanStr = channel.handle.ToString("X");
                        channel.stop();
                        channel.clearHandle();
                    }

                    if (sound.hasHandle())
                    {
                        soundStr = sound.handle.ToString("X");
                        sound.release();
                        sound.clearHandle();
                    }
#if TRACE_SOUND
                Logger.Log(String.Format("Removing channel {0} sound {1} ID {2}",
                    chanStr,
                    soundStr,
                    Id.ToString()),
                    Helpers.LogLevel.Debug);
#endif
                    lock (allBuffers)
                        allBuffers.Remove(ContainerId);
                }
                catch { }

                if (blocking)
                {
                    try { tcs?.TrySetResult(true); } catch { }
                }
            });

            if (blocking && tcs != null)
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

    }
}
