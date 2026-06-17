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

using System.Numerics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Sky and atmosphere rendering parameters that map directly to Linden Lab's
/// Windlight / EEP sky settings.  All default values match SL's built-in
/// "Default" midday preset.
/// </summary>
/// <remarks>
/// EEP integration: parse <c>EnvironmentData.DayCycle</c> LLSD and populate
/// this object from the sky track keys (blue_horizon, blue_density, etc.),
/// then assign to <see cref="GlViewportControl.Sky"/> on the UI thread.
/// </remarks>
public sealed class SkySettings
{
    /// <summary>Sky colour at the horizon (WL <c>blue_horizon</c>, RGB).</summary>
    public Vector3 BlueHorizon { get; set; } = new Vector3(0.4f, 0.4f, 0.9f);

    /// <summary>
    /// Rayleigh scattering density per colour channel (WL <c>blue_density</c>, RGB).
    /// Higher values darken the zenith and deepen the blue.
    /// </summary>
    public Vector3 BlueDensity { get; set; } = new Vector3(0.2f, 0.4f, 0.4f);

    /// <summary>Mie (haze) scattering contribution at the horizon (WL <c>haze_horizon</c>).</summary>
    public float HazeHorizon { get; set; } = 0.19f;

    /// <summary>Mie (haze) scattering density (WL <c>haze_density</c>).</summary>
    public float HazeDensity { get; set; } = 0.7f;

    /// <summary>Sun / moon colour (WL <c>sun_moon_color</c>, RGB).</summary>
    public Vector3 SunlightColor { get; set; } = new Vector3(0.8f, 0.8f, 0.8f);

    /// <summary>Ambient sky light (WL <c>sky_ambient</c>, RGB).</summary>
    public Vector3 Ambient { get; set; } = new Vector3(0.25f, 0.25f, 0.25f);

    /// <summary>
    /// World-space unit vector pointing toward the sun (Z-up convention).
    /// Default: 45° elevation facing positive Y (south in SL).
    /// Set this each frame from the day-cycle angle when EEP is active.
    /// </summary>
    public Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(0f, 1f, 1f));

    /// <summary>
    /// Sun glow sharpness (WL <c>glow.x / 20</c>).
    /// Higher values produce a tighter, more focused glow around the disc.
    /// </summary>
    public float SunGlowFocus { get; set; } = 0.1f;

    /// <summary>
    /// Sun glow intensity (WL <c>glow.z</c> scale).
    /// Higher values produce a brighter, wider corona.
    /// </summary>
    public float SunGlowSize { get; set; } = 1.75f;

    /// <summary>Returns a new <see cref="SkySettings"/> with SL default midday values.</summary>
    public static SkySettings Default => new();
}
