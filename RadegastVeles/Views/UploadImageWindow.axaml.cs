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

public partial class UploadImageWindow : Window
{
    public UploadImageWindow() { InitializeComponent(); }

    public UploadImageWindow(UploadImageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var files = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select image to upload",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Image files")
                    {
                        Patterns = ["*.jp2", "*.j2c", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tga", "*.tif", "*.tiff"]
                    },
                    new FilePickerFileType("All files") { Patterns = ["*.*"] }
                ]
            });

        if (files is [var file] && DataContext is UploadImageViewModel vm)
        {
            await vm.LoadImageAsync(file.Path.LocalPath);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
