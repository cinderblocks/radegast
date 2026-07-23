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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input.Platform;
using Radegast.Veles.Core;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace Radegast.Veles.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        PopulateVersion();
        PopulateGitHubContributors();
        PopulateSystemInfo();
        PopulateLibraries();
    }

    private void PopulateVersion()
    {
        if (this.FindControl<TextBlock>("AppVersion") is { } tb)
            tb.Text = $"Version {AppVersionInfo.CurrentVersionString.TrimStart('v')}";
    }

    private void PopulateGitHubContributors()
    {
        var panel = this.FindControl<StackPanel>("GitHubContributorsList");
        if (panel == null) return;

        var contributors = GetGitContributors();
        foreach (var name in contributors)
        {
            panel.Children.Add(new TextBlock { Text = name });
        }

        if (contributors.Count == 0)
            panel.Children.Add(new TextBlock { Text = "(Unable to read git history)", Opacity = 0.6 });
    }

    private static List<string> GetGitContributors()
    {
        try
        {
            // Walk up from assembly location to find .git directory
            var dir = new System.IO.DirectoryInfo(
                System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".");
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, ".git")))
                dir = dir.Parent;

            if (dir == null) return [];

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("git", "log --format=%aN")
                {
                    WorkingDirectory = dir.FullName,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(n => !string.IsNullOrWhiteSpace(n) && !n.Contains("Copilot"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // Package-id prefix -> friendly display/license info, used to collapse near-duplicate
    // PackageReferences (e.g. the six org.k2fsa.sherpa.onnx.runtime.* native packages, or the
    // four Avalonia.* packages) into one credited row each. Only the version numbers below are
    // read automatically from the csproj at build time (see AssemblyMetadata in
    // RadegastVeles.csproj / Radegast.Core.csproj) - a brand-new library family not matching an
    // existing prefix here needs one line added; existing ones update on every version bump
    // with no code change.
    private static readonly (string Prefix, string DisplayName, string License, string? Copyright)[] LibraryFamilies =
    [
        ("Avalonia", "Avalonia UI", "MIT License", "Copyright © 2013–2026 AvaloniaUI OÜ and contributors"),
        ("org.k2fsa.sherpa.onnx", "sherpa-onnx (offline speech recognition)", "Apache License 2.0", null),
        ("CoreJ2K", "CoreJ2K (JPEG2000 decoder)", "BSD 3-Clause License",
            "Copyright © 1999–2000 JJ2000 Partners, © 2007–2012 Jason S. Clary, © 2013–2018 Anders Gustafsson (Cureos AB), © 2024–2026 Sjofn LLC"),
        ("NetSparkleUpdater", "NetSparkleUpdater", "MIT License", null),
        ("BugSplatDotNetStandard", "BugSplat", "MIT License", "Copyright © BugSplat"),
        ("NVorbis", "NVorbis", "MIT License", "Copyright © 2020 Andrew Ward"),
        ("SharpCompress", "SharpCompress", "MIT License", null),
        ("Silk.NET", "Silk.NET", "MIT License", null),
        ("Newtonsoft.Json", "Json.NET", "MIT License", "Copyright © 2007 James Newton-King"),
        ("SkiaSharp", "SkiaSharp", "MIT License", null),
    ];

    // Specific package ids never credited in the Libraries tab, regardless of family matching.
    private static readonly HashSet<string> ExcludedPackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "CommunityToolkit.Mvvm",
    };

    private void PopulateLibraries()
    {
        var panel = this.FindControl<StackPanel>("LibrariesPanel");
        if (panel == null) return;

        var packages = new List<(string Id, string Version)>();
        CollectPackageMetadata(Assembly.GetExecutingAssembly(), packages);
        CollectPackageMetadata(typeof(Radegast.Grid).Assembly, packages);

        // Collapse into families, keeping the highest version seen per displayed row.
        var rows = new Dictionary<string, (string License, string? Copyright, string Version)>();
        foreach (var (id, version) in packages)
        {
            var family = LibraryFamilies.FirstOrDefault(f => id.StartsWith(f.Prefix, StringComparison.OrdinalIgnoreCase));
            var displayName = family.DisplayName ?? id;
            var license = family.License ?? "See project repository for license";

            if (!rows.TryGetValue(displayName, out var existing) ||
                (Version.TryParse(version, out var v) && Version.TryParse(existing.Version, out var ev) && v > ev))
            {
                rows[displayName] = (license, family.Copyright, version);
            }
        }

        foreach (var (name, info) in rows.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold });
            stack.Children.Add(new TextBlock { Text = $"Version {info.Version}  ·  {info.License}", Opacity = 0.8, FontSize = 12 });
            if (!string.IsNullOrEmpty(info.Copyright))
                stack.Children.Add(new TextBlock { Text = info.Copyright, Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(stack);
        }

        if (rows.Count == 0)
            panel.Children.Add(new TextBlock { Text = "(Unable to read package metadata)", Opacity = 0.6 });
    }

    private static void CollectPackageMetadata(Assembly assembly, List<(string Id, string Version)> into)
    {
        const string prefix = "PackageRef_";
        foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!attr.Key.StartsWith(prefix, StringComparison.Ordinal) || string.IsNullOrEmpty(attr.Value))
                continue;

            var id = attr.Key[prefix.Length..];
            // Framework/runtime infrastructure (Microsoft.* and System.*) isn't credited here -
            // it's part of .NET itself, not a third-party library the app depends on. A few
            // specific packages are excluded by name too, at the maintainer's request.
            if (id.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                ExcludedPackageIds.Contains(id))
                continue;

            into.Add((id, attr.Value));
        }
    }

    private List<(string Label, string Value)> _sysInfoRows = [];

    private void PopulateSystemInfo()
    {
        var panel = this.FindControl<StackPanel>("SysInfoPanel");
        if (panel == null) return;

        _sysInfoRows =
        [
            ("Operating System", RuntimeInformation.OSDescription),
            ("OS Architecture", RuntimeInformation.OSArchitecture.ToString()),
            ("Process Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
            (".NET Runtime", RuntimeInformation.FrameworkDescription),
            ("Logical Processors", Environment.ProcessorCount.ToString()),
            ("Total Memory", FormatBytes(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)),
        ];

        foreach (var (label, value) in _sysInfoRows)
        {
            var grid = new AvaloniaGrid
            {
                ColumnDefinitions = new ColumnDefinitions("180,*"),
                Margin = new Thickness(0, 3)
            };
            var labelTb = new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            var valueTb = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            AvaloniaGrid.SetColumn(labelTb, 0);
            AvaloniaGrid.SetColumn(valueTb, 1);
            grid.Children.Add(labelTb);
            grid.Children.Add(valueTb);
            panel.Children.Add(grid);
        }
    }

    private async void OnCopySystemInfoClick(object? sender, RoutedEventArgs e)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Radegast Veles {AppVersionInfo.CurrentVersionString}");
        sb.AppendLine();
        foreach (var (label, value) in _sysInfoRows)
            sb.AppendLine($"{label}: {value}");

        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(sb.ToString());
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            _ => $"{bytes} B"
        };
    }

    private void OnWebsiteClick(object? sender, RoutedEventArgs e) =>
        OpenUrl("https://radegast.life/");

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }
}
