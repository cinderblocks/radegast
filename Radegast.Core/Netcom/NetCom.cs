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
using System.Threading;
using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// NetCom is a class built on top of libsecondlife that provides a way to
    /// raise events on the proper thread (for GUI apps especially).
    /// </summary>
    public partial class NetCom : INetCom, IDisposable
    {
        private readonly GridClient Client;
        public LoginOptions LoginOptions { get; set; } = new LoginOptions();

        public bool AgreeToTos { get; set; } = false;
        public Grid Grid { get; private set; }

        // Duplicate suppression caches (short-lived)
        private readonly ConcurrentDictionary<string, DateTime> _recentChatHashes = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, DateTime> _recentIMHashes = new ConcurrentDictionary<string, DateTime>();
        private readonly TimeSpan _duplicateWindow = TimeSpan.FromSeconds(2);
        private readonly Timer _cleanupTimer;

        private void CleanupRecentCaches()
        {
            try
            {
                var cutoff = DateTime.UtcNow - _duplicateWindow;
                foreach (var k in _recentChatHashes.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                    _recentChatHashes.TryRemove(k, out _);
                foreach (var k in _recentIMHashes.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                    _recentIMHashes.TryRemove(k, out _);
            }
            catch (Exception ex)
            {
                Logger.Warn("CleanupRecentCaches failed", ex);
            }
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
            catch (Exception ex)
            {
                Logger.Warn("ClearDuplicateCaches failed", ex);
            }
        }

        private bool IsDuplicateChat(ChatEventArgs e)
        {
            try
            {
                // Normalize to avoid trivial differences bypassing suppression
                var key = $"{e.SourceID}:{e.FromName}:{e.Message}".Trim().ToLowerInvariant();
                var now = DateTime.UtcNow;

                if (_recentChatHashes.TryGetValue(key, out var t) && now - t <= _duplicateWindow) return true;

                // record/refresh timestamp
                _recentChatHashes.AddOrUpdate(key, now, (k, old) => now);
            }
            catch (Exception ex)
            {
                Logger.Warn("IsDuplicateChat failed", ex);
            }

            return false;
        }

        private bool IsDuplicateIM(InstantMessageEventArgs e)
        {
            try
            {
                var im = e.IM;
                var key = $"{im.FromAgentID}:{im.IMSessionID}:{im.Message}".Trim().ToLowerInvariant();
                var now = DateTime.UtcNow;

                if (_recentIMHashes.TryGetValue(key, out var t) && now - t <= _duplicateWindow) return true;

                _recentIMHashes.AddOrUpdate(key, now, (k, old) => now);
            }
            catch (Exception ex)
            {
                Logger.Warn("IsDuplicateIM failed", ex);
            }

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

        public NetCom(GridClient client)
        {
            Client = client;
            RegisterClientEvents(Client);

            // start periodic cleanup to remove stale entries from duplicate caches
            _cleanupTimer = new Timer(_ => CleanupRecentCaches(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public void Dispose()
        {
            try
            {
                _cleanupTimer?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn("Dispose cleanup timer failed", ex);
            }

            try
            {
                if (Client != null)
                {
                    UnregisterClientEvents(Client);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Dispose unregister client events failed", ex);
            }
        }

        private void RegisterClientEvents(GridClient client)
        {
            if (client == null) return;

            try
            {
                if (client.Self != null)
                {
                    client.Self.ChatFromSimulator += Self_ChatFromSimulator;
                    client.Self.IM += Self_IM;
                    client.Self.MoneyBalance += Self_MoneyBalance;
                    client.Self.TeleportProgress += Self_TeleportProgress;
                    client.Self.AlertMessage += Self_AlertMessage;
                }

                if (client.Network != null)
                {
                    client.Network.Disconnected += Network_Disconnected;
                    client.Network.LoginProgress += Network_LoginProgress;
                    client.Network.LoggedOut += Network_LoggedOut;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("RegisterClientEvents failed", ex);
            }
        }

        private void UnregisterClientEvents(GridClient client)
        {
            if (client == null) return;

            try
            {
                if (client.Self != null)
                {
                    client.Self.ChatFromSimulator -= Self_ChatFromSimulator;
                    client.Self.IM -= Self_IM;
                    client.Self.MoneyBalance -= Self_MoneyBalance;
                    client.Self.TeleportProgress -= Self_TeleportProgress;
                    client.Self.AlertMessage -= Self_AlertMessage;
                }

                if (client.Network != null)
                {
                    client.Network.Disconnected -= Network_Disconnected;
                    client.Network.LoginProgress -= Network_LoginProgress;
                    client.Network.LoggedOut -= Network_LoggedOut;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("UnregisterClientEvents failed", ex);
            }
        }

        public void Instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            try
            {
                UnregisterClientEvents(e.OldClient);
                RegisterClientEvents(e.Client);
            }
            catch (Exception ex)
            {
                Logger.Warn("Instance_ClientChanged failed", ex);
            }
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
                IsLoggingIn = false;
                return;
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
                var key = $"out:{Client.Self.AgentID}:{chat}".Trim().ToLowerInvariant();
                _recentChatHashes.AddOrUpdate(key, DateTime.UtcNow, (k, old) => DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                Logger.Warn("ChatOut duplicate cache update failed", ex);
            }

            OnChatSent(new ChatSentEventArgs(chat, type, channel));
        }

        public void SendInstantMessage(string message, UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            Client.Self.InstantMessage(
                LoginOptions.FullName, target, message, session, InstantMessageDialog.MessageFromAgent,
                InstantMessageOnline.Online, Client.Self.SimPosition, Client.Network.CurrentSim.ID, null);

            var ev = new InstantMessageSentEventArgs(message, target, session, DateTime.UtcNow, LoginOptions.FullName, Client.Self.AgentID);
            try
            {
                var key = $"outim:{ev.FromAgentID}:{ev.SessionID}:{ev.Message}".Trim().ToLowerInvariant();
                _recentIMHashes.AddOrUpdate(key, DateTime.UtcNow, (k, old) => DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                Logger.Warn("SendInstantMessage duplicate cache update failed", ex);
            }

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

        public bool IsLoggingIn { get; private set; } = false;

        public bool IsLoggedIn { get; private set; } = false;

        private void Self_IM(object sender, InstantMessageEventArgs e)
        {
            if (IsDuplicateIM(e)) return;
            OnInstantMessageReceived(e);
        }

        private void Network_LoginProgress(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Success)
            {
                IsLoggedIn = true;
                Client.Self.RequestBalance();
                OnClientConnected(EventArgs.Empty);
                IsLoggingIn = false;
            }

            LoginProgressEventArgs ea = new LoginProgressEventArgs(e.Status, e.Message, string.Empty);

            OnClientLoginStatus(e);

            if (e.Status == LoginStatus.Failed || e.Status == LoginStatus.Success)
            {
                IsLoggingIn = false;
            }
        }

        private void Network_LoggedOut(object sender, LoggedOutEventArgs e)
        {
            IsLoggedIn = false;
            ClearDuplicateCaches();
            OnClientLoggedOut(EventArgs.Empty);
        }

        private void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            OnTeleportStatusChanged(e);
        }

        private void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            if (IsDuplicateChat(e)) return;
            OnChatReceived(e);
        }

        private void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            IsLoggedIn = false;
            ClearDuplicateCaches();
            OnClientDisconnected(e);
        }

        private void Self_MoneyBalance(object sender, BalanceEventArgs e)
        {
            OnMoneyBalanceUpdated(e);
        }

        private void Self_AlertMessage(object sender, AlertMessageEventArgs e)
        {
             OnAlertMessageReceived(e);
        }
    }
}
