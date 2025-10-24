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
using System.IO;
using System.Linq;
using LibreMetaverse;
using Radegast.Commands;
using Radegast.Media;
using OpenMetaverse;
using Radegast.Core.RLV;

namespace Radegast
{
    public abstract class RadegastInstance : IRadegastInstance
    {
        public const string INCOMPLETE_NAME = "Loading...";

        /// <summary>When was Radegast started (UTC)</summary>
        public readonly DateTime StartupTimeUTC = DateTime.UtcNow;
        /// <summary>Time zone of the current world (currently hard coded to US Pacific time)</summary>
        public TimeZoneInfo WordTimeZone;
        public GridClient Client { get; private set; }
        public INetCom NetCom { get; private set; }
        /// <summary>System (not grid!) user's dir</summary>
        public string UserDir { get; protected set; }
        /// <summary>Grid client's user dir for settings and logs</summary>
        public string ClientDir => !string.IsNullOrEmpty(Client?.Self?.Name) ? Path.Combine(UserDir, Client.Self.Name) : Environment.CurrentDirectory;
        public string InventoryCacheFileName => Path.Combine(ClientDir, "inventory.cache");
        public string GlobalLogFile { get; protected set; }
        public bool MonoRuntime { get; } = Type.GetType("Mono.Runtime") != null;
        public string AppName { get; }
        /// <summary>Global settings for the entire application </summary>
        public Settings GlobalSettings { get; protected set; }
        /// <summary>Per client settings</summary>
        public Settings ClientSettings { get; protected set; }
        private string CrashMarkerFileName => Path.Combine(UserDir, "crash_marker");
        /// <summary>FIXME: Is this really the best place for this?</summary>
        public Dictionary<UUID, Group> Groups { get; private set; } = new Dictionary<UUID, Group>();

        #region Managers
        public StateManager State { get; private set; }

        /// <summary>Manages retrieving avatar names</summary>
        public NameManager Names { get; private set; }

        /// <summary>Radegast media manager for playing streams and in world sounds</summary>
        public MediaManager MediaManager { get; private set; }

        /// <summary>Radegast command manager for executing textual console commands</summary>
        public CommandsManager CommandsManager { get; private set; }

        /// <summary>Manager for RLV functionality</summary>
        public RlvManager RLV { get; private set; }

        /// <summary>Manages default params for different grids</summary>
        public GridManager GridManger { get; private set; }

        /// <summary>Current Outfit Folder (appearance) manager</summary>
        public CurrentOutfitFolder COF { get; private set; }

        /// <summary>Gesture manager</summary>
        public GestureManager GestureManager { get; private set; }

        /// <summary>LSL Syntax manager</summary>
        public LslSyntax LslSyntax { get; private set; }

        #endregion Managers

        /// <summary>Allows key emulation for moving avatar around</summary>
        public RadegastMovement Movement { get; private set; }

        private InventoryClipboard inventoryClipboard;
        /// <summary>
        /// The last item that was cut or copied in the inventory, used for pasting
        /// in a different place on the inventory, or other places like profile
        /// that allow sending copied inventory items
        /// </summary>
        public InventoryClipboard InventoryClipboard
        {
            get => inventoryClipboard;
            set
            {
                inventoryClipboard = value;
                OnInventoryClipboardUpdated(EventArgs.Empty);
            }
        }

        #region Events

        #region ClientChanged event
        /// <summary>The event subscribers, null of no subscribers</summary>
        private EventHandler<ClientChangedEventArgs> m_ClientChanged;

        ///<summary>Raises the ClientChanged Event</summary>
        /// <param name="e">A ClientChangedEventArgs object containing
        /// the old and the new client</param>
        protected virtual void OnClientChanged(ClientChangedEventArgs e)
        {
            EventHandler<ClientChangedEventArgs> handler = m_ClientChanged;
            handler?.Invoke(this, e);
        }

        /// <summary>Thread sync lock object</summary>
        private readonly object m_ClientChangedLock = new object();

        /// <summary>Raised when the GridClient object in the main Radegast instance is changed</summary>
        public event EventHandler<ClientChangedEventArgs> ClientChanged
        {
            add { lock (m_ClientChangedLock) { m_ClientChanged += value; } }
            remove { lock (m_ClientChangedLock) { m_ClientChanged -= value; } }
        }
        #endregion ClientChanged event

        #region InventoryClipboardUpdated event
        /// <summary>The event subscribers, null of no subscribers</summary>
        private EventHandler<EventArgs> m_InventoryClipboardUpdated;

        ///<summary>Raises the InventoryClipboardUpdated Event</summary>
        /// <param name="e">A EventArgs object containing
        /// the old and the new client</param>
        protected virtual void OnInventoryClipboardUpdated(EventArgs e)
        {
            EventHandler<EventArgs> handler = m_InventoryClipboardUpdated;
            handler?.Invoke(this, e);
        }

        /// <summary>Thread sync lock object</summary>
        private readonly object m_InventoryClipboardUpdatedLock = new object();

        /// <summary>Raised when the GridClient object in the main Radegast instance is changed</summary>
        public event EventHandler<EventArgs> InventoryClipboardUpdated
        {
            add { lock (m_InventoryClipboardUpdatedLock) { m_InventoryClipboardUpdated += value; } }
            remove { lock (m_InventoryClipboardUpdatedLock) { m_InventoryClipboardUpdated -= value; } }
        }
        #endregion InventoryClipboardUpdated event

        #endregion Events

        protected RadegastInstance(string appName, GridClient client0, INetCom netcom0)
        {
            AppName = appName;
            Client = client0;
            NetCom = netcom0;

            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += ThreadExceptionHandler;
            InitializeAppData();

            // Initialize current time zone, and mark when we started
            GetWorldTimeZone();
            StartupTimeUTC = DateTime.UtcNow;

            State = new StateManager(this);
            MediaManager = new MediaManager(this);
            CommandsManager = new CommandsManager(this);
            Movement = new RadegastMovement(this);

            InitializeClient(Client);

            // COF must be created before RLV
            COF = new CurrentOutfitFolder(this);

            RLV = new RlvManager(this);

            GridManger = new GridManager();
            GridManger.LoadGrids();

            Names = new NameManager(this);
            GestureManager = new GestureManager(this);
            LslSyntax = new LslSyntax(Client);
        }

        private void InitializeClient(GridClient client)
        {
            client.Settings.MULTIPLE_SIMS = false;

            client.Settings.USE_INTERPOLATION_TIMER = false;
            client.Settings.ALWAYS_REQUEST_OBJECTS = true;
            client.Settings.ALWAYS_DECODE_OBJECTS = true;
            client.Settings.OBJECT_TRACKING = true;
            client.Settings.ENABLE_SIMSTATS = true;
            client.Settings.SEND_AGENT_THROTTLE = true;
            client.Settings.SEND_AGENT_UPDATES = true;
            client.Settings.STORE_LAND_PATCHES = true;

            client.Settings.USE_ASSET_CACHE = true;
            client.Settings.ASSET_CACHE_DIR = Path.Combine(UserDir, "cache");
            client.Assets.Cache.AutoPruneEnabled = false;
            client.Assets.Cache.ComputeAssetCacheFilename = ComputeCacheName;

            client.Throttle.Total = 5000000f;
            client.Settings.THROTTLE_OUTGOING_PACKETS = false;
            client.Settings.LOGIN_TIMEOUT = 120 * 1000;
            client.Settings.SIMULATOR_TIMEOUT = 180 * 1000;
            client.Settings.MAX_CONCURRENT_TEXTURE_DOWNLOADS = 20;

            client.Self.Movement.AutoResetControls = false;
            client.Self.Movement.UpdateInterval = 250;

            RegisterClientEvents(client);
        }

        private void RegisterClientEvents(GridClient client)
        {
            client.Groups.CurrentGroups += Groups_CurrentGroups;
            client.Groups.GroupLeaveReply += Groups_GroupsChanged;
            client.Groups.GroupDropped += Groups_GroupsChanged;
            client.Groups.GroupJoinedReply += Groups_GroupsChanged;
            client.Network.LoginProgress += Network_LoginProgress;
            if (NetCom != null)
            {
                NetCom.ClientConnected += NetCom_ClientConnected;
                ClientChanged += NetCom.Instance_ClientChanged;
            }
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Groups.CurrentGroups -= Groups_CurrentGroups;
            client.Groups.GroupLeaveReply -= Groups_GroupsChanged;
            client.Groups.GroupDropped -= Groups_GroupsChanged;
            client.Groups.GroupJoinedReply -= Groups_GroupsChanged;
            client.Network.LoginProgress -= Network_LoginProgress;
            if (NetCom != null)
            {
                NetCom.ClientConnected -= NetCom_ClientConnected;
                ClientChanged -= NetCom.Instance_ClientChanged;
            }
        }

        public virtual void Reconnect()
        {
            ShowNotificationInChat("Attempting to reconnect...", ChatBufferTextStyle.StatusDarkBlue);
            Logger.Log("Attempting to reconnect", Helpers.LogLevel.Info, Client);
            GridClient oldClient = Client;
            Client = new GridClient();
            UnregisterClientEvents(oldClient);
            InitializeClient(Client);
            OnClientChanged(new ClientChangedEventArgs(oldClient, Client));
            NetCom.Login();
        }

        public virtual void CleanUp()
        {
            MarkEndExecution();

            if (COF != null)
            {
                COF.Dispose();
                COF = null;
            }

            if (Names != null)
            {
                Names.Dispose();
                Names = null;
            }

            if (GridManger != null)
            {
                GridManger.Dispose();
                GridManger = null;
            }

            if (RLV != null)
            {
                RLV.Dispose();
                RLV = null;
            }

            if (Client != null)
            {
                UnregisterClientEvents(Client);
            }

            if (Movement != null)
            {
                Movement.Dispose();
                Movement = null;
            }
            if (CommandsManager != null)
            {
                CommandsManager.Dispose();
                CommandsManager = null;
            }
            if (MediaManager != null)
            {
                MediaManager.Dispose();
                MediaManager = null;
            }
            if (State != null)
            {
                State.Dispose();
                State = null;
            }
            if (NetCom != null)
            {
                NetCom.Dispose();
                NetCom = null;
            }
            Logger.Log("RadegastInstance finished cleaning up.", Helpers.LogLevel.Debug);
        }

        private void NetCom_ClientConnected(object sender, EventArgs e)
        {
            Client.Self.RequestMuteList();
        }

        private void Network_LoginProgress(object sender, LoginProgressEventArgs e)
        {
            if (e.Status != LoginStatus.ConnectingToSim) return;
            try
            {
                if (!Directory.Exists(ClientDir))
                {
                    Directory.CreateDirectory(ClientDir);
                }
                ClientSettings = new Settings(Path.Combine(ClientDir, "client_settings.xml"));
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to create client directory", Helpers.LogLevel.Warning, ex);
            }
        }

        private void Groups_GroupsChanged(object sender, EventArgs e)
        {
            Client.Groups.RequestCurrentGroups();
        }

        private void Groups_CurrentGroups(object sender, CurrentGroupsEventArgs e)
        {
            Groups = e.Groups;
        }

        public static string ComputeCacheName(string cacheDir, UUID assetID)
        {
            string fileName = assetID.ToString();
            string dir = cacheDir
                         + Path.DirectorySeparatorChar + fileName.Substring(0, 1)
                         + Path.DirectorySeparatorChar + fileName.Substring(1, 1);
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch
            {
                return Path.Combine(cacheDir, fileName);
            }
            return Path.Combine(dir, fileName);
        }

        public static string SafeFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, lDisallowed) => current.Replace(lDisallowed.ToString(), "_"));
        }

        public string ChatFileName(string session)
        {
            string dir = GlobalSettings["chat_log_dir"] && !string.IsNullOrWhiteSpace(GlobalSettings["chat_log_dir"].AsString())
                ? Path.Combine(GlobalSettings["chat_log_dir"].AsString(), !string.IsNullOrEmpty(Client?.Self?.Name)
                    ? Path.Combine(UserDir, Client.Self.Name) : Environment.CurrentDirectory) 
                : ClientDir;
            string fileName = SafeFileName(session);
            return Path.Combine(dir, fileName);
        }

        public void LogClientMessage(string sessionName, string message)
        {
            if (GlobalSettings["disable_chat_im_log"]) return;

            lock (_lockChatLog)
            {
                try
                {
                    File.AppendAllText(ChatFileName(sessionName),
                        DateTime.Now.ToString("[yyyy/MM/dd HH:mm:ss] ") + message + Environment.NewLine);
                }
                catch (Exception) { }
            }
        }

        protected virtual void InitializeAppData()
        {
            try
            {
                UserDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
                if (!Directory.Exists(UserDir))
                {
                    Directory.CreateDirectory(UserDir);
                }
            }
            catch (Exception)
            {
                UserDir = Environment.CurrentDirectory;
            }
            GlobalLogFile = Path.Combine(UserDir, $"{AppName}.log");
            GlobalSettings = new Settings(Path.Combine(UserDir, "settings.xml"));
        }

        public abstract void ShowNotificationInChat(string message, ChatBufferTextStyle style = ChatBufferTextStyle.ObjectChat, bool highlight = false);
        public abstract void AddNotification(INotification notification);
        public abstract void RemoveNotification(INotification notification);
        public abstract void ShowAgentProfile(string agentName, UUID agentID);
        public abstract void ShowGroupProfile(UUID groupId);
        public abstract void ShowLocation(string region, int x, int y, int z);
        public abstract void RegisterContextAction(Type omvType, string label, EventHandler handler);
        public abstract void DeregisterContextAction(Type omvType, string label);

        public static void ThreadExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            Logger.Log("Unhandled thread exception: "
                + e.Message + Environment.NewLine
                + e.StackTrace + Environment.NewLine,
                Helpers.LogLevel.Error);
        }

        #region World time
        private void GetWorldTimeZone()
        {
            try
            {
                foreach (TimeZoneInfo tz in TimeZoneInfo.GetSystemTimeZones())
                {
                    if (tz.Id == "Pacific Standard Time" || tz.Id == "America/Los_Angeles")
                    {
                        WordTimeZone = tz;
                        break;
                    }
                }
            }
            catch (Exception) { }
        }

        public DateTime GetWorldTime()
        {
            DateTime now;

            try
            {
                now = WordTimeZone != null ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, WordTimeZone) : DateTime.UtcNow.AddHours(-7);
            }
            catch (Exception)
            {
                now = DateTime.UtcNow.AddHours(-7);
            }

            return now;
        }

        #endregion World time

        #region Crash reporting

        private FileStream MarkerLock = null;
        private readonly object _lockChatLog = new object();

        public bool AnotherInstanceRunning()
        {
            // We have successfully obtained lock
            if (MarkerLock?.CanWrite == true)
            {
                Logger.Log("No other instances detected, marker file already locked", Helpers.LogLevel.Debug);
                return MonoRuntime;
            }

            try
            {
                MarkerLock = new FileStream(CrashMarkerFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Logger.Log($"Successfully created and locked marker file {CrashMarkerFileName}", Helpers.LogLevel.Debug);
                return MonoRuntime;
            }
            catch
            {
                MarkerLock = null;
                Logger.Log($"Another instance detected, marker file {CrashMarkerFileName} locked", Helpers.LogLevel.Debug);
                return true;
            }
        }

        public LastExecStatus GetLastExecStatus()
        {
            // Crash marker file found and is not locked by us
            if (File.Exists(CrashMarkerFileName) && MarkerLock == null)
            {
                Logger.Log($"Found crash marker file {CrashMarkerFileName}", Helpers.LogLevel.Debug);
                return LastExecStatus.OtherCrash;
            }
            else
            {
                Logger.Log($"No crash marker file {CrashMarkerFileName} found", Helpers.LogLevel.Debug);
                return LastExecStatus.Normal;
            }
        }

        public void MarkStartExecution()
        {
            Logger.Log($"Marking start of execution run, creating file: {CrashMarkerFileName}", Helpers.LogLevel.Debug);
            try
            {
                File.Create(CrashMarkerFileName).Dispose();
            }
            catch { }
        }

        public void MarkEndExecution()
        {
            Logger.Log($"Marking end of execution run, deleting file: {CrashMarkerFileName}", Helpers.LogLevel.Debug);
            try
            {
                if (MarkerLock != null)
                {
                    MarkerLock.Close();
                    MarkerLock.Dispose();
                    MarkerLock = null;
                }

                File.Delete(CrashMarkerFileName);
            }
            catch { }
        }

        #endregion Crash reporting
    }

    #region Event classes
    public class ClientChangedEventArgs : EventArgs
    {
        public GridClient OldClient { get; }
        public GridClient Client { get; }

        public ClientChangedEventArgs(GridClient OldClient, GridClient Client)
        {
            this.OldClient = OldClient;
            this.Client = Client;
        }
    }
    #endregion Event classes
}
