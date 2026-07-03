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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.StructuredData;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public class AppearanceItem
{
    public UUID    ItemId      { get; init; }
    public string  Icon        { get; init; } = string.Empty;
    public string  Name        { get; init; } = string.Empty;
    public string  SlotLabel   { get; init; } = string.Empty;
    public bool    CanRemove   { get; init; }
    public int     SortKey     { get; init; }
    public ICommand? RemoveCommand { get; init; }
}

public partial class AppearanceViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private bool _disposed;

    public ObservableCollection<AppearanceItem> BodyParts   { get; } = [];
    public ObservableCollection<AppearanceItem> Clothing    { get; } = [];
    public ObservableCollection<AppearanceItem> Attachments { get; } = [];

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private bool   _hasBodyParts;
    [ObservableProperty] private bool   _hasClothing;
    [ObservableProperty] private bool   _hasAttachments;
    [ObservableProperty] private string _statusText = string.Empty;

    public const double MinHoverHeight = -2.0;
    public const double MaxHoverHeight =  2.0;
    [ObservableProperty] private double _hoverHeight;
    [ObservableProperty] private string _hoverHeightText = "0.00";
    [ObservableProperty] private bool   _isHoverBusy;

    // SL clothing layer order (base → top)
    private static readonly Dictionary<WearableType, int> ClothingLayerOrder = new()
    {
        { WearableType.Underpants,  0 },
        { WearableType.Undershirt,  1 },
        { WearableType.Pants,       2 },
        { WearableType.Shirt,       3 },
        { WearableType.Socks,       4 },
        { WearableType.Shoes,       5 },
        { WearableType.Jacket,      6 },
        { WearableType.Gloves,      7 },
        { WearableType.Skirt,       8 },
        { WearableType.Alpha,       9 },
        { WearableType.Tattoo,     10 },
        { WearableType.Physics,    11 },
        { WearableType.Universal,  12 },
    };

    public AppearanceViewModel(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        _hoverHeight     = instance.GlobalSettings["AvatarHoverOffsetZ"]?.AsReal() ?? 0.0;
        _hoverHeightText = _hoverHeight.ToString("F2");
        Client.Appearance.AgentWearablesReply   += OnWearablesReply;
        Client.Appearance.AppearanceSet         += OnAppearanceSet;
        Client.Inventory.ItemReceived           += OnInventoryItemReceived;
        Client.Self.AgentPreferencesUpdated     += OnAgentPreferencesUpdated;
        _ = LoadAsync();
    }

    partial void OnHoverHeightChanged(double value) => HoverHeightText = value.ToString("F2");

    private void OnWearablesReply(object? sender, AgentWearablesReplyEventArgs e)
        => Dispatcher.UIThread.Post(() => _ = LoadAsync());

    private void OnAppearanceSet(object? sender, AppearanceSetEventArgs e)
        => Dispatcher.UIThread.Post(() => _ = LoadAsync());

    private void OnInventoryItemReceived(object? sender, ItemReceivedEventArgs e)
    {
        var attachPoints = Client.Appearance.GetAttachmentsByItemId();
        var wearables    = Client.Appearance.GetWearables();
        if (attachPoints.ContainsKey(e.Item.UUID) || wearables.Any(w => w.ItemID == e.Item.UUID))
            Dispatcher.UIThread.Post(() => _ = LoadAsync());
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void RebakeTextures()
    {
        Client.Appearance.RequestSetAppearance(true);
        StatusText = "Rebake requested.";
    }

    [RelayCommand]
    private async Task ApplyHoverHeight()
    {
        IsHoverBusy = true;
        try
        {
            await Client.Self.SetHoverHeightAsync(HoverHeight);
            _instance.GlobalSettings["AvatarHoverOffsetZ"] = OSD.FromReal(HoverHeight);
            StatusText = $"Hover height set to {HoverHeight:F2}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            IsHoverBusy = false;
        }
    }

    private void OnAgentPreferencesUpdated(object? sender, AgentPreferencesEventArgs e)
        => Dispatcher.UIThread.Post(() => HoverHeight = e.Preferences.HoverHeight);

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var bodyList   = new List<AppearanceItem>();
            var clothList  = new List<AppearanceItem>();
            var attachList = new List<AppearanceItem>();

            // Wearables — body parts first, then clothing sorted by layer order
            var seen = new HashSet<UUID>();
            foreach (var w in Client.Appearance.GetWearables())
            {
                if (!seen.Add(w.ItemID)) continue;

                bool isBodyPart = w.WearableType is WearableType.Shape or WearableType.Skin
                                                 or WearableType.Hair  or WearableType.Eyes;
                var capturedId = w.ItemID;
                var entry = new AppearanceItem
                {
                    ItemId    = w.ItemID,
                    Icon      = GetWearableIcon(w.WearableType),
                    Name      = GetWearableName(w),
                    SlotLabel = w.WearableType.ToString(),
                    CanRemove = !isBodyPart,
                    SortKey   = ClothingLayerOrder.TryGetValue(w.WearableType, out var order) ? order : 99,
                    RemoveCommand = isBodyPart ? null
                        : new AsyncRelayCommand(() => RemoveItemAsync(capturedId)),
                };

                if (isBodyPart) bodyList.Add(entry);
                else            clothList.Add(entry);
            }
            clothList.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));

            // Attachments — resolved via AppearanceManager
            var pointByItemId = Client.Appearance.GetAttachmentsByItemId();
            foreach (var invItem in Client.Appearance.GetAttachments())
            {
                if (!pointByItemId.TryGetValue(invItem.UUID, out var point)) continue;
                var capturedId = invItem.UUID;
                attachList.Add(new AppearanceItem
                {
                    ItemId    = invItem.UUID,
                    Icon      = IsHudPoint(point) ? "🖥" : "📦",
                    Name      = string.IsNullOrEmpty(invItem.Name) ? "Attachment" : invItem.Name,
                    SlotLabel = AttachPointLabel(point),
                    CanRemove = true,
                    RemoveCommand = new AsyncRelayCommand(() => RemoveItemAsync(capturedId)),
                });
            }
            attachList.Sort((a, b) => string.Compare(a.SlotLabel, b.SlotLabel, StringComparison.Ordinal));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BodyParts.Clear();
                foreach (var i in bodyList)   BodyParts.Add(i);
                Clothing.Clear();
                foreach (var i in clothList)  Clothing.Add(i);
                Attachments.Clear();
                foreach (var i in attachList) Attachments.Add(i);

                HasBodyParts   = BodyParts.Count   > 0;
                HasClothing    = Clothing.Count    > 0;
                HasAttachments = Attachments.Count > 0;

                int total = BodyParts.Count + Clothing.Count + Attachments.Count;
                StatusText = total > 0
                    ? $"{total} item{(total != 1 ? "s" : "")} worn"
                    : string.Empty;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Error: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    private async Task RemoveItemAsync(UUID itemId)
    {
        var cof = _instance.COF;
        if (cof == null)
        {
            // Fallback for attachments when COF is unavailable
            Client.Appearance.Detach(itemId);
            await Task.Delay(300).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => _ = LoadAsync());
            return;
        }

        if (Client.Inventory.Store?.TryGetValue(itemId, out var invBase) != true
            || invBase is not InventoryItem invItem)
        {
            StatusText = "Item not in local inventory cache.";
            return;
        }

        await cof.RemoveFromOutfitAsync(invItem, CancellationToken.None).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    private string GetWearableName(AppearanceManager.WearableData w)
    {
        if (w.Asset?.Name is { Length: > 0 } n) return n;
        if (Client.Inventory.Store?.TryGetValue(w.ItemID, out var invBase) == true
            && invBase is InventoryItem item && !string.IsNullOrEmpty(item.Name))
            return item.Name;
        return w.WearableType.ToString();
    }

    private static bool IsHudPoint(AttachmentPoint point) =>
        point is AttachmentPoint.HUDCenter2    or AttachmentPoint.HUDTopRight
              or AttachmentPoint.HUDTop        or AttachmentPoint.HUDTopLeft
              or AttachmentPoint.HUDCenter     or AttachmentPoint.HUDBottomLeft
              or AttachmentPoint.HUDBottom     or AttachmentPoint.HUDBottomRight;

    private static string AttachPointLabel(AttachmentPoint p) => p switch
    {
        AttachmentPoint.Chest           => "Chest",
        AttachmentPoint.Skull           => "Skull",
        AttachmentPoint.LeftShoulder    => "Left Shoulder",
        AttachmentPoint.RightShoulder   => "Right Shoulder",
        AttachmentPoint.LeftHand        => "Left Hand",
        AttachmentPoint.RightHand       => "Right Hand",
        AttachmentPoint.LeftFoot        => "Left Foot",
        AttachmentPoint.RightFoot       => "Right Foot",
        AttachmentPoint.Spine           => "Spine",
        AttachmentPoint.Pelvis          => "Pelvis",
        AttachmentPoint.Mouth           => "Mouth",
        AttachmentPoint.Chin            => "Chin",
        AttachmentPoint.LeftEar         => "Left Ear",
        AttachmentPoint.RightEar        => "Right Ear",
        AttachmentPoint.LeftEyeball     => "Left Eye",
        AttachmentPoint.RightEyeball    => "Right Eye",
        AttachmentPoint.Nose            => "Nose",
        AttachmentPoint.RightUpperArm   => "Right Upper Arm",
        AttachmentPoint.RightForearm    => "Right Forearm",
        AttachmentPoint.LeftUpperArm    => "Left Upper Arm",
        AttachmentPoint.LeftForearm     => "Left Forearm",
        AttachmentPoint.RightHip        => "Right Hip",
        AttachmentPoint.RightUpperLeg   => "Right Upper Leg",
        AttachmentPoint.RightLowerLeg   => "Right Lower Leg",
        AttachmentPoint.LeftHip         => "Left Hip",
        AttachmentPoint.LeftUpperLeg    => "Left Upper Leg",
        AttachmentPoint.LeftLowerLeg    => "Left Lower Leg",
        AttachmentPoint.Stomach         => "Stomach",
        AttachmentPoint.LeftPec         => "Left Pec",
        AttachmentPoint.RightPec        => "Right Pec",
        AttachmentPoint.HUDCenter2      => "HUD Center 2",
        AttachmentPoint.HUDTopRight     => "HUD Top Right",
        AttachmentPoint.HUDTop          => "HUD Top",
        AttachmentPoint.HUDTopLeft      => "HUD Top Left",
        AttachmentPoint.HUDCenter       => "HUD Center",
        AttachmentPoint.HUDBottomLeft   => "HUD Bottom Left",
        AttachmentPoint.HUDBottom       => "HUD Bottom",
        AttachmentPoint.HUDBottomRight  => "HUD Bottom Right",
        AttachmentPoint.Neck            => "Neck",
        AttachmentPoint.Root            => "Avatar Center",
        AttachmentPoint.LeftHandRing    => "Left Hand Ring",
        AttachmentPoint.RightHandRing   => "Right Hand Ring",
        AttachmentPoint.TailBase        => "Tail Base",
        AttachmentPoint.TailTip         => "Tail Tip",
        AttachmentPoint.LeftWing        => "Left Wing",
        AttachmentPoint.RightWing       => "Right Wing",
        AttachmentPoint.Jaw             => "Jaw",
        AttachmentPoint.AltLeftEar      => "Alt Left Ear",
        AttachmentPoint.AltRightEar     => "Alt Right Ear",
        AttachmentPoint.AltLeftEye      => "Alt Left Eye",
        AttachmentPoint.AltRightEye     => "Alt Right Eye",
        AttachmentPoint.Tongue          => "Tongue",
        AttachmentPoint.Groin           => "Groin",
        AttachmentPoint.LeftHindFoot    => "Left Hind Foot",
        AttachmentPoint.RightHindFoot   => "Right Hind Foot",
        _ => p.ToString(),
    };

    private static string GetWearableIcon(WearableType t) => t switch
    {
        WearableType.Shape      => "🧍",
        WearableType.Skin       => "🎨",
        WearableType.Hair       => "💇",
        WearableType.Eyes       => "👁",
        WearableType.Shirt      => "👕",
        WearableType.Pants      => "👖",
        WearableType.Shoes      => "👟",
        WearableType.Socks      => "🧦",
        WearableType.Jacket     => "🧥",
        WearableType.Gloves     => "🧤",
        WearableType.Undershirt => "👚",
        WearableType.Underpants => "🩲",
        WearableType.Skirt      => "👗",
        WearableType.Alpha      => "⬜",
        WearableType.Tattoo     => "✏",
        WearableType.Physics    => "🔧",
        WearableType.Universal  => "✨",
        _ => "📎",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Client.Appearance.AgentWearablesReply   -= OnWearablesReply;
        Client.Appearance.AppearanceSet         -= OnAppearanceSet;
        Client.Inventory.ItemReceived           -= OnInventoryItemReceived;
        Client.Self.AgentPreferencesUpdated     -= OnAgentPreferencesUpdated;
    }
}
