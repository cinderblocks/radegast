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
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Extends <see cref="ClientAwareViewModelBase"/> with unread-badge state
/// for tabs that display incoming messages (<c>IsActive</c>, <c>HasUnread</c>,
/// <c>UnreadTabLabel</c>, <c>ClearUnread</c>).
/// </summary>
public abstract partial class TabViewModelBase : ClientAwareViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadTabLabel))]
    private bool _hasUnread;

    /// <summary>True when this tab is the currently visible tab.
    /// When false, incoming messages set <see cref="HasUnread"/>.</summary>
    public bool IsActive { get; set; }

    /// <summary>Screenreader-friendly tab label that announces unread state.</summary>
    public abstract string UnreadTabLabel { get; }

    /// <summary>Clears the unread badge. Override to also clear per-session unread state.</summary>
    public virtual void ClearUnread() => HasUnread = false;

    protected TabViewModelBase(RadegastInstanceAvalonia instance) : base(instance) { }
}
