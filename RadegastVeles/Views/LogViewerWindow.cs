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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Radegast.Veles.Core;

namespace Radegast.Veles.Views;

public class LogViewerWindow : Window
{
    private readonly ObservableCollection<LogDisplayEntry> _logEntries = [];
    private readonly ObservableCollection<LogDisplayEntry> _filteredEntries = [];
    private readonly ListBox _listBox;
    private readonly CheckBox _autoScrollCheck;
    private readonly ComboBox _levelCombo;
    private readonly TextBox _filterText;
    private bool _autoScroll = true;

    private static readonly IBrush BrushTrace = new SolidColorBrush(Color.FromRgb(150, 150, 150));
    private static readonly IBrush BrushDebug = new SolidColorBrush(Color.FromRgb(120, 120, 120));
    private static readonly IBrush BrushInfo = new SolidColorBrush(Color.FromRgb(80, 200, 80));
    private static readonly IBrush BrushWarning = new SolidColorBrush(Color.FromRgb(220, 160, 0));
    private static readonly IBrush BrushError = Brushes.Red;
    private static readonly IBrush BrushCritical = new SolidColorBrush(Color.FromRgb(180, 0, 0));
    private static readonly IBrush BrushCategory = new SolidColorBrush(Color.FromRgb(100, 149, 237));

    private const int MaxEntries = 2000;

    public LogViewerWindow()
    {
        Title = "Radegast Veles - Log Viewer";
        Width = 950;
        Height = 550;

        _levelCombo = new ComboBox
        {
            ItemsSource = new[] { "All", "Trace", "Debug", "Information", "Warning", "Error", "Critical" },
            SelectedIndex = 0,
            Margin = new Thickness(4),
            Width = 130
        };
        AutomationProperties.SetName(_levelCombo, "Log level filter");
        _levelCombo.SelectionChanged += (_, _) => ApplyFilter();

        _filterText = new TextBox
        {
            Watermark = "Filter by category or text...",
            Margin = new Thickness(4),
            Width = 250
        };
        AutomationProperties.SetName(_filterText, "Text filter");
        _filterText.TextChanged += (_, _) => ApplyFilter();

        _autoScrollCheck = new CheckBox
        {
            Content = "Auto-scroll",
            IsChecked = true,
            Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        _autoScrollCheck.IsCheckedChanged += (_, _) => _autoScroll = _autoScrollCheck.IsChecked == true;

        var clearButton = new Button
        {
            Content = "Clear",
            Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        clearButton.Click += (_, _) =>
        {
            _logEntries.Clear();
            _filteredEntries.Clear();
        };

        var openLogButton = new Button
        {
            Content = "Open Log File",
            Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        openLogButton.Click += (_, _) =>
        {
            var path = VelesLogProvider.LogFilePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        };

        var levelLabel = new TextBlock
        {
            Text = "Level:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { levelLabel, _levelCombo, _filterText, _autoScrollCheck, clearButton, openLogButton }
        };

        _listBox = new ListBox
        {
            ItemsSource = _filteredEntries,
            Margin = new Thickness(4, 0, 4, 4),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New, monospace"),
            FontSize = 12,
            SelectionMode = SelectionMode.Multiple
        };
        AutomationProperties.SetName(_listBox, "Log entries");

        var copyMenuItem = new MenuItem { Header = "_Copy Selected", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copyMenuItem.Click += async (_, _) => await CopySelectedToClipboard();

        var copyAllMenuItem = new MenuItem { Header = "Copy _All Visible" };
        copyAllMenuItem.Click += async (_, _) => await CopyAllToClipboard();

        _listBox.ContextMenu = new ContextMenu
        {
            Items = { copyMenuItem, copyAllMenuItem }
        };

        _listBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
            {
                await CopySelectedToClipboard();
                e.Handled = true;
            }
        };

        _listBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<LogDisplayEntry>((entry, _) =>
        {
            if (entry is null) return new TextBlock();

            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = $"{entry.TimeStamp:HH:mm:ss.fff} [",
                Foreground = Brushes.White,
                Opacity = 0.7
            });

            panel.Children.Add(new TextBlock
            {
                Text = entry.Category,
                Foreground = BrushCategory
            });

            panel.Children.Add(new TextBlock
            {
                Text = "] [",
                Foreground = Brushes.White,
                Opacity = 0.7
            });

            panel.Children.Add(new TextBlock
            {
                Text = entry.Level.ToString(),
                Foreground = GetLevelBrush(entry.Level)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "]: ",
                Foreground = Brushes.White,
                Opacity = 0.7
            });

            panel.Children.Add(new TextBlock
            {
                Text = entry.Message,
                Foreground = GetLevelBrush(entry.Level),
                TextWrapping = TextWrapping.NoWrap
            });

            return panel;
        });

        var dock = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(toolbar);
        dock.Children.Add(_listBox);

        Content = dock;

        VelesLogProvider.LogReceived += OnLogReceived;
    }

    private LogLevel GetMinLevel()
    {
        return _levelCombo.SelectedIndex switch
        {
            1 => LogLevel.Trace,
            2 => LogLevel.Debug,
            3 => LogLevel.Information,
            4 => LogLevel.Warning,
            5 => LogLevel.Error,
            6 => LogLevel.Critical,
            _ => LogLevel.Trace
        };
    }

    private bool MatchesFilter(LogDisplayEntry entry)
    {
        if (entry.Level < GetMinLevel())
            return false;

        var text = _filterText.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return true;

        return entry.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
            || entry.Message.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter()
    {
        _filteredEntries.Clear();
        foreach (var entry in _logEntries)
        {
            if (MatchesFilter(entry))
                _filteredEntries.Add(entry);
        }
    }

    private void OnLogReceived(object? sender, VelesLogEventArgs e)
    {
        var entry = new LogDisplayEntry(
            e.Entry.TimeStamp,
            e.Entry.Level,
            e.Entry.Category,
            e.Entry.Message,
            e.Entry.Exception?.Message);

        Dispatcher.UIThread.Post(() =>
        {
            while (_logEntries.Count >= MaxEntries)
            {
                _logEntries.RemoveAt(0);
                if (_filteredEntries.Count > 0 && _filteredEntries[0] == _logEntries[0])
                    _filteredEntries.RemoveAt(0);
            }

            _logEntries.Add(entry);

            if (MatchesFilter(entry))
            {
                _filteredEntries.Add(entry);
            }

            if (_autoScroll && _filteredEntries.Count > 0)
            {
                _listBox.ScrollIntoView(_filteredEntries[^1]);
            }
        });
    }

    private static IBrush GetLevelBrush(LogLevel level) => level switch
    {
        LogLevel.Trace => BrushTrace,
        LogLevel.Debug => BrushDebug,
        LogLevel.Information => BrushInfo,
        LogLevel.Warning => BrushWarning,
        LogLevel.Error => BrushError,
        LogLevel.Critical => BrushCritical,
        _ => Brushes.White
    };

    private async System.Threading.Tasks.Task CopySelectedToClipboard()
    {
        var selected = _listBox.SelectedItems;
        if (selected == null || selected.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var item in selected.OfType<LogDisplayEntry>())
        {
            sb.AppendLine(FormatEntry(item));
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(sb.ToString().TrimEnd());
        }
    }

    private async System.Threading.Tasks.Task CopyAllToClipboard()
    {
        if (_filteredEntries.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var entry in _filteredEntries)
        {
            sb.AppendLine(FormatEntry(entry));
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(sb.ToString().TrimEnd());
        }
    }

    private static string FormatEntry(LogDisplayEntry entry)
    {
        var line = $"{entry.TimeStamp:HH:mm:ss.fff} [{entry.Category}] [{entry.Level}]: {entry.Message}";
        if (!string.IsNullOrEmpty(entry.ExceptionMessage))
            line += $" | Exception: {entry.ExceptionMessage}";
        return line;
    }

    protected override void OnClosed(EventArgs e)
    {
        VelesLogProvider.LogReceived -= OnLogReceived;
        base.OnClosed(e);
    }
}

public record LogDisplayEntry(
    DateTime TimeStamp,
    LogLevel Level,
    string Category,
    string Message,
    string? ExceptionMessage);
