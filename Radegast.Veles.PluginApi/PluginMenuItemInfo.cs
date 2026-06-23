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

namespace Radegast.Veles.PluginApi;

/// <summary>Describes a menu item contributed by a plugin under the Plugins menu.</summary>
public sealed class PluginMenuItemInfo
{
    /// <summary>Unique identifier for this menu item.</summary>
    public string Id { get; }

    /// <summary>Display text shown in the menu.</summary>
    public string Header { get; }

    /// <summary>Callback invoked when the menu item is clicked.</summary>
    public Action OnClick { get; }

    /// <summary>
    /// The display name of the plugin that registered this item.
    /// Set by the host framework — plugins do not need to supply this.
    /// </summary>
    public string PluginName { get; set; } = string.Empty;

    /// <summary>
    /// The internal plugin ID of the plugin that registered this item.
    /// Set by the host framework — plugins do not need to supply this.
    /// </summary>
    public string PluginId { get; set; } = string.Empty;

    public PluginMenuItemInfo(string id, string header, Action onClick)
    {
        Id = id;
        Header = header;
        OnClick = onClick;
    }
}
