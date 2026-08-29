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

// Adapted from Avalonia's own samples/GpuInterop/VulkanDemo (MIT licensed,
// https://github.com/AvaloniaUI/Avalonia)'s VulkanContext.cs. Trimmed from the original: no
// GRContext/SkiaSharp interop (that was the sample's own texture-dump debug feature, not part
// of the render/present pipeline itself).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Microsoft.Extensions.Logging;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Holds the single, shared Vulkan instance/device/queue used by every Veles Vulkan-backed
/// viewer panel. Unlike <see cref="GlApi"/>'s pattern (one GL context per panel, each
/// initialised independently by Avalonia's <c>OpenGlControlBase</c>), there is exactly one
/// <see cref="VkContext"/> for the whole process: device/instance creation happens once, at
/// first-panel-construction, and every subsequent panel reuses it. Construct via
/// <see cref="TryCreate"/>, never directly.
/// </summary>
internal sealed unsafe class VkContext : IDisposable
{
    // The single canonical knob every N-buffered piece of per-panel state (VkFrameStatsTracker's
    // query pools, VkPrimDescriptorSets' per-frame UBO/FrameSet, VkFrameReapRing's slots) is
    // sized against.
    //
    // Attempt #7 at N=2 (2026-08-27), retrying a formally-closed decision: the six attempts on
    // 2026-08-20/21 (see plan mossy-sleeping-knuth.md) each hung under heavy load at a different,
    // never-repeating call site, and were closed back to N=1 with an explicit note that a future
    // attempt should start from a fresh diagnostic pass rather than re-adding the same
    // now-stripped bracketing (VkCommandBufferPool.WaitForFenceCore's before/after timing,
    // VkFrameStatsTracker.EndFrame's query-readback bracketing, RenderFrame's four checkpoint
    // log lines) and retrying blind -- so this flip deliberately does NOT restore any of that.
    // The new input this time is VK_LAYER_KHRONOS_validation itself (VELES_VK_VALIDATION=1,
    // gated in TryCreate below), which the plan's own validation-layers section notes was
    // implemented but never actually exercised live against a real N=2 hang -- core validation
    // "catches most of steps 4-6's risk surface... for free," so a genuine synchronization
    // violation should surface as a [VkValidation] log line instead of requiring another round
    // of manual bisection. Sync validation (VELES_VK_SYNC_VALIDATION=1) is deliberately NOT the
    // first thing to reach for -- the plan flags it as slow enough to make a populated
    // SceneViewer region look hung on its own, which would contaminate this exact repro; try
    // core validation alone against the real heavy-load repro first, and reach for sync
    // validation only against a smaller PrimViewer/AvatarViewer repro if core validation stays
    // silent.
    //
    // If this hangs again: do not re-instrument blind. Check Veles.log for a [VkValidation]
    // line first -- if one exists, it names the actual spec violation. If the log is silent and
    // the render thread is still stuck, that's already new information (rules out anything core
    // validation can catch), and the next call is the user's on whether to keep chasing this or
    // revert to 1 again.
    public const int FramesInFlight = 2;

    public required Vk Api { get; init; }
    public required Instance Instance { get; init; }
    public required PhysicalDevice PhysicalDevice { get; init; }
    public required Device Device { get; init; }
    public required Queue Queue { get; init; }
    public required uint QueueFamilyIndex { get; init; }
    public required VkCommandBufferPool Pool { get; init; }
    public required DescriptorPool DescriptorPool { get; init; }

    // Gated on VELES_VK_VALIDATION=1 (VK_LAYER_KHRONOS_validation, availability-checked, never a
    // hard requirement) and separately VELES_VK_SYNC_VALIDATION=1 (VK_VALIDATION_FEATURE_ENABLE_
    // SYNCHRONIZATION_VALIDATION_EXT layered on top -- slow enough that a populated SceneViewer
    // may be unusable with it on, so kept independently toggleable rather than bundled with core
    // validation). _debugCallback is stored as an
    // instance field, not a local, because native code holds a raw pointer into this managed
    // delegate for as long as _debugMessenger exists -- letting it go out of scope would leave a
    // dangling callback the moment the GC decides to collect it.
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;
    private DebugUtilsMessengerCallbackFunctionEXT? _debugCallback;

    /// <summary>
    /// Whether this device can sample a BC3 (S3TC DXT5) compressed image, checked once here
    /// rather than per-texture -- <see cref="VkTexture"/>'s compressed-upload constructor
    /// requires this. <c>textureCompressionBC</c> is a widely-supported desktop feature but not
    /// universal (mobile/ARM GPUs typically lack it), so callers must gate on this before
    /// looking up a <c>TextureDiskCache</c> compressed-tier entry, falling back to the
    /// uncompressed RGBA8 upload path when false.
    /// </summary>
    public required bool SupportsBc3 { get; init; }

    /// <summary>Assigned right after construction in <see cref="TryCreate"/>, not via object
    /// initializer -- its constructor needs a fully-built <see cref="VkContext"/> to pass to
    /// <see cref="VkBufferHelper"/>, so it can't be a <c>required init</c> property set inline
    /// alongside the others above. Never null on any <see cref="VkContext"/> a caller can
    /// observe. See <see cref="VkMaterialUboPool"/>'s own header comment for why it exists.</summary>
    public VkMaterialUboPool MaterialUboPool { get; private set; } = null!;

    /// <summary>
    /// One shared sampler for every <see cref="VkTexture"/> in the process, replacing what used
    /// to be a fresh <c>vkCreateSampler</c> call per texture instance. Real find, not a
    /// hypothesis: <c>VK_LAYER_KHRONOS_validation</c> caught <c>vkCreateSampler(): Number of
    /// currently valid sampler objects (4000) is not less than the maximum allowed (4000)</c>
    /// live, in a dense region -- <see cref="VkTexture.CreateViewAndSampler"/>'s own sampler
    /// parameters (linear filter/mipmap, repeat wrap, opaque-black border) are 100% fixed across
    /// every texture; the only thing that ever varied per-instance was <c>MaxLod</c>, set to
    /// that texture's own mip count. Vulkan clamps <c>MaxLod</c> against whatever mip count the
    /// BOUND image view actually has at sample time, so one sampler with a generously large
    /// <c>MaxLod</c> (comfortably above any real SL texture's mip chain) is correct for every
    /// texture regardless of its own level count -- this eliminates the whole
    /// maxSamplerAllocationCount ceiling (thousands of samplers down to 1) rather than just
    /// raising it. Assigned right after construction in <see cref="TryCreate"/>, same pattern as
    /// <see cref="MaterialUboPool"/>; owned by this VkContext, destroyed once in
    /// <see cref="Dispose"/> -- <see cref="VkTexture.Dispose"/> must NOT destroy it.
    /// </summary>
    public Sampler SharedTextureSampler { get; private set; }

    /// <summary>Non-null only on the Mode B (D3D11 cross-import) path, when Avalonia's
    /// compositor backend doesn't advertise native Vulkan handle sharing and render-target
    /// images must be exported as DXGI shared handles instead.</summary>
    public required ComPtr<ID3D11Device> D3DDevice { get; init; }

    /// <summary>
    /// Creates the shared Vulkan instance + device, negotiating whichever external
    /// memory/semaphore handle type <paramref name="gpuInterop"/> (Avalonia's compositor GPU
    /// interop feature for whatever backend it's actually running under) advertises support
    /// for. Works under both integration modes: Mode A
    /// (Avalonia's own compositor on Vulkan, native <c>VulkanOpaqueNtHandle</c> sharing) and
    /// Mode B (Avalonia on its default ANGLE/D3D11 backend, cross-import via
    /// <c>D3D11TextureNtHandle</c>) -- the branch on <c>SupportedImageHandleTypes</c> below is
    /// what picks between them at runtime; nothing here hardcodes one mode.
    /// </summary>
    public static (VkContext? result, string info) TryCreate(ICompositionGpuInterop gpuInterop)
    {
        using var appName = new VkByteString("RadegastVeles");
        var applicationInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = appName,
            ApiVersion = new Version32(1, 1, 0),
            PEngineName = appName,
            EngineVersion = new Version32(1, 0, 0),
            ApplicationVersion = new Version32(1, 0, 0)
        };

        var enabledExtensions = new List<string> { "VK_KHR_get_physical_device_properties2" };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            enabledExtensions.Add("VK_KHR_portability_enumeration");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            enabledExtensions.AddRange(["VK_KHR_external_memory_capabilities", "VK_KHR_external_semaphore_capabilities"]);

        var api = Vk.GetApi();

        // Dev-only, off by default: VK_LAYER_KHRONOS_validation is availability-checked, never a
        // hard requirement, so a machine without the Vulkan SDK installed just logs a warning and
        // runs unvalidated instead of failing to start. Sync validation is gated separately
        // (VELES_VK_SYNC_VALIDATION) since it's expensive enough to make a populated SceneViewer
        // unusable -- validate against PrimViewer/AvatarViewer's smaller submissions instead.
        var wantValidation = Environment.GetEnvironmentVariable("VELES_VK_VALIDATION") == "1";
        var wantSyncValidation = wantValidation && Environment.GetEnvironmentVariable("VELES_VK_SYNC_VALIDATION") == "1";
        var enabledLayers = new List<string>();
        var validationEnabled = false;
        if (wantValidation)
        {
            if (IsInstanceLayerAvailable(api, "VK_LAYER_KHRONOS_validation"))
            {
                enabledLayers.Add("VK_LAYER_KHRONOS_validation");
                validationEnabled = true;
                if (IsInstanceExtensionAvailable(api, ExtDebugUtils.ExtensionName))
                {
                    enabledExtensions.Add(ExtDebugUtils.ExtensionName);
                }
                else
                {
                    LibreMetaverse.Logger.Log(
                        "[VkContext] VELES_VK_VALIDATION=1 but VK_EXT_debug_utils is unavailable -- "
                        + "validation layer will run with no message sink wired up", LogLevel.Warning);
                }
            }
            else
            {
                LibreMetaverse.Logger.Log(
                    "[VkContext] VELES_VK_VALIDATION=1 but VK_LAYER_KHRONOS_validation is not "
                    + "available (Vulkan SDK not installed?) -- continuing without validation", LogLevel.Warning);
            }
        }

        Device device = default;
        DescriptorPool descriptorPool = default;
        VkCommandBufferPool? pool = null;
        ExtDebugUtils? debugUtils = null;
        DebugUtilsMessengerEXT debugMessenger = default;
        DebugUtilsMessengerCallbackFunctionEXT? debugCallback = null;
        var success = false;
        try
        {
            using var pRequiredExtensions = new VkByteStringList(enabledExtensions);
            using var pEnabledLayers = new VkByteStringList(enabledLayers);

            // Only wired up when sync validation is explicitly requested on top of core
            // validation -- VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT catches
            // exactly the host-visible memcpy/descriptor-rewrite-while-pending races steps 4-6 of
            // the frame-in-flight plan are working through.
            var syncFeature = ValidationFeatureEnableEXT.SynchronizationValidationExt;
            var validationFeatures = new ValidationFeaturesEXT
            {
                SType = StructureType.ValidationFeaturesExt,
                EnabledValidationFeatureCount = 1,
                PEnabledValidationFeatures = &syncFeature
            };

            var instanceCreateInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PNext = (validationEnabled && wantSyncValidation) ? &validationFeatures : null,
                PApplicationInfo = &applicationInfo,
                PpEnabledExtensionNames = pRequiredExtensions,
                EnabledExtensionCount = pRequiredExtensions.UCount,
                PpEnabledLayerNames = pEnabledLayers,
                EnabledLayerCount = pEnabledLayers.UCount,
                Flags = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? InstanceCreateFlags.EnumeratePortabilityBitKhr : default
            };

            api.CreateInstance(in instanceCreateInfo, null, out var vkInstance).ThrowOnError();

            if (validationEnabled && api.TryGetInstanceExtension<ExtDebugUtils>(vkInstance, out var extDebugUtils))
            {
                debugCallback = DebugCallback;
                var messengerInfo = new DebugUtilsMessengerCreateInfoEXT
                {
                    SType = StructureType.DebugUtilsMessengerCreateInfoExt,
                    MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt
                                    | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                                    | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt,
                    MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                                | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
                    PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(debugCallback)
                };
                if (extDebugUtils.CreateDebugUtilsMessenger(vkInstance, in messengerInfo, null, out debugMessenger) == Result.Success)
                {
                    debugUtils = extDebugUtils;
                    LibreMetaverse.Logger.Log(
                        $"[VkContext] Vulkan validation layer active (sync validation: {wantSyncValidation})", LogLevel.Information);
                }
                else
                {
                    LibreMetaverse.Logger.Log("[VkContext] Failed to create debug-utils messenger", LogLevel.Warning);
                }
            }

            var requireDeviceExtensions = new List<string>();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                requireDeviceExtensions.AddRange(["VK_KHR_external_memory", "VK_KHR_external_semaphore"]);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!(gpuInterop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle)
                      || gpuInterop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaqueNtHandle)))
                    return (null, "Image sharing is not supported by the current Avalonia rendering backend");
                requireDeviceExtensions.Add(KhrExternalMemoryWin32.ExtensionName);
                requireDeviceExtensions.Add(KhrExternalSemaphoreWin32.ExtensionName);
                requireDeviceExtensions.Add("VK_KHR_dedicated_allocation");
                requireDeviceExtensions.Add("VK_KHR_get_memory_requirements2");
            }
            else
            {
                if (!gpuInterop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor)
                    || !gpuInterop.SupportedSemaphoreTypes.Contains(KnownPlatformGraphicsExternalSemaphoreHandleTypes.VulkanOpaquePosixFileDescriptor))
                    return (null, "Image sharing is not supported by the current Avalonia rendering backend");
                requireDeviceExtensions.Add(KhrExternalMemoryFd.ExtensionName);
                requireDeviceExtensions.Add(KhrExternalSemaphoreFd.ExtensionName);
            }

            uint count = 0;
            api.EnumeratePhysicalDevices(vkInstance, ref count, null).ThrowOnError();
            var physicalDevices = stackalloc PhysicalDevice[(int)count];
            api.EnumeratePhysicalDevices(vkInstance, ref count, physicalDevices).ThrowOnError();

            for (uint c = 0; c < count; c++)
            {
                if (requireDeviceExtensions.Any(ext => !api.IsDeviceExtensionPresent(physicalDevices[c], ext)))
                    continue;

                var physicalDeviceIdProperties = new PhysicalDeviceIDProperties { SType = StructureType.PhysicalDeviceIDProperties };
                var physicalDeviceProperties2 = new PhysicalDeviceProperties2
                {
                    SType = StructureType.PhysicalDeviceProperties2,
                    PNext = &physicalDeviceIdProperties
                };
                api.GetPhysicalDeviceProperties2(physicalDevices[c], &physicalDeviceProperties2);

                if (gpuInterop.DeviceLuid != null && physicalDeviceIdProperties.DeviceLuidvalid)
                {
                    if (!new Span<byte>(physicalDeviceIdProperties.DeviceLuid, 8).SequenceEqual(gpuInterop.DeviceLuid))
                        continue;
                }
                else if (gpuInterop.DeviceUuid != null)
                {
                    if (!new Span<byte>(physicalDeviceIdProperties.DeviceUuid, 16).SequenceEqual(gpuInterop.DeviceUuid))
                        continue;
                }

                var physicalDevice = physicalDevices[c];

                uint queueFamilyCount = 0;
                api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, null);
                var familyProperties = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
                api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, familyProperties);

                for (uint queueFamilyIndex = 0; queueFamilyIndex < queueFamilyCount; queueFamilyIndex++)
                {
                    var family = familyProperties[queueFamilyIndex];
                    if (!family.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                        continue;

                    var queuePriorities = stackalloc float[(int)family.QueueCount];
                    for (var i = 0; i < family.QueueCount; i++) queuePriorities[i] = 1f;

                    var features = new PhysicalDeviceFeatures();
                    var queueCreateInfo = new DeviceQueueCreateInfo
                    {
                        SType = StructureType.DeviceQueueCreateInfo,
                        QueueFamilyIndex = queueFamilyIndex,
                        QueueCount = family.QueueCount,
                        PQueuePriorities = queuePriorities
                    };

                    using var pEnabledDeviceExtensions = new VkByteStringList(requireDeviceExtensions);
                    var deviceCreateInfo = new DeviceCreateInfo
                    {
                        SType = StructureType.DeviceCreateInfo,
                        QueueCreateInfoCount = 1,
                        PQueueCreateInfos = &queueCreateInfo,
                        PpEnabledExtensionNames = pEnabledDeviceExtensions,
                        EnabledExtensionCount = pEnabledDeviceExtensions.UCount,
                        PEnabledFeatures = &features
                    };

                    api.CreateDevice(physicalDevice, in deviceCreateInfo, null, out device).ThrowOnError();
                    api.GetDeviceQueue(device, queueFamilyIndex, 0, out var queue);

                    // MaxMemoryAllocationCount is a separate ceiling from the DescriptorPool
                    // sizing below: VkMesh does 2-3 AllocateMemory calls per face (vbo/ebo/
                    // +lebo) and VkMaterialDescriptorSet does 1 more (its UBO) -- neither
                    // suballocates, so a real scene's face count is directly bounded by this
                    // limit too.
                    api.GetPhysicalDeviceProperties(physicalDevice, out var deviceProps);
                    LibreMetaverse.Logger.Info(
                        $"[VkContext] Device limits: maxMemoryAllocationCount={deviceProps.Limits.MaxMemoryAllocationCount}, "
                        + $"maxDescriptorSetSamplers={deviceProps.Limits.MaxDescriptorSetSamplers}, "
                        + $"maxDescriptorSetUniformBuffers={deviceProps.Limits.MaxDescriptorSetUniformBuffers}");

                    // Descriptor scheme: set 0 per-frame UBO, set 1 per-pass samplers, set 2
                    // per-material, across ~13-15 pipeline variants.
                    //
                    // StorageBuffer covers skin-compute descriptor sets (VkAvatarSkinGpuData --
                    // 5 SSBO bindings per skinned avatar face) and flexi-compute descriptor sets
                    // (VkFlexiGpuData -- 3 SSBO bindings per face); each allocates one descriptor
                    // set per face at face-upload time and never rebinds it -- only the
                    // SkinMatsSSBO's contents change per animation tick, via a host-visible
                    // memory write, not a descriptor rebind. These are additional sets stacked
                    // on top of the scene-face budget below, not drawn from within it.
                    //
                    // UniformBuffer/CombinedImageSampler/MaxSets are sized for SceneViewer's
                    // per-face streaming: unlike PrimViewer/AvatarViewer's single bounded
                    // submission, SceneViewer streams a whole scene's worth of prims, each face
                    // allocating its own VkMaterialDescriptorSet (1 UBO + 5 samplers, see
                    // VkMaterialDescriptorSet.cs -- deliberately NOT deduplicated by material
                    // content). Sets are freed via vkFreeDescriptorSets on object removal
                    // (FreeDescriptorSetBit below), so this bounds *concurrently live*
                    // descriptor sets across the whole process (one shared VkContext/pool for
                    // all 4 panels), not a cumulative total. Sizing targets ~16000
                    // concurrently-live scene faces, budgeted by CombinedImageSampler at 5/face
                    // (the binding count observed to bind first, over UniformBuffer's larger
                    // per-face allowance).
                    //
                    // Sized for the lightweight region-crossing path
                    // (SceneViewerViewModel.HandleLightweightRegionPromotion), which keeps several
                    // already-built regions' worth of geometry resident at once by design instead
                    // of clearing on every crossing -- a corner with 3-4 simultaneous tracked
                    // neighbors can legitimately need more descriptor sets than a single region
                    // alone would. This is still a stopgap, not a complete fix: nothing currently
                    // bounds how many resident regions accumulate over a long play session
                    // (SimDisconnected only fires when the server drops a child sim, not when one
                    // is merely "far enough away now"), so a long enough session can still exhaust
                    // any fixed budget. The real fix is per-material descriptor-set dedup (most
                    // faces in a real build share texture sets) or a resident-region eviction
                    // policy -- not attempted here.
                    //
                    // These numbers are a documented budget, not a load-bearing hard limit:
                    // VK_ERROR_OUT_OF_POOL_MEMORY detection at the declared per-type counts is
                    // spec-permitted but not guaranteed in practice. If exhausted again, check the
                    // maxMemoryAllocationCount logged just above first -- VkMesh/VkMaterialDescriptorSet
                    // do 3-4 AllocateMemory calls per face, a separate ceiling from this pool that a
                    // larger pool alone can't work around if the driver's allocation-count cap is
                    // what's actually being hit at this face count.
                    var poolSizes = stackalloc DescriptorPoolSize[3]
                    {
                        new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = 16384 },
                        new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 81920 },
                        new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = 8192 },
                    };
                    var descriptorPoolInfo = new DescriptorPoolCreateInfo
                    {
                        SType = StructureType.DescriptorPoolCreateInfo,
                        PoolSizeCount = 3,
                        PPoolSizes = poolSizes,
                        MaxSets = 20480,
                        Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                    };
                    api.CreateDescriptorPool(device, &descriptorPoolInfo, null, out descriptorPool).ThrowOnError();

                    pool = new VkCommandBufferPool(api, device, queue, queueFamilyIndex);

                    ComPtr<ID3D11Device> d3dDevice = null;
                    if (physicalDeviceIdProperties.DeviceLuidvalid
                        && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        && !gpuInterop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaqueNtHandle))
                    {
                        // Mode B: this backend can't share native Vulkan handles, so mint a
                        // D3D11 device on the same physical adapter (matched by LUID) purely
                        // to create DXGI-shared render-target textures instead.
                        d3dDevice = VkD3DMemoryHelper.CreateDeviceByLuid(
                            MemoryMarshal.Read<Luid>(new Span<byte>(physicalDeviceIdProperties.DeviceLuid, 8)));
                    }

                    var name = Marshal.PtrToStringAnsi(new IntPtr(physicalDeviceProperties2.Properties.DeviceName))!;

                    // BC3 needs SAMPLED_IMAGE support under optimal tiling -- the feature bit
                    // alone (PhysicalDeviceFeatures.TextureCompressionBc) doesn't guarantee any
                    // particular usage is actually supported, so query the format directly.
                    api.GetPhysicalDeviceFormatProperties(physicalDevice, Format.BC3UnormBlock, out var bc3Props);
                    bool supportsBc3 = bc3Props.OptimalTilingFeatures.HasFlag(FormatFeatureFlags.SampledImageBit);

                    var vkContext = new VkContext
                    {
                        Api = api,
                        Device = device,
                        Instance = vkInstance,
                        PhysicalDevice = physicalDevice,
                        Queue = queue,
                        QueueFamilyIndex = queueFamilyIndex,
                        Pool = pool,
                        DescriptorPool = descriptorPool,
                        SupportsBc3 = supportsBc3,
                        D3DDevice = d3dDevice
                    };
                    vkContext._debugUtils = debugUtils;
                    vkContext._debugMessenger = debugMessenger;
                    vkContext._debugCallback = debugCallback;
                    // Capacity matches the UniformBuffer DescriptorPoolSize above (16384, not
                    // MaxSets=20480 -- MaxSets is a looser shared ceiling across every descriptor
                    // set kind this pool serves, not this pool's own binding constraint). Each
                    // live scene face rents exactly one slot here (one VkMaterialDescriptorSet =
                    // one UniformBuffer descriptor), so this MUST track the DescriptorPool's own
                    // UniformBuffer count above -- sizing it any lower makes this pool the
                    // effective face-count ceiling instead of the DescriptorPool, and since an
                    // object's entire face set is dropped together on the first Rent() failure
                    // (see UploadSceneObjectNoRebuild's catch path), objects needing many faces at
                    // once are hit hardest by that kind of undersizing.
                    vkContext.MaterialUboPool = new VkMaterialUboPool(vkContext, capacity: 16384);

                    // See SharedTextureSampler's own doc comment: one sampler for every
                    // VkTexture in the process, replacing what used to be a fresh
                    // vkCreateSampler call per texture instance. Parameters copied verbatim from
                    // VkTexture.CreateViewAndSampler's own (now-removed) per-instance call --
                    // MaxLod=16 is a fixed generous ceiling (any real SL texture's mip chain
                    // tops out around 12-13 levels for the largest supported resolution).
                    var sharedSamplerInfo = new SamplerCreateInfo
                    {
                        SType = StructureType.SamplerCreateInfo,
                        MagFilter = Silk.NET.Vulkan.Filter.Linear,
                        MinFilter = Silk.NET.Vulkan.Filter.Linear,
                        MipmapMode = SamplerMipmapMode.Linear,
                        AddressModeU = SamplerAddressMode.Repeat,
                        AddressModeV = SamplerAddressMode.Repeat,
                        AddressModeW = SamplerAddressMode.Repeat,
                        MinLod = 0,
                        MaxLod = 16,
                        BorderColor = BorderColor.IntOpaqueBlack,
                    };
                    api.CreateSampler(device, in sharedSamplerInfo, null, out var sharedSampler).ThrowOnError();
                    vkContext.SharedTextureSampler = sharedSampler;

                    // success is set only after MaterialUboPool's own allocation succeeds, so
                    // the finally block below still tears down pool/descriptorPool/device if
                    // that construction throws instead of leaking them.
                    success = true;
                    return (vkContext, name);
                }
            }

            return (null, "No suitable Vulkan device/queue found");
        }
        catch (Exception e)
        {
            return (null, e.ToString());
        }
        finally
        {
            if (!success)
            {
                pool?.Dispose();
                if (descriptorPool.Handle != default) api.DestroyDescriptorPool(device, descriptorPool, null);
                if (device.Handle != default) api.DestroyDevice(device, null);
            }
        }
    }

    private static bool IsInstanceLayerAvailable(Vk api, string layerName)
    {
        uint count = 0;
        if (api.EnumerateInstanceLayerProperties(ref count, null) != Result.Success || count == 0) return false;
        var layers = stackalloc LayerProperties[(int)count];
        if (api.EnumerateInstanceLayerProperties(ref count, layers) != Result.Success) return false;
        for (uint i = 0; i < count; i++)
        {
            if (Marshal.PtrToStringAnsi(new IntPtr(layers[i].LayerName)) == layerName) return true;
        }
        return false;
    }

    private static bool IsInstanceExtensionAvailable(Vk api, string extensionName)
    {
        uint count = 0;
        if (api.EnumerateInstanceExtensionProperties((byte*)null, ref count, null) != Result.Success || count == 0) return false;
        var extensions = stackalloc ExtensionProperties[(int)count];
        if (api.EnumerateInstanceExtensionProperties((byte*)null, ref count, extensions) != Result.Success) return false;
        for (uint i = 0; i < count; i++)
        {
            if (Marshal.PtrToStringAnsi(new IntPtr(extensions[i].ExtensionName)) == extensionName) return true;
        }
        return false;
    }

    private static uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT severity, DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        var message = Marshal.PtrToStringAnsi(new IntPtr(pCallbackData->PMessage)) ?? "(no message)";
        var level = severity switch
        {
            DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt => LogLevel.Error,
            DebugUtilsMessageSeverityFlagsEXT.WarningBitExt => LogLevel.Warning,
            _ => LogLevel.Debug
        };
        LibreMetaverse.Logger.Log($"[VkValidation] {message}", level);
        return 0u;
    }

    public void Dispose()
    {
        if (_debugUtils != null && _debugMessenger.Handle != default)
            _debugUtils.DestroyDebugUtilsMessenger(Instance, _debugMessenger, null);
        D3DDevice.Dispose();
        Pool.Dispose();
        MaterialUboPool.Dispose();
        if (SharedTextureSampler.Handle != default) Api.DestroySampler(Device, SharedTextureSampler, null);
        Api.DestroyDescriptorPool(Device, DescriptorPool, null);
        Api.DestroyDevice(Device, null);
    }
}
