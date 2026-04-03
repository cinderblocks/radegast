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
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse.StructuredData;
using Radegast.Media;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class PreferencesViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private readonly MediaViewModel _media;
    private readonly VoiceViewModel? _voice;
    private Settings GlobalSettings => _instance.GlobalSettings;

    // General – Image cache
    [ObservableProperty]
    private bool _imageCacheEnabled = true;

    [ObservableProperty]
    private int _imageCacheExpireMinutes = 30;

    [ObservableProperty]
    private int _imageDecodeConcurrency = 2;

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

    // Paths
    [ObservableProperty]
    private bool _chatLoggingEnabled = true;

    [ObservableProperty]
    private string _chatLogDir = string.Empty;

    [ObservableProperty]
    private string _imageCacheDir = string.Empty;

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
    }

    public void Dispose()
    {
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

        ImageCacheEnabled = s["image_cache_enabled"].Type != OSDType.Unknown
            ? s["image_cache_enabled"].AsBoolean() : true;
        ImageCacheExpireMinutes = s["image_cache_expire_minutes"].Type != OSDType.Unknown
            ? s["image_cache_expire_minutes"].AsInteger() : 30;
        ImageDecodeConcurrency = s["image_decode_concurrency"].Type != OSDType.Unknown
            ? s["image_decode_concurrency"].AsInteger()
            : Math.Max(1, Environment.ProcessorCount / 2);

        SoundSystemAvailable = _media.SoundSystemAvailable;
        SoundSystemStatus = _media.SoundSystemStatus;
        MasterVolume = _media.MasterVolume;
        StreamVolume = _media.StreamVolume;
        ObjectVolume = _media.ObjectVolume;
        UiVolume = _media.UiVolume;
        AutoPlay = _media.AutoPlay;
        KeepUrl = _media.KeepUrl;
        ObjectSoundsEnabled = _media.ObjectSoundsEnabled;

        ChatLogDir = s["chat_log_dir"].AsString();
        ChatLoggingEnabled = s["chat_logging_enabled"].Type != OSDType.Unknown
            ? s["chat_logging_enabled"].AsBoolean() : true;
        ImageCacheDir = _instance.Client.Settings.ASSET_CACHE_DIR;

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
        }
    }

    public void Apply()
    {
        var s = GlobalSettings;
        s["image_cache_enabled"] = OSD.FromBoolean(ImageCacheEnabled);
        s["image_cache_expire_minutes"] = OSD.FromInteger(ImageCacheExpireMinutes);
        s["image_decode_concurrency"] = OSD.FromInteger(ImageDecodeConcurrency);

        // Push audio changes to MediaViewModel; it will persist them to GlobalSettings
        _media.MasterVolume = MasterVolume;
        _media.StreamVolume = StreamVolume;
        _media.ObjectVolume = ObjectVolume;
        _media.UiVolume = UiVolume;
        _media.AutoPlay = AutoPlay;
        _media.KeepUrl = KeepUrl;
        _media.ObjectSoundsEnabled = ObjectSoundsEnabled;

        // Chat log directory
        if (string.IsNullOrWhiteSpace(ChatLogDir))
            s.Remove("chat_log_dir");
        else
            s["chat_log_dir"] = OSD.FromString(ChatLogDir);

        // Chat logging enabled
        s["chat_logging_enabled"] = OSD.FromBoolean(ChatLoggingEnabled);
        _instance.ChatLog.IsEnabled = ChatLoggingEnabled;
        _instance.ChatLog.BaseDirectory = string.IsNullOrWhiteSpace(ChatLogDir) ? null : ChatLogDir;

        // Image / texture cache directory
        var defaultCacheDir = Path.Combine(_instance.UserDir, "cache");
        if (!string.IsNullOrWhiteSpace(ImageCacheDir))
        {
            _instance.Client.Settings.ASSET_CACHE_DIR = ImageCacheDir;
            if (string.Equals(ImageCacheDir, defaultCacheDir, StringComparison.OrdinalIgnoreCase))
                s.Remove("texture_cache_dir");
            else
                s["texture_cache_dir"] = OSD.FromString(ImageCacheDir);
        }

        // Voice settings — push to VoiceViewModel and save
        if (_voice != null)
        {
            _voice.VoiceEnabled      = VoiceEnabled;
            _voice.PushToTalkEnabled  = VoicePushToTalkEnabled;
            _voice.AutoConnect        = VoiceAutoConnect;
            _voice.OutputVolume       = VoiceOutputVolume;
            _voice.SaveSettings();
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
    }
}
