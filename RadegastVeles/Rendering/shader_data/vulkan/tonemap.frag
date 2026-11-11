#version 310 es
precision highp float;

// Final composite: adds bloom and god rays to the full-resolution HDR scene colour, rolls off
// highlights above kKnee (see highlightShoulder) and writes the swapchain image. On the HDR tiers
// this is the last pass to touch it (see VkRenderPass.CreateTonemapOutputPass).

layout(set = 0, binding = 0) uniform sampler2D uSceneColor; // full-res HDR (VkViewportControl._hdrColorFormat)
layout(set = 0, binding = 1) uniform sampler2D uBloomTex;   // half-res, already blurred
layout(set = 0, binding = 2) uniform sampler2D uGodRayTex;  // half-res, already blurred/streaked
layout(set = 0, binding = 3) uniform sampler2D uBloomWideTex; // quarter-res, blurred again

layout(push_constant) uniform PerDraw
{
    // Redundant with godray_radial_blur.frag's own uIntensity multiply (already zeroed there
    // when the sun's below the horizon/off-screen) -- kept here too so a frame where the god-ray
    // passes were skipped entirely (CPU-side early-out) can't accidentally composite in whatever
    // stale content uGodRayTex was last written with; this scalar alone decides its contribution.
    float uGodRayIntensity;
} pc;
#define uGodRayIntensity pc.uGodRayIntensity

layout(location = 0) in  vec2 vTexCoord;
layout(location = 0) out vec4 fragColor;

// Additive weight for the blurred bright-pass -- see bloom_extract.frag's own threshold for
// what feeds this. Modest by design: bloom should read as glow around genuinely bright content
// (the sun disc, water sparkle, the sky shader's cloud silver lining), not a global soft-focus
// wash over the whole frame.
const float kBloomIntensity = 0.55;
// Share of that weight given to the wide (quarter-res) level; the rest goes to the tight one.
const float kBloomWideShare = 0.5;

// Tint for the god-ray contribution -- warm, matching the sun disc rather than a neutral white
// streak. The mask (godray_mask.frag) outputs a luminance-only brightness value so the final
// god-ray colour is determined entirely here: kGodRayTint * sunlight-warm, regardless of the
// sky shader's own colour. Modest additive weight for the same "glow, not a wash" reasoning
// as kBloomIntensity.
const vec3  kGodRayTint      = vec3(1.0, 0.92, 0.75);
const float kGodRayIntensity = 0.9;

// Highlight shoulder -- NOT a full filmic curve. Every scene shader that writes the HDR buffer
// (prim.frag, sky.frag, water.frag, particle.frag) already outputs DISPLAY-REFERRED colour:
// prim.frag's PBR path runs its own ACES fit and both of its paths gamma-encode, sky/water run
// atmSoftClip. Those are the same values VkGraphicsTier.Low writes straight to the swapchain. So
// the only job left here is rolling off what a UNORM target would have hard-clipped (the sun
// disc's >1.0 core, specular spikes, bloom/god-ray additions): identity below kKnee, a smooth
// C1-continuous shoulder toward 1.0 above it. A full tonemap curve here would be a second one on
// top of the scene shaders' own and would shift hue.
const float kKnee = 0.8;

vec3 highlightShoulder(vec3 c)
{
    vec3 over = max(c - vec3(kKnee), vec3(0.0));
    return min(c, vec3(kKnee)) + (1.0 - kKnee) * (vec3(1.0) - exp(-over / (1.0 - kKnee)));
}

void main()
{
    vec3 hdr   = texture(uSceneColor, vTexCoord).rgb;
    vec3 bloom = mix(texture(uBloomTex, vTexCoord).rgb,
                     texture(uBloomWideTex, vTexCoord).rgb, kBloomWideShare);

    vec3 combined = hdr + bloom * kBloomIntensity;

    // Branch, not just a multiply by uGodRayIntensity: on a frame where the god-ray passes
    // didn't run, uGodRayTex holds whatever it last held, and a float target can hold Inf/NaN
    // (Inf * 0.0 = NaN, which then poisons that channel of the whole composite).
    if (uGodRayIntensity > 0.0)
    {
        vec3 godRay = texture(uGodRayTex, vTexCoord).rgb;
        combined += godRay * kGodRayTint * (kGodRayIntensity * uGodRayIntensity);
    }

    // The RGBA16F fallback for the HDR buffer (see VkViewportControl._hdrColorFormat) is signed,
    // so negative values can reach this point; the shoulder assumes c >= 0.
    vec3 mapped = highlightShoulder(max(combined, vec3(0.0)));

    // Dither: same reasoning as sky.frag's own dither at the very end of its main() -- a smooth
    // gradient quantised to 8 bits still shows visible banding (sky, water, a softly lit wall)
    // without it.
    vec3 p3 = fract(vec3(gl_FragCoord.xy, gl_FragCoord.x + gl_FragCoord.y) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    float dither = fract((p3.x + p3.y) * p3.z) - 0.5;
    mapped += dither * (1.5 / 255.0);

    fragColor = vec4(max(mapped, vec3(0.0)), 1.0);
}
