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
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class PayViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private readonly UUID _targetId;
    private readonly string _targetName;
    private readonly bool _isObject;
    private readonly Simulator? _objectSim;

    private static readonly int[] DefaultAmounts = [1, 5, 10, 20];

    // Persists last paid amount across windows in the same session
    private static int _lastPaid = -1;

    private int _quick1Amount = 1;
    private int _quick2Amount = 5;
    private int _quick3Amount = 10;
    private int _quick4Amount = 20;

    [ObservableProperty] private string _targetDisplay = string.Empty;
    [ObservableProperty] private string _windowTitle = "Pay";
    [ObservableProperty] private int _balance;
    [ObservableProperty] private string _balanceText = string.Empty;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _canPay;

    [ObservableProperty] private string _quick1Text = "L$1";
    [ObservableProperty] private string _quick2Text = "L$5";
    [ObservableProperty] private string _quick3Text = "L$10";
    [ObservableProperty] private string _quick4Text = "L$20";
    [ObservableProperty] private bool _quick1Visible = true;
    [ObservableProperty] private bool _quick2Visible = true;
    [ObservableProperty] private bool _quick3Visible = true;
    [ObservableProperty] private bool _quick4Visible = true;

    public event EventHandler? CloseRequested;

    public PayViewModel(RadegastInstanceAvalonia instance, UUID targetId, string targetName,
        bool isObject = false, Simulator? sim = null)
    {
        _instance = instance;
        _targetId = targetId;
        _targetName = targetName;
        _isObject = isObject;
        _objectSim = sim;

        WindowTitle = $"Pay - {targetName}";
        TargetDisplay = isObject ? $"Via object: {targetName}" : $"Pay resident: {targetName}";
        Balance = Client.Self.Balance;
        BalanceText = $"Your balance: L${Balance:N0}";

        if (_lastPaid > 0)
            AmountText = _lastPaid.ToString();

        ValidateAmount();

        Client.Self.MoneyBalance += Self_MoneyBalance;

        if (isObject)
        {
            Client.Objects.PayPriceReply += Objects_PayPriceReply;
            var effectiveSim = _objectSim ?? Client.Network.CurrentSim;
            if (effectiveSim != null)
                Client.Objects.RequestPayPrice(effectiveSim, targetId);
        }
    }

    public void Dispose()
    {
        Client.Self.MoneyBalance -= Self_MoneyBalance;
        if (_isObject)
            Client.Objects.PayPriceReply -= Objects_PayPriceReply;
    }

    partial void OnAmountTextChanged(string value) => ValidateAmount();

    partial void OnBalanceChanged(int value)
    {
        BalanceText = $"Your balance: L${value:N0}";
        ValidateAmount();
    }

    partial void OnCanPayChanged(bool value) => PayCommand.NotifyCanExecuteChanged();

    private void ValidateAmount()
    {
        if (int.TryParse(AmountText, out var amount) && amount > 0)
        {
            if (amount > Balance)
            {
                StatusText = "Insufficient balance";
                CanPay = false;
            }
            else
            {
                StatusText = string.Empty;
                CanPay = true;
            }
        }
        else
        {
            StatusText = string.Empty;
            CanPay = false;
        }
    }

    [RelayCommand]
    private void QuickPay1() => ExecuteQuickPay(_quick1Amount);

    [RelayCommand]
    private void QuickPay2() => ExecuteQuickPay(_quick2Amount);

    [RelayCommand]
    private void QuickPay3() => ExecuteQuickPay(_quick3Amount);

    [RelayCommand]
    private void QuickPay4() => ExecuteQuickPay(_quick4Amount);

    private void ExecuteQuickPay(int amount)
    {
        if (amount <= 0 || amount > Balance)
        {
            StatusText = "Insufficient balance";
            return;
        }
        _lastPaid = amount;
        SendPayment(amount);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanPay))]
    private void Pay()
    {
        if (!int.TryParse(AmountText, out var amount) || amount <= 0) return;
        _lastPaid = amount;
        SendPayment(amount);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SendPayment(int amount)
    {
        if (_isObject)
            Client.Self.GiveObjectMoney(_targetId, amount, _targetName);
        else
            Client.Self.GiveAvatarMoney(_targetId, amount);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Self_MoneyBalance(object? sender, BalanceEventArgs e)
    {
        Dispatcher.UIThread.Post(() => Balance = e.Balance);
    }

    private void Objects_PayPriceReply(object? sender, PayPriceReplyEventArgs e)
    {
        if (e.ObjectID != _targetId) return;

        Dispatcher.UIThread.Post(() =>
        {
            UpdateButton(0, e.ButtonPrices.Length > 0 ? e.ButtonPrices[0] : (int)PayPriceType.Default, DefaultAmounts[0]);
            UpdateButton(1, e.ButtonPrices.Length > 1 ? e.ButtonPrices[1] : (int)PayPriceType.Default, DefaultAmounts[1]);
            UpdateButton(2, e.ButtonPrices.Length > 2 ? e.ButtonPrices[2] : (int)PayPriceType.Default, DefaultAmounts[2]);
            UpdateButton(3, e.ButtonPrices.Length > 3 ? e.ButtonPrices[3] : (int)PayPriceType.Default, DefaultAmounts[3]);

            if (e.DefaultPrice != (int)PayPriceType.Default && e.DefaultPrice != (int)PayPriceType.Hide)
                AmountText = e.DefaultPrice.ToString();
        });
    }

    private void UpdateButton(int index, int price, int defaultAmount)
    {
        bool visible = price != (int)PayPriceType.Hide;
        int amount = !visible ? 0
            : price == (int)PayPriceType.Default ? defaultAmount
            : price;

        switch (index)
        {
            case 0:
                _quick1Amount = amount;
                Quick1Text = visible ? $"L${amount:N0}" : string.Empty;
                Quick1Visible = visible;
                break;
            case 1:
                _quick2Amount = amount;
                Quick2Text = visible ? $"L${amount:N0}" : string.Empty;
                Quick2Visible = visible;
                break;
            case 2:
                _quick3Amount = amount;
                Quick3Text = visible ? $"L${amount:N0}" : string.Empty;
                Quick3Visible = visible;
                break;
            case 3:
                _quick4Amount = amount;
                Quick4Text = visible ? $"L${amount:N0}" : string.Empty;
                Quick4Visible = visible;
                break;
        }
    }
}
