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

namespace Veles.Plugin.Discord;

/// <summary>
/// Code-only Avalonia settings pane shown in the Preferences window.
/// </summary>
internal sealed class DiscordSettingsControl : UserControl
{
    private readonly TextBox _botTokenBox;
    private readonly TextBox _channelIdBox;
    private readonly TextBox _webhookUrlBox;
    private readonly RadioButton _relayNearby;
    private readonly RadioButton _relayGroup;
    private readonly RadioButton _relayIm;
    private readonly TextBox _relayUuidBox;
    private readonly TextBox _relayLabelBox;

    public DiscordSettingsControl(IPluginContext ctx)
    {
        string Get(string key, string def) => ctx.GetSetting($"discord_{key}") ?? def;

        _botTokenBox  = new TextBox
        {
            Text = Get("bot_token", string.Empty),
            PlaceholderText = "Bot token from Discord Developer Portal",
            PasswordChar = '●',
        };
        _channelIdBox = new TextBox
        {
            Text = Get("channel_id", string.Empty),
            PlaceholderText = "Right-click channel → Copy Channel ID",
        };
        _webhookUrlBox = new TextBox
        {
            Text = Get("webhook_url", string.Empty),
            PlaceholderText = "Optional — Channel Settings → Integrations → Webhooks",
        };

        string relayType = Get("relay_type", "nearby");
        _relayNearby = new RadioButton { Content = "Nearby Chat",        GroupName = "DiscordRelay", IsChecked = relayType == "nearby" };
        _relayGroup  = new RadioButton { Content = "Group / Conference", GroupName = "DiscordRelay", IsChecked = relayType == "group"  };
        _relayIm     = new RadioButton { Content = "Direct IM",          GroupName = "DiscordRelay", IsChecked = relayType == "im"     };
        _relayUuidBox  = new TextBox { Text = Get("relay_uuid",  string.Empty), PlaceholderText = "UUID" };
        _relayLabelBox = new TextBox { Text = Get("relay_label", string.Empty), PlaceholderText = "Label (optional)" };

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
                    MakeSection("Bot Credentials",
                        MakeRow("Bot Token",   _botTokenBox),
                        MakeRow("Channel ID",  _channelIdBox)),

                    MakeSection("Webhook (optional)",
                        MakeRow("Webhook URL", _webhookUrlBox),
                        new TextBlock
                        {
                            Text = "When set, SL messages appear with the avatar's name as the Discord username.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Opacity = 0.7,
                            Margin = new Thickness(0, 2, 0, 0),
                        }),

                    MakeSection("Relay Target",
                        MakeRow(string.Empty, _relayNearby),
                        MakeRow(string.Empty, _relayGroup),
                        MakeRow(string.Empty, _relayIm),
                        MakeRow("UUID",       _relayUuidBox),
                        MakeRow("Label",      _relayLabelBox)),
                },
            },
        };
    }

    /// <summary>Persist the current control values via <paramref name="ctx"/>.</summary>
    public void Apply(IPluginContext ctx)
    {
        void Save(string key, string value) => ctx.SetSetting($"discord_{key}", value);

        Save("bot_token",   _botTokenBox.Text?.Trim()   ?? string.Empty);
        Save("channel_id",  _channelIdBox.Text?.Trim()  ?? string.Empty);
        Save("webhook_url", _webhookUrlBox.Text?.Trim() ?? string.Empty);

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
