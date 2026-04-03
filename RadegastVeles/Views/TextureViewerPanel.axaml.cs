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

public partial class TextureViewerPanel : UserControl
{
    private TextureViewerViewModel? _vm;

    public event EventHandler? DetachRequested;
    public event EventHandler? CloseRequested;

    public TextureViewerPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as TextureViewerViewModel;
        if (_vm == null) return;

        _vm.SaveToFileRequested += OnSaveToFileRequested;

        var btnDetach = this.FindControl<Button>("BtnDetach");
        if (btnDetach != null) btnDetach.Click += (_, _) => DetachRequested?.Invoke(this, EventArgs.Empty);

        var btnClose = this.FindControl<Button>("BtnClose");
        if (btnClose != null) btnClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_vm != null) _vm.SaveToFileRequested -= OnSaveToFileRequested;
        _vm?.Dispose();
    }

    private async void OnSaveToFileRequested(object? sender, EventArgs e)
    {
        if (_vm?.TextureBitmap == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Texture",
            SuggestedFileName = _vm.TextureName + ".png",
            FileTypeChoices = [new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }]
        });

        if (file == null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            _vm.TextureBitmap.Save(stream);
        }
        catch { }
    }
}
