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
using LibreMetaverse;
using LibreMetaverse.StructuredData;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class ReconnectViewModel : ObservableObject
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly INetCom _netCom;
    private DispatcherTimer? _countdownTimer;

    public event EventHandler? ReconnectSucceeded;
    public event EventHandler? ReturnToLoginRequested;

    [ObservableProperty]
    private string _disconnectReason = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isReconnecting;

    [ObservableProperty]
    private bool _isMfaRequired;

    [ObservableProperty]
    private string _mfaToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCountingDown))]
    private int _countdownSeconds;

    public bool IsCountingDown => CountdownSeconds > 0 && !IsReconnecting;

    partial void OnCountdownSecondsChanged(int value) =>
        OnPropertyChanged(nameof(IsCountingDown));

    public ReconnectViewModel(RadegastInstanceAvalonia instance, string disconnectReason)
    {
        _instance = instance;
        _netCom = instance.NetCom;
        DisconnectReason = disconnectReason;
        _netCom.ClientLoginStatus += NetCom_ClientLoginStatus;

        if (_instance.GlobalSettings["auto_reconnect"].AsBoolean())
        {
            int seconds = _instance.GlobalSettings["reconnect_time"].Type != OSDType.Unknown
                ? _instance.GlobalSettings["reconnect_time"].AsInteger() : 120;
            CountdownSeconds = seconds;
            StatusText = $"Auto-reconnecting in {CountdownSeconds} seconds…";
            _countdownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnCountdownTick);
        }
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        CountdownSeconds--;
        if (CountdownSeconds > 0)
        {
            StatusText = $"Auto-reconnecting in {CountdownSeconds} seconds…";
        }
        else
        {
            StopCountdown();
            Reconnect();
        }
    }

    private void StopCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
        CountdownSeconds = 0;
    }

    [RelayCommand]
    private void Reconnect()
    {
        StopCountdown();
        if (IsMfaRequired)
        {
            if (string.IsNullOrWhiteSpace(MfaToken))
            {
                StatusText = "Please enter your authenticator code.";
                return;
            }
            _netCom.LoginOptions.MfaToken = MfaToken;
        }
        else
        {
            _netCom.LoginOptions.MfaToken = string.Empty;
        }

        IsReconnecting = true;
        StatusText = "Reconnecting...";
        _netCom.Login();
    }

    [RelayCommand]
    private void ReturnToLogin()
    {
        StopCountdown();
        Cleanup();
        ReturnToLoginRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelAutoReconnect()
    {
        StopCountdown();
        StatusText = string.Empty;
        _instance.GlobalSettings["auto_reconnect"] = OSD.FromBoolean(false);
    }

    private void NetCom_ClientLoginStatus(object? sender, LoginProgressEventArgs e)
    {
        if (e.FailReason == "mfa_challenge")
        {
            IsMfaRequired = true;
            IsReconnecting = false;
            MfaToken = string.Empty;
            StatusText = "Enter your authenticator code.";
            return;
        }

        // If login failed and auto_reconnect is still on, restart the countdown
        if (e.Status == LoginStatus.Failed && e.FailReason != "tos"
            && _instance.GlobalSettings["auto_reconnect"].AsBoolean())
        {
            int seconds = _instance.GlobalSettings["reconnect_time"].Type != OSDType.Unknown
                ? _instance.GlobalSettings["reconnect_time"].AsInteger() : 120;
            IsReconnecting = false;
            CountdownSeconds = seconds;
            StatusText = $"Auto-reconnecting in {CountdownSeconds} seconds…";
            _countdownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnCountdownTick);
            return;
        }

        StatusText = e.Status switch
        {
            LoginStatus.Success => "Reconnected!",
            LoginStatus.Failed => $"Reconnect failed: {e.Message}",
            _ => e.Message
        };

        if (e.Status == LoginStatus.Success)
        {
            IsReconnecting = false;
            IsMfaRequired = false;
            Cleanup();
            ReconnectSucceeded?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Status == LoginStatus.Failed)
        {
            IsReconnecting = false;
        }
    }

    private void Cleanup()
    {
        _netCom.ClientLoginStatus -= NetCom_ClientLoginStatus;
    }
}
