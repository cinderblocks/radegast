/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2026, Sjofn, LLC
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
using OpenMetaverse.StructuredData;
using System.Threading.Tasks;

namespace Radegast.Media
{
    public class MediaManager : MediaObject
    {
        /// <summary>
        /// Indicated whether sound system is ready for use
        /// </summary>
        public bool SoundSystemAvailable { get; private set; } = false;
        
        /// <summary>
        /// Event fired when sound system availability changes
        /// </summary>
        public event EventHandler<SoundSystemAvailableEventArgs> SoundSystemAvailableChanged;
        
        public IRadegastInstance Instance;

        private readonly CancellationTokenSource soundCancelToken;

        private List<MediaObject> sounds = new List<MediaObject>();
        private Task commandLoop;
        private Task listenerLoop;

        /// <summary>
        /// Currently selected audio driver index
        /// </summary>
        public int SelectedDriver { get; private set; } = 0;

        /// <summary>
        /// Number of available audio drivers
        /// </summary>
        public int DriverCount { get; private set; } = 0;

        /// <summary>
        /// DSP buffer size for FMOD (affects latency vs stability)
        /// </summary>
        public int DSPBufferSize { get; set; } = 1024;

        /// <summary>
        /// Number of DSP buffers (affects latency vs stability)
        /// </summary>
        public int DSPBufferCount { get; set; } = 4;

        /// <summary>
        /// Preferred audio driver index to use (set before Initialize)
        /// </summary>
        public int PreferredDriver { get; set; } = -1;

        public MediaManager(IRadegastInstance instance)
        {
            Instance = instance;
            manager = this;

            endCallback = DispatchEndCallback;
            allBuffers = new Dictionary<UUID, BufferSound>();

            soundCancelToken = new CancellationTokenSource();

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

            Instance.ClientChanged += Instance_ClientChanged;

            // Defer FMOD initialization until UI is ready. Call Initialize() to start FMOD and threads.
        }

        /// <summary>
        /// Attempt to initialize FMOD and start background threads. Safe to call multiple times.
        /// </summary>
        public void Initialize()
        {
            try
            {
                if (SoundSystemAvailable) return;

                InitFMOD();

                if (SoundSystemAvailable)
                {
                    // Start the background thread that does the audio calls.
                    commandLoop = Task.Factory.StartNew(() => CommandLoop(soundCancelToken.Token),
                        soundCancelToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    // Start the background thread that updates listener position.
                    listenerLoop = Task.Factory.StartNew(() => ListenerUpdate(soundCancelToken.Token),
                        soundCancelToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to initialize MediaManager", ex);
            }
        }

        private void Instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            if (ObjectEnable)
                RegisterClientEvents(e.Client);
        }

        private void RegisterClientEvents(GridClient client)
        {
            client.Sound.SoundTrigger += Sound_SoundTrigger;
            client.Sound.AttachedSound += Sound_AttachedSound;
            client.Sound.PreloadSound += Sound_PreloadSound;
            client.Objects.ObjectUpdate += Objects_ObjectUpdate;
            client.Objects.KillObject += Objects_KillObject;
            client.Network.SimChanged += Network_SimChanged;
            client.Self.ChatFromSimulator += Self_ChatFromSimulator;
        }

        private void UnregisterClientEvents(GridClient client)
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
        private void CommandLoop(CancellationToken token)
        {
            if (!SoundSystemAvailable) { return; }

            while (!token.IsCancellationRequested)
            {
                // Wait for something to show up in the queue; use a short timeout
                // so we can observe cancellation periodically.
                SoundDelegate action = null;
                lock (queue)
                {
                    while (queue.Count == 0 && !token.IsCancellationRequested)
                    {
                        // Use a timed wait to allow cancellation to be observed.
                        Monitor.Wait(queue, 100);
                    }

                    if (queue.Count > 0)
                    {
                        action = queue.Dequeue();
                    }
                }

                if (token.IsCancellationRequested) break;

                if (action == null) continue;

                // We have an action, so call it.
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in sound action: {ex.Message}", ex);
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
                    OUTPUTTYPE[] outputsToTry = Environment.OSVersion.Platform == PlatformID.MacOSX
                        ? new[] { OUTPUTTYPE.COREAUDIO, OUTPUTTYPE.AUTODETECT }
                        : new[] { OUTPUTTYPE.PULSEAUDIO, OUTPUTTYPE.ALSA, OUTPUTTYPE.AUTODETECT };

                    foreach (var output in outputsToTry)
                    {
                        var res = system.setOutput(output);
                        if (res == RESULT.OK)
                        {
                            Logger.Info($"Successfully set FMOD output to: {output}");
                            audioOK = true;
                            break;
                        }
                        else
                        {
                            Logger.Debug($"Failed to set FMOD output to {output}: {res}");
                        }
                    }

                    if (!audioOK)
                    {
                        Logger.Warn("Failed to set any audio output on Unix/Mac platform");
                    }
                }

                OUTPUTTYPE outputType = OUTPUTTYPE.UNKNOWN;
                FMODExec(system.getOutput(out outputType));

                // Get driver information
                FMODExec(system.getNumDrivers(out int numDrivers));
                DriverCount = numDrivers;
                Logger.Info($"FMOD detected {numDrivers} audio driver(s)");

                // Apply preferred driver selection if set
                if (PreferredDriver >= 0 && PreferredDriver < numDrivers)
                {
                    Logger.Info($"Applying preferred audio driver: {PreferredDriver}");
                    var driverResult = system.setDriver(PreferredDriver);
                    if (driverResult == RESULT.OK)
                    {
                        SelectedDriver = PreferredDriver;
                        Logger.Info($"Successfully set preferred audio driver to index {PreferredDriver}");
                    }
                    else
                    {
                        Logger.Warn($"Failed to set preferred audio driver {PreferredDriver}: {driverResult}");
                        PreferredDriver = -1; // Reset to default
                    }
                }
                else if (PreferredDriver >= numDrivers)
                {
                    Logger.Warn($"Preferred driver index {PreferredDriver} is out of range (0-{numDrivers - 1})");
                    PreferredDriver = -1; // Reset to default
                }

                // Log information about each driver
                for (int i = 0; i < numDrivers && i < 10; i++) // Limit to first 10 to avoid spam
                {
                    try
                    {
                        FMODExec(system.getDriverInfo(i, out string name, 256, out Guid guid, out int systemRate, out SPEAKERMODE speakerMode, out int speakerModeChannels));
                        Logger.Info($"Driver {i}: {name} - {systemRate}Hz, {speakerMode} ({speakerModeChannels} channels)");
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not get info for driver {i}: {ex.Message}");
                    }
                }

// *TODO: Investigate if this all is still needed under FMODStudio
#if false
                // The user has the 'Acceleration' slider set to off, which
                // is terrible for latency.  At 48khz, the latency between
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
                // Apply DSP buffer settings if configured
                if (DSPBufferSize > 0 && DSPBufferCount > 0)
                {
                    var bufferResult = system.setDSPBufferSize((uint)DSPBufferSize, DSPBufferCount);
                    if (bufferResult == RESULT.OK)
                    {
                        Logger.Info($"Set DSP buffer: {DSPBufferSize} samples x {DSPBufferCount} buffers");
                    }
                    else
                    {
                        Logger.Debug($"Could not set DSP buffer size: {bufferResult}");
                    }
                }

                // Try to initialize with all those settings, and Max 32 channels.
                RESULT result = system.init(32, INITFLAGS.NORMAL, (IntPtr)null);
                if (result != RESULT.OK)
                {
                    string errorMsg = $"FMOD initialization failed with result: {result} - {Error.String(result)}";
                    
                    // Provide specific guidance based on error type
                    switch (result)
                    {
                        case RESULT.ERR_OUTPUT_INIT:
                            errorMsg += "\nPossible causes: No audio device available, audio device in use by another application, or audio drivers need updating.";
                            break;
                        case RESULT.ERR_OUTPUT_CREATEBUFFER:
                            errorMsg += "\nPossible cause: Audio buffer creation failed. Try closing other audio applications.";
                            break;
                        case RESULT.ERR_OUTPUT_DRIVERCALL:
                            errorMsg += "\nPossible cause: Audio driver error. Check your audio drivers are up to date.";
                            break;
                        case RESULT.ERR_INVALID_PARAM:
                            errorMsg += "\nPossible cause: Invalid FMOD initialization parameters.";
                            break;
                    }
                    
                    Logger.Warn(errorMsg);
                    throw(new Exception(result.ToString()));
                }

                // Set real-world effect scales.
                FMODExec(system.set3DSettings(
                    1.0f,   // Doppler scale
                    1.0f,   // Distance scale is meters
                    1.0f)   // Rolloff factor
                );

                SoundSystemAvailable = true;
                Logger.Info($"Initialized FMOD interface: {outputType}");
                
                // Notify listeners that sound system is now available
                SoundSystemAvailableChanged?.Invoke(this, new SoundSystemAvailableEventArgs(true));
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to initialize the sound system", ex);
                SoundSystemAvailable = false;
                
                // Notify listeners that sound system failed to initialize
                SoundSystemAvailableChanged?.Invoke(this, new SoundSystemAvailableEventArgs(false));
            }
        }

        public override void Dispose()
        {
            if (Instance.Client != null)
                UnregisterClientEvents(Instance.Client);

            lock (sounds)
            {
                foreach (var s in sounds.Where(s => !s.Disposed))
                {
                    s.Dispose();
                }

                sounds.Clear();
            }

            sounds = null;

            if (system.hasHandle())
            {
                Logger.Info("FMOD interface stopping");
                system.release();
                system.clearHandle();
            }

            // Request cancellation of background loops and wait briefly for them to exit.
            try
            {
                soundCancelToken.Cancel();
                var tasks = new List<Task>();
                if (commandLoop != null) tasks.Add(commandLoop);
                if (listenerLoop != null) tasks.Add(listenerLoop);
                if (tasks.Count > 0)
                {
                    Task.WaitAll(tasks.ToArray(), 2000);
                }
            }
            catch (AggregateException) { }
            catch (Exception) { }
            finally
            {
                soundCancelToken.Dispose();
            }

            base.Dispose();
        }

        /// <summary>
        /// Thread to update listener position and generally keep
        /// FMOD up to date.
        /// </summary>
        private void ListenerUpdate(CancellationToken token)
        {
            // Notice changes in position or direction.
            Vector3 lastpos = new Vector3(0.0f, 0.0f, 0.0f);
            float lastface = 0.0f;

            while (!token.IsCancellationRequested)
            {
                // Two updates per second. Use wait handle so cancellation is observed quickly.
                if (token.WaitHandle.WaitOne(500)) break;

                if (!SoundSystemAvailable || !system.hasHandle()) continue;

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
        /// Handle request to play a sound, which might (or might not) have been preloaded.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sound_SoundTrigger(object sender, SoundTriggerEventArgs e)
        {
            if (!SoundSystemAvailable) return;
            if (e.SoundID == UUID.Zero) return;

            Logger.Debug($"Trigger sound {e.SoundID} in object {e.ObjectID}");

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
            if (!SoundSystemAvailable) return;
            
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
            if (!SoundSystemAvailable) return;
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
            if (!SoundSystemAvailable) return;
            HandleObjectSound(e.Prim, e.Simulator);
        }

        /// <summary>
        /// Handle deletion of a noise-making object
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Objects_KillObject(object sender, KillObjectEventArgs e)
        {
            if (!SoundSystemAvailable) return;
            
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
            if (!SoundSystemAvailable) return;
            
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
                    Logger.Info("Inworld sound enabled");
                }
                else
                {
                    // Subscribe to events about inworld sounds
                    UnregisterClientEvents(Instance.Client);
                    // Stop all running sounds
                    BufferSound.KillAll();
                    Logger.Info("Inworld sound disabled");
                }

                m_objectEnabled = value;
            }
            get => m_objectEnabled;
        }

        private void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            if (!SoundSystemAvailable) return;
            
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
        private void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            if (!SoundSystemAvailable) return;
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

        /// <summary>
        /// Get information about available audio drivers
        /// </summary>
        /// <returns>List of driver information</returns>
        public List<AudioDriverInfo> GetAudioDrivers()
        {
            var drivers = new List<AudioDriverInfo>();
            
            if (!system.hasHandle())
                return drivers;

            try
            {
                FMODExec(system.getNumDrivers(out int numDrivers));
                
                for (int i = 0; i < numDrivers; i++)
                {
                    try
                    {
                        FMODExec(system.getDriverInfo(i, out string name, 256, out Guid guid, out int systemRate, out SPEAKERMODE speakerMode, out int speakerModeChannels));
                        
                        drivers.Add(new AudioDriverInfo
                        {
                            Index = i,
                            Name = name,
                            Guid = guid,
                            SampleRate = systemRate,
                            SpeakerMode = speakerMode.ToString(),
                            Channels = speakerModeChannels,
                            IsDefault = (i == SelectedDriver)
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not get info for driver {i}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to enumerate audio drivers", ex);
            }

            return drivers;
        }

        /// <summary>
        /// Set the audio driver to use
        /// </summary>
        /// <param name="driverIndex">Index of the driver to use</param>
        /// <returns>True if successful</returns>
        public bool SetAudioDriver(int driverIndex)
        {
            if (!system.hasHandle())
                return false;

            try
            {
                var result = system.setDriver(driverIndex);
                if (result == RESULT.OK)
                {
                    SelectedDriver = driverIndex;
                    Logger.Info($"Successfully set audio driver to index {driverIndex}");
                    return true;
                }
                else
                {
                    Logger.Warn($"Failed to set audio driver to index {driverIndex}: {result} - {Error.String(result)}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception setting audio driver to index {driverIndex}", ex);
                return false;
            }
        }

        /// <summary>
        /// Retry FMOD initialization after a failure
        /// </summary>
        /// <returns>True if initialization succeeded</returns>
        public bool RetryInitialization()
        {
            if (SoundSystemAvailable)
            {
                Logger.Info("Sound system already initialized");
                return true;
            }

            Logger.Info("Retrying FMOD initialization...");
            
            try
            {
                // Clean up any partial initialization
                if (system.hasHandle())
                {
                    system.release();
                    system.clearHandle();
                }

                InitFMOD();

                if (SoundSystemAvailable)
                {
                    Logger.Info("FMOD initialization retry successful");
                    
                    // Restart background threads if needed
                    if (commandLoop == null || commandLoop.Status != TaskStatus.Running)
                    {
                        commandLoop = Task.Factory.StartNew(() => CommandLoop(soundCancelToken.Token),
                            soundCancelToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }
                    
                    if (listenerLoop == null || listenerLoop.Status != TaskStatus.Running)
                    {
                        listenerLoop = Task.Factory.StartNew(() => ListenerUpdate(soundCancelToken.Token),
                            soundCancelToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("FMOD initialization retry failed", ex);
            }

            return false;
        }


    }

    /// <summary>
    /// Event args for sound system availability changes
    /// </summary>
    public class SoundSystemAvailableEventArgs : EventArgs
    {
        public bool IsAvailable { get; }
        
        public SoundSystemAvailableEventArgs(bool isAvailable)
        {
            IsAvailable = isAvailable;
        }
    }

    /// <summary>
    /// Information about an audio driver
    /// </summary>
    public class AudioDriverInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public Guid Guid { get; set; }
        public int SampleRate { get; set; }
        public string SpeakerMode { get; set; }
        public int Channels { get; set; }
        public bool IsDefault { get; set; }

        public override string ToString()
        {
            return $"{Name} ({SampleRate}Hz, {Channels}ch)";
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
