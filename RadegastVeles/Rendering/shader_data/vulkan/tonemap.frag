#version 310 es
precision highp float;

// Final composite: combines the full-resolution HDR scene colour with the blurred bloom target,
// applies a filmic tonemap curve, and writes the result into the REAL swapchain image -- this
// pass (not the main scene pass) is now the true last thing that touches it. See
// VkRenderPass.CreateTonemapOutputPass's own doc comment for why this pass, not the main pass,
// owns that role after the HDR buffer was introduced.

layout(set = 0, binding = 0) uniform sampler2D uSceneColor; // full-res HDR (B10G11R11UfloatPack32)
layout(set = 0, binding = 1) uniform sampler2D uBloomTex;   // half-res, already blurred
layout(set = 0, binding = 2) uniform sampler2D uGodRayTex;  // half-res, already blurred/streaked

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

// Tint for the god-ray contribution -- warm, matching the sun disc rather than a neutral white
// streak. Modest additive weight for the same "glow, not a wash" reasoning as kBloomIntensity.
const vec3  kGodRayTint      = vec3(1.0, 0.92, 0.75);
const float kGodRayIntensity = 0.9;

// ACES filmic tonemap curve (Narkowicz 2015 fit) -- a cheap, well-behaved approximation of the
// full ACES reference curve: rolls bright highlights off toward white with a smooth shoulder
// instead of hard-clipping at 1.0 (which is what every UNORM-target frame before this pass
// existed did -- the sun disc's own >1.0 core, for one, previously just clipped to a flat white
// disc with a hard edge). Applied per-channel; input is expected roughly in the same "looks
// right around 0-1, real content can run somewhat higher" range this engine's existing shaders
// already target (see atmosphere.glsl's own atmSoftClip, a per-shader soft-knee doing local
// compression for the same reason before this pass existed) -- not strict scene-linear
// radiometric units, so this is a display-curve choice tuned by eye, not a colour-science claim.
vec3 acesFilm(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec3 hdr    = texture(uSceneColor, vTexCoord).rgb;
    vec3 bloom  = texture(uBloomTex, vTexCoord).rgb;
    vec3 godRay = texture(uGodRayTex, vTexCoord).rgb;

    vec3 combined = hdr + bloom * kBloomIntensity
                        + godRay * kGodRayTint * (kGodRayIntensity * uGodRayIntensity);
    vec3 mapped   = acesFilm(combined);

    // Dither: same reasoning as sky.frag's own dither at the very end of its main() -- a smooth
    // filmic curve quantised to 8 bits still shows visible banding across a slow gradient (sky,
    // water, a softly lit wall) without it.
    vec3 p3 = fract(vec3(gl_FragCoord.xy, gl_FragCoord.x + gl_FragCoord.y) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    float dither = fract((p3.x + p3.y) * p3.z) - 0.5;
    mapped += dither * (1.5 / 255.0);

    fragColor = vec4(max(mapped, vec3(0.0)), 1.0);
}
