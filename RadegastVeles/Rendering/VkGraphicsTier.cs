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

// A coarse, one-time-at-startup GPU capability classification -- NOT a live memory-pressure
// signal (that would need VK_EXT_memory_budget, re-queried periodically; deliberately not
// attempted here, see VkContext.DetectGraphicsTier's own doc comment for why a static
// total-VRAM heuristic was chosen instead). Exists specifically because 2026-09-03/04 field
// data showed a real regression on an NVIDIA GeForce GTX 760 (a 2013, 2-4GB card): a scene
// already pinned at VkMaterialUboPool's 16384-face cap (0-1 free slots for most of a session)
// went from "graceful per-object drops" to "repeated real ErrorOutOfDeviceMemory, swapchain
// image creation failing, multi-second frozen/black frames" purely from the tonemap/bloom
// post-process chain's own fixed ~12.5MB VRAM addition (see VkTonemapPipeline's own doc
// comment for that chain). The fix isn't a smaller version of the feature on old hardware --
// it's not paying for it at all, restoring the exact pre-tonemap zero-added-VRAM render path.
public enum VkGraphicsTier
{
    /// <summary>Low VRAM (below <see cref="VkContext.LowTierVramCeilingBytes"/>, e.g. a 2-4GB
    /// card like the GTX 760 this tier was written for). The HDR post-process chain
    /// (VkTonemapPipeline/VkBloomExtractPipeline/VkBloomBlurPipeline, plus the
    /// B10G11R11UfloatPack32 offscreen buffers they read/write) is skipped ENTIRELY --
    /// VkViewportControl's main scene pass renders straight into the swapchain image at its
    /// native R8G8B8A8Unorm, exactly as it did before that work existed. Zero added VRAM,
    /// zero added render passes -- not a cut-down version of the feature.</summary>
    Low,

    /// <summary>Moderate VRAM. HDR buffer + tonemap only (fixes hard-clipped highlights, e.g.
    /// the sun disc's own &gt;1.0 core, which a UNORM target could only ever hard-clip to flat
    /// white) -- bloom's own two extra offscreen targets are skipped, since bloom is the
    /// "nice to have" half of the feature and tonemap alone is roughly half this chain's total
    /// VRAM cost (~8.3MB vs ~12.5MB at 1080p, both figures for the B10G11R11 buffers).</summary>
    Medium,

    /// <summary>Plenty of VRAM. The full HDR + bloom + tonemap chain, unchanged from how it
    /// shipped.</summary>
    High
}
