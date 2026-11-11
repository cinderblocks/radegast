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

// Ported from Avalonia's own samples/GpuInterop/VulkanDemo's VulkanImage.cs (MIT licensed,
// https://github.com/AvaloniaUI/Avalonia). Represents a single interop-exportable VkImage:
// either the render target Veles renders into (constructed with exportable=true, used by
// VkInteropSwapchain), or -- not used by Veles today -- a non-exportable image if some future
// pass needed one.
//
// Trimmed from the original in one way: no GRContext/SkiaSharp SaveTexture() debug/screenshot
// feature (matches the same trim VkContext.cs already made -- not part of the render/present
// pipeline). Three interop modes are implemented: Windows D3D11-shared-handle / native-Vulkan-
// opaque-Win32 (Mode B, what Veles actually runs on Windows), generic Linux opaque-FD (unused
// today but zero extra package cost), and macOS IOSurface via VK_EXT_metal_objects (MoltenVK).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using static Silk.NET.Core.Native.SilkMarshal;
using Device = Silk.NET.Vulkan.Device;
using Format = Silk.NET.Vulkan.Format;

namespace Radegast.Veles.Rendering;

internal sealed unsafe class VkInteropImage : IDisposable
{
    private readonly VkContext _vk;
    private readonly Instance _instance;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly VkCommandBufferPool _commandBufferPool;
    private ImageLayout _currentLayout;
    private AccessFlags _currentAccessFlags;
    private readonly ImageUsageFlags _imageUsageFlags;
    private ImageView _imageView;
    private DeviceMemory _imageMemory;
    private ComPtr<ID3D11Texture2D> _d3dTexture2D;

    private readonly Dictionary<ulong, Framebuffer> _framebuffers = new();
    internal Image InternalHandle { get; private set; }
    internal Format Format { get; }
    internal ImageAspectFlags AspectFlags { get; }

    public ulong ViewHandle => _imageView.Handle;
    public DeviceMemory DeviceMemory => _imageMemory;
    public uint MipLevels { get; }
    public Vk Api { get; }
    public PixelSize Size { get; }
    public ulong MemorySize { get; }

    public VkInteropImage(VkContext vk, Format format, PixelSize size, bool exportable, IReadOnlyList<string> supportedHandleTypes)
    {
        _vk = vk;
        _instance = vk.Instance;
        _device = vk.Device;
        _physicalDevice = vk.PhysicalDevice;
        _commandBufferPool = vk.Pool;
        Format = format;
        Api = vk.Api;
        Size = size;
        MipLevels = 1;
        _imageUsageFlags = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit |
                            ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit;

        var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        var handleType = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (supportedHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle)
               && !supportedHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaqueNtHandle)
                ? ExternalMemoryHandleTypeFlags.D3D11TextureBit
                : ExternalMemoryHandleTypeFlags.OpaqueWin32Bit)
            : ExternalMemoryHandleTypeFlags.OpaqueFDBit; // Linux; unused on macOS

        var externalMemoryCreateInfo = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            HandleTypes = handleType
        };
        // macOS: VK_EXT_metal_objects uses pNext on the image create info instead of
        // ExternalMemoryImageCreateInfo to mark the image for IOSurface export.
        var exportMetalObjectCreateInfo = new ExportMetalObjectCreateInfoEXT
        {
            SType = StructureType.ExportMetalObjectCreateInfoExt,
            ExportObjectType = ExportMetalObjectTypeFlagsEXT.IosurfaceBitExt
        };

        var imageCreateInfo = new ImageCreateInfo
        {
            PNext = exportable
                ? (isMacOS ? (void*)&exportMetalObjectCreateInfo : (void*)&externalMemoryCreateInfo)
                : null,
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format,
            Extent = new Extent3D((uint?)Size.Width, (uint?)Size.Height, 1),
            MipLevels = MipLevels,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = _imageUsageFlags,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
            Flags = ImageCreateFlags.CreateMutableFormatBit
        };

        Api.CreateImage(_device, in imageCreateInfo, null, out var image).ThrowOnError();
        InternalHandle = image;

        {
            Api.GetImageMemoryRequirements(_device, InternalHandle, out var memoryRequirements);

            var dedicatedAllocation = new MemoryDedicatedAllocateInfoKHR
            {
                SType = StructureType.MemoryDedicatedAllocateInfoKhr,
                Image = image
            };

            var fdExport = new ExportMemoryAllocateInfo
            {
                HandleTypes = handleType,
                SType = StructureType.ExportMemoryAllocateInfo,
                PNext = &dedicatedAllocation
            };

            ImportMemoryWin32HandleInfoKHR handleImport = default;
            if (handleType == ExternalMemoryHandleTypeFlags.D3D11TextureBit && exportable)
            {
                if (vk.D3DDevice.Handle == null)
                    throw new NotSupportedException("Vulkan D3DDevice wasn't created");
                _d3dTexture2D = VkD3DMemoryHelper.CreateSharedTexture(vk.D3DDevice, size, Format);

                handleImport = new ImportMemoryWin32HandleInfoKHR
                {
                    PNext = &dedicatedAllocation,
                    SType = StructureType.ImportMemoryWin32HandleInfoKhr,
                    HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
                    Handle = CreateDxgiSharedHandle()
                };
            }

            var memoryAllocateInfo = new MemoryAllocateInfo
            {
                // macOS: IOSurface is associated with the VkImage via VK_EXT_metal_objects, not
                // with the VkDeviceMemory, so ExportMemoryAllocateInfo is not used; only the
                // dedicated-allocation hint is chained.
                PNext = isMacOS
                    ? (exportable ? (void*)&dedicatedAllocation : null)
                    : (exportable ? (handleImport.Handle != IntPtr.Zero ? (void*)&handleImport : (void*)&fdExport) : null),
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memoryRequirements.Size,
                MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(
                    Api, _physicalDevice, memoryRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
            };

            Api.AllocateMemory(_device, in memoryAllocateInfo, null, out var imageMemory).ThrowOnError();
            _imageMemory = imageMemory;
            MemorySize = memoryRequirements.Size;
            Api.BindImageMemory(_device, InternalHandle, _imageMemory, 0).ThrowOnError();
        }

        var componentMapping = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
            ComponentSwizzle.Identity, ComponentSwizzle.Identity);

        AspectFlags = ImageAspectFlags.ColorBit;
        var subresourceRange = new ImageSubresourceRange(AspectFlags, 0, MipLevels, 0, 1);

        var imageViewCreateInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = InternalHandle,
            ViewType = ImageViewType.Type2D,
            Format = Format,
            Components = componentMapping,
            SubresourceRange = subresourceRange
        };
        Api.CreateImageView(_device, in imageViewCreateInfo, null, out var imageView).ThrowOnError();
        _imageView = imageView;

        _currentLayout = ImageLayout.Undefined;
        TransitionLayout(ImageLayout.ColorAttachmentOptimal, AccessFlags.None);
    }

    private IntPtr CreateDxgiSharedHandle()
    {
        using var dxgiResource = _d3dTexture2D.QueryInterface<IDXGIResource1>();
        void* sharedHandle;
        ThrowHResult(dxgiResource.CreateSharedHandle((SecurityAttributes*)null,
            DXGI.SharedResourceRead | DXGI.SharedResourceWrite, (char*)null, &sharedHandle));
        return (IntPtr)sharedHandle;
    }

    private int ExportFd()
    {
        if (!Api.TryGetDeviceExtension<KhrExternalMemoryFd>(_instance, _device, out var ext))
            throw new InvalidOperationException();
        var info = new MemoryGetFdInfoKHR
        {
            Memory = _imageMemory,
            SType = StructureType.MemoryGetFDInfoKhr,
            HandleType = ExternalMemoryHandleTypeFlags.OpaqueFDBit
        };
        ext.GetMemoryF(_device, in info, out var fd).ThrowOnError();
        return fd;
    }

    private IntPtr ExportOpaqueNtHandle()
    {
        if (!Api.TryGetDeviceExtension<KhrExternalMemoryWin32>(_instance, _device, out var ext))
            throw new InvalidOperationException();
        var info = new MemoryGetWin32HandleInfoKHR
        {
            Memory = _imageMemory,
            SType = StructureType.MemoryGetWin32HandleInfoKhr,
            HandleType = ExternalMemoryHandleTypeFlags.OpaqueWin32Bit
        };
        ext.GetMemoryWin32Handle(_device, in info, out var fd).ThrowOnError();
        return fd;
    }

    /// <summary>Exports this image for import into Avalonia's compositor via
    /// <see cref="Avalonia.Rendering.Composition.ICompositionGpuInterop.ImportImage"/> --
    /// picks the handle type matching whatever platform/mode this image was actually created
    /// for (see the constructor's per-platform PNext branch).</summary>
    public IPlatformHandle Export()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (_d3dTexture2D.Handle != null)
                return new PlatformHandle(CreateDxgiSharedHandle(), KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle);
            return new PlatformHandle(ExportOpaqueNtHandle(), KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaqueNtHandle);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new PlatformHandle(ExportIOSurface(), KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef);
        return new PlatformHandle(new IntPtr(ExportFd()), KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor);
    }

    private nint ExportIOSurface()
    {
        if (!Api.TryGetDeviceExtension<ExtMetalObjects>(_instance, _device, out var ext))
            throw new InvalidOperationException("VK_EXT_metal_objects not available");
        var ioSurfaceInfo = new ExportMetalIOSurfaceInfoEXT
        {
            SType = StructureType.ExportMetalIOSurfaceInfoExt,
            Image = InternalHandle
        };
        var exportObjects = new ExportMetalObjectsInfoEXT
        {
            SType = StructureType.ExportMetalObjectsInfoExt,
            PNext = &ioSurfaceInfo
        };
        ext.ExportMetalObjects(_device, &exportObjects);
        return ioSurfaceInfo.IoSurface;
    }

    /// <summary>True on Mode B: this image's memory is a DXGI-shared D3D11
    /// texture, not a native Vulkan opaque handle, so submissions touching it must use the
    /// Win32-keyed-mutex acquire/release protocol (see <see cref="VkCommandBufferPool.VkCommandBuffer.Submit"/>'s
    /// <c>KeyedMutexSubmitInfo</c>) rather than semaphores.</summary>
    public bool IsDirectXBacked => _d3dTexture2D.Handle != null;

    internal void TransitionLayout(CommandBuffer commandBuffer, ImageLayout fromLayout, AccessFlags fromAccessFlags,
        ImageLayout destinationLayout, AccessFlags destinationAccessFlags)
    {
        VkMemoryHelper.TransitionLayout(Api, commandBuffer, InternalHandle, fromLayout, fromAccessFlags,
            destinationLayout, destinationAccessFlags, MipLevels);
        _currentLayout = destinationLayout;
        _currentAccessFlags = destinationAccessFlags;
    }

    internal void TransitionLayout(CommandBuffer commandBuffer, ImageLayout destinationLayout, AccessFlags destinationAccessFlags)
        => TransitionLayout(commandBuffer, _currentLayout, _currentAccessFlags, destinationLayout, destinationAccessFlags);

    internal void TransitionLayout(ImageLayout destinationLayout, AccessFlags destinationAccessFlags)
    {
        var commandBuffer = _commandBufferPool.CreateCommandBuffer("VkInteropImage");
        commandBuffer.BeginRecording();
        TransitionLayout(commandBuffer.InternalHandle, destinationLayout, destinationAccessFlags);
        commandBuffer.EndRecording();
        commandBuffer.SubmitAndWait();
    }

    /// <summary>Returns a framebuffer attaching only this image's view, compatible with
    /// <paramref name="renderPass"/>, creating it on first use. Lives as long as the image.</summary>
    public Framebuffer GetFramebuffer(RenderPass renderPass)
    {
        if (_framebuffers.TryGetValue(renderPass.Handle, out var framebuffer)) return framebuffer;

        var view = _imageView;
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = (uint)Size.Width,
            Height = (uint)Size.Height,
            Layers = 1
        };
        Api.CreateFramebuffer(_device, in info, null, out framebuffer).ThrowOnError();
        _framebuffers[renderPass.Handle] = framebuffer;
        return framebuffer;
    }

    public void Dispose()
    {
        foreach (var framebuffer in _framebuffers.Values) Api.DestroyFramebuffer(_device, framebuffer, null);
        _framebuffers.Clear();
        Api.DestroyImageView(_device, _imageView, null);
        Api.DestroyImage(_device, InternalHandle, null);
        if (_imageMemory.Handle != 0) Api.FreeMemory(_device, _imageMemory, null);
        _imageView = default;
        InternalHandle = default;
        _imageMemory = default;
    }
}
