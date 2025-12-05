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
using System.Threading;
using System.Runtime.InteropServices;
using FMOD;
using OpenMetaverse;

namespace Radegast.Media
{
    public abstract class MediaObject : object, IDisposable
    {
        /// <summary>
        /// Indicates if this object's resources have already been disposed
        /// </summary>
        public bool Disposed { get; private set; } = false;

        protected bool finished = false;

        /// All commands are made through queued delegate calls, so they
        /// are guaranteed to take place in the same thread.  FMOD requires this.
        public delegate void SoundDelegate();

        /// Queue of sound commands
        ///
        protected static Queue<SoundDelegate> queue;
        protected static MediaManager manager;
        protected static Dictionary<UUID,BufferSound> allBuffers;


        // A SOUND represents the data (buffer or stream)
        public Sound FMODSound => sound;
        protected Sound sound;

        // A CHANNEL represents a playback instance of a sound.
        public Channel FMODChannel => channel;
        protected Channel channel;

        // Additional info for callbacks goes here.
        protected CREATESOUNDEXINFO extraInfo;
        protected static CHANNELCONTROL_CALLBACK endCallback;

        // Vectors used for orienting spatial axes.
        protected static VECTOR UpVector;
        protected static VECTOR ZeroVector;

        /// <summary>
        /// Base FMOD system object, of which there is only one.
        /// </summary>
        protected static FMOD.System system;
        public FMOD.System FMODSystem => system;

        protected static float AllObjectVolume = 0.8f;

        protected MediaObject()
        {
            // Zero out the extra info structure.
            extraInfo = new CREATESOUNDEXINFO();
            extraInfo.cbsize = Marshal.SizeOf(extraInfo);
        }

        protected bool Cloned = false;
        public virtual void Dispose()
        {
            if (!Cloned && sound.hasHandle())
            {
                sound.release();
                sound.clearHandle();
            }

            Disposed = true;
        }

        public bool Active => (sound.hasHandle());

        /// <summary>
        /// Put a delegate call on the command queue.  These will be executed on
        /// the FMOD control thread.   All FMOD calls must happen there.
        /// </summary>
        /// <param name="action"></param>
        protected void invoke(SoundDelegate action)
        {
            // Do nothing if queue not ready yet.
            if (queue == null) return;

            // Put that on the queue and wake up the background thread.
            lock (queue)
            {
                queue.Enqueue( action );
                Monitor.Pulse(queue);
            }

        }

        /// <summary>
        ///  Change a playback volume
        /// </summary>
        protected float volume = 0.8f;
        public float Volume
        {
            get => volume;
            set
            {
                volume = value;
                if (!channel.hasHandle()) return;

                invoke(delegate
                {
                    try
                    {
                        FMODExec(channel.setVolume(volume));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(
                            $"Error on volume change on channel {channel.handle.ToString("X")} sound {sound.handle.ToString("X")} finished {finished}", ex);
                    }
                });
            }
        }

        /// <summary>
        /// Update the 3D position of a sound source.
        /// </summary>
        protected VECTOR position = new VECTOR();
        public Vector3 Position
        {
            set
            {
                position = FromOMVSpace(value);
                if (!channel.hasHandle()) return;

                invoke(delegate
                {
                    try
                    {
                        FMODExec(channel.set3DAttributes(
                            ref position,
                            ref ZeroVector));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(
                            $"Error on position change on channel {channel.handle.ToString("X")} sound {sound.handle.ToString("X")} finished {finished}", ex);
                    }
                });

            }
        }

        public void Stop()
        {
            if (channel.hasHandle())
            {
                invoke(delegate
                {
                    FMODExec(channel.stop());
                });
            }
        }

        /// <summary>
        /// Convert OpenMetaVerse to FMOD coordinate space.
        /// </summary>
        /// <param name="omvV"></param>
        /// <returns></returns>
        protected VECTOR FromOMVSpace(Vector3 omvV)
        {
            // OMV  X is forward/East, Y is left/North, Z is up.
            // FMOD Z is forward/East, X is right/South, Y is up.
            VECTOR v = new VECTOR
            {
                x = -omvV.Y,
                y = omvV.Z,
                z = omvV.X
            };
            return v;
        }

        protected static Dictionary<IntPtr,MediaObject> allSounds;
        protected static Dictionary<IntPtr, MediaObject> allChannels;
        protected void RegisterSound(Sound sound)
        {
            IntPtr raw = sound.handle;
            allSounds.Remove(raw);
            allSounds.Add(raw, this);
        }
        protected void RegisterChannel(Channel channel)
        {
            IntPtr raw = channel.handle;
            allChannels.Remove(raw);
            allChannels.Add(raw, this);
        }
        protected void UnRegisterSound()
        {
            IntPtr raw = sound.handle;
            allSounds.Remove(raw);
        }
        protected void UnRegisterChannel()
        {
            IntPtr raw = channel.handle;
            allChannels.Remove(raw);
        }

        /// <summary>
        /// A callback for asynchronous FMOD calls.
        /// </summary>
        /// <returns></returns>

        // Subclasses override these methods to handle callbacks.
        protected virtual RESULT EndCallbackHandler() { return RESULT.OK; }

        /// <summary>
        /// Main handler for playback-end callback.
        /// </summary>
        /// <param name="channelraw"></param>
        /// <param name="controltype"></param>
        /// <param name="type"></param>
        /// <param name="commanddata1"></param>
        /// <param name="commanddata2"></param>
        /// <returns></returns>
        protected RESULT DispatchEndCallback(
            IntPtr channelraw,
            CHANNELCONTROL_TYPE controltype,
            CHANNELCONTROL_CALLBACK_TYPE type,
            IntPtr commanddata1,
            IntPtr commanddata2)
        {
            // There are several callback types
            if (type == CHANNELCONTROL_CALLBACK_TYPE.END)
            {
                if (allChannels.TryGetValue(channelraw, out var channelObj))
                {
                    return channelObj.EndCallbackHandler();
                }
            }

            return RESULT.OK;
        }

        protected static void FMODExec(RESULT result)
        {
            if (result != RESULT.OK)
            {
                throw new MediaException($"FMOD error! {result} - {Error.String(result)}");
            }
        }

        protected static void FMODExec(RESULT result, string trace)
        {
            Logger.Info($"FMOD call {trace} returned {result}");
            if (result != RESULT.OK)
            {
                throw new MediaException($"FMOD error! {result} - {Error.String(result)}");
            }
        }

 
    }
}
