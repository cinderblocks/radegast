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
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Radegast.Veles.Core;

/// <summary>
/// Composites a numeric unread-count badge onto the application window icon.
/// Works on every platform Avalonia supports (Windows, macOS, Linux) because
/// it operates purely on the window's <see cref="WindowIcon"/> — no OS-specific
/// APIs are required.
/// </summary>
public static class BadgeService
{
    private static Bitmap?     _baseIconBitmap;
    private static WindowIcon? _baseWindowIcon;
    private static Window?     _window;

    /// <summary>
    /// Call once from the main window's <c>Loaded</c> event.
    /// Stores the original icon so it can be restored when the count drops to zero.
    /// </summary>
    public static void Initialize(Window window)
    {
        _window = window;
        _baseWindowIcon = window.Icon;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://RadegastVeles/Assets/radegast.ico"));
            _baseIconBitmap = new Bitmap(stream);
        }
        catch { /* non-fatal — badge will render without a base image */ }
    }

    /// <summary>
    /// Updates the window icon badge. Pass <c>0</c> to restore the original icon.
    /// Must be called from the UI thread.
    /// </summary>
    public static void Update(int count)
    {
        _window?.Icon = count > 0 ? CreateBadgedIcon(count) : _baseWindowIcon;
    }

    private static WindowIcon CreateBadgedIcon(int count)
    {
        const int size = 32;
        var rtb = new RenderTargetBitmap(new PixelSize(size, size));

        using (var dc = rtb.CreateDrawingContext())
        {
            // Draw base app icon
            if (_baseIconBitmap != null)
            {
                dc.DrawImage(_baseIconBitmap, new Rect(0, 0, size, size));
            }
            else
            {
                // Fallback: plain colored square so the badge is always visible
                dc.DrawRectangle(new SolidColorBrush(Color.Parse("#336699")), null,
                    new RoundedRect(new Rect(0, 0, size, size), 4));
            }

            // Red pill badge in the top-right corner
            const double r = 8.0;
            var center = new Point(size - r, r);
            dc.DrawEllipse(Brushes.Red, null, center, r, r);

            // White count text centred inside the badge circle
            var label = count > 99 ? "99" : count.ToString();
            var ft = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
                label.Length == 1 ? 9.0 : 7.0,
                Brushes.White);

            dc.DrawText(ft, new Point(
                center.X - ft.Width  / 2,
                center.Y - ft.Height / 2));
        }

        return new WindowIcon(rtb);
    }
}
