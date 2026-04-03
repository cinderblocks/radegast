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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.Controls;

/// <summary>
/// A reusable link-style button that displays an avatar name with a left-click profile
/// opener and a right-click context menu for all common avatar interactions.
/// </summary>
public partial class AvatarNameButton : UserControl
{
    // ── Styled Properties ────────────────────────────────────────────────────

    public static readonly StyledProperty<UUID> AgentIDProperty =
        AvaloniaProperty.Register<AvatarNameButton, UUID>(nameof(AgentID));

    /// <summary>The avatar name used for profile opening and menu actions.</summary>
    public static readonly StyledProperty<string> AgentNameProperty =
        AvaloniaProperty.Register<AvatarNameButton, string>(nameof(AgentName), string.Empty);

    /// <summary>
    /// Optional override for the displayed button label.
    /// If empty, <see cref="AgentName"/> is displayed instead.
    /// </summary>
    public static readonly StyledProperty<string> DisplayLabelProperty =
        AvaloniaProperty.Register<AvatarNameButton, string>(nameof(DisplayLabel), string.Empty);

    public static readonly StyledProperty<RadegastInstanceAvalonia?> InstanceProperty =
        AvaloniaProperty.Register<AvatarNameButton, RadegastInstanceAvalonia?>(nameof(Instance));

    public UUID AgentID
    {
        get => GetValue(AgentIDProperty);
        set => SetValue(AgentIDProperty, value);
    }

    public string AgentName
    {
        get => GetValue(AgentNameProperty);
        set => SetValue(AgentNameProperty, value);
    }

    public string DisplayLabel
    {
        get => GetValue(DisplayLabelProperty);
        set => SetValue(DisplayLabelProperty, value);
    }

    public RadegastInstanceAvalonia? Instance
    {
        get => GetValue(InstanceProperty);
        set => SetValue(InstanceProperty, value);
    }

    // ── Private Fields ───────────────────────────────────────────────────────

    private readonly Button _partButton;
    private readonly TextBlock _partText;

    // ── Constructor ──────────────────────────────────────────────────────────

    public AvatarNameButton()
    {
        _partText = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _partButton = new Button();
        _partButton.Classes.Add("avatarLink");
        _partButton.Content = _partText;
        _partButton.Click += OnButtonClick;
        Content = _partButton;
        InitializeComponent();
    }

    // ── Property Change Handling ─────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AgentNameProperty || change.Property == DisplayLabelProperty)
        {
            UpdateDisplayText();
        }

        if (change.Property == AgentIDProperty ||
            change.Property == AgentNameProperty ||
            change.Property == InstanceProperty)
        {
            RebuildContextMenu();
        }
    }

    private void UpdateDisplayText()
    {
        var label = string.IsNullOrEmpty(DisplayLabel) ? AgentName : DisplayLabel;
        _partText.Text = label;
        AutomationProperties.SetName(this, label);
    }

    private void RebuildContextMenu()
    {
        _partButton.ContextMenu = (Instance != null && AgentID != UUID.Zero)
            ? AvatarMenuBuilder.Build(Instance, AgentID, AgentName)
            : null;
    }

    // ── Event Handlers ───────────────────────────────────────────────────────

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (Instance == null || AgentID == UUID.Zero) return;
        Instance.ShowAgentProfile(AgentName, AgentID);
    }
}
