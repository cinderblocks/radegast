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

public partial class PiperVoiceDownloaderWindow : Window
{
    /// <summary>
    /// Set by the caller (PreferencesWindow) so the downloader knows where
    /// to save models and can update the voice-synth model directory after a successful
    /// download.
    /// </summary>
    public string DownloadRootDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Raised after a successful download with the path to the saved model folder.
    /// The caller can wire this to <see cref="VoiceSynthViewModel.SetModelDirectory"/>.
    /// </summary>
    public event Action<string>? VoiceDownloaded;

    public PiperVoiceDownloaderWindow()
    {
        InitializeComponent();
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PiperVoiceDownloaderViewModel vm) return;

        string root = DownloadRootDirectory;

        if (string.IsNullOrWhiteSpace(root))
        {
            // No pre-set directory — ask the user
            var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select folder to save Piper voice models",
                    AllowMultiple = false,
                });
            if (folders is not [var folder]) return;
            root = folder.Path.LocalPath;
        }
        else
        {
            // Pre-set directory exists — confirm or let user change it
            System.IO.Directory.CreateDirectory(root);
            var suggestedFolder = await GetTopLevel(this)!.StorageProvider.TryGetFolderFromPathAsync(root);
            var folders = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Download voices to this folder (change if needed)",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedFolder,
                });
            if (folders is not [var folder]) return;
            root = folder.Path.LocalPath;
        }

        await vm.DownloadVoiceAsync(root);

        if (!string.IsNullOrEmpty(vm.DownloadedPath))
            VoiceDownloaded?.Invoke(vm.DownloadedPath);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is IDisposable d) d.Dispose();
    }
}
