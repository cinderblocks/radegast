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
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenMetaverse;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Controls;

public partial class ObjectMinimapControl : UserControl
{
    // — Styled Properties —

    public static readonly StyledProperty<ObservableCollection<ObjectMapEntry>?> EntriesProperty =
        AvaloniaProperty.Register<ObjectMinimapControl, ObservableCollection<ObjectMapEntry>?>(nameof(Entries));

    public static readonly StyledProperty<Bitmap?> BackgroundImageProperty =
        AvaloniaProperty.Register<ObjectMinimapControl, Bitmap?>(nameof(BackgroundImage));

    public static readonly StyledProperty<UUID> SelectedObjectIdProperty =
        AvaloniaProperty.Register<ObjectMinimapControl, UUID>(nameof(SelectedObjectId));

    public static readonly StyledProperty<float> SelfXProperty =
        AvaloniaProperty.Register<ObjectMinimapControl, float>(nameof(SelfX));

    public static readonly StyledProperty<float> SelfYProperty =
        AvaloniaProperty.Register<ObjectMinimapControl, float>(nameof(SelfY));

    public ObservableCollection<ObjectMapEntry>? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public Bitmap? BackgroundImage
    {
        get => GetValue(BackgroundImageProperty);
        set => SetValue(BackgroundImageProperty, value);
    }

    public UUID SelectedObjectId
    {
        get => GetValue(SelectedObjectIdProperty);
        set => SetValue(SelectedObjectIdProperty, value);
    }

    public float SelfX
    {
        get => GetValue(SelfXProperty);
        set => SetValue(SelfXProperty, value);
    }

    public float SelfY
    {
        get => GetValue(SelfYProperty);
        set => SetValue(SelfYProperty, value);
    }

    private Canvas? _canvas;
    private Border? _hoverTooltip;
    private TextBlock? _hoverTooltipText;

    // Hit areas: bounding rect + tooltip text
    private readonly List<(Rect Bounds, string Tooltip)> _hitAreas = new();

    private Point _lastPointerPos;
    private bool _pointerOnCanvas;
    private bool _redrawPending;

    // Cached brushes — allocated once, reused every frame
    private readonly SolidColorBrush _gridBrushCached    = new(Color.FromArgb( 40, 255, 255, 255));
    private readonly SolidColorBrush _normalFillCached   = new(Color.FromArgb(160,  60, 130, 220));
    private readonly SolidColorBrush _selectedFillCached = new(Color.FromArgb(200, 255, 220,  50));
    private readonly SolidColorBrush _selectedRingCached = new(Color.FromRgb(255,  220,  50));
    private readonly SolidColorBrush _offSimFillCached   = new(Color.FromArgb(220, 255, 165,  32));
    private readonly SolidColorBrush _selfFillCached     = new(Color.FromRgb(  0, 200,  80));

    // Pre-allocated grid lines (3 vertical + 3 horizontal)
    private readonly Line[] _gridLines;

    // Shape pools — index resets to 0 at the start of each redraw
    private readonly List<Rectangle> _rectPool  = [];
    private readonly List<Rectangle> _ringPool  = [];
    private readonly List<TextBlock> _arrowPool = [];
    private int _rectPoolIdx;
    private int _ringPoolIdx;
    private int _arrowPoolIdx;

    // Single-instance reusable controls
    private readonly Image   _bgImageControl = new() { Stretch = Stretch.Fill };
    private readonly Ellipse _selfDotControl = new() { Width = 7, Height = 7 };

    // Staging buffer — children are written here then batch-applied to avoid per-Add notifications
    private readonly List<Control> _childrenBuffer = [];

    public ObjectMinimapControl()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("ObjectMapCanvas");
        _hoverTooltip = this.FindControl<Border>("HoverTooltip");
        _hoverTooltipText = this.FindControl<TextBlock>("HoverTooltipText");

        // Pre-allocate grid lines with cached brush
        _gridLines = new Line[6];
        for (int i = 0; i < 6; i++)
            _gridLines[i] = new Line { Stroke = _gridBrushCached, StrokeThickness = 0.5 };

        // Pre-configure self-dot
        _selfDotControl.Fill = _selfFillCached;

        if (_canvas != null)
        {
            _canvas.PointerMoved += Canvas_PointerMoved;
            _canvas.PointerExited += Canvas_PointerExited;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EntriesProperty)
        {
            if (change.OldValue is ObservableCollection<ObjectMapEntry> oldCol)
                oldCol.CollectionChanged -= Entries_CollectionChanged;
            if (change.NewValue is ObservableCollection<ObjectMapEntry> newCol)
                newCol.CollectionChanged += Entries_CollectionChanged;
            ScheduleRedraw();
        }
        else if (change.Property == BackgroundImageProperty
              || change.Property == SelectedObjectIdProperty
              || change.Property == SelfXProperty
              || change.Property == SelfYProperty)
        {
            ScheduleRedraw();
        }
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRedraw();

    protected override Size MeasureOverride(Size availableSize)
    {
        // Always measure as a square: side = available width, falling back to 268 if unconstrained.
        double side = double.IsInfinity(availableSize.Width) ? 268 : availableSize.Width;
        base.MeasureOverride(new Size(side, side));
        return new Size(side, side);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double side = Math.Min(finalSize.Width, finalSize.Height);
        var result = base.ArrangeOverride(new Size(side, side));
        ScheduleRedraw();
        return result;
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null || _hoverTooltip == null || _hoverTooltipText == null) return;
        var pt = e.GetPosition(_canvas);
        _lastPointerPos = pt;
        _pointerOnCanvas = true;
        UpdateTooltip(pt);
    }

    private void Canvas_PointerExited(object? sender, PointerEventArgs e)
    {
        _pointerOnCanvas = false;
        if (_hoverTooltip != null) _hoverTooltip.IsVisible = false;
    }

    private void UpdateTooltip(Point pt)
    {
        if (_canvas == null || _hoverTooltip == null || _hoverTooltipText == null) return;

        foreach (var (bounds, tooltip) in _hitAreas)
        {
            if (bounds.Contains(pt))
            {
                _hoverTooltipText.Text = tooltip;
                _hoverTooltip.IsVisible = true;

                double tx = pt.X + 12;
                double ty = pt.Y + 12;
                var cw = _canvas.Bounds.Width;
                var ch = _canvas.Bounds.Height;
                if (cw > 0 && tx + 160 > cw) tx = pt.X - 168;
                if (ch > 0 && ty + 46 > ch)  ty = pt.Y - 52;
                tx = Math.Max(0, tx);
                ty = Math.Max(0, ty);
                _hoverTooltip.Margin = new Thickness(tx, ty, 0, 0);
                return;
            }
        }

        _hoverTooltip.IsVisible = false;
    }

    private void ScheduleRedraw()
    {
        if (_redrawPending) return;
        _redrawPending = true;
        Dispatcher.UIThread.Post(Redraw, DispatcherPriority.Render);
    }

    private void Redraw()
    {
        _redrawPending = false;
        if (_canvas == null) return;

        var w = _canvas.Bounds.Width;
        var h = _canvas.Bounds.Height;

        _childrenBuffer.Clear();
        _hitAreas.Clear();
        _rectPoolIdx  = 0;
        _ringPoolIdx  = 0;
        _arrowPoolIdx = 0;

        if (w < 1 || h < 1)
        {
            _canvas.Children.Clear();
            return;
        }

        // Background map tile — reuse single Image instance
        if (BackgroundImage is { } bgImage)
        {
            _bgImageControl.Source = bgImage;
            _bgImageControl.Width  = w;
            _bgImageControl.Height = h;
            Canvas.SetLeft(_bgImageControl, 0);
            Canvas.SetTop(_bgImageControl,  0);
            _childrenBuffer.Add(_bgImageControl);
        }

        // Grid lines — 64 m intervals (pre-allocated, just update endpoints)
        for (int i = 1; i < 4; i++)
        {
            double gx = w * i / 4.0;
            double gy = h * i / 4.0;
            int vi = (i - 1) * 2;
            int hi = vi + 1;
            _gridLines[vi].StartPoint = new Point(gx, 0);
            _gridLines[vi].EndPoint   = new Point(gx, h);
            _gridLines[hi].StartPoint = new Point(0,  gy);
            _gridLines[hi].EndPoint   = new Point(w,  gy);
            _childrenBuffer.Add(_gridLines[vi]);
            _childrenBuffer.Add(_gridLines[hi]);
        }

        var entries    = Entries;
        var selectedId = SelectedObjectId;

        if (entries != null)
        {
            foreach (var entry in entries)
            {
                bool isSelected = selectedId != UUID.Zero && entry.Id == selectedId;
                if (entry.IsInSim)
                    DrawInSimObject(entry, w, h, isSelected);
                else
                    DrawOffSimArrow(entry, w, h);
            }
        }

        // Self dot — drawn on top, reuse single Ellipse instance
        var selfX = SelfX;
        var selfY = SelfY;
        if (selfX > 0 || selfY > 0)
        {
            const double dotSize = 7;
            double cx = selfX / 256.0 * w;
            double cy = (256.0 - selfY) / 256.0 * h;
            Canvas.SetLeft(_selfDotControl, cx - dotSize / 2);
            Canvas.SetTop(_selfDotControl,  cy - dotSize / 2);
            _childrenBuffer.Add(_selfDotControl);
        }

        // Batch-replace children — one Clear + one AddRange instead of N individual Adds
        _canvas.Children.Clear();
        _canvas.Children.AddRange(_childrenBuffer);

        if (_pointerOnCanvas)
            UpdateTooltip(_lastPointerPos);
    }

    private Rectangle GetOrCreateRect()
    {
        if (_rectPoolIdx < _rectPool.Count)
            return _rectPool[_rectPoolIdx++];
        var r = new Rectangle();
        _rectPool.Add(r);
        _rectPoolIdx++;
        return r;
    }

    private Rectangle GetOrCreateRing()
    {
        if (_ringPoolIdx < _ringPool.Count)
            return _ringPool[_ringPoolIdx++];
        var r = new Rectangle();
        _ringPool.Add(r);
        _ringPoolIdx++;
        return r;
    }

    private TextBlock GetOrCreateArrow()
    {
        if (_arrowPoolIdx < _arrowPool.Count)
            return _arrowPool[_arrowPoolIdx++];
        var tb = new TextBlock();
        _arrowPool.Add(tb);
        _arrowPoolIdx++;
        return tb;
    }

    private void DrawInSimObject(ObjectMapEntry entry, double w, double h, bool isSelected)
    {
        // Map object centre to canvas
        double cx = entry.X / 256.0 * w;
        double cy = (256.0 - entry.Y) / 256.0 * h;

        // Scale half-extents from region metres to canvas pixels
        double pw = Math.Max(4, entry.HalfW * 2.0 / 256.0 * w);
        double ph = Math.Max(4, entry.HalfD * 2.0 / 256.0 * h);

        double left = cx - pw / 2;
        double top  = cy - ph / 2;

        // Selection ring (slightly larger) — pooled
        if (isSelected)
        {
            double rw = pw + 6;
            double rh = ph + 6;
            var ring = GetOrCreateRing();
            ring.Width           = rw;
            ring.Height          = rh;
            ring.Stroke          = _selectedRingCached;
            ring.StrokeThickness = 1.5;
            ring.Fill            = Brushes.Transparent;
            Canvas.SetLeft(ring, cx - rw / 2);
            Canvas.SetTop(ring,  cy - rh / 2);
            _childrenBuffer.Add(ring);
        }

        var rect = GetOrCreateRect();
        rect.Width           = pw;
        rect.Height          = ph;
        rect.Fill            = isSelected ? _selectedFillCached : _normalFillCached;
        rect.Stroke          = isSelected ? _selectedRingCached : null;
        rect.StrokeThickness = isSelected ? 1 : 0;
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect,  top);
        _childrenBuffer.Add(rect);

        // Hit area
        const double hitPad = 4;
        _hitAreas.Add((
            new Rect(left - hitPad, top - hitPad, pw + hitPad * 2, ph + hitPad * 2),
            $"{entry.Name}\n({(int)entry.X}, {(int)entry.Y}, {(int)entry.Z})"
        ));
    }

    private void DrawOffSimArrow(ObjectMapEntry entry, double w, double h)
    {
        // Determine direction
        bool offN = entry.Y > 256;
        bool offS = entry.Y < 0;
        bool offE = entry.X > 256;
        bool offW = entry.X < 0;

        string arrow = (offN, offS, offE, offW) switch
        {
            (true,  false, false, true)  => "↖",
            (true,  false, true,  false) => "↗",
            (false, true,  false, true)  => "↙",
            (false, true,  true,  false) => "↘",
            (true,  false, false, false) => "↑",
            (false, true,  false, false) => "↓",
            (false, false, true,  false) => "→",
            (false, false, false, true)  => "←",
            _ => "?"
        };

        // Project the arrow onto the map edge at the object's clamped coordinate
        double clampedX = Math.Clamp(entry.X, 0, 256);
        double clampedY = Math.Clamp(entry.Y, 0, 256);
        double edgeCx   = clampedX / 256.0 * w;
        double edgeCy   = (256.0 - clampedY) / 256.0 * h;

        const double arrowSize = 14;
        const double margin    = 4;

        // Snap to the nearest edge(s)
        if (offN)  edgeCy = margin;
        if (offS)  edgeCy = h - arrowSize - margin;
        if (offE)  edgeCx = w - arrowSize - margin;
        if (offW)  edgeCx = margin;

        var tb = GetOrCreateArrow();
        tb.Text       = arrow;
        tb.FontSize   = arrowSize;
        tb.Foreground = _offSimFillCached;
        tb.FontWeight = FontWeight.Bold;
        Canvas.SetLeft(tb, edgeCx);
        Canvas.SetTop(tb,  edgeCy);
        _childrenBuffer.Add(tb);

        _hitAreas.Add((
            new Rect(edgeCx - 2, edgeCy - 2, arrowSize + 4, arrowSize + 4),
            $"{entry.Name} (off-map)"
        ));
    }
}
