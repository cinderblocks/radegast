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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenMetaverse;

namespace Radegast.Veles.Views;

/// <summary>
/// Dialog for changing or resetting the agent's display name.
/// Mirrors the SL viewer's display-name change flow:
///   1. Fetch the current display name via GetDisplayNames.
///   2. POST SetDisplayName with (oldName, newName).
///   3. Listen for SetDisplayNameReply to report success or failure.
/// Pass an empty newName to reset to the legacy username default.
/// </summary>
public sealed class SetDisplayNameWindow : Window
{
    private readonly GridClient _client;
    private readonly TextBox _nameBox;
    private readonly TextBox _confirmBox;
    private readonly TextBlock _statusLabel;
    private readonly Button _setButton;
    private readonly Button _resetButton;

    public SetDisplayNameWindow(GridClient client)
    {
        _client = client;
        Title = "Change Display Name";
        Width = 400;
        Height = 190;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameBox    = new TextBox { PlaceholderText = "New display name..." };
        _confirmBox = new TextBox { PlaceholderText = "Confirm new display name..." };
        _statusLabel = new TextBlock
        {
            Text = string.Empty,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            MinHeight = 16,
            FontSize = 12
        };

        _setButton   = new Button { Content = "Set Display Name", IsDefault = true };
        _resetButton = new Button { Content = "Reset to Default", Margin = new Thickness(6, 0, 0, 0) };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };

        _setButton.Click   += async (_, _) => await OnSetClickAsync();
        _resetButton.Click += async (_, _) => await OnResetClickAsync();
        cancelButton.Click += (_, _) => Close();

        _client.Self.SetDisplayNameReply += Self_SetDisplayNameReply;
        Closed += (_, _) => _client.Self.SetDisplayNameReply -= Self_SetDisplayNameReply;

        Content = new StackPanel
        {
            Margin  = new Thickness(10),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "New display name:" },
                _nameBox,
                _confirmBox,
                _statusLabel,
                new StackPanel
                {
                    Orientation         = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin              = new Thickness(0, 4, 0, 0),
                    Children            = { _setButton, _resetButton, cancelButton }
                }
            }
        };

        Opened += (_, _) => _nameBox.Focus();
    }

    private void Self_SetDisplayNameReply(object? sender, SetDisplayNameReplyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _setButton.IsEnabled   = true;
            _resetButton.IsEnabled = true;

            if (e.Status == 200)
            {
                var newName = e.DisplayName.DisplayName;
                var msg = string.IsNullOrEmpty(newName)
                    ? "Display name reset to default."
                    : $"Display name changed to: {newName}";
                ShowStatus(msg, isError: false);
            }
            else
            {
                ShowStatus($"Failed: {e.Reason}", isError: true);
            }
        });
    }

    private async Task OnSetClickAsync()
    {
        var name    = _nameBox.Text?.Trim()    ?? string.Empty;
        var confirm = _confirmBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            ShowStatus("Please enter a display name.", isError: true);
            return;
        }

        if (name != confirm)
        {
            ShowStatus("Names do not match.", isError: true);
            return;
        }

        await RequestChangeAsync(name);
    }

    private async Task OnResetClickAsync()
    {
        await RequestChangeAsync(string.Empty);
    }

    private async Task RequestChangeAsync(string newName)
    {
        _setButton.IsEnabled   = false;
        _resetButton.IsEnabled = false;
        ShowStatus("Fetching current display name...", isError: false);

        string? currentDisplayName = null;
        await _client.Avatars.GetDisplayNames(
            new List<UUID> { _client.Self.AgentID },
            (success, names, _) =>
            {
                if (success && names is { Length: > 0 })
                    currentDisplayName = names[0].DisplayName ?? string.Empty;
            });

        if (currentDisplayName == null)
        {
            ShowStatus("Failed to retrieve current display name.", isError: true);
            _setButton.IsEnabled   = true;
            _resetButton.IsEnabled = true;
            return;
        }

        ShowStatus("Sending request...", isError: false);
        try
        {
            await _client.Self.SetDisplayNameAsync(currentDisplayName, newName);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", isError: true);
            _setButton.IsEnabled   = true;
            _resetButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        _statusLabel.Text       = text;
        _statusLabel.Foreground = isError
            ? new SolidColorBrush(Colors.OrangeRed)
            : null;
    }
}
