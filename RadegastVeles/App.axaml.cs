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

    // Avalonia.Native's macOS tray-menu exporter binds to the first NativeMenu instance it sees
    // for a TrayIcon and throws "The menu being updated does not match" from
    // __MicroComIAvnMenuProxy.Update if it is ever handed a different one -- unhandled, that
    // aborts the whole process. Since Avalonia 12.1 the native tray icon is created when the icon
    // is attached (during XAML load) and binds to an empty NativeMenu if none is set yet, so the
    // menu is declared in App.axaml and we mutate that same instance's Items from here on.
    private NativeMenu? _trayMenu;

    // The macOS Dock icon's menu. One instance, set once and then only its Items change, for
    // the same reason as _trayMenu. The Dock appends its own Quit item below these.
    private NativeMenu? _dockMenu;
    private bool _shuttingDown;
    private AppCommandChannel? _commandChannel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Radegast.Veles.Core.MacDockIcon.Apply();

            var icons = TrayIcon.GetIcons(this);
            if (icons?.Count > 0)
            {
                _trayIcon = icons[0];
                _trayIcon.Clicked += (_, _) => TrayClick();
            }

            if (OperatingSystem.IsMacOS())
            {
                _dockMenu = new NativeMenu();
                NativeDock.SetMenu(this, _dockMenu);
            }
            RebuildMenus();

            // A quit that comes from the system (the Dock's Quit item, Cmd+Q, logout) closes
            // every window; main windows otherwise hide instead of closing, which would cancel it.
            desktop.ShutdownRequested += (_, _) => CloseEverything();

            // Clicking the Dock icon while every window is hidden brings one back.
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e.Kind != ActivationKind.Reopen || _shuttingDown) return;
                    bool anyVisible = _loginWindow is { IsVisible: true }
                                      || _agentWindows.Values.Any(w => w.IsVisible);
                    if (!anyVisible) TrayClick();
                };
            }

            VelesUpdateManager.Start();

            _commandChannel = AppCommandChannel.TryListen(AppCommandChannel.DefaultEndpoint(),
                command => Avalonia.Threading.Dispatcher.UIThread.Post(() => RunCommand(command)));

            ShowLogin();
            if (Program.StartupCommand is { } startupCommand) RunCommand(startupCommand);
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

        RebuildMenus();
    }

    private void OnLogoutRequested(AgentSession session)
    {
        if (_agentWindows.TryGetValue(session.Id, out var window))
        {
            window.ForceClose();
            _agentWindows.Remove(session.Id);
        }

        _sessionManager.RemoveSession(session);
        RebuildMenus();

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

    private void RunCommand(AppCommand command)
    {
        if (_shuttingDown) return;

        switch (command.Kind)
        {
            case AppCommandKind.NewLogin:
                ShowLogin();
                break;
            case AppCommandKind.CheckForUpdates:
                VelesUpdateManager.CheckForUpdatesAtUserRequest();
                break;
            case AppCommandKind.Exit:
                Exit();
                break;
            case AppCommandKind.ShowSession:
                if (_agentWindows.TryGetValue(command.SessionId, out var window))
                {
                    window.Show();
                    window.Activate();
                }
                else
                {
                    TrayClick();
                }
                break;
            case AppCommandKind.LogoutSession:
                var session = _sessionManager.Sessions.FirstOrDefault(s => s.Id == command.SessionId);
                if (session != null) OnLogoutRequested(session);
                break;
        }
    }

    /// <summary>
    /// The tasks on the Windows taskbar button: the same entries as the tray menu. With
    /// <paramref name="includeSessions"/> false only the entries that make sense while the
    /// program is not running remain.
    /// </summary>
    private List<(string Title, AppCommand Command)?> BuildTaskList(bool includeSessions)
    {
        var tasks = new List<(string Title, AppCommand Command)?>();
        if (includeSessions)
        {
            foreach (var session in _sessionManager.Sessions)
            {
                tasks.Add(($"Show {session.AgentName}", new AppCommand(AppCommandKind.ShowSession, session.Id)));
                tasks.Add(($"Logout {session.AgentName}", new AppCommand(AppCommandKind.LogoutSession, session.Id)));
            }
            if (tasks.Count > 0) tasks.Add(null);
        }

        tasks.Add(("New Login", new AppCommand(AppCommandKind.NewLogin)));
        tasks.Add(("Check for Updates...", new AppCommand(AppCommandKind.CheckForUpdates)));
        if (includeSessions)
        {
            tasks.Add(null);
            tasks.Add(("Exit", new AppCommand(AppCommandKind.Exit)));
        }
        return tasks;
    }

    private void RebuildMenus()
    {
        if (_dockMenu != null) FillAppMenu(_dockMenu, includeExit: false);
        if (OperatingSystem.IsWindows()) WindowsJumpList.SetTasks(BuildTaskList(includeSessions: true));

        if (_trayIcon == null) return;

        // Fall back to a fresh menu only if the XAML-declared one is missing (pre-12.1 behaviour).
        var trayMenu = _trayMenu ??= _trayIcon.Menu ?? new NativeMenu();
        FillAppMenu(trayMenu, includeExit: true);

        if (!ReferenceEquals(_trayIcon.Menu, trayMenu))
            _trayIcon.Menu = trayMenu;
    }

    private void FillAppMenu(NativeMenu menu, bool includeExit)
    {
        menu.Items.Clear();

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

        var checkForUpdatesItem = new NativeMenuItem("Check for Updates...");
        checkForUpdatesItem.Click += (_, _) => VelesUpdateManager.CheckForUpdatesAtUserRequest();
        menu.Items.Add(checkForUpdatesItem);

        if (!includeExit) return;

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => Exit();
        menu.Items.Add(exitItem);
    }

    private void CloseEverything()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        foreach (var window in _agentWindows.Values.ToArray())
        {
            window.ForceClose();
        }
        _agentWindows.Clear();

        _loginWindow?.Close();
        _sessionManager.Dispose();

        _commandChannel?.Dispose();
        _commandChannel = null;
        // The task list outlives the process, so leave one that works without it.
        if (OperatingSystem.IsWindows()) WindowsJumpList.SetTasks(BuildTaskList(includeSessions: false));
    }

    private void Exit()
    {
        CloseEverything();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
