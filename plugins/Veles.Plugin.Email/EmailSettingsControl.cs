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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.Email;

/// <summary>
/// Code-only Avalonia settings pane shown in the plugin's Settings window.
/// </summary>
internal sealed class EmailSettingsControl : UserControl
{
    // SMTP
    private readonly TextBox _smtpHostBox;
    private readonly TextBox _smtpPortBox;
    private readonly CheckBox _smtpSslCheck;
    private readonly TextBox _smtpUserBox;
    private readonly TextBox _smtpPassBox;

    // Addressing
    private readonly TextBox _fromBox;
    private readonly TextBox _toBox;
    private readonly TextBox _subjectBox;

    // Schedule
    private readonly TextBox _intervalBox;
    private readonly TextBox _maxMsgBox;

    // What to include
    private readonly CheckBox _includeNearbyCheck;
    private readonly CheckBox _includeImCheck;
    private readonly CheckBox _includeGroupCheck;

    public EmailSettingsControl(IPluginContext ctx)
    {
        string Get(string key, string def) => ctx.GetSetting($"email_{key}") ?? def;

        // SMTP
        _smtpHostBox  = new TextBox { Text = Get("smtp_host", string.Empty), PlaceholderText = "smtp.example.com" };
        _smtpPortBox  = new TextBox { Text = Get("smtp_port", "587"),        PlaceholderText = "587" };
        _smtpSslCheck = new CheckBox { Content = "Use TLS/SSL", IsChecked = Get("smtp_ssl", "true") == "true" };
        _smtpUserBox  = new TextBox { Text = Get("smtp_user", string.Empty), PlaceholderText = "user@example.com" };
        _smtpPassBox  = new TextBox { Text = Get("smtp_pass", string.Empty), PlaceholderText = "password", PasswordChar = '●' };

        // Addressing
        _fromBox    = new TextBox { Text = Get("from_address", string.Empty), PlaceholderText = "radegast@example.com" };
        _toBox      = new TextBox { Text = Get("to_address",   string.Empty), PlaceholderText = "you@example.com" };
        _subjectBox = new TextBox { Text = Get("subject", "SL Chat Digest — {date}"), PlaceholderText = "SL Chat Digest — {date}" };

        // Schedule
        _intervalBox = new TextBox { Text = Get("interval_mins", "60"),  PlaceholderText = "60" };
        _maxMsgBox   = new TextBox { Text = Get("max_messages",  "200"), PlaceholderText = "200" };

        // Include
        _includeNearbyCheck = new CheckBox { Content = "Nearby chat",           IsChecked = Get("include_nearby", "true") == "true" };
        _includeImCheck     = new CheckBox { Content = "Instant messages",       IsChecked = Get("include_im",     "true") == "true" };
        _includeGroupCheck  = new CheckBox { Content = "Group / conference chat",IsChecked = Get("include_group",  "true") == "true" };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin  = new Thickness(12),
                Spacing = 12,
                Children =
                {
                    MakeSection("SMTP Server",
                        MakeRow("Host",     _smtpHostBox),
                        MakeRow("Port",     _smtpPortBox),
                        MakeRow(string.Empty, _smtpSslCheck),
                        MakeRow("Username", _smtpUserBox),
                        MakeRow("Password", _smtpPassBox)),

                    MakeSection("Addressing",
                        MakeRow("From",    _fromBox),
                        MakeRow("To",      _toBox),
                        MakeRow("Subject", _subjectBox),
                        new TextBlock
                        {
                            Text = "Use {date} in the subject to insert the send timestamp.",
                            Opacity = 0.7,
                            FontSize = 11,
                            Margin = new Thickness(0, 2, 0, 0),
                        }),

                    MakeSection("Schedule",
                        MakeRow("Send every (minutes)", _intervalBox),
                        MakeRow("Max messages per batch", _maxMsgBox),
                        new TextBlock
                        {
                            Text = "If the buffer reaches the maximum, the oldest messages are dropped. Set to 0 for unlimited.",
                            Opacity = 0.7,
                            FontSize = 11,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0),
                        }),

                    MakeSection("Include in digest",
                        MakeRow(string.Empty, _includeNearbyCheck),
                        MakeRow(string.Empty, _includeImCheck),
                        MakeRow(string.Empty, _includeGroupCheck)),
                },
            },
        };
    }

    /// <summary>Persist the current control values via <paramref name="ctx"/>.</summary>
    public void Apply(IPluginContext ctx)
    {
        void Save(string key, string value) => ctx.SetSetting($"email_{key}", value);

        Save("smtp_host", _smtpHostBox.Text?.Trim()  ?? string.Empty);
        Save("smtp_port", _smtpPortBox.Text?.Trim()  ?? "587");
        Save("smtp_ssl",  _smtpSslCheck.IsChecked == true ? "true" : "false");
        Save("smtp_user", _smtpUserBox.Text?.Trim()  ?? string.Empty);
        Save("smtp_pass", _smtpPassBox.Text?.Trim()  ?? string.Empty);

        Save("from_address",  _fromBox.Text?.Trim()    ?? string.Empty);
        Save("to_address",    _toBox.Text?.Trim()      ?? string.Empty);
        Save("subject",       _subjectBox.Text?.Trim() ?? "SL Chat Digest — {date}");

        Save("interval_mins", _intervalBox.Text?.Trim() ?? "60");
        Save("max_messages",  _maxMsgBox.Text?.Trim()   ?? "200");

        Save("include_nearby", _includeNearbyCheck.IsChecked == true ? "true" : "false");
        Save("include_im",     _includeImCheck.IsChecked     == true ? "true" : "false");
        Save("include_group",  _includeGroupCheck.IsChecked  == true ? "true" : "false");
    }

    // ── Layout helpers ────────────────────────────────────────────────────

    private static Border MakeSection(string title, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 6 };
        if (!string.IsNullOrEmpty(title))
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
            });
        foreach (var row in rows)
            stack.Children.Add(row);

        return new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10),
            Child           = stack,
        };
    }

    private static Control MakeRow(string label, Control control)
    {
        if (string.IsNullOrEmpty(label))
            return control;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*") };
        var lbl  = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(lbl,     0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }
}
