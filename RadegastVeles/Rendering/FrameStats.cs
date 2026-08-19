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

public readonly record struct FrameStats(
    double CpuTimeMs,
    double GpuTimeMs,
    int    DrawCalls,
    int    Triangles,
    int    FacesSubmitted,
    int    FacesCulled);

public interface IFrameStatsTracker
{
    /// <summary>The most recent published <see cref="FrameStats"/> value.</summary>
    FrameStats Last { get; }

    /// <summary>Fired (on the render thread) once per frame after that frame's stats are ready.</summary>
    event Action<FrameStats>? FrameCompleted;
}
