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
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class RegionViewModel : ClientAwareViewModelBase
{
    private static readonly ISolidColorBrush BrushGood = new SolidColorBrush(Color.FromRgb(0x44, 0xBB, 0x44));
    private static readonly ISolidColorBrush BrushWarn = new SolidColorBrush(Color.FromRgb(0xDD, 0xAA, 0x00));
    private static readonly ISolidColorBrush BrushBad  = new SolidColorBrush(Color.FromRgb(0xCC, 0x22, 0x22));

    private DispatcherTimer? _timer;

    // Region identity
    [ObservableProperty] private string _regionName = string.Empty;
    [ObservableProperty] private string _regionType = string.Empty;
    [ObservableProperty] private string _simVersion = string.Empty;
    [ObservableProperty] private string _dataCenter = string.Empty;

    // Headline perf metrics
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FpsBrush))]
    private int _fps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DilationBrush))]
    private float _dilation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhysicsFpsBrush))]
    private float _physicsFps;

    // Frame time breakdown (milliseconds)
    [ObservableProperty] private float _frameTime;
    [ObservableProperty] private float _netTime;
    [ObservableProperty] private float _physicsTime;
    [ObservableProperty] private float _scriptTime;
    [ObservableProperty] private float _agentTime;
    [ObservableProperty] private float _imageTime;
    [ObservableProperty] private float _otherTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpareTimeBrush))]
    private float _spareTime;

    // Population
    [ObservableProperty] private int _agents;
    [ObservableProperty] private int _childAgents;
    [ObservableProperty] private int _objects;
    [ObservableProperty] private int _scriptedObjects;
    [ObservableProperty] private int _activeScripts;
    [ObservableProperty] private int _lslIps;

    // Network
    [ObservableProperty] private string _incomingBps = string.Empty;
    [ObservableProperty] private string _outgoingBps = string.Empty;
    [ObservableProperty] private int _pendingDownloads;
    [ObservableProperty] private int _pendingUploads;
    [ObservableProperty] private int _lag;
    [ObservableProperty] private int _missedPings;

    public ISolidColorBrush FpsBrush        => Fps       >= 40   ? BrushGood : Fps       >= 25   ? BrushWarn : BrushBad;
    public ISolidColorBrush DilationBrush   => Dilation  >= 0.8f ? BrushGood : Dilation  >= 0.6f ? BrushWarn : BrushBad;
    public ISolidColorBrush PhysicsFpsBrush => PhysicsFps >= 40f ? BrushGood : PhysicsFps >= 25f  ? BrushWarn : BrushBad;
    public ISolidColorBrush SpareTimeBrush  => SpareTime  >= 5f  ? BrushGood : SpareTime  >= 2f   ? BrushWarn : BrushBad;

    public RegionViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        if (!Client.Network.Connected) return;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;

        var s = sim.Stats;

        RegionName  = sim.Name;
        RegionType  = sim.ProductName  ?? string.Empty;
        SimVersion  = sim.SimVersion   ?? string.Empty;
        DataCenter  = sim.ColoLocation ?? string.Empty;

        Fps        = s.FPS;
        Dilation   = s.Dilation;
        PhysicsFps = s.PhysicsFPS;

        FrameTime   = s.FrameTime;
        NetTime     = s.NetTime;
        PhysicsTime = s.PhysicsTime;
        ScriptTime  = s.ScriptTime;
        AgentTime   = s.AgentTime;
        ImageTime   = s.ImageTime;
        OtherTime   = s.OtherTime;
        SpareTime   = Math.Max(0f, 1000f / 45f -
            (s.NetTime + s.PhysicsTime + s.OtherTime + s.AgentTime + s.ImageTime + s.ScriptTime));

        Agents        = s.Agents;
        ChildAgents   = s.ChildAgents;
        Objects       = s.Objects;
        ScriptedObjects = s.ScriptedObjects;
        ActiveScripts = s.ActiveScripts;
        LslIps        = s.LSLIPS;

        IncomingBps      = $"{s.GetIncomingBPS() / 1024.0:F1} KB/s";
        OutgoingBps      = $"{s.GetOutgoingBPS() / 1024.0:F1} KB/s";
        PendingDownloads = s.PendingDownloads;
        PendingUploads   = s.PendingUploads + s.PendingLocalUploads;
        Lag              = s.LastLag;
        MissedPings      = s.MissedPings;
    }

    protected override void RegisterClientEvents(GridClient client)   { }
    protected override void UnregisterClientEvents(GridClient client) { }

    public override void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        base.Dispose();
    }
}
