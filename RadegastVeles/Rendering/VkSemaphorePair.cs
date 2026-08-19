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

// Ported from Avalonia's own samples/GpuInterop/VulkanDemo's VulkanSemaphorePair.cs (MIT
// licensed, https://github.com/AvaloniaUI/Avalonia). Used by VkInteropSwapchain's Mode A path
// (native Vulkan compositor, ExternalMemoryHandleTypeFlags.OpaqueWin32Bit images -- see
// VkInteropImage's handleType branch) -- Mode B's D3D11-backed images use the Win32 keyed-
// mutex protocol instead (see VkCommandBufferPool's KeyedMutexSubmitInfo), not this class.
// Kept for completeness/portability even though Veles's chosen integration mode is B, matching
// VkContext.cs's own precedent of supporting both modes at the device level. No macOS
// (IOSurface/timeline-semaphore) branch -- see VkInteropImage.cs's note on why that path was
// dropped rather than ported (needs Silk.NET.Vulkan.Extensions.EXT, deliberately not added).

using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkSemaphorePair : IDisposable
{
    private readonly VkContext _vk;

    public VkSemaphorePair(VkContext vk, bool exportable)
    {
        _vk = vk;

        var semaphoreExportInfo = new ExportSemaphoreCreateInfo
        {
            SType = StructureType.ExportSemaphoreCreateInfo,
            HandleTypes = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ExternalSemaphoreHandleTypeFlags.OpaqueWin32Bit
                : ExternalSemaphoreHandleTypeFlags.OpaqueFDBit
        };

        var semaphoreCreateInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = exportable ? &semaphoreExportInfo : null
        };

        vk.Api.CreateSemaphore(vk.Device, in semaphoreCreateInfo, null, out var semaphore).ThrowOnError();
        ImageAvailableSemaphore = semaphore;

        vk.Api.CreateSemaphore(vk.Device, in semaphoreCreateInfo, null, out semaphore).ThrowOnError();
        RenderFinishedSemaphore = semaphore;
    }

    private int ExportFd(bool renderFinished)
    {
        if (!_vk.Api.TryGetDeviceExtension<KhrExternalSemaphoreFd>(_vk.Instance, _vk.Device, out var ext))
            throw new InvalidOperationException();
        var info = new SemaphoreGetFdInfoKHR
        {
            SType = StructureType.SemaphoreGetFDInfoKhr,
            Semaphore = renderFinished ? RenderFinishedSemaphore : ImageAvailableSemaphore,
            HandleType = ExternalSemaphoreHandleTypeFlags.OpaqueFDBit
        };
        ext.GetSemaphoreF(_vk.Device, in info, out var fd).ThrowOnError();
        return fd;
    }

    private IntPtr ExportWin32(bool renderFinished)
    {
        if (!_vk.Api.TryGetDeviceExtension<KhrExternalSemaphoreWin32>(_vk.Instance, _vk.Device, out var ext))
            throw new InvalidOperationException();
        var info = new SemaphoreGetWin32HandleInfoKHR
        {
            SType = StructureType.SemaphoreGetWin32HandleInfoKhr,
            Semaphore = renderFinished ? RenderFinishedSemaphore : ImageAvailableSemaphore,
            HandleType = ExternalSemaphoreHandleTypeFlags.OpaqueWin32Bit
        };
        ext.GetSemaphoreWin32Handle(_vk.Device, in info, out var fd).ThrowOnError();
        return fd;
    }

    public IPlatformHandle Export(bool renderFinished)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new PlatformHandle(ExportWin32(renderFinished), KnownPlatformGraphicsExternalSemaphoreHandleTypes.VulkanOpaqueNtHandle);
        return new PlatformHandle(new IntPtr(ExportFd(renderFinished)), KnownPlatformGraphicsExternalSemaphoreHandleTypes.VulkanOpaquePosixFileDescriptor);
    }

    internal Semaphore ImageAvailableSemaphore { get; }
    internal Semaphore RenderFinishedSemaphore { get; }

    public void Dispose()
    {
        _vk.Api.DestroySemaphore(_vk.Device, ImageAvailableSemaphore, null);
        _vk.Api.DestroySemaphore(_vk.Device, RenderFinishedSemaphore, null);
    }
}
