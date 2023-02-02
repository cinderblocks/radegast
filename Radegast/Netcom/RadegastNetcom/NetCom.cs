/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2023, Sjofn, LLC
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
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// Netcom is a class built on top of libsecondlife that provides a way to
    /// raise events on the proper thread (for GUI apps especially).
    /// </summary>
    public partial class Netcom : IDisposable
    {
        private readonly RadegastInstance instance;
        private GridClient client => instance.Client;
        public LoginOptions loginOptions;

        public bool AgreeToTos { get; set; } = false;
        public Grid Grid { get; private set; }

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

        public Netcom(RadegastInstance instance)
        {
            this.instance = instance;
            loginOptions = new LoginOptions();

            instance.ClientChanged += instance_ClientChanged;
            RegisterClientEvents(client);
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

        void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(client);
        }

        private bool CanSyncInvoke => NetcomSync != null && !NetcomSync.IsDisposed && NetcomSync.IsHandleCreated && NetcomSync.InvokeRequired;

        public void Dispose()
        {
            if (client != null)
            {
                UnregisterClientEvents(client);
            }
        }

        void Self_IM(object sender, InstantMessageEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnInstantMessageRaise(OnInstantMessageReceived), e);
            else
                OnInstantMessageReceived(e);
        }

        void Network_LoginProgress(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Success)
            {
                IsLoggedIn = true;
                client.Self.RequestBalance();
                if (CanSyncInvoke)
                {
                    NetcomSync.BeginInvoke(new ClientConnectedRaise(OnClientConnected), EventArgs.Empty);
                }
                else
                {
                    OnClientConnected(EventArgs.Empty);
                }
            }

            if (e.Status == LoginStatus.Failed)
            {
                instance.MarkEndExecution();
            }

            LoginProgressEventArgs ea = new LoginProgressEventArgs(e.Status, e.Message, string.Empty);

            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnClientLoginRaise(OnClientLoginStatus), e);
            else
                OnClientLoginStatus(e);
        }

        void Network_LoggedOut(object sender, LoggedOutEventArgs e)
        {
            IsLoggedIn = false;

            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnClientLogoutRaise(OnClientLoggedOut), EventArgs.Empty);
            else
                OnClientLoggedOut(EventArgs.Empty);
        }

        void Self_TeleportProgress(object sender, TeleportEventArgs e)
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

        void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            IsLoggedIn = false;
            instance.MarkEndExecution();

            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnClientDisconnectRaise(OnClientDisconnected), e);
            else
                OnClientDisconnected(e);
        }

        void Self_MoneyBalance(object sender, BalanceEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnMoneyBalanceRaise(OnMoneyBalanceUpdated), e);
            else
                OnMoneyBalanceUpdated(e);
        }

        void Self_AlertMessage(object sender, AlertMessageEventArgs e)
        {
            if (CanSyncInvoke)
                NetcomSync.BeginInvoke(new OnAlertMessageRaise(OnAlertMessageReceived), e);
            else
                OnAlertMessageReceived(e);
        }

        public void Login()
        {
            IsLoggingIn = true;

            // Report crashes only once and not on relogs/reconnects
            LastExecStatus execStatus = instance.GetLastExecStatus();
            if (!instance.AnotherInstanceRunning() && execStatus != LastExecStatus.Normal && (!instance.ReportedCrash))
            {
                instance.ReportedCrash = true;
                loginOptions.LastExecEvent = execStatus;
                Logger.Log("Reporting crash of the last application run to the grid login service", Helpers.LogLevel.Warning);
            }
            else
            {
                loginOptions.LastExecEvent = LastExecStatus.Normal;
                Logger.Log("Reporting normal shutdown of the last application run to the grid login service", Helpers.LogLevel.Info);
            }
            instance.MarkStartExecution();

            OverrideEventArgs ea = new OverrideEventArgs();
            OnClientLoggingIn(ea);

            if (ea.Cancel)
            {
                IsLoggingIn = false;
                return;
            }

            if (string.IsNullOrEmpty(loginOptions.FirstName) ||
                string.IsNullOrEmpty(loginOptions.LastName) ||
                string.IsNullOrEmpty(loginOptions.Password))
            {
                OnClientLoginStatus(
                    new LoginProgressEventArgs(LoginStatus.Failed, "One or more fields are blank.", string.Empty));
            }

            string startLocation = string.Empty;
            string loginLocation = string.Empty;

            switch (loginOptions.StartLocation)
            {
                case StartLocationType.Home: startLocation = "home"; break;
                case StartLocationType.Last: startLocation = "last"; break;

                case StartLocationType.Custom:
                    var parser = new LocationParser(loginOptions.StartLocationCustom.Trim());
                    startLocation = parser.GetStartLocationUri();
                    break;
            }

            string password;

            if (LoginOptions.IsPasswordMD5(loginOptions.Password))
            {
                password = loginOptions.Password;
            }
            else
            {
                password = RadegastUtils.Utils.MD5(loginOptions.Password.Length > 16
                    ? loginOptions.Password.Substring(0, 16)
                    : loginOptions.Password);
            }

            LoginParams loginParams = client.Network.DefaultLoginParams(
                loginOptions.FirstName, loginOptions.LastName, password,
                loginOptions.Channel, loginOptions.Version);

            Grid = loginOptions.Grid;
            loginParams.Start = startLocation;
            loginParams.LoginLocation = loginLocation;
            loginParams.AgreeToTos = AgreeToTos;
            loginParams.URI = Grid.LoginURI;
            loginParams.LastExecEvent = loginOptions.LastExecEvent;
            loginParams.MfaEnabled = true;
            loginParams.MfaHash = loginOptions.MfaHash;
            loginParams.Token = loginOptions.MfaToken;

            client.Network.BeginLogin(loginParams);
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
            if (ea.Cancel) return;

            client.Network.Logout();
        }

        public void ChatOut(string chat, ChatType type, int channel)
        {
            if (!IsLoggedIn) return;

            client.Self.Chat(chat, channel, type);
            OnChatSent(new ChatSentEventArgs(chat, type, channel));
        }

        public void SendInstantMessage(string message, UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            client.Self.InstantMessage(
                loginOptions.FullName, target, message, session, InstantMessageDialog.MessageFromAgent,
                InstantMessageOnline.Online, client.Self.SimPosition, client.Network.CurrentSim.ID, null);

            OnInstantMessageSent(new InstantMessageSentEventArgs(message, target, session, DateTime.Now));
        }

        public void SendIMStartTyping(UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            client.Self.InstantMessage(
                loginOptions.FullName, target, "typing", session, InstantMessageDialog.StartTyping,
                InstantMessageOnline.Online, client.Self.SimPosition, client.Network.CurrentSim.ID, null);
        }

        public void SendIMStopTyping(UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            client.Self.InstantMessage(
                loginOptions.FullName, target, "typing", session, InstantMessageDialog.StopTyping,
                InstantMessageOnline.Online, client.Self.SimPosition, client.Network.CurrentSim.ID, null);
        }

        public bool IsLoggingIn { get; private set; } = false;

        public bool IsLoggedIn { get; private set; } = false;

        public LoginOptions LoginOptions
        {
            get => loginOptions;
            set => loginOptions = value;
        }

        public Control NetcomSync { get; set; }
    }
}
