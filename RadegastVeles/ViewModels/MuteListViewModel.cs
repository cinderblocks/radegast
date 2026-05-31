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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public record MuteEntryItem(string TypeLabel, string Name, UUID Id, MuteEntry Entry);

public partial class MuteListViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnmute))]
    private MuteEntryItem? _selectedEntry;

    public bool CanUnmute => SelectedEntry != null;

    [ObservableProperty] private string _muteByNameInput = string.Empty;

    public ObservableCollection<MuteEntryItem> MuteEntries { get; } = [];

    public MuteListViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        Client.Self.MuteListUpdated += OnMuteListUpdated;
        Client.Self.RequestMuteList();
        LoadMuteEntries();
    }

    public void Dispose()
    {
        Client.Self.MuteListUpdated -= OnMuteListUpdated;
    }

    private void LoadMuteEntries()
    {
        Dispatcher.UIThread.Post(() =>
        {
            MuteEntries.Clear();
            foreach (var e in Client.Self.MuteList.Values.OrderBy(e => e.Name))
            {
                var typeLabel = e.Type switch
                {
                    MuteType.ByName   => "By Name",
                    MuteType.Resident => "Resident",
                    MuteType.Object   => "Object",
                    MuteType.Group    => "Group",
                    _                 => "Unknown"
                };
                MuteEntries.Add(new MuteEntryItem(typeLabel, e.Name, e.ID, e));
            }
        });
    }

    private void OnMuteListUpdated(object? sender, EventArgs e) => Dispatcher.UIThread.Post(LoadMuteEntries);

    [RelayCommand(CanExecute = nameof(CanUnmute))]
    private void Unmute()
    {
        Client.Self.RemoveMuteListEntry(SelectedEntry!.Id, SelectedEntry!.Name);
    }

    [RelayCommand]
    private void MuteByName()
    {
        if (string.IsNullOrWhiteSpace(MuteByNameInput)) return;
        Client.Self.UpdateMuteListEntry(MuteType.ByName, UUID.Zero, MuteByNameInput.Trim());
        MuteByNameInput = string.Empty;
    }

    [RelayCommand]
    private void MuteResident()
    {
        _instance.ShowAvatarPicker("Mute Resident", entry =>
            Client.Self.UpdateMuteListEntry(MuteType.Resident, entry.Id, entry.Name));
    }

    [RelayCommand]
    private void Refresh()
    {
        Client.Self.RequestMuteList();
    }

    partial void OnSelectedEntryChanged(MuteEntryItem? value) => UnmuteCommand.NotifyCanExecuteChanged();
}
