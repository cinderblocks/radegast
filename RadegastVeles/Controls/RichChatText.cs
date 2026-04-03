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
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Controls;

/// <summary>
/// Renders a chat message string as mixed plain text and clickable hyperlinks.
/// URLs and SLURLs/SLAPP URIs are automatically detected and made clickable.
/// The control locates the <see cref="RadegastInstanceAvalonia"/> by walking the
/// logical tree to the nearest ancestor whose DataContext implements
/// <see cref="IChatContext"/> — no explicit Instance binding is required.
/// </summary>
public class RichChatText : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RichChatText, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private readonly WrapPanel _panel;

    public RichChatText()
    {
        _panel = new WrapPanel { Orientation = Orientation.Horizontal };
        Content = _panel;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            RebuildContent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Re-parse now that we have access to the logical tree (and thus the instance).
        RebuildContent();
    }

    private void RebuildContent()
    {
        _panel.Children.Clear();

        var raw = Text;
        if (string.IsNullOrEmpty(raw))
            return;

        var instance = FindInstance();
        var segments = ChatLinkHelper.ParseSegments(raw, instance);

        foreach (var seg in segments)
        {
            if (seg.IsLink)
                _panel.Children.Add(BuildLinkButton(seg, instance));
            else
                _panel.Children.Add(BuildTextBlock(seg.DisplayText));
        }
    }

    private static TextBlock BuildTextBlock(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Button BuildLinkButton(ChatTextSegment seg, RadegastInstanceAvalonia? instance)
    {
        var url = seg.Url!;

        var label = new TextBlock
        {
            Text = seg.DisplayText,
            TextDecorations = TextDecorations.Underline,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(85, 153, 221))
        };

        var btn = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            MinWidth = 0,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Store the URL as the Tag so we can re-resolve the instance at click time.
        btn.Tag = url;
        btn.Click += (sender, _) =>
        {
            var clickedUrl = (sender as Button)?.Tag as string ?? url;
            var liveInstance = instance ?? ((sender as Button)?.FindInstance());
            ChatLinkHelper.ExecuteLink(clickedUrl, liveInstance);
        };

        // Tooltip showing the raw URL on hover
        ToolTip.SetTip(btn, url);

        return btn;
    }

    /// <summary>
    /// Walks the logical tree to find the nearest ancestor DataContext that
    /// implements <see cref="IChatContext"/>.
    /// </summary>
    private RadegastInstanceAvalonia? FindInstance()
    {
        ILogical? current = Parent;
        while (current != null)
        {
            if (current is StyledElement { DataContext: IChatContext ctx })
                return ctx.Instance;
            current = current.LogicalParent;
        }
        return null;
    }
}

/// <summary>Extension on Button to allow click-time instance resolution.</summary>
file static class ButtonExtensions
{
    internal static RadegastInstanceAvalonia? FindInstance(this Button btn)
    {
        ILogical? current = btn.Parent;
        while (current != null)
        {
            if (current is StyledElement { DataContext: IChatContext ctx })
                return ctx.Instance;
            current = current.LogicalParent;
        }
        return null;
    }
}
