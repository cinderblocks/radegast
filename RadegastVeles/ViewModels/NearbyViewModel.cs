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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class NearbyViewModel : ObservableObject, IDisposable, IChatContext
{
    private readonly RadegastInstanceAvalonia _instance;
    private INetCom NetCom => _instance.NetCom;
    private GridClient Client => _instance.Client;
    private RadegastMovement Movement => _instance.Movement;

    private readonly Regex _chatRegex = new(@"^/(\d+)\s*(.*)", RegexOptions.Compiled);
    private readonly List<string> _chatHistory = [];
    private int _chatPointer;
    private DispatcherTimer? _healthTimer;

    [ObservableProperty]
    private string _chatInput = string.Empty;

    [ObservableProperty]
    private int _selectedChatTypeIndex = 1; // Normal

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isFlying;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _locationText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRegionName))]
    private string _regionName = string.Empty;

    [ObservableProperty]
    private string _locationCoords = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParcelName))]
    private string _parcelName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaturityBrush))]
    private string _maturityText = string.Empty;

    // ── Parcel / sim permission indicators ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthText))]
    private float _health = 100f;

    [ObservableProperty] private bool _isDamageEnabled;
    [ObservableProperty] private bool _canFly          = true;
    [ObservableProperty] private bool _canBuild        = true;
    [ObservableProperty] private bool _scriptsAllowed  = true;
    [ObservableProperty] private bool _pushAllowed     = true;
    [ObservableProperty] private bool _voiceAllowed    = true;

    public string HealthText => $"♥ {Health:0}%";

    public bool HasRegionName => !string.IsNullOrEmpty(RegionName);
    public bool HasParcelName => !string.IsNullOrEmpty(ParcelName);

    public string MaturityBrush => MaturityText switch
    {
        "Adult"    => "#CC3333",
        "Moderate" => "#CC8800",
        _          => "#338833"
    };

    public ObservableCollection<ChatLine> ChatLines { get; } = [];
    public ObservableCollection<NearbyAvatar> NearbyAvatars { get; } = [];
    public ObservableCollection<MinimapEntry> MinimapEntries { get; } = [];
    public RadegastInstanceAvalonia Instance => _instance;

    /// <summary>Voice ViewModel — set by MainViewModel after both are constructed.</summary>
    public VoiceViewModel? Voice { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadTabLabel))]
    private bool _hasUnread;

    /// <summary>True when the Chat tab is the currently selected tab.</summary>
    public bool IsActive { get; set; }

    /// <summary>Screenreader-friendly tab label that announces unread state.</summary>
    public string UnreadTabLabel => HasUnread ? "Chat, new messages" : "Chat";

    public void ClearUnread() => HasUnread = false;

    [ObservableProperty]
    private Bitmap? _minimapTile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedMinimapEntryId))]
    private NearbyAvatar? _selectedNearbyAvatar;

    public UUID SelectedMinimapEntryId => SelectedNearbyAvatar?.Id ?? UUID.Zero;

    public string[] ChatTypes { get; } = ["Whisper", "Normal", "Shout"];

    public NearbyViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        // Subscribe to events
        NetCom.ChatReceived += NetCom_ChatReceived;
        NetCom.ChatSent += NetCom_ChatSent;
        NetCom.AlertMessageReceived += NetCom_AlertMessageReceived;
        NetCom.ClientLoginStatus += NetCom_ClientLoginStatus;
        NetCom.ClientLoggedOut += NetCom_ClientLoggedOut;
        NetCom.ClientDisconnected += NetCom_ClientDisconnected;

        RegisterClientEvents(Client);
        _instance.ClientChanged += Instance_ClientChanged;
        _instance.NotificationInChat += Instance_NotificationInChat;

        // Initial status
        StatusText = $"Logged in as {Client.Self.Name}";
        UpdateLocationText();
        FetchMinimapTile();
        RequestCurrentParcel();

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _healthTimer.Tick += (_, _) => Health = Client.Self.Health;
    }

    public void Dispose()
    {
        NetCom.ChatReceived -= NetCom_ChatReceived;
        NetCom.ChatSent -= NetCom_ChatSent;
        NetCom.AlertMessageReceived -= NetCom_AlertMessageReceived;
        NetCom.ClientLoginStatus -= NetCom_ClientLoginStatus;
        NetCom.ClientLoggedOut -= NetCom_ClientLoggedOut;
        NetCom.ClientDisconnected -= NetCom_ClientDisconnected;

        UnregisterClientEvents(Client);
        _instance.ClientChanged -= Instance_ClientChanged;
        _instance.NotificationInChat -= Instance_NotificationInChat;

        _healthTimer?.Stop();
        _healthTimer = null;
    }

    private void RegisterClientEvents(GridClient client)
    {
        client.Grid.CoarseLocationUpdate += Grid_CoarseLocationUpdate;
        client.Self.TeleportProgress += Self_TeleportProgress;
        client.Network.SimDisconnected += Network_SimDisconnected;
        client.Network.SimChanged += Network_SimChanged;
        client.Parcels.ParcelProperties += Parcels_ParcelProperties;
    }

    private void UnregisterClientEvents(GridClient client)
    {
        client.Grid.CoarseLocationUpdate -= Grid_CoarseLocationUpdate;
        client.Self.TeleportProgress -= Self_TeleportProgress;
        client.Network.SimDisconnected -= Network_SimDisconnected;
        client.Network.SimChanged -= Network_SimChanged;
        client.Parcels.ParcelProperties -= Parcels_ParcelProperties;
    }

    private void Instance_ClientChanged(object? sender, ClientChangedEventArgs e)
    {
        UnregisterClientEvents(e.OldClient);
        RegisterClientEvents(e.Client);
    }

    #region Chat Processing

    [RelayCommand]
    private void SendChat()
    {
        if (string.IsNullOrEmpty(ChatInput)) return;

        var chatType = SelectedChatTypeIndex switch
        {
            0 => ChatType.Whisper,
            2 => ChatType.Shout,
            _ => ChatType.Normal
        };

        ProcessChatInput(ChatInput, chatType);
        ChatInput = string.Empty;
    }

    [RelayCommand]
    private void SendWhisper()
    {
        if (string.IsNullOrEmpty(ChatInput)) return;
        ProcessChatInput(ChatInput, ChatType.Whisper);
        ChatInput = string.Empty;
    }

    [RelayCommand]
    private void SendShout()
    {
        if (string.IsNullOrEmpty(ChatInput)) return;
        ProcessChatInput(ChatInput, ChatType.Shout);
        ChatInput = string.Empty;
    }

    public void ProcessChatInput(string input, ChatType type)
    {
        if (string.IsNullOrEmpty(input)) return;
        _chatHistory.Add(input);
        _chatPointer = _chatHistory.Count;

        var msg = input.Length >= 1000 ? input[..1000] : input;

        if (_instance.GlobalSettings["mu_emotes"].AsBoolean() && msg.StartsWith(':'))
        {
            msg = "/me " + msg[1..];
        }

        int ch = 0;
        var m = _chatRegex.Match(msg);
        if (m.Groups.Count > 2)
        {
            ch = int.Parse(m.Groups[1].Value);
            msg = m.Groups[2].Value;
        }

        if (_instance.CommandsManager.IsValidCommand(msg))
        {
            _instance.CommandsManager.ExecuteCommand(msg);
        }
        else
        {
            _instance.GestureManager.TryPreProcessChatMessage(msg, out var processedMessage, out _);
            processedMessage = processedMessage?.Trim();
            if (!string.IsNullOrEmpty(processedMessage))
            {
                NetCom.ChatOut(processedMessage, type, ch);
            }
        }
    }

    public void ShowAgentProfile(UUID agentId, string name)
    {
        if (agentId == UUID.Zero) return;
        _instance.ShowAgentProfile(name, agentId);
    }

    public void IMAvatar(UUID agentId, string name)
    {
        if (agentId == UUID.Zero || agentId == Client.Self.AgentID) return;
        _instance.RequestIM(agentId, name);
    }

    public void PayAvatar(UUID agentId, string name)
    {
        if (agentId == UUID.Zero || agentId == Client.Self.AgentID) return;
        _instance.OpenPayWindow(agentId, name);
    }

    public void ChatHistoryPrev()
    {
        if (_chatPointer == 0) return;
        _chatPointer--;
        if (_chatHistory.Count > _chatPointer)
        {
            ChatInput = _chatHistory[_chatPointer];
        }
    }

    public void ChatHistoryNext()
    {
        if (_chatPointer == _chatHistory.Count) return;
        _chatPointer++;
        if (_chatPointer == _chatHistory.Count)
        {
            ChatInput = string.Empty;
            return;
        }
        ChatInput = _chatHistory[_chatPointer];
    }

    #endregion

    #region Movement Controls

    [RelayCommand]
    private void ShowParcelProfile() => _instance.ShowLandProfile();

    [RelayCommand]
    private void ShowEstateProfile() => _instance.ShowEstateProfile();

    [RelayCommand]
    private void ToggleFly()
    {
        IsFlying = Movement.ToggleFlight();
    }

    [RelayCommand]
    private void ToggleRun()
    {
        IsRunning = Movement.ToggleAlwaysRun();
    }

    public void SetMovingForward(bool value) => Movement.MovingForward = value;
    public void SetMovingBackward(bool value) => Movement.MovingBackward = value;
    public void SetTurningLeft(bool value) => Movement.TurningLeft = value;
    public void SetTurningRight(bool value) => Movement.TurningRight = value;
    public void SetJump(bool value) => Movement.Jump = value;
    public void SetCrouch(bool value) => Movement.Crouch = value;

    #endregion

    #region Chat Event Handlers

    private void NetCom_ChatReceived(object? sender, ChatEventArgs e)
    {
        if (e.Message == null) return;

        // Determine style
        var lineType = e.SourceType switch
        {
            ChatSourceType.Agent when e.FromName == Client.Self.Name => ChatLineType.Self,
            ChatSourceType.Agent => ChatLineType.Normal,
            ChatSourceType.Object => ChatLineType.Object,
            _ => ChatLineType.System
        };

        if (e.Type == ChatType.StartTyping || e.Type == ChatType.StopTyping) return;

        string prefix = e.Type == ChatType.Shout ? " shouts" :
                        e.Type == ChatType.Whisper ? " whispers" : "";

        string text = e.Message.StartsWith("/me ", StringComparison.OrdinalIgnoreCase)
            ? e.Message[4..]
            : $"{prefix}: {e.Message}";

        if (e.Message.StartsWith("/me ", StringComparison.OrdinalIgnoreCase))
        {
            text = e.Message[4..];
            lineType = ChatLineType.Emote;
        }

        AddChatLine(new ChatLine(DateTime.Now, e.FromName, text, lineType, e.SourceID));
    }

    private void NetCom_ChatSent(object? sender, ChatSentEventArgs e)
    {
        if (e.Channel == 0) return;
        AddChatLine(new ChatLine(DateTime.Now, "You", $"(channel {e.Channel}): {e.Message}", ChatLineType.Self, Client.Self.AgentID));
    }

    private void NetCom_AlertMessageReceived(object? sender, AlertMessageEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            VelesNotificationService.Show("Alert", e.Message, Avalonia.Controls.Notifications.NotificationType.Warning));
    }

    private void NetCom_ClientLoginStatus(object? sender, LoginProgressEventArgs e)
    {
        if (e.Status != LoginStatus.Success) return;
        var motd = Client.Network.LoginResponseData?.Message;
        if (string.IsNullOrWhiteSpace(motd)) return;
        // Show MOTD in nearby chat as a system message (not logged to disk)
        AddChatLine(new ChatLine(DateTime.Now, "Welcome", $": {motd}", ChatLineType.System));
    }

    private void NetCom_ClientLoggedOut(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            VelesNotificationService.Show("Session", "Logged out.", Avalonia.Controls.Notifications.NotificationType.Information));
    }

    private void NetCom_ClientDisconnected(object? sender, DisconnectedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            VelesNotificationService.Show("Disconnected", e.Message, Avalonia.Controls.Notifications.NotificationType.Error));
    }

    private void Instance_NotificationInChat(object? sender, NotificationChatEventArgs e)
    {
        AddChatLine(new ChatLine(DateTime.Now, string.Empty, e.Message, ChatLineType.System));
    }

    private void AddChatLine(ChatLine line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ChatLines.Add(line);
            LogChatLine(line);
            if (!IsActive && line.Type is ChatLineType.Normal or ChatLineType.Object or ChatLineType.Emote)
                HasUnread = true;
        });
    }

    private void LogChatLine(ChatLine line)
    {
        if (line.Type is ChatLineType.System or ChatLineType.Alert or ChatLineType.History) return;
        if (!_instance.ChatLog.IsEnabled) return;
        var avatarName = Client.Self.Name;
        if (string.IsNullOrWhiteSpace(avatarName)) return;
        _instance.ChatLog.Log(avatarName, "chat", line.Timestamp, line.DisplayText);
    }

    #endregion

    #region Chat History
    private int  _historyOffset;
    private bool _historyExhausted;
    public  bool IsLoadingHistory { get; private set; }

    public bool HistoryExhausted => _historyExhausted;

    public void LoadInitialHistory()
    {
        var avatarName = Client.Self.Name;
        if (string.IsNullOrWhiteSpace(avatarName) || !_instance.ChatLog.IsEnabled) return;
        if (_historyOffset > 0) return; // already loaded once

        var chunk = _instance.ChatLog.ReadHistoryChunk(avatarName, "chat", 0, 50, out var hasMore);
        if (chunk.Count == 0) return;

        _historyOffset = chunk.Count;
        _historyExhausted = !hasMore;

        Dispatcher.UIThread.Post(() =>
        {
            IsLoadingHistory = true;
            for (int i = chunk.Count - 1; i >= 0; i--)
                ChatLines.Insert(0, chunk[i]);
            if (_historyOffset > 0)
                ChatLines.Insert(chunk.Count, new ChatLine(DateTime.MinValue, string.Empty,
                    "─── Previous messages ───", ChatLineType.System));
            IsLoadingHistory = false;
        });
    }

    public void LoadMoreHistory()
    {
        if (_historyExhausted || IsLoadingHistory) return;
        var avatarName = Client.Self.Name;
        if (string.IsNullOrWhiteSpace(avatarName) || !_instance.ChatLog.IsEnabled) return;

        IsLoadingHistory = true;
        var chunk = _instance.ChatLog.ReadHistoryChunk(avatarName, "chat", _historyOffset, 50, out var hasMore);
        _historyExhausted = !hasMore || chunk.Count == 0;

        Dispatcher.UIThread.Post(() =>
        {
            for (int i = chunk.Count - 1; i >= 0; i--)
                ChatLines.Insert(0, chunk[i]);
            _historyOffset += chunk.Count;
            if (chunk.Count == 0) _historyExhausted = true;
            IsLoadingHistory = false;
        });
    }

    #endregion

    #region Radar / Nearby Avatars

    private readonly Dictionary<UUID, ulong> _agentSimHandle = [];

    private void Self_TeleportProgress(object? sender, TeleportEventArgs e)
    {
        if (e.Status == TeleportStatus.Finished)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _agentSimHandle.Clear();
                NearbyAvatars.Clear();
                MinimapEntries.Clear();
                // Force-invalidate cached values so UpdateLocationText() always writes new sim data.
                RegionName = string.Empty;
                LocationText = string.Empty;
                ParcelName = string.Empty;
                MinimapTile = null;
                UpdateLocationText();
                FetchMinimapTile();
                RequestCurrentParcel();
            });
        }
        else if (e.Status == TeleportStatus.Failed)
        {
            // A failed teleport may leave CurrentSim temporarily null, causing UpdateLocationText()
            // to return early and leave stale region text. Force-refresh after failure.
            Dispatcher.UIThread.Post(() =>
            {
                RegionName = string.Empty;
                LocationText = string.Empty;
                UpdateLocationText();
            });
        }
    }

    private void Network_SimDisconnected(object? sender, SimDisconnectedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var handle = e.Simulator.Handle;
            // Reset parcel indicators if our current sim disconnected
            if (Client.Network.CurrentSim == null || Client.Network.CurrentSim.Handle == handle)
                ResetParcelFlags();

            var toRemove = new List<UUID>();
            foreach (var kv in _agentSimHandle)
            {
                if (kv.Value == handle) toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
            {
                _agentSimHandle.Remove(key);
                for (int i = NearbyAvatars.Count - 1; i >= 0; i--)
                {
                    if (NearbyAvatars[i].Id == key)
                        NearbyAvatars.RemoveAt(i);
                }
            }
            RebuildMinimap();
        });
    }

    private void Grid_CoarseLocationUpdate(object? sender, CoarseLocationUpdateEventArgs e)
    {
        // Pre-compute expensive structures on the LibreMetaverse event thread (not the UI thread).
        // Name lookups hit a ConcurrentDictionary and are safe to call from any thread.
        var sim = e.Simulator;
        var agentId = Client.Self.AgentID;

        var agentPosition = sim.AvatarPositions.TryGetValue(agentId, out var myPos)
            ? PositionHelper.ToGlobalPosition(sim.Handle, myPos)
            : Client.Self.GlobalPosition;
        if (agentPosition.Z < 0.1)
            agentPosition.Z = Client.Self.GlobalPosition.Z;

        var globalPositions = new Dictionary<UUID, Vector3d>(sim.AvatarPositions.Count);
        foreach (var kv in sim.AvatarPositions)
            globalPositions[kv.Key] = PositionHelper.ToGlobalPosition(sim.Handle, kv.Value);

        var avatarsById = new Dictionary<UUID, Avatar>(sim.ObjectsAvatars.Count);
        foreach (var kv in sim.ObjectsAvatars)
        {
            if (kv.Value != null && kv.Value.ID != UUID.Zero)
                avatarsById[kv.Value.ID] = kv.Value;
        }

        var precomputedNames = new Dictionary<UUID, string>(sim.AvatarPositions.Count);
        foreach (var id in sim.AvatarPositions.Keys)
            precomputedNames[id] = id == agentId
                ? _instance.Names.Get(id, Client.Self.Name)
                : _instance.Names.Get(id);

        Dispatcher.UIThread.Post(() =>
            UpdateRadar(e, agentPosition, globalPositions, avatarsById, precomputedNames));
    }

    private void UpdateRadar(
        CoarseLocationUpdateEventArgs e,
        Vector3d agentPosition,
        Dictionary<UUID, Vector3d> globalPositions,
        Dictionary<UUID, Avatar> avatarsById,
        Dictionary<UUID, string> precomputedNames)
    {
        if (Client.Network.CurrentSim == null) return;

        const double maxDistance = 362.0;

        var existing = new HashSet<UUID>();
        var removed = new List<UUID>(e.RemovedEntries);
        bool changed = removed.Count > 0;

        // Add new avatars
        foreach (var avatarPos in e.Simulator.AvatarPositions)
        {
            existing.Add(avatarPos.Key);
            if (_agentSimHandle.ContainsKey(avatarPos.Key)) continue;

            precomputedNames.TryGetValue(avatarPos.Key, out var name);
            var isSelf = avatarPos.Key == Client.Self.AgentID;
            NearbyAvatars.Add(new NearbyAvatar(avatarPos.Key, name ?? string.Empty, 0, isSelf));
            _agentSimHandle[avatarPos.Key] = e.Simulator.Handle;
            changed = true;
        }

        // Update existing and mark removed
        for (int i = NearbyAvatars.Count - 1; i >= 0; i--)
        {
            var entry = NearbyAvatars[i];
            if (_agentSimHandle.TryGetValue(entry.Id, out var handle) && handle != e.Simulator.Handle)
                continue;

            if (entry.IsSelf) continue;

            if (!existing.Contains(entry.Id) ||
                !e.Simulator.AvatarPositions.TryGetValue(entry.Id, out var pos))
            {
                removed.Add(entry.Id);
                changed = true;
                continue;
            }

            avatarsById.TryGetValue(entry.Id, out var foundAvi);
            var unknownAltitude = NetCom.LoginOptions.Grid?.Platform == "SecondLife"
                ? pos.Z == 1020f : pos.Z == 0f;

            var globalPos = globalPositions.TryGetValue(entry.Id, out var gp)
                ? gp : PositionHelper.ToGlobalPosition(e.Simulator.Handle, pos);

            if (unknownAltitude && foundAvi != null)
            {
                if (foundAvi.ParentID == 0)
                    globalPos.Z = foundAvi.Position.Z;
                else if (e.Simulator.ObjectsPrimitives.TryGetValue(foundAvi.ParentID, out var prim))
                    globalPos.Z = prim.Position.Z;
            }

            var d = (int)Vector3d.Distance(globalPos, agentPosition);

            if (e.Simulator != Client.Network.CurrentSim && d > maxDistance)
            {
                removed.Add(entry.Id);
                changed = true;
                continue;
            }

            var distText = unknownAltitude ? "?" : d.ToString();
            precomputedNames.TryGetValue(entry.Id, out var newName);
            newName ??= entry.Name;
            if (foundAvi != null) newName += "*";

            // Skip the replace if nothing actually changed — avoids triggering
            // unnecessary ObservableCollection notifications for every avatar each tick.
            if (newName == entry.Name && d == entry.Distance && distText == entry.DistanceText)
                continue;

            NearbyAvatars[i] = new NearbyAvatar(entry.Id, newName, d, entry.IsSelf, distText);
            changed = true;
        }

        // Remove departed avatars
        foreach (var key in removed)
        {
            _agentSimHandle.Remove(key);
            for (int i = NearbyAvatars.Count - 1; i >= 0; i--)
            {
                if (NearbyAvatars[i].Id == key)
                    NearbyAvatars.RemoveAt(i);
            }
        }

        // Only re-sort and rebuild the minimap when the list actually changed.
        if (changed)
        {
            SortNearbyAvatars();
            RebuildMinimap();
        }

        // Update location text
        UpdateLocationText();
    }

    private void SortNearbyAvatars()
    {
        var sorted = new List<NearbyAvatar>(NearbyAvatars);
        sorted.Sort((a, b) =>
        {
            if (a.IsSelf) return -1;
            if (b.IsSelf) return 1;
            return a.Distance.CompareTo(b.Distance);
        });

        for (int i = 0; i < sorted.Count; i++)
        {
            var currentIndex = NearbyAvatars.IndexOf(sorted[i]);
            if (currentIndex != i)
                NearbyAvatars.Move(currentIndex, i);
        }
    }

    private void RebuildMinimap()
    {
        MinimapEntries.Clear();
        foreach (var av in NearbyAvatars)
        {
            if (Client.Network.CurrentSim == null ||
                !Client.Network.CurrentSim.AvatarPositions.TryGetValue(av.Id, out var pos))
                continue;
            MinimapEntries.Add(new MinimapEntry(av.Id, av.Name, pos.X, pos.Y, pos.Z, av.IsSelf));
        }
    }

    public void WalkToPoint(float x, float y)
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        Client.Self.AutoPilotLocal((int)x, (int)y, Client.Self.SimPosition.Z);
    }

    public void TeleportToPoint(float x, float y)
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        Client.Self.Teleport(sim.Handle, new Vector3(x, y, Client.Self.SimPosition.Z));
    }

    private void UpdateLocationText()
    {
        try
        {
            var sim = Client.Network.CurrentSim;
            if (sim == null) return;

            var pos = Client.Self.SimPosition;
            var regionName = sim.Name;
            var coords = $"({(int)pos.X},{(int)pos.Y},{(int)pos.Z})";
            var maturity = sim.Access switch
            {
                SimAccess.Adult  => "Adult",
                SimAccess.Mature => "Moderate",
                _                => "General"
            };
            var locationText = $"{regionName} {coords}";

            // Only assign observable properties that actually changed to avoid
            // spurious binding updates on every CoarseLocationUpdate tick.
            if (regionName != RegionName) RegionName = regionName;
            if (coords != LocationCoords) LocationCoords = coords;
            if (maturity != MaturityText) MaturityText = maturity;
            if (locationText != LocationText) LocationText = locationText;
        }
        catch { }
    }

    private void Network_SimChanged(object? sender, SimChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Force-invalidate cached values so UpdateLocationText() always writes the new sim name.
            RegionName = string.Empty;
            LocationText = string.Empty;
            ParcelName = string.Empty;
            MinimapTile = null;
            ResetParcelFlags();
            UpdateLocationText();
            FetchMinimapTile();
            RequestCurrentParcel();
        });
    }

    private void Parcels_ParcelProperties(object? sender, ParcelPropertiesEventArgs e)
    {
        if (e.Result != ParcelResult.Single) return;
        _instance.State.Parcel = e.Parcel;
        Dispatcher.UIThread.Post(() =>
        {
            ParcelName = e.Parcel.Name ?? string.Empty;
            ApplyParcelFlags(e.Parcel.Flags);
        });
    }

    private void ApplyParcelFlags(ParcelFlags flags)
    {
        CanFly         = flags.HasFlag(ParcelFlags.AllowFly);
        CanBuild       = flags.HasFlag(ParcelFlags.CreateObjects);
        ScriptsAllowed = flags.HasFlag(ParcelFlags.AllowOtherScripts);
        PushAllowed    = !flags.HasFlag(ParcelFlags.RestrictPushObject);
        VoiceAllowed   = flags.HasFlag(ParcelFlags.AllowVoiceChat);
        IsDamageEnabled = flags.HasFlag(ParcelFlags.AllowDamage);

        if (IsDamageEnabled)
        {
            Health = Client.Self.Health;
            _healthTimer?.Start();
        }
        else
        {
            _healthTimer?.Stop();
            Health = 100f;
        }
    }

    private void ResetParcelFlags()
    {
        _healthTimer?.Stop();
        CanFly = true;
        CanBuild = true;
        ScriptsAllowed = true;
        PushAllowed = true;
        VoiceAllowed = true;
        IsDamageEnabled = false;
        Health = 100f;
    }

    private void RequestCurrentParcel()
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        var pos = Client.Self.SimPosition;
        // Request parcel properties for the 1m box surrounding the avatar
        Client.Parcels.RequestParcelProperties(sim,
            pos.Y + 0.5f, pos.X + 0.5f,
            pos.Y - 0.5f, pos.X - 0.5f,
            0, false);
    }

    private void FetchMinimapTile()
    {
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        Utils.LongToUInts(sim.Handle, out var gridX, out var gridY);
        gridX /= 256;
        gridY /= 256;

        var cached = MapTileCache.GetTile(gridX, gridY);
        if (cached != null)
        {
            MinimapTile = cached;
            return;
        }

        MapTileCache.RequestTile(gridX, gridY, () => MinimapTile = MapTileCache.GetTile(gridX, gridY));
    }

    #endregion
}

#region Data Models

public record ChatLine(DateTime Timestamp, string From, string Text, ChatLineType Type, UUID AgentID = default)
{
    public string FormattedTime => Timestamp == DateTime.MinValue ? string.Empty : $"[{Timestamp:HH:mm}]";

    public string DisplayText => Type == ChatLineType.Emote
        ? $"{From} {Text}"
        : $"{From}{Text}";

    public string AutomationText => $"{FormattedTime} {DisplayText}";

    public bool HasAgentLink => Type is ChatLineType.Normal or ChatLineType.Emote or ChatLineType.Self
                                && AgentID != UUID.Zero;

    public bool HasObjectLink => Type == ChatLineType.Object && AgentID != UUID.Zero;

    /// <summary>True when neither an agent nor an object clickable link is available.</summary>
    public bool ShowStaticName => !HasAgentLink && !HasObjectLink;

    /// <summary>True for lines loaded from the log file (rendered with reduced opacity).</summary>
    public bool IsHistory => Type == ChatLineType.History;

    public double HistoryOpacity => IsHistory ? 0.6 : 1.0;
}

public enum ChatLineType
{
    Normal,
    Self,
    Object,
    Emote,
    System,
    Alert,
    History   // Lines loaded from the log file (shown with dimmed style)
}

public record NearbyAvatar(UUID Id, string Name, int Distance, bool IsSelf, string DistanceText = "")
{
    public string DisplayText => IsSelf ? Name : $"{Name} ({DistanceText}m)";
}

public record MinimapEntry(UUID Id, string Name, float X, float Y, float Z, bool IsSelf);

#endregion
