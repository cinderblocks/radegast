#version 310 es
precision highp float;

// God-ray occlusion mask: reads the full-resolution HDR scene colour and the main pass's depth,
// and writes a downsampled (half-resolution, same target size as bloom) brightness mask of the
// sky (sky pixels = their luminance, geometry pixels = 0). VkGodRayBlurPipeline then streaks
// this toward the sun's screen position. Uses quad.vert like every other full-screen pass here.
//
// Why luminance, not raw RGB? The sky's HDR colour is strongly influenced by the region's
// atmosphere uniforms (uBlueHorizon / uSunlightColor etc.), which can be green-dominant in
// unusual EEP presets. Propagating raw RGB through the radial blur and into the tonemap
// composite turns those presets into a green-screen effect. Using luminance here keeps the
// mask colour-neutral; the warm tint (kGodRayTint in tonemap.frag) is applied once, at composite
// time, so god-rays always read as warm-white streaks regardless of the region's sky colour.

layout(set = 0, binding = 0) uniform sampler2D uSceneColor; // full-res HDR (VkViewportControl._hdrColorFormat)
layout(set = 0, binding = 1) uniform sampler2D uDepthTex;   // main-pass depth

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

// Same cap bloom_extract.frag applies, for the same reason: one runaway texel would otherwise be
// streaked across the frame by the radial blur.
const float kMaxSceneValue = 16.0;

void main()
{
    float lumaSum = 0.0;
    float skyTaps = 0.0;
    vec2 offsets[4] = vec2[4](
        vec2(-0.5, -0.5), vec2(0.5, -0.5), vec2(-0.5, 0.5), vec2(0.5, 0.5));
    for (int i = 0; i < 4; i++)
    {
        vec2  uv    = vTexCoord + uSrcTexelSize * offsets[i];
        float depth = texture(uDepthTex, uv).r;
        if (depth >= kSkyDepthThreshold)
        {
            vec3 c = clamp(texture(uSceneColor, uv).rgb, vec3(0.0), vec3(kMaxSceneValue));
            lumaSum += dot(c, vec3(0.2126, 0.7152, 0.0722));
            skyTaps += 1.0;
        }
    }
    float luma = skyTaps > 0.0 ? lumaSum / skyTaps : 0.0;
    fragColor = vec4(luma, luma, luma, 1.0);
}
