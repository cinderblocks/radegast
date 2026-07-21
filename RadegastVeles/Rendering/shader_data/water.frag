#version 300 es
precision highp float;

in vec2 vNdc;

uniform mat4      uViewProj;       // view * proj — for gl_FragDepth of the hit point
uniform mat4      uReflViewProj;   // reflected-camera view * proj — for reflection UV lookup
uniform mat4      uInvViewProj;    // inverse(view * proj) — ray reconstruction
uniform vec3      uEyePos;         // camera world position (Z-up)
uniform float     uWaterHeight;    // world Z of the water plane
uniform float     uTime;           // animation clock (seconds, monotonically increasing)

uniform sampler2D uReflectionTex;  // unit 0 - reflected scene FBO
uniform sampler2D uNormalMap;      // unit 1 - water surface normals
uniform sampler2D uDudvMap;        // unit 2 - distortion UV map
uniform vec4      uWaterColor;     // deep-water tint (rgba) — EEP water_fog_color
uniform int       uHasReflection;  // 1 = reflection FBO available

// Shared atmosphere model (uSunDirection, uSunlightColor, uAmbient, haze params…).
// Used two ways: atmSkyGradient(reflectedRay) gives a physically-plausible sky
// reflection even with the reflection FBO disabled, and blending the far water
// into atmSkyGradient(horizontal ray) makes the water/sky horizon seamless.
#include "atmosphere.glsl"

out vec4 fragColor;

const float kMaxReflDist = 400.0; // world-units beyond which reflection FBO is not sampled

float hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// Reconstruct world-space view direction from NDC using the inverse VP matrix.
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
    vec3 ray = viewRay(vNdc);

    // Ray must be going downward (Z-up world) to hit the water plane.
    // Use a small epsilon so the horizon edge itself still renders.
    if (ray.z >= -0.0001) discard;

    // Ray-plane intersection: eye + t*ray at z = uWaterHeight
    float t = (uWaterHeight - uEyePos.z) / ray.z;
    if (t <= 0.0) discard;   // intersection behind camera

    vec3 hitPos  = uEyePos + ray * t;
    vec2 worldXY = hitPos.xy;

    // ── Wave normal: two animated scales + dudv-driven drift ─────────────────
    // Amplitude fades with distance: far water flattens toward a mirror of the
    // sky, which both looks right (wave detail is sub-pixel out there) and kills
    // the tiling/glitter aliasing the single fixed-scale sample produced.
    float nFade = exp(-t * 0.004);

    vec2 dudv = texture(uDudvMap, worldXY * (1.0 / 96.0)
                        + uTime * vec2(0.004, -0.003)).rg * 2.0 - 1.0;

    vec2 uvA = worldXY * (1.0 / 14.0) + uTime * vec2(0.021, 0.017) + dudv * 0.030;
    vec2 uvB = worldXY * (1.0 / 47.0) - uTime * vec2(0.009, 0.006) + dudv * 0.055;

    vec3 nA = texture(uNormalMap, uvA).rgb * 2.0 - 1.0;
    vec3 nB = texture(uNormalMap, uvB).rgb * 2.0 - 1.0;
    // Water plane's tangent frame is world-aligned (flat plane, +Z up), so the
    // tangent-space samples combine directly in world space.
    vec3 mapNorm = normalize(vec3(nA.xy * 0.70 + nB.xy * 0.55, 1.60));
    mapNorm      = normalize(mix(vec3(0.0, 0.0, 1.0), mapNorm, 0.08 + 0.75 * nFade));

    // View direction: from surface toward camera
    vec3 viewDir = -ray;

    // ── Schlick Fresnel (F0 = 0.02 for water) ────────────────────────────────
    float cosV    = max(dot(mapNorm, viewDir), 0.0);
    float fresnel = 0.02 + 0.98 * pow(1.0 - cosV, 5.0);

    // ── Reflection ────────────────────────────────────────────────────────────
    // Baseline: analytic sky colour along the reflected ray — always available,
    // and what the planar FBO fades back to at its distance/edge limits.
    vec3 reflDir = reflect(ray, mapNorm);
    reflDir.z    = abs(reflDir.z); // wave normals can dip the ray below horizon
    vec3 reflColor = atmSkyGradient(reflDir);

    if (uHasReflection != 0 && t < kMaxReflDist)
    {
        vec4 reflClip = uReflViewProj * vec4(hitPos, 1.0);
        vec2 reflUV   = reflClip.xy / reflClip.w * 0.5 + 0.5;
        // Distortion kept small: the FBO is only 512² and its texels stretch
        // tall near grazing angles, so screen-space offsets smear into long
        // vertical streaks well before they read as ripple.
        reflUV       += mapNorm.xy * 0.012 * nFade;

        // Fade the FBO sample out toward its UV edges and toward kMaxReflDist
        // (where the 512² FBO can no longer resolve the hit) instead of clamping —
        // the old clamp smeared arbitrary edge texels into "terrain" streaks.
        vec2  uvC   = clamp(reflUV, 0.0, 1.0);
        float edge  = smoothstep(0.0, 0.08, min(uvC.x, uvC.y))
                    * (1.0 - smoothstep(0.92, 1.0, max(uvC.x, uvC.y)));
        float range = 1.0 - smoothstep(kMaxReflDist * 0.6, kMaxReflDist, t);
        reflColor   = mix(reflColor, texture(uReflectionTex, uvC).rgb, edge * range);
    }

    // ── Water body colour ─────────────────────────────────────────────────────
    // Deep-water tint lit by the environment so it tracks day/night instead of
    // rendering as a constant flat blue sheet.
    vec3 deepCol = uWaterColor.rgb
                 * (uAmbient + uSunlightColor * max(uSunDirection.z, 0.0) * 0.8);

    vec3 col = mix(deepCol, reflColor, fresnel);

    // ── Sun glint ─────────────────────────────────────────────────────────────
    // Tight primary glint plus a broad soft sheen along the sun path.
    vec3  H       = normalize(uSunDirection + viewDir);
    float NdotH   = max(dot(mapNorm, H), 0.0);
    float sunUp   = clamp(uSunDirection.z * 4.0, 0.0, 1.0);
    float glint   = pow(NdotH, 240.0) * 1.10;
    float sheen   = pow(NdotH,  32.0) * 0.08;
    col += uSunlightColor * (glint + sheen) * sunUp;

    // ── Aerial perspective → seamless horizon ────────────────────────────────
    // Blend toward the sky's own colour just above the horizon in this pixel's
    // compass direction (includes the sun-side warm tint), so at the horizon
    // line water and sky converge on the identical colour.
    vec3  hazeTarget = atmSkyGradient(normalize(vec3(ray.xy, 0.02)));
    float aer        = 1.0 - exp(-t * (0.00055 + uHazeDensity * 0.0006));
    col = mix(col, hazeTarget, aer);

    // Dither the far-water gradient like the sky does — the haze blend is
    // smooth enough to Mach-band at 8-bit otherwise.
    col += (hash12(gl_FragCoord.xy) - 0.5) * (1.5 / 255.0);

    // Distant water is fully opaque (nothing readable through it anyway, and
    // letting underwater geometry bleed through at range caused blocky
    // artefacts); near water keeps the EEP fog alpha for shallow translucency.
    float alpha = mix(uWaterColor.a * 0.72, 1.0, max(fresnel, aer));

    fragColor = vec4(col, alpha);

    // Write the depth of the actual water surface so the water:
    //   • renders over underwater terrain (terrain depth > water depth → Less passes)
    //   • is culled by above-water terrain (terrain depth < water depth → Less fails)
    //   • appears at the horizon where no geometry fills the pixel (depth buffer = 1.0)
    // Disabling early-z is acceptable for a single full-screen pass per frame.
    vec4 clipHit  = uViewProj * vec4(hitPos, 1.0);
    gl_FragDepth  = clamp(clipHit.z / clipHit.w * 0.5 + 0.5, 0.0, 1.0);
}
