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
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class AnimationPanel : UserControl
{
    private AnimationViewModel? _vm;

    public event EventHandler? DetachRequested;
    public event EventHandler? CloseRequested;

    public AnimationPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _vm = DataContext as AnimationViewModel;
        if (_vm == null) return;

        _vm.SaveRequested += Vm_SaveRequested;

        var btnDetach = this.FindControl<Button>("BtnDetach");
        if (btnDetach != null) btnDetach.Click += (_, _) => DetachRequested?.Invoke(this, EventArgs.Empty);

        var btnClose = this.FindControl<Button>("BtnClose");
        if (btnClose != null) btnClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_vm != null) _vm.SaveRequested -= Vm_SaveRequested;
        _vm?.Dispose();
    }

    private async void Vm_SaveRequested(object? sender, byte[] data)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var name = (_vm?.AnimationName ?? "animation").Replace(Path.GetInvalidFileNameChars(), '_');
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save animation as…",
            SuggestedFileName = $"{name}.sla",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Second Life Animation") { Patterns = ["*.sla"] },
                new("All Files")             { Patterns = ["*"] }
            }
        });

        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(data);
    }
}

file static class StringExtensions
{
    public static string Replace(this string input, char[] chars, char replacement)
    {
        foreach (var c in chars) input = input.Replace(c, replacement);
        return input;
    }
}
