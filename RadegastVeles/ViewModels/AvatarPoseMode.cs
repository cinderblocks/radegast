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

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Selects the pose source displayed in the avatar viewer.
/// The integer values must remain stable — they map directly to
/// <c>ComboBox.SelectedIndex</c> in <c>AvatarViewerPanel.axaml</c>.
/// </summary>
public enum AvatarPoseMode
{
    /// <summary>Plays the avatar's active in-world BVH animations at 30 Hz.</summary>
    LiveAnimation = 0,

    /// <summary>
    /// Arms lowered ~39° from horizontal (A-pose). Collar and shoulder bones
    /// are rotated to the SL reference standing pose; all other joints stay at
    /// their bind-pose rotations.
    /// </summary>
    APose = 1,

    /// <summary>
    /// Bind pose — no animation applied; arms horizontal (T-pose).
    /// </summary>
    TPose = 2,
}
