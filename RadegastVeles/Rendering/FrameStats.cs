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

namespace Radegast.Veles.Rendering;

/// <param name="IntervalMaxMs">Largest wall-clock gap between consecutive <c>BeginFrame</c>
/// calls over a rolling window (~120 frames) -- the metric that actually answers "is it
/// choppy": CPU/GPU ms alone can drop once frame-in-flight pipelining lands (plan Step 6)
/// whether or not the user-visible frame pacing actually improves.</param>
/// <param name="IntervalP99Ms">99th-percentile frame interval over the same window -- less
/// sensitive to a single one-off spike than <see cref="IntervalMaxMs"/>, so a sustained
/// choppiness regression shows up here even when the max is dominated by one rare outlier.</param>
/// <param name="ObjectsOcclusionCulled">Count of scene objects newly marked occluded by the most
/// recent occlusion-query readback cycle (see <c>VkViewportControl.ReadOcclusionResults</c>) --
/// not a running total (the durable running state is <c>_occludedSceneKeys</c> itself), just this
/// cycle's outcome. Always 0 on Low tier or with occlusion culling disabled.</param>
public readonly record struct FrameStats(
    double CpuTimeMs,
    double GpuTimeMs,
    int    DrawCalls,
    int    Triangles,
    int    FacesSubmitted,
    int    FacesCulled,
    double IntervalMaxMs,
    double IntervalP99Ms,
    int    ObjectsOcclusionCulled = 0);

public interface IFrameStatsTracker
{
    /// <summary>The most recent published <see cref="FrameStats"/> value.</summary>
    FrameStats Last { get; }

    /// <summary>Fired (on the render thread) once per frame after that frame's stats are ready.</summary>
    event Action<FrameStats>? FrameCompleted;
}
