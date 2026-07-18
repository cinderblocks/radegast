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

// ─── Cloud layer (WL cloud_* fields describe ONE flat layer; several visual
// layers are synthesized from it below via fixed per-layer offsets, since the
// protocol itself has no concept of multiple altitudes for this field set) ──
uniform sampler2D uCloudNoise;       // tiling value-noise density texture (R8)
uniform vec3      uCloudColor;       // WL cloud_color
uniform vec4      uCloudPosDensity1; // WL cloud_pos_density1 (xy=offset, w=coverage)
uniform vec4      uCloudPosDensity2; // WL cloud_pos_density2 (detail octave)
uniform float     uCloudScale;       // WL cloud_scale
uniform float     uCloudShadow;      // WL cloud_shadow
uniform vec2      uCloudScrollRate;  // WL cloud_scroll_rate
uniform float     uCloudVariance;    // EEP cloud_variance
uniform vec3      uCameraPos;        // world-space eye position
uniform float     uTime;             // seconds, drives cloud scroll animation

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

const int NUM_CLOUD_LAYERS = 4;

// Samples one synthetic cloud layer's density where `dir` (from uCameraPos)
// crosses a horizontal plane at that layer's altitude. Parallax falls out of
// the ray-plane intersection for free: near-horizon rays travel much farther
// to reach each plane than near-zenith rays, so their noise UVs sweep
// proportionally faster — the classic "distant clouds slide by quicker" look,
// with no extra code. dir.z is assumed > 0 (checked by the caller).
float sampleCloudLayer(int i, vec3 dir)
{
    const float baseAlt[4]   = float[4](200.0, 260.0, 320.0, 380.0);
    const float scaleMul[4]  = float[4](1.0, 1.35, 0.8, 1.6);
    const float scrollMul[4] = float[4](1.0, 0.7, 1.3, 0.5);
    const float kPosScale    = 0.002; // world-metres → UV; scroll uses the same scale
                                       // (treated as "wind drift metres/sec") so cloud
                                       // motion and parallax-from-camera-movement read
                                       // at a consistent visual speed.

    float t        = (baseAlt[i] - uCameraPos.z) / max(dir.z, 0.05);
    vec2  worldXY  = uCameraPos.xy + dir.xy * t
                    + uCloudScrollRate * uTime * scrollMul[i] / kPosScale;

    vec2 uv1 = worldXY * uCloudScale * scaleMul[i] * kPosScale + uCloudPosDensity1.xy;
    vec2 uv2 = worldXY * uCloudScale * scaleMul[i] * kPosScale * 2.3 + uCloudPosDensity2.xy;

    float n1 = texture(uCloudNoise, uv1).r;
    float n2 = texture(uCloudNoise, uv2).r;
    float density = mix(n1, n2, clamp(0.4 + uCloudVariance * 0.3, 0.0, 1.0));

    // Higher coverage → more cloud, so it maps to a LOWER threshold (more of the
    // noise's value range passes). Clamped so a coverage value near 0 or 1 can't
    // pin every layer fully open/closed — with 4 layers compositing, a threshold
    // that's too low blows up to near-total sky coverage very quickly (each layer
    // stacks multiplicatively via cloudAlpha = cloudAlpha + density*(1-cloudAlpha)).
    float coverage  = mix(uCloudPosDensity1.w, uCloudPosDensity2.w, 0.5);
    float threshold = clamp(1.0 - coverage, 0.35, 0.85);
    return smoothstep(threshold, threshold + 0.12, density);
}

void main()
{
    vec3 dir = viewRay(vNdc);

    vec3 belowHorizonColor = uAmbient * 0.3;

    // ── Deep sub-horizon: dark ground ambient, no need to compute the sky term ──
    if (dir.z < -0.15)
    {
        fragColor = vec4(belowHorizonColor, 1.0);
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
    // Clamped defensively: an unusual region-supplied glow.x/glow.z can otherwise
    // push glowExp low enough (or uSunGlowSize high enough) that this smears into a
    // wide bright wash across much of the sky instead of a halo around the sun —
    // especially now that the sun direction comes from the simulator's real-time
    // broadcast rather than our own day-cycle interpolation, so it can legitimately
    // sit near the horizon where far more of the visible sky shares a similar angle
    // to it.
    float glowExp  = clamp(uSunGlowFocus * 16.0 + 8.0, 4.0, 24.0);
    float glow     = pow(max(cosA, 0.0), glowExp) * clamp(uSunGlowSize, 0.0, 3.0) * 0.3;
    vec3  sky      = rayleigh + uSunlightColor * glow + uAmbient;

    // ── Clouds (multi-layer, composited far-to-near) ─────────────────────────
    // Sits after the sky gradient/glow but before the sun disc, matching SL's
    // own layering — the disc stays visible on top of cloud cover.
    if (dir.z > 0.0)
    {
        vec3  cloudRgb   = vec3(0.0);
        float cloudAlpha = 0.0;
        for (int i = 0; i < NUM_CLOUD_LAYERS; i++)
        {
            float density = sampleCloudLayer(i, dir);
            float shade   = mix(1.0, 1.0 - uCloudShadow, density); // flat fake self-shadow
            vec3  col     = uCloudColor * uSunlightColor * shade;
            cloudRgb   = mix(cloudRgb, col, density * (1.0 - cloudAlpha));
            cloudAlpha = cloudAlpha + density * (1.0 - cloudAlpha);
        }
        // Fade clouds out near the horizon instead of aliasing: the ray-plane
        // intersection distance (and thus the noise UV step between adjacent
        // pixels) blows up as dir.z → 0, which otherwise shows up as jagged
        // high-frequency noise right at the horizon line.
        cloudAlpha *= smoothstep(0.0, 0.12, dir.z);
        sky = mix(sky, cloudRgb, cloudAlpha);
    }

    // ── Sun disc ──────────────────────────────────────────────────────────────
    float disc = smoothstep(0.9993, 0.9998, cosA);
    sky += uSunlightColor * disc * 8.0;

    // Smoothly blend into the below-horizon ambient across a small band instead of
    // the instantaneous jump the old dir.z < -0.02 hard branch produced — that was
    // a visible seam right at the horizon line.
    if (dir.z < 0.0)
        sky = mix(belowHorizonColor, sky, smoothstep(-0.15, 0.0, dir.z));

    fragColor = vec4(max(sky, vec3(0.0)), 1.0);
}
