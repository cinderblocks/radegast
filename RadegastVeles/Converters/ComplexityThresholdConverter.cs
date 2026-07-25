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
using Radegast.Veles.Rendering;

namespace Radegast.Veles.Converters;

/// <summary>
/// Formats the avatar-complexity threshold slider's value, showing "Unlimited" at its
/// maximum (<see cref="SceneAvatarStreamer.ComplexityThresholdMax"/>) instead of the
/// raw number, since that value means "always render everyone in full."
/// </summary>
public sealed class ComplexityThresholdConverter : IValueConverter
{
    public static readonly ComplexityThresholdConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is float f)
            return f >= SceneAvatarStreamer.ComplexityThresholdMax ? "Unlimited" : f.ToString("0", culture);
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
