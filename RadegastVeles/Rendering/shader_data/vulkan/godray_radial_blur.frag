#version 310 es
precision highp float;

// Radial "light scatter" streak toward the sun's screen-space position -- the classic
// screen-space god-ray technique (Mitchell/Sousa03): march a fixed number of samples from this
// pixel toward uSunUv, accumulating the occlusion mask (see godray_mask.frag) with an
// exponentially decaying weight. Single pass (unlike bloom_blur.frag's separable two-pass
// Gaussian): a radial blur only has one direction per pixel, so there's nothing to separate.
// Same half-resolution target as the mask stage. Uses quad.vert like every other full-screen
// pass here.

layout(set = 0, binding = 0) uniform sampler2D uMaskTex;

layout(push_constant) uniform PerDraw
{
    vec2  uSunUv;      // sun's screen-space UV this frame (CPU-projected, see VkViewportControl)
    float uIntensity;  // 0 when the sun is below the horizon/behind the camera/off-screen --
                        // multiplying here (not just at tonemap composite time) means a disabled
                        // frame's march is still skipped entirely by the CPU-side gate, and this
                        // scalar is only ever nonzero when the march below is worth doing.
    float uDecay;       // per-sample falloff, ~0.94-0.97
} pc;
#define uSunUv     pc.uSunUv
#define uIntensity pc.uIntensity
#define uDecay     pc.uDecay

layout(location = 0) in  vec2 vTexCoord;
layout(location = 0) out vec4 fragColor;

const int   kNumSamples   = 32;
const float kDensity      = 0.9;   // fraction of the pixel-to-sun distance covered by the march
const float kSampleWeight = 1.2;
const float kExposure     = 0.35;

void main()
{
    // Zero-intensity frames write true zeros without touching uMaskTex: VkViewportControl runs
    // this pass once with uIntensity = 0 to give the target defined contents when the mask stage
    // hasn't run (so uMaskTex may be uninitialised -- and garbage * 0.0 is NaN if it's Inf/NaN).
    if (uIntensity <= 0.0)
    {
        fragColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec2  delta = (uSunUv - vTexCoord) * kDensity / float(kNumSamples);
    vec2  uv    = vTexCoord;
    float decay = 1.0;
    vec3  color = vec3(0.0);

    for (int i = 0; i < kNumSamples; i++)
    {
        uv    += delta;
        color += texture(uMaskTex, uv).rgb * decay * kSampleWeight;
        decay *= uDecay;
    }
    fragColor = vec4(color * (kExposure * uIntensity / float(kNumSamples)), 1.0);
}
