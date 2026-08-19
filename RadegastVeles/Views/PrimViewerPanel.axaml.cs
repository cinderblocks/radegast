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
using Avalonia.Layout;
using Radegast.Veles.Rendering;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class PrimViewerPanel : UserControl
{
    /// <summary>The Vulkan viewport hosted by this panel, exposed through the backend-agnostic
    /// interface <see cref="PrimViewerViewModel.SetViewport"/> consumes.</summary>
    public readonly ISingleObjectViewport Viewport;

    public PrimViewerPanel()
    {
        InitializeComponent();

        var vk = new VkViewportControl();
        Viewport = vk;
        Control control = vk;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(control, "3D viewport");
        ViewportHost.Content = control;
    }

    /// <summary>
    /// Wire the viewport into the VM once the visual tree is ready.
    /// Called after DataContext is set.
    /// </summary>
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PrimViewerViewModel vm)
        {
            vm.SetViewport(Viewport);
            Viewport.InitFailed += msg =>
            {
                // Logged, not just reflected in UI state, so a panel-fatal init failure is
                // visible to log analysis (see SceneViewerViewModel's identical InitFailed handler).
                LibreMetaverse.Logger.Error($"[PrimViewer] Viewport init failed: {msg}");
                vm.HasError  = true;
                vm.ErrorText = $"Vulkan init failed: {msg}";
                vm.StatusText = vm.ErrorText;
                vm.IsLoading  = false;
            };
        }
    }
}
