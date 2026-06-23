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
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;
using Radegast.Veles.Views;

namespace Radegast.Veles;

public class App : Application
{
    private readonly CredentialManager _credentialManager = new();
    private readonly AgentSessionManager _sessionManager = new();
    private readonly Dictionary<Guid, MainWindow> _agentWindows = new();
    private LoginWindow? _loginWindow;
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var icons = TrayIcon.GetIcons(this);
            if (icons?.Count > 0)
            {
                _trayIcon = icons[0];
                _trayIcon.Clicked += (_, _) => TrayClick();
                RebuildTrayMenu();
            }

            ShowLogin();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowLogin()
    {
        if (_loginWindow is { IsVisible: true })
        {
            _loginWindow.Activate();
            return;
        }

        _loginWindow = new LoginWindow(_credentialManager);
        _loginWindow.LoginSucceeded += OnLoginSucceeded;
        _loginWindow.Show();
    }

    private void OnLoginSucceeded(object? sender, AgentLoginSucceededEventArgs e)
    {
        _loginWindow = null;

        e.Instance.CredentialManager = _credentialManager;
        e.Instance.AccountKey = $"{e.Username.ToLowerInvariant()}:{e.GridId}";

        var session = _sessionManager.AddSession(e.Instance);
        var mainWindow = new MainWindow(session);

        var capturedSession = session;
        mainWindow.LogoutRequested += (_, _) => OnLogoutRequested(capturedSession);

        _agentWindows[session.Id] = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();

        RebuildTrayMenu();
    }

    private void OnLogoutRequested(AgentSession session)
    {
        if (_agentWindows.TryGetValue(session.Id, out var window))
        {
            window.ForceClose();
            _agentWindows.Remove(session.Id);
        }

        _sessionManager.RemoveSession(session);
        RebuildTrayMenu();

        if (_sessionManager.Sessions.Count == 0)
        {
            ShowLogin();
        }
    }

    private void TrayClick()
    {
        if (_agentWindows.Count > 0)
        {
            var lastWindow = _agentWindows.Values.Last();
            lastWindow.Show();
            lastWindow.Activate();
        }
        else
        {
            ShowLogin();
        }
    }

    private void RebuildTrayMenu()
    {
        if (_trayIcon == null) return;

        var menu = new NativeMenu();

        foreach (var session in _sessionManager.Sessions)
        {
            var agentName = session.AgentName;
            var capturedSession = session;

            var showItem = new NativeMenuItem($"Show {agentName}");
            showItem.Click += (_, _) =>
            {
                if (_agentWindows.TryGetValue(capturedSession.Id, out var w))
                {
                    w.Show();
                    w.Activate();
                }
            };
            menu.Items.Add(showItem);

            var logoutItem = new NativeMenuItem($"Logout {agentName}");
            logoutItem.Click += (_, _) => OnLogoutRequested(capturedSession);
            menu.Items.Add(logoutItem);

            menu.Items.Add(new NativeMenuItemSeparator());
        }

        var loginItem = new NativeMenuItem("New Login");
        loginItem.Click += (_, _) => ShowLogin();
        menu.Items.Add(loginItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => Exit();
        menu.Items.Add(exitItem);

        _trayIcon.Menu = menu;
    }

    private void Exit()
    {
        foreach (var window in _agentWindows.Values.ToArray())
        {
            window.ForceClose();
        }
        _agentWindows.Clear();

        _loginWindow?.Close();
        _sessionManager.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
