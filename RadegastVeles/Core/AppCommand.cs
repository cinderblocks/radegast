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
using System.Collections.Generic;

namespace Radegast.Veles.Core;

internal enum AppCommandKind
{
    NewLogin,
    CheckForUpdates,
    Exit,
    ShowSession,
    LogoutSession,
}

/// <summary>
/// A request a second copy of the program passes to the one already running. The Windows jump
/// list and the Linux desktop actions can only start the program with arguments, so each of
/// their entries is one of these, written as command-line arguments.
/// </summary>
internal sealed record AppCommand(AppCommandKind Kind, Guid SessionId = default)
{
    private const string NewLoginVerb = "--new-login";
    private const string CheckForUpdatesVerb = "--check-updates";
    private const string ExitVerb = "--exit";
    private const string ShowVerb = "--show";
    private const string LogoutVerb = "--logout";

    /// <summary>Finds the first command verb in a command line. Other arguments are ignored.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out AppCommand command)
    {
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case NewLoginVerb: command = new AppCommand(AppCommandKind.NewLogin); return true;
                case CheckForUpdatesVerb: command = new AppCommand(AppCommandKind.CheckForUpdates); return true;
                case ExitVerb: command = new AppCommand(AppCommandKind.Exit); return true;
                case ShowVerb or LogoutVerb:
                    if (i + 1 < args.Count && Guid.TryParse(args[i + 1], out var id))
                    {
                        command = new AppCommand(
                            args[i] == ShowVerb ? AppCommandKind.ShowSession : AppCommandKind.LogoutSession, id);
                        return true;
                    }
                    break;
            }
        }

        command = null!;
        return false;
    }

    /// <summary>The command as command-line arguments, which is also its form on the wire.</summary>
    public string ToArguments() => Kind switch
    {
        AppCommandKind.NewLogin => NewLoginVerb,
        AppCommandKind.CheckForUpdates => CheckForUpdatesVerb,
        AppCommandKind.Exit => ExitVerb,
        AppCommandKind.ShowSession => $"{ShowVerb} {SessionId:D}",
        AppCommandKind.LogoutSession => $"{LogoutVerb} {SessionId:D}",
        _ => throw new InvalidOperationException($"Unknown command kind {Kind}."),
    };

    public static bool TryParseLine(string line, out AppCommand command) =>
        TryParse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), out command);
}
