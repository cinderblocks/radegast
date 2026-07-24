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
    public Vector3 BlueHorizon { get; set; } = new Vector3(0.24f, 0.35f, 0.75f);

    /// <summary>
    /// Rayleigh scattering density per colour channel (WL <c>blue_density</c>, RGB).
    /// <see cref="GlViewportControl"/>'s sky shader computes
    /// <c>BlueHorizon * exp(-BlueDensity / sinElevation)</c> — HIGHER density means
    /// MORE attenuation (less of that channel gets through), so for a blue-dominant
    /// sky the blue channel needs the LOWEST density here, not the highest.
    /// </summary>
    public Vector3 BlueDensity { get; set; } = new Vector3(0.15f, 0.25f, 0.35f);

    /// <summary>Mie (haze) scattering contribution at the horizon (WL <c>haze_horizon</c>).</summary>
    public float HazeHorizon { get; set; } = 0.19f;

    /// <summary>Mie (haze) scattering density (WL <c>haze_density</c>).</summary>
    public float HazeDensity { get; set; } = 0.7f;

    /// <summary>Sun / moon colour (WL <c>sun_moon_color</c>, RGB).</summary>
    public Vector3 SunlightColor { get; set; } = new Vector3(0.73f, 0.78f, 0.90f);

    /// <summary>Ambient sky light (WL <c>sky_ambient</c>, RGB).</summary>
    public Vector3 Ambient { get; set; } = new Vector3(0.25f, 0.25f, 0.25f);

    /// <summary>
    /// World-space unit vector pointing toward the sun (Z-up convention).
    /// Default: 60° elevation facing positive Y (south in SL) — SL's own midday
    /// preset keeps the noon sun well short of straight-up zenith so terrain
    /// shading reads correctly.
    /// Set this each frame from the day-cycle angle when EEP is active.
    /// </summary>
    public Vector3 SunDirection { get; set; } = new Vector3(0f, 0.5f, 0.8660254f);

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

    // ── Cloud layer (EEP cloud_* fields) ────────────────────────────────────────
    // These describe SL's single flat cloud layer; GlViewportControl synthesizes
    // several visual layers from them (altitude/scale/scroll offsets applied in
    // the sky shader) rather than the protocol describing multiple layers itself.

    /// <summary>Cloud tint (WL <c>cloud_color</c>, RGB).</summary>
    public Vector3 CloudColor { get; set; } = new Vector3(1f, 1f, 1f);

    /// <summary>
    /// Base cloud octave (WL <c>cloud_pos_density1</c>): xyz = scroll offset / octave
    /// weighting, w = coverage/density threshold.
    /// </summary>
    public Vector4 CloudPosDensity1 { get; set; } = new Vector4(1f, 0.5f, 1f, 0.35f);

    /// <summary>
    /// Secondary/detail cloud octave (WL <c>cloud_pos_density2</c>), same layout as
    /// <see cref="CloudPosDensity1"/>.
    /// </summary>
    public Vector4 CloudPosDensity2 { get; set; } = new Vector4(1f, 0.5f, 1f, 0.35f);

    /// <summary>Cloud noise tiling frequency (WL <c>cloud_scale</c>).</summary>
    public float CloudScale { get; set; } = 0.42f;

    /// <summary>Flat darkening clouds contribute to their own underside (WL <c>cloud_shadow</c>).</summary>
    public float CloudShadow { get; set; } = 0.27f;

    /// <summary>Cloud texture-space scroll speed, simulating wind (WL <c>cloud_scroll_rate</c>).</summary>
    public Vector2 CloudScrollRate { get; set; } = new Vector2(0.2f, 0.01f);

    /// <summary>Extra per-region noise turbulence on top of the base pattern (EEP <c>cloud_variance</c>).</summary>
    public float CloudVariance { get; set; }

    /// <summary>Returns a new <see cref="SkySettings"/> with SL default midday values.</summary>
    public static SkySettings Default => new();

    /// <summary>
    /// Neutral studio lighting for isolated object and avatar viewers.
    /// Overhead sun, elevated ambient — no sky dome needed.
    /// </summary>
    public static SkySettings Studio => new()
    {
        SunDirection  = Vector3.UnitZ,
        SunlightColor = new Vector3(0.7f, 0.7f, 0.7f),
        Ambient       = new Vector3(0.45f, 0.45f, 0.45f),
        BlueHorizon   = new Vector3(0.4f, 0.4f, 0.9f),
        BlueDensity   = new Vector3(0.2f, 0.4f, 0.4f),
        HazeHorizon   = 0.19f,
        HazeDensity   = 0.7f,
        SunGlowFocus  = 0.1f,
        SunGlowSize   = 1.75f,
    };
}
