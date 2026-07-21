// ── Shared analytic atmosphere model ─────────────────────────────────────────
// Included (via ShaderLoader's #include preprocessing) by sky.frag, water.frag
// and prim.frag so the sky dome, the water's reflection/haze and the geometry
// distance-haze all converge on the SAME horizon colour — that agreement is what
// makes the horizon read as one continuous environment instead of three shaders
// meeting at a seam.
//
// Driven by the classic Windlight parameter set (see SkySettings.cs). All
// functions return linear-ish "display-space" colour like the WL params
// themselves; no tonemapping is applied here.
//
// NOTE: do not put a #version directive in this file — it is textually inlined
// below the including shader's own #version line.

uniform vec3  uBlueHorizon;    // WL blue_horizon
uniform vec3  uBlueDensity;    // WL blue_density (per-channel Rayleigh density)
uniform float uHazeHorizon;    // WL haze_horizon
uniform float uHazeDensity;    // WL haze_density
uniform vec3  uSunlightColor;  // WL sun_moon_color
uniform vec3  uAmbient;        // WL sky_ambient
uniform vec3  uSunDirection;   // world-space unit vector toward sun (Z-up)
uniform float uSunGlowFocus;   // WL glow.x / 20
uniform float uSunGlowSize;    // WL glow.z scale

// Combined atmosphere illumination. WL regions split sky brightness
// unpredictably between sunlight and ambient — live EEP data has shown
// ambient-dominant skies whose sun_moon_color is nearly dark. LL's own
// atmospherics shade the sky as blue_horizon * (sunlight + ambient), so every
// gradient tint below is modulated by this combined term; a formula leaning
// only on uSunlightColor renders such regions as a flat unlit blue_horizon.
vec3 atmLight()
{
    return uSunlightColor * 0.70 + uAmbient * 0.65;
}

// Soft highlight shoulder: linear below 0.8, asymptotic toward 1.0 above.
// Region presets legitimately push tints past 1.0 (dawn magentas, ambient > 1);
// letting GL hard-clip them flattens the sky into a neon poster, while this
// compresses the overshoot and preserves the hue gradient.
vec3 atmSoftClip(vec3 c)
{
    vec3 x = max(c - vec3(0.8), vec3(0.0));
    return min(c, vec3(0.8)) + x / (1.0 + 5.0 * x);
}

// Colour of the haze band sitting on the horizon: the fog/aerial-perspective
// target for terrain, prims and near-horizon water. Deliberately has no view
// or sun-direction dependence so geometry fog stays cheap and view-consistent;
// atmSkyGradient() layers the directional sun tint on top of this.
vec3 atmHazeColor()
{
    // Denser/brighter haze pushes the horizon from tinted sky-blue toward a
    // bright desaturated white; both ends are lit by the combined illumination.
    float hazeStrength = clamp(uHazeHorizon * 1.7 + uHazeDensity * 0.25, 0.0, 1.0);
    vec3  tint = mix(uBlueHorizon * vec3(1.50, 1.45, 1.30), vec3(1.10), hazeStrength);
    return atmSoftClip(tint * atmLight());
}

// Full clear-sky colour for a world-space direction: zenith→horizon gradient,
// horizon haze band, sun-side warm scatter and a two-lobe forward-scatter glow.
// No sun disc and no clouds — those belong to sky.frag only.
vec3 atmSkyGradient(vec3 dir)
{
    float elev = clamp(dir.z, 0.0, 1.0);
    vec3  light = atmLight();

    // Three-stop gradient, every stop lit by the combined illumination.
    // blue_density deepens/darkens the zenith toward its dominant hue; the
    // per-channel factor is FLOORED at 0.35 so an extreme region density can
    // only deepen the zenith, never zero channels out into a flat primary.
    vec3 zenith = uBlueHorizon
                * clamp(vec3(0.95) - uBlueDensity * vec3(1.10, 0.55, 0.15),
                        vec3(0.35), vec3(1.0))
                * light;
    vec3 midSky  = uBlueHorizon * 1.28 * light;
    vec3 horizon = atmHazeColor();

    vec3 upper = mix(midSky, zenith, smoothstep(0.10, 0.65, elev));

    // Horizon band width grows with haze density.
    float band = mix(9.0, 4.0, clamp(uHazeDensity * 0.5, 0.0, 1.0));
    float hw   = exp(-elev * band);
    vec3  sky  = mix(upper, horizon, hw);

    // Warm in-scatter near the horizon on the sun side of the sky.
    float cosA   = dot(dir, uSunDirection);
    float sunAmt = pow(clamp(cosA * 0.5 + 0.5, 0.0, 1.0), 4.0);
    sky += uSunlightColor * (uHazeHorizon * 0.9) * hw * sunAmt;

    // Two-lobe forward-scatter glow: a tight WL-driven corona plus a broad,
    // subtle brightening of the whole sun-side sky. Clamped like the old sky
    // shader so odd region glow values can't wash out the frame.
    float glowExp   = clamp(uSunGlowFocus * 16.0 + 8.0, 4.0, 24.0);
    float glowTight = pow(max(cosA, 0.0), glowExp) * clamp(uSunGlowSize, 0.0, 3.0) * 0.25;
    float glowWide  = pow(max(cosA, 0.0), 3.0) * 0.10;
    sky += uSunlightColor * (glowTight + glowWide);

    return atmSoftClip(sky);
}
