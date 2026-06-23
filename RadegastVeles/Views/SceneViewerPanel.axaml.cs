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
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        var vm = DataContext as SceneViewerViewModel;

        // Escape resets the camera to behind the avatar.
        if (e.Key == Key.Escape)
        {
            vm?.ResetCameraCommand.Execute(null);
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
