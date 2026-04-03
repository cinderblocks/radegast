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
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using OpenMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class LandmarkViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    private AssetLandmark? _decodedLandmark;
    private ulong _regionHandle;
    private UUID _parcelId;
    private Vector3 _position;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _landmarkName = string.Empty;
    [ObservableProperty] private string _simName = string.Empty;
    [ObservableProperty] private string _parcelName = string.Empty;
    [ObservableProperty] private string _parcelDescription = string.Empty;
    [ObservableProperty] private string _coordinatesText = string.Empty;
    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _canTeleport;
    [ObservableProperty] private Bitmap? _parcelSnapshot;

    public LandmarkViewModel(RadegastInstanceAvalonia instance, InventoryLandmark item)
    {
        _instance = instance;
        LandmarkName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);

        Client.Grid.RegionHandleReply += Grid_RegionHandleReply;
        Client.Parcels.ParcelInfoReply += Parcels_ParcelInfoReply;
        Client.Assets.RequestAsset(item.AssetUUID, AssetType.Landmark, true, OnAssetReceived);
    }

    private void OnAssetReceived(AssetDownload transfer, Asset? asset)
    {
        if (!transfer.Success || asset is not AssetLandmark landmark)
        {
            Dispatcher.UIThread.Post(() => { IsLoading = false; StatusText = "Failed to load landmark."; });
            return;
        }

        landmark.Decode();
        _decodedLandmark = landmark;
        _position = landmark.Position;
        Dispatcher.UIThread.Post(() => StatusText = "Resolving region...");
        Client.Grid.RequestRegionHandle(landmark.RegionID);
    }

    private async void Grid_RegionHandleReply(object? sender, RegionHandleReplyEventArgs e)
    {
        if (_decodedLandmark == null || _decodedLandmark.RegionID != e.RegionID) return;

        _regionHandle = e.RegionHandle;
        _parcelId = await Client.Parcels.RequestRemoteParcelIDAsync(
            _decodedLandmark.Position, e.RegionHandle, e.RegionID, CancellationToken.None);

        if (_parcelId != UUID.Zero)
        {
            Dispatcher.UIThread.Post(() => StatusText = "Loading parcel info...");
            Client.Parcels.RequestParcelInfo(_parcelId);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                CoordinatesText = FormatCoords(_position);
                StatusText = CoordinatesText;
                CanTeleport = true;
            });
        }
    }

    private void Parcels_ParcelInfoReply(object? sender, ParcelInfoReplyEventArgs e)
    {
        if (e.Parcel.ID != _parcelId) return;

        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            SimName = e.Parcel.SimName ?? string.Empty;
            ParcelName = e.Parcel.Name ?? string.Empty;
            ParcelDescription = e.Parcel.Description ?? string.Empty;
            CoordinatesText = FormatCoords(_position);
            StatusText = string.IsNullOrEmpty(SimName)
                ? CoordinatesText
                : $"{SimName} ({CoordinatesText})";
            CanTeleport = true;

            if (e.Parcel.SnapshotID != UUID.Zero)
                GridTextureHelper.Download(Client, e.Parcel.SnapshotID, img => ParcelSnapshot = img);
        });
    }

    private static string FormatCoords(Vector3 v) =>
        $"{(int)v.X}, {(int)v.Y}, {(int)v.Z}";

    [RelayCommand(CanExecute = nameof(CanTeleport))]
    private void Teleport()
    {
        if (_regionHandle == 0) return;
        Client.Self.RequestTeleport(_regionHandle, _position);
    }

    partial void OnCanTeleportChanged(bool value) =>
        TeleportCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        Client.Grid.RegionHandleReply -= Grid_RegionHandleReply;
        Client.Parcels.ParcelInfoReply -= Parcels_ParcelInfoReply;
        Metadata.Dispose();
    }
}
