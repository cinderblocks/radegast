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

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class SceneViewerPanel : UserControl
{
    public SceneViewerPanel()
    {
        InitializeComponent();
        Focusable = true;
        // Register tunneling (preview) handlers so movement keys are claimed before
        // toolbar buttons can consume arrow keys for Avalonia's focus traversal.
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,   OnTunnelKeyUp,   RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Wire the GL viewport into the VM once the visual tree is ready.
    /// </summary>
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SceneViewerViewModel vm)
        {
            vm.SetViewport(Viewport);
            // Steal keyboard focus to this panel whenever the user clicks the viewport,
            // so that WASD / arrow keys are dispatched to our OnKeyDown handler.
            Viewport.PointerPressed += (_, _) => Focus();

            // Focusing isn't bindable from XAML, so grab it here whenever the chat
            // input box is revealed (Enter in the viewport, or the toolbar button).
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneViewerViewModel.ChatInputVisible)
            && DataContext is SceneViewerViewModel { ChatInputVisible: true })
        {
            ChatInputBox.Focus();
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        // The chat box is inside this same panel, so the tunnel phase reaches it before
        // its own bubble-phase handler does — bail out here and let ChatInputBox_KeyDown
        // (and ordinary text entry) handle the keystroke instead of treating it as movement.
        if (ChatInputBox.IsFocused) return;

        var vm = DataContext as SceneViewerViewModel;

        // Enter opens (or refocuses) the chat input, mirroring SL's viewport chat bar.
        if (e.Key == Key.Enter)
        {
            if (vm != null) vm.ChatInputVisible = true;
            ChatInputBox.Focus();
            e.Handled = true;
            return;
        }

        // Escape: close mouselook first if active, otherwise reset the camera.
        if (e.Key == Key.Escape)
        {
            if (Viewport.MouselookActive)
                Viewport.ExitMouselook();
            else
                vm?.ResetCameraCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // M toggles mouselook (view-only in this slice — see Camera3D.MouselookMode doc).
        if (e.Key == Key.M)
        {
            if (Viewport.MouselookActive) Viewport.ExitMouselook();
            else Viewport.EnterMouselook();
            e.Handled = true;
            return;
        }

        // F toggles fly — one-shot on key-down.
        if (e.Key == Key.F)
        {
            vm?.ToggleFlyCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Shift held → fast/run movement.
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            vm?.SetFastMove(true);
            e.Handled = true;
            return;
        }

        var dir = KeyToDirection(e.Key);
        if (dir is null) return;
        vm?.BeginMove(dir.Value);
        e.Handled = true;
    }

    private void OnTunnelKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (ChatInputBox.IsFocused) return;

        var vm = DataContext as SceneViewerViewModel;

        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            vm?.SetFastMove(false);
            e.Handled = true;
            return;
        }

        var dir = KeyToDirection(e.Key);
        if (dir is null) return;
        vm?.EndMove(dir.Value);
        e.Handled = true;
    }

    private void ChatInputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SceneViewerViewModel vm) return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.ChatInputVisible = false;
                Focus(); // hand keyboard focus back to the panel so WASD resumes immediately
                e.Handled = true;
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                vm.SendChatFromViewportCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.Shift:
                vm.SendWhisperFromViewportCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.Control:
                vm.SendShoutFromViewportCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Control:
                vm.ChatHistoryPrevInViewport();
                e.Handled = true;
                break;
            case Key.Down when e.KeyModifiers == KeyModifiers.Control:
                vm.ChatHistoryNextInViewport();
                e.Handled = true;
                break;
        }
    }

    private static MoveDirection? KeyToDirection(Key key) => key switch
    {
        Key.W or Key.Up       => MoveDirection.Forward,
        Key.S or Key.Down     => MoveDirection.Backward,
        Key.A or Key.Left     => MoveDirection.TurnLeft,
        Key.D or Key.Right    => MoveDirection.TurnRight,
        Key.Q                 => MoveDirection.Left,
        Key.Z                 => MoveDirection.Right,
        Key.E or Key.PageUp   => MoveDirection.Up,
        Key.C or Key.PageDown => MoveDirection.Down,
        Key.Space             => MoveDirection.Jump,
        _ => null,
    };
}
