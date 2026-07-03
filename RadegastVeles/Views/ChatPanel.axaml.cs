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
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Radegast.Veles.Controls;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class ChatPanel : UserControl
{
    private NearbyViewModel? _vm;

    public ChatPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as NearbyViewModel;
        if (_vm == null) return;

        // Wire minimap navigation events
        var minimap = this.FindControl<MinimapControl>("Minimap");
        if (minimap != null)
        {
            minimap.WalkToRequested += (x, y) => _vm.WalkToPoint(x, y);
            minimap.TeleportRequested += (x, y) => _vm.TeleportToPoint(x, y);
            minimap.AboutLandRequested += (x, y) => _vm.Instance.ShowLandProfile(x, y);
        }

        // Wire movement button press/release
        WireMovementButton("BtnForward", v => _vm.SetMovingForward(v));
        WireMovementButton("BtnBackward", v => _vm.SetMovingBackward(v));
        WireMovementButton("BtnLeft", v => _vm.SetTurningLeft(v));
        WireMovementButton("BtnRight", v => _vm.SetTurningRight(v));
        WireMovementButton("BtnJump", v => _vm.SetJump(v));
        WireMovementButton("BtnCrouch", v => _vm.SetCrouch(v));

        // Wire chat input keyboard
        var chatInput = this.FindControl<TextBox>("ChatInputBox");
        if (chatInput != null)
        {
            chatInput.KeyDown += ChatInputBox_KeyDown;
            chatInput.Focus();
        }

        // Auto-scroll chat log
        var chatLog = this.FindControl<ListBox>("ChatLog");
        if (chatLog != null && _vm.ChatLines is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += (_, args) =>
            {
                // Don't auto-scroll to bottom when inserting history at the top
                if (_vm.IsLoadingHistory) return;
                if (chatLog.ItemCount > 0)
                    chatLog.ScrollIntoView(chatLog.ItemCount - 1);
            };
        }

        // Load initial history, scroll to bottom, then wire scroll-to-top for progressive loading
        if (chatLog != null && _vm != null)
        {
            _vm.LoadInitialHistory();

            // Scroll to the newest message (bottom) after history inserts at top
            Dispatcher.UIThread.Post(() =>
            {
                if (chatLog.ItemCount > 0)
                    chatLog.ScrollIntoView(chatLog.ItemCount - 1);
            }, DispatcherPriority.Background);

            Dispatcher.UIThread.Post(() =>
            {
                var sv = chatLog.FindDescendantOfType<ScrollViewer>();
                if (sv != null)
                    sv.ScrollChanged += (_, _) =>
                    {
                        if (sv.Offset.Y < 10 && !_vm.IsLoadingHistory && !_vm.HistoryExhausted)
                            _vm.LoadMoreHistory();
                    };
            }, DispatcherPriority.Loaded);
        }
    }

    private void WireMovementButton(string name, Action<bool> setter)
    {
        var btn = this.FindControl<Button>(name);
        if (btn == null) return;

        btn.AddHandler(PointerPressedEvent, (_, _) => setter(true), RoutingStrategies.Tunnel);
        btn.AddHandler(PointerReleasedEvent, (_, _) => setter(false), RoutingStrategies.Tunnel);
        btn.AddHandler(PointerCaptureLostEvent, (_, _) => setter(false));
    }

    private void ChatInputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                _vm.SendChatCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.Shift:
                _vm.SendWhisperCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.Control:
                _vm.SendShoutCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Control:
                _vm.ChatHistoryPrev();
                e.Handled = true;
                break;
            case Key.Down when e.KeyModifiers == KeyModifiers.Control:
                _vm.ChatHistoryNext();
                e.Handled = true;
                break;
        }
    }

    private void OnChatLineContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not WrapPanel panel || panel.DataContext is not ChatLine line) return;
        var copyItem = new MenuItem { Header = "Copy" };
        copyItem.Click += async (_, _) =>
        {
            var chatLog = this.FindControl<ListBox>("ChatLog");
            var lines = chatLog?.SelectedItems?.OfType<ChatLine>().Where(l => !l.IsDateSeparator).ToList();
            // If nothing is selected (or only this row) fall back to the right-clicked line
            if (lines == null || lines.Count == 0) lines = [line];
            var text = string.Join(Environment.NewLine, lines.Select(l => l.CopyText));
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) await clipboard.SetTextAsync(text);
        };
        var menu = new ContextMenu { Items = { copyItem } };
        menu.Open(panel);
        e.Handled = true;
    }

    private void OnChatNameClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Button { Tag: ChatLine chatLine } && chatLine.HasAgentLink)
        {
            _vm.ShowAgentProfile(chatLine.AgentID, chatLine.From);
        }
    }

    private void OnNearbyAvatarClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Button { Tag: NearbyAvatar avatar } && !avatar.IsSelf)
        {
            _vm.ShowAgentProfile(avatar.Id, avatar.Name);
        }
    }

    private void OnChatObjectClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Button { Tag: ChatLine chatLine } && chatLine.HasObjectLink)
        {
            ObjectMenuBuilder.Build(_vm.Instance, chatLine.AgentID, chatLine.From).Open((Button)sender);
        }
    }

    private void OnChatObjectContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not Button btn || btn.Tag is not ChatLine chatLine || !chatLine.HasObjectLink) return;
        ObjectMenuBuilder.Build(_vm.Instance, chatLine.AgentID, chatLine.From).Open(btn);
        e.Handled = true;
    }

    private void OnChatNameContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not Button btn || btn.Tag is not ChatLine chatLine || !chatLine.HasAgentLink) return;
        AvatarMenuBuilder.Build(_vm.Instance, chatLine.AgentID, chatLine.From).Open(btn);
        e.Handled = true;
    }

    private void OnNearbyContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not Button btn || btn.Tag is not NearbyAvatar avatar || avatar.IsSelf) return;
        AvatarMenuBuilder.Build(_vm.Instance, avatar.Id, avatar.Name).Open(btn);
        e.Handled = true;
    }

    private void OnHistoryAvatarClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: RadarHistoryEntry entry }) return;
        _vm.ShowAgentProfile(entry.Id, entry.Name);
    }

    private void OnHistoryContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not Button btn || btn.Tag is not RadarHistoryEntry entry) return;
        AvatarMenuBuilder.Build(_vm.Instance, entry.Id, entry.Name, isNearby: entry.IsNearby).Open(btn);
        e.Handled = true;
    }
}
