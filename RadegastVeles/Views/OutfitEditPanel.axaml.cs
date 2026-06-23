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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class OutfitEditPanel : UserControl
{
    public OutfitEditPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var btnSaveAs = this.FindControl<Button>("BtnSaveAs");
        if (btnSaveAs != null) btnSaveAs.Click += BtnSaveAs_Click;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        var btnSaveAs = this.FindControl<Button>("BtnSaveAs");
        if (btnSaveAs != null) btnSaveAs.Click -= BtnSaveAs_Click;
    }

    private async void BtnSaveAs_Click(object? sender, RoutedEventArgs e)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return;
        var dialog = new RenameDialog("My Outfit") { Title = "Save Outfit As" };
        await dialog.ShowDialog(parentWindow);
        if (!string.IsNullOrWhiteSpace(dialog.Result) && DataContext is OutfitEditViewModel vm)
            await vm.SaveCurrentOutfitAsync(dialog.Result);
    }
}
