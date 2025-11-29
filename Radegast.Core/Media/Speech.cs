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
using FMOD;
using OpenMetaverse;

namespace Radegast.Media
{
    public class Speech : MediaObject
    {
        /// <summary>
        /// Fired when a stream meta data is received
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Key, value are sent in e</param>
        public delegate void SpeechDoneCallback(object sender, EventArgs e);
        /// <summary>
        /// Fired when a stream meta data is received
        /// </summary>
        public event SpeechDoneCallback OnSpeechDone;
        private string filename;
        private Vector3 speakerPos;

        public static bool Compress { set; get; } = true;

        public static bool Surround { set; get; } = false;

        /// <summary>
        /// Creates a new sound object
        /// </summary>
        public Speech()
        {
            extraInfo.format = SOUND_FORMAT.PCM16;
        }


        /// <summary>
        /// Releases resources of this sound object
        /// </summary>
        public override void Dispose()
        {
            if (sound.hasHandle())
            {
                sound.release();
                sound.clearHandle();
            }
            base.Dispose();
        }

        /// <summary>
        /// Plays audio from a file, as created by an external speech synthesizer.
        /// </summary>
        /// <param name="speakfile">Name of a WAV file</param>
        /// <param name="global"></param>
        /// <param name="pos"></param>
        /// <returns>Length of the sound in ms</returns>
        public uint Play(string speakfile, bool global, Vector3 pos)
        {
            // Synchronous wrapper preserving previous behavior
            try
            {
                return PlayAsync(speakfile, global, pos).GetAwaiter().GetResult();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Async variant of Play to avoid blocking threads.
        /// </summary>
        /// <param name="speakfile"></param>
        /// <param name="global"></param>
        /// <param name="pos"></param>
        /// <returns>Length of the sound in ms</returns>
        public async Task<uint> PlayAsync(string speakfile, bool global, Vector3 pos)
        {
            uint len = 0;

            speakerPos = pos;
            filename = speakfile;

            // Set flags to determine how it will be played.
            MODE mode = MODE.DEFAULT;

            if (Surround)
            {
                mode |= MODE._3D;

                // Set coordinate space interpretation.
                if (global)
                    mode |= MODE._3D_WORLDRELATIVE;
                else
                    mode |= MODE._3D_HEADRELATIVE;
            }

            var tcs = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);

            invoke(delegate
            {
                try
                {
                    FMODExec(
                        system.createSound(filename,
                            mode,
                            ref extraInfo,
                            out sound));

                    // Register for callbacks.
                    RegisterSound(sound);

                    // Find out how long the sound should play
                    FMODExec(sound.getLength(out len, TIMEUNIT.MS));

                    // Allocate a channel, initially paused.
                    ChannelGroup masterChannelGroup;
                    system.getMasterChannelGroup(out masterChannelGroup);
                    FMODExec(system.playSound(sound, masterChannelGroup, true, out channel));

                    // Set general Speech volume.
                    //TODO Set this in the GUI
                    volume = 1.0f;
                    FMODExec(channel.setVolume(volume));

                    if (Surround)
                    {
                        // Set attenuation limits so distant people get a little softer,
                        // but not TOO soft
                        if (Compress)
                            FMODExec(sound.set3DMinMaxDistance(
                                2f,       // Any closer than this gets no louder
                                3.5f));     // Further than this gets no softer.
                        else
                            FMODExec(sound.set3DMinMaxDistance(
                                2f,       // Any closer than this gets no louder
                                10f));     // Further than this gets no softer.

                        // Set speaker position.
                        position = FromOMVSpace(speakerPos);
                        FMODExec(channel.set3DAttributes(
                            ref position,
                            ref ZeroVector));

                        Logger.Log($"Speech at <{position.x:0.0},{position.y:0.0},{position.z:0.0}>", Helpers.LogLevel.Debug);
                    }

                    // SET a handler for when it finishes.
                    FMODExec(channel.setCallback(endCallback));
                    RegisterChannel(channel);

                    // Un-pause the sound.
                    FMODExec(channel.setPaused(false));

                    // Signal setup completion with length
                    try { tcs.TrySetResult(len); } catch { }
                }
                catch (Exception ex)
                {
                    Logger.Log("Error playing speech: ", Helpers.LogLevel.Error, ex);
                    try { tcs.TrySetResult(0); } catch { }
                }
            });

            // Wait up to 30 seconds for setup to complete (matches previous WaitOne timeout)
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (completed == tcs.Task)
            {
                try
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
                catch { return 0; }
            }

            return 0;
        }

        /// <summary>
        /// Handler for reaching the end of playback of a speech.
        /// </summary>
        /// <returns></returns>
        protected override RESULT EndCallbackHandler()
        {
            invoke(delegate
            {
                UnRegisterChannel();
                channel.clearHandle();
                UnRegisterSound();
                FMODExec(sound.release());
                sound.clearHandle();

                // Tell speech control the file has been played.  Note
                // the event is dispatched on FMOD's thread, to make sure
                // the event handler does not start a new sound before the
                // old one is cleaned up.
                if (OnSpeechDone != null)
                    try
                    {
                        OnSpeechDone(this, EventArgs.Empty);
                    }
                    catch (Exception) { }
            });

            return RESULT.OK;
        }
    }
}
