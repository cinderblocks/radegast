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

using System.Runtime.InteropServices;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;

namespace Radegast.Veles.Core;

public static class VelesUpdateManager
{
    // update.radegast.life is CNAME'd to the gh-pages site published by
    // .github/workflows/publish-veles-appcast.yml (was https://cinderblocks.github.io/Radegast/veles/appcast/).
    private const string AppcastBaseUrl = "https://update.radegast.life/veles/appcast/";

    // Dedicated Ed25519 public key for Veles appcast signature verification (separate from Legacy's key).
    private const string PublicKey = "VVHLysN7aQZgDQ0lt44ojBUX+quPOP/NOeQaJGaCZ6Q=";

    private static SparkleUpdater? _updater;

    /// <summary>
    /// Starts the background update-check loop. No-ops on Linux: NetSparkle has no installer
    /// type for Flatpak bundles and can't self-replace inside the sandbox, so Linux users rely
    /// on `flatpak update` (once published to Flathub) or manual downloads instead.
    /// </summary>
    public static void Start()
    {
        var appcastUrl = GetAppcastUrl();
        if (appcastUrl == null)
        {
            return;
        }

        _updater = new SparkleUpdater(appcastUrl, new Ed25519Checker(SecurityMode.Strict, PublicKey))
        {
            UIFactory = new NetSparkleUpdater.UI.Avalonia.UIFactory(),
            RelaunchAfterUpdate = true,
            UseNotificationToast = true
        };
        _updater.StartLoop(true);
    }

    /// <summary>Triggers an explicit user-initiated update check, e.g. from a "Check for Updates" menu item.</summary>
    public static void CheckForUpdatesAtUserRequest()
    {
        _updater?.CheckForUpdatesAtUserRequest();
    }

    private static string? GetAppcastUrl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => AppcastBaseUrl + "windows-x86.json",
                Architecture.X64 => AppcastBaseUrl + "windows-x64.json",
                Architecture.Arm64 => AppcastBaseUrl + "windows-arm64.json",
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return AppcastBaseUrl + "macos.json";
        }

        return null;
    }
}
