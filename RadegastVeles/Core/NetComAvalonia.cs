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
using System.Linq;
using Avalonia.Threading;
using OpenMetaverse;

namespace Radegast.Veles.Core;

/// <summary>
/// NetCom implementation for Avalonia that marshals events to the UI thread
/// via the Avalonia Dispatcher.
/// </summary>
public sealed class NetComAvalonia : INetCom
{
    private GridClient Client;

    public bool AgreeToTos { get; set; }
    public Grid Grid { get; private set; } = null!;
    public LoginOptions LoginOptions { get; set; } = new();

    public bool IsLoggingIn { get; private set; }
    public bool IsLoggedIn { get; private set; }

    // Duplicate suppression
    private readonly ConcurrentDictionary<string, DateTime> _recentChatHashes = new();
    private readonly ConcurrentDictionary<string, DateTime> _recentIMHashes = new();
    private readonly TimeSpan _duplicateWindow = TimeSpan.FromSeconds(2);

    #region Events

    public event EventHandler<EventArgs>? ClientConnected;
    public event EventHandler<OverrideEventArgs>? ClientLoggingIn;
    public event EventHandler<LoginProgressEventArgs>? ClientLoginStatus;
    public event EventHandler<OverrideEventArgs>? ClientLoggingOut;
    public event EventHandler? ClientLoggedOut;
    public event EventHandler<DisconnectedEventArgs>? ClientDisconnected;
    public event EventHandler<ChatEventArgs>? ChatReceived;
    public event EventHandler<ChatSentEventArgs>? ChatSent;
    public event EventHandler<InstantMessageEventArgs>? InstantMessageReceived;
    public event EventHandler<InstantMessageSentEventArgs>? InstantMessageSent;
    public event EventHandler<TeleportEventArgs>? TeleportStatusChanged;
    public event EventHandler<AlertMessageEventArgs>? AlertMessageReceived;
    public event EventHandler<BalanceEventArgs>? MoneyBalanceUpdated;

    #endregion

    public NetComAvalonia(GridClient client)
    {
        Client = client;
        RegisterClientEvents(Client);
    }

    public void Dispose()
    {
        UnregisterClientEvents(Client);
    }

    private void RegisterClientEvents(GridClient client)
    {
        client.Self.ChatFromSimulator += Self_ChatFromSimulator;
        client.Self.IM += Self_IM;
        client.Self.MoneyBalance += Self_MoneyBalance;
        client.Self.TeleportProgress += Self_TeleportProgress;
        client.Self.AlertMessage += Self_AlertMessage;
        client.Network.Disconnected += Network_Disconnected;
        client.Network.LoginProgress += Network_LoginProgress;
        client.Network.LoggedOut += Network_LoggedOut;
        client.Network.RegisterLoginResponseCallback(Network_LoginResponseCallback);
    }

    private void UnregisterClientEvents(GridClient client)
    {
        client.Self.ChatFromSimulator -= Self_ChatFromSimulator;
        client.Self.IM -= Self_IM;
        client.Self.MoneyBalance -= Self_MoneyBalance;
        client.Self.TeleportProgress -= Self_TeleportProgress;
        client.Self.AlertMessage -= Self_AlertMessage;
        client.Network.Disconnected -= Network_Disconnected;
        client.Network.LoginProgress -= Network_LoginProgress;
        client.Network.LoggedOut -= Network_LoggedOut;
        client.Network.UnregisterLoginResponseCallback(Network_LoginResponseCallback);
    }

    public void Instance_ClientChanged(object? sender, ClientChangedEventArgs e)
    {
        UnregisterClientEvents(e.OldClient);
        Client = e.Client;
        RegisterClientEvents(Client);
    }

    public void Login()
    {
        if (IsLoggingIn)
        {
            Client.Network.AbortLogin();
        }

        IsLoggingIn = true;

        var ea = new OverrideEventArgs();
        ClientLoggingIn?.Invoke(this, ea);
        if (ea.Cancel)
        {
            IsLoggingIn = false;
            return;
        }

        if (string.IsNullOrEmpty(LoginOptions.FirstName) ||
            string.IsNullOrEmpty(LoginOptions.LastName) ||
            string.IsNullOrEmpty(LoginOptions.Password))
        {
            ClientLoginStatus?.Invoke(this,
                new LoginProgressEventArgs(LoginStatus.Failed, "One or more fields are blank.", string.Empty));
            IsLoggingIn = false;
            return;
        }

        string startLocation = string.Empty;
        string loginLocation = string.Empty;

        switch (LoginOptions.StartLocation)
        {
            case StartLocationType.Home:
                startLocation = "home";
                break;
            case StartLocationType.Last:
                startLocation = "last";
                break;
            case StartLocationType.Custom:
                var parser = new SlurlParser(LoginOptions.StartLocationCustom.Trim());
                startLocation = parser.GetStartLocationUri();
                break;
        }

        string password = LoginOptions.IsPasswordMD5(LoginOptions.Password!)
            ? LoginOptions.Password!
            : Utils.MD5(LoginOptions.Password!.Length > 16
                ? LoginOptions.Password[..16]
                : LoginOptions.Password);

        var loginParams = Client.Network.DefaultLoginParams(
            LoginOptions.FirstName!, LoginOptions.LastName!, password,
            LoginOptions.Channel, LoginOptions.Version);

        Grid = LoginOptions.Grid!;
        loginParams.Start = startLocation;
        loginParams.LoginLocation = loginLocation;
        loginParams.AgreeToTos = AgreeToTos;
        loginParams.URI = Grid!.LoginURI;
        loginParams.LastExecEvent = LoginOptions.LastExecEvent;
        loginParams.MfaEnabled = true;
        loginParams.MfaHash = LoginOptions.MfaHash!;
        loginParams.Token = LoginOptions.MfaToken!;

        Client.Network.BeginLogin(loginParams);
    }

    public void CancelLogin()
    {
        if (!IsLoggingIn) { return; }
        Client.Network.AbortLogin();
        IsLoggingIn = false;
    }

    public void Logout()
    {
        if (!IsLoggedIn)
        {
            ClientLoggedOut?.Invoke(this, EventArgs.Empty);
            return;
        }

        var ea = new OverrideEventArgs();
        ClientLoggingOut?.Invoke(this, ea);
        if (ea.Cancel) return;

        Client.Network.Logout();
    }

    public void ChatOut(string chat, ChatType type, int channel)
    {
        if (!IsLoggedIn) return;

        Client.Self.Chat(chat, channel, type);
        try
        {
            _recentChatHashes[$"out:{Client.Self.AgentID}:{chat}"] = DateTime.UtcNow;
        }
        catch { }
        ChatSent?.Invoke(this, new ChatSentEventArgs(chat, type, channel));
    }

    public void SendInstantMessage(string message, UUID target, UUID session)
    {
        if (!IsLoggedIn) return;

        Client.Self.InstantMessage(
            LoginOptions.FullName, target, message, session,
            InstantMessageDialog.MessageFromAgent,
            InstantMessageOnline.Online, Client.Self.SimPosition,
            Client.Network.CurrentSim?.ID ?? UUID.Zero, Array.Empty<byte>());

        var ev = new InstantMessageSentEventArgs(message, target, session,
            DateTime.Now, LoginOptions.FullName, Client.Self.AgentID);
        try
        {
            _recentIMHashes[$"outim:{ev.FromAgentID}:{ev.SessionID}:{ev.Message}"] = DateTime.UtcNow;
        }
        catch { }
        InstantMessageSent?.Invoke(this, ev);
    }

    public void SendIMStartTyping(UUID target, UUID session)
    {
        if (!IsLoggedIn) return;
        Client.Self.InstantMessage(
            LoginOptions.FullName, target, "typing", session,
            InstantMessageDialog.StartTyping,
            InstantMessageOnline.Online, Client.Self.SimPosition,
            Client.Network.CurrentSim?.ID ?? UUID.Zero, Array.Empty<byte>());
    }

    public void SendIMStopTyping(UUID target, UUID session)
    {
        if (!IsLoggedIn) return;
        Client.Self.InstantMessage(
            LoginOptions.FullName, target, "typing", session,
            InstantMessageDialog.StopTyping,
            InstantMessageOnline.Online, Client.Self.SimPosition,
            Client.Network.CurrentSim?.ID ?? UUID.Zero, Array.Empty<byte>());
    }

    public void ClearDuplicateCaches()
    {
        try
        {
            _recentChatHashes.Clear();
            _recentIMHashes.Clear();
        }
        catch { }
    }

    private void CleanupRecentCaches()
    {
        var cutoff = DateTime.UtcNow - _duplicateWindow;
        foreach (var k in _recentChatHashes.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
            _recentChatHashes.TryRemove(k, out _);
        foreach (var k in _recentIMHashes.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
            _recentIMHashes.TryRemove(k, out _);
    }

    private bool IsDuplicateChat(ChatEventArgs e)
    {
        try
        {
            CleanupRecentCaches();
            var key = $"{e.SourceID}:{e.FromName}:{e.Message}";
            var now = DateTime.UtcNow;
            if (_recentChatHashes.TryGetValue(key, out var t) && now - t <= _duplicateWindow) return true;
            _recentChatHashes[key] = now;
        }
        catch { }
        return false;
    }

    private bool IsDuplicateIM(InstantMessageEventArgs e)
    {
        try
        {
            CleanupRecentCaches();
            var im = e.IM;
            var key = $"{im.FromAgentID}:{im.IMSessionID}:{im.Message}";
            var now = DateTime.UtcNow;
            if (_recentIMHashes.TryGetValue(key, out var t) && now - t <= _duplicateWindow) return true;
            _recentIMHashes[key] = now;
        }
        catch { }
        return false;
    }

    // Marshal events to Avalonia UI thread
    private void PostToUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void Self_ChatFromSimulator(object? sender, ChatEventArgs e)
    {
        if (IsDuplicateChat(e)) return;
        PostToUI(() => ChatReceived?.Invoke(this, e));
    }

    private void Self_IM(object? sender, InstantMessageEventArgs e)
    {
        if (IsDuplicateIM(e)) return;
        PostToUI(() => InstantMessageReceived?.Invoke(this, e));
    }

    private void Self_MoneyBalance(object? sender, BalanceEventArgs e)
    {
        PostToUI(() => MoneyBalanceUpdated?.Invoke(this, e));
    }

    private void Self_TeleportProgress(object? sender, TeleportEventArgs e)
    {
        PostToUI(() => TeleportStatusChanged?.Invoke(this, e));
    }

    private void Self_AlertMessage(object? sender, AlertMessageEventArgs e)
    {
        PostToUI(() => AlertMessageReceived?.Invoke(this, e));
    }

    private void Network_LoginResponseCallback(bool loginSuccess, bool redirect, string message, string reason, LoginResponseData? replyData)
    {
        // Capture the server-returned MFA hash on both challenge and success so
        // the correct hash is available for the next login attempt.
        if ((loginSuccess || reason == "mfa_challenge") && replyData != null)
        {
            LoginOptions.MfaHash = replyData.MfaHash;
        }
    }

    private void Network_LoginProgress(object? sender, LoginProgressEventArgs e)
    {
        if (e.Status == LoginStatus.Success)
        {
            IsLoggedIn = true;
            Client.Self.RequestBalance();
            PostToUI(() => ClientConnected?.Invoke(this, EventArgs.Empty));
        }

        if (e.Status == LoginStatus.Failed)
        {
            IsLoggingIn = false;
        }

        PostToUI(() => ClientLoginStatus?.Invoke(this, e));
    }

    private void Network_LoggedOut(object? sender, LoggedOutEventArgs e)
    {
        IsLoggedIn = false;
        IsLoggingIn = false;
        ClearDuplicateCaches();
        PostToUI(() => ClientLoggedOut?.Invoke(this, EventArgs.Empty));
    }

    private void Network_Disconnected(object? sender, DisconnectedEventArgs e)
    {
        IsLoggedIn = false;
        IsLoggingIn = false;
        PostToUI(() => ClientDisconnected?.Invoke(this, e));
    }
}
