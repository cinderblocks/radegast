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
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm)
            vm.Apply();
        Close();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm)
            vm.Apply();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is IDisposable d)
            d.Dispose();
    }

    private async void OnBrowseChatLogDirClick(object? sender, RoutedEventArgs e)
    {
        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select chat log directory", AllowMultiple = false });
        if (folders is [var folder] && DataContext is PreferencesViewModel vm)
            vm.ChatLogDir = folder.Path.LocalPath;
    }

    private async void OnBrowseAssetCacheDirClick(object? sender, RoutedEventArgs e)
    {
        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select asset cache directory", AllowMultiple = false });
        if (folders is [var folder] && DataContext is PreferencesViewModel vm)
            vm.AssetCacheDir = folder.Path.LocalPath;
    }

    private async void OnBrowseTextureDiskCacheDirClick(object? sender, RoutedEventArgs e)
    {
        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select texture disk cache directory", AllowMultiple = false });
        if (folders is [var folder] && DataContext is PreferencesViewModel vm)
            vm.TextureDiskCacheDir = folder.Path.LocalPath;
    }

    private async void OnBrowseTtsModelDirClick(object? sender, RoutedEventArgs e)
    {
        var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Piper TTS model directory", AllowMultiple = false });
        if (folders is [var folder] && DataContext is PreferencesViewModel vm)
            vm.Voice?.VoiceSynth.SetModelDirectory(folder.Path.LocalPath);
    }

    private async void OnDownloadVoiceModelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesViewModel vm) return;

        string voicesDir = vm.Voice?.VoiceSynth.VoicesDirectory ?? string.Empty;

        var dlg = new PiperVoiceDownloaderWindow
        {
            DownloadRootDirectory = voicesDir,
        };
        dlg.VoiceDownloaded += path => vm.Voice?.VoiceSynth.SetModelDirectory(path);
        dlg.DataContext = new PiperVoiceDownloaderViewModel();
        await dlg.ShowDialog(this);
    }
}
