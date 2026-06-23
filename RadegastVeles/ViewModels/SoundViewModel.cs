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
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class SoundViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventorySound _item;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _soundName = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _soundSystemAvailable;

    public SoundViewModel(RadegastInstanceAvalonia instance, InventorySound item)
    {
        _instance = instance;
        _item = item;
        SoundName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);
        SoundSystemAvailable = _instance.MediaManager?.SoundSystemAvailable == true;
        StatusText = SoundSystemAvailable ? "Ready" : "Sound system not available";
    }

    [RelayCommand(CanExecute = nameof(SoundSystemAvailable))]
    private void PlayLocal()
    {
        if (!_instance.MediaManager.SoundSystemAvailable) return;
        _instance.MediaManager.PlayUISound(_item.AssetUUID);
        StatusText = "Playing locally...";
    }

    [RelayCommand(CanExecute = nameof(SoundSystemAvailable))]
    private void PlayInworld()
    {
        if (!_instance.MediaManager.SoundSystemAvailable) return;
        var sim = Client.Network.CurrentSim;
        if (sim == null) return;
        Client.Sound.SendSoundTrigger(_item.AssetUUID, sim.Handle, Client.Self.SimPosition, 1.0f);
        StatusText = "Triggered inworld.";
    }

    partial void OnSoundSystemAvailableChanged(bool value)
    {
        PlayLocalCommand.NotifyCanExecuteChanged();
        PlayInworldCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => Metadata.Dispose();
}
