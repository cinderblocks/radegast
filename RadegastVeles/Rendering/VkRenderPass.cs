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

// New concept, no direct GL equivalent. GL has
// no render-pass object (an FBO's attachments are just bound directly); Vulkan needs an
// explicit VkRenderPass describing attachment formats/load-store behavior up front, which
// every compatible VkPipeline and VkFramebuffer is then created against.

using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal static class VkRenderPass
{
    /// <summary>
    /// Creates the main scene render pass: one color attachment plus one depth attachment. Used
    /// by the prim/wireframe/picking/particle/navmesh-overlay/water/sky pipelines -- all
    /// rendering into the same target, differing only in pipeline state, not attachments, so
    /// they share one render-pass-compatible object (viewport/scissor are dynamic state, so the
    /// render pass itself doesn't need to vary for that).
    /// <para>
    /// ONLY used on <see cref="VkGraphicsTier.Medium"/>/<see cref="VkGraphicsTier.High"/> --
    /// see <see cref="CreateMainScenePassDirect"/> for the <see cref="VkGraphicsTier.Low"/>
    /// counterpart this render pass has no role in. The color attachment here targets a
    /// PERSISTENT offscreen HDR buffer (<c>B10G11R11_UFLOAT_PACK32</c>,
    /// <c>VkViewportControl._hdrColorImage</c>), not the swapchain image directly -- the
    /// interop-exported swapchain image is hard-locked to <c>R8G8B8A8_UNORM</c> by Avalonia's
    /// GPU-interop layer (see <c>VkInteropSwapchain</c>'s own image-creation call), which cannot
    /// hold the over-1.0 values bloom needs to threshold against or the highlight rolloff the
    /// tonemap pass provides. <c>FinalLayout = ShaderReadOnlyOptimal</c> (not
    /// <c>ColorAttachmentOptimal</c>, which is what this used to be before the HDR buffer existed
    /// and this render pass wrote the swapchain image directly): this pass's output is now ALWAYS
    /// sampled next -- either by the underwater pass's copy-out (if underwater is active) or
    /// directly by the tonemap/bloom chain's first stage (VkBloomExtractPipeline) -- never
    /// presented, so it needs the same "reused across frames AND read by a later pass this frame"
    /// two-dependency shape <see cref="CreateGBufferPass"/> already documents, not the simpler
    /// single-dependency shape a truly-terminal, presented-every-frame target needs (see
    /// <see cref="CreateTonemapOutputPass"/>, which now owns that role for the real swapchain
    /// image instead).
    /// </para>
    /// </summary>
    public static unsafe RenderPass CreateMainScenePass(VkContext vk, Format colorFormat, Format depthFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        // Two dependencies, same reasoning as CreateGBufferPass's own (see that method's doc
        // comment) -- the color attachment is a persistent, cross-frame-reused image that's also
        // read later THIS frame, unlike a plain presented target. Depth is write-only (never
        // sampled by anything, matching _depthImage's own existing "write-only" note elsewhere),
        // so only the color half of the exit dependency needs a fragment-shader read stage.
        var dependencies = stackalloc SubpassDependency[2]
        {
            new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.FragmentShaderBit,
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            },
            new SubpassDependency
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstStageMask = PipelineStageFlags.FragmentShaderBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            }
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// <see cref="VkGraphicsTier.Low"/>'s main scene render pass: writes the REAL swapchain
    /// image directly, exactly like <see cref="CreateMainScenePass"/> itself did before the HDR-
    /// buffer/tonemap work existed (see that method's own doc comment) -- restores that exact
    /// zero-added-VRAM contract for hardware <see cref="VkContext.DetectGraphicsTier"/>
    /// classifies too VRAM-constrained to afford the offscreen HDR buffer at all, not a smaller
    /// version of it. Same shape as <see cref="CreatePickPass"/> (color+depth,
    /// <c>FinalLayout=ColorAttachmentOptimal</c>, single entry-only dependency guarding the
    /// swapchain image pool's cross-frame reuse) for the same reason: this output is presented,
    /// never sampled by a later pass the same frame -- there IS no later pass on this tier, the
    /// tonemap/bloom chain is skipped entirely, and underwater (if active) reads/writes this same
    /// swapchain image directly afterward exactly as it always has.
    /// </summary>
    public static unsafe RenderPass CreateMainScenePassDirect(VkContext vk, Format colorFormat, Format depthFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// First half of the main scene pass, split in two ONLY on frames where water is visible and
    /// refraction needs a pre-water snapshot of the opaque scene colour (<c>vkCmdCopyImage</c>
    /// isn't legal inside an active render pass -- see <see cref="CreateMainScenePassContinuation"/>'s
    /// own doc comment for the full two-pass picture). Draws sky + opaque geometry only, then ends
    /// -- the caller copies <c>_hdrColorImage</c> out, transitions it back, and begins the
    /// continuation pass for water/alpha/overlays.
    /// <para>
    /// Same attachment shape as <see cref="CreateMainScenePass"/> (this is still the frame's FIRST
    /// write -- same entry dependency, guarding the persistent, cross-frame-reused <c>_hdrColorImage</c>/
    /// depth against the PREVIOUS frame's fragment-shader read) except <c>FinalLayout =
    /// ColorAttachmentOptimal</c>, not <c>ShaderReadOnlyOptimal</c>: this pass's output is read by
    /// the very next thing in this same command buffer (the manual copy-out transition the caller
    /// records immediately after <c>CmdEndRenderPass</c>), never by a later pass's fragment shader,
    /// so no exit dependency is needed -- same reasoning <see cref="CreateMainScenePassDirect"/>
    /// already documents for its own single-dependency shape.
    /// </para>
    /// </summary>
    public static unsafe RenderPass CreateMainScenePassOpaque(VkContext vk, Format colorFormat, Format depthFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            // Store, NOT DontCare (unlike CreateMainScenePass's own depth attachment, which is
            // genuinely never read again): CreateMainScenePassContinuation's depth attachment
            // LoadOp=Load reads THIS pass's depth buffer back. DontCare would leave that load
            // reading undefined contents -- no validation error for it, just silent depth-test/
            // depth-write corruption that happens to "work" on GPUs that leave the memory intact.
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ShaderReadBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// Second half of the main scene pass split (see <see cref="CreateMainScenePassOpaque"/>'s own
    /// doc comment for why this split exists and the full sequence around it): continues into the
    /// SAME <c>_hdrColorImage</c>/depth attachments <see cref="CreateMainScenePassOpaque"/> just
    /// wrote (<c>LoadOp = Load</c> on both -- no clear, nothing is discarded), draws water + alpha
    /// + wireframe/selection-outline/particle overlays, then ends. This pass, not the opaque half,
    /// now owns the <c>FinalLayout = ShaderReadOnlyOptimal</c> transition <see cref="CreateMainScenePass"/>
    /// used to own directly -- it's the frame's actual last write to <c>_hdrColorImage</c>, so the
    /// same two-dependency "cross-frame-reused AND read later this frame" shape moves here.
    /// <para>
    /// Depth needs no copy/manual barrier at all (unlike color): <c>InitialLayout = FinalLayout =
    /// DepthStencilAttachmentOptimal</c>, identical on both sides of the split, so ordinary
    /// entry-dependency ordering against <see cref="CreateMainScenePassOpaque"/>'s own depth write
    /// (same command buffer, submission-order-adjacent) is all that's needed -- no explicit
    /// transition, since the layout never actually changes.
    /// </para>
    /// <para>
    /// Entry dependency's source side combines TWO distinct things this pass picks up after: the
    /// caller's own manual colour transition-back (<c>TransferSrcOptimal -&gt; ColorAttachmentOptimal</c>,
    /// recorded immediately before <c>CmdBeginRenderPass</c> for this pass, mirroring
    /// <see cref="CreateUnderwaterPass"/>'s identical "picks up after an external transition, not a
    /// cross-frame guard" entry-dependency shape) AND <see cref="CreateMainScenePassOpaque"/>'s own
    /// depth write completing -- both must be ordered-before this pass's own attachment writes.
    /// </para>
    /// </summary>
    public static unsafe RenderPass CreateMainScenePassContinuation(VkContext vk, Format colorFormat, Format depthFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        var dependencies = stackalloc SubpassDependency[2]
        {
            new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            },
            new SubpassDependency
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstStageMask = PipelineStageFlags.FragmentShaderBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            }
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// Creates the tonemap/bloom composite pass's render pass: one color attachment, no depth,
    /// targeting the REAL swapchain image -- this pass, not <see cref="CreateMainScenePass"/>
    /// (whose output now lands in an offscreen HDR buffer instead, see that method's own doc
    /// comment), is the thing that now writes what the compositor actually presents. Takes over
    /// the exact contract <see cref="CreateMainScenePass"/> used to have when IT wrote the
    /// swapchain image directly: <c>LoadOp = DontCare</c> (the full-screen quad this pass draws
    /// writes every pixel unconditionally, so there's nothing to preserve), single entry
    /// dependency only (this output is truly terminal -- presented, never sampled by a later pass
    /// the same frame, matching <see cref="CreateUnderwaterPass"/>'s own single-dependency
    /// reasoning), <c>FinalLayout = ColorAttachmentOptimal</c> (matching
    /// <c>VkInteropSwapchainImage.Present()</c>'s own expectation, and what
    /// <see cref="CreateUnderwaterPass"/>'s <c>InitialLayout</c> still assumes when underwater is
    /// active and runs after this pass).
    /// </summary>
    public static unsafe RenderPass CreateTonemapOutputPass(VkContext vk, Format colorFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        var attachments = stackalloc AttachmentDescription[1] { colorAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// The pick pass's own render pass: one color attachment (a flat-shaded, unique-per-face ID
    /// colour, read back via <c>vkCmdCopyImageToBuffer</c>) plus one depth attachment, targeting
    /// <c>VkViewportControl._pickColorImage</c>/<c>_pickDepthImage</c> -- its own dedicated
    /// R8G8B8A8Unorm target, NOT <see cref="CreateMainScenePass"/>'s (which is B10G11R11UfloatPack32
    /// since the HDR-buffer/tonemap work, a real format the pick shader's exact-byte ID encoding
    /// cannot survive; sharing that render pass object here was an actual regression caught before
    /// it shipped, not a hypothetical). This is otherwise IDENTICAL to what
    /// <see cref="CreateMainScenePass"/> itself used to be before that work: one subpass
    /// dependency (EXTERNAL -> 0 only), <c>FinalLayout = ColorAttachmentOptimal</c> on color --
    /// <c>RunPickPass</c> transitions it to <c>TransferSrcOptimal</c> itself via an explicit
    /// <c>CmdPipelineBarrier</c> right after <c>CmdEndRenderPass</c>, exactly like the swapchain
    /// image used to be handled (see that call site's own comment). No SECOND (exit) dependency
    /// is needed despite this target being reused across separate pick calls (unlike a true
    /// one-shot target): every <c>RunPickPass</c> call ends in <c>cmd.SubmitAndWait()</c>, a full
    /// CPU-GPU synchronous wait, before the method returns -- by the time a LATER pick call reuses
    /// this same image, the GPU has unconditionally finished with it already, so there is no
    /// write-after-read hazard for a subpass dependency to guard against in the first place.
    /// </summary>
    public static unsafe RenderPass CreatePickPass(VkContext vk, Format colorFormat, Format depthFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// Underwater post-process pass: one color attachment, no depth, targeting the SAME
    /// swapchain image the tonemap composite pass (<see cref="CreateTonemapOutputPass"/>) just
    /// wrote -- underwater runs AFTER tonemap now, as a plain LDR post-effect on the already-
    /// tonemapped frame, not a separate offscreen target of its own; this pass and its call site
    /// (<c>VkViewportControl.RenderUnderwaterPass</c>) are otherwise unchanged by the HDR-buffer/
    /// tonemap work -- see <see cref="CreateMainScenePass"/>'s own doc comment for why THAT pass
    /// stopped writing the swapchain image directly, and why this one didn't need to follow it
    /// offscreen too. <c>LoadOp = Load</c> (not <c>Clear</c>/<c>DontCare</c> like every other pass
    /// here) -- this pass composites a full-screen tint/distortion effect ON TOP of the already-
    /// rendered frame, it must not discard it. <c>InitialLayout = ColorAttachmentOptimal</c>,
    /// matching the actual layout the caller's own explicit <c>CmdPipelineBarrier</c> transitions
    /// the swapchain image back to (from <c>TransferSrcOptimal</c>, after copying it out to sample
    /// as the pass's input) immediately before <c>CmdBeginRenderPass</c> -- <c>LoadOp = Load</c>
    /// requires an accurate InitialLayout; <c>Undefined</c> would make the load's result
    /// undefined. <c>FinalLayout = ColorAttachmentOptimal</c> restores the same contract
    /// <see cref="CreateTonemapOutputPass"/>'s output already has: <c>VkInteropSwapchainImage.
    /// Present()</c> expects to find the image in that layout.
    /// <para>
    /// Only one subpass dependency, mirroring <see cref="CreateTonemapOutputPass"/>'s (not
    /// <see cref="CreateGBufferPass"/>'s two): this pass's output is presented, never sampled by
    /// a later pass in the same frame, so no exit dependency is needed. The entry dependency
    /// guards against the swapchain image pool's cross-FRAME reuse (same rationale as
    /// <see cref="CreateTonemapOutputPass"/>'s own dependency) -- ordering against the tonemap
    /// pass's write earlier in THIS frame is already fully handled by the caller's explicit
    /// barrier sequence (copy-out, then the transition back to ColorAttachmentOptimal)
    /// immediately preceding <c>CmdBeginRenderPass</c>.
    /// </para>
    /// </summary>
    public static unsafe RenderPass CreateUnderwaterPass(VkContext vk, Format colorFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        var attachments = stackalloc AttachmentDescription[1] { colorAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// G-buffer normal pre-pass (pipeline table row 6: prim.vert +
    /// gnorm.frag): one color attachment (packed view-space normal) + one depth attachment,
    /// both left in <c>ShaderReadOnlyOptimal</c> so the following SSAO pass can sample them as
    /// textures. Unlike <see cref="CreateTonemapOutputPass"/> (whose output really is only ever
    /// presented), this render pass -- and the SSAO/blur ones below, and (since the HDR-buffer/
    /// tonemap work) <see cref="CreateMainScenePass"/> too -- are read by ANOTHER pass within the
    /// SAME frame's command buffer, not just presented, so they need TWO subpass dependencies,
    /// not one:
    ///
    /// - Entry (EXTERNAL -> 0): the target images are reused across frames (like
    ///   <c>VkViewportControl</c>'s own depth target, not recreated per frame), so this
    ///   dependency orders this frame's attachment WRITE after the PREVIOUS frame's fragment-
    ///   shader READ of the same images (a write-after-read hazard the single-dependency main
    ///   pass doesn't have, since nothing ever samples the main pass's own color/depth output).
    /// - Exit (0 -> EXTERNAL): orders this pass's attachment WRITE before the FOLLOWING pass's
    ///   fragment-shader READ, later in the same command buffer (a read-after-write hazard).
    ///   Subpass dependencies targeting VK_SUBPASS_EXTERNAL act as a real barrier at the point
    ///   <c>vkCmdEndRenderPass</c> is recorded, in submission order -- covering later render
    ///   passes recorded after this one, not just non-render-pass commands.
    /// </summary>
    public static unsafe RenderPass CreateGBufferPass(VkContext vk, Format colorFormat, Format depthFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };
        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        var dependencies = stackalloc SubpassDependency[2]
        {
            new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.FragmentShaderBit,
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            },
            new SubpassDependency
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
                DstStageMask = PipelineStageFlags.FragmentShaderBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            }
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// A single-color-attachment, no-depth render pass for a full-screen-triangle pass whose
    /// output is sampled by a LATER pass this same frame -- reused for both the SSAO-raw pass
    /// and the SSAO-blur pass, which share this
    /// exact attachment shape (one R8Unorm color attachment, no depth). Same entry/exit
    /// subpass-dependency reasoning as <see cref="CreateGBufferPass"/> -- see its own doc
    /// comment. <c>LoadOp = DontCare</c>, not <c>Clear</c>: the full-screen triangle these
    /// pipelines draw writes every pixel unconditionally (both <c>ssao.frag</c>'s early-out and
    /// its main path reach a <c>fragColor</c> write; <c>ssaoblur.frag</c> has no early-out at
    /// all) -- confirmed by reading both shaders before choosing this, not assumed safe.
    /// </summary>
    public static unsafe RenderPass CreateOffscreenColorPass(VkContext vk, Format colorFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef
        };

        var dependencies = stackalloc SubpassDependency[2]
        {
            new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.FragmentShaderBit,
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit
            },
            new SubpassDependency
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstStageMask = PipelineStageFlags.FragmentShaderBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            }
        };

        var attachments = stackalloc AttachmentDescription[1] { colorAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }

    /// <summary>
    /// Depth-only shadow-caster render pass (pipeline table row 9): a single
    /// depth attachment, NO colour attachment at all -- shadow_depth.frag writes nothing, the
    /// GPU's fixed depth-test/write stage fills the texture from gl_Position alone (matching
    /// GL's own no-colour-attachment shadow FBO, see EnsureShadowFbo's completeness-check note).
    /// <c>FinalLayout = ShaderReadOnlyOptimal</c>, not
    /// <c>DepthStencilReadOnlyOptimal</c>: this depth image is consumed later the SAME frame as
    /// a COMBINED_IMAGE_SAMPLER (sampler2DShadow, shadow.glsl), not re-bound as a read-only
    /// depth attachment -- same reasoning <c>VkPlaceholderTextures.ShadowPlaceholder</c>'s own
    /// doc comment already established for the placeholder shadow maps. Two subpass
    /// dependencies, same entry/exit shape as <see cref="CreateGBufferPass"/> (the shadow target
    /// is reused across frames like the G-buffer targets, not recreated per frame, and is read
    /// by the main pass later in the SAME frame's command buffer) -- but scoped to the depth-
    /// only stages (EarlyFragmentTests/LateFragmentTests, DepthStencilAttachmentWrite) rather
    /// than colour-attachment stages, since there is no colour attachment here.
    /// </summary>
    public static unsafe RenderPass CreateShadowDepthPass(VkContext vk, Format depthFormat)
    {
        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var depthRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.DepthStencilAttachmentOptimal };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 0,
            PDepthStencilAttachment = &depthRef
        };

        var dependencies = stackalloc SubpassDependency[2]
        {
            new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.FragmentShaderBit,
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit
            },
            new SubpassDependency
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
                DstStageMask = PipelineStageFlags.FragmentShaderBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            }
        };

        var attachments = stackalloc AttachmentDescription[1] { depthAttachment };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        vk.Api.CreateRenderPass(vk.Device, in createInfo, null, out var renderPass).ThrowOnError();
        return renderPass;
    }
}
