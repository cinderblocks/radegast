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

// New concept, no direct GL equivalent -- see plan Section 5's pipeline table, row 1. GL has
// no render-pass object (an FBO's attachments are just bound directly); Vulkan needs an
// explicit VkRenderPass describing attachment formats/load-store behavior up front, which
// every compatible VkPipeline and VkFramebuffer is then created against.

using Silk.NET.Vulkan;

namespace Radegast.Veles.Rendering;

internal static class VkRenderPass
{
    /// <summary>
    /// Creates the main scene render pass: one color attachment (matching the
    /// interop-exported render-target image format negotiated in Phase 0 --
    /// <c>R8G8B8A8_UNORM</c>, see <c>experiments/VulkanEmbeddingSpike</c>) plus one depth
    /// attachment. Used by the prim/wireframe/picking/particle/navmesh-overlay/water/sky
    /// pipelines (plan Section 5's table rows 1-5, 10-11) -- all rendering into the same
    /// target, differing only in pipeline state, not attachments, so they share one
    /// render-pass-compatible object per the table's row-1 note about viewport/scissor
    /// already being dynamic state.
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
            // FinalLayout = ColorAttachmentOptimal, matching subpass state, NOT
            // TransferSrcOptimal. Read directly from the validated Phase 0 spike's
            // VulkanContent.CreateTemporalObjects/Render (experiments/VulkanEmbeddingSpike) --
            // its render pass also ends in ColorAttachmentOptimal, and Render() does a
            // SEPARATE explicit vkCmdPipelineBarrier transition to TransferSrcOptimal after
            // CmdEndRenderPass, right before the blit to the interop swapchain image. Setting
            // FinalLayout=TransferSrcOptimal directly here would need a matching Transfer-stage
            // subpass dependency this render pass doesn't declare -- a real synchronization
            // gap, not just a style difference. The render loop code owns the post-render-pass
            // transition; this render pass does not attempt it implicitly.
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
    /// G-buffer normal pre-pass (plan Section 8c-2b, pipeline table row 6: prim.vert +
    /// gnorm.frag): one color attachment (packed view-space normal) + one depth attachment,
    /// both left in <c>ShaderReadOnlyOptimal</c> so the following SSAO pass can sample them as
    /// textures. Unlike <see cref="CreateMainScenePass"/>, this render pass -- and the SSAO/blur
    /// ones below -- are read by ANOTHER pass within the SAME frame's command buffer, not just
    /// presented, so they need TWO subpass dependencies, not one:
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
    /// (plan Section 5's pipeline table row 7) and the SSAO-blur pass (row 8), which share this
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
    /// Depth-only shadow-caster render pass (plan Section 8c-3, pipeline table row 9): a single
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
