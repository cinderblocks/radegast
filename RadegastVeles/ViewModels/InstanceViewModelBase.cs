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

using CommunityToolkit.Mvvm.ComponentModel;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Base class for all session-scoped ViewModels that hold a
/// <see cref="RadegastInstanceAvalonia"/> reference.
/// Provides the canonical <c>_instance</c>, <c>Client</c>, <c>NetCom</c>
/// and <c>Instance</c> accessors so derived classes never redeclare them.
/// </summary>
public abstract class InstanceViewModelBase : ObservableObject
{
    protected readonly RadegastInstanceAvalonia _instance;
    protected GridClient Client => _instance.Client;
    protected INetCom NetCom => _instance.NetCom;
    public RadegastInstanceAvalonia Instance => _instance;

    protected InstanceViewModelBase(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
    }
}
