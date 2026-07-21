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
using CommunityToolkit.Mvvm.ComponentModel;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private int _prevBalance = -1;

    [ObservableProperty]
    private string _title = "Radegast Veles";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _locationText = string.Empty;

    [ObservableProperty]
    private string _balanceText = string.Empty;

    [ObservableProperty]
    private bool _isAway;

    [ObservableProperty]
    private bool _isBusy;

    public NearbyViewModel Chat { get; }
    public IMViewModel IM { get; }
    public MapViewModel Map { get; }
    public ObjectsViewModel Objects { get; }
    public InventoryViewModel Inventory { get; }
    public FriendsViewModel Friends { get; }
    public GroupsViewModel Groups { get; }
    public RegionViewModel Region { get; }
    public AppearanceViewModel Appearance { get; }
    public MediaViewModel Media { get; }
    public NotificationQueueViewModel Notifications { get; }
    public VoiceViewModel Voice { get; }
    public MarketplaceViewModel Marketplace { get; }

    /// <summary>
    /// The in-world 3-D scene viewer ViewModel. Lazily created when the user first
    /// opens the Scene Viewer tab (see <see cref="OpenSceneViewer"/>) and disposed
    /// when the user closes the tab (see <see cref="CloseSceneViewer"/>), so the
    /// rendering pipeline only consumes CPU/GPU resources while it is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSceneViewerOpen))]
    private SceneViewerViewModel? _sceneViewer;

    public bool IsSceneViewerOpen => SceneViewer != null;

    /// <summary>
    /// True while a second, independent Scene Viewer instance is floating in its own window
    /// (see MainWindow.axaml.cs's OnUndockSceneViewerClick — detaching closes the docked
    /// instance and opens a fresh one in a floating window, rather than moving the live,
    /// GL-backed panel between windows, which Avalonia does not support safely). Used to
    /// disable "Show Scene Viewer" while one is already floating, and to know whether Low
    /// Memory Mode needs to ask MainWindow to close that floating instance too.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenSceneViewer))]
    private bool _isSceneViewerDetached;

    /// <summary>
    /// Raised when the floating Scene Viewer window must be closed (e.g. Low Memory Mode being
    /// enabled while it's detached). MainWindow's code-behind owns that floating window/VM,
    /// since they're never tracked by <see cref="SceneViewer"/>.
    /// </summary>
    public event EventHandler? ForceCloseFloatingSceneViewerRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenSceneViewer))]
    private bool _isLowMemoryModeEnabled;

    /// <summary>Whether "Show Scene Viewer" should be enabled — blocked by Low Memory Mode, and
    /// while a floating instance already exists (opening docked too would double the GPU/streaming cost).</summary>
    public bool CanOpenSceneViewer => !IsLowMemoryModeEnabled && !IsSceneViewerDetached;

    [ObservableProperty]
    private bool _isRegionTabOpen;

    [ObservableProperty]
    private bool _isAppearanceOpen;

    /// <summary>Tab index of the Region Performance tab (always present, hidden when closed).</summary>
    public const int RegionTabIndex = 7;

    /// <summary>Tab index of the Appearance tab (hidden when closed).</summary>
    public const int AppearanceTabIndex = 8;

    /// <summary>Tab index of the Scene Viewer tab when it is visible.</summary>
    public const int SceneViewerTabIndex = 9;

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(
        RadegastInstanceAvalonia instance,
        NearbyViewModel chat,
        IMViewModel im,
        MapViewModel map,
        ObjectsViewModel objects,
        InventoryViewModel inventory,
        FriendsViewModel friends,
        GroupsViewModel groups,
        RegionViewModel region,
        AppearanceViewModel appearance,
        MediaViewModel media,
        NotificationQueueViewModel notifications,
        VoiceViewModel voice,
        MarketplaceViewModel marketplace)
    {
        _instance = instance;

        Chat = chat;
        IM = im;
        Map = map;
        Objects = objects;
        Inventory = inventory;
        Friends = friends;
        Groups = groups;
        Region = region;
        Appearance = appearance;
        Media = media;
        Notifications = notifications;
        Voice = voice;
        Marketplace = marketplace;

        // Back-references used by other parts of the session.
        _instance.Voice = voice;
        _instance.Media = media;
        Chat.Voice = voice;
        Chat.Media = media;

        // Forward status from Chat VM
        Chat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NearbyViewModel.StatusText))
                StatusText = Chat.StatusText;
            if (e.PropertyName == nameof(NearbyViewModel.LocationText))
            {
                LocationText = Chat.LocationText;
                Title = $"Radegast Veles - {Chat.LocationText}";
            }
        };

        StatusText = $"Logged in as {_instance.Client.Self.Name}";
        BalanceText = $"L${_instance.Client.Self.Balance:N0}";
        _prevBalance = _instance.Client.Self.Balance;
        Chat.IsActive = true;

        IsLowMemoryModeEnabled = _instance.LowMemoryModeEnabled;
        _instance.LowMemoryModeChanged += Instance_LowMemoryModeChanged;

        _instance.Client.Self.MoneyBalance += Self_MoneyBalance;
        _instance.IMRequested += Instance_IMRequested;
        _instance.GroupIMRequested += Instance_GroupIMRequested;
        _instance.ShowOnMapRequested += Instance_ShowOnMapRequested;
        _instance.NotificationReceived += Instance_NotificationReceived;
        IM.NavigateToSessionRequested += OnIMNavigateRequested;

        // Auto-connect voice if the setting is enabled
        _ = Voice.TryAutoConnectAsync();
    }

    public void Dispose()
    {
        _instance.Client.Self.MoneyBalance -= Self_MoneyBalance;
        _instance.IMRequested -= Instance_IMRequested;
        _instance.GroupIMRequested -= Instance_GroupIMRequested;
        _instance.ShowOnMapRequested -= Instance_ShowOnMapRequested;
        _instance.NotificationReceived -= Instance_NotificationReceived;
        _instance.LowMemoryModeChanged -= Instance_LowMemoryModeChanged;
        IM.NavigateToSessionRequested -= OnIMNavigateRequested;

        Chat.Dispose();
        IM.Dispose();
        Map.Dispose();
        Objects.Dispose();
        Inventory.Dispose();
        Friends.Dispose();
        Groups.Dispose();
        Region.Dispose();
        Appearance.Dispose();
        Media.Dispose();
        Voice.Dispose();
        Marketplace.Dispose();
        SceneViewer?.Dispose();
    }

    private void Self_MoneyBalance(object? sender, BalanceEventArgs e)
    {
        // Play money sound when balance changes significantly (threshold: 5L$)
        if (_prevBalance >= 0 && Math.Abs(e.Balance - _prevBalance) >= 5)
        {
            var sound = e.Balance > _prevBalance ? UISounds.MoneyIn : UISounds.MoneyOut;
            _instance.MediaManager.PlayUISound(sound);
        }
        _prevBalance = e.Balance;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            BalanceText = $"L${e.Balance:N0}");
    }

    private void OnIMNavigateRequested(IMSession session)
    {
        IM.FocusSession(session);
        ShowTab(1);
    }

    private void Instance_IMRequested(object? sender, IMRequestedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IM.OpenIMSession(e.AgentId, e.AgentName);
            ShowTab(1);
        });
    }

    private void Instance_GroupIMRequested(object? sender, GroupIMRequestedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IM.OpenGroupIMSession(e.GroupId, e.GroupName);
            ShowTab(1);
        });
    }

    private void Instance_ShowOnMapRequested(object? sender, LibreMetaverse.UUID agentId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ShowTab(2);
            Map.ShowFriendOnMap(agentId);
        });
    }

    private void Instance_NotificationReceived(object? sender, NotificationViewModel e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Notifications.Add(e));
    }

    private void Instance_LowMemoryModeChanged(object? sender, bool enabled)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsLowMemoryModeEnabled = enabled;
            if (!enabled) return;

            // The docked instance (if any) is closed here directly; the floating instance
            // (if any) is owned entirely by MainWindow's code-behind, so ask it to close that
            // one too rather than reopening it docked.
            if (IsSceneViewerDetached)
                ForceCloseFloatingSceneViewerRequested?.Invoke(this, EventArgs.Empty);
            CloseSceneViewer();
        });
    }

    public void ShowTab(int index)
    {
        SelectedTabIndex = index;
    }

    /// <summary>
    /// Open (or activate) the Scene Viewer tab. The first call constructs the
    /// <see cref="SceneViewerViewModel"/>; subsequent calls just re-select the tab.
    /// </summary>
    public void OpenSceneViewer()
    {
        if (_instance.LowMemoryModeEnabled) return;
        if (SceneViewer == null)
        {
            var vm = new SceneViewerViewModel(_instance, Chat);
            vm.CloseRequested += (_, _) => CloseSceneViewer();
            SceneViewer = vm;
        }
        ShowTab(SceneViewerTabIndex);
    }

    /// <summary>
    /// Close the Scene Viewer tab. Disposes the ViewModel (which tears down the
    /// GL viewport, shaders, FBOs, and per-frame rendering) and switches back to
    /// the Nearby tab so the now-hidden tab is not selected.
    /// </summary>
    public void CloseSceneViewer()
    {
        if (SceneViewer == null) return;

        if (SelectedTabIndex == SceneViewerTabIndex)
            SelectedTabIndex = 0;

        var vm = SceneViewer;
        SceneViewer = null;
        vm.Dispose();
    }

    public void OpenRegionTab()
    {
        IsRegionTabOpen = true;
        ShowTab(RegionTabIndex);
    }

    public void CloseRegionTab()
    {
        if (SelectedTabIndex == RegionTabIndex)
            SelectedTabIndex = 0;
        IsRegionTabOpen = false;
    }

    public void OpenAppearance()
    {
        IsAppearanceOpen = true;
        ShowTab(AppearanceTabIndex);
    }

    public void CloseAppearance()
    {
        if (SelectedTabIndex == AppearanceTabIndex)
            SelectedTabIndex = 0;
        IsAppearanceOpen = false;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        Chat.IsActive = value == 0;
        IM.IsActive = value == 1;
        if (value == 0) Chat.ClearUnread();
        if (value == 1) IM.ClearUnread();
    }
}
