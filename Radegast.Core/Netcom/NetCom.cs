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
using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// NetCom is a class built on top of libsecondlife that provides a way to
    /// raise events on the proper thread (for GUI apps especially).
    /// </summary>
    public partial class NetCom : INetCom
    {
        private readonly GridClient Client;
        public LoginOptions LoginOptions { get; set; } = new LoginOptions();

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

        public NetCom(GridClient client)
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
                    var parser = new LocationParser(LoginOptions.StartLocationCustom.Trim());
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
            OnChatSent(new ChatSentEventArgs(chat, type, channel));
        }

        public void SendInstantMessage(string message, UUID target, UUID session)
        {
            if (!IsLoggedIn) return;

            Client.Self.InstantMessage(
                LoginOptions.FullName, target, message, session, InstantMessageDialog.MessageFromAgent,
                InstantMessageOnline.Online, Client.Self.SimPosition, Client.Network.CurrentSim.ID, null);

            OnInstantMessageSent(new InstantMessageSentEventArgs(message, target, session, DateTime.Now));
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
            OnInstantMessageReceived(e);
        }

        private void Network_LoginProgress(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Success)
            {
                IsLoggedIn = true;
                Client.Self.RequestBalance();
                OnClientConnected(EventArgs.Empty);
            }

            LoginProgressEventArgs ea = new LoginProgressEventArgs(e.Status, e.Message, string.Empty);

            OnClientLoginStatus(e);
        }

        private void Network_LoggedOut(object sender, LoggedOutEventArgs e)
        {
            IsLoggedIn = false;
            OnClientLoggedOut(EventArgs.Empty);
        }

        private void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            OnTeleportStatusChanged(e);
        }

        private void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            OnChatReceived(e);
        }

        private void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            IsLoggedIn = false;

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
