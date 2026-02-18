/*
 * Radegast Metaverse Client
 * Copyright(c) 2025, Sjofn, LLC
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
    public interface INetCom : IDisposable
    {
        Grid Grid { get; }
        bool AgreeToTos { get; set; }
        bool IsLoggingIn { get; }
        bool IsLoggedIn { get; }
        LoginOptions LoginOptions { get; set; }

        void Login();
        void Logout();
        void ChatOut(string chat, ChatType type, int channel);
        void SendInstantMessage(string message, UUID target, UUID session);
        void SendIMStartTyping(UUID target, UUID session);
        void SendIMStopTyping(UUID target, UUID session);
        void ClearDuplicateCaches();

        event EventHandler<EventArgs> ClientConnected;
        event EventHandler<OverrideEventArgs> ClientLoggingIn;
        event EventHandler<LoginProgressEventArgs> ClientLoginStatus;
        event EventHandler<OverrideEventArgs> ClientLoggingOut;
        event EventHandler ClientLoggedOut;
        event EventHandler<DisconnectedEventArgs> ClientDisconnected;
        event EventHandler<ChatEventArgs> ChatReceived;
        event EventHandler<ChatSentEventArgs> ChatSent;
        event EventHandler<InstantMessageEventArgs> InstantMessageReceived;
        event EventHandler<InstantMessageSentEventArgs> InstantMessageSent;
        event EventHandler<TeleportEventArgs> TeleportStatusChanged;
        event EventHandler<AlertMessageEventArgs> AlertMessageReceived;
        event EventHandler<BalanceEventArgs> MoneyBalanceUpdated;

        void Instance_ClientChanged(object sender, ClientChangedEventArgs e);
    }
}
