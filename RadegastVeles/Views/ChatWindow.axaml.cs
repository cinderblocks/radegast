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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class ChatWindow : Window
{
    private NearbyViewModel? _vm;

    public ChatWindow()
    {
        InitializeComponent();
    }

    public ChatWindow(NearbyViewModel viewModel) : this()
    {
        _vm = viewModel;
        DataContext = _vm;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm ??= DataContext as NearbyViewModel;
        if (_vm == null) return;

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
            ncc.CollectionChanged += (_, _) =>
            {
                if (chatLog.ItemCount > 0)
                {
                    chatLog.ScrollIntoView(chatLog.ItemCount - 1);
                }
            };
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

    protected override void OnClosed(EventArgs e)
    {
        _vm?.Dispose();
        base.OnClosed(e);
    }
}
