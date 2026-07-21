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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using LibreMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.Plugins;
using Radegast.Veles.PluginApi;
using Radegast.Veles.ViewModels;
namespace Radegast.Veles.Views;

public partial class MainWindow : Window
{
    private readonly AgentSession _session;
    private readonly MainViewModel _vm;
    private bool _forceClose;

    private readonly Dictionary<int, PanelHostWindow> _detachedPanels = new();
    private readonly Dictionary<int, string> _panelTitles = new()
    {
        [0] = "Chat", [1] = "IMs", [2] = "World Map", [3] = "Objects",
        [4] = "Inventory", [5] = "Friends", [6] = "Groups",
    };
    // Count of tabs that are always visible (Chat..Groups, indices 0-6), used for Ctrl+Tab
    // cycling — unlike Scene Viewer (and Region/Appearance), these are never conditionally hidden.
    private const int AlwaysVisibleTabCount = 7;
    // The Scene Viewer instance currently floating in its own window, if any — a completely
    // separate ViewModel/panel from the docked MainViewModel.SceneViewer (see
    // OnUndockSceneViewerClick for why detaching doesn't move the live GL-backed panel).
    private SceneViewerViewModel? _floatingSceneViewer;
    // Suppresses reopening the Scene Viewer docked when its floating window closes — set while
    // that close is driven by Low Memory Mode or app shutdown rather than the user's own
    // "⬅ Dock"/"X" action.
    private bool _sceneViewerForceClosing;
    private LogViewerWindow? _logViewerWindow;
    private Window? _marketplaceWindow;
    private ReconnectWindow? _reconnectWindow;
    private PluginManagerWindow? _pluginManagerWindow;

    public event EventHandler? LogoutRequested;

    public MainWindow() { InitializeComponent(); _session = null!; _vm = null!; }

    public MainWindow(AgentSession session)
    {
        _session = session;
        _vm = session.ViewModel;
        DataContext = _vm;
        InitializeComponent();

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        VelesNotificationService.Initialize(this);
        BadgeService.Initialize(this);
        _vm.IM.PropertyChanged += OnIMPropertyChanged;
        _session.Instance.NetCom.ClientDisconnected += OnClientDisconnected;

        _session.Instance.PluginManager.MenuItems.CollectionChanged       += OnPluginMenuItemsChanged;
        _session.Instance.PluginManager.PreferenceTabs.CollectionChanged   += OnPluginMenuItemsChanged;
        RebuildPluginMenuItems();

        KeyDownEvent.AddClassHandler<TopLevel>(OnGlobalKeyDown, handledEventsToo: false);
        KeyUpEvent.AddClassHandler<TopLevel>(OnGlobalKeyUp, handledEventsToo: false);

        _vm.ForceCloseFloatingSceneViewerRequested += OnForceCloseFloatingSceneViewerRequested;
    }

    // Low Memory Mode wants the floating Scene Viewer (if any) gone entirely, not reopened
    // docked — MainViewModel.CloseSceneViewer() only ever touches the docked instance, so it
    // can't reach this one (it's never assigned to MainViewModel.SceneViewer).
    private void OnForceCloseFloatingSceneViewerRequested(object? sender, EventArgs e)
    {
        if (!_detachedPanels.TryGetValue(MainViewModel.SceneViewerTabIndex, out var hostWindow)) return;
        _sceneViewerForceClosing = true;
        hostWindow.Close();
        _sceneViewerForceClosing = false;
    }

    private void OnIMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IMViewModel.UnreadConversationCount))
            BadgeService.Update(_vm.IM.UnreadConversationCount);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _session.Instance.PluginManager.MenuItems.CollectionChanged     -= OnPluginMenuItemsChanged;
        _session.Instance.PluginManager.PreferenceTabs.CollectionChanged -= OnPluginMenuItemsChanged;
        _session.Instance.NetCom.ClientDisconnected -= OnClientDisconnected;
        _vm.IM.PropertyChanged -= OnIMPropertyChanged;
        _vm.ForceCloseFloatingSceneViewerRequested -= OnForceCloseFloatingSceneViewerRequested;

        _pluginManagerWindow?.Close();
        _pluginManagerWindow = null;

        // Re-dock any detached panels so their controls are released properly. The app is
        // shutting down, so suppress Scene Viewer's usual "reopen docked" reaction to its
        // floating window closing.
        _sceneViewerForceClosing = true;
        foreach (var kvp in _detachedPanels)
        {
            kvp.Value.Close();
        }
        _detachedPanels.Clear();
        _floatingSceneViewer?.Dispose();
        _floatingSceneViewer = null;

        base.OnClosed(e);
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public void ShowTab(int index)
    {
        _vm.ShowTab(index);
        Show();
        Activate();
    }

    private void OnShowMyProfileClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowAgentProfile(
            _session.Instance.Client.Self.Name,
            _session.Instance.Client.Self.AgentID);

    private void OnChangeDisplayNameClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowChangeDisplayName();

    private void OnAwayClick(object? sender, RoutedEventArgs e)
    {
        var newState = !_session.Instance.State.IsAway;
        _session.Instance.State.SetAway(newState);
        _vm.IsAway = newState;
        VelesNotificationService.Show("Status",
            newState ? "You are now Away." : "You are no longer Away.");
    }

    private void OnBusyClick(object? sender, RoutedEventArgs e)
    {
        var newState = !_session.Instance.State.IsBusy;
        _session.Instance.State.SetBusy(newState);
        _vm.IsBusy = newState;
        VelesNotificationService.Show("Status",
            newState ? "You are now Busy." : "You are no longer Busy.");
    }

    private void OnShowSearchClick(object? sender, RoutedEventArgs e)
        => _session.Instance?.ShowDirectorySearch();

    private void OnShowNearbyClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(0);
    private void OnShowIMClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(1);
    private void OnShowMapClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(2);
    private void OnShowObjectsClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(3);
    private void OnShowInventoryClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(4);
    private void OnShowFriendsClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(5);
    private void OnShowGroupsClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(6);
    private void OnShowMarketplaceClick(object? sender, RoutedEventArgs e) => OpenOrActivateMarketplace();

    private void OnShowSceneViewerClick(object? sender, RoutedEventArgs e) => _vm.OpenSceneViewer();

    private void OnCloseSceneViewerClick(object? sender, RoutedEventArgs e) => _vm.CloseSceneViewer();

    private void OnShowRegionClick(object? sender, RoutedEventArgs e) => _vm.OpenRegionTab();

    private void OnCloseRegionClick(object? sender, RoutedEventArgs e) => _vm.CloseRegionTab();

    private void OnShowAppearanceClick(object? sender, RoutedEventArgs e) => _vm.OpenAppearance();

    private void OnCloseAppearanceClick(object? sender, RoutedEventArgs e) => _vm.CloseAppearance();

    public void OpenOrActivateMarketplace()
    {
        if (_marketplaceWindow == null)
        {
            _marketplaceWindow = new MarketplaceWindow(_vm.Marketplace);
            _marketplaceWindow.Closed += (_, _) => _marketplaceWindow = null;
            _marketplaceWindow.Show(this);
        }
        else
        {
            _marketplaceWindow.Activate();
        }
    }

    private void OnUploadImageClick(object? sender, RoutedEventArgs e)
    {
        var vm = new UploadImageViewModel(_session.Instance);
        var window = new UploadImageWindow(vm);
        window.Show();
    }

    private void OnUploadMeshClick(object? sender, RoutedEventArgs e)
    {
        var vm = new UploadMeshViewModel(_session.Instance);
        var window = new UploadMeshWindow(vm);
        window.Show();
    }

    private void OnUploadSoundClick(object? sender, RoutedEventArgs e)
    {
        var vm = new UploadSoundViewModel(_session.Instance);
        var window = new UploadSoundWindow(vm);
        window.Show();
    }

    private void OnUploadAnimationClick(object? sender, RoutedEventArgs e)
    {
        var vm = new UploadAnimationViewModel(_session.Instance);
        var window = new UploadAnimationWindow(vm);
        window.Show();
    }

    private void OnCreateNotecardClick(object? sender, RoutedEventArgs e)
        => _session.Instance.CreateAndOpenNotecard();

    private void OnCreateScriptClick(object? sender, RoutedEventArgs e)
        => _session.Instance.CreateAndOpenScript();

    private void OnCreateGestureClick(object? sender, RoutedEventArgs e)
        => _session.Instance.CreateAndOpenGesture();

    private void OnCreateLandmarkClick(object? sender, RoutedEventArgs e)
        => _session.Instance.CreateAndOpenLandmark();

    private void RebuildPluginMenuItems()
    {
        var menu = this.FindControl<MenuItem>("PluginsMenu");
        if (menu == null) return;

        // Remove all dynamic entries (everything after the static Plugin Manager item)
        while (menu.Items.Count > 1)
            menu.Items.RemoveAt(menu.Items.Count - 1);

        var pluginItems = _session.Instance.PluginManager.MenuItems;
        if (pluginItems.Count == 0) return;

        // Group items by plugin — each plugin gets its own submenu
        var byPlugin = pluginItems
            .GroupBy(item => string.IsNullOrEmpty(item.PluginId) ? item.PluginName : item.PluginId)
            .OrderBy(g => g.First().PluginName);

        menu.Items.Add(new Separator());

        foreach (var group in byPlugin)
        {
            string pluginName = group.First().PluginName;
            string pluginId   = group.First().PluginId;

            var sub = new MenuItem { Header = pluginName };

            // Settings entry first — only shown if the plugin registered at least one preference tab
            var tabs = _session.Instance.PluginManager.GetPreferenceTabsForPlugin(pluginId);
            if (tabs.Count > 0)
            {
                var capturedTabs = tabs;
                var capturedName = pluginName;
                var settingsMi = new MenuItem { Header = "Settings…" };
                settingsMi.Click += (_, _) => OpenPluginSettings(capturedName, capturedTabs);
                sub.Items.Add(settingsMi);
                sub.Items.Add(new Separator());
            }

            // Action items below
            foreach (var info in group)
            {
                var mi = new MenuItem { Header = info.Header };
                var captured = info;
                mi.Click += (_, _) => captured.OnClick?.Invoke();
                sub.Items.Add(mi);
            }

            menu.Items.Add(sub);
        }
    }

    private void OpenPluginSettings(string pluginName, IReadOnlyList<PluginPreferenceTab> tabs)
    {
        var win = new PluginSettingsWindow(pluginName, tabs);
        win.Show(this);
    }

    private void OnPluginMenuItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            RebuildPluginMenuItems();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(RebuildPluginMenuItems);
    }

    private void OnPluginManagerClick(object? sender, RoutedEventArgs e)
    {
        if (_pluginManagerWindow is { IsVisible: true })
        {
            _pluginManagerWindow.Activate();
            return;
        }

        var vm = new PluginManagerViewModel(_session.Instance.PluginManager);
        _pluginManagerWindow = new PluginManagerWindow(vm);
        _pluginManagerWindow.Closed += (_, _) =>
        {
            vm.Dispose();
            _pluginManagerWindow = null;
        };
        _pluginManagerWindow.Show();
    }

    private async void OnPreferencesClick(object? sender, RoutedEventArgs e)
    {
        var vm = new PreferencesViewModel(_session.Instance, _vm.Media, _vm.Voice);
        var window = new PreferencesWindow { DataContext = vm };
        await window.ShowDialog(this);
    }

    private void OnPttPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (sender is Control ctrl) e.Pointer.Capture(ctrl);
        _vm.Voice.StartPushToTalk();
        e.Handled = true;
    }

    private void OnPttPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        _vm.Voice.StopPushToTalk();
        e.Handled = true;
    }

    private void OnGlobalKeyDown(TopLevel sender, KeyEventArgs e)
    {
        // PTT hotkey — skip when typing
        if (_vm.Voice.PushToTalkButtonVisible &&
            e.Key == _vm.Voice.PttKey &&
            FocusManager?.GetFocusedElement() is not TextBox)
        {
            _vm.Voice.StartPushToTalk();
            e.Handled = true;
            return;
        }

        var mod = e.KeyModifiers;
        var key = e.Key;

        // Ctrl+Tab / Ctrl+Shift+Tab — cycle tabs
        if (key == Key.Tab && mod == KeyModifiers.Control)
        {
            var count = AlwaysVisibleTabCount;
            _vm.ShowTab((_vm.SelectedTabIndex + 1) % count);
            e.Handled = true;
            return;
        }
        if (key == Key.Tab && mod == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            var count = AlwaysVisibleTabCount;
            _vm.ShowTab((_vm.SelectedTabIndex - 1 + count) % count);
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+H — Teleport Home
        if (key == Key.H && mod == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            TeleportHome();
            e.Handled = true;
            return;
        }

        // Ctrl+Alt+D — Debug / Log Viewer
        if (key == Key.D && mod == (KeyModifiers.Control | KeyModifiers.Alt))
        {
            OpenLogViewer();
            e.Handled = true;
        }
    }

    private void OnGlobalKeyUp(TopLevel sender, KeyEventArgs e)
    {
        if (e.Key != _vm.Voice.PttKey) return;
        _vm.Voice.StopPushToTalk();
        e.Handled = true;
    }

    private void OnShowLandInfoClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowLandProfile();

    private void OnShowLandHoldingsClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowLandHoldings();

    private void OnShowMuteListClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowMuteList();

    private void OnShowEstateInfoClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowEstateProfile();

    private void OnShowHudViewerClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowHudViewer();

    private void OnShowMyAvatarViewerClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowAvatarViewer(
            _session.Instance.Client.Self.AgentID,
            _session.Instance.Client.Self.Name);

    private void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHideClick(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnClientDisconnected(object? sender, DisconnectedEventArgs e)
    {
        if (_forceClose) return;
        if (e.Reason == NetworkManager.DisconnectType.ClientInitiated) return;

        // Show the reconnect window if not already visible.
        if (_reconnectWindow is { IsVisible: true }) return;

        var vm = new ReconnectViewModel(_session.Instance, e.Message);
        _reconnectWindow = new ReconnectWindow(vm);
        _reconnectWindow.Closed += (_, _) => _reconnectWindow = null;
        _reconnectWindow.ReconnectSucceeded += (_, _) => { Show(); Activate(); };
        _reconnectWindow.ReturnToLoginRequested += (_, _) => LogoutRequested?.Invoke(this, EventArgs.Empty);
        _reconnectWindow.Show();
    }

    private KeyboardShortcutsWindow? _keyboardShortcutsWindow;

    private void OnKeyboardShortcutsClick(object? sender, RoutedEventArgs e)
    {
        if (_keyboardShortcutsWindow is { IsVisible: true })
        {
            _keyboardShortcutsWindow.Activate();
            return;
        }

        var vm = new KeyboardShortcutsViewModel(_session.Instance.CommandsManager);
        _keyboardShortcutsWindow = new KeyboardShortcutsWindow { DataContext = vm };
        _keyboardShortcutsWindow.Closed += (_, _) => _keyboardShortcutsWindow = null;
        _keyboardShortcutsWindow.Show();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        // Signal the application to shut down entirely, not just close this window.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            ForceClose();
        }
    }

    private void OnLogViewerClick(object? sender, RoutedEventArgs e) => OpenLogViewer();

    private void OpenLogViewer()
    {
        if (_logViewerWindow is { IsVisible: true })
        {
            _logViewerWindow.Activate();
            return;
        }

        _logViewerWindow = new LogViewerWindow();
        _logViewerWindow.Closed += (_, _) => _logViewerWindow = null;
        _logViewerWindow.Show();
    }

    private void OnTeleportHomeClick(object? sender, RoutedEventArgs e) => TeleportHome();

    private void TeleportHome()
    {
        _session.Instance.Client.Self.RequestTeleport(LibreMetaverse.UUID.Zero);
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        new AboutWindow().ShowDialog(this);
    }

    private void OnWebsiteClick(object? sender, RoutedEventArgs e) =>
        AboutWindow.OpenUrl("https://radegast.life/");

    private void OnIssueTrackerClick(object? sender, RoutedEventArgs e) =>
        AboutWindow.OpenUrl("https://github.com/cinderblocks/radegast/issues");

    #region Panel Docking / Undocking

    private void OnUndockChatClick(object? sender, RoutedEventArgs e) => UndockPanel(0);
    private void OnUndockIMClick(object? sender, RoutedEventArgs e) => UndockPanel(1);
    private void OnUndockMapClick(object? sender, RoutedEventArgs e) => UndockPanel(2);
    private void OnUndockObjectsClick(object? sender, RoutedEventArgs e) => UndockPanel(3);
    private void OnUndockInventoryClick(object? sender, RoutedEventArgs e) => UndockPanel(4);
    private void OnUndockFriendsClick(object? sender, RoutedEventArgs e) => UndockPanel(5);
    private void OnUndockGroupsClick(object? sender, RoutedEventArgs e) => UndockPanel(6);

    // Scene Viewer does NOT use the generic UndockPanel/DockPanel mechanism below. That
    // mechanism moves the tab's already-live Control into PanelHostWindow, which works fine
    // for plain UI panels but not for Scene Viewer: its tab content is a ContentControl bound
    // via "{Binding SceneViewer}", relying on an inherited DataContext that resolves to null
    // the moment the control is reparented (briefly parentless mid-move, then under
    // PanelHostWindow, which never sets a DataContext of its own) — silently collapsing
    // Content to null and tearing down the templated SceneViewerPanel/GlViewportControl
    // entirely. So instead: detaching closes the docked instance and opens a brand new one
    // hosted directly in its own window, the same pattern already used for
    // PrimViewer/AvatarViewer (RadegastInstanceAvalonia.ShowPrimViewer/ShowAvatarViewer).
    private void OnUndockSceneViewerClick(object? sender, RoutedEventArgs e)
    {
        if (_detachedPanels.ContainsKey(MainViewModel.SceneViewerTabIndex)) return;

        _vm.CloseSceneViewer();

        _floatingSceneViewer = new SceneViewerViewModel(_session.Instance, _vm.Chat);
        var panel = new SceneViewerPanel { DataContext = _floatingSceneViewer };

        var hostWindow = new PanelHostWindow
        {
            Title = $"Radegast Veles - Scene Viewer - {_session.AgentName}"
        };
        hostWindow.DockRequested += OnSceneViewerHostDockRequested;
        _detachedPanels[MainViewModel.SceneViewerTabIndex] = hostWindow;
        _vm.IsSceneViewerDetached = true;

        hostWindow.Show();
        hostWindow.SetPanel(panel);
    }

    // Fires whether the user clicked "⬅ Dock" or the window's native close button — both mean
    // "I'm done with the floating Scene Viewer." Unless a forced close is already in progress
    // (Low Memory Mode / app shutdown), that's answered by reopening a fresh docked instance.
    private void OnSceneViewerHostDockRequested(object? sender, EventArgs e)
    {
        if (sender is PanelHostWindow hostWindow)
            hostWindow.DockRequested -= OnSceneViewerHostDockRequested;

        _floatingSceneViewer?.Dispose();
        _floatingSceneViewer = null;
        _detachedPanels.Remove(MainViewModel.SceneViewerTabIndex);
        _vm.IsSceneViewerDetached = false;

        if (!_sceneViewerForceClosing)
            _vm.OpenSceneViewer();
    }

    private void UndockPanel(int tabIndex)
    {
        if (_detachedPanels.ContainsKey(tabIndex)) return;

        var tabControl = this.FindControl<TabControl>("MainTabControl");

        var tabItem = tabControl?.Items[tabIndex] as TabItem;

        var panel = tabItem?.Content as Control;
        if (panel == null) return;

        // Replace with placeholder
        tabItem?.Content = new TextBlock
        {
            Text = $"{_panelTitles[tabIndex]} (Detached)",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontStyle = Avalonia.Media.FontStyle.Italic,
            Opacity = 0.6
        };

        var hostWindow = new PanelHostWindow
        {
            Title = $"Radegast Veles - {_panelTitles[tabIndex]} - {_session.AgentName}"
        };

        var capturedIndex = tabIndex;
        hostWindow.DockRequested += (_, _) => DockPanel(capturedIndex, hostWindow);
        _detachedPanels[tabIndex] = hostWindow;

        hostWindow.Show();
        hostWindow.SetPanel(panel);
    }

    private void DockPanel(int tabIndex, PanelHostWindow hostWindow)
    {
        var panel = hostWindow.RemovePanel();
        if (panel == null) return;

        var tabControl = this.FindControl<TabControl>("MainTabControl");
        var tabItem = tabControl?.Items[tabIndex] as TabItem;
        tabItem?.Content = panel;

        _detachedPanels.Remove(tabIndex);
    }

    #endregion
}
