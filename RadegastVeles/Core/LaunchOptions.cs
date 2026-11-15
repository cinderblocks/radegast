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

/// <summary>
/// Options given on the command line. The login options fill in the first login window; they
/// are not passed to a copy of the program that is already running.
/// </summary>
public sealed class LaunchOptions
{
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public bool PasswordFromStandardInput { get; private set; }
    public string? Grid { get; private set; }
    public string? LoginUri { get; private set; }
    public string? Location { get; private set; }
    public bool AutoLogin { get; private set; }
    public bool ListGrids { get; private set; }
    public bool NoSound { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }

    /// <summary>Problems found while parsing, each a complete sentence for the user.</summary>
    public IReadOnlyList<string> Errors => _errors;
    private readonly List<string> _errors = [];

    public bool HasLoginOptions =>
        Username != null || Password != null || Grid != null || LoginUri != null || Location != null || AutoLogin;

    public const string HelpText =
        """
        Usage: RadegastVeles [options]

        Login (applied to the first login window):
          -u, --username <name>     Account name; quote "First Last"
          -g, --grid <id>           Grid to log into; see --list-grids
              --login-uri <uri>     Log into a grid by its login URI instead of --grid
          -l, --location <where>    Start location: last, home, a saved favourite's name,
                                    or a region as Region or Region/x/y/z
          -a, --auto-login          Log in without waiting, using the saved password for
                                    the account or one given below
              --password-stdin      Read the password from standard input
          -p, --password <text>     Account password. Other programs on this computer can
                                    see command lines; prefer a saved password or
                                    --password-stdin

        Running copy (handed to the copy already running, if there is one):
              --new-login           Open a login window
              --check-updates       Check for updates
              --show <session>      Bring an account's window to the front
              --logout <session>    Log an account out
              --exit                Close the program

        Other:
              --list-grids          List the grid ids accepted by --grid
              --no-sound            Do not start the sound system
              --version             Print the version
          -h, --help                Print this help
        """;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var options = new LaunchOptions();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string name = arg;
            string? inlineValue = null;
            int equals = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && equals > 2)
            {
                name = arg[..equals];
                inlineValue = arg[(equals + 1)..];
            }

            string? Value()
            {
                if (inlineValue != null) return inlineValue;
                if (i + 1 < args.Count) return args[++i];
                options._errors.Add($"Option {name} needs a value.");
                return null;
            }

            switch (name)
            {
                case "-u" or "--username": options.Username = Value(); break;
                case "-p" or "--password": options.Password = Value(); break;
                case "--password-stdin": options.PasswordFromStandardInput = true; break;
                case "-g" or "--grid": options.Grid = Value(); break;
                case "--login-uri" or "--loginuri": options.LoginUri = Value(); break;
                case "-l" or "--location": options.Location = Value(); break;
                case "-a" or "--auto-login" or "--autologin": options.AutoLogin = true; break;
                case "--list-grids": options.ListGrids = true; break;
                case "--no-sound": options.NoSound = true; break;
                case "-h" or "--help" or "-?": options.ShowHelp = true; break;
                case "--version": options.ShowVersion = true; break;

                // Handled by AppCommand; the session verbs carry a value to step over.
                case "--new-login" or "--check-updates" or "--exit": break;
                case "--show" or "--logout":
                    if (inlineValue == null && i + 1 < args.Count) i++;
                    break;

                default:
                    options._errors.Add($"Unknown option {arg}.");
                    break;
            }
        }

        if (options.Grid != null && options.LoginUri != null)
            options._errors.Add("Use either --grid or --login-uri, not both.");
        if (options.Password != null && options.PasswordFromStandardInput)
            options._errors.Add("Use either --password or --password-stdin, not both.");

        return options;
    }

    /// <summary>Replaces the password with one read from elsewhere, such as standard input.</summary>
    public void SupplyPassword(string? password)
    {
        if (!string.IsNullOrEmpty(password)) Password = password;
    }

    /// <summary>
    /// Whether two account names refer to the same account: case, "First.Last" against
    /// "First Last", and a trailing "Resident" do not matter.
    /// </summary>
    public static bool SameAccountName(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string name)
    {
        var parts = name.Trim().Split(['.', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[1].Equals("Resident", StringComparison.OrdinalIgnoreCase))
            return parts[0];
        return string.Join(' ', parts);
    }
}
