#version 300 es
precision highp float;

// ─── Windlight / EEP sky dome ────────────────────────────────────────────────
// The atmosphere gradient itself (and its uniforms: uBlueHorizon, uBlueDensity,
// uHazeHorizon, uHazeDensity, uSunlightColor, uAmbient, uSunDirection,
// uSunGlowFocus, uSunGlowSize) lives in atmosphere.glsl, shared with the water
// and prim shaders so all three agree on the horizon colour.

uniform mat4  uInvViewProj;    // inverse(view * proj) — reconstructs world-ray

#include "atmosphere.glsl"

// ─── Cloud layer (WL cloud_* fields describe ONE flat layer; several visual
// layers are synthesized from it below via fixed per-layer offsets, since the
// protocol itself has no concept of multiple altitudes for this field set) ──
uniform sampler2D uCloudNoise;       // tiling value-noise density texture (R8, mipmapped)
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

// ── Small hash helpers (Dave Hoskins-style, no texture needed) ───────────────

float hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float hash13(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

// Procedural starfield: one candidate star per cell of a 3D grid intersected by
// the unit view direction scaled onto a large sphere. A steep magnitude power
// keeps most cells empty so the field reads as scattered real stars rather than
// uniform noise; a slow per-star sine adds faint twinkle.
float starField(vec3 dir)
{
    vec3  g    = dir * 220.0;
    vec3  cell = floor(g);
    vec3  sp   = vec3(hash13(cell), hash13(cell + 17.31), hash13(cell + 43.7));
    float d    = length(fract(g) - sp);
    float mag  = hash13(cell + 91.13);
    float star = smoothstep(0.10, 0.0, d) * pow(mag, 12.0) * 2.0;
    star *= 0.8 + 0.2 * sin(uTime * (1.5 + mag * 3.0) + mag * 41.0);
    return star;
}

// Samples one synthetic cloud layer's density where `dir` (from uCameraPos)
// crosses a horizontal plane at that layer's altitude, also returning the
// ray-plane distance for aerial-perspective attenuation and a directional
// self-shadow factor. Parallax falls out of the ray-plane intersection for
// free: near-horizon rays travel much farther to reach each plane than
// near-zenith rays, so their noise UVs sweep proportionally faster — the
// classic "distant clouds slide by quicker" look.
// dir.z is assumed > 0 (checked by the caller).
float sampleCloudLayer(int i, vec3 dir, out float planeDist, out float sunShade)
{
    const float baseAlt[4]   = float[4](200.0, 260.0, 320.0, 380.0);
    const float scaleMul[4]  = float[4](1.0, 1.35, 0.8, 1.6);
    const float scrollMul[4] = float[4](1.0, 0.7, 1.3, 0.5);
    const float kPosScale    = 0.002; // world-metres → UV; scroll uses the same scale
                                       // (treated as "wind drift metres/sec") so cloud
                                       // motion and parallax-from-camera-movement read
                                       // at a consistent visual speed.

    float t        = (baseAlt[i] - uCameraPos.z) / max(dir.z, 0.05);
    planeDist      = t;
    vec2  worldXY  = uCameraPos.xy + dir.xy * t
                    + uCloudScrollRate * uTime * scrollMul[i] / kPosScale;

    vec2 uvBase = worldXY * uCloudScale * scaleMul[i] * kPosScale + uCloudPosDensity1.xy;

    // Explicit mip level from the ray-plane distance (the per-pixel noise-UV
    // footprint grows ~linearly with t). Implicit-derivative LOD is unusable
    // here: these samples sit inside a loop with early-continue and a dir.z
    // branch, so neighbouring pixels diverge and the derivatives are undefined —
    // in practice pixels randomly sampled deep mips, whose values collapse to
    // the noise's ~0.5 mean and then die at the coverage threshold (clouds
    // rendered as sparse speckles). Capped at 3 for the same reason: deeper
    // mips lose the contrast the threshold needs.
    float lod  = clamp(log2(max(t, 1.0) / 300.0), 0.0, 3.0);
    float lod2 = min(lod + 1.2, 3.0); // detail octave tiles 2.3× denser

    // Domain warp: bend the sample position by a low-frequency pass over the
    // same noise, turning the raw value-noise's round blobs into the ragged,
    // billowed silhouettes real cumulus have.
    float w1 = textureLod(uCloudNoise, uvBase * 0.47 + vec2(0.17, 0.43), lod).r;
    float w2 = textureLod(uCloudNoise, uvBase * 0.47 + vec2(0.62, 0.09), lod).r;
    vec2  warp = (vec2(w1, w2) - 0.5) * 0.10;

    vec2 uv1 = uvBase + warp;
    vec2 uv2 = uvBase * 2.3 + uCloudPosDensity2.xy - warp * 0.6;

    float n1 = textureLod(uCloudNoise, uv1, lod).r;
    float n2 = textureLod(uCloudNoise, uv2, lod2).r;
    float density = mix(n1, n2, clamp(0.4 + uCloudVariance * 0.3, 0.0, 1.0));

    // Directional self-shadow: one extra sample displaced toward the sun tells
    // whether thicker cloud sits sunward of this point (in shadow) or thinner
    // (lit rim). This embossed light/dark modelling is what separates a
    // volumetric-looking cloud from a flat stain. Fades out when the sun is
    // near zenith, where "toward the sun" has no horizontal direction.
    vec2  sunAz  = uSunDirection.xy / max(length(uSunDirection.xy), 1e-4);
    float n1s    = textureLod(uCloudNoise, uv1 + sunAz * 0.02, lod).r;
    float emboss = clamp(1.0 - (n1s - n1) * 2.2, 0.55, 1.35);
    sunShade     = mix(1.0, emboss, clamp(length(uSunDirection.xy) * 3.0, 0.0, 1.0));

    // Higher coverage → more cloud, so it maps to a LOWER threshold (more of the
    // noise's value range passes). Clamped so a coverage value near 0 or 1 can't
    // pin every layer fully open/closed — with 4 layers compositing, a threshold
    // that's too low blows up to near-total sky coverage very quickly (each layer
    // stacks multiplicatively via cloudAlpha = cloudAlpha + density*(1-cloudAlpha)).
    float coverage  = mix(uCloudPosDensity1.w, uCloudPosDensity2.w, 0.5);
    float threshold = clamp(1.0 - coverage, 0.35, 0.85);
    float d = smoothstep(threshold, threshold + 0.16, density);
    return d * d * (3.0 - 2.0 * d); // extra smoothing → fluffier edge falloff
}

void main()
{
    vec3 dir = viewRay(vNdc);

    // ── Deep sub-horizon: haze darkening into ground ambient ─────────────────
    // Continues the horizon colour downward instead of snapping to a flat dark
    // band, so the seam against terrain/water at the horizon stays invisible.
    vec3 belowHorizonColor = mix(atmHazeColor() * 0.85, uAmbient * 0.3,
                                 smoothstep(0.0, 0.35, -dir.z));
    if (dir.z < -0.35)
    {
        fragColor = vec4(uAmbient * 0.3, 1.0);
        return;
    }

    // ── Clear-sky gradient + sun glow (shared model) ─────────────────────────
    // The elevation used for the gradient is perturbed by a whisper of
    // low-frequency noise: a mathematically perfect band gradient is one of the
    // strongest "computer graphics" tells, while real horizon haze undulates.
    float turb = textureLod(uCloudNoise, dir.xy * 0.35 + vec2(0.31, 0.77), 2.0).r;
    vec3  gdir = normalize(vec3(dir.xy, dir.z + (turb - 0.5) * 0.035));
    vec3  sky  = atmSkyGradient(gdir);

    // ── Sun disc (before clouds so cloud cover occludes it) ──────────────────
    float cosA = dot(dir, uSunDirection);
    float disc = smoothstep(0.99930, 0.99965, cosA);
    if (disc > 0.0)
    {
        // Limb darkening: radial falloff from disc centre keeps the sun a
        // sphere-like ball of light instead of a flat white circle.
        float r    = clamp((1.0 - cosA) / (1.0 - 0.99965), 0.0, 1.0);
        float limb = mix(1.0, 0.55, r * r);
        sky += uSunlightColor * disc * limb * 8.0;
    }

    // ── Night sky: stars + moon (before clouds so cover occludes them) ───────
    // Fades in as the sun sets. The moon follows SL's convention of sitting
    // roughly opposite the sun (moon_rotation isn't parsed, so this is the
    // closest cheap approximation).
    float night = smoothstep(0.03, -0.12, uSunDirection.z);
    if (night > 0.0 && dir.z > 0.0)
    {
        float star = starField(dir) * smoothstep(0.0, 0.15, dir.z);
        sky += vec3(0.75, 0.80, 0.90) * star * night;

        vec3  moonDir = -uSunDirection;
        float cosM    = dot(dir, moonDir);
        float mdisc   = smoothstep(0.99950, 0.99980, cosM);
        if (mdisc > 0.0)
        {
            float mr = clamp((1.0 - cosM) / (1.0 - 0.99980), 0.0, 1.0);
            sky += vec3(0.86, 0.90, 0.98) * mdisc * (0.9 - 0.3 * mr * mr) * night;
        }
        sky += vec3(0.45, 0.50, 0.65) * pow(max(cosM, 0.0), 90.0) * 0.18 * night;
    }

    // ── Clouds (multi-layer, composited far-to-near) ─────────────────────────
    if (dir.z > 0.0)
    {
        vec3  hazeCol    = atmHazeColor();
        vec3  cloudRgb   = vec3(0.0);
        float cloudAlpha = 0.0;
        for (int i = 0; i < NUM_CLOUD_LAYERS; i++)
        {
            float planeDist, sunShade;
            float density = sampleCloudLayer(i, dir, planeDist, sunShade);
            if (density <= 0.001) continue;

            // Self-shadow: denser cloud is darker on its underside, and the
            // directional emboss term lights the sunward flanks / darkens the
            // far side of each cloud form.
            float shade = mix(1.0, 1.0 - uCloudShadow, density) * sunShade;

            // Silver lining: thin cloud between the eye and the sun catches a
            // bright forward-scattered rim.
            float silver = pow(max(cosA, 0.0), 12.0) * (1.0 - density) * 0.6;

            vec3 col = uCloudColor * (uSunlightColor * (shade + silver)
                                      + uAmbient * 0.35);

            // Aerial perspective: distant cloud melts toward the horizon haze,
            // both in colour and in opacity, so cloud banks recede naturally
            // instead of wallpapering the sky at constant contrast.
            float aer = 1.0 - exp(-planeDist * 0.00035);
            col      = mix(col, hazeCol, aer);
            density *= exp(-planeDist * 0.00015);

            cloudRgb   = mix(cloudRgb, col, density * (1.0 - cloudAlpha));
            cloudAlpha = cloudAlpha + density * (1.0 - cloudAlpha);
        }
        // Fade clouds out right at the horizon: the ray-plane distance (and the
        // per-pixel noise UV step) blows up as dir.z → 0; mipmapping handles the
        // aliasing but the last few degrees still degenerate into mush.
        cloudAlpha *= smoothstep(0.0, 0.08, dir.z);
        sky = mix(sky, cloudRgb, cloudAlpha);
    }

    // Smoothly blend into the below-horizon gradient across a small band.
    if (dir.z < 0.0)
        sky = mix(belowHorizonColor, sky, smoothstep(-0.08, 0.0, dir.z));

    // Dither: the sky is a huge, very smooth gradient, and quantising it to
    // 8-bit output produces visible Mach bands that read as "flat plastic".
    // ±1 LSB of per-pixel noise fully hides the stepping.
    sky += (hash12(gl_FragCoord.xy) - 0.5) * (1.5 / 255.0);

    fragColor = vec4(max(sky, vec3(0.0)), 1.0);
}
