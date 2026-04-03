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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using OpenMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class EstateProfileViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private DispatcherTimer? _refreshTimer;

    [ObservableProperty] private string _regionName = string.Empty;
    [ObservableProperty] private string _regionType = string.Empty;
    [ObservableProperty] private string _simVersion = string.Empty;
    [ObservableProperty] private string _dataCenter = string.Empty;
    [ObservableProperty] private string _cpuClass = string.Empty;

    // Simulator statistics
    [ObservableProperty] private string _fps = "0";
    [ObservableProperty] private string _timeDilation = "0.000";
    [ObservableProperty] private string _mainAgents = "0";
    [ObservableProperty] private string _childAgents = "0";
    [ObservableProperty] private string _totalObjects = "0";
    [ObservableProperty] private string _activeObjects = "0";
    [ObservableProperty] private string _activeScripts = "0";
    [ObservableProperty] private string _pendingDownloads = "0";
    [ObservableProperty] private string _pendingUploads = "0";

    // Network bandwidth
    [ObservableProperty] private string _accessLevel = string.Empty;
    [ObservableProperty] private string _incomingBps = "0 KB/s";
    [ObservableProperty] private string _outgoingBps = "0 KB/s";
    [ObservableProperty] private string _unackedBytes = "0";

    // Region flags (read-only display)
    [ObservableProperty] private bool _flagAllowDamage;
    [ObservableProperty] private bool _flagNoFly;
    [ObservableProperty] private bool _flagAllowVoice;
    [ObservableProperty] private bool _flagSunFixed;
    [ObservableProperty] private bool _flagBlockTerraform;
    [ObservableProperty] private bool _flagDirectTeleport;
    [ObservableProperty] private bool _flagRestrictPush;
    [ObservableProperty] private bool _flagSkipScripts;
    [ObservableProperty] private bool _flagSkipPhysics;

    // Frame timing
    [ObservableProperty] private string _totalTime = "0.0 ms";
    [ObservableProperty] private string _netTime = "0.0 ms";
    [ObservableProperty] private string _physicsTime = "0.0 ms";
    [ObservableProperty] private string _simTime = "0.0 ms";
    [ObservableProperty] private string _agentTime = "0.0 ms";
    [ObservableProperty] private string _imageTime = "0.0 ms";
    [ObservableProperty] private string _scriptTime = "0.0 ms";
    [ObservableProperty] private string _spareTime = "0.0 ms";

    // Covenant
    [ObservableProperty] private string _covenantText = string.Empty;
    [ObservableProperty] private bool _isCovenantLoading = true;

    public EstateProfileViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        UpdateDisplay();

        Client.Estate.EstateCovenantReply += Estate_CovenantReply;
        Client.Estate.RequestCovenant();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (_, _) => UpdateDisplay();
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        Client.Estate.EstateCovenantReply -= Estate_CovenantReply;
    }

    private void UpdateDisplay()
    {
        if (!Client.Network.Connected || Client.Network.CurrentSim == null) return;

        var sim = Client.Network.CurrentSim;
        RegionName = sim.Name;
        RegionType = sim.ProductName ?? string.Empty;
        SimVersion = sim.SimVersion ?? string.Empty;
        DataCenter = sim.ColoLocation ?? string.Empty;
        CpuClass = sim.CPUClass > 0 ? sim.CPUClass.ToString() : string.Empty;

        var s = sim.Stats;
        Fps = s.FPS.ToString();
        TimeDilation = $"{s.Dilation:0.000}";
        MainAgents = s.Agents.ToString();
        ChildAgents = s.ChildAgents.ToString();
        TotalObjects = s.Objects.ToString();
        ActiveObjects = s.ScriptedObjects.ToString();
        ActiveScripts = s.ActiveScripts.ToString();
        PendingDownloads = s.PendingDownloads.ToString();
        PendingUploads = (s.PendingLocalUploads + s.PendingUploads).ToString();

        float total = s.NetTime + s.PhysicsTime + s.OtherTime + s.AgentTime +
                      s.ImageTime + s.ScriptTime;
        TotalTime = $"{s.FrameTime:0.0} ms";
        NetTime = $"{s.NetTime:0.0} ms";
        PhysicsTime = $"{s.PhysicsTime:0.0} ms";
        SimTime = $"{s.OtherTime:0.0} ms";
        AgentTime = $"{s.AgentTime:0.0} ms";
        ImageTime = $"{s.ImageTime:0.0} ms";
        ScriptTime = $"{s.ScriptTime:0.0} ms";
        SpareTime = $"{Math.Max(0f, 1000f / 45f - total):0.0} ms";

        AccessLevel = sim.Access switch
        {
            SimAccess.Adult  => "Adult",
            SimAccess.Mature => "Moderate",
            _                => "General"
        };

        IncomingBps = $"{sim.Stats.GetIncomingBPS() / 1024.0:F1} KB/s";
        OutgoingBps = $"{sim.Stats.GetOutgoingBPS() / 1024.0:F1} KB/s";
        UnackedBytes = sim.Stats.UnackedBytes.ToString();

        FlagAllowDamage    = sim.Flags.HasFlag(RegionFlags.AllowDamage);
        FlagNoFly          = sim.Flags.HasFlag(RegionFlags.NoFly);
        FlagAllowVoice     = sim.Flags.HasFlag(RegionFlags.AllowVoice);
        FlagSunFixed       = sim.Flags.HasFlag(RegionFlags.SunFixed);
        FlagBlockTerraform = sim.Flags.HasFlag(RegionFlags.BlockTerraform);
        FlagDirectTeleport = sim.Flags.HasFlag(RegionFlags.AllowDirectTeleport);
        FlagRestrictPush   = sim.Flags.HasFlag(RegionFlags.RestrictPushObject);
        FlagSkipScripts    = sim.Flags.HasFlag(RegionFlags.SkipScripts);
        FlagSkipPhysics    = sim.Flags.HasFlag(RegionFlags.SkipPhysics);
    }

    [RelayCommand]
    private void Refresh()
    {
        UpdateDisplay();
        IsCovenantLoading = true;
        Client.Estate.RequestCovenant();
    }

    private void Estate_CovenantReply(object? sender, EstateCovenantReplyEventArgs e)
    {
        if (e.CovenantID == UUID.Zero)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CovenantText = "No covenant for this estate.";
                IsCovenantLoading = false;
            });
            return;
        }

        Client.Estate.RequestCovenantNotecard(e.CovenantID, (transfer, asset) =>
        {
            string text;
            if (asset is AssetNotecard notecard)
            {
                notecard.Decode();
                text = string.IsNullOrWhiteSpace(notecard.BodyText)
                    ? "No covenant for this estate."
                    : notecard.BodyText;
            }
            else
            {
                text = "Unable to load covenant.";
            }

            Dispatcher.UIThread.Post(() =>
            {
                CovenantText = text;
                IsCovenantLoading = false;
            });
        });
    }
}
