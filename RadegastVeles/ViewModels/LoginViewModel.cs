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
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.Models;
using Radegast.Veles.Views;

namespace Radegast.Veles.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private RadegastInstanceAvalonia? _instance;
    private readonly CredentialManager _credentialManager;

    public event EventHandler<AgentLoginSucceededEventArgs>? LoginSucceeded;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private Grid? _selectedGrid;

    [ObservableProperty]
    private string _customLoginUri = string.Empty;

    [ObservableProperty]
    private int _selectedLocationIndex;

    [ObservableProperty]
    private string _customLocation = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private bool _isMfaRequired;

    [ObservableProperty]
    private string _mfaToken = string.Empty;

    [ObservableProperty]
    private bool _isCustomGrid;

    [ObservableProperty]
    private bool _isCustomLocation;

    [ObservableProperty]
    private bool _rememberCredentials;

    [ObservableProperty]
    private SavedAccount? _selectedSavedAccount;

    [ObservableProperty]
    private LoginLocationItem? _selectedLocationItem;

    // --- Sidebar info panel (grid status / version / news) ---------------------------

    [ObservableProperty]
    private string _currentVersionText = AppVersionInfo.CurrentVersionString;

    [ObservableProperty]
    private string? _latestVersionText;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    [ObservableProperty]
    private string? _gridStatusText;

    [ObservableProperty]
    private bool _hasGridStatus;

    [ObservableProperty]
    private IBrush _gridStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private bool _hasRecentIncidents;

    [ObservableProperty]
    private string? _latestNewsTitle;

    [ObservableProperty]
    private string? _latestNewsUrl;

    [ObservableProperty]
    private bool _hasNews;

    public ObservableCollection<IncidentSummary> RecentIncidents { get; } = new();

    public ObservableCollection<Grid> Grids { get; } = new();
    public ObservableCollection<SavedAccount> SavedAccounts { get; } = new();
    public ObservableCollection<LoginLocationItem> LocationItems { get; } = new();

    partial void OnSelectedLocationItemChanged(LoginLocationItem? value)
    {
        if (value == null) return;
        if (value.Kind == LoginLocationKind.Favorite && value.Location != null)
            CustomLocation = value.Location;
        IsCustomLocation = value.Kind == LoginLocationKind.Custom;
    }

    public LoginViewModel(CredentialManager credentialManager)
    {
        _credentialManager = credentialManager;

        EnsureInstance();

        // Load saved accounts
        foreach (var account in _credentialManager.GetSavedAccounts())
        {
            SavedAccounts.Add(account);
        }

        // Pre-populate with the last-used account (triggers OnSelectedSavedAccountChanged
        // which rebuilds LocationItems with that account's favorites)
        if (SavedAccounts.Count > 0)
        {
            SelectedSavedAccount = SavedAccounts[0];
        }
        else
        {
            RebuildLocationItems([]);
        }

        // Restore previously saved start-location selection
        var (savedIdx, savedCustom) = _credentialManager.LoadLoginPreferences();
        RestoreLocationPreference(savedIdx, savedCustom);

        // Fire-and-forget: populates the sidebar info panel. Each fetch is independent
        // and marshals its own result back to the UI thread, so a slow/failed one never
        // delays the others or the login form itself.
        _ = LoadVersionInfoAsync();
        _ = LoadGridStatusAsync();
        _ = LoadNewsAsync();
    }

    private async Task LoadVersionInfoAsync()
    {
        var latest = await VelesUpdateManager.GetLatestAvailableVersionAsync();
        // No newer version known (either up to date, or the check failed/is unsupported
        // on this platform) - show current as both, rather than hiding the line.
        var hasUpdate = !string.IsNullOrEmpty(latest) &&
                         Version.TryParse(latest, out var latestVer) &&
                         AppVersionInfo.Current != null &&
                         latestVer > AppVersionInfo.Current;

        Dispatcher.UIThread.Post(() =>
        {
            HasUpdateAvailable = hasUpdate;
            LatestVersionText = hasUpdate ? $"v{latest}" : "Up to date";
        });
    }

    private async Task LoadGridStatusAsync()
    {
        var status = await GridStatusService.GetStatusAsync();
        if (status == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var active = status.ActiveIncident;
            GridStatusText = active != null
                ? $"{active.Name}"
                : status.Description;
            HasGridStatus = true;
            GridStatusBrush = status.Indicator switch
            {
                GridStatusIndicator.None => Brushes.MediumSeaGreen,
                GridStatusIndicator.Minor => Brushes.Goldenrod,
                GridStatusIndicator.Major => Brushes.OrangeRed,
                GridStatusIndicator.Critical => Brushes.Crimson,
                GridStatusIndicator.Maintenance => Brushes.SteelBlue,
                _ => Brushes.Gray
            };

            RecentIncidents.Clear();
            foreach (var incident in status.Recent)
                RecentIncidents.Add(incident);
            HasRecentIncidents = RecentIncidents.Count > 0;
        });
    }

    private async Task LoadNewsAsync()
    {
        var items = await RadegastNewsService.GetLatestAsync(1);
        var item = items.FirstOrDefault();
        if (item == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            LatestNewsTitle = item.Title;
            LatestNewsUrl = item.Link;
            HasNews = true;
        });
    }

    [RelayCommand]
    private void OpenNews()
    {
        if (!string.IsNullOrEmpty(LatestNewsUrl))
            AboutWindow.OpenUrl(LatestNewsUrl);
    }

    [RelayCommand]
    private void OpenGridStatusPage()
    {
        AboutWindow.OpenUrl("https://status.secondlifegrid.net/");
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        AboutWindow.OpenUrl("https://radegast.life/");
    }

    private void EnsureInstance()
    {
        if (_instance != null) return;

        _instance = new RadegastInstanceAvalonia("RadegastVeles", new GridClient());

        Grids.Clear();
        foreach (var grid in _instance.GridManger.Grids)
        {
            Grids.Add(grid);
        }
        Grids.Add(new Grid("custom", "Custom", ""));

        if (Grids.Count > 0)
        {
            SelectedGrid = Grids[0];
        }

        _instance.NetCom.ClientLoginStatus += NetCom_ClientLoginStatus;
        _instance.NetCom.ClientLoggedOut += NetCom_ClientLoggedOut;
        _instance.NetCom.ClientDisconnected += NetCom_ClientDisconnected;
    }

    partial void OnSelectedGridChanged(Grid? value)
    {
        IsCustomGrid = value?.ID == "custom";
    }

    partial void OnSelectedSavedAccountChanged(SavedAccount? value)
    {
        if (value == null) return;

        Username = value.Username;

        var grid = Grids.FirstOrDefault(g => g.ID == value.GridId);
        if (grid != null) SelectedGrid = grid;

        var pwd = _credentialManager.GetPassword(value.Username, value.GridId);
        if (pwd != null) Password = pwd;

        RememberCredentials = true;
        IsMfaRequired = false;
        MfaToken = string.Empty;

        // Rebuild location items with this account's saved favorites
        var accountKey = $"{value.Username.ToLowerInvariant()}:{value.GridId}";
        var favs = _credentialManager.GetFavoriteLocations(accountKey);
        var currentItem = SelectedLocationItem;
        RebuildLocationItems(favs);

        // Preserve selection kind across account switches
        if (currentItem != null)
        {
            SelectedLocationItem = currentItem.Kind switch
            {
                LoginLocationKind.Home => LocationItems[0],
                LoginLocationKind.Last => LocationItems[1],
                LoginLocationKind.Favorite => LocationItems.FirstOrDefault(
                    i => i.Kind == LoginLocationKind.Favorite && i.Location == currentItem.Location)
                    ?? LocationItems[1],
                _ => LocationItems[^1]
            };
        }
    }

    [RelayCommand]
    private void RemoveSavedAccount()
    {
        if (SelectedSavedAccount == null) return;

        _credentialManager.RemoveAccount(SelectedSavedAccount.Username, SelectedSavedAccount.GridId);
        SavedAccounts.Remove(SelectedSavedAccount);
        SelectedSavedAccount = null;
    }

    private void RebuildLocationItems(List<(string Name, string Location)> favorites)
    {
        LocationItems.Clear();
        LocationItems.Add(new LoginLocationItem("Home", LoginLocationKind.Home));
        LocationItems.Add(new LoginLocationItem("Last Location", LoginLocationKind.Last));
        foreach (var (name, location) in favorites)
            LocationItems.Add(new LoginLocationItem(name, LoginLocationKind.Favorite, location));
        LocationItems.Add(new LoginLocationItem("Custom\u2026", LoginLocationKind.Custom));
        SelectedLocationItem = LocationItems[1]; // default to Last Location
    }

    private void RestoreLocationPreference(int savedIdx, string savedCustom)
    {
        if (savedIdx == 0)
        {
            SelectedLocationItem = LocationItems[0];
        }
        else if (savedIdx == 1)
        {
            SelectedLocationItem = LocationItems[1];
        }
        else
        {
            var fav = LocationItems.FirstOrDefault(
                i => i.Kind == LoginLocationKind.Favorite && i.Location == savedCustom);
            if (fav != null)
            {
                SelectedLocationItem = fav;
            }
            else
            {
                SelectedLocationItem = LocationItems[^1];
                CustomLocation = savedCustom;
            }
        }
    }

    [RelayCommand]
    private void Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            StatusText = "Please enter username and password.";
            return;
        }

        EnsureInstance();

        var netCom = _instance!.NetCom;

        // MFA challenge was issued — retry with only the token; hash already set by LoginResponseCallback.
        if (IsMfaRequired)
        {
            if (string.IsNullOrWhiteSpace(MfaToken))
            {
                StatusText = "Please enter your authenticator code.";
                return;
            }
            netCom.LoginOptions.MfaToken = MfaToken;
            IsLoggingIn = true;
            StatusText = "Verifying authenticator code...";
            netCom.Login();
            return;
        }

        string[] parts = Regex.Split(Username.Trim(), @"[. ]+");
        if (parts.Length >= 2)
        {
            netCom.LoginOptions.FirstName = parts[0];
            netCom.LoginOptions.LastName = parts[1];
        }
        else
        {
            netCom.LoginOptions.FirstName = Username.Trim();
            netCom.LoginOptions.LastName = "Resident";
        }

        netCom.LoginOptions.Password = Password;
        netCom.LoginOptions.Channel = "Radegast Veles";
        netCom.LoginOptions.Version = "Radegast Veles 0.1";
        netCom.AgreeToTos = true;

        switch (SelectedLocationItem?.Kind)
        {
            case LoginLocationKind.Home:
                netCom.LoginOptions.StartLocation = StartLocationType.Home;
                break;
            case LoginLocationKind.Last:
                netCom.LoginOptions.StartLocation = StartLocationType.Last;
                break;
            default:
                netCom.LoginOptions.StartLocation = StartLocationType.Custom;
                netCom.LoginOptions.StartLocationCustom = CustomLocation;
                break;
        }

        if (SelectedGrid?.ID == "custom")
        {
            if (string.IsNullOrWhiteSpace(CustomLoginUri))
            {
                StatusText = "Please specify a login URI for the custom grid.";
                return;
            }
            netCom.LoginOptions.Grid = new Grid("custom", "Custom", CustomLoginUri.Trim());
            netCom.LoginOptions.GridCustomLoginUri = CustomLoginUri.Trim();
        }
        else
        {
            netCom.LoginOptions.Grid = SelectedGrid;
        }

        IsLoggingIn = true;
        StatusText = "Logging in...";
        var prefIdx = SelectedLocationItem?.Kind switch
        {
            LoginLocationKind.Home => 0,
            LoginLocationKind.Last => 1,
            _ => 2
        };
        _credentialManager.SaveLoginPreferences(prefIdx, CustomLocation);

        // Load saved MFA hash for silent MFA (empty token = use cached hash only).
        var savedGridId = SelectedGrid?.ID == "custom" ? "custom" : SelectedGrid?.ID ?? string.Empty;
        netCom.LoginOptions.MfaHash = _credentialManager.GetMfaHash(Username.Trim(), savedGridId) ?? string.Empty;
        netCom.LoginOptions.MfaToken = string.Empty;

        netCom.Login();
    }

    [RelayCommand]
    private void CancelLogin()
    {
        if (_instance != null && _instance.NetCom.IsLoggingIn)
        {
            _instance.NetCom.CancelLogin();
        }
        IsLoggingIn = false;
        IsMfaRequired = false;
        MfaToken = string.Empty;
        StatusText = "Login cancelled.";
    }

    private void NetCom_ClientLoginStatus(object? sender, LoginProgressEventArgs e)
    {
        // MFA challenge: server wants a TOTP token, hash already updated in LoginOptions by NetComAvalonia.
        if (e.FailReason == "mfa_challenge")
        {
            IsMfaRequired = true;
            IsLoggingIn = false;
            MfaToken = string.Empty;
            StatusText = "Enter your authenticator code.";
            return;
        }

        StatusText = e.Status switch
        {
            LoginStatus.Success => "Login successful!",
            LoginStatus.Failed => $"Login failed: {e.Message}",
            _ => e.Message
        };

        if (e.Status == LoginStatus.Success)
        {
            IsLoggingIn = false;
            IsMfaRequired = false;

            if (RememberCredentials && SelectedGrid != null)
            {
                var gridId = SelectedGrid.ID;
                var gridName = SelectedGrid.Name;
                _credentialManager.SaveAccount(Username, Password, gridId, gridName);

                // Persist the updated MFA hash returned by the server for future silent MFA.
                var mfaHash = _instance!.NetCom.LoginOptions.MfaHash;
                if (!string.IsNullOrEmpty(mfaHash))
                    _credentialManager.SaveMfaHash(Username, gridId, mfaHash);
            }

            Cleanup();

            var instance = _instance!;
            _instance = null; // Transfer ownership
            LoginSucceeded?.Invoke(this, new AgentLoginSucceededEventArgs(instance, Username, SelectedGrid?.ID ?? string.Empty));
        }
        else if (e.Status == LoginStatus.Failed)
        {
            IsLoggingIn = false;
        }
    }

    private void Cleanup()
    {
        if (_instance == null) return;
        _instance.NetCom.ClientLoginStatus -= NetCom_ClientLoginStatus;
        _instance.NetCom.ClientLoggedOut -= NetCom_ClientLoggedOut;
        _instance.NetCom.ClientDisconnected -= NetCom_ClientDisconnected;
    }

    public RadegastInstanceAvalonia? GetInstanceForPreferences()
    {
        EnsureInstance();
        return _instance;
    }

    public void DisposeInstance()
    {
        if (_instance == null) return;
        Cleanup();
        _instance.NetCom.Dispose();
        _instance = null;
    }

    private void NetCom_ClientLoggedOut(object? sender, EventArgs e)
    {
        IsLoggingIn = false;
        StatusText = "Logged out.";
    }

    private void NetCom_ClientDisconnected(object? sender, DisconnectedEventArgs e)
    {
        IsLoggingIn = false;
        StatusText = $"Disconnected: {e.Message}";
    }
}

public enum LoginLocationKind { Home, Last, Favorite, Custom }

public sealed class LoginLocationItem
{
    public string Name { get; }
    public LoginLocationKind Kind { get; }
    public string? Location { get; }

    public LoginLocationItem(string name, LoginLocationKind kind, string? location = null)
    {
        Name = name;
        Kind = kind;
        Location = location;
    }

    public override string ToString() => Name;
}

public class AgentLoginSucceededEventArgs : EventArgs
{
    public RadegastInstanceAvalonia Instance { get; }
    public string Username { get; }
    public string GridId { get; }

    public AgentLoginSucceededEventArgs(RadegastInstanceAvalonia instance, string username, string gridId)
    {
        Instance = instance;
        Username = username;
        GridId = gridId;
    }
}
