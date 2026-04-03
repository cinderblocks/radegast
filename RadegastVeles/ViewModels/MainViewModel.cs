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
using OpenMetaverse;
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

    public NearbyViewModel Chat { get; }
    public IMViewModel IM { get; }
    public MapViewModel Map { get; }
    public ObjectsViewModel Objects { get; }
    public InventoryViewModel Inventory { get; }
    public FriendsViewModel Friends { get; }
    public GroupsViewModel Groups { get; }
    public MediaViewModel Media { get; }
    public NotificationQueueViewModel Notifications { get; }
    public VoiceViewModel Voice { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;

        Chat = new NearbyViewModel(instance);
        IM = new IMViewModel(instance);
        Map = new MapViewModel(instance);
        Objects = new ObjectsViewModel(instance);
        Inventory = new InventoryViewModel(instance);
        Friends = new FriendsViewModel(instance);
        Groups = new GroupsViewModel(instance);
        Media = new MediaViewModel(instance);
        Notifications = new NotificationQueueViewModel(instance);
        Voice = new VoiceViewModel(instance);

        // Expose voice through instance so sub-ViewModels (e.g. GroupProfileViewModel) can access it.
        _instance.Voice = Voice;
        // Expose voice through NearbyViewModel so ChatPanel can bind to it.
        Chat.Voice = Voice;

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

        _instance.Client.Self.MoneyBalance += Self_MoneyBalance;
        _instance.IMRequested += Instance_IMRequested;
        _instance.GroupIMRequested += Instance_GroupIMRequested;
        _instance.NotificationReceived += Instance_NotificationReceived;
    }

    public void Dispose()
    {
        _instance.Client.Self.MoneyBalance -= Self_MoneyBalance;
        _instance.IMRequested -= Instance_IMRequested;
        _instance.GroupIMRequested -= Instance_GroupIMRequested;
        _instance.NotificationReceived -= Instance_NotificationReceived;

        Chat.Dispose();
        IM.Dispose();
        Map.Dispose();
        Objects.Dispose();
        Inventory.Dispose();
        Friends.Dispose();
        Groups.Dispose();
        Media.Dispose();
        Voice.Dispose();
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

    private void Instance_NotificationReceived(object? sender, NotificationViewModel e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Notifications.Add(e));
    }

    public void ShowTab(int index)
    {
        SelectedTabIndex = index;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        Chat.IsActive = value == 0;
        IM.IsActive = value == 1;
        if (value == 0) Chat.ClearUnread();
        if (value == 1) IM.ClearUnread();
    }
}
