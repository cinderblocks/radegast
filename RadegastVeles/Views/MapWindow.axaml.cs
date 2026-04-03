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
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Radegast.Veles.Controls;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class MapWindow : Window
{
    private MapViewModel? _vm;
    private GridMapControl? _mapControl;

    public MapWindow()
    {
        InitializeComponent();
    }

    public MapWindow(MapViewModel viewModel) : this()
    {
        _vm = viewModel;
        DataContext = _vm;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm ??= DataContext as MapViewModel;
        if (_vm == null) return;

        _mapControl = this.FindControl<GridMapControl>("MapControl");

        // Wire map center changes from ViewModel to control
        _vm.MapCenterChanged += OnMapCenterChanged;

        // Wire map clicks from control back to ViewModel
        if (_mapControl != null)
        {
            _mapControl.MapClicked += OnMapClicked;
        }

        // Wire zoom slider to map control
        _vm.PropertyChanged += OnVmPropertyChanged;

        // Wire search input Enter key
        var searchBox = this.FindControl<TextBox>("TxtRegionSearch");
        if (searchBox != null)
        {
            searchBox.KeyDown += SearchBox_KeyDown;
        }

        // Refresh online friends when window opens
        _vm.RefreshOnlineFriends();
    }

    private void OnMapCenterChanged(object? sender, MapCenterChangedEventArgs e)
    {
        _mapControl?.CenterOn(e.RegionGridX, e.RegionGridY, e.LocalX, e.LocalY);
    }

    private void OnMapClicked(object? sender, MapClickEventArgs e)
    {
        if (_vm == null) return;
        _vm.CoordX = (int)e.LocalX;
        _vm.CoordY = (int)e.LocalY;
        _vm.CanTeleport = true;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapViewModel.ZoomLevel) && _vm != null && _mapControl != null)
        {
            // Map ZoomLevel (0-100 slider) to Zoom (0.5-8.0 control value)
            double zoom = 0.5 + (_vm.ZoomLevel / 100.0) * 7.5;
            _mapControl.Zoom = zoom;
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm != null)
        {
            _vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm != null)
        {
            _vm.MapCenterChanged -= OnMapCenterChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        if (_mapControl != null)
        {
            _mapControl.MapClicked -= OnMapClicked;
        }

        _vm?.Dispose();
        base.OnClosed(e);
    }
}
