#version 300 es
precision highp float;

// ─── Windlight / EEP sky uniforms ────────────────────────────────────────────
// All parameters match Linden Lab's Windlight naming; default values correspond
// to SL's "Default" midday preset.

uniform mat4  uInvViewProj;    // inverse(view * proj) — reconstructs world-ray

// Rayleigh scatter
uniform vec3  uBlueHorizon;    // WL blue_horizon  (0.4, 0.4, 0.9)
uniform vec3  uBlueDensity;    // WL blue_density  (0.2, 0.4, 0.4)

// Mie scatter / haze
uniform float uHazeHorizon;   // WL haze_horizon  (0.19)
uniform float uHazeDensity;   // WL haze_density  (0.7)

// Lighting
uniform vec3  uSunlightColor;  // WL sun_moon_color (0.8, 0.8, 0.8)
uniform vec3  uAmbient;        // WL sky_ambient    (0.25, 0.25, 0.25)
uniform vec3  uSunDirection;   // world-space unit vector toward sun

// Sun glow (WL glow parameters, remapped to [-10, +10] range)
uniform float uSunGlowFocus;   // WL glow.x / 20    (0.1)
uniform float uSunGlowSize;    // WL glow.z scale    (1.75)

in  vec2 vNdc;
out vec4 fragColor;

// ─────────────────────────────────────────────────────────────────────────────

// Reconstruct world-space view ray from NDC position using the inverse VP matrix.
vec3 viewRay(vec2 ndc)
{
    vec4 near = uInvViewProj * vec4(ndc, -1.0, 1.0);
    vec4 far  = uInvViewProj * vec4(ndc,  1.0, 1.0);
    near /= near.w;
    far  /= far.w;
    return normalize(far.xyz - near.xyz);
}

void main()
{
    vec3 dir = viewRay(vNdc);

    // ── Sub-horizon: dark ground ambient ─────────────────────────────────────
    if (dir.z < -0.02)
    {
        fragColor = vec4(uAmbient * 0.3, 1.0);
        return;
    }

    // Elevation: sin of the elevation angle above the horizon (Z-up world)
    // Clamped to avoid the singularity at the exact horizon.
    float sinElev = max(dir.z, 0.001);

    // ── Rayleigh scatter (blue sky) ───────────────────────────────────────────
    // Optical depth = 1/sin(elevation): light travels longer paths near horizon.
    // exp(-density / elevation) → bright at zenith, fades to 0 at horizon where
    // ambient and haze take over.
    vec3 rayleigh = uBlueHorizon * exp(-uBlueDensity / sinElev);

    // ── Haze (Mie-like) ───────────────────────────────────────────────────────
    // Additive brightening near horizon, modulated by haze_density.
    float horizonFactor = 1.0 - clamp(dir.z, 0.0, 1.0);
    float hazeAmt = pow(horizonFactor, 3.0) * uHazeDensity;

    // Sun-side brightening: warmer on the sun side, neutral on the opposite side
    float cosA    = dot(dir, normalize(uSunDirection));
    float sunSide = cosA * 0.3 + 0.7;
    rayleigh += uSunlightColor * uHazeHorizon * hazeAmt * sunSide;

    // ── Sun glow (forward Mie scatter) ───────────────────────────────────────
    float glowExp  = uSunGlowFocus * 16.0 + 8.0;
    float glow     = pow(max(cosA, 0.0), glowExp) * uSunGlowSize * 0.3;
    vec3  sky      = rayleigh + uSunlightColor * glow + uAmbient;

    // ── Sun disc ──────────────────────────────────────────────────────────────
    float disc = smoothstep(0.9993, 0.9998, cosA);
    sky += uSunlightColor * disc * 8.0;

    fragColor = vec4(max(sky, vec3(0.0)), 1.0);
}
