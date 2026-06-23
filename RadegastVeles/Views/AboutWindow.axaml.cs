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
    }

    private void PopulateVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"Version {version.ToString(3)}" : "Version unknown";
        if (this.FindControl<TextBlock>("AppVersion") is { } tb)
            tb.Text = versionText;
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
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Radegast Veles {version?.ToString(3) ?? "unknown"}");
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
