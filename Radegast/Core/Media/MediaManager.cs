/*
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
using System.Linq;
using FMOD;
using System.Threading;
using OpenMetaverse;
using System.Threading.Tasks;

namespace Radegast.Media
{
    public class MediaManager : MediaObject
    {
        /// <summary>
        /// Indicated whether sound system is ready for use
        /// </summary>
        public bool SoundSystemAvailable { get; private set; } = false;
        public RadegastInstance Instance;

        private CancellationTokenSource soundCancelToken;

        private List<MediaObject> sounds = new List<MediaObject>();
        ManualResetEvent initDone = new ManualResetEvent(false);

        public MediaManager(RadegastInstance instance)
        {
            Instance = instance;
            manager = this;

            if (MainProgram.s_CommandLineOpts.DisableSound)
            {
                SoundSystemAvailable = false;
                return;
            }

            endCallback = DispatchEndCallback;
            allBuffers = new Dictionary<UUID, BufferSound>();

            soundCancelToken = new CancellationTokenSource();

            // Start the background thread that does the audio calls.
            Task soundTask = Task.Factory.StartNew(CommandLoop, 
                soundCancelToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // Start the background thread that updates listerner position.
            Task listenerTask = Task.Factory.StartNew(ListenerUpdate, 
                soundCancelToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Instance.ClientChanged += Instance_ClientChanged;

            // Wait for init to complete
            initDone.WaitOne();
            initDone = null;
        }

        void Instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            if (ObjectEnable)
                RegisterClientEvents(e.Client);
        }

        void RegisterClientEvents(GridClient client)
        {
            client.Sound.SoundTrigger += Sound_SoundTrigger;
            client.Sound.AttachedSound += Sound_AttachedSound;
            client.Sound.PreloadSound += Sound_PreloadSound;
            client.Objects.ObjectUpdate += Objects_ObjectUpdate;
            client.Objects.KillObject += Objects_KillObject;
            client.Network.SimChanged += Network_SimChanged;
            client.Self.ChatFromSimulator += Self_ChatFromSimulator;
        }

        void UnregisterClientEvents(GridClient client)
        {
            client.Sound.SoundTrigger -= Sound_SoundTrigger;
            client.Sound.AttachedSound -= Sound_AttachedSound;
            client.Sound.PreloadSound -= Sound_PreloadSound;
            client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
            client.Objects.KillObject -= Objects_KillObject;
            client.Network.SimChanged -= Network_SimChanged;
            client.Self.ChatFromSimulator -= Self_ChatFromSimulator;
        }

        /// <summary>
        /// Thread that processes FMOD calls.
        /// </summary>
        private void CommandLoop()
        {
            // Initialze a bunch of static values
            UpVector.x = 0.0f;
            UpVector.y = 1.0f;
            UpVector.z = 0.0f;
            ZeroVector.x = 0.0f;
            ZeroVector.y = 0.0f;
            ZeroVector.z = 0.0f;

            allSounds = new Dictionary<IntPtr, MediaObject>();
            allChannels = new Dictionary<IntPtr, MediaObject>();

            // Initialize the command queue.
            queue = new Queue<SoundDelegate>();

            // Initialize the FMOD sound package
            InitFMOD();
            initDone.Set();
            if (!SoundSystemAvailable) return;

            SoundDelegate action = null;

            while (true)
            {
                if (soundCancelToken.IsCancellationRequested)
                {
                    break;
                }
                // Wait for something to show up in the queue.
                lock (queue)
                {
                    while (queue.Count == 0)
                    {
                        Monitor.Wait(queue);
                    }
                    action = queue.Dequeue();
                }

                if (soundCancelToken.IsCancellationRequested)
                {
                    break;
                }
                // We have an action, so call it.
                try
                {
                    action();
                    action = null;
                }
                catch (Exception e)
                {
                    Logger.Log("Error in sound action:\n    " + e.Message + "\n" + e.StackTrace,
                        Helpers.LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Initialize the FMOD sound system.
        /// </summary>
        private void InitFMOD()
        {
            try
            {
                FMODExec(Factory.System_Create(out system));
                uint version = 0;
                FMODExec(system.getVersion(out version));

                if (version < VERSION.number)
                    throw new MediaException("You are using an old version of FMOD " +
                        version.ToString("X") +
                        ".  This program requires " +
                        VERSION.number.ToString("X") + ".");

                // Try to detect soud system used
                if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    bool audioOK = false;
                    var res = system.setOutput(OUTPUTTYPE.COREAUDIO);
                    if (res == RESULT.OK)
                    {
                        audioOK = true;
                    }

                    if (!audioOK)
                    {
                        res = system.setOutput(OUTPUTTYPE.PULSEAUDIO);
                        if (res == RESULT.OK)
                        {
                            audioOK = true;
                        }
                    }

                    if (!audioOK)
                    {
                        res = system.setOutput(OUTPUTTYPE.ALSA);
                        if (res == RESULT.OK)
                        {
                            audioOK = true;
                        }
                    }

                    if (!audioOK)
                    {
                        res = system.setOutput(OUTPUTTYPE.AUTODETECT);
                        if (res == RESULT.OK)
                        {
                            audioOK = true;
                        }
                    }

                }

                OUTPUTTYPE outputType = OUTPUTTYPE.UNKNOWN;
                FMODExec(system.getOutput(out outputType));

// *TODO: Investigate if this all is still needed under FMODStudio
#if false
                // The user has the 'Acceleration' slider set to off, which
                // is really bad for latency.  At 48khz, the latency between
                // issuing an fmod command and hearing it will now be about 213ms.
                if ((caps & FMOD.CAPS.HARDWARE_EMULATED) == FMOD.CAPS.HARDWARE_EMULATED)
                {
                    FMODExec(system.setDSPBufferSize(1024, 10));
                }

                try
                {
                    StringBuilder name = new StringBuilder(128);
                    // Get driver information so we can check for a wierd one.
                    Guid guid = new Guid();
                    FMODExec(system.getDriverInfo(0, name, 128, out guid));
    
                    // Sigmatel sound devices crackle for some reason if the format is pcm 16bit.
                    // pcm floating point output seems to solve it.
                    if (name.ToString().IndexOf("SigmaTel") != -1)
                    {
                        FMODExec(system.setSoftwareFormat(
                            48000,
                            FMOD.SOUND_FORMAT.PCMFLOAT,
                            0, 0,
                            FMOD.DSP_RESAMPLER.LINEAR)
                        );
                    }
                }
                catch {}
#endif
                // FMODExec(system.setDSPBufferSize(1024, 10));

                // Try to initialize with all those settings, and Max 32 channels.
                RESULT result = system.init(32, INITFLAGS.NORMAL, (IntPtr)null);
                if (result != RESULT.OK)
                {
                    throw(new Exception(result.ToString()));
                }

                // Set real-world effect scales.
                FMODExec(system.set3DSettings(
                    1.0f,   // Doppler scale
                    1.0f,   // Distance scale is meters
                    1.0f)   // Rolloff factor
                );

                SoundSystemAvailable = true;
                Logger.Log("Initialized FMOD interface: " + outputType, Helpers.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to initialize the sound system: " + ex, Helpers.LogLevel.Warning);
            }
        }

        public override void Dispose()
        {
            if (Instance.Client != null)
                UnregisterClientEvents(Instance.Client);

            lock (sounds)
            {
                foreach (var s in sounds)
                {
                    if (!s.Disposed)
                        s.Dispose();
                }
                sounds.Clear();
            }

            sounds = null;

            if (system.hasHandle())
            {
                Logger.Log("FMOD interface stopping", Helpers.LogLevel.Info);
                system.release();
                system.clearHandle();
            }

            soundCancelToken.Cancel();
            soundCancelToken.Dispose();

            base.Dispose();
        }

        /// <summary>
        /// Thread to update listener position and generally keep
        /// FMOD up to date.
        /// </summary>
        private void ListenerUpdate()
        {
            // Notice changes in position or direction.
            Vector3 lastpos = new Vector3(0.0f, 0.0f, 0.0f);
            float lastface = 0.0f;

            while (true)
            {
                if (soundCancelToken.IsCancellationRequested)
                {
                    break;
                }
                // Two updates per second.
                System.Threading.Thread.Sleep(500);

                if (!system.hasHandle()) continue;

                var my = Instance.Client.Self;
                Vector3 newPosition = new Vector3(my.SimPosition);
                float newFace = my.SimRotation.W;

                // If we are standing still, nothing to update now, but
                // FMOD needs a 'tick' anyway for callbacks, etc.  In looping
                // 'game' programs, the loop is the 'tick'.   Since Radegast
                // uses events and has no loop, we use this position update
                // thread to drive the FMOD tick.  Have to move more than
                // 500mm or turn more than 10 desgrees to bother with.
                //
                if (newPosition.ApproxEquals(lastpos, 0.5f) &&
                    Math.Abs(newFace - lastface) < 0.2)
                {
                    invoke(delegate
                    {
                        FMODExec(system.update());
                    });
                    continue;
                }

                // We have moved or turned.  Remember new position.
                lastpos = newPosition;
                lastface = newFace;

                // Convert coordinate spaces.
                VECTOR listenerpos = FromOMVSpace(newPosition);

                // Get azimuth from the facing Quaternion.  Note we assume the
                // avatar is standing upright.  Avatars in unusual positions
                // hear things from unpredictable directions.
                // By definition, facing.W = Cos( angle/2 )
                // With angle=0 meaning East.
                double angle = 2.0 * Math.Acos(newFace);

                // Construct facing unit vector in FMOD coordinates.
                // Z is East, X is South, Y is up.
                VECTOR forward = new VECTOR
                {
                    x = (float) Math.Sin(angle),
                    y = 0.0f,
                    z = (float) Math.Cos(angle)
                };
                // South
                // East

                //Logger.Log(
                //    String.Format(
                //        "Standing at <{0:0.0},{1:0.0},{2:0.0}> facing {3:d}",
                //            listenerpos.x,
                //            listenerpos.y,
                //            listenerpos.z,
                //            (int)(angle * 180.0 / 3.141592)),
                //    Helpers.LogLevel.Debug);

                // Tell FMOD the new orientation.
                invoke(delegate
                {
                    FMODExec(system.set3DListenerAttributes(
                        0,
                        ref listenerpos,    // Position
                        ref ZeroVector,        // Velocity
                        ref forward,        // Facing direction
                        ref UpVector));    // Top of head

                    FMODExec(system.update());
                });
            }
        }

        /// <summary>
        /// Handle request to play a sound, which might (or mioght not) have been preloaded.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sound_SoundTrigger(object sender, SoundTriggerEventArgs e)
        {
            if (e.SoundID == UUID.Zero) return;

            Logger.Log("Trigger sound " + e.SoundID +" in object " + e.ObjectID,
                Helpers.LogLevel.Debug);

            new BufferSound(
                e.ObjectID,
                e.SoundID,
                false,
                true,
                e.Position,
                e.Gain * ObjectVolume);
        }

        /// <summary>
        /// Handle sound attached to an object
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sound_AttachedSound(object sender, AttachedSoundEventArgs e)
        {
            // This event tells us the Object ID, but not the Prim info directly.
            // So we look it up in our internal Object memory.
            Simulator sim = e.Simulator;
            var kvp = sim.ObjectsPrimitives.FirstOrDefault(p2 => p2.Value.ID == e.ObjectID);
            if (kvp.Value == null) { return; }
            var p = kvp.Value;
            // Only one attached sound per prim, so we kill any previous
            BufferSound.Kill(p.ID);

            // If this is stop sound, we're done since we've already killed sound for this object
            if ((e.Flags & SoundFlags.Stop) == SoundFlags.Stop)
            {
                return;
            }

            // We seem to get a lot of these zero sounds.
            if (e.SoundID == UUID.Zero) return;

            // If this is a child prim, its position is relative to the root.
            Vector3 fullPosition = p.Position;

            while (p != null && p.ParentID != 0)
            {
                if (sim.ObjectsAvatars.TryGetValue(p.ParentID, out var av))
                {
                    p = av;
                    fullPosition += p.Position;
                }
                else
                {
                    if (sim.ObjectsPrimitives.TryGetValue(p.ParentID, out p))
                    {
                        fullPosition += p.Position;
                    }
                }
            }

            // Didn't find root prim
            if (p == null) { return; }

            new BufferSound(
                e.ObjectID,
                e.SoundID,
                (e.Flags & SoundFlags.Loop) == SoundFlags.Loop,
                true,
                fullPosition,
                e.Gain * ObjectVolume);
        }


        /// <summary>
        /// Handle request to preload a sound for playing later.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sound_PreloadSound(object sender, PreloadSoundEventArgs e)
        {
            if (e.SoundID == UUID.Zero) return;

            if (!Instance.Client.Assets.Cache.HasAsset(e.SoundID))
                new BufferSound(e.SoundID);
        }

        /// <summary>
        /// Handle object updates, looking for sound events
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            HandleObjectSound(e.Prim, e.Simulator);
        }

        /// <summary>
        /// Handle deletion of a noise-making object
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Objects_KillObject(object sender, KillObjectEventArgs e)
        {
            Primitive p = null;
            if (!e.Simulator.ObjectsPrimitives.TryGetValue(e.ObjectLocalID, out  p)) return;

            // Objects without sounds are not interesting.
            if (p.Sound == UUID.Zero) return;

            BufferSound.Kill(p.ID);
        }

        /// <summary>
        /// Common object sound processing for various Update events
        /// </summary>
        /// <param name="p"></param>
        /// <param name="s"></param>
        private void HandleObjectSound(Primitive p, Simulator s)
        {
            // Objects without sounds are not interesting.
            if (p.Sound == UUID.Zero) return;

            if ((p.SoundFlags & SoundFlags.Stop) == SoundFlags.Stop)
            {
                BufferSound.Kill(p.ID);
                return;
            }

            // If this is a child prim, its position is relative to the root prim.
            Vector3 fullPosition = p.Position;
            if (p.ParentID != 0)
            {
                Primitive parentP;
                if (!s.ObjectsPrimitives.TryGetValue(p.ParentID, out parentP)) return;
                fullPosition += parentP.Position;
            }

            // See if this is an update to  something we already know about.
            if (allBuffers.ContainsKey(p.ID))
            {
                // Exists already, so modify existing sound.
                BufferSound snd = allBuffers[p.ID];
                snd.Volume = p.SoundGain * ObjectVolume;
                snd.Position = fullPosition;
            }
            else
            {
                // Does not exist, so create a new one.
                new BufferSound(
                    p.ID,
                    p.Sound,
                    (p.SoundFlags & SoundFlags.Loop) == SoundFlags.Loop,
                    true,
                    fullPosition, //Instance.State.GlobalPosition(e.Simulator, fullPosition),
                    p.SoundGain * ObjectVolume);
            }
        }

        /// <summary>
        /// Control the volume of all inworld sounds
        /// </summary>
        public float ObjectVolume
        {
            set
            {
                AllObjectVolume = value;
                BufferSound.AdjustVolumes();
            }
            get => AllObjectVolume;
        }

        /// <summary>
        /// UI sounds volume
        /// </summary>
        public float UIVolume = 0.5f;

        private bool m_objectEnabled = true;
        /// <summary>
        /// Enable and Disable inworld sounds
        /// </summary>
        public bool ObjectEnable
        {
            set
            {
                if (value)
                {
                    // Subscribe to events about inworld sounds
                    RegisterClientEvents(Instance.Client);
                    Logger.Log("Inworld sound enabled", Helpers.LogLevel.Info);
                }
                else
                {
                    // Subscribe to events about inworld sounds
                    UnregisterClientEvents(Instance.Client);
                    // Stop all running sounds
                    BufferSound.KillAll();
                    Logger.Log("Inworld sound disabled", Helpers.LogLevel.Info);
                }

                m_objectEnabled = value;
            }
            get => m_objectEnabled;
        }

        void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            if (e.Type == ChatType.StartTyping)
            {
                new BufferSound(
                    UUID.Random(),
                    UISounds.Typing,
                    false,
                    true,
                    e.Position,
                    ObjectVolume / 2f);
            }
        }

        /// <summary>
        /// Watch for Teleports to cancel all the old sounds
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            BufferSound.KillAll();
        }

        /// <summary>
        /// Plays a sound
        /// </summary>
        /// <param name="sound">UUID of the sound to play</param>
        public void PlayUISound(UUID sound)
        {
            if (!SoundSystemAvailable) return;

            new BufferSound(
                UUID.Random(),
                sound,
                false,
                true,
                Instance.Client.Self.SimPosition,
                UIVolume);
        }


    }

    public class MediaException : Exception
    {
        public MediaException(string msg)
            : base(msg)
        {
        }
    }
}
