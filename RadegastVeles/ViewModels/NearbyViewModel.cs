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
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.Voice.WebRTC;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class NearbyViewModel : TabViewModelBase, IChatContext
{
    private RadegastMovement Movement => _instance.Movement;

    private readonly Regex _chatRegex = new(@"^/(\d+)\s*(.*)", RegexOptions.Compiled);
    private readonly List<string> _chatHistory = [];
    private int _chatPointer;
    private DispatcherTimer? _healthTimer;
    private DispatcherTimer? _typingStopTimer;
    private bool _isSendingTyping;

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
    [NotifyPropertyChangedFor(nameof(SitOnGroundLabel))]
    private bool _isSittingOnGround;

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

    // Voice participant tracking (updated from VoiceViewModel events)
    private readonly HashSet<UUID> _voiceParticipantIds = [];
    private readonly HashSet<UUID> _speakingIds = [];
    // Typing indicator tracking (updated from AvatarManager.AvatarAnimation)
    private readonly HashSet<UUID> _typingIds = [];
    // Away / Busy status tracking (updated from AvatarManager.AvatarAnimation)
    private readonly HashSet<UUID> _awayIds = [];
    private readonly HashSet<UUID> _busyIds = [];
    // Flying / Sitting status tracking
    private readonly HashSet<UUID> _flyingIds = [];
    private readonly HashSet<UUID> _sittingIds = [];

    /// <summary>Voice ViewModel — set by MainViewModel after both are constructed.</summary>
    private VoiceViewModel? _voiceViewModel;
    public VoiceViewModel? Voice
    {
        get => _voiceViewModel;
        internal set
        {
            if (_voiceViewModel != null)
            {
                _voiceViewModel.PeerJoined        -= OnVoicePeerJoined;
                _voiceViewModel.PeerLeft          -= OnVoicePeerLeft;
                _voiceViewModel.PeerAudioUpdated  -= OnVoicePeerAudioUpdated;
                _voiceViewModel.PropertyChanged   -= OnVoicePropertyChanged;
            }
            _voiceViewModel = value;
            if (_voiceViewModel != null)
            {
                _voiceViewModel.PeerJoined        += OnVoicePeerJoined;
                _voiceViewModel.PeerLeft          += OnVoicePeerLeft;
                _voiceViewModel.PeerAudioUpdated  += OnVoicePeerAudioUpdated;
                _voiceViewModel.PropertyChanged   += OnVoicePropertyChanged;
            }
        }
    }

    private MediaViewModel? _media;
    /// <summary>Media ViewModel — set by MainViewModel after both are constructed.</summary>
    public MediaViewModel? Media
    {
        get => _media;
        internal set
        {
            if (_media != null) _media.PropertyChanged -= OnMediaPropertyChanged;
            _media = value;
            if (_media != null) _media.PropertyChanged += OnMediaPropertyChanged;
            IsMediaAvailable = _media != null;
            UpdateNowPlaying();
        }
    }

    [ObservableProperty]
    private string _nowPlayingText = string.Empty;

    [ObservableProperty]
    private bool _isStreamPlaying;

    private bool _isMediaAvailable;
    public bool IsMediaAvailable
    {
        get => _isMediaAvailable;
        private set => SetProperty(ref _isMediaAvailable, value);
    }

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaViewModel.SongTitle) or nameof(MediaViewModel.StationName))
            UpdateNowPlaying();
        else if (e.PropertyName == nameof(MediaViewModel.IsPlaying))
        {
            IsStreamPlaying = _media?.IsPlaying ?? false;
            if (!IsStreamPlaying) NowPlayingText = string.Empty;
        }
    }

    private void UpdateNowPlaying()
    {
        if (_media == null) { NowPlayingText = string.Empty; IsStreamPlaying = false; return; }
        IsStreamPlaying = _media.IsPlaying;
        var station = _media.StationName;
        var song = _media.SongTitle;
        if (!string.IsNullOrEmpty(song) && !string.IsNullOrEmpty(station))
            NowPlayingText = $"{station} - {song}";
        else if (!string.IsNullOrEmpty(song))
            NowPlayingText = song;
        else if (!string.IsNullOrEmpty(station))
            NowPlayingText = station;
        else
            NowPlayingText = string.Empty;
    }

    [RelayCommand]
    private void PlayStream() => _media?.PlayStreamCommand.Execute(null);

    [RelayCommand]
    private void StopStream() => _media?.StopStreamCommand.Execute(null);

    public override string UnreadTabLabel => HasUnread ? "Chat, new messages" : "Chat";

    [ObservableProperty]
    private Bitmap? _minimapTile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedMinimapEntryId))]
    private NearbyAvatar? _selectedNearbyAvatar;

    public UUID SelectedMinimapEntryId => SelectedNearbyAvatar?.Id ?? UUID.Zero;

    public string[] ChatTypes { get; } = ["Whisper", "Normal", "Shout"];

    public NearbyViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        // Subscribe to events
        NetCom.ChatReceived += NetCom_ChatReceived;
        NetCom.ChatSent += NetCom_ChatSent;
        NetCom.AlertMessageReceived += NetCom_AlertMessageReceived;
        NetCom.ClientLoginStatus += NetCom_ClientLoginStatus;
        NetCom.ClientLoggedOut += NetCom_ClientLoggedOut;
        NetCom.ClientDisconnected += NetCom_ClientDisconnected;

        RegisterClientEvents(Client);
        _instance.NotificationInChat += Instance_NotificationInChat;

        // Initial status
        StatusText = $"Logged in as {Client.Self.Name}";
        UpdateLocationText();
        FetchMinimapTile();
        RequestCurrentParcel();

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _healthTimer.Tick += (_, _) =>
        {
            Health = Client.Self.Health;
            IsSittingOnGround = Client.Self.Movement.SitOnGround;
        };

        ShowMotdIfAvailable();
    }

    public override void Dispose()
    {
        if (_media != null) _media.PropertyChanged -= OnMediaPropertyChanged;
        NetCom.ChatReceived -= NetCom_ChatReceived;
        NetCom.ChatSent -= NetCom_ChatSent;
        NetCom.AlertMessageReceived -= NetCom_AlertMessageReceived;
        NetCom.ClientLoginStatus -= NetCom_ClientLoginStatus;
        NetCom.ClientLoggedOut -= NetCom_ClientLoggedOut;
        NetCom.ClientDisconnected -= NetCom_ClientDisconnected;

        _instance.NotificationInChat -= Instance_NotificationInChat;

        _healthTimer?.Stop();
        _healthTimer = null;

        _typingStopTimer?.Stop();
        _typingStopTimer = null;

        base.Dispose();
    }

    // Typing-indicator debounce: send StartTyping on first keystroke,
    // StopTyping after 5 s of inactivity (or immediately on send/clear).
    partial void OnChatInputChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            if (!_isSendingTyping)
            {
                _isSendingTyping = true;
                NotifyTypingStarted();
            }
            if (_typingStopTimer == null)
            {
                _typingStopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _typingStopTimer.Tick += (_, _) => StopTypingIndicator();
            }
            _typingStopTimer.Stop();
            _typingStopTimer.Start();
        }
        else
        {
            StopTypingIndicator();
        }
    }

    internal void StopTypingIndicator()
    {
        _typingStopTimer?.Stop();
        if (_isSendingTyping)
        {
            _isSendingTyping = false;
            NotifyTypingStopped();
        }
    }

    protected override void RegisterClientEvents(GridClient client)
    {
        client.Grid.CoarseLocationUpdate += Grid_CoarseLocationUpdate;
        client.Self.TeleportProgress += Self_TeleportProgress;
        client.Network.SimDisconnected += Network_SimDisconnected;
        client.Network.SimChanged += Network_SimChanged;
        client.Parcels.ParcelProperties += Parcels_ParcelProperties;
        client.Avatars.AvatarAnimation += Avatars_AvatarAnimation;
    }

    protected override void UnregisterClientEvents(GridClient client)
    {
        client.Grid.CoarseLocationUpdate -= Grid_CoarseLocationUpdate;
        client.Self.TeleportProgress -= Self_TeleportProgress;
        client.Network.SimDisconnected -= Network_SimDisconnected;
        client.Network.SimChanged -= Network_SimChanged;
        client.Parcels.ParcelProperties -= Parcels_ParcelProperties;
        client.Avatars.AvatarAnimation -= Avatars_AvatarAnimation;
    }

    #region Chat Processing

    /// <summary>
    /// Called by the view when the user starts typing in the chat input box.
    /// Sends ChatType.StartTyping so nearby avatars see the typing animation.
    /// </summary>
    public void NotifyTypingStarted()
    {
        if (_instance.GlobalSettings["send_typing_notifications"].Type != OpenMetaverse.StructuredData.OSDType.Unknown
            && !_instance.GlobalSettings["send_typing_notifications"].AsBoolean()) return;
        Client.Self.Chat(string.Empty, 0, ChatType.StartTyping);
    }

    /// <summary>
    /// Called by the view when the user stops typing (idle timeout or message sent).
    /// Sends ChatType.StopTyping to clear the typing animation for nearby avatars.
    /// </summary>
    public void NotifyTypingStopped()
    {
        if (_instance.GlobalSettings["send_typing_notifications"].Type != OpenMetaverse.StructuredData.OSDType.Unknown
            && !_instance.GlobalSettings["send_typing_notifications"].AsBoolean()) return;
        Client.Self.Chat(string.Empty, 0, ChatType.StopTyping);
    }

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

        NotifyTypingStopped();
        ProcessChatInput(ChatInput, chatType);
        ChatInput = string.Empty;
    }

    [RelayCommand]
    private void SendWhisper()
    {
        if (string.IsNullOrEmpty(ChatInput)) return;
        NotifyTypingStopped();
        ProcessChatInput(ChatInput, ChatType.Whisper);
        ChatInput = string.Empty;
    }

    [RelayCommand]
    private void SendShout()
    {
        if (string.IsNullOrEmpty(ChatInput)) return;
        NotifyTypingStopped();
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
                // Apply @sendchat / @sendchannel restriction
                if (_instance.RLV?.Enabled == true &&
                    !_instance.RLV.Permissions.CanChat(ch, processedMessage))
                {
                    _instance.ShowNotificationInChat("[RLV] Sending chat is currently restricted.");
                    return;
                }
                NetCom.ChatOut(processedMessage, type, ch);
                if (ch == 0)
                    Voice?.VoiceSynth.SpeakOutbound(processedMessage);
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
        if (_instance.RLV?.Enabled == true && !_instance.RLV.Permissions.CanFly())
        {
            _instance.ShowNotificationInChat("[RLV] Flying is restricted.");
            return;
        }
        IsFlying = Movement.ToggleFlight();
    }

    [RelayCommand]
    private void ToggleRun()
    {
        IsRunning = Movement.ToggleAlwaysRun();
    }

    public string SitOnGroundLabel => IsSittingOnGround ? "Stand Up" : "Sit";

    [RelayCommand]
    private void ToggleSitOnGround()
    {
        if (IsSittingOnGround)
        {
            Client.Self.Stand();
            IsSittingOnGround = false;
        }
        else
        {
            Client.Self.SitOnGround();
            IsSittingOnGround = true;
        }
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

        // Process RLV commands from scripted objects — suppress them from the chat display
        if (e.SourceType == ChatSourceType.Object && e.Message.StartsWith("@"))
        {
            _ = _instance.RLV?.ProcessCMD(e);
            return;
        }

        // Apply @recvchat restriction — suppress incoming agent chat when restricted
        if (e.SourceType == ChatSourceType.Agent &&
            _instance.RLV?.Enabled == true &&
            !_instance.RLV.Permissions.CanReceiveChat(e.Message, e.SourceID.Guid))
        {
            return;
        }

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

        // Apply @shownames restriction — anonymize non-self agent names when restricted
        var fromName = e.SourceType == ChatSourceType.Agent &&
            e.SourceID != Client.Self.AgentID &&
            _instance.RLV?.Enabled == true &&
            !_instance.RLV.Permissions.CanShowNames(e.SourceID.Guid)
            ? "Nearby Resident"
            : e.FromName;

        AddChatLine(new ChatLine(DateTime.Now, fromName, text, lineType, e.SourceID));
    }

    private void NetCom_ChatSent(object? sender, ChatSentEventArgs e)
    {
        if (e.Channel == 0) return;
        AddChatLine(new ChatLine(DateTime.Now, "You", $"(channel {e.Channel}): {e.Message}", ChatLineType.Self, Client.Self.AgentID));
    }

    private void NetCom_AlertMessageReceived(object? sender, AlertMessageEventArgs e)
    {
        VelesNotificationService.Show("Alert", e.Message, Avalonia.Controls.Notifications.NotificationType.Warning);
    }

    private void NetCom_ClientLoginStatus(object? sender, LoginProgressEventArgs e)
    {
        if (e.Status != LoginStatus.Success) return;
        ShowMotdIfAvailable();
    }

    private void ShowMotdIfAvailable()
    {
        var motd = Client.Network.LoginResponseData?.Message;
        if (!string.IsNullOrWhiteSpace(motd))
            AddChatLine(new ChatLine(DateTime.Now, "Welcome", $": {motd}", ChatLineType.System));
    }

    private void NetCom_ClientLoggedOut(object? sender, EventArgs e)
    {
        VelesNotificationService.Show("Session", "Logged out.", Avalonia.Controls.Notifications.NotificationType.Information);
    }

    private void NetCom_ClientDisconnected(object? sender, DisconnectedEventArgs e)
    {
        VelesNotificationService.Show("Disconnected", e.Message, Avalonia.Controls.Notifications.NotificationType.Error);
    }

    private void Instance_NotificationInChat(object? sender, NotificationChatEventArgs e)
    {
        AddChatLine(new ChatLine(DateTime.Now, string.Empty, e.Message, ChatLineType.System));
    }

    private void AddChatLine(ChatLine line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MaybeInsertDateSeparator(ChatLines, line);
            ChatLines.Add(line);
            LogChatLine(line);
            if (!IsActive && line.Type is ChatLineType.Normal or ChatLineType.Object or ChatLineType.Emote)
                HasUnread = true;
        });
    }

    private static void MaybeInsertDateSeparator(ObservableCollection<ChatLine> lines, ChatLine incoming)
    {
        if (incoming.Type is ChatLineType.System or ChatLineType.DateSeparator) return;
        DateTime? lastDate = null;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var l = lines[i];
            if (l.Type is ChatLineType.DateSeparator or ChatLineType.System) continue;
            lastDate = l.Timestamp.Date;
            break;
        }
        if (lastDate.HasValue && incoming.Timestamp.Date != lastDate.Value)
            lines.Add(ChatLine.CreateDateSeparator(incoming.Timestamp.Date));
    }

    private void LogChatLine(ChatLine line)
    {
        if (line.Type is ChatLineType.System or ChatLineType.History) return;
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

    /// <summary>True when at least one history chunk has been loaded and more chunks remain above.</summary>
    public bool HasPrecedingHistory => _historyOffset > 0 && !_historyExhausted;

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

            DateTime? firstLiveDate = null;
            foreach (var l in ChatLines)
            {
                if (l.Type is ChatLineType.DateSeparator or ChatLineType.System) continue;
                firstLiveDate = l.Timestamp.Date;
                break;
            }

            var processed = ChatLine.WithDateSeparators(chunk, firstLiveDate);

            for (int i = processed.Count - 1; i >= 0; i--)
                ChatLines.Insert(0, processed[i]);
            if (_historyOffset > 0)
                ChatLines.Insert(processed.Count, new ChatLine(DateTime.MinValue, string.Empty,
                    "─── Previous messages ───", ChatLineType.System));
            IsLoadingHistory = false;
            OnPropertyChanged(nameof(HasPrecedingHistory));
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
            DateTime? oldestCurrentDate = null;
            foreach (var l in ChatLines)
            {
                if (l.Type is ChatLineType.DateSeparator or ChatLineType.System) continue;
                oldestCurrentDate = l.Timestamp.Date;
                break;
            }

            var processed = ChatLine.WithDateSeparators(chunk, oldestCurrentDate);

            for (int i = processed.Count - 1; i >= 0; i--)
                ChatLines.Insert(0, processed[i]);
            _historyOffset += chunk.Count;
            if (chunk.Count == 0) _historyExhausted = true;
            IsLoadingHistory = false;
            OnPropertyChanged(nameof(HasPrecedingHistory));
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
                IsSittingOnGround = false;
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
            NearbyAvatars.Add(new NearbyAvatar(avatarPos.Key, name ?? string.Empty, 0, isSelf,
                IsInVoice: _voiceParticipantIds.Contains(avatarPos.Key),
                IsSpeaking: _speakingIds.Contains(avatarPos.Key),
                IsTyping: _typingIds.Contains(avatarPos.Key),
                IsAway: _awayIds.Contains(avatarPos.Key),
                IsBusy: _busyIds.Contains(avatarPos.Key),
                IsFlying: _flyingIds.Contains(avatarPos.Key),
                IsSitting: _sittingIds.Contains(avatarPos.Key)));
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

            // Track sitting via ParentID — more reliable than any specific sit animation.
            if (foundAvi != null)
            {
                if (foundAvi.ParentID != 0) _sittingIds.Add(entry.Id);
                else _sittingIds.Remove(entry.Id);
            }

            NearbyAvatars[i] = new NearbyAvatar(entry.Id, newName, d, entry.IsSelf, distText,
                IsInVoice: _voiceParticipantIds.Contains(entry.Id),
                IsSpeaking: _speakingIds.Contains(entry.Id),
                IsTyping: _typingIds.Contains(entry.Id),
                IsAway: _awayIds.Contains(entry.Id),
                IsBusy: _busyIds.Contains(entry.Id),
                IsFlying: _flyingIds.Contains(entry.Id),
                IsSitting: _sittingIds.Contains(entry.Id));
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

            // Apply @showloc restriction — hide location when restricted
            if (_instance.RLV?.Enabled == true && !_instance.RLV.Permissions.CanShowLoc())
            {
                locationText = "(Hidden)";
                regionName = "(Hidden)";
            }

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

    #region Voice integration

    private void OnVoicePeerJoined(UUID peerId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _voiceParticipantIds.Add(peerId);
            RefreshNearbyAvatarVoiceState(peerId);
        });
    }

    private void OnVoicePeerLeft(UUID peerId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _voiceParticipantIds.Remove(peerId);
            _speakingIds.Remove(peerId);
            RefreshNearbyAvatarVoiceState(peerId);
        });
    }

    private void OnVoicePeerAudioUpdated(UUID peerId, LibreMetaverse.Voice.WebRTC.VoiceSession.PeerAudioState state)
    {
        bool speaking = state.VoiceActive ?? false;
        Dispatcher.UIThread.Post(() =>
        {
            if (speaking) _speakingIds.Add(peerId);
            else _speakingIds.Remove(peerId);
            RefreshNearbyAvatarVoiceState(peerId);
        });
    }

    private void OnVoicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When voice disconnects, clear all voice indicators
        if (e.PropertyName != nameof(VoiceViewModel.IsConnected)) return;
        if (_voiceViewModel?.IsConnected == false)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _voiceParticipantIds.Clear();
                _speakingIds.Clear();
                for (int i = 0; i < NearbyAvatars.Count; i++)
                {
                    var av = NearbyAvatars[i];
                    if (av.IsInVoice || av.IsSpeaking)
                        NearbyAvatars[i] = av with { IsInVoice = false, IsSpeaking = false };
                }
            });
        }
    }

    private void RefreshNearbyAvatarVoiceState(UUID peerId)
    {
        for (int i = 0; i < NearbyAvatars.Count; i++)
        {
            var av = NearbyAvatars[i];
            if (av.Id != peerId) continue;
            bool inVoice = _voiceParticipantIds.Contains(peerId);
            bool speaking = _speakingIds.Contains(peerId);
            if (av.IsInVoice != inVoice || av.IsSpeaking != speaking)
                NearbyAvatars[i] = av with { IsInVoice = inVoice, IsSpeaking = speaking };
            break;
        }
    }

    private void Avatars_AvatarAnimation(object? sender, AvatarAnimationEventArgs e)
    {
        // Ignore self — we don't show status indicators for the local avatar.
        if (e.AvatarID == Client.Self.AgentID) return;

        bool isTyping  = e.Animations.Any(a => a.AnimationID == Animations.TYPE);
        bool isAway    = e.Animations.Any(a => a.AnimationID == Animations.AWAY);
        bool isBusy    = e.Animations.Any(a => a.AnimationID == Animations.BUSY);
        bool isFlying  = e.Animations.Any(a =>
            a.AnimationID == Animations.FLY      ||
            a.AnimationID == Animations.FLYSLOW  ||
            a.AnimationID == Animations.HOVER    ||
            a.AnimationID == Animations.HOVER_UP ||
            a.AnimationID == Animations.HOVER_DOWN);
        Dispatcher.UIThread.Post(() =>
        {
            if (isTyping) _typingIds.Add(e.AvatarID);  else _typingIds.Remove(e.AvatarID);
            if (isAway)   _awayIds.Add(e.AvatarID);    else _awayIds.Remove(e.AvatarID);
            if (isBusy)   _busyIds.Add(e.AvatarID);    else _busyIds.Remove(e.AvatarID);
            if (isFlying) _flyingIds.Add(e.AvatarID);  else _flyingIds.Remove(e.AvatarID);
            RefreshNearbyAvatarStatusState(e.AvatarID);
        });
    }

    private void RefreshNearbyAvatarStatusState(UUID avatarId)
    {
        for (int i = 0; i < NearbyAvatars.Count; i++)
        {
            var av = NearbyAvatars[i];
            if (av.Id != avatarId) continue;
            bool typing  = _typingIds.Contains(avatarId);
            bool away    = _awayIds.Contains(avatarId);
            bool busy    = _busyIds.Contains(avatarId);
            bool flying  = _flyingIds.Contains(avatarId);
            bool sitting = _sittingIds.Contains(avatarId);
            if (av.IsTyping != typing || av.IsAway != away || av.IsBusy != busy ||
                av.IsFlying != flying || av.IsSitting != sitting)
                NearbyAvatars[i] = av with { IsTyping = typing, IsAway = away, IsBusy = busy, IsFlying = flying, IsSitting = sitting };
            break;
        }
    }

    #endregion

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

    /// <summary>Full date + time timestamp for copy operations.</summary>
    public string CopyText => IsDateSeparator || Timestamp == DateTime.MinValue
        ? string.Empty
        : $"[{Timestamp:yyyy-MM-dd HH:mm}] {DisplayText}";

    public bool HasAgentLink => Type is ChatLineType.Normal or ChatLineType.Emote or ChatLineType.Self
                                && AgentID != UUID.Zero;

    /// <summary>True for non-emote lines with a clickable agent name.</summary>
    public bool HasNonEmoteAgentLink => HasAgentLink && !IsEmote;

    public bool HasObjectLink => Type == ChatLineType.Object && AgentID != UUID.Zero;

    /// <summary>True when neither an agent nor an object clickable link is available, and not an emote (whose name is inline).</summary>
    public bool ShowStaticName => !HasAgentLink && !HasObjectLink && !IsEmote;

    /// <summary>True for lines loaded from the log file (rendered with reduced opacity).</summary>
    public bool IsHistory => Type == ChatLineType.History;

    public double HistoryOpacity => IsHistory ? 0.6 : 1.0;

    /// <summary>True when this line is a /me emote action.</summary>
    public bool IsEmote => Type == ChatLineType.Emote;

    /// <summary>False for emotes — emotes show the name inline as part of DisplayText, not as a separate prefix.</summary>
    public bool ShowNamePrefix => !IsEmote;

    /// <summary>Emote text wrapped in asterisks for display: *Name does something*</summary>
    public string EmoteDisplayText => $"* {From} {Text} *";

    public bool IsDateSeparator => Type == ChatLineType.DateSeparator;

    /// <summary>Human-readable date label shown in the day-change separator.</summary>
    public string DateLabel
    {
        get
        {
            var today = DateTime.Today;
            if (Timestamp.Date == today) return "Today";
            if (Timestamp.Date == today.AddDays(-1)) return "Yesterday";
            return Timestamp.ToString("dddd, MMMM d, yyyy");
        }
    }

    public static ChatLine CreateDateSeparator(DateTime date) =>
        new(date, string.Empty, string.Empty, ChatLineType.DateSeparator);

    /// <summary>
    /// Returns a new list that is <paramref name="chunk"/> with date-change separator items
    /// injected between messages on different calendar days.
    /// <paramref name="dateAfterChunk"/> is the date of the first item that will follow this
    /// chunk in the display list; pass it so a boundary separator is added when needed.
    /// </summary>
    public static List<ChatLine> WithDateSeparators(IReadOnlyList<ChatLine> chunk, DateTime? dateAfterChunk = null)
    {
        var result = new List<ChatLine>(chunk.Count + 4);
        DateTime? prevDate = null;

        foreach (var line in chunk)
        {
            if (line.Type is ChatLineType.System or ChatLineType.DateSeparator)
            {
                result.Add(line);
                continue;
            }
            var date = line.Timestamp.Date;
            if (prevDate.HasValue && date != prevDate.Value)
                result.Add(CreateDateSeparator(date));
            prevDate = date;
            result.Add(line);
        }

        if (prevDate.HasValue && dateAfterChunk.HasValue && prevDate.Value != dateAfterChunk.Value)
            result.Add(CreateDateSeparator(dateAfterChunk.Value));

        return result;
    }
}

public enum ChatLineType
{
    Normal,
    Self,
    Object,
    Emote,
    System,
    Alert,
    History,      // Lines loaded from the log file (shown with dimmed style)
    DateSeparator // Visual day-change divider — not a real chat message
}

public record NearbyAvatar(UUID Id, string Name, int Distance, bool IsSelf, string DistanceText = "", bool IsInVoice = false, bool IsSpeaking = false, bool IsTyping = false, bool IsAway = false, bool IsBusy = false, bool IsFlying = false, bool IsSitting = false)
{
    public string DisplayText => IsSelf ? Name : $"{Name} ({DistanceText}m)";
    /// <summary>Mic icon shown when the avatar is in the same voice channel.</summary>
    public string VoiceIcon => IsSpeaking ? "🔊" : IsInVoice ? "🎙" : string.Empty;
    public bool ShowVoiceIcon => IsInVoice || IsSpeaking;
    /// <summary>Typing indicator icon.</summary>
    public string TypingIcon => IsTyping ? "✍" : string.Empty;
    public bool ShowTypingIcon => IsTyping;
    /// <summary>Away indicator icon.</summary>
    public string AwayIcon => IsAway ? "💤" : string.Empty;
    public bool ShowAwayIcon => IsAway;
    /// <summary>Busy (Do Not Disturb) indicator icon.</summary>
    public string BusyIcon => IsBusy ? "⛔" : string.Empty;
    public bool ShowBusyIcon => IsBusy;
    /// <summary>Flying indicator icon.</summary>
    public string FlyingIcon => IsFlying ? "✈" : string.Empty;
    public bool ShowFlyingIcon => IsFlying;
    /// <summary>Sitting indicator icon.</summary>
    public string SittingIcon => IsSitting ? "🪑" : string.Empty;
    public bool ShowSittingIcon => IsSitting;
}

public record MinimapEntry(UUID Id, string Name, float X, float Y, float Z, bool IsSelf);

#endregion
