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

/// <summary>
/// Primary interface for Veles plugins. Classes implementing this interface
/// must also be decorated with <see cref="VelesPluginAttribute"/>.
/// </summary>
public interface IVelesPlugin : IDisposable
{
    /// <summary>
    /// Called when the plugin is started. Use the <paramref name="context"/>
    /// to register commands, menu items, event handlers, and UI extensions.
    /// </summary>
    void Attach(IPluginContext context);

    /// <summary>
    /// Called when the plugin is being stopped. Unregister all event handlers
    /// and clean up resources. The context will also automatically remove
    /// any registrations that were not explicitly cleaned up.
    /// </summary>
    void Detach();
}
