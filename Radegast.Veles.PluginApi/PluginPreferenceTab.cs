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
using Avalonia.Controls;

namespace Radegast.Veles.PluginApi;

/// <summary>Describes a preference tab contributed by a plugin.</summary>
public sealed class PluginPreferenceTab
{
    /// <summary>Unique identifier for this preference tab.</summary>
    public string Id { get; }

    /// <summary>Tab header text.</summary>
    public string Header { get; }

    /// <summary>Factory that creates the tab content control on demand.</summary>
    public Func<Control> ContentFactory { get; }

    /// <summary>Optional callback invoked when the user clicks Apply or OK in Preferences.</summary>
    public Action? OnApply { get; set; }

    public PluginPreferenceTab(string id, string header, Func<Control> contentFactory)
    {
        Id = id;
        Header = header;
        ContentFactory = contentFactory;
    }
}
