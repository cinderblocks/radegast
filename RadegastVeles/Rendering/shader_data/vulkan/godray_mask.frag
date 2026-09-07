#version 310 es
precision highp float;

// God-ray occlusion mask: reads the full-resolution HDR scene colour and SSAO's G-buffer depth,
// and writes a downsampled (half-resolution, same target size as bloom) copy of the HDR colour
// ONLY where nothing was drawn (depth reads as the far plane -- sky, including the sky shader's
// own sun-glow disc), zeroed everywhere geometry occluded it. VkGodRayBlurPipeline then streaks
// this toward the sun's screen position. Uses quad.vert like every other full-screen pass here.

layout(set = 0, binding = 0) uniform sampler2D uSceneColor; // full-res HDR (B10G11R11UfloatPack32)
layout(set = 0, binding = 1) uniform sampler2D uDepthTex;   // SSAO's G-buffer depth (same camera)

layout(push_constant) uniform PerDraw
{
    vec2 uSrcTexelSize; // 1.0 / full-res scene size -- source texel size (see bloom_extract.frag's
                         // identical field for why: this pass's own output is half that size).
} pc;
#define uSrcTexelSize pc.uSrcTexelSize

layout(location = 0) in  vec2 vTexCoord;
layout(location = 0) out vec4 fragColor;

// Depth clears to 1.0 (far plane, see VkViewportControl's main-pass ClearDepthStencilValue) --
// anything strictly less than this had real geometry drawn into it. No soft threshold needed
// (unlike bloom's brightness knee): a pixel is either sky or it isn't.
const float kSkyDepthThreshold = 0.9999;

void main()
{
    // 4-tap box downsample, same reasoning as bloom_extract.frag's identical pattern: a single
    // bilinear tap at this pass's own (half-res) UV would alias under camera motion.
    vec3 colorSum = vec3(0.0);
    float skyTaps = 0.0;
    vec2 offsets[4] = vec2[4](
        vec2(-0.5, -0.5), vec2(0.5, -0.5), vec2(-0.5, 0.5), vec2(0.5, 0.5));
    for (int i = 0; i < 4; i++)
    {
        vec2 uv = vTexCoord + uSrcTexelSize * offsets[i];
        float depth = texture(uDepthTex, uv).r;
        if (depth >= kSkyDepthThreshold)
        {
            colorSum += texture(uSceneColor, uv).rgb;
            skyTaps += 1.0;
        }
    }

    fragColor = vec4(skyTaps > 0.0 ? colorSum / skyTaps : vec3(0.0), 1.0);
}
