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
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public enum IMSessionType
{
    Personal,
    Group,
    Conference
}

public partial class IMViewModel : ObservableObject, IDisposable, IChatContext
{
    private readonly RadegastInstanceAvalonia _instance;
    private INetCom NetCom => _instance.NetCom;
    private GridClient Client => _instance.Client;

    public RadegastInstanceAvalonia Instance => _instance;

    public ObservableCollection<IMSession> Sessions { get; } = [];

    [ObservableProperty]
    private IMSession? _selectedSession;

    [ObservableProperty]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    private string _statusText = "No conversations";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadTabLabel))]
    private bool _hasUnreadAny;

    /// <summary>True when the IMs tab is the currently selected tab.</summary>
    public bool IsActive { get; set; }

    /// <summary>Screenreader-friendly tab label that announces unread state.</summary>
    public string UnreadTabLabel => HasUnreadAny ? "IMs, new messages" : "IMs";

    /// <summary>
    /// Number of IM/Group/Conference sessions that have at least one unread message.
    /// Drives the app-icon badge; hidden when zero.
    /// </summary>
    public int UnreadConversationCount => Sessions.Count(s => s.HasUnread);

    public void ClearUnread()
    {
        foreach (var s in Sessions)
            s.HasUnread = false;
        HasUnreadAny = false;
    }

    private void UpdateHasUnreadAny()
    {
        HasUnreadAny = Sessions.Any(s => s.HasUnread);
        OnPropertyChanged(nameof(UnreadConversationCount));
    }

    public IMViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        NetCom.InstantMessageReceived += NetCom_InstantMessageReceived;
        NetCom.InstantMessageSent += NetCom_InstantMessageSent;
        _instance.ClientChanged += Instance_ClientChanged;
        RegisterClientEvents(Client);
    }

    public void Dispose()
    {
        NetCom.InstantMessageReceived -= NetCom_InstantMessageReceived;
        NetCom.InstantMessageSent -= NetCom_InstantMessageSent;
        _instance.ClientChanged -= Instance_ClientChanged;
        UnregisterClientEvents(Client);
    }

    private void RegisterClientEvents(GridClient client)
    {
        client.Self.ChatSessionMemberAdded += Self_ChatSessionMemberAdded;
        client.Self.ChatSessionMemberLeft += Self_ChatSessionMemberLeft;
    }

    private void UnregisterClientEvents(GridClient client)
    {
        client.Self.ChatSessionMemberAdded -= Self_ChatSessionMemberAdded;
        client.Self.ChatSessionMemberLeft -= Self_ChatSessionMemberLeft;
    }

    private void Instance_ClientChanged(object? sender, ClientChangedEventArgs e)
    {
        UnregisterClientEvents(e.OldClient);
        RegisterClientEvents(e.Client);
        Dispatcher.UIThread.Post(() =>
        {
            Sessions.Clear();
            SelectedSession = null;
            StatusText = "No conversations";
        });
    }

    #region Session Management

    /// <summary>
    /// Opens a P2P IM session with the given agent. Creates the session tab if it
    /// does not already exist and selects it.
    /// </summary>
    public void OpenIMSession(UUID agentId, string agentName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var session = GetOrCreatePersonalSession(agentId, agentName);
            SelectedSession = session;
        });
    }

    /// <summary>
    /// Opens a group IM/chat session. Creates the session tab if it does not
    /// already exist, joins the session, and selects it.
    /// </summary>
    public void OpenGroupIMSession(UUID groupId, string groupName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var session = GetOrCreateGroupSession(groupId, groupName);
            SelectedSession = session;
            // Ask the server to join/start the group session if not already joined
            Client.Self.RequestJoinGroupChat(groupId);
        });
    }

    private IMSession GetOrCreatePersonalSession(UUID agentId, string agentName)
    {
        var sessionId = Client.Self.AgentID ^ agentId;
        var existing = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (existing != null) return existing;

        var session = new IMSession(sessionId, agentName, IMSessionType.Personal, agentId);
        Sessions.Add(session);
        UpdateStatus();
        LoadSessionHistory(session);
        PlayUISound(UISounds.IM);
        return session;
    }

    private IMSession GetOrCreateGroupSession(UUID sessionId, string groupName)
    {
        var existing = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (existing != null) return existing;

        var session = new IMSession(sessionId, groupName, IMSessionType.Group, sessionId);
        Sessions.Add(session);
        UpdateStatus();
        LoadSessionHistory(session);
        PlayUISound(UISounds.IM);
        return session;
    }

    private IMSession GetOrCreateConferenceSession(UUID sessionId, string label)
    {
        var existing = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (existing != null) return existing;

        var session = new IMSession(sessionId, label, IMSessionType.Conference, sessionId);
        Sessions.Add(session);
        UpdateStatus();
        LoadSessionHistory(session);
        PlayUISound(UISounds.IM);
        return session;
    }

    private void UpdateStatus()
    {
        StatusText = Sessions.Count == 0
            ? "No conversations"
            : $"{Sessions.Count} conversation{(Sessions.Count != 1 ? "s" : "")}";
    }

    #region History Loading

    private void LoadSessionHistory(IMSession session)
    {
        if (!_instance.ChatLog.IsEnabled) return;
        var avatarName = Client.Self.Name;
        if (string.IsNullOrWhiteSpace(avatarName)) return;

        var chunk = _instance.ChatLog.ReadHistoryChunk(avatarName, session.Label, 0, 50, out var hasMore);
        if (chunk.Count == 0) return;

        session.HistoryOffset = chunk.Count;
        session.HistoryExhausted = !hasMore;

        // Must be on UI thread (called from GetOrCreate which is always on UI thread)
        for (int i = chunk.Count - 1; i >= 0; i--)
            session.Messages.Insert(0, chunk[i]);
        session.Messages.Insert(chunk.Count, new ChatLine(DateTime.MinValue, string.Empty,
            "─── Previous messages ───", ChatLineType.System));
    }

    public void LoadMoreHistory(IMSession session)
    {
        if (session.HistoryExhausted || session.IsLoadingHistory) return;
        if (!_instance.ChatLog.IsEnabled) return;
        var avatarName = Client.Self.Name;
        if (string.IsNullOrWhiteSpace(avatarName)) return;

        session.IsLoadingHistory = true;
        var chunk = _instance.ChatLog.ReadHistoryChunk(avatarName, session.Label, session.HistoryOffset, 50, out var hasMore);
        session.HistoryExhausted = !hasMore || chunk.Count == 0;

        for (int i = chunk.Count - 1; i >= 0; i--)
            session.Messages.Insert(0, chunk[i]);
        session.HistoryOffset += chunk.Count;
        if (chunk.Count == 0) session.HistoryExhausted = true;
        session.IsLoadingHistory = false;
    }

    #endregion

    #endregion

    #region Commands

    [RelayCommand]
    private void SendMessage()
    {
        if (SelectedSession == null || string.IsNullOrEmpty(MessageInput)) return;

        var text = MessageInput.Length >= 1000 ? MessageInput[..1000] : MessageInput;
        MessageInput = string.Empty;

        var session = SelectedSession;

        switch (session.SessionType)
        {
            case IMSessionType.Personal:
                NetCom.SendInstantMessage(text, session.TargetId, session.SessionId);
                break;

            case IMSessionType.Group:
                Client.Self.InstantMessageGroup(session.SessionId, text);
                AddOutgoingMessage(session, text);
                break;

            case IMSessionType.Conference:
                Client.Self.InstantMessageGroup(session.SessionId, text);
                AddOutgoingMessage(session, text);
                break;
        }
    }

    [RelayCommand]
    private void CloseSession()
    {
        if (SelectedSession == null) return;

        var session = SelectedSession;

        // Leave group/conference chat sessions on the server side
        if (session.SessionType is IMSessionType.Group or IMSessionType.Conference)
        {
            Client.Self.RequestLeaveGroupChat(session.SessionId);
        }

        Sessions.Remove(session);

        if (Sessions.Count > 0)
            SelectedSession = Sessions[^1];
        else
            SelectedSession = null;

        UpdateStatus();
    }

    #endregion

    #region Incoming Messages

    private void NetCom_InstantMessageReceived(object? sender, InstantMessageEventArgs e)
    {
        // This event is already marshalled to the UI thread by NetComAvalonia
        switch (e.IM.Dialog)
        {
            case InstantMessageDialog.MessageFromAgent:
                HandleMessageFromAgent(e);
                break;

            case InstantMessageDialog.SessionSend:
                HandleSessionSend(e);
                break;

            case InstantMessageDialog.StartTyping:
                HandleTypingNotification(e, true);
                break;

            case InstantMessageDialog.StopTyping:
                HandleTypingNotification(e, false);
                break;

            // Other dialog types (teleport offers, friend requests, etc.) are not
            // IM-tab conversations; they are handled elsewhere.
        }
    }

    private void HandleMessageFromAgent(InstantMessageEventArgs e)
    {
        // System messages
        if (e.IM.FromAgentName == "Second Life" || e.IM.FromAgentID == UUID.Zero)
        {
            _instance.ShowNotificationInChat($"{e.IM.FromAgentName}: {e.IM.Message}");
            return;
        }

        // Group IM
        if (e.IM.GroupIM || _instance.Groups.ContainsKey(e.IM.IMSessionID))
        {
            HandleGroupIM(e);
            return;
        }

        // Conference (ad-hoc multi-party)
        if (e.IM.BinaryBucket.Length > 1)
        {
            HandleConferenceIM(e);
            return;
        }

        // Zero-session system notification
        if (e.IM.IMSessionID == UUID.Zero)
        {
            _instance.ShowNotificationInChat(
                $"Message from {_instance.Names.Get(e.IM.FromAgentID, e.IM.FromAgentName)}: {e.IM.Message}");
            return;
        }

        // Regular P2P IM
        HandlePersonalIM(e);
    }

    private void HandlePersonalIM(InstantMessageEventArgs e)
    {
        var fromName = _instance.Names.Get(e.IM.FromAgentID, e.IM.FromAgentName);
        var session = GetOrCreatePersonalSession(e.IM.FromAgentID, fromName);
        AddMessage(session, new ChatLine(DateTime.Now, fromName, $": {e.IM.Message}", ChatLineType.Normal, e.IM.FromAgentID));
        MarkUnreadIfNotSelected(session);
        if (!IsActive)
            VelesNotificationService.Show(fromName, e.IM.Message);
    }

    private void HandleGroupIM(InstantMessageEventArgs e)
    {
        var groupName = Utils.BytesToString(e.IM.BinaryBucket);
        if (string.IsNullOrEmpty(groupName) && _instance.Groups.TryGetValue(e.IM.IMSessionID, out var grp))
        {
            groupName = grp.Name;
        }
        groupName = string.IsNullOrEmpty(groupName) ? "Group Chat" : groupName;

        var session = GetOrCreateGroupSession(e.IM.IMSessionID, groupName);
        var fromName = _instance.Names.Get(e.IM.FromAgentID, e.IM.FromAgentName);
        AddMessage(session, new ChatLine(DateTime.Now, fromName, $": {e.IM.Message}", ChatLineType.Normal, e.IM.FromAgentID));
        MarkUnreadIfNotSelected(session);
        if (!IsActive)
            VelesNotificationService.Show(groupName, $"{fromName}: {e.IM.Message}");
    }

    private void HandleConferenceIM(InstantMessageEventArgs e)
    {
        var label = Utils.BytesToString(e.IM.BinaryBucket);
        if (string.IsNullOrEmpty(label)) label = "Conference";

        var session = GetOrCreateConferenceSession(e.IM.IMSessionID, label);
        var fromName = _instance.Names.Get(e.IM.FromAgentID, e.IM.FromAgentName);
        AddMessage(session, new ChatLine(DateTime.Now, fromName, $": {e.IM.Message}", ChatLineType.Normal, e.IM.FromAgentID));
        MarkUnreadIfNotSelected(session);
    }

    private void HandleSessionSend(InstantMessageEventArgs e)
    {
        if (_instance.Groups.ContainsKey(e.IM.IMSessionID))
        {
            HandleGroupIM(e);
        }
        else
        {
            HandleConferenceIM(e);
        }
    }

    private void HandleTypingNotification(InstantMessageEventArgs e, bool isTyping)
    {
        var sessionId = Client.Self.AgentID ^ e.IM.FromAgentID;
        var session = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        session?.IsPartnerTyping = isTyping;
    }

    private void MarkUnreadIfNotSelected(IMSession session)
    {
        if (SelectedSession != session)
        {
            session.HasUnread = true;
            UpdateHasUnreadAny();
        }
    }

    #endregion

    #region Outgoing Messages

    private void NetCom_InstantMessageSent(object? sender, InstantMessageSentEventArgs e)
    {
        // For P2P IMs sent via NetCom.SendInstantMessage, the event fires on the
        // UI thread already. Add the outgoing message to the session log.
        var session = Sessions.FirstOrDefault(s => s.SessionId == e.SessionID);
        if (session != null)
        {
            AddMessage(session, new ChatLine(DateTime.Now, Client.Self.Name, $": {e.Message}", ChatLineType.Self, Client.Self.AgentID));
        }
    }

    private void AddOutgoingMessage(IMSession session, string text)
    {
        AddMessage(session, new ChatLine(DateTime.Now, Client.Self.Name, $": {text}", ChatLineType.Self, Client.Self.AgentID));
    }

    private void AddMessage(IMSession session, ChatLine line)
    {
        session.Messages.Add(line);
        if (_instance.ChatLog.IsEnabled)
        {
            var avatarName = Client.Self.Name;
            if (!string.IsNullOrWhiteSpace(avatarName))
                _instance.ChatLog.Log(avatarName, session.Label, line.Timestamp, line.DisplayText);
        }
    }

    private void PlayUISound(UUID sound)
    {
        _instance.MediaManager?.PlayUISound(sound);
    }

    #endregion

    partial void OnSelectedSessionChanged(IMSession? value)
    {
        if (value != null)
        {
            value.HasUnread = false;
            UpdateHasUnreadAny();
            if (value.ShowParticipants)
                RefreshParticipants(value);
        }
    }

    public void ShowAgentProfile(UUID agentId, string name)
    {
        if (agentId == UUID.Zero) return;
        _instance.ShowAgentProfile(name, agentId);
    }

    public void ShowSessionProfile(IMSession session)
    {
        switch (session.SessionType)
        {
            case IMSessionType.Personal:
                _instance.ShowAgentProfile(session.Label, session.TargetId);
                break;
            case IMSessionType.Group:
                _instance.ShowGroupProfile(session.TargetId);
                break;
        }
    }

    #region Participant Tracking

    private void Self_ChatSessionMemberAdded(object? sender, ChatSessionMemberAddedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var session = Sessions.FirstOrDefault(s => s.SessionId == e.SessionID);
            if (session?.ShowParticipants == true)
                RefreshParticipants(session);
        });
    }

    private void Self_ChatSessionMemberLeft(object? sender, ChatSessionMemberLeftEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var session = Sessions.FirstOrDefault(s => s.SessionId == e.SessionID);
            if (session?.ShowParticipants == true)
                RefreshParticipants(session);
        });
    }

    private void RefreshParticipants(IMSession session)
    {
        session.Participants.Clear();
        if (!Client.Self.GroupChatSessions.TryGetValue(session.SessionId, out var members)) return;
        foreach (var m in members)
        {
            var name = _instance.Names.Get(m.AvatarKey);
            session.Participants.Add(new IMParticipant(m.AvatarKey, name, m.IsModerator));
        }
    }

    #endregion
}

#region Data Models

public partial class IMSession : ObservableObject
{
    public UUID SessionId { get; }
    public string Label { get; }
    public IMSessionType SessionType { get; }
    public UUID TargetId { get; }

    public ObservableCollection<ChatLine> Messages { get; } = [];
    public ObservableCollection<IMParticipant> Participants { get; } = [];

    public bool ShowParticipants => SessionType is IMSessionType.Group or IMSessionType.Conference;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _hasUnread;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(TypingText))]
    private bool _isPartnerTyping;

    public IMSession(UUID sessionId, string label, IMSessionType sessionType, UUID targetId)
    {
        SessionId = sessionId;
        Label = label;
        SessionType = sessionType;
        TargetId = targetId;
    }

    // History loading state
    public int  HistoryOffset     { get; set; }
    public bool HistoryExhausted  { get; set; }
    public bool IsLoadingHistory  { get; set; }

    /// <summary>True for P2P or Group sessions where clicking opens a profile.</summary>
    public bool HasProfileLink => SessionType is IMSessionType.Personal or IMSessionType.Group;

    public string TypeIcon => SessionType switch
    {
        IMSessionType.Group => "\U0001F465",
        IMSessionType.Conference => "\U0001F4AC",
        _ => "\U0001F4E8"
    };

    public string DisplayText
    {
        get
        {
            var prefix = HasUnread ? "● " : "";
            return $"{prefix}{TypeIcon} {Label}";
        }
    }

    /// <summary>Non-empty when a partner is typing; used for the typing indicator label.</summary>
    public string TypingText => IsPartnerTyping ? $"{Label} is typing..." : string.Empty;
}

public record IMParticipant(UUID Id, string Name, bool IsModerator)
{
    public string DisplayText => IsModerator ? $"{Name} ★" : Name;
}

#endregion
