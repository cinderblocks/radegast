#version 310 es
precision highp float;

// Bright-pass extract: reads the full-resolution HDR scene colour and writes a downsampled
// (half-resolution target -- see VkViewportControl's bloom target sizing), soft-thresholded
// copy containing only the content bloom should spread. Uses quad.vert like every other
// full-screen pass in this port.

layout(set = 0, binding = 0) uniform sampler2D uSceneColor; // full-res HDR (B10G11R11UfloatPack32)

layout(push_constant) uniform PerDraw
{
    vec2 uSrcTexelSize; // 1.0 / full-res scene size -- source texel size, NOT this pass's own
                         // output size, since the 4 taps below sample uSceneColor.
} pc;
#define uSrcTexelSize pc.uSrcTexelSize

layout(location = 0) in  vec2 vTexCoord;
layout(location = 0) out vec4 fragColor;

// Soft-knee threshold: values well below kThreshold contribute nothing, values well above pass
// through close to linearly, with a smooth transition in between (not a hard cutoff, which would
// make the bloom's own edge visible as a ring around bright objects).
const float kThreshold = 1.0;
const float kKnee      = 0.5;

vec3 softThreshold(vec3 c)
{
    float brightness = max(c.r, max(c.g, c.b));
    float soft = brightness - kThreshold + kKnee;
    soft = clamp(soft, 0.0, 2.0 * kKnee);
    soft = soft * soft / (4.0 * kKnee + 1e-5);
    float contribution = max(soft, brightness - kThreshold) / max(brightness, 1e-5);
    return c * contribution;
}

void main()
{
    // 4-tap box downsample (not a single bilinear sample at this pass's own UV): this pass's
    // output is half the input's resolution, so a single tap would only ever read ONE of the
    // 2x2 source texels a bilinear filter blends toward, depending on exactly where the texel
    // centres land -- flickering/aliasing under camera motion. Averaging an explicit 2x2 offset
    // pattern is cheap (4 taps) and stable regardless of exact alignment.
    vec3 sum = vec3(0.0);
    sum += texture(uSceneColor, vTexCoord + uSrcTexelSize * vec2(-0.5, -0.5)).rgb;
    sum += texture(uSceneColor, vTexCoord + uSrcTexelSize * vec2( 0.5, -0.5)).rgb;
    sum += texture(uSceneColor, vTexCoord + uSrcTexelSize * vec2(-0.5,  0.5)).rgb;
    sum += texture(uSceneColor, vTexCoord + uSrcTexelSize * vec2( 0.5,  0.5)).rgb;
    sum *= 0.25;

    fragColor = vec4(softThreshold(sum), 1.0);
}
