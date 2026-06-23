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

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Static <see cref="IValueConverter"/> instances used by <c>MarketplacePanel.axaml</c>
/// to show/hide the four filter list boxes based on <c>SelectedFilterIndex</c>.
/// </summary>
public static class MarketplaceFilterConverters
{
    public static readonly IValueConverter IsAll          = new IndexMatchConverter(0);
    public static readonly IValueConverter IsActive       = new IndexMatchConverter(1);
    public static readonly IValueConverter IsInactive     = new IndexMatchConverter(2);
    public static readonly IValueConverter IsUnassociated = new IndexMatchConverter(3);

    private sealed class IndexMatchConverter(int expected) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is int idx && idx == expected;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
