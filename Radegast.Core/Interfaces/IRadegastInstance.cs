/**
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

using LibreMetaverse;
using OpenMetaverse;
using Radegast.Commands;
using Radegast.Core.RLV;
using Radegast.Media;
using System;
using System.Collections.Generic;

namespace Radegast
{
    public interface IRadegastInstance
    {
        GridClient Client { get; }
        INetCom NetCom { get; }

        bool MonoRuntime { get; }
        string AppName { get; }
        Dictionary<UUID, Group> Groups { get; }
        StateManager State { get; }
        NameManager Names { get; }
        MediaManager MediaManager { get; }
        CommandsManager CommandsManager { get; }
        RlvManager RLV { get; }
        GridManager GridManger { get; }
        OutfitManager COF { get; }
        GestureManager GestureManager { get; }
        LslSyntax LslSyntax { get; }
        RadegastMovement Movement { get; }

        void Reconnect();
        void LogClientMessage(string sessionName, string message);
        void ShowNotificationInChat(string message, ChatBufferTextStyle style = ChatBufferTextStyle.ObjectChat, bool highlight = false);
        void AddNotification(INotification notification);
        void RemoveNotification(INotification notification);
        void ShowAgentProfile(string agentName, UUID agentID);
        void ShowGroupProfile(UUID groupId);
        void ShowLocation(string region, int x, int y, int z);

        void RegisterContextAction(Type omvType, string label, EventHandler handler);
        void DeregisterContextAction(Type omvType, string label);

        event EventHandler<ClientChangedEventArgs> ClientChanged;
    }
}
