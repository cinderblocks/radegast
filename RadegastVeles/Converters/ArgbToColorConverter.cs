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
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Radegast.Veles.Converters;

/// <summary>
/// Converts a packed ARGB <see cref="uint"/> value to an Avalonia <see cref="IBrush"/>.
/// Used to render prim hover-text in the server-supplied colour.
/// </summary>
public sealed class ArgbToColorConverter : IValueConverter
{
    public static readonly ArgbToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is uint argb)
        {
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >>  8) & 0xFF);
            byte b = (byte)( argb        & 0xFF);
            // Ensure text is at least somewhat visible even if the prim author
            // set alpha to 0 (treat 0-alpha as fully opaque white).
            if (a == 0) { a = 255; r = 255; g = 255; b = 255; }
            return new SolidColorBrush(new Color(a, r, g, b));
        }
        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
