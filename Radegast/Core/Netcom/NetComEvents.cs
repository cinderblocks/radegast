/**
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
    public partial class NetComForms
    {
        // For the NetcomSync stuff
        private delegate void OnClientLoginRaise(LoginProgressEventArgs e);
        private delegate void OnClientLogoutRaise(EventArgs e);
        private delegate void OnClientDisconnectRaise(DisconnectedEventArgs e);
        private delegate void OnChatRaise(ChatEventArgs e);
        private delegate void OnInstantMessageRaise(InstantMessageEventArgs e);
        private delegate void OnAlertMessageRaise(AlertMessageEventArgs e);
        private delegate void OnMoneyBalanceRaise(BalanceEventArgs e);
        private delegate void OnTeleportStatusRaise(TeleportEventArgs e);

        public event EventHandler<OverrideEventArgs> ClientLoggingIn;
        public event EventHandler<LoginProgressEventArgs> ClientLoginStatus;
        public event EventHandler<OverrideEventArgs> ClientLoggingOut;
        public event EventHandler ClientLoggedOut;
        public event EventHandler<DisconnectedEventArgs> ClientDisconnected;
        public event EventHandler<ChatEventArgs> ChatReceived;
        public event EventHandler<ChatSentEventArgs> ChatSent;
        public event EventHandler<InstantMessageEventArgs> InstantMessageReceived;
        public event EventHandler<InstantMessageSentEventArgs> InstantMessageSent;
        public event EventHandler<TeleportEventArgs> TeleportStatusChanged;
        public event EventHandler<AlertMessageEventArgs> AlertMessageReceived;
        public event EventHandler<BalanceEventArgs> MoneyBalanceUpdated;

        protected virtual void OnClientLoggingIn(OverrideEventArgs e)
        {
            ClientLoggingIn?.Invoke(this, e);
        }

        protected virtual void OnClientLoginStatus(LoginProgressEventArgs e)
        {
            ClientLoginStatus?.Invoke(this, e);
        }

        protected virtual void OnClientLoggingOut(OverrideEventArgs e)
        {
            ClientLoggingOut?.Invoke(this, e);
        }

        protected virtual void OnClientLoggedOut(EventArgs e)
        {
            ClientLoggedOut?.Invoke(this, e);
        }

        protected virtual void OnClientDisconnected(DisconnectedEventArgs e)
        {
            ClientDisconnected?.Invoke(this, e);
        }

        protected virtual void OnChatReceived(ChatEventArgs e)
        {
            ChatReceived?.Invoke(this, e);
        }

        protected virtual void OnChatSent(ChatSentEventArgs e)
        {
            ChatSent?.Invoke(this, e);
        }

        protected virtual void OnInstantMessageReceived(InstantMessageEventArgs e)
        {
            InstantMessageReceived?.Invoke(this, e);
        }

        protected virtual void OnInstantMessageSent(InstantMessageSentEventArgs e)
        {
            InstantMessageSent?.Invoke(this, e);
        }

        protected virtual void OnTeleportStatusChanged(TeleportEventArgs e)
        {
            TeleportStatusChanged?.Invoke(this, e);
        }

        protected virtual void OnAlertMessageReceived(AlertMessageEventArgs e)
        {
            AlertMessageReceived?.Invoke(this, e);
        }

        protected virtual void OnMoneyBalanceUpdated(BalanceEventArgs e)
        {
            MoneyBalanceUpdated?.Invoke(this, e);
        }
    }
}
