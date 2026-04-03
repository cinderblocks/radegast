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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;
using Radegast.Veles.Models;

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
    private bool _isCustomGrid;

    [ObservableProperty]
    private bool _isCustomLocation;

    [ObservableProperty]
    private bool _rememberCredentials;

    [ObservableProperty]
    private SavedAccount? _selectedSavedAccount;

    public ObservableCollection<Grid> Grids { get; } = new();
    public ObservableCollection<SavedAccount> SavedAccounts { get; } = new();

    public ObservableCollection<string> LocationOptions { get; } = new()
    {
        "Home",
        "Last Location",
        "Custom…"
    };

    partial void OnSelectedLocationIndexChanged(int value)
        => IsCustomLocation = value == 2;

    public LoginViewModel(CredentialManager credentialManager)
    {
        _credentialManager = credentialManager;

        EnsureInstance();

        // Load saved accounts
        foreach (var account in _credentialManager.GetSavedAccounts())
        {
            SavedAccounts.Add(account);
        }

        // Pre-populate with the last-used account
        if (SavedAccounts.Count > 0)
        {
            SelectedSavedAccount = SavedAccounts[0];
        }

        // Restore previously saved start-location selection.
        var (savedIdx, savedCustom) = _credentialManager.LoadLoginPreferences();
        SelectedLocationIndex = savedIdx;
        CustomLocation = savedCustom;
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
    }

    [RelayCommand]
    private void RemoveSavedAccount()
    {
        if (SelectedSavedAccount == null) return;

        _credentialManager.RemoveAccount(SelectedSavedAccount.Username, SelectedSavedAccount.GridId);
        SavedAccounts.Remove(SelectedSavedAccount);
        SelectedSavedAccount = null;
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

        switch (SelectedLocationIndex)
        {
            case 0:
                netCom.LoginOptions.StartLocation = StartLocationType.Home;
                break;
            case 1:
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
        _credentialManager.SaveLoginPreferences(SelectedLocationIndex, CustomLocation);
        netCom.Login();
    }

    [RelayCommand]
    private void CancelLogin()
    {
        if (_instance != null && (_instance.NetCom.IsLoggingIn || _instance.NetCom.IsLoggedIn))
        {
            _instance.NetCom.Logout();
        }
        IsLoggingIn = false;
        StatusText = "Login cancelled.";
    }

    private void NetCom_ClientLoginStatus(object? sender, LoginProgressEventArgs e)
    {
        StatusText = e.Status switch
        {
            LoginStatus.Success => "Login successful!",
            LoginStatus.Failed => $"Login failed: {e.Message}",
            _ => e.Message
        };

        if (e.Status == LoginStatus.Success)
        {
            IsLoggingIn = false;

            if (RememberCredentials && SelectedGrid != null)
            {
                var gridId = SelectedGrid.ID;
                var gridName = SelectedGrid.Name;
                _credentialManager.SaveAccount(Username, Password, gridId, gridName);
            }

            Cleanup();

            var instance = _instance!;
            _instance = null; // Transfer ownership
            LoginSucceeded?.Invoke(this, new AgentLoginSucceededEventArgs(instance));
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

public class AgentLoginSucceededEventArgs : EventArgs
{
    public RadegastInstanceAvalonia Instance { get; }

    public AgentLoginSucceededEventArgs(RadegastInstanceAvalonia instance)
    {
        Instance = instance;
    }
}
