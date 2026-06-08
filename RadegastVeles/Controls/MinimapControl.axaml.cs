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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenMetaverse;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Controls;

public partial class MinimapControl : UserControl
{
    // — Styled Properties —

    public static readonly StyledProperty<ObservableCollection<MinimapEntry>?> EntriesProperty =
        AvaloniaProperty.Register<MinimapControl, ObservableCollection<MinimapEntry>?>(nameof(Entries));

    public static readonly StyledProperty<Bitmap?> BackgroundImageProperty =
        AvaloniaProperty.Register<MinimapControl, Bitmap?>(nameof(BackgroundImage));

    public static readonly StyledProperty<UUID> SelectedEntryIdProperty =
        AvaloniaProperty.Register<MinimapControl, UUID>(nameof(SelectedEntryId));

    public static readonly StyledProperty<string> SimNameProperty =
        AvaloniaProperty.Register<MinimapControl, string>(nameof(SimName), string.Empty);

    public ObservableCollection<MinimapEntry>? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public Bitmap? BackgroundImage
    {
        get => GetValue(BackgroundImageProperty);
        set => SetValue(BackgroundImageProperty, value);
    }

    public UUID SelectedEntryId
    {
        get => GetValue(SelectedEntryIdProperty);
        set => SetValue(SelectedEntryIdProperty, value);
    }

    /// <summary>Current simulator name, used when building "Copy SLURL" menu items.</summary>
    public string SimName
    {
        get => GetValue(SimNameProperty);
        set => SetValue(SimNameProperty, value);
    }

    /// <summary>Fired when the user left-clicks on an empty map area. x/y are region coordinates (0–256).</summary>
    public event Action<float, float>? WalkToRequested;
    /// <summary>Fired when the user right-clicks and chooses Teleport To. x/y are region coordinates (0–256).</summary>
    public event Action<float, float>? TeleportRequested;
    /// <summary>Fired when the user chooses "About Land…" from the context menu. x/y are region coordinates (0–256).</summary>
    public event Action<float, float>? AboutLandRequested;

    private Canvas? _canvas;
    private Border? _hoverTooltip;
    private TextBlock? _hoverTooltipText;

    // Hit areas for avatar dots: bounding rect + associated entry
    private readonly List<(Rect Bounds, MinimapEntry Entry)> _dotHitAreas = new();

    private Point _lastPointerPos;
    private bool _pointerOnCanvas;

    public MinimapControl()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("MinimapCanvas");
        _hoverTooltip = this.FindControl<Border>("HoverTooltip");
        _hoverTooltipText = this.FindControl<TextBlock>("HoverTooltipText");

        if (_canvas != null)
        {
            _canvas.PointerPressed += Canvas_PointerPressed;
            _canvas.PointerMoved += Canvas_PointerMoved;
            _canvas.PointerExited += Canvas_PointerExited;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EntriesProperty)
        {
            if (change.OldValue is ObservableCollection<MinimapEntry> oldCol)
                oldCol.CollectionChanged -= Entries_CollectionChanged;
            if (change.NewValue is ObservableCollection<MinimapEntry> newCol)
                newCol.CollectionChanged += Entries_CollectionChanged;
            Redraw();
        }
        else if (change.Property == BackgroundImageProperty || change.Property == SelectedEntryIdProperty)
        {
            Redraw();
        }
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        Redraw();
        return result;
    }

    // Convert canvas position to region coordinates (0–256 range)
    private (float rx, float ry) CanvasToRegion(Point pt)
    {
        var w = _canvas!.Bounds.Width;
        var h = _canvas!.Bounds.Height;
        float rx = (float)(pt.X / w * 256.0);
        float ry = (float)((h - pt.Y) / h * 256.0); // flip Y: top of canvas = north = high Y
        return (Math.Clamp(rx, 0, 256), Math.Clamp(ry, 0, 256));
    }

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_canvas == null) return;
        var pt = e.GetPosition(_canvas);
        var (rx, ry) = CanvasToRegion(pt);

        var props = e.GetCurrentPoint(_canvas).Properties;

        if (props.IsRightButtonPressed)
        {
            var menu = new ContextMenu();
            var walkItem = new MenuItem { Header = $"Walk To ({(int)rx}, {(int)ry})" };
            walkItem.Click += (_, _) => WalkToRequested?.Invoke(rx, ry);
            var tpItem = new MenuItem { Header = $"Teleport To ({(int)rx}, {(int)ry})" };
            tpItem.Click += (_, _) => TeleportRequested?.Invoke(rx, ry);
            menu.Items.Add(walkItem);
            menu.Items.Add(tpItem);

            menu.Items.Add(new Separator());
            var landItem = new MenuItem { Header = "About Land…" };
            landItem.Click += (_, _) => AboutLandRequested?.Invoke(rx, ry);
            menu.Items.Add(landItem);

            var sim = SimName;
            if (!string.IsNullOrEmpty(sim))
            {
                var slurl = $"secondlife://{Uri.EscapeDataString(sim)}/{(int)rx}/{(int)ry}/0";
                menu.Items.Add(new Separator());
                var copyItem = new MenuItem { Header = $"Copy SLURL ({(int)rx},{(int)ry})" };
                copyItem.Click += async (s, _) =>
                {
                    var clip = TopLevel.GetTopLevel(s as MenuItem)?.Clipboard;
                    if (clip != null) await clip.SetTextAsync(slurl);
                };
                menu.Items.Add(copyItem);
            }

            menu.Open(this);
        }
        else if (props.IsLeftButtonPressed)
        {
            // Left-click on empty space = walk to; clicking on a dot has no extra action
            bool onDot = false;
            foreach (var (bounds, _) in _dotHitAreas)
            {
                if (bounds.Contains(pt)) { onDot = true; break; }
            }
            if (!onDot)
                WalkToRequested?.Invoke(rx, ry);
        }

        e.Handled = true;
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null || _hoverTooltip == null || _hoverTooltipText == null) return;
        var pt = e.GetPosition(_canvas);
        _lastPointerPos = pt;
        _pointerOnCanvas = true;
        UpdateTooltip(pt);
    }

    private void UpdateTooltip(Point pt)
    {
        if (_canvas == null || _hoverTooltip == null || _hoverTooltipText == null) return;

        foreach (var (bounds, entry) in _dotHitAreas)
        {
            if (bounds.Contains(pt))
            {
                _hoverTooltipText.Text = $"{entry.Name}\n({(int)entry.X}, {(int)entry.Y}, {(int)entry.Z})";
                _hoverTooltip.IsVisible = true;

                // Position tooltip near the cursor, keeping it within the control
                double tx = pt.X + 12;
                double ty = pt.Y + 12;
                var cw = _canvas.Bounds.Width;
                var ch = _canvas.Bounds.Height;
                if (cw > 0 && tx + 140 > cw) tx = pt.X - 148;
                if (ch > 0 && ty + 40 > ch)  ty = pt.Y - 46;
                tx = Math.Max(0, tx);
                ty = Math.Max(0, ty);
                _hoverTooltip.Margin = new Thickness(tx, ty, 0, 0);
                return;
            }
        }

        _hoverTooltip.IsVisible = false;
    }

    private void Canvas_PointerExited(object? sender, PointerEventArgs e)
    {
        _pointerOnCanvas = false;
        _hoverTooltip?.IsVisible = false;
    }

    private void Redraw()
    {
        if (_canvas == null) return;
        _canvas.Children.Clear();
        _dotHitAreas.Clear();

        var w = _canvas.Bounds.Width;
        var h = _canvas.Bounds.Height;
        if (w < 1 || h < 1) return;

        // Background tile image
        if (BackgroundImage is { } bgImage)
        {
            var img = new Image
            {
                Source = bgImage,
                Width = w,
                Height = h,
                Stretch = Stretch.Fill
            };
            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);
            _canvas.Children.Add(img);
        }

        var entries = Entries;
        if (entries == null || entries.Count == 0) return;

        // Grid lines (64 m intervals on a 256 m region)
        var gridBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        for (int i = 1; i < 4; i++)
        {
            double gx = w * i / 4.0;
            double gy = h * i / 4.0;
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(gx, 0),
                EndPoint   = new Point(gx, h),
                Stroke = gridBrush,
                StrokeThickness = 0.5
            });
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(0, gy),
                EndPoint   = new Point(w, gy),
                Stroke = gridBrush,
                StrokeThickness = 0.5
            });
        }

        var selectedId = SelectedEntryId;

        // Avatar dots
        foreach (var entry in entries)
        {
            double cx = entry.X / 256.0 * w;
            double cy = (256.0 - entry.Y) / 256.0 * h; // flip Y so north is up

            bool isSelected = entry.Id == selectedId && selectedId != UUID.Zero;
            double dotSize = entry.IsSelf ? 8 : 6;
            if (isSelected) dotSize = 9;

            // Selection ring
            if (isSelected)
            {
                double ringSize = dotSize + 6;
                var ring = new Ellipse
                {
                    Width  = ringSize,
                    Height = ringSize,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 220, 50)),
                    StrokeThickness = 1.5,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(ring, cx - ringSize / 2);
                Canvas.SetTop(ring,  cy - ringSize / 2);
                _canvas.Children.Add(ring);
            }

            var dot = new Ellipse
            {
                Width  = dotSize,
                Height = dotSize,
                Fill = entry.IsSelf
                    ? new SolidColorBrush(Color.FromRgb(0, 200, 80))
                    : isSelected
                        ? new SolidColorBrush(Color.FromRgb(255, 220, 50))
                        : new SolidColorBrush(Color.FromRgb(60, 160, 255))
            };
            Canvas.SetLeft(dot, cx - dotSize / 2);
            Canvas.SetTop(dot,  cy - dotSize / 2);
            _canvas.Children.Add(dot);

            // Heading arc: 1/5-circle arc on the outer rim pointing in the facing direction
            if (entry.Heading is float yaw)
            {
                double arcR = dotSize / 2.0 + 2.5;
                const double sweepHalf = Math.PI / 5.0; // 36° → 72° total sweep
                // Canvas angle: negate yaw because canvas Y increases downward (north = -Y)
                double ca = -(double)yaw;
                double sa = ca - sweepHalf;
                double ea = ca + sweepHalf;

                double sx = cx + arcR * Math.Cos(sa);
                double sy = cy + arcR * Math.Sin(sa);
                double ex = cx + arcR * Math.Cos(ea);
                double ey = cy + arcR * Math.Sin(ea);

                var figure = new PathFigure
                {
                    StartPoint = new Point(sx, sy),
                    IsClosed = false,
                    IsFilled = false
                };
                figure.Segments.Add(new ArcSegment
                {
                    Point = new Point(ex, ey),
                    Size = new Size(arcR, arcR),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = false
                });
                var pathGeo = new PathGeometry();
                pathGeo.Figures.Add(figure);
                var arc = new Path
                {
                    Data = pathGeo,
                    Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                    StrokeThickness = 2.0,
                    StrokeLineCap = PenLineCap.Round
                };
                Canvas.SetLeft(arc, 0);
                Canvas.SetTop(arc, 0);
                _canvas.Children.Add(arc);
            }

            // Hit area for hover (slightly larger than the visual dot)
            double hitPad = 4;
            _dotHitAreas.Add((
                new Rect(cx - dotSize / 2 - hitPad, cy - dotSize / 2 - hitPad,
                         dotSize + hitPad * 2, dotSize + hitPad * 2),
                entry
            ));

            // Name label for self
            if (entry.IsSelf)
            {
                var label = new TextBlock
                {
                    Text = "You",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Canvas.SetLeft(label, cx + dotSize / 2 + 2);
                Canvas.SetTop(label,  cy - 6);
                _canvas.Children.Add(label);
            }
        }

        // If the pointer is still on the canvas, re-evaluate the tooltip
        // (a PointerMoved won't fire automatically after a redraw)
        if (_pointerOnCanvas)
            UpdateTooltip(_lastPointerPos);
    }
}
