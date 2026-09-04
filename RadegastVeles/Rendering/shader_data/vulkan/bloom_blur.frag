#version 310 es
precision highp float;

// One direction of a separable 9-tap Gaussian blur -- run twice by the SAME pipeline (see
// VkBloomBlurPipeline), horizontal then vertical, ping-ponging between two half-resolution
// targets. uDirection carries the per-pass texel step (see ssaoblur.frag's own doc comment for
// why this is a push constant rather than a UBO binding -- same reasoning, just a direction
// vector here instead of a fixed box size): (1/w, 0) for the horizontal pass, (0, 1/h) for the
// vertical pass, so this one shader body handles both without a branch.

layout(set = 0, binding = 0) uniform sampler2D uSrcTex;

layout(push_constant) uniform PerDraw
{
    vec2 uDirection; // texel step for ONE tap, already oriented (see doc comment above)
} pc;
#define uDirection pc.uDirection

layout(location = 0) in  vec2 vTexCoord;
layout(location = 0) out vec4 fragColor;

// Standard 9-tap Gaussian weights (sigma ~3), symmetric around the centre tap.
const float kWeights[5] = float[5](0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);

void main()
{
    vec3 result = texture(uSrcTex, vTexCoord).rgb * kWeights[0];
    for (int i = 1; i < 5; i++)
    {
        vec2 offset = uDirection * float(i);
        result += texture(uSrcTex, vTexCoord + offset).rgb * kWeights[i];
        result += texture(uSrcTex, vTexCoord - offset).rgb * kWeights[i];
    }
    fragColor = vec4(result, 1.0);
}
