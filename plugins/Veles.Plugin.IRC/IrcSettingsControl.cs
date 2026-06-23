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

namespace Veles.Plugin.IRC;

/// <summary>
/// Code-only Avalonia settings pane shown in the Preferences window.
/// </summary>
internal sealed class IrcSettingsControl : UserControl
{
    private readonly TextBox _serverBox;
    private readonly TextBox _portBox;
    private readonly CheckBox _tlsCheck;
    private readonly TextBox _nickBox;
    private readonly TextBox _channelBox;
    private readonly TextBox _saslLoginBox;
    private readonly TextBox _saslPasswordBox;
    private readonly RadioButton _relayNearby;
    private readonly RadioButton _relayGroup;
    private readonly RadioButton _relayIm;
    private readonly TextBox _relayUuidBox;
    private readonly TextBox _relayLabelBox;

    public IrcSettingsControl(IPluginContext ctx)
    {
        string Get(string key, string def) => ctx.GetSetting($"irc_{key}") ?? def;

        _serverBox       = new TextBox { Text = Get("server", "irc.libera.chat") };
        _portBox         = new TextBox { Text = Get("port", "6697") };
        _tlsCheck        = new CheckBox { Content = "Use TLS", IsChecked = Get("tls", "on") != "off" };
        _nickBox         = new TextBox { Text = Get("nick", ctx.Client.Self.Name.Replace(' ', '_')) };
        _channelBox      = new TextBox { Text = Get("channel", "#veles") };
        _saslLoginBox    = new TextBox { Text = ctx.GetSetting("irc_sasl_login") ?? string.Empty };
        _saslPasswordBox = new TextBox
        {
            Text = ctx.GetSetting("irc_sasl_password") ?? string.Empty,
            PasswordChar = '●',
        };

        string relayType = Get("relay_type", "nearby");
        _relayNearby = new RadioButton { Content = "Nearby Chat",        GroupName = "IrcRelay", IsChecked = relayType == "nearby" };
        _relayGroup  = new RadioButton { Content = "Group / Conference", GroupName = "IrcRelay", IsChecked = relayType == "group"  };
        _relayIm     = new RadioButton { Content = "Direct IM",          GroupName = "IrcRelay", IsChecked = relayType == "im"     };
        _relayUuidBox  = new TextBox { Text = Get("relay_uuid",  string.Empty), PlaceholderText = "UUID" };
        _relayLabelBox = new TextBox { Text = Get("relay_label", string.Empty), PlaceholderText = "Label (optional)" };

        // Update UUID / label row enabled state on relay type change
        void UpdateRelayUuidEnabled(bool enabled)
        {
            _relayUuidBox.IsEnabled  = enabled;
            _relayLabelBox.IsEnabled = enabled;
        }
        bool needsUuid = relayType is "group" or "im";
        UpdateRelayUuidEnabled(needsUuid);
        _relayNearby.IsCheckedChanged += (_, _) => UpdateRelayUuidEnabled(_relayNearby.IsChecked != true);
        _relayGroup.IsCheckedChanged  += (_, _) => UpdateRelayUuidEnabled(_relayGroup.IsChecked  == true);
        _relayIm.IsCheckedChanged     += (_, _) => UpdateRelayUuidEnabled(_relayIm.IsChecked     == true);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Spacing = 12,
                Children =
                {
                    MakeSection("Connection",
                        MakeRow("Server",          _serverBox),
                        MakeRow("Port",            _portBox),
                        MakeRow(string.Empty,      _tlsCheck)),

                    MakeSection("Identity",
                        MakeRow("Nick",            _nickBox),
                        MakeRow("Channel",         _channelBox)),

                    MakeSection("SASL (optional)",
                        MakeRow("Login",           _saslLoginBox),
                        MakeRow("Password",        _saslPasswordBox)),

                    MakeSection("Relay Target",
                        MakeRow(string.Empty,      _relayNearby),
                        MakeRow(string.Empty,      _relayGroup),
                        MakeRow(string.Empty,      _relayIm),
                        MakeRow("UUID",            _relayUuidBox),
                        MakeRow("Label",           _relayLabelBox)),
                },
            },
        };
    }

    /// <summary>Persist the current control values via <paramref name="ctx"/>.</summary>
    public void Apply(IPluginContext ctx)
    {
        void Save(string key, string value) => ctx.SetSetting($"irc_{key}", value);

        Save("server", _serverBox.Text?.Trim() ?? string.Empty);
        Save("port",   _portBox.Text?.Trim()   ?? "6697");
        Save("tls",    _tlsCheck.IsChecked == true ? "on" : "off");
        Save("nick",   _nickBox.Text?.Trim()    ?? string.Empty);
        Save("channel", _channelBox.Text?.Trim() ?? string.Empty);

        string login    = _saslLoginBox.Text?.Trim()    ?? string.Empty;
        string password = _saslPasswordBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(login))
        {
            ctx.SetSetting("irc_sasl_login",    login);
            ctx.SetSetting("irc_sasl_password", password);
        }
        else
        {
            ctx.SetSetting("irc_sasl_login",    string.Empty);
            ctx.SetSetting("irc_sasl_password", string.Empty);
        }

        if (_relayGroup.IsChecked == true)
        {
            Save("relay_type",  "group");
            Save("relay_uuid",  _relayUuidBox.Text?.Trim()  ?? string.Empty);
            Save("relay_label", _relayLabelBox.Text?.Trim() ?? string.Empty);
        }
        else if (_relayIm.IsChecked == true)
        {
            Save("relay_type",  "im");
            Save("relay_uuid",  _relayUuidBox.Text?.Trim()  ?? string.Empty);
            Save("relay_label", _relayLabelBox.Text?.Trim() ?? string.Empty);
        }
        else
        {
            Save("relay_type",  "nearby");
            Save("relay_uuid",  string.Empty);
            Save("relay_label", "Nearby Chat");
        }
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
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = stack,
        };
    }

    private static Control MakeRow(string label, Control control)
    {
        if (string.IsNullOrEmpty(label))
            return control;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
        };
        var lbl = new TextBlock
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
