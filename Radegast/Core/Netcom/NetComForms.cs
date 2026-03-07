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
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// Netcom is a class built on top of libsecondlife that provides a way to
    /// raise events on the proper thread (for GUI apps especially).
    /// </summary>
    public partial class NetComForms : INetCom
    {
        private GridClient Client;

        public bool AgreeToTos { get; set; } = false;
        public Grid Grid { get; private set; }
        public LoginOptions LoginOptions { get; set; } = new LoginOptions();

        // duplicate suppression caches
        private readonly ConcurrentDictionary<string, DateTime> _recentChatHashes = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, DateTime> _recentIMHashes = new ConcurrentDictionary<string, DateTime>();
        private readonly TimeSpan _duplicateWindow = TimeSpan.FromSeconds(2);

        private void CleanupRecentCaches()
        {
            var cutoff = DateTime.UtcNow - _duplicateWindow;
            foreach (var k in _recentChatHashes.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                _recentChatHashes.TryRemove(k, out _);
            foreach (var k in _recentIMHashes.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                _recentIMHashes.TryRemove(k, out _);
        }

        /// <summary>
        /// Clears all duplicate detection caches. Called when disconnecting/reconnecting
        /// to avoid stale state from previous sessions.
        /// </summary>
        public void ClearDuplicateCaches()
        {
            try
            {
                _recentChatHashes.Clear();
                _recentIMHashes.Clear();
            }
            catch { }
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

        // NetcomSync is used for raising certain events on the
        // GUI/main thread. Useful if you're modifying GUI controls
        // in the client app when responding to those events.

        #region ClientConnected event
        /// <summary>The event subscribers, null of no subscribers</summary>
        private EventHandler<EventArgs> m_ClientConnected;

        ///<summary>Raises the ClientConnected Event</summary>
        /// <param name="e">A ClientConnectedEventArgs object containing
        /// the old and the new client</param>
        protected virtual void OnClientConnected(EventArgs e)
        {
            EventHandler<EventArgs> handler = m_ClientConnected;
            handler?.Invoke(this, e);
        }

        /// <summary>Thread sync lock object</summary>
        private readonly object m_ClientConnectedLock = new object();

        /// <summary>Raise event delegate</summary>
        private delegate void ClientConnectedRaise(EventArgs e);

        /// <summary>Raised when the GridClient object in the main Radegast instance is changed</summary>
        public event EventHandler<EventArgs> ClientConnected
        {
            add { lock (m_ClientConnectedLock) { m_ClientConnected += value; } }
            remove { lock (m_ClientConnectedLock) { m_ClientConnected -= value; } }
        }
        #endregion ClientConnected event

        public NetComForms(GridClient client)
        {
            Client = client;
            RegisterClientEvents(Client);
        }

        public void Dispose()
        {
            if (Client != null)
            {
                UnregisterClientEvents(Client);
            }
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
        }

        public void Instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            Client = e.Client;
            RegisterClientEvents(Client);
        }

        public void Login()
        {
            IsLoggingIn = true;


            OverrideEventArgs ea = new OverrideEventArgs();
            OnClientLoggingIn(ea);

            if (ea.Cancel)
            {
                IsLoggingIn = false;
                return;
            }

            if (string.IsNullOrEmpty(LoginOptions.FirstName) ||
                string.IsNullOrEmpty(LoginOptions.LastName) ||
                string.IsNullOrEmpty(LoginOptions.Password))
            {
                OnClientLoginStatus(
                    new LoginProgressEventArgs(LoginStatus.Failed, "One or more fields are blank.", string.Empty));
            }

            string startLocation = string.Empty;
            string loginLocation = string.Empty;

            switch (LoginOptions.StartLocation)
            {
                case StartLocationType.Home: startLocation = "home"; break;
                case StartLocationType.Last: startLocation = "last"; break;

                case StartLocationType.Custom:
                    var parser = new SlurlParser(LoginOptions.StartLocationCustom.Trim());
                    startLocation = parser.GetStartLocationUri();
                    break;
            }

            string password;

            if (LoginOptions.IsPasswordMD5(LoginOptions.Password))
            {
                password = LoginOptions.Password;
            }
            else
            {
                password = Utils.MD5(LoginOptions.Password.Length > 16
                    ? LoginOptions.Password.Substring(0, 16) 
                    : LoginOptions.Password);
            }

            LoginParams loginParams = Client.Network.DefaultLoginParams(
                LoginOptions.FirstName, LoginOptions.LastName, password,
                LoginOptions.Channel, LoginOptions.Version);

            Grid = LoginOptions.Grid;
            loginParams.Start = startLocation;
            loginParams.LoginLocation = loginLocation;
            loginParams.AgreeToTos = AgreeToTos;
            loginParams.URI = Grid.LoginURI;
            loginParams.LastExecEvent = LoginOptions.LastExecEvent;
            loginParams.MfaEnabled = true;
            loginParams.MfaHash = LoginOptions.MfaHash;
            loginParams.Token = LoginOptions.MfaToken;

            Client.Network.BeginLogin(loginParams);
        }

        public void Logout()
        {
            if (!IsLoggedIn)
            {
                OnClientLoggedOut(EventArgs.Empty);
                return;
            }

            OverrideEventArgs ea = new OverrideEventArgs();
            OnClientLoggingOut(ea);
            if (ea.Cancel) { return; }

            Client.Network.Logout();
        }

        public void ChatOut(string chat, ChatType type, int channel)
        {
            if (!IsLoggedIn) return;

            Client.Self.Chat(chat, channel, type);
            try
            {
                var key = $"out:{Client.Self.AgentID}:{chat}";
                _recentChatHashes[key] = DateTime.UtcNow;
            }
            catch { }
            OnChatSent(new ChatSentEventArgs(chat, type, channel));
        }

        public void SendInstantMessage(string message, UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            Client.Self.InstantMessage(
                LoginOptions.FullName, target, message, session, InstantMessageDialog.MessageFromAgent,
                InstantMessageOnline.Online, Client.Self.SimPosition, Client.Network.CurrentSim.ID, null);

            var ev = new InstantMessageSentEventArgs(message, target, session, DateTime.Now, LoginOptions.FullName, Client.Self.AgentID);
            try
            {
                var key = $"outim:{ev.FromAgentID}:{ev.SessionID}:{ev.Message}";
                _recentIMHashes[key] = DateTime.UtcNow;
            }
            catch { }

            OnInstantMessageSent(ev);
        }

        public void SendIMStartTyping(UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            Client.Self.InstantMessage(
                LoginOptions.FullName, target, "typing", session, InstantMessageDialog.StartTyping,
                InstantMessageOnline.Online, Client.Self.SimPosition, Client.Network.CurrentSim.ID, null);
        }

        public void SendIMStopTyping(UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            Client.Self.InstantMessage(
                LoginOptions.FullName, target, "typing", session, InstantMessageDialog.StopTyping,
                InstantMessageOnline.Online, Client.Self.SimPosition, Client.Network.CurrentSim.ID, null);
        }

        public bool IsLoggingIn { get; private set; }

        public bool IsLoggedIn { get; private set; }

        private bool CanSyncInvoke => NetcomSync != null && !NetcomSync.IsDisposed && NetcomSync.IsHandleCreated && NetcomSync.InvokeRequired;

        private void Self_IM(object sender, InstantMessageEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnInstantMessageRaise(OnInstantMessageReceived), e);
            else
                OnInstantMessageReceived(e);
        }

        private void Network_LoginProgress(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Success)
            {
                IsLoggedIn = true;
                Client.Self.RequestBalance();
                if (CanSyncInvoke)
                {
                    NetcomSync.BeginInvoke(new ClientConnectedRaise(OnClientConnected), EventArgs.Empty);
                }
                else
                {
                    OnClientConnected(EventArgs.Empty);
                }
            }

            LoginProgressEventArgs ea = new LoginProgressEventArgs(e.Status, e.Message, string.Empty);

            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnClientLoginRaise(OnClientLoginStatus), e);
            else
                OnClientLoginStatus(e);
        }

        private void Network_LoggedOut(object sender, LoggedOutEventArgs e)
        {
            IsLoggedIn = false;
            ClearDuplicateCaches();

            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnClientLogoutRaise(OnClientLoggedOut), EventArgs.Empty);
            else
                OnClientLoggedOut(EventArgs.Empty);
        }

        private void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnTeleportStatusRaise(OnTeleportStatusChanged), e);
            else
                OnTeleportStatusChanged(e);
        }

        private void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnChatRaise(OnChatReceived), e);
            else
                OnChatReceived(e);
        }

        private void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            IsLoggedIn = false;

            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnClientDisconnectRaise(OnClientDisconnected), e);
            else
                OnClientDisconnected(e);
        }

        private void Self_MoneyBalance(object sender, BalanceEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnMoneyBalanceRaise(OnMoneyBalanceUpdated), e);
            else
                OnMoneyBalanceUpdated(e);
        }

        private void Self_AlertMessage(object sender, AlertMessageEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnAlertMessageRaise(OnAlertMessageReceived), e);
            else
                OnAlertMessageReceived(e);
        }

        public Control NetcomSync { get; set; }
    }
}
