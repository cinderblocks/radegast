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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenMetaverse;
using OpenMetaverse.Messages.Linden;
using OpenMetaverse.StructuredData;
using Radegast;
using Radegast.Media;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class PreferencesViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly MediaViewModel _media;
    private readonly VoiceViewModel? _voice;
    private Settings GlobalSettings => _instance.GlobalSettings;

    // General – Asset cache (LibreMetaverse built-in, all asset types)
    [ObservableProperty]
    private bool _assetCacheEnabled = true;

    [ObservableProperty]
    private int _assetCacheMaxSizeMb = 1024;

    [ObservableProperty]
    private int _imageDecodeConcurrency = 2;

    [ObservableProperty]
    private int _decodeReservedRamMb = (int)GridTextureHelper.DefaultDecodeReservedMb;

    [ObservableProperty]
    private double _decodePerDecodeMb = GridTextureHelper.DefaultDecodePerDecodeMb;

    [ObservableProperty]
    private int _skBitmapCacheCap = 512;

    /// <summary>
    /// Live value of <see cref="GridTextureHelper.MaxConcurrentDecodes"/> after the last tuning call.
    /// Refreshed whenever <see cref="DecodeReservedRamMb"/> or <see cref="DecodePerDecodeMb"/> change.
    /// </summary>
    public int MaxConcurrentDecodes => GridTextureHelper.MaxConcurrentDecodes;

    partial void OnDecodeReservedRamMbChanged(int value) => OnPropertyChanged(nameof(MaxConcurrentDecodes));
    partial void OnDecodePerDecodeMbChanged(double value) => OnPropertyChanged(nameof(MaxConcurrentDecodes));

    /// <summary>
    /// Re-runs <see cref="GridTextureHelper.TuneDecodeGateForAvailableRam"/> with default arguments
    /// and pulls the auto-detected values back into the slider properties so the UI reflects them.
    /// </summary>
    [RelayCommand]
    private void ResetDecodeGateToAuto()
    {
        GridTextureHelper.TuneDecodeGateForAvailableRam();
        DecodeReservedRamMb = (int)GridTextureHelper.DefaultDecodeReservedMb;
        DecodePerDecodeMb   = GridTextureHelper.DefaultDecodePerDecodeMb;
        SkBitmapCacheCap    = 512;
        GridTextureHelper.SkBitmapCacheCap = 512;
        OnPropertyChanged(nameof(MaxConcurrentDecodes));
        Apply();
    }

    // Cache tab — texture disk cache
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextureDiskCacheSizeText))]
    private bool _textureDiskCacheEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextureDiskCacheSizeText))]
    private string _textureDiskCacheDir = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextureDiskCacheSizeText))]
    private int _textureDiskCacheMaxFiles = 8192;

    [ObservableProperty]
    private string _textureDiskCacheStatusText = string.Empty;

    [ObservableProperty]
    private bool _isClearingTextureDiskCache;

    // Cache tab — bitmap memory cache
    [ObservableProperty]
    private int _textureBitmapCacheCapacity = 2500;

    /// <summary>Live entry count read from the LRU cache; refreshed every second by <see cref="_cacheCountTimer"/>.</summary>
    public int TextureBitmapCacheCount => TextureDownloadQueue.Instance.CacheCount;

    /// <summary>Fill fraction (0.0–1.0) of the bitmap cache: count ÷ capacity.</summary>
    public double TextureBitmapCacheFill =>
        TextureBitmapCacheCapacity > 0
            ? Math.Min(1.0, (double)TextureDownloadQueue.Instance.CacheCount / TextureBitmapCacheCapacity)
            : 0.0;

    private readonly DispatcherTimer _cacheCountTimer;

    partial void OnTextureBitmapCacheCapacityChanged(int value) =>
        OnPropertyChanged(nameof(TextureBitmapCacheFill));

    /// <summary>Human-readable summary of current disk cache size and file count (.j2k files).</summary>
    public string TextureDiskCacheSizeText
    {
        get
        {
            long bytes = TextureDiskCache.GetCacheSizeBytes();
            int  count = TextureDiskCache.GetCacheFileCount();
            return $"{count:N0} file(s) · {FormatBytes(bytes)}";
        }
    }

    // Cache tab — name cache
    [ObservableProperty]
    private int _nameCacheDisplayNameMaxAgeHours = 48;

    [ObservableProperty]
    private int _nameCacheDisplayModeIndex;

    [ObservableProperty]
    private string _nameCacheStatusText = string.Empty;

    [ObservableProperty]
    private bool _isClearingNameCache;

    public string NameCacheSizeText =>
        $"{_instance.Names.Count:N0} name(s) · {_instance.Names.CacheFilePath}";

    public static IReadOnlyList<string> NameDisplayModeItems { get; } =
    [
        "Legacy name only  (e.g. Jane Resident)",
        "Smart  (Display name if set, else legacy)",
        "Display name only",
        "Display name + username",
    ];

    [RelayCommand]
    private void RefreshNameCacheStats()
    {
        OnPropertyChanged(nameof(NameCacheSizeText));
        NameCacheStatusText = string.Empty;
    }

    [RelayCommand]
    private void ClearNameCache()
    {
        if (IsClearingNameCache) return;
        IsClearingNameCache = true;
        try
        {
            _instance.Names.CleanCache();
            OnPropertyChanged(nameof(NameCacheSizeText));
            NameCacheStatusText = "Name cache cleared.";
        }
        catch (Exception ex)
        {
            NameCacheStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsClearingNameCache = false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024L                => $"{bytes} B",
        < 1024L * 1024         => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024  => $"{bytes / (1024.0 * 1024):F1} MB",
        _                      => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    // Audio
    [ObservableProperty]
    private bool _soundSystemAvailable;

    [ObservableProperty]
    private string _soundSystemStatus = string.Empty;

    [ObservableProperty]
    private int _masterVolume = 100;

    [ObservableProperty]
    private int _streamVolume = 20;

    [ObservableProperty]
    private int _objectVolume = 50;

    [ObservableProperty]
    private int _uiVolume = 50;

    [ObservableProperty]
    private bool _autoPlay;

    [ObservableProperty]
    private bool _keepUrl;

    [ObservableProperty]
    private bool _objectSoundsEnabled = true;

    [ObservableProperty]
    private bool _mediaMetadataNotificationsEnabled;

    // Paths
    [ObservableProperty]
    private bool _chatLoggingEnabled = true;

    [ObservableProperty]
    private string _chatLogDir = string.Empty;

    [ObservableProperty]
    private string _assetCacheDir = string.Empty;

    // Voice
    [ObservableProperty]
    private bool _voiceSystemAvailable;

    [ObservableProperty]
    private bool _voiceEnabled = true;

    [ObservableProperty]
    private bool _voicePushToTalkEnabled = true;

    [ObservableProperty]
    private bool _voiceAutoConnect;

    [ObservableProperty]
    private int _voiceOutputVolume = 80;

    [ObservableProperty]
    private bool _voiceNoiseSuppression = true;

    [ObservableProperty]
    private bool _voiceHighPassFilter = true;

    [ObservableProperty]
    private bool _voiceAgc;

    [ObservableProperty]
    private bool _voiceEchoCancellation;

    /// <summary>Available microphone input device names. Passes through from VoiceViewModel.</summary>
    public ObservableCollection<string> VoiceInputDevices => _voice?.InputDevices ?? _emptyDevices;
    private static readonly ObservableCollection<string> _emptyDevices = [];

    public string VoiceSelectedInputDevice
    {
        get => _voice?.SelectedInputDevice ?? string.Empty;
        set { if (_voice != null) { _voice.SelectedInputDevice = value; OnPropertyChanged(); } }
    }

    /// <summary>Exposes the live VoiceViewModel for real-time bindings (mic level, test command).</summary>
    public VoiceViewModel? Voice => _voice;

    // Graphics
    [ObservableProperty]
    private bool _ssaoEnabled = true;

    /// <summary>Enable CPU-side frustum culling of off-screen faces in all scene viewers. Default true.</summary>
    [ObservableProperty]
    private bool _frustumCullingEnabled = true;

    /// <summary>Enable planar water reflections in scene viewers. Default false (costs a full scene pre-pass).</summary>
    [ObservableProperty]
    private bool _waterReflectionsEnabled = false;

    /// <summary>Scene viewer draw distance in metres (16–512). Default 96.</summary>
    [ObservableProperty]
    private float _sceneViewerDrawDistance = 96f;

    // Connection
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReconnectTimeEnabled))]
    private bool _autoReconnect;

    [ObservableProperty]
    private int _reconnectTime = 120;

    public bool IsReconnectTimeEnabled => AutoReconnect;

    partial void OnAutoReconnectChanged(bool value) => OnPropertyChanged(nameof(IsReconnectTimeEnabled));

    // Privacy
    [ObservableProperty]
    private bool _disableLookAt;

    // Chat
    [ObservableProperty]
    private bool _muEmotes;

    [ObservableProperty]
    private bool _noTypingAnim;

    [ObservableProperty]
    private bool _sendTypingNotifications = true;

    [ObservableProperty]
    private bool _chatTimestamps = true;

    [ObservableProperty]
    private bool _imTimestamps = true;

    [ObservableProperty]
    private bool _resolveUris = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolveUriPlaintextEnabled))]
    private bool _resolveUrisAsPlaintext;

    public bool IsResolveUriPlaintextEnabled => ResolveUris;

    partial void OnResolveUrisChanged(bool value) => OnPropertyChanged(nameof(IsResolveUriPlaintextEnabled));

    [ObservableProperty]
    private bool _avNameLink;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConferenceAllowFromFriendsEnabled))]
    private bool _ignoreConferenceChats;

    [ObservableProperty]
    private bool _allowConferenceChatsFromFriends;

    public bool IsConferenceAllowFromFriendsEnabled => IgnoreConferenceChats;

    partial void OnIgnoreConferenceChatsChanged(bool value) => OnPropertyChanged(nameof(IsConferenceAllowFromFriendsEnabled));

    [ObservableProperty]
    private int _autoResponseTypeIndex; // 0=WhenBusy, 1=WhenFromNonFriend, 2=Always

    [ObservableProperty]
    private string _autoResponseText = string.Empty;

    public static IReadOnlyList<string> AutoResponseTypeItems { get; } =
    [
        "When in Busy/Do Not Disturb mode",
        "When sender is not a friend",
        "Always",
    ];

    // Local notifications
    [ObservableProperty]
    private bool _friendsNotificationHighlight = true;

    [ObservableProperty]
    private bool _transactionNotificationChat = true;

    [ObservableProperty]
    private bool _transactionNotificationDialog = true;

    [ObservableProperty]
    private bool _highlightOnChat = true;

    [ObservableProperty]
    private bool _highlightOnIm = true;

    [ObservableProperty]
    private bool _highlightOnGroupIm = true;

    [ObservableProperty]
    private bool _groupImSound = true;

    [ObservableProperty]
    private bool _mentionMeSound = true;

    // RLV
    [ObservableProperty]
    private bool _rlvEnabled;

    [ObservableProperty]
    private bool _rlvDebugCommands;

    // Grids
    public ObservableCollection<Grid> Grids { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuiltInGrid))]
    [NotifyPropertyChangedFor(nameof(IsFormActive))]
    [NotifyCanExecuteChangedFor(nameof(RemoveGridCommand))]
    private Grid? _selectedGrid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormActive))]
    private bool _isEditingNewGrid;

    [ObservableProperty]
    private string _editGridId = string.Empty;

    [ObservableProperty]
    private string _editGridName = string.Empty;

    [ObservableProperty]
    private string _editGridLoginUri = string.Empty;

    public bool IsBuiltInGrid => SelectedGrid?.ID is "agni" or "aditi";
    public bool IsFormActive => SelectedGrid != null || IsEditingNewGrid;

    partial void OnSelectedGridChanged(Grid? value)
    {
        if (value == null) { IsEditingNewGrid = false; return; }
        EditGridId = value.ID;
        EditGridName = value.Name;
        EditGridLoginUri = value.LoginURI;
        IsEditingNewGrid = false;
    }

    [RelayCommand]
    private void AddNewGrid()
    {
        SelectedGrid = null;
        EditGridId = string.Empty;
        EditGridName = string.Empty;
        EditGridLoginUri = string.Empty;
        IsEditingNewGrid = true;
    }

    [RelayCommand]
    private void SaveGrid()
    {
        if (string.IsNullOrWhiteSpace(EditGridName) || string.IsNullOrWhiteSpace(EditGridLoginUri)) return;

        var id = string.IsNullOrWhiteSpace(EditGridId)
            ? EditGridName.Trim().ToLowerInvariant().Replace(" ", "_")
            : EditGridId.Trim();

        var grid = new Grid(id, EditGridName.Trim(), EditGridLoginUri.Trim());
        _instance.GridManger.RegisterGrid(grid);

        var existing = Grids.FirstOrDefault(g => g.ID == grid.ID);
        if (existing != null)
            Grids[Grids.IndexOf(existing)] = grid;
        else
            Grids.Add(grid);

        SelectedGrid = grid;
        IsEditingNewGrid = false;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveGrid))]
    private void RemoveGrid()
    {
        if (SelectedGrid == null) return;
        _instance.GridManger.Grids.Remove(SelectedGrid);
        Grids.Remove(SelectedGrid);
        SelectedGrid = null;
    }

    private bool CanRemoveGrid() => SelectedGrid != null && !IsBuiltInGrid;

    [RelayCommand]
    private async Task ClearTextureDiskCacheAsync()
    {
        if (IsClearingTextureDiskCache) return;
        IsClearingTextureDiskCache = true;
        TextureDiskCacheStatusText = "Clearing…";
        await Task.Run(() => TextureDiskCache.Clear()).ConfigureAwait(false);
        IsClearingTextureDiskCache = false;
        OnPropertyChanged(nameof(TextureDiskCacheSizeText));
        TextureDiskCacheStatusText = "Cache cleared.";
    }

    /// <summary>Refresh the cache-size display without clearing.</summary>
    [RelayCommand]
    private void RefreshCacheStats() => OnPropertyChanged(nameof(TextureDiskCacheSizeText));

    public int MaxDecodeConcurrency { get; } = Math.Max(4, Environment.ProcessorCount);

    // Audio device and profile passthroughs — changes take effect immediately in MediaViewModel
    public ObservableCollection<AudioDriverInfo> AudioDevices => _media.AudioDevices;
    public ObservableCollection<AudioProfile> AudioProfiles => _media.AudioProfiles;

    public AudioDriverInfo? SelectedAudioDevice
    {
        get => _media.SelectedAudioDevice;
        set { _media.SelectedAudioDevice = value; OnPropertyChanged(); }
    }

    public AudioProfile? SelectedProfile
    {
        get => _media.SelectedProfile;
        set { _media.SelectedProfile = value; OnPropertyChanged(); }
    }

    [RelayCommand]
    private void LoadProfile()
    {
        _media.LoadProfileCommand.Execute(null);
        // Sync volume snapshot back from MediaViewModel
        MasterVolume = _media.MasterVolume;
        StreamVolume = _media.StreamVolume;
        ObjectVolume = _media.ObjectVolume;
        UiVolume = _media.UiVolume;
        ObjectSoundsEnabled = _media.ObjectSoundsEnabled;
    }

    public PreferencesViewModel(RadegastInstanceAvalonia instance, MediaViewModel media, VoiceViewModel? voice = null)
    {
        _instance = instance;
        _media = media;
        _voice = voice;
        Load();
        _media.PropertyChanged += OnMediaPropertyChanged;

        _cacheCountTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) =>
            {
                OnPropertyChanged(nameof(TextureBitmapCacheCount));
                OnPropertyChanged(nameof(TextureBitmapCacheFill));
            });
        _cacheCountTimer.Start();
    }

    public void Dispose()
    {
        _cacheCountTimer.Stop();
        _media.PropertyChanged -= OnMediaPropertyChanged;
    }

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaViewModel.SoundSystemAvailable))
        {
            SoundSystemAvailable = _media.SoundSystemAvailable;
            SoundSystemStatus = _media.SoundSystemStatus;
        }
    }

    private void Load()
    {
        var s = GlobalSettings;

        AssetCacheEnabled = s["asset_cache_enabled"].Type != OSDType.Unknown
            ? s["asset_cache_enabled"].AsBoolean() : true;
        AssetCacheMaxSizeMb = s["asset_cache_max_size_mb"].Type != OSDType.Unknown
            ? s["asset_cache_max_size_mb"].AsInteger() : 1024;
        ImageDecodeConcurrency = s["image_decode_concurrency"].Type != OSDType.Unknown
            ? s["image_decode_concurrency"].AsInteger()
            : Math.Max(1, Environment.ProcessorCount / 2);
        DecodeReservedRamMb = s["decode_reserved_ram_mb"].Type != OSDType.Unknown
            ? s["decode_reserved_ram_mb"].AsInteger() : (int)GridTextureHelper.DefaultDecodeReservedMb;
        DecodePerDecodeMb = s["decode_per_decode_mb"].Type != OSDType.Unknown
            ? s["decode_per_decode_mb"].AsReal() : GridTextureHelper.DefaultDecodePerDecodeMb;
        SkBitmapCacheCap = s["sk_bitmap_cache_cap"].Type != OSDType.Unknown
            ? s["sk_bitmap_cache_cap"].AsInteger() : 512;
        GridTextureHelper.SkBitmapCacheCap = SkBitmapCacheCap;

        // Apply immediately
        _instance.Client.Settings.USE_ASSET_CACHE = AssetCacheEnabled;
        _instance.Client.Settings.ASSET_CACHE_MAX_SIZE = (long)AssetCacheMaxSizeMb * 1024 * 1024;

        // Texture disk cache
        TextureDiskCacheEnabled = s["texture_disk_cache_enabled"].Type != OSDType.Unknown
            ? s["texture_disk_cache_enabled"].AsBoolean() : true;
        TextureDiskCacheDir = s["texture_disk_cache_dir"].Type != OSDType.Unknown
            ? s["texture_disk_cache_dir"].AsString() : TextureDiskCache.CacheDir;
        TextureDiskCacheMaxFiles = s["texture_disk_cache_max_files"].Type != OSDType.Unknown
            ? s["texture_disk_cache_max_files"].AsInteger() : 8192;

        // Apply disk cache config immediately so downloads benefit from the stored settings.
        TextureDiskCache.Enabled        = TextureDiskCacheEnabled;
        TextureDiskCache.MaxCachedFiles = TextureDiskCacheMaxFiles;
        if (!string.IsNullOrWhiteSpace(TextureDiskCacheDir))
            TextureDiskCache.CacheDir = TextureDiskCacheDir;

        // Bitmap memory cache
        TextureBitmapCacheCapacity = s["texture_bitmap_cache_capacity"].Type != OSDType.Unknown
            ? s["texture_bitmap_cache_capacity"].AsInteger() : 2500;
        TextureDownloadQueue.Instance.CacheCapacity = TextureBitmapCacheCapacity;

        SoundSystemAvailable = _media.SoundSystemAvailable;
        SoundSystemStatus = _media.SoundSystemStatus;
        MasterVolume = _media.MasterVolume;
        StreamVolume = _media.StreamVolume;
        ObjectVolume = _media.ObjectVolume;
        UiVolume = _media.UiVolume;
        AutoPlay = _media.AutoPlay;
        KeepUrl = _media.KeepUrl;
        ObjectSoundsEnabled = _media.ObjectSoundsEnabled;
        MediaMetadataNotificationsEnabled = _media.MediaMetadataNotificationsEnabled;

        ChatLogDir = s["chat_log_dir"].AsString();
        ChatLoggingEnabled = s["chat_logging_enabled"].Type != OSDType.Unknown
            ? s["chat_logging_enabled"].AsBoolean() : true;
        AssetCacheDir = s["asset_cache_dir"].Type != OSDType.Unknown
            ? s["asset_cache_dir"].AsString()
            : _instance.Client.Settings.ASSET_CACHE_DIR;

        // Grids — apply persisted user grids to the manager, then populate the display list
        if (s["user_grids"] is OSDArray savedUserGrids)
        {
            foreach (var item in savedUserGrids)
            {
                var g = Grid.FromOSD(item);
                if (g != null) _instance.GridManger.RegisterGrid(g);
            }
        }

        Grids.Clear();
        foreach (var g in _instance.GridManger.Grids)
            Grids.Add(g);

        // Voice
        VoiceSystemAvailable = _voice?.IsAvailable ?? false;
        if (_voice != null)
        {
            VoiceEnabled          = _voice.VoiceEnabled;
            VoicePushToTalkEnabled = _voice.PushToTalkEnabled;
            VoiceAutoConnect      = _voice.AutoConnect;
            VoiceOutputVolume     = _voice.OutputVolume;
            VoiceNoiseSuppression = _voice.NoiseSuppressionEnabled;
            VoiceHighPassFilter   = _voice.HighPassFilterEnabled;
            VoiceAgc              = _voice.AgcEnabled;
            VoiceEchoCancellation = _voice.EchoCancellationEnabled;
        }

        // Graphics
        SsaoEnabled = s["ssao_enabled"].Type != OSDType.Unknown
            ? s["ssao_enabled"].AsBoolean() : true;
        FrustumCullingEnabled = s["frustum_culling_enabled"].Type != OSDType.Unknown
            ? s["frustum_culling_enabled"].AsBoolean() : true;
        WaterReflectionsEnabled = s["water_reflections_enabled"].Type != OSDType.Unknown
            ? s["water_reflections_enabled"].AsBoolean() : false;
        SceneViewerDrawDistance = s["scene_draw_distance"].Type != OSDType.Unknown
            ? (float)s["scene_draw_distance"].AsReal() : 96f;

        // RLV
        RlvEnabled = _instance.RLV?.Enabled ?? false;
        RlvDebugCommands = _instance.RLV?.EnabledDebugCommands ?? false;

        // Connection
        AutoReconnect = s["auto_reconnect"].Type != OSDType.Unknown ? s["auto_reconnect"].AsBoolean() : false;
        ReconnectTime = s["reconnect_time"].Type != OSDType.Unknown ? s["reconnect_time"].AsInteger() : 120;

        // Privacy
        DisableLookAt = s["disable_look_at"].Type != OSDType.Unknown ? s["disable_look_at"].AsBoolean() : false;

        // Chat
        MuEmotes = s["mu_emotes"].Type != OSDType.Unknown ? s["mu_emotes"].AsBoolean() : false;
        NoTypingAnim = s["no_typing_anim"].Type != OSDType.Unknown ? s["no_typing_anim"].AsBoolean() : false;
        SendTypingNotifications = s["send_typing_notifications"].Type != OSDType.Unknown ? s["send_typing_notifications"].AsBoolean() : true;
        ChatTimestamps = s["chat_timestamps"].Type != OSDType.Unknown ? s["chat_timestamps"].AsBoolean() : true;
        ImTimestamps = s["im_timestamps"].Type != OSDType.Unknown ? s["im_timestamps"].AsBoolean() : true;
        ResolveUris = s["resolve_uris"].Type != OSDType.Unknown ? s["resolve_uris"].AsBoolean() : true;
        ResolveUrisAsPlaintext = s["resolve_uris_as_plaintext"].Type != OSDType.Unknown ? s["resolve_uris_as_plaintext"].AsBoolean() : false;
        AvNameLink = s["av_name_link"].Type != OSDType.Unknown ? s["av_name_link"].AsBoolean() : false;
        IgnoreConferenceChats = s["ignore_conference_chats"].Type != OSDType.Unknown ? s["ignore_conference_chats"].AsBoolean() : false;
        AllowConferenceChatsFromFriends = s["allow_conference_chats_from_friends"].Type != OSDType.Unknown ? s["allow_conference_chats_from_friends"].AsBoolean() : false;
        AutoResponseTypeIndex = s["auto_response_type"].Type != OSDType.Unknown ? s["auto_response_type"].AsInteger() : 0;
        AutoResponseText = s["auto_response_text"].Type != OSDType.Unknown ? s["auto_response_text"].AsString()
            : "The Resident you messaged is in 'busy mode' which means they have requested not to be disturbed.  Your message will still be shown in their IM panel for later viewing.";

        // Local notifications
        FriendsNotificationHighlight = s["friends_notification_highlight"].Type != OSDType.Unknown ? s["friends_notification_highlight"].AsBoolean() : true;
        TransactionNotificationChat = s["transaction_notification_chat"].Type != OSDType.Unknown ? s["transaction_notification_chat"].AsBoolean() : true;
        TransactionNotificationDialog = s["transaction_notification_dialog"].Type != OSDType.Unknown ? s["transaction_notification_dialog"].AsBoolean() : true;
        HighlightOnChat = s["highlight_on_chat"].Type != OSDType.Unknown ? s["highlight_on_chat"].AsBoolean() : true;
        HighlightOnIm = s["highlight_on_im"].Type != OSDType.Unknown ? s["highlight_on_im"].AsBoolean() : true;
        HighlightOnGroupIm = s["highlight_on_group_im"].Type != OSDType.Unknown ? s["highlight_on_group_im"].AsBoolean() : true;
        GroupImSound = s["group_im_sound"].Type != OSDType.Unknown ? s["group_im_sound"].AsBoolean() : true;
        MentionMeSound = s["mention_me_sound"].Type != OSDType.Unknown ? s["mention_me_sound"].AsBoolean() : true;

        // Name cache
        NameCacheDisplayNameMaxAgeHours = _instance.Names.DisplayNameMaxAgeHours;
        NameCacheDisplayModeIndex = (int)_instance.Names.Mode;
        OnPropertyChanged(nameof(NameCacheSizeText));
    }

    public void Apply()
    {
        var s = GlobalSettings;
        s["asset_cache_enabled"]      = OSD.FromBoolean(AssetCacheEnabled);
        s["asset_cache_max_size_mb"]   = OSD.FromInteger(AssetCacheMaxSizeMb);
        s["image_decode_concurrency"]  = OSD.FromInteger(ImageDecodeConcurrency);
        s["decode_reserved_ram_mb"]    = OSD.FromInteger(DecodeReservedRamMb);
        s["decode_per_decode_mb"]      = OSD.FromReal(DecodePerDecodeMb);
        s["sk_bitmap_cache_cap"]       = OSD.FromInteger(SkBitmapCacheCap);
        GridTextureHelper.SkBitmapCacheCap = SkBitmapCacheCap;
        _instance.Client.Settings.USE_ASSET_CACHE      = AssetCacheEnabled;
        _instance.Client.Settings.ASSET_CACHE_MAX_SIZE = (long)AssetCacheMaxSizeMb * 1024 * 1024;

        // Re-tune
        GridTextureHelper.TuneDecodeGateForAvailableRam(DecodeReservedRamMb, DecodePerDecodeMb);
        var availableMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0);
        Logger.Log(
            $"J2K decode gate re-tuned via preferences: MaxConcurrentDecodes={GridTextureHelper.MaxConcurrentDecodes} " +
            $"(available RAM: {availableMb:F0} MB, reserved: {DecodeReservedRamMb} MB, per-decode: {DecodePerDecodeMb:F1} MB)",
            LogLevel.Information);

        // Texture disk cache
        s["texture_disk_cache_enabled"]   = OSD.FromBoolean(TextureDiskCacheEnabled);
        s["texture_disk_cache_max_files"] = OSD.FromInteger(TextureDiskCacheMaxFiles);
        TextureDiskCache.Enabled       = TextureDiskCacheEnabled;
        TextureDiskCache.MaxCachedFiles = TextureDiskCacheMaxFiles;
        if (!string.IsNullOrWhiteSpace(TextureDiskCacheDir))
        {
            s["texture_disk_cache_dir"] = OSD.FromString(TextureDiskCacheDir);
            TextureDiskCache.CacheDir   = TextureDiskCacheDir;
        }
        else
        {
            s.Remove("texture_disk_cache_dir");
        }

        // Bitmap memory cache
        s["texture_bitmap_cache_capacity"] = OSD.FromInteger(TextureBitmapCacheCapacity);
        TextureDownloadQueue.Instance.CacheCapacity = TextureBitmapCacheCapacity;

        // Push audio changes to MediaViewModel; it will persist them to GlobalSettings
        _media.MasterVolume = MasterVolume;
        _media.StreamVolume = StreamVolume;
        _media.ObjectVolume = ObjectVolume;
        _media.UiVolume = UiVolume;
        _media.AutoPlay = AutoPlay;
        _media.KeepUrl = KeepUrl;
        _media.ObjectSoundsEnabled = ObjectSoundsEnabled;
        _media.MediaMetadataNotificationsEnabled = MediaMetadataNotificationsEnabled;

        // Chat log directory
        if (string.IsNullOrWhiteSpace(ChatLogDir))
            s.Remove("chat_log_dir");
        else
            s["chat_log_dir"] = OSD.FromString(ChatLogDir);

        // Chat logging enabled
        s["chat_logging_enabled"] = OSD.FromBoolean(ChatLoggingEnabled);
        _instance.ChatLog.IsEnabled = ChatLoggingEnabled;
        _instance.ChatLog.BaseDirectory = string.IsNullOrWhiteSpace(ChatLogDir) ? null : ChatLogDir;

        // Asset cache directory
        var defaultCacheDir = Path.Combine(_instance.UserDir, "cache");
        if (!string.IsNullOrWhiteSpace(AssetCacheDir))
        {
            _instance.Client.Settings.ASSET_CACHE_DIR = AssetCacheDir;
            if (string.Equals(AssetCacheDir, defaultCacheDir, StringComparison.OrdinalIgnoreCase))
                s.Remove("asset_cache_dir");
            else
                s["asset_cache_dir"] = OSD.FromString(AssetCacheDir);
        }

        // Name cache
        _instance.Names.DisplayNameMaxAgeHours = NameCacheDisplayNameMaxAgeHours;
        _instance.Names.Mode = (NameMode)NameCacheDisplayModeIndex;

        // Voice settings — push to VoiceViewModel and save
        if (_voice != null)
        {
            _voice.VoiceEnabled             = VoiceEnabled;
            _voice.PushToTalkEnabled        = VoicePushToTalkEnabled;
            _voice.AutoConnect              = VoiceAutoConnect;
            _voice.OutputVolume             = VoiceOutputVolume;
            _voice.NoiseSuppressionEnabled  = VoiceNoiseSuppression;
            _voice.HighPassFilterEnabled    = VoiceHighPassFilter;
            _voice.AgcEnabled               = VoiceAgc;
            _voice.EchoCancellationEnabled  = VoiceEchoCancellation;
            _voice.SaveSettings();
            _voice.VoiceSynth.SaveSettings();
        }

        // User grids — persist all non-built-in grids
        var userGrids = new OSDArray();
        foreach (var g in _instance.GridManger.Grids)
        {
            if (g.ID is "agni" or "aditi") continue;
            userGrids.Add(new OSDMap
            {
                ["gridnick"]  = OSD.FromString(g.ID),
                ["gridname"]  = OSD.FromString(g.Name),
                ["platform"]  = OSD.FromString(g.Platform),
                ["loginuri"]  = OSD.FromString(g.LoginURI),
                ["loginpage"] = OSD.FromString(g.LoginPage),
                ["helperuri"] = OSD.FromString(g.HelperURI),
                ["website"]   = OSD.FromString(g.Website),
                ["support"]   = OSD.FromString(g.Support),
                ["register"]  = OSD.FromString(g.Register),
                ["password"]  = OSD.FromString(g.PasswordURL),
                ["version"]   = OSD.FromString(g.Version),
            });
        }
        s["user_grids"] = userGrids;

        // Graphics
        s["ssao_enabled"] = OSD.FromBoolean(SsaoEnabled);
        s["frustum_culling_enabled"] = OSD.FromBoolean(FrustumCullingEnabled);
        s["water_reflections_enabled"] = OSD.FromBoolean(WaterReflectionsEnabled);
        s["scene_draw_distance"] = OSD.FromReal(SceneViewerDrawDistance);

        // RLV
        if (_instance.RLV != null)
        {
            _instance.RLV.Enabled = RlvEnabled;
            _instance.RLV.EnabledDebugCommands = RlvDebugCommands;
        }

        // Connection
        s["auto_reconnect"] = OSD.FromBoolean(AutoReconnect);
        s["reconnect_time"] = OSD.FromInteger(ReconnectTime);

        // Privacy
        s["disable_look_at"] = OSD.FromBoolean(DisableLookAt);

        // Chat
        s["mu_emotes"] = OSD.FromBoolean(MuEmotes);
        s["no_typing_anim"] = OSD.FromBoolean(NoTypingAnim);
        s["send_typing_notifications"] = OSD.FromBoolean(SendTypingNotifications);
        s["chat_timestamps"] = OSD.FromBoolean(ChatTimestamps);
        s["im_timestamps"] = OSD.FromBoolean(ImTimestamps);
        s["resolve_uris"] = OSD.FromBoolean(ResolveUris);
        s["resolve_uris_as_plaintext"] = OSD.FromBoolean(ResolveUrisAsPlaintext);
        s["av_name_link"] = OSD.FromBoolean(AvNameLink);
        s["ignore_conference_chats"] = OSD.FromBoolean(IgnoreConferenceChats);
        s["allow_conference_chats_from_friends"] = OSD.FromBoolean(AllowConferenceChatsFromFriends);
        s["auto_response_type"] = OSD.FromInteger(AutoResponseTypeIndex);
        s["auto_response_text"] = OSD.FromString(AutoResponseText);

        // Local notifications
        s["friends_notification_highlight"] = OSD.FromBoolean(FriendsNotificationHighlight);
        s["transaction_notification_chat"] = OSD.FromBoolean(TransactionNotificationChat);
        s["transaction_notification_dialog"] = OSD.FromBoolean(TransactionNotificationDialog);
        s["highlight_on_chat"] = OSD.FromBoolean(HighlightOnChat);
        s["highlight_on_im"] = OSD.FromBoolean(HighlightOnIm);
        s["highlight_on_group_im"] = OSD.FromBoolean(HighlightOnGroupIm);
        s["group_im_sound"] = OSD.FromBoolean(GroupImSound);
        s["mention_me_sound"] = OSD.FromBoolean(MentionMeSound);
    }

    // --- Notifications ---
    public ObservableCollection<NotificationChannelEntry> NotificationChannels { get; } = [];
    [ObservableProperty] private bool _isLoadingNotificationPrefs;
    [ObservableProperty] private string _notificationPrefsStatus = string.Empty;

    [RelayCommand]
    private async Task LoadNotificationPrefsAsync()
    {
        if (IsLoadingNotificationPrefs) return;
        IsLoadingNotificationPrefs = true;
        NotificationPrefsStatus = "Loading\u2026";
        var msg = await _instance.Client.Self.GetNotificationPreferencesAsync();
        IsLoadingNotificationPrefs = false;
        NotificationChannels.Clear();
        if (msg == null) { NotificationPrefsStatus = "Server does not support reading notification preferences. Configure channels below and use Save to push settings to the server."; return; }
        foreach (var e in msg.Notifications)
            NotificationChannels.Add(new NotificationChannelEntry(e.Name, e.Value));
        NotificationPrefsStatus = $"{NotificationChannels.Count} channel(s) loaded.";
    }

    [RelayCommand]
    private async Task SaveNotificationPrefsAsync()
    {
        var msg = new NotificationPreferencesMessage();
        msg.Notifications = NotificationChannels
            .Select(c => new NotificationPreferencesMessage.NotificationEntry
                { Name = c.Name, Value = c.IsEnabled })
            .ToList();
        await _instance.Client.Self.SetNotificationPreferencesAsync(msg);
        NotificationPrefsStatus = "Saved.";
    }
}

public partial class NotificationChannelEntry : ObservableObject
{
    public string Name { get; }
    [ObservableProperty] private bool _isEnabled;

    public NotificationChannelEntry(string name, bool isEnabled)
    {
        Name = name;
        _isEnabled = isEnabled;
    }
}
