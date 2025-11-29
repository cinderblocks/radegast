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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FMOD;
using OpenMetaverse;

namespace Radegast.Media
{
    public class StreamInfoArgs : EventArgs
    {
        public string Key;
        public string Value;

        public StreamInfoArgs(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }

    public class Stream : MediaObject
    {
        /// <summary>
        /// Fired when a stream meta data is received
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Key, value are sent in e</param>
        public delegate void StreamInfoCallback(object sender, StreamInfoArgs e);

        /// <summary>
        /// Fired when a stream meta data is received
        /// </summary>
        public event StreamInfoCallback OnStreamInfo;

        private Timer updateTimer = null;
        private readonly uint updateIntervl = 500;

        /// <summary>
        /// Releases resources of this sound object
        /// </summary>
        public override void Dispose()
        {
            StopStream();
            base.Dispose();
        }

        public void StopStream()
        {
            // Preserve synchronous behavior for callers by waiting for async cleanup to finish
            try
            {
                StopStreamAsync(true).GetAwaiter().GetResult();
            }
            catch { }
        }

        /// <summary>
        /// Asynchronous stop implementation. When 'blocking' is true the returned Task will complete
        /// after the FMOD resources have been released inside the invoke callback.
        /// </summary>
        /// <param name="blocking">If true, waits for cleanup to finish</param>
        /// <returns></returns>
        public async Task StopStreamAsync(bool blocking = false)
        {
            if (updateTimer != null)
            {
                try
                {
                    updateTimer.Dispose();
                }
                catch { }

                updateTimer = null;
            }

            if (!channel.hasHandle()) return;

            TaskCompletionSource<bool> tcs = null;
            if (blocking) tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            invoke(delegate
            {
                try
                {
                    FMODExec(channel.stop());
                    channel.clearHandle();
                    UnRegisterSound();
                    FMODExec(sound.release());
                    sound.clearHandle();
                }
                catch { }

                if (blocking)
                {
                    try { tcs.TrySetResult(true); } catch { }
                }
            });

            if (blocking && tcs != null)
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Plays audio stream
        /// </summary>
        /// <param name="url">URL of the stream</param>
        public void PlayStream(string url)
        {
            // Stop old stream first.
            StopStream();

            extraInfo.format = SOUND_FORMAT.PCM16;

            invoke(delegate
            {
                try
                {
                    FMODExec(
                        system.setStreamBufferSize(4 * 128 * 128, TIMEUNIT.RAWBYTES));

                    FMODExec(
                        system.createSound(url,
                            (MODE._2D | MODE.CREATESTREAM),
                            ref extraInfo,
                            out sound), "Stream load");
                    // Register for callbacks.
                    RegisterSound(sound);

                    // Allocate a channel and set initial volume.
                    ChannelGroup masterChannelGroup;
                    system.getMasterChannelGroup(out masterChannelGroup);
                    FMODExec(system.playSound(sound, masterChannelGroup, false,
                        out channel), "Stream channel");
                    FMODExec(channel.setVolume(volume), "Stream volume");

                    if (updateTimer == null)
                    {
                        updateTimer = new Timer(Update);
                    }
                    updateTimer.Change(0, updateIntervl);
                }
                catch (Exception ex)
                {
                    Logger.Log("Error playing stream: " + ex, Helpers.LogLevel.Debug);
                }
            });
        }

        private void Update(object sender)
        {
            if (!sound.hasHandle()) return;

            invoke(() =>
            {
                try
                {
                    FMODExec(system.update());

                    TAG tag = new TAG();
                    var numTags = 0;
                    var numTagsUpdated = 0;

                    var res = sound.getNumTags(out numTags, out numTagsUpdated);

                    if (res != RESULT.OK || numTagsUpdated <= 0) return;
                    for (var i=0; i < numTags; i++)
                    {
                        if (sound.getTag(null, i, out tag) != RESULT.OK)
                        {
                            continue;
                        }

                        if (tag.type == TAGTYPE.FMOD && tag.name == "Sample Rate Change")
                        {
                            float newfreq = (float)Marshal.PtrToStructure(tag.data, typeof(float));
                            Logger.DebugLog("New stream frequency: " + newfreq.ToString("F" + 0));
                            channel.setFrequency(newfreq);
                        }

                        if (tag.datatype != TAGDATATYPE.STRING) continue;

                        // Tell listeners about the Stream tag.  This can be
                        // displayed to the user.
                        OnStreamInfo?.Invoke(this, new StreamInfoArgs(tag.name.ToString().ToLower(), Marshal.PtrToStringAnsi(tag.data)));
                    }
                }
                catch (Exception ex)
                {
                    Logger.DebugLog("Error getting stream tags: " + ex.Message);
                }
            });
        }
    }
}
