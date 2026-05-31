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
using Radegast.Veles.Plugins;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Core;

public sealed class AgentSession : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public RadegastInstanceAvalonia Instance { get; }
    public MainViewModel ViewModel { get; }
    public PluginManager PluginManager => Instance.PluginManager;

    public string AgentName => Instance.Client.Self.Name;
    public bool IsConnected => Instance.Client.Network.Connected;

    public AgentSession(RadegastInstanceAvalonia instance)
    {
        Instance = instance;
        ViewModel = new MainViewModel(instance);

        // Initialise and start the plugin system
        instance.InitPluginManager();
        instance.PluginManager.LoadPluginsFromDirectory();
        instance.PluginManager.StartAll();
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        if (Instance.Client.Network.Connected)
        {
            Instance.NetCom.Logout();
        }
        Instance.CleanUp();
    }
}
