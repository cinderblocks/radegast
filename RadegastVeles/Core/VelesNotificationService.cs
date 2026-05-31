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
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace Radegast.Veles.Core;

/// <summary>
/// Cross-platform in-app notification service backed by Avalonia's
/// <see cref="WindowNotificationManager"/>. Works correctly with OS
/// accessibility / screen-reader tooling on Windows, macOS and Linux
/// because notifications are rendered as real UI elements with proper
/// AutomationProperties.
/// </summary>
public static class VelesNotificationService
{
    private static WindowNotificationManager? _manager;

    /// <summary>
    /// Must be called once from the main window's <c>Loaded</c> event
    /// before any notifications are shown.
    /// </summary>
    public static void Initialize(TopLevel topLevel)
    {
        _manager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3
        };
    }

    /// <summary>
    /// Displays a toast notification. Safe to call from any thread —
    /// the call is marshalled to the UI thread automatically.
    /// </summary>
    public static void Show(
        string title,
        string message,
        NotificationType type = NotificationType.Information,
        TimeSpan? expiration = null)
    {
        var manager = _manager;
        if (manager == null) return;
        var notification = new Notification(title, message, type, expiration ?? TimeSpan.FromSeconds(5));
        if (Dispatcher.UIThread.CheckAccess())
            manager.Show(notification);
        else
            Dispatcher.UIThread.Post(() => manager.Show(notification));
    }
}
