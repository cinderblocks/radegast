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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OpenMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Controls;

/// <summary>
/// A cross-platform grid map control that renders known regions and avatar markers.
/// Supports pan (drag) and zoom (scroll wheel).
/// </summary>
public class GridMapControl : Control
{
    #region Styled Properties

    public static readonly StyledProperty<ObservableCollection<MapRegionEntry>?> RegionsProperty =
        AvaloniaProperty.Register<GridMapControl, ObservableCollection<MapRegionEntry>?>(nameof(Regions));

    public static readonly StyledProperty<ObservableCollection<MapAvatarEntry>?> AvatarsProperty =
        AvaloniaProperty.Register<GridMapControl, ObservableCollection<MapAvatarEntry>?>(nameof(Avatars));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<GridMapControl, double>(nameof(Zoom), 1.0);

    public ObservableCollection<MapRegionEntry>? Regions
    {
        get => GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    public ObservableCollection<MapAvatarEntry>? Avatars
    {
        get => GetValue(AvatarsProperty);
        set => SetValue(AvatarsProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    #endregion

    // Center of the view in grid coordinates (region units, e.g. 1000, 1000)
    private double _centerGridX = 1000;
    private double _centerGridY = 1000;

    // Marker position (local within region)
    private uint _markerLocalX = 128;
    private uint _markerLocalY = 128;
    private uint _markerGridX;
    private uint _markerGridY;

    // Drag state
    private bool _isDragging;
    private Point _dragStart;
    private double _dragStartCenterX;
    private double _dragStartCenterY;
    private int _lastPressClickCount;

    // Visible range tracking (for map block requests)
    private (int, int, int, int) _lastVisibleRange;

    // Repaint throttle: coalesce many tile-loaded callbacks into one repaint
    private bool _repaintRequested;
    private DispatcherTimer? _repaintTimer;

    // Drawing resources
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(4, 4, 75));
    private static readonly IBrush RegionFillBrush = new SolidColorBrush(Color.FromArgb(120, 60, 120, 60));
    private static readonly IBrush RegionBorderBrush = new SolidColorBrush(Color.FromArgb(80, 200, 200, 200));
    private static readonly IBrush AvatarBrush = new SolidColorBrush(Color.FromRgb(30, 210, 30));
    private static readonly IBrush MarkerBrush = new SolidColorBrush(Color.FromRgb(255, 80, 80));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush TextBgBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
    private static readonly IPen GridLinePen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 0.5);

    /// <summary>Event raised when the user clicks a position on the map.</summary>
    public event EventHandler<MapClickEventArgs>? MapClicked;

    /// <summary>Event raised when the user double-clicks a position on the map.</summary>
    public event EventHandler<MapClickEventArgs>? MapDoubleClicked;

    /// <summary>Event raised when the zoom level changes (e.g. via scroll wheel).</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>Event raised when the visible grid range changes (for requesting map blocks).</summary>
    public event EventHandler<VisibleRangeEventArgs>? VisibleRangeChanged;

    static GridMapControl()
    {
        AffectsRender<GridMapControl>(RegionsProperty, AvatarsProperty, ZoomProperty);
    }

    public GridMapControl()
    {
        ClipToBounds = true;
        Focusable = true;

        _repaintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _repaintTimer.Tick += (_, _) =>
        {
            _repaintTimer.Stop();
            if (_repaintRequested)
            {
                _repaintRequested = false;
                InvalidateVisual();
            }
        };
    }

    /// <summary>
    /// Schedule a throttled repaint. Multiple calls within the interval
    /// are coalesced into a single InvalidateVisual.
    /// </summary>
    public void ScheduleRepaint()
    {
        _repaintRequested = true;
        if (_repaintTimer is { IsEnabled: false })
            _repaintTimer.Start();
    }

    /// <summary>
    /// Resets the visible-range cache and schedules a redraw so that
    /// <see cref="VisibleRangeChanged"/> fires again on the next render pass.
    /// Call this after wiring up event handlers (e.g. from OnLoaded).
    /// </summary>
    public void ForceRefresh()
    {
        _lastVisibleRange = (-1, -1, -1, -1);
        InvalidateVisual();
    }

    public void CenterOn(uint regionGridX, uint regionGridY, uint localX, uint localY)
    {
        _centerGridX = regionGridX + localX / 256.0;
        _centerGridY = regionGridY + localY / 256.0;
        _markerGridX = regionGridX;
        _markerGridY = regionGridY;
        _markerLocalX = localX;
        _markerLocalY = localY;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RegionsProperty)
        {
            if (change.OldValue is ObservableCollection<MapRegionEntry> oldC)
                oldC.CollectionChanged -= OnCollectionChanged;
            if (change.NewValue is ObservableCollection<MapRegionEntry> newC)
                newC.CollectionChanged += OnCollectionChanged;
            InvalidateVisual();
        }
        else if (change.Property == AvatarsProperty)
        {
            if (change.OldValue is ObservableCollection<MapAvatarEntry> oldC)
                oldC.CollectionChanged -= OnCollectionChanged;
            if (change.NewValue is ObservableCollection<MapAvatarEntry> newC)
                newC.CollectionChanged += OnCollectionChanged;
            InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRepaint();

    #region Input Handling

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _lastPressClickCount = e.ClickCount;
            if (e.ClickCount >= 2)
            {
                HandleMapDoubleClick(pt);
                e.Handled = true;
                return;
            }
            _isDragging = true;
            _dragStart = pt;
            _dragStartCenterX = _centerGridX;
            _dragStartCenterY = _centerGridY;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        var pt = e.GetPosition(this);
        double regSize = 256.0 / Zoom;
        double pixPerRegion = regSize; // 1 region = regSize pixels at zoom=1 → actually pixels per region
        // Calculate how many grid units per pixel
        double pxPerRegUnit = Bounds.Width > 0 ? GetPixelsPerRegion() : 32;
        double dx = (pt.X - _dragStart.X) / pxPerRegUnit;
        double dy = (pt.Y - _dragStart.Y) / pxPerRegUnit;

        _centerGridX = _dragStartCenterX - dx;
        _centerGridY = _dragStartCenterY + dy; // Y is inverted (screen Y goes down, grid Y goes up)
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);

            // If minimal movement, treat as click
            var pt = e.GetPosition(this);
            if (Math.Abs(pt.X - _dragStart.X) < 3 && Math.Abs(pt.Y - _dragStart.Y) < 3)
            {
                HandleMapClick(pt);
            }
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double delta = e.Delta.Y > 0 ? 0.8 : 1.25; // Zoom in = smaller Zoom value = more pixels per region
        double newZoom = Math.Clamp(Zoom * delta, 0.5, 10.0);
        Zoom = newZoom;
        ZoomChanged?.Invoke(this, newZoom);
        e.Handled = true;
    }

    private void HandleMapDoubleClick(Point screenPoint)
    {
        double pxPerReg = GetPixelsPerRegion();
        double w = Bounds.Width;
        double h = Bounds.Height;
        double gridX = _centerGridX + (screenPoint.X - w / 2) / pxPerReg;
        double gridY = _centerGridY - (screenPoint.Y - h / 2) / pxPerReg;
        uint regionX = (uint)Math.Floor(gridX);
        uint regionY = (uint)Math.Floor(gridY);
        uint localX = (uint)Math.Clamp((gridX - regionX) * 256, 0, 255);
        uint localY = (uint)Math.Clamp((gridY - regionY) * 256, 0, 255);
        _markerGridX = regionX;
        _markerGridY = regionY;
        _markerLocalX = localX;
        _markerLocalY = localY;
        MapDoubleClicked?.Invoke(this, new MapClickEventArgs(regionX, regionY, localX, localY));
        InvalidateVisual();
    }

    private void HandleMapClick(Point screenPoint)
    {
        double pxPerReg = GetPixelsPerRegion();
        double w = Bounds.Width;
        double h = Bounds.Height;

        // Convert screen to grid coordinates
        double gridX = _centerGridX + (screenPoint.X - w / 2) / pxPerReg;
        double gridY = _centerGridY - (screenPoint.Y - h / 2) / pxPerReg;

        uint regionX = (uint)Math.Floor(gridX);
        uint regionY = (uint)Math.Floor(gridY);
        uint localX = (uint)((gridX - regionX) * 256);
        uint localY = (uint)((gridY - regionY) * 256);

        _markerGridX = regionX;
        _markerGridY = regionY;
        _markerLocalX = localX;
        _markerLocalY = localY;

        MapClicked?.Invoke(this, new MapClickEventArgs(regionX, regionY, localX, localY));
        InvalidateVisual();
    }

    #endregion

    #region Rendering

    private double GetPixelsPerRegion()
    {
        return 256.0 / Zoom; // At zoom=1, each region is 256 pixels (matches legacy regionSize/zoom)
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        double w = bounds.Width;
        double h = bounds.Height;
        if (w < 1 || h < 1) return;

        // Background
        ctx.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        double pxPerReg = GetPixelsPerRegion();

        // Determine visible grid range
        double halfW = w / 2 / pxPerReg;
        double halfH = h / 2 / pxPerReg;
        int minGX = (int)Math.Floor(_centerGridX - halfW) - 1;
        int maxGX = (int)Math.Ceiling(_centerGridX + halfW) + 1;
        int minGY = (int)Math.Floor(_centerGridY - halfH) - 1;
        int maxGY = (int)Math.Ceiling(_centerGridY + halfH) + 1;

        // Draw grid lines
        for (int gx = minGX; gx <= maxGX; gx++)
        {
            double sx = (gx - _centerGridX) * pxPerReg + w / 2;
            ctx.DrawLine(GridLinePen, new Point(sx, 0), new Point(sx, h));
        }
        for (int gy = minGY; gy <= maxGY; gy++)
        {
            double sy = (_centerGridY - gy) * pxPerReg + h / 2;
            ctx.DrawLine(GridLinePen, new Point(0, sy), new Point(w, sy));
        }

        // Build region lookup from bound Regions collection
        var regions = Regions;
        Dictionary<(uint, uint), MapRegionEntry>? regionLookup = null;
        if (regions != null)
        {
            regionLookup = new Dictionary<(uint, uint), MapRegionEntry>();
            foreach (var r in regions)
                regionLookup[(r.GridX, r.GridY)] = r;
        }

        // Draw tiles for ALL visible grid cells (not just known regions)
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.Normal);
        bool fetchTiles = pxPerReg >= 8;

        for (int gy = Math.Max(0, minGY); gy <= maxGY; gy++)
        {
            for (int gx = Math.Max(0, minGX); gx <= maxGX; gx++)
            {
                double sx = (gx - _centerGridX) * pxPerReg + w / 2;
                double sy = (_centerGridY - gy - 1) * pxPerReg + h / 2;
                var rect = new Rect(sx, sy, pxPerReg, pxPerReg);

                if (fetchTiles)
                {
                    var tile = MapTileCache.GetTile((uint)gx, (uint)gy);
                    if (tile != null)
                    {
                        ctx.DrawImage(tile, new Rect(0, 0, tile.PixelSize.Width, tile.PixelSize.Height), rect);
                    }
                    else
                    {
                        MapTileCache.RequestTile((uint)gx, (uint)gy, ScheduleRepaint);
                    }
                }
                else if (regionLookup != null && regionLookup.ContainsKey(((uint)gx, (uint)gy)))
                {
                    ctx.DrawRectangle(RegionFillBrush, new Pen(RegionBorderBrush, 0.5), rect);
                }

                // Region name + maturity label (only if large enough to read)
                if (pxPerReg >= 32 && regionLookup != null
                    && regionLookup.TryGetValue(((uint)gx, (uint)gy), out var entry))
                {
                    string maturity = entry.Access switch
                    {
                        SimAccess.PG => " (PG)",
                        SimAccess.Mature => " (M)",
                        SimAccess.Adult => " (A)",
                        _ => string.Empty
                    };
                    var label = entry.Name + maturity;
                    var text = new FormattedText(
                        label,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        Math.Min(10, pxPerReg / 4),
                        TextBrush);

                    double tx = sx + 2;
                    double ty = sy + 2;
                    ctx.DrawRectangle(TextBgBrush, null, new Rect(tx - 1, ty - 1, text.Width + 2, text.Height + 2));
                    ctx.DrawText(text, new Point(tx, ty));
                }
            }
        }

        // Draw avatar markers
        var avatars = Avatars;
        if (avatars != null)
        {
            foreach (var av in avatars)
            {
                int gx = (int)av.GridX;
                int gy = (int)av.GridY;
                if (gx < minGX || gx > maxGX || gy < minGY || gy > maxGY) continue;

                double sx = (gx + av.LocalX / 256.0 - _centerGridX) * pxPerReg + w / 2;
                double sy = (_centerGridY - gy - av.LocalY / 256.0) * pxPerReg + h / 2;
                double dotSize = Math.Max(4, av.Count * 2);
                ctx.DrawEllipse(AvatarBrush, null, new Point(sx, sy), dotSize / 2, dotSize / 2);
            }
        }

        // Draw marker (teleport target / click position)
        {
            double mx = (_markerGridX + _markerLocalX / 256.0 - _centerGridX) * pxPerReg + w / 2;
            double my = (_centerGridY - _markerGridY - _markerLocalY / 256.0) * pxPerReg + h / 2;

            // Crosshair
            var markerPen = new Pen(MarkerBrush, 2);
            ctx.DrawLine(markerPen, new Point(mx - 8, my), new Point(mx + 8, my));
            ctx.DrawLine(markerPen, new Point(mx, my - 8), new Point(mx, my + 8));
        }

        // Notify if visible range changed (for map block requests)
        var newRange = (minGX, minGY, maxGX, maxGY);
        if (newRange != _lastVisibleRange)
        {
            _lastVisibleRange = newRange;
            Dispatcher.UIThread.Post(() =>
                VisibleRangeChanged?.Invoke(this, new VisibleRangeEventArgs(
                    (ushort)Math.Max(0, minGX), (ushort)Math.Max(0, minGY),
                    (ushort)Math.Max(0, maxGX), (ushort)Math.Max(0, maxGY))));
        }
    }

    #endregion
}

public class MapClickEventArgs : EventArgs
{
    public uint RegionGridX { get; }
    public uint RegionGridY { get; }
    public uint LocalX { get; }
    public uint LocalY { get; }

    public MapClickEventArgs(uint regionGridX, uint regionGridY, uint localX, uint localY)
    {
        RegionGridX = regionGridX;
        RegionGridY = regionGridY;
        LocalX = localX;
        LocalY = localY;
    }
}

public class VisibleRangeEventArgs : EventArgs
{
    public ushort MinX { get; }
    public ushort MinY { get; }
    public ushort MaxX { get; }
    public ushort MaxY { get; }

    public VisibleRangeEventArgs(ushort minX, ushort minY, ushort maxX, ushort maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }
}
