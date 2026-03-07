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
using System.Reflection;
using System.Runtime.InteropServices;
using FMOD;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Assets;
using System.Threading.Tasks;
using System.IO;

namespace Radegast.Media
{
    public class MediaManager : MediaObject
    {
        // P/Invoke declarations for DLL loading on Windows
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

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
        private Task deviceMonitorLoop;

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

        /// <summary>
        /// Maximum allowed volume (safety limiter to prevent ear damage)
        /// </summary>
        public float MaxVolume { get; set; } = 1.0f;

        /// <summary>
        /// Enable volume normalization (reduces sudden loud sounds)
        /// </summary>
        public bool VolumeNormalization { get; set; } = false;

        /// <summary>
        /// Performance statistics
        /// </summary>
        public AudioPerformanceStats PerformanceStats { get; } = new AudioPerformanceStats();

        /// <summary>
        /// Sound cache for preloading and caching audio assets
        /// </summary>
        public SoundCache Cache { get; private set; }

        private float m_masterVolume = 1.0f;
        /// <summary>
        /// Master volume - affects all audio output (0.0 to 1.0)
        /// </summary>
        public float MasterVolume
        {
            get => m_masterVolume;
            set
            {
                m_masterVolume = Math.Max(0.0f, Math.Min(1.0f, value));
                UpdateAllVolumes();
            }
        }

        private bool m_muteAll = false;
        /// <summary>
        /// Mute all audio output
        /// </summary>
        public bool MuteAll
        {
            get => m_muteAll;
            set
            {
                m_muteAll = value;
                UpdateAllVolumes();
            }
        }

        /// <summary>
        /// 3D audio distance model
        /// </summary>
        public DistanceModel CurrentDistanceModel { get; set; } = DistanceModel.Linear;

        /// <summary>
        /// Maximum distance for sound audibility (in meters)
        /// </summary>
        public float MaxSoundDistance { get; set; } = 100.0f;

        /// <summary>
        /// Rolloff factor for distance attenuation
        /// </summary>
        public float DistanceRolloff { get; set; } = 1.0f;

        /// <summary>
        /// Get predefined audio profiles
        /// </summary>
        public static List<AudioProfile> GetPredefinedProfiles()
        {
            return new List<AudioProfile>
            {
                new AudioProfile("Balanced")
                {
                    MasterVolume = 1.0f,
                    ObjectVolume = 0.8f,
                    UIVolume = 0.5f,
                    MusicVolume = 0.5f,
                    DistanceModel = DistanceModel.Linear,
                    MaxDistance = 100.0f,
                    Rolloff = 1.0f,
                    ObjectSoundsEnabled = true
                },
                new AudioProfile("Headphones")
                {
                    MasterVolume = 0.7f,
                    ObjectVolume = 0.6f,
                    UIVolume = 0.4f,
                    MusicVolume = 0.4f,
                    DistanceModel = DistanceModel.Inverse,
                    MaxDistance = 80.0f,
                    Rolloff = 1.2f,
                    ObjectSoundsEnabled = true
                },
                new AudioProfile("Speakers")
                {
                    MasterVolume = 1.0f,
                    ObjectVolume = 0.9f,
                    UIVolume = 0.6f,
                    MusicVolume = 0.6f,
                    DistanceModel = DistanceModel.Linear,
                    MaxDistance = 120.0f,
                    Rolloff = 0.8f,
                    ObjectSoundsEnabled = true
                },
                new AudioProfile("Quiet Mode")
                {
                    MasterVolume = 0.3f,
                    ObjectVolume = 0.2f,
                    UIVolume = 0.3f,
                    MusicVolume = 0.2f,
                    DistanceModel = DistanceModel.Exponential,
                    MaxDistance = 50.0f,
                    Rolloff = 1.5f,
                    ObjectSoundsEnabled = true
                },
                new AudioProfile("Music Focus")
                {
                    MasterVolume = 1.0f,
                    ObjectVolume = 0.3f,
                    UIVolume = 0.4f,
                    MusicVolume = 0.8f,
                    DistanceModel = DistanceModel.Exponential,
                    MaxDistance = 60.0f,
                    Rolloff = 1.5f,
                    ObjectSoundsEnabled = true
                },
                new AudioProfile("Max Immersion")
                {
                    MasterVolume = 1.0f,
                    ObjectVolume = 1.0f,
                    UIVolume = 0.5f,
                    MusicVolume = 0.7f,
                    DistanceModel = DistanceModel.InverseSquare,
                    MaxDistance = 150.0f,
                    Rolloff = 1.0f,
                    ObjectSoundsEnabled = true
                }
            };
        }

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

            // Initialize sound cache
            Cache = new SoundCache(Instance, 50 * 1024 * 1024); // 50MB default

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
                    // Start device monitoring thread
                    deviceMonitorLoop = Task.Factory.StartNew(() => MonitorAudioDevices(soundCancelToken.Token),
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
            client.Network.Disconnected += Network_Disconnected;
            client.Network.LoggedOut += Network_LoggedOut;
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
            client.Network.Disconnected -= Network_Disconnected;
            client.Network.LoggedOut -= Network_LoggedOut;
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
                    PerformanceStats.QueueDepth = queue.Count;
                    
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
                var startTime = DateTime.UtcNow;
                try
                {
                    action();
                    PerformanceStats.SuccessfulOperations++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in sound action: {ex.Message}", ex);
                    PerformanceStats.FailedOperations++;
                }
                finally
                {
                    var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    PerformanceStats.UpdateAverageProcessingTime(duration);
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
                    DistanceRolloff)   // Rolloff factor
                );

                SoundSystemAvailable = true;
                Logger.Info($"Initialized FMOD interface: {outputType}");
                
                // Preload UI sounds after initialization
                Cache.PreloadUISounds();
                
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

        /// <summary>
        /// Determine the correct architecture folder name for native DLLs
        /// </summary>
        private static string GetArchitectureFolder()
        {
            // Try to use RuntimeInformation if available (more reliable)
            try
            {
                var arch = RuntimeInformation.ProcessArchitecture;
                switch (arch)
                {
                    case Architecture.X64:
                        return "x64";
                    case Architecture.X86:
                        return "x86";
                    case Architecture.Arm64:
                        return "arm64";
                    case Architecture.Arm:
                        return "arm";
                    default:
                        Logger.Debug($"Unknown architecture from RuntimeInformation: {arch}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Could not determine architecture via RuntimeInformation: {ex.Message}");
            }

            // Fallback to Environment.Is64BitProcess
            if (Environment.Is64BitProcess)
            {
                return "x64";
            }
            else
            {
                return "x86";
            }
        }

        /// <summary>
        /// Load a native library from architecture-specific paths (Windows only)
        /// </summary>
        /// <param name="libraryName">Name of the DLL without extension (e.g., "fmod", "nvdaControllerClient")</param>
        /// <returns>True if library was found and loaded, false otherwise</returns>
        private static bool LoadNativeLibrary(string libraryName)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Logger.Debug($"Skipping native library loading for {libraryName} on non-Windows platform");
                return false;
            }

            string dllName = $"{libraryName}.dll";
            
            // Check if already loaded
            IntPtr existingHandle = GetModuleHandle(libraryName);
            if (existingHandle != IntPtr.Zero)
            {
                Logger.Debug($"{libraryName} library already loaded");
                return true;
            }

            // Determine the correct architecture
            string architecture = GetArchitectureFolder();
            Logger.Info($"Attempting to load {libraryName} for architecture: {architecture}");
            
            // Try multiple possible DLL locations
            var possiblePaths = new List<string>();

            // Get the application base directory
            string appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            
            // Primary installation paths - dll subdirectory (matches LibreMetaverse convention)
            possiblePaths.Add(Path.Combine(appDirectory, "dll", architecture, dllName));
            possiblePaths.Add(Path.Combine(appDirectory, architecture, dllName));
            possiblePaths.Add(Path.Combine(appDirectory, dllName));
            
            // If we're 64-bit but looking for arm64 and it doesn't exist, try x64 as fallback (emulation)
            if (architecture == "arm64")
            {
                possiblePaths.Add(Path.Combine(appDirectory, "dll", "x64", dllName));
                possiblePaths.Add(Path.Combine(appDirectory, "x64", dllName));
            }
            
            // Check Program Files if we're installed there
            if (appDirectory.Contains("Program Files"))
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string radegastPath = Path.Combine(programFiles, "Radegast");
                possiblePaths.Add(Path.Combine(radegastPath, "dll", architecture, dllName));
                possiblePaths.Add(Path.Combine(radegastPath, architecture, dllName));
                
                if (architecture == "arm64")
                {
                    possiblePaths.Add(Path.Combine(radegastPath, "dll", "x64", dllName));
                    possiblePaths.Add(Path.Combine(radegastPath, "x64", dllName));
                }
            }

            string foundPath = null;
            string foundDirectory = null;

            // Find the first path that exists
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    foundPath = path;
                    foundDirectory = Path.GetDirectoryName(path);
                    Logger.Info($"Found {libraryName} library at: {path}");
                    break;
                }
                else
                {
                    Logger.Debug($"{libraryName} library not found at: {path}");
                }
            }

            if (foundPath == null)
            {
                Logger.Debug($"Could not locate {libraryName} library for {architecture} architecture. Searched paths:");
                foreach (var path in possiblePaths)
                {
                    Logger.Debug($"  - {path}");
                }
                return false;
            }

            try
            {
                // Set the DLL directory so dependent DLLs can be found
                if (SetDllDirectory(foundDirectory))
                {
                    Logger.Debug($"Set DLL directory to: {foundDirectory}");
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Logger.Debug($"Failed to set DLL directory to: {foundDirectory}, Win32 error code: {error}");
                }

                // Manually load the library
                IntPtr handle = LoadLibrary(foundPath);
                if (handle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Logger.Warn($"Failed to load {libraryName} library from {foundPath}, Win32 error code: {error}");
                    return false;
                }
                else
                {
                    Logger.Info($"Successfully loaded {libraryName} library from: {foundPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while loading {libraryName} library: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Ensure FMOD native library can be found and loaded (Windows only)
        /// </summary>
        private void EnsureFMODLibraryLoaded()
        {
            LoadNativeLibrary("fmod");
        }

        /// <summary>
        /// Load all required native libraries for the application
        /// </summary>
        public static void LoadNativeLibraries()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Logger.Debug("Skipping native library loading on non-Windows platform");
                return;
            }

            Logger.Info("Loading native libraries...");

            // List of native libraries to load
            var libraries = new[] { "fmod", "nvdaControllerClient", "UniversalSpeech" };
            
            foreach (var library in libraries)
            {
                try
                {
                    LoadNativeLibrary(library);
                }
                catch (Exception ex)
                {
                    // Don't fail startup if optional libraries can't be loaded
                    Logger.Debug($"Could not load optional native library {library}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Update all volume levels based on master volume and mute state
        /// </summary>
        private void UpdateAllVolumes()
        {
            if (MuteAll)
            {
                // Muted - set all to 0
                BufferSound.AdjustVolumes(0);
            }
            else
            {
                // Apply master volume
                BufferSound.AdjustVolumes(MasterVolume);
            }
        }

        public override void Dispose()
        {
            if (Instance.Client != null)
                UnregisterClientEvents(Instance.Client);

            // Dispose cache
            Cache?.Dispose();

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
                if (deviceMonitorLoop != null) tasks.Add(deviceMonitorLoop);
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
        /// Monitor for audio device changes (hotplug/unplug)
        /// </summary>
        private void MonitorAudioDevices(CancellationToken token)
        {
            int lastDriverCount = DriverCount;

            while (!token.IsCancellationRequested)
            {
                // Check every 5 seconds
                if (token.WaitHandle.WaitOne(5000)) break;

                if (!SoundSystemAvailable || !system.hasHandle()) continue;

                try
                {
                    invoke(delegate
                    {
                        system.getNumDrivers(out int currentDriverCount);
                        
                        if (currentDriverCount != lastDriverCount)
                        {
                            Logger.Info($"Audio device change detected: {lastDriverCount} -> {currentDriverCount} device(s)");
                            DriverCount = currentDriverCount;
                            lastDriverCount = currentDriverCount;
                            
                            // Fire event to notify UI
                            AudioDevicesChanged?.Invoke(this, new AudioDevicesChangedEventArgs(currentDriverCount));
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error monitoring audio devices: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Event fired when audio devices are added or removed
        /// </summary>
        public event EventHandler<AudioDevicesChangedEventArgs> AudioDevicesChanged;

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
        /// Stop all sounds when disconnected from the grid
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            if (!SoundSystemAvailable) return;
            BufferSound.KillAll();
            Logger.Debug("Stopped all sounds due to disconnection", Instance.Client);
        }

        /// <summary>
        /// Stop all sounds when logged out
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Network_LoggedOut(object sender, LoggedOutEventArgs e)
        {
            if (!SoundSystemAvailable) return;
            BufferSound.KillAll();
            Logger.Debug("Stopped all sounds due to logout", Instance.Client);
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
                    
                    if (deviceMonitorLoop == null || deviceMonitorLoop.Status != TaskStatus.Running)
                    {
                        deviceMonitorLoop = Task.Factory.StartNew(() => MonitorAudioDevices(soundCancelToken.Token),
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

        /// <summary>
        /// Get detailed performance information from FMOD
        /// </summary>
        public FMODPerformanceInfo GetPerformanceInfo()
        {
            var info = new FMODPerformanceInfo
            {
                Stats = PerformanceStats.ToString(),
                SoundSystemAvailable = SoundSystemAvailable,
                DriverCount = DriverCount,
                SelectedDriver = SelectedDriver
            };

            if (!system.hasHandle())
                return info;

            try
            {
                invoke(delegate
                {
                    // Get CPU usage
                    system.getCPUUsage(out float dsp, out float stream, out float geometry, out float update, out float total);
                    info.DSPUsage = dsp;
                    info.StreamUsage = stream;
                    info.GeometryUsage = geometry;
                    info.UpdateUsage = update;
                    info.TotalUsage = total;

                    // Get channel count
                    system.getChannelsPlaying(out int channelsPlaying, out int realChannels);
                    info.ChannelsPlaying = channelsPlaying;
                    info.RealChannels = realChannels;
                });
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error getting FMOD performance info: {ex.Message}");
            }

            return info;
        }


    }

    /// <summary>
    /// FMOD performance information
    /// </summary>
    public class FMODPerformanceInfo
    {
        public string Stats { get; set; }
        public bool SoundSystemAvailable { get; set; }
        public int DriverCount { get; set; }
        public int SelectedDriver { get; set; }
        public float DSPUsage { get; set; }
        public float StreamUsage { get; set; }
        public float GeometryUsage { get; set; }
        public float UpdateUsage { get; set; }
        public float TotalUsage { get; set; }
        public int ChannelsPlaying { get; set; }
        public int RealChannels { get; set; }

        public override string ToString()
        {
            return $"Available: {SoundSystemAvailable}, Drivers: {DriverCount}, " +
                   $"Channels: {ChannelsPlaying}/{RealChannels}, " +
                   $"CPU: {TotalUsage:F2}% (DSP: {DSPUsage:F2}%, Stream: {StreamUsage:F2}%), " +
                   $"{Stats}";
        }
    }

    /// <summary>
    /// 3D audio distance attenuation models
    /// </summary>
    public enum DistanceModel
    {
        /// <summary>Linear rolloff (default)</summary>
        Linear,
        /// <summary>Inverse distance (realistic)</summary>
        Inverse,
        /// <summary>Inverse square (physics-based)</summary>
        InverseSquare,
        /// <summary>Exponential rolloff</summary>
        Exponential,
        /// <summary>No distance attenuation</summary>
        None
    }

    /// <summary>
    /// Audio profile for saving/loading audio configurations
    /// </summary>
    public class AudioProfile
    {
        public string Name { get; set; }
        public float MasterVolume { get; set; } = 1.0f;
        public float ObjectVolume { get; set; } = 0.8f;
        public float UIVolume { get; set; } = 0.5f;
        public float MusicVolume { get; set; } = 0.5f;
        public int PreferredDriver { get; set; } = -1;
        public DistanceModel DistanceModel { get; set; } = DistanceModel.Linear;
        public float MaxDistance { get; set; } = 100.0f;
        public float Rolloff { get; set; } = 1.0f;
        public bool ObjectSoundsEnabled { get; set; } = true;

        public AudioProfile() { }

        public AudioProfile(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Apply this profile to a MediaManager
        /// </summary>
        public void ApplyTo(MediaManager manager)
        {
            manager.MasterVolume = MasterVolume;
            manager.ObjectVolume = ObjectVolume;
            manager.UIVolume = UIVolume;
            manager.CurrentDistanceModel = DistanceModel;
            manager.MaxSoundDistance = MaxDistance;
            manager.DistanceRolloff = Rolloff;
            manager.ObjectEnable = ObjectSoundsEnabled;
        }

        /// <summary>
        /// Create a profile from current MediaManager settings
        /// </summary>
        public static AudioProfile FromMediaManager(MediaManager manager, string name)
        {
            return new AudioProfile
            {
                Name = name,
                MasterVolume = manager.MasterVolume,
                ObjectVolume = manager.ObjectVolume,
                UIVolume = manager.UIVolume,
                PreferredDriver = manager.SelectedDriver,
                DistanceModel = manager.CurrentDistanceModel,
                MaxDistance = manager.MaxSoundDistance,
                Rolloff = manager.DistanceRolloff,
                ObjectSoundsEnabled = manager.ObjectEnable
            };
        }

        /// <summary>
        /// Convert to OSD for storage
        /// </summary>
        public OSD ToOSD()
        {
            var map = new OSDMap
            {
                ["name"] = Name,
                ["master_volume"] = MasterVolume,
                ["object_volume"] = ObjectVolume,
                ["ui_volume"] = UIVolume,
                ["music_volume"] = MusicVolume,
                ["preferred_driver"] = PreferredDriver,
                ["distance_model"] = (int)DistanceModel,
                ["max_distance"] = MaxDistance,
                ["rolloff"] = Rolloff,
                ["object_sounds_enabled"] = ObjectSoundsEnabled
            };
            return map;
        }

        /// <summary>
        /// Create from OSD storage
        /// </summary>
        public static AudioProfile FromOSD(OSD osd)
        {
            if (!(osd is OSDMap map)) return null;

            return new AudioProfile
            {
                Name = map["name"].AsString(),
                MasterVolume = (float)map["master_volume"].AsReal(),
                ObjectVolume = (float)map["object_volume"].AsReal(),
                UIVolume = (float)map["ui_volume"].AsReal(),
                MusicVolume = (float)map["music_volume"].AsReal(),
                PreferredDriver = map["preferred_driver"].AsInteger(),
                DistanceModel = (DistanceModel)map["distance_model"].AsInteger(),
                MaxDistance = (float)map["max_distance"].AsReal(),
                Rolloff = (float)map["rolloff"].AsReal(),
                ObjectSoundsEnabled = map["object_sounds_enabled"].AsBoolean()
            };
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Event args for audio device changes
    /// </summary>
    public class AudioDevicesChangedEventArgs : EventArgs
    {
        public int DeviceCount { get; }
        
        public AudioDevicesChangedEventArgs(int deviceCount)
        {
            DeviceCount = deviceCount;
        }
    }

    /// <summary>
    /// Audio performance statistics
    /// </summary>
    public class AudioPerformanceStats
    {
        private readonly object lockObj = new object();
        private double totalProcessingTime = 0;
        private long operationCount = 0;

        public int QueueDepth { get; set; }
        public long SuccessfulOperations { get; set; }
        public long FailedOperations { get; set; }
        public double AverageProcessingTimeMs { get; private set; }
        
        public void UpdateAverageProcessingTime(double durationMs)
        {
            lock (lockObj)
            {
                totalProcessingTime += durationMs;
                operationCount++;
                AverageProcessingTimeMs = totalProcessingTime / operationCount;
            }
        }

        public void Reset()
        {
            lock (lockObj)
            {
                QueueDepth = 0;
                SuccessfulOperations = 0;
                FailedOperations = 0;
                totalProcessingTime = 0;
                operationCount = 0;
                AverageProcessingTimeMs = 0;
            }
        }

        public override string ToString()
        {
            return $"Queue: {QueueDepth}, Success: {SuccessfulOperations}, Failed: {FailedOperations}, Avg Time: {AverageProcessingTimeMs:F2}ms";
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

    /// <summary>
    /// Smart cache system for audio assets
    /// </summary>
    public class SoundCache : IDisposable
    {
        private readonly Dictionary<UUID, CachedSound> cache = new Dictionary<UUID, CachedSound>();
        private readonly LinkedList<UUID> lruList = new LinkedList<UUID>();
        private readonly object cacheLock = new object();
        private long currentCacheSize = 0;
        private readonly IRadegastInstance instance;

        public long MaxCacheSize { get; set; }
        public long CurrentCacheSize => currentCacheSize;
        public int CachedItemCount => cache.Count;
        public int CacheHits { get; private set; }
        public int CacheMisses { get; private set; }
        public bool EnablePersistentCache { get; set; } = true;

        public SoundCache(IRadegastInstance instance, long maxSize)
        {
            this.instance = instance;
            MaxCacheSize = maxSize;
        }

        /// <summary>
        /// Get cached sound data, or null if not cached
        /// </summary>
        public byte[] Get(UUID soundId)
        {
            lock (cacheLock)
            {
                if (cache.TryGetValue(soundId, out var cached))
                {
                    // Update LRU - move to front
                    lruList.Remove(cached.LruNode);
                    cached.LruNode = lruList.AddFirst(soundId);
                    cached.LastAccessed = DateTime.UtcNow;
                    cached.AccessCount++;
                    
                    CacheHits++;
                    Logger.Debug($"Cache HIT for sound {soundId}");
                    return cached.Data;
                }

                CacheMisses++;
                Logger.Debug($"Cache MISS for sound {soundId}");
                return null;
            }
        }

        /// <summary>
        /// Add sound data to cache
        /// </summary>
        public void Add(UUID soundId, byte[] data)
        {
            if (data == null || data.Length == 0)
                return;

            lock (cacheLock)
            {
                // Check if already cached
                if (cache.ContainsKey(soundId))
                {
                    Logger.Debug($"Sound {soundId} already in cache");
                    return;
                }

                // Evict if necessary
                while (currentCacheSize + data.Length > MaxCacheSize && lruList.Count > 0)
                {
                    EvictLRU();
                }

                // Add to cache
                var cached = new CachedSound
                {
                    SoundId = soundId,
                    Data = data,
                    Size = data.Length,
                    CachedTime = DateTime.UtcNow,
                    LastAccessed = DateTime.UtcNow,
                    LruNode = lruList.AddFirst(soundId)
                };

                cache[soundId] = cached;
                currentCacheSize += data.Length;

                Logger.Debug($"Cached sound {soundId}, size: {data.Length} bytes, total cache: {currentCacheSize}/{MaxCacheSize}");
            }
        }

        /// <summary>
        /// Preload commonly used UI sounds
        /// </summary>
        public void PreloadUISounds()
        {
            Logger.Info("Preloading UI sounds...");

            var uiSounds = new[]
            {
                UISounds.Click,
                UISounds.IM,
                UISounds.IMWindow,
                UISounds.Typing,
                UISounds.MoneyIn,
                UISounds.MoneyOut,
                UISounds.Error,
                UISounds.Alert,
                UISounds.Snapshot
            };

            foreach (var soundId in uiSounds)
            {
                PreloadSound(soundId);
            }
        }

        /// <summary>
        /// Preload a specific sound
        /// </summary>
        public void PreloadSound(UUID soundId)
        {
            if (soundId == UUID.Zero)
                return;

            // Check if already cached
            lock (cacheLock)
            {
                if (cache.ContainsKey(soundId))
                    return;
            }

            // Request from asset system
            instance.Client.Assets.RequestAsset(soundId, AssetType.Sound, false, (transfer, asset) =>
            {
                if (transfer.Success && asset is AssetSound soundAsset)
                {
                    Add(soundId, soundAsset.AssetData);
                    Logger.Debug($"Preloaded sound {soundId}");
                }
            });
        }

        /// <summary>
        /// Evict least recently used item
        /// </summary>
        private void EvictLRU()
        {
            if (lruList.Count == 0)
                return;

            var lruId = lruList.Last.Value;
            lruList.RemoveLast();

            if (cache.TryGetValue(lruId, out var cached))
            {
                currentCacheSize -= cached.Size;
                cache.Remove(lruId);
                Logger.Debug($"Evicted sound {lruId} from cache, freed {cached.Size} bytes");
            }
        }

        /// <summary>
        /// Clear all cached items
        /// </summary>
        public void Clear()
        {
            lock (cacheLock)
            {
                cache.Clear();
                lruList.Clear();
                currentCacheSize = 0;
                CacheHits = 0;
                CacheMisses = 0;
                Logger.Info("Sound cache cleared");
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (cacheLock)
            {
                var totalAccesses = CacheHits + CacheMisses;
                var hitRate = totalAccesses > 0 ? (float)CacheHits / totalAccesses * 100f : 0f;

                return new CacheStatistics
                {
                    ItemCount = cache.Count,
                    TotalSize = currentCacheSize,
                    MaxSize = MaxCacheSize,
                    Hits = CacheHits,
                    Misses = CacheMisses,
                    HitRate = hitRate,
                    UsagePercent = MaxCacheSize > 0 ? (float)currentCacheSize / MaxCacheSize * 100f : 0f
                };
            }
        }

        public void Dispose()
        {
            Clear();
        }

        /// <summary>
        /// Cached sound data with metadata
        /// </summary>
        private class CachedSound
        {
            public UUID SoundId { get; set; }
            public byte[] Data { get; set; }
            public long Size { get; set; }
            public DateTime CachedTime { get; set; }
            public DateTime LastAccessed { get; set; }
            public int AccessCount { get; set; }
            public LinkedListNode<UUID> LruNode { get; set; }
        }
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStatistics
    {
        public int ItemCount { get; set; }
        public long TotalSize { get; set; }
        public long MaxSize { get; set; }
        public int Hits { get; set; }
        public int Misses { get; set; }
        public float HitRate { get; set; }
        public float UsagePercent { get; set; }

        public override string ToString()
        {
            return $"Items: {ItemCount}, Size: {TotalSize / 1024}KB / {MaxSize / 1024}KB ({UsagePercent:F1}%), " +
                   $"Hits: {Hits}, Misses: {Misses}, Hit Rate: {HitRate:F1}%";
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
