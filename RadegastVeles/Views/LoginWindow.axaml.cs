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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;
    private LogViewerWindow? _logViewerWindow;

    public event EventHandler<AgentLoginSucceededEventArgs>? LoginSucceeded;

    public LoginWindow() { InitializeComponent(); _vm = null!; }

    public LoginWindow(CredentialManager credentialManager)
    {
        InitializeComponent();
        _vm = new LoginViewModel(credentialManager);
        _vm.LoginSucceeded += OnLoginSucceeded;
        DataContext = _vm;
    }

    private void OnLoginSucceeded(object? sender, AgentLoginSucceededEventArgs e)
    {
        LoginSucceeded?.Invoke(this, e);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.DisposeInstance();
        base.OnClosed(e);
    }

    private async void OnPreferencesClick(object? sender, RoutedEventArgs e)
    {
        var instance = _vm.GetInstanceForPreferences();
        if (instance == null) return;

        var media = new MediaViewModel(instance);
        var vm = new PreferencesViewModel(instance, media);
        var window = new PreferencesWindow { DataContext = vm };
        await window.ShowDialog(this);
        media.Dispose();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
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
        var about = new AboutWindow();
        about.ShowDialog(this);
    }

    private void OnWebsiteClick(object? sender, RoutedEventArgs e) =>
        AboutWindow.OpenUrl("https://radegast.life/");

    private void OnIssueTrackerClick(object? sender, RoutedEventArgs e) =>
        AboutWindow.OpenUrl("https://github.com/cinderblocks/radegast/issues");
}
