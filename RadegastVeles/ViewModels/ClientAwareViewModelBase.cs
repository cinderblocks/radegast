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
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Extends <see cref="InstanceViewModelBase"/> for ViewModels that must
/// re-subscribe to <see cref="GridClient"/> events whenever the active client
/// is replaced (<c>RadegastInstanceAvalonia.ClientChanged</c>).
/// <para>
/// Derived classes implement <see cref="RegisterClientEvents"/> and
/// <see cref="UnregisterClientEvents"/>; the base wires the swap handler
/// automatically and tears it down in <see cref="Dispose"/>.
/// </para>
/// </summary>
public abstract class ClientAwareViewModelBase : InstanceViewModelBase, IDisposable
{
    protected ClientAwareViewModelBase(RadegastInstanceAvalonia instance) : base(instance)
    {
        _instance.ClientChanged += Instance_ClientChanged;
    }

    public virtual void Dispose()
    {
        _instance.ClientChanged -= Instance_ClientChanged;
        UnregisterClientEvents(_instance.Client);
    }

    /// <summary>Subscribe to all <paramref name="client"/> events needed by this ViewModel.</summary>
    protected abstract void RegisterClientEvents(GridClient client);

    /// <summary>Unsubscribe from all <paramref name="client"/> events subscribed by <see cref="RegisterClientEvents"/>.</summary>
    protected abstract void UnregisterClientEvents(GridClient client);

    /// <summary>
    /// Called after the client swap completes (old events removed, new events registered).
    /// Override to perform additional work on client change.
    /// </summary>
    protected virtual void OnClientChanged(GridClient oldClient, GridClient newClient) { }

    private void Instance_ClientChanged(object? sender, ClientChangedEventArgs e)
    {
        UnregisterClientEvents(e.OldClient);
        RegisterClientEvents(e.Client);
        OnClientChanged(e.OldClient, e.Client);
    }
}
