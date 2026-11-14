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
using System.Diagnostics;
using LibreMetaverse;

namespace Radegast.Veles.Core;

/// <summary>
/// Opens web addresses that came from the grid (chat, profiles, scripted objects, web services)
/// in the system browser. Handing such text to the shell unchecked would let it launch a local
/// file or any registered URL scheme, so only absolute http and https addresses are accepted.
/// </summary>
public static class WebLink
{
    /// <summary>Returns the address in canonical form if it is an absolute http or https URL.</summary>
    public static bool TryNormalize(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host)) return false;
        normalized = uri.AbsoluteUri;
        return true;
    }

    /// <summary>Opens the address in the system browser. Returns false if it was refused or failed.</summary>
    public static bool TryOpen(string? url)
    {
        if (!TryNormalize(url, out var normalized))
        {
            if (!string.IsNullOrWhiteSpace(url))
                Logger.Warn($"Refused to open a link that is not an http or https address: {Preview(url)}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to open {Preview(normalized)} in the browser.", ex);
            return false;
        }
    }

    private static string Preview(string text)
    {
        text = text.Trim();
        return text.Length > 120 ? text[..120] + "…" : text;
    }
}
