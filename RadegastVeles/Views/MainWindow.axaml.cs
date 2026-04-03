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
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;
namespace Radegast.Veles.Views;

public partial class MainWindow : Window
{
    private readonly AgentSession _session;
    private readonly MainViewModel _vm;
    private bool _forceClose;

    private readonly Dictionary<int, PanelHostWindow> _detachedPanels = new();
    private readonly string[] _panelTitles = ["Chat", "IMs", "World Map", "Objects", "Inventory", "Friends", "Groups", "Media"];
    private LogViewerWindow? _logViewerWindow;

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
        _vm.IM.PropertyChanged -= OnIMPropertyChanged;

        // Re-dock any detached panels so their controls are released properly
        foreach (var kvp in _detachedPanels)
        {
            kvp.Value.Close();
        }
        _detachedPanels.Clear();

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

    private void OnShowMyAvatarViewerClick(object? sender, RoutedEventArgs e)
        => _session.Instance.ShowAvatarViewer(
            _session.Instance.Client.Self.AgentID,
            _session.Instance.Client.Self.Name);

    private void OnShowSearchClick(object? sender, RoutedEventArgs e)
        => _session.Instance?.ShowDirectorySearch();

    private void OnShowNearbyClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(0);
    private void OnShowIMClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(1);
    private void OnShowMapClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(2);
    private void OnShowObjectsClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(3);
    private void OnShowInventoryClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(4);
    private void OnShowFriendsClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(5);
    private void OnShowGroupsClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(6);
    private void OnShowMediaClick(object? sender, RoutedEventArgs e) => _vm.ShowTab(7);

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

    private void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHideClick(object? sender, RoutedEventArgs e)
    {
        Hide();
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

    private void OnLogViewerClick(object? sender, RoutedEventArgs e)
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
    private void OnUndockMediaClick(object? sender, RoutedEventArgs e) => UndockPanel(7);

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
        hostWindow.SetPanel(panel);

        var capturedIndex = tabIndex;
        hostWindow.DockRequested += (_, _) => DockPanel(capturedIndex, hostWindow);
        _detachedPanels[tabIndex] = hostWindow;
        hostWindow.Show();
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
