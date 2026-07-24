/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using LibreMetaverse;
using LibreMetaverse.Messages.Linden;
using Radegast.Veles.Plugins;
using Radegast.Veles.ViewModels;
using Radegast.Veles.Views;

namespace Radegast.Veles.Core;

public sealed class RadegastInstanceAvalonia : RadegastInstance
{
    private bool _initialCapsFetched;
    private System.Threading.Timer? _renderInfoTimer;
    private System.Threading.Timer? _viewerStatsTimer;
    private DateTime _loginTime;
    private int _regionsVisited;
    public event EventHandler<NotificationChatEventArgs>? NotificationInChat;

    /// <summary>Per-account credentials store; set by the app after successful login.</summary>
    internal CredentialManager? CredentialManager { get; set; }

    /// <summary>Identifies the logged-in account as "username:gridId"; set by the app after login.</summary>
    internal string? AccountKey { get; set; }

    /// <summary>Raised when any part of the UI requests opening a P2P IM session.</summary>
    public event EventHandler<IMRequestedEventArgs>? IMRequested;

    /// <summary>Ask the IM system to open (or focus) a session with the given agent.</summary>
    public void RequestIM(UUID agentId, string agentName)
        => IMRequested?.Invoke(this, new IMRequestedEventArgs(agentId, agentName));

    /// <summary>Raised when any part of the UI requests opening a group IM session.</summary>
    public event EventHandler<GroupIMRequestedEventArgs>? GroupIMRequested;

    /// <summary>Ask the IM system to open (or focus) a group chat session.</summary>
    public void RequestGroupIM(UUID groupId, string groupName)
        => GroupIMRequested?.Invoke(this, new GroupIMRequestedEventArgs(groupId, groupName));

    /// <summary>Raised when any part of the UI requests showing an avatar on the world map.</summary>
    public event EventHandler<UUID>? ShowOnMapRequested;

    /// <summary>Navigate to the World Map tab and locate the given agent.</summary>
    public void ShowOnMap(UUID agentId)
        => ShowOnMapRequested?.Invoke(this, agentId);

    /// <summary>Open the group picker and call <paramref name="onSelected"/> with the chosen group.</summary>
    public void ShowGroupPicker(string title, Action<GroupPickerEntry> onSelected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new GroupPickerViewModel(this);
            var window = new GroupPickerWindow { DataContext = vm, Title = title };
            vm.Selected += (_, entry) => { onSelected(entry); window.Close(); };
            vm.Cancelled += (_, _) => window.Close();
            window.Show();
        });
    }

    /// <summary>Open the Pay dialog for an avatar or an in-world object.</summary>
    public void OpenPayWindow(UUID targetId, string name, bool isObject = false, Simulator? sim = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new PayViewModel(this, targetId, name, isObject, sim);
            var window = new PayWindow { DataContext = vm };
            vm.CloseRequested += (_, _) => window.Close();
            window.Show();
        });
    }

    public ChatLogger ChatLog { get; } = new ChatLogger();

    private readonly ConcurrentDictionary<UUID, string> _groupNameCache = new();

    public bool TryGetCachedGroupName(UUID groupId, out string name)
        => _groupNameCache.TryGetValue(groupId, out name!);

    /// <summary>Raised when any in-world notification should be shown to the user.</summary>
    public event EventHandler<NotificationViewModel>? NotificationReceived;

    /// <summary>Raises a notification through the notification queue.</summary>
    public void RaiseNotification(NotificationViewModel vm) =>
        NotificationReceived?.Invoke(this, vm);

    /// <summary>The active voice session manager (set by MainViewModel after login).</summary>
    public VoiceViewModel? Voice { get; internal set; }

    /// <summary>The active media / audio manager (set by MainViewModel after login).</summary>
    public MediaViewModel? Media { get; internal set; }

    /// <summary>The plugin manager for this session.</summary>
    public PluginManager PluginManager { get; private set; } = null!;

    /// <summary>Initialise the plugin manager. Called after construction.</summary>
    internal void InitPluginManager()
    {
        PluginManager = new PluginManager(this);
    }

    /// <summary>Persistent group-notice archive for this session.</summary>
    public GroupNoticeArchiveService NoticeArchive { get; }

    /// <summary>
    /// When enabled, disables the 3D Scene Viewer, the Nearby/Objects minimaps, and tightens
    /// texture/asset cache sizes to a conservative preset. Intended for running many bot
    /// instances at once. Read from settings at construction so it takes effect even if the
    /// user never opens Preferences that session.
    /// </summary>
    public bool LowMemoryModeEnabled { get; private set; }

    /// <summary>Raised when <see cref="LowMemoryModeEnabled"/> changes so live UI/state can react.</summary>
    public event EventHandler<bool>? LowMemoryModeChanged;

    /// <summary>Sets <see cref="LowMemoryModeEnabled"/> and raises <see cref="LowMemoryModeChanged"/> if it changed.</summary>
    public void SetLowMemoryModeEnabled(bool enabled)
    {
        if (LowMemoryModeEnabled == enabled) return;
        LowMemoryModeEnabled = enabled;
        LowMemoryModeChanged?.Invoke(this, enabled);
    }

    /// <summary>
    /// Pushes texture/asset cache size settings to the live caches, substituting
    /// <see cref="LowMemoryModePreset"/> values for the user's own settings while
    /// <see cref="LowMemoryModeEnabled"/> is on.
    /// </summary>
    internal void ApplyCacheSettings(
        int assetCacheMaxSizeMb, int skBitmapCacheCap, int textureBitmapCacheCapacity, int textureDiskCacheMaxFiles)
    {
        bool low = LowMemoryModeEnabled;
        Client.Settings.AssetCache.MaxSize =
            (long)(low ? LowMemoryModePreset.AssetCacheMaxSizeMb : assetCacheMaxSizeMb) * 1024 * 1024;
        GridTextureHelper.SkBitmapCacheCap =
            low ? LowMemoryModePreset.SkBitmapCacheCap : skBitmapCacheCap;
        MapTileCache.CacheCapacity =
            low ? LowMemoryModePreset.TextureBitmapCacheCapacity : textureBitmapCacheCapacity;
        TextureDiskCache.MaxCachedFiles =
            low ? LowMemoryModePreset.TextureDiskCacheMaxFiles : textureDiskCacheMaxFiles;
        Rendering.PrimMeshBuilder.MeshCacheMaxVertices = low
            ? LowMemoryModePreset.MeshDecodeCacheMaxVertices
            : Rendering.PrimMeshBuilder.DefaultMeshCacheMaxVertices;
    }

    internal RadegastInstanceAvalonia(string appName, GridClient client)
        : base(appName, client, new NetComAvalonia(client))
    {
        NoticeArchive = new GroupNoticeArchiveService(this);

        // Honour a user-configured texture-cache path stored by Preferences.
        var customCacheDir = GlobalSettings["texture_cache_dir"]?.AsString();
        if (!string.IsNullOrWhiteSpace(customCacheDir))
            Client.Settings.AssetCache.Dir = customCacheDir;

        // Apply chat logging preferences
        var chatLogDir = GlobalSettings["chat_log_dir"]?.AsString();
        if (!string.IsNullOrWhiteSpace(chatLogDir))
            ChatLog.BaseDirectory = chatLogDir;

        if (GlobalSettings["chat_logging_enabled"].Type != LibreMetaverse.StructuredData.OSDType.Unknown)
            ChatLog.IsEnabled = GlobalSettings["chat_logging_enabled"].AsBoolean();

        // Low Memory Mode must take effect from process start, even if the user never opens
        // Preferences this session (important for unattended bots).
        LowMemoryModeEnabled = GlobalSettings["low_memory_mode_enabled"].AsBoolean();

        var assetCacheMaxSizeMb = GlobalSettings["asset_cache_max_size_mb"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? GlobalSettings["asset_cache_max_size_mb"].AsInteger() : 1024;
        var skBitmapCacheCap = GlobalSettings["sk_bitmap_cache_cap"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? GlobalSettings["sk_bitmap_cache_cap"].AsInteger() : GridTextureHelper.RecommendSkBitmapCacheCap();
        var textureBitmapCacheCapacity = GlobalSettings["texture_bitmap_cache_capacity"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? GlobalSettings["texture_bitmap_cache_capacity"].AsInteger() : 2500;
        var textureDiskCacheMaxFiles = GlobalSettings["texture_disk_cache_max_files"].Type != LibreMetaverse.StructuredData.OSDType.Unknown
            ? GlobalSettings["texture_disk_cache_max_files"].AsInteger() : 8192;
        ApplyCacheSettings(assetCacheMaxSizeMb, skBitmapCacheCap, textureBitmapCacheCapacity, textureDiskCacheMaxFiles);

        client.Self.ScriptDialog += Self_ScriptDialog;
        client.Self.ScriptQuestion += Self_ScriptQuestion;
        client.Self.LoadURL += Self_LoadURL;
        client.Self.TeleportProgress += Self_TeleportProgress;
        client.Network.EventQueueRunning += Network_EventQueueRunning;
        NetCom.InstantMessageReceived += NetCom_InstantMessageReceived;
        NetCom.AlertMessageReceived += NetCom_AlertMessageReceived;
        client.Friends.CallingCardOffered += Friends_CallingCardOffered;
        client.Groups.GroupNamesReply += Groups_GroupNamesReply;
    }

    private void Groups_GroupNamesReply(object? sender, GroupNamesEventArgs e)
    {
        foreach (var kvp in e.GroupNames)
            _groupNameCache[kvp.Key] = kvp.Value;
    }

    private void Network_EventQueueRunning(object? sender, EventQueueRunningEventArgs e)
    {
        _regionsVisited++;

        // ViewerBenefits, AgentPreferences, and ProductInfo are account-level — fetch once per login session.
        if (!_initialCapsFetched)
        {
            _initialCapsFetched = true;
            _loginTime = DateTime.UtcNow;
            _ = Task.Run(() => Client.Self.GetViewerBenefitsAsync());
            _ = Task.Run(() => Client.Self.GetAgentPreferencesAsync());
            _ = Task.Run(() => Client.Self.GetProductInfoAsync());

            // Send viewer stats after 1 minute, then every 5 minutes — matching SL C++ behaviour.
            _viewerStatsTimer = new System.Threading.Timer(
                _ => _ = Task.Run(SendViewerStatsAsync),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5));
        }

        // AvatarRenderInfo is region-scoped — only act on the sim we are actually entering.
        if (e.Simulator != Client.Network.CurrentSim) return;

        // GET: populate Client.Self.AvatarRenderInfo with the region's current complexity data.
        _ = Task.Run(() => Client.Self.GetAvatarRenderInfoAsync());

        // POST: report local render weights on a 60-second interval, matching SL C++ behaviour.
        // Veles has no renderer, so we report weight=0 / tooComplex=false for self only.
        _renderInfoTimer?.Dispose();
        _renderInfoTimer = new System.Threading.Timer(
            _ => _ = Task.Run(() => Client.Self.PostAvatarRenderInfoAsync(0, false)),
            null,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60));
    }

    private async Task SendViewerStatsAsync()
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        long memKb;
        try { memKb = Process.GetCurrentProcess().WorkingSet64 / 1024L; }
        catch { memKb = 0L; }

        var stats = new ViewerStatsMessage
        {
            SessionID         = Client.Self.SessionID,
            AgentFPS          = 0f,
            AgentLanguage     = CultureInfo.CurrentCulture.Name,
            AgentMemoryUsed   = memKb,
            AgentPing         = sim.Stats.LastLag,
            RegionsVisited    = _regionsVisited,
            AgentRuntime      = (float)(DateTime.UtcNow - _loginTime).TotalSeconds,
            SimulatorFPS      = sim.Stats.FPS,
            AgentStartTime    = _loginTime,
            AgentVersion      = NetCom.LoginOptions.Version,
            AgentsInView      = sim.Stats.Agents,
            MiscVersion       = 1f,
            VertexBuffersEnabled = false,
            InKbytes          = sim.Stats.GetRecvBytes() / 1024f,
            InPackets         = sim.Stats.GetRecvPackets(),
            OutKbytes         = sim.Stats.GetSentBytes() / 1024f,
            OutPackets        = sim.Stats.GetSentPackets(),
            StatsFailedResends = sim.Stats.GetResentPackets(),
            SystemOS          = Environment.OSVersion.ToString(),
            SystemCPU         = string.Empty,
            SystemGPU         = string.Empty,
            SystemGPUVendor   = string.Empty,
            SystemGPUVersion  = string.Empty,
            MiscString1       = string.Empty,
        };

        await Client.Self.SendViewerStatsAsync(stats);
    }

    private void Self_ScriptDialog(object? sender, ScriptDialogEventArgs e)
    {
        // Check mute list
        if (Client.Self.MuteList.Values.Any(m => m.Type == MuteType.Object && m.ID == e.ObjectID)) return;
        if (Client.Self.MuteList.Values.Any(m => m.Type == MuteType.ByName && m.Name == e.ObjectName)) return;

        var vm = NotificationViewModel.ForScriptDialog(
            Client, e.ObjectName, $"{e.FirstName} {e.LastName}",
            e.Message, e.ButtonLabels, e.ObjectID, e.Channel);
        NotificationReceived?.Invoke(this, vm);
        MediaManager.PlayUISound(UISounds.PieAppear);
    }

    private void Self_ScriptQuestion(object? sender, ScriptQuestionEventArgs e)
    {
        // Check mute list by object name
        if (Client.Self.MuteList.Values.Any(m => m.Type == MuteType.ByName && m.Name == e.ObjectName)) return;
        if (Client.Self.MuteList.Values.Any(m => m.Type == MuteType.Object && m.ID == e.TaskID)) return;

        var vm = NotificationViewModel.ForPermissions(
            Client, e.Simulator, e.TaskID, e.ItemID,
            e.ObjectName, e.ObjectOwnerName, e.Questions);
        NotificationReceived?.Invoke(this, vm);
    }

    private void Self_LoadURL(object? sender, LoadUrlEventArgs e)
    {
        if (Client.Self.MuteList.Values.Any(m =>
                (m.Type == MuteType.Object && m.ID == e.ObjectID) ||
                (m.Type == MuteType.ByName && m.Name == e.ObjectName) ||
                (m.Type == MuteType.Resident && m.ID == e.OwnerID))) return;

        string ownerName = Names.Get(e.OwnerID);
        var vm = NotificationViewModel.ForLoadUrl(e.ObjectName, ownerName, e.URL, e.Message);
        NotificationReceived?.Invoke(this, vm);
    }

    private void NetCom_InstantMessageReceived(object? sender, InstantMessageEventArgs e)
    {
        InstantMessage msg = e.IM;
        switch (msg.Dialog)
        {
            case InstantMessageDialog.FriendshipOffered:
                if (msg.FromAgentName == "Second Life") return;
                NotificationReceived?.Invoke(this, NotificationViewModel.ForFriendshipOffer(Client, msg));
                MediaManager.PlayUISound(UISounds.Alert);
                break;

            case InstantMessageDialog.GroupNotice:
                if (Client.Self.MuteList.Values.Any(m => m.Type == MuteType.Group && m.ID == msg.FromAgentID)) return;
                NotificationReceived?.Invoke(this, NotificationViewModel.ForGroupNotice(Client, msg));
                MediaManager.PlayUISound(UISounds.Alert);
                break;

            case InstantMessageDialog.GroupInvitation:
                NotificationReceived?.Invoke(this, NotificationViewModel.ForGroupInvitation(Client, msg));
                MediaManager.PlayUISound(UISounds.Alert);
                break;

            case InstantMessageDialog.InventoryOffered:
                NotificationReceived?.Invoke(this, NotificationViewModel.ForInventoryOffer(Client, msg));
                MediaManager.PlayUISound(UISounds.Alert);
                break;

            case InstantMessageDialog.TaskInventoryOffered:
                if (Client.Self.MuteList.Values.Any(m => m.Type == MuteType.ByName && m.Name == msg.FromAgentName)) return;
                NotificationReceived?.Invoke(this, NotificationViewModel.ForInventoryOffer(Client, msg));
                MediaManager.PlayUISound(UISounds.Alert);
                break;

            case InstantMessageDialog.RequestTeleport:
                NotificationReceived?.Invoke(this, NotificationViewModel.ForTeleportOffer(Client, msg));
                MediaManager.PlayUISound(UISounds.Alert);
                break;

            case InstantMessageDialog.RequestLure:
                NotificationReceived?.Invoke(this, NotificationViewModel.ForTeleportRequest(Client, msg));
                MediaManager.PlayUISound(UISounds.Alert);
                break;

            case InstantMessageDialog.MessageBox:
                NotificationReceived?.Invoke(this, NotificationViewModel.ForGenericMessage("Message", msg.Message));
                MediaManager.PlayUISound(UISounds.Alert);
                break;
        }
    }

    private void NetCom_AlertMessageReceived(object? sender, AlertMessageEventArgs e)
    {
        if (e.NotificationId == "RegionRestartMinutes")
        {
            int minutes = e.ExtraParams?["MINUTES"].AsInteger() ?? 0;
            string regionName = e.ExtraParams?["NAME"].AsString() ?? string.Empty;
            var vm = NotificationViewModel.ForRegionRestart(Client, regionName, minutes * 60);
            NotificationReceived?.Invoke(this, vm);
        }
        else if (e.NotificationId == "RegionRestartSeconds")
        {
            int seconds = e.ExtraParams?["SECONDS"].AsInteger() ?? 0;
            string regionName = e.ExtraParams?["NAME"].AsString() ?? string.Empty;
            var vm = NotificationViewModel.ForRegionRestart(Client, regionName, seconds);
            NotificationReceived?.Invoke(this, vm);
        }
    }

    private void Friends_CallingCardOffered(object? sender, CallingCardOfferedEventArgs e)
    {
        var vm = NotificationViewModel.ForCallingCardOffer(Client, e);
        NotificationReceived?.Invoke(this, vm);
        MediaManager.PlayUISound(UISounds.Alert);
    }

    public override void ShowNotificationInChat(string message, ChatBufferTextStyle style = ChatBufferTextStyle.ObjectChat, bool highlight = false)
    {
        NotificationInChat?.Invoke(this, new NotificationChatEventArgs(message, style, highlight));
    }

    public override void AddNotification(INotification notification) { }
    public override void RemoveNotification(INotification notification) { }
    public override void ShowAgentProfile(string agentName, UUID agentID)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new AvatarProfileViewModel(this, agentName, agentID);
            var panel = new AvatarProfilePanel { DataContext = vm };
            var window = new ProfileWindow($"Profile - {agentName}", panel);
            window.Show();
        });
    }

    public override void ShowGroupProfile(UUID groupId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new GroupProfileViewModel(this, groupId);
            var panel = new GroupProfilePanel { DataContext = vm };
            var window = new ProfileWindow($"Group Profile", panel);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GroupProfileViewModel.GroupName))
                    window.Title = $"Group - {vm.GroupName}";
            };
            window.Show();
        });
    }

    public void ShowGroupNoticeArchive()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new GroupNoticeArchiveViewModel(this);
            var panel = new GroupNoticeArchivePanel { DataContext = vm };
            var window = new ProfileWindow("Group Notice Archive", panel)
            {
                Width = 750,
                Height = 550
            };
            window.Show();
        });
    }

    public void ShowAvatarPicker(string title, Action<AvatarPickerEntry> onSelected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new AvatarPickerViewModel(this);
            var window = new AvatarPickerWindow { DataContext = vm, Title = title };
            vm.Selected += (_, entry) => { onSelected(entry); window.Close(); };
            vm.Cancelled += (_, _) => window.Close();
            window.Show();
        });
    }

    public void ShowInventoryPicker(string title, AssetType[]? allowedTypes, Action<InventoryPickerEntry> onSelected, Func<InventoryItem, bool>? itemFilter = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new InventoryPickerViewModel(this, allowedTypes, itemFilter);
            var window = new InventoryPickerWindow { DataContext = vm, Title = title };
            vm.Selected += (_, entry) => { onSelected(entry); window.Close(); };
            vm.Cancelled += (_, _) => window.Close();
            window.Show();
        });
    }

    public void ShowLandProfile()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new LandProfileViewModel(this);
            var panel = new LandProfilePanel { DataContext = vm };
            var window = new ProfileWindow("Land Info", panel);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LandProfileViewModel.ParcelName))
                    window.Title = $"Land - {vm.ParcelName}";
            };
            window.Show();
        });
    }

    public void ShowLandProfile(float rx, float ry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new LandProfileViewModel(this, loadCurrentParcel: false);
            vm.LoadParcelAtPosition(rx, ry);
            var panel = new LandProfilePanel { DataContext = vm };
            var window = new ProfileWindow("Land Info", panel);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LandProfileViewModel.ParcelName))
                    window.Title = $"Land - {vm.ParcelName}";
            };
            window.Show();
        });
    }

    public void ShowLandHoldings()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new LandHoldingsViewModel(this);
            var panel = new LandHoldingsPanel { DataContext = vm };
            var window = new ProfileWindow("Land Holdings", panel);
            window.Show();
        });
    }

    public void ShowDirectorySearch()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new DirectorySearchViewModel(this);
            var panel = new DirectorySearchPanel { DataContext = vm };
            var window = new ProfileWindow("Search", panel);
            window.Width = 820;
            window.Height = 650;
            window.Closed += (_, _) => vm.Dispose();
            window.Show();
        });
    }

    public void ShowEstateProfile()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new EstateProfileViewModel(this);
            var panel = new EstateProfilePanel { DataContext = vm };
            var window = new ProfileWindow($"Region - {vm.RegionName}", panel);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EstateProfileViewModel.RegionName))
                    window.Title = $"Region - {vm.RegionName}";
            };
            window.Show();
        });
    }

    public void ShowChangeDisplayName()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var window = new SetDisplayNameWindow(Client);
            window.Show();
        });
    }


    public void ShowObjectContents(UUID objectId, uint localId, string objectName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new ObjectContentsViewModel(this, objectId, localId, objectName);
            var panel = new ObjectContentsPanel { DataContext = vm };
            var window = new ProfileWindow($"Contents - {objectName}", panel);
            window.Show();
        });
    }

    public void ShowPrimViewer(uint rootLocalId, string objectName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm     = new PrimViewerViewModel(this, rootLocalId);
            var panel  = new PrimViewerPanel { DataContext = vm };
            var window = new ProfileWindow($"3D View — {objectName}", panel);
            window.Closed += (_, _) => vm.Dispose();
            window.Width  = 640;
            window.Height = 520;
            window.Show();
        });
    }

    public void ShowHudViewer()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm     = new HudViewerViewModel(this);
            var panel  = new HudViewerPanel { DataContext = vm };
            var window = new ProfileWindow("HUD Viewer", panel);
            window.Closed += (_, _) => vm.Dispose();
            window.Width  = 820;
            window.Height = 560;
            window.Show();
        });
    }

    public void ShowAvatarViewer(UUID avatarId, string avatarName)
    {
        if (avatarId == UUID.Zero) return;
        Dispatcher.UIThread.Post(() =>
        {
            var vm     = new AvatarViewerViewModel(this, avatarId);
            var panel  = new AvatarViewerPanel { DataContext = vm };
            var window = new ProfileWindow($"3D Avatar — {avatarName}", panel);
            window.Closed += (_, _) => vm.Dispose();
            window.Width  = 640;
            window.Height = 520;
            window.Show();
        });
    }

    public override void ShowLocation(string region, int x, int y, int z) { }

    public override void RegisterContextAction(Type omvType, string label, EventHandler handler) { }
    public override void DeregisterContextAction(Type omvType, string label) { }

    public void ShowMuteList()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new MuteListViewModel(this);
            var panel = new MuteListPanel { DataContext = vm };
            var window = new ProfileWindow("Mute List", panel);
            window.Closed += (_, _) => vm.Dispose();
            window.Show();
        });
    }

    public void ShowTextureViewer(UUID textureId, string name)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new TextureViewerViewModel(this, textureId, name);
            var panel = new TextureViewerPanel { DataContext = vm };
            var window = new ProfileWindow($"Texture - {name}", panel);
            window.Show();
        });
    }

    public void CreateAndOpenNotecard()
    {
        var parentId = Client.Inventory.FindFolderForType(FolderType.Notecard);
        _ = Task.Run(async () =>
        {
            var item = await Client.Inventory.CreateItemAsync(parentId, "New Notecard", string.Empty,
                AssetType.Notecard, UUID.Random(), InventoryType.Notecard, PermissionMask.All);
            if (item is not InventoryNotecard nc) return;
            Dispatcher.UIThread.Post(() =>
            {
                var vm = new NotecardViewModel(this, nc);
                var panel = new NotecardPanel { DataContext = vm };
                var window = new ProfileWindow($"Notecard - {nc.Name}", panel);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(NotecardViewModel.NotecardName))
                        window.Title = $"Notecard - {vm.NotecardName}";
                };
                window.Closed += (_, _) => vm.Dispose();
                window.Show();
            });
        });
    }

    public void CreateAndOpenScript()
    {
        var parentId = Client.Inventory.FindFolderForType(FolderType.LSLText);
        _ = Task.Run(async () =>
        {
            var item = await Client.Inventory.CreateItemAsync(parentId, "New Script", string.Empty,
                AssetType.LSLText, UUID.Random(), InventoryType.LSL, PermissionMask.All);
            if (item is not InventoryLSL lsl) return;
            Dispatcher.UIThread.Post(() =>
            {
                var vm = new ScriptEditorViewModel(this, lsl);
                var panel = new ScriptEditorPanel { DataContext = vm };
                var window = new ProfileWindow($"Script - {lsl.Name}", panel);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ScriptEditorViewModel.ScriptName))
                        window.Title = $"Script - {vm.ScriptName}";
                };
                window.Closed += (_, _) => vm.Dispose();
                window.Show();
            });
        });
    }

    public void CreateAndOpenLandmark()
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        var simName = sim.Name;
        var parcelName = State.Parcel?.Name;
        var landmarkName = !string.IsNullOrWhiteSpace(parcelName) ? parcelName : simName;
        var parentId = Client.Inventory.FindFolderForType(FolderType.Landmark);
        _ = Task.Run(async () =>
        {
            var item = await Client.Inventory.CreateItemAsync(parentId, landmarkName, string.Empty,
                AssetType.Landmark, UUID.Random(), InventoryType.Landmark, PermissionMask.All);
            if (item is not InventoryLandmark lm) return;
            Dispatcher.UIThread.Post(() =>
            {
                var vm = new LandmarkViewModel(this, lm);
                var panel = new LandmarkPanel { DataContext = vm };
                var window = new ProfileWindow($"Landmark - {lm.Name}", panel);
                window.Closed += (_, _) => vm.Dispose();
                window.Show();
            });
        });
    }

    public void CreateAndOpenGesture()
    {
        var parentId = Client.Inventory.FindFolderForType(FolderType.Gesture);
        _ = Task.Run(async () =>
        {
            var item = await Client.Inventory.CreateItemAsync(parentId, "New Gesture", string.Empty,
                AssetType.Gesture, UUID.Random(), InventoryType.Gesture, PermissionMask.All);
            if (item is not InventoryGesture gesture) return;
            Dispatcher.UIThread.Post(() =>
            {
                var vm = new GestureViewModel(this, gesture);
                var panel = new GesturePanel { DataContext = vm };
                var window = new ProfileWindow($"Gesture - {gesture.Name}", panel);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(GestureViewModel.GestureName))
                        window.Title = $"Gesture - {vm.GestureName}";
                };
                window.Closed += (_, _) => vm.Dispose();
                window.Show();
            });
        });
    }

    public override void CleanUp()
    {
        NoticeArchive.Dispose();
        PluginManager?.Dispose();
        _renderInfoTimer?.Dispose();
        _renderInfoTimer = null;
        _viewerStatsTimer?.Dispose();
        _viewerStatsTimer = null;
        Client.Self.ScriptDialog -= Self_ScriptDialog;
        Client.Self.ScriptQuestion -= Self_ScriptQuestion;
        Client.Self.LoadURL -= Self_LoadURL;
        Client.Self.TeleportProgress -= Self_TeleportProgress;
        Client.Network.EventQueueRunning -= Network_EventQueueRunning;
        NetCom.InstantMessageReceived -= NetCom_InstantMessageReceived;
        NetCom.AlertMessageReceived -= NetCom_AlertMessageReceived;
        Client.Friends.CallingCardOffered -= Friends_CallingCardOffered;
        ChatLog.Dispose();
        base.CleanUp();
    }

    private void Self_TeleportProgress(object? sender, TeleportEventArgs e)
    {
        if (e.Status == TeleportStatus.Finished)
            MediaManager.PlayUISound(UISounds.Teleport);
    }
}

public class NotificationChatEventArgs : EventArgs
{
    public string Message { get; }
    public ChatBufferTextStyle Style { get; }
    public bool Highlight { get; }

    public NotificationChatEventArgs(string message, ChatBufferTextStyle style, bool highlight)
    {
        Message = message;
        Style = style;
        Highlight = highlight;
    }
}

public class IMRequestedEventArgs : EventArgs
{
    public UUID AgentId { get; }
    public string AgentName { get; }

    public IMRequestedEventArgs(UUID agentId, string agentName)
    {
        AgentId = agentId;
        AgentName = agentName;
    }
}

public class GroupIMRequestedEventArgs : EventArgs
{
    public UUID GroupId { get; }
    public string GroupName { get; }

    public GroupIMRequestedEventArgs(UUID groupId, string groupName)
    {
        GroupId   = groupId;
        GroupName = groupName;
    }
}
