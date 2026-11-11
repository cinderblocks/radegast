#version 310 es
precision mediump float;

// uHasTexture shares the push-constant block declared in particle.vert; the two declarations
// must match byte-for-byte (one pipeline layout, one range covering both stages).
//
// Per-particle glow arrives folded into vColor (VkParticleBuffer.ExpandToVertices), so vColor.rgb
// may exceed 1.0; the output is deliberately unclamped so glowing particles reach the bloom pass.

layout(location = 0) in vec2 vTexCoord;
layout(location = 1) in vec4 vColor;

layout(set = 0, binding = 0) uniform sampler2D uAlbedo;

layout(push_constant) uniform PushConstants {
    mat4  uMvp;
    int   uHasTexture;
} pc;
#define uHasTexture pc.uHasTexture

layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 texColor = (uHasTexture != 0) ? texture(uAlbedo, vTexCoord) : vec4(1.0);
    vec4 col = texColor * vColor;
    if (col.a < 0.004) discard;
    fragColor = col;
}
