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
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class UploadMeshWindow : Window
{
    public UploadMeshWindow() { InitializeComponent(); }

    public UploadMeshWindow(UploadMeshViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var files = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select Collada mesh files",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Collada files") { Patterns = ["*.dae"] },
                    new FilePickerFileType("All files") { Patterns = ["*.*"] }
                ]
            });

        if (files.Count > 0 && DataContext is UploadMeshViewModel vm)
        {
            vm.AddFiles(files.Select(f => f.Path.LocalPath));
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
