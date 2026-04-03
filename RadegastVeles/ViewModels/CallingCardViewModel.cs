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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class CallingCardViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryCallingCard _item;

    public UUID AvatarId { get; }
    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _avatarName = "Loading...";
    [ObservableProperty] private string _cardName = string.Empty;
    [ObservableProperty] private string _aboutText = string.Empty;
    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private Bitmap? _profileImage;
    [ObservableProperty] private string _bornOn = string.Empty;
    [ObservableProperty] private string _accountInfo = string.Empty;

    public CallingCardViewModel(RadegastInstanceAvalonia instance, InventoryCallingCard item)
    {
        _instance = instance;
        _item = item;
        AvatarId = item.CreatorID != UUID.Zero ? item.CreatorID : item.OwnerID;
        CardName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);

        _instance.Names.NameUpdated += Names_NameUpdated;
        Client.Avatars.AvatarPropertiesReply += Avatars_AvatarPropertiesReply;

        // Try cache first; schedule a network request if not already known
        var cached = _instance.Names.Get(AvatarId);
        if (!string.IsNullOrEmpty(cached) && cached != "(???) (???)")
        {
            AvatarName = cached;
        }

        Client.Avatars.RequestAvatarProperties(AvatarId);
    }

    private void Names_NameUpdated(object? sender, UUIDNameReplyEventArgs e)
    {
        if (!e.Names.TryGetValue(AvatarId, out var name)) return;
        Dispatcher.UIThread.Post(() => AvatarName = name);
    }

    private void Avatars_AvatarPropertiesReply(object? sender, AvatarPropertiesReplyEventArgs e)
    {
        if (e.AvatarID != AvatarId) return;
        Dispatcher.UIThread.Post(() =>
        {
            AboutText = e.Properties.AboutText ?? string.Empty;
            BornOn = e.Properties.BornOn ?? string.Empty;

            var info = string.Empty;
            if (AvatarName.EndsWith("Linden")) info = "Linden Lab Employee";
            else if (!string.IsNullOrEmpty(e.Properties.CharterMember)) info = e.Properties.CharterMember;
            AccountInfo = info;

            IsLoading = false;
            StatusText = string.Empty;

            if (e.Properties.ProfileImage != UUID.Zero)
                GridTextureHelper.Download(Client, e.Properties.ProfileImage, img => ProfileImage = img);
        });
    }

    [RelayCommand]
    private void ViewProfile() =>
        _instance.ShowAgentProfile(AvatarName, AvatarId);

    [RelayCommand]
    private void SendIM()
    {
        Client.Self.InstantMessage(AvatarId, string.Empty);
        _instance.ShowNotificationInChat($"Opening IM with {AvatarName}.");
    }

    public void Dispose()
    {
        _instance.Names.NameUpdated -= Names_NameUpdated;
        Client.Avatars.AvatarPropertiesReply -= Avatars_AvatarPropertiesReply;
        Metadata.Dispose();
    }
}
